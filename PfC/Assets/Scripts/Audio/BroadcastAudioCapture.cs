 using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Captures audio from the broadcast mixer group for NDI/OBS output.
/// Attach this to a GameObject with an AudioSource that has the broadcast mixer as output.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class BroadcastAudioCapture : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("This AudioSource will capture audio from the broadcast mixer")]
    public AudioSource captureSource;

    [Tooltip("Output the captured audio data here (for NDI sender, OBS, etc.)")]
    public AudioClip outputClip;

    [Header("Status")]
    [SerializeField] private bool isCapturing = false;
    [SerializeField] private float currentRMS = 0f;

    // Audio buffer for captured data
    private float[] capturedData;
    private int capturedChannels;
    private int capturedSampleRate;

    // Delegates for external audio consumers
    public delegate void AudioDataAvailable(float[] data, int channels, int sampleRate);
    public event AudioDataAvailable OnAudioDataCaptured;

    void Start()
    {
        if (captureSource == null)
        {
            captureSource = GetComponent<AudioSource>();
        }

        // Setup the capture source
        captureSource.loop = true;
        captureSource.playOnAwake = false;

        // Create a silent clip to drive the audio system
        outputClip = AudioClip.Create("BroadcastCapture", AudioSettings.outputSampleRate, 2, AudioSettings.outputSampleRate, false);
        captureSource.clip = outputClip;
        captureSource.Play();

        isCapturing = true;
        Debug.Log($"[BroadcastAudioCapture] Started capturing from broadcast mixer");
    }

    /// <summary>
    /// Unity's audio callback - this is where we capture the mixed audio
    /// </summary>
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!isCapturing) return;

        // Store the captured data
        capturedData = data;
        capturedChannels = channels;
        capturedSampleRate = AudioSettings.outputSampleRate;

        // Calculate RMS for monitoring
        currentRMS = CalculateRMS(data);

        // Notify any listeners (NDI sender, OBS, etc.)
        OnAudioDataCaptured?.Invoke(data, channels, capturedSampleRate);
    }

    private float CalculateRMS(float[] audioData)
    {
        float sum = 0f;
        for (int i = 0; i < audioData.Length; i++)
        {
            sum += audioData[i] * audioData[i];
        }
        return Mathf.Sqrt(sum / audioData.Length);
    }

    /// <summary>
    /// Get the most recent captured audio data
    /// </summary>
    public void GetCapturedAudio(out float[] data, out int channels, out int sampleRate)
    {
        data = capturedData;
        channels = capturedChannels;
        sampleRate = capturedSampleRate;
    }

    /// <summary>
    /// Check if audio is currently being captured
    /// </summary>
    public bool IsCapturing => isCapturing && currentRMS > 0.0001f;

    void OnDestroy()
    {
        isCapturing = false;
        if (captureSource != null && captureSource.isPlaying)
        {
            captureSource.Stop();
        }
    }

    #region Debug

    void OnGUI()
    {
        if (Application.isPlaying)
        {
            GUI.Label(new Rect(10, 200, 300, 20), $"Broadcast Audio RMS: {currentRMS:F4}");
            GUI.Label(new Rect(10, 220, 300, 20), $"Capturing: {isCapturing}");
        }
    }

    #endregion
}