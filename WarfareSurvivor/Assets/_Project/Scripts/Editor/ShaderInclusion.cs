using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WarfareSurvivor.EditorTools
{
    /// <summary>
    /// Следит, чтобы шейдеры, которые мы ищем через Shader.Find, попадали
    /// в сборку.
    ///
    /// В сборку включается только то, на что ссылается хоть один материал
    /// в сценах или ресурсах. Наши шейдеры не ссылается никто: материалы
    /// для трасс и полосок здоровья создаются кодом во время игры. Поэтому
    /// при сборке их вырезают, Shader.Find возвращает null — и в редакторе
    /// всё работает, а на устройстве нет.
    ///
    /// Это стоило целой сборки: null-шейдер уронил создание полоски здоровья,
    /// исключение оборвало создание отряда на первом бойце, список живых
    /// остался пустым, зомби некого стало искать, а джойстик двигал отряд,
    /// которого нет.
    /// </summary>
    public static class ShaderInclusion
    {
        static readonly string[] Required =
        {
            "WarfareSurvivor/AdditiveTracer",              // трассы, искры, вспышка
            "Universal Render Pipeline/Unlit",             // полоски здоровья
            "Universal Render Pipeline/Lit",               // запасной для тиров зомби
            "WarfareSurvivor/CheapGround",                 // земля
            "WarfareSurvivor/CheapToon",                   // персонажи и враги
            "WarfareSurvivor/VertexAnimationToon",         // зомби на запечённой анимации
            "WarfareSurvivor/Pickup",                      // ресурс тир-апа
            "WarfareSurvivor/MeleeArc",                    // дуга замаха
            "WarfareSurvivor/AcidZone",                    // зона поражения плевка
            "WarfareSurvivor/GlowSprite",                  // капли кислоты, след за добычей
            "WarfareSurvivor/Flame",                       // струя и языки огнемёта
            "Universal Render Pipeline/Simple Lit"         // для сравнения на стенде
        };

        [MenuItem("WarfareSurvivor/Setup/Include Runtime Shaders")]
        public static void Ensure()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (settings == null || settings.Length == 0)
            {
                Debug.LogError("[Shaders] Не читается ProjectSettings/GraphicsSettings.asset");
                return;
            }

            var so = new SerializedObject(settings[0]);
            var list = so.FindProperty("m_AlwaysIncludedShaders");

            var present = new HashSet<Object>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var value = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (value != null) present.Add(value);
            }

            var added = new List<string>();
            foreach (var name in Required)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogWarning($"[Shaders] В проекте нет шейдера {name}");
                    continue;
                }
                if (present.Contains(shader)) continue;

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                present.Add(shader);
                added.Add(name);
            }

            if (added.Count == 0) return;

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Shaders] В сборку добавлены: {string.Join(", ", added)}");
        }
    }
}
