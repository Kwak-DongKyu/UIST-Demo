using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class HandshakeOrchestrator : MonoBehaviour
{
    [Header("Agents (auto-fill if empty)")]
    public List<HandshakeAgent> agents = new();

    [Header("Haptics (single device)")]
    public HapticRendering2 haptics; // ���� HapticRendering ������Ʈ

    // ���� ��ƽ�� ������ ������Ʈ
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
            haptics?.StartHandshakeForProfile(mode.hapticProfile); // �� �߰�
            return;
        }

        // (��ü ��å ���� ��쿡�� ���� �� �Ʒ�ó�� ȣ��)
        /*
        SetOwner(agent);
        haptics?.StartHandshakeForProfile(mode.hapticProfile);
        */
    }


    void HandleEnd(HandshakeAgent agent)
    {
        if (owner == agent)
        {
            // ���ʰ� �������� �����ϰ� ��ƽ stop
            haptics?.StopHandshakeNow();
            owner = null;

            // ��� �� �ٸ� Ȱ���ڿ��� �ѱ�� �ʹٸ� ���⼭ ��ĵ�ؼ� �ѱ� ���� ����
            // var next = agents.FirstOrDefault(a => a.HandShake_on);
            // if (next) SetOwner(next);
        }
    }

    void SetOwner(HandshakeAgent agent)
    {
        if (owner == agent) return;

        if (owner) haptics?.StopHandshakeNow();
        owner = agent;

        haptics?.BindAgent(owner); // �� ������ HandleStart���� profile�� ȣ��
    }

}
