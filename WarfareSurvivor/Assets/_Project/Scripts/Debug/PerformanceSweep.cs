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

            /// <summary>Шейдер земли на время ступени. null — не трогать.</summary>
            public string GroundShader;
        }

        [SerializeField] ArenaConfig config;
        [SerializeField] Camera view;
        [SerializeField] Light sun;
        [SerializeField] UnityEngine.UI.Text banner;

        static readonly Stage[] Stages =
        {
            Full("прогрев (не считается)", shadows: true),

            Full("полная сцена, как есть", shadows: true),

            // Две встречные ступени: они отвечают на вопрос прямо, без
            // накопительного вычитания. Прежние ступени убирали объекты
            // по очереди, и «всё, кроме земли» ни разу не измерялось.
            // Земля оказалась дороже всей остальной сцены. Выясняем, за что
            // именно платим: за площадь заливки или за дорогой шейдер.
            // URP/Lit считает полноценный PBR на КАЖДЫЙ пиксель экрана,
            // Simple Lit — упрощённую модель, Unlit — только текстуру.
            Ground("земля на Simple Lit", "Universal Render Pipeline/Simple Lit"),
            Ground("земля на Unlit", "Universal Render Pipeline/Unlit"),
            // Свой шейдер: текстура, главный свет, его тень — и всё.
            // Должен дать цену Unlit, сохранив тени на земле.
            Ground("земля на своём шейдере", "WarfareSurvivor/CheapGround"),

            Shadowed("всё, КРОМЕ земли", LayerUtility.Ground),
            Shadowed("только земля", LayerUtility.Zombies, LayerUtility.Survivors, LayerUtility.Environment),
            Hide("всё, кроме земли, без теней", LayerUtility.Ground),

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

        /// <summary>Полная сцена с подменённым шейдером земли.</summary>
        /// <summary>
        /// Сравнение шейдеров земли по кругу. Отдельный список, потому что
        /// смотреть надо на картинку: полный прогон уводит сцену в состояния,
        /// где смотреть не на что.
        /// </summary>
        static readonly Stage[] GroundAB =
        {
            Ground("земля: URP/Lit (полный PBR)", "Universal Render Pipeline/Lit"),
            Ground("земля: свой дешёвый шейдер", "WarfareSurvivor/CheapGround"),
        };

        Stage[] Current => config.sweepGroundOnly ? GroundAB : Stages;

        static Stage Ground(string name, string shader) => new Stage
        {
            Name = name, HiddenLayers = new string[0], Shadows = true,
            Zombies = true, Survivors = true, Separation = true, Ui = true,
            GroundShader = shader
        };

        /// <summary>Скрыть слои, оставив тени: сравнимо с «полной сценой».</summary>
        static Stage Shadowed(string name, params string[] layers) => new Stage
        {
            Name = name, HiddenLayers = layers, Shadows = true,
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
        int baseTargetRate;
        Material groundMaterial;
        Shader baseGroundShader;
        int baseMaxAlive;
        float baseSpawnInterval;

        /// <summary>Сколько зомби держим на сцене всё время прогона.</summary>
        const int SweepZombies = 30;
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

            var ground = GameObject.Find("Ground");
            if (ground != null)
            {
                groundMaterial = ground.GetComponent<Renderer>().sharedMaterial;
                baseGroundShader = groundMaterial.shader;
            }

            // Снимаем ограничитель на время прогона. Под ним всё, что быстрее
            // 16.7 мс, читается как ровно 16.7 — то есть измеряется потолок,
            // а не затраты. Именно так «пустой экран» выглядел одинаково
            // при разрешении 1.0 и 0.5.
            baseTargetRate = config.targetFrameRate;
            config.targetFrameRate = 1000;

            // Держим толпу постоянной: за минуту прогона она успевала
            // вырасти с двадцати до почти двухсот, и поздние ступени
            // сравнивать было не с чем.
            baseMaxAlive = config.maxAliveZombies;
            baseSpawnInterval = config.spawnInterval;
            config.maxAliveZombies = SweepZombies;
            config.spawnInterval = 0.25f;
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

            if (baseTargetRate > 0) config.targetFrameRate = baseTargetRate;
            if (baseMaxAlive > 0) config.maxAliveZombies = baseMaxAlive;
            if (baseSpawnInterval > 0f) config.spawnInterval = baseSpawnInterval;

            // Настройки конвейера живут в ассете и переживают выход из игры —
            // вернуть их обязательно, иначе стенд молча поменяет проект.
            if (Pipe == null || baseRenderScale <= 0f) return;
            Pipe.renderScale = baseRenderScale;
            Pipe.shadowDistance = baseShadowDistance;
            Pipe.supportsHDR = baseHdr;
            if (ui != null) ui.enabled = true;
            // Материал земли — ассет проекта, подмена шейдера переживёт выход
            // из игры. Вернуть обязательно.
            if (groundMaterial != null && baseGroundShader != null) groundMaterial.shader = baseGroundShader;
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

            if (banner != null && frames > 10 && frames % 30 == 0)
                banner.text = $"{Current[stage].Name}\n{Mathf.RoundToInt(frames * 1000f / Mathf.Max(total, 0.01f))} fps   " +
                              $"{(total / frames):F1} мс";

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

            if (stage >= Current.Length)
            {
                // Сравнение шейдеров крутится по кругу: на него смотрят,
                // а не читают лог после. Полный прогон — один раз.
                if (config.sweepGroundOnly) stage = 0;
                else
                {
                    Debug.Log("[Стенд] Прогон закончен");
                    enabled = false;
                    return;
                }
            }

            var current = Current[stage];

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

            if (groundMaterial != null)
            {
                var shader = current.GroundShader != null ? Shader.Find(current.GroundShader) : baseGroundShader;
                if (shader != null) groundMaterial.shader = shader;
            }

            // Первые кадры после переключения не считаем: там перестройка
            // теневых карт и прогрев, к установившейся стоимости отношения
            // не имеющие. Полсекунды хватает.
            settleLeft = 30;
            stageEnds = Time.unscaledTime + Mathf.Max(2f, config.sweepStageSeconds) + 0.5f;
            if (banner != null) banner.text = current.Name;
        }

        void Report()
        {
            if (frames <= 0) return;
            // В полном прогоне первая ступень — прогрев с загрузкой сцены.
            // В сравнении шейдеров прогрева нет: ступени идут по кругу.
            if (!config.sweepGroundOnly && stage == 0) return;

            var line = new StringBuilder(160);
            line.Append("[Стенд] ").Append(Current[stage].Name)
                .Append(": ").Append((total / frames).ToString("F1")).Append(" мс сред")
                .Append(", худший ").Append(worst.ToString("F1")).Append(" мс")
                .Append(", ").Append(Mathf.RoundToInt(frames * 1000f / Mathf.Max(total, 0.01f))).Append(" fps")
                .Append(" | зомби ").Append(Registry.Zombies.Count)
                .Append(", бойцов ").Append(Registry.Survivors.Count);

            int drift = Mathf.Abs(Registry.Zombies.Count - SweepZombies);
            if (drift > SweepZombies / 3)
                line.Append("  <- толпа уплыла, ступень несравнима");

            if (frames > 1 && total / frames > worst + 0.01f)
                line.Append("  <- ЗАМЕР ВРЁТ: среднее больше худшего");

            Debug.Log(line.ToString());
        }
    }
}
