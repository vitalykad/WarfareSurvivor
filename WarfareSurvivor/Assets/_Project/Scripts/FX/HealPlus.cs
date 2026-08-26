using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Зелёный плюсик, взлетающий над вылеченным бойцом.
    ///
    /// Сделан не текстом, а квадом с нарисованной текстурой — ради подсветки.
    /// У TextMesh свечения нет и быть не может: он рисуется шрифтовым атласом
    /// в обычном альфа-смешении. Здесь же и плюс, и его ореол лежат в одной
    /// текстуре, а аддитивный шейдер складывает её с фоном — свечение
    /// достаётся бесплатно, одним квадом и одним вызовом отрисовки.
    ///
    /// Пул с потолком стоит с самого начала, как у цифр урона: восемь медиков
    /// на быстром темпе — это десятки плюсиков в секунду, и дорисовывать
    /// ограничение потом дороже, чем заложить сразу (§8 project.md).
    /// </summary>
    public class HealPlus : MonoBehaviour
    {
        static readonly List<HealPlus> All = new List<HealPlus>();
        static ArenaConfig config;
        static Transform root;
        static Camera view;
        static Material material;
        static int nextVictim;

        MeshRenderer view3D;
        Mesh mesh;
        readonly Color[] corners = new Color[4];
        float bornTime;
        float dieTime;
        float wobble;

        public static void Configure(ArenaConfig cfg, Camera camera)
        {
            config = cfg;
            view = camera;

            if (root == null)
            {
                root = new GameObject("HealPluses").transform;
                All.Clear();
            }
        }

        public static void Spawn(Vector3 position)
        {
            if (config == null || root == null) return;

            var plus = Rent();
            if (plus == null) return;

            // Яркость перечитываем на каждом плюсике, а не один раз при
            // создании материала: иначе настройка застывала бы на том
            // значении, какое стояло в момент первого лечения за забег,
            // и крутить её во время игры было бы нельзя.
            if (material != null && material.HasProperty("_Boost"))
                material.SetFloat("_Boost", Mathf.Max(0.1f, config.healPlusBoost));

            plus.transform.position = position;
            plus.transform.localScale = Vector3.one * Mathf.Max(0.05f, config.healPlusScale);
            plus.bornTime = Time.time;
            plus.dieTime = Time.time + Mathf.Max(0.1f, config.healPlusLifetime);

            // Разброс по фазе, чтобы два плюсика подряд не шли одной колонной.
            plus.wobble = Random.value * Mathf.PI * 2f;
            plus.gameObject.SetActive(true);
        }

        static HealPlus Rent()
        {
            for (int i = 0; i < All.Count; i++)
                if (!All[i].gameObject.activeSelf)
                    return All[i];

            if (All.Count < Mathf.Max(4, config.maxHealPluses))
            {
                var created = Create();
                All.Add(created);
                return created;
            }

            // Потолок выбран — забираем самый старый по кругу. Пропускать
            // новый плюсик нельзя: пропадёт ответ ровно там, где лечат чаще
            // всего, то есть в самой гуще.
            nextVictim = (nextVictim + 1) % All.Count;
            return All[nextVictim];
        }

        static HealPlus Create()
        {
            var go = new GameObject("HealPlus");
            go.transform.SetParent(root, false);

            var filter = go.AddComponent<MeshFilter>();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            var plus = go.AddComponent<HealPlus>();
            plus.view3D = renderer;

            // Меш СВОЙ у каждого плюсика, а не общий. Цвет и прозрачность
            // в этом шейдере живут в вершинах — так он устроен для трасс, —
            // и общий меш означал бы, что все плюсики гаснут разом,
            // подчиняясь последнему из них.
            plus.mesh = Quad();
            filter.sharedMesh = plus.mesh;

            go.SetActive(false);
            return plus;
        }

        static Mesh Quad()
        {
            var quad = new Mesh { name = "HealPlusQuad" };
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

            // Тот же шейдер, что у трасс и искр: он уже в списке обязательных
            // для сборки. Заводить свой значило бы завести и новый повод
            // потерять его при вырезании шейдеров.
            var shader = Shader.Find("WarfareSurvivor/AdditiveTracer");
            if (shader == null)
            {
                Debug.LogError("[HealPlus] Не нашёлся шейдер WarfareSurvivor/AdditiveTracer — " +
                               "плюсики останутся без подсветки.");
                shader = Shader.Find("Sprites/Default");
            }

            material = new Material(shader) { name = "HealPlus" };
            material.mainTexture = Texture();
            if (material.HasProperty("_Boost")) material.SetFloat("_Boost", Mathf.Max(0.1f, config.healPlusBoost));

            // Мягкая плёночная кривая: жёсткое обрезание в белый нужно трассам,
            // а плюсик от него теряет цвет и перестаёт читаться зелёным.
            if (material.HasProperty("_Rolloff")) material.SetFloat("_Rolloff", 1f);
            return material;
        }

        /// <summary>
        /// Рисуем плюс и его ореол в текстуру.
        ///
        /// Кодом, а не файлом: это две вложенные формулы на 64 пикселя,
        /// а импортированная картинка — ещё один ассет, который надо не забыть
        /// положить в сборку и который кто-нибудь однажды подменит.
        /// </summary>
        static Texture2D Texture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "HealPlusGlow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            const float arm = 0.56f;   // половина длины перекладины
            const float thick = 0.19f; // половина толщины

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;

                    // Сам крест: расстояние до фигуры из двух пересечённых полос.
                    float dx = Mathf.Max(0f, Mathf.Abs(u) - arm);
                    float dy = Mathf.Max(0f, Mathf.Abs(v) - arm);
                    float toBarH = Mathf.Sqrt(dx * dx + Mathf.Max(0f, Mathf.Abs(v) - thick) * Mathf.Max(0f, Mathf.Abs(v) - thick));
                    float toBarV = Mathf.Sqrt(dy * dy + Mathf.Max(0f, Mathf.Abs(u) - thick) * Mathf.Max(0f, Mathf.Abs(u) - thick));
                    float toCross = Mathf.Min(toBarH, toBarV);

                    // Тело плюса с мягким краем в пару пикселей.
                    float body = 1f - Step(toCross, 2.5f / size);

                    // Ореол: спад от центра фигуры, а не от центра картинки —
                    // иначе свечение выглядит кругом, к плюсу не привязанным.
                    float halo = Mathf.Exp(-toCross * 7f) * 0.55f;

                    float alpha = Mathf.Clamp01(body + halo);

                    // Ядро подсвечиваем, но НЕ добела: аддитивное смешение
                    // и без того тянет всё к белому, а поверх песочной земли
                    // выбеленный плюс перестаёт быть зелёным вовсе.
                    float core = Mathf.Clamp01(body * 1.15f);
                    var rgb = Color.Lerp(new Color(0.22f, 1f, 0.40f), Color.white, core * 0.3f);

                    pixels[y * size + x] = new Color(rgb.r, rgb.g, rgb.b, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Мягкий порог: 0 до нуля, 1 после edge, плавно между.
        ///
        /// Свой, потому что Mathf.SmoothStep — это НЕ шейдерный smoothstep,
        /// а интерполяция МЕЖДУ ДВУМЯ ЗНАЧЕНИЯМИ по параметру. Подстановка
        /// туда расстояния дала альфу 0.96 по всему квадрату: плюс вышел
        /// сплошной заливкой, и это было видно в кадре, но не в коде.
        /// </summary>
        static float Step(float value, float edge)
        {
            float t = Mathf.Clamp01(value / Mathf.Max(edge, 0.0001f));
            return t * t * (3f - 2f * t);
        }

        void LateUpdate()
        {
            if (config == null || Time.time >= dieTime)
            {
                gameObject.SetActive(false);
                return;
            }

            float life = Mathf.InverseLerp(bornTime, dieTime, Time.time);

            // Взлёт с замедлением: рывок вверх в начале, зависание в конце.
            // Ровная скорость читается как уезжающая декорация, а не как
            // всплывший ответ на лечение.
            float rise = config.healPlusRiseSpeed * (1f - life) * Time.deltaTime;
            float sway = Mathf.Cos(wobble + life * 3f) * config.healPlusSway * Time.deltaTime;

            var step = Vector3.up * rise;
            if (view != null) step += view.transform.right * sway;
            transform.position += step;

            if (view != null) transform.rotation = view.transform.rotation;

            // Гаснет по квадрату: первую половину жизни плюсик почти в полную
            // силу, дальше уходит быстро. Линейное затухание всё время
            // выглядит полупрозрачным.
            var color = config.healPlusColor;
            color.a *= 1f - life * life;

            for (int i = 0; i < corners.Length; i++) corners[i] = color;
            mesh.colors = corners;

            // Чуть разрастается по мере подъёма: плюсик, гаснущий без роста,
            // выглядит выключенным, а не растворившимся.
            transform.localScale = Vector3.one * Mathf.Max(0.05f, config.healPlusScale) * (1f + life * 0.35f);
        }
    }
}
