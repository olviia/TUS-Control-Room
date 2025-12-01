//
// URP (Universal Render Pipeline) version of PointCloudTextured shader
// Converted from Built-in Pipeline for Unity 6
//
Shader "URP/cwipc/PointCloudTextured"
{
    Properties 
    {
        _Tint("Tint", Color) = (0.5, 0.5, 0.5, 1)
        _PointSize("Point Size", Float) = 0.05
        _PointSizeFactor("Point Size multiply", Float) = 1.0
        _MainTex("Texture", 2D) = "white" {}
        _Cutoff("Alpha cutoff", Range(0,1)) = 0.5
        _OverridePointSize("Override Point Size", Float) = 0.0
    }

    SubShader 
    {
        Tags 
        { 
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        // Fallback pass without geometry shader - ONLY PASS
        Pass 
        {
            Name "PointCloudSimple"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertexSimple
            #pragma fragment FragmentSimple

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct AttributesSimple
            {
                uint vertexID : SV_VertexID;
            };

            struct VaryingsSimple 
            {
                float4 positionCS : SV_Position;
                half4 color : COLOR;
                float psize : PSIZE;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float4x4 _Transform;
                half _PointSize;
                half _PointSizeFactor;
                half _Cutoff;
                half _OverridePointSize;
            CBUFFER_END

            StructuredBuffer<float4> _PointBuffer;

            half3 PcxDecodeColor(uint data) 
            {
                half r = (data >> 0) & 0xff;
                half g = (data >> 8) & 0xff;
                half b = (data >> 16) & 0xff;
                return half3(r, g, b) / 255.0;
            }

            VaryingsSimple VertexSimple(AttributesSimple input) 
            {
                float4 pt = _PointBuffer[input.vertexID];
                float4 positionWS = mul(_Transform, float4(pt.xyz, 1.0));
                half4 col = half4(PcxDecodeColor(asuint(pt.w)), _Tint.a);

                #ifdef UNITY_COLORSPACE_GAMMA
                    col.rgb *= _Tint.rgb * 2.0;
                #else
                    col.rgb *= _Tint.rgb * 2.0;
                #endif

                VaryingsSimple output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = col;
                
                float pixelsPerMeter = _ScreenParams.y / output.positionCS.w;
                if (_OverridePointSize == 0) 
                {
                    output.psize = _PointSize * _PointSizeFactor * pixelsPerMeter;
                }
                else 
                {
                    output.psize = _OverridePointSize;
                }
                
                return output;
            }

            half4 FragmentSimple(VaryingsSimple input) : SV_Target 
            {
                return input.color;
            }

            ENDHLSL
        }
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
