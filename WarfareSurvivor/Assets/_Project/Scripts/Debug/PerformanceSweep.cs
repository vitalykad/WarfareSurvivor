using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

            /// <summary>Масштаб рендера. Ноль — не трогать.</summary>
            public float RenderScale;

            /// <summary>Дальность теней. Ноль — не трогать.</summary>
            public float ShadowDistance;

            /// <summary>HDR-буфер. null — не трогать.</summary>
            public bool? Hdr;

            /// <summary>Рисовать ли интерфейс.</summary>
            public bool Ui;
        }

        [SerializeField] ArenaConfig config;
        [SerializeField] Camera view;
        [SerializeField] Light sun;

        static readonly Stage[] Stages =
        {
            Full("прогрев (не считается)", shadows: true),

            Full("полная сцена, как есть", shadows: true),

            // Дальше — охота за ПОЛОМ: экран пуст, логика выключена,
            // и всё равно кадр стоит двадцать миллисекунд. Значит платит
            // сам конвейер, и надо понять, за что именно.
            Floor("пол: пустой экран"),
            Floor("пол без интерфейса", ui: false),
            Floor("пол, разрешение 1.0", ui: false, renderScale: 1f),
            Floor("пол, разрешение 1.0 без HDR", ui: false, renderScale: 1f, hdr: false),
            Floor("пол, разрешение 0.5", ui: false, renderScale: 0.5f),
            Floor("пол, разрешение 0.5 без HDR", ui: false, renderScale: 0.5f, hdr: false),

            // И то же на живой сцене, чтобы выбирать не вслепую.
            Pipeline("сцена, разрешение 1.0 без HDR", renderScale: 1f, hdr: false),
            Pipeline("сцена, разрешение 0.5 без HDR", renderScale: 0.5f, hdr: false),
            Pipeline("сцена, без HDR", hdr: false),

            Full("всё обратно", shadows: true),
        };

        /// <summary>Ступень «пола»: не рисуем и не считаем ничего, меряем конвейер.</summary>
        static Stage Floor(string name, bool ui = true, float renderScale = 0f, bool? hdr = null) => new Stage
        {
            Name = name, HiddenLayers = All(), Shadows = false,
            Zombies = false, Survivors = false, Separation = false,
            RenderScale = renderScale, Hdr = hdr, Ui = ui
        };

        static Stage Pipeline(string name, float renderScale = 0f, float shadowDistance = 0f, bool? hdr = null) => new Stage
        {
            Name = name, HiddenLayers = new string[0], Shadows = true,
            Zombies = true, Survivors = true, Separation = true,
            RenderScale = renderScale, ShadowDistance = shadowDistance, Hdr = hdr, Ui = true
        };

        static string[] All() => new[]
        {
            LayerUtility.Zombies, LayerUtility.Survivors,
            LayerUtility.Environment, LayerUtility.Ground
        };

        static Stage Full(string name, bool shadows) => new Stage
        {
            Name = name, HiddenLayers = new string[0], Shadows = shadows,
            Zombies = true, Survivors = true, Separation = true, Ui = true
        };

        static Stage Hide(string name, params string[] layers) => new Stage
        {
            Name = name, HiddenLayers = layers, Shadows = false,
            Zombies = true, Survivors = true, Separation = true, Ui = true
        };

        int stage = -1;
        int settleLeft;
        float stageEnds;
        int frames;
        float total;
        float worst;
        float gpuTotal;
        float cpuTotal;
        int baseMask;
        float savedSeparation;
        float baseRenderScale;
        float baseShadowDistance;
        bool baseHdr;
        Canvas ui;

        static UniversalRenderPipelineAsset Pipe =>
            GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

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
            ui = FindFirstObjectByType<Canvas>();
            if (Pipe != null)
            {
                baseRenderScale = Pipe.renderScale;
                baseShadowDistance = Pipe.shadowDistance;
                baseHdr = Pipe.supportsHDR;
            }
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

            // Настройки конвейера живут в ассете и переживают выход из игры —
            // вернуть их обязательно, иначе стенд молча поменяет проект.
            if (Pipe == null || baseRenderScale <= 0f) return;
            Pipe.renderScale = baseRenderScale;
            Pipe.shadowDistance = baseShadowDistance;
            Pipe.supportsHDR = baseHdr;
            if (ui != null) ui.enabled = true;
        }

        void Update()
        {
            // Кадры устаканивания ПРОПУСКАЕМ целиком, а не считаем со знаком
            // минус. Раньше счётчик кадров стартовал отрицательным, а сумма
            // времени копилась с первого кадра — сумма по 348 кадрам делилась
            // на 276, и среднее выходило на четверть выше настоящего.
            // Признак вранья был на виду: худший кадр оказывался МЕНЬШЕ
            // среднего, чего не бывает.
            if (settleLeft > 0)
            {
                settleLeft--;
                return;
            }

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

            if (Pipe != null)
            {
                Pipe.renderScale = current.RenderScale > 0f ? current.RenderScale : baseRenderScale;
                Pipe.shadowDistance = current.ShadowDistance > 0f ? current.ShadowDistance : baseShadowDistance;
                Pipe.supportsHDR = current.Hdr ?? baseHdr;
            }

            if (ui != null) ui.enabled = current.Ui;

            // Первые кадры после переключения не считаем: там перестройка
            // теневых карт и прогрев, к установившейся стоимости отношения
            // не имеющие. Полсекунды хватает.
            settleLeft = 30;
            stageEnds = Time.unscaledTime + Mathf.Max(2f, config.sweepStageSeconds) + 0.5f;
        }

        void Report()
        {
            if (frames <= 0) return;
            if (stage == 0) return;   // прогрев: там загрузка сцены

            var line = new StringBuilder(160);
            line.Append("[Стенд] ").Append(Stages[stage].Name)
                .Append(": ").Append((total / frames).ToString("F1")).Append(" мс сред")
                .Append(", худший ").Append(worst.ToString("F1")).Append(" мс")
                .Append(", ").Append(Mathf.RoundToInt(frames * 1000f / Mathf.Max(total, 0.01f))).Append(" fps")
                .Append(" | зомби ").Append(Registry.Zombies.Count)
                .Append(", бойцов ").Append(Registry.Survivors.Count);

            if (frames > 1 && total / frames > worst + 0.01f)
                line.Append("  <- ЗАМЕР ВРЁТ: среднее больше худшего");

            Debug.Log(line.ToString());
        }
    }
}
