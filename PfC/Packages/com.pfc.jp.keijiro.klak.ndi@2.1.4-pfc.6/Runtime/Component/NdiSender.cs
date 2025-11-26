using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Klak.Ndi.Audio;
#if OSC_JACK
using OscJack;
#endif
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
namespace Klak.Ndi {

[ExecuteInEditMode]
public sealed partial class NdiSender : MonoBehaviour
{
    // Verbose audio debug controls
    public bool audioDebugVerbose = false; // expose in inspector to toggle
    [Range(0.0f,1.0f)] public float initialBufferFillFraction = 0.5f; // fraction of ring capacity to collect before starting playback

    #region Sender objects

    Interop.Send _send;
    ReadbackPool _pool;
    FormatConverter _converter;
    System.Action<AsyncGPUReadbackRequest> _onReadback;

    void PrepareSenderObjects()
    {
        // Game view capture method: Borrow the shared sender instance.
        if (_send == null && captureMethod == CaptureMethod.GameView)
            _send = SharedInstance.GameViewSend;

        // Private object initialization
        if (_send == null) _send = Interop.Send.Create(ndiName, true, false);
        if (_pool == null) _pool = new ReadbackPool();
        if (_converter == null) _converter = new FormatConverter(_resources);
        if (_onReadback == null) _onReadback = OnReadback;
    }

    void ReleaseSenderObjects()
    {
        // Total synchronization: This may cause a frame hiccup, but it's
        // needed to dispose the readback buffers safely.
        AsyncGPUReadback.WaitAllRequests();

        // Game view capture method: Leave the sender instance without
        // disposing (we're not the owner) but synchronize it. It's needed to
        // dispose the readback buffers safely too.
        if (SharedInstance.IsGameViewSend(_send))
        {
            _send.SendVideoAsync(); // Sync by null-send
            _send = null;
        }

        // Private objet disposal
        _send?.Dispose();
        _send = null;

        _pool?.Dispose();
        _pool = null;

        _converter?.Dispose();
        _converter = null;

        // We don't dispose _onReadback because it's reusable.
    }

    #endregion
    
    #region Sound Sender

    private AudioListenerBridge _audioListenerBridge;
    private AudioListenerIndividualBridge _selectedIndividualBridge;
    private VivoxAudioBridge _selectedVivoxBridge;
    private int numSamples = 0;
    private int numChannels = 0;
    private float[] samples = new float[1];
    private int sampleRate = 44100;
    private AudioMode _audioMode;
    private IntPtr _metaDataPtr = IntPtr.Zero;
    private float[] _channelVisualisations;
    private object _channelVisualisationsLock = new object();
    private object _channelObjectLock = new object();
    private List<NativeArray<float>> _objectBasedChannels = new List<NativeArray<float>>();
    private List<Vector3> _objectBasedPositions = new List<Vector3>();
    private List<float> _objectBasedGains = new List<float>();

    // AudioListenerIndividualBridge registration for object-based mode (by ID)
    private static Dictionary<int, AudioListenerIndividualBridge> _registeredIndividualBridges = new Dictionary<int, AudioListenerIndividualBridge>();
    private static readonly object _individualBridgesLock = new object();

    public static void RegisterIndividualAudioBridge(AudioListenerIndividualBridge bridge)
    {
        lock (_individualBridgesLock)
        {
            int id = bridge.BridgeId;
            if (_registeredIndividualBridges.ContainsKey(id))
            {
                Debug.LogWarning($"[NdiSender] Bridge ID {id} already registered. Replacing with {bridge.gameObject.name}");
            }
            _registeredIndividualBridges[id] = bridge;
            Debug.Log($"[NdiSender] Registered individual bridge ID {id}: {bridge.gameObject.name}");
        }
    }

    public static void UnregisterIndividualAudioBridge(AudioListenerIndividualBridge bridge)
    {
        lock (_individualBridgesLock)
        {
            int id = bridge.BridgeId;
            if (_registeredIndividualBridges.Remove(id))
            {
                Debug.Log($"[NdiSender] Unregistered individual bridge ID {id}: {bridge.gameObject.name}");
            }
        }
    }

    // VivoxAudioBridge registration for Vivox mode (by ID)
    private static Dictionary<int, VivoxAudioBridge> _registeredVivoxBridges = new Dictionary<int, VivoxAudioBridge>();
    private static readonly object _vivoxBridgesLock = new object();

    public static void RegisterVivoxAudioBridge(VivoxAudioBridge bridge)
    {
        lock (_vivoxBridgesLock)
        {
            int id = bridge.BridgeId;
            if (_registeredVivoxBridges.ContainsKey(id))
            {
                Debug.LogWarning($"[NdiSender] Vivox Bridge ID {id} already registered. Replacing with {bridge.gameObject.name}");
            }
            _registeredVivoxBridges[id] = bridge;
            Debug.Log($"[NdiSender] Registered Vivox bridge ID {id}: {bridge.gameObject.name}");
        }
    }

