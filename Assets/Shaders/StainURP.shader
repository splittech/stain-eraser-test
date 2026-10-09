Shader "Custom/StainURP"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
        _EraseMask ("Erase Mask", 2D) = "black" {}
        _EraseCutoff ("Erase Cutoff", Range(0,1)) = 0.3
        _EdgeSoftness ("Edge Softness", Range(0.001,0.3)) = 0.02

        [Header(Bounds)]
        _BoundsMin ("Bounds Min", Vector) = (-0.5,-0.5,-0.5,0)
        _BoundsSize ("Bounds Size", Vector) = (1,1,1,0)
        _PlaneAxis ("Plane Axis (0=XY, 1=XZ)", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

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
                float3 positionOS : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EraseMask);
            SAMPLER(sampler_EraseMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _EraseCutoff;
                float _EdgeSoftness;
                float4 _BoundsMin;
                float4 _BoundsSize;
                float _PlaneAxis;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.positionOS = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                // UV маски считаем из локальной позиции — совпадает с C# WorldToMaskUV
                float u, v;
                if (_PlaneAxis < 0.5)
                {
                    u = (IN.positionOS.x - _BoundsMin.x) / max(_BoundsSize.x, 1e-5);
                    v = (IN.positionOS.y - _BoundsMin.y) / max(_BoundsSize.y, 1e-5);
                }
                else
                {
                    u = (IN.positionOS.x - _BoundsMin.x) / max(_BoundsSize.x, 1e-5);
                    v = (IN.positionOS.z - _BoundsMin.z) / max(_BoundsSize.z, 1e-5);
                }

                float mask = SAMPLE_TEXTURE2D(_EraseMask, sampler_EraseMask, float2(u, v)).r;

                float eraseAmount = smoothstep(_EraseCutoff - _EdgeSoftness,
                                               _EraseCutoff + _EdgeSoftness,
                                               mask);

                col.a *= (1.0 - eraseAmount);
                return col;
            }
            ENDHLSL
        }
    }
}