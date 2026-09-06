using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Пламя: струя огнемёта, языки вокруг неё и огоньки на горящих зомби.
    ///
    /// Струя — это ЛЕНТА от дула к кончику, а не россыпь клубов. Первая
    /// версия рисовала огонь мягкими круглыми клубами, и он читался
    /// дымкой: у клуба нет ни формы, ни края. Огонь узнают по рваному
    /// краю и языкам, а их даёт шум в шейдере — лента только несёт
    /// силуэт, по которому этот шум ползёт от дула наружу.
    ///
    /// Языки поверх ленты — те же силуэты, только вытянутые по направлению
    /// полёта и сгорающие клочьями. Одна система на струю, взрыв ракеты
    /// и горящего зомби нарочно: огонь на враге — это тот же огонь, только
    /// меньше, и если рисовать их разным, поджиг перестаёт читаться
    /// как следствие струи.
    /// </summary>
    public class FlameJet : MonoBehaviour
    {
        static readonly List<FlameJet> All = new List<FlameJet>();
        static readonly Dictionary<object, FlameStream> Streams = new Dictionary<object, FlameStream>();
        static ArenaConfig config;
        static Camera view;
        static Transform root;
        static Material material;
        static Material streamMaterial;
        static Mesh tongueMesh;
        static int nextVictim;

        // Каждый язык шумит со своего места, иначе они — копии друг друга.
        static readonly System.Random offsets = new System.Random(7);

        Mesh mesh;
        readonly Vector3[] tongues = new Vector3[4];

        Vector3 velocity;
        float bornTime;
        float dieTime;
        float startSize;
        float endSize;
        float stretch;
        Vector2 noiseShift;

        public static void Configure(ArenaConfig cfg, Camera camera)
        {
            config = cfg;
            view = camera;

            if (root == null)
            {
                root = new GameObject("FlameJets").transform;
                All.Clear();
                Streams.Clear();
            }

            ApplyLook();
        }

        /// <summary>
        /// Переносит настройки пламени из конфига в материал.
        ///
        /// Вызывается при старте и при каждом появлении струи: цвета
        /// и пороги крутят в инспекторе прямо во время игры, и ждать
        /// перезапуска сцены, чтобы увидеть результат, — значит крутить
        /// вслепую.
        /// </summary>
        static void ApplyLook()
        {
            if (config == null) return;
            Tune(FlameMaterial());
            Tune(StreamMaterial());
        }

        static void Tune(Material m)
        {
            m.SetVector("_NoiseScale", new Vector4(config.flameNoiseScale.x, config.flameNoiseScale.y, 0f, 0f));
            m.SetFloat("_Flow", config.flameFlow);
            m.SetFloat("_BandMid", config.flameBandMid);
            m.SetFloat("_BandCore", config.flameBandCore);
            m.SetFloat("_Soft", config.flameSoftness);
            m.SetColor("_CoreColor", config.flameCoreColor);
            m.SetColor("_MidColor", config.flameColor);
            m.SetColor("_EdgeColor", config.flameEmberColor);
        }

        // --- струя ----------------------------------------------------------

        /// <summary>
        /// Струя работающего огнемёта. Зовётся КАЖДЫЙ КАДР, пока он горит:
        /// лента перестраивается под текущее направление и жар, а если
        /// её перестали кормить — гаснет сама.
        ///
        /// Владелец — ключ: у каждого огнемётчика своя лента, и сколько
        /// их в отряде, столько и лент.
        /// </summary>
        public static void Stream(object owner, Vector3 origin, Vector3 forward,
                                  float reach, float angleDegrees, float heat)
        {
            if (config == null || root == null || owner == null) return;

            if (!Streams.TryGetValue(owner, out var stream) || stream == null)
            {
                stream = FlameStream.Create(root, StreamMaterial());
                Streams[owner] = stream;
                ApplyLook();
            }

            stream.Feed(config, view, origin, forward, reach, angleDegrees, heat);
        }

        // --- языки ----------------------------------------------------------

        /// <summary>
        /// Один язык работающей струи.
        ///
        /// Языки рождаются в дальней половине струи и летят дальше неё:
        /// лента даёт телу огня форму, языки — движение и то, что
        /// огонь не кончается ровно по линейке.
        /// </summary>
        public static void Puff(Vector3 origin, Vector3 forward, float reach, float angleDegrees, float heat)
        {
            if (config == null || root == null) return;

            // Языки рождаются ВНУТРИ ленты, в её дальней половине, и летят
            // дальше по струе. Первая версия раскидывала их по всему конусу
            // класса, и огонь читался веером вокруг отряда, а не струёй.
            float along = 0.55f + 0.4f * Mathf.Sqrt(Random.value);
            float width = FlameStream.WidthAt(config, reach, angleDegrees, heat, Mathf.Clamp01(along));

            forward.y = 0f;
            forward.Normalize();
            var side = Vector3.Cross(Vector3.up, forward);
            var at = origin + forward * (reach * along) + side * Random.Range(-width * 0.5f, width * 0.5f);

            // Медленно и недалеко: язык — это лепесток, отслоившийся от
            // струи, а не снаряд. Быстрые языки улетали за метры от неё
            // и ложились на землю пятнами, которые читались лужами.
            float life = config.flameLife * Random.Range(0.6f, 0.9f);
            float speed = reach / Mathf.Max(0.05f, config.flameLife) * Random.Range(0.15f, 0.3f);
            float size = config.flameSize * Mathf.Lerp(0.45f, 1f, heat);

            // Чуть в сторону: языки на краю струи расходятся, как у факела.
            var direction = (forward + side * Random.Range(-0.35f, 0.35f)).normalized;

            Spawn(at, direction * speed + Vector3.up * Random.Range(0.4f, 1f),
                  size * Random.Range(0.6f, 0.9f),
                  size * Random.Range(0.9f, 1.25f),
                  Random.Range(1.15f, 1.5f),
                  life);
        }

        /// <summary>Огонёк на горящем: тот же язык, мельче и вверх.</summary>
        public static void Wisp(Vector3 at)
        {
            if (config == null || root == null) return;

            Spawn(at + Random.insideUnitSphere * 0.25f,
                  Vector3.up * Random.Range(0.9f, 1.6f),
                  config.flameSize * 0.4f,
                  config.flameSize * 0.75f,
                  Random.Range(1.3f, 1.8f),
                  config.flameLife * 0.9f);
        }

        static void Spawn(Vector3 at, Vector3 velocity, float from, float to, float stretch, float life)
        {
            var tongue = Rent();
            if (tongue == null) return;

            tongue.transform.position = at;
            tongue.velocity = velocity;
            tongue.startSize = from;
            tongue.endSize = to;
            tongue.stretch = stretch;
            tongue.bornTime = Time.time;
            tongue.dieTime = Time.time + Mathf.Max(0.05f, life);
            tongue.noiseShift = new Vector2((float)offsets.NextDouble() * 8f, (float)offsets.NextDouble() * 8f);
            tongue.Face();
            tongue.Shape(0f);
            tongue.gameObject.SetActive(true);
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
            var go = new GameObject("FlameTongue");
            go.transform.SetParent(root, false);

            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.sharedMaterial = FlameMaterial();

            var tongue = go.AddComponent<FlameJet>();

            // Меш у каждого языка свой: в нём живут сдвиг шума и эрозия,
            // а они у языков разные.
            tongue.mesh = Object.Instantiate(TongueMesh());
            tongue.mesh.name = "FlameTongue";
            tongue.mesh.MarkDynamic();
            filter.sharedMesh = tongue.mesh;

            go.SetActive(false);
            return tongue;
        }

        /// <summary>
        /// Квад языка: основание в нуле, растёт вверх по локальной оси Y.
        /// Пивот у основания, а не в центре: язык растёт от точки, где
        /// родился, а не раздувается вокруг неё.
        /// </summary>
        static Mesh TongueMesh()
        {
            if (tongueMesh != null) return tongueMesh;

            tongueMesh = new Mesh { name = "FlameTongueBase" };
            tongueMesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
                new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f)
            };
            tongueMesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            tongueMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            tongueMesh.SetUVs(1, new List<Vector3> { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero });
            tongueMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            tongueMesh.RecalculateBounds();
            return tongueMesh;
        }

        // --- материал и текстуры --------------------------------------------

        static Material FlameMaterial()
        {
            if (material != null) return material;

            var shader = Shader.Find("WarfareSurvivor/Flame");
            if (shader == null)
            {
                Debug.LogError("[FlameJet] Не нашёлся шейдер WarfareSurvivor/Flame — " +
                               "проверь, что он включён в сборку (ShaderInclusion).");
                shader = Shader.Find("WarfareSurvivor/GlowSprite");
            }

            material = new Material(shader) { name = "Flame", mainTexture = Teardrop() };
            material.SetTexture("_Noise", Noise());
            return material;
        }

        /// <summary>
        /// Материал ленты: тот же шейдер, но силуэт струи, а не капли.
        /// Капля на ленте давала сплошную белую полосу по оси — ядро
        /// у капли тянется во всю длину. У струи ядро должно мерцать.
        /// </summary>
        static Material StreamMaterial()
        {
            if (streamMaterial != null) return streamMaterial;

            streamMaterial = new Material(FlameMaterial()) { name = "FlameStream", mainTexture = JetMask() };
            return streamMaterial;
        }

        /// <summary>
        /// Силуэт струи: x — поперёк, y — от дула к кончику.
        ///
        /// Потолок маски НИЖЕ единицы нарочно: ядро в шейдере появляется
        /// там, где маска перекрывает шум с запасом, и при потолке в 0.95
        /// оно вспыхивает лишь на впадинах шума — то есть мерцает, как
        /// сердцевина настоящего факела, а не лежит белой полосой.
        /// </summary>
        static Texture2D JetMask()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "FlameJetMask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;

                // От дула плотно, к кончику всё реже: у конца остаётся
                // только край и середина, ядро гаснет раньше.
                float along = Mathf.Lerp(0.95f, 0.45f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, v)));

                for (int x = 0; x < size; x++)
                {
                    float u = Mathf.Abs((x + 0.5f) / size * 2f - 1f);
                    float across = Mathf.Pow(Mathf.Clamp01(1f - u), 0.8f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, across * along);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Силуэт языка: капля остриём вверх. Альфа — единица по оси,
        /// ноль по краю: по ней шейдер режет цветные ленты, и ядро
        /// ложится вдоль оси, а край темнеет.
        /// </summary>
        static Texture2D Teardrop()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "FlameTeardrop", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;

                // Ширина: круглое основание, самое широкое в нижней трети,
                // к вершине сходится в остриё.
                // Показатель ниже единицы — как у купола кислоты: широкое
                // тело почти до самого верха, остриё короткое.
                float width = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01((v + 0.12f) / 1.2f)), 0.5f);

                for (int x = 0; x < size; x++)
                {
                    float u = Mathf.Abs((x + 0.5f) / size * 2f - 1f);
                    float across = width > 0.001f ? u / width : 2f;
                    float a = Mathf.Clamp01(1f - across * across);

                    // К острию силуэт слабеет: там остаётся только край,
                    // а ядро гаснет раньше, чем язык кончается.
                    a *= 1f - v * 0.35f;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Бесшовный шум: три октавы, склеенные по краям. Бесшовность
        /// обязательна — шум ТЕЧЁТ, и любой шов проезжал бы по языку
        /// раз в секунду ровной полосой.
        /// </summary>
        public static Texture2D Noise()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "FlameNoise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear
            };

            var values = new float[size * size];
            float min = float.MaxValue, max = float.MinValue;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;

                    // Четыре выборки по углам тора, смешанные билинейно:
                    // так края сходятся сами.
                    float n = Fbm(u, v) * (1f - u) * (1f - v)
                            + Fbm(u + 1f, v) * u * (1f - v)
                            + Fbm(u, v + 1f) * (1f - u) * v
                            + Fbm(u + 1f, v + 1f) * u * v;

                    values[y * size + x] = n;
                    min = Mathf.Min(min, n);
                    max = Mathf.Max(max, n);
                }

            var pixels = new Color32[size * size];
            for (int i = 0; i < values.Length; i++)
            {
                float n = Mathf.InverseLerp(min, max, values[i]);
                byte b = (byte)Mathf.RoundToInt(n * 255f);
                pixels[i] = new Color32(b, b, b, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        static float Fbm(float u, float v)
        {
            // Две октавы, а не три, и крупный период: мелкий шум давал
            // рваные, «живописные» языки, а игра рисована плоско и крупно —
            // клубы кислоты у плевуна гладкие, в два тона. Огонь должен
            // быть из той же коробки: большие округлые лепестки.
            const float period = 2f;
            float sum = 0f, amp = 1f, freq = period;
            for (int o = 0; o < 2; o++)
            {
                sum += Mathf.PerlinNoise(u * freq + 11.3f, v * freq + 7.1f) * amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return sum;
        }

        // --- жизнь языка ----------------------------------------------------

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

            Face();
            Shape(life);
        }

        /// <summary>
        /// Разворот к камере с наклоном по полёту: язык вытянут туда, куда
        /// летит. Круглый клуб не показывает направления вовсе — а огнемёт
        /// узнают именно по тому, что огонь идёт НАРУЖУ.
        /// </summary>
        void Face()
        {
            if (view == null) { transform.rotation = Quaternion.identity; return; }

            var cam = view.transform;
            var flat = new Vector2(Vector3.Dot(velocity, cam.right), Vector3.Dot(velocity, cam.up));
            float angle = flat.sqrMagnitude > 0.0001f ? Mathf.Atan2(flat.y, flat.x) * Mathf.Rad2Deg - 90f : 0f;
            transform.rotation = cam.rotation * Quaternion.Euler(0f, 0f, angle);
        }

        void Shape(float life)
        {
            float size = Mathf.Lerp(startSize, endSize, life);
            transform.localScale = new Vector3(size, size * stretch, 1f);

            // Эрозия растёт с возрастом: язык не тает, а сгорает клочьями.
            float erosion = Mathf.Pow(life, 1.3f) * 0.95f;
            var t = new Vector3(noiseShift.x, noiseShift.y, erosion);
            for (int i = 0; i < 4; i++) tongues[i] = t;
            mesh.SetUVs(1, tongues);
        }
    }

    /// <summary>
    /// Лента струи: полоса от дула к кончику, развёрнутая к камере вокруг
    /// своей оси. Расширяется по конусу класса, у кончика мотается
    /// и сгорает в языки — эрозия растёт вдоль ленты.
    /// </summary>
    public class FlameStream : MonoBehaviour
    {
        const int Segments = 14;

        Mesh mesh;
        float lastFed = -1f;

        readonly Vector3[] vertices = new Vector3[(Segments + 1) * 2];
        readonly Vector2[] uv = new Vector2[(Segments + 1) * 2];
        readonly Vector3[] tongue = new Vector3[(Segments + 1) * 2];
        readonly Color[] colors = new Color[(Segments + 1) * 2];
        float noiseShift;

        public static FlameStream Create(Transform root, Material material)
        {
            var go = new GameObject("FlameStream");
            go.transform.SetParent(root, false);

            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.sharedMaterial = material;

            var stream = go.AddComponent<FlameStream>();
            stream.noiseShift = Random.value * 8f;
            stream.mesh = new Mesh { name = "FlameStream" };
            stream.mesh.MarkDynamic();

            var triangles = new int[Segments * 6];
            for (int s = 0; s < Segments; s++)
            {
                int a = s * 2, b = a + 1, c = a + 2, d = a + 3;
                int t = s * 6;
                triangles[t] = a; triangles[t + 1] = c; triangles[t + 2] = b;
                triangles[t + 3] = c; triangles[t + 4] = d; triangles[t + 5] = b;
            }

            stream.mesh.vertices = stream.vertices;
            stream.mesh.uv = stream.uv;
            stream.mesh.colors = stream.colors;
            stream.mesh.SetUVs(1, stream.tongue);
            stream.mesh.triangles = triangles;
            filter.sharedMesh = stream.mesh;

            go.SetActive(false);
            return stream;
        }

        /// <summary>
        /// Ширина струи на доле её длины. Одна формула на ленту и языки:
        /// языки должны рождаться внутри ленты, а не рядом с ней.
        ///
        /// У кончика — по конусу класса, но не шире настройки: картинка
        /// чуть уже зоны поражения, чтобы край не обещал урона, которого нет.
        /// </summary>
        public static float WidthAt(ArenaConfig config, float reach, float angleDegrees, float heat, float t)
        {
            float coneWidth = 2f * reach * Mathf.Tan(Mathf.Clamp(angleDegrees, 1f, 170f) * 0.5f * Mathf.Deg2Rad);
            float tipWidth = Mathf.Min(coneWidth, config.flameJetWidth) * Mathf.Lerp(0.55f, 1f, heat);
            float baseWidth = Mathf.Min(0.35f, tipWidth * 0.5f);
            return Mathf.Lerp(baseWidth, tipWidth, t);
        }

        public void Feed(ArenaConfig config, Camera view, Vector3 origin, Vector3 forward,
                         float reach, float angleDegrees, float heat)
        {
            lastFed = Time.time;
            if (reach < 0.05f || heat <= 0.001f) { gameObject.SetActive(false); return; }

            gameObject.SetActive(true);

            forward.y = 0f;
            forward.Normalize();

            var toCam = view != null ? (view.transform.position - origin) : Vector3.up;
            var side = Vector3.Cross(forward, toCam);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(forward, Vector3.up);
            side.Normalize();

            // Мотание: у дула струя прибита к стволу, у кончика гуляет.
            float wag = config.flameWag;
            float time = Time.time;

            for (int s = 0; s <= Segments; s++)
            {
                float t = (float)s / Segments;
                float width = WidthAt(config, reach, angleDegrees, heat, t);

                var centre = origin + forward * (reach * t) + Vector3.up * (0.15f * t);
                centre += side * (Mathf.Sin(time * 7.3f + t * 6f + noiseShift) * wag * t * t);
                centre += Vector3.up * (Mathf.Sin(time * 9.1f + t * 5f) * wag * 0.4f * t);

                var half = side * (width * 0.5f);
                int i = s * 2;
                vertices[i] = centre - half;
                vertices[i + 1] = centre + half;

                uv[i] = new Vector2(0f, t);
                uv[i + 1] = new Vector2(1f, t);

                // Эрозия: с середины ленты силуэт начинает сгорать, к кончику
                // от него остаются одни языки.
                float erosion = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 1.05f, t)) * 0.95f;
                tongue[i] = tongue[i + 1] = new Vector3(noiseShift, 0f, erosion);

                // У самого дула — прозрачно: струя выходит из ствола, а не
                // рисуется поверх него.
                float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.12f, t));
                colors[i] = colors[i + 1] = new Color(1f, 1f, 1f, fadeIn);
            }

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.SetUVs(1, tongue);
            mesh.RecalculateBounds();
        }

        void LateUpdate()
        {
            // Перестали кормить — гаснем. Кадр-другой без вызова случается
            // при смене цели, поэтому не сразу.
            if (Time.time - lastFed > 0.1f) gameObject.SetActive(false);
        }
    }
}
