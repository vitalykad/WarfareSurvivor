// Светящийся спрайт, НЕ теряющий свой цвет на светлом фоне.
//
// Общий для всего, что должно и светиться, и оставаться узнаваемого
// цвета: капли кислоты, следа за добычей, всего последующего.
//
// Отдельный от трасс из-за смешения. Аддитивное смешение только прибавляет
// свет, и поверх песочной земли любой цвет уходит в белый: зелёная кислота
// (0.45, 1, 0.2) на песке (1, 0.75, 0.45) давала (1, 1, 0.7), а синий след
// за бутылкой — чистый белый. Цвет пропадал ровно там, где по нему
// и узнают, что это.
//
// Здесь смешение с ПРЕДУМНОЖЕННОЙ АЛЬФОЙ: плотное ядро замещает собой
// землю и держит цвет, а редкий край прибавляет свет, как аддитивный.
// Одна формула даёт и то, и другое.
Shader "WarfareSurvivor/GlowSprite"
{
    Properties
    {
        _MainTex ("Картинка", 2D) = "white" {}
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
            Name "Glow"
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
