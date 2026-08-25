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
        // Имена с префиксом _Toon намеренно: при смене шейдера на живом
        // материале Unity переносит совпадающие по имени свойства, и общие
        // имена вроде _ShadowColor подхватываются из чужого шейдера. Зомби
        // от этого становились бирюзовыми — TCP2 держит в _ShadowColor
        // холодный оттенок.
        //
        // Тень ТЁПЛАЯ, не синяя. Синева неба вместе с зелёной кожей зомби
        // уводила их в болотный оттенок, которого у TCP2 нет: подобрано
        // сравнением бок о бок.
        _ToonShadow ("Цвет тени", Color) = (0.52, 0.47, 0.40, 1)

        // Где проходит граница света и тени по N·L. Считается по СЫРОМУ
        // косинусу: 0 — поверхность смотрит вбок от света, 1 — прямо на него.
        _ToonEdge ("Граница", Range(0,1)) = 0.3

        // Ширина перехода. Ноль — жёсткая ступенька.
        _ToonSoft ("Мягкость границы", Range(0.001,0.4)) = 0.16

        // Вклад непрямого света. Больше — площе картинка.
        _ToonAmbient ("Непрямой свет", Range(0,1)) = 0.45

        // Сколько формы остаётся ВНУТРИ освещённой полосы. Ноль — плоская
        // заливка одним цветом, единица — полный градиент по углу.
        _ToonGradient ("Форма на свету", Range(0,1)) = 0.45

        // Насколько тень подкрашивается цветом света.
        _ToonShadowTint ("Тень в цвете света", Range(0,1)) = 0.7
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
            // Набор ровно как у стандартного URP/Lit. Если пропустить хоть
            // один вариант, шейдер молча собирается БЕЗ теней: конвейер
            // включает ключевое слово, которого в шейдере нет, и берётся
            // вариант «теней нет». Ошибка тихая — в одном окне тени видны,
            // в другом нет.
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
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
                half4 _ToonShadow;
                half _ToonEdge;
                half _ToonSoft;
                half _ToonAmbient;
                half _ToonGradient;
                half _ToonShadowTint;
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

                // СЫРОЙ косинус, без «заворачивания» в 0..1.
                //
                // Сначала я взял завёрнутый (dot * 0.5 + 0.5) — и модель
                // оказалась освещена целиком: при таком отображении всё,
                // что отвёрнуто от света меньше чем на 96 градусов, попадает
                // в светлую полосу, а это почти весь силуэт. Порог должен
                // стоять на самом косинусе, тогда терминатор ложится там,
                // где поверхность действительно уходит от света.
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half lightness = ndotl * mainLight.shadowAttenuation;

                // Та самая ступенька — весь тун держится на ней.
                half band = smoothstep(_ToonEdge - _ToonSoft, _ToonEdge + _ToonSoft, lightness);

                // ФОРМА ВНУТРИ СВЕТЛОЙ ПОЛОСЫ.
                //
                // Одной ступеньки мало: она насыщается, и всё, что повёрнуто
                // к солнцу сильнее порога, заливается одним цветом — макушка,
                // плечо и грудь становятся неразличимы, объём пропадает.
                // Поэтому внутри света остаётся градиент по углу, а резким
                // остаётся только сам терминатор — в нём и есть тун.
                half shaping = 1.0h - _ToonGradient * (1.0h - ndotl);

                // ТЕНЬ ТОЖЕ КРАСИТСЯ СВЕТОМ.
                //
                // Была константой, одинаковой при любом солнце: рядом
                // с тёплым светом выходила серой, чего не бывает. Тень —
                // тот же свет, только ослабленный.
                half3 shade = _ToonShadow.rgb * lerp(half3(1.0h, 1.0h, 1.0h), mainLight.color, _ToonShadowTint);

                half3 lighting = lerp(shade, mainLight.color * shaping, band);

                // Непрямой свет одной константой: сферические гармоники
                // на каждом пикселе тут не окупаются. Вклад держим малым —
                // он добавляется к обеим полосам и, если переборщить,
                // съедает разницу между ними, ради которой всё и затевалось.
                lighting += unity_AmbientSky.rgb * _ToonAmbient;

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
