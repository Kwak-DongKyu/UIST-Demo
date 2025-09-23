using UnityEngine;
using Oculus.Interaction.HandGrab;
using System;
using Oculus.Interaction;

public class HandshakeAgent : MonoBehaviour
{
    public enum StartInputMode { Grab, Trigger }   // ★ 어떤 방식으로 시작할지
    [Header("Input Mode")]
    public StartInputMode inputMode = StartInputMode.Grab; // Inspector에서 전환

    [Header("Per-Character Refs")]
    public HandGrabInteractor interactor;
    public Animator animator;

    [Header("Animation Modes (select by grabbed Tag)")]
    public AnimMode[] modes;

    [Header("Animator Defaults")]
    public RuntimeAnimatorController defaultController;

    [Header("State (readonly)")]
    public bool HandShake_on { get; private set; }
    public string CurrentModeTag { get; private set; } = "";
    public GameObject CurrentGrabbedObject { get; private set; }

    // 이벤트: 시작(어떤 모드), 종료
    public event Action<HandshakeAgent, AnimMode> OnHandshakeStart;
    public event Action<HandshakeAgent> OnHandshakeEnd;

    [Header("Debug")]
    public bool debugLogs = false;
    string DbgTag => $"[HandshakeAgent:{name}]";

    // 내부
    private bool wasGrabbing;
    private GameObject _cachedGrabbedGO;   // Grab 선택 캐시
    private bool externalSessionActive;    // Trigger 모드에서 외부로 시작했는가

