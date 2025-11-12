using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class CameraController : MonoBehaviour
{
    [Header("Camera Reference")]
    public Camera targetCamera;

    [Header("Movement Speed Settings")]
    public float positionSpeed = 0.1f;
    public float rotationSpeed = 1f;
    public float fovSpeed = 1f;

    [Header("UI References - Buttons")]
    public Button posXPlus;
    public Button posXMinus;
    public Button posYPlus;
    public Button posYMinus;
    public Button posZPlus;
    public Button posZMinus;

    public Button rotXPlus;
    public Button rotXMinus;
    public Button rotYPlus;
    public Button rotYMinus;
    public Button rotZPlus;
    public Button rotZMinus;

    public Button fovPlus;
    public Button fovMinus;

    [Header("UI References - Display Text")]
    public TextMeshProUGUI positionText;
    public TextMeshProUGUI rotationText;
    public TextMeshProUGUI fovText;

    // Track which buttons are currently pressed
    private Vector3 positionDelta = Vector3.zero;
    private Vector3 rotationDelta = Vector3.zero;
    private float fovDelta = 0f;

    private void Start()
    {
        SetupButtons();
        UpdateDisplay();
    }

    private void Update()
    {
        // Apply continuous adjustments while buttons are held
        if (targetCamera != null)
        {
            if (positionDelta != Vector3.zero)
            {
                targetCamera.transform.position += positionDelta * Time.deltaTime;
            }

            if (rotationDelta != Vector3.zero)
            {
                targetCamera.transform.Rotate(rotationDelta * Time.deltaTime);
            }

            if (fovDelta != 0f)
            {
                targetCamera.fieldOfView = Mathf.Clamp(targetCamera.fieldOfView + fovDelta * Time.deltaTime, 1f, 179f);
            }
        }

        UpdateDisplay();
    }

    private void SetupButtons()
    {
        // Position buttons
        SetupButton(posXPlus, Vector3.right * positionSpeed, ButtonType.Position);
        SetupButton(posXMinus, Vector3.left * positionSpeed, ButtonType.Position);
        SetupButton(posYPlus, Vector3.up * positionSpeed, ButtonType.Position);
        SetupButton(posYMinus, Vector3.down * positionSpeed, ButtonType.Position);
        SetupButton(posZPlus, Vector3.forward * positionSpeed, ButtonType.Position);
        SetupButton(posZMinus, Vector3.back * positionSpeed, ButtonType.Position);

        // Rotation buttons
        SetupButton(rotXPlus, Vector3.right * rotationSpeed, ButtonType.Rotation);
        SetupButton(rotXMinus, Vector3.left * rotationSpeed, ButtonType.Rotation);
        SetupButton(rotYPlus, Vector3.up * rotationSpeed, ButtonType.Rotation);
        SetupButton(rotYMinus, Vector3.down * rotationSpeed, ButtonType.Rotation);
        SetupButton(rotZPlus, Vector3.forward * rotationSpeed, ButtonType.Rotation);
        SetupButton(rotZMinus, Vector3.back * rotationSpeed, ButtonType.Rotation);

        // FOV buttons
        SetupButton(fovPlus, Vector3.forward * fovSpeed, ButtonType.FOV);
        SetupButton(fovMinus, Vector3.back * fovSpeed, ButtonType.FOV);
    }

    private enum ButtonType { Position, Rotation, FOV }

    private void SetupButton(Button button, Vector3 delta, ButtonType type)
    {
        if (button == null) return;

        EventTrigger trigger = button.gameObject.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = button.gameObject.AddComponent<EventTrigger>();
        }

        // Pointer Down - Start continuous adjustment
        EventTrigger.Entry pointerDown = new EventTrigger.Entry();
        pointerDown.eventID = EventTriggerType.PointerDown;
        pointerDown.callback.AddListener((data) => { OnButtonPressed(delta, type); });
        trigger.triggers.Add(pointerDown);

        // Pointer Up - Stop continuous adjustment
        EventTrigger.Entry pointerUp = new EventTrigger.Entry();
        pointerUp.eventID = EventTriggerType.PointerUp;
        pointerUp.callback.AddListener((data) => { OnButtonReleased(type); });
        trigger.triggers.Add(pointerUp);

        // Pointer Exit - Stop if pointer leaves button while pressed
        EventTrigger.Entry pointerExit = new EventTrigger.Entry();
        pointerExit.eventID = EventTriggerType.PointerExit;
        pointerExit.callback.AddListener((data) => { OnButtonReleased(type); });
        trigger.triggers.Add(pointerExit);
    }

    private void OnButtonPressed(Vector3 delta, ButtonType type)
    {
        switch (type)
        {
            case ButtonType.Position:
                positionDelta = delta;
                break;
            case ButtonType.Rotation:
                rotationDelta = delta;
                break;
            case ButtonType.FOV:
                fovDelta = delta.z; // Using z component for FOV delta
                break;
        }
    }

    private void OnButtonReleased(ButtonType type)
    {
        switch (type)
        {
            case ButtonType.Position:
                positionDelta = Vector3.zero;
                break;
            case ButtonType.Rotation:
                rotationDelta = Vector3.zero;
                break;
            case ButtonType.FOV:
                fovDelta = 0f;
                break;
        }
    }

    private void UpdateDisplay()
    {
        if (targetCamera == null) return;

        if (positionText != null)
        {
            Vector3 pos = targetCamera.transform.position;
            positionText.text = $"Position: X:{pos.x:F2} Y:{pos.y:F2} Z:{pos.z:F2}";
        }

        if (rotationText != null)
        {
            Vector3 rot = targetCamera.transform.eulerAngles;
            rotationText.text = $"Rotation: X:{rot.x:F1} Y:{rot.y:F1} Z:{rot.z:F1}";
        }

        if (fovText != null)
        {
            fovText.text = $"FOV: {targetCamera.fieldOfView:F1}";
        }
    }
}