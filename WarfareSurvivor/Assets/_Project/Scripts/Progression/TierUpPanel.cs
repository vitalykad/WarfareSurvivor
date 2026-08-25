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

        void Awake() => Hide();

        public void Show(List<TierUpOffer> options, System.Action<TierUpOffer> onPick)
        {
            choice = onPick;
            if (root != null) root.gameObject.SetActive(true);

            // Заголовка нет намеренно: три карточки и так не оставляют
            // сомнений, что от игрока ждут выбора, а надпись только лезла
            // из-под них.
            if (title != null) title.text = string.Empty;

            BuildCards(options);
        }

        public void Hide()
        {
            if (root != null) root.gameObject.SetActive(false);
        }

        void BuildCards(List<TierUpOffer> options)
        {
            for (int i = 0; i < cards.Count; i++)
                if (cards[i] != null) Destroy(cards[i].gameObject);
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

        void Pick(TierUpOffer offer)
        {
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

        static void AddText(RectTransform parent, string value, int size, FontStyle style,
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
            text.font = UIFont.Get();
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAnchor.UpperCenter;
            text.color = Color.white;
            text.raycastTarget = false;
        }
    }
}
