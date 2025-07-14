using Klak.Ndi;
using UnityEngine;

public class StudioStreamRetranslator : MonoBehaviour
{
    public MeshRenderer studioMesh;

    public MeshRenderer studioCaptionsMesh;
    public NdiReceiver studioNdiReceiver;
    private MeshRenderer mesh;
    private MaterialPropertyBlock studioBlock;
    private MaterialPropertyBlock captionsBlock;
    private MaterialPropertyBlock meshBlock;

    private Texture texture;
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

    
        if(studioMesh.HasPropertyBlock()) studioMesh.GetPropertyBlock(studioBlock);
        if (studioBlock.GetTexture(textureName) != null)
        {
            studioTexture = studioBlock.GetTexture(textureName);
            meshBlock.SetTexture(textureName, studioTexture);
            mesh.SetPropertyBlock(meshBlock);

            if (/*FindAnyObjectByType<StreamManager>().isStreaming && */studioCaptionsMesh.HasPropertyBlock())
            { 
                studioCaptionsMesh.GetPropertyBlock(captionsBlock);
                
                captionsTexture = captionsBlock.GetTexture(textureName);
                if (compositeRT == null)
                {
                    compositeRT = new RenderTexture(studioTexture.width, studioTexture.height, depth: 0);
                    compositeRT.Create();
                }
                if (captionsTexture != null) 
                {
                    blendMaterial.SetTexture("_MainTex", studioTexture);
                    blendMaterial.SetTexture("_OverlayTex", captionsTexture);

                    Graphics.Blit(null, compositeRT, blendMaterial);

                    // Apply composite to mesh
                    meshBlock.SetTexture(textureName, compositeRT);
                    mesh.SetPropertyBlock(meshBlock);
                }            
            }
            else
            {
                if (compositeRT != null) compositeRT.Release();
                compositeRT = null;
            }
        }
    }
}
