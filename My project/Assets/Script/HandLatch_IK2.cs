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
    public Transform leftInitialRef;   // �޼� ���̵� ���� ����
    public Transform rightInitialRef;  // ������ ���̵� ���� ����

    [Header("Initial Offsets (apply while idle)")]
    [Tooltip("�ʱ�(���̵�) ��� ������ �������� Ref�� ������ �������� ���� ����")]
    public bool useLocalInitialOffsetAxes = true;

    [Tooltip("�޼� �ʱ� ��ġ ������")]
    public Vector3 leftInitialPosOffset = Vector3.zero;
    [Tooltip("�޼� �ʱ� ȸ�� ������(��)")]
    public Vector3 leftInitialEulerOffset = Vector3.zero;

    [Tooltip("������ �ʱ� ��ġ ������")]
    public Vector3 rightInitialPosOffset = Vector3.zero;
    [Tooltip("������ �ʱ� ȸ�� ������(��)")]
    public Vector3 rightInitialEulerOffset = Vector3.zero;

    [Header("Right-hand Follow")]
    public Transform playerRightPalm;   // �÷��̾� ������ palm
    //public HandshakeManager handshake;  // handshake.HandShake_on
    public HandshakeAgent handshakeAgent;  // handshake.HandShake_on


    [Header("Offsets (RIGHT follow only)")]
    public bool autoCalibrateOnToggle = true;  // ON ���� ȸ�� ������ ĸó
    public Vector3 wristFixEuler = Vector3.zero;
    public Vector3 positionOffset = Vector3.zero;
    public Vector3 eulerRotationOffset = Vector3.zero;
    public bool useLocalOffsetAxes = false;

    [Header("Smoothing (RIGHT)")]
    [Range(0f, 1f)] public float followPosLerp = 1f;
    [Range(0f, 1f)] public float followRotLerp = 1f;

    // ���� ����
    private bool following = false;
    private bool prevHandshakeOn = false;
    private Quaternion latchedRotOffset = Quaternion.identity;

    void Start()
    {
        // �޼�/������ Ÿ���� "�ʱ� Ref + �ʱ� ������"���� ����
        SnapLeftIdle();
        SnapRightIdle();

        // �׻� IK=1 (���̵��� ���� Ÿ���� initialRef+offset�� ����Ű�Ƿ� �� �ڼ��� ������)
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
        //bool wantFollow = handshake.HandShake_on; // �����ո� ����

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

            following = true; // LateUpdate���� �÷��̾� �� ����
        }
        // OFF edge
        else if (!wantFollow && prevHandshakeOn)
        {
            // ���̵�(�ʱ�)�� ����: ������ Ÿ���� �ٽ� initialRef+offset����
            SnapRightIdle();
            following = false;
        }

        prevHandshakeOn = wantFollow;

        // �޼��� �׻� ���̵� ���� ����(Ȥ�� �ܺο��� ���� �ٲ���� ��� ���)
        if (!following)
            SnapLeftIdle();
            //SnapRightIdle();

    }

    void LateUpdate()
    {
        // ������ ���� ���� ���� �÷��̾� �� �������� Ÿ�� ������Ʈ
        if (!following) return;
        if (!playerRightPalm || !rightIKTarget) return;

        Vector3 targetPos = playerRightPalm.position;
        Quaternion targetRot = playerRightPalm.rotation;
        if (!IsFinite(targetPos) || !IsFinite(targetRot)) return;

        // ��ġ ȸ�� ������
        targetRot = targetRot * latchedRotOffset;

        // �ո� ����
        targetRot = targetRot * Quaternion.Euler(SafeEuler(wristFixEuler));

        // ������ ������
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

        // ������
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

        // �ʱ� ������ ����
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

        // �ʱ� ������ ����
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
