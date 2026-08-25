// Круг досягаемости под бойцом.
//
// Рисуется процедурно из развёртки, без текстуры: кольцо это две мягкие
// границы по расстоянию от центра, и хранить ради них картинку незачем.
//
// Прозрачность задаётся ИЗВНЕ, на каждый экземпляр: круг вспыхивает
// в момент удара и гаснет, а бойцов на поле полтора десятка, и каждый
// бьёт в своём темпе.
Shader "WarfareSurvivor/RangeRing"
{
    Properties
    {
        _RingColor ("Цвет", Color) = (1, 0.25, 0.2, 1)

        _RingWidth ("Толщина кольца", Range(0.01, 0.5)) = 0.09
        _RingSoft ("Мягкость края", Range(0.001, 0.3)) = 0.05

        _FillPower ("Заливка внутри", Range(0, 1)) = 0.12
        _Fade ("Видимость", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }

        // Лежит на земле, поэтому глубину не пишем и слегка смещаем к камере:
        // иначе круг спорит с землёй за один и тот же пиксель и мерцает.
        ZWrite Off
        Cull Off
        Offset -1, -1
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Ring"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RingColor;
                half _RingWidth;
                half _RingSoft;
                half _FillPower;
                half _Fade;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _RingFade)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half2 fromCenter = IN.uv * 2.0h - 1.0h;
                half dist = length(fromCenter);

                // Кольцо: полоса шириной _RingWidth, прижатая к самому краю.
                // Край круга и есть дальность удара, поэтому полоса уходит
                // ВНУТРЬ от единицы, а не размазывается по обе стороны.
                half inner = 1.0h - _RingWidth;
                half ring = smoothstep(inner - _RingSoft, inner, dist)
                          * (1.0h - smoothstep(1.0h - _RingSoft, 1.0h, dist));

                // Слабая заливка внутри: без неё круг читается как обод,
                // а нужно показать площадь, которую боец простреливает.
                half fill = (1.0h - smoothstep(inner, 1.0h, dist)) * _FillPower;

                half alpha = saturate(ring + fill) * _RingColor.a
                           * _Fade * UNITY_ACCESS_INSTANCED_PROP(Props, _RingFade);

                return half4(_RingColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
