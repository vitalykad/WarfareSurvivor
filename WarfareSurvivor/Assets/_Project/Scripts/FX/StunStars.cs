using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Звёзды над оглушённым: мультяшный венчик, по которому оглушение
    /// видно с игровой дистанции.
    ///
    /// Без него оглушение читается только по тому, что зомби замер, — а в
    /// толпе, где половина и так топчется в куче, это не читается никак.
    /// Звёзды нужны именно потому, что вероятность оглушения невелика:
    /// редкое событие обязано быть заметным, иначе игрок не свяжет его
    /// с выбором «оглушение вместо отброса».
    ///
    /// ВСЕ звёзды всех оглушённых лежат в ОДНОМ меше и рисуются одним
    /// вызовом. Отдельный объект на звезду означал бы при трёх звёздах и
    /// двадцати оглушённых шесть десятков вызовов отрисовки на украшение.
    ///
    /// Звёзды ходят по эллипсу, а не по кругу: круг сверху смотрится
    /// плоским кольцом, а сплюснутый по вертикали эллипс читается орбитой
    /// вокруг головы. Дальняя половина орбиты рисуется мельче и тусклее —
    /// этого хватает, чтобы глаз увидел глубину без всякой геометрии.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class StunStars : MonoBehaviour
    {
        static StunStars instance;
        static ArenaConfig config;
        static Camera view;

        Mesh mesh;
        MeshRenderer meshRenderer;

        Vector3[] vertices;
        Color[] colors;
        Vector2[] uv;
        int[] triangles;
        int capacity;
        int count;

        public static int ActiveCount => instance != null ? instance.count : 0;

        public static void Configure(ArenaConfig cfg, Camera camera)
        {
            config = cfg;
            view = camera;

            if (instance != null) return;

            var go = new GameObject("StunStars");
            instance = go.AddComponent<StunStars>();
            instance.Build();
        }

        void Build()
        {
            capacity = Mathf.Max(8, config.maxStunStars);

            vertices = new Vector3[capacity * 4];
            colors = new Color[capacity * 4];
            uv = new Vector2[capacity * 4];
            triangles = new int[capacity * 6];

            for (int i = 0; i < capacity; i++)
            {
                int v = i * 4;
                uv[v + 0] = new Vector2(0f, 0f);
                uv[v + 1] = new Vector2(1f, 0f);
                uv[v + 2] = new Vector2(1f, 1f);
                uv[v + 3] = new Vector2(0f, 1f);

                int t = i * 6;
                triangles[t + 0] = v + 0; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 0; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }

            mesh = new Mesh { name = "StunStars" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
            GetComponent<MeshFilter>().sharedMesh = mesh;

            // Аддитивный: звёзды — это свет, и на любом фоне они должны
            // оставаться жёлтыми искрами, а не бледными наклейками.
            var shader = Shader.Find("WarfareSurvivor/AdditiveTracer");
            var material = new Material(shader) { name = "StunStar", mainTexture = StarTexture() };
            if (material.HasProperty("_Boost")) material.SetFloat("_Boost", 1.6f);

            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }

        /// <summary>
        /// Пятиконечная звезда с круглым ореолом.
        ///
        /// Ореол не по форме звезды, а круглый: на игровой дистанции звезда
        /// занимает десяток пикселей, и точный контур свечения там всё равно
        /// не различить, а круглое сияние читается как «горит».
        /// </summary>
        static Texture2D StarTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "StunStar", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };

            // Десять углов: пять внешних и пять внутренних.
            var poly = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float r = (i % 2 == 0) ? 0.92f : 0.38f;
                poly[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Четыре подпробы на пиксель: без них края звезды
                    // получаются рваными, и с расстояния она рассыпается.
                    float inside = 0f;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float u = (x + 0.25f + sx * 0.5f) / size * 2f - 1f;
                            float v = (y + 0.25f + sy * 0.5f) / size * 2f - 1f;
                            if (InPolygon(poly, u, v)) inside += 0.25f;
                        }

                    float ux = (x + 0.5f) / size * 2f - 1f;
                    float uy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(ux * ux + uy * uy);
                    float glow = r >= 1f ? 0f : Mathf.Pow(1f - r, 3f) * 0.5f;

                    float a = Mathf.Clamp01(inside + glow);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        static bool InPolygon(Vector2[] poly, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > y) == (poly[j].y > y)) continue;
                float t = (y - poly[i].y) / (poly[j].y - poly[i].y);
                if (x < poly[i].x + t * (poly[j].x - poly[i].x)) inside = !inside;
            }
            return inside;
        }

        void LateUpdate()
        {
            if (config == null || mesh == null) return;
            if (view == null) view = Camera.main;

            count = 0;

            int perZombie = Mathf.Max(1, config.stunStarCount);
            var right = view != null ? view.transform.right : Vector3.right;
            var up = view != null ? view.transform.up : Vector3.up;

            var zombies = Registry.Zombies;
            for (int i = 0; i < zombies.Count && count < capacity; i++)
            {
                var zombie = zombies[i];
                if (zombie == null || zombie.IsDead || !zombie.Stunned) continue;

                // Венчик садится НАД головой, и высота считается от роста
                // самого зомби: здоровяк вдвое выше обычного, и общая
                // высота в метрах повисла бы у него на груди.
                var centre = zombie.HitPoint + Vector3.up * (config.stunStarHeight * zombie.transform.localScale.y);
                float orbit = config.stunOrbitRadius * zombie.transform.localScale.x;
                float phase = Time.time * config.stunOrbitSpeed + zombie.GetInstanceID() * 0.37f;

                for (int k = 0; k < perZombie && count < capacity; k++)
                {
                    float a = phase + k * Mathf.PI * 2f / perZombie;

                    // Эллипс: по горизонтали полный радиус, по вертикали
                    // сплюснутый — так орбита читается лежащей вокруг головы,
                    // а не стоящей колесом.
                    var at = centre
                             + right * (Mathf.Cos(a) * orbit)
                             + up * (Mathf.Sin(a) * orbit * 0.32f);

                    // Дальняя половина орбиты мельче и тусклее.
                    float depth = (Mathf.Sin(a) + 1f) * 0.5f;
                    float scale = Mathf.Lerp(0.65f, 1f, 1f - depth) * config.stunStarSize
                                  * zombie.transform.localScale.x;

                    var colour = config.stunStarColor;
                    colour.a *= Mathf.Lerp(0.45f, 1f, 1f - depth);

                    Quad(at, right * (scale * 0.5f), up * (scale * 0.5f), colour);
                }
            }

            for (int i = count; i < capacity; i++)
            {
                int v = i * 4;
                vertices[v] = vertices[v + 1] = vertices[v + 2] = vertices[v + 3] = Vector3.zero;
            }

            mesh.vertices = vertices;
            mesh.colors = colors;
            if (meshRenderer != null) meshRenderer.enabled = count > 0;
        }

        void Quad(Vector3 centre, Vector3 right, Vector3 up, Color colour)
        {
            int v = count * 4;

            vertices[v + 0] = centre - right - up;
            vertices[v + 1] = centre + right - up;
            vertices[v + 2] = centre + right + up;
            vertices[v + 3] = centre - right + up;

            colors[v + 0] = colors[v + 1] = colors[v + 2] = colors[v + 3] = colour;
            count++;
        }
    }
}
