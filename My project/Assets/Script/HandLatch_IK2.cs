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
    [Tooltip("초기(아이들) 포즈에 적용할 오프셋을 Ref의 로컬축 기준으로 쓸지 여부")]
    public bool useLocalInitialOffsetAxes = true;

    [Tooltip("왼손 초기 위치 오프셋")]
    public Vector3 leftInitialPosOffset = Vector3.zero;
    [Tooltip("왼손 초기 회전 오프셋(도)")]
    public Vector3 leftInitialEulerOffset = Vector3.zero;

    [Tooltip("오른손 초기 위치 오프셋")]
    public Vector3 rightInitialPosOffset = Vector3.zero;
    [Tooltip("오른손 초기 회전 오프셋(도)")]
    public Vector3 rightInitialEulerOffset = Vector3.zero;

    [Header("Right-hand Follow")]
    public Transform playerRightPalm;   // 플레이어 오른손 palm
    //public HandshakeManager handshake;  // handshake.HandShake_on
    public HandshakeAgent handshakeAgent;  // handshake.HandShake_on


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
        // 왼손/오른손 타깃을 "초기 Ref + 초기 오프셋"으로 세팅
        SnapLeftIdle();
        SnapRightIdle();

        // 항상 IK=1 (아이들일 때는 타깃이 initialRef+offset을 가리키므로 그 자세가 유지됨)
        if (leftArmIK) leftArmIK.weight = 1f;
        if (rightArmIK) rightArmIK.weight = 1f;

        following = false;
        prevHandshakeOn = false;
    }

    void Update()
    {
        if (!handshakeAgent) return;
        bool wantFollow = handshakeAgent.HandShake_on;
        
        //if (!handshake) return;
        //bool wantFollow = handshake.HandShake_on; // 오른손만 추종

        // ON edge
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
        // OFF edge
        else if (!wantFollow && prevHandshakeOn)
        {
            // 아이들(초기)로 복귀: 오른손 타깃을 다시 initialRef+offset으로
            SnapRightIdle();
            following = false;
        }

        prevHandshakeOn = wantFollow;

        // 왼손은 항상 아이들 포즈 유지(혹시 외부에서 값이 바뀌었을 경우 대비)
        if (!following)
            SnapLeftIdle();
            //SnapRightIdle();

    }

    void LateUpdate()
    {
        // 오른손 추종 중일 때만 플레이어 손 기준으로 타깃 업데이트
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

        // 스무딩
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

        // 초기 오프셋 적용
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

        // 초기 오프셋 적용
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

    // ---------- Utilities ----------
    static bool IsFinite(Vector3 v)
    {
        return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                 float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }
    static bool IsFinite(Quaternion q)
    {
        return !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w) ||
                 float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w));
    }
    static Vector3 SafeEuler(Vector3 e)
    {
        e.x = Mathf.Repeat(e.x + 360f, 720f) - 360f;
        e.y = Mathf.Repeat(e.y + 360f, 720f) - 360f;
        e.z = Mathf.Repeat(e.z + 360f, 720f) - 360f;
        return e;
    }
}
