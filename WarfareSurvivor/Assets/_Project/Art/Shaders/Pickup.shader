// Подбираемый предмет: картинка на плоскости плюс мягкое свечение вокруг.
//
// Свечение нужно не для красоты. Бутылка лежит на песке среди трупов
// и обломков, и без подсветки игрок её просто не находит — а вся ценность
// ресурса в том, что за ним идут. Ореол пульсирует: движение в неподвижной
// сцене глаз ловит гораздо раньше, чем контраст.
Shader "WarfareSurvivor/Pickup"
{
    Properties
    {
        _BaseMap ("Картинка", 2D) = "white" {}
        _BaseColor ("Оттенок", Color) = (1,1,1,1)

        [HDR] _GlowColor ("Цвет свечения", Color) = (0.35, 0.75, 1, 1)

        _GlowSize ("Размах свечения", Range(1, 2)) = 1.35
        _GlowPower ("Сила свечения", Range(0, 2)) = 0.55

        _PulseSpeed ("Скорость пульса", Range(0, 8)) = 2.2
        _PulseDepth ("Глубина пульса", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }

        // Глубину не пишем: предмет полупрозрачный, и запись в буфер
        // обрезала бы его же ореол.
        ZWrite Off
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _GlowColor;
            half _GlowSize;
            half _GlowPower;
            half _PulseSpeed;
            half _PulseDepth;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv         : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionHCS : SV_POSITION;
            float2 uv          : TEXCOORD0;
        };

        half Pulse()
        {
            // От единицы вниз, а не вверх: пульс приглушает свечение,
            // а не раздувает его сверх заданной силы.
            half wave = 0.5h + 0.5h * sin(_Time.y * _PulseSpeed);
            return 1.0h - _PulseDepth * wave;
        }
        ENDHLSL

        // Ореол: та же картинка, раздутая от центра, аддитивно и без резких
        // краёв. Отдельной текстуры свечения не заводим — лишний ассет ради
        // пятна под предметом не окупается.
        Pass
        {
            Name "Glow"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 blown = IN.positionOS.xyz * _GlowSize;
                OUT.positionHCS = TransformObjectToHClip(blown);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a;
                half strength = alpha * _GlowPower * Pulse();
                return half4(_GlowColor.rgb * strength, strength);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Sprite"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                // Освещение не считаем намеренно: предмет должен читаться
                // одинаково и на солнце, и в тени руин.
                return albedo;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
