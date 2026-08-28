using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Ставит одного бойца перед камерой и гоняет его анимацию на месте.
    ///
    /// Боец создаётся из ПРЕФАБА, а не кладётся в сцену готовым: чинить
    /// придётся риг и аватар, а они живут в модели — сцена со слепком
    /// показывала бы вчерашнее состояние и врала бы после каждой правки.
    /// </summary>
    public class CharacterLab : MonoBehaviour
    {
        [Tooltip("Кого разглядываем.")]
        public GameObject who;

        [Tooltip("Скорость для аниматора: единица — бег, ноль — покой.")]
        [Range(0f, 1f)] public float speed = 1f;

        [Tooltip("Крутить бойца вокруг себя, чтобы осмотреть со всех сторон. " +
                 "Градусов в секунду; ноль — стоит спиной.")]
        public float turn = 25f;

        static readonly int SpeedParam = Animator.StringToHash("Speed");
        static readonly int MoveDirParam = Animator.StringToHash("MoveDir");

        Animator animator;
        Transform body;

        void Start()
        {
            if (who == null)
            {
                Debug.LogError("[Стенд персонажа] Не задан префаб.", this);
                enabled = false;
                return;
            }

            var instance = Instantiate(who, Vector3.zero, Quaternion.identity);
            instance.name = who.name;
            body = instance.transform;

            // Боевую логику глушим: бойцу тут не по кому стрелять, а его
            // компоненты полезли бы искать конфиг и отряд.
            foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(behaviour is Animator)) behaviour.enabled = false;

            animator = instance.GetComponentInChildren<Animator>();

            // Без этого Animator не считает позу, когда объект за кадром,
            // а разглядывать мы будем именно позу.
            if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        void Update()
        {
            if (animator == null) return;

            animator.SetFloat(SpeedParam, speed);
            animator.SetFloat(MoveDirParam, 1f);

            if (turn != 0f && body != null)
                body.Rotate(Vector3.up, turn * Time.deltaTime, Space.World);
        }
    }
}
