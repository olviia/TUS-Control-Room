using IntPtr = System.IntPtr;
using Marshal = System.Runtime.InteropServices.Marshal;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace Klak.Ndi
{

    // Small helper class for NDI recv interop
    static class RecvHelper
    {
        // Track if we've already logged a warning for a specific source name
        private static Dictionary<string, float> _sourceWarningLog = new Dictionary<string, float>();
        private const float WARNING_INTERVAL = 10f; // Log warnings at most every 10 seconds

        public static Interop.Source? FindSource(string sourceName)
        {

            // Early out if sourceName is null or empty
            if (string.IsNullOrEmpty(sourceName))
            {
                Debug.LogWarning("[NDI Receiver] Attempted to find source with empty name");
                return null;
            }

            var currentSources = SharedInstance.Find.CurrentSources;

            foreach (var source in currentSources)
            {
                if (source.NdiName == sourceName)
                {
                    // Source found - clear warning state for this source
                    if (_sourceWarningLog.ContainsKey(sourceName))
                    {
                        _sourceWarningLog.Remove(sourceName);
                    }
                    return source;
                }
            }
            // Throttle warnings to avoid log spam
            if (!_sourceWarningLog.ContainsKey(sourceName) ||
                (Time.realtimeSinceStartup - _sourceWarningLog[sourceName]) > WARNING_INTERVAL)
            {

                // Log available sources to help debugging
                string availableSources = "Available sources: ";
                foreach (var source in currentSources)
                {
                    availableSources += source.NdiName + ", ";
                }
                if (currentSources.Length > 0)
                {
                    availableSources = availableSources.Substring(0, availableSources.Length - 2);
                }
                else
                {
                    availableSources += "none";
                }

                Debug.LogWarning($"[NDI Receiver] NDI source '{sourceName}' not found. {availableSources}");
                _sourceWarningLog[sourceName] = Time.realtimeSinceStartup;
            }
            return null;
        }

        public static unsafe Interop.Recv TryCreateRecv(string sourceName, Interop.Bandwidth bandwidth)
        {
            try
            {
                var source = FindSource(sourceName);
                if (source == null) return null;

                var opt = new Interop.Recv.Settings
                {
                    Source = (Interop.Source)source,
                    ColorFormat = Interop.ColorFormat.Fastest,
                    Bandwidth = bandwidth
                };

                // Create and return the receiver
                var recv = Interop.Recv.Create(opt);

                // Verify receiver was created properly
                if (recv == null || recv.IsInvalid || recv.IsClosed)
                {
                    Debug.LogError($"[NDI Receiver] Failed to create receiver for source '{sourceName}'");
                    return null;
                }

                Debug.Log($"[NDI Receiver] Successfully connected to NDI source '{sourceName}'");
                return recv;
            }
            catch (Exception e)
            {
                // Log any exceptions that occur during creation
                Debug.LogError($"[NDI Receiver] Exception creating receiver for '{sourceName}': {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        public static Interop.VideoFrame? TryCaptureVideoFrame(Interop.Recv recv)
        {
            try
            {
                // Check for null, invalid or closed receiver
                if (recv == null || recv.IsInvalid || recv.IsClosed)
                {
                    return null;
                }

                Interop.VideoFrame video;
                Interop.AudioFrame audio;
                Interop.MetadataFrame metadata;

                var type = recv.Capture(out video, out audio, out metadata, 0);

                if (type != Interop.FrameType.Video) return null;
                return (Interop.VideoFrame?)video;
            }
            catch (Exception e)
            {
                Debug.LogError($"[NDI Receiver] Exception capturing video frame: {e.Message}");
                return null;
            }
        }

        public static string GetStringData(IntPtr dataPtr)
        {
            if (dataPtr == IntPtr.Zero)
            {
                return null;
            }

            return Marshal.PtrToStringAnsi(dataPtr);
        }
        public static string[] GetAvailableSourceNames()
        {
            var sources = SharedInstance.Find.CurrentSources;
            string[] names = new string[sources.Length];
            
            for (int i = 0; i < sources.Length; i++) {
                names[i] = sources[i].NdiName;
            }
            
            return names;
        }
    }

} // namespace Klak.Ndi
