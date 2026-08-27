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
    /// <summary>Что предлагает карточка тир-апа.</summary>
    public enum OfferKind { AddUnit, Damage, Health }

    /// <summary>
    /// Одно предложение на тир-апе.
    ///
    /// Разнородные по смыслу карточки в одном списке намеренно: игрок
    /// выбирает не «какого бойца добрать», а «во что вложиться», и добор
    /// бойца — лишь один из вариантов.
    /// </summary>
    public struct TierUpOffer
    {
        public OfferKind Kind;
        public SurvivorClassSO Class;
        public string Title;
        public string Subtitle;
        public string Body;

        /// <summary>Картинка карточки. Пусто — рисуется одним текстом.</summary>
        public Sprite Art;
    }

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

        /// <summary>
        /// Сколько наборов прошло без пополнения.
        ///
        /// Начинается с большого числа намеренно: ПЕРВЫЙ набор забега должен
        /// содержать пополнение. Это первое решение игрока за забег, и оно
        /// должно быть про состав отряда — то есть про то, ради чего игра
        /// и затевалась. Чередование само по себе отдало бы первый набор
        /// под улучшения.
        /// </summary>
        int sinceAddUnit = 99;

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

            spawner.ApplyWave(index + 1, config.runWaves[index]);
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
            var options = BuildOffers();
            if (options.Count == 0 || tierUp == null)
            {
                pending = 0;
                return;
            }

            // Лог набора: на устройстве инспектора нет, и понять, почему
            // карточка не появилась, больше нечем.
            var report = new System.Text.StringBuilder("[Тир-ап] набор:");
            foreach (var o in options) report.Append(' ').Append(o.Kind).Append('/').Append(o.Title).Append(';');
            report.Append(" бойцов ").Append(squad.MemberCount).Append('/').Append(config.squadSlotCap)
                  .Append(", классов в конфиге ").Append(AllClasses().Count)
                  .Append(", шанс пополнения ").Append(config.tierUpAddUnitChance);
            Debug.Log(report.ToString());

            if (Current != Phase.Choosing) resume = Current;
            Current = Phase.Choosing;

            // Замораживаем время целиком. Выбор состава — это пауза,
            // а не ещё одна задача под давлением: за время чтения трёх
            // карточек отряд успел бы потерять бойца, и решение принималось
            // бы вслепую.
            Time.timeScale = 0f;
            tierUp.Show(options, Take);
        }

        void Take(TierUpOffer offer)
        {
            switch (offer.Kind)
            {
                case OfferKind.AddUnit: squad.AddMember(offer.Class); break;
                case OfferKind.Damage: squad.AddDamageBonus(offer.Class, config.tierUpDamageStep); break;
                case OfferKind.Health: squad.AddHealthBonus(offer.Class, config.tierUpHealthStep); break;
            }

            // Не ниже нуля: отрицательная очередь означала бы, что тир-ап
            // засчитан дважды, и следующий набор не показался бы вовсе.
            pending = Mathf.Max(0, pending - 1);
            Current = resume;
            Time.timeScale = 1f;

            if (pending > 0) OfferTierUp();
        }

        /// <summary>
        /// Собирает предложение: одно пополнение и два улучшения.
        ///
        /// Добор бойца — ровно одна карточка из трёх, и это главное отличие
        /// от прежнего вида. Отряд, который только растёт числом, ощущается
        /// одинаково от первой волны до последней: врагов больше, бойцов
        /// больше, ничего не меняется. Улучшения дают второй способ стать
        /// сильнее, и выбор между «шире» и «глубже» — это уже решение.
        ///
        /// Когда слоты кончились, пополнения в списке нет, и остаются одни
        /// улучшения: тир-ап не должен пропадать впустую.
        /// </summary>
        List<TierUpOffer> BuildOffers()
        {
            var offers = new List<TierUpOffer>(3);

            // Пополнение попадается НЕ ВСЕГДА.
            //
            // Пока оно было в каждом наборе, выбор сводился к «взять бойца
            // или улучшить», и вложиться сразу в двух разных бойцов игрок
            // не мог. Через раз пополнения нет, и набор целиком уходит
            // под улучшения — тогда решение становится «кого именно
            // усиливать».
            // Пополнение идёт ЧЕРЕДОВАНИЕМ, а не по броску монеты.
            //
            // С монеткой при шансе в половину выпадали полосы: на устройстве
            // из восьми наборов пополнение пришло один раз, и игрок весь
            // забег не мог расширить отряд. Чередование даёт ровно то, что
            // и просили — «через раз», — без полос невезения.
            //
            // Шаг выводится из того же числа: половина — через один,
            // треть — через два.
            int step = Mathf.Max(1, Mathf.RoundToInt(1f / Mathf.Max(0.01f, config.tierUpAddUnitChance)));

            bool roomLeft = squad.MemberCount < config.squadSlotCap;
            bool addUnitTurn = sinceAddUnit >= step - 1;

            if (roomLeft && addUnitTurn)
            {
                sinceAddUnit = 0;
                // Раз уж пополнение выпало — предлагаем ВСЕ классы разом,
                // а не один случайный.
                //
                // Пополнение и есть то самое решение, ради которого игра
                // затевалась: из кого собран отряд. Показывать один случайный
                // класс значит подменять выбор жребием — игрок не решает,
                // кого взять, он берёт того, кого выпало.
                foreach (var klass in AllClasses()) offers.Add(AddUnitOffer(klass));
            }
            else sinceAddUnit++;

            AddUpgrades(offers);

            // Улучшать некого и слот свободен — лучше предложить пополнение,
            // чем показать пустое окно.
            if (offers.Count == 0 && roomLeft)
                foreach (var klass in AllClasses()) offers.Add(AddUnitOffer(klass));

            return offers;
        }

        /// <summary>
        /// Добирает набор улучшениями, не повторяя одно и то же.
        ///
        /// Пара «что усилить и у кого» должна быть уникальной: две карточки
        /// «+20% урона фермеру» — это выбор из одного варианта, притворяющийся
        /// выбором из двух. Если различных пар меньше, чем мест, карточек
        /// будет меньше: лучше две настоящих, чем три с обманкой.
        /// </summary>
        void AddUpgrades(List<TierUpOffer> offers)
        {
            int target = Mathf.Max(1, config.tierUpOptions);
            if (offers.Count >= target) return;

            // Сначала классы, которые ЕСТЬ в строю, потом остальные.
            //
            // Улучшение классу, которого в отряде нет, — не бессмыслица:
            // пополнение может привести его в следующем наборе. Но пока
            // в строю есть кого усиливать, предлагать надо их: вложение
            // должно работать сразу.
            // Перемешиваем ВНУТРИ групп, а не поверх них. Общая тасовка
            // убивала весь смысл порядка: в набор попадали два улучшения
            // полицейского, которого в отряде ноль, а фермеры оставались
            // без вложений.
            var present = new List<TierUpOffer>();
            AddPairs(present, PresentClasses());
            Shuffle(present);

            var rest = new List<TierUpOffer>();
            AddPairs(rest, AllClasses());
            rest.RemoveAll(o => present.Exists(p => p.Class == o.Class));
            Shuffle(rest);

            var pool = new List<TierUpOffer>(present);
            pool.AddRange(rest);

            // Повторяться не должна ПАРА «что усилить и у кого». Одинаковый
            // вид для разных классов — это разные вложения, и на карточках
            // они отличаются нарисованным персонажем.
            foreach (var offer in pool)
            {
                if (offers.Count >= target) break;
                if (offers.Exists(o => o.Kind == offer.Kind && o.Class == offer.Class)) continue;
                offers.Add(offer);
            }
        }

        static void Shuffle(List<TierUpOffer> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>Добавляет в набор по паре карточек на каждый класс.</summary>
        void AddPairs(List<TierUpOffer> pool, List<SurvivorClassSO> classes)
        {
            foreach (var klass in classes)
            {
                if (klass == null) continue;
                if (pool.Exists(o => o.Class == klass)) continue;

                pool.Add(DamageOffer(klass));
                pool.Add(HealthOffer(klass));
            }
        }

        /// <summary>Классы, которые сейчас в строю.</summary>
        List<SurvivorClassSO> PresentClasses()
        {
            var present = new List<SurvivorClassSO>();

            foreach (var member in squad.Members)
                if (member != null && member.Class != null && !present.Contains(member.Class))
                    present.Add(member.Class);

            return present;
        }

        TierUpOffer AddUnitOffer(SurvivorClassSO klass) => new TierUpOffer
        {
            Kind = OfferKind.AddUnit,
            Class = klass,
            Art = klass.cardAddUnit,
            Title = klass.displayName,
            Subtitle = "пополнение",
            Body = ClassBody(klass)
        };

        TierUpOffer DamageOffer(SurvivorClassSO klass) => new TierUpOffer
        {
            Kind = OfferKind.Damage,
            Class = klass,
            Art = klass.cardDamage,
            Title = "+" + Percent(config.tierUpDamageStep) + " урона",
            Subtitle = klass.displayName,
            Body = "Бьют сильнее все бойцы этого класса,\nвключая тех, кто придёт потом.\n\nсейчас " +
                   Percent(squad.DamageBonusFor(klass) - 1f) + " сверх базового"
        };

        TierUpOffer HealthOffer(SurvivorClassSO klass) => new TierUpOffer
        {
            Kind = OfferKind.Health,
            Class = klass,
            Art = klass.cardHealth,
            Title = "+" + Percent(config.tierUpHealthStep) + " здоровья",
            Subtitle = klass.displayName,
            Body = "Живучести прибавляется сразу,\nа не только новичкам.\n\nсейчас " +
                   Percent(squad.HealthBonusFor(klass) - 1f) + " сверх базового"
        };

        static string Percent(float value) => Mathf.RoundToInt(value * 100f) + "%";

        static string ClassBody(SurvivorClassSO klass)
        {
            float dps = klass.attackInterval > 0f ? klass.damage / klass.attackInterval : 0f;
            var text = new System.Text.StringBuilder();
            text.Append("здоровье   ").Append(Mathf.RoundToInt(klass.maxHealth)).Append('\n');
            text.Append("урон   ").Append(Mathf.RoundToInt(klass.damage)).Append('\n');
            text.Append("раз в   ").Append(klass.attackInterval.ToString("0.0")).Append(" с\n");
            text.Append("дальность   ").Append(Mathf.RoundToInt(klass.attackRange)).Append(" м\n\n");
            text.Append("урон в секунду   ").Append(dps.ToString("0.#"));
            if (klass.knockbackDistance > 0f) text.Append("\n\nотбрасывает тела");
            return text.ToString();
        }

        /// <summary>
        /// Все классы, какие вообще бывают в отряде.
        ///
        /// Берутся из состава конфига, а не из строя: класс с нулевым
        /// количеством на старте — это по-прежнему класс, которым отряд
        /// можно расширить, и не предлагать его значит запереть игрока
        /// в том составе, с которым он вышел.
        /// </summary>
        List<SurvivorClassSO> AllClasses()
        {
            var pool = new List<SurvivorClassSO>();
            if (config.squadComposition != null)
                foreach (var entry in config.squadComposition)
                {
                    // Класс без нарисованных карточек в набор не идёт: пустая
                    // картинка читается как поломка, а не как новый боец.
                    // Взять такой класс можно только стартовым составом.
                    if (entry.Class == null || !entry.Class.offerInTierUp) continue;
                    if (!pool.Contains(entry.Class)) pool.Add(entry.Class);
                }

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
