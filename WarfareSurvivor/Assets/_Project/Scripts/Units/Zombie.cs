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

        // Один буфер на всех: расталкивание считается по очереди, и держать
        // список на каждом зомби значило бы двести списков вместо одного.
        static readonly List<Zombie> Neighbours = new List<Zombie>(32);

        [Tooltip("Проходит сквозь препятствия. Ставится на летающих: они " +
                 "перелетают руины, а не обходят их. Свойство префаба, " +
                 "а не состояние — пул его не сбрасывает.")]
        [SerializeField] bool ignoresObstacles;

        ArenaConfig config;
        Animator animator;
        Health health;
        Renderer[] renderers;

        Material tierMaterial;
        Material flashMaterial;
        float flashUntil;

        Survivor target;
        Vector3 knockbackVelocity;
        float knockbackUntil;
        float nextRetargetTime;
        float nextContactTime;
        float despawnTime;
        bool dying;

        public bool IsDead => health == null || health.IsDead;

        /// <summary>Точка попадания — грудь, а не пятки: туда летит трасса.</summary>
        public Vector3 HitPoint => transform.position + Vector3.up * (PopupHeight() * 0.6f);

        /// <summary>Радиус тела с учётом масштаба тира: крупные толкаются шире.</summary>
        public float BodyRadius => config == null ? 0.45f : config.zombieSeparationRadius * transform.localScale.x;

        /// <summary>Тир 1..N. Чем выше, тем больше здоровья, размера и желтизны.</summary>
        public int Tier { get; private set; }

        public event System.Action<Zombie> Released;

        void Awake()
        {
            health = GetComponent<Health>();
            animator = GetComponentInChildren<Animator>();
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
            health.Init(tier * cfg.zombieHitsPerTier);

            transform.localScale = Vector3.one * scale;

            tierMaterial = tierMat;
            flashMaterial = flashMat;
            flashUntil = 0f;
            ApplyMaterial(tierMaterial);

            target = null;
            dying = false;
            despawnTime = 0f;
            nextContactTime = 0f;
            knockbackUntil = 0f;
            knockbackVelocity = Vector3.zero;
            nextRetargetTime = Time.time + Random.value * cfg.retargetInterval;

            if (animator != null)
            {
                animator.Rebind();
                animator.Update(0f);
            }

            if (!Registry.Zombies.Contains(this)) Registry.Zombies.Add(this);
        }

        public void TakeHit(float damage)
        {
            if (dying) return;
            health.TakeDamage(damage, transform.position);
        }

        /// <summary>
        /// Отбрасывает тело. На время полёта расталкивание отключается —
        /// летящий проходит сквозь других: удерживать его снаружи чужих тел
        /// значило бы гасить весь отброс о первого же встречного, и удар
        /// перестал бы читаться.
        /// </summary>
        public void Knockback(Vector3 direction, float distance, float duration)
        {
            if (dying || duration <= 0f) return;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            knockbackVelocity = direction.normalized * (distance / duration);
            knockbackUntil = Time.time + duration;
        }

        void OnDamaged(float amount, Vector3 point)
        {
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
            for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = material;
        }

        float PopupHeight()
        {
            if (renderers.Length == 0) return 1.5f;
            return renderers[0].bounds.size.y * 0.9f;
        }

        void OnDied()
        {
            dying = true;
            knockbackUntil = 0f;
            // Из реестра убираем сразу, чтобы бойцы не расстреливали труп.
            Registry.Zombies.Remove(this);
            if (animator != null) animator.SetTrigger(DieParam);
            despawnTime = Time.time + config.zombieCorpseTime;
        }

        void Update()
        {
            if (config == null) return;

            if (flashUntil > 0f && Time.time >= flashUntil)
            {
                flashUntil = 0f;
                ApplyMaterial(tierMaterial);
            }

            if (dying)
            {
                if (Time.time >= despawnTime) Released?.Invoke(this);
                return;
            }

            if (Time.time < knockbackUntil)
            {
                var flight = transform.position + knockbackVelocity * Time.deltaTime;
                transform.position = ignoresObstacles ? flight : Obstacle.Resolve(flight, BodyRadius);
                return;
            }

            UpdateTarget();
            if (target == null) return;

            var to = target.transform.position - transform.position;
            to.y = 0f;
            float distance = to.magnitude;

            if (distance > config.zombieContactRange)
            {
                var direction = to / Mathf.Max(distance, 0.0001f);
                var next = transform.position + direction * (config.zombieSpeed * Time.deltaTime);
                if (!ignoresObstacles) next = Obstacle.Resolve(next, BodyRadius);

                transform.position = next;
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
            else if (Time.time >= nextContactTime)
            {
                nextContactTime = Time.time + config.zombieContactInterval;
                target.ApplyDamage(config.zombieContactDamage, target.transform.position);
            }

            // Расцепление идёт ВСЕГДА, а не только на ходу. Дошедшие до отряда
            // стоят на месте, и именно они образуют неподвижную стену, в которую
            // спрессовывается всё, что подходит следом.
            ResolveOverlap();
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