    public static void UnregisterVivoxAudioBridge(VivoxAudioBridge bridge)
    {
        lock (_vivoxBridgesLock)
        {
            int id = bridge.BridgeId;
            if (_registeredVivoxBridges.Remove(id))
            {
                Debug.Log($"[NdiSender] Unregistered Vivox bridge ID {id}: {bridge.gameObject.name}");
            }
        }
    }

    // Ring buffer for audio (accumulates incoming frames)
    private const int AUDIO_BUFFER_LENGTH_MS = 200;
    private float[] _audioRingBuffer;
    private int _audioWriteIndex = 0;
    private int _audioReadIndex = 0;
    private int _audioAvailableSamples = 0;
    private bool _audioReadStarted = false;
    private int _audioChannels = 2;
    private int _audioSampleRate = 48000;
    private readonly object _audioBufferLock = new object();

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
            return;
        
        var listenersPositions = VirtualAudio.GetListenersPositions();
        var listenerVolumes = VirtualAudio.GetListenersVolume();
        
        if (listenersPositions == null || listenerVolumes == null)
            return;
        
        Gizmos.color = Color.yellow;
        int listIndex = 0;
        foreach (var listener in listenersPositions)
        {
            Gizmos.DrawWireSphere(listener, 1f);
            // Add text label
            UnityEditor.Handles.Label(listener + new Vector3(2f, 0, 0f), "Channel: "+listIndex+ System.Environment.NewLine + "Volume: "+listenerVolumes[listIndex]);
            listIndex++;
        }
    }
#endif

    private void CheckAudioListener(bool willBeActive)
    {
        if (!Application.isPlaying)
            return;
        
        if (willBeActive && !GetComponent<AudioListener>() && !_audioListenerBridge)
        {
            var audioListener = FindObjectOfType<AudioListener>();
            if (!audioListener)
            {
                Debug.LogError("No AudioListener found in scene. Please add an AudioListener to the scene.");
                return;
            }
            
            _audioListenerBridge = audioListener.gameObject.AddComponent<AudioListenerBridge>();
        }
        if (!willBeActive && _audioListenerBridge)
            Util.Destroy(_audioListenerBridge);
        
        if (willBeActive && _audioListenerBridge)
            AudioListenerBridge.OnAudioFilterReadEvent = OnAudioFilterRead;
    }
    
    private void ClearVirtualSpeakerListeners()
    {
        VirtualAudio.UseVirtualAudio = false;
        VirtualAudio.ActivateObjectBasedAudio(false);

        VirtualAudio.ClearAllVirtualSpeakerListeners();
    }
    
    private void CreateAudioSetup_Quad()
    {
        VirtualAudio.UseVirtualAudio = true;
        VirtualAudio.ClearAllVirtualSpeakerListeners();

        float distance = virtualListenerDistance;
        
        VirtualAudio.AddListener( new Vector3(-1, 0f, 1).normalized * distance, 1f);
        VirtualAudio.AddListener( new Vector3(1, 0f, 1).normalized * distance, 1f);
        
        VirtualAudio.AddListener( new Vector3(-1, 0f, -1).normalized * distance, 1f);
        VirtualAudio.AddListener( new Vector3(1, 0f, -1).normalized * distance, 1f);
    }    
    
    private void CreateAudioSetup_5point1()
    {
        VirtualAudio.UseVirtualAudio = true;
        VirtualAudio.ClearAllVirtualSpeakerListeners();

        float distance = virtualListenerDistance;
        VirtualAudio.AddListener( new Vector3(-1, 0f, 1).normalized * distance, 1f);
        VirtualAudio.AddListener( new Vector3(1, 0f, 1).normalized * distance, 1f);
        
        VirtualAudio.AddListener( new Vector3(0f, 0f, 1).normalized * distance, 1f);
        VirtualAudio.AddListener( new Vector3(0f, 0f, 0f), 0f);
        
        VirtualAudio.AddListener( new Vector3(-1, 0f, -1).normalized * distance, 1f);
        VirtualAudio.AddListener( new Vector3(1, 0f, -1).normalized * distance, 1f);
    }

