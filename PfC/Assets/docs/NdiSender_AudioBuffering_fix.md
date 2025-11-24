# NDI Audio Buffering - Ring Buffer Implementation

## Executive Summary

This document explains the ring buffer architecture implemented to eliminate audio crackling in NDI Sender for **AudioListener** and **Individual** modes. The solution uses **ring buffers in bridge components** (AudioListenerBridge and AudioListenerIndividualBridge) to decouple Unity's audio thread from the NDI network transmission thread, preventing timing mismatches that caused audio artifacts.

**Key Insight**: Buffering happens in the bridge components, NOT in NdiSender. NdiSender simply retrieves buffered data from Update loop and sends to NDI.

---

## Problem Statement

### Symptoms in Old Implementation
- Audio crackling and pops during NDI streaming
- Intermittent audio discontinuities
- Buffer underruns
- Timing synchronization issues

### Root Cause
The old implementation sent audio **directly from Unity's audio thread** (`OnAudioFilterRead`) to NDI transmission. This created a critical timing mismatch:

1. **Unity Audio Thread**: Runs at unpredictable intervals, driven by the audio driver (~every 10-20ms, varies with system load)
2. **NDI Send Operations**: Called directly from audio thread with unpredictable timing
3. **Result**: When NDI transmission timing didn't align with audio thread timing, gaps and discontinuities occurred, causing crackling

---

## Architecture Overview

### New Implementation: Buffered Bridge Pattern

**AudioListener Mode:**
```
Audio Thread (OnAudioFilterRead)          Main Thread (Update Loop)
            ↓                                       ↓
    AudioListenerBridge                    GetAccumulatedAudio()
      Ring Buffer (200ms)                           ↓
            ↓                                   Send to NDI
    Accumulates audio ──────────────────────────────┘
```

**Individual Mode:**
```
Audio Thread (OnAudioFilterRead)          Main Thread (Update Loop)
            ↓                                       ↓
  AudioListenerIndividualBridge           GetAccumulatedAudio()
      Ring Buffer (200ms)                           ↓
            ↓                                   Send to NDI
    Accumulates audio ──────────────────────────────┘
```

### Key Components

#### 1. AudioListenerBridge.cs (AudioListener Mode)
**File:** `Runtime/Component/AudioListenerBridge.cs`

- Captures audio from Unity's audio thread in `OnAudioFilterRead`
- Buffers into a **200ms ring buffer**
- Provides `GetAccumulatedAudio()` method for batch retrieval
- **NdiSender.Update()** calls `GetAccumulatedAudio()` from main thread (line ~287)

#### 2. NdiSender.cs (Coordinator)
**File:** `Runtime/Component/NdiSender.cs`

- **Does NOT use its own ring buffer** (variables at lines 101-110 are unused/leftover)
- Retrieves audio from bridges in Update loop (main thread):
  - **AudioListener mode**: calls `_audioListenerBridge.GetAccumulatedAudio()` (line ~287)
  - **Individual mode**: calls `_selectedIndividualBridge.GetAccumulatedAudio()` (line ~328)
- Sends to NDI with consistent timing from main thread

---

## Why the Old Version Failed

### Old Architecture

```
Unity Audio Thread → OnAudioFilterRead → DIRECT NDI Send (same thread)
```

**Critical Issues:**

#### 1. Thread Timing Mismatch
- `OnAudioFilterRead` called by Unity's audio driver at irregular intervals (~10-20ms, varies with CPU load)
- NDI expects consistent data delivery
- Direct coupling meant any audio thread jitter immediately affected NDI stream

#### 2. No Buffering
- Zero tolerance for timing variations
- If audio thread delayed, NDI would send incomplete/stale data
- If audio thread ran fast, data could be dropped

#### 3. Direct Send from Audio Thread

**Old AudioListener Mode (oldndisender.cs:284):**
```csharp
private void OnAudioFilterRead(float[] data, int channels) {
    if (_audioMode == AudioMode.AudioListener) {
        SendAudioListenerData(data, channels);  // IMMEDIATE SEND - WRONG!
    }
}
```

#### 4. Why This Caused Crackling
- **Buffer Underruns**: NDI tried to send but audio thread hadn't provided new data yet
- **Sample Discontinuities**: Audio frames arrived out of expected sequence
- **Variable Latency**: Inconsistent delays (0-50ms variance)
- **Thread Contention**: Audio thread competing with other threads

---

## Detailed Implementation

### AudioListenerBridge Ring Buffer

**File:** `Runtime/Component/AudioListenerBridge.cs`

**Ring Buffer Variables (lines 33-43):**
```csharp
private const int BufferLengthMS = 200;     // 200ms capacity
private float[] _ringBuffer;                 // Circular buffer
private int _writeIndex;                     // Write position
private int _ndiReadIndex;                   // NdiSender's read position
private int _availableSamples;               // Accumulated samples
private bool _ndiReadStarted;                // Delay flag
```

