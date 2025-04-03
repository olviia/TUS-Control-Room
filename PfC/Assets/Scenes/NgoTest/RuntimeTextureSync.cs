using Unity.Netcode;
using UnityEngine;

// This component will be on your single media display object that syncs with the Reporter
public class RuntimeTextureSync : NetworkBehaviour
{

    [SerializeField] private Renderer targetRenderer;

    // For texture data synchronization
    private NetworkVariable<bool> hasTextureUpdate = new NetworkVariable<bool>(false);
    private Texture2D syncedTexture;

    // Buffer for texture data chunks
    private byte[] pendingTextureData;
    private int currentChunkIndex = 0;
    private int totalChunks = 0;
    private Vector2Int textureSize;

    public override void OnNetworkSpawn()
    {
        // Subscribe to texture update flag
        hasTextureUpdate.OnValueChanged += OnTextureUpdateFlagChanged;

        // Create a texture if needed
        if (syncedTexture == null)
        {
            syncedTexture = new Texture2D(2, 2);

            if (targetRenderer != null)
            {
                targetRenderer.material.mainTexture = syncedTexture;
            }
        }
    }

    // Called to copy texture from a source renderer
    public void CopyTextureFromRenderer(Renderer sourceRenderer)
    {
        if (!IsServer || sourceRenderer == null || sourceRenderer.material.mainTexture == null)
            return;

        Texture2D sourceTexture = sourceRenderer.material.mainTexture as Texture2D;
        if (sourceTexture != null)
        {
            StartTextureSync(sourceTexture);
        }
    }

    private void OnTextureUpdateFlagChanged(bool oldValue, bool newValue)
    {
        if (newValue && !IsServer)
        {
            // Client received notification that texture updates are coming
            RequestTextureInfoServerRpc();
        }
    }

    // Start the texture synchronization process
    private void StartTextureSync(Texture2D sourceTexture)
    {
        // Notify clients that a texture update is coming
        hasTextureUpdate.Value = true;

        // Store texture info for sending to clients
        textureSize = new Vector2Int(sourceTexture.width, sourceTexture.height);

        // Get texture data
        byte[] textureData = sourceTexture.EncodeToPNG();

        // Store for sending in chunks
        pendingTextureData = textureData;
        totalChunks = Mathf.CeilToInt((float)textureData.Length / 1024); // 1KB chunks
        currentChunkIndex = 0;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestTextureInfoServerRpc(ServerRpcParams serverRpcParams = default)
    {
        // Send texture info to the requesting client
        SendTextureInfoClientRpc(textureSize.x, textureSize.y, totalChunks,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { serverRpcParams.Receive.SenderClientId }
                }
            });
    }

    [ClientRpc]
    private void SendTextureInfoClientRpc(int width, int height, int chunks, ClientRpcParams clientRpcParams = default)
    {
        // Client prepares to receive texture data
        textureSize = new Vector2Int(width, height);
        totalChunks = chunks;
        currentChunkIndex = 0;
        pendingTextureData = new byte[totalChunks * 1024]; // Might be slightly larger than needed

        // Request the first chunk
        RequestTextureChunkServerRpc(0);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestTextureChunkServerRpc(int chunkIndex, ServerRpcParams serverRpcParams = default)
    {
        // Calculate chunk start and length
        int startIndex = chunkIndex * 1024;
        int length = Mathf.Min(1024, pendingTextureData.Length - startIndex);

        if (length <= 0) return; // No more data to send

        // Copy chunk data
        byte[] chunkData = new byte[length];
        System.Array.Copy(pendingTextureData, startIndex, chunkData, 0, length);

        // Send chunk to client
        SendTextureChunkClientRpc(chunkIndex, chunkData,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { serverRpcParams.Receive.SenderClientId }
                }
            });
    }

    [ClientRpc]
    private void SendTextureChunkClientRpc(int chunkIndex, byte[] chunkData, ClientRpcParams clientRpcParams = default)
    {
        // Copy chunk data to pending data
        int startIndex = chunkIndex * 1024;
        System.Array.Copy(chunkData, 0, pendingTextureData, startIndex, chunkData.Length);

        currentChunkIndex++;

        if (currentChunkIndex < totalChunks)
        {
            // Request next chunk
            RequestTextureChunkServerRpc(currentChunkIndex);
        }
        else
        {
            // All chunks received, apply the texture
            ApplyTextureData();
        }
    }

    private void ApplyTextureData()
    {
        // Trim the array to actual data size
        int actualSize = (currentChunkIndex - 1) * 1024 + pendingTextureData.Length % 1024;
        byte[] actualData = new byte[actualSize];
        System.Array.Copy(pendingTextureData, actualData, actualSize);

        // Load texture from PNG data
        if (syncedTexture == null)
        {
            syncedTexture = new Texture2D(2, 2);
        }

        syncedTexture.LoadImage(actualData);

        // Apply to renderer
        if (targetRenderer != null)
        {
            targetRenderer.material.mainTexture = syncedTexture;
        }

        // Clean up
        pendingTextureData = null;
    }
}