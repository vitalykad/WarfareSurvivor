// Дешёвый тун для персонажей и врагов.
//
// Стиль тот же, что у Toony Colors Pro: ступенчатое освещение, мягкая
// граница света и тени, цветная тень. Разница в цене — TCP2 универсален
// и тащит пять проходов и множество ключевых слов на любой случай;
// здесь один проход и ровно то, что нужно этому проекту.
//
// Тун при этом ДЕШЕВЛЕ полного PBR по своей природе: вместо микрофасетов
// и френеля — одна ступенька по N·L.
Shader "WarfareSurvivor/CheapToon"
{
    Properties
    {
        _BaseMap ("Текстура", 2D) = "white" {}
        _BaseColor ("Оттенок", Color) = (1,1,1,1)

        // Цвет в тени. Холодный оттенок читается лучше простого затемнения.
        _ShadowColor ("Цвет тени", Color) = (0.45, 0.5, 0.62, 1)

        // Где проходит граница света и тени по N·L.
        _Edge ("Граница", Range(0,1)) = 0.45

        // Ширина перехода. Ноль — жёсткая ступенька.
        _Soft ("Мягкость границы", Range(0.001,0.4)) = 0.06
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Скиннинг нужен: это персонажи.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half _Edge;
                half _Soft;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);

                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                float3 normalWS = normalize(IN.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));

                // Освещённость с завёрнутым N·L: чистый dot даёт слишком
                // резкий терминатор на низкополигональных моделях.
                half ndotl = dot(normalWS, mainLight.direction) * 0.5h + 0.5h;
                half lightness = ndotl * mainLight.shadowAttenuation;

                // Та самая ступенька — весь тун держится на ней.
                half band = smoothstep(_Edge - _Soft, _Edge + _Soft, lightness);

                half3 lighting = lerp(_ShadowColor.rgb, mainLight.color, band);

                // Непрямой свет одной константой: сферические гармоники
                // на каждом пикселе тут не окупаются.
                lighting += unity_AmbientSky.rgb * 0.5h;

                return half4(albedo.rgb * lighting, 1.0h);
            }
            ENDHLSL
        }

        // Тень отбрасываем стандартным проходом URP: писать свой ради
        // скиннинга смысла нет, а без него персонажи перестанут давать тень.
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }

    Fallback "Universal Render Pipeline/Unlit"
}
