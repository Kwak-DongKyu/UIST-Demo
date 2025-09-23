using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HandRetargeter (Rotation-only, auto-calib, safe restore)
/// - Mixamo wrist(=mixamorig:RightHand)는 '기준'으로 고정 (위치/회전 변경 X)
/// - XR wrist를 기준으로 한 "상대 회전"만 각 Mixamo 손가락 본에 적용
/// - SessionBegin에서 캘리브레이션 + 초기 localRotation 스냅샷 저장
/// - SessionEndReset에서 Animator 프레임을 넘겨가며 2회 복구 후 비활성화
/// </summary>
public class HandRetargeter : MonoBehaviour
{
    [Header("Wrist (기준 프레임)")]
    public Transform xrWrist;           // XRHand_Wrist
    public Transform mixamoWrist;       // mixamorig:RightHand

    [Header("손가락 루트 (루트 쌍만 연결하면 나머지는 자동)")]
    public Transform xrIndexRoot; public Transform mixIndexRoot;
    public Transform xrMiddleRoot; public Transform mixMiddleRoot;
    public Transform xrRingRoot; public Transform mixRingRoot;
    public Transform xrPinkyRoot; public Transform mixPinkyRoot;
    public Transform xrThumbRoot; public Transform mixThumbRoot;

    [Header("XR에 Metacarpal 단계가 있고 Mixamo는 1(=Proximal)부터인 경우 체크")]
    public bool xrHasMetacarpal_Index = true;
    public bool xrHasMetacarpal_Middle = true;
    public bool xrHasMetacarpal_Ring = true;
    public bool xrHasMetacarpal_Pinky = true;
    public bool xrHasMetacarpal_Thumb = false;

    [Header("좌표계 공통 보정 (필요시 90/180 등)")]
    public Vector3 spaceEulerOffset = Vector3.zero; // 예: (0,90,0)
    Quaternion SpaceRotOffset => Quaternion.Euler(spaceEulerOffset);

    [Header("탐색/동작 옵션")]
    [Range(1, 5)] public int maxDepthPerFinger = 4; // Prox/Inter/Dist/(Tip)
    public bool autoCalibrateOnStart = false;       // Orchestrator가 세션단위로 관리하므로 기본 off
    public KeyCode recalibrateKey = KeyCode.C;      // 수동 테스팅용

    // ───────── 내부 상태 ─────────
    Quaternion _wristOffset; // XR wrist → Mixamo wrist 공통 회전 매핑
    readonly Dictionary<Transform, Quaternion> _boneOffset = new(); // 본별 오프셋(초기 꺾임 제거)
    readonly Dictionary<Transform, Quaternion> _initialLocalRot = new(); // 세션 시작시 snapshot
    readonly List<Transform> _allMixBones = new(); // 복구 대상 전체 Mixamo 본 목록
    bool _sessionActive = false; // 세션 중일 때만 LateUpdate에서 회전 적용

    // ───────── Unity lifecycle ─────────
    void Start()
    {
        if (!xrWrist || !mixamoWrist)
        {
            Debug.LogError("[Retarget] xrWrist / mixamoWrist가 비었습니다.");
            enabled = false;
            return;
        }
        // 공통 손목 오프셋(월드 회전 기준)
        _wristOffset = Quaternion.Inverse(xrWrist.rotation * SpaceRotOffset) * mixamoWrist.rotation;

        if (autoCalibrateOnStart) CalibrateNow(); // 디버그용
    }

    void Update()
    {
        // 디버그용 재캘리브레이트
        if (Input.GetKeyDown(recalibrateKey))
            CalibrateNow();
    }

    void LateUpdate()
    {
        if (!_sessionActive) return;

        RetargetFinger(xrIndexRoot, mixIndexRoot, xrHasMetacarpal_Index);
        RetargetFinger(xrMiddleRoot, mixMiddleRoot, xrHasMetacarpal_Middle);
        RetargetFinger(xrRingRoot, mixRingRoot, xrHasMetacarpal_Ring);
        RetargetFinger(xrPinkyRoot, mixPinkyRoot, xrHasMetacarpal_Pinky);
        RetargetFinger(xrThumbRoot, mixThumbRoot, xrHasMetacarpal_Thumb);
    }

    // ───────── 외부 제어(오케스트레이터에서 호출) ─────────
    public void SessionBegin(bool doCalib = true)
    {
        enabled = true;              // 컴포넌트 활성
        if (doCalib) CalibrateNow(); // 손가락 꺾임 오프셋 계산
        SnapshotInitialPose();       // 시작 당시 localRotation 저장
        _sessionActive = true;       // 이제부터 LateUpdate에서 회전 적용
    }

    public void SessionEndReset()
    {
        // Animator와 경합 방지를 위해 프레임 경계에서 순차 복구
        StartCoroutine(GracefulRestoreAndDisable());
    }

    IEnumerator GracefulRestoreAndDisable()
    {
        // 1) 덮어쓰기 중단 (여전히 enabled=true 유지)
        _sessionActive = false;

        // 2) 현재 프레임의 Animator 업데이트가 끝나도록 대기
        yield return new WaitForEndOfFrame();

        // 3) 강제 복구 1회
        ApplySnapshotPoseOnce();

        // 4) 다음 프레임에도 혹시 상쇄될 수 있어 한 번 더 적용
        yield return null;
        ApplySnapshotPoseOnce();

        // 5) 이제 안전하게 끈다
        enabled = false;
    }

