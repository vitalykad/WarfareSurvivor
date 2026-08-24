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
        [SerializeField] Zombie zombiePrefab;
        [SerializeField] SquadController squad;

        readonly Stack<Zombie> idle = new Stack<Zombie>();
        readonly List<Zombie> alive = new List<Zombie>();
        Material[] tierMaterials;
        Material[] flashMaterials;
        Transform pool;
        float nextSpawnTime;
        float startTime;

        void Start()
        {
            if (config == null || zombiePrefab == null)
            {
                // Молчаливый null тут превращается в NRE каждый кадр и топит
                // консоль — а причина (не проставленная ссылка) не видна.
                Debug.LogError($"[{name}] Не заданы config или zombiePrefab. Спавн выключен.", this);
                enabled = false;
                return;
            }

            startTime = Time.time;
            nextSpawnTime = Time.time + config.firstSpawnDelay;

            pool = new GameObject("ZombiePool").transform;
            BuildTierMaterials();
            DamagePopup.Configure(config, Camera.main);
        }

        /// <summary>
        /// По материалу на тир, а не по материалу на зомби: 200 инстансов
        /// материала — это 200 отдельных батчей и вся выгода инстансинга
        /// насмарку. Тиров четыре, значит и материалов четыре.
        /// </summary>
        void BuildTierMaterials()
        {
            var sourceRenderer = zombiePrefab.GetComponentInChildren<Renderer>();
            var source = sourceRenderer != null ? sourceRenderer.sharedMaterial : null;

            tierMaterials = new Material[config.zombieTiers];
            flashMaterials = new Material[config.zombieTiers];

            for (int i = 0; i < tierMaterials.Length; i++)
            {
                float t = config.zombieTiers <= 1 ? 0f : i / (float)(config.zombieTiers - 1);
                var color = Color.Lerp(config.zombieTierColorLow, config.zombieTierColorHigh, t);

                tierMaterials[i] = MakeMaterial(source, $"Zombie_Tier{i + 1}", color, emissive: false);

                // Вспышка — отдельный ГОТОВЫЙ материал на тир, а не правка
                // свойств на лету: подсвеченные тогда рисуются одной пачкой.
                flashMaterials[i] = MakeMaterial(source, $"Zombie_Tier{i + 1}_Flash",
                    config.hitFlashColor, emissive: true);
            }
        }

        Material MakeMaterial(Material source, string name, Color color, bool emissive)
        {
            var material = source != null
                ? new Material(source)
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = name;

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);

            if (!emissive || !material.HasProperty("_EmissionColor")) return material;

            // Без свечения белая заливка на солнце теряется среди светлого
            // песка: вспышка должна быть ярче фона, а не просто белее модели.
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 0.8f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            return material;
        }

        void Update()
        {
            if (Time.time < nextSpawnTime) return;
            nextSpawnTime = Time.time + config.spawnInterval;
            SpawnGroup();
        }

        void SpawnGroup()
        {
            int room = config.maxAliveZombies - alive.Count;
            if (room <= 0) return;

            int size = Mathf.Min(room, Random.Range(config.groupSizeMin, config.groupSizeMax + 1));
            float baseAngle = Random.value * 360f;
            int minTier = MinTier();
            int maxTier = UnlockedTier();

            for (int i = 0; i < size; i++)
            {
                float angle = baseAngle + Random.Range(-config.groupAngleSpread, config.groupAngleSpread);
                var offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * config.spawnRadius;
                var position = SquadCenter() + offset;

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

        void Spawn(Vector3 position, int tier)
        {
            var zombie = idle.Count > 0 ? idle.Pop() : CreateZombie();

            float t = config.zombieTiers <= 1 ? 0f : (tier - 1) / (float)(config.zombieTiers - 1);
            float scale = Mathf.Lerp(config.zombieScaleLow, config.zombieScaleHigh, t);

            zombie.transform.SetPositionAndRotation(position, Quaternion.identity);
            zombie.gameObject.SetActive(true);
            zombie.Init(config, tier, tierMaterials[tier - 1], flashMaterials[tier - 1], scale);

            alive.Add(zombie);
        }

        const string BakedShaderName = "WarfareSurvivor/VertexAnimationToon";
        Shader bakedShader;

        Zombie CreateZombie()
        {
            var zombie = Instantiate(zombiePrefab, pool);
            zombie.Released += Release;
            LayerUtility.Apply(zombie.gameObject, LayerUtility.Zombies);
            ApplyBakedView(zombie);
            zombie.gameObject.SetActive(false);
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

            if (config.bakedZombies == null)
            {
                Debug.LogWarning("[Зомби] Запечённая анимация включена, но набор " +
                                 "не назначен в конфиге. Остаёмся на костях.");
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

            var view = BakedZombieView.Convert(zombie.gameObject, config.bakedZombies, shader);
            if (view != null) zombie.UseBakedView(view);
        }

        void Release(Zombie zombie)
        {
            alive.Remove(zombie);
            Registry.Zombies.Remove(zombie);
            zombie.gameObject.SetActive(false);
            zombie.transform.SetParent(pool, false);
            idle.Push(zombie);
        }
    }
}
