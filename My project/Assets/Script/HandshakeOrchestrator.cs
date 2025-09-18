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
        // 1) ���� ������ ������ �� ��������
        if (!owner)
        {
            SetOwner(agent);
            return;
        }

        // 2) �켱���� ��å (��: ���� ���� ��� ����)
        //    ���� "����� ��� �켱"�� ���ϸ� ������ ���� ��ü:
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

        // ���� ���� ����
        if (owner) haptics?.StopHandshakeNow();

        owner = agent;

        // HapticRendering�� ������ ���´� �̺�Ʈ ���(Start/Stop)���θ��� �ñ�
        haptics?.BindAgent(owner);
        haptics?.StartHandshakeOnce(); // �ִϸ��̼ǰ� ���� ����
    }
}
