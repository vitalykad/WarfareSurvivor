using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace WarfareSurvivor.EditorTools
{
    /// <summary>
    /// Собирает тестовую арену целиком из кода, чтобы её можно было
    /// пересобрать после любой правки, а не чинить руками.
    /// </summary>
    public static class TestArenaBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Sandbox/TestArena.unity";
        const string RunScenePath = "Assets/_Project/Scenes/Run.unity";
        const string ConfigPath = "Assets/_Project/Configs/ArenaConfig.asset";

        /// <summary>
        /// У забега СВОЙ конфиг.
        ///
        /// Общий не годится: стенду замеров нужны двадцать пять бойцов и
        /// бесконечные волны, при которых сняты все цифры, а забегу — малый
        /// стартовый отряд и три волны. Одно поле не может служить обоим,
        /// и попытка развести их флагами уже начала плодить костыли.
        ///
        /// Оба конфига одного типа, поэтому балансная правка переносится
        /// между ними копированием значения, а не переписыванием кода.
        /// </summary>
        const string RunConfigPath = "Assets/_Project/Configs/RunConfig.asset";
        const string PolicePrefab = "Assets/_Project/Prefabs/Survivors/Survivor_Police.prefab";
        const string SouthPolicePrefab = "Assets/_Project/Prefabs/Survivors/Survivor_SouthPoliceman.prefab";
        const string FarmerPrefab = "Assets/_Project/Prefabs/Survivors/Survivor_ShovelFarmer.prefab";
        const string ClassesDir = "Assets/_Project/Configs/Classes";
        const string ZombiePrefab = "Assets/_Project/Prefabs/Monsters/Monster_Zombie.prefab";
        const string RuinModel = "Assets/Models/Environment/desert+ruin+3d+model/desert_ruin_3d_model.fbx";
        const string RuinPrefab = "Assets/_Project/Prefabs/Environment/Ruin.prefab";
        const string GroundMaterial = "Assets/Materials/Ground/DesertGround1/DesertGround1.mat";

        /// <summary>
        /// Стенд замеров: та же арена плюс счётчик кадра и стенд нагрузки,
        /// бесконечные волны, никакого забега. Сюда ходят мерить.
        /// </summary>
        [MenuItem("WarfareSurvivor/Build Test Arena")]
        public static void Build() => BuildScene(gameplay: false);

        /// <summary>
        /// Сцена забега: три волны, тир-апы, победа и поражение. Ни счётчика
        /// кадра, ни стенда — сюда ходят ИГРАТЬ, и всё, что мешает смотреть
        /// на игру глазами, отсюда убрано.
        ///
        /// Обе сцены собираются одним кодом. Разойтись руками они не могут:
        /// правка арены сама попадает в забег после пересборки.
        /// </summary>
        [MenuItem("WarfareSurvivor/Build Run Scene")]
        public static void BuildRun() => BuildScene(gameplay: true);

        static void BuildScene(bool gameplay)
        {
            // Шейдеры, которые ищутся через Shader.Find, надо явно включить
            // в сборку — иначе на устройстве их не окажется.
            ShaderInclusion.Ensure();
            LayerSetup.Ensure();

            // Без этого FrameTimingManager молчит и разделения CPU/GPU не будет.
            PlayerSettings.enableFrameTimingStats = true;

            LoadOrCreateConfig();
            if (gameplay) EnsureRunConfig();
            PrepareSurvivorPrefab(PolicePrefab);
            PrepareSurvivorPrefab(SouthPolicePrefab);
            PrepareSurvivorPrefab(FarmerPrefab);
            PrepareZombiePrefab();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Конфиг берём ПОСЛЕ пересохранения префабов. Их импорт
            // пересоздаёт managed-объекты, и ссылка, взятая раньше, уже
            // указывает на уничтоженный инстанс — в сцену она уходит как
            // fileID 0, а компонент стартует с пустым конфигом.
            string configPath = gameplay ? RunConfigPath : ConfigPath;
            var config = AssetDatabase.LoadAssetAtPath<ArenaConfig>(configPath);
            if (config == null) Debug.LogError($"[TestArena] Не читается конфиг {configPath}");
            EnsureClasses(config);

            CreateGround();
            CreateLight(config);
            CreateRuins(config);

            var squad = CreateSquad(config);
            var camera = CreateCamera(squad.transform);
            var joystick = CreateUI();
            if (!gameplay) CreateFrameMeter(config);

            var frameRate = new GameObject("FrameRate").AddComponent<FrameRateController>();
            Wire(frameRate, nameof(config), config);

            // Стенд нагрузки в сцену не кладём, пока он не нужен: это
            // инструмент замера, а не часть игры. Включается галочкой
            // debugSweep в конфиге и пересборкой сцены.
            if (config.debugSweep && !gameplay)
            {
                var sweep = new GameObject("PerformanceSweep").AddComponent<PerformanceSweep>();
                Wire(sweep, nameof(config), config);
                Wire(sweep, "banner", CreateSweepBanner());
                Wire(sweep, "view", camera.GetComponent<Camera>());
                Wire(sweep, "sun", Object.FindFirstObjectByType<Light>());
            }

            Wire(squad, nameof(config), config);
            Wire(squad, "joystick", joystick);
            Wire(squad, "viewCamera", camera.GetComponent<Camera>());

            var tracers = new GameObject("TracerLayer",
                typeof(MeshFilter), typeof(MeshRenderer)).AddComponent<TracerLayer>();
            Wire(tracers, nameof(config), config);

            var spawner = new GameObject("ZombieSpawner").AddComponent<ZombieSpawner>();
            Wire(spawner, nameof(config), config);
            Wire(spawner, "zombiePrefab", AssetDatabase.LoadAssetAtPath<Zombie>(ZombiePrefab));
            Wire(spawner, "squad", squad);

            if (gameplay) CreateRun(config, squad, spawner);

            EditorSceneManager.MarkSceneDirty(scene);

            string path = gameplay ? RunScenePath : ScenePath;
            EnsureFolder(gameplay ? "Assets/_Project/Scenes" : "Assets/_Project/Scenes/Sandbox");
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.SaveAssets();

            Debug.Log($"[{(gameplay ? "Забег" : "TestArena")}] Сцена собрана: {path}");
        }

        [MenuItem("WarfareSurvivor/Setup/Reset Config to Defaults")]
        public static void ResetConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<ArenaConfig>(ConfigPath);
            if (config == null)
            {
                LoadOrCreateConfig();
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Сбросить настройки арены",
                    "Все числа в ArenaConfig вернутся к значениям из кода. " +
                    "Ручная настройка — состав отряда, свет, камера, баланс — пропадёт.\n\n" +
                    "Пересборка сцены этого НЕ делает: сбросить можно только отсюда.",
                    "Сбросить", "Отмена"))
                return;

            // Копируем поля свежесозданного экземпляра поверх существующего:
            // так сохраняется GUID ассета, а значит и все ссылки на него
            // в сцене. Пересоздание ассета их бы обнулило.
            var defaults = ScriptableObject.CreateInstance<ArenaConfig>();
            EditorUtility.CopySerialized(defaults, config);
            Object.DestroyImmediate(defaults);

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TestArena] {ConfigPath} сброшен к умолчаниям кода");
        }

        /// <summary>
        /// Гарантирует, что ассет конфига существует на диске.
        ///
        /// Существующий НЕ трогаем ни при каких условиях: пересборка сцены
        /// не должна стирать настроенное руками. Новые поля Unity добавит
        /// сама при следующем импорте, старые значения при этом сохранятся.
        /// </summary>
        /// <summary>
        /// Заводит конфиг забега, если его ещё нет.
        ///
        /// Создаём КОПИЕЙ общего, а не пустышкой: свет, камера, поведение
        /// зомби и прочая настройка уже подобраны, и начинать забег
        /// с умолчаний кода значило бы подбирать всё заново.
        ///
        /// Существующий не трогаем никогда. Пересборка сцены не должна
        /// стирать баланс, который в него уже вложен.
        /// </summary>
        static ArenaConfig EnsureRunConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ArenaConfig>(RunConfigPath);
            if (existing != null) return existing;

            var source = LoadOrCreateConfig();

            var copy = ScriptableObject.CreateInstance<ArenaConfig>();
            EditorUtility.CopySerialized(source, copy);

            // Отличия забега от стенда — здесь и только здесь.
            // Дальше конфиг живёт своей жизнью, и правят его в инспекторе.
            copy.squadComposition = StartingSquad(source.squadComposition);

            // Отладочное с собой не тащим: забег — это игра, а не замер.
            copy.debugSweep = false;
            copy.debugSquadInvincible = false;
            copy.showFrameMeter = false;

            EnsureFolder("Assets/_Project/Configs");
            AssetDatabase.CreateAsset(copy, RunConfigPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Забег] Создан конфиг {RunConfigPath} копией общего");
            return AssetDatabase.LoadAssetAtPath<ArenaConfig>(RunConfigPath);
        }

        /// <summary>
        /// Отряд, с которым выходят на забег: по паре бойцов каждой роли.
        ///
        /// Мало и намеренно: смысл забега в том, что отряд РАСТЁТ на тир-апах.
        /// Выйти полным составом значит отдать игроку результат до того,
        /// как он принял хоть одно решение.
        /// </summary>
        static SquadEntry[] StartingSquad(SquadEntry[] source)
        {
            var start = new System.Collections.Generic.List<SquadEntry>();
            if (source == null) return start.ToArray();

            foreach (var entry in source)
            {
                if (entry.Class == null) continue;
                if (start.Exists(e => e.Class == entry.Class)) continue;

                // Ближний бой держит удар, поэтому его вдвое больше:
                // двое стрелков без прикрытия не переживут первую же волну.
                int count = entry.Class.role == SquadRole.Melee ? 4 : 2;
                start.Add(new SquadEntry { Class = entry.Class, Count = count });
            }
            return start.ToArray();
        }

        static ArenaConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<ArenaConfig>(ConfigPath);
            if (config != null) return config;

            EnsureFolder("Assets/_Project/Configs");
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ArenaConfig>(), ConfigPath);
            AssetDatabase.SaveAssets();

            // Перечитываем с диска, а не используем созданный инстанс:
            // ссылка на ещё не записанный ассет сериализуется в сцену как
            // fileID 0, и компоненты стартуют с пустым конфигом.
            config = AssetDatabase.LoadAssetAtPath<ArenaConfig>(ConfigPath);
            Debug.Log($"[TestArena] Создан конфиг {ConfigPath}");
            return config;
        }

        // --- префабы -------------------------------------------------------

        static void PrepareSurvivorPrefab(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning($"[TestArena] Нет префаба {path}. " +
                                 "Сначала WarfareSurvivor/Setup/Build Character Prefabs.");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            Ensure<Health>(root);
            Ensure<TorsoAim>(root);
            Ensure<HealthBarView>(root);
            Ensure<Survivor>(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        static void PrepareZombiePrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(ZombiePrefab);
            Ensure<Health>(root);
            Ensure<Zombie>(root);
            PrefabUtility.SaveAsPrefabAsset(root, ZombiePrefab);
            PrefabUtility.UnloadPrefabContents(root);
        }

        /// <summary>
        /// Заводит ассеты классов и, если состав ещё не задан, наполняет его.
        /// Существующие ассеты не переписываем — числа классов правятся руками.
        /// </summary>
        static void EnsureClasses(ArenaConfig config)
        {
            if (config == null) return;

            var police = EnsureClass("Police", "Полицейский", SquadRole.Ranged, PolicePrefab, c =>
            {
                c.maxHealth = 100f;
                c.attackRange = 9f;
                c.attackInterval = 0.9f;
                c.damage = 10f;
                c.knockbackDistance = 0f;
            });

            var farmer = EnsureClass("ShovelFarmer", "Фермер с лопатой", SquadRole.Melee, FarmerPrefab, c =>
            {
                c.maxHealth = 150f;
                // Заметно больше дистанции касания зомби (1.1 м): лопатой
                // достают с замахом, и удар должен успевать РАНЬШЕ, чем враг
                // дотянется до тела. Иначе ближний бой всегда получает первым.
                c.attackRange = 2.4f;
                c.attackInterval = 1.1f;
                c.damage = 25f;
                c.knockbackDistance = 2.5f;
                c.knockbackDuration = 0.22f;
            });

            if (config.squadComposition != null && config.squadComposition.Length > 0) return;

            config.squadComposition = new[]
            {
                new SquadEntry { Class = police, Count = 6 },
                new SquadEntry { Class = farmer, Count = 4 },
            };
            EditorUtility.SetDirty(config);
            Debug.Log("[TestArena] Состав отряда заполнен по умолчанию: 6 стрелков + 4 лопаты");
        }

        static SurvivorClassSO EnsureClass(string fileName, string display, SquadRole role,
            string prefabPath, System.Action<SurvivorClassSO> defaults)
        {
            var path = $"{ClassesDir}/{fileName}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<SurvivorClassSO>(path);
            if (existing != null) return existing;

            EnsureFolder(ClassesDir);
            var klass = ScriptableObject.CreateInstance<SurvivorClassSO>();
            klass.displayName = display;
            klass.role = role;
            klass.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            defaults(klass);

            AssetDatabase.CreateAsset(klass, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TestArena] Создан класс {path}");
            return AssetDatabase.LoadAssetAtPath<SurvivorClassSO>(path);
        }

        static T Ensure<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        // --- сцена ---------------------------------------------------------

        static void CreateGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            // План 10×10 м на единицу масштаба; 12 даёт 120 м — отряд не
            // добежит до края за тестовый прогон.
            ground.transform.localScale = Vector3.one * 12f;

            var material = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterial);
            if (material != null) ground.GetComponent<MeshRenderer>().sharedMaterial = material;

            LayerUtility.Apply(ground, LayerUtility.Ground);
        }

        /// <summary>
        /// Разбрасывает руины разного размера и разворота.
        ///
        /// Зерно фиксировано: пересборка сцены должна давать ТУ ЖЕ расстановку,
        /// иначе после каждой правки кода поле выглядит по-новому и сравнить
        /// два прогона между собой невозможно.
        /// </summary>
        static void CreateRuins(ArenaConfig config)
        {
            if (config == null || config.ruinCount <= 0) return;

            var model = EnsureRuinPrefab();
            if (model == null) return;

            var footprint = MeasureFootprint(model);
            var root = new GameObject("Environment").transform;

            Random.InitState(config.ruinSeed);

            var placed = new List<Vector3>();
            var radii = new List<float>();
            int attempts = 0;

            while (placed.Count < config.ruinCount && attempts < config.ruinCount * 40)
            {
                attempts++;

                float scale = Random.Range(config.ruinScaleMin, config.ruinScaleMax);
                float radius = footprint.Radius * scale;

                var flat = Random.insideUnitCircle * config.ruinAreaRadius;
                var position = new Vector3(flat.x, 0f, flat.y);

                // Старт отряда держим свободным: забег не должен начинаться
                // внутри стены.
                if (position.magnitude < config.ruinKeepClearRadius + radius) continue;

                bool overlaps = false;
                for (int i = 0; i < placed.Count; i++)
                {
                    if (Vector3.Distance(placed[i], position) >= radii[i] + radius) continue;
                    overlaps = true;
                    break;
                }
                if (overlaps) continue;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
                instance.name = $"Ruin_{placed.Count:00}";
                instance.transform.position = position + Vector3.up * (footprint.BaseOffset * scale);
                instance.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                instance.transform.localScale = Vector3.one * scale;
                instance.AddComponent<Obstacle>();
                LayerUtility.Apply(instance, LayerUtility.Environment);

                placed.Add(position);
                radii.Add(radius);
            }

            Debug.Log($"[TestArena] Расставлено руин: {placed.Count} из {config.ruinCount}");
        }

        /// <summary>
        /// Префаб-обёртка над моделью руин с поправкой осей.
        ///
        /// Модель авторена в Z-up: в файле объявлен Y-up, поэтому штатный
        /// Bake Axis Conversion при импорте ничего не делает — конвертировать
        /// ему нечего, — а геометрия при этом лежит на боку. Поправку кладём
        /// в префаб, а не в константу генератора: так она видна в проекте,
        /// правится руками и не размножается по коду с каждой новой моделью.
        ///
        /// Создаётся один раз. Настроенный руками префаб не переписываем.
        /// </summary>
        static GameObject EnsureRuinPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(RuinPrefab);
            if (existing != null) return existing;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(RuinModel);
            if (model == null)
            {
                Debug.LogWarning($"[TestArena] Не найдена модель руин {RuinModel}");
                return null;
            }

            var root = new GameObject("Ruin");
            var child = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            EnsureFolder("Assets/_Project/Prefabs/Environment");
            var saved = PrefabUtility.SaveAsPrefabAsset(root, RuinPrefab);
            Object.DestroyImmediate(root);

            Debug.Log($"[TestArena] Создан префаб руин {RuinPrefab} с поправкой осей (-90° по X)");
            return saved;
        }

        /// <summary>Габарит модели: радиус для разведения и подъём, чтобы основание легло на землю.</summary>
        static (float Radius, float BaseOffset) MeasureFootprint(GameObject model)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            var renderers = instance.GetComponentsInChildren<Renderer>();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            Object.DestroyImmediate(instance);

            // Радиус — по описанной окружности следа: при случайном повороте
            // угол коробки может оказаться где угодно, и разводить надо по нему.
            float radius = new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
            return (radius, -bounds.min.y);
        }

        static void CreateLight(ArenaConfig config)
        {
            var go = new GameObject("Directional Light");

            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;

            var rig = go.AddComponent<LightRig>();
            Wire(rig, nameof(config), config);

            // Применяем сразу, чтобы сохранённая сцена не расходилась
            // с конфигом до первого запуска.
            LightRig.Apply(config, light);
        }

        static SquadController CreateSquad(ArenaConfig config)
        {
            var go = new GameObject("Squad");
            return go.AddComponent<SquadController>();
        }

        static GameObject CreateCamera(Transform target)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";

            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.17f, 0.2f);

            var follow = go.AddComponent<IsoCameraFollow>();
            Wire(follow, "target", target);
            Wire(follow, "config", AssetDatabase.LoadAssetAtPath<ArenaConfig>(ConfigPath));
            return go;
        }

        static VirtualJoystick CreateUI()
        {
            var canvasGo = new GameObject("UI",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            CreateEventSystem();

            // Зона ввода — весь экран: на телефоне палец ставят где придётся,
            // и промах по маленькой области читается как «управление не работает».
            var zone = new GameObject("JoystickZone", typeof(Image), typeof(VirtualJoystick));
            var zoneRect = (RectTransform)zone.transform;
            zoneRect.SetParent(canvasGo.transform, false);
            Stretch(zoneRect);

            var zoneImage = zone.GetComponent<Image>();
            zoneImage.color = new Color(0f, 0f, 0f, 0f);
            zoneImage.raycastTarget = true;

            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            var background = CreateCircle("Base", zoneRect, knob, 260f, new Color(1f, 1f, 1f, 0.22f));
            background.anchorMin = background.anchorMax = new Vector2(0f, 0f);
            background.pivot = new Vector2(0.5f, 0.5f);
            background.anchoredPosition = new Vector2(280f, 300f);

            var handle = CreateCircle("Handle", background, knob, 120f, new Color(1f, 1f, 1f, 0.65f));
            handle.anchoredPosition = Vector2.zero;

            var joystick = zone.GetComponent<VirtualJoystick>();
            Wire(joystick, "background", background);
            Wire(joystick, "handle", handle);
            return joystick;
        }

        /// <summary>
        /// Счётчик кадра поверх игры. Живёт на том же канвасе, что джойстик.
        /// </summary>
        /// <summary>Крупная подпись сверху: что сейчас проверяется.</summary>
        static Text CreateSweepBanner()
        {
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null) return null;

            var go = new GameObject("SweepBanner", typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -300f);
            rect.sizeDelta = new Vector2(-40f, 160f);

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 44;
            text.alignment = TextAnchor.UpperCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        static void CreateFrameMeter(ArenaConfig config)
        {
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null) return;

            var go = new GameObject("FrameMeter", typeof(Text), typeof(FrameMeter));
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvas.transform, false);

            // Левый верхний угол: там ничего не происходит, а джойстик внизу.
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -24f);
            rect.sizeDelta = new Vector2(560f, 260f);

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 34;
            text.color = Color.white;
            text.raycastTarget = false;   // не должен перехватывать касания у джойстика
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            // Тень: белые цифры на светлом песке иначе не читаются.
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
            shadow.effectDistance = new Vector2(2f, -2f);

            var meter = go.GetComponent<FrameMeter>();
            Wire(meter, nameof(config), config);
            Wire(meter, "label", text);
        }

        /// <summary>
        /// Собирает всё, чем забег отличается от арены: искры, ведущий
        /// забега, окно тир-апа и панель состояния.
        /// </summary>
        static void CreateRun(ArenaConfig config, SquadController squad, ZombieSpawner spawner)
        {
            var canvas = Object.FindFirstObjectByType<Canvas>();

            var sparks = new GameObject("SparkField").AddComponent<SparkField>();
            Wire(sparks, nameof(config), config);
            Wire(sparks, "squad", squad);
            Wire(sparks, "spawner", spawner);
            Wire(sparks, "sparkMaterial", EnsureSparkMaterial());
            Wire(sparks, "view", Object.FindFirstObjectByType<Camera>());

            var panel = CreateTierUpPanel(canvas, config);
            var hud = CreateRunHud(canvas);

            var run = new GameObject("RunController").AddComponent<RunController>();
            Wire(run, nameof(config), config);
            Wire(run, "squad", squad);
            Wire(run, "spawner", spawner);
            Wire(run, "sparks", sparks);
            Wire(run, "tierUp", panel);

            Wire(hud, "run", run);
            Wire(hud, "squad", squad);
        }

        const string BottleTexture = "Assets/Sprites/BottleOfWater.png";

        /// <summary>
        /// Материал ресурса: картинка бутылки плюс свечение вокруг неё.
        /// Освещение не считается — предмет должен читаться одинаково
        /// и на солнце, и в тени руин.
        ///
        /// Шейдер и текстуру проставляем при каждой сборке, а подобранные
        /// цвет и сила свечения остаются: их правят в инспекторе, и стирать
        /// подобранное пересборкой нельзя.
        /// </summary>
        static Material EnsureSparkMaterial()
        {
            const string path = "Assets/_Project/Art/Materials/Spark.mat";

            var shader = Shader.Find("WarfareSurvivor/Pickup");
            if (shader == null)
            {
                Debug.LogError("[Забег] Не найден шейдер WarfareSurvivor/Pickup");
                return null;
            }

            PrepareBottleTexture();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BottleTexture);

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = "Spark" };
                EnsureFolder("Assets/_Project/Art/Materials");
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.shader != shader) material.shader = shader;
            if (texture != null) material.SetTexture("_BaseMap", texture);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Картинка приходит в 1024x1536 — полтора мегабайта на предмет
        /// размером с ладонь. На целевом устройстве 2.6 ГБ памяти, и система
        /// уже шлёт предупреждения о нехватке, так что ужимаем до 256
        /// и сжимаем. На экране предмет занимает десятки пикселей, разницы
        /// не видно.
        /// </summary>
        static void PrepareBottleTexture()
        {
            var importer = AssetImporter.GetAtPath(BottleTexture) as TextureImporter;
            if (importer == null) return;

            bool dirty = false;

            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                dirty = true;
            }

            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                dirty = true;
            }

            if (importer.maxTextureSize > 256)
            {
                importer.maxTextureSize = 256;
                dirty = true;
            }

            if (!dirty) return;

            importer.SaveAndReimport();
            Debug.Log("[Забег] Настройки импорта бутылки поправлены: 256 пикселей, прозрачность");
        }

        static TierUpPanel CreateTierUpPanel(Canvas canvas, ArenaConfig config)
        {
            if (canvas == null) return null;

            var go = new GameObject("TierUpPanel", typeof(RectTransform), typeof(TierUpPanel));
            go.transform.SetParent(canvas.transform, false);

            var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            var backdropRect = (RectTransform)backdrop.transform;
            backdropRect.SetParent(go.transform, false);
            Stretch(backdropRect);
            // Перехватывает касания: под окном выбора джойстик работать
            // не должен, иначе отряд уедет, пока игрок читает карточки.
            backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

            var title = CreateLabel(backdropRect, "Title", 56, TextAnchor.UpperCenter);
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -160f);
            titleRect.sizeDelta = new Vector2(-40f, 90f);

            var row = new GameObject("Cards", typeof(RectTransform),
                typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            row.SetParent(backdropRect, false);
            row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
            row.pivot = new Vector2(0.5f, 0.5f);
            row.sizeDelta = new Vector2(1000f, 440f);

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 28f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var panel = go.GetComponent<TierUpPanel>();
            Wire(panel, nameof(config), config);
            Wire(panel, "root", backdropRect);
            Wire(panel, "title", title);
            Wire(panel, "cardRow", row);
            return panel;
        }

        static RunHud CreateRunHud(Canvas canvas)
        {
            if (canvas == null) return null;

            // RectTransform добавляем ЯВНО: new GameObject даёт обычный
            // Transform, и родство с канвасом его не превращает.
            var go = new GameObject("RunHud", typeof(RectTransform), typeof(RunHud));
            go.transform.SetParent(canvas.transform, false);
            var parent = (RectTransform)go.transform;
            Stretch(parent);

            var wave = CreateLabel(parent, "Wave", 42, TextAnchor.UpperCenter);
            var waveRect = (RectTransform)wave.transform;
            waveRect.anchorMin = new Vector2(0f, 1f);
            waveRect.anchorMax = new Vector2(1f, 1f);
            waveRect.pivot = new Vector2(0.5f, 1f);
            waveRect.anchoredPosition = new Vector2(0f, -36f);
            waveRect.sizeDelta = new Vector2(-40f, 60f);

            var squadLabel = CreateLabel(parent, "Squad", 34, TextAnchor.UpperLeft);
            var squadRect = (RectTransform)squadLabel.transform;
            squadRect.anchorMin = squadRect.anchorMax = new Vector2(0f, 1f);
            squadRect.pivot = new Vector2(0f, 1f);
            squadRect.anchoredPosition = new Vector2(28f, -110f);
            squadRect.sizeDelta = new Vector2(700f, 50f);

            // Полоска искр — над джойстиком, во всю ширину: это единственный
            // индикатор, за которым игрок следит по ходу боя.
            var barBack = new GameObject("SparkBar", typeof(RectTransform), typeof(Image));
            var barRect = (RectTransform)barBack.transform;
            barRect.SetParent(parent, false);
            barRect.anchorMin = new Vector2(0.5f, 0f);
            barRect.anchorMax = new Vector2(0.5f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = new Vector2(0f, 46f);
            barRect.sizeDelta = new Vector2(720f, 26f);
            barBack.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var fillRect = (RectTransform)fillGo.transform;
            fillRect.SetParent(barRect, false);
            Stretch(fillRect);

            var fill = fillGo.GetComponent<Image>();
            fill.color = new Color(1f, 0.86f, 0.35f, 0.95f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            var sparkLabel = CreateLabel(barRect, "SparkCount", 26, TextAnchor.MiddleCenter);
            Stretch((RectTransform)sparkLabel.transform);

            var banner = CreateLabel(parent, "Banner", 72, TextAnchor.MiddleCenter);
            Stretch((RectTransform)banner.transform);

            var hud = go.GetComponent<RunHud>();
            Wire(hud, "waveLabel", wave);
            Wire(hud, "squadLabel", squadLabel);
            Wire(hud, "sparkFill", fill);
            Wire(hud, "sparkLabel", sparkLabel);
            Wire(hud, "banner", banner);
            return hud;
        }

        static Text CreateLabel(RectTransform parent, string name, int size, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = align;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            // Проект стоит на новом Input System; старый модуль тут просто
            // не получит событий, и джойстик молча не будет реагировать.
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        static RectTransform CreateCircle(string name, RectTransform parent, Sprite sprite, float size, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(size, size);

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // --- утилиты -------------------------------------------------------

        /// <summary>
        /// Поля компонентов приватные и с [SerializeField] — присвоить их
        /// снаружи можно только через SerializedObject.
        /// </summary>
        /// <summary>Тот же Wire, но для булевых флагов: перегрузка с Object их не берёт.</summary>
        static void Wire(Object component, string fieldName, bool value)
        {
            var field = component.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            if (field == null)
            {
                Debug.LogError($"[TestArena] Нет поля {fieldName} у {component.GetType().Name}");
                return;
            }

            field.SetValue(component, value);
            EditorUtility.SetDirty(component);
        }

        static void Wire(Object component, string fieldName, Object value)
        {
            var so = new SerializedObject(component);
            var property = so.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[TestArena] У {component.GetType().Name} нет поля \"{fieldName}\"");
                return;
            }
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
