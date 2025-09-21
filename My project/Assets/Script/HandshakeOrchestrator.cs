using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class HandshakeOrchestrator : MonoBehaviour
{
    public static HandshakeOrchestrator Instance { get; private set; }

    [Header("Agents (auto-fill if empty)")]
    public List<HandshakeAgent> agents = new();

    [Header("Haptics (single device)")]
    public HapticRendering2 haptics;

    [Header("Policy")]
    [Tooltip("하나의 세션이 끝난 뒤 이 시간 동안 시작 금지")]
    public float cooldownSeconds = 1f;

    [Header("Debug")]
    public bool debugLogs = false;

    // 내부 상태
    private HandshakeAgent owner;
    private float cooldownUntil = -1f;
    private Coroutine watchdog;

    // HandshakeOrchestrator 클래스 안에 추가
    [Header("SFX")]
    public AudioSource weakSfx;    // 약
    public AudioSource middleSfx;  // 중
    public AudioSource strongSfx;  // 강

    private AudioSource currentSfx; // 내부: 지금 재생 중인 소스

    void Awake()
    {
        Instance = this;

        if (agents == null || agents.Count == 0)
            agents = FindObjectsOfType<HandshakeAgent>(true).ToList();

        foreach (var a in agents)
        {
            a.OnHandshakeStart += HandleStart; // 이제는 거의 no-op (로그용)
            a.OnHandshakeEnd += HandleEnd;
        }

        if (!haptics) haptics = FindObjectOfType<HapticRendering2>(true);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;

        foreach (var a in agents)
        {
            if (!a) continue;
            a.OnHandshakeStart -= HandleStart;
            a.OnHandshakeEnd -= HandleEnd;
        }
    }

    private HapticProfile pendingProfile;
    // ★ Agent가 시작 엣지에서 호출
    // ★ TryBegin: 오너만 세팅하고 프로필만 기억 (여기서는 햅틱 시작하지 않음)
    // ★ TryBegin: 오너/프로필만 설정 (햅틱 시작 금지!)
    public bool TryBegin(HandshakeAgent agent, AnimMode mode)
    {
        // 1) 쿨다운 중엔 무조건 거부
        if (Time.time < cooldownUntil)
        {
            if (debugLogs) Debug.Log($"[Orch] DENY (cooldown {cooldownUntil - Time.time:F2}s)");
            return false;
        }

        // 2) 누가 진행 중이면(오너 있거나 햅틱 코루틴 동작 중이면) 절대 거부 — 선점 금지
        if (owner != null || (haptics && haptics.IsRunning))
        {
            if (debugLogs)
            {
                string why = owner ? $"owner={owner.name}" : "haptics running";
                Debug.Log($"[Orch] DENY (busy: {why})");
            }
            return false;
        }

        // 3) 여기서만 오너 지정 + 프로필 기억 (햅틱은 아직 시작하지 않음)
        SetOwner(agent);
        pendingProfile = mode.hapticProfile;
        if (debugLogs) Debug.Log($"[Orch] ALLOW {agent.name}, profile={pendingProfile}");
        return true;
    }


    // ★ HandleStart: 이 시점엔 Agent가 HandShake_on=true → 여기서 실제 시작
    // HandleStart(...) 안에서, haptics 시작 직후 한 줄 추가
    void HandleStart(HandshakeAgent agent, AnimMode mode)
    {
        Debug.Log("handlestart 시작함" + pendingProfile);
        if (owner != agent) return;

        haptics?.StartHandshakeForProfile(pendingProfile);
        PlaySfxForProfile(pendingProfile);   // ★ 추가: 소리 재생

        StartWatchdog();
        if (debugLogs) Debug.Log($"[Orch] START haptics for {agent.name} ({pendingProfile})");
    }


    void HandleEnd(HandshakeAgent agent)
    {
        if (owner == agent)
        {
            // ❌ 기존: 즉시 haptics.StopHandshakeNow();
            // ✅ 변경: 햅틱이 스스로 끝날 때까지 대기 후 해제/쿨다운
            if (debugLogs) Debug.Log($"[Orch] HandleEnd from owner {agent.name} → wait haptics to finish");
            StartCoroutine(ReleaseAfterHaptics());
        }
    }

    // ReleaseAfterHaptics() 마지막에 SFX도 정리
    IEnumerator ReleaseAfterHaptics()
    {
        while (haptics && haptics.IsRunning)
            yield return null;

        StopCurrentSfx();            // ★ 추가: 소리 정지
        ClearOwnerAndCooldown();
    }


    void SetOwner(HandshakeAgent agent)
    {
        if (owner == agent) return;
        if (owner) haptics?.StopHandshakeNow();
        owner = agent;
        haptics?.BindAgent(owner);
    }

    // ForceRelease(...) , ClearOwnerAndCooldown()에서도 혹시 몰라 한 번 더 정리
    void ForceRelease(string reason)
    {
        if (debugLogs) Debug.Log($"[Orch] ForceRelease ({reason})");
        haptics?.StopHandshakeNow();
        StopCurrentSfx();            // ★ 추가
        ClearOwnerAndCooldown();
    }

    void ClearOwnerAndCooldown()
    {
        owner = null;
        cooldownUntil = Time.time + Mathf.Max(0f, cooldownSeconds);
        StopCurrentSfx();            // ★ 추가
        if (watchdog != null) { StopCoroutine(watchdog); watchdog = null; }
    }

    // Orchestrator 클래스 안 아무 곳에 추가
    void PlaySfxForProfile(HapticProfile p)
    {
        // 먼저 이전 소리 정지
        StopCurrentSfx();

        // 프로필 → 소스 매핑
        AudioSource src = null;
        switch (p)
        {
            case HapticProfile.Strong: src = strongSfx; break;
            case HapticProfile.Middle: src = middleSfx; break;
            default: src = weakSfx; break;
        }

        // 재생
        if (src)
        {
            // 중복 재생 방지
            src.Stop();
            src.Play();
            currentSfx = src;
        }
    }

    void StopCurrentSfx()
    {
        if (currentSfx && currentSfx.isPlaying)
            currentSfx.Stop();
        currentSfx = null;
    }

    void StartWatchdog()
    {
        if (watchdog != null) StopCoroutine(watchdog);
        float timeout = (haptics ? haptics.handshakeDuration : 4f) + 0.25f; // 살짝 여유
        watchdog = StartCoroutine(Watchdog(timeout));
    }

    IEnumerator Watchdog(float timeout)
    {
        yield return new WaitForSeconds(timeout);
        // 지정 시간 내 HandleEnd가 안 왔다면 강제 정리 (애니메이션만 돌고 햅틱 안도는 꼬임 방지)
        if (owner != null)
        {
            if (debugLogs) Debug.Log("[Orch] Watchdog timeout → force release");
            ForceRelease("watchdog timeout");
        }
        watchdog = null;
    }
}
