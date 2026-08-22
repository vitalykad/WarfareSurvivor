using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace WarfareSurvivor.EditorTools
{
    /// <summary>
    /// Собирает из FBX готовые к работе префабы: аватар + контроллер + Animator.
    ///
    /// Создаёт только то, чего ещё нет. Перезаписать настроенное руками может
    /// только пункт «Rebuild» — см. §8 project.md, генератор не должен затирать
    /// ручную настройку молча.
    /// </summary>
    public static class CharacterSetupBuilder
    {
        const string AnimatorsDir = "Assets/_Project/Animators";
        const string SurvivorsDir = "Assets/_Project/Prefabs/Survivors";
        const string MonstersDir = "Assets/_Project/Prefabs/Monsters";

        const string FarmerModel = "Assets/Models/Characters/ShovelFarmer/ShovelFarmer.fbx";
        const string PoliceModel = "Assets/Models/police+officer+3d+model/police+officer+3d+model.fbx";
        const string ZombieModel = "Assets/Models/zombie+3d+model/zombie+3d+model.fbx";

        const string PoliceController = AnimatorsDir + "/Survivor_Police.controller";
        const string ZombieController = AnimatorsDir + "/Monster_Zombie.controller";
        const string PolicePrefab = SurvivorsDir + "/Survivor_Police.prefab";
        const string FarmerController = AnimatorsDir + "/Survivor_ShovelFarmer.controller";
        const string FarmerPrefab = SurvivorsDir + "/Survivor_ShovelFarmer.prefab";
        const string UpperBodyMask = AnimatorsDir + "/UpperBody.mask";

        /// <summary>Имя слоя удара. Его же ищет Survivor, чтобы поднимать вес.</summary>
        public const string UpperBodyLayer = "UpperBody";
        const string ZombiePrefab = MonstersDir + "/Monster_Zombie.prefab";

        // Скорость, ниже которой боец считается стоящим. Относительная величина:
        // сравнивается с параметром Speed, который код нормализует к 0..1.
        const float MoveThreshold = 0.1f;
        const float BlendDuration = 0.15f;

        [MenuItem("WarfareSurvivor/Setup/Build Character Prefabs")]
        public static void Build() => Run(force: false);

        [MenuItem("WarfareSurvivor/Setup/Rebuild Character Prefabs (overwrite)")]
        public static void Rebuild()
        {
            if (!EditorUtility.DisplayDialog(
                    "Rebuild character prefabs",
                    "Контроллеры и префабы персонажей будут перезаписаны кодом. " +
                    "Ручная настройка на них пропадёт. Продолжить?",
                    "Перезаписать", "Отмена"))
                return;
            Run(force: true);
        }

        [InitializeOnLoadMethod]
        static void BuildOnceIfMissing()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PolicePrefab) != null &&
                    AssetDatabase.LoadAssetAtPath<GameObject>(FarmerPrefab) != null &&
                    AssetDatabase.LoadAssetAtPath<GameObject>(ZombiePrefab) != null)
                    return;
                Run(force: false);
            };
        }

        static void Run(bool force)
        {
            EnsureFolder(AnimatorsDir);
            EnsureFolder(SurvivorsDir);
            EnsureFolder(MonstersDir);

            var clips = LoadClips();
            if (clips.Count == 0)
            {
                Debug.LogWarning("[CharacterSetup] В Assets/Animations не нашлось ни одного клипа. " +
                                 "Дождись импорта FBX и запусти WarfareSurvivor/Setup ещё раз.");
                return;
            }

            BuildSurvivor(clips, force);
            BuildFarmer(clips, force);
            BuildZombie(clips, force);

            // Правим уже созданные контроллеры на месте, а не пересоздаём:
            // пересоздание меняет GUID, и префабы теряют ссылку на контроллер.
            EnsureReverseRun(PoliceController);
            EnsureReverseRun(FarmerController);
            EnsureUpperBodyAttack(FarmerController, clips);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void BuildSurvivor(Dictionary<string, AnimationClip> clips, bool force)
        {
            var idle = Find(clips, "Pistol Idle");
            var run = Find(clips, "Pistol Run");
            if (idle == null || run == null) return;

            if (force || AssetDatabase.LoadAssetAtPath<AnimatorController>(PoliceController) == null)
            {
                var ac = AnimatorController.CreateAnimatorControllerAtPath(PoliceController);
                ac.AddParameter("Speed", AnimatorControllerParameterType.Float);

                var sm = ac.layers[0].stateMachine;
                var idleState = sm.AddState("Idle");
                idleState.motion = idle;
                var runState = sm.AddState("Run");
                runState.motion = run;
                sm.defaultState = idleState;

                Link(idleState, runState, AnimatorConditionMode.Greater, MoveThreshold);
                Link(runState, idleState, AnimatorConditionMode.Less, MoveThreshold);

                Debug.Log($"[CharacterSetup] Создан контроллер {PoliceController}");
            }

            BuildPrefab(PoliceModel, PoliceController, PolicePrefab, force);
        }

        static void BuildFarmer(Dictionary<string, AnimationClip> clips, bool force)
        {
            var idle = Find(clips, "Idle");
            var run = Find(clips, "Run");
            var attack = Find(clips, "Attack1");
            if (idle == null || run == null || attack == null) return;

            if (force || AssetDatabase.LoadAssetAtPath<AnimatorController>(FarmerController) == null)
            {
                var ac = AnimatorController.CreateAnimatorControllerAtPath(FarmerController);
                ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
                ac.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

                var sm = ac.layers[0].stateMachine;
                var idleState = sm.AddState("Idle");
                idleState.motion = idle;
                var runState = sm.AddState("Run");
                runState.motion = run;
                sm.defaultState = idleState;

                Link(idleState, runState, AnimatorConditionMode.Greater, MoveThreshold);
                Link(runState, idleState, AnimatorConditionMode.Less, MoveThreshold);

                Debug.Log($"[CharacterSetup] Создан контроллер {FarmerController}");
            }

            BuildPrefab(FarmerModel, FarmerController, FarmerPrefab, force);
        }

        static void BuildZombie(Dictionary<string, AnimationClip> clips, bool force)
        {
            var run = Find(clips, "Zombie Running");
            var dying = Find(clips, "Zombie Dying");
            if (run == null || dying == null) return;

            if (force || AssetDatabase.LoadAssetAtPath<AnimatorController>(ZombieController) == null)
            {
                var ac = AnimatorController.CreateAnimatorControllerAtPath(ZombieController);
                ac.AddParameter("Die", AnimatorControllerParameterType.Trigger);

                var sm = ac.layers[0].stateMachine;
                // Idle у зомби нет и не нужен: он всегда бежит на отряд.
                var runState = sm.AddState("Run");
                runState.motion = run;
                var dieState = sm.AddState("Die");
                dieState.motion = dying;
                sm.defaultState = runState;

                // Через AnyState, а не из Run: умереть можно в любом состоянии,
                // включая те, что появятся позже (удар, оглушение).
                var toDie = sm.AddAnyStateTransition(dieState);
                toDie.AddCondition(AnimatorConditionMode.If, 0f, "Die");
                toDie.hasExitTime = false;
                toDie.duration = 0.1f;
                toDie.canTransitionToSelf = false;

                Debug.Log($"[CharacterSetup] Создан контроллер {ZombieController}");
            }

            BuildPrefab(ZombieModel, ZombieController, ZombiePrefab, force);
        }

        /// <summary>
        /// Даёт состоянию Run множитель скорости из параметра MoveDir.
        /// Отрицательное значение проигрывает клип бега назад — так боец
        /// отбегает спиной, оставаясь лицом к врагу, и ноги идут в ту сторону,
        /// в которую он на самом деле движется. Отдельный клип для этого
        /// не нужен.
        /// </summary>
        /// <summary>
        /// Выносит удар на отдельный слой с маской верха тела.
        ///
        /// Смысл: на бегу ноги должны продолжать бежать, а верх — замахиваться.
        /// Одним слоем это невыразимо: состояние Attack на базовом слое
        /// перебивает и ноги, и боец бьёт стоя посреди бега.
        ///
        /// Вес слоя поднимает КОД, а не сам контроллер. Пустое состояние
        /// на override-слое с ненулевым весом выводит верх тела в бинд-позу,
        /// поэтому вес держится нулевым везде, кроме самого удара
        /// (см. Survivor.UpdateAttackLayer).
        /// </summary>
        static void EnsureUpperBodyAttack(string controllerPath, Dictionary<string, AnimationClip> clips)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return;

            var attack = Find(clips, "Attack1");
            if (attack == null) return;

            if (!controller.parameters.Any(p => p.name == "Attack"))
                controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

            if (!controller.parameters.Any(p => p.name == "AttackSpeed"))
                controller.AddParameter("AttackSpeed", AnimatorControllerParameterType.Float);

            // Со старого базового слоя удар убираем: он там всё ломает.
            var baseMachine = controller.layers[0].stateMachine;
            foreach (var child in baseMachine.states)
            {
                if (child.state.name != "Attack") continue;
                baseMachine.RemoveState(child.state);
                break;
            }

            int index = System.Array.FindIndex(controller.layers, l => l.name == UpperBodyLayer);
            if (index < 0)
            {
                controller.AddLayer(UpperBodyLayer);
                index = controller.layers.Length - 1;
            }

            var layers = controller.layers;
            layers[index].avatarMask = EnsureUpperBodyMask();
            layers[index].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[index].defaultWeight = 0f;
            controller.layers = layers;

            var machine = controller.layers[index].stateMachine;
            foreach (var child in machine.states) machine.RemoveState(child.state);

            // Пустое состояние — «удара нет». Видно его не будет: вес слоя
            // к этому моменту уже сведён к нулю.
            var idle = machine.AddState("NoAttack");
            var attackState = machine.AddState("Attack");
            attackState.motion = attack;
            // Темп удара задаётся в классе, а замах длится сколько длится клип.
            // Без этого множителя интервал короче клипа ничего не ускоряет:
            // следующий удар просто ждёт, пока доиграет предыдущий.
            attackState.speedParameterActive = true;
            attackState.speedParameter = "AttackSpeed";
            machine.defaultState = idle;

            var toAttack = machine.AddAnyStateTransition(attackState);
            toAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            toAttack.hasExitTime = false;
            toAttack.duration = 0.04f;
            toAttack.canTransitionToSelf = false;

            // Клип удара не зациклен (см. CharacterImportSettings), поэтому
            // выход по окончании отрабатывает.
            var fromAttack = attackState.AddTransition(idle);
            fromAttack.hasExitTime = true;
            fromAttack.exitTime = 0.98f;
            fromAttack.duration = 0f;

            EditorUtility.SetDirty(controller);
            Debug.Log($"[CharacterSetup] Слой {UpperBodyLayer} настроен в {controllerPath}");
        }

        /// <summary>Маска верха тела: корпус, голова, руки. Ноги и корень — базовому слою.</summary>
        static AvatarMask EnsureUpperBodyMask()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMask);
            if (existing != null) return existing;

            var mask = new AvatarMask { name = "UpperBody" };

            var all = new[]
            {
                AvatarMaskBodyPart.Root, AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head,
                AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg,
                AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers,
                AvatarMaskBodyPart.LeftFootIK, AvatarMaskBodyPart.RightFootIK,
                AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK
            };
            foreach (var part in all) mask.SetHumanoidBodyPartActive(part, false);

            foreach (var part in new[]
            {
                AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head,
                AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers
            })
                mask.SetHumanoidBodyPartActive(part, true);

            AssetDatabase.CreateAsset(mask, UpperBodyMask);
            Debug.Log($"[CharacterSetup] Создана маска {UpperBodyMask}");
            return AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMask);
        }

        static void EnsureReverseRun(string controllerPath)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return;

            if (!controller.parameters.Any(p => p.name == "MoveDir"))
                controller.AddParameter("MoveDir", AnimatorControllerParameterType.Float);

            foreach (var child in controller.layers[0].stateMachine.states)
            {
                if (child.state.name != "Run") continue;
                child.state.speedParameterActive = true;
                child.state.speedParameter = "MoveDir";
            }

            EditorUtility.SetDirty(controller);
        }

        static void Link(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, float threshold)
        {
            var t = from.AddTransition(to);
            t.AddCondition(mode, threshold, "Speed");
            t.hasExitTime = false;
            t.duration = BlendDuration;
        }

        static void BuildPrefab(string modelPath, string controllerPath, string prefabPath, bool force)
        {
            if (!force && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) return;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogWarning($"[CharacterSetup] Не найдена модель {modelPath}");
                return;
            }

            var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
            ReportAvatar(modelPath, avatar);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (avatar != null) animator.avatar = avatar;
            // Движение задаёт код, а не клип: см. запекание root motion в
            // CharacterImportSettings.
            animator.applyRootMotion = false;
            // Толпа большая — обсчитывать скелет за экраном незачем.
            animator.cullingMode = AnimatorCullingMode.CullCompletely;

            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            Object.DestroyImmediate(instance);
            Debug.Log($"[CharacterSetup] Создан префаб {prefabPath}");
        }

        /// <summary>
        /// Риг у моделей нестандартный (Tripo), и авто-маппинг Unity может не
        /// разложить его по костям Humanoid. Молча это выглядит как «анимация
        /// не проигрывается», поэтому проверяем и говорим прямо.
        /// </summary>
        static void ReportAvatar(string modelPath, Avatar avatar)
        {
            if (avatar == null)
            {
                Debug.LogError($"[CharacterSetup] {modelPath}: аватар не создан. " +
                               "Rig -> Animation Type должен быть Humanoid.");
                return;
            }
            if (!avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError($"[CharacterSetup] {modelPath}: аватар невалиден " +
                               $"(isValid={avatar.isValid}, isHuman={avatar.isHuman}). " +
                               "Открой Rig -> Configure и домапь кости руками — " +
                               "без валидного аватара mixamo-клипы не ретаргетятся.");
                return;
            }
            Debug.Log($"[CharacterSetup] {modelPath}: аватар валиден.");
        }

        static Dictionary<string, AnimationClip> LoadClips()
        {
            var result = new Dictionary<string, AnimationClip>();
            // Ищем по FBX, а не по t:AnimationClip: клип лежит внутри модели
            // саб-ассетом, и поиск по типу его не всегда видит.
            foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Animations" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    if (clip.name.StartsWith("__preview__")) continue;
                    result[clip.name] = clip;
                }
            }
            return result;
        }

        static AnimationClip Find(Dictionary<string, AnimationClip> clips, string name)
        {
            if (clips.TryGetValue(name, out var clip)) return clip;
            Debug.LogWarning($"[CharacterSetup] Не найден клип \"{name}\". Есть: {string.Join(", ", clips.Keys)}");
            return null;
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
