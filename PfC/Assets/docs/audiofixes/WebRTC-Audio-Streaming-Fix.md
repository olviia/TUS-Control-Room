# WebRTC Audio Streaming Fix: Eliminating Crackling and Audio Artifacts

## Problem Description

### Symptoms
Audio streamed through WebRTC exhibited crackling, noise, and slowdown issues, particularly on faster computers (e.g., Dell XPS). The audio worked correctly on some systems but failed on others, indicating a timing-dependent bug.

### Root Causes

#### 1. **Thread Timing Mismatch**
The original implementation called `audioStreamTrack.SetData()` directly from Unity's audio thread (`OnAudioFilterRead`), which runs at the audio system's rate (~10-20ms intervals). This created a race condition with WebRTC's processing thread.

```csharp
// ❌ PROBLEMATIC APPROACH
void OnAudioFilterRead(float[] data, int channels)
{
    // Called on audio thread at variable intervals
    audioStreamTrack.SetData(data, channels, sampleRate); // Race condition!
}
```

**Why this caused problems:**
- Audio thread timing varies by system and CPU speed
- WebRTC expects consistent packet intervals
- Faster CPUs exposed the race condition more severely
- Thread context switching caused audio glitches

#### 2. **Variable Packet Sizes**
Even when moved to `Update()`, sending whatever accumulated between frames violated WebRTC's packet size requirements.

```csharp
// ⚠️ IMPROVED BUT STILL PROBLEMATIC
void Update()
{
    GetAccumulatedAudio(out audioData, out channels);
    audioStreamTrack.SetData(audioData, channels, sampleRate);
    // Variable size: 16.6ms, 33ms, 50ms chunks
}
```

**WebRTC Requirements:**
- Expects fixed-size audio packets (10ms, 20ms, 40ms, or 60ms)
- Internal processing uses 10ms frames
- Network packetization uses 20ms packets (standard)
- Variable sizes confuse the NetEQ jitter buffer

#### 3. **Hardware-Dependent Manifestation**

**Dell XPS (Faster CPU):**
- Audio thread runs more frequently
- Sends data to WebRTC too quickly
- Buffer overruns cause crackling

**Slower Computer:**
- Accidentally matched WebRTC's processing rate
- Worked by coincidence, not by design
- Still had underlying race condition

## Solution Architecture

### Three-Stage Pipeline

```
┌─────────────────────────────────────────────────────────────┐
│ Stage 1: Audio Thread (OnAudioFilterRead)                   │
│ - Frequency: ~10-20ms intervals                             │
│ - Task: Write to ring buffer                                │
└────────────────────┬────────────────────────────────────────┘
                     │ Thread-safe ring buffer
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ Stage 2: Main Thread (Update)                               │
│ - Frequency: ~16.6ms (60 FPS)                               │
│ - Task: Read accumulated audio, assemble packets            │
└────────────────────┬────────────────────────────────────────┘
                     │ Fixed-size packets
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ Stage 3: WebRTC Processing                                  │
│ - Receives exactly 20ms packets                             │
│ - Consistent timing for NetEQ jitter buffer                 │
└─────────────────────────────────────────────────────────────┘
```

### Implementation Details

#### Stage 1: Ring Buffer in AudioSourceBridge

**Purpose:** Decouple audio thread from main thread

**File:** `NdiReceiverAudioSourceBridge.cs` (AudioSourceBridge class)

```csharp
// Ring buffer configuration
private const int BufferLengthMS = 200;
private float[] _ringBuffer;
private int _writeIndex;
private int _availableSamples;
private int _webrtcReadIndex;
private bool _webrtcReadStarted;
private readonly object _bufferAccessLock = new object();

// Write from audio thread
private void OnAudioFilterRead(float[] data, int channels)
{
    // ... existing NDI audio processing ...

    _channels = channels;
    WriteToRing(data);  // Thread-safe write
}

// Read from main thread
public bool GetAccumulatedAudio(out float[] audioData, out int channels)
{
    lock (_bufferAccessLock)
    {
        // Wait until buffer is half full before starting
        if (!_webrtcReadStarted && _availableSamples >= capacity / 2)
        {
            int delay = capacity / 2;
            _webrtcReadIndex = (_writeIndex - delay + capacity) % capacity;
            _webrtcReadStarted = true;
        }

        // Return accumulated samples since last read
        // ...
    }
}
```

