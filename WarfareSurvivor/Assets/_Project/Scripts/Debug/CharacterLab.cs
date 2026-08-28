using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Стенд для разглядывания персонажа: один боец, крупно, без боя.
    ///
    /// Нужен ровно для таких случаев, как подламывающаяся ступня: в бою
    /// боец занимает полсотни пикселей среди толпы, и понять, что именно
    /// не так с ногой, нельзя.
    ///
    /// Всё, что нужно для разбора, крутится ОТСЮДА, без пересборки сцены:
    /// ракурс, дальность, кадр анимации. Пересобирать сцену на каждый угол
    /// значит терять полминуты на каждый взгляд.
    /// </summary>
    public class CharacterLab : MonoBehaviour
    {
        [Tooltip("Кого разглядываем.")]
        public GameObject who;

        [Header("Анимация")]

        [Tooltip("Скорость для аниматора: единица — бег, ноль — покой.")]
        [Range(0f, 1f)] public float speed = 1f;

        [Tooltip("Остановить анимацию и держать кадр из поля ниже. " +
                 "Разбирать позу на ходу нельзя: между двумя снимками " +
                 "она успевает уйти, и сравнивать нечего.")]
        public bool freeze;

        [Tooltip("Какой кадр цикла держать, доля 0..1.")]
        [Range(0f, 1f)] public float frame;

        [Header("Обзор")]

        [Tooltip("Наклон камеры. 58 — как в игре, 0 — вид сбоку.")]
        public float viewPitch = 58f;

        [Tooltip("Поворот камеры вокруг бойца. 180 — вид со спины.")]
        public float viewYaw = 180f;

        [Tooltip("Дальность камеры, метров.")]
        public float viewDistance = 3.2f;

        [Tooltip("Куда смотрит камера по высоте, метров от земли. " +
                 "Ноль — на ступни.")]
        public float viewHeight = 0.5f;

        static readonly int SpeedParam = Animator.StringToHash("Speed");
        static readonly int MoveDirParam = Animator.StringToHash("MoveDir");

        Animator animator;
        Transform body;
        Camera view;

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

            // Без этого Animator не считает позу за кадром, а разглядывать
            // мы будем именно позу.
            if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            view = Camera.main;
        }

        void Update()
        {
            if (animator != null)
            {
                animator.SetFloat(SpeedParam, speed);
                animator.SetFloat(MoveDirParam, 1f);

                // Останов через сам Animator, а не через timeScale: время
                // нужно живым, иначе нельзя крутить обзор и снимать.
                animator.speed = freeze ? 0f : 1f;

                if (freeze)
                {
                    var state = animator.GetCurrentAnimatorStateInfo(0);
                    animator.Play(state.fullPathHash, 0, Mathf.Clamp01(frame));
                    animator.Update(0f);
                }
            }

            if (view == null || body == null) return;

            var focus = body.position + Vector3.up * viewHeight;
            var rotation = Quaternion.Euler(viewPitch, viewYaw, 0f);

            view.transform.rotation = rotation;
            view.transform.position = focus - rotation * Vector3.forward * Mathf.Max(0.5f, viewDistance);
        }
    }
}