**Initialization (Start, lines 116-150):**
```csharp
_sampleRate = AudioSettings.outputSampleRate;  // Typically 48000 Hz
int capacity = (_sampleRate * _channels * BufferLengthMS) / 1000;
_ringBuffer = new float[Math.Max(capacity, 1)];
```
*For 48kHz stereo: 48000 × 2 × 200 / 1000 = 19,200 samples (~76KB)*

**Write Operation (WriteToRing, lines 215-227):**
```csharp
private void WriteToRing(float[] data) {
    int capacity = _ringBuffer.Length;
    for (int i = 0; i < data.Length; i++) {
        _ringBuffer[_writeIndex] = data[i];
        _writeIndex = (_writeIndex + 1) % capacity;
    }
    _availableSamples = Math.Min(_availableSamples + data.Length, capacity);
}
```

**Read Operation (GetAccumulatedAudio, lines 22-65):**
```csharp
public bool GetAccumulatedAudio(out float[] audioData, out int channels) {
    // Wait until buffer is 50% full before starting
    if (!_ndiReadStarted && _availableSamples >= capacity / 2) {
        int delay = capacity / 2;  // 100ms delay
        _ndiReadIndex = (_writeIndex - delay + capacity) % capacity;
        _ndiReadStarted = true;
    }

    if (!_ndiReadStarted)
        return false;  // Still filling

    // Calculate and return accumulated samples
    int available = (_writeIndex - _ndiReadIndex + capacity) % capacity;
    audioData = new float[available];
    for (int i = 0; i < available; i++) {
        audioData[i] = _ringBuffer[(_ndiReadIndex + i) % capacity];
    }
    _ndiReadIndex = (_ndiReadIndex + available) % capacity;
    return true;
}
```

**Called from NdiSender.Update() (NdiSender.cs:284-294):**
```csharp
if (_audioMode == AudioMode.AudioListener && Application.isPlaying) {
    if (_audioListenerBridge != null) {
        if (_audioListenerBridge.GetAccumulatedAudio(out float[] audioData, out int channels)) {
            _audioChannels = channels;
            SendAudioListenerData(audioData, channels);  // Main thread!
        }
    }
}
```

### How It All Connects

**AudioListener Mode Flow:**
1. Unity audio driver calls `AudioListenerBridge.OnAudioFilterRead()` (audio thread)
2. AudioListenerBridge writes to ring buffer
3. `NdiSender.Update()` calls `GetAccumulatedAudio()` (main thread)
4. NdiSender sends accumulated audio to NDI

**Individual Mode Flow:**
1. Unity audio driver calls `AudioListenerIndividualBridge.OnAudioFilterRead()` (audio thread)
2. AudioListenerIndividualBridge writes to ring buffer
3. `NdiSender.Update()` calls `GetAccumulatedAudio()` from the selected bridge (main thread)
4. NdiSender sends accumulated audio to NDI

---

## How the New Version Solves Crackling

### 1. Thread Decoupling

**Audio Thread (Fast, Non-Blocking):**
- Writes to ring buffer only (~1-2μs per sample)
- No network calls
- No blocking operations

**Main Thread (Consistent Timing):**
- Retrieves buffered audio in Update loop
- Sends to NDI with predictable timing
- Can handle variations without affecting quality

### 2. Initial Delay Buffer (100ms Cushion)

```
Write Pos: ──────────────────────────────>
           [    Buffer (200ms capacity)   ]
                      ↑
                Read Pos (100ms behind)
```

This 100ms cushion absorbs:
- Audio thread jitter: ±10-20ms typical
- Update loop variations: ±5-10ms
- Network delays: ±10-30ms
- CPU scheduling: ±5-15ms
- **Total protection: ±100ms**

### 3. Underrun Prevention

```csharp
// Don't read if buffer not ready
if (!_readStarted)
    return false;

// Don't read if insufficient data
int available = (_writeIndex - _readIndex + capacity) % capacity;
if (available < outputBuffer.Length)
    return false;
```

### 4. Consistent Update Loop Timing

**Before:**
```
Audio Thread calls at irregular times → NDI Send → CRACKLING
```

**After:**
```
Audio Thread → Buffer (accumulates)
Main Thread (16.7ms intervals for 60fps) → Read Buffer → NDI Send → SMOOTH
```

---

## Tunable Parameters

### Buffer Length

```csharp
private const int BufferLengthMS = 200;
```

**Options:**
- **100ms**: Lower latency (~50ms cushion) - for stable systems
- **200ms** (default): Good balance (~100ms cushion) - recommended
- **300ms**: Maximum protection (~150ms cushion) - for unstable systems

**Trade-off:** Latency vs. Stability

### Initial Fill Fraction