    private void CreateAudioSetup_7point1()
    {
        VirtualAudio.UseVirtualAudio = true;
        VirtualAudio.ClearAllVirtualSpeakerListeners();

        float distance = virtualListenerDistance;
        VirtualAudio.AddListener( new Vector3(-1, 0f, 1).normalized * distance, 1f);
        VirtualAudio.AddListener( new Vector3(1, 0f, 1).normalized * distance, 1f);
        
        VirtualAudio.AddListener( new Vector3(0f, 0f, 1).normalized * distance, 1f);
        VirtualAudio.AddListener( new Vector3(0f, 0f, 0f), 0f);

        VirtualAudio.AddListener( new Vector3(-1, 0f, 0).normalized * distance, 1f);
        VirtualAudio.AddListener( new Vector3(1, 0f, 0).normalized * distance, 1f);
        
        VirtualAudio.AddListener( new Vector3(-1, 0f, -1).normalized * distance, 1f);
        VirtualAudio.AddListener( new Vector3(1, 0f, -1).normalized * distance, 1f);
    }

    private void CreateAudioSetup_32Array()
    {
        VirtualAudio.UseVirtualAudio = true;
        VirtualAudio.ClearAllVirtualSpeakerListeners();
        
        for (int i = 0; i < 32; i++)
        {
            // Add 32 virtual speakers in a circle around the listener
            float angle = i * Mathf.PI * 2 / 32;
            float x = Mathf.Sin(angle) * virtualListenerDistance;
            float z = Mathf.Cos(angle) * virtualListenerDistance;
            VirtualAudio.AddListener(new Vector3(x, 0f, z), 1f);
        }
    }

    private void CreateAudioSetup_bySpeakerConfig()
    {
        VirtualAudio.ClearAllVirtualSpeakerListeners();
        if (!customSpeakerConfig)
        {
            Debug.LogError("No custom speaker config assigned!");
            return;
        }
        VirtualAudio.UseVirtualAudio = true;

        var allSpeakers = customSpeakerConfig.GetAllSpeakers();
        for (int i = 0; i < allSpeakers.Length; i++)
        {
            VirtualAudio.AddListener(allSpeakers[i].position, allSpeakers[i].volume);
        }
    }
    
    private void Update()
    {
        if (audioOrigin)
            VirtualAudio.AudioOrigin = new Pose(audioOrigin.position, audioOrigin.rotation);
        else
            VirtualAudio.AudioOrigin = Pose.identity;
        
        if (audioMode != AudioMode.CustomVirtualAudioSetup)
            VirtualAudio.PlayCenteredAudioSourceOnAllListeners = playCenteredAudioSourcesOnAllSpeakers;
        

        // Ensure sender objects exist for audio-only usage (they were only created for video before)
        if (Application.isPlaying && _send == null && audioMode != AudioMode.None)
        {
            PrepareSenderObjects();
        }
        

        int targetFrameRate = setRenderTargetFrameRate ? frameRate.GetUnityFrameTarget() : -1;
        if (Application.targetFrameRate != targetFrameRate)
            Application.targetFrameRate = targetFrameRate;


        if (_audioMode == AudioMode.AudioListener && Application.isPlaying)
        {
            // Get accumulated audio directly from AudioListenerBridge's ring buffer
            if (_audioListenerBridge != null)
            {
                if (_audioListenerBridge.GetAccumulatedAudio(out float[] audioData, out int channels))
                {
                    _audioChannels = channels;

                    // Send to NDI
                    SendAudioListenerData(audioData, channels);
                }
            }
        }

        // Individual audio mode: Get audio from cached selected bridge
        if (_audioMode == AudioMode.Individual && Application.isPlaying)
        {
            // Try to get the bridge if not already cached (handles timing issues)
            if (_selectedIndividualBridge == null)
            {
                lock (_individualBridgesLock)
                {
                    if (_registeredIndividualBridges.TryGetValue(objectBasedBridgeId, out var bridge))
                    {
                        _selectedIndividualBridge = bridge;
                        Debug.Log($"[NdiSender] Selected individual bridge ID {objectBasedBridgeId}: {bridge.gameObject.name}");
                    }
                }
            }

            if (_selectedIndividualBridge != null)
            {
                // Get buffered audio from the selected bridge
                if (_selectedIndividualBridge.GetAccumulatedAudio(out float[] audioData, out int channels))
                {
                    // Send audio using same method as AudioListener mode
                    SendAudioListenerData(audioData, channels);
                }
            }
        }

        // Vivox audio mode: Get audio from cached selected Vivox bridge
        if (_audioMode == AudioMode.Vivox && Application.isPlaying)
        {
            // Try to get the bridge if not already cached (handles timing issues)
            if (_selectedVivoxBridge == null)
            {
                lock (_vivoxBridgesLock)
                {
                    if (_registeredVivoxBridges.TryGetValue(objectBasedBridgeId, out var bridge))
                    {
                        _selectedVivoxBridge = bridge;
                        Debug.Log($"[NdiSender] Selected Vivox bridge ID {objectBasedBridgeId}: {bridge.gameObject.name}");
                    }
                    else
                    {
                        Debug.LogWarning($"[NdiSender] No Vivox bridge found with ID {objectBasedBridgeId}");
                    }
                }
            }

            if (_selectedVivoxBridge != null)
            {
                // Get buffered audio from the selected Vivox bridge
                if (_selectedVivoxBridge.GetAccumulatedAudio(out float[] audioData, out int channels))
                {
                    // Send audio using same method as AudioListener mode
                    SendAudioListenerData(audioData, channels);
                    Debug.Log($"[NdiSender] Sending Vivox audio to NDI: {audioData.Length} samples, {channels} channels");
                }
            }
            else
            {
                Debug.LogWarning($"[NdiSender] Vivox mode active but no bridge selected");
            }
        }
    }

