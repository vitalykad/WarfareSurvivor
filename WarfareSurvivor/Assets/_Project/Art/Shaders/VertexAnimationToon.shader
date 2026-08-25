// Тун для запечённой анимации: позиция вершины берётся из текстуры,
// а не считается по костям.
//
// Зачем. Стенд намерил (PERFORMANCE.md §6), что сотня зомби стоит около
// двадцати четырёх миллисекунд, и десять из них — скиннинг. Здесь его нет
// вовсе: вершинный шейдер читает готовую позицию из строки, соответствующей
// кадру анимации.
//
// Освещение слово в слово из CheapToon — стиль должен остаться прежним.
Shader "WarfareSurvivor/VertexAnimationToon"
{
    Properties
    {
        _BaseMap ("Текстура", 2D) = "white" {}
        _BaseColor ("Оттенок", Color) = (1,1,1,1)

        _ToonShadow ("Цвет тени", Color) = (0.52, 0.47, 0.40, 1)
        _ToonEdge ("Граница", Range(0,1)) = 0.3
        _ToonSoft ("Мягкость границы", Range(0.001,0.4)) = 0.16
        _ToonAmbient ("Непрямой свет", Range(0,1)) = 0.45

        // Сколько формы остаётся ВНУТРИ освещённой полосы. Ноль — плоская
        // заливка одним цветом, единица — полный градиент по углу.
        _ToonGradient ("Форма на свету", Range(0,1)) = 0.45

        // Насколько тень подкрашивается цветом света.
        _ToonShadowTint ("Тень в цвете света", Range(0,1)) = 0.7

        _PosTex ("Позиции вершин", 2D) = "black" {}
        _NrmTex ("Нормали вершин", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_PosTex);
        SAMPLER(sampler_PosTex);
        TEXTURE2D(_NrmTex);
        SAMPLER(sampler_NrmTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _PosTex_TexelSize;
            half4 _BaseColor;
            half4 _ToonShadow;
            half _ToonEdge;
            half _ToonSoft;
            half _ToonAmbient;
            half _ToonGradient;
            half _ToonShadowTint;
        CBUFFER_END

        // Кадр задаётся НА ЭКЗЕМПЛЯР: сто зомби бегут вразнобой, и общего
        // для всех номера кадра быть не может. x и y — соседние строки,
        // z — доля перехода между ними. Смешивание нужно, потому что печём
        // тридцать кадров в секунду, а показываем шестьдесят: без него
        // движение получается рубленым.
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _AnimRows)
        UNITY_INSTANCING_BUFFER_END(Props)

        void SampleBaked(float2 vertexUV, out float3 positionOS, out float3 normalOS)
        {
            float4 rows = UNITY_ACCESS_INSTANCED_PROP(Props, _AnimRows);

            // .w в TexelSize — высота в текселях, то есть число запечённых
            // кадров. Половина текселя — чтобы попадать в середину строки.
            float height = max(_PosTex_TexelSize.w, 1.0);
            float2 uvA = float2(vertexUV.x, (rows.x + 0.5) / height);
            float2 uvB = float2(vertexUV.x, (rows.y + 0.5) / height);

            float3 pA = SAMPLE_TEXTURE2D_LOD(_PosTex, sampler_PosTex, uvA, 0).xyz;
            float3 pB = SAMPLE_TEXTURE2D_LOD(_PosTex, sampler_PosTex, uvB, 0).xyz;
            positionOS = lerp(pA, pB, rows.z);

            float3 nA = SAMPLE_TEXTURE2D_LOD(_NrmTex, sampler_NrmTex, uvA, 0).xyz;
            float3 nB = SAMPLE_TEXTURE2D_LOD(_NrmTex, sampler_NrmTex, uvB, 0).xyz;
            normalOS = normalize(lerp(nA, nB, rows.z));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                // Номер вершины, положенный печью во второй набор UV сразу
                // как координата текстуры.
                float2 vertexUV   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionOS, normalOS;
                SampleBaked(IN.vertexUV, positionOS, normalOS);

                OUT.positionWS = TransformObjectToWorld(positionOS);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                float3 normalWS = normalize(IN.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));

                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half lightness = ndotl * mainLight.shadowAttenuation;
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
                lighting += unity_AmbientSky.rgb * _ToonAmbient;

                return half4(albedo.rgb * lighting, 1.0h);
            }
            ENDHLSL
        }

        // Свой проход тени, а не чужой готовый: стандартный ставит вершину
        // туда, где она лежит в меше, а у нас в меше поза не хранится вовсе.
        // С чужим проходом зомби бежал бы, а тень его стояла бы столбом.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float2 vertexUV   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings shadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionOS, normalOS;
                SampleBaked(IN.vertexUV, positionOS, normalOS);

                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);

                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionHCS = positionCS;
                return OUT;
            }

            half4 shadowFrag(ShadowVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
