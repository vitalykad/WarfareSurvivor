using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Пламя: струя огнемёта, клубы вокруг неё и огоньки на горящих зомби.
    ///
    /// Огонь рисуется МЯГКИМИ КЛУБАМИ — теми же косинусными куполами, что
    /// и кислота плевуна, на том же шейдере. Одна коробка на все эффекты:
    /// версия с шумом и тремя лентами цвета выглядела как из комикса
    /// и рядом с кислотой читалась чужой.
    ///
    /// Форма взята с настоящего огнемёта: у дула струя узкая и белая, дальше
    /// расходится в широкое рыхлое оранжевое облако, а на излёте темнеет
    /// и растворяется в дым. Поэтому клуб окрашивается и размеряется по тому,
    /// ГДЕ вдоль струи он родился, а не только по возрасту: у дула — мелкий
    /// и белый, у конца — крупный и тёмный. У дула поверх клубов лежит тонкий
    /// светлый луч — то самое белое ядро, по которому огнемёт и узнают.
    ///
    /// Одна система на струю, взрыв ракеты и горящего зомби нарочно: огонь
    /// на враге — это тот же огонь, только меньше, и если рисовать их разным,
    /// поджиг перестаёт читаться как следствие струи.
    /// </summary>
    public class FlameJet : MonoBehaviour
    {
        static readonly List<FlameJet> All = new List<FlameJet>();
        static readonly Dictionary<object, FlameStream> Streams = new Dictionary<object, FlameStream>();
        static ArenaConfig config;
        static Camera view;
        static Transform root;
        static Material material;
        static Mesh puffMesh;
        static int nextVictim;

        Mesh mesh;
        readonly Color[] corners = new Color[4];

        Vector3 velocity;
        float bornTime;
        float dieTime;
        float startSize;
        float endSize;
        float stretch;
        Color bornColor;
        Color deadColor;

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
        }

        // --- струя ----------------------------------------------------------

        /// <summary>
        /// Струя работающего огнемёта. Зовётся КАЖДЫЙ КАДР, пока он горит:
        /// луч у дула перестраивается под направление и жар, а если его
        /// перестали кормить — гаснет сам.
        /// </summary>
        public static void Stream(object owner, Vector3 origin, Vector3 forward,
                                  float reach, float angleDegrees, float heat)
        {
            if (config == null || root == null || owner == null) return;

            if (!Streams.TryGetValue(owner, out var stream) || stream == null)
            {
                stream = FlameStream.Create(root, FlameMaterial());
                Streams[owner] = stream;
            }

            stream.Feed(config, view, origin, forward, reach, angleDegrees, heat);
        }

        // --- клубы ----------------------------------------------------------

        /// <summary>
        /// Один клуб работающей струи.
        ///
        /// Рождается где-то вдоль струи, и место рождения решает всё: цвет,
        /// размер, время жизни. Клубы летят ОТ БОЙЦА и растут по дороге —
        /// огнемёт узнаётся по движению наружу, неподвижный конус читается
        /// нарисованной зоной, как у кислоты.
        /// </summary>
        public static void Puff(Vector3 origin, Vector3 forward, float reach, float angleDegrees, float heat)
        {
            if (config == null || root == null) return;

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();

            // Равномерно по длине: у дула клубов столько же, сколько у конца,
            // но у дула они мелкие и плотные, у конца — крупные и редкие
            // на вид. Так и выглядит факел: узкая яркая ножка, широкая шапка.
            float along = Random.Range(0.08f, 1f);
            float width = FlameStream.WidthAt(config, reach, angleDegrees, heat, along);

            var side = Vector3.Cross(Vector3.up, forward);
            var at = origin + forward * (reach * along)
                            + side * Random.Range(-width * 0.5f, width * 0.5f)
                            + Vector3.up * (0.1f * along);

            // Цвет по месту: белое ядро у дула, оранжевое тело, тёмный уголь
            // на излёте. К смерти каждый клуб ещё темнеет и растворяется —
            // это и есть дым.
            var born = RampAt(along);

            // Умирая, ближние клубы остаются огнём и просто тают; в дым
            // уходят только дальние. Иначе три четверти струи с рождения
            // были бурыми, и огнемёт читался дымовой шашкой.
            var dead = Color.Lerp(born, config.flameEmberColor, Mathf.Lerp(0.25f, 0.9f, along));
            dead.a = 0f;

            // Крупно и с запасом: клубы должны ПЕРЕКРЫВАТЬСЯ, иначе вместо
            // облака выходит редкая дымка. С предумноженной альфой наложение
            // не выбеливает — оно сгущает цвет, и это то, что нужно.
            float size = config.flameSize * Mathf.Lerp(0.45f, 1f, heat);
            float from = size * Mathf.Lerp(0.35f, 0.8f, along) * Random.Range(0.85f, 1.15f);
            float to = size * Mathf.Lerp(1f, 2.4f, along) * Random.Range(0.85f, 1.15f);

            // Дальние клубы живут дольше и летят медленнее: облако у конца
            // должно висеть и расплываться, а не улетать за струю.
            float life = config.flameLife * Random.Range(0.9f, 1.3f) * Mathf.Lerp(0.8f, 1.4f, along);
            float speed = reach / Mathf.Max(0.05f, config.flameLife) * Random.Range(0.35f, 0.55f) * (1f - along * 0.5f);

            var direction = (forward + side * Random.Range(-0.25f, 0.25f) * along).normalized;
            Spawn(at, direction * speed + Vector3.up * Random.Range(0.3f, 1f),
                  from, to, Random.Range(1.15f, 1.45f), life, born, dead);
        }

        /// <summary>Огонёк на горящем: тот же клуб, мельче и вверх.</summary>
        public static void Wisp(Vector3 at)
        {
            if (config == null || root == null) return;

            var born = Color.Lerp(config.flameCoreColor, config.flameColor, 0.6f);
            var dead = config.flameEmberColor;
            dead.a = 0f;

            Spawn(at + Random.insideUnitSphere * 0.25f,
                  Vector3.up * Random.Range(0.9f, 1.6f),
                  config.flameSize * 0.3f,
                  config.flameSize * 0.8f,
                  Random.Range(1.2f, 1.6f),
                  config.flameLife * 0.9f, born, dead);
        }

        /// <summary>
        /// Цвет струи на доле её длины: белое ядро коротко, оранжевое тело
        /// держится до самого конца, уголь — только на излёте. Как на снимке
        /// настоящего огнемёта: тёмное там лишь то, что уже отгорело.
        /// </summary>
        static Color RampAt(float along)
        {
            const float coreEnd = 0.2f;
            const float bodyEnd = 0.72f;
            if (along < coreEnd) return Color.Lerp(config.flameCoreColor, config.flameColor, along / coreEnd);
            if (along < bodyEnd) return config.flameColor;
            return Color.Lerp(config.flameColor, config.flameEmberColor, (along - bodyEnd) / (1f - bodyEnd));
        }

        static void Spawn(Vector3 at, Vector3 velocity, float from, float to, float stretch, float life,
                          Color born, Color dead)
        {
            var puff = Rent();
            if (puff == null) return;

            puff.transform.position = at;
            puff.velocity = velocity;
            puff.startSize = from;
            puff.endSize = to;
            puff.stretch = stretch;
            puff.bornTime = Time.time;
            puff.dieTime = Time.time + Mathf.Max(0.05f, life);
            puff.bornColor = born;
            puff.deadColor = dead;
            puff.Face();
            puff.Shape(0f);
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
            puff.mesh = Object.Instantiate(PuffMesh());
            puff.mesh.name = "FlamePuff";
            puff.mesh.MarkDynamic();
            filter.sharedMesh = puff.mesh;

            go.SetActive(false);
            return puff;
        }

        /// <summary>Квад клуба с центром в нуле: растёт вокруг точки рождения.</summary>
        static Mesh PuffMesh()
        {
            if (puffMesh != null) return puffMesh;

            puffMesh = new Mesh { name = "FlamePuffBase" };
            puffMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f)
            };
            puffMesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            puffMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            puffMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            puffMesh.RecalculateBounds();
            return puffMesh;
        }

        // --- материал и текстура --------------------------------------------

        static Material FlameMaterial()
        {
            if (material != null) return material;

            // Тот же шейдер, что у кислоты: предумноженная альфа, плотное
            // ядро держит цвет на песке, редкий край прибавляет свет.
            var shader = Shader.Find("WarfareSurvivor/GlowSprite");
            material = new Material(shader) { name = "Flame", mainTexture = Blob() };
            return material;
        }

        /// <summary>
        /// Косинусный купол — как у клуба кислоты, и показатель ниже единицы
        /// по той же причине: плотная часть занимает почти весь клуб, иначе
        /// облако выходит дымкой, а не огнём.
        /// </summary>
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
                    float density = r >= 1f ? 0f : 0.5f + 0.5f * Mathf.Cos(r * Mathf.PI);

                    // Ещё площе, чем у кислоты: огонь плотнее облака, и
                    // плотная часть должна доходить почти до края клуба.
                    float a = Mathf.Pow(density, 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        // --- жизнь клуба ----------------------------------------------------

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
        /// К камере, с лёгким наклоном по полёту: клуб чуть вытянут туда,
        /// куда летит. Чуть — потому что сильно вытянутый клуб перестаёт
        /// быть куполом и снова становится языком из комикса.
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

            // Цвет уходит к дыму, прозрачность — по квадрату: клуб держится
            // плотным большую часть жизни и растворяется в самом конце.
            var color = Color.Lerp(bornColor, deadColor, life);
            color.a = bornColor.a * (1f - life * life);

            for (int i = 0; i < corners.Length; i++) corners[i] = color;
            mesh.colors = corners;
        }
    }

    /// <summary>
    /// Луч у дула: тонкая светлая полоса, развёрнутая к камере вокруг своей
    /// оси. Это белое ядро настоящего огнемёта — оно короткое и гаснет
    /// там, где струя расходится в облако.
    /// </summary>
    public class FlameStream : MonoBehaviour
    {
        const int Segments = 10;

        Mesh mesh;
        float lastFed = -1f;
        float noiseShift;

        readonly Vector3[] vertices = new Vector3[(Segments + 1) * 2];
        readonly Vector2[] uv = new Vector2[(Segments + 1) * 2];
        readonly Color[] colors = new Color[(Segments + 1) * 2];

        public static FlameStream Create(Transform root, Material material)
        {
            var go = new GameObject("FlameCore");
            go.transform.SetParent(root, false);

            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.sharedMaterial = material;

            var stream = go.AddComponent<FlameStream>();
            stream.noiseShift = Random.value * 8f;
            stream.mesh = new Mesh { name = "FlameCore" };
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
            stream.mesh.triangles = triangles;
            filter.sharedMesh = stream.mesh;

            go.SetActive(false);
            return stream;
        }

        /// <summary>
        /// Ширина струи на доле её длины. Одна формула на луч и клубы:
        /// клубы должны рождаться внутри струи, а не рядом с ней.
        ///
        /// У кончика — по конусу класса, но не шире настройки: картинка
        /// чуть уже зоны поражения, чтобы край не обещал урона, которого нет.
        /// </summary>
        public static float WidthAt(ArenaConfig config, float reach, float angleDegrees, float heat, float t)
        {
            float coneWidth = 2f * reach * Mathf.Tan(Mathf.Clamp(angleDegrees, 1f, 170f) * 0.5f * Mathf.Deg2Rad);
            float tipWidth = Mathf.Min(coneWidth, config.flameJetWidth) * Mathf.Lerp(0.55f, 1f, heat);
            float baseWidth = Mathf.Min(0.3f, tipWidth * 0.4f);

            // Расходится не по прямой, а по корню: узкая ножка держится
            // дольше, шапка распахивается ближе к концу — как на снимке.
            return Mathf.Lerp(baseWidth, tipWidth, Mathf.Sqrt(Mathf.Clamp01(t)));
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

            // Луч короче струи: он гаснет там, где начинается облако.
            float coreLength = Mathf.Clamp01(config.flameCoreLength);
            float length = reach * coreLength;
            float wag = config.flameWag;
            float time = Time.time;

            var core = config.flameCoreColor;
            var white = Color.Lerp(core, Color.white, 0.6f);

            for (int s = 0; s <= Segments; s++)
            {
                float t = (float)s / Segments;

                // Толщина луча — доля от ширины струи в этом месте.
                float width = WidthAt(config, reach, angleDegrees, heat, t * coreLength) * 0.55f;

                var centre = origin + forward * (length * t) + Vector3.up * (0.1f * t);
                centre += side * (Mathf.Sin(time * 7.3f + t * 6f + noiseShift) * wag * t * t);

                var half = side * (width * 0.5f);
                int i = s * 2;
                vertices[i] = centre - half;
                vertices[i + 1] = centre + half;

                uv[i] = new Vector2(0f, t);
                uv[i + 1] = new Vector2(1f, t);

                // Белый у дула, к концу луча — цвет ядра и прозрачность:
                // луч растворяется в облаке, а не обрывается.
                float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.15f, t));
                float fadeOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, t));
                var color = Color.Lerp(white, core, t);
                color.a = fadeIn * fadeOut * Mathf.Lerp(0.6f, 1f, heat);
                colors[i] = colors[i + 1] = color;
            }

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.RecalculateBounds();
        }

        void LateUpdate()
        {
            if (Time.time - lastFed > 0.1f) gameObject.SetActive(false);
        }
    }
}
