// Дешёвая земля: текстура, главный свет и его тень. Больше ничего.
//
// URP/Lit считает на каждом пикселе полный PBR — металличность,
// шероховатость, отражения, сферические гармоники, до четырёх
// дополнительных источников. Земля занимает весь экран, поэтому платится
// это полным кадром: замер на устройстве дал 27.2 мс против 16.7 мс
// с Unlit, то есть 10.5 мс за освещение одного квада.
//
// Simple Lit при этом почти не помог (26.6 мс) — значит дорога не модель
// освещения, а сам блок расчёта света. Поэтому здесь его нет: только
// главный свет, только его тень.
Shader "WarfareSurvivor/CheapGround"
{
    Properties
    {
        _BaseMap ("Текстура", 2D) = "white" {}
        _BaseColor ("Оттенок", Color) = (1,1,1,1)

        // Сколько света остаётся в тени. Ноль — чёрная тень.
        _ShadowFloor ("Дно тени", Range(0,1)) = 0.45
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

            // Только тени главного света. Дополнительных источников,
            // отражений и всего прочего здесь нет намеренно.
            // Набор ровно как у стандартного URP/Lit. Если пропустить хоть
            // один вариант, шейдер молча собирается БЕЗ теней: конвейер
            // включает ключевое слово, которого в шейдере нет, и берётся
            // вариант «теней нет». Ошибка тихая — в одном окне тени видны,
            // в другом нет.
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _ShadowFloor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                // Нормаль не интерполируем: земля плоская, она всегда вверх.
                // Это экономит и интерполятор, и нормализацию на пиксель.
                const half3 normalWS = half3(0, 1, 0);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));

                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half shadow = lerp(_ShadowFloor, 1.0h, mainLight.shadowAttenuation);

                // Непрямой свет берём одной константой из настроек окружения,
                // а не сферическими гармониками: на плоской земле разницы
                // не видно, а гармоники считаются на каждом пикселе.
                half3 ambient = unity_AmbientSky.rgb;

                half3 lit = ambient + mainLight.color * ndotl * shadow;
                return half4(albedo.rgb * lit, 1.0h);
            }
            ENDHLSL
        }

        // Прохода отбрасывания тени нет: земля тень не отбрасывает,
        // и лишний проход по всему квадру нам не нужен.
    }

    Fallback "Universal Render Pipeline/Unlit"
}