    private void LateUpdate()
    {
        if (_audioMode == AudioMode.None)
            return;
        if (_audioMode != AudioMode.ObjectBased && _audioMode != AudioMode.AudioListener)
            VirtualAudio.UpdateAudioSourceToListenerWeights(useAudioOriginPositionForVirtualAttenuation);
    }
    
    internal static AudioSource[] SearchForAudioSourcesWithMissingListener()
    {
        var audioSources = GameObject.FindObjectsByType<AudioSource>( FindObjectsInactive.Include, FindObjectsSortMode.None);
        var audioSourcesList = new List<AudioSource>();
        foreach (var a in audioSources)
        {
            if (a.GetComponent<AudioSourceListener>() == null)
            {
                audioSourcesList.Add(a);
            }
        }
        return audioSourcesList.ToArray();
    }

    public float[] GetChannelVisualisations()
    {
        lock (_channelVisualisationsLock)
            return _channelVisualisations;
    }
    
    internal Vector3[] GetChannelObjectPositions()
    {
        lock (_channelObjectLock)
            return _objectBasedPositions.ToArray();
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (_audioMode == AudioMode.None)
            return;

        else if (VirtualAudio.UseVirtualAudio)
        {
            SendCustomListenerData();
        }
    }

    private void SendObjectBasedChannels()
    {
        if (!VirtualAudio.UseVirtualAudio)
            return;
        
        lock (_channelObjectLock)
        {
            bool hasDataToSend = VirtualAudio.GetObjectBasedAudio(out var stream, out int samplesCount, _objectBasedChannels, _objectBasedPositions, _objectBasedGains);
            if (!hasDataToSend)
            {
                lock (_channelVisualisationsLock)
                    if (_channelVisualisations != null)
                        Array.Fill(_channelVisualisations, 0f);
                SendChannels(stream, samplesCount, _objectBasedChannels.Count);
                return;
            }

            lock (_channelVisualisationsLock)
            {
                if (_channelVisualisations == null || _channelVisualisations.Length != _objectBasedChannels.Count)
                    _channelVisualisations = new float[_objectBasedChannels.Count];

                unsafe
                {
                    for (int i = 0; i < _channelVisualisations.Length; i++)
                    {
                        if (_objectBasedChannels[i].IsCreated && _objectBasedChannels[i].Length > 0)
                        {
                            BurstMethods.GetVU((float*)_objectBasedChannels[i].GetUnsafePtr(), _objectBasedChannels[i].Length, out float vu);
                            _channelVisualisations[i] = vu;
                        }
                        else
                            _channelVisualisations[i] = 0;
                    }
                }
            }
            
            var admData = new AdmData();
            admData.positions = _objectBasedPositions;
            admData.gains = _objectBasedGains;
            lock (_admEventLock)
                _onAdmDataChanged?.Invoke(admData);
            
            SendChannels(stream, samplesCount, _objectBasedChannels.Count);
        }
    }
    
    private void SendChannels(NativeArray<float> stream, int samplesCount, int channelsCount)
    {
        unsafe
        {
            numSamples = samplesCount;
            numChannels = channelsCount;

            UpdateAudioMetaData();
            
            unsafe
            {
                if (!stream.IsCreated || stream.Length == 0)
                    return;
                
                var framev3 = new Interop.AudioFrameV3
                {
                    sample_rate = sampleRate,
                    no_channels = channelsCount,
                    no_samples = numSamples,
                    channel_stride_in_bytes = numSamples * sizeof(float),
                    p_data = (System.IntPtr)stream.GetUnsafePtr(),
                    p_metadata = _metaDataPtr,
                    FourCC = Interop.FourCC_audio_type_e.FourCC_audio_type_FLTP,
                    timecode =  long.MaxValue
                };
                
                if (_send != null && !_send.IsInvalid && !_send.IsClosed)
                { 
                    _send.SendAudioV3(framev3);
                }
            }
        }
    }

