using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Взрыв кислотного плевка: вспышка и ударная волна.
    ///
    /// Дым и брызги отвечают на вопрос «что разлетелось», но не дают
    /// самого удара: облако проявляется полсекунды, а взрыв — событие
    /// на два-три кадра. Нужен резкий всплеск света в момент касания
    /// и кольцо, разбегающееся по земле.
    ///
    /// Вспышка стоит вертикально к камере, кольцо ЛЕЖИТ НА ЗЕМЛЕ. Это не
    /// придирка: кольцо, повёрнутое к камере, читается вторым шаром,
    /// а лежащее — расходящейся по земле волной, то есть тем, чем и должно.
    /// </summary>
    public class AcidBlast : MonoBehaviour
    {
        enum Kind { Flash, Ring }

        static readonly List<AcidBlast> All = new List<AcidBlast>();
        static ArenaConfig config;
        static Camera view;
        static Transform root;
        static Material flashMaterial;
        static Material ringMaterial;
        static int nextVictim;

        MeshRenderer view3D;
        Mesh mesh;
        readonly Color[] corners = new Color[4];

        Kind kind;
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
                root = new GameObject("AcidBlasts").transform;
                All.Clear();
            }
        }

        /// <summary>Вспышка и волна разом — весь удар одним вызовом.</summary>
        public static void Burst(Vector3 at, float radius)
        {
            if (config == null || root == null) return;

            radius = Mathf.Max(0.2f, radius);

            Spawn(Kind.Flash, at + Vector3.up * (radius * 0.35f),
                  radius * 0.5f, radius * Mathf.Max(0.2f, config.acidFlashScale),
                  Mathf.Max(0.03f, config.acidFlashTime));

            Spawn(Kind.Ring, at, radius * 0.35f,
                  radius * Mathf.Max(0.2f, config.acidRingScale),
                  Mathf.Max(0.05f, config.acidRingTime));
        }

        static void Spawn(Kind kind, Vector3 at, float from, float to, float life)
        {
            var blast = Rent();
            if (blast == null) return;

            blast.kind = kind;
            blast.transform.position = kind == Kind.Ring ? new Vector3(at.x, 0.03f, at.z) : at;

            // Кольцо кладём плашмя, вспышку разворачиваем к камере.
            blast.transform.rotation = kind == Kind.Ring
                ? Quaternion.Euler(90f, 0f, 0f)
                : (view != null ? view.transform.rotation : Quaternion.identity);

            blast.view3D.sharedMaterial = kind == Kind.Ring ? RingMaterial() : FlashMaterial();

            blast.startSize = from;
            blast.endSize = to;
            blast.bornTime = Time.time;
            blast.dieTime = Time.time + life;

            blast.transform.localScale = Vector3.one * from;
            blast.gameObject.SetActive(true);
        }

        static AcidBlast Rent()
        {
            for (int i = 0; i < All.Count; i++)
                if (!All[i].gameObject.activeSelf)
                    return All[i];

            if (All.Count < Mathf.Max(4, config.maxAcidBlasts))
            {
                var created = Create();
                All.Add(created);
                return created;
            }

            nextVictim = (nextVictim + 1) % All.Count;
            return All[nextVictim];
        }

        static AcidBlast Create()
        {
            var go = new GameObject("AcidBlast");
            go.transform.SetParent(root, false);

            var filter = go.AddComponent<MeshFilter>();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            var blast = go.AddComponent<AcidBlast>();
            blast.view3D = renderer;
            blast.mesh = Quad();
            filter.sharedMesh = blast.mesh;

            go.SetActive(false);
            return blast;
        }

        static Mesh Quad()
        {
            var quad = new Mesh { name = "AcidBlastQuad" };
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

        static Material FlashMaterial()
        {
            if (flashMaterial != null) return flashMaterial;

            // Вспышка АДДИТИВНАЯ: она должна выбивать в белое всё, на чём
            // лежит, — в этом и состоит удар. Держать свой цвет ей незачем.
            var shader = Shader.Find("WarfareSurvivor/AdditiveTracer");
            flashMaterial = new Material(shader) { name = "AcidFlash", mainTexture = Blob() };
            if (flashMaterial.HasProperty("_Boost")) flashMaterial.SetFloat("_Boost", 2.2f);
            return flashMaterial;
        }

        static Material RingMaterial()
        {
            if (ringMaterial != null) return ringMaterial;

            var shader = Shader.Find("WarfareSurvivor/AdditiveTracer");
            ringMaterial = new Material(shader) { name = "AcidRing", mainTexture = Ring() };
            if (ringMaterial.HasProperty("_Boost")) ringMaterial.SetFloat("_Boost", 1.8f);
            return ringMaterial;
        }

        /// <summary>Мягкое пятно для вспышки: плотное ядро и быстрый спад.</summary>
        static Texture2D Blob()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "AcidFlashBlob", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float d = r >= 1f ? 0f : 0.5f + 0.5f * Mathf.Cos(r * Mathf.PI);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, d * d * d);
                }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Кольцо: пусто в середине, полоса у края.
        ///
        /// Именно кольцо, а не диск: расходящийся диск читается вторым
        /// облаком, а кольцо — фронтом волны. Пустая середина и делает
        /// эту разницу.
        /// </summary>
        static Texture2D Ring()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "AcidRing", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            float width = 0.16f;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);

                    // Полоса у самого края квада: снаружи резче, внутрь мягче,
                    // как у настоящего фронта.
                    float edge = 1f - Mathf.Clamp01((r - (1f - width)) / width);
                    float inner = Mathf.Clamp01((r - (1f - width * 2.2f)) / (width * 1.2f));
                    float a = r > 1f ? 0f : Mathf.Clamp01(edge * inner);

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

            // Разлетается с ЗАМЕДЛЕНИЕМ: рывок в первые кадры и торможение.
            // Ровное расширение читается раздувающимся шаром, а не ударом.
            float grow = 1f - (1f - life) * (1f - life);
            transform.localScale = Vector3.one * Mathf.Lerp(startSize, endSize, grow);

            if (kind == Kind.Flash && view != null) transform.rotation = view.transform.rotation;

            // Вспышка гаснет почти сразу, кольцо доживает до конца.
            var color = kind == Kind.Flash ? config.acidFlashColor : config.acidRingColor;
            color.a *= kind == Kind.Flash
                ? Mathf.Pow(1f - life, 2.2f)
                : 1f - life * life;

            for (int i = 0; i < corners.Length; i++) corners[i] = color;
            mesh.colors = corners;
        }
    }
}
