using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Стенд для кислотного плевка: один снаряд крупно и без толпы.
    ///
    /// Нужен потому, что в бою снаряд занимает пару десятков пикселей среди
    /// полусотни зомби, цифр урона и бутылок — по такому кадру нельзя понять,
    /// что именно нарисовано. Три захода правок по боевым снимкам ушли впустую
    /// ровно из-за этого.
    ///
    /// Здесь снаряд летит поперёк кадра, камера стоит вплотную, фон пустой.
    /// Видно каждую точку ленты.
    /// </summary>
    public class SpitLab : MonoBehaviour
    {
        [Tooltip("Конфиг. Публичный НАРОЧНО: строитель сцены присваивает его " +
                 "напрямую — через SerializedObject ссылка в сцену не сохранилась, " +
                 "и стенд молча падал каждый кадр, ничего не стреляя.")]
        public ArenaConfig config;

        [Tooltip("Сколько метров пролетает снаряд поперёк кадра.")]
        public float distance = 8f;

        [Tooltip("Сколько секунд длится полёт. Медленнее, чем в бою: " +
                 "стенд нужен, чтобы РАЗГЛЯДЕТЬ, а не чтобы воспроизвести темп.")]
        public float flightTime = 4f;

        [Tooltip("Пауза между запусками.")]
        public float pause = 1.2f;

        [Tooltip("Высота горки. Ноль — летит по прямой поперёк кадра, " +
                 "так лента видна целиком и не уходит вверх за край.")]
        public float arc;

        [Tooltip("Радиус взрыва в конце полёта. Ноль — снаряд просто гаснет.")]
        public float blastRadius = 1.8f;

        float nextLaunch;

        void Start()
        {
            if (config == null)
            {
                Debug.LogError("[Стенд плевка] Не задан конфиг — стрелять нечем.", this);
                enabled = false;
                return;
            }

            AcidDrop.Configure(config, Camera.main);
            AcidCloud.Configure(config, Camera.main);
            AcidZone.Configure(config);
            AcidBlast.Configure(config, Camera.main);
        }

        void Update()
        {
            if (config == null) return;
            if (Time.time < nextLaunch) return;
            nextLaunch = Time.time + Mathf.Max(0.1f, flightTime + pause);

            float half = Mathf.Max(0.5f, distance) * 0.5f;
            // Прилетает НА ЗЕМЛЮ, как в бою. Горизонтальный полёт на высоте
            // разводил вспышку и кольцо по экрану: кольцо ложится на землю,
            // и стенд врал бы про то, как взрыв выглядит на самом деле.
            var from = new Vector3(-half, 1.6f, 0f);
            var to = new Vector3(half, 0.05f, 0f);

            // Горку стенд по умолчанию не делает: она уводит снаряд вверх
            // за край кадра, а разглядывать надо ленту, а не траекторию.
            float keep = config.acidArcHeight;
            config.acidArcHeight = arc;
            AcidDrop.Spit(from, to, flightTime, blastRadius, 0f, null);
            config.acidArcHeight = keep;
        }
    }
}
