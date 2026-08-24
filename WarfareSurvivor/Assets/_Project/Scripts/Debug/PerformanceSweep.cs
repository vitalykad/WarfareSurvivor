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

            /// <summary>Шейдер зомби на время ступени. null — не трогать.</summary>
            public string ZombieShader;

            /// <summary>Отбрасывают ли зомби тень. null — не трогать.</summary>
            public bool? ZombieShadows;

            /// <summary>
            /// Гнутся ли зомби по костям. false — рисуем застывшую позу
            /// обычным мешем. null — не трогать.
            /// </summary>
            public bool? ZombieSkinning;

            /// <summary>Крутится ли аниматор зомби. null — не трогать.</summary>
            public bool? ZombieAnimator;
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

        /// <summary>
        /// Сравнение настроек конвейера по кругу. Дальность теней на кадре
        /// почти не сказывается, зато сказывается на РЕЗКОСТИ: карта теней
        /// одна и та же, и чем меньше площадь, которую она покрывает, тем
        /// больше её точек приходится на каждый метр. Поэтому смотреть надо
        /// глазами, а не только в цифры.
        /// </summary>
        static readonly Stage[] PipelineAB =
        {
            Framing("масштаб 0.8 · тени 70 м (как сейчас)", 0.8f, 70f),
            Framing("масштаб 1.0 · тени 70 м", 1f, 70f),
            Framing("масштаб 1.0 · тени 60 м", 1f, 60f),
            Framing("масштаб 0.8 · тени 60 м", 0.8f, 60f),
        };

        static Stage Framing(string name, float renderScale, float shadowDistance) => new Stage
        {
            Name = name, HiddenLayers = new string[0], Shadows = true,
            Zombies = true, Survivors = true, Separation = true, Ui = true,
            RenderScale = renderScale, ShadowDistance = shadowDistance
        };

        /// <summary>
        /// Зомби: шейдер и отбрасывание тени, на закреплённой толпе.
        /// Толпа упирается в GPU линейно, по 0.13 мс на зомби, — здесь
        /// выясняем, из чего эта цифра складывается.
        /// </summary>
        static readonly Stage[] ZombieAB =
        {
            Zombie("зомби: как есть (TCP2, 5 проходов)", null, true),
            Zombie("зомби: без отбрасывания тени", null, false),
            Zombie("зомби: свой тун", "WarfareSurvivor/CheapToon", true),
            Zombie("зомби: свой тун, без тени", "WarfareSurvivor/CheapToon", false),
        };

        /// <summary>
        /// За что платим на зомби. Треугольников у зомби 1107 — для этого
        /// телефона пустяк, значит дело не в геометрии. Остаются три
        /// подозреваемых, и каждый снимается своей ступенью:
        ///
        ///   как есть -> застывшая поза   = цена СКИННИНГА (кости, отдельная
        ///                                  свёртка на каждого зомби);
        ///   застывшая -> без аниматора   = цена расчёта поз на процессоре;
        ///   застывшая -> плоский шейдер  = цена ПИКСЕЛЕЙ и материала.
        ///
        /// Если основное уходит на первую разницу — толпу спасёт запекание
        /// анимации в текстуру и BatchRendererGroup. Если на третью — их
        /// заводить незачем, и лечить надо шейдер.
        ///
        /// Застывшая поза — это тот же меш, та же площадь на экране и то же
        /// число вызовов отрисовки. Убраны ровно кости, ничего больше.
        /// </summary>
        static readonly Stage[] SkinningAB =
        {
            Skin("зомби: как есть (кости + аниматор)", skinning: true, animator: true),
            Skin("зомби: застывшая поза (без костей)", skinning: false, animator: true),
            Skin("зомби: застывшая поза, аниматор выключен", skinning: false, animator: false),
            Skin("зомби: застывшая поза, плоский шейдер", skinning: false, animator: false,
                 shader: "Universal Render Pipeline/Unlit"),
        };

        static Stage Skin(string name, bool skinning, bool animator, string shader = null) => new Stage
        {
            Name = name, HiddenLayers = new string[0], Shadows = true,
            Zombies = true, Survivors = true, Separation = true, Ui = true,
            ZombieSkinning = skinning, ZombieAnimator = animator,
            ZombieShader = shader, ZombieShadows = true
        };

        static Stage Zombie(string name, string shader, bool shadows) => new Stage
        {
            Name = name, HiddenLayers = new string[0], Shadows = true,
            Zombies = true, Survivors = true, Separation = true, Ui = true,
            ZombieShader = shader, ZombieShadows = shadows
        };

        Stage[] Current
        {
            get
            {
                switch (config.sweepMode)
                {
                    case SweepMode.Ground: return GroundAB;
                    case SweepMode.Pipeline: return PipelineAB;
                    case SweepMode.Zombies: return ZombieAB;
                    case SweepMode.Skinning: return SkinningAB;
                    default: return Stages;
                }
            }
        }

        bool Looping => config.sweepMode == SweepMode.Ground
                     || config.sweepMode == SweepMode.Pipeline
                     || config.sweepMode == SweepMode.Zombies
                     || config.sweepMode == SweepMode.Skinning;

        bool Ramping => config.sweepMode == SweepMode.Ramp;

        /// <summary>
        /// Ждать ли набора толпы перед счётом.
        ///
        /// Спавнер доливает зомби постепенно, и за десять секунд ступени
        /// численность успевает вырасти вдвое. Тогда соседние ступени
        /// сравнивают не настройку, а размер толпы — то есть ничего.
        /// </summary>
        bool CrowdPinned => config.sweepMode == SweepMode.Zombies
                         || config.sweepMode == SweepMode.Skinning;

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

        /// <summary>Толпа на этой ступени уже набрана — счёт пошёл.</summary>
        bool crowdReady;

        /// <summary>
        /// Успел ли стенд запомнить исходное состояние сцены.
        ///
        /// Без этого флага выключение стенда гасило игру: отказ в Awake ставит
        /// enabled = false, это зовёт OnDisable, а тот восстанавливал маску
        /// камеры из ещё не заполненного поля — то есть из нуля. Камера
        /// переставала рисовать всё.
        /// </summary>
        bool captured;
        int timed;
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
        Shader baseZombieShader;

        /// <summary>Исходный материал -> его копия на подменном шейдере.</summary>
        readonly System.Collections.Generic.Dictionary<Material, Material> swapped =
            new System.Collections.Generic.Dictionary<Material, Material>();
        string wantZombieShader;
        bool? wantZombieShadows;
        bool? wantZombieSkinning;
        bool? wantZombieAnimator;

        /// <summary>Поза, снятая с первого попавшегося зомби, одна на всех.</summary>
        Mesh frozenPose;
        int baseMaxAlive;
        float baseSpawnInterval;

        /// <summary>Сколько зомби держим на сцене всё время прогона.</summary>
        const int SweepZombies = 30;
        float baseRenderScale;
        float baseShadowDistance;
        bool baseHdr;
        bool baseInvincible;
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
            captured = true;
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
            baseInvincible = config.debugSquadInvincible;

            if (CrowdPinned)
            {
                // Толпа закреплена на численности, где разница уже видна,
                // а отряд бессмертен — иначе прогон оборвётся вайпом.
                // Долив частый: чем быстрее наберём потолок, тем меньше
                // прогон стоит в ожидании.
                config.debugSquadInvincible = true;
                config.maxAliveZombies = 150;
                config.spawnInterval = 0.05f;
            }

            if (Ramping)
            {
                // Отряд бессмертен: иначе прогон обрывается вайпом задолго
                // до интересной численности, и верхняя часть диапазона
                // остаётся неизмеренной.
                config.debugSquadInvincible = true;
                config.maxAliveZombies = Mathf.Max(1, config.rampStep);
                config.spawnInterval = 0.2f;
                StartRampStep();
                return;
            }

            // Только для прогонов, где толпа — фон, а не предмет замера.
            // Раньше эта пара строк стояла безусловно и затирала закрепление
            // толпы, сделанное выше: сравнения по зомби шли не на полутора
            // сотнях, а на тридцати.
            if (!CrowdPinned)
            {
                config.maxAliveZombies = SweepZombies;
                config.spawnInterval = 0.25f;
            }
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
            // Нечего восстанавливать: до запоминания состояния не дошли.
            if (!captured) return;

            // Стенд не должен оставлять сцену в разобранном виде.
            if (view != null) view.cullingMask = baseMask;
            if (sun != null) sun.shadows = LightShadows.Soft;
            if (config == null) return;

            config.simulateZombies = true;
            config.simulateSurvivors = true;
            if (savedSeparation > 0f) config.zombieSeparationRadius = savedSeparation;

            if (baseTargetRate > 0) config.targetFrameRate = baseTargetRate;
            if (baseMaxAlive > 0) config.maxAliveZombies = baseMaxAlive;
            config.debugSquadInvincible = baseInvincible;

            // Копии материалов больше не нужны: пул выдаёт зомби с исходным
            // материалом тира при следующем появлении.
            foreach (var pair in swapped)
                if (pair.Value != null) Destroy(pair.Value);
            swapped.Clear();

            foreach (var zombie in Registry.Zombies)
            {
                if (zombie == null) continue;

                // Кости обратно, застывшая копия — прочь. Иначе сцена
                // останется с толпой манекенов после выключения стенда.
                ApplySkinning(zombie, true);
                var animator = zombie.GetComponentInChildren<Animator>();
                if (animator != null) animator.enabled = true;

                foreach (var renderer in zombie.GetComponentsInChildren<Renderer>())
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            if (frozenPose != null) Destroy(frozenPose);
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
            if (Ramping)
            {
                UpdateRamp();
                return;
            }

            // Пока толпа не набрана, ступень не идёт: настройку раскладываем,
            // время не считаем, конец ступени отодвигаем.
            //
            // Порог — две трети потолка, и вот почему. Потолок спавнера
            // считает и трупы, доигрывающие смерть, а Registry — только
            // живых. Под потолком в 150 живых стабильно держится около 110,
            // и требовать больше значило бы ждать вечно. Рисуются, впрочем,
            // и те и другие, так что нагрузка соответствует потолку.
            if (CrowdPinned && !crowdReady)
            {
                ApplyToZombies();
                if (Registry.Zombies.Count < config.maxAliveZombies * 2 / 3)
                {
                    stageEnds = Time.unscaledTime + Mathf.Max(2f, config.sweepStageSeconds) + 0.5f;
                    if (banner != null)
                        banner.text = Current[stage].Name + "\nнабираем толпу " +
                                      Registry.Zombies.Count + " из " + config.maxAliveZombies;
                    return;
                }

                crowdReady = true;
                settleLeft = 30;
            }

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

            // Когда кадр упирается в развёртку экрана, все настройки читаются
            // как ровно 16.7 мс и сравнить их нельзя. Время GPU показывает
            // настоящую стоимость даже под потолком — именно по нему и надо
            // выбирать между разрешениями.
            FrameTimingManager.CaptureFrameTimings();
            var timings = new FrameTiming[1];
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                gpuTotal += (float)timings[0].gpuFrameTime;
                cpuTotal += (float)timings[0].cpuFrameTime;
                timed++;
            }

            if (config.sweepMode == SweepMode.Zombies) ApplyToZombies();

            if (banner != null && frames > 10 && frames % 30 == 0)
                banner.text = $"{Current[stage].Name}\n{Mathf.RoundToInt(frames * 1000f / Mathf.Max(total, 0.01f))} fps   " +
                              $"{(total / frames):F1} мс   gpu {(timed > 0 ? gpuTotal / timed : 0f):F1} мс";

            if (Time.unscaledTime < stageEnds) return;

            Report();
            Next();
        }

        /// <summary>
        /// Растущая толпа. Потолок живых поднимается ступенями, и на каждой
        /// в лог уходит строка «столько-то зомби — такой-то кадр». Ищем
        /// не среднюю цену зомби, а ЧИСЛЕННОСТЬ, на которой кадр перестаёт
        /// укладываться в бюджет.
        /// </summary>
        void UpdateRamp()
        {
            if (settleLeft > 0)
            {
                settleLeft--;
                return;
            }

            frames++;
            float ms = Time.unscaledDeltaTime * 1000f;
            total += ms;
            if (ms > worst) worst = ms;

            FrameTimingManager.CaptureFrameTimings();
            var timings = new FrameTiming[1];
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                gpuTotal += (float)timings[0].gpuFrameTime;
                timed++;
            }

            if (banner != null && frames > 5 && frames % 20 == 0)
                banner.text = $"зомби {Registry.Zombies.Count} из {config.maxAliveZombies}\n" +
                              $"{Mathf.RoundToInt(frames * 1000f / Mathf.Max(total, 0.01f))} fps   " +
                              $"{(total / frames):F1} мс   gpu {(timed > 0 ? gpuTotal / timed : 0f):F1} мс";

            if (Time.unscaledTime < stageEnds) return;

            ReportRamp();

            if (config.maxAliveZombies >= config.rampMax)
            {
                Debug.Log("[Толпа] Прогон закончен");
                enabled = false;
                return;
            }

            config.maxAliveZombies += Mathf.Max(1, config.rampStep);
            StartRampStep();
        }

        /// <summary>
        /// Раскладывает настройку по живым зомби. Зовётся каждый кадр,
        /// а не только при смене ступени: зомби берутся из пула и приходят
        /// новые, а настройка должна быть на всех, иначе замер размажется.
        /// </summary>
        void ApplyToZombies()
        {
            if (wantZombieShader == null && wantZombieShadows == null
                && wantZombieSkinning == null && wantZombieAnimator == null) return;

            var mode = wantZombieShadows == true
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;

            var zombies = Registry.Zombies;
            for (int i = 0; i < zombies.Count; i++)
            {
                var zombie = zombies[i];
                if (zombie == null) continue;

                // Сначала кости, потом материалы: заморозка добавляет
                // ещё один рисователь, и он тоже должен получить настройку.
                if (wantZombieSkinning != null) ApplySkinning(zombie, wantZombieSkinning.Value);

                if (wantZombieAnimator != null)
                {
                    var animator = zombie.GetComponentInChildren<Animator>();
                    if (animator != null && animator.enabled != wantZombieAnimator.Value)
                        animator.enabled = wantZombieAnimator.Value;
                }

                foreach (var renderer in zombie.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled) continue;

                    if (wantZombieShadows != null && renderer.shadowCastingMode != mode)
                        renderer.shadowCastingMode = mode;

                    if (wantZombieShader == null || renderer.sharedMaterial == null) continue;

                    var swapped = SwappedMaterial(renderer.sharedMaterial);
                    if (swapped != null) renderer.sharedMaterial = swapped;
                }
            }
        }

        /// <summary>
        /// Переключает зомби между скиннингом и застывшей позой.
        ///
        /// Застывшая поза — тот же меш, тот же материал, тот же размер
        /// на экране и тот же один вызов отрисовки. Отличие ровно одно:
        /// вершины не пересчитываются по сорока одной кости каждый кадр.
        /// Разница во времени кадра и есть цена скиннинга.
        ///
        /// Позу снимаем один раз с первого зомби и раздаём всем — именно так
        /// выглядела бы толпа под BatchRendererGroup: один общий меш.
        /// </summary>
        void ApplySkinning(Component zombie, bool skinned)
        {
            var smr = zombie.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null) return;

            var frozen = smr.GetComponent<MeshRenderer>();

            if (skinned)
            {
                if (frozen != null && frozen.enabled) frozen.enabled = false;
                if (!smr.enabled) smr.enabled = true;
                return;
            }

            if (frozenPose == null)
            {
                frozenPose = new Mesh { name = "ЗастывшийЗомби" };
                // Без масштаба: меш живёт под тем же объектом, и масштаб
                // тира применит сам transform. С ним размер удвоился бы.
                smr.BakeMesh(frozenPose, false);
            }

            var filter = smr.GetComponent<MeshFilter>();
            if (filter == null) filter = smr.gameObject.AddComponent<MeshFilter>();
            if (filter.sharedMesh != frozenPose) filter.sharedMesh = frozenPose;

            if (frozen == null)
            {
                frozen = smr.gameObject.AddComponent<MeshRenderer>();
                frozen.sharedMaterials = smr.sharedMaterials;
            }

            if (!frozen.enabled) frozen.enabled = true;
            if (smr.enabled) smr.enabled = false;
        }

        /// <summary>
        /// Возвращает НОВЫЙ материал на нужном шейдере, а не подменяет шейдер
        /// у существующего.
        ///
        /// Подмена ломает материал: у него остаются включёнными ключевые слова
        /// прежнего шейдера, вариантов с ними в новом нет, и Unity рисует
        /// ошибку — зомби становились пурпурными. Новый материал такого
        /// наследства не несёт.
        /// </summary>
        Material SwappedMaterial(Material source)
        {
            if (source == null) return null;
            if (swapped.TryGetValue(source, out var ready)) return ready;

            var shader = Shader.Find(wantZombieShader);
            if (shader == null) return null;

            var copy = new Material(shader) { name = source.name + "_" + shader.name };
            if (source.HasProperty("_BaseMap")) copy.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
            if (source.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", source.GetColor("_BaseColor"));

            swapped[source] = copy;
            return copy;
        }

        void StartRampStep()
        {
            frames = 0;
            total = 0f;
            worst = 0f;
            gpuTotal = 0f;
            timed = 0;

            // Ждём, пока толпа доберётся до нового потолка, и только потом
            // начинаем считать: иначе померим момент долива, а не нагрузку.
            settleLeft = 45;
            stageEnds = Time.unscaledTime + Mathf.Max(2f, config.rampStepSeconds) + 0.75f;
        }

        void ReportRamp()
        {
            if (frames <= 0) return;

            float avg = total / frames;
            var line = new StringBuilder(160);
            line.Append("[Толпа] зомби ").Append(Registry.Zombies.Count)
                .Append(" (потолок ").Append(config.maxAliveZombies).Append(')')
                .Append(": ").Append(avg.ToString("F1")).Append(" мс сред")
                .Append(", худший ").Append(worst.ToString("F1")).Append(" мс")
                .Append(", ").Append(Mathf.RoundToInt(1000f / Mathf.Max(avg, 0.01f))).Append(" fps");

            if (timed > 0) line.Append(", gpu ").Append((gpuTotal / timed).ToString("F1")).Append(" мс");

            // Бюджет 16.7 мс. Помечаем ступень, на которой вышли за него, —
            // ради этой отметки прогон и затевается.
            if (avg > 17.5f) line.Append("   <- ВЫШЛИ ЗА БЮДЖЕТ");

            Debug.Log(line.ToString());
        }

        void Next()
        {
            stage++;
            frames = 0;
            total = 0f;
            worst = 0f;
            gpuTotal = 0f;
            cpuTotal = 0f;
            timed = 0;

            if (stage >= Current.Length)
            {
                // Сравнение шейдеров крутится по кругу: на него смотрят,
                // а не читают лог после. Полный прогон — один раз.
                if (Looping) stage = 0;
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

            wantZombieShader = current.ZombieShader;
            wantZombieShadows = current.ZombieShadows;
            wantZombieSkinning = current.ZombieSkinning;
            wantZombieAnimator = current.ZombieAnimator;
            ApplyToZombies();

            // Первые кадры после переключения не считаем: там перестройка
            // теневых карт и прогрев, к установившейся стоимости отношения
            // не имеющие. Полсекунды хватает.
            settleLeft = 30;
            crowdReady = false;
            stageEnds = Time.unscaledTime + Mathf.Max(2f, config.sweepStageSeconds) + 0.5f;
            if (banner != null) banner.text = current.Name;
        }

        void Report()
        {
            if (frames <= 0) return;
            // В полном прогоне первая ступень — прогрев с загрузкой сцены.
            // В сравнении шейдеров прогрева нет: ступени идут по кругу.
            if (!Looping && stage == 0) return;

            var line = new StringBuilder(160);
            line.Append("[Стенд] ").Append(Current[stage].Name)
                .Append(": ").Append((total / frames).ToString("F1")).Append(" мс сред")
                .Append(", худший ").Append(worst.ToString("F1")).Append(" мс")
                .Append(", ").Append(Mathf.RoundToInt(frames * 1000f / Mathf.Max(total, 0.01f))).Append(" fps")
                .Append(" | зомби ").Append(Registry.Zombies.Count)
                .Append(", бойцов ").Append(Registry.Survivors.Count);

            if (timed > 0)
                line.Append(" || gpu ").Append((gpuTotal / timed).ToString("F1"))
                    .Append(" мс, cpu ").Append((cpuTotal / timed).ToString("F1")).Append(" мс");

            // Сравнивать надо с тем потолком, на котором прогон и идёт:
            // сравнения по зомби держат толпу в 150, и мерка в 30 ругалась
            // бы на каждую ступень подряд.
            int expected = config.sweepMode == SweepMode.Zombies
                        || config.sweepMode == SweepMode.Skinning
                ? config.maxAliveZombies
                : SweepZombies;
            int drift = Mathf.Abs(Registry.Zombies.Count - expected);
            if (drift > expected / 3)
                line.Append("  <- толпа уплыла, ступень несравнима");

            if (frames > 1 && total / frames > worst + 0.01f)
                line.Append("  <- ЗАМЕР ВРЁТ: среднее больше худшего");

            Debug.Log(line.ToString());
        }
    }
}
