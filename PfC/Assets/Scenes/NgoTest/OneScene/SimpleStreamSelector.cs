using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class SimpleStreamSelector : MonoBehaviour
{
    // References to all streamers in the scene
    private SimpleVideoStreamer[] streamers;

    // Reference to the display renderer
    public MeshRenderer displayRenderer;

    // UI elements
    public Button[] streamButtons;

    void Start()
    {
        // Add safety check for displayRenderer
        if (displayRenderer == null)
        {
            Debug.LogError("Display Renderer not assigned in SimpleStreamSelector!");
            return; // Exit the method to prevent further errors
        }

        // Find all streamers with safety check
        streamers = FindObjectsOfType<SimpleVideoStreamer>();
        Debug.Log($"Found {streamers.Length} SimpleVideoStreamer objects in the scene");

        if (streamers.Length == 0)
        {
            Debug.LogWarning("No SimpleVideoStreamer objects found in the scene!");
        }

        // Check streamButtons array
        if (streamButtons == null || streamButtons.Length == 0)
        {
            Debug.LogError("No stream buttons assigned in SimpleStreamSelector!");
            return;
        }

        // Set up buttons with safety checks
        for (int i = 0; i < streamButtons.Length && i < streamers.Length; i++)
        {
            if (streamButtons[i] == null)
            {
                Debug.LogError($"Stream button at index {i} is null!");
                continue; // Skip this iteration
            }

            int streamIndex = i; // Local copy for closure

            // Check for Text component
            Text buttonText = streamButtons[i].GetComponentInChildren<Text>();
            if (buttonText != null)
            {
                buttonText.text = "Stream " + streamers[i].streamId;
            }

            // Set up the button click handler
            streamButtons[i].onClick.AddListener(() => SelectStream(streamIndex));
            Debug.Log($"Set up button for Stream {streamers[i].streamId}");
        }
    }

    // Select which stream to display
    void SelectStream(int streamIndex)
    {
        if (streamIndex >= 0 && streamIndex < streamers.Length)
        {
            // Safety check
            if (streamers[streamIndex] == null ||
                streamers[streamIndex].displayRenderer == null ||
                streamers[streamIndex].displayRenderer.material == null)
            {
                Debug.LogError($"Invalid streamer or material at index {streamIndex}");
                return;
            }

            // Get the texture from the selected streamer
            Texture mainTex = streamers[streamIndex].displayRenderer.material.mainTexture;

            // Set it on our display
            displayRenderer.material.mainTexture = mainTex;

            Debug.Log("Selected stream: " + streamers[streamIndex].streamId);
        }
    }
}