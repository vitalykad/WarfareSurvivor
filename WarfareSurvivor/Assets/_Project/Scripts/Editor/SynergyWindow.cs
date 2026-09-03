using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WarfareSurvivor.EditorTools
{
    /// <summary>
    /// Окно, в котором синергии заводят и разбирают.
    ///
    /// Отдельное окно, а не просто папка с ассетами: синергию мало создать —
    /// её надо видеть рядом с остальными. Половина работы над балансом здесь
    /// это вопросы «а сколько всего прибавок к урону ближнего боя я уже
    /// раздал» и «кто ещё пользуется вот этой», и по одиночным ассетам
    /// в папке на них не ответить.
    /// </summary>
    public class SynergyWindow : EditorWindow
    {
        [MenuItem("WarfareSurvivor/Синергии")]
        public static void Open()
        {
            var window = GetWindow<SynergyWindow>("Синергии");
            window.minSize = new Vector2(460f, 400f);
        }

        // поля формы создания
        SynergyKind newKind = SynergyKind.Bonus;
        SquadStat newStat = SquadStat.MeleeDamage;
        SynergyAmount newAmountType = SynergyAmount.Percent;
        float newAmount = 3f;
        bool newScales = true;
        string newNote = "";

        Vector2 scroll;
        readonly Dictionary<SynergySO, bool> expanded = new Dictionary<SynergySO, bool>();

        // Кэш. Собрать список синергий и карту их использования стоит четверть
        // секунды: это обходы всего проекта через AssetDatabase. В отрисовке
        // окна такому не место — OnGUI зовётся по нескольку раз на одно
        // нажатие, и переключение вкладки занимало две секунды.
        List<SynergySO> cached;
        Dictionary<SynergySO, List<SurvivorClassSO>> usage;
        readonly Dictionary<SynergySO, UnityEditor.Editor> editors = new Dictionary<SynergySO, UnityEditor.Editor>();

        void OnEnable()
        {
            Rebuild();
            EditorApplication.projectChanged += Rebuild;
        }

        void OnDisable()
        {
            EditorApplication.projectChanged -= Rebuild;
            DropEditors();
        }

        /// <summary>Возврат в окно — самый дешёвый момент заметить чужие правки.</summary>
        void OnFocus() => Rebuild();

        void Rebuild()
        {
            cached = SynergyLibrary.All();
            usage = SynergyLibrary.UsageMap(cached);
            DropEditors();
            Repaint();
        }

        void DropEditors()
        {
            foreach (var pair in editors)
                if (pair.Value != null) DestroyImmediate(pair.Value);
            editors.Clear();
        }

        List<SurvivorClassSO> UsersOf(SynergySO synergy)
        {
            if (usage != null && synergy != null && usage.TryGetValue(synergy, out var users)) return users;
            return new List<SurvivorClassSO>();
        }

        /// <summary>Инспектор синергии держим готовым, а не создаём заново каждый кадр.</summary>
        UnityEditor.Editor EditorFor(SynergySO synergy)
        {
            if (editors.TryGetValue(synergy, out var editor) && editor != null) return editor;

            editor = UnityEditor.Editor.CreateEditor(synergy);
            editors[synergy] = editor;
            return editor;
        }

        void OnGUI()
        {
            DrawCreateForm();
            EditorGUILayout.Space(6f);
            DrawList();
        }

        void DrawCreateForm()
        {
            EditorGUILayout.LabelField("Новая синергия", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                newKind = (SynergyKind)GUILayout.Toolbar((int)newKind, SynergyLibrary.KindLabels);

                if (newKind == SynergyKind.Special)
                {
                    EditorGUILayout.HelpBox(
                        "«Другое» — правило, которое не сводится к прибавке к числу. " +
                        "В сумму параметров оно не идёт; код разбирает его по метке.",
                        MessageType.None);
                    newNote = EditorGUILayout.TextField("Метка правила", newNote);
                }
                else
                {
                    newStat = (SquadStat)EditorGUILayout.Popup(
                        "Параметр", (int)newStat, SynergyLibrary.StatLabels());

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        newAmount = Mathf.Max(0f, EditorGUILayout.FloatField("Сколько даёт один боец", newAmount));
                        newAmountType = (SynergyAmount)EditorGUILayout.Popup(
                            (int)newAmountType, SynergyLibrary.AmountLabels, GUILayout.Width(90f));
                    }

                    newScales = EditorGUILayout.ToggleLeft(
                        "Растёт от числа бойцов роли", newScales);

                    if (!SquadStatInfo.HasConsumer(newStat))
                        EditorGUILayout.HelpBox(
                            "Этот параметр в бою пока никто не читает: " + SquadStatInfo.Missing(newStat) +
                            ". Синергию завести можно, но на тесте она ничего не изменит.",
                            MessageType.Warning);
                }

                EditorGUILayout.LabelField("Получится", Preview(), EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Создать синергию", GUILayout.Width(160f)))
                    {
                        var made = SynergyLibrary.Create(newKind, newStat, newAmountType,
                                                         newAmount, newScales, newNote);
                        Rebuild();
                        Selection.activeObject = made;
                        EditorGUIUtility.PingObject(made);
                    }
                }
            }
        }

        string Preview()
        {
            if (newKind == SynergyKind.Special)
                return "◆ " + (string.IsNullOrEmpty(newNote) ? "особое правило" : newNote);

            string sign = newKind == SynergyKind.Bonus ? "+" : "−";
            string value = newAmountType == SynergyAmount.Percent
                ? newAmount.ToString("0.##") + "%"
                : newAmount.ToString("0.##");
            return sign + " " + value + " " + SquadStatInfo.Label(newStat) +
                   (newScales ? " за бойца" : " за роль");
        }

        void DrawList()
        {
            if (cached == null) Rebuild();
            var all = cached;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Заведено: " + all.Count, EditorStyles.boldLabel);
                if (GUILayout.Button("обновить", EditorStyles.miniButton, GUILayout.Width(70f)))
                {
                    Rebuild();
                    GUIUtility.ExitGUI();
                }
            }

            if (all.Count == 0)
            {
                EditorGUILayout.HelpBox("Синергий пока нет. Заведи первую формой выше.", MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);

            foreach (var synergy in all)
            {
                if (synergy == null) continue;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        expanded.TryGetValue(synergy, out bool open);
                        bool nowOpen = EditorGUILayout.Foldout(open, synergy.Describe(), true);
                        if (nowOpen != open) expanded[synergy] = nowOpen;

                        var users = UsersOf(synergy);
                        GUILayout.Label(users.Count == 0 ? "ни у кого" : "ролей: " + users.Count,
                                        EditorStyles.miniLabel, GUILayout.Width(80f));

                        if (GUILayout.Button("к ассету", EditorStyles.miniButton, GUILayout.Width(70f)))
                            EditorGUIUtility.PingObject(synergy);

                        if (GUILayout.Button("удалить", EditorStyles.miniButton, GUILayout.Width(60f)))
                        {
                            string who = users.Count == 0
                                ? "Её никто не использует."
                                : "Она снимется с ролей: " + string.Join(", ", users.ConvertAll(k => k.displayName)) + ".";

                            if (EditorUtility.DisplayDialog("Удалить синергию?",
                                    synergy.Describe() + "\n\n" + who, "Удалить", "Отмена"))
                            {
                                SynergyLibrary.Delete(synergy);
                                Rebuild();
                                GUIUtility.ExitGUI();
                            }
                        }
                    }

                    if (expanded.TryGetValue(synergy, out bool isOpen) && isOpen)
                    {
                        EditorGUI.indentLevel++;
                        EditorFor(synergy).OnInspectorGUI();

                        var users = UsersOf(synergy);
                        if (users.Count > 0)
                            EditorGUILayout.LabelField("Стоит у ролей",
                                string.Join(", ", users.ConvertAll(k => k.displayName)), EditorStyles.miniLabel);

                        EditorGUI.indentLevel--;
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
