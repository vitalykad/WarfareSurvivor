using System.Text;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Стенд нагрузки: сам проходит ступени, выключая группы объектов
    /// по очереди, и пишет в лог стоимость каждой.
    ///
    /// Смысл в том, чтобы не гадать. Один прогон даёт таблицу «что убрали —
    /// сколько выиграли», и виновник просадки виден вычитанием, а не
    /// рассуждением. Это тот самый растущий стенд из §12
    /// CROWD_PROJECT_LESSONS, только вместо роста толпы — снятие слоёв.
    ///
    /// Гасим МАСКОЙ КАМЕРЫ, а не компонентами: рисование выключается,
    /// а логика продолжает считаться. Выключив компоненты, мы померили бы
    /// заодно и отсутствие их работы, то есть не то.
    /// </summary>
    public class PerformanceSweep : MonoBehaviour
    {
        struct Stage
        {
            public string Name;
            public string[] HiddenLayers;
            public bool Shadows;
            public bool Zombies;
            public bool Survivors;
            public bool Separation;
        }

        [SerializeField] ArenaConfig config;
        [SerializeField] Camera view;
        [SerializeField] Light sun;

        static readonly Stage[] Stages =
        {
            Full("всё как есть", shadows: true),
            Full("без теней", shadows: false),
            Hide("без теней и зомби", LayerUtility.Zombies),
            Hide("+ без отряда", LayerUtility.Zombies, LayerUtility.Survivors),
            Hide("+ без окружения", LayerUtility.Zombies, LayerUtility.Survivors, LayerUtility.Environment),
            // Дальше выключаем не картинку, а счёт: если время не падает
            // и здесь, значит виновата не наша логика, а сам движок.
            new Stage { Name = "ничего не рисуем, без расталкивания зомби", Shadows = false,
                        HiddenLayers = All(), Zombies = true, Survivors = true, Separation = false },
            new Stage { Name = "+ зомби не думают", Shadows = false,
                        HiddenLayers = All(), Zombies = false, Survivors = true, Separation = false },
            new Stage { Name = "+ отряд не думает", Shadows = false,
                        HiddenLayers = All(), Zombies = false, Survivors = false, Separation = false },
            Full("всё обратно", shadows: true),
        };

        static string[] All() => new[] { LayerUtility.Zombies, LayerUtility.Survivors, LayerUtility.Environment };

        static Stage Full(string name, bool shadows) => new Stage
        {
            Name = name, HiddenLayers = new string[0], Shadows = shadows,
            Zombies = true, Survivors = true, Separation = true
        };

        static Stage Hide(string name, params string[] layers) => new Stage
        {
            Name = name, HiddenLayers = layers, Shadows = false,
            Zombies = true, Survivors = true, Separation = true
        };

        int stage = -1;
        float stageEnds;
        int frames;
        float total;
        float worst;
        float gpuTotal;
        float cpuTotal;
        int baseMask;
        float savedSeparation;

        void Awake()
        {
            if (config == null || view == null)
            {
                enabled = false;
                return;
            }

            if (!config.debugSweep)
            {
                enabled = false;
                return;
            }

            baseMask = view.cullingMask;
            Next();
        }

        void OnDisable()
        {
            // Стенд не должен оставлять сцену в разобранном виде.
            if (view != null) view.cullingMask = baseMask;
            if (sun != null) sun.shadows = LightShadows.Soft;
            if (config == null) return;

            config.simulateZombies = true;
            config.simulateSurvivors = true;
            if (savedSeparation > 0f) config.zombieSeparationRadius = savedSeparation;
        }

        void Update()
        {
            frames++;
            float ms = Time.unscaledDeltaTime * 1000f;
            total += ms;
            if (ms > worst) worst = ms;

            if (Time.unscaledTime < stageEnds) return;

            Report();
            Next();
        }

        void Next()
        {
            stage++;
            frames = 0;
            total = 0f;
            worst = 0f;
            gpuTotal = 0f;
            cpuTotal = 0f;

            if (stage >= Stages.Length)
            {
                Debug.Log("[Стенд] Прогон закончен");
                enabled = false;
                return;
            }

            var current = Stages[stage];

            int mask = baseMask;
            foreach (var layerName in current.HiddenLayers)
            {
                int layer = LayerMask.NameToLayer(layerName);
                if (layer >= 0) mask &= ~(1 << layer);
            }
            view.cullingMask = mask;

            if (sun != null) sun.shadows = current.Shadows ? LightShadows.Soft : LightShadows.None;

            config.simulateZombies = current.Zombies;
            config.simulateSurvivors = current.Survivors;
            // Радиус ноль отключает расталкивание, не трогая остальное.
            if (savedSeparation <= 0f) savedSeparation = config.zombieSeparationRadius;
            config.zombieSeparationRadius = current.Separation ? savedSeparation : 0f;

            // Первые кадры после переключения не считаем: там перестройка
            // теневых карт и прогрев, к установившейся стоимости отношения
            // не имеющие.
            stageEnds = Time.unscaledTime + Mathf.Max(2f, config.sweepStageSeconds);
            frames = -Mathf.Max(5, Mathf.RoundToInt(config.sweepStageSeconds * 0.2f * 60f));
        }

        void Report()
        {
            if (frames <= 0) return;

            var line = new StringBuilder(160);
            line.Append("[Стенд] ").Append(Stages[stage].Name)
                .Append(": ").Append((total / frames).ToString("F1")).Append(" мс сред")
                .Append(", худший ").Append(worst.ToString("F1")).Append(" мс")
                .Append(", ").Append(Mathf.RoundToInt(frames * 1000f / Mathf.Max(total, 0.01f))).Append(" fps")
                .Append(" | зомби ").Append(Registry.Zombies.Count)
                .Append(", бойцов ").Append(Registry.Survivors.Count);

            Debug.Log(line.ToString());
        }
    }
}
