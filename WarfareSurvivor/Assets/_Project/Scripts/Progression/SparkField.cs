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

            sparks.Add(new Spark
            {
                View = item,
                Position = position,
                Value = value,
                From = position,
                To = ScatterTarget(position, ground),
                ScatterLeft = Mathf.Max(0.05f, config.sparkScatterTime),
                Phase = Random.value * Mathf.PI * 2f
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

                // Некому подбирать — некуда и лететь.
                if (Nearest(spark.Position) == null) continue;

                spark.Flying = true;
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
            float step = config.sparkFlySpeed * Time.deltaTime;

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

                var delta = collector.transform.position - spark.Position;
                delta.y = 0f;
                float sqr = delta.sqrMagnitude;

                // Притяжение включается один раз и обратно не выключается:
                // иначе искра на границе радиуса дёргается туда-сюда, пока
                // отряд рядом ходит.
                if (!spark.Flying && sqr <= attract) spark.Flying = true;

                if (spark.Flying && sqr > 0.0001f)
                {
                    spark.Position += delta.normalized * Mathf.Min(step, delta.magnitude);
                    spark.View.position = spark.Position;
                }

                sparks[i] = spark;

                Bob(spark);

                if (sqr <= pickup) CollectAt(i);
            }
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

            return go.transform;
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