    private void SendCustomListenerData()
    {
        if (!VirtualAudio.UseVirtualAudio)
            return;
        
        var mixedAudio = VirtualAudio.GetMixedAudio(out var stream, out int samplesCount, out var tmpVus);
        lock (_channelVisualisationsLock)
        {
            if (_channelVisualisations == null || _channelVisualisations.Length != tmpVus.Length)
                _channelVisualisations = new float[tmpVus.Length];
            Array.Copy(tmpVus, _channelVisualisations, tmpVus.Length);
        }
        
        SendChannels(stream, samplesCount, mixedAudio.Count);
    }

    private void UpdateAudioMetaData()
    {
        if (_metaDataPtr != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(_metaDataPtr);
            _metaDataPtr = IntPtr.Zero;
        }
        
        var xml = _audioMode == AudioMode.ObjectBased ? 
            AudioMeta.GenerateObjectBasedConfigXmlMetaData(_objectBasedPositions, _objectBasedGains) 
            : AudioMeta.GenerateSpeakerConfigXmlMetaData();
        
        if (!string.IsNullOrEmpty(xml))
            _metaDataPtr = Marshal.StringToCoTaskMemAnsi(xml);
    }
    
    /// <summary>
    /// Public method for Vivox to send audio data directly to NDI
    /// Called from VivoxAudioProcessor after fetching audio from Vivox SDK
    /// </summary>
    public void SendVivoxAudioData(float[] data, int channels, int audioSampleRate)
    {
        if (_audioMode != AudioMode.Vivox) return;
        if (data == null || data.Length == 0 || channels == 0) return;

        // Update sample rate if provided
        if (audioSampleRate > 0 && audioSampleRate != sampleRate)
        {
            sampleRate = audioSampleRate;
        }

        // Send using the same method as AudioListener mode
        SendAudioListenerData(data, channels);
    }

    private void SendAudioListenerData(float[] data, int channels)
    {
        if (data.Length == 0 || channels == 0) return;

        unsafe
        {
            bool settingsChanged = false;
            int tempSamples = data.Length / channels;

            if (tempSamples != numSamples)
            {
                settingsChanged = true;
                numSamples = tempSamples;
                //PluginEntry.SetNumSamples(_plugin, numSamples);
            }

            if (channels != numChannels)
            {
                settingsChanged = true;
                numChannels = channels;
                //PluginEntry.SetAudioChannels(_plugin, channels);
            }

            if (settingsChanged)
            {
                System.Array.Resize<float>(ref samples, numSamples * numChannels);
            }

            var dataPtr = UnsafeUtility.PinGCArrayAndGetDataAddress(data, out var handleData);
            var samplesPtr = UnsafeUtility.PinGCArrayAndGetDataAddress(samples, out var handleSamples);

            BurstMethods.InterleavedToPlanar((float*)dataPtr, (float*)samplesPtr, numChannels, numSamples);

            UnsafeUtility.ReleaseGCObject(handleData);
            UnsafeUtility.ReleaseGCObject(handleSamples);

            fixed (float* p = samples)
            {
                var frame = new Interop.AudioFrame
                {
                    SampleRate = sampleRate,
                    NoChannels = channels,
                    NoSamples = numSamples,
                    ChannelStrideInBytes = numSamples * sizeof(float),
                    Data = (System.IntPtr)p
                };

                if (_send != null)
                {
                    if (!_send.IsClosed && !_send.IsInvalid)
                        _send.SendAudio(frame);
                }
            }

        }
    }

    private void WriteToAudioRing(float[] data)
    {
        if (_audioRingBuffer == null) return;

            int capacity = _audioRingBuffer.Length;
            for (int i = 0; i < data.Length; i++)
            {
                _audioRingBuffer[_audioWriteIndex] = data[i];
                _audioWriteIndex = (_audioWriteIndex + 1) % capacity;
            }
            _audioAvailableSamples = Math.Min(_audioAvailableSamples + data.Length, capacity);
        
    }

