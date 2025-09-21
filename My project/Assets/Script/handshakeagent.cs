using UnityEngine;
using Oculus.Interaction.HandGrab;
using System;
using Oculus.Interaction;

public class HandshakeAgent : MonoBehaviour
{
    [Header("Per-Character Refs")]
    public HandGrabInteractor interactor;
    public Animator animator;

    [Header("Animation Modes (select by grabbed Tag)")]
    public AnimMode[] modes;

    [Header("State (readonly)")]
    public bool HandShake_on { get; private set; }
    public string CurrentModeTag { get; private set; } = "";
    public GameObject CurrentGrabbedObject { get; private set; }

    // 이벤트: 시작(어떤 모드), 종료
    public event Action<HandshakeAgent, AnimMode> OnHandshakeStart;
    public event Action<HandshakeAgent> OnHandshakeEnd;

    private bool wasGrabbing;

    // ★ 추가: 선택된(잡힌) 오브젝트 캐시 + 디버그 토글
    private GameObject _cachedGrabbedGO;

    [Header("Debug")]
    public bool debugLogs = false;
    private string DbgTag => $"[HandshakeAgent:{name}]";

    // (선택) 기본 컨트롤러 복구용
    [Header("Animator Defaults")]
    public RuntimeAnimatorController defaultController;

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
    // ★ 잡기 상태 변화를 이벤트로 캐치해서 선택된 GO를 캐싱
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

    // ★ 현재 선택(잡기)된 Interactable로부터 GameObject 얻기 (여러 버전 대응)
    private GameObject GetSelectedGameObject()
    {
        // 가장 확실: SelectedInteractable을 MonoBehaviour로 캐스팅
        var selMb = interactor.SelectedInteractable as MonoBehaviour;
        if (selMb) return selMb.gameObject;

        // 폴백 1: interactor 주변에서 HandGrabInteractable 추적
        var hgi = interactor.GetComponentInParent<HandGrabInteractable>();
        if (hgi) return hgi.gameObject;

        // 폴백 2: null
        return null;
    }


    void Reset()
    {
        if (!interactor) interactor = GetComponent<HandGrabInteractor>();
        if (!animator) animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (!interactor || !animator) return;

        bool now = interactor.IsGrabbing;
        if (debugLogs) Debug.Log($"{DbgTag} grabNow={now}, was={wasGrabbing}");

        // ───── Grab 시작 엣지 ─────
        // Grab 시작 엣지
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

            // 게이트 (하나는만 실행/쿨다운)
            bool allowed = HandshakeOrchestrator.Instance && HandshakeOrchestrator.Instance.TryBegin(this, mode);
            if (!allowed) { wasGrabbing = now; return; }

            // (선택) AOC 적용: Base 없으면 경고하고 적용 스킵
            if (mode.overrideController)
            {
                if (mode.overrideController.runtimeAnimatorController == null)
                {
                    if (debugLogs) Debug.LogWarning($"{DbgTag} OverrideController '{mode.overrideController.name}' has NO Base Controller. Using defaultController.");
                }
                else
                {
                    animator.runtimeAnimatorController = mode.overrideController;
                    if (debugLogs) Debug.Log($"{DbgTag} AOC applied: {mode.overrideController.name}");
                }
            }

            // ★ 트리거 존재 여부 검증 (여기서 가장 많이 낚여요)
            if (!string.IsNullOrEmpty(mode.playTrigger))
            {
                if (!HasParam(animator, mode.playTrigger, AnimatorControllerParameterType.Trigger))
                {
                    Debug.LogWarning($"{DbgTag} Animator has NO Trigger '{mode.playTrigger}'. Check controller/params.");
                    wasGrabbing = now; return; // 존재 안 하면 재생 안 함
                }

                animator.ResetTrigger(mode.playTrigger);
                animator.SetTrigger(mode.playTrigger);
                if (debugLogs) Debug.Log($"{DbgTag} Animator Trigger → {mode.playTrigger}");
            }
            else
            {
                Debug.LogWarning($"{DbgTag} mode.playTrigger is empty for tag '{grabbedTag}'");
                wasGrabbing = now; return;
            }

            CurrentModeTag = mode.tag;
            HandShake_on = true;
            OnHandshakeStart?.Invoke(this, mode);
        }


