using UnityEngine;
using Oculus.Interaction.HandGrab;
using System;

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

    void Reset()
    {
        if (!interactor) interactor = GetComponent<HandGrabInteractor>();
        if (!animator) animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (!interactor || !animator) return;

        bool now = interactor.IsGrabbing;

        // Grab 시작 엣지
        if (now && !wasGrabbing)
        {
            // 1) 현재 잡힌 오브젝트 얻기 (Oculus SDK에 따라 달라질 수 있어 아래 헬퍼에서 최대한 안전하게 얻음)
            CurrentGrabbedObject = TryGetGrabbedObject();

            // 2) Tag로 AnimMode 선택
            var mode = ResolveModeByTag(CurrentGrabbedObject ? CurrentGrabbedObject.tag : "");

            // 3) (선택) AnimatorOverrideController 적용
            if (mode.overrideController != null)
                animator.runtimeAnimatorController = mode.overrideController;

            // 4) Play 트리거
            if (!string.IsNullOrEmpty(mode.playTrigger))
            {
                animator.ResetTrigger(mode.playTrigger);
                animator.SetTrigger(mode.playTrigger);
            }

            CurrentModeTag = mode.tag;
            HandShake_on = true;
            OnHandshakeStart?.Invoke(this, mode);
        }

        // Grab 종료 엣지 → Idle 트리거
        if (!now && wasGrabbing)
        {
            var mode = ResolveModeByTag(CurrentModeTag);
            if (!string.IsNullOrEmpty(mode.idleTrigger))
            {
                animator.ResetTrigger(mode.idleTrigger);
                animator.SetTrigger(mode.idleTrigger);
            }
        }

        // Idle 상태 진입 감지 → HandShake_on false & End 이벤트
        // (각 모드마다 idle state 이름이 다를 수 있으니 현재 모드의 idleStateName 확인)
        var st = animator.GetCurrentAnimatorStateInfo(0);
        var modeForState = ResolveModeByTag(CurrentModeTag);
        bool isIdle = !animator.IsInTransition(0) &&
                      !string.IsNullOrEmpty(modeForState.idleStateName) &&
                      st.IsName(modeForState.idleStateName);

        HandShake_on = !isIdle;
        if (isIdle && wasGrabbing)
        {
            OnHandshakeEnd?.Invoke(this);
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
