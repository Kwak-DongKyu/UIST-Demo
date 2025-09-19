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
        if (!owner)
        {
            SetOwner(agent);
            haptics?.StartHandshakeForProfile(mode.hapticProfile); // ← 추가
            return;
        }

        // (교체 정책 쓰는 경우에도 세팅 후 아래처럼 호출)
        /*
        SetOwner(agent);
        haptics?.StartHandshakeForProfile(mode.hapticProfile);
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

        if (owner) haptics?.StopHandshakeNow();
        owner = agent;

        haptics?.BindAgent(owner); // ← 시작은 HandleStart에서 profile로 호출
    }

}
