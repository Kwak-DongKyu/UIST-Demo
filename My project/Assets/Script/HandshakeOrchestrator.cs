using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class HandshakeOrchestrator : MonoBehaviour
{
    [Header("Agents (auto-fill if empty)")]
    public List<HandshakeAgent> agents = new();

    [Header("Haptics (single device)")]
    public HapticRendering2 haptics; // 기존 HapticRendering 컴포넌트

    // 현재 햅틱을 차지한 에이전트
    private HandshakeAgent owner;

    void Awake()
    {
        if (agents == null || agents.Count == 0)
            agents = FindObjectsOfType<HandshakeAgent>(true).ToList();

        foreach (var a in agents)
        {
            a.OnHandshakeStart += HandleStart;
            a.OnHandshakeEnd += HandleEnd;
        }

        if (!haptics) haptics = FindObjectOfType<HapticRendering2>(true);
    }

    void OnDestroy()
    {
        foreach (var a in agents)
        {
            if (!a) continue;
            a.OnHandshakeStart -= HandleStart;
            a.OnHandshakeEnd -= HandleEnd;
        }
    }

    void HandleStart(HandshakeAgent agent, AnimMode mode)
    {
        // 1) 현재 주인이 없으면 새 주인으로
        if (!owner)
        {
            SetOwner(agent);
            return;
        }

        // 2) 우선순위 정책 (예: 먼저 잡은 사람 유지)
        //    만약 "가까운 사람 우선"을 원하면 다음과 같이 교체:
        /*
        var player = Camera.main ? Camera.main.transform : null;
        if (player && agent.CurrentGrabbedObject)
        {
            float newD = Vector3.Distance(player.position, agent.CurrentGrabbedObject.transform.position);
            float oldD = owner && owner.CurrentGrabbedObject
                ? Vector3.Distance(player.position, owner.CurrentGrabbedObject.transform.position)
                : Mathf.Infinity;
            if (newD < oldD - 0.1f) SetOwner(agent);
        }
        */
    }

    void HandleEnd(HandshakeAgent agent)
    {
        if (owner == agent)
        {
            // 오너가 끝났으면 해제하고 햅틱 stop
            haptics?.StopHandshakeNow();
            owner = null;

            // 대기 중 다른 활성자에게 넘기고 싶다면 여기서 스캔해서 넘길 수도 있음
            // var next = agents.FirstOrDefault(a => a.HandShake_on);
            // if (next) SetOwner(next);
        }
    }

    void SetOwner(HandshakeAgent agent)
    {
        if (owner == agent) return;

        // 기존 오너 정지
        if (owner) haptics?.StopHandshakeNow();

        owner = agent;

        // HapticRendering에 “현재 상태는 이벤트 기반(Start/Stop)으로만” 맡김
        haptics?.BindAgent(owner);
        haptics?.StartHandshakeOnce(); // 애니메이션과 동시 시작
    }
}
