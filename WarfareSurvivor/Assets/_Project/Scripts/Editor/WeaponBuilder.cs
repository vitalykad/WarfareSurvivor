using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WarfareSurvivor.EditorTools
{
    /// <summary>
    /// Готовит оружие к ношению в толпе и вешает его на кость руки.
    ///
    /// Исходные модели из набора собраны из отдельных деталей — у револьвера
    /// это барабан, шесть патронов, курок и спусковой крючок, одиннадцать
    /// рендереров. По отдельности они нужны для анимации оружия крупным
    /// планом; у нас камера в тридцати метрах, деталей не видно, а в отряде
    /// из сорока стрелков это четыреста сорок рендереров на пустом месте.
    /// Поэтому детали склеиваются в один меш — материал у них общий, терять
    /// нечего.
    /// </summary>
    public static class WeaponBuilder
    {
        const string WeaponsDir = "Assets/_Project/Prefabs/Weapons";
        const string MeshesDir = "Assets/_Project/Art/Weapons";

        const string SpadeSource = "Assets/Models/Weapons/SM_Wep_Spade_01 1.prefab";
        const string RevolverSource = "Assets/Models/Weapons/SM_Wep_Revolver_01 1.prefab";

        public const string SpadePrefab = WeaponsDir + "/Weapon_Spade.prefab";
        public const string RevolverPrefab = WeaponsDir + "/Weapon_Revolver.prefab";

        /// <summary>Имя точки вылета трассы. По нему её ищет боец.</summary>
        public const string MuzzleName = "Muzzle";

        [MenuItem("WarfareSurvivor/Setup/Build Weapons")]
        public static void Build()
        {
            EnsureWeapon(SpadeSource, SpadePrefab, muzzle: false);
            EnsureWeapon(RevolverSource, RevolverPrefab, muzzle: true);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Собранное оружие. Существующее не переписываем — его настраивают руками.</summary>
        public static GameObject EnsureWeapon(string sourcePath, string outputPath, bool muzzle)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath);
            if (existing != null) return existing;

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                Debug.LogWarning($"[Weapons] Не найдена модель {sourcePath}");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            var mesh = CombineMeshes(instance, out var material);
            var bounds = mesh.bounds;
            Object.DestroyImmediate(instance);

            if (mesh == null) return null;

            EnsureFolder(MeshesDir);
            var meshPath = $"{MeshesDir}/{System.IO.Path.GetFileNameWithoutExtension(outputPath)}.mesh";
            AssetDatabase.CreateAsset(mesh, meshPath);

            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(outputPath));
            root.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            // Оружие мелкое и в гуще толпы: собственная тень от него не читается,
            // а стоит столько же, сколько тень персонажа.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (muzzle)
            {
                // Дуло — дальний конец габарита по оси ствола. Точное место
                // подгоняется руками на префабе; здесь важно не угадать
                // до миллиметра, а не оставить трассу торчать из кулака.
                var muzzlePoint = new GameObject(MuzzleName).transform;
                muzzlePoint.SetParent(root.transform, false);
                muzzlePoint.localPosition = new Vector3(0f, bounds.center.y, bounds.max.z);
            }

            EnsureFolder(WeaponsDir);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, outputPath);
            Object.DestroyImmediate(root);

            Debug.Log($"[Weapons] Собрано {outputPath}: рендерер один вместо " +
                      $"{source.GetComponentsInChildren<Renderer>().Length}");
            return saved;
        }

        static Mesh CombineMeshes(GameObject root, out Material material)
        {
            material = null;

            var filters = root.GetComponentsInChildren<MeshFilter>();
            var combines = new List<CombineInstance>();

            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;

                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled) continue;
                if (material == null) material = renderer.sharedMaterial;

                combines.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    // Матрица относительно корня оружия, чтобы детали легли
                    // туда же, где стояли.
                    transform = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix
                });
            }

            if (combines.Count == 0) return null;

            var mesh = new Mesh { name = root.name };
            mesh.CombineMeshes(combines.ToArray(), true, true);
            mesh.RecalculateBounds();
            return mesh;
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
