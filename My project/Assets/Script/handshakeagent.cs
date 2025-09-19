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

    // �̺�Ʈ: ����(� ���), ����
    public event Action<HandshakeAgent, AnimMode> OnHandshakeStart;
    public event Action<HandshakeAgent> OnHandshakeEnd;

    private bool wasGrabbing;

    // �� �߰�: ���õ�(����) ������Ʈ ĳ�� + ����� ���
    private GameObject _cachedGrabbedGO;

    [Header("Debug")]
    public bool debugLogs = false;
    private string DbgTag => $"[HandshakeAgent:{name}]";

    // (����) �⺻ ��Ʈ�ѷ� ������
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
    // �� ��� ���� ��ȭ�� �̺�Ʈ�� ĳġ�ؼ� ���õ� GO�� ĳ��
    private void OnInteractorStateChanged(InteractorStateChangeArgs args)
    {
        if (args.NewState == InteractorState.Select)
        {
            _cachedGrabbedGO = GetSelectedGameObject();
            if (debugLogs) Debug.Log($"{DbgTag} SELECT �� {_cachedGrabbedGO?.name ?? "null"}");
        }
        else if (args.NewState == InteractorState.Hover || args.NewState == InteractorState.Normal)
        {
            if (debugLogs && _cachedGrabbedGO) Debug.Log($"{DbgTag} DESELECT �� clear {_cachedGrabbedGO.name}");
            _cachedGrabbedGO = null;
        }
    }

    // �� ���� ����(���)�� Interactable�κ��� GameObject ��� (���� ���� ����)
    private GameObject GetSelectedGameObject()
    {
        // ���� Ȯ��: SelectedInteractable�� MonoBehaviour�� ĳ����
        var selMb = interactor.SelectedInteractable as MonoBehaviour;
        if (selMb) return selMb.gameObject;

        // ���� 1: interactor �ֺ����� HandGrabInteractable ����
        var hgi = interactor.GetComponentInParent<HandGrabInteractable>();
        if (hgi) return hgi.gameObject;

        // ���� 2: null
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

        // ���������� Grab ���� ���� ����������
        if (now && !wasGrabbing)
        {
            // �� �̺�Ʈ ĳ�� �켱, ������ ��� ��ȸ
            CurrentGrabbedObject = _cachedGrabbedGO ?? GetSelectedGameObject();
            if (debugLogs) Debug.Log($"{DbgTag} StartEdge: sel={(CurrentGrabbedObject ? CurrentGrabbedObject.name : "null")}");

            // �� �� ����(GrabOwner) üũ
            var ownerComp = CurrentGrabbedObject ? CurrentGrabbedObject.GetComponentInParent<GrabOwner>() : null;
            bool mine = ownerComp && ownerComp.owner == this;
            if (debugLogs)
            {
                var ownerName = ownerComp ? (ownerComp.owner ? ownerComp.owner.name : "nullOwnerField") : "noOwnerComp";
                Debug.Log($"{DbgTag} OwnerCheck: mine={mine} (ownerComp={ownerName})");
            }

            if (!mine)
            {
                // �� ���� �ƴϸ� Ʈ����/�̺�Ʈ ���� ���� (���� �Ҹ�� �ؼ� �ߺ� ����)
                wasGrabbing = now;
                return;
            }

            var mode = ResolveModeByTag(CurrentGrabbedObject ? CurrentGrabbedObject.tag : "");
            if (debugLogs) Debug.Log($"{DbgTag} ModeResolved: tag='{mode.tag}', play='{mode.playTrigger}' idle='{mode.idleTrigger}'");

            // (����) AOC Base üũ �� ����
            if (mode.overrideController && mode.overrideController.runtimeAnimatorController != null)
            {
                animator.runtimeAnimatorController = mode.overrideController;
                if (debugLogs) Debug.Log($"{DbgTag} AOC applied: {mode.overrideController.name}");
            }

            // Ʈ���� �߻�
            if (!string.IsNullOrEmpty(mode.playTrigger))
            {
                animator.ResetTrigger(mode.playTrigger);
                animator.SetTrigger(mode.playTrigger);
                if (debugLogs) Debug.Log($"{DbgTag} Animator Trigger �� {mode.playTrigger}");
            }

            CurrentModeTag = mode.tag;
            HandShake_on = true;
            OnHandshakeStart?.Invoke(this, mode);
            if (debugLogs) Debug.Log($"{DbgTag} OnHandshakeStart fired");
        }

        // ���������� Grab ���� ���� �� Idle ����������
        if (!now && wasGrabbing)
        {
            var mode = ResolveModeByTag(CurrentModeTag);
            if (!string.IsNullOrEmpty(mode.idleTrigger))
            {
                animator.ResetTrigger(mode.idleTrigger);
                animator.SetTrigger(mode.idleTrigger);
                if (debugLogs) Debug.Log($"{DbgTag} Animator Trigger �� {mode.idleTrigger}");
            }
        }

        // ���������� Idle ���� ���� ���� ����������
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

            // (����) �⺻ ��Ʈ�ѷ� ����
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
