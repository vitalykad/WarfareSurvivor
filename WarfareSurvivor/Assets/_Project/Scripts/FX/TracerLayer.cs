using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Все трассы и искры — ОДИН меш, а не объект на выстрел.
    ///
    /// Это главный вывод прошлого проекта про эффекты: число эффектов не должно
    /// расти вместе с толпой. Восемь бойцов по два выстрела в секунду — это
    /// шестнадцать объектов в секунду с рендерером и материалом на каждом;
    /// один перестраиваемый меш стоит одного вызова отрисовки независимо
    /// от того, сколько трасс в воздухе.
    ///
    /// Искры от попадания — те же трассы, просто короткие: отдельная система
    /// частиц ради них не нужна (§13 CROWD_PROJECT_LESSONS).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TracerLayer : MonoBehaviour
    {
        struct Dash
        {
            public Vector3 From;
            public Vector3 To;
            public float Born;
            public float Life;
            public float DashMeters;
            public float Width;
            public Color Color;
        }

        static TracerLayer instance;

        [SerializeField] ArenaConfig config;

        [Tooltip("Гасит ТОЛЬКО отрисовку. Счёт при этом продолжается — если " +
                 "выключить сам компонент, трассы перестают стареть, список " +
                 "копится, и следующий замер меряет разгребание этой кучи.")]
        [SerializeField] bool renderEnabled = true;

        Dash[] items;
        int count;

        /// <summary>Сколько отрезков сейчас в воздухе — трассы, искры и вспышки вместе.</summary>
        public static int ActiveCount => instance != null ? instance.count : 0;

        /// <summary>
        /// Площадь перерисовки в квадратных метрах.
        ///
        /// Главная величина для мобильного кадра: платят за площадь, а не
        /// за число эффектов. Считается по геометрии, поэтому честна —
        /// это ровно те метры, которые залиты аддитивным шейдером.
        /// </summary>
        public static float ActiveArea
        {
            get
            {
                if (instance == null) return 0f;

                float area = 0f;
                for (int i = 0; i < instance.count; i++)
                {
                    var item = instance.items[i];
                    float length = Mathf.Min(item.DashMeters, (item.To - item.From).magnitude);
                    area += length * item.Width;
                }
                return area;
            }
        }

        Mesh mesh;
        Vector3[] vertices;
        Vector2[] uv;
        Color[] colors;
        int[] triangles;

        Material material;
        MeshRenderer meshRenderer;
        Camera view;

        void Awake()
        {
            if (config == null)
            {
                Debug.LogError($"[{name}] Не задан config. Трассы выключены.", this);
                enabled = false;
                return;
            }

            instance = this;
            view = Camera.main;
            BuildBuffers();
            BuildMaterial();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void BuildBuffers()
        {
            int capacity = Mathf.Max(4, config.maxTracers);
            items = new Dash[capacity];

            vertices = new Vector3[capacity * 4];
            uv = new Vector2[capacity * 4];
            colors = new Color[capacity * 4];
            triangles = new int[capacity * 6];

            for (int i = 0; i < capacity; i++)
            {
                int v = i * 4;
                int t = i * 6;
                triangles[t + 0] = v + 0;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 0;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;

                // Развёртка по ОБЕИМ осям: u поперёк ленты, v вдоль неё.
                //
                // Раньше v было заперто в середине, и отрезок обрывался
                // резко — короткая пуля выходила квадратной. Мягкость нужна
                // и вдоль: тогда короткий отрезок читается круглой пулей,
                // а длинный — лентой со скруглёнными концами.
                uv[v + 0] = new Vector2(0f, 0f);
                uv[v + 1] = new Vector2(1f, 0f);
                uv[v + 2] = new Vector2(1f, 1f);
                uv[v + 3] = new Vector2(0f, 1f);
            }

            mesh = new Mesh { name = "TracerLayer" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.triangles = triangles;
            // Меш строится в мировых координатах и живёт вокруг отряда;
            // без большого бокса он отсекается камерой на первом же кадре.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);

            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        void BuildMaterial()
        {
            var shader = Shader.Find("WarfareSurvivor/AdditiveTracer");
            if (shader == null)
            {
                Debug.LogError("[TracerLayer] Не найден шейдер WarfareSurvivor/AdditiveTracer", this);
                enabled = false;
                return;
            }

            material = new Material(shader) { name = "TracerAdditive" };
            material.SetTexture("_MainTex", BuildRibbonTexture());
            material.SetFloat("_Boost", config.tracerBoost);
            material.SetFloat("_Rolloff", 0f);

            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }

        /// <summary>
        /// Тугое ядро плюс широкий мягкий ореол в ОДНОЙ текстуре. Один общий
        /// мягкий спад читается размазанным пятном, а не светом.
        /// </summary>
        /// <summary>
        /// Профиль отрезка: мягкий и поперёк, и вдоль.
        ///
        /// Поперёк — плотное ядро и ореол, как было. Вдоль добавлены
        /// скруглённые концы: без них короткий отрезок рисовался квадратом,
        /// и пуля выглядела кирпичом.
        ///
        /// Одна текстура служит обоим: короткий отрезок вытягивает её мало
        /// и выходит круглым, длинный растягивает — и концы у ленты остаются
        /// скруглёнными.
        /// </summary>
        static Texture2D BuildRibbonTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TracerRibbon",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                // Вдоль отрезка: полная сила в середине, спад к обоим концам.
                float alongEdge = Mathf.Abs((y + 0.5f) / size - 0.5f) * 2f;
                float along = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Max(0f, alongEdge - 0.45f) / 0.55f), 1.4f);

                for (int x = 0; x < size; x++)
                {
                    float acrossEdge = Mathf.Abs((x + 0.5f) / size - 0.5f) * 2f;
                    float core = Mathf.Pow(Mathf.Clamp01(1f - acrossEdge / 0.22f), 1.6f);
                    float halo = Mathf.Pow(Mathf.Clamp01(1f - acrossEdge), 3.5f) * 0.42f;

                    float a = Mathf.Max(core, halo) * along;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        // --- публичный вход -------------------------------------------------

        public static void Fire(Vector3 from, Vector3 to)
        {
            if (instance == null || !instance.config.tracersEnabled) return;
            var cfg = instance.config;

            var direction = (to - from).normalized;

            // Вспышка у ствола: короткий широкий отрезок, живущий доли трассы.
            if (cfg.muzzleFlashLength > 0f)
                instance.Add(from, from + direction * cfg.muzzleFlashLength,
                    cfg.muzzleFlashLife, cfg.muzzleFlashLength,
                    cfg.tracerWidth * cfg.muzzleFlashWidth, cfg.tracerColor);

            // Трасса из ДВУХ отрезков: тусклый длинный хвост и яркая
            // короткая пуля на его голове.
            //
            // Оба идут по одному лучу с одной скоростью, отличаются только
            // длиной, шириной и яркостью — отрезок рисуется назад от головы,
            // поэтому короткий и оказывается самой пулей. Отдельного объекта
            // под пулю не нужно: это те же данные в том же меше.
            //
            // Хвост добавляем ПЕРВЫМ, чтобы яркая пуля легла поверх него.
            var tail = cfg.tracerColor * Mathf.Clamp01(cfg.tracerTailDim);
            tail.a = cfg.tracerColor.a;

            instance.Add(from, to, cfg.tracerLife, cfg.tracerDashMeters,
                cfg.tracerWidth * Mathf.Max(0.05f, cfg.tracerTailWidth), tail);

            instance.Add(from, to, cfg.tracerLife, Mathf.Max(0.05f, cfg.tracerBulletMeters),
                cfg.tracerWidth * Mathf.Max(0.05f, cfg.tracerBulletWidth), cfg.tracerColor);

            instance.Sparks(to, direction);
        }

        void Sparks(Vector3 point, Vector3 incoming)
        {
            for (int i = 0; i < config.sparkCount; i++)
            {
                var scatter = Random.insideUnitSphere * config.sparkSpread;
                var direction = (-incoming + scatter).normalized;
                var end = point + direction * config.sparkLengthMeters;

                // Искры — те же трассы: короче, шире долей отрезка и живут меньше.
                Add(point, end, config.sparkLife, config.sparkLengthMeters,
                    config.tracerWidth * 0.7f, config.tracerColor);
            }
        }

        void Add(Vector3 from, Vector3 to, float life, float dashMeters, float width, Color color)
        {
            // Потолок выбран — вытесняем из головы списка. Пропускать новую
            // трассу нельзя: пропадёт обратная связь ровно тогда, когда
            // стреляют чаще всего.
            if (count == items.Length) ShiftDown();
            int index = count++;

            items[index] = new Dash
            {
                From = from,
                To = to,
                Born = Time.time,
                Life = life,
                DashMeters = dashMeters,
                Width = width,
                Color = color
            };
        }

        void ShiftDown()
        {
            for (int i = 1; i < count; i++) items[i - 1] = items[i];
            count--;
        }

        // --- отрисовка ------------------------------------------------------

        void LateUpdate()
        {
            Expire();
            if (meshRenderer != null) meshRenderer.enabled = renderEnabled && count > 0;
            if (!renderEnabled) return;

            BuildGeometry();
        }

        void Expire()
        {
            float now = Time.time;
            for (int i = count - 1; i >= 0; i--)
            {
                if (now - items[i].Born < items[i].Life) continue;
                items[i] = items[count - 1];
                count--;
            }
        }

        void BuildGeometry()
        {
            if (view == null) view = Camera.main;
            var viewDirection = view != null ? view.transform.forward : Vector3.forward;
            float now = Time.time;

            for (int i = 0; i < items.Length; i++)
            {
                int v = i * 4;

                if (i >= count)
                {
                    // Лишние квады схлопываем в точку: перестраивать индексы
                    // каждый кадр дороже, чем нарисовать вырожденный треугольник.
                    vertices[v] = vertices[v + 1] = vertices[v + 2] = vertices[v + 3] = Vector3.zero;
                    continue;
                }

                var item = items[i];
                var along = item.To - item.From;
                float length = along.magnitude;
                if (length < 0.0001f)
                {
                    vertices[v] = vertices[v + 1] = vertices[v + 2] = vertices[v + 3] = Vector3.zero;
                    continue;
                }

                var direction = along / length;
                float t01 = Mathf.Clamp01((now - item.Born) / item.Life);

                // Короткий отрезок, который ЛЕТИТ. Сплошная черта от ствола до
                // цели читается непрерывным лучом, а не выстрелом.
                float headDistance = length * t01;
                // Длина отрезка в МЕТРАХ, а не долей пути: доля означала бы,
                // что дальний выстрел рисуется длинной чертой, а ближний
                // коротышкой — один и тот же снаряд меняет размер вчетверо.
                float tailDistance = Mathf.Max(0f, headDistance - item.DashMeters);

                var head = item.From + direction * headDistance;
                var tail = item.From + direction * tailDistance;

                var side = Vector3.Cross(direction, viewDirection);
                if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(direction, Vector3.up);
                side = side.normalized * (item.Width * 0.5f);

                vertices[v + 0] = tail - side;
                vertices[v + 1] = tail + side;
                vertices[v + 2] = head + side;
                vertices[v + 3] = head - side;

                var color = item.Color;
                color.a = 1f - t01 * t01;   // истончение к концу жизни
                colors[v + 0] = colors[v + 1] = colors[v + 2] = colors[v + 3] = color;
            }

            mesh.vertices = vertices;
            mesh.colors = colors;
        }
    }
}
