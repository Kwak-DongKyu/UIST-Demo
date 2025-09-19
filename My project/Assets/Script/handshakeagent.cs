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
        if (now && !wasGrabbing)
        {
            // ★ 이벤트 캐시 우선, 없으면 즉시 조회
            CurrentGrabbedObject = _cachedGrabbedGO ?? GetSelectedGameObject();
            if (debugLogs) Debug.Log($"{DbgTag} StartEdge: sel={(CurrentGrabbedObject ? CurrentGrabbedObject.name : "null")}");

            // ★ 내 소유(GrabOwner) 체크
            var ownerComp = CurrentGrabbedObject ? CurrentGrabbedObject.GetComponentInParent<GrabOwner>() : null;
            bool mine = ownerComp && ownerComp.owner == this;
            if (debugLogs)
            {
                var ownerName = ownerComp ? (ownerComp.owner ? ownerComp.owner.name : "nullOwnerField") : "noOwnerComp";
                Debug.Log($"{DbgTag} OwnerCheck: mine={mine} (ownerComp={ownerName})");
            }

            if (!mine)
            {
                // 내 것이 아니면 트리거/이벤트 발행 금지 (엣지 소모는 해서 중복 방지)
                wasGrabbing = now;
                return;
            }

            var mode = ResolveModeByTag(CurrentGrabbedObject ? CurrentGrabbedObject.tag : "");
            if (debugLogs) Debug.Log($"{DbgTag} ModeResolved: tag='{mode.tag}', play='{mode.playTrigger}' idle='{mode.idleTrigger}'");

            // (선택) AOC Base 체크 후 적용
            if (mode.overrideController && mode.overrideController.runtimeAnimatorController != null)
            {
                animator.runtimeAnimatorController = mode.overrideController;
                if (debugLogs) Debug.Log($"{DbgTag} AOC applied: {mode.overrideController.name}");
            }

            // 트리거 발사
            if (!string.IsNullOrEmpty(mode.playTrigger))
            {
                animator.ResetTrigger(mode.playTrigger);
                animator.SetTrigger(mode.playTrigger);
                if (debugLogs) Debug.Log($"{DbgTag} Animator Trigger → {mode.playTrigger}");
            }

            CurrentModeTag = mode.tag;
            HandShake_on = true;
            OnHandshakeStart?.Invoke(this, mode);
            if (debugLogs) Debug.Log($"{DbgTag} OnHandshakeStart fired");
        }

        // ───── Grab 종료 엣지 → Idle ─────
        if (!now && wasGrabbing)
        {
            var mode = ResolveModeByTag(CurrentModeTag);
            if (!string.IsNullOrEmpty(mode.idleTrigger))
            {
                animator.ResetTrigger(mode.idleTrigger);
                animator.SetTrigger(mode.idleTrigger);
                if (debugLogs) Debug.Log($"{DbgTag} Animator Trigger → {mode.idleTrigger}");
            }
        }

        // ───── Idle 상태 진입 감지 ─────
        var st = animator.GetCurrentAnimatorStateInfo(0);
        var modeForState = ResolveModeByTag(CurrentModeTag);
        bool isIdle = !animator.IsInTransition(0) &&
                      !string.IsNullOrEmpty(modeForState.idleStateName) &&
                      st.IsName(modeForState.idleStateName);

        HandShake_on = !isIdle;

        if (isIdle && wasGrabbing)
        {
            OnHandshakeEnd?.Invoke(this);
            if (debugLogs) Debug.Log($"{DbgTag} OnHandshakeEnd fired (idle='{modeForState.idleStateName}')");

            // (선택) 기본 컨트롤러 복구
            if (defaultController)
            {
                animator.runtimeAnimatorController = defaultController;
                if (debugLogs) Debug.Log($"{DbgTag} Animator controller restored to default");
            }

            CurrentModeTag = "";
            CurrentGrabbedObject = null;
        }

        wasGrabbing = now;
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
