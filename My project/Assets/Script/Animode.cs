using UnityEngine;

public enum HapticProfile { Weak, Middle, Strong }

[System.Serializable]
public struct AnimMode
{
    public string tag;

    // Animator Controls
    public string playTrigger;
    public string idleTrigger;
    public string idleStateName;

    // Optional Override
    public AnimatorOverrideController overrideController;

    // ★ 추가: 이 애니메이션 모드에서 사용할 햅틱 프로필
    public HapticProfile hapticProfile;
}