    void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!defaultController && animator) defaultController = animator.runtimeAnimatorController;
    }

    void OnEnable()
    {
        if (interactor != null)
            interactor.WhenStateChanged += OnInteractorStateChanged;
    }

    void OnDisable()
    {
        if (interactor != null)
            interactor.WhenStateChanged -= OnInteractorStateChanged;
    }

    // ===== Grab 선택 캐시 =====
    private void OnInteractorStateChanged(InteractorStateChangeArgs args)
    {
        if (args.NewState == InteractorState.Select)
        {
            _cachedGrabbedGO = GetSelectedGameObject();
            if (debugLogs) Debug.Log($"{DbgTag} SELECT → {_cachedGrabbedGO?.name ?? "null"}");
        }
        else if (args.NewState == InteractorState.Hover || args.NewState == InteractorState.Normal)
        {
            if (debugLogs && _cachedGrabbedGO) Debug.Log($"{DbgTag} DESELECT → clear {_cachedGrabbedGO.name}");
            _cachedGrabbedGO = null;
        }
    }

    private GameObject GetSelectedGameObject()
    {
        var selMb = interactor ? (interactor.SelectedInteractable as MonoBehaviour) : null;
        if (selMb) return selMb.gameObject;

        var hgi = interactor ? interactor.GetComponentInParent<HandGrabInteractable>() : null;
        if (hgi) return hgi.gameObject;

        return null;
    }

    void Reset()
    {
        if (!interactor) interactor = GetComponent<HandGrabInteractor>();
        if (!animator) animator = GetComponentInChildren<Animator>();
    }

    // ===== 외부(Trigger) 시작/종료 API =====
    //  - TagCollisionHandshakeStarter 같은 스크립트가 호출
    public bool BeginByTag(string tag, GameObject source = null)
    {
        if (!animator) return false;
        if (inputMode != StartInputMode.Trigger) return false;
        if (externalSessionActive || HandShake_on) return false;

        if (!TryResolveModeByTag(tag, out var mode))
        {
            if (debugLogs) Debug.LogWarning($"{DbgTag} Trigger begin denied: mode not found for tag '{tag}'");
            return false;
        }

        // 중앙 게이트 허가 (여기서 먼저 확인)
        if (!(HandshakeOrchestrator.Instance && HandshakeOrchestrator.Instance.TryBegin(this, mode)))
            return false;

        // ★ TryBegin 성공한 경우에만 애니메이션 + latch + 햅틱 실행
        CurrentGrabbedObject = source;
        ApplyAOCIfAny(mode);

        if (!KickPlayTrigger(mode)) return false;

        CurrentModeTag = mode.tag;
        HandShake_on = true;
        Debug.Log("dddd 여기 실행도미" + tag+","+HandShake_on);

        externalSessionActive = true;
        OnHandshakeStart?.Invoke(this, mode);
        if (debugLogs) Debug.Log($"{DbgTag} Trigger begin OK (tag={tag})");
        return true;
    }


    public void EndExternal()
    {
        if (inputMode != StartInputMode.Trigger) return;
        if (!externalSessionActive) return;

        var mode = ResolveModeByTag(CurrentModeTag);
        FireIdleTriggerIfAny(mode);  // 실제 종료는 Idle 진입 감지에서
        externalSessionActive = false;
        if (debugLogs) Debug.Log($"{DbgTag} Trigger end requested");
    }

    // ===== Update 루프 =====
    void Update()
    {
        if (!animator) return;

        // ───────────────────────────────────────── Grab 모드 ─────────────────────────────────────────
        if (inputMode == StartInputMode.Grab)
        {
            if (!interactor) return;

            bool now = interactor.IsGrabbing;
            if (debugLogs) Debug.Log($"{DbgTag} grabNow={now}, was={wasGrabbing}");

            // 시작 엣지
            if (now && !wasGrabbing)
            {
                CurrentGrabbedObject = _cachedGrabbedGO ?? GetSelectedGameObject();

                var ownerComp = CurrentGrabbedObject ? CurrentGrabbedObject.GetComponentInParent<GrabOwner>() : null;
                if (!ownerComp || ownerComp.owner != this) { wasGrabbing = now; return; }

                var grabbedTag = CurrentGrabbedObject ? CurrentGrabbedObject.tag : "";
                if (!TryResolveModeByTag(grabbedTag, out var mode))
                {
                    if (debugLogs) Debug.LogWarning($"{DbgTag} Begin denied: mode not found for tag '{grabbedTag}'");
                    wasGrabbing = now; return;
                }

                // ★ 중앙 게이트 통과해야만 실행
                bool allowed = HandshakeOrchestrator.Instance && HandshakeOrchestrator.Instance.TryBegin(this, mode);
                if (!allowed) { wasGrabbing = now; return; }

                ApplyAOCIfAny(mode);
                if (!KickPlayTrigger(mode)) { wasGrabbing = now; return; }

                CurrentModeTag = mode.tag;
                HandShake_on = true;
                OnHandshakeStart?.Invoke(this, mode);
            }

            // 종료 엣지(Grab 해제) → Idle 트리거만
            if (!now && wasGrabbing)
            {
                var mode = ResolveModeByTag(CurrentModeTag);
                FireIdleTriggerIfAny(mode);
            }

            wasGrabbing = now;
        }

        // ───────────────────────────────────────── 공통: Idle 진입 감지 ─────────────────────────────────────────
        var st = animator.GetCurrentAnimatorStateInfo(0);
        var modeForState = ResolveModeByTag(CurrentModeTag);
        bool isIdle = !animator.IsInTransition(0) &&
                      !string.IsNullOrEmpty(modeForState.idleStateName) &&
                      st.IsName(modeForState.idleStateName);

        // 종료 조건:
        //  - Grab 모드: Idle && !interactor.IsGrabbing
        //  - Trigger 모드: Idle (외부 세션 여부와 무관)
        bool canEnd =
            (inputMode == StartInputMode.Trigger && isIdle) ||
            (inputMode == StartInputMode.Grab && isIdle && interactor && !interactor.IsGrabbing);

        if (canEnd && !string.IsNullOrEmpty(CurrentModeTag))
        {
            HandShake_on = false;
            OnHandshakeEnd?.Invoke(this);
            if (debugLogs) Debug.Log($"{DbgTag} OnHandshakeEnd (idle='{modeForState.idleStateName}', mode={inputMode})");

            if (defaultController)
                animator.runtimeAnimatorController = defaultController;

            CurrentModeTag = "";
            CurrentGrabbedObject = null;
            externalSessionActive = false;
        }
    }


    // ===== 유틸 =====
    void ApplyAOCIfAny(AnimMode mode)
    {
        if (mode.overrideController)
        {
            if (mode.overrideController.runtimeAnimatorController == null)
            {
                if (debugLogs) Debug.LogWarning($"{DbgTag} OverrideController '{mode.overrideController.name}' has NO Base Controller.");
            }
            else
            {
                animator.runtimeAnimatorController = mode.overrideController;
                if (debugLogs) Debug.Log($"{DbgTag} AOC applied: {mode.overrideController.name}");
            }
        }
    }

    bool KickPlayTrigger(AnimMode mode)
    {
        if (string.IsNullOrEmpty(mode.playTrigger))
        {
            Debug.LogWarning($"{DbgTag} mode.playTrigger is empty (tag '{mode.tag}')");
            return false;
        }
        if (!HasParam(animator, mode.playTrigger, AnimatorControllerParameterType.Trigger))
        {
            Debug.LogWarning($"{DbgTag} Animator has NO Trigger '{mode.playTrigger}'. Check controller/params.");
            return false;
        }
        animator.ResetTrigger(mode.playTrigger);
        animator.SetTrigger(mode.playTrigger);
        if (debugLogs) Debug.Log($"ㅇㅇㅇ {DbgTag} Animator Trigger → {mode.playTrigger}");
        return true;
    }

    void FireIdleTriggerIfAny(AnimMode mode)
    {
        if (string.IsNullOrEmpty(mode.idleTrigger)) return;
        if (!HasParam(animator, mode.idleTrigger, AnimatorControllerParameterType.Trigger))
        {
            Debug.LogWarning($"{DbgTag} Animator has NO Trigger '{mode.idleTrigger}'");
            return;
        }
        animator.ResetTrigger(mode.idleTrigger);
        animator.SetTrigger(mode.idleTrigger);
        if (debugLogs) Debug.Log($"{DbgTag} Animator Trigger → {mode.idleTrigger}");
    }

    bool TryResolveModeByTag(string tag, out AnimMode mode)
    {
        if (!string.IsNullOrEmpty(tag))
        {
            for (int i = 0; i < modes.Length; i++)
            {
                if (modes[i].tag == tag) { mode = modes[i]; return true; }
            }
        }
        if (modes != null && modes.Length > 0) { mode = modes[0]; return true; }
        mode = default; return false;
    }

    AnimMode ResolveModeByTag(string tag)
    {
        TryResolveModeByTag(tag, out var m);
        return m;
    }

    static bool HasParam(Animator anim, string name, AnimatorControllerParameterType type)
    {
        if (!anim) return false;
        foreach (var p in anim.parameters)
            if (p.type == type && p.name == name) return true;
        return false;
    }
}