    private bool ReadFromAudioRing(float[] outputBuffer)
    {
        if (_audioRingBuffer == null) { if (audioDebugVerbose) Debug.Log("[NdiSender][AudioRing] Skip read: ring buffer null"); return false; }

        lock (_audioBufferLock)
        {
            int capacity = _audioRingBuffer.Length;
            int startThreshold = Mathf.Clamp((int)(capacity * initialBufferFillFraction), 0, capacity - 1);

            // Initial setup: establish delay once buffer reaches threshold
            if (!_audioReadStarted && _audioAvailableSamples >= startThreshold)
            {
                int delay = startThreshold; // use threshold instead of half capacity
                _audioReadIndex = (_audioWriteIndex - delay + capacity) % capacity;
                _audioReadStarted = true;
                Debug.Log($"[NdiSender] Audio buffering started. Delay={delay} samples ({delay/(float)(_audioSampleRate*_audioChannels)*1000:F1}ms, fraction={initialBufferFillFraction:F2})");
            }

            if (!_audioReadStarted)
            {
                if (audioDebugVerbose)
                    Debug.Log($"[NdiSender][AudioRing] Waiting for start. Have={_audioAvailableSamples} Need>={startThreshold}");
                return false;  // Still filling buffer
            }

            // Check available samples (interleaved)
            int available = (_audioWriteIndex - _audioReadIndex + capacity) % capacity;
            if (available < outputBuffer.Length)
            {
                if (audioDebugVerbose)
                    Debug.Log($"[NdiSender][AudioRing] Underrun (available {available} < requested {outputBuffer.Length})");
                return false;
            }

            // Read from ring
            for (int i = 0; i < outputBuffer.Length; i++)
            {
                outputBuffer[i] = _audioRingBuffer[_audioReadIndex];
                _audioReadIndex = (_audioReadIndex + 1) % capacity;
            }

            // Adjust available sample counter (approximate)
            _audioAvailableSamples = Mathf.Max(0, _audioAvailableSamples - outputBuffer.Length);

            if (audioDebugVerbose)
                Debug.Log($"[NdiSender][AudioRing] Read {outputBuffer.Length} samples, remaining approx {_audioAvailableSamples}, writeIdx={_audioWriteIndex}, readIdx={_audioReadIndex}");

            return true;
        }
    }



    #endregion

    #region Capture coroutine for the Texture/GameView capture methods

    System.Collections.IEnumerator CaptureCoroutine()
    {
        for (var eof = new WaitForEndOfFrame(); true;)
        {
        #if !UNITY_ANDROID || UNITY_EDITOR
            // Wait for the end of the frame.
            yield return eof;
        #else
            // Temporary workaround for glitches on Android:
            // Process the input at the beginning of the frame instead of EoF.
            // I don't know why these glitches occur, but this change solves
            // them anyway. I should investigate them further if they reappear.
            yield return null;
        #endif

            PrepareSenderObjects();

            // Texture capture method
            if (captureMethod == CaptureMethod.Texture && sourceTexture != null)
            {
                var (w, h) = (sourceTexture.width, sourceTexture.height);

                // Pixel format conversion
                var buffer = _converter.Encode(sourceTexture, keepAlpha, true);

                // Readback entry allocation and request
                _pool.NewEntry(w, h, keepAlpha, metadata)
                     .RequestReadback(buffer, _onReadback);
            }

            // Game View capture method
            if (captureMethod == CaptureMethod.GameView)
            {
                // Game View screen capture with a temporary RT
                var (w, h) = (Screen.width, Screen.height);
                var tempRT = RenderTexture.GetTemporary(w, h, 0);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(tempRT);

                // Pixel format conversion
                var buffer = _converter.Encode(tempRT, keepAlpha, false);
                RenderTexture.ReleaseTemporary(tempRT);

                // Readback entry allocation and request
                _pool.NewEntry(w, h, keepAlpha, metadata)
                     .RequestReadback(buffer, _onReadback);
            }
        }
    }

    #endregion

    #region SRP camera capture callback for the Camera capture method

    void OnCameraCapture(RenderTargetIdentifier source, CommandBuffer cb)
    {
        // A SRP may call this callback after object destruction. We can
        // exclude those cases by null-checking _attachedCamera.
        if (_attachedCamera == null) return;

        PrepareSenderObjects();

        // Pixel format conversion
        var (w, h) = (sourceCamera.pixelWidth, sourceCamera.pixelHeight);
        var buffer = _converter.Encode(cb, source, w, h, keepAlpha, true);

        // Readback entry allocation and request
        _pool.NewEntry(w, h, keepAlpha, metadata)
             .RequestReadback(buffer, _onReadback);
    }

    #endregion

    #region GPU readback completion callback

    private object _lastVideoFrameLock = new object();
    private ReadbackEntry _lastVideoFrameEntry = null;
    private List<ReadbackEntry> _finished = new List<ReadbackEntry>();
    private Task _videoSendThreadClocked;
    private CancellationToken _videoSendThreadCancelToken;
    private CancellationTokenSource _videoSendThreadCancelTokenSource;
    
