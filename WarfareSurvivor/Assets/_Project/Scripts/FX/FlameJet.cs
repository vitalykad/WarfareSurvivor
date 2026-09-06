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
        static Material streamMaterial;
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
                stream = FlameStream.Create(root, StreamMaterial());
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

            // Место рождения: как далеко по струе и как далеко от её оси.
            float along = Random.Range(0.05f, 0.95f);
            float width = FlameStream.WidthAt(config, reach, angleDegrees, heat, Mathf.Clamp01(along));

            // Поперёк — с перекосом к оси: у настоящей струи по краям клубов
            // меньше, чем в середине, поэтому и края у неё рваные.
            float across = Random.Range(-1f, 1f);
            across = Mathf.Sign(across) * across * across;

            var side = Vector3.Cross(Vector3.up, forward);

            // ЖАР В ТОЧКЕ: единица у дула по оси, ноль на краю в конце.
            // По нему считается всё — цвет, размер, высота, скорость.
            // Именно эта поперечная слоистость и делает струю струёй:
            // белое ядро, жёлтая сердцевина, красная оболочка. Когда цвет
            // зависел только от длины, слоёв не было, и огонь читался
            // одноцветной пеной.
            float hot = Mathf.Clamp01((1f - along * 0.72f) * (1f - Mathf.Abs(across) * 0.5f));

            var at = origin + forward * (reach * along)
                            + side * (across * width * 0.5f)
                            // Горячее — выше: камера смотрит сверху, и так
                            // ядро рисуется ПОВЕРХ оболочки без сортировки.
                            + Vector3.up * (0.05f + hot * 0.35f);

            var born = HeatColor(hot);
            var dead = HeatColor(hot * 0.35f);
            dead.a = 0f;

            // Холодные клубы крупнее: оболочка и дым должны обнимать ядро,
            // а не тонуть в нём.
            float size = config.flameSize * Mathf.Lerp(0.5f, 1f, heat);
            float grow = Mathf.Lerp(1.25f, 0.9f, hot);
            float from = size * Mathf.Lerp(0.6f, 0.32f, hot) * Random.Range(0.8f, 1.2f);
            float to = from * grow * Random.Range(0.9f, 1.2f);

            // Дым живёт лишь немногим дольше огня: если дать ему жить вдвое,
            // он копится и застилает пол-арены бурым, а огня за ним не видно.
            float life = config.flameLife * Random.Range(0.8f, 1.2f) * Mathf.Lerp(1.05f, 0.75f, hot);
            float speed = reach / Mathf.Max(0.05f, config.flameLife) * Random.Range(0.3f, 0.5f) * (1f - along * 0.45f);

            // Вбок почти не расходятся: струя должна оставаться струёй,
            // а не растекаться в лужу тумана по всему двору.
            var direction = (forward + side * Random.Range(-0.12f, 0.12f) * along).normalized;

            // Дым всплывает выше огня, но недалеко: улетая, он растягивается
            // в бледные перья вокруг отряда и застилает сам огонь.
            float lift = Mathf.Lerp(0.7f, 0.35f, hot);

            // Вытянуты вдоль полёта, но умеренно: круглый клуб читается
            // пузырём, а сильно вытянутый — пером, и струя превращается
            // в размазанные крылья. Полторы длины к ширине — предел.
            Spawn(at, direction * speed + Vector3.up * lift,
                  from, to, Random.Range(1.2f, 1.45f), life, born, dead);
        }

        /// <summary>Огонёк на горящем: тот же клуб, мельче и вверх.</summary>
        public static void Wisp(Vector3 at)
        {
            if (config == null || root == null) return;

            var born = HeatColor(0.75f);
            var dead = HeatColor(0.2f);
            dead.a = 0f;

            Spawn(at + Random.insideUnitSphere * 0.25f,
                  Vector3.up * Random.Range(0.9f, 1.6f),
                  config.flameSize * 0.3f,
                  config.flameSize * 0.8f,
                  Random.Range(1.2f, 1.6f),
                  config.flameLife * 0.9f, born, dead);
        }

        /// <summary>
        /// Цвет по жару: дым — уголь — пламя — белое ядро.
        ///
        /// Четыре слоя, а не два: на референсах огнемёта видно ровно их,
        /// и узнаётся огонь именно по переходу от белого к жёлтому и дальше
        /// к красному. Двух цветов на это не хватает — выходит одноцветная
        /// клякса.
        /// </summary>
        /// <summary>
        /// Плавная ступенька, как smoothstep в шейдерах: ноль до edge0,
        /// единица после edge1. Именно этого от Mathf.SmoothStep ждёшь
        /// по привычке, а она делает совсем другое — интерполирует между
        /// первыми двумя числами.
        /// </summary>
        static float Smooth(float edge0, float edge1, float x) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, x));

        public static Color HeatColor(float hot)
        {
            if (hot > 0.6f) return Color.Lerp(config.flameColor, config.flameCoreColor, (hot - 0.6f) / 0.4f);
            if (hot > 0.14f) return Color.Lerp(config.flameEmberColor, config.flameColor, (hot - 0.14f) / 0.46f);
            return Color.Lerp(config.flameSmokeColor, config.flameEmberColor, hot / 0.14f);
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

        /// <summary>
        /// Материал сплошного тела струи. Тот же шейдер, но своя текстура:
        /// у клуба она круглая, у ленты — поперечный профиль.
        /// </summary>
        static Material StreamMaterial()
        {
            if (streamMaterial != null) return streamMaterial;

            streamMaterial = new Material(FlameMaterial()) { name = "FlameStream", mainTexture = JetProfile() };
            return streamMaterial;
        }

        /// <summary>
        /// Профиль струи: x — поперёк, y — вдоль. Плотное тело с мягким
        /// краем и затемнением к краям, как у клуба.
        /// </summary>
        static Texture2D JetProfile()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "FlameJetProfile", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;

                // У дула струя только разгоняется, у кончика её доедают
                // клубы — там лента гаснет, чтобы не обрываться по линейке.
                float along = Smooth(0f, 0.1f, v) * (1f - Smooth(0.72f, 1f, v));

                for (int x = 0; x < size; x++)
                {
                    float u = Mathf.Abs((x + 0.5f) / size * 2f - 1f);
                    float across = 1f - Smooth(0.62f, 1f, u);
                    float shade = Mathf.Lerp(1f, 0.7f, Smooth(0f, 0.9f, u));
                    pixels[y * size + x] = new Color(shade, shade, shade, across * along);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        static Material FlameMaterial()
        {
            if (material != null) return material;

            // Тот же шейдер, что у кислоты: предумноженная альфа, плотное
            // ядро держит цвет на песке, редкий край прибавляет свет.
            var shader = Shader.Find("WarfareSurvivor/GlowSprite");
            material = new Material(shader) { name = "Flame", mainTexture = Blob() };

            // Без подсветки: с ней оранжевый уезжает в жёлтый и сливается
            // с песком. Огню на этой земле нужен не свет, а насыщенность.
            material.SetFloat("_Boost", 1.15f);
            return material;
        }

        /// <summary>
        /// Косинусный купол — как у клуба кислоты, и показатель ниже единицы
        /// по той же причине: плотная часть занимает почти весь клуб, иначе
        /// облако выходит дымкой, а не огнём.
        /// </summary>
        /// <summary>
        /// Клуб пламени: плотный шар с ЧЁТКИМ краем и затемнением к краю.
        ///
        /// Не косинусный купол, как у кислоты. Купол мягко сходит на нет,
        /// и три десятка таких куполов сливаются в ровное пятно — та самая
        /// пена из огнетушителя. У огня на референсах край РЕЗКИЙ: струя
        /// собрана из читаемых круглых клубов, и её силуэт фестончатый,
        /// а не размытый.
        ///
        /// Затемнение к краю (rgb, не альфа) даёт то же, что обводка
        /// у персонажей: соседние клубы не сливаются, между ними видны
        /// тёмные дуги, и облако читается объёмным.
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

                    // Плотно до 0.78 радиуса, дальше быстрый спад: край
                    // читается, но не режет пикселями.
                    //
                    // Через InverseLerp, а НЕ Mathf.SmoothStep(0.78f, 1f, r):
                    // в Unity SmoothStep(a, b, t) — это интерполяция ОТ a К b,
                    // а не нормализация, как одноимённая функция в шейдерах.
                    // Написанная по шейдерной привычке, она давала в центре
                    // клуба альфу 0.22 вместо единицы — весь огонь выходил
                    // бледной дымкой, сколько клубов ни добавляй.
                    float a = 1f - Smooth(0.62f, 1f, r);

                    // Внутри светлее к середине — так у клуба виден объём.
                    // Затемнения к краю НЕТ. Тёмный ободок обводит каждый
                    // клуб по кругу, и струя рассыпается на пузыри — ровно
                    // то, чем она и выглядела. Форму держит силуэт целого
                    // облака, а не контур каждого шарика.
                    const float shade = 1f;

                    pixels[y * size + x] = new Color(shade, shade, shade, a);
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
    /// Тело струи: сплошной конус от дула, развёрнутый к камере вокруг
    /// своей оси, с ЯРКИМ ЯДРОМ ПО ОСИ и тёмными краями.
    ///
    /// Сплошное тело обязательно. Струя, набранная из одних клубов,
    /// рвётся на шарики и читается пеной из огнетушителя: на референсах
    /// у огнемёта есть непрерывное тело, а клубы лишь обрамляют его край.
    ///
    /// Три вершины на сегмент — левая, средняя, правая. Средняя несёт
    /// горячий цвет, боковые холодный: поперечная слоистость и делает
    /// огонь огнём, вдоль одной оси её не получить.
    /// </summary>
    public class FlameStream : MonoBehaviour
    {
        const int Segments = 14;
        const int Columns = 3;

        Mesh mesh;
        float lastFed = -1f;
        float noiseShift;

        readonly Vector3[] vertices = new Vector3[(Segments + 1) * Columns];
        readonly Vector2[] uv = new Vector2[(Segments + 1) * Columns];
        readonly Color[] colors = new Color[(Segments + 1) * Columns];

        public static FlameStream Create(Transform root, Material material)
        {
            var go = new GameObject("FlameBody");
            go.transform.SetParent(root, false);

            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.sharedMaterial = material;

            var stream = go.AddComponent<FlameStream>();
            stream.noiseShift = Random.value * 8f;
            stream.mesh = new Mesh { name = "FlameBody" };
            stream.mesh.MarkDynamic();

            var triangles = new int[Segments * (Columns - 1) * 6];
            int t = 0;
            for (int s = 0; s < Segments; s++)
                for (int c = 0; c < Columns - 1; c++)
                {
                    int a = s * Columns + c, b = a + 1;
                    int d = a + Columns, e = d + 1;
                    triangles[t++] = a; triangles[t++] = d; triangles[t++] = b;
                    triangles[t++] = d; triangles[t++] = e; triangles[t++] = b;
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
        /// Ширина струи на доле её длины. Одна формула на тело и клубы:
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
            // дольше, шапка распахивается ближе к концу.
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

            float wag = config.flameWag;
            float time = Time.time;

            for (int s = 0; s <= Segments; s++)
            {
                float t = (float)s / Segments;
                float half = WidthAt(config, reach, angleDegrees, heat, t) * 0.5f;

                var centre = origin + forward * (reach * t) + Vector3.up * (0.12f + 0.25f * t);
                centre += side * (Mathf.Sin(time * 6.5f + t * 5f + noiseShift) * wag * t * t);

                // Жар падает вдоль струи: у дула бело-жёлтое ядро, к концу
                // оранжевое и красное.
                float hot = Mathf.Clamp01(1f - t * 0.8f);
                var core = FlameJet.HeatColor(hot);
                var edge = FlameJet.HeatColor(hot * 0.45f);

                int i = s * Columns;
                vertices[i] = centre - side * half;
                vertices[i + 1] = centre;
                vertices[i + 2] = centre + side * half;

                uv[i] = new Vector2(0f, t);
                uv[i + 1] = new Vector2(0.5f, t);
                uv[i + 2] = new Vector2(1f, t);

                float fade = Mathf.Lerp(0.7f, 1f, heat);
                edge.a *= fade;
                core.a *= fade;

                colors[i] = edge;
                colors[i + 1] = core;
                colors[i + 2] = edge;
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
