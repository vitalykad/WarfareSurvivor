using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Как разворачивать стрелка, чтобы ОРУЖИЕ смотрело на цель.
    ///
    /// Вынесено из бойца, чтобы стенд отладки считал ровно то же, что и бой.
    /// Пока эти три строчки жили внутри Survivor, стенд повторял их у себя,
    /// и любая правка расходилась с проверкой — то есть проверка переставала
    /// что-либо значить.
    /// </summary>
    public static class AimMath
    {
        /// <summary>
        /// Разворот фигуры, при котором ствол ложится на цель.
        ///
        /// Клип стрельбы снят вполоборота: ствол в нём смотрит не туда, куда
        /// смотрит корень, а на yawOffset в сторону. Поэтому корень доворачиваем
        /// на столько же в обратную сторону.
        /// </summary>
        public static Quaternion BodyRotation(Vector3 toTarget, float yawOffset)
        {
            return Quaternion.LookRotation(toTarget, Vector3.up)
                   * Quaternion.Euler(0f, yawOffset, 0f);
        }

        /// <summary>
        /// Куда боец ЦЕЛИТСЯ — в отличие от того, куда развёрнута фигура.
        ///
        /// Проверять наведение по развороту фигуры нельзя: поправка в 35
        /// градусов при пороге в 20 означала, что стоя коп не стрелял вовсе.
        /// </summary>
        public static Vector3 AimForward(Quaternion body, float yawOffset)
        {
            return body * Quaternion.Euler(0f, -yawOffset, 0f) * Vector3.forward;
        }
    }
}
