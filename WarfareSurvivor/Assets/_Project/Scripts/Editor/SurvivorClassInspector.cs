using UnityEditor;
using UnityEngine;

namespace WarfareSurvivor.EditorTools
{
    /// <summary>
    /// Инспектор класса: обычные поля плюсчеловекочитаемый блок синергий.
    ///
    /// Список ассетов сам по себе не годится. В стандартном виде синергия
    /// в списке выглядит как «Synergy_Plus_MeleeDamage» — чтобы понять, что
    /// это, надо ткнуть в каждую. А решение на балансе принимается по
    /// строчке «+ 3% урон ближнего боя за бойца», и она должна быть видна
    /// сразу, всеми строками разом.
    /// </summary>
    [CustomEditor(typeof(SurvivorClassSO))]
    public class SurvivorClassInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "synergies");
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8f);
            DrawSynergies((SurvivorClassSO)target);
        }

        void DrawSynergies(SurvivorClassSO klass)
        {
            EditorGUILayout.LabelField("Синергия — что боец даёт всему отряду", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (klass.synergies == null || klass.synergies.Count == 0)
                {
                    EditorGUILayout.LabelField("Синергий нет — роль работает только собой.",
                                               EditorStyles.miniLabel);
                }

                for (int i = 0; klass.synergies != null && i < klass.synergies.Count; i++)
                {
                    var synergy = klass.synergies[i];

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (synergy == null)
                        {
                            EditorGUILayout.LabelField("— пусто —", EditorStyles.miniLabel);
                        }
                        else
                        {
                            EditorGUILayout.LabelField(synergy.Describe());

                            if (synergy.IsNumeric && !SquadStatInfo.HasConsumer(synergy.stat))
                                GUILayout.Label(EditorGUIUtility.IconContent("console.warnicon.sml"),
                                                GUILayout.Width(20f));

                            if (GUILayout.Button("к ассету", EditorStyles.miniButton, GUILayout.Width(70f)))
                                EditorGUIUtility.PingObject(synergy);
                        }

                        if (GUILayout.Button("убрать", EditorStyles.miniButton, GUILayout.Width(60f)))
                        {
                            Undo.RecordObject(klass, "Убрать синергию");
                            klass.synergies.RemoveAt(i);
                            EditorUtility.SetDirty(klass);
                            GUIUtility.ExitGUI();
                        }
                    }
                }

                EditorGUILayout.Space(4f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Добавить синергию", GUILayout.Width(150f)))
                        ShowAddMenu(klass);

                    if (GUILayout.Button("Завести новую…", GUILayout.Width(130f)))
                        SynergyWindow.Open();
                }
            }
        }

        void ShowAddMenu(SurvivorClassSO klass)
        {
            var menu = new GenericMenu();
            var all = SynergyLibrary.All();

            if (all.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("Синергий ещё не заведено"));
            }

            foreach (var synergy in all)
            {
                if (synergy == null) continue;

                var captured = synergy;
                bool already = klass.synergies != null && klass.synergies.Contains(synergy);

                // Разложены по параметру: прибавок к одному и тому же обычно
                // несколько, и в плоском списке из двадцати строк нужную
                // приходится вычитывать.
                string group = synergy.IsNumeric ? SquadStatInfo.Label(synergy.stat) : "другое";
                var label = new GUIContent(group + "/" + synergy.Describe());

                if (already) menu.AddDisabledItem(label, true);
                else menu.AddItem(label, false, () =>
                {
                    Undo.RecordObject(klass, "Добавить синергию");
                    if (klass.synergies == null)
                        klass.synergies = new System.Collections.Generic.List<SynergySO>();
                    klass.synergies.Add(captured);
                    EditorUtility.SetDirty(klass);
                });
            }

            menu.ShowAsContext();
        }
    }
}
