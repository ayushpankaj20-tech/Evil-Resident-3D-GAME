using UnityEngine;
using UnityEngine.InputSystem;

// Over-the-shoulder follow camera. Mouse orbits; it pulls in when something is between it and the player.
public class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;
    public float pivotHeight = 2.0f;
    public float shoulderOffset = 0.95f;
    public float distance = 4.6f;
    public float aimDistance = 2.4f;
    public float normalFov = 58f;
    public float aimFov = 44f;
    public float sensitivity = 0.11f;
    public float minPitch = -30f;
    public float maxPitch = 55f;
    public float collisionRadius = 0.25f;
    public LayerMask collisionMask = ~0;

    public bool Aiming { get; set; }
    public float Yaw { get; private set; }

    float pitch = 8f, currentDistance;
    Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();
        if (target != null) Yaw = target.eulerAngles.y;
        currentDistance = distance;
    }

    public void Snap(float yaw) { Yaw = yaw; pitch = 8f; }

    void LateUpdate()
    {
        if (target == null || (GameManager.I != null && GameManager.I.InMenu)) return;   // the menu flies the camera itself
        var mouse = Mouse.current;
        bool frozen = GameManager.I != null && (GameManager.I.Ended || GameManager.I.Transitioning);
        if (mouse != null && !frozen && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 d = mouse.delta.ReadValue();
            float s = sensitivity * (Aiming ? 0.6f : 1f);
            Yaw += d.x * s;
            pitch = Mathf.Clamp(pitch - d.y * s, minPitch, maxPitch);
        }

        var rot = Quaternion.Euler(pitch, Yaw, 0f);
        Vector3 pivot = target.position + Vector3.up * pivotHeight;
        float wanted = Aiming ? aimDistance : distance;
        Vector3 offset = rot * new Vector3(shoulderOffset, 0f, -wanted);
        float len = offset.magnitude;
        if (Physics.SphereCast(pivot, collisionRadius, offset / len, out RaycastHit hit, len, collisionMask, QueryTriggerInteraction.Ignore))
            wanted *= Mathf.Clamp01((hit.distance - 0.05f) / len);
        currentDistance = Mathf.Lerp(currentDistance, wanted, 1f - Mathf.Exp(-14f * Time.deltaTime));
        if (currentDistance > wanted) currentDistance = wanted;

        float shoulder = shoulderOffset * Mathf.Clamp01(currentDistance / aimDistance);
        transform.position = pivot + rot * new Vector3(shoulder, 0f, -currentDistance);
        transform.rotation = rot;
        if (cam != null) cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, Aiming ? aimFov : normalFov, 1f - Mathf.Exp(-10f * Time.deltaTime));
    }
}
