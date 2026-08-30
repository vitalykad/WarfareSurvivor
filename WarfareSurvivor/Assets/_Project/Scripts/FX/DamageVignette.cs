using UnityEngine;
using UnityEngine.UI;

namespace WarfareSurvivor
{
    /// <summary>
    /// Красная кайма по краю экрана, когда отряд едят.
    ///
    /// Отвечает на вопрос, на который не отвечает ничто другое: НАСКОЛЬКО
    /// БЫСТРО отряд умирает прямо сейчас. Вспышки на бойцах говорят, кого
    /// ударили, полоски — сколько у кого осталось, но решение игрока —
    /// уходить или стоять — зависит от темпа, а темп нигде не показан.
    ///
    /// Удары СКЛАДЫВАЮТСЯ и гаснут со временем. Значит густота каймы и есть
    /// урон в секунду по всему отряду: один укус её едва тронет, десяток
    /// в секунду зальёт край экрана.
    ///
    /// По КРАЮ, а не по всему кадру: в середине идёт бой, и закрашивать
    /// его — значит мешать смотреть ровно тогда, когда смотреть важнее всего.
    /// </summary>
    public class DamageVignette : MonoBehaviour
    {
        static DamageVignette instance;
        static ArenaConfig config;

        RawImage view;
        float intensity;

        public static void Configure(ArenaConfig cfg)
        {
            config = cfg;
            if (instance != null) return;

            var go = new GameObject("Виньетка урона");
            instance = go.AddComponent<DamageVignette>();
            instance.Build();
        }

        /// <summary>Отряд получил удар. Считаем удары, а не урон: игроку важна частота.</summary>
        public static void Hit()
        {
            if (instance == null || config == null) return;
            instance.intensity = Mathf.Min(1f, instance.intensity + Mathf.Max(0f, config.damageVignettePerHit));
        }

        void Build()
        {
            var canvasGo = new GameObject("Canvas виньетки", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Поверх всего интерфейса: кайма не должна оказаться под полоской
            // тир-апа или под карточками.
            canvas.sortingOrder = 500;

            var imageGo = new GameObject("Кайма", typeof(RawImage));
            imageGo.transform.SetParent(canvasGo.transform, false);

            var rect = (RectTransform)imageGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            view = imageGo.GetComponent<RawImage>();
            view.texture = Texture();

            // Клики сквозь неё должны проходить: она лежит поверх всего,
            // включая кнопки звука и карточки тир-апа.
            view.raycastTarget = false;
            view.color = new Color(1f, 1f, 1f, 0f);
        }

        /// <summary>
        /// Прозрачная середина и плотный край.
        ///
        /// Растягивается на весь экран, поэтому по горизонтали её ужимает
        /// или растягивает вместе с ним — для мягкой каймы это незаметно,
        /// а отдельная текстура на каждое соотношение сторон не нужна.
        /// </summary>
        static Texture2D Texture()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "DamageVignette",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;

                    // Расстояние до края КАДРА, а не до центра по кругу:
                    // круглая виньетка на вытянутом экране оставляет светлые
                    // углы сверху и снизу.
                    float edge = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));

                    // Пусто до 0.45 и плотнеет к самому краю.
                    float a = Mathf.Clamp01((edge - 0.45f) / 0.55f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        void LateUpdate()
        {
            if (config == null || view == null) return;

            // Гаснет НЕЗАВИСИМО от скорости времени: на паузе тир-апа
            // застывшая красная кайма выглядела бы поломкой.
            // Гаснет ЭКСПОНЕНТОЙ, а не по прямой.
            //
            // Прямая спадает с одной и той же скоростью до последнего кадра,
            // а потом резко останавливается — глаз цепляется именно за это
            // изменение скорости и читает его как рывок. У экспоненты
            // скорость сама сходит на нет, и конец не виден вовсе.
            //
            // Кривую не применяем к самой прозрачности: возведение в степень
            // притушило бы и вспышку от одиночного удара, а она должна быть
            // заметной — то есть сработало бы против цели.
            float fade = Mathf.Max(0.05f, config.damageVignetteFade);

            // fade — время, за которое густота падает примерно до пятидесятой
            // доли, то есть до незаметного. Отсюда и делитель.
            intensity *= Mathf.Exp(-Time.unscaledDeltaTime * 4f / fade);

            // Иначе она тянулась бы бесконечно малым остатком и держала
            // прозрачный слой поверх кадра без всякой на то причины.
            if (intensity < 0.004f) intensity = 0f;

            var color = config.damageVignetteColor;
            color.a = intensity * Mathf.Clamp01(config.damageVignetteMax);
            view.color = color;
        }
    }
}
