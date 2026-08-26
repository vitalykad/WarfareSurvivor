using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Весь звук игры в одном месте: музыка и короткие эффекты.
    ///
    /// Громкость живёт в <see cref="ArenaConfig"/> и читается каждый кадр,
    /// как камера и свет: значение можно крутить прямо во время игры,
    /// и подобранное не откатывается на выходе из режима игры.
    ///
    /// Эффекты играются через СВОЙ пул источников, а не через
    /// PlayClipAtPoint. Тот создаёт объект на каждый звук и уничтожает
    /// его по окончании — при двадцати бойцах, бьющих дважды в секунду,
    /// это сорок объектов в секунду на ровном месте.
    /// </summary>
    public class AudioDirector : MonoBehaviour
    {
        public static AudioDirector Instance { get; private set; }

        [SerializeField] ArenaConfig config;

        [SerializeField] AudioClip music;
        [SerializeField] AudioClip shovelHit;
        [SerializeField] AudioClip pistolShot;

        [SerializeField, Tooltip("Сколько эффектов может звучать одновременно.")]
        int voices = 8;

        AudioSource musicSource;
        AudioSource[] sfx;
        int nextVoice;

        /// <summary>Когда этому клипу можно звучать снова.</summary>
        float shovelReady;
        float pistolReady;

        void Awake()
        {
            Instance = this;

            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.clip = music;
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;

            sfx = new AudioSource[Mathf.Max(1, voices)];
            for (int i = 0; i < sfx.Length; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;

                // Двумерный звук: камера смотрит сверху, расстояние до
                // источника почти одинаково для всей сцены, и объёмность
                // дала бы только лишние расчёты.
                source.spatialBlend = 0f;
                sfx[i] = source;
            }

            if (music != null) musicSource.Play();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (config == null) return;

            // Каждый кадр, а не при старте: громкость должна отзываться
            // на правку в инспекторе сразу.
            musicSource.volume = Mathf.Clamp01(config.musicVolume);

            float effects = Mathf.Clamp01(config.sfxVolume);
            for (int i = 0; i < sfx.Length; i++) sfx[i].volume = effects;
        }

        /// <summary>
        /// Громкость музыки, 0..1. Хранится в конфиге, а не на компоненте:
        /// правки объектов сцены откатываются на выходе из режима игры,
        /// а правки конфига остаются — подобранное на слух не теряется.
        /// </summary>
        public float MusicVolume
        {
            get => config != null ? config.musicVolume : 0f;
            set { if (config != null) config.musicVolume = Mathf.Clamp01(value); }
        }

        /// <summary>Громкость эффектов, 0..1.</summary>
        public float SfxVolume
        {
            get => config != null ? config.sfxVolume : 0f;
            set { if (config != null) config.sfxVolume = Mathf.Clamp01(value); }
        }

        /// <summary>Заглушить или вернуть звук целиком.</summary>
        public void Mute(bool silent)
        {
            AudioListener.volume = silent ? 0f : 1f;
        }

        public static void ShovelHit() => Instance?.PlayShovel();
        public static void PistolShot() => Instance?.PlayPistol();

        void PlayShovel()
        {
            if (Time.unscaledTime < shovelReady) return;
            shovelReady = Time.unscaledTime + Interval;
            Play(shovelHit);
        }

        void PlayPistol()
        {
            if (Time.unscaledTime < pistolReady) return;
            pistolReady = Time.unscaledTime + Interval;
            Play(pistolShot);
        }

        float Interval => config != null ? Mathf.Max(0f, config.sfxMinInterval) : 0.05f;

        /// <summary>
        /// Играет клип на свободном голосе по кругу.
        ///
        /// Пауза между повторами одного клипа обязательна: двадцать бойцов
        /// бьют почти синхронно, и без неё двадцать одинаковых звуков
        /// складываются в один громкий щелчок вместо частой дроби —
        /// то же самое сложение фаз, что даёт «металлический» призвук.
        /// </summary>
        void Play(AudioClip clip)
        {
            if (clip == null || sfx == null || sfx.Length == 0) return;

            var source = sfx[nextVoice];
            nextVoice = (nextVoice + 1) % sfx.Length;

            source.clip = clip;
            source.Play();
        }
    }
}
