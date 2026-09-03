namespace WarfareSurvivor
{
    /// <summary>
    /// Что синергия усиливает или ослабляет.
    ///
    /// ПЕРЕЧИСЛЕНИЕ, А НЕ АССЕТ — сознательно. Параметр отличается от синергии
    /// тем, что его мало создать: он обязан на что-то влиять, а влияние живёт
    /// в коде. Ассет-параметр позволил бы завести «шанс поджога» мышкой и
    /// получить настройку, которая молча ничего не делает, — а такую поломку
    /// в балансе не видно, пока не начнёшь мерить.
    ///
    /// Здесь же наоборот: новый параметр стоит одну строчку и сразу видно,
    /// кто его читает. Порядок значения не имеет, но менять его не надо —
    /// значения сохранены в ассетах синергий по номеру.
    /// </summary>
    public enum SquadStat
    {
        MeleeDamage = 0,
        RangedDamage = 1,
        AttackRange = 2,
        AttackSpeed = 3,
        CritChance = 4,
        DotDamage = 5,
        StunChance = 6,
        DamageReduction = 7,
        MoveSpeed = 8,
        CardTierChance = 9
    }

    /// <summary>
    /// Русские названия параметров и то, чем каждый из них сейчас является
    /// для игры: настройкой, которую кто-то читает, или пока только записью.
    ///
    /// Второе поле важнее, чем кажется. Синергию можно собрать на любой
    /// параметр, но пока её никто не читает, она в бою не меняет ничего —
    /// и на тесте это выглядит как «синергия слабая», а не как «синергии нет».
    /// Библиотека синергий показывает эту пометку прямо в списке.
    /// </summary>
    public static class SquadStatInfo
    {
        public static string Label(SquadStat stat)
        {
            switch (stat)
            {
                case SquadStat.MeleeDamage:     return "урон ближнего боя";
                case SquadStat.RangedDamage:    return "урон дальнего боя";
                case SquadStat.AttackRange:     return "дальность атаки";
                case SquadStat.AttackSpeed:     return "скорость атаки";
                case SquadStat.CritChance:      return "крит шанс";
                case SquadStat.DotDamage:       return "урон от DoT";
                case SquadStat.StunChance:      return "шанс оглушить";
                case SquadStat.DamageReduction: return "снижение получаемого урона";
                case SquadStat.MoveSpeed:       return "скорость толпы";
                case SquadStat.CardTierChance:  return "шанс на более высокий тир карточки";
                default:                        return stat.ToString();
            }
        }

        /// <summary>Есть ли в игре механика, которая этот параметр читает.</summary>
        public static bool HasConsumer(SquadStat stat)
        {
            switch (stat)
            {
                // Пока не читает никто: система синергий собрана как настройка,
                // подключение к бою — отдельный шаг.
                default: return false;
            }
        }

        /// <summary>Чего не хватает, чтобы параметр заработал. Для подсказки в окне.</summary>
        public static string Missing(SquadStat stat)
        {
            switch (stat)
            {
                case SquadStat.MeleeDamage:     return "умножить Survivor.Damage у ближнего боя";
                case SquadStat.RangedDamage:    return "умножить Survivor.Damage у стрелков";
                case SquadStat.AttackRange:     return "умножить klass.attackRange при выборе цели";
                case SquadStat.AttackSpeed:     return "поделить klass.attackInterval";
                case SquadStat.CritChance:      return "механики крита в игре ещё нет";
                case SquadStat.DotDamage:       return "механики урона по времени ещё нет";
                case SquadStat.StunChance:      return "механики оглушения ещё нет";
                case SquadStat.DamageReduction: return "уменьшить урон в Survivor.TakeHit";
                case SquadStat.MoveSpeed:       return "умножить скорость в SquadController";
                case SquadStat.CardTierChance:  return "механики тиров карточек ещё нет";
                default:                        return "нет потребителя";
            }
        }
    }
}
