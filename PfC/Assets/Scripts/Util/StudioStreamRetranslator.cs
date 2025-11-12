using UnityEngine;

public class StudioStreamRetranslator : MonoBehaviour
{
    public MeshRenderer studioMesh;
    public MeshRenderer studioCaptionsMesh;

    private MeshRenderer mesh;
    private MaterialPropertyBlock studioBlock;
    private MaterialPropertyBlock captionsBlock;
    private MaterialPropertyBlock meshBlock;

    private Texture studioTexture;
    private Texture captionsTexture;

    private RenderTexture compositeRT;

    private Material blendMaterial;
    private string textureName = "_BaseMap";

    private void Start()
    {
        mesh = GetComponent<MeshRenderer>();
        
        studioBlock = new MaterialPropertyBlock();
        captionsBlock = new MaterialPropertyBlock();
        meshBlock = new MaterialPropertyBlock();
        
        blendMaterial = new Material(Shader.Find("Custom/BlendTwoTextures"));

    }

    // Update is called once per frame
    void Update()
    {
        AssignBlitMaterialPropertyBlock();
    }

    private void AssignBlitMaterialPropertyBlock()
    {
        if (studioMesh == null || !studioMesh.HasPropertyBlock())
            return;

        studioMesh.GetPropertyBlock(studioBlock);
        studioTexture = studioBlock.GetTexture(textureName);

        if (studioTexture == null)
            return;

        // Check if captions mesh is assigned and has valid texture
        bool hasCaptions = studioCaptionsMesh != null && studioCaptionsMesh.HasPropertyBlock();

        if (hasCaptions)
        {
            studioCaptionsMesh.GetPropertyBlock(captionsBlock);
            captionsTexture = captionsBlock.GetTexture(textureName);

            if (captionsTexture != null)
            {
                // Create composite render texture if needed
                if (compositeRT == null)
                {
                    compositeRT = new RenderTexture(studioTexture.width, studioTexture.height, depth: 0);
                    compositeRT.Create();
                }

                // Blend studio texture with captions
                blendMaterial.SetTexture("_MainTex", studioTexture);
                blendMaterial.SetTexture("_OverlayTex", captionsTexture);
                Graphics.Blit(null, compositeRT, blendMaterial);

                // Apply composite to mesh
                meshBlock.SetTexture(textureName, compositeRT);
                mesh.SetPropertyBlock(meshBlock);
                return;
            }
        }

        // No captions or captions texture invalid - use studio texture directly
        if (compositeRT != null)
        {
            compositeRT.Release();
            compositeRT = null;
        }

        meshBlock.SetTexture(textureName, studioTexture);
        mesh.SetPropertyBlock(meshBlock);
    }
}
