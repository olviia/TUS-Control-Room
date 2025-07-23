using Unity.WebRTC;
using UnityEngine;

public class WebRTCAudioDebugger : MonoBehaviour
{
    private AudioStreamTrack testTrack;
    private AudioSource testAudioSource;
    
    [ContextMenu("Test 1: Create AudioStreamTrack Only")]
    public void Test1_CreateTrackOnly()
    {
        testTrack = new AudioStreamTrack();
        Debug.Log($"aabb_[🔍AudioDebug] AudioStreamTrack created. Can SetData work now?");
        
        // Try to set data immediately
        float[] dummyData = new float[1024];
        for (int i = 0; i < dummyData.Length; i++)
            dummyData[i] = Mathf.Sin(2.0f * Mathf.PI * 440.0f * i / 48000.0f); // 440Hz sine wave
        
        try
        {
            testTrack.SetData(dummyData, 2, 48000);
            Debug.Log($"aabb_[🔍AudioDebug] ✅ SetData SUCCESS without SetTrack initialization");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"aabb_[🔍AudioDebug] ❌ SetData FAILED without SetTrack: {e.Message}");
        }
    }
    
    [ContextMenu("Test 2: Initialize Audio System with SetTrack")]
    public void Test2_InitializeWithSetTrack()
    {
        if (testAudioSource == null)
        {
            var go = new GameObject("TestAudioSource");
            go.transform.SetParent(transform);
            testAudioSource = go.AddComponent<AudioSource>();
            testAudioSource.enabled = false;
            testAudioSource.volume = 0f;
        }
        
        // This mimics what happens during receiving
        testAudioSource.SetTrack(testTrack);
        testAudioSource.enabled = true;
        testAudioSource.Play();
        
        Debug.Log($"aabb_[🔍AudioDebug] Audio system initialized with SetTrack (mimicking receive)");
        
        // Immediately disable to avoid playing sound
        testAudioSource.Stop();
        testAudioSource.enabled = false;
    }
    
    [ContextMenu("Test 3: Try SetData After SetTrack Initialization")]
    public void Test3_SetDataAfterInit()
    {
        if (testTrack == null)
        {
            Debug.LogError("aabb_[🔍AudioDebug] No track created. Run Test 1 first.");
            return;
        }
        
        float[] dummyData = new float[1024];
        for (int i = 0; i < dummyData.Length; i++)
            dummyData[i] = Mathf.Sin(2.0f * Mathf.PI * 440.0f * i / 48000.0f);
        
        try
        {
            testTrack.SetData(dummyData, 2, 48000);
            Debug.Log($"aabb_[🔍AudioDebug] ✅ SetData SUCCESS after SetTrack initialization");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"aabb_[🔍AudioDebug] ❌ SetData FAILED after SetTrack: {e.Message}");
        }
    }
    
    void OnDestroy()
    {
        testTrack?.Dispose();
    }
}