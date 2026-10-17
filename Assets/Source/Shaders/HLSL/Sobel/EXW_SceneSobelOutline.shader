Shader "EXW/Fullscreen/Scene Sobel Outline"
{
    Properties
    {
        [HDR] _OutlineColor("Outline Color", Color) = (0.015, 0.02, 0.035, 0.45)
        _Thickness("Thickness (Pixels)", Range(1.0, 6.0)) = 2.0

        _DepthThreshold("Depth Threshold", Range(0.0001, 0.05)) = 0.004
        _DepthSoftness("Depth Softness", Range(0.0001, 0.05)) = 0.003
        _DepthWeight("Depth Weight", Range(0.0, 5.0)) = 1.0

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

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _Thickness;

                float _DepthThreshold;
                float _DepthSoftness;
                float _DepthWeight;

                float _FadeStart;
                float _FadeEnd;
                float _FarIntensity;

                float _Intensity;
            CBUFFER_END

            // Camera-scoped opaque UI coverage. UI does not replace scene depth.
            float4 _EXW_SobelUiRect;
            float4 _EXW_SobelUiOpacity;

            float GetVisibleSceneAmount(float2 pixelPosition)
            {
                float matteVisibility = 1.0 - saturate(_EXW_SobelUiOpacity.y);
                float imageOpacity = saturate(_EXW_SobelUiOpacity.x);
                if (matteVisibility <= 0.0001 || imageOpacity <= 0.0)
                {
                    return matteVisibility;
                }

                float2 screenUv = pixelPosition / _ScaledScreenParams.xy;
                #if UNITY_UV_STARTS_AT_TOP
                    screenUv.y = 1.0 - screenUv.y;
                #endif

                float insideUi = all(screenUv >= _EXW_SobelUiRect.xy) &&
                                 all(screenUv <= _EXW_SobelUiRect.zw) ? 1.0 : 0.0;
                return (1.0 - imageOpacity * insideUi) * matteVisibility;
            }

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

            // Yalnızca merkezin arkasındaki komşular kenar üretir.
            // Böylece çizgi objenin içinde kalır, gökyüzüne taşmaz.
            void AccumulateEdgeSample(
                float2 neighborUv,
                float centerEyeDepth,
                inout float depthDifference)
            {
                float neighborRawDepth = SampleSceneDepth(saturate(neighborUv));

                if (IsBackgroundDepth(neighborRawDepth))
                {
                    depthDifference = 1.0;
                    return;
                }

                float neighborEyeDepth = RawToEyeDepth(neighborRawDepth);

                float inwardDepthDifference =
                    max(neighborEyeDepth - centerEyeDepth, 0.0) /
                    max(centerEyeDepth, 0.05);

                depthDifference = max(
                    depthDifference,
                    inwardDepthDifference);
            }

            float GetDepthDifference(
                float2 uv,
                float2 pixelSize,
                float centerEyeDepth)
            {
                float depthDifference = 0.0;

                float2 cardinal = pixelSize * _Thickness;
                float2 diagonal = cardinal * 0.70710678;

                AccumulateEdgeSample(
                    uv + float2( cardinal.x, 0.0),
                    centerEyeDepth, depthDifference);

                AccumulateEdgeSample(
                    uv + float2(-cardinal.x, 0.0),
                    centerEyeDepth, depthDifference);

                AccumulateEdgeSample(
                    uv + float2(0.0,  cardinal.y),
                    centerEyeDepth, depthDifference);

                AccumulateEdgeSample(
                    uv + float2(0.0, -cardinal.y),
                    centerEyeDepth, depthDifference);

                AccumulateEdgeSample(
                    uv + float2( diagonal.x,  diagonal.y),
                    centerEyeDepth, depthDifference);

                AccumulateEdgeSample(
                    uv + float2(-diagonal.x,  diagonal.y),
                    centerEyeDepth, depthDifference);

                AccumulateEdgeSample(
                    uv + float2( diagonal.x, -diagonal.y),
                    centerEyeDepth, depthDifference);

                AccumulateEdgeSample(
                    uv + float2(-diagonal.x, -diagonal.y),
                    centerEyeDepth, depthDifference);

                return depthDifference;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

                half4 sceneColor = SAMPLE_TEXTURE2D_X(
                    _BlitTexture,
                    sampler_LinearClamp,
                    uv);

                float visibleScene = GetVisibleSceneAmount(input.positionCS.xy);
                if (visibleScene <= 0.0001)
                {
                    return sceneColor;
                }

                float centerRawDepth = SampleSceneDepth(uv);

                if (IsBackgroundDepth(centerRawDepth))
                {
                    return sceneColor;
                }

                float centerEyeDepth = RawToEyeDepth(centerRawDepth);

                float depthDifference = GetDepthDifference(
                    uv,
                    rcp(_ScaledScreenParams.xy),
                    centerEyeDepth);

                float edge = smoothstep(
                    _DepthThreshold,
                    _DepthThreshold + _DepthSoftness,
                    depthDifference * _DepthWeight);

                float fadeRange = max(
                    _FadeEnd - _FadeStart,
                    0.001);

                float fadeT = saturate(
                    (centerEyeDepth - _FadeStart) / fadeRange);

                float distanceMultiplier = lerp(
                    1.0,
                    _FarIntensity,
                    fadeT);

                edge = saturate(
                    edge *
                    visibleScene *
                    distanceMultiplier *
                    _Intensity *
                    _OutlineColor.a);

                sceneColor.rgb = lerp(
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
