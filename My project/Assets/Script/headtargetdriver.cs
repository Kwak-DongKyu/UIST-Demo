using UnityEngine;

public class HeadTargetDriver : MonoBehaviour
{
    [Header("Refs")]
    public Transform headPivot;      // 머리(또는 목) 본 (회전 기준 & 위치 기준)
    public Transform headTarget;     // Multi-Parent Constraint의 Source (회전만 ON)
    public Transform playerHead;     // Main Camera (VR HMD)

    [Header("Gating (optional)")]
    public bool onlyDuringHandshake = false;
    public HandshakeAgent handshakeAgent;

    [Header("Tuning")]
    [Tooltip("모델 축 보정 (필요시)")]
    public Vector3 rotationOffsetEuler = Vector3.zero;
    [Tooltip("회전 스무딩 속도")]
    public float rotateSpeed = 12f;
    [Tooltip("좌우(Yaw) 허용각(도). 0이면 무제한")]
    public float maxYaw = 70f;
    [Tooltip("상하(Pitch) 허용각(도). 0이면 무제한")]
    public float maxPitch = 40f;

    [Header("Base Capture")]
    [Tooltip("시작 시 또는 Follow ON 순간에 기준 회전을 캡처")]
    public bool recaptureBaseOnStart = true;
    public bool recaptureBaseOnFollowOn = true;

    [Header("Debug")]
    public bool debugLogs = false;
    public bool debugGizmos = true;

    private Quaternion baseRotation;        // 고정 기준(중립) 월드 회전
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

        // Follow ON 엣지에서 기준 회전 캡처
        if (recaptureBaseOnFollowOn && follow && !prevFollow)
            CaptureBase("FollowON");

        // 기준이 없다면 지금이라도 한 번 잡자
        if (!baseCaptured)
            CaptureBase("Auto");

        if (!follow) { prevFollow = false; return; }

        // 1) 기준(고정) 회전 좌표계로 플레이어 방향을 변환
        Vector3 toPlayer = playerHead.position - headPivot.position;
        if (toPlayer.sqrMagnitude < 1e-6f) { prevFollow = true; return; }

        Vector3 dirWorld = toPlayer.normalized;
        Vector3 dirLocal = Quaternion.Inverse(baseRotation) * dirWorld;

        // 2) yaw/pitch 추출 + 제한
        float yaw = Mathf.Atan2(dirLocal.x, dirLocal.z) * Mathf.Rad2Deg;           // 좌우
        float pitch = Mathf.Asin(Mathf.Clamp(dirLocal.y, -1f, 1f)) * Mathf.Rad2Deg;  // 상하(+위)

        if (maxYaw > 0f) yaw = Mathf.Clamp(yaw, -maxYaw, maxYaw);
        if (maxPitch > 0f) pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);

        // 3) 제한된 방향을 기준 좌표계에서 다시 회전으로 만들기
        Quaternion targetWorldRot = baseRotation * Quaternion.Euler(pitch, yaw, 0f) * RotOffset;

        // 4) 스무딩 적용하여 HeadTarget에 세팅
        float t = 1f - Mathf.Exp(-rotateSpeed * Time.deltaTime);
        headTarget.rotation = Quaternion.Slerp(headTarget.rotation, targetWorldRot, t);

        if (debugLogs)
            Debug.Log($"[HeadTargetDriver:{name}] yaw={yaw:F1}, pitch={pitch:F1}, baseCaptured={baseCaptured}");

        prevFollow = true;
    }

    void CaptureBase(string reason)
    {
        if (!headPivot) return;
        baseRotation = headPivot.rotation; // 현재 머리의 월드 회전을 ‘중립’으로 저장
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
