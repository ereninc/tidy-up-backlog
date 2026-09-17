Shader "EXW/Fullscreen/Scene Sobel Outline"
{
    Properties
    {
        [HDR]
        _OutlineColor(
            "Outline Color",
            Color
        ) = (0.015, 0.02, 0.035, 0.45)

        _Thickness(
            "Thickness",
            Range(0.5, 4.0)
        ) = 1.0

        _DepthThreshold(
            "Depth Threshold",
            Range(0.0001, 0.05)
        ) = 0.004

        _DepthSoftness(
            "Depth Softness",
            Range(0.0001, 0.05)
        ) = 0.003

        _DepthWeight(
            "Depth Weight",
            Range(0.0, 5.0)
        ) = 1.0

        _NormalThreshold(
            "Normal Threshold",
            Range(0.001, 1.0)
        ) = 0.18

        _NormalSoftness(
            "Normal Softness",
            Range(0.001, 1.0)
        ) = 0.10

        _NormalWeight(
            "Normal Weight",
            Range(0.0, 5.0)
        ) = 1.0

        _Intensity(
            "Intensity",
            Range(0.0, 1.0)
        ) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Name "Scene Sobel Outline"

            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM

            #pragma target 3.5

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)

                float4 _OutlineColor;

                float _Thickness;

                float _DepthThreshold;
                float _DepthSoftness;
                float _DepthWeight;

                float _NormalThreshold;
                float _NormalSoftness;
                float _NormalWeight;

                float _Intensity;

            CBUFFER_END

            float GetEyeDepth(float2 uv)
            {
                float rawDepth =
                    SampleSceneDepth(uv);

                return LinearEyeDepth(
                    rawDepth,
                    _ZBufferParams);
            }

            float GetDepthEdge(
                float2 uv,
                float2 texel)
            {
                /*
                 * 3x3 Sobel kernel.
                 *
                 * d00 d10 d20
                 * d01  C  d21
                 * d02 d12 d22
                 */

                float d00 =
                    GetEyeDepth(
                        uv + texel * float2(-1,  1));

                float d10 =
                    GetEyeDepth(
                        uv + texel * float2( 0,  1));

                float d20 =
                    GetEyeDepth(
                        uv + texel * float2( 1,  1));

                float d01 =
                    GetEyeDepth(
                        uv + texel * float2(-1,  0));

                float d21 =
                    GetEyeDepth(
                        uv + texel * float2( 1,  0));

                float d02 =
                    GetEyeDepth(
                        uv + texel * float2(-1, -1));

                float d12 =
                    GetEyeDepth(
                        uv + texel * float2( 0, -1));

                float d22 =
                    GetEyeDepth(
                        uv + texel * float2( 1, -1));

                float gx =
                    -d00 +
                     d20 +
                    -2.0 * d01 +
                     2.0 * d21 +
                    -d02 +
                     d22;

                float gy =
                     d00 +
                     2.0 * d10 +
                     d20 +
                    -d02 +
                    -2.0 * d12 +
                    -d22;

                float gradient =
                    sqrt(
                        gx * gx +
                        gy * gy);

                /*
                 * Normalize by nearest sampled depth.
                 *
                 * Böylece 2 metre ve 30 metre mesafedeki
                 * objeler tamamen farklı threshold istemiyor.
                 */
                float nearestDepth =
                    min(
                        min(
                            min(d00, d10),
                            min(d20, d01)),
                        min(
                            min(d21, d02),
                            min(d12, d22)));

                nearestDepth =
                    max(
                        nearestDepth,
                        0.05);

                return
                    gradient /
                    nearestDepth;
            }

            float GetNormalEdge(
                float2 uv,
                float2 texel)
            {
                float3 center =
                    normalize(
                        SampleSceneNormals(uv));

                float3 left =
                    normalize(
                        SampleSceneNormals(
                            uv +
                            texel *
                            float2(-1, 0)));

                float3 right =
                    normalize(
                        SampleSceneNormals(
                            uv +
                            texel *
                            float2(1, 0)));

                float3 up =
                    normalize(
                        SampleSceneNormals(
                            uv +
                            texel *
                            float2(0, 1)));

                float3 down =
                    normalize(
                        SampleSceneNormals(
                            uv +
                            texel *
                            float2(0, -1)));

                float leftDifference =
                    1.0 -
                    saturate(
                        dot(
                            center,
                            left));

                float rightDifference =
                    1.0 -
                    saturate(
                        dot(
                            center,
                            right));

                float upDifference =
                    1.0 -
                    saturate(
                        dot(
                            center,
                            up));

                float downDifference =
                    1.0 -
                    saturate(
                        dot(
                            center,
                            down));

                return max(
                    max(
                        leftDifference,
                        rightDifference),
                    max(
                        upDifference,
                        downDifference));
            }

            half4 Frag(
                Varyings input
            ) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(
                    input);

                float2 uv =
                    input.texcoord;

                float2 texel =
                    (
                        1.0 /
                        _ScaledScreenParams.xy
                    ) *
                    _Thickness;

                float depthDifference =
                    GetDepthEdge(
                        uv,
                        texel) *
                    _DepthWeight;

                float normalDifference =
                    GetNormalEdge(
                        uv,
                        texel) *
                    _NormalWeight;

                float depthEdge =
                    smoothstep(
                        _DepthThreshold,
                        _DepthThreshold +
                        _DepthSoftness,
                        depthDifference);

                float normalEdge =
                    smoothstep(
                        _NormalThreshold,
                        _NormalThreshold +
                        _NormalSoftness,
                        normalDifference);

                float edge =
                    max(
                        depthEdge,
                        normalEdge);

                edge =
                    saturate(
                        edge *
                        _Intensity *
                        _OutlineColor.a);

                half4 sceneColor =
                    SAMPLE_TEXTURE2D_X(
                        _BlitTexture,
                        sampler_LinearClamp,
                        uv);

                sceneColor.rgb =
                    lerp(
                        sceneColor.rgb,
                        _OutlineColor.rgb,
                        edge);

                return sceneColor;
            }

            ENDHLSL
        }
    }

    FallBack Off
}