    // ───────── 캘리브레이션 ─────────
    public void CalibrateNow()
    {
        _boneOffset.Clear();

        CalibFinger(xrIndexRoot, mixIndexRoot, xrHasMetacarpal_Index);
        CalibFinger(xrMiddleRoot, mixMiddleRoot, xrHasMetacarpal_Middle);
        CalibFinger(xrRingRoot, mixRingRoot, xrHasMetacarpal_Ring);
        CalibFinger(xrPinkyRoot, mixPinkyRoot, xrHasMetacarpal_Pinky);
        CalibFinger(xrThumbRoot, mixThumbRoot, xrHasMetacarpal_Thumb);

        Debug.Log("[Retarget] Calibration complete.");
    }

    void CalibFinger(Transform xrRoot, Transform mixRoot, bool xrHasMetacarpal)
    {
        if (!xrRoot || !mixRoot) return;

        var xrChain = CollectChain(xrRoot, maxDepthPerFinger);
        var mxChain = CollectChain(mixRoot, maxDepthPerFinger);
        if (xrHasMetacarpal && xrChain.Count > 1) xrChain.RemoveAt(0); // XR의 metacarpal 스킵

        int n = Mathf.Min(xrChain.Count, mxChain.Count);
        for (int i = 0; i < n; i++)
        {
            var xr = xrChain[i];
            var mx = mxChain[i];

            // 현재 프레임의 상대 회전 계산 (월드 기준)
            Quaternion xrRel0 = Quaternion.Inverse(xrWrist.rotation) * xr.rotation;

            Quaternion mxWorld0 = (mx.parent != null)
                ? mx.parent.rotation * mx.localRotation
                : mx.localRotation;
            Quaternion mxRel0 = Quaternion.Inverse(mixamoWrist.rotation) * mxWorld0;

            // 목표: mxRel ≈ boneOffset * (_wristOffset * xrRel)
            // => boneOffset = mxRel0 * inverse(_wristOffset * xrRel0)
            Quaternion boneOffset = mxRel0 * Quaternion.Inverse(_wristOffset * xrRel0);
            _boneOffset[mx] = boneOffset;
        }
    }

    // ───────── 적용 ─────────
    void RetargetFinger(Transform xrRoot, Transform mixRoot, bool xrHasMetacarpal)
    {
        if (!xrRoot || !mixRoot) return;

        var xrChain = CollectChain(xrRoot, maxDepthPerFinger);
        var mxChain = CollectChain(mixRoot, maxDepthPerFinger);
        if (xrHasMetacarpal && xrChain.Count > 1) xrChain.RemoveAt(0);

        int n = Mathf.Min(xrChain.Count, mxChain.Count);
        for (int i = 0; i < n; i++)
        {
            ApplyRotation(xrChain[i], mxChain[i]);
        }
    }

    void ApplyRotation(Transform xrJoint, Transform mixBone)
    {
        // XR wrist 기준 상대 회전(현재 프레임)
        Quaternion xrRel = Quaternion.Inverse(xrWrist.rotation) * xrJoint.rotation;

        // 본별 오프셋(없으면 I) * 공통 손목 오프셋 * XR 상대회전
        Quaternion boneOffset = _boneOffset.TryGetValue(mixBone, out var off) ? off : Quaternion.identity;
        Quaternion mxRel = boneOffset * (_wristOffset * xrRel);

        // Mixamo wrist 기준으로 월드 회전 만든 뒤, 부모-로컬로 투영해 적용
        Quaternion worldTarget = mixamoWrist.rotation * mxRel;
        mixBone.localRotation = (mixBone.parent != null)
            ? Quaternion.Inverse(mixBone.parent.rotation) * worldTarget
            : mxRel;
    }

    // ───────── 스냅샷 & 복구 ─────────
    public void SnapshotInitialPose()
    {
        CollectAllMixBones();
        _initialLocalRot.Clear();
        foreach (var t in _allMixBones)
        {
            if (!t) continue;
            _initialLocalRot[t] = t.localRotation;
        }
    }

    void ApplySnapshotPoseOnce()
    {
        foreach (var kv in _initialLocalRot)
        {
            var t = kv.Key;
            if (!t) continue;
            t.localRotation = kv.Value;
        }
    }

    void CollectAllMixBones()
    {
        _allMixBones.Clear();
        void add(Transform root)
        {
            if (!root) return;
            var chain = CollectChain(root, maxDepthPerFinger);
            foreach (var t in chain) if (t) _allMixBones.Add(t);
        }
        add(mixIndexRoot);
        add(mixMiddleRoot);
        add(mixRingRoot);
        add(mixPinkyRoot);
        add(mixThumbRoot);
    }

    // ───────── 체인 탐색 ─────────
    List<Transform> CollectChain(Transform root, int maxDepth)
    {
        var list = new List<Transform>(maxDepth);
        var t = root;
        for (int i = 0; i < maxDepth && t != null; i++)
        {
            list.Add(t);
            t = GetPrimaryChild(t);
        }
        return list;
    }

    Transform GetPrimaryChild(Transform parent)
    {
        if (!parent) return null;
        int childCount = parent.childCount;
        if (childCount == 0) return null;
        if (childCount == 1) return parent.GetChild(0);

        // 이름 힌트로 주 체인 선택
        string[] prefer = { "Thumb", "Index", "Middle", "Ring", "Pinky", "Little",
                            "Prox", "Inter", "Dist", "Tip", "1", "2", "3", "4" };
        foreach (var key in prefer)
        {
            for (int i = 0; i < childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name.Contains(key)) return c;
            }
        }
        return parent.GetChild(0);
    }

    // ───────── 호환용(기존 API 유지) ─────────
    public void EnableAndMaybeCalibrate(bool doCalib = true)
    {
        SessionBegin(doCalib);
    }
    public void DisableNow()
    {
        SessionEndReset();
    }
}
