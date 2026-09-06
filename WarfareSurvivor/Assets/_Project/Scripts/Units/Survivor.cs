using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Боец отряда. Держит свой слот, сам ищет цель и сам атакует —
    /// игрок в бой не вмешивается (§3 project.md).
    ///
    /// Чем именно он бьёт и с какой дистанции, решает класс, а не этот
    /// компонент: добавление нового класса не должно требовать правки кода.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class Survivor : MonoBehaviour
    {
        static readonly int SpeedParam = Animator.StringToHash("Speed");
        static readonly int MoveDirParam = Animator.StringToHash("MoveDir");
        static readonly int AttackParam = Animator.StringToHash("Attack");
        static readonly int AttackShieldParam = Animator.StringToHash("AttackShield");
        static readonly int DieParam = Animator.StringToHash("Die");
        static readonly int AttackSpeedParam = Animator.StringToHash("AttackSpeed");

        /// <summary>Имя слоя удара — тот же, что заводит CharacterSetupBuilder.</summary>
        const string AttackLayerName = "UpperBody";

        /// <summary>Пустое состояние слоя удара: на нём слой не должен весить ничего.</summary>
        const string NoAttackStateName = "NoAttack";

        /// <summary>Имя метки дула — его же ставит WeaponBuilder.</summary>
        const string MuzzleName = "Muzzle";

        /// <summary>Как боец держится по отношению к движению отряда.</summary>
        enum Stance
        {
            /// <summary>Отряд стоит: разворачиваемся к врагу целиком.</summary>
            Standing,

            /// <summary>Тело смотрит по движению, бег вперёд.</summary>
            Forward,

            /// <summary>Тело смотрит ПРОТИВ движения, бег назад. Враг сзади.</summary>
            Backward
        }

        ArenaConfig config;
        SurvivorClassSO klass;
        SquadController squad;
        Animator animator;
        Health health;
        TorsoAim torsoAim;

        /// <summary>
        /// Материалы вспышки: по одному на каждый исходный материал,
        /// а не на каждого бойца. Бойцов полтора десятка, а обликов
        /// три-четыре — незачем плодить копии.
        /// </summary>
        static readonly Dictionary<Material, Material> FlashMaterials = new Dictionary<Material, Material>();

        Renderer[] renderers;
        Material[] baseMaterials;
        float flashUntil;

        Zombie target;
        Zombie pendingVictim;

        /// <summary>
        /// Замах начат, попадание ещё впереди.
        ///
        /// Отдельно от pendingVictim, потому что удар по площади цели не
        /// имеет: намеченный зомби может умереть, пока идёт замах, а молот
        /// всё равно обязан ударить — иначе боец с площадным ударом молчит
        /// ровно в самой плотной толпе, где его и берут.
        /// </summary>
        bool hitPending;
        Stance stance;
        Transform muzzle;

        /// <summary>У бойца настоящая метка дула, а не запасная кисть.</summary>
        bool hasBarrel;

        int attackLayer = -1;
        bool hasAttackSpeed;

        /// <summary>Дуга замаха под ногами. Только у ближнего боя.</summary>
        MeleeArc meleeArc;

        /// <summary>Боец умер и доигрывает падение.</summary>
        bool dying;
        float hideTime;
        float attackLayerWeight;
        float attackClipLength = 0.5f;

        /// <summary>Длина падения, секунд. Снимается с клипа, а не задаётся числом.</summary>
        float deathClipLength;

        /// <summary>Длина замаха щитом. У него свой клип и своя длина.</summary>
        float shieldClipLength;

        /// <summary>Сколько ударов уже сделано — по счёту решается, что следующее.</summary>
        int swingCount;

        /// <summary>Текущий замах идёт ЩИТОМ. Нужно знать на момент попадания.</summary>
        bool pendingShield;
        float pendingHitTime;
        float nextRetargetTime;
        float nextAttackTime;
        float nextStanceChangeTime;
        Vector3 lastPosition;

        /// <summary>Время следующего лечения. Копится, пока лечить некого.</summary>
        float nextHealTime;

        /// <summary>Точка строя, которую держит этот боец. Ставит SquadController.</summary>
        public Vector3 SlotPosition { get; set; }

        /// <summary>
        /// Кого этот медик взял на себя. Читается ЧУЖИМИ медиками: пока боец
        /// числится за одним, второй его не берёт и уходит к следующему
        /// по тяжести.
        /// </summary>
        public Survivor HealTarget { get; private set; }

        public Health Health => health;
        public SurvivorClassSO Class => klass;

        /// <summary>Боец выбыл. Отряд по этому событию пересобирает строй.</summary>
        public event System.Action<Survivor> Lost;

        /// <summary>
        /// Урон с учётом улучшений, взятых за забег.
        ///
        /// Бонус хранится у ОТРЯДА, а не у бойца: иначе пополнение приходило
        /// бы без вложенного игроком, и каждый добор класса обесценивал бы
        /// предыдущие улучшения.
        /// </summary>
        float Damage => klass.damage *
                        (squad != null ? squad.DamageBonusFor(klass) * squad.DamageMultiplier : 1f);

        /// <summary>
        /// Пауза между атаками с учётом разгона от отряда.
        ///
        /// Считается ЗДЕСЬ, а не в конфиге: разгон приходит от живых
        /// бойцов рядом и меняется по ходу боя, а klass.attackInterval —
        /// это характер оружия, и трогать его нельзя.
        /// </summary>
        float AttackInterval =>
            klass.attackInterval / (squad != null ? squad.AttackSpeedMultiplier : 1f);

        /// <summary>
        /// Сколько медик доливает за раз, с учётом вложенного за забег.
        ///
        /// Умножается ТЕМ ЖЕ бонусом, что и урон. Карточка «усилить» означает
        /// «класс делает своё дело лучше», а не «бьёт сильнее»: у медика
        /// урон равен нулю, и прибавка к нему множила ноль — карточка
        /// выпадала, тратила выбор игрока и не делала ничего.
        /// </summary>
        float HealPower => klass.healPerTick * (squad != null ? squad.DamageBonusFor(klass) : 1f);

        public void Bind(SquadController owner, ArenaConfig cfg, SurvivorClassSO survivorClass)
        {
            squad = owner;
            config = cfg;
            klass = survivorClass;

            health = GetComponent<Health>();
            // Пополнение приходит уже с накопленной живучестью: иначе
            // новичок оказывался бы слабее тех, кто пришёл раньше.
            health.Init(klass.maxHealth * squad.HealthBonusFor(klass));

            animator = GetComponentInChildren<Animator>();
            muzzle = FindMuzzle();

            CacheAttackAnimation();
            CacheBolt();

            torsoAim = GetComponent<TorsoAim>();
            if (torsoAim != null)
            {
                torsoAim.Configure(config.torsoAimMaxAngle, config.torsoAimSpeed);
                torsoAim.Barrel = hasBarrel ? muzzle : null;

                // Поправка стойки и есть развал оружия, взятый со знаком минус:
                // фигуру доворачивают ровно настолько, насколько оружие
                // смотрит мимо. На бегу фигуру не доворачивают, и это же
                // число сообщаем довороту груди.
                torsoAim.PoseYawFallback = hasBarrel ? 0f : -klass.aimYawOffset;
            }

            // Дугу включает САМ КЛАСС: размах привязан к дальности его
            // оружия, и решать за все классы одной галочкой в общем конфиге
            // неправильно.
            //
            // Под try по той же причине, что и полоска здоровья ниже: это
            // украшение, и его поломка не должна мешать бойцу появиться.
            // Пренебрёг однажды — и исключение внутри косметики оборвало
            // создание отряда на первом же бойце, отряд вышел пустым,
            // и забег проигрывался мгновенно.
            if (klass.showAttackArc)
            {
                try
                {
                    meleeArc = MeleeArc.Attach(transform, klass.HitsArea ? klass.splashRadius : klass.attackRange,
                        squad.MeleeArcMaterial, klass.arcDegrees, klass.arcInner,
                        klass.arcHeight, klass.arcTilt);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[{name}] Дуга замаха не собралась: {e.Message}", this);
                }
            }

            var bar = GetComponent<HealthBarView>();
            if (bar != null)
            {
                // Полоска, трассы, вспышки — украшения. Любой их сбой не должен
                // мешать бойцу появиться: именно так одно исключение внутри
                // полоски оставило от отряда одного человека.
                try { bar.Bind(health, config); }
                catch (System.Exception e) { Debug.LogError($"[{name}] Полоска здоровья не собралась: {e.Message}", this); }
            }

            // Вспышка по удару — та же, что у зомби: подмена общего материала.
            // Косметика, поэтому под try: её поломка не должна мешать бойцу
            // появиться.
            try { CacheFlashMaterials(); }
            catch (System.Exception e) { Debug.LogError($"[{name}] Вспышка не собралась: {e.Message}", this); }

            health.Damaged += OnDamaged;
            health.Died += OnDied;

            SlotPosition = transform.position;
            lastPosition = transform.position;

            // Разводим пересчёт целей по времени: если все считают в один кадр,
            // на толпе это заметный шип, а не ровная нагрузка.
            nextRetargetTime = Time.time + Random.value * config.retargetInterval;

            Registry.Survivors.Add(this);
        }

        void OnDestroy()
        {
            Registry.Survivors.Remove(this);
            if (health != null)
            {
                health.Damaged -= OnDamaged;
                health.Died -= OnDied;
            }
        }

        /// <summary>
        /// Вспышка на получившем удар.
        ///
        /// Отвечает на вопрос «кого бьют». Полоска здоровья на него
        /// не отвечает: она мелкая и в куче из полутора десятков фигур
        /// читается как мелькание.
        ///
        /// Цвет НЕ белый, в отличие от зомби: белым у нас уже помечено
        /// попадание ПО зомби, и одинаковый цвет для «я попал» и «попали
        /// по мне» стирал бы разницу ровно там, где она важнее всего.
        /// </summary>
        void OnDamaged(float amount, Vector3 point)
        {
            DamageVignette.Hit();

            if (renderers == null || renderers.Length == 0) return;

            flashUntil = Time.time + Mathf.Max(0.02f, config.survivorFlashDuration);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                if (FlashMaterials.TryGetValue(baseMaterials[i], out var flash)) renderers[i].sharedMaterial = flash;
            }
        }

        void UpdateFlash()
        {
            if (flashUntil <= 0f || Time.time < flashUntil) return;

            flashUntil = 0f;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].sharedMaterial = baseMaterials[i];
        }

        void CacheFlashMaterials()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            baseMaterials = new Material[renderers.Length];

            for (int i = 0; i < renderers.Length; i++)
            {
                var source = renderers[i] != null ? renderers[i].sharedMaterial : null;
                baseMaterials[i] = source;
                if (source == null || FlashMaterials.ContainsKey(source)) continue;

                var flash = new Material(source) { name = source.name + "_Flash" };

                // Текстуру убираем: вспышка красит фигуру ровно, и по ней
                // сразу видно силуэт того, кого достали.
                foreach (var n in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" })
                    if (flash.HasProperty(n)) flash.SetTexture(n, null);

                foreach (var n in new[] { "_BaseColor", "_Color" })
                    if (flash.HasProperty(n)) flash.SetColor(n, config.survivorFlashColor);

                if (flash.HasProperty("_EmissionColor"))
                {
                    flash.EnableKeyword("_EMISSION");
                    flash.SetColor("_EmissionColor", config.survivorFlashColor * 0.7f);
                }

                FlashMaterials[source] = flash;
            }
        }

        void OnDied()
        {
            // Из реестра и из строя убираем СРАЗУ: мёртвый не должен
            // ни стрелять, ни занимать место в формации, ни ловить на себя
            // цели зомби. Из виду он уходит позже, доиграв падение.
            Registry.Survivors.Remove(this);
            Lost?.Invoke(this);

            dying = true;

            // Тело держим НЕ МЕНЬШЕ, чем идёт само падение.
            //
            // Число из конфига задаёт, сколько труп ещё лежит после падения,
            // но не может его обрезать: обрыв на середине читается как
            // пропавший боец, а не как погибший. Длину берём с клипа —
            // поменяют анимацию, и время подстроится само.
            float speed = Mathf.Max(0.05f, config.zombieDeathSpeed);
            float fall = deathClipLength / speed;
            hideTime = Time.time + Mathf.Max(Mathf.Max(0.1f, config.survivorCorpseTime), fall);

            if (animator != null)
            {
                animator.SetTrigger(DieParam);
                animator.speed = speed;
            }

            // Оружие в руке падающего выглядит приклеенным, но убирать его
            // отдельно незачем: тело уходит через секунду с небольшим.
        }

        /// <summary>
        /// Единственная дверь для урона по бойцу — через неё же работает
        /// отладочное бессмертие. Бить напрямую в Health нельзя: галочку
        /// пришлось бы проверять в каждом источнике урона.
        /// </summary>
        public void ApplyDamage(float amount, Vector3 point)
        {
            if (config != null && config.debugSquadInvincible) return;
            health.TakeDamage(amount, point);
        }

        void Update()
        {
            if (config == null) return;

            // Умерший доигрывает падение и уходит. Логику ему не считаем:
            // он уже не боец, но ещё тело на земле.
            if (dying)
            {
                if (Time.time >= hideTime) gameObject.SetActive(false);
                return;
            }

            if (health.IsDead) return;
            if (!config.simulateSurvivors) return;

            UpdateFlash();

            Move();
            Heal();
            UpdateTarget();
            UpdateStance();
            Aim();
            Attack();
            UpdateBurst();
            UpdateFlame();
            ResolvePendingHit();
            UpdateAttackLayer();
        }

        // --- движение -------------------------------------------------------

        void Move()
        {
            var toSlot = SlotPosition - transform.position;
            toSlot.y = 0f;

            var desired = Vector3.zero;
            float maxSpeed = config.squadSpeed * config.memberSpeedMultiplier;

            // Подходим к слоту тем медленнее, чем ближе: без этого боец
            // проскакивает точку и вечно дёргается вокруг неё.
            float arriveRadius = config.separationRadius * squad.UnitRadius;
            if (toSlot.sqrMagnitude > 0.0001f)
                desired = toSlot.normalized * Mathf.Min(maxSpeed, toSlot.magnitude / arriveRadius * maxSpeed);

            desired += Separation();

            var step = Vector3.ClampMagnitude(desired, maxSpeed) * Time.deltaTime;

            // Препятствие гасит только составляющую движения внутрь стены,
            // поэтому боец съезжает вдоль неё и обходит сам.
            transform.position = Obstacle.Resolve(transform.position + step, squad.UnitRadius);

            float travelled = (transform.position - lastPosition).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            lastPosition = transform.position;

            if (animator == null) return;

            animator.SetFloat(SpeedParam, Mathf.Clamp01(travelled / Mathf.Max(config.squadSpeed, 0.01f)));
            // Клип бега проигрывается назад, когда боец отбегает спиной.
            // Ноль сюда попасть не должен — состояние Run на нём замрёт.
            animator.SetFloat(MoveDirParam, stance == Stance.Backward ? -1f : 1f);
        }

        /// <summary>Расталкивание, чтобы бойцы не слипались в точку на разворотах.</summary>
        Vector3 Separation()
        {
            var push = Vector3.zero;
            float radius = config.separationRadius * squad.UnitRadius;
            float radiusSqr = radius * radius;

            var others = Registry.Survivors;
            for (int i = 0; i < others.Count; i++)
            {
                var other = others[i];
                if (other == this || other == null) continue;

                var away = transform.position - other.transform.position;
                away.y = 0f;
                float distSqr = away.sqrMagnitude;
                if (distSqr > radiusSqr || distSqr < 0.0001f) continue;

                push += away.normalized * ((radius - Mathf.Sqrt(distSqr)) / radius);
            }

            return push * config.separationStrength;
        }

        // --- цель и стойка ---------------------------------------------------

        void UpdateTarget()
        {
            if (!klass.Attacks)
            {
                target = null;
                return;
            }

            if (Time.time < nextRetargetTime && target != null && IsValidTarget(target)) return;

            nextRetargetTime = Time.time + config.retargetInterval;
            target = FindTarget();
        }

        /// <summary>
        /// Сначала ищем того, до кого боец достаёт НЕ разворачиваясь — то есть
        /// в пределах доворота груди от текущего положения тела. И только если
        /// таких нет, берём ближайшего вообще, соглашаясь на разворот.
        ///
        /// Без этого предпочтения боец в плотной толпе пируэтит: ближайший
        /// зомби постоянно меняется, и каждая смена цели за спину разворачивает
        /// тело на 180°. Стрелять при этом некогда.
        /// </summary>
        Zombie FindTarget()
        {
            var reachable = FindNearest(requireReachable: true);
            return reachable != null ? reachable : FindNearest(requireReachable: false);
        }

        Zombie FindNearest(bool requireReachable)
        {
            float rangeSqr = klass.attackRange * klass.attackRange;

            Zombie best = null;
            float bestSqr = float.MaxValue;

            var zombies = Registry.Zombies;
            for (int i = 0; i < zombies.Count; i++)
            {
                var zombie = zombies[i];
                if (zombie == null || zombie.IsDead) continue;

                var to = zombie.transform.position - transform.position;
                to.y = 0f;
                float distSqr = to.sqrMagnitude;
                if (distSqr > rangeSqr || distSqr >= bestSqr) continue;

                if (requireReachable && !CanReach(to)) continue;

                best = zombie;
                bestSqr = distSqr;
            }

            return best;
        }

        /// <summary>
        /// Дотянется ли боец до цели, не разворачивая ног.
        ///
        /// У стрелка спрашиваем доворот груди: он один знает, на сколько
        /// ствол развалило позой клипа. У ближнего боя ствола нет, и мера
        /// прежняя — угол от корня.
        /// </summary>
        bool CanReach(Vector3 to)
        {
            if (hasBarrel && torsoAim != null) return torsoAim.CanReach(to);
            return Vector3.Angle(transform.forward, to) <= config.torsoAimMaxAngle;
        }

        bool IsValidTarget(Zombie zombie)
        {
            if (zombie == null || zombie.IsDead || !zombie.isActiveAndEnabled) return false;

            var to = zombie.transform.position - transform.position;
            to.y = 0f;
            return to.sqrMagnitude <= klass.attackRange * klass.attackRange;
        }

        /// <summary>
        /// Выбирает, каким боком тело стоит к движению. Вариантов ровно два —
        /// вдоль движения или против него, — и берётся тот, к которому враг
        /// ближе.
        ///
        /// Промежуточных положений быть не должно: тело задаёт, что отыгрывают
        /// ноги. Развернув тело прямо на врага под произвольным углом, мы
        /// получим бойца, который перебирает ногами вперёд, а едет вбок.
        ///
        /// Угол меряется от направления ДВИЖЕНИЯ, а не от transform.forward:
        /// развернувшись, боец изменил бы собственный угол и условие бы поплыло.
        /// </summary>
        void UpdateStance()
        {
            if (squad == null || !squad.IsMoving)
            {
                stance = Stance.Standing;
                return;
            }

            if (target == null)
            {
                stance = Stance.Forward;
                return;
            }

            var toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;

            float angle = Vector3.Angle(squad.MoveDirection, toTarget);
            float threshold = stance == Stance.Backward
                ? config.backwardSwitchAngle - config.stanceHysteresis
                : config.backwardSwitchAngle + config.stanceHysteresis;

            var desired = angle > threshold ? Stance.Backward : Stance.Forward;
            if (desired == stance) return;

            // Разворот на 180° занимает заметное время, и всё это время боец
            // стоит боком к движению. Поэтому переворачиваемся не чаще, чем
            // раз в stanceMinHoldTime.
            if (Time.time < nextStanceChangeTime) return;

            stance = desired;
            nextStanceChangeTime = Time.time + config.stanceMinHoldTime;
        }

        void Aim()
        {
            if (stance == Stance.Standing)
            {
                if (target == null)
                {
                    if (torsoAim != null) torsoAim.Target = null;
                    return;
                }

                // Грудь доворачивается ДАЖЕ СТОЯ — она добирает остаток.
                //
                // Поправка стойки — величина приблизительная: она снята с
                // одного клипа, а поза меняется. Доворот груди считает
                // промах ствола по факту, каждый кадр, и потому не зависит
                // ни от того, насколько точно выставлена поправка, ни от
                // того, какой клип сейчас играет. Стоя ему обычно нечего
                // добирать — тем лучше, значит поправка верна.
                if (torsoAim != null)
                    torsoAim.Target = hasBarrel ? target.transform : null;

                var toTarget = target.transform.position - transform.position;
                toTarget.y = 0f;
                FaceTowards(toTarget, klass.aimYawOffset);
                return;
            }

            // В движении тело строго вдоль оси движения, к врагу тянется грудь.
            //
            // Поправку стойки здесь НЕ применяем. Она про то, как боец стоит
            // на месте у цели; на бегу же ноги обязаны смотреть туда, куда
            // боец бежит, иначе он едет боком. Раньше поправка шла и сюда —
            // и коп бежал вполоборота к собственному движению.
            FaceTowards(stance == Stance.Backward ? -squad.MoveDirection : squad.MoveDirection, 0f);
            if (torsoAim != null) torsoAim.Target = target != null ? target.transform : null;
        }

        /// <summary>
        /// Куда боец ЦЕЛИТСЯ — в отличие от того, куда развёрнута фигура.
        ///
        /// У стрелка они расходятся на поправку стойки: фигура стоит
        /// вполоборота, потому что так снят клип, а целится он прямо.
        /// Проверять наведение по развороту фигуры нельзя — на этом
        /// уже обожглись: поправка в 35 градусов при пороге в 5 означала,
        /// что стоя коп не мог выстрелить вовсе.
        /// </summary>
        /// <summary>
        /// Поправка стойки, которую бойцу применили В ПОСЛЕДНИЙ РАЗ.
        ///
        /// Стоя она берётся из класса, на бегу равна нулю: там ноги обязаны
        /// смотреть по движению. Направление удара считается от неё же —
        /// иначе на бегу линия поражения уезжает на всю поправку от того,
        /// куда развёрнута фигура, и копьё колет мимо собственного древка.
        /// </summary>
        float appliedYawOffset;

        Vector3 AimForward
        {
            get
            {
                // Настоящий ствол честнее любой поправки: игрок видит именно
                // его, и стрелять боец должен тогда, когда оружие правда
                // наведено, а не когда развёрнут корень.
                if (hasBarrel && muzzle != null)
                {
                    var barrel = muzzle.forward;
                    barrel.y = 0f;
                    if (barrel.sqrMagnitude > 0.0001f) return barrel.normalized;
                }

                return AimMath.AimForward(transform.rotation, appliedYawOffset);
            }
        }

        void FaceTowards(Vector3 direction, float yawOffset)
        {
            if (direction.sqrMagnitude < 0.0001f) return;

            appliedYawOffset = yawOffset;

            // Поправка на стойку из анимации: у стрелка клип снят боком,
            // и без неё объект наведён точно, а фигура стоит вполоборота.
            var wanted = AimMath.BodyRotation(direction, yawOffset);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, wanted, config.bodyTurnSpeed * Time.deltaTime);
        }

        // --- лечение ---------------------------------------------------------

        /// <summary>
        /// Медик доливает здоровье самому тяжёлому из тех, кем ещё никто
        /// не занят.
        ///
        /// Готовность КОПИТСЯ: пока в отряде все целы, отсчёт не идёт. Иначе
        /// медик, простоявший спокойную минуту, встречал бы первый же наплыв
        /// с неостывшим отсчётом — то есть простаивал ровно в тот момент,
        /// ради которого его и берут.
        /// </summary>
        void Heal()
        {
            if (!klass.Heals) return;
            if (Time.time < nextHealTime) return;

            var patient = FindPatient();
            if (patient == null)
            {
                // Лечить некого — снимаем заявку, чтобы не держать целого
                // бойца занятым перед чужими медиками.
                HealTarget = null;
                return;
            }

            HealTarget = patient;
            nextHealTime = Time.time + Mathf.Max(0.05f, klass.healInterval);

            float healed = patient.health.Heal(HealPower);
            if (healed <= 0f) return;

            HealPlus.Burst(patient.transform.position + Vector3.up * config.healPlusHeight);
        }

        /// <summary>
        /// Самый тяжёлый из свободных.
        ///
        /// Тяжесть считается ДОЛЕЙ здоровья, а не остатком: у классов разные
        /// потолки, и разные бонусы живучести за забег. Боец с сорока очками
        /// из двухсот ближе к смерти, чем боец с сорока из пятидесяти,
        /// хотя остаток у них одинаковый.
        /// </summary>
        Survivor FindPatient()
        {
            Survivor best = null;
            float lowest = 1f;

            var all = Registry.Survivors;
            for (int i = 0; i < all.Count; i++)
            {
                var other = all[i];
                if (other == null || other.health == null || other.health.IsDead) continue;

                float fraction = other.health.Fraction;
                if (fraction >= 1f) continue;      // целого лечить нечем
                if (fraction >= lowest) continue;
                if (ClaimedByOther(other)) continue;

                best = other;
                lowest = fraction;
            }

            return best;
        }

        /// <summary>Этим бойцом уже занят другой медик?</summary>
        bool ClaimedByOther(Survivor patient)
        {
            var all = Registry.Survivors;
            for (int i = 0; i < all.Count; i++)
            {
                var medic = all[i];
                if (medic == null || medic == this) continue;
                if (medic.HealTarget == patient) return true;
            }

            return false;
        }

        // --- атака -----------------------------------------------------------

        void Attack()
        {
            // Огнемётчик живёт по своим правилам: он не бьёт раз в интервал,
            // а держит струю, пока перед ним есть кого жечь. Ему сюда нельзя.
            if (klass.HitsCone) return;

            if (target == null || Time.time < nextAttackTime) return;

            var to = target.transform.position - transform.position;
            to.y = 0f;
            float angle = Vector3.Angle(AimForward, to);

            // Со стволом порог один на обе стойки: ствол либо наведён, либо
            // нет, и бежит боец при этом или стоит — дело десятое. Послабление
            // на бегу было костылём под доворот, считавшийся от корня: тогда
            // угол мерился не по оружию, и приходилось прощать промах в 75
            // градусов. Теперь мерить есть по чему.
            float allowed = hasBarrel || stance == Stance.Standing
                ? config.aimedAngleThreshold
                : config.torsoAimMaxAngle;
            if (angle > allowed) return;

            nextAttackTime = Time.time + AttackInterval;

            if (klass.role == SquadRole.Melee) { StrikeMelee(); return; }

            if (klass.FiresBurst)
            {
                // Очередь заряжается, а не выстреливается разом: выстрелы
                // идут из UpdateBurst с шагом burstInterval, начиная с этого
                // же кадра.
                burstLeft = klass.burstCount;
                nextBurstShot = Time.time;

                // Пауза между очередями считается от ПОСЛЕДНЕГО выстрела,
                // а не от первого: иначе длинная очередь съедает собственную
                // паузу, и темп роли зависит от её длины.
                nextAttackTime = Time.time + AttackInterval
                                 + (klass.burstCount - 1) * Mathf.Max(0.01f, klass.burstInterval);
                return;
            }

            Shoot();
        }

        void Shoot()
        {
            AudioDirector.PistolShot();

            if (klass.FiresRocket)
            {
                // Целимся В ЗЕМЛЮ под врагом: ракета не попадает в тело,
                // она падает в точку и бьёт кругом. Прицел в грудь дал бы
                // взрыв в воздухе, и радиус на земле читался бы неверно.
                var at = target.transform.position;
                at.y = 0f;
                Rocket.Fire(MuzzlePosition(), at, Damage, klass.blastRadius,
                            klass.knockbackDistance, klass.knockbackDuration);
                return;
            }

            if (klass.FiresBolt)
            {
                // Болт летит В ЦЕЛЬ и дальше сквозь неё: те, кто стоит за
                // ней на той же прямой, получают своё. Направление берём
                // от цели, а не от корпуса, — на бегу корпус смотрит
                // по движению, и болт ушёл бы мимо.
                var direction = target.HitPoint - MuzzlePosition();
                direction.y = 0f;

                float range = klass.boltRange > 0f ? klass.boltRange : klass.attackRange;
                CrossbowBolt.Fire(MuzzlePosition(), direction, Damage, klass.boltPierce, range);
                return;
            }

            TracerLayer.Fire(MuzzlePosition(), target.HitPoint, klass.shotScale);
            target.TakeHit(Damage);
        }

        void StrikeMelee()
        {
            // Анимацию ускоряем ровно настолько, чтобы замах уложился
            // в заданный темп. Замедлять не нужно: при редких ударах клип
            // играется в своей скорости, а разница уходит в паузу.
            // Ускорение из класса умножается СВЕРХУ на подгонку под темп:
            // подгонка не даёт замаху не уложиться в интервал, а множитель
            // решает, насколько движение резкое.
            // Через раз — щитом. По СЧЁТУ, а не по случаю: игрок должен
            // видеть закономерность, случайный толчок читается сбоем
            // анимации, а не приёмом.
            swingCount++;
            pendingShield = klass.HasShield && shieldClipLength > 0f
                            && swingCount % klass.shieldEveryNth == 0;

            float clipLength = pendingShield ? shieldClipLength : attackClipLength;

            float playback = Mathf.Max(1f, clipLength / Mathf.Max(AttackInterval, 0.05f))
                             * Mathf.Max(0.1f, klass.attackAnimationSpeed);

            if (animator != null)
            {
                if (hasAttackSpeed) animator.SetFloat(AttackSpeedParam, playback);
                animator.SetTrigger(pendingShield ? AttackShieldParam : AttackParam);
            }

            // Урон не наносится сейчас: он наступит на середине замаха.
            // Иначе враг отлетает раньше, чем лопата до него дошла, и удар
            // читается как несвязанный с попаданием.
            //
            // Момент делится на ускорение: клип идёт быстрее — середина
            // наступает раньше, иначе попадание отстанет от картинки.
            pendingVictim = target;
            hitPending = true;

            float toHit = clipLength * Mathf.Clamp01(klass.attackHitTime) / playback;
            pendingHitTime = Time.time + toHit;

            // Дуга идёт вместе с замахом, а не с попаданием: она и есть
            // изображение замаха, и появиться должна тогда же, когда боец
            // заносит лопату.
            if (meleeArc != null) meleeArc.Swing(toHit * Mathf.Max(0.2f, klass.arcStretch));
        }

        void ResolvePendingHit()
        {
            if (!hitPending || Time.time < pendingHitTime) return;
            hitPending = false;

            if (pendingShield) { BashWithShield(); return; }
            if (klass.HitsChain) { StrikeChain(); return; }
            if (klass.HitsLine) { StrikeLine(); return; }
            if (klass.HitsArea) { LaunchShockWave(); return; }

            var victim = pendingVictim;
            pendingVictim = null;

            if (victim == null || victim.IsDead || !victim.isActiveAndEnabled) return;

            var to = victim.transform.position - transform.position;
            to.y = 0f;

            // Запас к дальности: пока шёл замах, цель успела шагнуть.
            // Без запаса часть ударов уходила бы в пустоту по формальности.
            float reach = klass.attackRange * 1.35f;
            if (to.sqrMagnitude > reach * reach) return;

            // Звук в момент ПОПАДАНИЯ, а не замаха: удар читается по нему,
            // и рассинхрон со вспышкой урона слышен сразу.
            AudioDirector.ShovelHit();

            victim.TakeHit(Damage);
            if (klass.knockbackDistance > 0f)
                victim.Knockback(to.normalized, klass.knockbackDistance, klass.knockbackDuration);
        }

        /// <summary>
        /// Кого задело и на каком удалении. Общие для копья и щита: оба
        /// разбирают цели по удалению, и держать под это два одинаковых
        /// списка незачем — удары не идут одновременно.
        /// </summary>
        readonly List<Zombie> hitBuffer = new List<Zombie>();
        readonly List<float> hitDepth = new List<float>();

        /// <summary>
        /// Укол копьём: достаётся всем, кто попал в полосу перед бойцом.
        ///
        /// Полоса считается от НАПРАВЛЕНИЯ ПРИЦЕЛИВАНИЯ, а не от цели: копьё
        /// идёт прямо, и те, кто оказался на этой прямой за целью, получают
        /// столько же. В этом вся роль — она отвечает на строй, идущий
        /// колонной, а не на толпу вокруг.
        ///
        /// Цели бьются В ПОРЯДКЕ УДАЛЕНИЯ, и предел числа целей отсекает
        /// дальних. Иначе в плотной толпе предел срезал бы случайных —
        /// тех, кто попался раньше в списке, — и копьё било бы сквозь
        /// первый ряд, не задев его.
        /// </summary>
        /// <summary>
        /// Счётчики уколов для замера попадаемости. Включаются стендом,
        /// в бою ничего не стоят: два инкремента на удар.
        /// </summary>
        public static int DebugSwings, DebugHitsTotal, DebugEmptySwings;

        public static void DebugReset() { DebugSwings = DebugHitsTotal = DebugEmptySwings = 0; }

        void StrikeLine()
        {
            // Колем В ЦЕЛЬ, а не вдоль корпуса.
            //
            // Вдоль корпуса было ошибкой, и заметной. Стоя боец разворачивается
            // к врагу, и разницы нет; на бегу же корпус смотрит по движению,
            // а цель выбирается в секторе 75 градусов от него. Коридор шириной
            // 1.4 метра прощает на трёх с половиной метрах всего 11 градусов
            // в сторону — то есть почти всякий выбранный на бегу враг
            // оказывался вне полосы, и копейщики били в пустоту.
            //
            // Линия идёт СКВОЗЬ цель, поэтому стоящие за ней получают своё:
            // ровно то, ради чего роль и заведена.
            var forward = AimForward;
            float length = klass.pierceLength > 0f ? klass.pierceLength : klass.attackRange;

            // Цель ПЕРЕСПРАШИВАЕТСЯ в момент укола, а не берётся та, на которую
            // замахивались.
            //
            // Пока идёт замах, отряд проезжает три с лишним метра, и намеченный
            // враг успевает выпасть за длину копья: замер показал, что на ходу
            // так уходило в пустоту 55% уколов против 36% на месте. Боец,
            // начавший выпад, должен достать того, кто перед ним СЕЙЧАС, —
            // это и честнее, и читается как выпад, а не как промах по воздуху.
            var victim = pendingVictim;
            pendingVictim = null;

            if (victim == null || victim.IsDead || !victim.isActiveAndEnabled ||
                !WithinReach(victim, length))
                victim = NearestInFront(forward, length);

            if (victim != null)
            {
                var toTarget = victim.transform.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f) forward = toTarget;
            }

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();

            float halfWidth = klass.pierceWidth * 0.5f;

            hitBuffer.Clear();
            hitDepth.Clear();

            var zombies = Registry.Zombies;
            for (int i = zombies.Count - 1; i >= 0; i--)
            {
                var zombie = zombies[i];
                if (zombie == null || zombie.IsDead || !zombie.isActiveAndEnabled) continue;

                var to = zombie.transform.position - transform.position;
                to.y = 0f;

                float along = Vector3.Dot(to, forward);
                if (along < 0f || along > length) continue;

                float side = (to - forward * along).magnitude;
                if (side > halfWidth) continue;

                // Вставкой по удалению: целей единицы, и заводить сортировку
                // с выделением памяти на каждый укол незачем.
                int at = hitBuffer.Count;
                while (at > 0 && hitDepth[at - 1] > along) at--;
                hitBuffer.Insert(at, zombie);
                hitDepth.Insert(at, along);
            }

            DebugSwings++;
            if (hitBuffer.Count == 0) { DebugEmptySwings++; return; }

            AudioDirector.ShovelHit();

            int limit = klass.pierceMaxTargets > 0
                ? Mathf.Min(klass.pierceMaxTargets, hitBuffer.Count)
                : hitBuffer.Count;

            for (int i = 0; i < limit; i++)
            {
                var zombie = hitBuffer[i];
                if (zombie == null || zombie.IsDead) continue;

                zombie.TakeHit(Damage);
                DebugHitsTotal++;

                // Отброс ВДОЛЬ копья, а не от бойца в стороны: укол насаживает
                // и толкает вперёд, и разлёт веером выглядел бы взрывом.
                if (klass.knockbackDistance > 0f)
                    zombie.Knockback(forward, klass.knockbackDistance, klass.knockbackDuration);
            }

            hitBuffer.Clear();
            hitDepth.Clear();
        }

        /// <summary>
        /// Толчок щитом: расталкивает всех перед собой.
        ///
        /// Бьёт слабо и по дуге, а не по одной цели: щитоносец не убивает,
        /// он держит пространство. Одиночный толчок делал бы из него плохого
        /// бойца ближнего боя вместо того, кем он задуман.
        ///
        /// Толкает ОТ БОЙЦА, каждого по своему направлению — веером. Толчок
        /// всех в одну сторону читался бы порывом ветра, а не щитом.
        /// </summary>
        void BashWithShield()
        {
            var forward = AimForward;

            var victim = pendingVictim;
            pendingVictim = null;

            if (victim != null && !victim.IsDead)
            {
                var toTarget = victim.transform.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f) forward = toTarget;
            }

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();

            float range = klass.shieldRange > 0f ? klass.shieldRange : klass.attackRange;
            float rangeSqr = range * range;
            float half = klass.shieldArc * 0.5f;

            hitBuffer.Clear();
            hitDepth.Clear();

            var zombies = Registry.Zombies;
            for (int i = zombies.Count - 1; i >= 0; i--)
            {
                if (i >= zombies.Count) continue;

                var zombie = zombies[i];
                if (zombie == null || zombie.IsDead || !zombie.isActiveAndEnabled) continue;

                var to = zombie.transform.position - transform.position;
                to.y = 0f;

                float distSqr = to.sqrMagnitude;
                if (distSqr > rangeSqr) continue;
                if (distSqr > 0.0001f && Vector3.Angle(forward, to) > half) continue;

                // Вставкой по удалению: целей единицы, и заводить сортировку
                // с выделением памяти на каждый удар незачем.
                int at = hitBuffer.Count;
                while (at > 0 && hitDepth[at - 1] > distSqr) at--;
                hitBuffer.Insert(at, zombie);
                hitDepth.Insert(at, distSqr);
            }

            if (hitBuffer.Count == 0) return;

            AudioDirector.ShovelHit();

            int limit = klass.shieldMaxTargets > 0
                ? Mathf.Min(klass.shieldMaxTargets, hitBuffer.Count)
                : hitBuffer.Count;

            // Через Damage, а не через своё число: сюда входят улучшения,
            // взятые за забег, и «+20% урона» с тир-апа работает на щит
            // так же, как на любой другой удар.
            float damage = Damage * klass.shieldDamageScale;

            for (int i = 0; i < limit; i++)
            {
                var zombie = hitBuffer[i];
                if (zombie == null || zombie.IsDead) continue;

                if (damage > 0f) zombie.TakeHit(damage);

                var to = zombie.transform.position - transform.position;
                to.y = 0f;
                var push = to.sqrMagnitude > 0.0001f ? to.normalized : forward;
                zombie.Knockback(push, klass.shieldPush, klass.shieldPushDuration);
            }

            hitBuffer.Clear();
            hitDepth.Clear();
        }

        int burstLeft;
        float nextBurstShot;

        /// <summary>
        /// Доводит начатую очередь.
        ///
        /// Очередь ПРЕРЫВАЕТСЯ, если цель кончилась: досылать оставшиеся
        /// выстрелы в пустоту значит тратить темп роли впустую, а по трассам
        /// это читается как стрельба по воздуху. Новая цель подберётся
        /// обычным порядком, и следующая очередь пойдёт уже по ней.
        /// </summary>
        void UpdateBurst()
        {
            if (burstLeft <= 0) return;

            if (target == null || target.IsDead || !target.isActiveAndEnabled)
            {
                burstLeft = 0;
                return;
            }

            if (Time.time < nextBurstShot) return;

            Shoot();
            burstLeft--;
            nextBurstShot = Time.time + Mathf.Max(0.01f, klass.burstInterval);
        }

        /// <summary>Насколько разогрет огнемёт, 0..1.</summary>
        float flameHeat;
        float flamePuffDebt;
        float nextFlameTick;
        float nextFlameSound;

        /// <summary>
        /// Струя огнемёта: непрерывный конус, который разгорается и опадает.
        ///
        /// Не залп раз в интервал, а поток. Разница не косметическая: залп
        /// одинаково силён с первого мига, а поток наказывает за то, что его
        /// включили поздно — пока факел разгорается, он короче и слабее,
        /// и набежавшая толпа успевает подойти вплотную.
        ///
        /// ЖАР решает всё сразу: длину струи, её урон и густоту клубов.
        /// Если бы он менял только длину, разогрев был бы виден глазом,
        /// но не чувствовался в бою.
        ///
        /// Остывание медленнее разогрева нарочно: короткая пауза между
        /// целями не должна гасить факел заново, иначе огнемётчик в редкой
        /// толпе не разгорается вовсе.
        /// </summary>
        void UpdateFlame()
        {
            if (!klass.HitsCone) return;

            float range = klass.coneRange > 0f ? klass.coneRange : klass.attackRange;

            var forward = AimForward;
            bool wants = false;

            if (target != null && !target.IsDead && target.isActiveAndEnabled)
            {
                var to = target.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude <= range * range && to.sqrMagnitude > 0.0001f)
                {
                    forward = to;
                    wants = true;
                }
            }

            float warm = Mathf.Max(0.05f, config.flameWarmUp);
            float cool = Mathf.Max(0.05f, config.flameCoolDown);

            flameHeat = Mathf.Clamp01(flameHeat + (wants ? Time.deltaTime / warm : -Time.deltaTime / cool));

            if (flameHeat <= 0.001f)
            {
                flamePuffDebt = 0f;
                return;
            }

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();

            float reach = range * flameHeat;

            // Тело струи — лента от дула; языки ниже — только поверх неё.
            FlameJet.Stream(this, MuzzlePosition(), forward, reach, klass.coneAngle, flameHeat);

            // Клубы копятся ДРОБНО: при десяти клубах в секунду и шестидесяти
            // кадрах целое число за кадр всегда ноль, и струи не было бы вовсе.
            flamePuffDebt += config.flamePuffsPerSecond * flameHeat * Time.deltaTime;
            while (flamePuffDebt >= 1f)
            {
                flamePuffDebt -= 1f;
                FlameJet.Puff(MuzzlePosition(), forward, reach, klass.coneAngle, flameHeat);
            }

            if (Time.time >= nextFlameSound)
            {
                nextFlameSound = Time.time + 0.3f;
                AudioDirector.ShovelHit();
            }

            if (Time.time < nextFlameTick) return;

            float tick = Mathf.Max(0.03f, config.flameTickInterval);
            nextFlameTick = Time.time + tick;
            BurnCone(forward, reach, tick);
        }

        /// <summary>Наносит урон и поджигает всех в текущем конусе.</summary>
        void BurnCone(Vector3 forward, float reach, float tick)
        {
            float half = klass.coneAngle * 0.5f;
            float reachSqr = reach * reach;

            // Урон за тик, а не за удар: сила огнемёта задаётся в секунду,
            // поэтому от дробности тиков она не зависит.
            float perSecond = Damage * flameHeat;
            float tip = Mathf.Clamp01(klass.coneTipDamage);

            var zombies = Registry.Zombies;
            for (int i = zombies.Count - 1; i >= 0; i--)
            {
                if (i >= zombies.Count) continue;

                var zombie = zombies[i];
                if (zombie == null || zombie.IsDead || !zombie.isActiveAndEnabled) continue;

                var to = zombie.transform.position - transform.position;
                to.y = 0f;

                float distSqr = to.sqrMagnitude;
                if (distSqr > reachSqr) continue;
                if (Vector3.Angle(forward, to) > half) continue;

                // Спад по длине струи: у основания пламя плотное, у конца
                // разреженное. Считается от ТЕКУЩЕЙ длины, а не от полной, —
                // иначе холодный короткий факел бил бы у самого дула так же
                // слабо, как разогретый бьёт на излёте.
                float along = reach > 0.01f ? Mathf.Sqrt(distSqr) / reach : 0f;
                float falloff = Mathf.Lerp(1f, tip, Mathf.Clamp01(along));

                // Тихий урон: без вспышки на каждый тик, иначе горящий
                // стоит белым и огня на нём не видно.
                zombie.Scorch(perSecond * falloff * tick);

                // Поджиг слабее и на холодной струе, и на её конце: иначе
                // едва тлеющий факел поджигал бы так же, как разогретый.
                zombie.Ignite(klass.burnDuration * flameHeat * falloff,
                              klass.burnDamagePerSecond * flameHeat * falloff);
            }
        }

        /// <summary>Кого уже задела цепь. Переиспользуется между ударами.</summary>
        readonly List<Zombie> chainHits = new List<Zombie>();

        /// <summary>
        /// Цепной разряд: случайный враг и дальше по ближайшим.
        ///
        /// Первый выбирается СЛУЧАЙНО из тех, до кого боец достаёт, а не
        /// ближайший. Иначе восемь электриков в одном строю бьют в одного и
        /// того же — разряды ложатся друг на друга, и вместо цепи по толпе
        /// получается один перегруженный зомби.
        ///
        /// Дальше цепь идёт по ближайшему ещё не задетому от ПРЕДЫДУЩЕГО
        /// звена, а не от бойца: так она уходит вглубь толпы, и именно это
        /// делает роль ответом на плотный строй.
        /// </summary>
        void StrikeChain()
        {
            pendingVictim = null;
            chainHits.Clear();

            var first = RandomInReach();
            if (first == null) return;

            AudioDirector.ShovelHit();

            var from = MuzzlePosition();
            var current = first;
            float damage = Damage;

            for (int link = 0; link < klass.chainTargets && current != null; link++)
            {
                chainHits.Add(current);

                TracerLayer.Bolt(from, current.HitPoint, klass.chainColor);
                current.TakeHit(damage);

                if (klass.knockbackDistance > 0f)
                {
                    var push = current.transform.position - transform.position;
                    push.y = 0f;
                    if (push.sqrMagnitude > 0.0001f)
                        current.Knockback(push.normalized, klass.knockbackDistance, klass.knockbackDuration);
                }

                // Следующее звено бьёт слабее: цепь должна затухать, иначе
                // плотная толпа превращает одного бойца в площадное оружие.
                damage *= 1f - Mathf.Clamp01(klass.chainFalloff);

                from = current.HitPoint;
                current = NextInChain(current);
            }

            chainHits.Clear();
        }

        /// <summary>Случайный враг в пределах дальности удара.</summary>
        Zombie RandomInReach()
        {
            float rangeSqr = klass.attackRange * klass.attackRange;

            // Резервуарная выборка: один проход, без списка и без выделения
            // памяти. Толпа доходит до двух сотен, и собирать её в список
            // на каждый разряд каждого электрика — работа на пустом месте.
            Zombie chosen = null;
            int seen = 0;

            var zombies = Registry.Zombies;
            for (int i = zombies.Count - 1; i >= 0; i--)
            {
                if (i >= zombies.Count) continue;

                var zombie = zombies[i];
                if (zombie == null || zombie.IsDead || !zombie.isActiveAndEnabled) continue;

                var to = zombie.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > rangeSqr) continue;

                seen++;
                if (Random.Range(0, seen) == 0) chosen = zombie;
            }

            return chosen;
        }

        /// <summary>Ближайший ещё не задетый враг рядом с этим звеном.</summary>
        Zombie NextInChain(Zombie previous)
        {
            float jumpSqr = klass.chainJumpRange * klass.chainJumpRange;

            Zombie best = null;
            float bestSqr = jumpSqr;

            var zombies = Registry.Zombies;
            for (int i = zombies.Count - 1; i >= 0; i--)
            {
                if (i >= zombies.Count) continue;

                var zombie = zombies[i];
                if (zombie == null || zombie.IsDead || !zombie.isActiveAndEnabled) continue;
                if (chainHits.Contains(zombie)) continue;

                var to = zombie.transform.position - previous.transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > bestSqr) continue;

                best = zombie;
                bestSqr = to.sqrMagnitude;
            }

            return best;
        }

        bool WithinReach(Zombie zombie, float length)
        {
            var to = zombie.transform.position - transform.position;
            to.y = 0f;
            return to.sqrMagnitude <= length * length;
        }

        /// <summary>
        /// Ближайший враг перед бойцом, в пределах длины копья.
        ///
        /// Сектор ограничен: боец доворачивает выпад, но не разворачивается
        /// на месте. Без ограничения копьё било бы за спину, и удар перестал
        /// бы совпадать с тем, куда смотрит боец.
        /// </summary>
        Zombie NearestInFront(Vector3 forward, float length)
        {
            const float MaxTurn = 45f;

            Zombie best = null;
            float bestSqr = length * length;

            var zombies = Registry.Zombies;
            for (int i = zombies.Count - 1; i >= 0; i--)
            {
                if (i >= zombies.Count) continue;

                var zombie = zombies[i];
                if (zombie == null || zombie.IsDead || !zombie.isActiveAndEnabled) continue;

                var to = zombie.transform.position - transform.position;
                to.y = 0f;

                float distSqr = to.sqrMagnitude;
                if (distSqr > bestSqr) continue;
                if (Vector3.Angle(forward, to) > MaxTurn) continue;

                best = zombie;
                bestSqr = distSqr;
            }

            return best;
        }

        /// <summary>
        /// Пускает ударную волну из-под молота.
        ///
        /// Урон наносит САМА ВОЛНА, по мере того как фронт доходит до врага.
        /// Раньше здесь бил мгновенный круг; разница не в цифрах, а в том,
        /// что мгновенный удар не объясняет, почему пострадали дальние —
        /// вся толпа дёргается в один кадр. Когда урон идёт вслед за кольцом,
        /// радиус читается сам собой, без цифр и без подсветки земли.
        ///
        /// Звук один на замах, а не на каждого задетого. Иначе один удар
        /// по десятку зомби съедает весь запас голосов, и следующие
        /// несколько секунд боя идут в тишине — на этом уже обжигались.
        /// </summary>
        void LaunchShockWave()
        {
            // Направление удара берём от цели, а не от корпуса: на бегу
            // корпус смотрит по движению, и сектор ушёл бы мимо всех — та же
            // мина, на которой уже погорело копьё.
            var forward = AimForward;
            if (pendingVictim != null && !pendingVictim.IsDead)
            {
                var to = pendingVictim.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.0001f) forward = to;
            }

            pendingVictim = null;
            AudioDirector.ShovelHit();

            bool directed = klass.shockShape == ShockShape.Directed;
            bool stuns = klass.shockImpact == ImpactKind.Stun;

            ShockWave.Burst(
                transform.position, klass.splashRadius, Damage,
                stuns ? 0f : klass.knockbackDistance,
                klass.knockbackDuration,
                forward,
                directed ? klass.shockAngle : 360f,
                stuns ? klass.stunChance : 0f,
                stuns ? klass.stunDuration : 0f);
        }

        /// <summary>
        /// Вес слоя удара ведёт код, а не контроллер.
        ///
        /// На override-слое пустое состояние с ненулевым весом выводит верх
        /// тела в бинд-позу — то есть боец на долю секунды раскидывает руки.
        /// Поэтому вес держится нулевым везде, кроме самого удара, и сводится
        /// к нулю ЗАРАНЕЕ, на хвосте клипа, пока анимация ещё играет.
        /// </summary>
        void UpdateAttackLayer()
        {
            if (attackLayer < 0 || animator == null) return;

            const float FadeOutFrom = 0.85f;
            const float BlendTime = 0.07f;

            // Вес поднимаем на ЛЮБОМ состоянии, кроме пустого.
            //
            // Раньше здесь стояла проверка на имя «Attack», и это молча
            // ломало каждый новый удар: щитоносцу завели второе состояние
            // «AttackShield», оно исправно проигрывалось — но на слое
            // с нулевым весом, то есть невидимо. Со стороны выглядело так,
            // будто боец бьёт только палкой, хотя урон и толчок шли верно.
            //
            // Перечислять имена ударов здесь нельзя: каждый следующий удар
            // у следующей роли пришлось бы дописывать сюда же, и забытая
            // строчка снова дала бы невидимый замах. Пустое состояние одно,
            // и правило строится от него.
            float goal = 0f;
            var state = animator.GetCurrentAnimatorStateInfo(attackLayer);
            if (!state.IsName(NoAttackStateName))
            {
                float t = Mathf.Clamp01(state.normalizedTime);
                goal = t < FadeOutFrom ? 1f : Mathf.InverseLerp(1f, FadeOutFrom, t);
            }

            attackLayerWeight = Mathf.MoveTowards(attackLayerWeight, goal, Time.deltaTime / BlendTime);
            animator.SetLayerWeight(attackLayer, attackLayerWeight);
        }

        void CacheAttackAnimation()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;

            for (int i = 0; i < animator.layerCount; i++)
                if (animator.GetLayerName(i) == AttackLayerName)
                {
                    attackLayer = i;
                    break;
                }

            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip == null) continue;

                // Длину берём из клипа, а не из конфига: поменяют анимацию —
                // момент удара переедет сам, без правки чисел.
                if (attackClipLength <= 0.5f &&
                    clip.name.IndexOf("Attack", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    attackClipLength = clip.length;

                // То же и для падения. Раньше тело убиралось по числу из
                // конфига, и падение обрывалось на середине: клип идёт 1.52
                // секунды на скорости смерти, а тело жило 1.4.
                if (deathClipLength <= 0f &&
                    clip.name.IndexOf("Dying", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    deathClipLength = clip.length;

                // Клип щита узнаём по «Left»: удар щитом снят левой рукой,
                // и длина у него своя — 1.10 против 1.40. Считать момент
                // попадания по чужой длине значит бить раньше или позже,
                // чем видно на экране.
                if (shieldClipLength <= 0f &&
                    clip.name.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    shieldClipLength = clip.length;
            }

            // Параметр есть только у контроллеров с ударом. Ставить его
            // вслепую нельзя: Animator ругается на каждое обращение
            // к несуществующему параметру, и лог тонет в предупреждениях.
            hasAttackSpeed = HasParameter(AttackSpeedParam);
            if (hasAttackSpeed) animator.SetFloat(AttackSpeedParam, 1f);
        }

        bool HasParameter(int hash)
        {
            foreach (var parameter in animator.parameters)
                if (parameter.nameHash == hash)
                    return true;
            return false;
        }

        /// <summary>
        /// Точка вылета трассы — метка на стволе оружия. Её ставит сборщик
        /// оружия у дальнего конца ствола.
        ///
        /// Раньше здесь бралась кость кисти, и трасса выходила из кулака:
        /// пока оружия в руках не было, разницы не было видно, а с револьвером
        /// стало бы заметно сразу.
        /// </summary>
        Transform FindMuzzle()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == MuzzleName)
                {
                    hasBarrel = true;
                    return t;
                }

            // Оружия нет или метки на нём нет — стреляем от кисти, как раньше.
            // Наводить по кисти нельзя: у неё своя ось, к направлению
            // выстрела отношения не имеющая, — потому и флаг.
            hasBarrel = false;
            return animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.RightHand)
                : null;
        }

        /// <summary>
        /// Отдаёт снаряду меш стрелы и материал — прямо из оружия в руке.
        ///
        /// Стрела в арбалете не отдельный префаб, а дочерний объект внутри
        /// модели, и вытаскивать её во что-то своё незачем: на подмеш можно
        /// сослаться как есть. Меньше сущностей, и если модель оружия
        /// заменят, снаряд сменится вместе с ней сам.
        ///
        /// Колчан пропускаем: стрелы в нём — часть облика арбалета, а нам
        /// нужна та, что лежит на ложе.
        /// </summary>
        void CacheBolt()
        {
            if (klass.FiresBolt) TakeProjectileMesh("Arrow", "Quiver", (mesh, material) =>
                CrossbowBolt.Configure(config, mesh, material));

            if (klass.FiresRocket) TakeProjectileMesh("Missile", null, (mesh, material) =>
                Rocket.Configure(config, mesh, material));
        }

        /// <summary>
        /// Ищет в оружии деталь, которая и есть снаряд, и отдаёт её меш.
        ///
        /// Снаряд лежит внутри модели оружия обычным дочерним объектом —
        /// и стрела в арбалете, и ракета в гранатомёте. Вытаскивать его
        /// в отдельный префаб незачем: на подмеш можно сослаться как есть,
        /// и если модель оружия заменят, снаряд сменится вместе с ней.
        /// </summary>
        void TakeProjectileMesh(string contains, string skip, System.Action<Mesh, Material> use)
        {
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                if (filter.name.IndexOf(contains, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (skip != null && filter.name.IndexOf(skip, System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                var renderer = filter.GetComponent<Renderer>();
                use(filter.sharedMesh, renderer != null ? renderer.sharedMaterial : null);
                return;
            }

            Debug.LogWarning($"[{name}] У класса {klass.displayName} в оружии нет детали " +
                             $"«{contains}» — снаряд рисовать нечем.", this);
        }

        Vector3 MuzzlePosition()
        {
            if (muzzle != null) return muzzle.position;
            // Запасной вариант на случай аватара без правой кисти.
            return transform.position + Vector3.up * 1.2f + transform.forward * 0.35f;
        }
    }
}
