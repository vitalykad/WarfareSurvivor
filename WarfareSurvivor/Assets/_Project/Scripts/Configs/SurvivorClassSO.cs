using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Роль бойца в строю. ПОРЯДОК ЗНАЧИМ: он же задаёт порядок колец изнутри
    /// наружу. Кто ничего не делает по площади — в центр, кто бьёт вплотную —
    /// наружу, стрелки между ними.
    /// </summary>
    public enum SquadRole
    {
        /// <summary>Лечит, баффает, сам не атакует. Самое защищённое место — центр.</summary>
        Support = 0,

        /// <summary>Стреляет на дистанции.</summary>
        Ranged = 1,

        /// <summary>Бьёт вблизи — значит стоит там, куда враг приходит первым.</summary>
        Melee = 2
    }

    /// <summary>
    /// Класс выжившего: один ассет на класс, ни строчки кода на добавление.
    /// Ровно то требование к архитектуре, которое записано в §5 project.md —
    /// классы будут добавляться по ходу игры, и каждый раз трогать код нельзя.
    /// </summary>
    [CreateAssetMenu(menuName = "WarfareSurvivor/Survivor Class", fileName = "SurvivorClass")]
    public class SurvivorClassSO : ScriptableObject
    {
        [Header("Кто это")]
        public string displayName = "Класс";

        [Tooltip("Префаб бойца. Собирается меню WarfareSurvivor/Setup.")]
        public GameObject prefab;

        [Tooltip("Роль. Определяет, на каком кольце строя стоит боец, " +
                 "и как он ведёт бой.")]
        public SquadRole role = SquadRole.Ranged;

        [Header("Бой")]
        public float maxHealth = 100f;

        [Tooltip("Дальность атаки в метрах. У ближнего боя это НЕ вплотную: " +
                 "лопатой достают с замахом, и бить надо раньше, чем враг " +
                 "дошёл до тела.")]
        public float attackRange = 9f;

        [Tooltip("Секунд между атаками — темп боя этого класса.\n\n" +
                 "У ближнего боя это число управляет и скоростью замаха: если " +
                 "интервал короче анимации удара, клип проигрывается быстрее, " +
                 "чтобы успеть. Иначе темп упирался бы в длину анимации и " +
                 "настройка ничего не меняла.")]
        public float attackInterval = 0.9f;

        public float damage = 10f;

        [Tooltip("На сколько метров отбрасывает врага. Ноль — не отбрасывает " +
                 "вовсе, так у стрелков.")]
        public float knockbackDistance;

        [Tooltip("В какой момент анимации удара наносится урон, 0..1. " +
                 "Половина — на середине замаха. Наносить урон в момент " +
                 "запуска анимации нельзя: враг падает раньше, чем лопата " +
                 "до него дошла, и удар выглядит несвязанным с попаданием.")]
        [Range(0f, 1f)] public float attackHitTime = 0.5f;

        [Tooltip("Сколько длится полёт от отбрасывания, секунд. Слишком " +
                 "длинный полёт превращает толпу в облако разлетающихся тел.")]
        public float knockbackDuration = 0.25f;

        /// <summary>Пассивные классы в бою не участвуют — им незачем искать цель.</summary>
        public bool Attacks => role != SquadRole.Support && damage > 0f;
    }
}
