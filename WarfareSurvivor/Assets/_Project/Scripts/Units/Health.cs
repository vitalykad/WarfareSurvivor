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

        public void TakeDamage(float amount, Vector3 point)
        {
            if (IsDead || amount <= 0f) return;

            Current = Mathf.Max(0f, Current - amount);
            Damaged?.Invoke(amount, point);

            if (Current <= 0f) Died?.Invoke();
        }
    }
}