        // ───── Grab 종료 엣지 → Idle 트리거만 쏜다 (세션 종료는 아직 아님) ─────
        if (!now && wasGrabbing)
        {
            var mode = ResolveModeByTag(CurrentModeTag);
            if (!string.IsNullOrEmpty(mode.idleTrigger))
            {
                animator.ResetTrigger(mode.idleTrigger);
                animator.SetTrigger(mode.idleTrigger);
                if (debugLogs) Debug.Log($"{DbgTag} Animator Trigger → {mode.idleTrigger}");
            }
            // 여기서는 HandShake_on을 건드리지 않는다. (아직 애니메이션이 Idle로 돌아오지 않았을 수 있음)
        }

        // ───── Idle 상태 진입 감지 → ‘잡기 해제된 상태’에서만 세션 종료 ─────
        var st = animator.GetCurrentAnimatorStateInfo(0);
        var modeForState = ResolveModeByTag(CurrentModeTag);
        bool isIdle = !animator.IsInTransition(0) &&
                      !string.IsNullOrEmpty(modeForState.idleStateName) &&
                      st.IsName(modeForState.idleStateName);

        // ★ 종료 조건을 "isIdle && !now" 로 강화 (여전히 잡고 있으면 종료 금지)
        if (isIdle && !now && !string.IsNullOrEmpty(CurrentModeTag))
        {
            HandShake_on = false;                   // ← 이제서야 false
            OnHandshakeEnd?.Invoke(this);           // ← 이때만 오케스트레이터에 종료 알림
            if (debugLogs) Debug.Log($"{DbgTag} OnHandshakeEnd fired (idle='{modeForState.idleStateName}')");

            // (선택) 기본 컨트롤러 복구
            if (defaultController)
                animator.runtimeAnimatorController = defaultController;

            CurrentModeTag = "";
            CurrentGrabbedObject = null;
        }

        // ★ HandShake_on은 프레임마다 isIdle로 강제 갱신하지 말고,
        //   - 시작 엣지에서 true
        //   - 위 종료 블록에서만 false 로 바꾸세요.

        // 마지막에 상태 갱신
        wasGrabbing = now;

    }

    static bool HasParam(Animator anim, string name, AnimatorControllerParameterType type)
    {
        if (!anim || string.IsNullOrEmpty(name)) return false;
        var ps = anim.parameters;
        for (int i = 0; i < ps.Length; i++)
            if (ps[i].type == type && ps[i].name == name) return true;
        return false;
    }

    bool TryResolveModeByTag(string tag, out AnimMode mode)
    {
        mode = default;
        if (modes == null || modes.Length == 0)
        {
            if (debugLogs) Debug.LogWarning($"{DbgTag} modes empty");
            return false;
        }
        if (string.IsNullOrEmpty(tag))
        {
            if (debugLogs) Debug.LogWarning($"{DbgTag} grabbed Tag is empty");
            return false;
        }

        for (int i = 0; i < modes.Length; i++)
        {
            if (modes[i].tag == tag) { mode = modes[i]; return true; }
        }

        if (debugLogs) Debug.LogWarning($"{DbgTag} NO mode matched for Tag='{tag}'");
        return false; // ★ 더 이상 0번으로 fallback 안 함
    }

    AnimMode ResolveModeByTag(string tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            // 태그 없으면 첫 모드나 기본값
            return modes != null && modes.Length > 0 ? modes[0] : default;
        }

        for (int i = 0; i < modes.Length; i++)
            if (modes[i].tag == tag) return modes[i];

        // 매치 실패 시 0번 모드
        return modes != null && modes.Length > 0 ? modes[0] : default;
    }

    GameObject TryGetGrabbedObject()
    {
        // SDK 버전에 따라 접근 방식이 다를 수 있어 최대한 일반적으로 시도
        // 1) interactor에서 가장 가까운/선택된 interactable의 Transform을 얻는 API가 있으면 사용
        //    (예시) interactor.SelectedInteractable? interactor.Grabbable? 등
        // 2) 없다면, 손 콜라이더 주변에서 HandGrabInteractable 컴포넌트가 붙은 부모를 탐색
        //    프로젝트에 맞게 필요 시 여기 커스터마이즈

        // (안전 기본) interactor 게임오브젝트의 부모쪽에서 HandGrabInteractable 찾기
        var hg = interactor.GetComponentInParent<HandGrabInteractable>();
        if (hg) return hg.gameObject;

        // fallback: interactor 트랜스폼 근처 레이/오버랩으로 찾아도 됨(생략)
        return null;
    }
}
