using UnityEngine;

public class ProximityDoor : MonoBehaviour
{
    [Header("Door Movement")]
    public float openAngle = 90f;
    public float openSpeed = 3f;

    [Header("Player Target")]
    [Tooltip("Drag your Player Capsule, Character Controller, or Camera here from the Hierarchy.")]
    public Transform playerTarget;
    public float openDistance = 2.5f;

    private bool isOpen = false;
    private Quaternion closedRotation;
    private Quaternion openRotation;

    void Start()
    {
        closedRotation = transform.localRotation;
        openRotation = closedRotation * Quaternion.Euler(0, openAngle, 0);
    }

    void Update()
    {
        // Fallback: If no target is assigned in Inspector, try finding MainCamera
        if (playerTarget == null)
        {
            if (Camera.main != null) playerTarget = Camera.main.transform;
            return;
        }

        // Check distance to the slotted player object
        float distance = Vector3.Distance(transform.position, playerTarget.position);
        isOpen = distance <= openDistance;

        // Smooth swing
        Quaternion targetRotation = isOpen ? openRotation : closedRotation;
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, Time.deltaTime * openSpeed);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, openDistance);
    }
}