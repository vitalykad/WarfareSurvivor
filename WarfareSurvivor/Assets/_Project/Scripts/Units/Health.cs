using System;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>Здоровье юнита. У каждого своё — отряд общей полоски не имеет.</summary>
    public class Health : MonoBehaviour
    {
        public float Max { get; private set; } = 100f;
        public float Current { get; private set; } = 100f;
        public bool IsDead => Current <= 0f;
        public float Fraction => Max <= 0f ? 0f : Mathf.Clamp01(Current / Max);

        /// <summary>Пришедший урон и точка попадания.</summary>
        public event Action<float, Vector3> Damaged;
        public event Action Died;

        /// <summary>Сколько здоровья долили. Ноль сюда не приходит.</summary>
        public event Action<float> Healed;

        /// <summary>
        /// Полный сброс состояния — вызывается пулом при выдаче объекта.
        /// Если не обнулять здесь, здоровье прошлого жильца протекает
        /// в следующего (§8 project.md).
        /// </summary>
        public void Init(float max)
        {
            Max = max;
            Current = max;
        }

        /// <summary>
        /// Поднимает потолок здоровья и доливает столько же текущему.
        ///
        /// Доливает намеренно: улучшение, поднявшее только потолок, читается
        /// как ослабление — полоска у всех разом становится короче, хотя
        /// игрок только что вложился в живучесть.
        /// </summary>
        public void RaiseMax(float multiplier)
        {
            if (multiplier <= 1f) return;

            float added = Max * (multiplier - 1f);
            Max += added;
            Current = Mathf.Min(Max, Current + added);
        }

        /// <summary>
        /// Долить здоровья, но не выше потолка. Возвращает, сколько ДЕЙСТВИТЕЛЬНО
        /// долилось: у целого это ноль, и по нему медик понимает, что лечить
        /// тут нечего, а эффект показывать не за что.
        /// </summary>
        public float Heal(float amount)
        {
            if (IsDead || amount <= 0f) return 0f;

            float before = Current;
            Current = Mathf.Min(Max, Current + amount);
            float healed = Current - before;
            if (healed > 0f) Healed?.Invoke(healed);
            return healed;
        }

        public void TakeDamage(float amount, Vector3 point)
        {
            if (IsDead || amount <= 0f) return;

            Current = Mathf.Max(0f, Current - amount);
            Damaged?.Invoke(amount, point);

            if (Current <= 0f) Died?.Invoke();
        }
    }
}
