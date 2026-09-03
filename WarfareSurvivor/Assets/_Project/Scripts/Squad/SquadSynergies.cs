using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Складывает синергии всего отряда в набор итоговых чисел.
    ///
    /// Считается ПО СОБЫТИЮ, а не каждый кадр: состав меняется на тир-апе и
    /// на смерти бойца, то есть единицы раз за забег, а обход отряда с его
    /// списками синергий на каждом кадре при полутора десятках бойцов —
    /// работа впустую. Пересчёт помечается грязным флагом и происходит
    /// на первом же чтении.
    ///
    /// Проценты СКЛАДЫВАЮТСЯ, а не перемножаются. Десять бойцов по +3% дают
    /// +30%, а не +34.4%. Перемножение выглядит честнее, но на тесте по нему
    /// нельзя посчитать в уме, что даст следующая карточка, — а решение на
    /// тир-апе игрок принимает именно в уме.
    /// </summary>
    public sealed class SquadSynergies
    {
        /// <summary>Одна строка итога — для отладки и для интерфейса.</summary>
        public struct Line
        {
            public SquadStat stat;
            public float percent;
            public float flat;
        }

        static readonly int StatCount = System.Enum.GetValues(typeof(SquadStat)).Length;

        readonly float[] percent = new float[StatCount];
        readonly float[] flat = new float[StatCount];
        readonly Dictionary<SurvivorClassSO, int> counts = new Dictionary<SurvivorClassSO, int>();
        readonly List<SynergySO> specials = new List<SynergySO>();

        bool dirty = true;

        /// <summary>Состав изменился — пересчитать на следующем чтении.</summary>
        public void Invalidate() => dirty = true;

        /// <summary>Суммарные проценты по параметру: 30 значит «на 30% больше».</summary>
        public float Percent(SquadStat stat) => percent[(int)stat];

        /// <summary>Суммарная прибавка в единицах параметра.</summary>
        public float Flat(SquadStat stat) => flat[(int)stat];

        /// <summary>
        /// Готовый множитель: 1.3 при +30%. Не опускается ниже нуля —
        /// иначе достаточная стопка убавок вывернула бы урон в отрицательный.
        /// </summary>
        public float Multiplier(SquadStat stat) => Mathf.Max(0f, 1f + percent[(int)stat] * 0.01f);

        /// <summary>Особые синергии — те, что кодом разбираются по метке.</summary>
        public IReadOnlyList<SynergySO> Specials => specials;

        /// <summary>Пересчитывает, если состав менялся. Дёргать можно свободно.</summary>
        public void Refresh(IReadOnlyList<Survivor> living)
        {
            if (!dirty) return;
            dirty = false;

            System.Array.Clear(percent, 0, percent.Length);
            System.Array.Clear(flat, 0, flat.Length);
            counts.Clear();
            specials.Clear();

            if (living == null) return;

            for (int i = 0; i < living.Count; i++)
            {
                var member = living[i];
                if (member == null || member.Class == null) continue;

                counts.TryGetValue(member.Class, out int n);
                counts[member.Class] = n + 1;
            }

            foreach (var pair in counts)
            {
                var klass = pair.Key;
                int count = pair.Value;
                if (klass.synergies == null) continue;

                for (int i = 0; i < klass.synergies.Count; i++)
                {
                    var synergy = klass.synergies[i];
                    if (synergy == null) continue;

                    if (!synergy.IsNumeric)
                    {
                        if (!specials.Contains(synergy)) specials.Add(synergy);
                        continue;
                    }

                    // Синергия задана НА БОЙЦА, поэтому множим на число
                    // бойцов роли. Снятая галочка означает «за сам факт роли».
                    float total = synergy.Signed * (synergy.scalesWithCount ? count : 1);

                    if (synergy.amountType == SynergyAmount.Percent) percent[(int)synergy.stat] += total;
                    else flat[(int)synergy.stat] += total;
                }
            }
        }

        /// <summary>Ненулевые итоги — для отладочной выкладки.</summary>
        public List<Line> Report()
        {
            var lines = new List<Line>();
            for (int i = 0; i < StatCount; i++)
            {
                if (Mathf.Approximately(percent[i], 0f) && Mathf.Approximately(flat[i], 0f)) continue;
                lines.Add(new Line { stat = (SquadStat)i, percent = percent[i], flat = flat[i] });
            }
            return lines;
        }
    }
}
