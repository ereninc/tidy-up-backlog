Shader "EXW/Fullscreen/Scene Sobel Outline"
{
    Properties
    {
        [HDR] _OutlineColor("Outline Color", Color) = (0.015, 0.02, 0.035, 0.45)
        _Thickness("Thickness (Pixels)", Range(1.0, 6.0)) = 2.0

        _DepthThreshold("Depth Threshold", Range(0.0001, 0.05)) = 0.004
        _DepthSoftness("Depth Softness", Range(0.0001, 0.05)) = 0.003
        _DepthWeight("Depth Weight", Range(0.0, 5.0)) = 1.0

        _NormalThreshold("Normal Threshold", Range(0.001, 1.0)) = 0.18
        _NormalSoftness("Normal Softness", Range(0.001, 1.0)) = 0.10
        _NormalWeight("Normal Weight", Range(0.0, 5.0)) = 1.0
        _NormalDepthGate("Normal Depth Gate", Range(0.001, 0.20)) = 0.03

        _FadeStart("Distance Fade Start", Float) = 20.0
        _FadeEnd("Distance Fade End", Float) = 80.0
        _FarIntensity("Far Outline Intensity", Range(0.0, 1.0)) = 0.35

        _Intensity("Intensity", Range(0.0, 1.0)) = 1.0
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
            Name "Scene Stylized Outline"

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
                float _NormalDepthGate;

                float _FadeStart;
                float _FadeEnd;
                float _FarIntensity;

                float _Intensity;
            CBUFFER_END

            bool IsBackgroundDepth(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return rawDepth <= 0.00001;
                #else
                    return rawDepth >= 0.99999;
                #endif
            }

            float RawToEyeDepth(float rawDepth)
            {
                return LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            // Bir komsu sample hem silhouette/depth hem de normal crease bilgisi toplar.
            // Depth tek tarafli hesaplanir: cizgi yakin objenin icinde kalir ve sky'a tasmaz.
            void AccumulateEdgeSample(
                float2 neighborUv,
                float centerEyeDepth,
                float3 centerNormal,
                inout float depthDifference,
                inout float normalDifference)
            {
                neighborUv = saturate(neighborUv);

                float neighborRawDepth = SampleSceneDepth(neighborUv);

                if (IsBackgroundDepth(neighborRawDepth))
                {
                    depthDifference = 1.0;
                    return;
                }

                float neighborEyeDepth = RawToEyeDepth(neighborRawDepth);
                float depthBase = max(min(centerEyeDepth, neighborEyeDepth), 0.05);
                float relativeDepthDifference =
                    abs(neighborEyeDepth - centerEyeDepth) / depthBase;

                // Sadece merkezden daha uzaktaki komsular foreground silhouette uretir.
                float inwardDepthDifference =
                    max(neighborEyeDepth - centerEyeDepth, 0.0) /
                    max(centerEyeDepth, 0.05);

                depthDifference = max(depthDifference, inwardDepthDifference);

                // Farkli derinlikteki objelerin normalleri birbirine karistirilmaz.
                // Silhouette depth tarafindan, ayni yuzeydeki kiriklar normal tarafindan bulunur.
                if (relativeDepthDifference <= _NormalDepthGate)
                {
                    float3 neighborNormal = SampleSceneNormals(neighborUv);
                    float neighborLengthSq = dot(neighborNormal, neighborNormal);

                    if (neighborLengthSq > 0.0001)
                    {
                        neighborNormal *= rsqrt(neighborLengthSq);
                        float normalDelta =
                            1.0 - saturate(dot(centerNormal, neighborNormal));

                        normalDifference = max(normalDifference, normalDelta);
                    }
                }
            }

            void GetEdgeDifferences(
                float2 uv,
                float2 pixelSize,
                float centerEyeDepth,
                float3 centerNormal,
                out float depthDifference,
                out float normalDifference)
            {
                depthDifference = 0.0;
                normalDifference = 0.0;

                float2 cardinal = pixelSize * _Thickness;
                float2 diagonal = cardinal * 0.70710678;

                // Sekiz yone thickness kadar bakmak, tek pass'te objenin icine dogru
                // dolu ve ekran-pikseli bazli bir outline bandi verir.
                AccumulateEdgeSample(uv + float2( cardinal.x, 0.0), centerEyeDepth, centerNormal, depthDifference, normalDifference);
                AccumulateEdgeSample(uv + float2(-cardinal.x, 0.0), centerEyeDepth, centerNormal, depthDifference, normalDifference);
                AccumulateEdgeSample(uv + float2(0.0,  cardinal.y), centerEyeDepth, centerNormal, depthDifference, normalDifference);
                AccumulateEdgeSample(uv + float2(0.0, -cardinal.y), centerEyeDepth, centerNormal, depthDifference, normalDifference);

                AccumulateEdgeSample(uv + float2( diagonal.x,  diagonal.y), centerEyeDepth, centerNormal, depthDifference, normalDifference);
                AccumulateEdgeSample(uv + float2(-diagonal.x,  diagonal.y), centerEyeDepth, centerNormal, depthDifference, normalDifference);
                AccumulateEdgeSample(uv + float2( diagonal.x, -diagonal.y), centerEyeDepth, centerNormal, depthDifference, normalDifference);
                AccumulateEdgeSample(uv + float2(-diagonal.x, -diagonal.y), centerEyeDepth, centerNormal, depthDifference, normalDifference);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 sceneColor = SAMPLE_TEXTURE2D_X(
                    _BlitTexture,
                    sampler_LinearClamp,
                    uv);

                float centerRawDepth = SampleSceneDepth(uv);

                // Background pixel'larini boyamayarak dis halo yerine inward outline uretiriz.
                if (IsBackgroundDepth(centerRawDepth))
                {
                    return sceneColor;
                }

                float centerEyeDepth = RawToEyeDepth(centerRawDepth);
                float3 centerNormal = SampleSceneNormals(uv);
                float centerNormalLengthSq = dot(centerNormal, centerNormal);

                if (centerNormalLengthSq > 0.0001)
                {
                    centerNormal *= rsqrt(centerNormalLengthSq);
                }
                else
                {
                    centerNormal = float3(0.0, 0.0, 1.0);
                }

                float depthDifference;
                float normalDifference;

                GetEdgeDifferences(
                    uv,
                    rcp(_ScaledScreenParams.xy),
                    centerEyeDepth,
                    centerNormal,
                    depthDifference,
                    normalDifference);

                depthDifference *= _DepthWeight;
                normalDifference *= _NormalWeight;

                float depthEdge = smoothstep(
                    _DepthThreshold,
                    _DepthThreshold + _DepthSoftness,
                    depthDifference);

                float normalEdge = smoothstep(
                    _NormalThreshold,
                    _NormalThreshold + _NormalSoftness,
                    normalDifference);

                float edge = max(depthEdge, normalEdge);

                // Uzakta ince geometri ve normal texture kaynakli shimmer'i yumusatir.
                float fadeRange = max(_FadeEnd - _FadeStart, 0.001);
                float fadeT = saturate((centerEyeDepth - _FadeStart) / fadeRange);
                float distanceMultiplier = lerp(1.0, _FarIntensity, fadeT);

                edge = saturate(
                    edge *
                    distanceMultiplier *
                    _Intensity *
                    _OutlineColor.a);

                sceneColor.rgb = lerp(sceneColor.rgb, _OutlineColor.rgb, edge);
                return sceneColor;
            }

            ENDHLSL
        }
    }

    FallBack Off
}
