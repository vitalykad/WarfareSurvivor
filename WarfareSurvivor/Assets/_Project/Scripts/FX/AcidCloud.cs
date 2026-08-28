using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Ядовитое облако на месте упавшего плевка.
    ///
    /// Собрано из нескольких клубов, а не из одного пятна: одно пятно,
    /// как его ни рисуй, читается наклейкой на земле. Несколько клубов
    /// разного размера, расходящихся с разной скоростью, дают объём —
    /// глазу достаточно того, что они движутся вразнобой.
    ///
    /// Облако ЧИСТО ЗРИТЕЛЬНОЕ: урон наносится один раз, в момент падения.
    /// Дым, который продолжает жечь, — это уже другая механика, и заводить
    /// её надо осознанно, а не потому что нарисовалось похожее.
    /// </summary>
    public class AcidCloud : MonoBehaviour
    {
        static readonly List<AcidCloud> All = new List<AcidCloud>();
        static ArenaConfig config;
        static Camera view;
        static Transform root;
        static Material material;
        static int nextVictim;

        MeshRenderer view3D;
        Mesh mesh;
        readonly Color[] corners = new Color[4];

        float bornTime;
        float dieTime;
        float startSize;
        float endSize;
        float spin;
        Vector3 drift;

        public static void Configure(ArenaConfig cfg, Camera camera)
        {
            config = cfg;
            view = camera;

            if (root == null)
            {
                root = new GameObject("AcidClouds").transform;
                All.Clear();
            }
        }

        /// <summary>Облако радиусом с зону поражения.</summary>
        public static void Bloom(Vector3 at, float radius)
        {
            if (config == null || root == null) return;

            int count = Mathf.Max(1, config.acidCloudPuffs);
            radius = Mathf.Max(0.2f, radius);

            for (int i = 0; i < count; i++)
            {
                var puff = Rent();
                if (puff == null) return;

                // Первый клуб — в самом центре: без него облако выходит
                // кольцом, то есть дыркой на месте попадания.
                float reach = i == 0 ? 0f : radius * config.acidCloudSpread * Mathf.Sqrt(Random.value);
                float angle = Random.value * Mathf.PI * 2f;

                var place = at + new Vector3(Mathf.Cos(angle) * reach, 0f, Mathf.Sin(angle) * reach);
                place.y = Mathf.Max(0.2f, radius * 0.35f) * Random.Range(0.5f, 1.1f);

                puff.transform.position = place;

                puff.startSize = radius * config.acidCloudScale * Random.Range(0.55f, 1f);
                puff.endSize = puff.startSize * Random.Range(1.5f, 2.1f);
                puff.spin = Random.Range(-1f, 1f);

                // Клубы расходятся наружу и слегка всплывают — так облако
                // растёт само, а не просто раздувается на месте.
                var away = place - at;
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f) away = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                puff.drift = away.normalized * (radius * 0.35f) + Vector3.up * (radius * 0.25f);

                puff.bornTime = Time.time;
                puff.dieTime = Time.time + Mathf.Max(0.15f, config.acidCloudTime) * Random.Range(0.75f, 1.15f);

                puff.transform.localScale = Vector3.one * puff.startSize;
                puff.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// Один клуб — тот же, из которых собрано облако взрыва.
        ///
        /// Нужен летящему снаряду: он сеет за собой такие же клубы, и дым
        /// от полёта и дым от попадания оказываются одной породы. Разный
        /// дым читался бы как два разных вещества.
        /// </summary>
        public static void Puff(Vector3 at, float size, float life)
        {
            if (config == null || root == null) return;

            var puff = Rent();
            if (puff == null) return;

            puff.transform.position = at;

            puff.startSize = Mathf.Max(0.05f, size);
            puff.endSize = puff.startSize * Random.Range(1.6f, 2.3f);
            puff.spin = Random.Range(-1f, 1f);

            // Оседает и слегка расходится вбок: дым за снарядом должен
            // отставать, а не лететь вместе с ним.
            puff.drift = new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(-0.5f, -0.1f), Random.Range(-0.4f, 0.4f));

            puff.bornTime = Time.time;
            puff.dieTime = Time.time + Mathf.Max(0.1f, life) * Random.Range(0.8f, 1.2f);

            puff.transform.localScale = Vector3.one * puff.startSize;
            puff.gameObject.SetActive(true);
        }

        static AcidCloud Rent()
        {
            for (int i = 0; i < All.Count; i++)
                if (!All[i].gameObject.activeSelf)
                    return All[i];

            if (All.Count < Mathf.Max(8, config.maxAcidCloudPuffs))
            {
                var created = Create();
                All.Add(created);
                return created;
            }

            nextVictim = (nextVictim + 1) % All.Count;
            return All[nextVictim];
        }

        static AcidCloud Create()
        {
            var go = new GameObject("AcidPuff");
            go.transform.SetParent(root, false);

            var filter = go.AddComponent<MeshFilter>();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            var puff = go.AddComponent<AcidCloud>();
            puff.view3D = renderer;

            // Меш свой у каждого клуба: прозрачность в этом шейдере живёт
            // в вершинах, и общий меш гасил бы всё облако разом.
            puff.mesh = Quad();
            filter.sharedMesh = puff.mesh;

            go.SetActive(false);
            return puff;
        }

        static Mesh Quad()
        {
            var quad = new Mesh { name = "AcidPuffQuad" };
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
                Debug.LogError("[AcidCloud] Не нашёлся шейдер WarfareSurvivor/GlowSprite — " +
                               "облако останется без цвета.");
                shader = Shader.Find("WarfareSurvivor/AdditiveTracer");
            }

            material = new Material(shader) { name = "AcidCloud", mainTexture = Texture() };
            if (material.HasProperty("_Boost")) material.SetFloat("_Boost", 1f);
            return material;
        }

        /// <summary>
        /// Мягкий клуб: плотнее к середине, полностью прозрачный к краю.
        ///
        /// Спад ПОЛОГИЙ, в отличие от капли: у капли есть плотное ядро,
        /// потому что это предмет, а у дыма ядра нет вовсе — резкий край
        /// сразу выдаёт в нём кружок.
        /// </summary>
        static Texture2D Texture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "AcidPuff",
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

                    // Косинусный купол: гладкий и в центре, и на краю.
                    float density = r >= 1f ? 0f : 0.5f + 0.5f * Mathf.Cos(r * Mathf.PI);

                    // Показатель ниже единицы НАМЕРЕННО: при квадрате плотным
                    // остаётся только середина клуба, и облако выходит
                    // зеленоватой дымкой вместо дыма. Здесь плотная часть
                    // занимает почти весь клуб, а на нет сходит у края.
                    float alpha = Mathf.Pow(density, 0.75f) * 0.95f;

                    var rgb = Color.Lerp(new Color(0.20f, 0.62f, 0.10f), new Color(0.55f, 1f, 0.35f), density * 0.65f);
                    pixels[y * size + x] = new Color(rgb.r, rgb.g, rgb.b, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        void LateUpdate()
        {
            if (config == null || Time.time >= dieTime)
            {
                gameObject.SetActive(false);
                return;
            }

            float life = Mathf.InverseLerp(bornTime, dieTime, Time.time);

            // Расходится с замедлением: рывок в начале, оседание в конце.
            transform.position += drift * ((1f - life) * Time.deltaTime);

            transform.localScale = Vector3.one * Mathf.Lerp(startSize, endSize, Mathf.Sqrt(life));

            if (view != null) transform.rotation = view.transform.rotation;
            transform.Rotate(Vector3.forward, spin * 25f * Time.deltaTime, Space.Self);

            // Проявляется быстро, тает долго: облако должно возникнуть
            // разом на попадании и потом медленно расходиться.
            float alpha = life < 0.15f
                ? Mathf.InverseLerp(0f, 0.15f, life)
                : 1f - Mathf.InverseLerp(0.15f, 1f, life);

            var color = config.acidCloudColor;
            color.a *= alpha;

            for (int i = 0; i < corners.Length; i++) corners[i] = color;
            mesh.colors = corners;
        }
    }
}
