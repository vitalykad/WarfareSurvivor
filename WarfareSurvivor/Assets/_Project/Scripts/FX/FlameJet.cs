using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Пламя: струя огнемёта и язычки на горящих зомби.
    ///
    /// Одна система на оба случая нарочно. Огонь на враге — это не отдельный
    /// эффект, а тот же самый огонь, только меньше и реже: если рисовать их
    /// разным, поджиг перестаёт читаться как следствие струи.
    ///
    /// Клубы летят ОТ БОЙЦА и растут по дороге, а не висят на месте. Огнемёт
    /// узнаётся именно по движению наружу: неподвижный конус читается
    /// нарисованной зоной, как у кислоты.
    ///
    /// Цвет идёт от белого к красному через оранжевый и гаснет прозрачностью.
    /// Ровный оранжевый на песке не виден вовсе — песок сам оранжевый.
    /// </summary>
    public class FlameJet : MonoBehaviour
    {
        static readonly List<FlameJet> All = new List<FlameJet>();
        static ArenaConfig config;
        static Camera view;
        static Transform root;
        static Material material;
        static int nextVictim;

        MeshRenderer view3D;
        Mesh mesh;
        readonly Color[] corners = new Color[4];

        Vector3 velocity;
        float bornTime;
        float dieTime;
        float startSize;
        float endSize;

        public static void Configure(ArenaConfig cfg, Camera camera)
        {
            config = cfg;
            view = camera;

            if (root == null)
            {
                root = new GameObject("FlameJets").transform;
                All.Clear();
            }
        }

        /// <summary>
        /// Один клуб работающей струи.
        ///
        /// По одному, а не пачкой на удар: огнемёт горит непрерывно, и пачки
        /// читались бы отдельными вспышками — ровно так первая версия и
        /// выглядела. Сколько клубов выпустить за кадр, решает сам боец,
        /// исходя из жара.
        ///
        /// Жар меняет и размер клуба: холодная струя не только короче,
        /// но и жиже. Иначе разогрев виден только по длине, а сила удара
        /// меняется незаметно для глаза.
        /// </summary>
        public static void Puff(Vector3 origin, Vector3 forward, float reach, float angleDegrees, float heat)
        {
            if (config == null || root == null) return;

            float half = angleDegrees * 0.5f;

            // Ближе к бойцу струя узкая, к концу расходится — так и ведёт себя
            // настоящий факел, и заодно видно, докуда он достаёт.
            float along = Mathf.Sqrt(Random.value);
            float spread = Random.Range(-half, half) * along;

            var direction = Quaternion.Euler(0f, spread, 0f) * forward;
            var at = origin + direction * (reach * along * 0.3f);

            float life = config.flameLife * Random.Range(0.7f, 1f);
            float speed = reach / Mathf.Max(0.05f, config.flameLife) * Random.Range(0.7f, 1.1f);
            float size = config.flameSize * Mathf.Lerp(0.45f, 1f, heat);

            Spawn(at, direction * speed + Vector3.up * Random.Range(0.2f, 0.7f),
                  size * Random.Range(0.5f, 0.9f),
                  size * Random.Range(1.6f, 2.4f),
                  life);
        }

        /// <summary>Язычок на горящем: тот же огонь, мельче и без разлёта.</summary>
        public static void Wisp(Vector3 at)
        {
            if (config == null || root == null) return;

            Spawn(at + Random.insideUnitSphere * 0.25f,
                  Vector3.up * Random.Range(0.9f, 1.6f),
                  config.flameSize * 0.35f,
                  config.flameSize * 0.9f,
                  config.flameLife * 0.8f);
        }

        static void Spawn(Vector3 at, Vector3 velocity, float from, float to, float life)
        {
            var puff = Rent();
            if (puff == null) return;

            puff.transform.position = at;
            puff.transform.rotation = view != null ? view.transform.rotation : Quaternion.identity;
            puff.transform.localScale = Vector3.one * from;

            puff.velocity = velocity;
            puff.startSize = from;
            puff.endSize = to;
            puff.bornTime = Time.time;
            puff.dieTime = Time.time + Mathf.Max(0.05f, life);
            puff.gameObject.SetActive(true);
        }

        static FlameJet Rent()
        {
            for (int i = 0; i < All.Count; i++)
                if (!All[i].gameObject.activeSelf)
                    return All[i];

            if (All.Count < Mathf.Max(8, config.maxFlamePuffs))
            {
                var created = Create();
                All.Add(created);
                return created;
            }

            nextVictim = (nextVictim + 1) % All.Count;
            return All[nextVictim];
        }

        static FlameJet Create()
        {
            var go = new GameObject("FlamePuff");
            go.transform.SetParent(root, false);

            var filter = go.AddComponent<MeshFilter>();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.sharedMaterial = FlameMaterial();

            var puff = go.AddComponent<FlameJet>();
            puff.view3D = renderer;
            puff.mesh = Quad();
            filter.sharedMesh = puff.mesh;

            go.SetActive(false);
            return puff;
        }

        static Mesh Quad()
        {
            var quad = new Mesh { name = "FlameQuad" };
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

        static Material FlameMaterial()
        {
            if (material != null) return material;

            // НЕ аддитивный: на оранжевом песке аддитивное пламя выбеливает
            // землю и теряет собственный цвет. Тот же выбор, что у волны.
            var shader = Shader.Find("WarfareSurvivor/GlowSprite");
            material = new Material(shader) { name = "Flame", mainTexture = Blob() };
            return material;
        }

        static Texture2D Blob()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "FlameBlob", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = r >= 1f ? 0f : 0.5f + 0.5f * Mathf.Cos(r * Mathf.PI);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
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

            transform.position += velocity * Time.deltaTime;
            velocity *= 1f - Mathf.Clamp01(config.flameDrag * Time.deltaTime);

            transform.localScale = Vector3.one * Mathf.Lerp(startSize, endSize, life);
            if (view != null) transform.rotation = view.transform.rotation;

            // Белое ядро в начале, красный уголь в конце.
            var color = life < 0.35f
                ? Color.Lerp(config.flameCoreColor, config.flameColor, life / 0.35f)
                : Color.Lerp(config.flameColor, config.flameEmberColor, (life - 0.35f) / 0.65f);

            color.a *= 1f - life * life;

            for (int i = 0; i < corners.Length; i++) corners[i] = color;
            mesh.colors = corners;
        }
    }
}
