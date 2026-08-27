// Капля кислоты: светящаяся, но НЕ теряющая цвет.
//
// Отдельный шейдер, а не общий с трассами, из-за смешения. Аддитивное
// смешение только прибавляет свет, и поверх песочной земли любой цвет
// уходит в жёлто-белый: зелёный (0.45, 1, 0.2) на песке (1, 0.75, 0.45)
// даёт (1, 1, 0.7). Кислота переставала быть зелёной ровно там, где её
// надо узнать с одного взгляда.
//
// Здесь смешение с ПРЕДУМНОЖЕННОЙ АЛЬФОЙ: плотное ядро замещает собой
// землю и остаётся зелёным, а редкий ореол вокруг прибавляет свет,
// как аддитивный. Одна формула даёт и то, и другое.
Shader "WarfareSurvivor/AcidDrop"
{
    Properties
    {
        _MainTex ("Капля", 2D) = "white" {}
        _Boost ("Яркость", Float) = 1.3
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "Drop"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                half4  color       : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Boost;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                half alpha = tex.a * IN.color.a;
                half3 rgb = tex.rgb * IN.color.rgb * _Boost;

                // Предумножение: цвет уже взвешен своей же прозрачностью,
                // поэтому смешение идёт как One / OneMinusSrcAlpha.
                return half4(rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
