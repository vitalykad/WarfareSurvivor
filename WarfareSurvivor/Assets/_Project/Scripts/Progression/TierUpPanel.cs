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

        System.Action<SurvivorClassSO> choice;
        readonly List<Button> cards = new List<Button>();

        void Awake() => Hide();

        public void Show(List<SurvivorClassSO> options, System.Action<SurvivorClassSO> onPick)
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

        void BuildCards(List<SurvivorClassSO> options)
        {
            for (int i = 0; i < cards.Count; i++)
                if (cards[i] != null) Destroy(cards[i].gameObject);
            cards.Clear();

            if (cardRow == null) return;

            foreach (var klass in options)
            {
                var card = CreateCard(klass);
                var captured = klass;
                card.onClick.AddListener(() => Pick(captured));
                cards.Add(card);
            }
        }

        void Pick(SurvivorClassSO klass)
        {
            Hide();
            choice?.Invoke(klass);
        }

        Button CreateCard(SurvivorClassSO klass)
        {
            var go = new GameObject(klass.displayName, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(cardRow, false);
            rect.sizeDelta = new Vector2(300f, 420f);

            go.GetComponent<Image>().color = new Color(0.12f, 0.13f, 0.16f, 0.96f);

            AddText(rect, klass.displayName, 40, FontStyle.Bold,
                    new Vector2(0f, 0.78f), new Vector2(1f, 0.96f));
            AddText(rect, RoleName(klass.role), 28, FontStyle.Normal,
                    new Vector2(0f, 0.64f), new Vector2(1f, 0.78f));
            AddText(rect, Stats(klass), 26, FontStyle.Normal,
                    new Vector2(0f, 0.08f), new Vector2(1f, 0.62f));

            return go.GetComponent<Button>();
        }

        /// <summary>
        /// Роль называется тем, что она значит для игрока, — местом в строю.
        /// «Melee» не говорит ничего, «в первом ряду» говорит всё.
        /// </summary>
        static string RoleName(SquadRole role)
        {
            switch (role)
            {
                case SquadRole.Melee: return "первый ряд";
                case SquadRole.Ranged: return "второй ряд";
                default: return "в ядре отряда";
            }
        }

        static string Stats(SurvivorClassSO klass)
        {
            float dps = klass.attackInterval > 0f ? klass.damage / klass.attackInterval : 0f;
            var text = new System.Text.StringBuilder();
            text.Append("здоровье   ").Append(Mathf.RoundToInt(klass.maxHealth)).Append('\n');
            text.Append("урон   ").Append(Mathf.RoundToInt(klass.damage)).Append('\n');
            text.Append("раз в   ").Append(klass.attackInterval.ToString("0.0")).Append(" с\n");
            text.Append("дальность   ").Append(Mathf.RoundToInt(klass.attackRange)).Append(" м\n\n");
            text.Append("урон в секунду   ").Append(dps.ToString("0.#"));
            if (klass.knockbackDistance > 0f)
                text.Append("\n\nотбрасывает тела");
            return text.ToString();
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
