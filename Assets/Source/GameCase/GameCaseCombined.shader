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
                float2 uv : TEXCOORD0;

                // x:
                // 0 = plastic shell
                // 1 = cover artwork
                float2 surfaceData : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float coverMask : TEXCOORD1;
            };

            TEXTURE2D(_ShellTexture);
            SAMPLER(sampler_ShellTexture);

            TEXTURE2D_ARRAY(_GameCaseCoverArray);
            SAMPLER(sampler_GameCaseCoverArray);

            /*
             * Bunlar MaterialPropertyBlock ile renderer başına
             * override ediliyor.
             *
             * Şimdilik GPU instancing path'ine sokmuyoruz.
             */
            CBUFFER_START(UnityPerMaterial)

                float4 _ShellColor;
                float _CoverIndex;

            CBUFFER_END

            Varyings Vert(Attributes input)
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

            half4 Frag(Varyings input)
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
                    saturate(input.coverMask));
            }

            ENDHLSL
        }
    }

    FallBack Off
}