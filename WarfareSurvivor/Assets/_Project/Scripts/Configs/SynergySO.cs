using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>Знак синергии: прибавка, убавка или что-то, не сводимое к числу.</summary>
    public enum SynergyKind
    {
        /// <summary>«+»: параметр растёт.</summary>
        Bonus = 0,

        /// <summary>«−»: параметр падает. Плата за сильную сторону роли.</summary>
        Penalty = 1,

        /// <summary>«другое»: правило, а не прибавка к числу. Разбирается кодом по метке.</summary>
        Special = 2
    }

    /// <summary>В чём мерится прибавка.</summary>
    public enum SynergyAmount
    {
        /// <summary>Проценты. Складываются между собой, потом применяются разом.</summary>
        Percent = 0,

        /// <summary>Единицы параметра: метры дальности, секунды и прочее.</summary>
        Flat = 1
    }

    /// <summary>
    /// Одна синергия: «+ урон ближнего боя», «− скорость толпы» и так далее.
    ///
    /// ОТДЕЛЬНЫЙ АССЕТ, а не поля внутри класса. Причина простая: одна и та же
    /// синергия висит на разных ролях — «+ урон ближнего боя» есть и у
    /// Тяжеловеса, и у Копа. Впиши её в класс — и на тесте, поправив число у
    /// одного, забудешь про второго, а расхождение вылезет через десяток
    /// забегов как необъяснимый перекос. Ассет правится один раз для всех,
    /// кто на него ссылается.
    ///
    /// Прибавка задаётся НА ОДНОГО БОЙЦА. Так это и описано в документе ролей:
    /// каждый отдельный чел роли X даёт толпе небольшой прирост, и чем больше
    /// таких челов, тем больше суммарный прирост. Общая величина считается
    /// умножением на число бойцов этой роли в строю.
    /// </summary>
    [CreateAssetMenu(menuName = "WarfareSurvivor/Синергия", fileName = "Synergy")]
    public class SynergySO : ScriptableObject
    {
        [Tooltip("Знак: прибавка, убавка или правило, не сводимое к числу.")]
        public SynergyKind kind = SynergyKind.Bonus;

        [Tooltip("Какой параметр меняем. Список расширяется в коде: параметр " +
                 "обязан на что-то влиять, а влияние живёт не в ассете.")]
        public SquadStat stat = SquadStat.MeleeDamage;

        [Tooltip("В чём мерить прибавку: в процентах или в единицах параметра.")]
        public SynergyAmount amountType = SynergyAmount.Percent;

        [Tooltip("Сколько даёт ОДИН боец этой роли. Всегда положительное " +
                 "число: знак берётся из поля выше.\n\n" +
                 "Так сделано нарочно. Хранить убавку отрицательным числом " +
                 "значит однажды написать минус дважды и получить прибавку " +
                 "там, где задумывалась плата.")]
        [Min(0f)] public float amountPerUnit = 3f;

        [Tooltip("Прибавка растёт от числа бойцов роли. Выключить — и синергия " +
                 "даётся один раз за само присутствие роли в отряде, сколько " +
                 "бы её ни набрали. Нужно для проверок на тесте: так видно, " +
                 "что именно ощущается — сам факт роли или её количество.")]
        public bool scalesWithCount = true;

        [Tooltip("Для «другого» — метка, по которой правило разбирает код. " +
                 "Для остальных — свободная заметка, ни на что не влияет.")]
        public string note = "";

        /// <summary>Величина со знаком: убавка уходит в минус здесь и только здесь.</summary>
        public float Signed => kind == SynergyKind.Penalty ? -amountPerUnit : amountPerUnit;

        /// <summary>Числовая синергия, которую есть смысл складывать.</summary>
        public bool IsNumeric => kind != SynergyKind.Special;

        public string Sign
        {
            get
            {
                switch (kind)
                {
                    case SynergyKind.Bonus:   return "+";
                    case SynergyKind.Penalty: return "−";
                    default:                  return "◆";
                }
            }
        }

        /// <summary>Как синергия читается человеком: то же, что в документе ролей.</summary>
        public string Describe()
        {
            if (kind == SynergyKind.Special)
                return "◆ " + (string.IsNullOrEmpty(note) ? "особое правило" : note);

            string value = amountType == SynergyAmount.Percent
                ? amountPerUnit.ToString("0.##") + "%"
                : amountPerUnit.ToString("0.##");

            string tail = scalesWithCount ? " за бойца" : " за роль";
            return Sign + " " + value + " " + SquadStatInfo.Label(stat) + tail;
        }

        /// <summary>Короткая запись без чисел — как в таблице ролей.</summary>
        public string ShortDescribe()
        {
            return kind == SynergyKind.Special
                ? "◆ " + (string.IsNullOrEmpty(note) ? "особое правило" : note)
                : Sign + " " + SquadStatInfo.Label(stat);
        }

        /// <summary>Имя файла, выводимое из содержания. Без кириллицы — путями меньше сюрпризов.</summary>
        public string SuggestedFileName()
        {
            string k = kind == SynergyKind.Bonus ? "Plus" : kind == SynergyKind.Penalty ? "Minus" : "Special";
            return "Synergy_" + k + "_" + stat;
        }
    }
}
