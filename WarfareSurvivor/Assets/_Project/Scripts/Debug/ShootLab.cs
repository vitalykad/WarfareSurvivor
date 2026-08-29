using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Стенд наведения: один стрелок, одна цель, три линии.
    ///
    /// Нужен потому, что в бою вопрос «куда развёрнут коп» неразрешим:
    /// фигура занимает полсотни пикселей, цель за кадром, а трасса рисуется
    /// от дула до жертвы независимо от позы — то есть выглядит правильной
    /// всегда, даже когда ствол смотрит в сторону. Здесь наоборот: цель
    /// стоит там, где я её поставил, и видно каждую из трёх осей отдельно.
    ///
    /// Синяя линия — куда развёрнут КОРЕНЬ фигуры.
    /// Красная — куда смотрит СТВОЛ (это и есть то, что видит игрок).
    /// Зелёная — где ЦЕЛЬ.
    /// Сходятся красная с зелёной — наведение верное. Расхождение красной
    /// и есть та ошибка, которую ловим.
    /// </summary>
    public class ShootLab : MonoBehaviour
    {
        [Tooltip("Префаб стрелка.")]
        public GameObject who;

        [Tooltip("Класс стрелка: отсюда берётся поправка стойки.")]
        public SurvivorClassSO klass;

        [Header("Цель")]

        [Tooltip("Где стоит цель: градусы по кругу вокруг бойца.")]
        [Range(-180f, 180f)] public float targetAngle;

        [Tooltip("Дальность до цели, метров.")]
        public float targetDistance = 6f;

        [Tooltip("Гонять цель по кругу: так видно ошибку на всех углах разом.")]
        public bool sweep;

        [Tooltip("Скорость обхода, градусов в секунду.")]
        public float sweepSpeed = 30f;

        [Header("Наведение")]

        [Tooltip("Поправка стойки. Живая копия значения из класса: " +
                 "крутить её здесь можно не трогая ассет.")]
        public float yawOffset = 35f;

        [Tooltip("Доворачивать грудь к цели поверх разворота фигуры.")]
        public bool useTorsoAim = true;

        [Tooltip("Доворот наводит СТВОЛ, а не корень фигуры. " +
                 "Снять галочку — вернуться к прежнему поведению и увидеть промах.")]
        public bool aimBarrel = true;

        [Tooltip("Мгновенный разворот вместо плавного: на стенде важна " +
                 "установившаяся поза, а не то, как боец в неё въезжает.")]
        public bool snapTurn = true;

        [Tooltip("Скорость доворота, если он плавный.")]
        public float turnSpeed = 720f;

        [Header("Поза")]

        [Tooltip("Единица — бег на месте, ноль — стойка.")]
        [Range(0f, 1f)] public float speed;

        [Tooltip("Бег: тело идёт вдоль движения, к цели тянется только грудь.")]
        public bool moving;

        [Tooltip("Куда бежит отряд, градусы.")]
        [Range(-180f, 180f)] public float moveAngle;

        [Header("Обзор")]

        public float viewPitch = 58f;
        public float viewYaw = 180f;
        public float viewDistance = 6f;
        public float viewHeight = 1f;

        static readonly int SpeedParam = Animator.StringToHash("Speed");
        static readonly int MoveDirParam = Animator.StringToHash("MoveDir");

        Animator animator;
        Transform body;
        Transform muzzle;
        Transform mark;
        TorsoAim torsoAim;
        Camera view;

        LineRenderer rootLine;
        LineRenderer barrelLine;
        LineRenderer targetLine;

        void Start()
        {
            if (who == null)
            {
                Debug.LogError("[Стенд стрельбы] Не задан префаб.", this);
                enabled = false;
                return;
            }

            var instance = Instantiate(who, Vector3.zero, Quaternion.identity);
            instance.name = who.name;
            body = instance.transform;

            // Боевую логику глушим: стрелку тут не по кому стрелять, а Survivor
            // полез бы за отрядом и конфигом. Наведение стенд считает сам,
            // но ТОЙ ЖЕ формулой — через AimMath.
            foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(behaviour is Animator) && !(behaviour is TorsoAim)) behaviour.enabled = false;

            animator = instance.GetComponentInChildren<Animator>();
            if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                if (t.name == "Muzzle") muzzle = t;

            torsoAim = instance.GetComponent<TorsoAim>();
            if (torsoAim != null)
            {
                torsoAim.Configure(75f, 480f);
                torsoAim.Barrel = muzzle;
            }

            if (klass != null) yawOffset = klass.aimYawOffset;

            mark = BuildMark();
            rootLine = BuildLine(new Color(0.3f, 0.6f, 1f), 0.05f);
            barrelLine = BuildLine(new Color(1f, 0.25f, 0.2f), 0.05f);
            targetLine = BuildLine(new Color(0.3f, 1f, 0.4f), 0.03f);

            view = Camera.main;
        }

        Transform BuildMark()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Цель";
            go.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            Destroy(go.GetComponent<Collider>());

            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.color = new Color(0.2f, 0.9f, 0.3f);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }

        static LineRenderer BuildLine(Color color, float width)
        {
            var go = new GameObject("Линия");
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.widthMultiplier = width;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.color = color;
            line.sharedMaterial = material;
            return line;
        }

        Vector3 Direction(float degrees)
        {
            return Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
        }

        void Update()
        {
            if (body == null) return;

            if (sweep) targetAngle = Mathf.Repeat(targetAngle + sweepSpeed * Time.deltaTime + 180f, 360f) - 180f;

            var toTarget = Direction(targetAngle);
            mark.position = body.position + toTarget * targetDistance + Vector3.up * 0.9f;

            if (animator != null)
            {
                animator.SetFloat(SpeedParam, speed);
                animator.SetFloat(MoveDirParam, 1f);
            }

            // Ровно то же, что делает боец в Survivor.Aim.
            // Поправка стойки — только когда боец стоит: на бегу ноги идут
            // вдоль движения, а ствол на цель тянет грудь.
            var facing = moving ? Direction(moveAngle) : toTarget;
            var wanted = AimMath.BodyRotation(facing, moving ? 0f : yawOffset);

            body.rotation = snapTurn
                ? wanted
                : Quaternion.RotateTowards(body.rotation, wanted, turnSpeed * Time.deltaTime);

            if (torsoAim != null)
            {
                torsoAim.Barrel = aimBarrel ? muzzle : null;
                torsoAim.Target = useTorsoAim ? mark : null;
            }
        }

        void LateUpdate()
        {
            if (body == null) return;

            // ПОСЛЕ TorsoAim: он крутит грудь в LateUpdate, и до него ствол
            // стоит не там, где его увидит игрок.
            var from = body.position + Vector3.up * 1.1f;
            Draw(rootLine, from, from + body.forward * targetDistance);
            if (muzzle != null) Draw(barrelLine, muzzle.position, muzzle.position + muzzle.forward * targetDistance);
            Draw(targetLine, from, mark.position);

            if (view == null) return;
            var focus = body.position + Vector3.up * viewHeight;
            var rotation = Quaternion.Euler(viewPitch, viewYaw, 0f);
            view.transform.rotation = rotation;
            view.transform.position = focus - rotation * Vector3.forward * Mathf.Max(1f, viewDistance);
        }

        static void Draw(LineRenderer line, Vector3 a, Vector3 b)
        {
            line.SetPosition(0, a);
            line.SetPosition(1, b);
        }

        /// <summary>Числа для отладки: все углы в одной системе отсчёта.</summary>
        public string Report()
        {
            if (body == null) return "стенд не запущен";

            var toTarget = Direction(targetAngle);
            float target = Yaw(toTarget);
            float root = Yaw(body.forward);
            float barrel = muzzle != null ? Yaw(muzzle.forward) : 0f;

            return string.Format(
                "цель {0,6:F1} | корень {1,6:F1} | ствол {2,6:F1} | ОШИБКА СТВОЛА {3,6:F1} | корень-ствол {4,6:F1}",
                target, root, barrel, Mathf.DeltaAngle(target, barrel), Mathf.DeltaAngle(root, barrel));
        }

        static float Yaw(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
        }
    }
}