    private void VideoSendThreadClocked()
    {
        bool sleep = false;
        do
        {
            if (_videoSendThreadCancelToken.IsCancellationRequested)
                return;

            if (sleep)
            {
                sleep = false;
                Thread.Sleep(1);
            }

            if (_send == null || _send.IsInvalid || _send.IsClosed)
            {
                sleep = true;
                continue;
            }

            ReadbackEntry entry;
            lock (_lastVideoFrameLock)
            {
                if (_lastVideoFrameEntry == null)
                {
                    sleep = true;
                    continue;
                }

                entry = _lastVideoFrameEntry;
                _lastVideoFrameEntry = null;
            }

            frameRate.GetND(out var frameRateN, out var frameRateD);
            
            // Frame data setup
            var frame = new Interop.VideoFrame
            {
                Width = entry.Width,
                Height = entry.Height,
                LineStride = entry.Width * 2,
                FourCC = entry.FourCC,
                FrameFormat = Interop.FrameFormat.Progressive,
                Data = entry.ImagePointer,
                _Metadata = entry.MetadataPointer,
                Timecode = long.MaxValue,
                FrameRateD = frameRateD,
                FrameRateN = frameRateN
            };

            _send.SendVideoAsync(frame);
            _send.SendVideoAsync();
            lock (_lastVideoFrameLock)
            {
                _finished.Add(entry);
            }
        } while (true);
    }
    
    unsafe void OnReadback(AsyncGPUReadbackRequest req)
    {
        // Readback entry retrieval
        var entry = _pool.FindEntry(req.GetData<byte>());
        if (entry == null) return;

        // Invalid state detection
        if (req.hasError || _send == null || _send.IsInvalid || _send.IsClosed)
        {
            // Do nothing but release the readback entry.
            _pool.Free(entry);
            return;
        }

        lock (_lastVideoFrameLock)
        {
            while (_finished.Count > 0)
            {
                _pool.Free(_finished[0]);
                _finished.RemoveAt(0);
            }
            if (_lastVideoFrameEntry != null)
            {
                // Mark this frame to get freed in the next frame.
                _pool.Free(_lastVideoFrameEntry);  
            }
             //  _pool.FreeMarkedEntry();
            _lastVideoFrameEntry = entry;
        }
        
        // frameRate.GetND(out var frameRateN, out var frameRateD);
        //
        // // Frame data
        // // Frame data setup
        // var frame = new Interop.VideoFrame
        // {
        //     Width = entry.Width,
        //     Height = entry.Height,
        //     LineStride = entry.Width * 2,
        //     FourCC = entry.FourCC,
        //     FrameFormat = Interop.FrameFormat.Progressive,
        //     Data = entry.ImagePointer,
        //     _Metadata = entry.MetadataPointer,
        //     Timecode = long.MaxValue,
        //     FrameRateD = frameRateD,
        //     FrameRateN = frameRateN
        // };
        //
        // // Async-send initiation
        // // This causes a synchronization for the last frame -- i.e., It locks
        // // the thread if the last frame is still under processing.
        // _send.SendVideoAsync(frame);
        //
        // // We don't need the last frame anymore. Free it.
        // _pool.FreeMarkedEntry();
        //
        // // Mark this frame to get freed in the next frame.
        // _pool.Mark(entry);
    }

    #endregion

    #region Component state controller

    Camera _attachedCamera;
    private float _lastVirtualListenerDistance = -1f;
    
