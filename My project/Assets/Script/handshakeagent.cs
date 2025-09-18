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

    // �̺�Ʈ: ����(� ���), ����
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

        // Grab ���� ����
        if (now && !wasGrabbing)
        {
            // 1) ���� ���� ������Ʈ ��� (Oculus SDK�� ���� �޶��� �� �־� �Ʒ� ���ۿ��� �ִ��� �����ϰ� ����)
            CurrentGrabbedObject = TryGetGrabbedObject();

            // 2) Tag�� AnimMode ����
            var mode = ResolveModeByTag(CurrentGrabbedObject ? CurrentGrabbedObject.tag : "");

            // 3) (����) AnimatorOverrideController ����
            if (mode.overrideController != null)
                animator.runtimeAnimatorController = mode.overrideController;

            // 4) Play Ʈ����
            if (!string.IsNullOrEmpty(mode.playTrigger))
            {
                animator.ResetTrigger(mode.playTrigger);
                animator.SetTrigger(mode.playTrigger);
            }

            CurrentModeTag = mode.tag;
            HandShake_on = true;
            OnHandshakeStart?.Invoke(this, mode);
        }

        // Grab ���� ���� �� Idle Ʈ����
        if (!now && wasGrabbing)
        {
            var mode = ResolveModeByTag(CurrentModeTag);
            if (!string.IsNullOrEmpty(mode.idleTrigger))
            {
                animator.ResetTrigger(mode.idleTrigger);
                animator.SetTrigger(mode.idleTrigger);
            }
        }

        // Idle ���� ���� ���� �� HandShake_on false & End �̺�Ʈ
        // (�� ��帶�� idle state �̸��� �ٸ� �� ������ ���� ����� idleStateName Ȯ��)
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
            // �±� ������ ù ��峪 �⺻��
            return modes != null && modes.Length > 0 ? modes[0] : default;
        }

        for (int i = 0; i < modes.Length; i++)
            if (modes[i].tag == tag) return modes[i];

        // ��ġ ���� �� 0�� ���
        return modes != null && modes.Length > 0 ? modes[0] : default;
    }

    GameObject TryGetGrabbedObject()
    {
        // SDK ������ ���� ���� ����� �ٸ� �� �־� �ִ��� �Ϲ������� �õ�
        // 1) interactor���� ���� �����/���õ� interactable�� Transform�� ��� API�� ������ ���
        //    (����) interactor.SelectedInteractable? interactor.Grabbable? ��
        // 2) ���ٸ�, �� �ݶ��̴� �ֺ����� HandGrabInteractable ������Ʈ�� ���� �θ� Ž��
        //    ������Ʈ�� �°� �ʿ� �� ���� Ŀ���͸�����

        // (���� �⺻) interactor ���ӿ�����Ʈ�� �θ��ʿ��� HandGrabInteractable ã��
        var hg = interactor.GetComponentInParent<HandGrabInteractable>();
        if (hg) return hg.gameObject;

        // fallback: interactor Ʈ������ ��ó ����/���������� ã�Ƶ� ��(����)
        return null;
    }
}
