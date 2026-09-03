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

            /// <summary>
            /// Рисовать отрезок ЦЕЛИКОМ и неподвижно.
            ///
            /// Обычный отрезок этого слоя летит: голова едет от начала к концу
            /// за время жизни, и позади неё тянется хвост заданной длины —
            /// так устроена трасса пули. Молнии это не подходит: разряд
            /// возникает сразу весь. Пока флага не было, каждое звено ломаной
            /// росло от собственного начала, и разряд выглядел цепочкой
            /// сосисок вместо линии.
            /// </summary>
            public bool Whole;
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
            Fire(from, to, 1f);
        }

        /// <summary>
        /// Выстрел заданного калибра: множитель растягивает и пулю, и хвост.
        ///
        /// Множитель, а не свой набор чисел на класс: соотношение пули,
        /// хвоста и вспышки подобрано так, чтобы выстрел читался, и класс,
        /// который начнёт задавать их порознь, это соотношение сломает.
        /// </summary>
        public static void Fire(Vector3 from, Vector3 to, float scale)
        {
            if (instance == null || !instance.config.tracersEnabled) return;
            var cfg = instance.config;
            scale = Mathf.Max(0.1f, scale);

            var direction = (to - from).normalized;

            // Вспышка у ствола: короткий широкий отрезок, живущий доли трассы.
            if (cfg.muzzleFlashLength > 0f)
                instance.Add(from, from + direction * (cfg.muzzleFlashLength * scale),
                    cfg.muzzleFlashLife, cfg.muzzleFlashLength * scale,
                    cfg.tracerWidth * cfg.muzzleFlashWidth * scale, cfg.tracerColor);

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

            instance.Add(from, to, cfg.tracerLife, cfg.tracerDashMeters * scale,
                cfg.tracerWidth * Mathf.Max(0.05f, cfg.tracerTailWidth) * scale, tail);

            instance.Add(from, to, cfg.tracerLife, Mathf.Max(0.05f, cfg.tracerBulletMeters) * scale,
                cfg.tracerWidth * Mathf.Max(0.05f, cfg.tracerBulletWidth) * scale, cfg.tracerColor);

            instance.Sparks(to, direction);
        }

        /// <summary>
        /// Разряд молнии от точки до точки.
        ///
        /// Рисуется теми же отрезками, что и трассы, и попадает в ту же общую
        /// сетку — то есть не добавляет ни одного вызова отрисовки.
        ///
        /// Ломаная строится ДРОБЛЕНИЕМ СЕРЕДИНЫ: отрезок делится пополам,
        /// середина сбивается вбок, и так несколько раз. Каждый следующий
        /// уровень сбивает вдвое слабее, потому что и отрезки вдвое короче, —
        /// отсюда та самая мелкая рвань, по которой молния и узнаётся.
        /// Первая версия ломала линию тремя длинными коленами и читалась
        /// трассой пули.
        ///
        /// ВЕТВИ обязательны. Отростки, уходящие в сторону и гаснущие, не дойдя
        /// никуда, — единственная черта, которой нет ни у одного другого
        /// эффекта в игре: луч, трасса и след все идут из точки в точку.
        /// Без них разряд остаётся кривой линией.
        ///
        /// Два слоя по одной ломаной: широкий тусклый ореол и тонкое белое
        /// ядро поверх. Горячим разряд делает именно перепад между размытым
        /// краем и белой сердцевиной.
        /// </summary>
        public static void Bolt(Vector3 from, Vector3 to)
        {
            if (instance == null || !instance.config.tracersEnabled) return;
            instance.DrawBolt(from, to, instance.config.lightningColor);
        }

        /// <summary>
        /// Прямой отрезок, живущий на месте: след снаряда.
        ///
        /// Именно неподвижный. Обычный отрезок этого слоя летит от начала
        /// к концу — так устроена трасса пули, — и след из таких отрезков
        /// уезжал бы вперёд от самого снаряда.
        /// </summary>
        public static void Streak(Vector3 from, Vector3 to, float width, Color color, float life)
        {
            if (instance == null || !instance.config.tracersEnabled) return;

            float length = Vector3.Distance(from, to);
            if (length < 0.001f) return;

            instance.Add(from, to, life, length, width, color, whole: true);
        }

        /// <summary>Разряд своего цвета. Прозрачность в ноль — берём цвет из конфига.</summary>
        public static void Bolt(Vector3 from, Vector3 to, Color glow)
        {
            if (instance == null || !instance.config.tracersEnabled) return;
            if (glow.a <= 0.001f) glow = instance.config.lightningColor;
            instance.DrawBolt(from, to, glow);
        }

        // Буферы под ломаную. Два: ветвь строится, пока основной разряд ещё
        // нужен, и общий буфер она бы затёрла.
        static readonly Vector3[] MainPoints = new Vector3[33];
        static readonly Vector3[] BranchPoints = new Vector3[9];

        void DrawBolt(Vector3 from, Vector3 to, Color glowColor)
        {
            var delta = to - from;
            float length = delta.magnitude;
            if (length < 0.05f) return;

            // Дробим ПО ДЛИНЕ, а не всегда до упора: скачок цепи в три метра
            // не нуждается в шестнадцати коленах, а каждое колено — это два
            // отрезка в общем пуле. Восемь электриков по четыре звена
            // переполнили бы его, а переполнение вытесняет трассы пуль.
            int levels = Mathf.Clamp(Mathf.RoundToInt(Mathf.Log(length / 0.45f, 2f)), 2, 4);
            int count = Jag(MainPoints, from, to, levels);

            var glow = glowColor;
            var core = new Color(1f, 1f, 1f, glowColor.a);

            for (int i = 1; i < count; i++)
                Segment(MainPoints[i - 1], MainPoints[i], config.lightningWidth, glow, core, 1f);

            // Ветви — только на длинных разрядах и по одному слою.
            //
            // Ореол у ветви не нужен: она и так тусклая и тонкая, а второй
            // слой удваивает её цену. Свою работу — рвать силуэт разряда —
            // делает уже само ядро.
            int branches = length > 3f ? 2 : 1;
            for (int b = 0; b < branches; b++)
            {
                int node = Random.Range(1, count - 1);
                var start = MainPoints[node];

                var forward = (to - start);
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.01f) continue;

                var turn = Quaternion.Euler(Random.Range(-35f, 35f),
                                            Random.Range(-70f, 70f),
                                            0f);
                var end = start + turn * forward * Random.Range(0.35f, 0.7f);

                int bc = Jag(BranchPoints, start, end, levels: 2);
                for (int i = 1; i < bc; i++)
                {
                    // Ветвь ГАСНЕТ к своему концу: обрубленная на полной
                    // яркости читается второй молнией, а не отростком.
                    float fade = 1f - (i / (float)(bc - 1)) * 0.85f;
                    CoreOnly(BranchPoints[i - 1], BranchPoints[i],
                             config.lightningWidth * 0.6f, core, fade);
                }
            }
        }

        /// <summary>Дробит отрезок на рваную ломаную. Возвращает число точек.</summary>
        int Jag(Vector3[] buffer, Vector3 from, Vector3 to, int levels)
        {
            buffer[0] = from;
            buffer[1] = to;
            int count = 2;

            float jag = Mathf.Clamp(config.lightningJag, 0f, 0.4f);

            for (int level = 0; level < levels; level++)
            {
                int next = count * 2 - 1;
                if (next > buffer.Length) break;

                // Идём С КОНЦА, раздвигая точки на месте: иначе новые
                // середины затирают ещё не прочитанные узлы.
                for (int i = count - 1; i > 0; i--)
                {
                    var a = buffer[i - 1];
                    var b = buffer[i];
                    buffer[i * 2] = b;

                    var step = b - a;
                    float len = step.magnitude;
                    var mid = (a + b) * 0.5f;

                    if (len > 0.0001f)
                    {
                        var dir = step / len;
                        var side = Vector3.Cross(dir, Vector3.up);
                        if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
                        side.Normalize();
                        var up = Vector3.Cross(side, dir);

                        mid += (side * Random.Range(-1f, 1f) + up * Random.Range(-1f, 1f)) * len * jag;
                    }

                    buffer[i * 2 - 1] = mid;
                }

                count = next;
            }

            return count;
        }

        /// <summary>
        /// Одно звено разряда в два слоя.
        ///
        /// Концы ПРОДЛЕНЫ за узел. Текстура отрезка гаснет к краям — она
        /// рисовалась под одиночную трассу пули, — и на стыке двух звеньев
        /// получается провал яркости: ломаная разваливается на отдельные
        /// чёрточки, ровно так первая версия и выглядела.
        /// </summary>
        /// <summary>Звено в один слой — для ветвей, которым ореол не нужен.</summary>
        void CoreOnly(Vector3 a, Vector3 b, float width, Color core, float brightness)
        {
            var step = b - a;
            float length = step.magnitude;
            if (length < 0.001f) return;

            var overhang = step * 0.4f;
            var c = core; c.a *= brightness;
            Add(a - overhang, b + overhang, config.lightningLife, length * 1.8f, width, c, whole: true);
        }

        void Segment(Vector3 a, Vector3 b, float width, Color glow, Color core, float brightness)
        {
            var step = b - a;
            float length = step.magnitude;
            if (length < 0.001f) return;

            var overhang = step * 0.4f;
            var start = a - overhang;
            var end = b + overhang;
            float drawn = length * 1.8f;

            var g = glow; g.a *= brightness;
            var c = core; c.a *= brightness;

            Add(start, end, config.lightningLife, drawn, width * 3.5f, g, whole: true);
            Add(start, end, config.lightningLife, drawn, width, c, whole: true);
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
            Add(from, to, life, dashMeters, width, color, whole: false);
        }

        void Add(Vector3 from, Vector3 to, float life, float dashMeters, float width, Color color, bool whole)
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
                Color = color,
                Whole = whole
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
                float headDistance = item.Whole ? length : length * t01;
                // Длина отрезка в МЕТРАХ, а не долей пути: доля означала бы,
                // что дальний выстрел рисуется длинной чертой, а ближний
                // коротышкой — один и тот же снаряд меняет размер вчетверо.
                float tailDistance = item.Whole ? 0f : Mathf.Max(0f, headDistance - item.DashMeters);

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
