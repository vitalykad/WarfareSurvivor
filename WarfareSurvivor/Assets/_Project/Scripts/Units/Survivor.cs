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
        static readonly int AttackSpeedParam = Animator.StringToHash("AttackSpeed");

        /// <summary>Имя слоя удара — тот же, что заводит CharacterSetupBuilder.</summary>
        const string AttackLayerName = "UpperBody";

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

        Zombie target;
        Zombie pendingVictim;
        Stance stance;
        Transform muzzle;
        int attackLayer = -1;
        bool hasAttackSpeed;
        float attackLayerWeight;
        float attackClipLength = 0.5f;
        float pendingHitTime;
        float nextRetargetTime;
        float nextAttackTime;
        float nextStanceChangeTime;
        Vector3 lastPosition;

        /// <summary>Точка строя, которую держит этот боец. Ставит SquadController.</summary>
        public Vector3 SlotPosition { get; set; }

        public Health Health => health;
        public SurvivorClassSO Class => klass;

        /// <summary>Боец выбыл. Отряд по этому событию пересобирает строй.</summary>
        public event System.Action<Survivor> Lost;

        public void Bind(SquadController owner, ArenaConfig cfg, SurvivorClassSO survivorClass)
        {
            squad = owner;
            config = cfg;
            klass = survivorClass;

            health = GetComponent<Health>();
            health.Init(klass.maxHealth);

            animator = GetComponentInChildren<Animator>();
            muzzle = FindMuzzle();

            CacheAttackAnimation();

            torsoAim = GetComponent<TorsoAim>();
            if (torsoAim != null) torsoAim.Configure(config.torsoAimMaxAngle, config.torsoAimSpeed);

            var bar = GetComponent<HealthBarView>();
            if (bar != null)
            {
                // Полоска, трассы, вспышки — украшения. Любой их сбой не должен
                // мешать бойцу появиться: именно так одно исключение внутри
                // полоски оставило от отряда одного человека.
                try { bar.Bind(health, config); }
                catch (System.Exception e) { Debug.LogError($"[{name}] Полоска здоровья не собралась: {e.Message}", this); }
            }

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
            if (health != null) health.Died -= OnDied;
        }

        void OnDied()
        {
            Registry.Survivors.Remove(this);
            gameObject.SetActive(false);
            Lost?.Invoke(this);
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
            if (config == null || health.IsDead) return;
            if (!config.simulateSurvivors) return;

            Move();
            UpdateTarget();
            UpdateStance();
            Aim();
            Attack();
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

                if (requireReachable && Vector3.Angle(transform.forward, to) > config.torsoAimMaxAngle)
                    continue;

                best = zombie;
                bestSqr = distSqr;
            }

            return best;
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
                // Отряд стоит — оси движения нет, ноги ничего не отыгрывают,
                // и тело можно развернуть на врага целиком.
                if (torsoAim != null) torsoAim.Target = null;
                if (target == null) return;

                var toTarget = target.transform.position - transform.position;
                toTarget.y = 0f;
                FaceTowards(toTarget);
                return;
            }

            // В движении тело строго вдоль оси движения, к врагу тянется грудь.
            FaceTowards(stance == Stance.Backward ? -squad.MoveDirection : squad.MoveDirection);
            if (torsoAim != null) torsoAim.Target = target != null ? target.transform : null;
        }

        void FaceTowards(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up),
                config.bodyTurnSpeed * Time.deltaTime);
        }

        // --- атака -----------------------------------------------------------

        void Attack()
        {
            if (target == null || Time.time < nextAttackTime) return;

            var to = target.transform.position - transform.position;
            to.y = 0f;
            float angle = Vector3.Angle(transform.forward, to);

            // В движении бьём в пределах доворота груди — тело уже стоит так,
            // как надо. Стоя и на отходе спиной боец разворачивается целиком,
            // поэтому там ждём, пока он действительно довернётся.
            float allowed = stance == Stance.Standing
                ? config.aimedAngleThreshold
                : config.torsoAimMaxAngle;
            if (angle > allowed) return;

            nextAttackTime = Time.time + klass.attackInterval;

            if (klass.role == SquadRole.Melee) StrikeMelee();
            else Shoot();
        }

        void Shoot()
        {
            TracerLayer.Fire(MuzzlePosition(), target.HitPoint);
            target.TakeHit(klass.damage);
        }

        void StrikeMelee()
        {
            // Анимацию ускоряем ровно настолько, чтобы замах уложился
            // в заданный темп. Замедлять не нужно: при редких ударах клип
            // играется в своей скорости, а разница уходит в паузу.
            float playback = Mathf.Max(1f, attackClipLength / Mathf.Max(klass.attackInterval, 0.05f));

            if (animator != null)
            {
                if (hasAttackSpeed) animator.SetFloat(AttackSpeedParam, playback);
                animator.SetTrigger(AttackParam);
            }

            // Урон не наносится сейчас: он наступит на середине замаха.
            // Иначе враг отлетает раньше, чем лопата до него дошла, и удар
            // читается как несвязанный с попаданием.
            //
            // Момент делится на ускорение: клип идёт быстрее — середина
            // наступает раньше, иначе попадание отстанет от картинки.
            pendingVictim = target;
            pendingHitTime = Time.time + attackClipLength * Mathf.Clamp01(klass.attackHitTime) / playback;
        }

        void ResolvePendingHit()
        {
            if (pendingVictim == null || Time.time < pendingHitTime) return;

            var victim = pendingVictim;
            pendingVictim = null;

            if (victim.IsDead || !victim.isActiveAndEnabled) return;

            var to = victim.transform.position - transform.position;
            to.y = 0f;

            // Запас к дальности: пока шёл замах, цель успела шагнуть.
            // Без запаса часть ударов уходила бы в пустоту по формальности.
            float reach = klass.attackRange * 1.35f;
            if (to.sqrMagnitude > reach * reach) return;

            victim.TakeHit(klass.damage);
            if (klass.knockbackDistance > 0f)
                victim.Knockback(to.normalized, klass.knockbackDistance, klass.knockbackDuration);
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

            float goal = 0f;
            var state = animator.GetCurrentAnimatorStateInfo(attackLayer);
            if (state.IsName("Attack"))
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
                if (clip == null || clip.name.IndexOf("Attack", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // Длину берём из клипа, а не из конфига: поменяют анимацию —
                // момент удара переедет сам, без правки чисел.
                attackClipLength = clip.length;
                break;
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
                    return t;

            // Оружия нет или метки на нём нет — стреляем от кисти, как раньше.
            return animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.RightHand)
                : null;
        }

        Vector3 MuzzlePosition()
        {
            if (muzzle != null) return muzzle.position;
            // Запасной вариант на случай аватара без правой кисти.
            return transform.position + Vector3.up * 1.2f + transform.forward * 0.35f;
        }
    }
}
