using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Отряд («squad») — так называется игровая толпа во всём коде.
    ///
    /// Игрок двигает не бойцов, а невидимый ЯКОРЬ. Бойцы держат слоты вокруг
    /// него и догоняют их сами. Отсюда пластичность: на резком развороте отряд
    /// растягивается и собирается обратно.
    ///
    /// Слоты разложены по КОЛЬЦАМ, по одному на роль: пассивные в центре,
    /// стрелки следом, ближний бой снаружи — там, куда враг приходит первым.
    /// </summary>
    public class SquadController : MonoBehaviour
    {
        struct Ring
        {
            public float Radius;
            public int Count;
            public float AngleOffset;
        }

        [SerializeField] ArenaConfig config;
        [SerializeField] VirtualJoystick joystick;
        [SerializeField] Camera viewCamera;

        /// <summary>Куда отряд идёт сейчас. Нулевой вектор — стоим.</summary>
        public Vector3 MoveDirection { get; private set; }
        public bool IsMoving => MoveDirection.sqrMagnitude > 0.0001f;

        /// <summary>Радиус бойца в метрах — от него считаются все размеры строя.</summary>
        public float UnitRadius { get; private set; } = 0.25f;

        // Живые бойцы, отсортированные по роли. Порядок сохраняется при
        // удалении, поэтому пересборка строя — это пересчёт индексов,
        // а не новая сортировка.
        readonly List<Survivor> living = new List<Survivor>();
        readonly List<int> livingRing = new List<int>();
        readonly List<int> livingSlot = new List<int>();

        Ring[] rings = new Ring[0];
        bool formationDirty;
        Vector3 anchor;

        void Awake()
        {
            if (config == null)
            {
                Debug.LogError($"[{name}] Не задан config. Отряд не создан.", this);
                enabled = false;
                return;
            }

            if (viewCamera == null) viewCamera = Camera.main;
            anchor = transform.position;
            SpawnSquad();
        }

        void OnDestroy() => Registry.Survivors.Clear();

        // --- создание -------------------------------------------------------

        void SpawnSquad()
        {
            var plan = BuildPlan();
            if (plan.Count == 0)
            {
                Debug.LogError($"[{name}] Состав отряда пуст — заполни squadComposition в конфиге.", this);
                enabled = false;
                return;
            }

            float unitRadius = 0f;

            for (int i = 0; i < plan.Count; i++)
            {
                var klass = plan[i];
                var prefab = klass.ActivePrefab.GetComponent<Survivor>();

                var member = Instantiate(prefab, anchor, Quaternion.identity, transform);
                member.name = $"{klass.displayName}_{i:00}";
                member.Bind(this, config, klass);
                member.Lost += OnMemberLost;
                living.Add(member);

                unitRadius = Mathf.Max(unitRadius, MeasureUnitRadius(member));
            }

            // Радиус берём по САМОМУ КРУПНОМУ классу: строй, рассчитанный
            // по мелкому, посадит крупных друг в друга.
            UnitRadius = Mathf.Max(0.15f, unitRadius);

            RebuildFormation();

            // Ставим на места сразу: иначе первый кадр отряд стоит в куче
            // и потом рывком расходится.
            for (int i = 0; i < living.Count; i++)
                living[i].transform.position = anchor + SlotOffset(i);
        }

        /// <summary>
        /// Разворачивает состав в список бойцов, отсортированный по роли.
        /// Сортировка нужна, чтобы бойцы одного кольца шли подряд — тогда
        /// номер слота внутри кольца получается простым вычитанием.
        /// </summary>
        List<SurvivorClassSO> BuildPlan()
        {
            var plan = new List<SurvivorClassSO>();
            if (config.squadComposition == null) return plan;

            foreach (var entry in config.squadComposition)
            {
                if (entry.Class == null || entry.Count <= 0) continue;
                var prefab = entry.Class.ActivePrefab;
                if (prefab == null || prefab.GetComponent<Survivor>() == null)
                {
                    Debug.LogError($"[{name}] У класса {entry.Class.name} нет префаба с компонентом Survivor " +
                                   $"(вариант {entry.Class.variant}).", this);
                    continue;
                }

                for (int i = 0; i < entry.Count; i++) plan.Add(entry.Class);
            }

            plan.Sort((a, b) => ((int)a.role).CompareTo((int)b.role));
            return plan;
        }

        /// <summary>
        /// Кольца считаются только по ПРИСУТСТВУЮЩИМ ролям и нумеруются подряд.
        /// Поэтому отряд из одних стрелков ставит их на внутреннее кольцо
        /// с минимальным спейсингом, а появление медиков отодвигает стрелков
        /// наружу само собой — правило одно, частных случаев нет.
        /// </summary>
        /// <summary>
        /// Боец выбыл — строй пересобираем. Не сразу: за один кадр погибнуть
        /// может сразу несколько, и пересчитывать кольца на каждого по
        /// отдельности незачем.
        /// </summary>
        void OnMemberLost(Survivor member)
        {
            member.Lost -= OnMemberLost;
            living.Remove(member);
            formationDirty = true;
        }

        /// <summary>
        /// Раскладывает ЖИВЫХ по кольцам заново.
        ///
        /// Именно живых, а не изначальный состав: иначе на месте погибшего
        /// остаётся дыра, а оставшиеся продолжают стоять по старым углам.
        /// После пересборки они расходятся по кольцу равномерно — каждый
        /// доходит до нового слота сам, обычным движением.
        ///
        /// Побочное следствие правила «кольца только по присутствующим
        /// ролям»: когда последний медик погиб, стрелки переезжают
        /// на внутреннее кольцо. Так и задумано — пустых колец не бывает.
        /// </summary>
        void RebuildFormation()
        {
            formationDirty = false;

            var counts = new List<int>();
            var order = new List<SquadRole>();

            foreach (var member in living)
            {
                var role = member.Class.role;
                if (order.Count > 0 && order[order.Count - 1] == role) counts[counts.Count - 1]++;
                else { order.Add(role); counts.Add(1); }
            }

            rings = new Ring[counts.Count];
            float diameter = UnitRadius * 2f;
            float minGap = config.formationRingGap * diameter;
            float previous = 0f;

            for (int i = 0; i < counts.Count; i++)
            {
                int count = counts[i];
                float spacing = (config.formationRingSpacingMin + config.formationRingSpacingStep * i) * diameter;

                // Радиус выводится из спейсинга и числа бойцов на кольце:
                // задавать радиус напрямую нельзя — на многолюдном кольце
                // бойцы встали бы друг в друга.
                float radius = count > 1
                    ? spacing / (2f * Mathf.Sin(Mathf.PI / count))
                    : 0f;

                // Зазор между кольцами обязателен: без него внешнее кольцо
                // с тремя бойцами окажется внутри внутреннего с десятью.
                if (i > 0) radius = Mathf.Max(radius, previous + minGap);
                previous = radius;

                rings[i] = new Ring
                {
                    Radius = radius,
                    Count = count,
                    // Соседние кольца сдвинуты на полшага, чтобы бойцы не
                    // выстраивались в спицы и строй читался толпой.
                    AngleOffset = count > 0 && i % 2 == 1 ? Mathf.PI / count : 0f
                };
            }

            livingRing.Clear();
            livingSlot.Clear();
            for (int ring = 0; ring < rings.Length; ring++)
                for (int slot = 0; slot < rings[ring].Count; slot++)
                {
                    livingRing.Add(ring);
                    livingSlot.Add(slot);
                }
        }

        /// <summary>
        /// Габарит берём из меша, а не из константы: модели приходят из разных
        /// источников и с разным импортным масштабом.
        ///
        /// Берём МЕНЬШИЙ горизонтальный габарит: больший — это размах рук
        /// в бинд-позе, и строй, посчитанный по нему, разъезжается вчетверо
        /// шире нужного.
        /// </summary>
        static float MeasureUnitRadius(Survivor sample)
        {
            var renderers = sample.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0.25f;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            return Mathf.Max(0.15f, Mathf.Min(bounds.extents.x, bounds.extents.z));
        }

        // --- движение -------------------------------------------------------

        void Update()
        {
            if (living.Count == 0) return;
            if (formationDirty) RebuildFormation();

            var input = joystick != null ? joystick.ReadWithKeyboardFallback() : Vector2.zero;
            if (input.sqrMagnitude < 0.0001f && config.debugAutoDrive) input = AutoDriveInput();
            MoveDirection = ToWorldDirection(input);

            anchor += MoveDirection * (config.squadSpeed * Time.deltaTime);
            transform.position = anchor;

            for (int i = 0; i < living.Count; i++)
                living[i].SlotPosition = anchor + SlotOffset(i);
        }

        /// <summary>
        /// Отладочный автопроход по кругу: даёт ту же картинку, что палец на
        /// джойстике, и позволяет смотреть поведение отряда на бегу.
        /// </summary>
        Vector2 AutoDriveInput()
        {
            float omega = config.squadSpeed / Mathf.Max(config.debugAutoDriveRadius, 0.1f);
            float phase = Time.time * omega;
            return new Vector2(Mathf.Cos(phase), Mathf.Sin(phase));
        }

        /// <summary>
        /// Ввод разворачивается по камере: без этого «вверх» на джойстике
        /// уводило бы отряд вбок при любом повороте камеры.
        /// </summary>
        Vector3 ToWorldDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;

            float yaw = viewCamera != null ? viewCamera.transform.eulerAngles.y : 0f;
            return Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y);
        }

        /// <summary>
        /// Смещения в мировых осях и НЕ вращаются вслед за движением: иначе
        /// отряд крутится вокруг своей оси при каждом повороте джойстика,
        /// и бойцы наматывают круги вместо того, чтобы идти.
        /// </summary>
        Vector3 SlotOffset(int index)
        {
            var ring = rings[livingRing[index]];

            // Проверяем только радиус. Условие «на кольце один боец» тут было
            // бы ошибкой: последний уцелевший из внешнего кольца уехал бы
            // в центр, внутрь стрелков.
            if (ring.Radius <= 0f) return Vector3.zero;

            float angle = ring.AngleOffset + livingSlot[index] * Mathf.PI * 2f / Mathf.Max(1, ring.Count);
            return new Vector3(Mathf.Cos(angle) * ring.Radius, 0f, Mathf.Sin(angle) * ring.Radius);
        }
    }
}