**Key Features:**
- 200ms buffer capacity for stability
- Thread-safe lock protection
- Waits until half full before starting (reduces initial latency issues)
- Separate read/write pointers

#### Stage 2: Packet Assembly in NdiAudioInterceptor

**Purpose:** Create fixed-size WebRTC packets

**File:** `NdiAudioInterceptor.cs`

```csharp
// Configuration
[SerializeField] private int webrtcPacketDurationMs = 20; // Standard

// Assembly buffer
private List<float> packetAssemblyBuffer = new List<float>();

void Update()
{
    if (isStreamingActive && targetAudioSourceBridge != null)
    {
        SendFixedSizeAudioPackets();
    }
}

private void SendFixedSizeAudioPackets()
{
    // Get accumulated audio from ring buffer
    if (targetAudioSourceBridge.GetAccumulatedAudio(out float[] accumulatedData, out int channels))
    {
        packetAssemblyBuffer.AddRange(accumulatedData);
        lastChannelCount = channels;
    }

    // Calculate exact packet size (e.g., 1920 samples for 20ms at 48kHz stereo)
    int samplesPerPacket = (sampleRate * lastChannelCount * webrtcPacketDurationMs) / 1000;

    // Send complete packets
    while (packetAssemblyBuffer.Count >= samplesPerPacket)
    {
        float[] packet = new float[samplesPerPacket];
        packetAssemblyBuffer.CopyTo(0, packet, 0, samplesPerPacket);
        packetAssemblyBuffer.RemoveRange(0, samplesPerPacket);

        audioStreamTrack.SetData(packet, lastChannelCount, sampleRate);
    }

    // Overflow protection
    int maxBufferSamples = (sampleRate * lastChannelCount * 100) / 1000; // 100ms max
    if (packetAssemblyBuffer.Count > maxBufferSamples)
    {
        packetAssemblyBuffer.RemoveRange(0, packetAssemblyBuffer.Count - maxBufferSamples);
    }
}
```

**Key Features:**
- Buffers incomplete packets until enough samples accumulate
- Sends exactly sized packets (default 20ms)
- Handles leftover samples for next packet
- Overflow protection prevents memory growth

## Configuration Options

### Packet Duration (`webrtcPacketDurationMs`)

Adjustable in Unity Inspector on `NdiAudioInterceptor` component:

| Duration | Use Case | Pros | Cons |
|----------|----------|------|------|
| **10ms** | Ultra-low latency | Lowest latency (~10ms) | Less stable, higher CPU |
| **20ms** | Standard (recommended) | Good balance | Standard latency (~20ms) |
| **40ms** | Poor networks | More stable | Higher latency (~40ms) |
| **60ms** | Very poor networks | Most stable | Highest latency (~60ms) |

### Ring Buffer Size

Configured in `AudioSourceBridge`:
```csharp
private const int BufferLengthMS = 200; // 200ms capacity
```

**Trade-offs:**
- **Larger buffer** (200ms+): More stable, higher initial latency
- **Smaller buffer** (100ms): Lower latency, less tolerance for timing jitter

## Technical Background

### WebRTC Audio Processing Requirements

1. **Internal Frame Size:** WebRTC processes audio in 10ms frames
2. **Packet Size:** Network transmission uses 20ms packets by default
3. **Sample Rate:** 48kHz is standard for WebRTC
4. **NetEQ Jitter Buffer:** Expects consistent packet arrival times

