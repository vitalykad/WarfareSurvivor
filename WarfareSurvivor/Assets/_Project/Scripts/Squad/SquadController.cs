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
        /// <summary>
        /// Золотой угол. Поворот на него между соседними бойцами — то самое,
        /// по которому уложены семечки в подсолнухе: доли оборота никогда
        /// не повторяются, поэтому не возникает ни рядов, ни спиц.
        /// </summary>
        const float GoldenAngle = 2.39996323f;

        /// <summary>Площадь на бойца при плотнейшей укладке — sqrt(3)/2 от квадрата шага.</summary>
        const float HexAreaFactor = 0.866f;

        [SerializeField] ArenaConfig config;

        [SerializeField, Tooltip("Брать стартовый состав забега, а не состав стенда.")]
        bool useRunComposition;
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

        /// <summary>Смещение слота от якоря, по одному на живого бойца.</summary>
        readonly List<Vector3> slotOffsets = new List<Vector3>();

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

        /// <summary>Сколько бойцов сейчас живо.</summary>
        public int MemberCount => living.Count;

        /// <summary>Живые бойцы — для подсчёта состава в интерфейсе.</summary>
        public System.Collections.Generic.IReadOnlyList<Survivor> Members => living;

        /// <summary>
        /// Добавляет бойца по ходу забега — это и есть тир-ап.
        ///
        /// Новичок встаёт в центр отряда и сам доходит до своего места:
        /// ставить его сразу в слот нельзя, потому что слоты пересчитаются
        /// только на следующем кадре.
        ///
        /// Список живых пересортировывается по роли — от этого порядка
        /// зависит раскладка по кольцам, и вставка в конец сломала бы её:
        /// добавленный медик оказался бы снаружи, за ближним боем.
        /// </summary>
        public Survivor AddMember(SurvivorClassSO klass)
        {
            if (klass == null || klass.prefab == null) return null;

            var prefab = klass.prefab.GetComponent<Survivor>();
            if (prefab == null) return null;

            var member = Instantiate(prefab, anchor, Quaternion.identity, transform);
            member.name = $"{klass.displayName}_{living.Count:00}";
            member.Bind(this, config, klass);
            member.Lost += OnMemberLost;
            LayerUtility.Apply(member.gameObject, LayerUtility.Survivors);

            living.Add(member);
            living.Sort((a, b) => ((int)a.Class.role).CompareTo((int)b.Class.role));

            UnitRadius = Mathf.Max(UnitRadius, MeasureUnitRadius(member));
            formationDirty = true;
            return member;
        }

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
                var prefab = klass.prefab.GetComponent<Survivor>();

                var member = Instantiate(prefab, anchor, Quaternion.identity, transform);
                member.name = $"{klass.displayName}_{i:00}";
                member.Bind(this, config, klass);
                member.Lost += OnMemberLost;
                LayerUtility.Apply(member.gameObject, LayerUtility.Survivors);
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

            ReportSquad();
        }

        /// <summary>
        /// Печатает, каким отряд получился. Нужно не в редакторе, а
        /// на устройстве: там инспектора нет, и единственный способ понять,
        /// почему строй выглядит не так, — прочитать это в логе.
        /// </summary>
        void ReportSquad()
        {
            int ranged = 0, melee = 0, support = 0;
            foreach (var member in living)
                switch (member.Class.role)
                {
                    case SquadRole.Melee: melee++; break;
                    case SquadRole.Support: support++; break;
                    default: ranged++; break;
                }

            float spread = 0f;
            foreach (var offset in slotOffsets) spread = Mathf.Max(spread, offset.magnitude);

            Debug.Log($"[Squad] бойцов {living.Count} (стрелков {ranged}, ближнего боя {melee}, " +
                      $"поддержки {support}); радиус бойца {UnitRadius:F2}м; " +
                      $"строй {spread * 2f:F2}м в поперечнике; слотов {slotOffsets.Count}");
        }

        /// <summary>
        /// Разворачивает состав в список бойцов, отсортированный по роли.
        /// Сортировка нужна, чтобы бойцы одного кольца шли подряд — тогда
        /// номер слота внутри кольца получается простым вычитанием.
        /// </summary>
        List<SurvivorClassSO> BuildPlan()
        {
            var plan = new List<SurvivorClassSO>();

            // Сцена забега берёт свой стартовый состав: там отряд выходит
            // малым и растёт на тир-апах, а на стенде замеров стоят те же
            // двадцать пять бойцов, при которых сняты все цифры.
            var source = useRunComposition && config.runSquadStart != null && config.runSquadStart.Length > 0
                ? config.runSquadStart
                : config.squadComposition;

            if (source == null || source.Length == 0)
            {
                Debug.LogError($"[{name}] Состав отряда пуст: заполни squadComposition в конфиге.", this);
                return plan;
            }

            foreach (var entry in source)
            {
                if (entry.Class == null || entry.Count <= 0) continue;
                if (entry.Class.prefab == null || entry.Class.prefab.GetComponent<Survivor>() == null)
                {
                    Debug.LogError($"[{name}] У класса {entry.Class.name} нет префаба с компонентом Survivor.", this);
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
        ///
        /// Роль занимает КРУГ, а не кольцо: бойцы кладутся по спирали
        /// с золотым углом — как семечки в подсолнухе. Кольцами строй
        /// выглядел то крестом, то коробочкой: соседние кольца приходилось
        /// сдвигать по углу, и через одно они снова оказывались в фазе,
        /// а глаз читает такие совпадения как ряды и спицы. При золотом угле
        /// доли оборота не повторяются никогда, поэтому рядов не возникает
        /// вовсе, а круг заполняется равномерно.
        ///
        /// Побочное следствие правила «кольца только по присутствующим
        /// ролям»: когда последний медик погиб, стрелки переезжают внутрь.
        /// Так и задумано — пустых колец не бывает.
        /// </summary>
        void RebuildFormation()
        {
            formationDirty = false;

            slotOffsets.Clear();

            float diameter = UnitRadius * 2f;
            float roleGap = config.formationRingGap * diameter;
            float innerRadius = 0f;
            int roleIndex = 0;
            int cursor = 0;

            while (cursor < living.Count)
            {
                // living отсортирован по роли, поэтому одна роль — один отрезок.
                var role = living[cursor].Class.role;
                int count = 0;
                while (cursor + count < living.Count && living[cursor + count].Class.role == role) count++;

                float spacing = (config.formationRingSpacingMin + config.formationRingSpacingStep * roleIndex) * diameter;

                // Плотность задаём площадью на бойца — тогда круг заполняется
                // равномерно, без разрежения к краю или сгущения в центре.
                float areaPerUnit = spacing * spacing * HexAreaFactor;
                float outerRadius = innerRadius;

                for (int k = 0; k < count; k++)
                {
                    // Радиус растёт как корень: каждое следующее кольцо площади
                    // вмещает больше бойцов, и плотность остаётся постоянной.
                    float r = Mathf.Sqrt(innerRadius * innerRadius + k * areaPerUnit / Mathf.PI);
                    float angle = k * GoldenAngle;

                    slotOffsets.Add(new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r));
                    outerRadius = Mathf.Max(outerRadius, r);
                }

                innerRadius = outerRadius + roleGap;
                cursor += count;
                roleIndex++;
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
            // Только тело, без оружия. Лопата длиной полтора метра висит
            // на кости руки и входит в общий габарит — строй, посчитанный
            // по нему, разъезжается в несколько раз шире нужного.
            // Тело — это скиннинг, оружие — обычный меш, и это надёжный
            // признак: любое новое оружие отсеется само.
            var renderers = sample.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (renderers.Length == 0) return 0.25f;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            // Меньший горизонтальный габарит: больший — это размах рук
            // в бинд-позе, вчетверо шире реальной толщины тела.
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
        Vector3 SlotOffset(int index) =>
            index >= 0 && index < slotOffsets.Count ? slotOffsets[index] : Vector3.zero;
    }
}
