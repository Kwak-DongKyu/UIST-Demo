using UnityEngine;
using UnityEngine.Animations.Rigging; // ★ Multi-Parent Constraint 제어용

public class HeadTargetDriver : MonoBehaviour
{
    [Header("Refs")]
    public Transform headPivot;      // 머리(또는 목) 본 (회전 기준 & 위치 기준)
    public Transform headTarget;     // Multi-Parent Constraint의 Source (회전만 ON)
    public Transform playerHead;     // Main Camera (VR HMD)

    [Header("Constraint Control (optional)")]
    public MultiParentConstraint headConstraint;       // 회전만 ON인 Multi-Parent
    [Range(0, 1)] public float followConstraintWeight = 1f;
    [Range(0, 1)] public float idleConstraintWeight = 0f;
    public float constraintWeightLerp = 12f;           // weight 스무딩 속도

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
    [Tooltip("시작 시 기준 회전 캡처")]
    public bool recaptureBaseOnStart = true;
    [Tooltip("Follow ON 엣지에서 기준 회전 캡처")]
    public bool recaptureBaseOnFollowOn = true;

    [Header("On End (OFF edge) Reset")]
    [Tooltip("OFF 시 HeadTarget 회전을 기준으로 즉시 돌려놓기")]
    public bool resetTargetRotationOnEnd = true;
    [Tooltip("OFF 시 다음 ON에서 기준을 새로 캡처하도록 플래그 초기화")]
    public bool clearBaseOnEnd = true;

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
        // 초기엔 constraint를 idle weight로 내려두고 시작하고 싶으면 아래 한 줄:
        if (headConstraint) headConstraint.weight = idleConstraintWeight;
    }

    void LateUpdate()
    {
        if (!headPivot || !headTarget || !playerHead) return;

        bool follow = !onlyDuringHandshake || (handshakeAgent && handshakeAgent.HandShake_on);
        bool onEdge = follow && !prevFollow;
        bool offEdge = !follow && prevFollow;

        // ★ OFF 엣지: 악수 종료 순간 → 리셋
        if (offEdge)
        {
            if (resetTargetRotationOnEnd && baseCaptured)
            {
                headTarget.rotation = baseRotation * RotOffset;
                if (debugLogs) Debug.Log($"[HeadTargetDriver:{name}] OFF → reset headTarget to base");
            }
            if (headConstraint)
            {
                // 즉시 내릴 수도 있고, 아래 weight 스무딩 루프로 자연 감쇠시켜도 됨
                // headConstraint.weight = idleConstraintWeight;
            }
            if (clearBaseOnEnd)
            {
                baseCaptured = false; // 다음 ON에서 새 기준 캡처
                if (debugLogs) Debug.Log($"[HeadTargetDriver:{name}] OFF → clear baseCaptured");
            }
        }

        // ★ ON 엣지: 악수 시작 순간 → 기준 재캡처
        if (onEdge && recaptureBaseOnFollowOn)
        {
            CaptureBase("FollowON");
        }

        // 기준이 없다면 지금 캡처(최초 1회 또는 OFF 이후)
        if (!baseCaptured) CaptureBase("Auto");

        // ★ constraint weight 스무딩
        if (headConstraint)
        {
            float wTarget = follow ? followConstraintWeight : idleConstraintWeight;
            headConstraint.weight = Mathf.MoveTowards(headConstraint.weight, wTarget, constraintWeightLerp * Time.deltaTime);
        }

        // 팔로우 안 할 땐 여기서 종료
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
            Debug.Log($"[HeadTargetDriver:{name}] follow=ON yaw={yaw:F1}, pitch={pitch:F1}, baseCaptured={baseCaptured}, w={(headConstraint ? headConstraint.weight : -1f):F2}");

        prevFollow = true;
    }

    void CaptureBase(string reason)
    {
        if (!headPivot) return;
        baseRotation = headPivot.rotation; // 현재 머리의 ‘월드 회전’을 기준으로 저장
        baseCaptured = true;
        if (debugLogs) Debug.Log($"[HeadTargetDriver:{name}] Base captured ({reason}): {baseRotation.eulerAngles}");
    }

    void OnDrawGizmosSelected()
    {
        if (!debugGizmos || !headPivot) return;

        Gizmos.color = Color.cyan;   // 현재 머리 forward
        Gizmos.DrawLine(headPivot.position, headPivot.position + headPivot.forward * 0.5f);

        if (baseCaptured)
        {
            Gizmos.color = Color.yellow; // 기준 forward
            Vector3 f = baseRotation * Vector3.forward;
            Gizmos.DrawLine(headPivot.position, headPivot.position + f * 0.5f);
        }

        if (playerHead)
        {
            Gizmos.color = Color.green;  // 머리→플레이어 벡터
            Gizmos.DrawLine(headPivot.position, playerHead.position);
        }
    }
}
