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

    // �� �߰�: �� �ִϸ��̼� ��忡�� ����� ��ƽ ������
    public HapticProfile hapticProfile;
}
