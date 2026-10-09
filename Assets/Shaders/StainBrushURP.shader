Shader "Hidden/StainBrushURP"
{
    Properties
    {
        _MainTex ("Base", 2D) = "black" {}
        _BrushTex ("Brush", 2D) = "white" {}
        _BrushPos ("Brush Pos (uv)", Vector) = (0.5,0.5,0,0)
        _BrushRadius ("Radius", Float) = 0.1
        _BrushStrength ("Strength", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_BrushTex);
            SAMPLER(sampler_BrushTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BrushPos;
                float _BrushRadius;
                float _BrushStrength;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // _MainTex автоматически устанавливается Graphics.Blit на источник
                float existing = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).r;

                // UV кисти
                float2 bUV = (IN.uv - _BrushPos.xy) / max(_BrushRadius, 1e-5) + 0.5;

                float brush = 0;
                if (bUV.x >= 0 && bUV.x <= 1 && bUV.y >= 0 && bUV.y <= 1)
                    brush = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex, bUV).r;

                // Накопление через сложение
                float result = saturate(existing + brush * _BrushStrength);

                return half4(result, result, result, 1);
            }
            ENDHLSL
        }
    }
}