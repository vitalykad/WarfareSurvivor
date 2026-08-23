using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace WarfareSurvivor
{
    /// <summary>
    /// Счётчик кадра со счётчиками нагрузки рядом.
    ///
    /// Показывает ХУДШИЙ кадр в окне, а не только средний: игру рвут отдельные
    /// длинные кадры, и среднее их прячет (§9 CROWD_PROJECT_LESSONS).
    ///
    /// Рядом с кадром выводится то, за что в этом проекте платят: число
    /// зомби и бойцов, а главное — ПЛОЩАДЬ ПЕРЕРИСОВКИ трасс. В прошлом
    /// проекте все просадки оказались про площадь, а видно её не было —
    /// и это записано отдельным пунктом в «что бы я сделал иначе».
    ///
    /// Где возможно, разделяет время CPU и GPU: это сразу отвечает,
    /// упёрлись мы в скрипты или в заливку, и избавляет от гадания.
    /// </summary>
    public class FrameMeter : MonoBehaviour
    {
        [SerializeField] ArenaConfig config;
        [SerializeField] Text label;

        /// <summary>Сколько секунд после старта не считаем: там кадр загрузки.</summary>
        const float WarmupSeconds = 1.5f;

        readonly StringBuilder text = new StringBuilder(256);

        float windowStart;
        float windowWorst;
        float windowTotal;
        int windowFrames;

        float sessionWorst;
        float nextLogTime;
        float warmupUntil;

        // последние посчитанные значения — их же пишем в лог
        float shownAverage;
        float shownWorst;
        float cpuMs;
        float gpuMs;

        void Awake()
        {
            if (config == null)
            {
                Debug.LogError($"[{name}] Не задан config. Замер выключен.", this);
                enabled = false;
                return;
            }

            if (label != null) label.enabled = config.showFrameMeter;
            windowStart = Time.unscaledTime;
            nextLogTime = Time.unscaledTime + config.frameMeterLogInterval;

            // Первый кадр после загрузки сцены длится секунды: в нём грузятся
            // меши и компилируются шейдеры. Записав его в «худший за сеанс»,
            // мы получили бы 9 секунд, рядом с которыми настоящие просадки
            // не видны вовсе.
            warmupUntil = Time.unscaledTime + WarmupSeconds;
        }

        void Update()
        {
            // Считаем в НЕмасштабированном времени: пауза и замедление не должны
            // выглядеть просадкой.
            float ms = Time.unscaledDeltaTime * 1000f;

            if (Time.unscaledTime < warmupUntil)
            {
                windowStart = Time.unscaledTime;
                windowWorst = 0f;
                windowTotal = 0f;
                windowFrames = 0;
                return;
            }

            windowFrames++;
            windowTotal += ms;
            if (ms > windowWorst) windowWorst = ms;

            float now = Time.unscaledTime;
            if (now - windowStart < Mathf.Max(0.1f, config.frameMeterWindow)) return;

            shownAverage = windowTotal / Mathf.Max(1, windowFrames);
            shownWorst = windowWorst;
            if (shownWorst > sessionWorst) sessionWorst = shownWorst;

            ReadGpuTimings();
            Compose();

            if (label != null && config.showFrameMeter) label.text = text.ToString();

            if (now >= nextLogTime)
            {
                nextLogTime = now + Mathf.Max(1f, config.frameMeterLogInterval);
                Debug.Log("[Кадр] " + text.ToString().Replace('\n', ' '));
            }

            windowStart = now;
            windowWorst = 0f;
            windowTotal = 0f;
            windowFrames = 0;
        }

        /// <summary>
        /// Разделение CPU/GPU доступно не везде: на части драйверов
        /// FrameTimingManager молчит. Тогда просто не показываем эти цифры,
        /// а не выдумываем их.
        /// </summary>
        void ReadGpuTimings()
        {
            cpuMs = 0f;
            gpuMs = 0f;

            FrameTimingManager.CaptureFrameTimings();
            var timings = new FrameTiming[1];
            if (FrameTimingManager.GetLatestTimings(1, timings) == 0) return;

            cpuMs = (float)timings[0].cpuFrameTime;
            gpuMs = (float)timings[0].gpuFrameTime;
        }

        void Compose()
        {
            text.Clear();

            text.Append(Mathf.RoundToInt(1000f / Mathf.Max(shownAverage, 0.01f))).Append(" fps   ")
                .Append(shownAverage.ToString("F1")).Append(" мс сред\n");

            // Худший кадр — главная цифра. Целевой бюджет 16.7 мс.
            text.Append("худший ").Append(shownWorst.ToString("F1"))
                .Append(" мс   за сеанс ").Append(sessionWorst.ToString("F1")).Append(" мс\n");

            if (cpuMs > 0f || gpuMs > 0f)
                text.Append("cpu ").Append(cpuMs.ToString("F1"))
                    .Append("   gpu ").Append(gpuMs.ToString("F1")).Append("\n");

            text.Append("зомби ").Append(Registry.Zombies.Count)
                .Append("   бойцов ").Append(Registry.Survivors.Count).Append('\n');

            // Площадь перерисовки трасс — та самая величина, которой в прошлом
            // проекте не хватало на экране.
            text.Append("трасс ").Append(TracerLayer.ActiveCount)
                .Append("   площадь ").Append(TracerLayer.ActiveArea.ToString("F1")).Append(" м²");
        }
    }
}
