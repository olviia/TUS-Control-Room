Shader "Unlit/mesh_multi-view-0"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha // Enable transparency
            ZWrite On // Disable depth writing for proper blending
            Cull Back  // Disables culling so both sides are rendered

            CGPROGRAM
            #pragma vertex VS_Main
            #pragma geometry GS_Main
            #pragma fragment FS_Main
            #include "UnityCG.cginc"

            #define MAX_CAMERAS 10

            // Uniforms
            uniform int CameraNumber;
            uniform float4x4 CameraViewMatrix[MAX_CAMERAS];   // World-to-Camera
            uniform float4x4 DepthToColorMatrix[MAX_CAMERAS]; // Depth-to-Color
            uniform float4x4 ColorIntrinsics[MAX_CAMERAS];    // Intrinsics
            uniform float4 ColorResolution[MAX_CAMERAS];      // Color image size
            uniform float4 CameraPositions[MAX_CAMERAS];      // Camera positions

            uniform sampler2D CameraTextures0;
            uniform sampler2D CameraTextures1;
            uniform sampler2D CameraTextures2;
            uniform sampler2D CameraTextures3;
            uniform sampler2D CameraTextures4;
            uniform sampler2D CameraTextures5;
            uniform sampler2D CameraTextures6;
            uniform sampler2D CameraTextures7;
            uniform sampler2D CameraTextures8;
            uniform sampler2D CameraTextures9;

            // Distortion Parameters
            uniform float4 RadialDist123[MAX_CAMERAS];
            uniform float4 RadialDist456[MAX_CAMERAS];
            uniform float4 TangentialDist[MAX_CAMERAS];

            struct VS_Input {
                float4 position : POSITION;
                float3 normal : NORMAL;
            };

            struct GS_Output {
                float4 clipPos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 vertex : TEXCOORD2;
                float3 vert_normal: TEXCOORD3;
            };

            struct FS_Input {
                float4 clipPos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 vertex : TEXCOORD2;
                float3 vert_normal: TEXCOORD3;
            };

            // Vertex Shader
            GS_Output VS_Main(VS_Input IN)
            {
                GS_Output OUT;
                OUT.clipPos = UnityObjectToClipPos(IN.position);
                OUT.worldPos = mul(unity_ObjectToWorld, IN.position).xyz;
                OUT.normal = normalize(mul((float3x3)unity_ObjectToWorld, IN.normal));
                OUT.vertex = IN.position;
                OUT.vert_normal = IN.normal;
                return OUT;
            }

            // Geometry Shader
            [maxvertexcount(3)]
            void GS_Main(triangle GS_Output IN[3], inout TriangleStream<FS_Input> triStream)
            {
                FS_Input OUT;
                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    OUT.clipPos = IN[i].clipPos;
                    OUT.worldPos = IN[i].worldPos;
                    OUT.normal = IN[i].normal;
                    OUT.vertex = IN[i].vertex;
                    OUT.vert_normal = IN[i].vert_normal;
                    triStream.Append(OUT);
                }
            }

            // Fragment Shader
            float4 FS_Main(FS_Input IN) : SV_Target
            {
                float maxWeight = -1.0;
                int maxWeightIndex = -1;
                float3 maxSampledColor = float3(0.0,0.0,0.0);

                for (int i = 0; i < CameraNumber; i++)
                {
                    // Transform world position to camera space
                    float4 cameraPos = mul(CameraViewMatrix[i], float4(IN.vertex, 1.0));

                    // Transform from depth space to color space
                    float4 depthPos = mul(DepthToColorMatrix[i], cameraPos);
                    depthPos /= depthPos.z;  // Normalize

                    // Apply distortion
                    float x_undist = depthPos.x;
                    float y_undist = depthPos.y;
                    float r2 = x_undist * x_undist + y_undist * y_undist;
                    float r4 = r2 * r2;
                    float r6 = r2 * r4;

                    float3 radial123 = RadialDist123[i].xyz;
                    float3 radial456 = RadialDist456[i].xyz;
                    float2 tangential = TangentialDist[i].xy;

                    float a = 1.0 + radial456.x * r2 + radial456.y * r4 + radial456.z * r6;
                    float b = 1.0 + radial123.x * r2 + radial123.y * r4 + radial123.z * r6;

                    float delta_x = 2.0 * tangential.x * x_undist * y_undist + tangential.y * (r2 + 2.0 * x_undist * x_undist);
                    float delta_y = tangential.x * (r2 + 2.0 * y_undist * y_undist) + 2.0 * tangential.y * x_undist * y_undist;

                    float2 distortedUV;
                    distortedUV.x = x_undist * b / a + delta_x;
                    distortedUV.y = y_undist * b / a + delta_y;

                    // Convert to color image space
                    float4 xy_color_dist = mul(ColorIntrinsics[i], float4(distortedUV, 1.0, depthPos.w));
                    float2 uv = xy_color_dist.xy / ColorResolution[i].xy;

                    // Check if UV is valid
                    if (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0)
                    {
                        float3 sampledColor;
                        switch (i)
                        {
                            case 0: sampledColor = tex2D(CameraTextures0, uv).bgr; break;
                            case 1: sampledColor = tex2D(CameraTextures1, uv).bgr; break;
                            case 2: sampledColor = tex2D(CameraTextures2, uv).bgr; break;
                            case 3: sampledColor = tex2D(CameraTextures3, uv).bgr; break;
                            case 4: sampledColor = tex2D(CameraTextures4, uv).bgr; break;
                            case 5: sampledColor = tex2D(CameraTextures5, uv).bgr; break;
                            case 6: sampledColor = tex2D(CameraTextures6, uv).bgr; break;
                            case 7: sampledColor = tex2D(CameraTextures7, uv).bgr; break;
                            case 8: sampledColor = tex2D(CameraTextures8, uv).bgr; break;
                            case 9: sampledColor = tex2D(CameraTextures9, uv).bgr; break;
                        }

                        // Compute weight based on normal and view direction
                        float3 viewDir = normalize(CameraPositions[i].xyz - IN.vertex);
                        float weight = max(0.0, dot(viewDir, IN.vert_normal));

                        // Update the max weight and corresponding view
                        if (weight > maxWeight)
                        {
                            maxWeight = weight;
                            maxWeightIndex = i;
                            maxSampledColor = sampledColor;
                        }
                    }
                    else
                    {
                        return float4(0.0,1.0,0.0,0.0);
                    }
                }

                // Normalize final color
                if (maxWeight > 0.0)
                {
                    return float4(maxSampledColor, 1.0);
                }
                else
                {
                    return float4(maxSampledColor, 0.0);
                }

            }

            ENDCG
        }
    }
}
