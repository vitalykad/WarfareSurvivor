using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Камера над отрядом. Перспективная.
    ///
    /// Расплата за перспективу: юнит на краю кадра мельче, чем в центре,
    /// поэтому градация зомби по размеру (тир виден по габариту) читается
    /// уже не так однозначно, как в ортографии. При узком FOV расхождение
    /// невелико, но если тиры начнут путаться — смотреть надо сюда.
    ///
    /// Все числа берутся из <see cref="ArenaConfig"/> КАЖДЫЙ КАДР, а не при
    /// старте. Это позволяет крутить камеру прямо во время игры — правка
    /// в инспекторе видна сразу. Правки ScriptableObject в Play-режиме
    /// вдобавок не откатываются на выходе, в отличие от правок объектов
    /// сцены: подобранное в бою значение остаётся.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class IsoCameraFollow : MonoBehaviour
    {
        [SerializeField] ArenaConfig config;
        [SerializeField] Transform target;

        Camera view;
        Vector3 velocity;

        /// <summary>
        /// Дистанция, на которой камера стоит СЕЙЧАС, и скорость её изменения.
        ///
        /// Отъезд сглаживается ОТДЕЛЬНО от слежения. Общее сглаживание тут
        /// не годится: следовать за отрядом камера должна цепко, иначе кадр
        /// плывёт при каждом движении джойстика, — а отъезжать на пополнение
        /// плавно, потому что резкий скачок кадра в награду за нового бойца
        /// читается как сбой, а не как награда.
        /// </summary>
        float shownDistance;
        float distanceVelocity;

        /// <summary>
        /// Сколько бойцов было на старте. От этого числа и считается отъезд:
        /// стартовая дистанция должна означать «отряд, с которым вышли»,
        /// а он в забеге и на стенде замеров разный.
        /// </summary>
        int startCount;

        public void Bind(Transform followTarget) => target = followTarget;

        void Awake()
        {
            view = GetComponent<Camera>();
            view.orthographic = false;

            if (config == null)
            {
                Debug.LogError($"[{name}] Не задан config. Камера останется как есть.", this);
                enabled = false;
                return;
            }

            ApplyFraming();
            // На старте встаём сразу на нужную дистанцию, без наплыва:
            // плавный отъезд нужен по ходу забега, а не в первом кадре.
            shownDistance = CurrentDistance();
            if (target != null) transform.position = DesiredPosition();
        }

        void LateUpdate()
        {
            if (target == null) return;

            // Стартовый состав запоминаем в первом кадре, где он вообще есть:
            // в Awake отряд ещё не создан, и запомнился бы ноль.
            if (startCount == 0) startCount = Registry.Survivors.Count;

            shownDistance = Mathf.SmoothDamp(shownDistance, CurrentDistance(),
                                             ref distanceVelocity, Mathf.Max(0f, config.cameraZoomTime));

            ApplyFraming();
            transform.position = Vector3.SmoothDamp(
                transform.position, DesiredPosition(), ref velocity, Mathf.Max(0f, config.cameraSmoothTime));
        }

        /// <summary>
        /// Дистанция с поправкой на размер отряда.
        ///
        /// Отряд растёт на тир-апах, и строй растёт вместе с ним — внешнее
        /// кольцо иначе уползает за край кадра ровно в тот момент, когда
        /// игрок только что вложился в пополнение.
        ///
        /// Ближе стартовой не подъезжаем никогда, хотя формула это позволила
        /// бы: терять бойцов и без того больно, а камера, наезжающая на
        /// поредевший отряд, добавила бы к потере ещё и дёрганье кадра.
        ///
        /// Отъезд ЛИНЕЙНЫЙ, по метру с бойца, как и задумано. Строй при этом
        /// растёт медленнее — как корень, потому что бойцы укладываются
        /// по площади. Значит при очень больших отрядах камера уедет с запасом;
        /// на это и стоит потолок.
        /// </summary>
        float CurrentDistance()
        {
            float distance = Mathf.Max(1f, config.cameraDistance);
            int extra = Registry.Survivors.Count - startCount;
            if (extra > 0) distance += config.cameraDistancePerMember * extra;

            float ceiling = config.cameraDistanceMax > 0f
                ? Mathf.Max(distance > 0f ? config.cameraDistance : 1f, config.cameraDistanceMax)
                : distance;

            return Mathf.Min(distance, ceiling);
        }

        void ApplyFraming()
        {
            transform.rotation = Quaternion.Euler(
                Mathf.Clamp(config.cameraPitch, 5f, 89f), config.cameraYaw, 0f);

            view.fieldOfView = Mathf.Clamp(config.cameraFieldOfView, 5f, 120f);

            // Ближняя плоскость не должна дорасти до дальней и не должна
            // подрезать сам отряд — оставляем запас до него.
            float near = Mathf.Clamp(config.cameraNearClip, 0.05f, Mathf.Max(1f, shownDistance * 0.5f));
            view.nearClipPlane = near;
            view.farClipPlane = Mathf.Max(near + 1f, config.cameraFarClip);
        }

        Vector3 DesiredPosition()
            => target.position - transform.rotation * Vector3.forward * shownDistance;
    }
}
