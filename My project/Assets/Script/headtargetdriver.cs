using UnityEngine;

public class HeadTargetDriver : MonoBehaviour
{
    [Header("Refs")]
    public Transform headPivot;      // �Ӹ�(�Ǵ� ��) �� (ȸ�� ���� & ��ġ ����)
    public Transform headTarget;     // Multi-Parent Constraint�� Source (ȸ���� ON)
    public Transform playerHead;     // Main Camera (VR HMD)

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
    [Tooltip("���� �� �Ǵ� Follow ON ������ ���� ȸ���� ĸó")]
    public bool recaptureBaseOnStart = true;
    public bool recaptureBaseOnFollowOn = true;

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
    }

    void LateUpdate()
    {
        if (!headPivot || !headTarget || !playerHead) return;

        bool follow =
            !onlyDuringHandshake ||
            (handshakeAgent && handshakeAgent.HandShake_on);

        // Follow ON �������� ���� ȸ�� ĸó
        if (recaptureBaseOnFollowOn && follow && !prevFollow)
            CaptureBase("FollowON");

        // ������ ���ٸ� �����̶� �� �� ����
        if (!baseCaptured)
            CaptureBase("Auto");

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
            Debug.Log($"[HeadTargetDriver:{name}] yaw={yaw:F1}, pitch={pitch:F1}, baseCaptured={baseCaptured}");

        prevFollow = true;
    }

    void CaptureBase(string reason)
    {
        if (!headPivot) return;
        baseRotation = headPivot.rotation; // ���� �Ӹ��� ���� ȸ���� ���߸������� ����
        baseCaptured = true;
        if (debugLogs) Debug.Log($"[HeadTargetDriver:{name}] Base captured ({reason}): {baseRotation.eulerAngles}");
    }

    void OnDrawGizmosSelected()
    {
        if (!debugGizmos || !headPivot) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(headPivot.position, headPivot.position + headPivot.forward * 0.5f);

        if (baseCaptured)
        {
            Gizmos.color = Color.yellow;
            Vector3 f = baseRotation * Vector3.forward;
            Gizmos.DrawLine(headPivot.position, headPivot.position + f * 0.5f);
        }

        if (playerHead)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(headPivot.position, playerHead.position);
        }
    }
}
