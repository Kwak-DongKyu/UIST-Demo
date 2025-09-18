// HandRetarget_RotationOnly_AutoCalib.cs
// - Mixamo wrist(=mixamorig:RightHand): 절대 변경하지 않음 (기준 고정)
// - XR wrist 기준 상대 "회전"만 Mixamo 본에 적용 (포지션 X)
// - 시작 시/핫키(C)로 자동 캘리브레이션: 각 관절의 오프셋을 계산해 초기 꺾임 제거
using System.Collections.Generic;
using UnityEngine;

public class HandRetarget_RotationOnly_AutoCalib : MonoBehaviour
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
    public int maxDepthPerFinger = 4;   // Prox/Inter/Dist/(Tip) 최대 4
    public bool autoCalibrateOnStart = true;
    public KeyCode recalibrateKey = KeyCode.C;

    // XR wrist → Mixamo wrist 회전 매핑(공통)
    Quaternion _wristOffset;

    // 본별 오프셋(초기 꺾임 제거): Mixamo wrist-로컬 공간에서 정의
    readonly Dictionary<Transform, Quaternion> _boneOffset = new();

    void Start()
    {
        if (!xrWrist || !mixamoWrist)
        {
            Debug.LogError("[Retarget] xrWrist / mixamoWrist가 비었습니다.");
            enabled = false;
            return;
        }

        // Mixamo wrist는 '기준'으로 고정.
        // XR wrist의 축을 Mixamo wrist 축으로 보정하는 공통 오프셋
        _wristOffset = Quaternion.Inverse(xrWrist.rotation * SpaceRotOffset) * mixamoWrist.rotation;

        if (autoCalibrateOnStart) CalibrateNow();
    }

    void Update()
    {
        if (Input.GetKeyDown(recalibrateKey))
            CalibrateNow();
    }

    void LateUpdate()
    {
        // 손가락 체인별로 회전만 적용
        RetargetFinger(xrIndexRoot, mixIndexRoot, xrHasMetacarpal_Index);
        RetargetFinger(xrMiddleRoot, mixMiddleRoot, xrHasMetacarpal_Middle);
        RetargetFinger(xrRingRoot, mixRingRoot, xrHasMetacarpal_Ring);
        RetargetFinger(xrPinkyRoot, mixPinkyRoot, xrHasMetacarpal_Pinky);
        RetargetFinger(xrThumbRoot, mixThumbRoot, xrHasMetacarpal_Thumb);
    }

    // ===== 캘리브레이션 =====
    public void CalibrateNow()
    {
        _boneOffset.Clear();

        CalibFinger(xrIndexRoot, mixIndexRoot, xrHasMetacarpal_Index);
        CalibFinger(xrMiddleRoot, mixMiddleRoot, xrHasMetacarpal_Middle);
        CalibFinger(xrRingRoot, mixRingRoot, xrHasMetacarpal_Ring);
        CalibFinger(xrPinkyRoot, mixPinkyRoot, xrHasMetacarpal_Pinky);
        CalibFinger(xrThumbRoot, mixThumbRoot, xrHasMetacarpal_Thumb);

        // 팁: C키로 재캘리브레이트할 때는 손을 "편안한 기본 자세(오픈 핸드)"로 유지하세요.
        Debug.Log("[Retarget] Calibration complete.");
    }

    void CalibFinger(Transform xrRoot, Transform mixRoot, bool xrHasMetacarpal)
    {
        if (!xrRoot || !mixRoot) return;

        var xrChain = CollectChain(xrRoot, maxDepthPerFinger);
        var mxChain = CollectChain(mixRoot, maxDepthPerFinger);
        if (xrHasMetacarpal && xrChain.Count > 1) xrChain.RemoveAt(0);

        int n = Mathf.Min(xrChain.Count, mxChain.Count);
        for (int i = 0; i < n; i++)
        {
            var xr = xrChain[i];
            var mx = mxChain[i];

            // XR wrist 기준 상대 회전(현재 포즈)
            Quaternion xrRel0 = Quaternion.Inverse(xrWrist.rotation) * xr.rotation;

            // Mixamo wrist 기준 상대 회전(현재 포즈)
            Quaternion mxWorld0 = (mx.parent != null) ? mx.parent.rotation * mx.localRotation : mx.localRotation;
            Quaternion mxRel0 = Quaternion.Inverse(mixamoWrist.rotation) * mxWorld0;

            // 목표: mxRel ≈ boneOffset * (_wristOffset * xrRel)
            // => boneOffset = mxRel0 * inverse(_wristOffset * xrRel0)
            Quaternion boneOffset = mxRel0 * Quaternion.Inverse(_wristOffset * xrRel0);

            _boneOffset[mx] = boneOffset;
        }
    }

    // ===== 적용 =====
    void RetargetFinger(Transform xrRoot, Transform mixRoot, bool xrHasMetacarpal)
    {
        if (!xrRoot || !mixRoot) return;

        var xrChain = CollectChain(xrRoot, maxDepthPerFinger);
        var mxChain = CollectChain(mixRoot, maxDepthPerFinger);
        if (xrHasMetacarpal && xrChain.Count > 1) xrChain.RemoveAt(0);

        int n = Mathf.Min(xrChain.Count, mxChain.Count);
        for (int i = 0; i < n; i++)
        {
            var xr = xrChain[i];
            var mx = mxChain[i];
            ApplyRotation(xr, mx);
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

    // ===== 체인 탐색 유틸 =====
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
        if (parent.childCount == 0) return null;
        if (parent.childCount == 1) return parent.GetChild(0);

        // 이름 힌트로 주 체인 선택
        string[] prefer = { "Thumb", "Index", "Middle", "Ring", "Pinky", "Little",
                            "Prox", "Inter", "Dist", "Tip", "1", "2", "3", "4" };
        foreach (var key in prefer)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name.Contains(key)) return c;
            }
        }
        return parent.GetChild(0);
    }
}
