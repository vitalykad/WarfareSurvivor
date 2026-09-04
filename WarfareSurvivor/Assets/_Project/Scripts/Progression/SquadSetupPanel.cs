using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WarfareSurvivor
{
    /// <summary>
    /// Стенд: экран сборки отряда перед забегом.
    ///
    /// Три ряда по ролям — ближний бой, стрелки, поддержка, — карточка
    /// на каждый класс, и счётчик у каждой карточки. Роль читается по
    /// ряду, а не по подписи: игрок собирает строй, а строй раскладывается
    /// именно по ролям.
    ///
    /// Собирается целиком в рантайме, а не лежит в сцене. Сцену строит
    /// генератор, и всякий новый экран в ней — это ещё одна вещь, которую
    /// он обязан построить, иначе стенд появляется только в той сцене,
    /// где его собрали руками.
    /// </summary>
    public class SquadSetupPanel : MonoBehaviour
    {
        ArenaConfig config;
        BenchConfig bench;
        System.Action<List<SquadEntry>> done;

        readonly Dictionary<SurvivorClassSO, int> picked = new Dictionary<SurvivorClassSO, int>();
        readonly Dictionary<SurvivorClassSO, Text> counters = new Dictionary<SurvivorClassSO, Text>();
        readonly Dictionary<SurvivorClassSO, Image> frames = new Dictionary<SurvivorClassSO, Image>();

        Text totalLabel;
        Button startButton;
        Image startFill;

        static readonly Color Ink = new Color(0.92f, 0.93f, 0.96f);
        static readonly Color Idle = new Color(0.16f, 0.18f, 0.23f, 0.96f);
        static readonly Color Taken = new Color(0.20f, 0.34f, 0.28f, 0.98f);

        /// <summary>
        /// Строит и показывает стенд. Забег ждёт, пока не нажмут «В бой».
        /// </summary>
        public static SquadSetupPanel Show(ArenaConfig config, System.Action<List<SquadEntry>> onReady)
        {
            var go = new GameObject("Стенд отряда");
            var panel = go.AddComponent<SquadSetupPanel>();
            panel.config = config;
            panel.bench = config != null ? config.bench : null;
            panel.done = onReady;
            panel.Build();
            return panel;
        }

        void Build()
        {
            var canvasGo = new GameObject("Канва стенда",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Поверх всего остального интерфейса: под ним живой HUD забега,
            // и он не должен просвечивать сквозь стенд.
            canvas.sortingOrder = 500;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);

            // Expand, а не match: экран стенда должен влезать целиком
            // на ЛЮБОМ соотношении сторон. При обычном match половина
            // рядов уезжала за верхний край, стоило открыть стенд
            // в почти квадратном окне редактора, — а увидеть это можно
            // только по координатам, потому что за краем ничего не видно.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            if (EventSystemPresent() == false) CreateEventSystem();

            var back = Stretch(new GameObject("Фон", typeof(RectTransform), typeof(Image)),
                               (RectTransform)canvasGo.transform);
            back.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.09f, 0.98f);

            var column = new GameObject("Колонка", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var columnRect = Stretch(column, (RectTransform)canvasGo.transform);
            columnRect.offsetMin = new Vector2(24f, 24f);
            columnRect.offsetMax = new Vector2(-24f, -24f);

            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            AddHeader(columnRect, "СБОРКА ОТРЯДА", 52);

            AddRow(columnRect, "БЛИЖНИЙ БОЙ", SquadRole.Melee);
            AddRow(columnRect, "СТРЕЛКИ", SquadRole.Ranged);
            AddRow(columnRect, "ПОДДЕРЖКА", SquadRole.Support);

            AddFooter(columnRect);
            Restore();
            Refresh();
        }

        // --- ряды и карточки ------------------------------------------------

        void AddRow(RectTransform parent, string title, SquadRole role)
        {
            var classes = ClassesOf(role);
            if (classes.Count == 0) return;

            var block = new GameObject("Ряд " + title, typeof(RectTransform), typeof(VerticalLayoutGroup));
            var blockRect = (RectTransform)block.transform;
            blockRect.SetParent(parent, false);

            // Ряды делят между собой лишнюю высоту поровну: на вытянутом
            // экране телефона карточки становятся крупнее, а не оставляют
            // снизу пустоту.
            var blockSize = block.AddComponent<LayoutElement>();
            blockSize.minHeight = 220f;
            blockSize.flexibleHeight = 1f;

            var blockLayout = block.GetComponent<VerticalLayoutGroup>();
            blockLayout.spacing = 4f;
            blockLayout.childControlHeight = true;
            blockLayout.childForceExpandHeight = false;
            blockLayout.childControlWidth = true;
            blockLayout.childForceExpandWidth = true;

            AddHeader(blockRect, title, 30, TextAnchor.MiddleLeft);

            // Ряд листается вбок: классов уже за десяток, и на телефоне
            // они в строку не помещаются. Резать список нельзя — стенд
            // затевался ровно ради того, чтобы видеть их все.
            var view = new GameObject("Прокрутка", typeof(RectTransform), typeof(Image),
                                      typeof(ScrollRect), typeof(RectMask2D));
            var viewRect = (RectTransform)view.transform;
            viewRect.SetParent(blockRect, false);
            view.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);

            var fit = view.AddComponent<LayoutElement>();
            fit.minHeight = 180f;
            fit.preferredHeight = 330f;
            fit.flexibleHeight = 1f;

            var strip = new GameObject("Карточки", typeof(RectTransform),
                                       typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            var stripRect = (RectTransform)strip.transform;
            stripRect.SetParent(viewRect, false);
            stripRect.anchorMin = new Vector2(0f, 0f);
            stripRect.anchorMax = new Vector2(0f, 1f);
            stripRect.pivot = new Vector2(0f, 0.5f);

            var strips = strip.GetComponent<HorizontalLayoutGroup>();
            strips.spacing = 12f;
            strips.padding = new RectOffset(8, 8, 8, 8);
            strips.childControlWidth = true;
            strips.childForceExpandWidth = false;
            strips.childControlHeight = true;
            strips.childForceExpandHeight = true;

            var fitter = strip.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = view.GetComponent<ScrollRect>();
            scroll.content = stripRect;
            scroll.viewport = viewRect;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            for (int i = 0; i < classes.Count; i++) AddCard(stripRect, classes[i]);
        }

        void AddCard(RectTransform parent, SurvivorClassSO klass)
        {
            var card = new GameObject(klass.displayName, typeof(RectTransform), typeof(Image),
                                      typeof(VerticalLayoutGroup));
            var rect = (RectTransform)card.transform;
            rect.SetParent(parent, false);

            var size = card.AddComponent<LayoutElement>();
            size.minWidth = 210f;
            size.preferredWidth = 210f;

            var frame = card.GetComponent<Image>();
            frame.color = Idle;
            frames[klass] = frame;

            var group = card.GetComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(10, 10, 10, 10);
            group.spacing = 4f;
            group.childControlWidth = true;
            group.childForceExpandWidth = true;
            group.childControlHeight = true;
            group.childForceExpandHeight = false;

            // Портрет. Тычок по нему добавляет бойца — это самый крупный
            // и самый очевидный способ набрать отряд, а минус нужен реже.
            var art = new GameObject("Портрет", typeof(RectTransform), typeof(Image), typeof(Button));
            var artRect = (RectTransform)art.transform;
            artRect.SetParent(rect, false);

            var artSize = art.AddComponent<LayoutElement>();
            artSize.minHeight = 90f;
            artSize.preferredHeight = 190f;
            artSize.flexibleHeight = 1f;

            var image = art.GetComponent<Image>();
            if (klass.icon != null)
            {
                image.sprite = klass.icon;
                image.color = Color.white;
                image.preserveAspect = true;
            }
            else
            {
                // Иконки нет — рисуем заглушку, а не пустоту: пустая
                // карточка читается как сломанная.
                image.color = new Color(0.30f, 0.32f, 0.38f, 1f);
            }
            art.GetComponent<Button>().onClick.AddListener(() => Add(klass, +1));

            AddLabel(rect, klass.displayName, 26, TextAnchor.MiddleCenter, 34f);

            // Счётчик: минус, число, плюс.
            var line = new GameObject("Счётчик", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var lineRect = (RectTransform)line.transform;
            lineRect.SetParent(rect, false);

            var lineSize = line.AddComponent<LayoutElement>();
            lineSize.minHeight = 56f;
            lineSize.preferredHeight = 56f;

            var lineGroup = line.GetComponent<HorizontalLayoutGroup>();
            lineGroup.spacing = 6f;
            lineGroup.childControlWidth = true;
            lineGroup.childForceExpandWidth = true;
            lineGroup.childControlHeight = true;
            lineGroup.childForceExpandHeight = true;

            AddStep(lineRect, "−", () => Add(klass, -1));
            counters[klass] = AddLabel(lineRect, "0", 34, TextAnchor.MiddleCenter, 56f);
            AddStep(lineRect, "+", () => Add(klass, +1));
        }

        void AddStep(RectTransform parent, string caption, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject("Кнопка " + caption, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var size = go.AddComponent<LayoutElement>();
            size.minWidth = 56f;
            size.preferredWidth = 56f;

            go.GetComponent<Image>().color = new Color(0.26f, 0.29f, 0.36f, 1f);
            go.GetComponent<Button>().onClick.AddListener(action);

            var label = AddLabel(rect, caption, 36, TextAnchor.MiddleCenter, 0f);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        // --- низ экрана -----------------------------------------------------

        void AddFooter(RectTransform parent)
        {
            var footer = new GameObject("Низ", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rect = (RectTransform)footer.transform;
            rect.SetParent(parent, false);

            var size = footer.AddComponent<LayoutElement>();
            size.minHeight = 110f;
            size.preferredHeight = 110f;

            var group = footer.GetComponent<HorizontalLayoutGroup>();
            group.spacing = 16f;
            group.childControlWidth = true;
            group.childForceExpandWidth = true;
            group.childControlHeight = true;
            group.childForceExpandHeight = true;

            totalLabel = AddLabel(rect, string.Empty, 32, TextAnchor.MiddleLeft, 0f);

            var clear = new GameObject("Сбросить", typeof(RectTransform), typeof(Image), typeof(Button));
            var clearRect = (RectTransform)clear.transform;
            clearRect.SetParent(rect, false);
            clear.AddComponent<LayoutElement>().preferredWidth = 240f;
            clear.GetComponent<Image>().color = new Color(0.22f, 0.20f, 0.22f, 1f);
            clear.GetComponent<Button>().onClick.AddListener(Clear);
            Fill(AddLabel(clearRect, "СБРОСИТЬ", 30, TextAnchor.MiddleCenter, 0f));

            var start = new GameObject("В бой", typeof(RectTransform), typeof(Image), typeof(Button));
            var startRect = (RectTransform)start.transform;
            startRect.SetParent(rect, false);
            start.AddComponent<LayoutElement>().preferredWidth = 320f;

            startFill = start.GetComponent<Image>();
            startButton = start.GetComponent<Button>();
            startButton.onClick.AddListener(Begin);
            Fill(AddLabel(startRect, "В БОЙ", 36, TextAnchor.MiddleCenter, 0f));
        }

        void Add(SurvivorClassSO klass, int delta)
        {
            // Ноль в настройке значит «без потолка».
            int max = bench != null && bench.maxPerClass > 0 ? bench.maxPerClass : int.MaxValue;
            int now = Count(klass);
            picked[klass] = Mathf.Clamp(now + delta, 0, max);
            Refresh();
        }

        void Clear()
        {
            picked.Clear();
            Refresh();
        }

        void Begin()
        {
            var chosen = new List<SquadEntry>();
            foreach (var pair in picked)
                if (pair.Value > 0) chosen.Add(new SquadEntry { Class = pair.Key, Count = pair.Value });

            if (Total() < MinTotal()) return;

            Remember(chosen);
            var callback = done;
            done = null;

            Destroy(gameObject);
            callback?.Invoke(chosen);
        }

        /// <summary>Ключ памяти стенда.</summary>
        const string MemoryKey = "bench.lastPick";

        /// <summary>
        /// Запоминает набранное — и в настройках проекта, и в памяти игрока.
        ///
        /// Двумя способами, потому что каждый по отдельности дырявый.
        /// Ассет виден в инспекторе и уезжает в гит вместе с проектом, но
        /// в собранной игре его не записать, а в редакторе правка из плей-мода
        /// может не дожить до диска: выход из игры перезагружает домен,
        /// и помеченный грязным ассет, который никто не успел сохранить,
        /// откатывается. Это и случалось — набор терялся через раз.
        /// PlayerPrefs же работает и в сборке, и переживает перезагрузку
        /// домена, но человеку его не видно.
        ///
        /// Классы пишутся ИМЕНЕМ АССЕТА, а не порядковым номером: список
        /// классов на стенде меняется, и номера съезжают молча — стенд
        /// подставил бы вместо снайпера первого попавшегося соседа.
        /// </summary>
        void Remember(List<SquadEntry> chosen)
        {
            var line = new System.Text.StringBuilder();
            foreach (var entry in chosen)
            {
                if (entry.Class == null || entry.Count <= 0) continue;
                if (line.Length > 0) line.Append(';');
                line.Append(entry.Class.name).Append(':').Append(entry.Count);
            }

            PlayerPrefs.SetString(MemoryKey, line.ToString());
            PlayerPrefs.Save();

#if UNITY_EDITOR
            if (bench == null) return;
            bench.lastPick = chosen.ToArray();
            UnityEditor.EditorUtility.SetDirty(bench);

            // Сохраняем СРАЗУ: пометки «грязный» мало, см. выше.
            UnityEditor.AssetDatabase.SaveAssets();
#endif
        }

        void Restore()
        {
            if (bench == null) return;

            // Память игрока свежее ассета: в неё пишет каждый запуск,
            // включая тот, после которого редактор закрыли не сохраняясь.
            var saved = PlayerPrefs.GetString(MemoryKey, string.Empty);
            if (!string.IsNullOrEmpty(saved) && RestoreFrom(saved)) return;

            foreach (var klass in bench.classes)
            {
                if (klass == null) continue;
                int remembered = bench.RememberedCount(klass);
                if (remembered > 0) picked[klass] = remembered;
            }
        }

        /// <summary>
        /// Разбирает строку памяти. Возвращает false, если из неё не вышло
        /// ни одного живого класса, — тогда стенд откатывается на ассет.
        /// Классы могли переименовать, спрятать или удалить, и упираться
        /// в мёртвую запись незачем.
        /// </summary>
        bool RestoreFrom(string saved)
        {
            bool any = false;

            foreach (var pair in saved.Split(';'))
            {
                int colon = pair.LastIndexOf(':');
                if (colon <= 0) continue;

                string assetName = pair.Substring(0, colon);
                if (!int.TryParse(pair.Substring(colon + 1), out int count) || count <= 0) continue;

                foreach (var klass in bench.classes)
                {
                    if (klass == null || klass.name != assetName) continue;
                    picked[klass] = count;
                    any = true;
                    break;
                }
            }

            return any;
        }

        void Refresh()
        {
            foreach (var pair in counters)
            {
                int count = Count(pair.Key);
                pair.Value.text = count.ToString();

                if (frames.TryGetValue(pair.Key, out var frame))
                    frame.color = count > 0 ? Taken : Idle;
            }

            int total = Total();
            int need = MinTotal();
            bool ready = total >= need;

            if (totalLabel != null)
                totalLabel.text = ready
                    ? "в отряде " + total
                    : "в отряде " + total + ", нужно хотя бы " + need;

            if (startButton != null) startButton.interactable = ready;
            if (startFill != null)
                startFill.color = ready
                    ? new Color(0.20f, 0.45f, 0.30f, 1f)
                    : new Color(0.18f, 0.20f, 0.24f, 1f);
        }

        // --- мелочи ---------------------------------------------------------

        int Count(SurvivorClassSO klass) => picked.TryGetValue(klass, out int n) ? n : 0;

        int Total()
        {
            int n = 0;
            foreach (var pair in picked) n += pair.Value;
            return n;
        }

        int MinTotal() => bench != null ? Mathf.Max(1, bench.minTotal) : 1;

        List<SurvivorClassSO> ClassesOf(SquadRole role)
        {
            var list = new List<SurvivorClassSO>();
            if (bench == null || bench.classes == null) return list;

            foreach (var klass in bench.classes)
                if (klass != null && klass.role == role && klass.prefab != null) list.Add(klass);
            return list;
        }

        Text AddHeader(RectTransform parent, string caption, int size,
                       TextAnchor align = TextAnchor.MiddleCenter)
        {
            var label = AddLabel(parent, caption, size, align, size + 14f);
            label.color = new Color(0.62f, 0.68f, 0.78f);
            return label;
        }

        Text AddLabel(RectTransform parent, string caption, int size, TextAnchor align, float height)
        {
            var go = new GameObject("Подпись", typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            if (height > 0f)
            {
                var element = go.AddComponent<LayoutElement>();
                element.minHeight = height;
                element.preferredHeight = height;
            }

            var text = go.GetComponent<Text>();
            text.text = caption;
            text.font = UIFont.Get(config);
            text.fontSize = size;
            text.alignment = align;
            text.color = Ink;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        static void Fill(Text label)
        {
            var rect = (RectTransform)label.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static RectTransform Stretch(GameObject go, RectTransform parent)
        {
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        static bool EventSystemPresent() =>
            FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null;

        static void CreateEventSystem()
        {
            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));
        }
    }
}
