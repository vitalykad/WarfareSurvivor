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
            if (target != null) transform.position = DesiredPosition();
        }

        void LateUpdate()
        {
            if (target == null) return;

            ApplyFraming();
            transform.position = Vector3.SmoothDamp(
                transform.position, DesiredPosition(), ref velocity, Mathf.Max(0f, config.cameraSmoothTime));
        }

        void ApplyFraming()
        {
            transform.rotation = Quaternion.Euler(
                Mathf.Clamp(config.cameraPitch, 5f, 89f), config.cameraYaw, 0f);

            view.fieldOfView = Mathf.Clamp(config.cameraFieldOfView, 5f, 120f);

            // Ближняя плоскость не должна дорасти до дальней и не должна
            // подрезать сам отряд — оставляем запас до него.
            float near = Mathf.Clamp(config.cameraNearClip, 0.05f, Mathf.Max(1f, config.cameraDistance * 0.5f));
            view.nearClipPlane = near;
            view.farClipPlane = Mathf.Max(near + 1f, config.cameraFarClip);
        }

        Vector3 DesiredPosition()
            => target.position - transform.rotation * Vector3.forward * Mathf.Max(1f, config.cameraDistance);
    }
}
