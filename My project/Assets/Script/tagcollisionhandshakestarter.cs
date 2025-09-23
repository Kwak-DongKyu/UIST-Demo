using UnityEngine;

[RequireComponent(typeof(Collider))]
public class TagCollisionHandshakeStarter : MonoBehaviour
{
    [Header("Agent on THIS avatar hand")]
    public HandshakeAgent agent;                // 이 손의 HandshakeAgent
    [Tooltip("true면 이 오브젝트의 Tag(GrabA/B/C)를 모드 태그로 자동 사용")]
    public bool useSelfTagAsMode = true;
    [Tooltip("수동 지정이 필요하면 여기에 입력 (useSelfTagAsMode=false일 때만 사용)")]
    public string overrideModeTag = "GrabA";

    [Header("Who can trigger?")]
    [Tooltip("충돌해 들어오는 콜라이더의 태그(플레이어 손)")]
    public string playerHandTag = "PlayerHand";

    [Header("End policy")]
    public bool endOnExit = false;               // 충돌이 끝나면 종료 요청

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
        Debug.Log("현재 아바타의 태그는" + ModeTag);
        if (!agent) agent = GetComponentInParent<HandshakeAgent>();
        // 트리거로만 시작하길 원하니 보장
        if (agent && agent.inputMode != HandshakeAgent.StartInputMode.Trigger)
            agent.inputMode = HandshakeAgent.StartInputMode.Trigger;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!agent) return;

        // 플레이어 손 필터: 태그만 본다.
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

        // 아바타 손의 태그(GrabA/B/C)로 시작
        bool ok = agent.BeginByTag(tagToUse, other.gameObject);
        if (debugLogs) Debug.Log($"dddd [{name}] TriggerEnter by {other.name} → Begin({tagToUse}) = {ok}");
    }

    void OnTriggerExit(Collider other)
    {
        if (!agent || !endOnExit) return;

        if (!string.IsNullOrEmpty(playerHandTag) && !other.CompareTag(playerHandTag))
            return;

        agent.EndExternal();
        if (debugLogs) Debug.Log($"[{name}] TriggerExit by {other.name} → EndExternal()");
    }
}
