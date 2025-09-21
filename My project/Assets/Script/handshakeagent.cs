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
        // Grab ���� ����
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

            // ����Ʈ (�ϳ��¸� ����/��ٿ�)
            bool allowed = HandshakeOrchestrator.Instance && HandshakeOrchestrator.Instance.TryBegin(this, mode);
            if (!allowed) { wasGrabbing = now; return; }

            // (����) AOC ����: Base ������ ����ϰ� ���� ��ŵ
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

            // �� Ʈ���� ���� ���� ���� (���⼭ ���� ���� ������)
            if (!string.IsNullOrEmpty(mode.playTrigger))
            {
                if (!HasParam(animator, mode.playTrigger, AnimatorControllerParameterType.Trigger))
                {
                    Debug.LogWarning($"{DbgTag} Animator has NO Trigger '{mode.playTrigger}'. Check controller/params.");
                    wasGrabbing = now; return; // ���� �� �ϸ� ��� �� ��
                }

                animator.ResetTrigger(mode.playTrigger);
                animator.SetTrigger(mode.playTrigger);
                if (debugLogs) Debug.Log($"{DbgTag} Animator Trigger �� {mode.playTrigger}");
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


        // ���������� Grab ���� ���� �� Idle Ʈ���Ÿ� ��� (���� ����� ���� �ƴ�) ����������
        if (!now && wasGrabbing)
        {
            var mode = ResolveModeByTag(CurrentModeTag);
            if (!string.IsNullOrEmpty(mode.idleTrigger))
            {
                animator.ResetTrigger(mode.idleTrigger);
                animator.SetTrigger(mode.idleTrigger);
                if (debugLogs) Debug.Log($"{DbgTag} Animator Trigger �� {mode.idleTrigger}");
            }
            // ���⼭�� HandShake_on�� �ǵ帮�� �ʴ´�. (���� �ִϸ��̼��� Idle�� ���ƿ��� �ʾ��� �� ����)
        }

        // ���������� Idle ���� ���� ���� �� ����� ������ ���¡������� ���� ���� ����������
        var st = animator.GetCurrentAnimatorStateInfo(0);
        var modeForState = ResolveModeByTag(CurrentModeTag);
        bool isIdle = !animator.IsInTransition(0) &&
                      !string.IsNullOrEmpty(modeForState.idleStateName) &&
                      st.IsName(modeForState.idleStateName);

        // �� ���� ������ "isIdle && !now" �� ��ȭ (������ ��� ������ ���� ����)
        if (isIdle && !now && !string.IsNullOrEmpty(CurrentModeTag))
        {
            HandShake_on = false;                   // �� �������� false
            OnHandshakeEnd?.Invoke(this);           // �� �̶��� ���ɽ�Ʈ�����Ϳ� ���� �˸�
            if (debugLogs) Debug.Log($"{DbgTag} OnHandshakeEnd fired (idle='{modeForState.idleStateName}')");

            // (����) �⺻ ��Ʈ�ѷ� ����
            if (defaultController)
                animator.runtimeAnimatorController = defaultController;

            CurrentModeTag = "";
            CurrentGrabbedObject = null;
        }

        // �� HandShake_on�� �����Ӹ��� isIdle�� ���� �������� ����,
        //   - ���� �������� true
        //   - �� ���� ��Ͽ����� false �� �ٲټ���.

        // �������� ���� ����
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
        return false; // �� �� �̻� 0������ fallback �� ��
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