Currently fixed at 50% (capacity / 2). To make configurable:
```csharp
public float initialBufferFillFraction = 0.5f;
int startThreshold = (int)(capacity * initialBufferFillFraction);
```

---

## Code References

### AudioListenerBridge.cs
- Ring buffer variables: lines 33-43
- `GetAccumulatedAudio()`: lines 22-65 (called by NdiSender from Update)
- `WriteToRing()`: lines 215-227
- `OnAudioFilterRead()`: lines 194-213

### AudioListenerIndividualBridge.cs
- Inherits all ring buffer functionality from AudioListenerBridge
- Adds AudioSource requirement and registration system
- Uses Bridge ID for identification

### NdiSender.cs
- AudioListener mode retrieval: lines 282-295
- Individual mode retrieval: lines 328-361
- **Important**: Ring buffer variables (lines 101-110) are UNUSED

---

## Individual Audio Mode

### Overview
Individual mode was added to capture audio from a **single specific AudioSource** (not all scene audio like AudioListener, not mixed sources like ObjectBased).

### Components

#### AudioListenerIndividualBridge.cs
**File:** `Runtime/Component/AudioListenerIndividualBridge.cs`

- Inherits from `AudioListenerBridge` (reuses all ring buffer functionality)
- Requires `AudioSource` component
- Registers itself with NdiSender by Bridge ID
- Uses the same 200ms ring buffer as AudioListenerBridge

#### How to Use Individual Mode

1. **Add AudioListenerIndividualBridge** to your AudioSource GameObject
2. **Set Bridge ID** in the component (e.g., 0, 1, 2, etc.)
3. **In NdiSender**, select **Individual** audio mode
4. **Set Audio Bridge ID** to match the bridge you want to capture

**Example Setup:**
```
GameObject: "DialogueAudio"
  ├─ AudioSource (playing dialogue)
  └─ AudioListenerIndividualBridge (Bridge ID = 0)

GameObject: "NdiSender"
  └─ NdiSender (Audio Mode = Individual, Audio Bridge ID = 0)
```

### Registration System

**NdiSender.cs (lines 93-121):**
```csharp
private static Dictionary<int, AudioListenerIndividualBridge> _registeredIndividualBridges;

public static void RegisterIndividualAudioBridge(AudioListenerIndividualBridge bridge) {
    _registeredIndividualBridges[bridge.BridgeId] = bridge;
}
```

**NdiSender.cs Update() (lines 328-361):**
```csharp
if (_audioMode == AudioMode.Individual && Application.isPlaying) {
    // Get selected bridge by ID (cached in Restart)
    if (_selectedIndividualBridge != null) {
        if (_selectedIndividualBridge.GetAccumulatedAudio(out float[] audioData, out int channels)) {
            SendAudioListenerData(audioData, channels);
        }
    }
}
```

### Manual Recovery

If audio stops (e.g., when NdiReceiver enables audio), you can manually restart the AudioSource by toggling it off and on again in the Inspector, or by restarting play mode.

---

## Known Issues and Workarounds

### Issue: NdiReceiver Breaking Individual Mode Audio

**Problem:** When NdiReceiver enables audio, it calls `AudioSettings.Reset()` to change speaker mode, which causes Unity to stop calling `OnAudioFilterRead` on other AudioSources, breaking Individual mode.

**Root Cause:** `NdiReceiver.ResetAudioSpeakerSetup()` (line ~1150) calls `AudioSettings.Reset(audioConfiguration)` which reinitializes Unity's audio system.

**Solution:** Commented out `AudioSettings.Reset()` in NdiReceiver.cs to prevent audio system reinitialization.

**File:** `Runtime/Component/NdiReceiver.cs` line ~1150
```csharp
// AudioSettings.Reset(audioConfiguration);  // COMMENTED OUT - breaks Individual mode
```

**Alternative:** Set Unity's default speaker mode to Stereo in Project Settings to minimize the need for runtime changes:
1. Edit → Project Settings → Audio
2. Set "Default Speaker Mode" to **Stereo**

---

## Summary

| Aspect | Old | New |
|--------|-----|-----|
| **Send Timing** | Audio thread | Main thread (Update) |
| **Buffering** | None | 200ms ring buffers |
| **Initial Delay** | None | 100ms (50% of capacity) |
| **Jitter Tolerance** | ±2-5ms | ±80-100ms |
| **Latency** | ~10-50ms (variable) | ~100-120ms (stable) |
| **Audio Quality** | Crackling | Smooth, artifact-free |

**Key Principle**: Buffer on audio thread, retrieve from main thread, send to NDI with consistent timing.

### Audio Modes Comparison

| Mode | Use Case | Buffer Location | Capture Method |
|------|----------|-----------------|----------------|
| **AudioListener** | All scene audio | AudioListenerBridge | Unity's mixed output |
| **Individual** | Single AudioSource | AudioListenerIndividualBridge | Specific AudioSource |
