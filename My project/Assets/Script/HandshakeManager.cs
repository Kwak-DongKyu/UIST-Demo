using UnityEngine;
using Oculus.Interaction.HandGrab;

public class HandshakeManager : MonoBehaviour
{
    public HandGrabInteractor interactor;
    public Animator animator;
    public string playTrigger = "Play";   // ��� Ʈ����
    public string idleTrigger = "Idle";   // (�ɼ�) Idle�� ������ Ʈ����
    public string idleStateName = "Idle"; // Animator�� Idle ���� �̸��� ��Ȯ�� ��ġ

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

        // 1) Grab ���� �� �ִϸ��̼� ���� + HandShake_on = true
        if (now && !wasGrabbing)
        {
            HandShake_on = true;
            animator.ResetTrigger(playTrigger);
            animator.SetTrigger(playTrigger);
            // Debug.Log("[GRAB] START");
        }

        // 2) Grab ���� �� Idle�� ������(�ɼ�)
        if (!now && wasGrabbing)
        {
            // Idle Ʈ���Ÿ� ���� �����̸� ����, �ƴϸ� �� �� ������ ��
            animator.ResetTrigger(idleTrigger);
            animator.SetTrigger(idleTrigger);
            // Debug.Log("[GRAB] END");
        }

        // 3) ���� �ִϸ����� ���¸� �������� HandShake_on ����
        //    - Idle ���¸� false
        //    - Idle�� �ƴϸ� true
        var st = animator.GetCurrentAnimatorStateInfo(0);
        if (!animator.IsInTransition(0) && st.IsName(idleStateName))
            HandShake_on = false;
        else
            HandShake_on = true;

        wasGrabbing = now;
    }
}
