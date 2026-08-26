using UnityEngine;
using UnityEngine.UI;

namespace WarfareSurvivor
{
    /// <summary>
    /// Что игрок видит по ходу забега: волна, остаток времени, полоска
    /// до пополнения и состав отряда.
    ///
    /// Состав здесь — не украшение. Ядро игры в том, из кого собран отряд,
    /// и если игрок не видит, кого у него сколько, решения на тир-апе
    /// он принимает вслепую.
    /// </summary>
    public class RunHud : MonoBehaviour
    {
        [SerializeField] RunController run;
        [SerializeField] SquadController squad;
        [SerializeField] Text waveLabel;
        [SerializeField] Text squadLabel;
        [SerializeField] Image sparkFill;
        [SerializeField] Text sparkLabel;
        [SerializeField] Text banner;

        int lastMembers = -1;
        int lastSparks = -1;

        /// <summary>Сколько осталось от толчка: 1 — только что подобрали, 0 — покой.</summary>
        float pulse;

        [SerializeField, Tooltip("За сколько секунд гаснет толчок счётчика.")]
        float pulseFade = 0.25f;

        [SerializeField, Tooltip("Насколько счётчик подскакивает при подборе.")]
        float pulseScale = 0.35f;

        Vector3 sparkLabelScale = Vector3.one;

        void Awake()
        {
            if (sparkLabel != null) sparkLabelScale = sparkLabel.transform.localScale;
        }

        void LateUpdate()
        {
            if (run == null) return;

            UpdateWave();
            UpdateSparks();
            UpdateSquad();
            UpdateBanner();
        }

        void UpdateWave()
        {
            if (waveLabel == null) return;

            switch (run.Current)
            {
                case RunController.Phase.Fighting:
                    waveLabel.text = $"ВОЛНА {run.WaveIndex + 1}/{run.WaveCount}   {Clock(run.TimeLeft)}";
                    break;
                case RunController.Phase.Break:
                    waveLabel.text = $"ВОЛНА ОТБИТА   следующая через {Mathf.CeilToInt(run.TimeLeft)}";
                    break;
                case RunController.Phase.Choosing:
                    // Подпись не гасим: выбирая пополнение, игрок должен
                    // видеть, в какую волну он с ним пойдёт.
                    waveLabel.text = $"ВОЛНА {run.WaveIndex + 1}/{run.WaveCount}";
                    break;
                default:
                    waveLabel.text = string.Empty;
                    break;
            }
        }

        void UpdateSparks()
        {
            if (sparkFill != null && run.SparksNeeded > 0)
                sparkFill.fillAmount = Mathf.Clamp01(run.Sparks / (float)run.SparksNeeded);

            if (sparkLabel == null) return;

            if (run.Sparks != lastSparks)
            {
                // Толчок ТОЛЬКО на прибавке. На сбросе после тир-апа счётчик
                // тоже меняется, но подпрыгивать ему там незачем — там уже
                // открывается окно выбора, и лишнее движение спорит с ним.
                if (run.Sparks > lastSparks) pulse = 1f;

                lastSparks = run.Sparks;
                sparkLabel.text = $"{run.Sparks} / {run.SparksNeeded}";
            }

            if (pulse <= 0f) return;

            pulse -= Time.unscaledDeltaTime / Mathf.Max(0.05f, pulseFade);
            pulse = Mathf.Max(0f, pulse);

            // Затухающий подскок: резкий рост и мягкий возврат читаются
            // как отклик на подбор, ровная синусоида — как мигание.
            float grow = 1f + pulseScale * pulse * pulse;
            sparkLabel.transform.localScale = sparkLabelScale * grow;
        }

        /// <summary>
        /// Состав пересобираем только когда он изменился: строка собирается
        /// склейкой, а кадр здесь дороже удобства.
        /// </summary>
        void UpdateSquad()
        {
            if (squadLabel == null || squad == null) return;
            if (squad.MemberCount == lastMembers) return;
            lastMembers = squad.MemberCount;

            int melee = 0, ranged = 0, support = 0;
            var members = squad.Members;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] == null || members[i].Class == null) continue;
                switch (members[i].Class.role)
                {
                    case SquadRole.Melee: melee++; break;
                    case SquadRole.Ranged: ranged++; break;
                    default: support++; break;
                }
            }

            var text = new System.Text.StringBuilder(48);
            text.Append("ОТРЯД ").Append(squad.MemberCount);
            if (melee > 0) text.Append("   лопаты ").Append(melee);
            if (ranged > 0) text.Append("   стрелки ").Append(ranged);
            if (support > 0) text.Append("   ядро ").Append(support);
            squadLabel.text = text.ToString();
        }

        void UpdateBanner()
        {
            if (banner == null) return;

            switch (run.Current)
            {
                case RunController.Phase.Won:
                    banner.text = $"ОТБИЛИСЬ\nтир-апов взято: {run.TierUpsTaken}";
                    break;
                case RunController.Phase.Lost:
                    banner.text = $"ОТРЯД ВЫБИТ\nволна {run.WaveIndex + 1} из {run.WaveCount}";
                    break;
                default:
                    if (banner.text.Length > 0) banner.text = string.Empty;
                    break;
            }
        }

        static string Clock(float seconds)
        {
            int whole = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{whole / 60}:{whole % 60:00}";
        }
    }
}
