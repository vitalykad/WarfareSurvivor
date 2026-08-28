using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Волны зомби. Группы приходят с разных сторон кольцом за краем экрана —
    /// самый простой контракт, заведомо временный (§6 project.md).
    ///
    /// Пул и потолок живых заложены сразу, а не по факту просадки.
    /// </summary>
    public class ZombieSpawner : MonoBehaviour
    {
        [SerializeField] ArenaConfig config;

        [SerializeField, Tooltip("Виды зомби. Появляются вперемешку, вид " +
                                 "выбирается на каждого случайно. У каждого " +
                                 "свой меш, свои материалы тиров и своя " +
                                 "запечённая анимация.")]
        Zombie[] zombiePrefabs = new Zombie[0];

        [SerializeField] SquadController squad;

        [SerializeField, Tooltip("Камера: по её кадру считается, где начинается " +
                                 "«за экраном».")]
        Camera view;

        /// <summary>
        /// Один вид зомби со всем своим хозяйством.
        ///
        /// Пул и материалы у каждого вида СВОИ. Общий пул выдавал бы офисника
        /// на месте обычного и наоборот — вид зомби определялся бы тем, кто
        /// раньше умер, а не случайностью. Материалы тоже не разделить:
        /// у моделей разные текстуры, а цвет тира красит их поверх.
        /// </summary>
        class Variant
        {
            public Zombie Prefab;
            public Material[] Tier;
            public Material[] Flash;
            public readonly Stack<Zombie> Idle = new Stack<Zombie>();

            /// <summary>Сколько таких сейчас на поле. Держит потолок вида.</summary>
            public int Live;
        }

        readonly List<Variant> variants = new List<Variant>();
        readonly List<Zombie> alive = new List<Zombie>();
        Transform pool;
        float nextSpawnTime;
        float startTime;

        void Start()
        {
            if (config == null || zombiePrefabs == null || zombiePrefabs.Length == 0)
            {
                // Молчаливый null тут превращается в NRE каждый кадр и топит
                // консоль — а причина (не проставленная ссылка) не видна.
                Debug.LogError($"[{name}] Не заданы config или ни одного вида зомби. Спавн выключен.", this);
                enabled = false;
                return;
            }

            startTime = Time.time;
            nextSpawnTime = Time.time + config.firstSpawnDelay;

            pool = new GameObject("ZombiePool").transform;
            BuildTierMaterials();
            DamagePopup.Configure(config, Camera.main);
            HealPlus.Configure(config, Camera.main);
            AcidZone.Configure(config);
            AcidDrop.Configure(config, Camera.main);
            AcidCloud.Configure(config, Camera.main);
            AcidBlast.Configure(config, Camera.main);
        }

        /// <summary>
        /// По материалу на тир, а не по материалу на зомби: 200 инстансов
        /// материала — это 200 отдельных батчей и вся выгода инстансинга
        /// насмарку. Тиров четыре, значит и материалов четыре.
        /// </summary>
        void BuildTierMaterials()
        {
            variants.Clear();

            foreach (var prefab in zombiePrefabs)
            {
                if (prefab == null) continue;
                variants.Add(BuildVariant(prefab));
            }

            if (variants.Count == 0)
                Debug.LogError($"[{name}] Ни один вид зомби не собрался.", this);
        }

        Variant BuildVariant(Zombie prefab)
        {
            var variant = new Variant { Prefab = prefab };

            var sourceRenderer = prefab.GetComponentInChildren<Renderer>(true);
            var source = sourceRenderer != null ? sourceRenderer.sharedMaterial : null;

            variant.Tier = new Material[config.zombieTiers];
            variant.Flash = new Material[config.zombieTiers];

            for (int i = 0; i < variant.Tier.Length; i++)
            {
                float t = config.zombieTiers <= 1 ? 0f : i / (float)(config.zombieTiers - 1);
                var color = Color.Lerp(config.zombieTierColorLow, config.zombieTierColorHigh, t);

                // Подкраска ослабляется к белому: цвет тира — подсказка,
                // а не заливка. На полной силе текстура зомби исчезала
                // под ровным зелёным, и модель переставала читаться.
                color = Color.Lerp(Color.white, color, Mathf.Clamp01(config.zombieTierTint));

                variant.Tier[i] = MakeMaterial(source, $"{prefab.name}_Tier{i + 1}", color, emissive: false);

                // Вспышка — отдельный ГОТОВЫЙ материал на тир, а не правка
                // свойств на лету: подсвеченные тогда рисуются одной пачкой.
                variant.Flash[i] = MakeMaterial(source, $"{prefab.name}_Tier{i + 1}_Flash",
                    config.hitFlashColor, emissive: true);
            }

            return variant;
        }

        Material MakeMaterial(Material source, string name, Color color, bool emissive)
        {
            var material = source != null
                ? new Material(source)
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = name;

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);

            if (!emissive) return material;

            // ТЕКСТУРУ УБИРАЕМ. Вспышка красит модель ровным цветом, а с
            // текстурой она остаётся собой, только чуть светлее.
            //
            // Раньше это сходило с рук: материал тира был крашен в зелёный,
            // и белая вспышка читалась на его фоне. Когда покраску по тиру
            // выключили, обычный материал стал белым — и белая вспышка на нём
            // перестала быть заметна вовсе.
            foreach (var n in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" })
                if (material.HasProperty(n)) material.SetTexture(n, null);

            if (!material.HasProperty("_EmissionColor")) return material;

            // Без свечения белая заливка на солнце теряется среди светлого
            // песка: вспышка должна быть ярче фона, а не просто белее модели.
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 0.8f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            return material;
        }

        /// <summary>
        /// Настройки текущей волны. Пока их нет, спавнер живёт по конфигу —
        /// так работает старая тестовая арена.
        ///
        /// Волна задаётся ИЗВНЕ, а не записью в конфиг: конфиг принадлежит
        /// разработчику, и правки в него из рантайма переживают выход
        /// из игры. Стенд замеров на этом уже обжигался.
        /// </summary>
        WaveEntry? wave;
        bool paused;

        /// <summary>Убит зомби — вот здесь. Забег вешает сюда искры.</summary>
        public event System.Action<Zombie> Killed;

        /// <summary>Номер идущей волны, с единицы. Ноль — забег вне волн.</summary>
        int waveNumber;

        public void ApplyWave(int number, in WaveEntry entry)
        {
            waveNumber = number;
            wave = entry;
        }

        /// <summary>
        /// Волна кончилась. Номер НЕ сбрасываем: в перерыве частоты видов
        /// должны остаться теми же, что были, а не откатиться к нулевой волне.
        /// </summary>
        public void ClearWave() => wave = null;
        public void SetPaused(bool value) => paused = value;

        /// <summary>Убирает всех живых с поля — конец волны, конец забега.</summary>
        public void ClearField()
        {
            for (int i = alive.Count - 1; i >= 0; i--)
                if (alive[i] != null) Release(alive[i]);
        }

        void Update()
        {
            if (paused) return;
            if (Time.time < nextSpawnTime) return;
            nextSpawnTime = Time.time + (wave?.SpawnInterval ?? config.spawnInterval);
            SpawnGroup();
        }

        void SpawnGroup()
        {
            int cap = wave?.MaxAlive ?? config.maxAliveZombies;
            int room = cap - alive.Count;
            if (room <= 0) return;

            int groupMin = wave?.GroupMin ?? config.groupSizeMin;
            int groupMax = wave?.GroupMax ?? config.groupSizeMax;

            int size = Mathf.Min(room, Random.Range(groupMin, groupMax + 1));
            float baseAngle = Random.value * 360f;
            int minTier = wave?.MinTier ?? MinTier();
            int maxTier = wave?.MaxTier ?? UnlockedTier();
            if (maxTier < minTier) maxTier = minTier;

            for (int i = 0; i < size; i++)
            {
                float angle = baseAngle + Random.Range(-config.groupAngleSpread, config.groupAngleSpread);
                var direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                var position = SquadCenter() + direction * OffscreenRadius(direction);

                int tier = Random.Range(minTier, maxTier + 1);
                Spawn(position, tier);
            }
        }

        int MinTier() => Mathf.Clamp(config.minZombieTier, 1, config.zombieTiers);

        /// <summary>
        /// Тиры открываются по времени, чтобы первые волны читались.
        /// Отсчёт идёт от минимального тира, а не от единицы: иначе при
        /// поднятом minZombieTier верхние тиры не открылись бы вовсе.
        /// </summary>
        int UnlockedTier()
        {
            float elapsed = Time.time - startTime;
            int unlocked = MinTier() + Mathf.FloorToInt(elapsed / config.tierUnlockInterval);
            return Mathf.Clamp(unlocked, MinTier(), config.zombieTiers);
        }

        Vector3 SquadCenter() => squad != null ? squad.transform.position : Vector3.zero;

        /// <summary>
        /// Радиус, на котором зомби заведомо за краем экрана — В ЭТОМ
        /// направлении.
        ///
        /// Считается ПО КАДРУ, а не берётся числом из конфига. Камера
        /// отъезжает по мере роста отряда, и постоянный радиус рано или
        /// поздно оказывается внутри кадра — зомби появляются из воздуха
        /// на глазах у игрока, и волна перестаёт читаться как приход
        /// откуда-то извне.
        ///
        /// Но и один радиус на все стороны не годится. Экран вытянут
        /// по вертикали: сбоку видно метров на десять, сверху на двадцать
        /// с лишним. Радиус по самому дальнему углу отправлял боковых зомби
        /// на двенадцать метров за видимый край — они шли до кадра пять
        /// секунд, и волна начиналась заметно позже, чем начиналась.
        ///
        /// Ищем край двоичным поиском вдоль луча: где точка перестаёт
        /// попадать в кадр, там и край. Дюжина умножений на матрицу
        /// на зомби — на фоне всего остального ничто.
        /// </summary>
        float OffscreenRadius(Vector3 direction)
        {
            float fallback = Mathf.Max(1f, config.spawnRadius);
            float margin = Mathf.Max(0f, config.spawnMargin);
            if (view == null) return fallback;

            var center = SquadCenter();
            float near = 0f;
            float far = fallback * 3f;

            // Отряд сам не в кадре — искать край не от чего.
            if (!InView(center)) return margin;

            // Даже дальняя точка в кадре: камера смотрит слишком полого,
            // земля уходит за горизонт. Отступаем на весь запас.
            if (InView(center + direction * far)) return far + margin;

            for (int i = 0; i < 12; i++)
            {
                float middle = (near + far) * 0.5f;
                if (InView(center + direction * middle)) near = middle;
                else far = middle;
            }

            return far + margin;
        }

        /// <summary>Точка на земле попадает в кадр?</summary>
        bool InView(Vector3 world)
        {
            var point = view.WorldToViewportPoint(world);
            return point.z > 0f && point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f;
        }

        void Spawn(Vector3 position, int tier)
        {
            if (variants.Count == 0) return;

            // Вид выбирается на КАЖДОГО зомби, а не на группу: иначе волна
            // приходит однородными пачками, и вместо смешанной толпы
            // получается чередование отрядов.
            var variant = PickVariant();
            if (variant == null) return;
            var zombie = variant.Idle.Count > 0 ? variant.Idle.Pop() : CreateZombie(variant);

            float t = config.zombieTiers <= 1 ? 0f : (tier - 1) / (float)(config.zombieTiers - 1);
            float scale = Mathf.Lerp(config.zombieScaleLow, config.zombieScaleHigh, t);

            zombie.transform.SetPositionAndRotation(position, Quaternion.identity);
            zombie.gameObject.SetActive(true);
            zombie.Init(config, tier, variant.Tier[tier - 1], variant.Flash[tier - 1], scale);

            alive.Add(zombie);
            variant.Live++;
        }

        const string BakedShaderName = "WarfareSurvivor/VertexAnimationToon";
        Shader bakedShader;

        /// <summary>
        /// Выбирает вид с учётом его частоты.
        ///
        /// Равновероятный выбор не годится с появлением крупного зомби:
        /// он вчетверо живучее, и встречаться должен вчетверо реже, иначе
        /// перестаёт быть событием и просто утраивает время на зачистку.
        /// </summary>
        Variant PickVariant()
        {
            // Вид может быть заперт по времени: крупный приходит в середине
            // второй волны, а не с первых секунд. Первую волну игрок должен
            // отстоять, разобравшись с обычными, — иначе он не успевает
            // понять, где безопасная дистанция, и учится не тому.
            float elapsed = Time.time - startTime;

            float total = 0f;
            for (int i = 0; i < variants.Count; i++) total += WeightOf(variants[i], elapsed);

            if (total <= 0f) return variants.Count > 0 ? variants[0] : null;

            float roll = Random.value * total;
            for (int i = 0; i < variants.Count; i++)
            {
                roll -= WeightOf(variants[i], elapsed);
                if (roll <= 0f) return variants[i];
            }

            // Досюда доходим только на ошибке округления — берём любой
            // из доступных, но никогда запертый.
            for (int i = variants.Count - 1; i >= 0; i--)
                if (WeightOf(variants[i], elapsed) > 0f) return variants[i];

            return variants[0];
        }

        /// <summary>
        /// Частота вида прямо сейчас: ноль у запертого по времени
        /// и у того, кого эта волна не приводит вовсе.
        /// </summary>
        float WeightOf(Variant variant, float elapsed)
        {
            var prefab = variant.Prefab;
            if (elapsed < prefab.UnlockAfter) return 0f;

            // Вид, которого на поле уже достаточно, в розыгрыше не участвует
            // вовсе — иначе его доля просто перетекала бы в пустые броски.
            int cap = prefab.MaxAlive;
            if (cap > 0 && variant.Live >= cap) return 0f;

            return prefab.WeightForWave(waveNumber);
        }

        /// <summary>Кто из какого вида вышел — чтобы вернуть его в свой пул.</summary>
        readonly Dictionary<Zombie, Variant> origin = new Dictionary<Zombie, Variant>();

        Zombie CreateZombie(Variant variant)
        {
            var zombie = Instantiate(variant.Prefab, pool);
            zombie.Released += Release;
            zombie.Died += OnZombieDied;
            LayerUtility.Apply(zombie.gameObject, LayerUtility.Zombies);
            ApplyBakedView(zombie);
            zombie.gameObject.SetActive(false);
            origin[zombie] = variant;
            return zombie;
        }

        /// <summary>
        /// Переводит зомби на запечённую анимацию, если это включено.
        /// Делается один раз при создании: перевод меняет состав компонентов,
        /// и повторять его на каждой выдаче из пула незачем.
        /// </summary>
        void ApplyBakedView(Zombie zombie)
        {
            if (!config.useBakedZombies) return;

            if (zombie.BakedSet == null && config.bakedZombies == null)
            {
                Debug.LogWarning($"[Зомби] У {zombie.name} нет запечённой анимации " +
                                 "ни на префабе, ни в конфиге. Остаётся на костях.");
                return;
            }

            // Сначала ссылка из набора, и только потом поиск по имени.
            // Ровно на этом порядке погорела первая сборка: шейдер искался
            // по имени, сборщик выбросил его как неиспользуемый, и вся толпа
            // тихо откатилась на кости — замер намерил старый путь.
            var shader = config.bakedZombies.shader;
            if (shader == null)
            {
                if (bakedShader == null) bakedShader = Shader.Find(BakedShaderName);
                shader = bakedShader;
            }

            if (shader == null)
            {
                Debug.LogWarning("[Зомби] Нечем рисовать запечённую анимацию: " +
                                 "в наборе нет ссылки на шейдер, и по имени " +
                                 BakedShaderName + " он не нашёлся. Остаёмся на костях.");
                return;
            }

            // Набор берём У САМОГО ЗОМБИ: у каждой модели свой меш, а значит
            // и своя текстура позиций. Общий из конфига остаётся запасным
            // путём для вида, которому набор ещё не привязан.
            var set = zombie.BakedSet != null ? zombie.BakedSet : config.bakedZombies;
            if (set == null) return;

            var view = BakedZombieView.Convert(zombie.gameObject, set, shader);
            if (view != null) zombie.UseBakedView(view);
        }

        void OnZombieDied(Zombie zombie) => Killed?.Invoke(zombie);

        void Release(Zombie zombie)
        {
            alive.Remove(zombie);
            Registry.Zombies.Remove(zombie);
            zombie.gameObject.SetActive(false);
            zombie.transform.SetParent(pool, false);

            // В СВОЙ пул: иначе офисник выйдет на месте обычного зомби
            // с чужими материалами и чужой запечённой анимацией.
            //
            // Здесь же освобождается место под потолком вида. Считаем
            // по ВОЗВРАТУ В ПУЛ, а не по смерти: труп ещё пару секунд лежит
            // на поле, и пускать на его место следующего плевуна рано.
            if (origin.TryGetValue(zombie, out var variant))
            {
                variant.Idle.Push(zombie);
                variant.Live = Mathf.Max(0, variant.Live - 1);
            }
        }
    }
}
