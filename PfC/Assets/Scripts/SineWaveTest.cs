using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class SineWaveTest : MonoBehaviour
{
    public float frequency = 440f; // A4 note
    public float volume = 0.1f;

    private float phase = 0f;
    private float sampleRate;

    void Start()
    {
        sampleRate = AudioSettings.outputSampleRate;

        AudioSource audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = true;
        audioSource.loop = true;
        audioSource.spatialBlend = 0f; // 2D sound
        audioSource.Play();

        Debug.Log($"[SineWaveTest] Playing {frequency}Hz sine wave at {sampleRate}Hz sample rate");
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        float increment = frequency * 2f * Mathf.PI / sampleRate;

        for (int i = 0; i < data.Length; i += channels)
        {
            float sample = Mathf.Sin(phase) * volume;
            phase += increment;

            // Wrap phase to avoid floating point drift
            if (phase > 2f * Mathf.PI)
                phase -= 2f * Mathf.PI;

            // Write same sample to all channels (mono→stereo)
            for (int c = 0; c < channels; c++)
            {
                data[i + c] = sample;
            }
        }
    }
}
