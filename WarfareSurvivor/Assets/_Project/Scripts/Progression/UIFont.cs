using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Встроенный шрифт для служебного интерфейса.
    ///
    /// Отдельным местом, потому что имя ресурса менялось между версиями
    /// Unity: старое Arial.ttf в новых сборках отсутствует, и Text молча
    /// остаётся без шрифта — текст есть, а на экране пусто.
    /// </summary>
    public static class UIFont
    {
        static Font cached;

        /// <summary>
        /// Шрифт из конфига, а если там пусто — встроенный.
        ///
        /// Через конфиг, потому что часть текста создаётся в рантайме
        /// (карточки тир-апа, цифры урона), и связать их с ассетом
        /// в редакторе нечем.
        /// </summary>
        public static Font Get(ArenaConfig config)
        {
            if (config != null && config.uiFont != null) return config.uiFont;
            return Get();
        }

        public static Font Get()
        {
            if (cached != null) return cached;

            cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (cached == null) cached = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return cached;
        }
    }
}
