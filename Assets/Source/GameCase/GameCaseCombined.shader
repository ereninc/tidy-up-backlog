Shader "EXW/Game Case Combined"
{
    Properties
    {
        _ShellColor(
            "Shell Color",
            Color
        ) = (0.02, 0.05, 0.18, 1)

        [NoScaleOffset]
        _ShellTexture(
            "Shell Texture",
            2D
        ) = "white" {}

        _CoverIndex(
            "Cover Index",
            Float
        ) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        /*
         * =========================================================
         * FORWARD
         * =========================================================
         */

        Pass
        {
            Name "Forward"

            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;

                float2 uv :
                    TEXCOORD0;

                // x:
                // 0 = shell
                // 1 = cover
                float2 surfaceData :
                    TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS :
                    SV_POSITION;

                float2 uv :
                    TEXCOORD0;

                float coverMask :
                    TEXCOORD1;
            };

            TEXTURE2D(
                _ShellTexture);

            SAMPLER(
                sampler_ShellTexture);

            TEXTURE2D_ARRAY(
                _GameCaseCoverArray);

            SAMPLER(
                sampler_GameCaseCoverArray);

            CBUFFER_START(UnityPerMaterial)

                float4 _ShellColor;

                float _CoverIndex;

            CBUFFER_END

            Varyings Vert(
                Attributes input)
            {
                Varyings output;

                VertexPositionInputs positions =
                    GetVertexPositionInputs(
                        input.positionOS.xyz);

                output.positionCS =
                    positions.positionCS;

                output.uv =
                    input.uv;

                output.coverMask =
                    saturate(
                        input.surfaceData.x);

                return output;
            }

            half4 Frag(
                Varyings input)
                : SV_Target
            {
                half4 shellTexture =
                    SAMPLE_TEXTURE2D(
                        _ShellTexture,
                        sampler_ShellTexture,
                        input.uv);

                half4 shell =
                    shellTexture *
                    _ShellColor;

                half4 cover =
                    SAMPLE_TEXTURE2D_ARRAY(
                        _GameCaseCoverArray,
                        sampler_GameCaseCoverArray,
                        input.uv,
                        _CoverIndex);

                return lerp(
                    shell,
                    cover,
                    saturate(
                        input.coverMask));
            }

            ENDHLSL
        }

        /*
         * =========================================================
         * DEPTH
         * =========================================================
         *
         * Fullscreen Sobel'in Camera Depth Texture'ında
         * case'leri görebilmesi için.
         */

        Pass
        {
            Name "DepthOnly"

            Tags
            {
                "LightMode" = "DepthOnly"
            }

            Cull Back
            ZWrite On
            ZTest LEqual

            ColorMask 0

            HLSLPROGRAM

            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttributes
            {
                float4 positionOS :
                    POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS :
                    SV_POSITION;
            };

            DepthVaryings DepthVert(
                DepthAttributes input)
            {
                DepthVaryings output;

                output.positionCS =
                    TransformObjectToHClip(
                        input.positionOS.xyz);

                return output;
            }

            half4 DepthFrag(
                DepthVaryings input)
                : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }

        /*
         * =========================================================
         * DEPTH NORMALS
         * =========================================================
         *
         * Fullscreen Sobel'in Camera Normals Texture'ında
         * case'leri görebilmesi için.
         */

        Pass
        {
            Name "DepthNormals"

            Tags
            {
                "LightMode" = "DepthNormals"
            }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthNormalsAttributes
            {
                float4 positionOS :
                    POSITION;

                float3 normalOS :
                    NORMAL;
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS :
                    SV_POSITION;

                float3 normalWS :
                    TEXCOORD0;
            };

            DepthNormalsVaryings
                DepthNormalsVert(
                    DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output;

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(
                        input.positionOS.xyz);

                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(
                        input.normalOS);

                output.positionCS =
                    positionInputs.positionCS;

                output.normalWS =
                    normalInputs.normalWS;

                return output;
            }

            half4 DepthNormalsFrag(
                DepthNormalsVaryings input)
                : SV_Target
            {
                float3 normalWS =
                    normalize(
                        input.normalWS);

                /*
                 * Normal buffer output.
                 */
                return half4(
                    normalWS * 0.5 + 0.5,
                    1.0);
            }

            ENDHLSL
        }
    }

    FallBack Off
}