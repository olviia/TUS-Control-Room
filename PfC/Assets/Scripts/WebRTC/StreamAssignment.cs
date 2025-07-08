using Unity.Netcode;
using BroadcastPipeline;

[System.Serializable]
public struct StreamAssignment : INetworkSerializable
{
    public ulong directorClientId;
    public string streamSourceName;
    public string sessionId;
    public PipelineType pipelineType;
    public bool isActive;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref directorClientId);
        
        // Handle null strings safely
        if (serializer.IsWriter)
        {
            var safeSourceName = streamSourceName ?? "";
            var safeSessionId = sessionId ?? "";
            serializer.SerializeValue(ref safeSourceName);
            serializer.SerializeValue(ref safeSessionId);
        }
        else
        {
            serializer.SerializeValue(ref streamSourceName);
            serializer.SerializeValue(ref sessionId);
        }
        
        serializer.SerializeValue(ref pipelineType);
        serializer.SerializeValue(ref isActive);
    }
}