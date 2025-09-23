using UnityEngine;
using UnityEngine.Animations.Rigging;

public class HandLatchIK2 : MonoBehaviour
{
    [Header("IK (Left / Right)")]
    public TwoBoneIKConstraint leftArmIK;
    public Transform leftIKTarget;
    public TwoBoneIKConstraint rightArmIK;
    public Transform rightIKTarget;

    [Header("Initial Pose Refs (Idle pose)")]
    public Transform leftInitialRef;   // 왼손 아이들 포즈 기준
    public Transform rightInitialRef;  // 오른손 아이들 포즈 기준

    [Header("Initial Offsets (apply while idle)")]
    public bool useLocalInitialOffsetAxes = true;
    public Vector3 leftInitialPosOffset = Vector3.zero;
    public Vector3 leftInitialEulerOffset = Vector3.zero;
    public Vector3 rightInitialPosOffset = Vector3.zero;
    public Vector3 rightInitialEulerOffset = Vector3.zero;

    [Header("Right-hand Follow")]
    public Transform playerRightPalm;          // 플레이어 오른손 palm
    public HandshakeAgent handshakeAgent;      // 악수 상태를 알려주는 Agent

    [Header("Offsets (RIGHT follow only)")]
    public bool autoCalibrateOnToggle = true;  // ON 순간 회전 오프셋 캡처
    public Vector3 wristFixEuler = Vector3.zero;
    public Vector3 positionOffset = Vector3.zero;
    public Vector3 eulerRotationOffset = Vector3.zero;
    public bool useLocalOffsetAxes = false;

    [Header("Smoothing (RIGHT)")]
    [Range(0f, 1f)] public float followPosLerp = 1f;
    [Range(0f, 1f)] public float followRotLerp = 1f;

    // 내부 상태
    private bool following = false;
    private bool prevHandshakeOn = false;
    private Quaternion latchedRotOffset = Quaternion.identity;

    void Start()
    {
        SnapLeftIdle();
        SnapRightIdle();

        if (leftArmIK) leftArmIK.weight = 1f;
        if (rightArmIK) rightArmIK.weight = 1f;

        following = false;
        prevHandshakeOn = false;
    }
    void OnEnable()
    {
        // 켜졌을 때 이미 세션 중이면 바로 따라붙도록
        if (handshakeAgent && handshakeAgent.HandShake_on)
        {
            prevHandshakeOn = false; // ON edge를 만들거나
            following = true;        // 즉시 활성화
        }
        
    }

    // 오케스트레이터에서 종료 직전 호출해 아이들 포즈로 스냅
    public void ForceIdleSnap()
    {
        SnapLeftIdle();
        SnapRightIdle();
    }


    void Update()
    {
        if (!handshakeAgent) return;

        bool wantFollow = handshakeAgent.HandShake_on;
        if (wantFollow) Debug.Log("dddd latch 실행 중임"+ handshakeAgent.name);
        //else Debug.Log("dddd latch 실행 안하는 중");

        // ─────────────── ON edge (Handshake 시작) ───────────────
        if (wantFollow && !prevHandshakeOn)
        {
            if (autoCalibrateOnToggle && playerRightPalm && rightIKTarget
                && IsFinite(playerRightPalm.rotation) && IsFinite(rightIKTarget.rotation))
            {
                latchedRotOffset = Quaternion.Inverse(playerRightPalm.rotation) * rightIKTarget.rotation;
                if (!IsFinite(latchedRotOffset)) latchedRotOffset = Quaternion.identity;
            }
            else
            {
                latchedRotOffset = Quaternion.identity;
            }

            following = true; // LateUpdate부터 플레이어 손 추종
        }
        // ─────────────── OFF edge (Handshake 종료) ───────────────
        else if (!wantFollow && prevHandshakeOn)
        {
            SnapRightIdle();
            following = false;
        }

        prevHandshakeOn = wantFollow;

        // 왼손은 항상 아이들 포즈 유지
        if (!following)
            SnapLeftIdle();
    }

