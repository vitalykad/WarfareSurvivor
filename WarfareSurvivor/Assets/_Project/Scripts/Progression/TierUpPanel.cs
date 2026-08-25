using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WarfareSurvivor
{
    /// <summary>
    /// Окно тир-апа: карточки классов, из которых игрок добирает бойца.
    ///
    /// Это единственное решение, которое игрок принимает за забег, и ради
    /// него собрано всё остальное. Поэтому карточка показывает не название,
    /// а ПОСЛЕДСТВИЯ: где боец встанет, чем и как часто бьёт.
    /// </summary>
    public class TierUpPanel : MonoBehaviour
    {
        [SerializeField] ArenaConfig config;
        [SerializeField] RectTransform root;
        [SerializeField] Text title;
        [SerializeField] RectTransform cardRow;

        System.Action<TierUpOffer> choice;
        readonly List<Button> cards = new List<Button>();

        /// <summary>
        /// Выбор уже сделан, и окно ждёт закрытия.
        ///
        /// Без этого два касания в один кадр — или касание сразу по двум
        /// карточкам — засчитываются оба: игрок получает два улучшения
        /// за один тир-ап, а очередь уходит в минус. На телефоне это
        /// случается легко: палец накрывает край соседней карточки.
        /// </summary>
        bool picked;

        void Awake() => Hide();

        public void Show(List<TierUpOffer> options, System.Action<TierUpOffer> onPick)
        {
            choice = onPick;
            picked = false;
            if (root != null) root.gameObject.SetActive(true);

            // Заголовка нет намеренно: три карточки и так не оставляют
            // сомнений, что от игрока ждут выбора, а надпись только лезла
            // из-под них.
            if (title != null) title.text = string.Empty;

            BuildCards(options);
            ReportLayout();
        }

        public void Hide()
        {
            if (root != null) root.gameObject.SetActive(false);
        }

        void BuildCards(List<TierUpOffer> options)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;

                // Гасим СРАЗУ, а не только помечаем на удаление: Unity
                // уничтожает объект в конце кадра, и до тех пор старые
                // карточки остаются детьми ряда — участвуют в раскладке
                // и попадают в отчёт, из-за чего он показывал шесть карточек
                // вместо трёх.
                cards[i].transform.SetParent(null, false);
                Destroy(cards[i].gameObject);
            }
            cards.Clear();

            if (cardRow == null) return;

            foreach (var offer in options)
            {
                var card = CreateCard(offer);
                var captured = offer;
                card.onClick.AddListener(() => Pick(captured));
                cards.Add(card);
            }
        }

        /// <summary>
        /// Проверяет, что все карточки попали в экран.
        ///
        /// Карточка за краем читается игроком как «её нет», и отличить это
        /// от «её не предложили» со стороны невозможно — на устройстве
        /// инспектора нет.
        /// </summary>
        void ReportLayout()
        {
            if (cardRow == null) return;

            // Пересчитываем раскладку СЕЙЧАС, а не ждём конца кадра.
            //
            // Иначе замер видит карточки до того, как группа их расставила,
            // и все три отчитываются с одними координатами — отчёт врёт
            // ровно про то, ради чего заведён.
            LayoutRebuilder.ForceRebuildLayoutImmediate(cardRow);

            var corners = new Vector3[4];
            var report = new System.Text.StringBuilder("[Тир-ап] на экране:");

            for (int i = 0; i < cardRow.childCount; i++)
            {
                var rect = cardRow.GetChild(i) as RectTransform;
                if (rect == null) continue;

                rect.GetWorldCorners(corners);
                float left = corners[0].x;
                float right = corners[2].x;

                report.Append(' ').Append(rect.name).Append('[')
                      .Append(Mathf.RoundToInt(left)).Append("..").Append(Mathf.RoundToInt(right)).Append(']');

                if (left < 0f || right > Screen.width) report.Append("<-ЗА КРАЕМ");
            }

            report.Append(" ширина экрана ").Append(Screen.width);
            Debug.Log(report.ToString());
        }

        void Pick(TierUpOffer offer)
        {
            if (picked) return;
            picked = true;

            Hide();
            choice?.Invoke(offer);
        }

        Button CreateCard(TierUpOffer offer)
        {
            var go = new GameObject(offer.Title, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(cardRow, false);
            rect.sizeDelta = new Vector2(300f, 420f);

            var background = go.GetComponent<Image>();

            if (offer.Art != null)
            {
                // Картинка И ЕСТЬ карточка — никаких подписей поверх.
                //
                // Подписи дублировали нарисованное: на рисунке крупно
                // написано, что это за карточка, а мы писали то же самое
                // сверху и снизу, да ещё поверх лица. Что именно даёт
                // карточка, игрок читает с рисунка, а точные проценты
                // ему в момент выбора не нужны.
                background.sprite = offer.Art;
                background.color = Color.white;
                background.preserveAspect = true;

                return go.GetComponent<Button>();
            }

            // Картинки нет — старая текстовая карточка. Пополнение
            // и улучшение отличаются цветом подложки.
            background.color = offer.Kind == OfferKind.AddUnit
                ? new Color(0.12f, 0.16f, 0.22f, 0.96f)
                : new Color(0.18f, 0.14f, 0.12f, 0.96f);

            AddText(rect, offer.Title, 38, FontStyle.Bold,
                    new Vector2(0f, 0.78f), new Vector2(1f, 0.96f));
            AddText(rect, offer.Subtitle, 26, FontStyle.Normal,
                    new Vector2(0f, 0.66f), new Vector2(1f, 0.78f));
            AddText(rect, offer.Body, 25, FontStyle.Normal,
                    new Vector2(0f, 0.08f), new Vector2(1f, 0.64f));

            return go.GetComponent<Button>();
        }

        /// <summary>Тёмная вуаль поверх картинки, чтобы текст читался.</summary>
        static void AddVeil(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = new GameObject("Затемнение", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.color = color;

            // Не перехватывает касания: нажатие должно доходить до карточки.
            image.raycastTarget = false;
        }

        void AddText(RectTransform parent, string value, int size, FontStyle style,
                     Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject("Текст", typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(18f, 0f);
            rect.offsetMax = new Vector2(-18f, 0f);

            var text = go.GetComponent<Text>();
            text.text = value;
            text.font = UIFont.Get(config);
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAnchor.UpperCenter;
            text.color = Color.white;
            text.raycastTarget = false;
        }
    }
}
