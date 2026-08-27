using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Искры, падающие с убитых зомби, и их подбор отрядом.
    ///
    /// Это ресурс тир-апа — но важнее другое: **это единственное, ради чего
    /// игрок двигается**. До искр джойстик не решал ничего: бой шёл сам,
    /// монстры приходили сами, идти было некуда. Искра лежит там, где упал
    /// зомби, и за ней надо прийти.
    ///
    /// Подбирает БЛИЖАЙШИЙ БОЕЦ, а не центр отряда. От центра выходило
    /// неправильно вдвойне: у большого отряда центр далеко от края, где
    /// зомби и гибнут, поэтому добыча улетала через весь строй и выглядела
    /// так, будто появляется прямо под ногами у игрока. От бойца она
    /// подбирается там, где упала.
    /// </summary>
    public class SparkField : MonoBehaviour
    {
        [SerializeField] ArenaConfig config;
        [SerializeField] ZombieSpawner spawner;

        [SerializeField, Tooltip("Центр отряда: от него добыча отлетает наружу.")]
        Transform squadCenter;
        [SerializeField] Material sparkMaterial;

        [SerializeField, Tooltip("Камера: к ней разворачиваются бутылки.")]
        Camera view;

        [SerializeField, Tooltip("Высота бутылки в метрах.")]
        float sparkHeight = 0.9f;

        /// <summary>Пропорции картинки: она вытянута по вертикали.</summary>
        const float SparkAspect = 1024f / 1536f;

        /// <summary>Подобрана искра, столько-то штук.</summary>
        public event System.Action<int> Collected;

        struct Spark
        {
            public Transform View;
            public Vector3 Position;
            public int Value;

            /// <summary>Летит к бойцу.</summary>
            public bool Flying;

            /// <summary>Расстояние до бойца в момент начала полёта.</summary>
            public float FlyRadius;

            /// <summary>
            /// Радиус витка сейчас. Убывает ровно, ВИДИМОЕ же расстояние
            /// до бойца больше на ширину дуги — по нему подбор считать нельзя.
            /// </summary>
            public float FlyRadiusNow;

            /// <summary>Направление от бойца на бутылку. Доворачивается каждый кадр.</summary>
            public Vector3 FlyDir;

            /// <summary>С какой скоростью убывает радиус, м/с.</summary>
            public float FlyRate;

            /// <summary>В какую сторону закручивается: плюс или минус единица.</summary>
            public float Spin;

            /// <summary>Откуда и куда идёт разлёт, и сколько его осталось.</summary>
            public Vector3 From;
            public Vector3 To;
            public float ScatterLeft;

            /// <summary>Сдвиг фазы покачивания: без него всё поле качается в такт.</summary>
            public float Phase;
        }

        readonly List<Spark> sparks = new List<Spark>();
        readonly Stack<Transform> idle = new Stack<Transform>();
        Transform pool;
        Mesh quad;
        Material trailMaterial;

        void Awake()
        {
            pool = new GameObject("Искры").transform;
            pool.SetParent(transform, false);

            if (spawner != null) spawner.Killed += OnKilled;
        }

        void OnDestroy()
        {
            if (spawner != null) spawner.Killed -= OnKilled;
        }

        void OnKilled(Zombie zombie)
        {
            if (zombie == null || config == null) return;
            // Добыча — за ВЛОЖЕННЫЙ УРОН, а не за труп. Так доход искр
            // держится ровно пропорционально урону отряда и не зависит
            // от того, из кого сложена толпа: третьей волне больше не нужно
            // убивать втрое больше, чтобы купить тот же тир-ап.
            int worth = Mathf.Max(1, Mathf.RoundToInt(config.sparkPerKill * zombie.Effort));
            Drop(zombie.transform.position, worth);
        }

        public void Drop(Vector3 position, int value)
        {
            // Потолок искр держим не ради кадра, а ради читаемости поля:
            // тысяча светляков превращает арену в кашу. Самую старую
            // засчитываем, а не выбрасываем — прогресс терять нельзя,
            // иначе игрок наказан за то, что бой шёл слишком хорошо.
            if (sparks.Count >= Mathf.Max(8, config.maxSparks)) CollectAt(0);

            // Приподнимаем на половину стороны: центр плоскости должен
            // оказаться над землёй, иначе нижняя половина уходит под неё.
            float ground = Mathf.Max(0.2f, sparkHeight) * 0.5f;
            position.y = ground;

            var item = idle.Count > 0 ? idle.Pop() : CreateView();
            item.position = position;
            item.rotation = Facing();
            item.gameObject.SetActive(true);

            // След от прошлого жильца надо стереть ЗДЕСЬ, после того как
            // объект встал на новое место: иначе он проведёт полосу через
            // полкарты от точки, где подобрали предыдущую бутылку.
            var trail = item.GetComponent<TrailRenderer>();
            if (trail != null)
            {
                trail.emitting = false;
                trail.Clear();
            }

            sparks.Add(new Spark
            {
                View = item,
                Position = position,
                Value = value,
                From = position,
                To = ScatterTarget(position, ground),
                ScatterLeft = Mathf.Max(0.05f, config.sparkScatterTime),
                Phase = Random.value * Mathf.PI * 2f,

                // Сторона закрутки у каждой своя: одинаковая читается
                // как заводной механизм, а не как втягивание.
                Spin = Random.value < 0.5f ? -1f : 1f
            });
        }

        /// <summary>
        /// Куда отлетит добыча.
        ///
        /// НАРУЖУ от отряда, а не в случайную сторону: зомби гибнут вплотную
        /// к строю, и разлёт внутрь положил бы добычу под ноги — то есть
        /// туда же, откуда её и так подберут не двигаясь. Наружу же она
        /// ложится кольцом вокруг отряда, и игроку приходится выбирать,
        /// в какую сторону идти.
        /// </summary>
        Vector3 ScatterTarget(Vector3 from, float ground)
        {
            var away = from - CrowdCenter();
            away.y = 0f;

            // Погиб ровно в центре — направление берём любое, лишь бы было.
            if (away.sqrMagnitude < 0.01f)
            {
                float angle = Random.value * Mathf.PI * 2f;
                away = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }

            float distance = Random.Range(config.sparkScatterMin, Mathf.Max(config.sparkScatterMin, config.sparkScatterMax));
            var target = from + away.normalized * distance;
            target.y = ground;
            return target;
        }

        Vector3 CrowdCenter() => squadCenter != null ? squadCenter.position : Vector3.zero;

        /// <summary>Убирает всё с поля, ничего не засчитывая: забег кончился.</summary>
        public void Clear()
        {
            for (int i = sparks.Count - 1; i >= 0; i--) Recycle(i);
        }

        /// <summary>
        /// Конец волны: поле подбирается само.
        ///
        /// Добыча ЛЕТИТ к отряду, а не засчитывается разом. Мгновенный
        /// подсчёт лишал игрока единственного момента, когда видно, сколько
        /// он за волну насобирал: бутылки просто пропадали, а число
        /// на полоске прыгало.
        ///
        /// Летит только то, что ПРИМЕРНО В КАДРЕ. Бутылка, лежащая за краем
        /// экрана, влететь ниоткуда не может — она не читается как подобранная,
        /// она читается как подаренная. Остальные никуда не деваются: лежат
        /// где лежали, и в следующей волне их можно поднять ногами.
        /// </summary>
        public void CollectAll()
        {
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var spark = sparks[i];

                // Ещё разлетается после смерти зомби — пусть долетит,
                // подберётся обычным порядком.
                if (spark.ScatterLeft > 0f) continue;
                if (spark.Flying) continue;
                if (!InView(spark.Position)) continue;

                var collector = Nearest(spark.Position);
                if (collector == null) continue;

                StartFlight(ref spark, collector.transform.position);
                sparks[i] = spark;
            }
        }

        /// <summary>
        /// Точка примерно в кадре? Границы с запасом: у самого края экрана
        /// бутылка видна лишь наполовину, и отсекать её ровно по краю —
        /// значит терять то, что игрок считает своим.
        /// </summary>
        bool InView(Vector3 point)
        {
            if (view == null) return true;

            var viewport = view.WorldToViewportPoint(point);
            const float margin = 0.06f;
            return viewport.z > 0f
                && viewport.x > -margin && viewport.x < 1f + margin
                && viewport.y > -margin && viewport.y < 1f + margin;
        }

        /// <summary>
        /// Разворот лицом к камере.
        ///
        /// Камера смотрит под ПОСТОЯННЫМ углом, поэтому разворот считается
        /// один раз при появлении, а не каждый кадр на каждую бутылку.
        /// Наклон камеры меняется только правкой конфига, и ради этого
        /// крутить сотню объектов ежекадрово незачем.
        /// </summary>
        Quaternion Facing() => view != null ? view.transform.rotation : Quaternion.Euler(90f, 0f, 0f);

        void Update()
        {
            if (config == null || sparks.Count == 0) return;

            float attract = config.sparkAttractRadius * config.sparkAttractRadius;
            float pickup = config.sparkPickupRadius * config.sparkPickupRadius;

            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var spark = sparks[i];

                // Пока летит разлёт — не подбирается ни при каких условиях.
                // Иначе отряд, идущий следом за добычей, ловил бы её
                // в воздухе, и разлёт не значил бы ничего.
                if (spark.ScatterLeft > 0f)
                {
                    Scatter(ref spark, i);
                    continue;
                }

                var collector = Nearest(spark.Position);
                if (collector == null) continue;

                if (spark.Flying)
                {
                    Spiral(ref spark, collector.transform.position);
                    spark.View.position = spark.Position;
                    sparks[i] = spark;
                    Bob(spark);

                    // Подбор по РАДИУСУ ВИТКА, а не по видимому расстоянию:
                    // на широкой дуге бутылка уходит от бойца дальше, чем
                    // была вначале, и по настоящему расстоянию подбор
                    // не случился бы никогда.
                    if (spark.FlyRadiusNow <= config.sparkPickupRadius) CollectAt(i);
                    continue;
                }

                var delta = collector.transform.position - spark.Position;
                delta.y = 0f;
                float sqr = delta.sqrMagnitude;

                // Притяжение включается один раз и обратно не выключается:
                // иначе искра на границе радиуса дёргается туда-сюда, пока
                // отряд рядом ходит.
                if (sqr <= attract)
                {
                    StartFlight(ref spark, collector.transform.position);
                    sparks[i] = spark;
                    Bob(spark);
                    continue;
                }

                sparks[i] = spark;
                Bob(spark);

                if (sqr <= pickup) CollectAt(i);
            }
        }

        /// <summary>
        /// Пускает добычу в полёт к бойцу и включает след.
        ///
        /// Расстояние запоминается: по нему считается закрутка, чтобы
        /// спираль выглядела одинаково и при подборе в двух метрах,
        /// и при сборе поля после волны с двадцати.
        /// </summary>
        void StartFlight(ref Spark spark, Vector3 target)
        {
            if (spark.Flying) return;

            spark.Flying = true;

            var flat = spark.Position - target;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;

            spark.FlyRadius = Mathf.Max(0.01f, flat.magnitude);
            spark.FlyRadiusNow = spark.FlyRadius;
            spark.FlyDir = flat.normalized;

            // Время полёта зажато с обеих сторон. Снизу — чтобы виток успел
            // прочитаться: подбор идёт с двух с половиной метров, и на
            // постоянной скорости это две десятых секунды, за которые
            // широкой дуги не разглядеть. Сверху — чтобы добыча с дальнего
            // края поля не тянулась через весь экран.
            float duration = Mathf.Clamp(spark.FlyRadius / Mathf.Max(0.1f, config.sparkFlySpeed),
                                         Mathf.Max(0.05f, config.sparkFlyTimeMin),
                                         Mathf.Max(0.06f, config.sparkFlyTimeMax));
            spark.FlyRate = spark.FlyRadius / duration;

            if (spark.View == null) return;
            var trail = spark.View.GetComponent<TrailRenderer>();
            if (trail == null) return;

            trail.time = Mathf.Max(0.02f, config.sparkTrailTime);
            trail.widthMultiplier = Mathf.Max(0.01f, config.sparkTrailWidth);
            ApplyTrailColor(trail);
            trail.Clear();
            trail.emitting = true;
        }

        /// <summary>
        /// Шаг полёта ПО СПИРАЛИ: радиус убывает ровно, угол доворачивается.
        ///
        /// Радиус убывает с той же скоростью, что и раньше по прямой, —
        /// значит время полёта и, стало быть, весь баланс подбора не изменились.
        /// Меняется только путь.
        /// </summary>
        void Spiral(ref Spark spark, Vector3 target)
        {
            spark.FlyRadiusNow = Mathf.Max(0f, spark.FlyRadiusNow - spark.FlyRate * Time.deltaTime);

            // Пройденная доля пути считается по радиусу витка, а не по времени:
            // так и доворот, и ширина дуги привязаны к одному и тому же,
            // и виток выходит одинаковой формы при любой дальности.
            float done = 1f - spark.FlyRadiusNow / spark.FlyRadius;

            float turn = config.sparkSpiralTurn * spark.Spin * (spark.FlyRate * Time.deltaTime / spark.FlyRadius);
            spark.FlyDir = Quaternion.AngleAxis(turn, Vector3.up) * spark.FlyDir;

            // Дуга наружу: к середине полёта бутылка отходит от бойца,
            // а не сходится сразу. Без этого при подборе под ногами
            // от спирали остаётся завиток в два метра.
            float bulge = Mathf.Max(0f, config.sparkSpiralBulge) * Mathf.Sin(done * Mathf.PI);

            var moved = target + spark.FlyDir * (spark.FlyRadiusNow + bulge);
            moved.y = spark.Position.y;
            spark.Position = moved;
        }

        /// <summary>
        /// Двигает добычу по дуге разлёта.
        ///
        /// Дуга, а не прямая: подскок читается как «выбило ударом»,
        /// а скольжение по земле — как ошибку физики.
        /// </summary>
        void Scatter(ref Spark spark, int index)
        {
            float total = Mathf.Max(0.05f, config.sparkScatterTime);
            spark.ScatterLeft -= Time.deltaTime;

            float t = Mathf.Clamp01(1f - spark.ScatterLeft / total);
            var position = Vector3.Lerp(spark.From, spark.To, t);
            position.y += Mathf.Sin(t * Mathf.PI) * Mathf.Max(0f, config.sparkScatterHop);

            spark.Position = position;
            spark.View.position = position;

            if (spark.ScatterLeft <= 0f)
            {
                spark.Position = spark.To;
                spark.View.position = spark.To;
            }

            sparks[index] = spark;
        }

        /// <summary>
        /// Покачивание и крен.
        ///
        /// Неподвижный предмет на песке глаз принимает за часть фона —
        /// особенно наш, лежащий среди трупов и обломков. Движение отличает
        /// подбираемое от декорации раньше, чем игрок успеет разглядеть,
        /// что именно там лежит.
        ///
        /// Двигается ТОЛЬКО картинка, а не сама добыча: расстояние подбора
        /// считается от её настоящего места, и качающаяся точка сбора
        /// давала бы подбор то раньше, то позже.
        ///
        /// Крен идёт вокруг оси взгляда, а не вокруг вертикали: плоскость
        /// повёрнута к камере, и разворот вокруг вертикали показал бы её
        /// ребром — бутылка исчезала бы дважды за оборот.
        /// </summary>
        void Bob(Spark spark)
        {
            if (spark.View == null) return;

            float t = Time.time + spark.Phase;

            var position = spark.Position;
            position.y += Mathf.Sin(t * config.sparkBobSpeed) * config.sparkBobHeight;
            spark.View.position = position;

            float sway = Mathf.Sin(t * config.sparkSwaySpeed) * config.sparkSwayAngle;
            spark.View.rotation = Facing() * Quaternion.Euler(0f, 0f, sway);
        }

        /// <summary>
        /// Ближайший к добыче боец.
        ///
        /// Перебором: бойцов пятнадцать, добычи полторы сотни — это две
        /// тысячи сравнений в кадр, что на фоне измеренной цены логики
        /// зомби (PERFORMANCE.md §7) не стоит ничего. Сетка соседей тут
        /// была бы преждевременной.
        /// </summary>
        static Survivor Nearest(Vector3 point)
        {
            Survivor best = null;
            float bestSqr = float.MaxValue;

            var members = Registry.Survivors;
            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (member == null) continue;

                var delta = member.transform.position - point;
                delta.y = 0f;

                float sqr = delta.sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = member;
            }
            return best;
        }

        void CollectAt(int index)
        {
            int value = sparks[index].Value;
            Recycle(index);
            Collected?.Invoke(value);
        }

        void Recycle(int index)
        {
            var item = sparks[index].View;
            if (item != null)
            {
                var trail = item.GetComponent<TrailRenderer>();
                if (trail != null)
                {
                    trail.emitting = false;
                    trail.Clear();
                }

                item.gameObject.SetActive(false);
                idle.Push(item);
            }
            sparks.RemoveAt(index);
        }

        Transform CreateView()
        {
            if (quad == null) quad = BuildQuad();

            var go = new GameObject("Искра", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(pool, false);
            go.GetComponent<MeshFilter>().sharedMesh = quad;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = sparkMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = TrailMaterial();
            trail.alignment = LineAlignment.View;
            trail.numCapVertices = 2;
            trail.minVertexDistance = 0.06f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            trail.emitting = false;

            // Сходит на нет к хвосту: полоса ровной ширины читается ниткой,
            // а не следом.
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0f));

            ApplyTrailColor(trail);

            return go.transform;
        }

        /// <summary>
        /// Цвет следа. Живёт в ГРАДИЕНТЕ ленты, а не в материале: материал
        /// один на все следы, и красить в нём значило бы красить все разом.
        /// </summary>
        void ApplyTrailColor(TrailRenderer trail)
        {
            var color = config != null ? config.sparkTrailColor : new Color(0.35f, 0.72f, 1f, 0.9f);

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(0f, 1f) });

            trail.colorGradient = gradient;
        }

        Material TrailMaterial()
        {
            if (trailMaterial != null) return trailMaterial;

            // НЕ аддитивный шейдер трасс, а с предумноженной альфой.
            // Аддитивный поверх песочной земли выбивает все три канала
            // в единицу, и синий след выходил чисто белым — проверено
            // съёмкой. Здесь плотная часть ленты держит свой цвет.
            var shader = Shader.Find("WarfareSurvivor/GlowSprite");
            if (shader == null)
            {
                Debug.LogError("[Искры] Не нашёлся шейдер WarfareSurvivor/GlowSprite — " +
                               "след за добычей останется без свечения.");
                return null;
            }

            trailMaterial = new Material(shader) { name = "SparkTrail" };
            trailMaterial.SetFloat("_Boost", config != null ? Mathf.Max(0.1f, config.sparkTrailBoost) : 1.5f);
            return trailMaterial;
        }

        /// <summary>
        /// Квадрат в плоскости XY. Разворачивает его к камере уже трансформ
        /// объекта, поэтому здесь плоскость, а не лежачий прямоугольник.
        /// </summary>
        Mesh BuildQuad()
        {
            float halfHeight = Mathf.Max(0.2f, sparkHeight) * 0.5f;
            float halfWidth = halfHeight * SparkAspect;

            var mesh = new Mesh { name = "Добыча" };
            mesh.vertices = new[]
            {
                new Vector3(-halfWidth, -halfHeight, 0f), new Vector3(-halfWidth, halfHeight, 0f),
                new Vector3(halfWidth, halfHeight, 0f), new Vector3(halfWidth, -halfHeight, 0f)
            };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
