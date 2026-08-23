using UnityEditor;
using UnityEngine;

namespace WarfareSurvivor.EditorTools
{
    /// <summary>
    /// Заводит слои, по которым можно гасить отрисовку целыми группами.
    ///
    /// Маска камеры — самый честный способ померить, сколько стоит группа
    /// объектов: рисование выключается, а вся игровая логика продолжает
    /// считаться. Выключив вместо этого компоненты, мы померили бы заодно
    /// и отсутствие их работы, то есть не то.
    /// </summary>
    public static class LayerSetup
    {
        public const string Survivors = "Survivors";
        public const string Zombies = "Zombies";
        public const string Environment = "Environment";
        public const string Ground = "Ground";

        [MenuItem("WarfareSurvivor/Setup/Create Layers")]
        public static void Ensure()
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (asset == null || asset.Length == 0) return;

            var so = new SerializedObject(asset[0]);
            var layers = so.FindProperty("layers");

            foreach (var name in new[] { Survivors, Zombies, Environment, Ground })
            {
                if (IndexOf(layers, name) >= 0) continue;

                // Слои 0..7 заняты движком, свои кладём с восьмого.
                int free = -1;
                for (int i = 8; i < layers.arraySize; i++)
                    if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) { free = i; break; }

                if (free < 0)
                {
                    Debug.LogWarning($"[Слои] Нет свободного слоя для {name}");
                    continue;
                }

                layers.GetArrayElementAtIndex(free).stringValue = name;
                Debug.Log($"[Слои] {name} -> {free}");
            }

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        static int IndexOf(SerializedProperty layers, string name)
        {
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;
            return -1;
        }
    }
}
