using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Белое свечение вокруг раненого бойца.
    ///
    /// Отвечает на вопрос «кого бьют». Полоска здоровья на него не отвечает:
    /// она мелкая, появляется на пару секунд и в куче из полутора десятков
    /// фигур читается как мелькание, а не как сведения. Свечение же видно
    /// боковым зрением и держится всё время, пока боец ранен.
    ///
    /// Яркость идёт от НЕДОСТАЮЩЕГО здоровья, а не от факта ранения:
    /// поцарапанный чуть светится, полумёртвый горит. Так игрок видит
    /// не только кого бьют, но и кого пора уводить.
    ///
    /// Плоскость за бойцом, а не обводка модели: обводка требует своего
    /// прохода отрисовки на каждого бойца, а это ровно та цена, которую
    /// прототипу платить не за что.
    /// </summary>
    public class WoundedGlow : MonoBehaviour
    {
        static Material shared;
        static Mesh quad;

        ArenaConfig config;
        Health health;
        Camera view;
        MeshRenderer view3D;
        Mesh mesh;
        readonly Color[] corners = new Color[4];
        float phase;

        /// <summary>
        /// Вешает свечение на бойца. Отдельным вызовом, как дуга замаха:
        /// боец собирается из префаба, и лишний объект в самом префабе
        /// пришлось бы держать выключенным и не забыть включить.
        /// </summary>
        public static WoundedGlow Attach(Transform owner, Health health, ArenaConfig config, Camera view)
        {
            var go = new GameObject("Свечение раненого");
            go.transform.SetParent(owner, false);

            // Позади бойца и на высоте груди: под ногами свечение читается
            // как лужа, а вокруг головы — как нимб.
            go.transform.localPosition = new Vector3(0f, config.woundedGlowHeight, 0f);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = Quad();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Shared();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            var glow = go.AddComponent<WoundedGlow>();
            glow.config = config;
            glow.health = health;
            glow.view = view;
            glow.view3D = renderer;
            glow.phase = Random.value * Mathf.PI * 2f;

            // Меш свой: прозрачность живёт в вершинах, и общий меш означал бы
            // одну яркость на весь отряд.
            glow.mesh = Quad();
            filter.sharedMesh = glow.mesh;

            // Объект остаётся ВКЛЮЧЁННЫМ, гасится только рендерер:
            // LateUpdate на выключенном объекте не идёт, и свечение,
            // однажды спрятавшись, больше никогда бы не зажглось.
            renderer.enabled = false;
            return glow;
        }

        static Mesh Quad()
        {
            var mesh = new Mesh { name = "WoundedGlowQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            mesh.MarkDynamic();
            return mesh;
        }

        static Material Shared()
        {
            if (shared != null) return shared;

            // Аддитивный, а не с предумноженной альфой: свечению не нужно
            // держать свой цвет — оно белое, и белым выходит хоть на песке,
            // хоть на тёмной куртке.
            var shader = Shader.Find("WarfareSurvivor/AdditiveTracer");
            if (shader == null)
            {
                Debug.LogError("[Свечение раненого] Не нашёлся шейдер WarfareSurvivor/AdditiveTracer.");
                return null;
            }

            shared = new Material(shader) { name = "WoundedGlow", mainTexture = Texture() };
            shared.SetFloat("_Boost", 1f);
            shared.SetFloat("_Rolloff", 1f);
            return shared;
        }

        /// <summary>Мягкое пятно без края: у свечения границы быть не должно.</summary>
        static Texture2D Texture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "WoundedGlow",
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

                    // В четвёртой степени: ореол собирается к середине,
                    // и свечение обнимает фигуру, а не заливает квадрат.
                    float alpha = density * density * density * density;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        void LateUpdate()
        {
            if (config == null || health == null) return;

            // Недостающая доля здоровья и есть яркость.
            float hurt = 1f - health.Fraction;

            if (hurt <= 0.01f || health.IsDead)
            {
                view3D.enabled = false;
                return;
            }

            view3D.enabled = true;

            // Пульс тем быстрее, чем хуже дела: ровное свечение глаз
            // перестаёт замечать через полминуты, ускоряющееся — нет.
            float pulse = 1f + Mathf.Sin((Time.time + phase) * config.woundedGlowPulse * (0.6f + hurt)) * 0.18f;

            transform.localPosition = new Vector3(0f, config.woundedGlowHeight, 0f);
            transform.localScale = Vector3.one * (config.woundedGlowSize * pulse);
            if (view != null) transform.rotation = view.transform.rotation;

            var color = config.woundedGlowColor;
            color.a *= Mathf.Clamp01(hurt * config.woundedGlowStrength) * pulse;

            for (int i = 0; i < corners.Length; i++) corners[i] = color;
            mesh.colors = corners;
        }
    }
}
