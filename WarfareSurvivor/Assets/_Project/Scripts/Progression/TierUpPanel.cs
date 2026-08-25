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
            if (title != null) title.text = "ПОПОЛНЕНИЕ";

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
                // Картинка — сама карточка. Подложку делаем белой, чтобы
                // не красить рисунок, и растягиваем на всю карточку.
                background.sprite = offer.Art;
                background.color = Color.white;
                background.preserveAspect = true;

                // Затемнение поверх картинки, но ПОД текстом: рисунки светлые
                // и пёстрые, и белые буквы на них не читаются. Градиентом
                // не делаем — хватает ровной вуали в нижней половине, где
                // и стоит текст.
                AddVeil(rect, new Vector2(0f, 0f), new Vector2(1f, 0.42f),
                        new Color(0f, 0f, 0f, 0.72f));
                AddVeil(rect, new Vector2(0f, 0.84f), new Vector2(1f, 1f),
                        new Color(0f, 0f, 0f, 0.55f));

                AddText(rect, offer.Title, 34, FontStyle.Bold,
                        new Vector2(0f, 0.855f), new Vector2(1f, 0.995f));
                AddText(rect, offer.Subtitle + "\n" + offer.Body, 22, FontStyle.Normal,
                        new Vector2(0f, 0.02f), new Vector2(1f, 0.40f));

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
