// Аддитивный шейдер для трасс и искр.
//
// Свой, а не URP/Unlit с настройкой прозрачности: у того есть ShaderGUI,
// который пересчитывает режим смешивания при каждой валидации материала
// и упорно возвращает своё (§13 CROWD_PROJECT_LESSONS).
//
// Разделение обязанностей: цвет и прозрачность живут в вершинах, яркость —
// в материале (_Boost). Цвет вершины ужимается в байт при загрузке меша,
// и всё выше единицы туда просто не пролезает.
Shader "WarfareSurvivor/AdditiveTracer"
{
    Properties
    {
        _MainTex ("Ribbon", 2D) = "white" {}
        _Boost ("Brightness", Float) = 3
        _Rolloff ("Film rolloff", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Blend SrcAlpha One   // сложение с фоном
        ZWrite Off
        ZTest LEqual
        Cull Off             // ленты строятся через Cross, нормаль скачет

        Pass
        {
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
                float _Rolloff;
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
                half3 rgb = tex.rgb * IN.color.rgb * _Boost;

                // Плёночная кривая переключателем: 0 — жёсткое обрезание
                // в белый (резкость, нужна трассам), 1 — мягкий переход.
                // Полноэкранного тонмаппера на мобиле нет, а здесь та же
                // кривая стоит пару операций и только на пикселях эффекта.
                rgb = lerp(rgb, rgb / (1.0h + rgb), _Rolloff);

                return half4(rgb, tex.a * IN.color.a);
            }
            ENDHLSL
        }
    }
}
