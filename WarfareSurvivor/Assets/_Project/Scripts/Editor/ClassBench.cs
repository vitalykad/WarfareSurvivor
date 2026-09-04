using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Сборка стенда: список классов и портреты к ним.
    ///
    /// Портрет каждого класса — снимок его собственной модели, а не
    /// нарисованная картинка. Причина простая: классов уже полтора десятка,
    /// и рисовать иконку под каждый — работа на дни, а отличать их на стенде
    /// нужно сегодня. Модель к тому же не врёт: на снимке ровно тот боец,
    /// который встанет в строй, вместе со своим оружием.
    /// </summary>
    public static class ClassBench
    {
        const string IconFolder = "Assets/_Project/Art/ClassIcons";
        const int IconSize = 512;

        [MenuItem("WarfareSurvivor/Стенд/Собрать все классы")]
        public static void CollectClasses()
        {
            var bench = FindBench();
            if (bench == null) return;

            var found = new List<SurvivorClassSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:SurvivorClassSO"))
            {
                var klass = AssetDatabase.LoadAssetAtPath<SurvivorClassSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (klass != null && klass.prefab != null && !bench.Hidden(klass)) found.Add(klass);
            }

            // По ролям, внутри роли — по имени: стенд раскладывает карточки
            // в том же порядке, и лазить глазами по случайному списку
            // не приходится.
            found.Sort((a, b) => a.role != b.role
                ? a.role.CompareTo(b.role)
                : string.Compare(a.displayName, b.displayName, System.StringComparison.CurrentCulture));

            bench.classes = found.ToArray();
            EditorUtility.SetDirty(bench);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Стенд] Классов собрано: {found.Count}.");
        }

        [MenuItem("WarfareSurvivor/Стенд/Испечь иконки классов")]
        public static void BakeIcons()
        {
            var classes = new List<SurvivorClassSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:SurvivorClassSO"))
            {
                var klass = AssetDatabase.LoadAssetAtPath<SurvivorClassSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (klass != null && klass.prefab != null) classes.Add(klass);
            }

            Directory.CreateDirectory(IconFolder);
            int baked = 0;

            foreach (var klass in classes)
            {
                var texture = Snapshot(klass.prefab);
                if (texture == null)
                {
                    Debug.LogWarning($"[Стенд] Не удалось снять {klass.displayName}.", klass);
                    continue;
                }

                string path = $"{IconFolder}/{klass.name}.png";
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                AsSprite(path);

                klass.icon = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                EditorUtility.SetDirty(klass);
                baked++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Стенд] Иконок испечено: {baked} из {classes.Count}. Папка: {IconFolder}");
        }

        /// <summary>
        /// Снимок модели: три четверти спереди, в рост.
        ///
        /// Кадр строится по габаритам конкретного бойца, а не по общему
        /// числу: копейщик с трёхметровым древком и медик без оружия
        /// в один и тот же кадр не влезают, а нам нужны обе фигуры целиком.
        /// </summary>
        static Texture2D Snapshot(GameObject prefab)
        {
            var preview = new PreviewRenderUtility();
            try
            {
                var instance = Object.Instantiate(prefab);
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                Pose(instance);
                preview.AddSingleGO(instance);

                var bounds = Measure(instance);
                if (bounds.size == Vector3.zero) { Object.DestroyImmediate(instance); return null; }

                // Смотрим чуть сверху и сбоку: строго анфас теряет объём,
                // а сильный поворот прячет оружие за спину.
                var direction = new Vector3(0.55f, 0.35f, 1f).normalized;
                float radius = bounds.extents.magnitude;
                float distance = radius * 2.6f;

                var camera = preview.camera;
                camera.transform.position = bounds.center + direction * distance;
                camera.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = distance * 4f;
                camera.fieldOfView = 32f;
                camera.clearFlags = CameraClearFlags.SolidColor;

                // Фон под цвет карточки, а не прозрачный: альфа из превью
                // приходит непредсказуемой, и на карточке это выглядит
                // как грязный прямоугольник вокруг фигуры.
                camera.backgroundColor = new Color(0.12f, 0.13f, 0.17f, 1f);

                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35f, 140f, 0f);
                preview.lights[1].intensity = 0.7f;
                preview.lights[1].transform.rotation = Quaternion.Euler(20f, -60f, 0f);
                preview.ambientColor = new Color(0.35f, 0.36f, 0.40f);

                preview.BeginStaticPreview(new Rect(0f, 0f, IconSize, IconSize));
                camera.Render();
                var result = preview.EndStaticPreview();

                Object.DestroyImmediate(instance);
                return result;
            }
            finally
            {
                preview.Cleanup();
            }
        }

        /// <summary>
        /// Ставит бойца в стойку покоя.
        ///
        /// Без этого снимок выходит в бинд-позе — руки в стороны, как
        /// у распятого. Это не только некрасиво: широко расставленные руки
        /// раздувают габариты, и фигура на карточке получается вдвое мельче,
        /// чем могла бы.
        /// </summary>
        static void Pose(GameObject instance)
        {
            var animator = instance.GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null) return;

            AnimationClip idle = null;
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip == null) continue;
                if (idle == null) idle = clip;
                if (clip.name.ToLower().Contains("idle")) { idle = clip; break; }
            }
            if (idle == null) return;

            // Не с нулевого кадра: в начале клипа фигура нередко ещё
            // доезжает из бинд-позы.
            idle.SampleAnimation(instance, idle.length * 0.4f);
        }

        /// <summary>Габариты по всем мешам, включая оружие в руках.</summary>
        static Bounds Measure(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static void AsSprite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        static BenchConfig FindBench()
        {
            var guids = AssetDatabase.FindAssets("t:BenchConfig");
            if (guids.Length == 0)
            {
                Debug.LogError("[Стенд] Не найден BenchConfig. Создай его: " +
                               "Create → WarfareSurvivor → Bench Config.");
                return null;
            }
            return AssetDatabase.LoadAssetAtPath<BenchConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
