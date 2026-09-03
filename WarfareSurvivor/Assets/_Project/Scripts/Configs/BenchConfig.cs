using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Настройки стенда: экран сборки отряда перед забегом.
    ///
    /// Отдельным файлом, а не полями в конфиге арены, по той же причине,
    /// по какой стенд вообще существует: это инструмент разработки, а не
    /// часть игры. Его можно выключить одной галкой, и от забега не
    /// останется ни одной лишней строчки.
    ///
    /// Список классов заполняется кнопкой в инспекторе, а не руками:
    /// новый класс иначе молча не попадает на стенд, и выглядит это как
    /// «класс не работает», а не как «класс забыли добавить».
    /// </summary>
    [CreateAssetMenu(menuName = "WarfareSurvivor/Bench Config", fileName = "BenchConfig")]
    public class BenchConfig : ScriptableObject
    {
        [Header("Стенд")]

        [Tooltip("Спрашивать состав отряда перед забегом. Выключено — забег " +
                 "начинается сразу с составом из конфига арены.")]
        public bool askOnStart = true;

        [Tooltip("Какие классы показывать. Заполняется кнопкой «Собрать все " +
                 "классы» в инспекторе — руками сюда лазить незачем.")]
        public SurvivorClassSO[] classes = new SurvivorClassSO[0];

        [Tooltip("Сколько бойцов одного класса можно взять на старте.")]
        [Min(1)] public int maxPerClass = 12;

        [Tooltip("Сколько всего бойцов нужно набрать, чтобы кнопка «В бой» " +
                 "стала доступна.")]
        [Min(1)] public int minTotal = 1;

        [Header("Память стенда")]

        [Tooltip("Последний набранный состав. Пишется стендом при старте " +
                 "забега и подставляется в следующий раз — чтобы не набирать " +
                 "один и тот же отряд по десять раз подряд.")]
        public SquadEntry[] lastPick = new SquadEntry[0];

        /// <summary>Сколько таких бойцов взяли в прошлый раз.</summary>
        public int RememberedCount(SurvivorClassSO klass)
        {
            if (klass == null || lastPick == null) return 0;
            for (int i = 0; i < lastPick.Length; i++)
                if (lastPick[i].Class == klass) return Mathf.Max(0, lastPick[i].Count);
            return 0;
        }
    }
}
