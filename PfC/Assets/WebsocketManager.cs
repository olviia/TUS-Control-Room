using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication; // important for ObsDisconnectionInfo
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;
using System;
using System.Collections.Generic;
using UnityEngine;

public class WebsocketManager : MonoBehaviour
{

    private OBSWebsocket obsWebSocket = new OBSWebsocket();
    private Queue<Action> actionsToExecuteOnMainThread = new Queue<Action>();

    //Development Server Settings
    public string defaultWsAdress = "0.0.0.0";
    public int defaultWsPort = 32419;
    public string defaultWsPassword = "";

    //Public Events
    public event Action<bool> WsConnected;
    public event Action<string> WsMessage;
    private string defaultNotConnectedMessage = "No WebSocket connection, please check your settings";

    private void Awake()
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
                Debug.LogWarning($"{nameof(actionsToExecuteOnMainThread)} tryed to do a Null Action");
            }
        }

    }
    void OnDestroy()
    {
        if (obsWebSocket.IsConnected)
            obsWebSocket.Disconnect();
    }

    #region Connection Handling
    private void AutoConnectToServer() //called on Startup if PlayerPrefs contain Connection Data
    {
        string serverAdress = defaultWsAdress;
        int serverPort = defaultWsPort;
        string serverPassword = defaultWsPassword;

        ConnectToServer(serverAdress, serverPort, serverPassword);
    }

    public void ConnectToServer(string serverAdress, int serverPort, string serverPassword)
    {
        obsWebSocket.Connected -= OnConnected;  // delete old event listeners before adding new ones
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
        }
        catch (Exception e)
        {
            actionsToExecuteOnMainThread.Enqueue(() => WsMessage?.Invoke("Unable to trigger transition: " + e.Message));
            Debug.LogError("Error triggering Studio Mode Transition: " + e.Message);
        }
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
