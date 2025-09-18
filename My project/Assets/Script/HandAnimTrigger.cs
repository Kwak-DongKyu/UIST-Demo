using UnityEngine;

public class HandAnimTrigger : MonoBehaviour
{
    public Animator animator;
    public string triggerName = "Play"; // Animator�� ���� Trigger �̸�

    void Reset()
    {
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            animator.ResetTrigger(triggerName); // �ߺ� ����(����)
            animator.SetTrigger(triggerName);
            Debug.Log("[HandAnimTrigger] A pressed �� Trigger fired");
        }
    }
}
