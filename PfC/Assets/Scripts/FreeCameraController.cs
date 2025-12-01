using UnityEngine;
using UnityEngine.InputSystem;

public class FreeCameraController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float sprintMultiplier = 2f;
    public float smoothness = 10f;

    [Header("Look Settings")]
    public float mouseSensitivity = 2f;
    public bool invertY = false;

    private Vector3 targetPosition;
    private float rotationX = 0f;
    private float rotationY = 0f;
    private bool cursorLocked = true;

    void Start()
    {
        targetPosition = transform.position;
        
        // Get initial rotation
        Vector3 rotation = transform.eulerAngles;
        rotationX = rotation.x;
        rotationY = rotation.y;

        // Lock cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // Toggle cursor lock with Escape
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            cursorLocked = !cursorLocked;
            Cursor.lockState = cursorLocked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !cursorLocked;
        }

        // Mouse look (only when cursor is locked)
        if (cursorLocked && Mouse.current != null)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            float mouseX = mouseDelta.x * mouseSensitivity * 0.02f;
            float mouseY = mouseDelta.y * mouseSensitivity * 0.02f;

            rotationY += mouseX;
            rotationX += invertY ? mouseY : -mouseY;
            rotationX = Mathf.Clamp(rotationX, -90f, 90f);

            transform.rotation = Quaternion.Euler(rotationX, rotationY, 0f);
        }

        // WASD movement
        float currentSpeed = moveSpeed;
        if (Keyboard.current.leftShiftKey.isPressed)
        {
            currentSpeed *= sprintMultiplier;
        }

        Vector3 moveDirection = Vector3.zero;

        if (Keyboard.current.wKey.isPressed) moveDirection += transform.forward;
        if (Keyboard.current.sKey.isPressed) moveDirection -= transform.forward;
        if (Keyboard.current.aKey.isPressed) moveDirection -= transform.right;
        if (Keyboard.current.dKey.isPressed) moveDirection += transform.right;
        if (Keyboard.current.eKey.isPressed) moveDirection += transform.up;      // Up
        if (Keyboard.current.qKey.isPressed) moveDirection -= transform.up;      // Down

        moveDirection.Normalize();
        targetPosition += moveDirection * currentSpeed * Time.deltaTime;

        // Smooth movement
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * smoothness);
    }
}
