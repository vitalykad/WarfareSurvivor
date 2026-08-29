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

        /// <summary>
        /// Развал ствола относительно корня, снятый с ЧИСТОЙ позы.
        ///
        /// Считается здесь, а не у того, кто спрашивает, потому что чистой
        /// поза бывает ровно в одно мгновение кадра: Animator её только что
        /// разложил, а грудь ещё не повёрнута. В Update у спрашивающего
        /// ствол уже несёт вчерашнюю поправку, и меряется не развал позы,
        /// а остаток промаха.
        /// </summary>
        public float PoseYaw { get; private set; }

        /// <summary>Куда целимся. null — корпус плавно возвращается прямо.</summary>
        public Transform Target { get; set; }

        /// <summary>
        /// Дуло. Задано — доворачиваем СТВОЛ на цель, а не корень фигуры.
        ///
        /// Разница не косметическая. В клипе стрельбы ствол смотрит не туда,
        /// куда развёрнут корень: у копа в стойке — на 46 градусов вбок,
        /// на бегу — на 29. Пока доворот считался от корня, он этот развал
        /// не учитывал и ровно на столько же промахивался. А если фигуру
        /// заранее довернули на поправку стойки, доворот груди её просто
        /// гасил: тело вправо на 35, грудь влево на 35, ствол там же, где
        /// и был. Игрок видел копа, стреляющего мимо.
        /// </summary>
        public Transform Barrel { get; set; }

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

            PoseYaw = 0f;
            if (Barrel != null)
            {
                var pose = Barrel.forward;
                pose.y = 0f;
                if (pose.sqrMagnitude > 0.0001f)
                    PoseYaw = Vector3.SignedAngle(transform.forward, pose, Vector3.up);
            }

            float desired = 0f;
            if (Target != null)
            {
                var toTarget = Target.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    float need = Vector3.SignedAngle(transform.forward, toTarget, Vector3.up);

                    // Вычитаем развал ствола, заложенный в позу клипа.
                    desired = Mathf.Clamp(need - PoseYaw, -maxAngle, maxAngle);
                }
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

        /// <summary>
        /// Дотянется ли ствол до этого направления одной грудью, без разворота ног.
        ///
        /// Мерить по корню нельзя: у пистолета в правой руке дотяг влево и
        /// вправо разный, и отбор целей по корню одновременно отсекал бы
        /// достижимое слева и предлагал недостижимое справа.
        /// </summary>
        public bool CanReach(Vector3 toTarget)
        {
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return true;

            float need = Vector3.SignedAngle(transform.forward, toTarget, Vector3.up) - PoseYaw;
            return Mathf.Abs(need) <= maxAngle;
        }
    }
}
