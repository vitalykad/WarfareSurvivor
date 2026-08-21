using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Доворачивает корпус к цели, не трогая проигрываемую анимацию.
    ///
    /// Смысл в требовании «развернуть корпус не прекращая бега»: анимаций
    /// стрельбы на бегу у нас нет, а отдельный слой с маской потребовал бы
    /// второго набора клипов. Доворот кости груди после того, как Animator
    /// уже разложил позу, даёт нужное поведение на тех клипах, что есть.
    /// Поэтому именно LateUpdate — в Update поза ещё не посчитана.
    /// </summary>
    public class TorsoAim : MonoBehaviour
    {
        [SerializeField] float maxAngle = 45f;
        [SerializeField] float turnSpeed = 480f;

        Animator animator;
        Transform torso;
        float currentAngle;

        /// <summary>Куда целимся. null — корпус плавно возвращается прямо.</summary>
        public Transform Target { get; set; }

        public void Configure(float max, float speed)
        {
            maxAngle = max;
            turnSpeed = speed;
        }

        void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return;

            // Грудь есть не у всякого аватара — она необязательная кость.
            torso = animator.GetBoneTransform(HumanBodyBones.Chest)
                    ?? animator.GetBoneTransform(HumanBodyBones.Spine);
        }

        void LateUpdate()
        {
            if (torso == null) return;

            float desired = 0f;
            if (Target != null)
            {
                var toTarget = Target.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f)
                    desired = Mathf.Clamp(
                        Vector3.SignedAngle(transform.forward, toTarget, Vector3.up),
                        -maxAngle, maxAngle);
            }

            currentAngle = Mathf.MoveTowards(currentAngle, desired, turnSpeed * Time.deltaTime);
            if (Mathf.Abs(currentAngle) < 0.01f) return;

            torso.rotation = Quaternion.AngleAxis(currentAngle, Vector3.up) * torso.rotation;
        }

        /// <summary>Сброс для пула: накопленный угол не должен утечь в следующего жильца.</summary>
        public void ResetAim()
        {
            Target = null;
            currentAngle = 0f;
        }
    }
}
