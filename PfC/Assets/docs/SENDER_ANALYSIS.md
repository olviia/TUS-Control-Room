# NDI Sender Audio Analysis - Looking for Receiver-Analogous Bug

## Summary
User reports "same audio issues" (noise like misaligned radio frequency) in sender as they had with receiver's right channel before the stride fix. This document analyzes if there's an analogous bug in the sender.

## Receiver Bug (FIXED)
**Problem:** Right channel corruption due to wrong stride in planar-to-interleaved conversion
**Root Cause:** Used `length` (samples to copy) instead of `samplesPerChannel` (total buffer size) as stride
**Fix:** Created `PlanarToInterleavedWithStride` with explicit `planarStride` parameter

### Receiver Fix Code:
```csharp
BurstMethods.PlanarToInterleavedWithStride(
    channelDataPtr,
    audioFramesamplesReaded,  // offset into NDI planar buffer
    _currentAudioFrame.noChannels,
    _currentAudioFrame.samplesPerChannel,  // STRIDE - total samples per channel in NDI frame
    destPtr,
    samplesCopied * channelCountInData,
    channelCountInData,
    samplesToCopy  // how many samples to copy in this call
);
```

## Sender Current Implementation

### Sender Conversion:
```csharp
BurstMethods.InterleavedToPlanarWithStride(
    (float*)dataPtr, 0, numChannels,  // source: Unity's interleaved buffer
    (float*)samplesPtr, 0, alignedStride, numChannels,  // dest: planar buffer, stride=alignedStride
    numSamples  // samples to convert
);
```

### InterleavedToPlanarWithStride Code:
```csharp
public static unsafe void InterleavedToPlanarWithStride(
    float* interleavedData, int interleavedOffset, int interleavedChannels,
    float* destData, int destOffset, int destStride, int destChannels,
    int length)
{
    int channels = math.min(interleavedChannels, destChannels);
    for (int i = 0; i < length; i++)
    {
        for (int c = 0; c < channels; c++)
            destData[destOffset + (destStride * c + i)] =
                interleavedData[interleavedOffset + (i * interleavedChannels + c)];
    }
}
```

## Key Differences: Receiver vs Sender

| Aspect | Receiver (reads partial frames) | Sender (always full frames) |
|--------|-------------------------------|---------------------------|
| **Scenario** | NDI frame has 1600 samples/channel, Unity needs 512 | Unity gives 1024 samples/channel, we send all 1024 |
| **Stride** | `samplesPerChannel=1600` (full NDI frame) | `alignedStride=1024` (same as length) |
| **Length** | `samplesToCopy=512` (partial copy) | `numSamples=1024` (full conversion) |
| **Bug?** | stride ≠ length → **BUG WAS HERE** | stride == length → **should be correct?** |

## WAIT - Potential Issues in Sender:

### 1. `alignedStride` might differ from `numSamples`
The sender uses power-of-2 alignment:
```csharp
int alignedStride = numSamples;  // 1024
if ((alignedStride & (alignedStride - 1)) != 0) {
    alignedStride = 1;
    while (alignedStride < numSamples) alignedStride <<= 1;
}
```

If `numSamples=1024` (already power of 2), then `alignedStride=1024` → stride == length ✓

BUT: What if Unity sends a different buffer size?

### 2. Ring Buffer Write vs Read Mismatch?
**Write:** `_audioRingBuffer.Write(samples, alignedStride * numChannels);`
- Writes planar data: `[L0...L1023, R0...R1023]` with stride 1024

**Read:** `_audioRingBuffer.Read(_tempReadBuffer, samplesPerFrame * channels);`
- Where `samplesPerFrame = numSamples > 0 ? numSamples : 1024`

**Send:** `SendAudioToNDI(_tempReadBuffer, samplesPerFrame, channels);`
- With `channel_stride_in_bytes = samplesPerFrame * sizeof(float)`

This should match... UNLESS `numSamples` changes between write and read!

### 3. Potential Bug: What if `alignedStride` was LARGER than `numSamples`?

If we align to next power of 2 and then write `alignedStride * numChannels` samples, but only `numSamples` samples are valid, we'd be writing GARBAGE in the padding!

Example:
- `numSamples = 1000` (not power of 2)
- `alignedStride = 1024` (aligned up)
- We convert only 1000 samples per channel
- But write 1024 * 2 = 2048 samples to ring buffer
- The last 24 * 2 = 48 samples are GARBAGE (uninitialized or stale)!

BUT we do `Array.Clear(samples, 0, samples.Length);` before conversion, so garbage should be zeros...

### 4. CRITICAL: `Array.Clear` clears BEFORE conversion!
```csharp
System.Array.Clear(samples, 0, samples.Length);  // Clears entire buffer to 0
BurstMethods.InterleavedToPlanarWithStride(...);  // Converts numSamples per channel
_audioRingBuffer.Write(samples, alignedStride * numChannels);  // Writes aligned size
```

So if `alignedStride > numSamples`, we're writing:
- Valid audio: samples [0 to numSamples-1] per channel
- SILENCE: samples [numSamples to alignedStride-1] per channel

This would inject SILENCE/GAPS, causing audio dropouts/glitches that could sound like noise!

## HYPOTHESIS:
The noise might be caused by sending SILENCE PADDING when `alignedStride > numSamples`, creating gaps in the audio stream!

## PROPOSED FIX:
Only write `numSamples * numChannels` to the ring buffer, NOT `alignedStride * numChannels`:

```csharp
// FIX: Write actual converted samples, not aligned size
int totalSamples = numSamples * numChannels;  // Use numSamples, not alignedStride!
_audioRingBuffer.Write(samples, totalSamples);
```

This ensures we only send the actual audio data, not any padding/silence.
