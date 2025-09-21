using UnityEngine;
using UnityEngine.Animations.Rigging; // �� Multi-Parent Constraint �����

public class HeadTargetDriver : MonoBehaviour
{
    [Header("Refs")]
    public Transform headPivot;      // �Ӹ�(�Ǵ� ��) �� (ȸ�� ���� & ��ġ ����)
    public Transform headTarget;     // Multi-Parent Constraint�� Source (ȸ���� ON)
    public Transform playerHead;     // Main Camera (VR HMD)

    [Header("Constraint Control (optional)")]
    public MultiParentConstraint headConstraint;       // ȸ���� ON�� Multi-Parent
    [Range(0, 1)] public float followConstraintWeight = 1f;
    [Range(0, 1)] public float idleConstraintWeight = 0f;
    public float constraintWeightLerp = 12f;           // weight ������ �ӵ�

    [Header("Gating (optional)")]
    public bool onlyDuringHandshake = false;
    public HandshakeAgent handshakeAgent;

    [Header("Tuning")]
    [Tooltip("�� �� ���� (�ʿ��)")]
    public Vector3 rotationOffsetEuler = Vector3.zero;
    [Tooltip("ȸ�� ������ �ӵ�")]
    public float rotateSpeed = 12f;
    [Tooltip("�¿�(Yaw) ��밢(��). 0�̸� ������")]
    public float maxYaw = 70f;
    [Tooltip("����(Pitch) ��밢(��). 0�̸� ������")]
    public float maxPitch = 40f;

    [Header("Base Capture")]
    [Tooltip("���� �� ���� ȸ�� ĸó")]
    public bool recaptureBaseOnStart = true;
    [Tooltip("Follow ON �������� ���� ȸ�� ĸó")]
    public bool recaptureBaseOnFollowOn = true;

    [Header("On End (OFF edge) Reset")]
    [Tooltip("OFF �� HeadTarget ȸ���� �������� ��� ��������")]
    public bool resetTargetRotationOnEnd = true;
    [Tooltip("OFF �� ���� ON���� ������ ���� ĸó�ϵ��� �÷��� �ʱ�ȭ")]
    public bool clearBaseOnEnd = true;

    [Header("Debug")]
    public bool debugLogs = false;
    public bool debugGizmos = true;

    private Quaternion baseRotation;        // ���� ����(�߸�) ���� ȸ��
    private bool baseCaptured = false;
    private bool prevFollow = false;

    Quaternion RotOffset => Quaternion.Euler(rotationOffsetEuler);

    void Awake()
    {
        if (!playerHead && Camera.main) playerHead = Camera.main.transform;
    }

    void Start()
    {
        if (recaptureBaseOnStart) CaptureBase("Start");
        // �ʱ⿣ constraint�� idle weight�� �����ΰ� �����ϰ� ������ �Ʒ� �� ��:
        if (headConstraint) headConstraint.weight = idleConstraintWeight;
    }

    void LateUpdate()
    {
        if (!headPivot || !headTarget || !playerHead) return;

        bool follow = !onlyDuringHandshake || (handshakeAgent && handshakeAgent.HandShake_on);
        bool onEdge = follow && !prevFollow;
        bool offEdge = !follow && prevFollow;

        // �� OFF ����: �Ǽ� ���� ���� �� ����
        if (offEdge)
        {
            if (resetTargetRotationOnEnd && baseCaptured)
            {
                headTarget.rotation = baseRotation * RotOffset;
                if (debugLogs) Debug.Log($"[HeadTargetDriver:{name}] OFF �� reset headTarget to base");
            }
            if (headConstraint)
            {
                // ��� ���� ���� �ְ�, �Ʒ� weight ������ ������ �ڿ� ������ѵ� ��
                // headConstraint.weight = idleConstraintWeight;
            }
            if (clearBaseOnEnd)
            {
                baseCaptured = false; // ���� ON���� �� ���� ĸó
                if (debugLogs) Debug.Log($"[HeadTargetDriver:{name}] OFF �� clear baseCaptured");
            }
        }

        // �� ON ����: �Ǽ� ���� ���� �� ���� ��ĸó
        if (onEdge && recaptureBaseOnFollowOn)
        {
            CaptureBase("FollowON");
        }

        // ������ ���ٸ� ���� ĸó(���� 1ȸ �Ǵ� OFF ����)
        if (!baseCaptured) CaptureBase("Auto");

        // �� constraint weight ������
        if (headConstraint)
        {
            float wTarget = follow ? followConstraintWeight : idleConstraintWeight;
            headConstraint.weight = Mathf.MoveTowards(headConstraint.weight, wTarget, constraintWeightLerp * Time.deltaTime);
        }

        // �ȷο� �� �� �� ���⼭ ����
        if (!follow) { prevFollow = false; return; }

        // 1) ����(����) ȸ�� ��ǥ��� �÷��̾� ������ ��ȯ
        Vector3 toPlayer = playerHead.position - headPivot.position;
        if (toPlayer.sqrMagnitude < 1e-6f) { prevFollow = true; return; }

        Vector3 dirWorld = toPlayer.normalized;
        Vector3 dirLocal = Quaternion.Inverse(baseRotation) * dirWorld;

        // 2) yaw/pitch ���� + ����
        float yaw = Mathf.Atan2(dirLocal.x, dirLocal.z) * Mathf.Rad2Deg;           // �¿�
        float pitch = Mathf.Asin(Mathf.Clamp(dirLocal.y, -1f, 1f)) * Mathf.Rad2Deg;  // ����(+��)

        if (maxYaw > 0f) yaw = Mathf.Clamp(yaw, -maxYaw, maxYaw);
        if (maxPitch > 0f) pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);

        // 3) ���ѵ� ������ ���� ��ǥ�迡�� �ٽ� ȸ������ �����
        Quaternion targetWorldRot = baseRotation * Quaternion.Euler(pitch, yaw, 0f) * RotOffset;

        // 4) ������ �����Ͽ� HeadTarget�� ����
        float t = 1f - Mathf.Exp(-rotateSpeed * Time.deltaTime);
        headTarget.rotation = Quaternion.Slerp(headTarget.rotation, targetWorldRot, t);

        if (debugLogs)
            Debug.Log($"[HeadTargetDriver:{name}] follow=ON yaw={yaw:F1}, pitch={pitch:F1}, baseCaptured={baseCaptured}, w={(headConstraint ? headConstraint.weight : -1f):F2}");

        prevFollow = true;
    }

    void CaptureBase(string reason)
    {
        if (!headPivot) return;
        baseRotation = headPivot.rotation; // ���� �Ӹ��� ������ ȸ������ �������� ����
        baseCaptured = true;
        if (debugLogs) Debug.Log($"[HeadTargetDriver:{name}] Base captured ({reason}): {baseRotation.eulerAngles}");
    }

    void OnDrawGizmosSelected()
    {
        if (!debugGizmos || !headPivot) return;

        Gizmos.color = Color.cyan;   // ���� �Ӹ� forward
        Gizmos.DrawLine(headPivot.position, headPivot.position + headPivot.forward * 0.5f);

        if (baseCaptured)
        {
            Gizmos.color = Color.yellow; // ���� forward
            Vector3 f = baseRotation * Vector3.forward;
            Gizmos.DrawLine(headPivot.position, headPivot.position + f * 0.5f);
        }

        if (playerHead)
        {
            Gizmos.color = Color.green;  // �Ӹ����÷��̾� ����
            Gizmos.DrawLine(headPivot.position, playerHead.position);
        }
    }
}