    // Component state reset without NDI object disposal
    internal void ResetState(bool willBeActive)
    {
        _audioMode = audioMode;
        if (Application.isPlaying)
        {
            CheckAudioListener(willBeActive && _audioMode != AudioMode.None);

            if (audioMode != AudioMode.CustomVirtualAudioSetup)
                ClearVirtualSpeakerListeners();

            _lastVirtualListenerDistance = virtualListenerDistance;
            switch (audioMode)
            {
                case AudioMode.None:
                case AudioMode.AudioListener:
                    lock (_channelVisualisationsLock)
                        _channelVisualisations = null;
                    break;
                case AudioMode.VirtualQuad:
                    CreateAudioSetup_Quad();
                    break;
                case AudioMode.Virtual5Point1:
                    CreateAudioSetup_5point1();
                    break;
                case AudioMode.Virtual7Point1:
                    CreateAudioSetup_7point1();
                    break;
                case AudioMode.Virtual32Array:
                    CreateAudioSetup_32Array();
                    break;
                case AudioMode.SpeakerConfigAsset:
                    CreateAudioSetup_bySpeakerConfig();
                    break;
                case AudioMode.ObjectBased:
                    VirtualAudio.UseVirtualAudio = true;
                    VirtualAudio.ActivateObjectBasedAudio(true, maxObjectBasedChannels);
                    break;
                case AudioMode.CustomVirtualAudioSetup:
                    break;
                case AudioMode.Individual:
                    // Individual mode uses AudioListenerIndividualBridge components
                    // No VirtualAudio setup needed - bridges handle audio capture
                    lock (_channelVisualisationsLock)
                        _channelVisualisations = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        // Camera capture coroutine termination
        // We use this to kill only a single coroutine. It may sound like
        // overkill, but I think there is no side effect in doing so.
        StopAllCoroutines();

        #if KLAK_NDI_HAS_SRP

        // A SRP may call this callback after camera destruction. We can
        // exclude those cases by null-checking _attachedCamera.
        if (_attachedCamera != null)
            CameraCaptureBridge.RemoveCaptureAction(_attachedCamera, OnCameraCapture);

        #endif

        _attachedCamera = null;

        // The following part of code is to activate the subcomponents. We can
        // break here if willBeActive is false.
        if (!willBeActive)
        {
            _videoSendThreadCancelTokenSource?.Cancel();
            if (_videoSendThreadClocked != null && !_videoSendThreadClocked.IsCanceled)
                _videoSendThreadClocked?.Wait();
            _videoSendThreadCancelTokenSource?.Dispose();
            _videoSendThreadClocked?.Dispose();
            _videoSendThreadClocked = null;
            _videoSendThreadCancelTokenSource = null;
            return;
        }

        if (captureMethod == CaptureMethod.Camera)
        {
            #if KLAK_NDI_HAS_SRP

            // Camera capture callback setup
            if (sourceCamera != null)
                CameraCaptureBridge.AddCaptureAction(sourceCamera, OnCameraCapture);

            #endif

            _attachedCamera = sourceCamera;
        }
        else
        {
            // Capture coroutine initiation
            StartCoroutine(CaptureCoroutine());
        }

        _videoSendThreadCancelTokenSource?.Cancel();
        if (_videoSendThreadClocked != null && !_videoSendThreadClocked.IsCanceled)
            _videoSendThreadClocked?.Wait();
        
        _videoSendThreadCancelTokenSource?.Dispose();
        _videoSendThreadClocked?.Dispose();
        _videoSendThreadCancelTokenSource = null;
        _videoSendThreadClocked = null;
        
        _videoSendThreadCancelTokenSource = new CancellationTokenSource();
        _videoSendThreadCancelToken = _videoSendThreadCancelTokenSource.Token;

        _videoSendThreadClocked = new Task(VideoSendThreadClocked, _videoSendThreadCancelToken, TaskCreationOptions.LongRunning);
        _videoSendThreadClocked.Start();
    }

    // Component state reset with NDI object disposal
    internal void Restart(bool willBeActivate)
    {
        if (_metaDataPtr != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(_metaDataPtr);
            _metaDataPtr = IntPtr.Zero;
        }

        if (Application.isPlaying)
        {
            sampleRate = AudioSettings.outputSampleRate;
            _audioSampleRate = sampleRate;
        }

        // Initialize audio ring buffer (eager init when restarting active)
        if (willBeActivate && audioMode == AudioMode.AudioListener)
        {
            _audioChannels = Mathf.Max(_audioChannels, 2); // will be corrected later
            int capacity = (_audioSampleRate * _audioChannels * AUDIO_BUFFER_LENGTH_MS) / 1000;
            _audioRingBuffer = new float[Math.Max(capacity, 1)];
            _audioWriteIndex = 0;
            _audioReadIndex = 0;
            _audioAvailableSamples = 0;
            _audioReadStarted = false;
            Debug.Log($"[NdiSender] Audio ring buffer initialized (Restart): {capacity} samples ({AUDIO_BUFFER_LENGTH_MS}ms)");
        }

        // Get selected individual bridge for Individual mode
        if (willBeActivate && audioMode == AudioMode.Individual)
        {
            lock (_individualBridgesLock)
            {
                if (_registeredIndividualBridges.TryGetValue(objectBasedBridgeId, out var bridge))
                {
                    _selectedIndividualBridge = bridge;
                    Debug.Log($"[NdiSender] Selected individual bridge ID {objectBasedBridgeId}: {bridge.gameObject.name}");
                }
                else
                {
                    _selectedIndividualBridge = null;
                    Debug.LogWarning($"[NdiSender] No bridge found with ID {objectBasedBridgeId}");
                }
            }
        }
        else
        {
            _selectedIndividualBridge = null;
        }

        // Debug.Log("Driver capabilties: " + AudioSettings.driverCapabilities);
        ResetState(willBeActivate);

        // Do NOT release sender objects if we are activating (previous behavior disposed them)
        if (!willBeActivate)
        {
            ReleaseSenderObjects();
        }
    }

    internal void ResetState() => ResetState(isActiveAndEnabled);
    internal void Restart() => Restart(isActiveAndEnabled);

    #endregion

    #region MonoBehaviour implementation

    void OnEnable()
    {
        if (Application.isPlaying && addMissingAudioSourceListenersAtRuntime)
        {
            var audioSources = SearchForAudioSourcesWithMissingListener();
            foreach (var audioSource in audioSources)
                audioSource.gameObject.AddComponent<AudioSourceListener>();
        }
        ResetState();
    }

    void OnDisable() => Restart(false);

    void OnDestroy()
    {
        Restart(false);
    } 
        

    #endregion
}

} // namespace Klak.Ndi