    void LateUpdate()
    {
        if (!following) return;
        if (!playerRightPalm || !rightIKTarget) return;

        Vector3 targetPos = playerRightPalm.position;
        Quaternion targetRot = playerRightPalm.rotation;
        if (!IsFinite(targetPos) || !IsFinite(targetRot)) return;

        // 러치 회전 오프셋
        targetRot = targetRot * latchedRotOffset;

        // 손목 보정
        targetRot = targetRot * Quaternion.Euler(SafeEuler(wristFixEuler));

        // 추종용 오프셋
        if (useLocalOffsetAxes)
        {
            Vector3 add = playerRightPalm.TransformVector(positionOffset);
            if (!IsFinite(add)) add = Vector3.zero;
            targetPos += add;
            targetRot = targetRot * Quaternion.Euler(SafeEuler(eulerRotationOffset));
        }
        else
        {
            targetPos += positionOffset;
            targetRot = Quaternion.Euler(SafeEuler(eulerRotationOffset)) * targetRot;
        }

        if (!IsFinite(targetPos) || !IsFinite(targetRot)) return;

        // 스무딩 적용
        rightIKTarget.position = (followPosLerp < 1f)
            ? Vector3.Lerp(rightIKTarget.position, targetPos, followPosLerp)
            : targetPos;

        rightIKTarget.rotation = (followRotLerp < 1f)
            ? Quaternion.Slerp(rightIKTarget.rotation, targetRot, followRotLerp)
            : targetRot;
    }

    // ---------- Idle snap helpers ----------
    private void SnapLeftIdle()
    {
        if (!leftIKTarget || !leftInitialRef) return;

        Vector3 pos = leftInitialRef.position;
        Quaternion rot = leftInitialRef.rotation;

        if (useLocalInitialOffsetAxes)
        {
            pos += leftInitialRef.TransformVector(leftInitialPosOffset);
            rot = rot * Quaternion.Euler(SafeEuler(leftInitialEulerOffset));
        }
        else
        {
            pos += leftInitialPosOffset;
            rot = Quaternion.Euler(SafeEuler(leftInitialEulerOffset)) * rot;
        }

        if (!IsFinite(pos) || !IsFinite(rot)) return;

        leftIKTarget.position = pos;
        leftIKTarget.rotation = rot;
    }

    private void SnapRightIdle()
    {
        if (!rightIKTarget || !rightInitialRef) return;

        Vector3 pos = rightInitialRef.position;
        Quaternion rot = rightInitialRef.rotation;

        if (useLocalInitialOffsetAxes)
        {
            pos += rightInitialRef.TransformVector(rightInitialPosOffset);
            rot = rot * Quaternion.Euler(SafeEuler(rightInitialEulerOffset));
        }
        else
        {
            pos += rightInitialPosOffset;
            rot = Quaternion.Euler(SafeEuler(rightInitialEulerOffset)) * rot;
        }

        if (!IsFinite(pos) || !IsFinite(rot)) return;

        rightIKTarget.position = pos;
        rightIKTarget.rotation = rot;
    }
    // HandLatchIK2 내부 아무 곳

    public void BeginFollowNow(bool doCalibrate = true)
    {
        if (doCalibrate && autoCalibrateOnToggle && playerRightPalm && rightIKTarget
            && IsFinite(playerRightPalm.rotation) && IsFinite(rightIKTarget.rotation))
        {
            latchedRotOffset = Quaternion.Inverse(playerRightPalm.rotation) * rightIKTarget.rotation;
            if (!IsFinite(latchedRotOffset)) latchedRotOffset = Quaternion.identity;
        }
        else
        {
            latchedRotOffset = Quaternion.identity;
        }
        following = true;
        prevHandshakeOn = true; // 핸드쉐이크 이벤트 없이 직접 시작할 때도 바로 따라가게
    }

    public void EndFollowAndSnap()
    {
        following = false;
        prevHandshakeOn = false;
        SnapRightIdle();
        SnapLeftIdle();
    }

    public void SnapBothIdle() { SnapLeftIdle(); SnapRightIdle(); }

    // ---------- Utilities ----------
    static bool IsFinite(Vector3 v) =>
        !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
          float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

    static bool IsFinite(Quaternion q) =>
        !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w) ||
          float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w));

    static Vector3 SafeEuler(Vector3 e)
    {
        e.x = Mathf.Repeat(e.x + 360f, 720f) - 360f;
        e.y = Mathf.Repeat(e.y + 360f, 720f) - 360f;
        e.z = Mathf.Repeat(e.z + 360f, 720f) - 360f;
        return e;
    }
}
