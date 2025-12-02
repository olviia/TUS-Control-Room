using UnityEngine;
using System.IO;
using System;

/// <summary>
/// Captures WebRTC-related logs and writes them to a file for debugging
/// </summary>
public class WebRTCLogger : MonoBehaviour
{
    [Header("Logger Settings")]
    public bool enableLogging = true;
    public string logFileName = "WebRTC_Log";

    private StreamWriter logWriter;
    private string logFilePath;
    private int logCount = 0;

    void Awake()
    {
        if (!enableLogging) return;

        // Create unique log file with timestamp
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string computerName = SystemInfo.deviceName.Replace(" ", "_");
        logFileName = $"{logFileName}_{computerName}_{timestamp}.txt";

        // Use persistent data path (works across platforms)
        logFilePath = Path.Combine(Application.persistentDataPath, logFileName);

        // Create log file
        try
        {
            logWriter = new StreamWriter(logFilePath, false);
            logWriter.AutoFlush = true;

            WriteHeader();

            // Subscribe to Unity's log callback
            Application.logMessageReceived += HandleLog;

            Debug.Log($"[WebRTCLogger] Logging enabled - file: {logFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[WebRTCLogger] Failed to create log file: {e.Message}");
            enableLogging = false;
        }
    }

    void OnDestroy()
    {
        if (logWriter != null)
        {
            Application.logMessageReceived -= HandleLog;

            WriteFooter();
            logWriter.Close();
            logWriter = null;

            Debug.Log($"[WebRTCLogger] Log file closed: {logFilePath}");
            Debug.Log($"[WebRTCLogger] Total logs captured: {logCount}");
        }
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (!enableLogging || logWriter == null) return;

        // Filter for WebRTC-related logs
        if (IsWebRTCRelated(logString))
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string typePrefix = GetLogTypePrefix(type);

            logWriter.WriteLine($"[{timestamp}] {typePrefix} {logString}");
            logCount++;

            // Also write stack trace for errors
            if (type == LogType.Error || type == LogType.Exception)
            {
                if (!string.IsNullOrEmpty(stackTrace))
                {
                    logWriter.WriteLine($"    Stack Trace: {stackTrace}");
                }
            }
        }
    }

    private bool IsWebRTCRelated(string logString)
    {
        // Check if log contains WebRTC-related keywords
        return logString.Contains("[📡") ||           // WebRTCStreamer
               logString.Contains("[🎯StreamManager]") ||  // StreamManager
               logString.Contains("[Audio]") ||          // Audio logs
               logString.Contains("WebRTC") ||           // General WebRTC
               logString.Contains("ICE") ||              // ICE candidate logs
               logString.Contains("Offer") ||            // SDP offer/answer
               logString.Contains("Answer") ||
               logString.Contains("Signaling") ||        // Signaling logs
               logString.Contains("Streaming") ||        // Streaming logs
               logString.Contains("TextureSource") ||    // Texture source logs
               logString.Contains("NDI") ||              // NDI logs
               logString.Contains("Connection state") ||  // Connection state changes
               logString.Contains("session") ||          // Session management
               logString.Contains("aaa_") ||             // Your custom prefix
               logString.Contains("aabb_");              // Your custom prefix
    }

    private string GetLogTypePrefix(LogType type)
    {
        switch (type)
        {
            case LogType.Error:
                return "[ERROR]";
            case LogType.Warning:
                return "[WARNING]";
            case LogType.Exception:
                return "[EXCEPTION]";
            default:
                return "[INFO]";
        }
    }

    private void WriteHeader()
    {
        logWriter.WriteLine("================================================================================");
        logWriter.WriteLine($"WebRTC Debug Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        logWriter.WriteLine($"Computer: {SystemInfo.deviceName}");
        logWriter.WriteLine($"OS: {SystemInfo.operatingSystem}");
        logWriter.WriteLine($"Unity Version: {Application.unityVersion}");
        logWriter.WriteLine("================================================================================");
        logWriter.WriteLine();
    }

    private void WriteFooter()
    {
        logWriter.WriteLine();
        logWriter.WriteLine("================================================================================");
        logWriter.WriteLine($"Log session ended - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        logWriter.WriteLine($"Total logs captured: {logCount}");
        logWriter.WriteLine("================================================================================");
    }

    [ContextMenu("Open Log File Location")]
    public void OpenLogFileLocation()
    {
        if (!string.IsNullOrEmpty(logFilePath))
        {
            string directory = Path.GetDirectoryName(logFilePath);
            Application.OpenURL($"file://{directory}");
            Debug.Log($"[WebRTCLogger] Opening: {directory}");
        }
    }

    [ContextMenu("Print Log File Path")]
    public void PrintLogFilePath()
    {
        if (!string.IsNullOrEmpty(logFilePath))
        {
            Debug.Log($"[WebRTCLogger] Log file: {logFilePath}");
        }
        else
        {
            Debug.Log($"[WebRTCLogger] No log file created yet");
        }
    }
}