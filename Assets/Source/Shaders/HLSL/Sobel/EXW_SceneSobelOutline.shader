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

            /*
             * ---------------------------------------------------------
             * DEPTH HELPERS
             * ---------------------------------------------------------
             */

            float GetRawDepth(float2 uv)
            {
                return SampleSceneDepth(uv);
            }

            bool IsBackgroundDepth(float rawDepth)
            {
                /*
                 * Unity reversed-Z:
                 *
                 * Near = 1
                 * Far / sky = 0
                 *
                 * Non reversed-Z:
                 *
                 * Near = 0
                 * Far / sky = 1
                 */
                #if UNITY_REVERSED_Z

                    return rawDepth <= 0.00001;

                #else

                    return rawDepth >= 0.99999;

                #endif
            }

            float GetEyeDepth(float2 uv)
            {
                float rawDepth =
                    GetRawDepth(uv);

                return LinearEyeDepth(
                    rawDepth,
                    _ZBufferParams);
            }

            /*
             * ---------------------------------------------------------
             * DEPTH SOBEL
             * ---------------------------------------------------------
             */

            float GetDepthEdge(
                float2 uv,
                float2 texel)
            {
                float d00 =
                    GetEyeDepth(
                        uv + texel * float2(-1, 1));

                float d10 =
                    GetEyeDepth(
                        uv + texel * float2(0, 1));

                float d20 =
                    GetEyeDepth(
                        uv + texel * float2(1, 1));

                float d01 =
                    GetEyeDepth(
                        uv + texel * float2(-1, 0));

                float d21 =
                    GetEyeDepth(
                        uv + texel * float2(1, 0));

                float d02 =
                    GetEyeDepth(
                        uv + texel * float2(-1, -1));

                float d12 =
                    GetEyeDepth(
                        uv + texel * float2(0, -1));

                float d22 =
                    GetEyeDepth(
                        uv + texel * float2(1, -1));

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

            /*
             * ---------------------------------------------------------
             * NORMAL EDGE
             * ---------------------------------------------------------
             */

            float GetNormalEdge(
                float2 uv,
                float2 texel)
            {
                /*
                 * Çok önemli:
                 *
                 * Sky/background'ın normal texture değeri valid geometry
                 * normal'i değil.
                 *
                 * Background üzerinde normal edge hesaplamazsak
                 * bütün sky'ın outlineColor ile boyanmasını engelleriz.
                 */
                float centerRawDepth =
                    GetRawDepth(uv);

                if (IsBackgroundDepth(
                        centerRawDepth))
                {
                    return 0.0;
                }

                float3 center =
                    SampleSceneNormals(uv);

                float centerLengthSq =
                    dot(
                        center,
                        center);

                if (centerLengthSq <
                    0.0001)
                {
                    return 0.0;
                }

                center =
                    normalize(center);

                float edge =
                    0.0;

                /*
                 * LEFT
                 */

                float2 leftUv =
                    uv +
                    texel *
                    float2(-1, 0);

                float leftDepth =
                    GetRawDepth(leftUv);

                if (!IsBackgroundDepth(
                        leftDepth))
                {
                    float3 left =
                        SampleSceneNormals(
                            leftUv);

                    float leftLengthSq =
                        dot(left, left);

                    if (leftLengthSq >
                        0.0001)
                    {
                        left =
                            normalize(left);

                        edge =
                            max(
                                edge,
                                1.0 -
                                saturate(
                                    dot(
                                        center,
                                        left)));
                    }
                }

                /*
                 * RIGHT
                 */

                float2 rightUv =
                    uv +
                    texel *
                    float2(1, 0);

                float rightDepth =
                    GetRawDepth(rightUv);

                if (!IsBackgroundDepth(
                        rightDepth))
                {
                    float3 right =
                        SampleSceneNormals(
                            rightUv);

                    float rightLengthSq =
                        dot(right, right);

                    if (rightLengthSq >
                        0.0001)
                    {
                        right =
                            normalize(right);

                        edge =
                            max(
                                edge,
                                1.0 -
                                saturate(
                                    dot(
                                        center,
                                        right)));
                    }
                }

                /*
                 * UP
                 */

                float2 upUv =
                    uv +
                    texel *
                    float2(0, 1);

                float upDepth =
                    GetRawDepth(upUv);

                if (!IsBackgroundDepth(
                        upDepth))
                {
                    float3 up =
                        SampleSceneNormals(
                            upUv);

                    float upLengthSq =
                        dot(up, up);

                    if (upLengthSq >
                        0.0001)
                    {
                        up =
                            normalize(up);

                        edge =
                            max(
                                edge,
                                1.0 -
                                saturate(
                                    dot(
                                        center,
                                        up)));
                    }
                }

                /*
                 * DOWN
                 */

                float2 downUv =
                    uv +
                    texel *
                    float2(0, -1);

                float downDepth =
                    GetRawDepth(downUv);

                if (!IsBackgroundDepth(
                        downDepth))
                {
                    float3 down =
                        SampleSceneNormals(
                            downUv);

                    float downLengthSq =
                        dot(down, down);

                    if (downLengthSq >
                        0.0001)
                    {
                        down =
                            normalize(down);

                        edge =
                            max(
                                edge,
                                1.0 -
                                saturate(
                                    dot(
                                        center,
                                        down)));
                    }
                }

                return edge;
            }

            /*
             * ---------------------------------------------------------
             * FRAGMENT
             * ---------------------------------------------------------
             */

            half4 Frag(
                Varyings input
            ) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(
                    input);

                float2 uv =
                    input.texcoord;

                half4 sceneColor =
                    SAMPLE_TEXTURE2D_X(
                        _BlitTexture,
                        sampler_LinearClamp,
                        uv);

                /*
                 * Eğer şu anki pixel tamamen background/sky ise
                 * normal detection çalışmayacak.
                 *
                 * Fakat geometry'nin sky ile birleştiği sınır
                 * depth Sobel tarafından hâlâ bulunabilir.
                 */

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