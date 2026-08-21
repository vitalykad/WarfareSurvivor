using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WarfareSurvivor
{
    /// <summary>
    /// Экранный джойстик. Скрипт висит на прозрачной зоне ввода, которая ловит
    /// касания; сама «шайба» — два дочерних Image.
    ///
    /// Два режима, потому что они решают разные задачи. Fixed — база всегда на
    /// одном месте: палец знает, куда возвращаться, но надо попасть.
    /// Floating — база прыгает под палец: попадать не нужно, зато у джойстика
    /// нет постоянного места на экране. Дефолт — Floating с возвратом базы
    /// домой, самый прощающий вариант на телефоне.
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public enum Mode { Floating, Fixed }

        [SerializeField] Mode mode = Mode.Floating;
        [SerializeField] RectTransform background;
        [SerializeField] RectTransform handle;

        [Tooltip("Насколько далеко уводится шайба от центра, в пикселях канваса.")]
        [SerializeField] float radius = 110f;

        [Tooltip("Мёртвая зона в долях радиуса: гасит дрожь пальца у центра.")]
        [SerializeField] float deadZone = 0.12f;

        [Tooltip("Скрывать джойстик, пока экран не тронут (только для Floating).")]
        [SerializeField] bool hideWhenIdle = false;

        RectTransform self;
        Canvas canvas;
        Vector2 homePosition;
        int activePointer = -1;

        /// <summary>Направление -1..1 по обеим осям. Ноль — палец отпущен.</summary>
        public Vector2 Value { get; private set; }

        void Awake()
        {
            self = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
            if (background != null) homePosition = background.anchoredPosition;
            SetVisible(!(hideWhenIdle && mode == Mode.Floating));
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (activePointer != -1) return;
            activePointer = e.pointerId;

            if (mode == Mode.Floating && background != null &&
                ScreenToAnchored(e.position, e.pressEventCamera, out var anchored))
                background.anchoredPosition = anchored;

            SetVisible(true);
            OnDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != activePointer || background == null) return;
            if (!ScreenToAnchored(e.position, e.pressEventCamera, out var anchored)) return;

            var delta = anchored - background.anchoredPosition;
            var clamped = Vector2.ClampMagnitude(delta, radius);
            if (handle != null) handle.anchoredPosition = clamped;

            var raw = clamped / radius;
            // Мёртвую зону вычитаем, а не обнуляем: иначе на её границе
            // отряд дёргается со стоящего на полную скорость.
            var magnitude = raw.magnitude;
            Value = magnitude <= deadZone
                ? Vector2.zero
                : raw.normalized * ((magnitude - deadZone) / (1f - deadZone));
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != activePointer) return;
            activePointer = -1;
            Value = Vector2.zero;

            if (handle != null) handle.anchoredPosition = Vector2.zero;
            if (mode == Mode.Floating && background != null)
            {
                background.anchoredPosition = homePosition;
                if (hideWhenIdle) SetVisible(false);
            }
        }

        /// <summary>
        /// Переводит точку касания в ТУ ЖЕ систему координат, в которой живёт
        /// anchoredPosition базы.
        ///
        /// Здесь была ошибка, из-за которой джойстик пропадал при нажатии:
        /// ScreenPointToLocalPointInRectangle отсчитывает точку от ПИВОТА зоны
        /// (центр экрана), а база привязана к левому нижнему углу и её
        /// anchoredPosition отсчитывается от него же. Присвоение центровой
        /// координаты элементу с угловым якорем уносило базу на пол-экрана
        /// за край, и она просто исчезала.
        ///
        /// Пересчёт идёт через якорь базы, а не через константу, чтобы правка
        /// не развалилась, если базу перевесят на другой угол.
        /// </summary>
        bool ScreenToAnchored(Vector2 screen, Camera cam, out Vector2 anchored)
        {
            anchored = Vector2.zero;

            var eventCam = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cam;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(self, screen, eventCam, out var local))
                return false;

            var rect = self.rect;
            // rect.x / rect.y — это левый нижний угол зоны в её локальных
            // координатах (для пивота 0.5 они отрицательные).
            var fromCorner = local - new Vector2(rect.x, rect.y);
            var anchorPoint = background != null
                ? new Vector2(rect.width * background.anchorMin.x, rect.height * background.anchorMin.y)
                : Vector2.zero;

            anchored = fromCorner - anchorPoint;
            return true;
        }

        void SetVisible(bool visible)
        {
            if (background != null) background.gameObject.SetActive(visible);
        }

        /// <summary>
        /// Клавиатура как запасной ввод: на телефоне её нет, но в редакторе
        /// проверять отряд мышью по джойстику неудобно.
        /// </summary>
        public Vector2 ReadWithKeyboardFallback()
        {
            if (Value.sqrMagnitude > 0.0001f) return Value;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;
            var v = Vector2.zero;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
            return Vector2.ClampMagnitude(v, 1f);
#else
            return Vector2.zero;
#endif
        }
    }
}
