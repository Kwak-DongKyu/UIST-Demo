using UnityEngine;

[System.Serializable]
public struct AnimMode
{
    [Tooltip("Grabbed GameObject�� Unity Tag")]
    public string tag;

    [Header("Animator Controls")]
    public string playTrigger;        // ��: "Play_TypeA"
    public string idleTrigger;        // ��: "Idle"
    public string idleStateName;      // ��: "Idle"

    [Header("Optional Override")]
    public AnimatorOverrideController overrideController; // ���� �ٸ� Ŭ�� ��Ʈ ����
}
