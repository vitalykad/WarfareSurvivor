// Стилизованное пламя: шум, силуэт и три ступени цвета.
//
// Так делают огонь почти во всех мультяшных играх, и делают одинаково:
// по силуэту (маска) ползёт шум, и там, где шум пересиливает силуэт,
// пламени нет. Три порога — край, середина, ядро — режут остаток
// на цветные ленты. Никакого мягкого градиента: у мягкого клуба нет
// формы, он читается дымкой, а не огнём. Огонь узнают по РВАНОМУ краю
// и по ЯЗЫКАМ, и то и другое здесь даёт шум.
//
// Две выборки текстур и десяток операций на пиксель — дешевле, чем
// старые мягкие клубы, которых при этом надо было втрое больше.
//
// Смешение с предумноженной альфой, как у GlowSprite: плотное ядро
// замещает землю и держит цвет, редкий край прибавляет свет. Аддитивный
// огонь на оранжевом песке уходил в белое пятно.
Shader "WarfareSurvivor/Flame"
{
    Properties
    {
        _MainTex   ("Силуэт (альфа)", 2D) = "white" {}
        _Noise     ("Шум (R)", 2D) = "gray" {}
        _NoiseScale("Масштаб шума (вдоль, поперёк)", Vector) = (3, 2, 0, 0)
        _Flow      ("Скорость течения, повторов/с", Float) = 2.5
        _BandMid   ("Порог середины", Range(0, 1)) = 0.22
        _BandCore  ("Порог ядра", Range(0, 1)) = 0.45
        _Soft      ("Мягкость порогов", Range(0, 0.2)) = 0.04
        _CoreColor ("Ядро", Color) = (1, 0.97, 0.75, 1)
        _MidColor  ("Середина", Color) = (1, 0.55, 0.12, 1)
        _EdgeColor ("Край", Color) = (0.55, 0.13, 0.05, 0.8)
        _Boost     ("Яркость", Float) = 1.15
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
            Name "Flame"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                // x, y — сдвиг шума этого языка, чтобы языки не были
                // копиями друг друга; z — эрозия: 0 — цел, 1 — сгорел.
                float3 tongue     : TEXCOORD1;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 tongue      : TEXCOORD1;
                half4  color       : COLOR;
            };

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_Noise);    SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _NoiseScale;
                float  _Flow;
                float  _BandMid;
                float  _BandCore;
                float  _Soft;
                half4  _CoreColor;
                half4  _MidColor;
                half4  _EdgeColor;
                float  _Boost;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.tongue = IN.tongue;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Шум ТЕЧЁТ вдоль языка: у струи — от дула к кончику,
                // у языка — от основания вверх. Неподвижный шум читается
                // узором на ткани, а не огнём.
                float2 flow = IN.uv * _NoiseScale.xy + IN.tongue.xy;
                flow.y -= _Time.y * _Flow;
                half n = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, flow).r;

                half mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;

                // Силуэт минус эрозия: чем ближе к смерти, тем меньше
                // остаётся силуэта, и он уходит рваными клочьями, а не
                // тает целиком.
                half g = mask - IN.tongue.z;

                half outer = smoothstep(n - _Soft, n + _Soft, g);
                half mid   = smoothstep(n - _Soft, n + _Soft, g - _BandMid);
                half core  = smoothstep(n - _Soft, n + _Soft, g - _BandCore);

                half4 tint = lerp(_EdgeColor, _MidColor, mid);
                tint = lerp(tint, _CoreColor, core);

                half alpha = outer * tint.a * IN.color.a;
                half3 rgb = tint.rgb * IN.color.rgb * _Boost;

                return half4(rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