**References:**
- [WebRTC Audio Processing Module](https://groups.google.com/g/discuss-webrtc/c/hFuSb2FaPso) - 10ms frame requirement
- [Opus Codec Duration](https://github.com/pion/webrtc/discussions/2609) - 20ms packet standard
- [WebRTC NetEQ Jitter Buffer](https://webrtchacks.com/how-webrtcs-neteq-jitter-buffer-provides-smooth-audio/) - Packet timing requirements

### Unity Audio Thread Behavior

**OnAudioFilterRead Timing:**
- Called on separate audio thread
- Interval depends on DSP buffer size:
  - Best Latency: 512 samples (~11.6ms at 44.1kHz)
  - Good Latency: 1024 samples (~23.3ms)
  - Best Performance: 2048 samples (~46.4ms)

**Update() Timing:**
- Main thread, tied to frame rate
- Typically ~16.6ms (60 FPS)
- Provides consistent timing for packet assembly

## Comparison with NDI Audio Streaming

This solution mirrors the approach used by NDI's `NdiSender` for audio:

```csharp
// NdiSender.cs - Update()
if (_audioListenerBridge.GetAccumulatedAudio(out float[] audioData, out int channels))
{
    SendAudioListenerData(audioData, channels);
}
```

**Key Difference:**
- NDI can handle variable chunk sizes
- WebRTC requires fixed packet sizes
- Both use ring buffer + main thread pattern

## Troubleshooting

### Still Hearing Crackling?

1. **Check Packet Duration:**
   - Try increasing to 40ms for stability
   - Try decreasing to 10ms for lower latency

2. **Verify Sample Rate:**
   ```csharp
   Debug.Log($"Sample Rate: {AudioSettings.outputSampleRate}");
   // Should be 48000 for WebRTC
   ```

3. **Monitor Buffer State:**
   - Add debug logs to track buffer fill level
   - Check for overflow warnings

4. **Check Network Conditions:**
   - Poor networks may need 40ms packets
   - Use browser DevTools → WebRTC internals

### Buffer Overflow Warnings?

```
[🎵AudioInterceptor] Buffer overflow! Dropping samples
```

**Causes:**
- WebRTC can't keep up with incoming audio
- Network congestion
- CPU overload

**Solutions:**
- Increase packet duration (20ms → 40ms)
- Check network bandwidth
- Reduce video quality

## Performance Characteristics

### Latency Analysis

**Total Pipeline Latency:**
```
Ring Buffer Initial Fill:  ~100ms (half of 200ms)
Packet Assembly:           ~20ms (one packet duration)
WebRTC Processing:         ~20ms (NetEQ jitter buffer)
Network:                   Variable (10-100ms typical)
─────────────────────────────────────────────────────
Total:                     ~150-240ms
```

### CPU Impact

- **Ring Buffer:** Negligible (simple array copy)
- **Packet Assembly:** Low (List operations on small buffers)
- **Lock Contention:** Minimal (ring buffer lock held briefly)

### Memory Usage

- Ring buffer: `sampleRate * channels * 0.2 seconds`
  - 48kHz stereo: ~38KB
- Assembly buffer: Max 100ms = ~19KB
- Total: ~57KB per audio stream

## Migration Guide

### From Direct SetData to Ring Buffer

**Before:**
```csharp
targetAudioSourceBridge.OnWebRTCAudioReady += (data, channels, sampleRate) => {
    audioStreamTrack.SetData(data, channels, sampleRate);
};
```

**After:**
```csharp
// In Update()
if (targetAudioSourceBridge.GetAccumulatedAudio(out float[] accumulatedData, out int channels))
{
    packetAssemblyBuffer.AddRange(accumulatedData);
    // ... packet assembly and sending ...
}
```

## Future Improvements

### Potential Enhancements

1. **Adaptive Packet Duration:**
   - Dynamically adjust based on network conditions
   - Monitor WebRTC stats for packet loss

2. **Resampling Support:**
   - Convert between sample rates if needed
   - Support non-48kHz audio sources

3. **Multi-Channel Support:**
   - Currently handles stereo
   - Could support 5.1, 7.1 surround

4. **Drift Compensation:**
   - Monitor clock drift between audio thread and WebRTC
   - Adjust buffer size dynamically (like Vivox implementation)

## Credits and References

### Documentation
- [Unity WebRTC Package Docs](https://docs.unity3d.com/Packages/com.unity.webrtc@2.4/manual/audiostreaming.html)
- [Unity OnAudioFilterRead](https://docs.unity3d.com/ScriptReference/MonoBehaviour.OnAudioFilterRead.html)

### WebRTC Specifications
- [WebRTC Audio Processing](https://groups.google.com/g/discuss-webrtc/c/hFuSb2FaPso)
- [NetEQ Jitter Buffer](https://webrtchacks.com/how-webrtcs-neteq-jitter-buffer-provides-smooth-audio/)
- [Opus Codec Configuration](https://github.com/pion/webrtc/discussions/2609)

### Related Issues
- [Unity WebRTC Audio Latency Issue](https://github.com/Unity-Technologies/com.unity.webrtc/issues/525)
- [WebRTC Crackling Issues](https://github.com/AlexxIT/go2rtc/issues/1164)

---

**Document Version:** 1.0
**Last Updated:** 2025-12-02
**Applicable Unity WebRTC Package:** 2.4.0+
**Tested Environments:** Windows 10/11, Dell XPS, various audio configurations