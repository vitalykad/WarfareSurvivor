using UnityEngine;
using UnityEngine.Rendering;

namespace WarfareSurvivor
{
    /// <summary>
    /// Держит солнце и непрямой свет по значениям из конфига.
    ///
    /// Читает их каждый кадр — свет невозможно подобрать по одному стоп-кадру,
    /// его крутят, глядя на движущуюся картинку. Правки ScriptableObject
    /// в Play-режиме не откатываются на выходе, так что подобранное в бою
    /// значение останется.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class LightRig : MonoBehaviour
    {
        [SerializeField] ArenaConfig config;

        Light sun;

        void Awake()
        {
            sun = GetComponent<Light>();
            if (config != null) return;

            Debug.LogError($"[{name}] Не задан config. Свет останется как есть.", this);
            enabled = false;
        }

        void LateUpdate() => Apply(config, sun);

        /// <summary>
        /// Общая для игры и генератора сцены точка применения. Генератор
        /// зовёт её же, чтобы сохранённая сцена не расходилась с конфигом
        /// до первого запуска.
        /// </summary>
        public static void Apply(ArenaConfig config, Light sun)
        {
            if (config == null) return;

            if (sun != null)
            {
                sun.color = config.sunColor;
                sun.intensity = config.sunIntensity;
                sun.transform.rotation = Quaternion.Euler(
                    Mathf.Clamp(config.sunPitch, 1f, 89f), config.sunYaw, 0f);
            }

            // Trilight — это и есть Gradient в окне Lighting. Три константы
            // вместо сферических гармоник со скайбокса: и управляемее,
            // и дешевле.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = config.ambientSky;
            RenderSettings.ambientEquatorColor = config.ambientHorizon;
            RenderSettings.ambientGroundColor = config.ambientGround;
        }
    }
}
