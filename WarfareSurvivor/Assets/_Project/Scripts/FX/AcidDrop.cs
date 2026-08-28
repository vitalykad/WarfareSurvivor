using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Светящаяся капля кислоты: и сам плевок в полёте, и брызги от него.
    ///
    /// Один класс на то и другое, потому что разница только в размере
    /// и в том, что делает капля, коснувшись земли. Плевок наносит урон
    /// по кругу и разлетается брызгами, брызги просто гаснут.
    ///
    /// Летит по дуге, а не по прямой: настильная траектория на виде почти
    /// сверху выглядит скольжением по земле, и понять, что предмет летит,
    /// нельзя. Горка сразу читается как бросок.
    /// </summary>
    public class AcidDrop : MonoBehaviour
    {
        static readonly List<AcidDrop> All = new List<AcidDrop>();
        static ArenaConfig config;
        static Camera view;
        static Transform root;
        static Material material;

        MeshRenderer renderer3D;
        Mesh mesh;
        readonly Color[] corners = new Color[4];

        /// <summary>Когда снаряд обронит следующий клуб дыма и следующую искру.</summary>
        float nextPuff;
        float nextSpark;

        /// <summary>Дымка вокруг шара. Есть только у самого плевка, у брызг нет.</summary>
        Transform haze;
        MeshRenderer hazeView;
        Mesh hazeMesh;
        readonly Color[] hazeCorners = new Color[4];
        bool comet;

        Vector3 from;
        Vector3 to;
        float arc;
        float bornTime;
        float dieTime;
        float size;
        float spin;

        /// <summary>Радиус поражения. Ноль у брызг — они безвредны.</summary>
        float damageRadius;
        float damage;
        AcidZone zone;

        public static void Configure(ArenaConfig cfg, Camera camera)
        {
            config = cfg;
            view = camera;

            if (root == null)
            {
                root = new GameObject("AcidDrops").transform;
                All.Clear();
            }
        }

        /// <summary>Сам плевок: летит в точку, взрывается и бьёт по кругу.</summary>
        public static void Spit(Vector3 origin, Vector3 landing, float flightTime,
                                float radius, float hitDamage, AcidZone marker)
        {
            var drop = Launch(origin, landing, flightTime, config != null ? config.acidSpitSize : 0.5f, comet: true);
            if (drop == null)
            {
                // Пул выбран под завязку — плевок всё равно должен ударить,
                // иначе зона обманет игрока: круг был, а урона нет.
                Land(landing, radius, hitDamage, marker);
                return;
            }

            drop.damageRadius = radius;
            drop.damage = hitDamage;
            drop.zone = marker;
        }

        /// <summary>Брызги от упавшего плевка. Безвредны, живут доли секунды.</summary>
        public static void Splash(Vector3 at, float radius)
        {
            if (config == null) return;

            int count = Mathf.Max(0, config.acidSplashCount);
            for (int i = 0; i < count; i++)
            {
                // По окружности с лёгким разбросом: чистый случай сбивает
                // брызги в одну сторону и оставляет пустые сектора.
                float angle = (i + Random.value * 0.7f) / count * Mathf.PI * 2f;
                float reach = radius * Mathf.Lerp(0.35f, 1.15f, Random.value);

                var landing = at + new Vector3(Mathf.Cos(angle) * reach, 0f, Mathf.Sin(angle) * reach);
                landing.y = 0.05f;

                var drop = Launch(at + Vector3.up * 0.2f, landing,
                                  config.acidSplashTime * Random.Range(0.7f, 1.3f),
                                  config.acidSpitSize * Random.Range(0.22f, 0.42f), comet: false);
                if (drop != null) drop.arc *= 0.5f;
            }
        }

        static AcidDrop Launch(Vector3 origin, Vector3 landing, float flightTime, float dropSize, bool comet)
        {
            if (config == null || root == null) return null;

            var drop = Rent();
            if (drop == null) return null;

            drop.from = origin;
            drop.to = landing;
            drop.bornTime = Time.time;
            drop.dieTime = Time.time + Mathf.Max(0.05f, flightTime);
            drop.size = dropSize;
            drop.damageRadius = 0f;
            drop.damage = 0f;
            drop.zone = null;
            drop.spin = Random.Range(-1f, 1f);

            // Горка тем выше, чем дальше лететь: короткий плевок под ноги
            // с той же высотой выглядел бы навесным миномётом.
            drop.arc = Mathf.Max(0.2f, Vector3.Distance(origin, landing) * config.acidArcHeight);

            drop.transform.position = origin;
            drop.comet = comet;
            drop.nextPuff = Time.time;
            drop.nextSpark = Time.time;

            // Дымка и шлейф — только у самого плевка. У брызг они превратили бы
            // попадание в зелёную кашу: их десяток, и все летят разом.
            if (drop.hazeView != null) drop.hazeView.enabled = comet;

            drop.gameObject.SetActive(true);
            return drop;
        }

        static AcidDrop Rent()
        {
            for (int i = 0; i < All.Count; i++)
                if (!All[i].gameObject.activeSelf)
                    return All[i];

            if (All.Count < Mathf.Max(8, config.maxAcidDrops))
            {
                var created = Create();
                All.Add(created);
                return created;
            }

            // Потолок выбран. Брызгу отказать можно — она украшение;
            // сам плевок вызывающая сторона доводит до земли без нас.
            return null;
        }

        static AcidDrop Create()
        {
            var go = new GameObject("AcidDrop");
            go.transform.SetParent(root, false);

            var filter = go.AddComponent<MeshFilter>();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            var drop = go.AddComponent<AcidDrop>();
            drop.renderer3D = renderer;

            // Меш свой у каждой капли: цвет и прозрачность в этом шейдере
            // живут в вершинах, и общий меш гасил бы все капли разом.
            drop.mesh = Quad();
            filter.sharedMesh = drop.mesh;

            // Дымка — ОТДЕЛЬНАЯ плоскость, крупнее и мягче ядра. Одной
            // текстурой это не сделать: у ядра должен быть резкий яркий центр,
            // у дымки его быть не должно вовсе.
            var mist = new GameObject("AcidHaze");
            mist.transform.SetParent(go.transform, false);

            var mistFilter = mist.AddComponent<MeshFilter>();
            drop.hazeMesh = Quad();
            mistFilter.sharedMesh = drop.hazeMesh;

            var mistView = mist.AddComponent<MeshRenderer>();
            mistView.sharedMaterial = HazeMaterial();
            mistView.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mistView.receiveShadows = false;
            mistView.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            drop.haze = mist.transform;
            drop.hazeView = mistView;

            go.SetActive(false);
            return drop;
        }

        static Mesh Quad()
        {
            var quad = new Mesh { name = "AcidDropQuad" };
            quad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f)
            };
            quad.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quad.RecalculateBounds();
            quad.MarkDynamic();
            return quad;
        }

        static Material Material()
        {
            if (material != null) return material;

            var shader = Shader.Find("WarfareSurvivor/GlowSprite");
            if (shader == null)
            {
                Debug.LogError("[AcidDrop] Не нашёлся шейдер WarfareSurvivor/GlowSprite — " +
                               "капли останутся без свечения.");
                shader = Shader.Find("WarfareSurvivor/AdditiveTracer");
            }

            material = new Material(shader) { name = "AcidDrop", mainTexture = Texture() };
            return material;
        }

        static Material hazeMaterial;

        static Material HazeMaterial()
        {
            if (hazeMaterial != null) return hazeMaterial;

            // Дымка АДДИТИВНАЯ, в отличие от самого шара.
            //
            // У шара смешение с предумноженной альфой: ему надо держать
            // белое ядро, а для этого — замещать собой фон. Но замещает оно
            // и хвост: пятно дымки в три метра поверх ленты выедало её первые
            // полтора метра, и хвост будто начинался поодаль от шара.
            // Замер это и показал — лента начиналась в 0.18 м от центра шара,
            // то есть внутри него, а видно её было только за краем дымки.
            //
            // Аддитивная дымка ничего не гасит, только прибавляет свет,
            // и лента сквозь неё проходит целиком.
            var shader = Shader.Find("WarfareSurvivor/AdditiveTracer");
            if (shader == null) return Material();

            hazeMaterial = new Material(shader) { name = "AcidHaze", mainTexture = HazeTexture() };
            if (hazeMaterial.HasProperty("_Boost")) hazeMaterial.SetFloat("_Boost", 1.2f);

            // Мягкая кривая: без неё зелёный ореол поверх песка уходит в белый.
            if (hazeMaterial.HasProperty("_Rolloff")) hazeMaterial.SetFloat("_Rolloff", 1f);
            return hazeMaterial;
        }

        /// <summary>Мягкий ореол без ядра: у дымки середины быть не должно.</summary>
        static Texture2D HazeTexture()
        {
            const int size = 48;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "AcidHazeGlow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);

                    float density = r >= 1f ? 0f : 0.5f + 0.5f * Mathf.Cos(r * Mathf.PI);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, density * density * 0.85f);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Круглая капля со свечением, нарисованная кодом.
        ///
        /// Ядро почти непрозрачное, вокруг — мягкий спад. Это и делает
        /// её похожей на светящуюся жидкость, а не на плоский кружок.
        /// </summary>
        static Texture2D Texture()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "AcidDropGlow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);

                    // Ядро — СПЛОШНОЙ диск с мягким краем, а не спад от центра.
                    // Спадом капля вышла размером с пиксель: ярким оставался
                    // только самый центр, а по меркам квада это доли метра.
                    float beyond = Mathf.Max(0f, r - 0.30f);
                    float core = 1f - Smooth(beyond, 0.10f);

                    // Ореола в самой капле БОЛЬШЕ НЕТ, и это главное.
                    //
                    // Смешение у неё с предумноженной альфой: где альфа
                    // заметна, капля ЗАМЕЩАЕТ собой всё, что под ней, —
                    // и стирала хвост на всей площади своего ореола.
                    // Просвет между шаром и лентой был ровно в радиус шара,
                    // это и выдало причину.
                    //
                    // Свечение вокруг ядра даёт дымка: она аддитивная
                    // и ничего не стирает.
                    float alpha = core;

                    // Ядро ЧИСТО БЕЛОЕ, зелень только вокруг него.
                    //
                    // Цвет живёт в ТЕКСТУРЕ, а не в цвете вершины: вершинный
                    // цвет умножается на всю каплю разом, и зелёный в нём
                    // красил заодно и ядро — белого не оставалось нигде.
                    // Поэтому цвет капли в конфиге теперь белый и работает
                    // как общая яркость.
                    float white = 1f - Smooth(Mathf.Max(0f, r - 0.16f), 0.26f);
                    var rgb = Color.Lerp(new Color(0.30f, 1f, 0.12f), Color.white, white);
                    pixels[y * size + x] = new Color(rgb.r, rgb.g, rgb.b, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Мягкий порог. Свой, потому что Mathf.SmoothStep — это интерполяция
        /// между двумя значениями, а не шейдерный smoothstep.
        /// </summary>
        static float Smooth(float value, float edge)
        {
            float t = Mathf.Clamp01(value / Mathf.Max(edge, 0.0001f));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Вытягивает шар вдоль полёта и покачивает его, как пузырь.
        ///
        /// Круглый снаряд не говорит, куда летит, — направление приходится
        /// угадывать по дыму позади. Вытянутый вдоль скорости говорит это
        /// сам, одной своей формой.
        ///
        /// Покачивание идёт С СОХРАНЕНИЕМ объёма: вытянулся вдоль — сжался
        /// поперёк. Иначе шар просто пульсирует размером, а это читается
        /// как мигание, а не как упругая капля.
        /// </summary>
        void Shape(Vector3 velocity, float life)
        {
            if (view == null) return;

            // Разворачиваем к камере, а потом доворачиваем вокруг взгляда так,
            // чтобы длинная ось легла по направлению полёта НА ЭКРАНЕ.
            float onRight = Vector3.Dot(velocity, view.transform.right);
            float onUp = Vector3.Dot(velocity, view.transform.up);
            float angle = Mathf.Atan2(onUp, onRight) * Mathf.Rad2Deg;
            transform.rotation = view.transform.rotation * Quaternion.Euler(0f, 0f, angle);

            float wobble = 1f + Mathf.Sin((Time.time + spin) * config.acidSpitWobbleSpeed) * config.acidSpitWobble;
            float grown = size * (1f + life * 0.25f);

            float along = grown * Mathf.Max(0.2f, config.acidSpitStretch) * wobble;
            float across = grown / (Mathf.Max(0.2f, config.acidSpitStretch) * wobble);

            transform.localScale = new Vector3(along, across, grown);

            // Дымка — ребёнок, и растяжение родителя её бы перекосило.
            // Гасим его обратной величиной, чтобы она осталась круглой.
            if (haze == null) return;
            float wanted = Mathf.Max(1f, config.acidHazeScale) * (1f + life * 0.3f);
            haze.localScale = new Vector3(wanted * grown / along, wanted * grown / across, wanted);
        }

        /// <summary>Попадание: урон по кругу и брызги.</summary>
        static void Land(Vector3 at, float radius, float damage, AcidZone zone)
        {
            if (zone != null) zone.Hide();

            // Вспышка и волна — В САМОМ НАЧАЛЕ, до урона и дыма: удар должен
            // быть виден в тот же кадр, в который он случился.
            if (radius > 0f) AcidBlast.Burst(at, radius);

            if (radius > 0f && damage > 0f)
            {
                float radiusSqr = radius * radius;
                var survivors = Registry.Survivors;

                // По списку с конца: попадание может убить бойца, а смерть
                // убирает его из реестра прямо во время обхода.
                for (int i = survivors.Count - 1; i >= 0; i--)
                {
                    var survivor = survivors[i];
                    if (survivor == null || survivor.Health.IsDead) continue;

                    var away = survivor.transform.position - at;
                    away.y = 0f;
                    if (away.sqrMagnitude > radiusSqr) continue;

                    survivor.ApplyDamage(damage, survivor.transform.position);
                }
            }

            Splash(at, Mathf.Max(0.4f, radius * 0.55f));

            // Облако поднимается на месте попадания. Радиусом ровно в зону:
            // круг обещал именно эту площадь, и облако не должно её ни
            // приукрашивать, ни занижать.
            if (radius > 0f) AcidCloud.Bloom(at, radius);
        }

        void LateUpdate()
        {
            if (config == null)
            {
                gameObject.SetActive(false);
                return;
            }

            float life = Mathf.InverseLerp(bornTime, dieTime, Time.time);

            if (life >= 1f)
            {
                gameObject.SetActive(false);

                // Урон и брызги наносит САМА капля в момент касания земли,
                // а не плевун в момент выстрела. Иначе попадание случалось бы
                // раньше картинки, и зона переставала бы что-либо значить.
                if (damageRadius > 0f) Land(to, damageRadius, damage, zone);
                else if (zone != null) zone.Hide();

                damageRadius = 0f;
                zone = null;
                return;
            }

            // Горка: линейное движение плюс парабола по высоте.
            var flat = Vector3.Lerp(from, to, life);
            flat.y += arc * 4f * life * (1f - life);
            transform.position = flat;

            // Мгновенная скорость: прямая часть плюс производная параболы.
            // Считаем её, а не разницу с прошлым кадром, — так направление
            // точное и не дрожит на медленном ходу.
            var velocity = to - from;
            velocity.y += arc * 4f * (1f - 2f * life);

            // Снаряд сеет за собой клубы дыма — те же, из которых собрано
            // облако взрыва. Так дым от полёта и дым от попадания читаются
            // одним веществом, а не двумя разными эффектами.
            if (comet && Time.time >= nextPuff)
            {
                nextPuff = Time.time + Mathf.Max(0.005f, config.acidSmokeInterval);

                // Клуб рождается ПОЗАДИ шара, а не в нём. Густой шлейф,
                // начинающийся в самой точке снаряда, хоронит его под собой:
                // свежие клубы садятся ровно на ядро, и от кометы остаётся
                // одно зелёное пятно.
                var behind = transform.position - velocity.normalized * (size * config.acidSmokeOffset);

                AcidCloud.Puff(behind,
                               size * Mathf.Max(0.05f, config.acidSmokeSize),
                               Mathf.Max(0.1f, config.acidSmokeTime));
            }

            if (comet) Shape(velocity, life);
            else if (view != null)
            {
                transform.rotation = view.transform.rotation;
                transform.Rotate(Vector3.forward, spin * 180f * Time.deltaTime, Space.Self);
                transform.localScale = Vector3.one * size * (1f + life * 0.25f);
            }

            // Искры из летящего шара — те же брызги, что и при взрыве.
            // Разлетаются назад и вбок: искра, обгоняющая снаряд, читается
            // как второй снаряд.
            if (comet && Time.time >= nextSpark && config.acidSparkInterval > 0f)
            {
                nextSpark = Time.time + config.acidSparkInterval;

                var back = -velocity.normalized;
                float reach = size * Random.Range(0.8f, 1.8f);
                var away = (back + Random.insideUnitSphere * 0.8f).normalized * reach;

                var spark = Launch(transform.position, transform.position + away,
                                   config.acidSparkTime * Random.Range(0.7f, 1.3f),
                                   size * Random.Range(0.14f, 0.28f), comet: false);
                if (spark != null) spark.arc *= 0.3f;
            }

            var color = config.acidDropColor;
            color.a *= 1f - life * life * 0.35f;

            for (int i = 0; i < corners.Length; i++) corners[i] = color;
            mesh.colors = corners;

            if (material != null && material.HasProperty("_Boost"))
                material.SetFloat("_Boost", Mathf.Max(0.1f, config.acidDropBoost));

            if (!comet || haze == null) return;

            // Размер дымке задаёт Shape вместе с формой шара: она ребёнок,
            // и растяжение родителя надо гасить обратной величиной.
            haze.rotation = view != null ? view.transform.rotation : transform.rotation;

            var hazeColor = config.acidHazeColor;
            hazeColor.a *= 1f - life * life * 0.35f;

            for (int i = 0; i < hazeCorners.Length; i++) hazeCorners[i] = hazeColor;
            hazeMesh.colors = hazeCorners;
        }
    }
}
