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
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

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
        if (debugLogs) Debug.Log($"현재 [Orch] ALLOW {agent.name}, profile={pendingProfile}");
        return true;
    }


    // ★ HandleStart: 이 시점엔 Agent가 HandShake_on=true → 여기서 실제 시작
    // 도우미: 자식에서 찾아 enable 토글
    T EnableInChildren<T>(HandshakeAgent agent, bool on) where T : Behaviour
    {
        var c = agent ? agent.GetComponentInChildren<T>(true) : null;
        if (c) c.enabled = on;
        return c;
    }

    // HandshakeOrchestrator.HandleStart(...)
    // HandshakeOrchestrator.cs (기존 HandleStart에 보강)
    void HandleStart(HandshakeAgent agent, AnimMode mode)
    {
        if (owner != agent) return;

        // 0) Head follow(있으면) 에이전트 연결 + 세션 동안 활성화
        var head = agent.GetComponentInChildren<HeadTargetDriver>(true);
        if (head)
        {
            head.handshakeAgent = agent;
            head.onlyDuringHandshake = true;
            head.enabled = true; // 게이팅이 알아서 weight를 올림
        }

        // 1) LatchIK2: 즉시 캘리브레이트하여 한 프레임도 안 늦게 추종
        var latch = agent.GetComponent<HandLatchIK2>();
        if (latch)
        {
            latch.handshakeAgent = agent; // 바인딩 보강
            latch.enabled = true;
            latch.BeginFollowNow(doCalibrate: true);
        }

        // 2) Retargeter ON
        var retarget = agent.GetComponent<HandRetargeter>();
        if (retarget) retarget.EnableAndMaybeCalibrate(doCalib: true);

        // 3) (안전) Animator 트리거 재발사
        if (!string.IsNullOrEmpty(mode.playTrigger))
        {
            var anim = agent.animator;
            if (anim && HasParam(anim, mode.playTrigger, AnimatorControllerParameterType.Trigger))
            {
                anim.ResetTrigger(mode.playTrigger);
                anim.SetTrigger(mode.playTrigger);
            }
        }

        // 4) 햅틱 시작
        haptics?.BindAgent(owner);
        haptics?.StartHandshakeForProfile(pendingProfile);

        PlaySfxForProfile(pendingProfile);


        StartWatchdog();
        if (debugLogs) Debug.Log($"[Orch] START: latch/retarget/head/haptics all armed. profile={pendingProfile}");
    }


    IEnumerator PlaySfxAfterHapticsTick(HapticProfile p)
    {
        yield return null; // 한 프레임 대기
        if (haptics && haptics.IsRunning)
            PlaySfxForProfile(p);
    }



    // HandshakeOrchestrator.cs
    void HandleEnd(HandshakeAgent agent)
    {
        if (owner != agent) return;

        // 1) 햅틱 즉시 정지 + SFX 정지 (애니메이션과 동시 종료)
        if (haptics && haptics.IsRunning) haptics.StopHandshakeNow();
        StopCurrentSfx();

        // 2) Latch는 아이들로 스냅하고 끈다
        var latch = agent.GetComponent<HandLatchIK2>();
        if (latch)
        {
            latch.EndFollowAndSnap(); // 내부에서 Snap + following false
            latch.enabled = false;
        }

        // 3) Retargeter OFF
        //var retarget = agent.GetComponent<HandRetargeter>();
        //if (retarget) retarget.SessionBegin(doCalib: true);
        var retarget = agent.GetComponent<HandRetargeter>();
        if (retarget) retarget.SessionEndReset();



        // (선택) HeadTargetDriver는 게이팅으로 알아서 해제되지만,
        // 명시적으로 agent를 비우고 싶다면 여기서 처리할 수도 있음.

        // 4) 소유권/쿨다운 처리(워치독도 정리)
        ClearOwnerAndCooldown();

        if (debugLogs) Debug.Log("[Orch] END: all features stopped together (animation-ended).");
    }


    static bool HasParam(Animator anim, string name, AnimatorControllerParameterType type)
{
    if (!anim) return false;
    foreach (var p in anim.parameters)
        if (p.type == type && p.name == name) return true;
    return false;
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
    // HandshakeOrchestrator.cs
    void ForceRelease(string reason)
    {
        if (debugLogs) Debug.Log($"[Orch] ForceRelease ({reason})");

        // 햅틱/SFX 즉시 정지
        haptics?.StopHandshakeNow();
        StopCurrentSfx();

        // 현재 오너 클린다운 (Latch/Retargeter OFF + 스냅)
        if (owner)
        {
            var latch = owner.GetComponent<HandLatchIK2>();
            if (latch)
            {
                latch.EndFollowAndSnap();
                latch.enabled = false;
            }
            var retarget = owner.GetComponent<HandRetargeter>();
            if (retarget) retarget.enabled = false;
        }

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
        Debug.Log("현재 소리 재생 중"+ p);
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
