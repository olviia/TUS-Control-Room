Shader "Custom/BlendTwoTextures"
{
    Properties
    {
        _MainTex ("Base Texture", 2D) = "white" {}
        _OverlayTex ("Overlay Texture", 2D) = "white" {}
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            sampler2D _MainTex;
            sampler2D _OverlayTex;
            
            fixed4 frag (v2f_img i) : SV_Target
            {
                fixed4 base = tex2D(_MainTex, i.uv);
                fixed4 overlay = tex2D(_OverlayTex, i.uv);
                
                // Alpha blend: Result = Base * (1 - OverlayAlpha) + Overlay * OverlayAlpha
                fixed4 result = base * (1.0 - overlay.a) + overlay * overlay.a;
                result.a = max(base.a, overlay.a); // Preserve maximum alpha
                
                return result;
            }
            ENDCG
        }
    }
}