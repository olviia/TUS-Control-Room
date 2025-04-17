/* Filename: NetworkSceneManager.cs
 * Creator: Deniz Mevlevioglu
 * Date: 02/04/2025
 */

using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// From OBS start websocket and use the port from OBS
/// in the defaultWsPort here
/// Can also be set from the editor
/// </summary>

public class WebsocketManager : MonoBehaviour
{

    private OBSWebsocket obsWebSocket = new OBSWebsocket();
    private Queue<Action> actionsToExecuteOnMainThread = new Queue<Action>();

    //Development Server Settings
    [SerializeField] private string defaultWsAdress = "0.0.0.0";
    [SerializeField] private int defaultWsPort = 32419;
    [SerializeField] private string defaultWsPassword = "";

    //Public Events
    public event Action<bool> WsConnected;
    public event Action<string> WsMessage;
    private string defaultNotConnectedMessage = "No WebSocket connection, please check your settings";

    void Awake()
    {
        actionsToExecuteOnMainThread = new Queue<Action>();
    }

    void Start()
    {
        AutoConnectToServer();
    }

    private void Update()
    {

        while (actionsToExecuteOnMainThread.Count > 0)
        {
            Action action = actionsToExecuteOnMainThread.Dequeue();
            if (action != null)
            {
                action.Invoke();
            }
            else
            {
                Debug.LogWarning($"{nameof(actionsToExecuteOnMainThread)} tried to do a Null Action");
            }
        }

    }
    void OnDestroy()
    {
        if (obsWebSocket.IsConnected)
            obsWebSocket.Disconnect();
    }

    #region Connection Handling
    private void AutoConnectToServer()
    {
        string serverAdress = defaultWsAdress;
        int serverPort = defaultWsPort;
        string serverPassword = defaultWsPassword;

        ConnectToServer(serverAdress, serverPort, serverPassword);
    }

    public void ConnectToServer(string serverAdress, int serverPort, string serverPassword)
    {
        obsWebSocket.Connected -= OnConnected; 
        obsWebSocket.Disconnected -= OnDisconnected;

        obsWebSocket.Connected += OnConnected;
        obsWebSocket.Disconnected += OnDisconnected;

        try
        {
            obsWebSocket.ConnectAsync($"ws://{serverAdress}:{serverPort}", serverPassword);
        }
        catch (Exception e)
        {
            Debug.LogError($"WebSocket connection error: {e.Message}");
            actionsToExecuteOnMainThread.Enqueue(() => WsMessage?.Invoke($"WebSocket connection error: {e.Message}"));
        }
    }

    #endregion

    #region Methods

    //Studio mode needs to be enabled on OBS for transition
    //TODO: Also implement arguments for transition
    //i.e. style, length
    public void Transition()
    {
        if (!obsWebSocket.IsConnected)
        {
            actionsToExecuteOnMainThread.Enqueue(() => WsMessage?.Invoke(defaultNotConnectedMessage));
            Debug.LogError("Cant trigger Transition, not connected to OBS!");
            return;
        }

        if (!obsWebSocket.GetStudioModeEnabled())
        {
            actionsToExecuteOnMainThread.Enqueue(() => WsMessage?.Invoke("Please enable Studio Mode in OBS"));
            Debug.LogError("Studio Mode is not enabled");
            return;
        }

        try
        {
            obsWebSocket.TriggerStudioModeTransition();
            Debug.Log("Studio mode transition");
        }
        catch (Exception e)
        {
            actionsToExecuteOnMainThread.Enqueue(() => WsMessage?.Invoke("Unable to trigger transition: " + e.Message));
            Debug.LogError("Error triggering Studio Mode Transition: " + e.Message);
        }
    }

    //Start/end stream
    //Known issue: Log calls before streaming status changes so it outputs the wrong bool
    public void ToggleStream()
    {
        if (!obsWebSocket.IsConnected)
        {
            actionsToExecuteOnMainThread.Enqueue(() => WsMessage?.Invoke(defaultNotConnectedMessage));
            Debug.LogError("Cant trigger Transition, not connected to OBS!");
            return;
        }

        try
        {
            obsWebSocket.ToggleStream();
            Debug.Log("Streaming status: " + obsWebSocket.GetStreamStatus().IsActive);
        }
        catch (Exception e)
        {
            actionsToExecuteOnMainThread.Enqueue(() => WsMessage?.Invoke("Unable to trigger transition: " + e.Message));
            Debug.LogError("Error triggering Studio Mode Transition: " + e.Message);
        }
    }

    //Start/end recording
    //Known issue: Log calls before recording status changes so it outputs the wrong bool
    public void ToggleRecord()
    {
        if (!obsWebSocket.IsConnected)
        {
            actionsToExecuteOnMainThread.Enqueue(() => WsMessage?.Invoke(defaultNotConnectedMessage));
            Debug.LogError("Cant trigger Transition, not connected to OBS!");
            return;
        }

        try
        {
            obsWebSocket.ToggleRecord();
            Debug.Log("Recording status: " + obsWebSocket.GetRecordStatus().IsRecording);
        }
        catch (Exception e)
        {
            actionsToExecuteOnMainThread.Enqueue(() => WsMessage?.Invoke("Unable to trigger transition: " + e.Message));
            Debug.LogError("Error triggering Studio Mode Transition: " + e.Message);
        }
    }
    //needs to be called asynchronously
    public bool IsRecording()
    {
        return obsWebSocket.GetRecordStatus().IsRecording;
    }

    //needs to be called asynchronously
    public bool IsStreaming()
    {
        return obsWebSocket.GetStreamStatus().IsActive;
    }

    private void OnConnected(object sender, EventArgs e)
    {
        actionsToExecuteOnMainThread.Enqueue(() => WsConnected?.Invoke(true));
        actionsToExecuteOnMainThread.Enqueue(() => WsMessage?.Invoke("WebSocket connection successful"));
        Debug.Log("Connected to OBS");
    }

    private void OnDisconnected(object sender, ObsDisconnectionInfo e)
    {
        actionsToExecuteOnMainThread.Enqueue(() => WsConnected?.Invoke(false));
        actionsToExecuteOnMainThread.Enqueue(() => WsMessage?.Invoke("Disconnected from OBS"));
        Debug.Log($"Disconnected from OBS WebSocket Server. Reason: {e.WebsocketDisconnectionInfo?.CloseStatusDescription}");
    }
    #endregion

}
