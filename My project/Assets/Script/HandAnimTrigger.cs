using UnityEngine;

public class HandAnimTrigger : MonoBehaviour
{
    public Animator animator;
    public string triggerName = "Play"; // Animator에 만든 Trigger 이름

    void Reset()
    {
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            animator.ResetTrigger(triggerName); // 중복 방지(선택)
            animator.SetTrigger(triggerName);
            Debug.Log("[HandAnimTrigger] A pressed → Trigger fired");
        }
    }
}
