using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Задаёт частоту кадров из конфига.
    ///
    /// Нужен потому, что умолчание Unity на мобильных — 30 кадров, независимо
    /// от развёртки экрана и от того, выключена ли вертикальная синхронизация.
    /// Пока это число не выставлено явно, замер показывает ровно 33.3 мс
    /// с разбросом в десятые доли — картина, неотличимая от упора
    /// в производительность, хотя на деле игра просто ждёт.
    ///
    /// Отдельный компонент, а не строчка в измерителе: измеритель — отладочный
    /// и выключается, а частота кадров нужна всегда.
    /// </summary>
    public class FrameRateController : MonoBehaviour
    {
        [SerializeField] ArenaConfig config;

        int appliedRate = int.MinValue;
        int appliedVSync = int.MinValue;

        void Awake()
        {
            if (config != null) return;

            Debug.LogError($"[{name}] Не задан config. Частота кадров останется умолчанием платформы.", this);
            enabled = false;
        }

        // Читаем каждый кадр, как камера и свет: число подбирают на ходу,
        // сравнивая с показаниями измерителя.
        void Update()
        {
            if (config.vSyncCount != appliedVSync)
            {
                appliedVSync = Mathf.Clamp(config.vSyncCount, 0, 2);
                QualitySettings.vSyncCount = appliedVSync;
            }

            if (config.targetFrameRate == appliedRate) return;

            appliedRate = Mathf.Max(1, config.targetFrameRate);
            Application.targetFrameRate = appliedRate;
            Debug.Log($"[Кадр] Целевая частота: {appliedRate}, vSync: {appliedVSync}");
        }
    }
}
