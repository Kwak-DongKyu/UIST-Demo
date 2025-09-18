using UnityEngine;

[System.Serializable]
public struct AnimMode
{
    [Tooltip("Grabbed GameObject의 Unity Tag")]
    public string tag;

    [Header("Animator Controls")]
    public string playTrigger;        // 예: "Play_TypeA"
    public string idleTrigger;        // 예: "Idle"
    public string idleStateName;      // 예: "Idle"

    [Header("Optional Override")]
    public AnimatorOverrideController overrideController; // 서로 다른 클립 세트 사용시
}
