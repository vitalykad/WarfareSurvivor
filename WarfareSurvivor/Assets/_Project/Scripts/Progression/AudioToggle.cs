using UnityEngine;
using UnityEngine.UI;

namespace WarfareSurvivor
{
    /// <summary>
    /// Кнопка «музыка» или «звук» в углу экрана.
    ///
    /// Состояние показывается двумя способами разом: яркостью значка
    /// и перечёркиванием. Одной яркости мало — на солнце и на плохом экране
    /// разница между тусклым и ярким пропадает, а перечёркнутый значок
    /// читается всегда.
    /// </summary>
    public class AudioToggle : MonoBehaviour
    {
        public enum Channel { Music, Effects }

        [SerializeField] ArenaConfig config;
        [SerializeField] Channel channel;
        [SerializeField] Image icon;
        [SerializeField] Image slash;

        void Start() => Refresh();

        /// <summary>Вешается на кнопку строителем сцены.</summary>
        public void Toggle()
        {
            if (config == null) return;

            if (channel == Channel.Music) config.musicOn = !config.musicOn;
            else config.sfxOn = !config.sfxOn;

            Refresh();
        }

        /// <summary>
        /// Обновляем и в Update: громкость можно поменять из инспектора
        /// или из кода, и кнопка не должна показывать неправду.
        /// </summary>
        void Update() => Refresh();

        void Refresh()
        {
            if (config == null) return;

            bool on = channel == Channel.Music ? config.musicOn : config.sfxOn;

            if (icon != null)
                icon.color = on ? Color.white : new Color(1f, 1f, 1f, 0.35f);

            if (slash != null && slash.enabled != !on)
                slash.enabled = !on;
        }
    }
}
