using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Забег: три волны, тир-апы между боями, победа или поражение.
    ///
    /// До него игра не имела ни начала, ни конца, ни единого решения игрока.
    /// Здесь появляется и то, и другое, и третье.
    ///
    /// Всё, что задаёт темп — длины волн, плотность спавна, цена тир-апа, —
    /// живёт в конфиге. Прототип, у которого темп зашит в код, умирает
    /// на первой же итерации баланса.
    /// </summary>
    public class RunController : MonoBehaviour
    {
        public enum Phase { Fighting, Break, Choosing, Won, Lost }

        [SerializeField] ArenaConfig config;
        [SerializeField] SquadController squad;
        [SerializeField] ZombieSpawner spawner;
        [SerializeField] SparkField sparks;
        [SerializeField] TierUpPanel tierUp;

        public Phase Current { get; private set; } = Phase.Fighting;
        public int WaveIndex { get; private set; }
        public int WaveCount => config != null && config.runWaves != null ? config.runWaves.Length : 0;

        /// <summary>Сколько секунд осталось текущей волне.</summary>
        public float TimeLeft { get; private set; }

        public int Sparks { get; private set; }
        public int SparksNeeded { get; private set; }
        public int TierUpsTaken { get; private set; }

        /// <summary>Сколько тир-апов ждёт своей очереди.</summary>
        int pending;

        /// <summary>Куда вернуться, когда выбор сделан.</summary>
        Phase resume = Phase.Fighting;

        void Awake()
        {
            if (config == null || squad == null || spawner == null)
            {
                Debug.LogError("[Забег] Не хватает ссылок, забег не запустится.", this);
                enabled = false;
                return;
            }

            if (sparks != null) sparks.Collected += OnSparks;
            SparksNeeded = CostOf(0);
        }

        void OnDestroy()
        {
            if (sparks != null) sparks.Collected -= OnSparks;
            // Пауза живёт в глобальном времени и переживёт выход из сцены,
            // если её тут не снять: следующий забег стартовал бы замороженным.
            Time.timeScale = 1f;
        }

        void Start() => StartWave(0);

        void Update()
        {
            if (Current == Phase.Won || Current == Phase.Lost) return;

            // Проверяем ДО фазы: отряд может кончиться и в паузе между волнами,
            // если последнего добили на излёте.
            if (squad.MemberCount == 0)
            {
                Finish(Phase.Lost);
                return;
            }

            if (Current == Phase.Choosing) return;

            TimeLeft -= Time.deltaTime;
            if (TimeLeft > 0f) return;

            if (Current == Phase.Fighting) EndWave();
            else StartWave(WaveIndex + 1);
        }

        void StartWave(int index)
        {
            if (config.runWaves == null || index >= config.runWaves.Length)
            {
                Finish(Phase.Won);
                return;
            }

            WaveIndex = index;
            Current = Phase.Fighting;
            TimeLeft = Mathf.Max(1f, config.runWaves[index].Duration);

            spawner.ApplyWave(config.runWaves[index]);
            spawner.SetPaused(false);
        }

        /// <summary>
        /// Волна кончилась. Спавн останавливаем, но поле НЕ зачищаем:
        /// недобитые остаются, и пауза уходит на то, чтобы их дожать.
        /// Иначе конец волны выглядел бы как телепорт, а не как победа.
        /// </summary>
        void EndWave()
        {
            spawner.SetPaused(true);
            Current = Phase.Break;
            TimeLeft = Mathf.Max(0.5f, config.waveBreak);

            // Искры с поля засчитываем сами: гоняться за последними
            // светляками в пустой паузе — не решение, а работа.
            if (sparks != null) sparks.CollectAll();
        }

        void OnSparks(int amount)
        {
            Sparks += amount;

            // Копим ОЧЕРЕДЬ тир-апов, а не показываем выбор сразу.
            // Искры прилетают пачками, и выбор посреди боя за каждую —
            // это дёрганье, а не решение.
            while (Sparks >= SparksNeeded)
            {
                Sparks -= SparksNeeded;
                pending++;
                TierUpsTaken++;
                SparksNeeded = CostOf(TierUpsTaken);
            }

            if (pending > 0 && Current != Phase.Choosing) OfferTierUp();
        }

        void OfferTierUp()
        {
            var options = PickOptions();
            if (options.Count == 0 || tierUp == null || squad.MemberCount >= config.squadSlotCap)
            {
                // Предлагать нечего — очередь просто сгорает. Держать её
                // до освобождения слота значило бы вывалить пять окон подряд
                // в момент первой же потери.
                pending = 0;
                return;
            }

            if (Current != Phase.Choosing) resume = Current;
            Current = Phase.Choosing;

            // Замораживаем время целиком. Выбор состава — это пауза,
            // а не ещё одна задача под давлением: за время чтения трёх
            // карточек отряд успел бы потерять бойца, и решение принималось
            // бы вслепую.
            Time.timeScale = 0f;
            tierUp.Show(options, Take);
        }

        void Take(SurvivorClassSO klass)
        {
            squad.AddMember(klass);

            pending--;
            Current = resume;
            Time.timeScale = 1f;

            if (pending > 0) OfferTierUp();
        }

        /// <summary>
        /// Что предложить на выбор.
        ///
        /// Классы тасуются и берутся первые N. Пока классов всего два,
        /// выбор беднее задуманного — это ограничение содержания,
        /// а не механики: добавится медик, и предложение станет настоящим.
        /// </summary>
        List<SurvivorClassSO> PickOptions()
        {
            var pool = new List<SurvivorClassSO>();
            if (config.squadComposition != null)
                foreach (var entry in config.squadComposition)
                    if (entry.Class != null && !pool.Contains(entry.Class)) pool.Add(entry.Class);

            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            int take = Mathf.Clamp(config.tierUpOptions, 1, pool.Count);
            pool.RemoveRange(take, pool.Count - take);
            return pool;
        }

        int CostOf(int taken) =>
            Mathf.Max(1, Mathf.RoundToInt(config.tierUpCostBase *
                Mathf.Pow(Mathf.Max(1f, config.tierUpCostGrowth), taken)));

        void Finish(Phase outcome)
        {
            Current = outcome;
            Time.timeScale = 1f;

            spawner.SetPaused(true);
            if (outcome == Phase.Won) spawner.ClearField();
            if (sparks != null) sparks.Clear();
            if (tierUp != null) tierUp.Hide();

            Debug.Log(outcome == Phase.Won
                ? $"[Забег] Победа. Тир-апов взято: {TierUpsTaken}, бойцов осталось: {squad.MemberCount}"
                : $"[Забег] Поражение на волне {WaveIndex + 1} из {WaveCount}. Тир-апов взято: {TierUpsTaken}");
        }
    }
}
