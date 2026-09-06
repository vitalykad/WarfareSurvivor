using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Зомби. Мозгов нет: вектор на ближайшего бойца и урон от соприкосновения,
    /// потому что анимации атаки пока нет (§6 project.md).
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class Zombie : MonoBehaviour
    {
        static readonly int DieParam = Animator.StringToHash("Die");
        static readonly int SpitParam = Animator.StringToHash("Spit");
        static readonly int AttackParam = Animator.StringToHash("Attack");
        static readonly int AttackSpeedParam = Animator.StringToHash("AttackSpeed");
        static readonly int MovingParam = Animator.StringToHash("Moving");

        /// <summary>
        /// Длина клипа удара. Одна на всех: контроллер у видов общий,
        /// и читать её у каждого зомби значило бы шестьдесят раз искать
        /// одно и то же.
        /// </summary>
        static float attackClipLength = -1f;

        // Имена клипов те же, что в контроллере: печь берёт их оттуда,
        // и расхождение сразу оставило бы зомби без анимации.
        const string ClipRunning = "Zombie Running";
        const string ClipDying = "Zombie Dying";

        // Один буфер на всех: расталкивание считается по очереди, и держать
        // список на каждом зомби значило бы двести списков вместо одного.
        static readonly List<Zombie> Neighbours = new List<Zombie>(32);

        [Tooltip("Проходит сквозь препятствия. Ставится на летающих: они " +
                 "перелетают руины, а не обходят их. Свойство префаба, " +
                 "а не состояние — пул его не сбрасывает.")]
        [SerializeField] bool ignoresObstacles;

        ArenaConfig config;
        Animator animator;

        /// <summary>Не null, когда зомби показывается запечённой анимацией.</summary>
        BakedZombieView baked;

        [SerializeField, Tooltip("Запечённая анимация ЭТОЙ модели. Живёт " +
                                 "на префабе, а не в конфиге: у каждого вида " +
                                 "зомби свой меш, а значит и своя текстура " +
                                 "позиций — общий набор годится ровно для " +
                                 "одного вида.")]
        BakedAnimationSet bakedSet;

        /// <summary>Набор запечённой анимации этого вида. Пусто — берётся из конфига.</summary>
        public BakedAnimationSet BakedSet => bakedSet;

        [Header("Свойства вида")]

        [SerializeField, Tooltip("Во сколько раз этот вид живучее обычного. " +
                                 "Живучесть тира умножается на это число.")]
        float healthMultiplier = 1f;

        [SerializeField, Tooltip("Во сколько раз этот вид крупнее обычного. " +
                                 "Умножается на масштаб тира, поэтому крупный " +
                                 "вид остаётся крупным на любом тире.")]
        float scaleMultiplier = 1f;

        [SerializeField, Tooltip("Сопротивление отталкиванию, в метрах. " +
                                 "Вычитается из дальности отброса ударившего: " +
                                 "лопата бьёт на три метра, сопротивление два — " +
                                 "отлетит на метр. Больше дальности удара — " +
                                 "не сдвинется вовсе.")]
        float knockbackResist;

        [SerializeField, Tooltip("Насколько часто этот вид попадается. " +
                                 "Единица у обычного; половина — вдвое реже. " +
                                 "Крупные должны быть редкими, иначе они " +
                                 "перестают быть событием.")]
        float spawnWeight = 1f;

        [Header("Свечение")]

        [SerializeField, Tooltip("Скорость пульса свечения. Ноль — свечение " +
                                 "ровное, как задано в материале.\n\n" +
                                 "Пульс идёт по ОБЩЕМУ материалу тира, а не " +
                                 "по каждому зомби: иначе каждый выбивался бы " +
                                 "в свою пачку отрисовки. Все светящиеся одного " +
                                 "вида поэтому пульсируют в такт — что при " +
                                 "одном плевуне на поле незаметно.")]
        float emissionPulseSpeed;

        [SerializeField, Tooltip("Во сколько раз свечение приглушается в нижней " +
                                 "точке пульса.")]
        [Range(0f, 1f)] float emissionPulseLow = 0.35f;

        [SerializeField, Tooltip("Во сколько раз разгорается в верхней точке.")]
        float emissionPulseHigh = 1.6f;

        [Header("Кислотный плевок")]

        [SerializeField, Tooltip("С какой дистанции плюётся, в метрах. " +
                                 "Ноль — вид не плюётся вовсе, и вся эта " +
                                 "секция его не касается.")]
        float spitRange;

        [SerializeField, Tooltip("Сколько секунд длится замах: от остановки " +
                                 "и появления зоны до вылета плевка.\n\n" +
                                 "Это ВРЕМЯ НА РЕАКЦИЮ. Слишком короткий замах " +
                                 "превращает зону в украшение — увидеть её " +
                                 "игрок успевает, а увести отряд уже нет.")]
        float spitWindup = 1.1f;

        [SerializeField, Tooltip("Сколько плевун стоит ПОСЛЕ вылета капли, " +
                                 "секунд.\n\n" +
                                 "Держит его до конца анимации плевка. Без этого " +
                                 "он трогался с места сразу после вылета и ехал " +
                                 "за отрядом, доигрывая замах: ноги стоят, " +
                                 "а тело скользит по земле.\n\n" +
                                 "Складывать с замахом надо так, чтобы вышла " +
                                 "длина клипа: \"Zombie Scream\" идёт 2.8 с, " +
                                 "замах 1.1 — значит здесь 1.7.")]
        float spitHold = 1.7f;

        [SerializeField, Tooltip("Секунд между плевками. Отсчитывается " +
                                 "от вылета капли и включает в себя стойку " +
                                 "после неё.")]
        float spitInterval = 3.5f;

        [SerializeField, Tooltip("Урон в момент попадания. Достаётся всем, " +
                                 "кто оказался в круге.")]
        float spitDamage = 18f;

        [SerializeField, Tooltip("Радиус поражения в метрах. Он же радиус " +
                                 "красного круга на земле — иначе круг врал бы.")]
        float spitRadius = 2.2f;

        [SerializeField, Tooltip("Сколько летит плевок, секунд. Складывается " +
                                 "с замахом: столько всего есть у игрока, " +
                                 "чтобы уйти.")]
        float spitFlightTime = 0.75f;

        /// <summary>Вид умеет плеваться.</summary>
        public bool Spits => spitRange > 0f && spitDamage > 0f;

        [SerializeField, Tooltip("С какой секунды забега этот вид вообще " +
                                 "появляется. Ноль — с самого начала. " +
                                 "Крупный приходит в середине второй волны: " +
                                 "первую игрок должен отстоять, разобравшись " +
                                 "с обычными.")]
        float unlockAfter;

        [SerializeField, Tooltip("Частота ПО ВОЛНАМ, если вид должен " +
                                 "появляться не всегда одинаково часто. " +
                                 "Первое число — первая волна.\n\n" +
                                 "Ноль означает, что в этой волне вида нет вовсе. " +
                                 "Волн больше, чем чисел — берётся последнее. " +
                                 "Список пуст — во всех волнах работает обычная " +
                                 "частота выше.\n\n" +
                                 "В волнах, а не в секундах: длительность волн " +
                                 "правится, и привязка к секундам поехала бы " +
                                 "вместе с ней.")]
        float[] spawnWeightByWave = new float[0];

        [SerializeField, Tooltip("Сколько таких может быть на поле одновременно. " +
                                 "Ноль — без ограничения.\n\n" +
                                 "Не то же самое, что частота: частота решает, " +
                                 "как часто вид выпадает, а это — сколько его " +
                                 "терпит поле. Двое плевунов накрывают отряд " +
                                 "с двух сторон, и уйти становится некуда, " +
                                 "сколь бы редко они ни выпадали.")]
        int maxAlive;

        /// <summary>Потолок одновременно живых этого вида. Ноль — без потолка.</summary>
        public int MaxAlive => Mathf.Max(0, maxAlive);

        /// <summary>Как часто этот вид выпадает при спавне.</summary>
        public float SpawnWeight => Mathf.Max(0f, spawnWeight);

        /// <summary>
        /// Частота в волне с этим номером (первая волна — единица).
        /// Ноль вне волн забега и там, где список говорит ноль.
        /// </summary>
        public float WeightForWave(int wave)
        {
            if (spawnWeightByWave == null || spawnWeightByWave.Length == 0) return SpawnWeight;
            if (wave < 1) return SpawnWeight;

            int index = Mathf.Min(wave, spawnWeightByWave.Length) - 1;
            return Mathf.Max(0f, spawnWeightByWave[index]);
        }

        /// <summary>Секунда забега, раньше которой вид не спавнится.</summary>
        public float UnlockAfter => Mathf.Max(0f, unlockAfter);

        /// <summary>
        /// Во сколько раз этот зомби дороже обычного первотирного —
        /// по числу ударов, которые в него надо вложить.
        ///
        /// По этому числу считается добыча. Иначе крупный, стоящий четырёх
        /// обычных, приносит столько же, сколько один: игрок платит вчетверо
        /// больше времени за ту же награду, и чем дальше в забег, тем реже
        /// капают тир-апы — ровно там, где они нужнее всего.
        /// </summary>
        public float Effort => Tier * Mathf.Max(0.01f, healthMultiplier);
        Health health;
        Renderer[] renderers;

        Material tierMaterial;
        Material flashMaterial;

        /// <summary>
        /// Материалы горения: по одному на каждый исходный, а не на зомби.
        /// Зомби бывает две сотни, обликов — единицы.
        /// </summary>
        static readonly Dictionary<Material, Material> BurnMaterials = new Dictionary<Material, Material>();

        float burnUntil;
        float burnDps;
        float nextBurnTick;
        float nextWispTime;
        bool burning;

        float stunUntil;
        float flashUntil;

        Survivor target;
        Vector3 knockbackVelocity;
        float knockbackUntil;
        float nextRetargetTime;
        float nextContactTime;
        float despawnTime;
        bool dying;

        /// <summary>Длина падения, секунд. Снимается с клипа или с запечённого набора.</summary>
        float deathClipLength;

        /// <summary>
        /// Свечение материалов, каким его задал художник.
        ///
        /// Хранится ПО МАТЕРИАЛУ и запоминается один раз. Держать его
        /// у зомби нельзя: материал тира общий, и каждый следующий плевун
        /// брал за основу уже подкрученное пульсом значение. За несколько
        /// поколений оно множилось само на себя и схлопывалось — свечение
        /// гасло насовсем, как только умирал первый.
        /// </summary>
        static readonly Dictionary<Material, Color> BaseEmission = new Dictionary<Material, Color>();

        bool hasEmission;

        /// <summary>Замах начат: кого и когда ударит.</summary>
        Survivor pendingVictim;
        float pendingHitTime;

        /// <summary>Замах начат: зона показана, плевок ещё не вылетел.</summary>
        float spitReleaseTime;

        /// <summary>До этого времени плевун стоит, доигрывая анимацию.</summary>
        float spitHoldUntil;
        float nextSpitTime;
        Vector3 spitAimPoint;
        AcidZone spitZone;

        /// <summary>Круг под самим плевуном: по нему его видно в толпе.</summary>
        AcidZone spitMark;

        public bool IsDead => health == null || health.IsDead;

        /// <summary>Точка попадания — грудь, а не пятки: туда летит трасса.</summary>
        public Vector3 HitPoint => transform.position + Vector3.up * (PopupHeight() * 0.6f);

        /// <summary>Радиус тела с учётом масштаба тира: крупные толкаются шире.</summary>
        public float BodyRadius => config == null ? 0.45f : config.zombieSeparationRadius * transform.localScale.x;

        /// <summary>Тир 1..N. Чем выше, тем больше здоровья, размера и желтизны.</summary>
        public int Tier { get; private set; }

        public event System.Action<Zombie> Released;

        /// <summary>
        /// Убит. Отличается от Released: тот приходит через пару секунд,
        /// когда труп уходит в пул, а искру ронять надо в момент смерти,
        /// на том месте, где зомби упал.
        /// </summary>
        public event System.Action<Zombie> Died;

        void Awake()
        {
            health = GetComponent<Health>();
            animator = GetComponentInChildren<Animator>();
            baked = GetComponentInChildren<BakedZombieView>(true);
            renderers = GetComponentsInChildren<Renderer>();
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        void OnDestroy()
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
            Registry.Zombies.Remove(this);
        }

        /// <summary>
        /// Полный сброс состояния при выдаче из пула. Всё, что копится по ходу
        /// жизни — таймеры, цель, отброс, вспышка, фаза смерти, поза аниматора —
        /// обнуляется здесь. Иначе следующий жилец рождается уже мёртвым
        /// и подсвеченным (§8 project.md).
        /// </summary>
        public void Init(ArenaConfig cfg, int tier, Material tierMat, Material flashMat, float scale)
        {
            config = cfg;
            Tier = tier;

            // Здоровье тира — ровно N попаданий из пистолета. Так градация
            // читается игроком без цифр: «этот с трёх выстрелов».
            // Живучесть и размер вида умножаются на тирные: крупный зомби
            // первого тира всё равно крупнее и живучее обычного первого.
            health.Init(tier * cfg.zombieHitsPerTier * Mathf.Max(0.01f, healthMultiplier));

            transform.localScale = Vector3.one * (scale * Mathf.Max(0.01f, scaleMultiplier));

            tierMaterial = tierMat;
            flashMaterial = flashMat;

            // Исходное свечение запоминаем один раз: пульс крутит его вокруг
            // этого значения, и брать за основу уже подкрученное — значит
            // за несколько кадров увести материал куда угодно.
            hasEmission = emissionPulseSpeed > 0f && tierMat != null && tierMat.HasProperty("_EmissionColor");
            if (hasEmission && !BaseEmission.ContainsKey(tierMat))
                BaseEmission[tierMat] = tierMat.GetColor("_EmissionColor");
            flashUntil = 0f;
            burning = false;
            burnUntil = 0f;
            burnDps = 0f;
            stunUntil = 0f;
            scorchPending = 0f;
            scorchFlushTime = 0f;
            quietHit = false;
            ApplyMaterial(tierMaterial);

            target = null;
            dying = false;
            despawnTime = 0f;
            nextContactTime = 0f;
            pendingVictim = null;
            pendingHitTime = 0f;
            CancelSpit();
            nextSpitTime = Time.time + Random.value * Mathf.Max(0.1f, spitInterval);
            knockbackUntil = 0f;
            knockbackVelocity = Vector3.zero;
            nextRetargetTime = Time.time + Random.value * cfg.retargetInterval;

            if (baked != null) baked.Play(ClipRunning);
            else if (animator != null)
            {
                animator.Rebind();
                animator.Update(0f);
            }

            if (!Registry.Zombies.Contains(this)) Registry.Zombies.Add(this);
        }

        /// <summary>
        /// Сообщает зомби, что его показывает запечённая анимация.
        ///
        /// Отдельным вызовом, а не поиском в Awake: спавнер переводит зомби
        /// уже после создания объекта, когда Awake давно прошёл.
        /// </summary>
        public void UseBakedView(BakedZombieView view) => baked = view;

        public void TakeHit(float damage)
        {
            if (dying) return;
            health.TakeDamage(damage, transform.position);
        }

        /// <summary>
        /// Урон от огня: тот же урон, но БЕЗ вспышки и с цифрами пачкой.
        ///
        /// Струя бьёт семь раз в секунду, горение — три. Через обычный
        /// удар каждый тик белил зомби вспышкой и выпускал цифру: горящий
        /// стоял белым силуэтом, а над головой у него рос столб из цифр.
        /// Огонь при этом не читался вовсе — его перекрывала вспышка.
        ///
        /// Цифры копятся и вылетают одной раз в полсекунды: игроку важно,
        /// СКОЛЬКО снимает огонь, а не сколько раз в секунду он это делает.
        /// </summary>
        public void Scorch(float damage)
        {
            if (dying || damage <= 0f) return;

            quietHit = true;
            health.TakeDamage(damage, transform.position);
            quietHit = false;
        }

        /// <summary>Урон идёт тихим путём: без вспышки, цифра в копилку.</summary>
        bool quietHit;

        /// <summary>Накопленный урон огнём, ещё не показанный цифрой.</summary>
        float scorchPending;
        float scorchFlushTime;

        /// <summary>
        /// Поджигает. Повторное попадание ПРОДЛЕВАЕТ горение, а не складывает
        /// два костра: иначе восемь огнемётчиков сжигали бы толпу мгновенно,
        /// и поджиг перестал бы быть довеском к струе.
        /// </summary>
        public void Ignite(float seconds, float damagePerSecond)
        {
            if (dying || seconds <= 0f || damagePerSecond <= 0f) return;

            burnUntil = Mathf.Max(burnUntil, Time.time + seconds);
            burnDps = Mathf.Max(burnDps, damagePerSecond);

            if (!burning)
            {
                burning = true;
                nextBurnTick = Time.time + Mathf.Max(0.05f, config.burnTickInterval);
                nextWispTime = Time.time;
                ApplyMaterial(BurnTwin());
            }
        }

        /// <summary>Горит ли сейчас. Читается интерфейсом и отладкой.</summary>
        public bool Burning => burning;

        /// <summary>
        /// Оглушает: враг стоит и не бьёт, пока не очнётся.
        ///
        /// Продлевает, а не складывает — как и поджиг. Иначе восемь
        /// молотобойцев с оглушением держали бы толпу в постоянном ступоре,
        /// и выбор между отбросом и оглушением перестал бы быть выбором.
        ///
        /// Отброс при этом НЕ отменяется: оглушённого можно и отшвырнуть,
        /// это разные вещи. Просто у молота включено что-то одно.
        /// </summary>
        public void Stun(float seconds)
        {
            if (dying || seconds <= 0f) return;
            stunUntil = Mathf.Max(stunUntil, Time.time + seconds);
        }

        /// <summary>Оглушён ли сейчас.</summary>
        public bool Stunned => Time.time < stunUntil;

        void UpdateBurning()
        {
            if (!burning) return;

            if (Time.time >= burnUntil)
            {
                burning = false;
                burnDps = 0f;
                if (flashUntil <= 0f) ApplyMaterial(tierMaterial);
                return;
            }

            if (Time.time >= nextWispTime)
            {
                nextWispTime = Time.time + 0.12f;
                FlameJet.Wisp(HitPoint);
            }

            if (Time.time < nextBurnTick) return;

            float step = Mathf.Max(0.05f, config.burnTickInterval);
            nextBurnTick = Time.time + step;

            // Урон идёт ТИКАМИ, а не каждый кадр: при двух сотнях горящих
            // покадровое начисление — это две сотни вызовов урона в кадр
            // ради той же самой цифры.
            Scorch(burnDps * step);
        }

        /// <summary>Выпускает накопленную цифру огня. Зовётся из Update.</summary>
        void FlushScorch()
        {
            if (scorchFlushTime <= 0f || Time.time < scorchFlushTime) return;

            if (scorchPending >= 0.5f)
                DamagePopup.Spawn(transform.position + Vector3.up * PopupHeight(), scorchPending);

            scorchPending = 0f;
            scorchFlushTime = 0f;
        }

        /// <summary>Копия материала тира со свечением огня. Текстуру сохраняем.</summary>
        Material BurnTwin()
        {
            if (tierMaterial == null) return null;
            if (BurnMaterials.TryGetValue(tierMaterial, out var ready) && ready != null) return ready;

            var twin = new Material(tierMaterial) { name = tierMaterial.name + "_Горит" };

            // Текстуру НЕ снимаем, в отличие от вспышки удара: горящий зомби
            // должен оставаться собой, иначе толпа огня читается как толпа
            // одинаковых оранжевых силуэтов.
            if (twin.HasProperty("_EmissionColor"))
            {
                twin.EnableKeyword("_EMISSION");
                twin.SetColor("_EmissionColor", config.burnGlowColor);
            }
            if (twin.HasProperty("_BaseColor"))
                twin.SetColor("_BaseColor", Color.Lerp(twin.GetColor("_BaseColor"), config.burnGlowColor, 0.35f));

            BurnMaterials[tierMaterial] = twin;
            return twin;
        }

        /// <summary>
        /// Отбрасывает тело. На время полёта расталкивание отключается —
        /// летящий проходит сквозь других: удерживать его снаружи чужих тел
        /// значило бы гасить весь отброс о первого же встречного, и удар
        /// перестал бы читаться.
        ///
        /// Работает и на убитом. Смертельный удар должен отбрасывать так же,
        /// как несмертельный: иначе самый сильный удар выглядит самым слабым —
        /// зомби, которого добили, оседает на месте, а выживший улетает.
        /// </summary>
        public void Knockback(Vector3 direction, float distance, float duration)
        {
            if (duration <= 0f) return;

            // Сопротивление ВЫЧИТАЕТСЯ из дальности удара, а не делит её.
            // Так тяжёлого врага можно сдвинуть только оружием, которое бьёт
            // сильнее его упора, — и разница между лопатой на три метра
            // и упором в два читается как «еле пошатнулся», а не как
            // «отлетел чуть меньше».
            distance -= Mathf.Max(0f, knockbackResist);
            if (distance <= 0f) return;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            knockbackVelocity = direction.normalized * (distance / duration);
            knockbackUntil = Time.time + duration;
        }

        void OnDamaged(float amount, Vector3 point)
        {
            if (quietHit)
            {
                scorchPending += amount;
                if (scorchFlushTime <= 0f) scorchFlushTime = Time.time + 0.5f;
                return;
            }

            // Цифра вылетает из зомби, а не из бойца: игрок смотрит туда,
            // куда стреляет.
            DamagePopup.Spawn(point + Vector3.up * PopupHeight(), amount);

            if (flashMaterial == null) return;
            flashUntil = Time.time + config.hitFlashDuration;
            ApplyMaterial(flashMaterial);
        }

        /// <summary>
        /// Вспышка сделана ПОДМЕНОЙ ОБЩЕГО МАТЕРИАЛА, а не MaterialPropertyBlock.
        /// Блок свойств задаётся на каждый рендерер отдельно и выбивает
        /// подсвеченных из общей пачки отрисовки; подменённый общий материал
        /// один на весь тир, поэтому все вспыхнувшие рисуются вместе.
        /// </summary>
        void ApplyMaterial(Material material)
        {
            if (material == null) return;

            // У запечённого зомби материал тира не подходит: он на шейдере
            // со скиннингом. Двойник на шейдере запечённой анимации несёт
            // ту же текстуру и тот же оттенок.
            if (baked != null)
            {
                baked.SetMaterial(material);
                return;
            }

            for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = material;
        }

        float PopupHeight()
        {
            if (renderers.Length == 0) return 1.5f;
            return renderers[0].bounds.size.y * 0.9f;
        }

        /// <summary>
        /// Дышит свечением. Только когда вспышка не идёт: та подменяет
        /// материал целиком, и крутить в это время свечение исходного
        /// значило бы менять то, чего сейчас не видно.
        /// </summary>
        void PulseEmission()
        {
            if (!hasEmission || flashUntil > 0f || tierMaterial == null) return;

            if (!BaseEmission.TryGetValue(tierMaterial, out var start)) return;

            float wave = (Mathf.Sin(Time.time * emissionPulseSpeed) + 1f) * 0.5f;
            float factor = Mathf.Lerp(emissionPulseLow, Mathf.Max(emissionPulseLow, emissionPulseHigh), wave);
            tierMaterial.SetColor("_EmissionColor", start * factor);
        }

        void OnDied()
        {
            dying = true;
            CancelSpit();
            // Начатый полёт НЕ сбрасываем: тело должно долететь и упасть там,
            // куда его отбросило, а не замереть в точке смерти.
            // Из реестра убираем сразу, чтобы бойцы не расстреливали труп.
            Registry.Zombies.Remove(this);
            Died?.Invoke(this);
            if (baked != null) baked.Play(ClipDying, config.zombieDeathSpeed);
            // ниже — см. despawnTime: тело не убираем раньше конца падения
            else if (animator != null)
            {
                // Незабранные триггеры ГАСИМ, иначе они переиграют смерть.
                // Переходы из «любого состояния» разбираются по порядку,
                // а плевок и удар заведены в контроллере раньше смерти:
                // зомби, погибший в тот же кадр, в котором замахнулся,
                // начинал не падать, а плевать.
                animator.ResetTrigger(SpitParam);
                animator.ResetTrigger(AttackParam);
                animator.SetTrigger(DieParam);
            }
            // Труп лежит НЕ МЕНЬШЕ, чем идёт само падение. Число из конфига
            // добавляет время сверху, но обрезать падение не может: зомби,
            // исчезающий на середине falling, читается как пропавший, а не
            // как убитый — а обратная связь по убийству здесь важнее всего.
            float speed = Mathf.Max(0.05f, config.zombieDeathSpeed);
            despawnTime = Time.time + Mathf.Max(config.zombieCorpseTime, DeathLength() / speed);
        }

        /// <summary>
        /// Сколько длится падение. Ищется один раз и запоминается: у
        /// запечённого зомби длина лежит в наборе, у обычного — в клипе.
        /// </summary>
        float DeathLength()
        {
            if (deathClipLength > 0f) return deathClipLength;

            if (bakedSet != null)
            {
                foreach (var clip in bakedSet.clips)
                    if (clip.name.IndexOf("Dying", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        deathClipLength = clip.Length;
            }

            if (deathClipLength <= 0f && animator != null && animator.runtimeAnimatorController != null)
            {
                foreach (var clip in animator.runtimeAnimatorController.animationClips)
                    if (clip != null && clip.name.IndexOf("Dying", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        deathClipLength = clip.length;
                        break;
                    }
            }

            return deathClipLength;
        }

        void Update()
        {
            if (config == null) return;
            // Отключается стендом нагрузки: так видно, сколько стоят мозги
            // и расталкивание отдельно от отрисовки.
            if (!config.simulateZombies) return;

            if (flashUntil > 0f && Time.time >= flashUntil)
            {
                flashUntil = 0f;
                ApplyMaterial(tierMaterial);
            }

            PulseEmission();
            UpdateBurning();
            FlushScorch();

            if (dying)
            {
                // Труп продолжает лететь, пока не выйдет время отброса,
                // и только потом ждёт своей очереди в пул.
                if (Time.time < knockbackUntil) FlyBack();
                if (Time.time >= despawnTime) Released?.Invoke(this);
                return;
            }

            if (Time.time < knockbackUntil)
            {
                FlyBack();
                return;
            }

            // Оглушённый стоит: не идёт, не бьёт, не плюёт. Замах при этом
            // сбрасываем — иначе зомби, оглушённый посреди плевка, доплюнет,
            // и оглушение окажется без последствий ровно в том случае,
            // ради которого его и берут.
            if (Stunned)
            {
                CancelSpit();
                SetMoving(false);
                return;
            }

            UpdateTarget();

            // Начатый замах доводится до конца НЕЗАВИСИМО от того, жива ли
            // цель: зона уже обещала удар в конкретную точку, и плевок должен
            // туда прилететь.
            //
            // Без этого плевун, потерявший цель посреди замаха, застревал
            // в нём навсегда — выход по «цели нет» стоял раньше. В замере
            // это выглядело так: шесть плевунов, из них один вечно «в замахе»,
            // и зона от него давно погасла.
            if (spitReleaseTime > 0f && Time.time >= spitReleaseTime) Release();

            if (target == null)
            {
                CancelSpit();
                return;
            }

            var to = target.transform.position - transform.position;
            to.y = 0f;
            float distance = to.magnitude;

            // Идёт ли он в этом кадре. Ниже ветки её сбрасывают, и в самом
            // конце по ней переключается покой.
            bool moving = true;

            if (Spits && UpdateSpit(to, distance))
            {
                // Плевун стоит: и на замахе, и доигрывая плевок, и всю
                // перезарядку. Бег на месте с доворотом к отряду выглядит
                // сломанным.
                SetMoving(false);

                // Плевун, вставший на позицию, стоит НАМЕРТВО — и на замахе,
                // и доигрывая плевок, и всю перезарядку. Расталкивание
                // не считаем вовсе: замер показал, что толпа сзади сносила
                // его на метр в секунду, и он всё равно ехал за отрядом.
                //
                // Соседям это не мешает: в расталкивании каждый двигает СЕБЯ
                // и на половину перекрытия, так что они разойдутся с ним
                // и без его участия — просто вдвое медленнее.
                return;
            }

            if (distance > config.zombieContactRange)
            {
                var direction = to / Mathf.Max(distance, 0.0001f);
                var next = transform.position + direction * (config.zombieSpeed * Time.deltaTime);
                if (!ignoresObstacles) next = Obstacle.Resolve(next, BodyRadius);

                transform.position = next;
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
            else
            {
                // Дошёл — стоит и бьёт. Бег на месте у стоящего вплотную
                // читается как заедание анимации.
                moving = false;
                FaceTowards(to);

                if (Time.time >= nextContactTime) Strike();
            }

            SetMoving(moving);
            ResolvePendingHit();

            // Расцепление идёт ВСЕГДА, а не только на ходу. Дошедшие до отряда
            // стоят на месте, и именно они образуют неподвижную стену, в которую
            // спрессовывается всё, что подходит следом.
            ResolveOverlap();
        }

        // --- удар вблизи -----------------------------------------------------

        /// <summary>
        /// Замах. Урон наносится НЕ СЕЙЧАС, а на середине клипа.
        ///
        /// Иначе здоровье бойца убывает раньше, чем рука дошла, и удар
        /// читается как несвязанный с попаданием — а именно по нему игрок
        /// и должен понимать, кто именно его ест.
        /// </summary>
        void Strike()
        {
            nextContactTime = Time.time + config.zombieContactInterval;

            float clip = AttackClipLength();

            // Клип втрое длиннее темпа ударов, поэтому его ускоряем ровно
            // настолько, чтобы замах уложился в интервал. Замедлять не нужно:
            // при редких ударах он играет в своей скорости, а разница
            // уходит в паузу.
            float playback = Mathf.Max(1f, clip / Mathf.Max(config.zombieContactInterval, 0.05f));

            if (animator != null)
            {
                animator.SetFloat(AttackSpeedParam, playback);
                animator.SetTrigger(AttackParam);
            }

            pendingVictim = target;
            pendingHitTime = Time.time + clip * 0.45f / playback;
        }

        void ResolvePendingHit()
        {
            if (pendingVictim == null || Time.time < pendingHitTime) return;

            var victim = pendingVictim;
            pendingVictim = null;

            if (victim == null || !victim.isActiveAndEnabled || victim.Health.IsDead) return;

            // Дистанцию перепроверяем: за время замаха отряд мог уйти,
            // и удар в пустоту засчитывать нечестно.
            var away = victim.transform.position - transform.position;
            away.y = 0f;
            if (away.magnitude > config.zombieContactRange * 1.4f) return;

            victim.ApplyDamage(config.zombieContactDamage, victim.transform.position);
        }

        /// <summary>
        /// Длина клипа удара, взятая у самого контроллера.
        ///
        /// Не числом в коде: клип заменят, и захардкоженная длина молча
        /// разъедется с картинкой — удар начнёт попадать не в тот момент.
        /// </summary>
        float AttackClipLength()
        {
            if (attackClipLength > 0f) return attackClipLength;

            attackClipLength = 2.8f;
            if (animator == null || animator.runtimeAnimatorController == null) return attackClipLength;

            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip == null || clip.name != "Zombie Attack") continue;
                attackClipLength = Mathf.Max(0.1f, clip.length);
                break;
            }

            return attackClipLength;
        }

        void SetMoving(bool moving)
        {
            if (animator != null) animator.SetBool(MovingParam, moving);
        }

        // --- кислотный плевок ------------------------------------------------

        /// <summary>
        /// Ведёт замах и выстрел. Возвращает true, пока плевун занят —
        /// тогда обычное преследование в этом кадре не выполняется.
        ///
        /// Плевун ОСТАНАВЛИВАЕТСЯ на весь замах. Плюющий на бегу читался бы
        /// как случайность: игрок не понимает, кто именно и когда в него
        /// выстрелил. Остановка — это и есть предупреждение, а красный круг
        /// говорит куда.
        /// </summary>
        bool UpdateSpit(Vector3 toTarget, float distance)
        {
            // Замах идёт — стоим и доводим его до конца. Прицел НЕ обновляем:
            // круг обещал точку, и плевок должен прилететь именно туда,
            // иначе уходить из круга бессмысленно.
            // Замах идёт — стоим НЕПОДВИЖНО, не доворачиваясь. Разворот
            // взят один раз, в момент прицеливания: плевок летит в точку,
            // а не в бойца, и водить корпусом за уходящим отрядом — значит
            // целиться в одно, а смотреть в другое.
            if (spitReleaseTime > 0f) return true;

            // Капля уже вылетела, но анимация ещё идёт — тоже стоим. Иначе
            // плевун трогается с места посреди собственного плевка и едет
            // за отрядом, скользя по земле неподвижными ногами.
            if (Time.time < spitHoldUntil) return true;

            // Слишком далеко — идём сближаться обычным ходом.
            if (distance > spitRange) return false;

            // Враг вплотную: кислота своих не разбирает, и плевать себе
            // под ноги плевун не станет. Отдаём его обычной драке.
            if (distance <= config.zombieContactRange) return false;

            // Перезарядка. СТОИМ И ЖДЁМ, а не идём сближаться: дистанция
            // уже подходящая, и подходить ближе плевуну незачем — он от того
            // только теряет своё единственное преимущество и оказывается
            // под лопатами.
            //
            // Раньше здесь был выход «не занят», и дальше работал обычный
            // блок ходьбы: плевун послушно шёл вплотную к отряду и следующий
            // плевок делал уже в упор.
            if (Time.time < nextSpitTime)
            {
                FaceTowards(toTarget);
                return true;
            }

            Aim();
            return true;
        }

        /// <summary>Останавливается, показывает зону и начинает замах.</summary>
        void Aim()
        {
            spitAimPoint = target.transform.position;
            spitAimPoint.y = 0f;

            spitReleaseTime = Time.time + Mathf.Max(0.05f, spitWindup);

            // Разворот на точку прицеливания — ОДИН РАЗ, здесь. Дальше
            // до конца плевка корпус не двигается.
            var toAim = spitAimPoint - transform.position;
            toAim.y = 0f;
            FaceTowards(toAim);

            // Зона живёт весь замах И весь полёт: она гаснет в момент
            // попадания, а не раньше.
            float shows = Mathf.Max(0.05f, spitWindup) + Mathf.Max(0.05f, spitFlightTime);
            spitZone = AcidZone.Show(spitAimPoint, spitRadius, shows);

            // Круг под ногами самого плевуна. Красный круг говорит КУДА
            // прилетит, а этот — ОТКУДА летит: в полусотне зомби разглядеть
            // стрелка иначе нельзя, и уйти из зоны игрок успевает, а понять,
            // кого за это бить, — нет.
            // Ноль радиуса выключает метку. Сейчас она выключена: жёлтое
            // кольцо на песочной земле не читается, а другого цвета,
            // который не спорил бы с красной зоной, пока не подобрано.
            if (config.spitterMarkRadius > 0f)
            {
                var under = transform.position;
                under.y = 0f;
                spitMark = AcidZone.ShowMark(under, config.spitterMarkRadius, shows + Mathf.Max(0f, spitHold));
            }

            if (animator != null) animator.SetTrigger(SpitParam);
        }

        void Release()
        {
            spitReleaseTime = 0f;
            spitHoldUntil = Time.time + Mathf.Max(0f, spitHold);
            nextSpitTime = Time.time + Mathf.Max(0.2f, spitInterval);

            // Вылетает изо рта, а не из-под ног: иначе плевок начинает полёт
            // в земле и первую треть пути его не видно.
            var mouth = transform.position + Vector3.up * (PopupHeight() * 0.72f) + transform.forward * 0.35f;

            AcidDrop.Spit(mouth, spitAimPoint, spitFlightTime, spitRadius, spitDamage, spitZone);
            spitZone = null;
        }

        /// <summary>
        /// Снимает начатый замах. Нужен и пулу, и смерти: зона, оставшаяся
        /// от убитого плевуна, обещает удар, которого не будет, — а игрок
        /// по ней уводит отряд.
        /// </summary>
        void CancelSpit()
        {
            if (spitZone != null) spitZone.Hide();
            spitZone = null;
            if (spitMark != null) spitMark.Hide();
            spitMark = null;
            spitReleaseTime = 0f;
            spitHoldUntil = 0f;

            if (animator != null) animator.ResetTrigger(SpitParam);
        }

        void FaceTowards(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        /// <summary>Шаг полёта от удара. В стену не пускаем и живого, и мёртвого.</summary>
        void FlyBack()
        {
            var flight = transform.position + knockbackVelocity * Time.deltaTime;
            transform.position = ignoresObstacles ? flight : Obstacle.Resolve(flight, BodyRadius);
        }

        /// <summary>
        /// Разводит тела, которые уже пересеклись, СДВИГОМ ПОЗИЦИИ, а не силой.
        ///
        /// Отталкивание через скорость здесь не работает: в однородном коме
        /// соседи окружают зомби со всех сторон, их толчки взаимно гасятся,
        /// сумма выходит около нуля — и ком остаётся комом. Позиционная
        /// коррекция гасит не сумму сил, а каждое пересечение по отдельности,
        /// поэтому расцепляет даже симметричную кучу.
        ///
        /// Каждый двигает только СЕБЯ и только на половину перекрытия: вторую
        /// половину в тот же кадр выберет сосед со своей стороны.
        /// </summary>
        void ResolveOverlap()
        {
            if (config.zombieSeparationRadius <= 0f) return;

            float myRadius = BodyRadius;
            // Клетка сетки — с запасом от самой крупной возможной пары.
            ZombieGrid.EnsureBuilt(myRadius * 4f);
            ZombieGrid.Neighbours(transform.position, Neighbours);

            var shift = Vector3.zero;
            for (int i = 0; i < Neighbours.Count; i++)
            {
                var other = Neighbours[i];
                if (other == this || other == null || other.dying) continue;

                // Летящего от удара не удерживаем — он сейчас проходит насквозь.
                if (Time.time < other.knockbackUntil) continue;

                var away = transform.position - other.transform.position;
                away.y = 0f;

                float wanted = myRadius + other.BodyRadius;
                float distSqr = away.sqrMagnitude;
                if (distSqr >= wanted * wanted) continue;

                float dist = Mathf.Sqrt(distSqr);
                if (dist < 0.0001f)
                {
                    // Тела совпали точка в точку — направления нет. Расталкиваем
                    // по стабильному признаку, а не случайно: случайное
                    // направление каждый кадр даёт дрожание на месте.
                    float angle = GetInstanceID() * 0.017f;
                    away = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    dist = 0.0001f;
                }

                shift += away / dist * ((wanted - dist) * 0.5f);
            }

            if (shift.sqrMagnitude < 0.000001f) return;

            // Коэффициент меньше единицы гасит перелёт: за кадр выбирается
            // часть перекрытия, за несколько кадров — всё.
            var pushed = transform.position + shift * Mathf.Clamp01(config.zombieSeparationStrength);
            transform.position = ignoresObstacles ? pushed : Obstacle.Resolve(pushed, BodyRadius);
        }

        void UpdateTarget()
        {
            if (target != null && target.isActiveAndEnabled && !target.Health.IsDead &&
                Time.time < nextRetargetTime)
                return;

            nextRetargetTime = Time.time + config.retargetInterval;

            Survivor best = null;
            float bestSqr = float.MaxValue;

            var survivors = Registry.Survivors;
            for (int i = 0; i < survivors.Count; i++)
            {
                var survivor = survivors[i];
                if (survivor == null || !survivor.isActiveAndEnabled || survivor.Health.IsDead) continue;

                float distSqr = (survivor.transform.position - transform.position).sqrMagnitude;
                if (distSqr >= bestSqr) continue;

                best = survivor;
                bestSqr = distSqr;
            }

            target = best;
        }
    }
}
