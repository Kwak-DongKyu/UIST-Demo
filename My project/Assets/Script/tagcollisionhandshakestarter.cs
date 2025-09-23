using UnityEngine;

[RequireComponent(typeof(Collider))]
public class TagCollisionHandshakeStarter : MonoBehaviour
{
    [Header("Agent on THIS avatar hand")]
    public HandshakeAgent agent;                // �� ���� HandshakeAgent
    [Tooltip("true�� �� ������Ʈ�� Tag(GrabA/B/C)�� ��� �±׷� �ڵ� ���")]
    public bool useSelfTagAsMode = true;
    [Tooltip("���� ������ �ʿ��ϸ� ���⿡ �Է� (useSelfTagAsMode=false�� ���� ���)")]
    public string overrideModeTag = "GrabA";

    [Header("Who can trigger?")]
    [Tooltip("�浹�� ������ �ݶ��̴��� �±�(�÷��̾� ��)")]
    public string playerHandTag = "PlayerHand";

    [Header("End policy")]
    public bool endOnExit = false;               // �浹�� ������ ���� ��û

    [Header("Debug")]
    public bool debugLogs = false;

    string ModeTag =>
        useSelfTagAsMode ? gameObject.tag : overrideModeTag;

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    void Awake()
    {
        Debug.Log("���� �ƹ�Ÿ�� �±״�" + ModeTag);
        if (!agent) agent = GetComponentInParent<HandshakeAgent>();
        // Ʈ���ŷθ� �����ϱ� ���ϴ� ����
        if (agent && agent.inputMode != HandshakeAgent.StartInputMode.Trigger)
            agent.inputMode = HandshakeAgent.StartInputMode.Trigger;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!agent) return;

        // �÷��̾� �� ����: �±׸� ����.
        if (!string.IsNullOrEmpty(playerHandTag) && !other.CompareTag(playerHandTag))
        {
            //if (debugLogs) Debug.Log($"[{name}] ignore enter by {other.name} (tag={other.tag})");
            return;
        }

        var tagToUse = ModeTag;
        if (string.IsNullOrEmpty(tagToUse))
        {
            if (debugLogs) Debug.LogWarning($"[{name}] ModeTag empty. Set useSelfTagAsMode or overrideModeTag.");
            return;
        }

        // �ƹ�Ÿ ���� �±�(GrabA/B/C)�� ����
        bool ok = agent.BeginByTag(tagToUse, other.gameObject);
        if (debugLogs) Debug.Log($"dddd [{name}] TriggerEnter by {other.name} �� Begin({tagToUse}) = {ok}");
    }

    void OnTriggerExit(Collider other)
    {
        if (!agent || !endOnExit) return;

        if (!string.IsNullOrEmpty(playerHandTag) && !other.CompareTag(playerHandTag))
            return;

        agent.EndExternal();
        if (debugLogs) Debug.Log($"[{name}] TriggerExit by {other.name} �� EndExternal()");
    }
}
