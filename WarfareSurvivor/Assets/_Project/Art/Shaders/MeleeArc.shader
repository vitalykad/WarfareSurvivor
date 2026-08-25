// Дуга замаха: широкий взмах по земле радиусом в дальность удара.
//
// Рисует не путь лопаты, а ГРАНИЦУ ДОСЯГАЕМОСТИ. Полотно лопаты уходит
// от бойца на метр с небольшим, а достаёт он на четыре — след по самому
// оружию соврал бы про дистанцию ровно там, где игрок ему поверит.
//
// Развёртка несёт смысл: uv.x — доля пройденной дуги, uv.y — поперёк
// полосы. Поэтому взмах это одно число _Sweep, а не перестройка меша.
Shader "WarfareSurvivor/MeleeArc"
{
    Properties
    {
        _ArcColor ("Цвет", Color) = (1, 0.82, 0.45, 1)
        [HDR] _HeadColor ("Цвет кромки", Color) = (1, 0.96, 0.8, 1)

        _Tail ("Длина хвоста", Range(0.05, 1)) = 0.45
        _HeadWidth ("Ширина кромки", Range(0.01, 0.4)) = 0.09
        _EdgeSoft ("Мягкость поперёк", Range(0.01, 1)) = 0.55
        _Fade ("Общая видимость", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }

        ZWrite Off
        Cull Off
        Offset -1, -1
        Blend SrcAlpha One

        Pass
        {
            Name "Arc"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ArcColor;
                half4 _HeadColor;
                half _Tail;
                half _HeadWidth;
                half _EdgeSoft;
                half _Fade;
            CBUFFER_END

            // Положение взмаха задаётся НА ЭКЗЕМПЛЯР: бойцов полтора десятка,
            // и каждый машет в своём такте.
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _Sweep)
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

                half sweep = UNITY_ACCESS_INSTANCED_PROP(Props, _Sweep);

                // Сколько дуги позади кромки. Отрицательное — сюда взмах
                // ещё не дошёл, и рисовать нечего.
                half behind = sweep - IN.uv.x;
                if (behind < 0.0h) return 0.0h;

                // Хвост гаснет по мере удаления от кромки.
                half tail = saturate(1.0h - behind / max(_Tail, 0.001h));
                tail *= tail;

                // Поперёк полосы — мягкие края, иначе дуга выглядит
                // вырезанной ножницами.
                half across = sin(saturate(IN.uv.y) * 3.14159h);
                across = pow(across, max(_EdgeSoft, 0.01h));

                // Сама кромка ярче хвоста: именно она читается как удар.
                half head = saturate(1.0h - behind / max(_HeadWidth, 0.001h));

                half alpha = tail * across * _Fade;
                half3 color = lerp(_ArcColor.rgb, _HeadColor.rgb, head);

                return half4(color * alpha, alpha * _ArcColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
