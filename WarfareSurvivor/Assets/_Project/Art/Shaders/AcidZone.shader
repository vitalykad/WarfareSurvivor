// Зона поражения кислотного плевка: круг на земле там, куда прилетит.
//
// Круг рисуется МАТЕМАТИКОЙ ПО КВАДУ, а не мешем-диском и не текстурой:
// радиус зоны равен радиусу поражения, а он у каждого плевка свой. Меш
// пришлось бы перестраивать под каждый радиус, текстуру — тянуть за собой
// в сборку. Здесь же радиус — это просто масштаб квада.
//
// Смешение обычное, с прозрачностью, а НЕ аддитивное. Аддитивный красный
// поверх песочной земли даёт розовое пятно, которое читается как подсветка,
// а не как опасность. Зона должна темнить землю под собой.
Shader "WarfareSurvivor/AcidZone"
{
    Properties
    {
        _ZoneColor ("Цвет заливки", Color) = (0.75, 0.05, 0.05, 0.35)
        [HDR] _RimColor ("Цвет кромки", Color) = (1, 0.25, 0.15, 0.9)

        _Fill ("Заполнение отсчёта", Range(0,1)) = 0
        _Fade ("Общая видимость", Range(0,1)) = 1
        _RimWidth ("Ширина кромки", Range(0.01, 0.5)) = 0.13
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "IgnoreProjector"="True" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        // Смещение к камере: зона лежит на земле вплотную, и без этого
        // она пятнами проваливается под неё.
        Offset -1, -1

        Pass
        {
            Name "Zone"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            CBUFFER_START(UnityPerMaterial)
                half4 _ZoneColor;
                half4 _RimColor;
                float _Fill;
                float _Fade;
                float _RimWidth;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Развёртка квада 0..1 — переводим в круг с центром в нуле.
                float2 p = IN.uv * 2.0 - 1.0;
                float r = length(p);

                // За границей круга не рисуем ничего. Мягкость в пару
                // процентов радиуса убирает ступеньку на краю.
                float inside = 1.0 - smoothstep(1.0 - 0.04, 1.0, r);

                // Кромка — яркое кольцо у самой границы. Именно она читается
                // как «граница опасности»; заливка внутри только подсказывает,
                // что это не украшение земли.
                float rim = smoothstep(1.0 - _RimWidth, 1.0 - _RimWidth * 0.35, r) * inside;

                // Отсчёт до попадания: внутренний круг растёт от центра
                // к кромке. Игрок видит не только КУДА прилетит, но и КОГДА,
                // и решение уходить принимает по картинке, а не наугад.
                float filled = 1.0 - smoothstep(_Fill - 0.05, _Fill + 0.05, r);

                half3 rgb = lerp(_ZoneColor.rgb, _RimColor.rgb, rim);
                half alpha = (_ZoneColor.a * (0.4 + 0.6 * filled) + rim * _RimColor.a) * inside * _Fade;

                return half4(rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
