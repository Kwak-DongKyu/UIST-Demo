using UnityEngine;
using Oculus.Interaction.HandGrab;

public class HandshakeManager : MonoBehaviour
{
    public HandGrabInteractor interactor;
    public Animator animator;
    public string playTrigger = "Play";   // 재생 트리거
    public string idleTrigger = "Idle";   // (옵션) Idle로 보내는 트리거
    public string idleStateName = "Idle"; // Animator의 Idle 상태 이름과 정확히 일치

    public bool HandShake_on;
    private bool wasGrabbing;

    void Reset()
    {
        if (interactor == null) interactor = GetComponent<HandGrabInteractor>();
    }

    void Update()
    {
        if (!animator || !interactor) return;

        bool now = interactor.IsGrabbing;

        // 1) Grab 시작 → 애니메이션 시작 + HandShake_on = true
        if (now && !wasGrabbing)
        {
            HandShake_on = true;
            animator.ResetTrigger(playTrigger);
            animator.SetTrigger(playTrigger);
            // Debug.Log("[GRAB] START");
        }

        // 2) Grab 종료 → Idle로 보내기(옵션)
        if (!now && wasGrabbing)
        {
            // Idle 트리거를 쓰는 세팅이면 유지, 아니면 이 줄 지워도 됨
            animator.ResetTrigger(idleTrigger);
            animator.SetTrigger(idleTrigger);
            // Debug.Log("[GRAB] END");
        }

        // 3) 현재 애니메이터 상태를 기준으로 HandShake_on 갱신
        //    - Idle 상태면 false
        //    - Idle이 아니면 true
        var st = animator.GetCurrentAnimatorStateInfo(0);
        if (!animator.IsInTransition(0) && st.IsName(idleStateName))
            HandShake_on = false;
        else
            HandShake_on = true;

        wasGrabbing = now;
    }
}
