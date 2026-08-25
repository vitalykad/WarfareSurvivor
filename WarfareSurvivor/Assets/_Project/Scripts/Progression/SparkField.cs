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
    /// Поэтому подбор идёт от ЦЕНТРА ОТРЯДА, а не от отдельных бойцов:
    /// игрок ведёт отряд как курсор, и собирать он должен тем же движением,
    /// которым ведёт.
    ///
    /// Выглядит ресурс бутылкой воды: узнаваемый предмет читается на песке
    /// лучше абстрактного огонька, а подсветку ему даёт шейдер.
    /// </summary>
    public class SparkField : MonoBehaviour
    {
        [SerializeField] ArenaConfig config;
        [SerializeField] SquadController squad;
        [SerializeField] ZombieSpawner spawner;
        [SerializeField] Material sparkMaterial;

        [SerializeField, Tooltip("Камера: к ней разворачиваются бутылки.")]
        Camera view;

        [SerializeField, Tooltip("Высота бутылки в метрах.")]
        float sparkHeight = 1.1f;

        /// <summary>Пропорции картинки: она вытянута по вертикали.</summary>
        const float SparkAspect = 1024f / 1536f;

        /// <summary>Подобрана искра, столько-то штук.</summary>
        public event System.Action<int> Collected;

        struct Spark
        {
            public Transform View;
            public Vector3 Position;
            public int Value;
            public bool Flying;
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
            Drop(zombie.transform.position, Mathf.Max(1, config.sparkPerKill));
        }

        public void Drop(Vector3 position, int value)
        {
            // Потолок искр держим не ради кадра, а ради читаемости поля:
            // тысяча светляков превращает арену в кашу. Самую старую
            // засчитываем, а не выбрасываем — прогресс терять нельзя,
            // иначе игрок наказан за то, что бой шёл слишком хорошо.
            if (sparks.Count >= Mathf.Max(8, config.maxSparks)) CollectAt(0);

            // Приподнимаем на половину высоты: центр плоскости должен
            // оказаться над землёй, иначе нижняя половина уходит под неё.
            position.y = Mathf.Max(0.2f, sparkHeight) * 0.45f;

            var item = idle.Count > 0 ? idle.Pop() : CreateView();
            item.position = position;
            item.rotation = Facing();
            item.gameObject.SetActive(true);

            sparks.Add(new Spark { View = item, Position = position, Value = value });
        }

        /// <summary>Убирает всё с поля, ничего не засчитывая: забег кончился.</summary>
        public void Clear()
        {
            for (int i = sparks.Count - 1; i >= 0; i--) Recycle(i);
        }

        /// <summary>Засчитывает всё разом — конец волны, поле подбирается само.</summary>
        public void CollectAll()
        {
            int total = 0;
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                total += sparks[i].Value;
                Recycle(i);
            }
            if (total > 0) Collected?.Invoke(total);
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
            if (config == null || squad == null || sparks.Count == 0) return;

            var center = squad.transform.position;
            float attract = config.sparkAttractRadius * config.sparkAttractRadius;
            float pickup = config.sparkPickupRadius * config.sparkPickupRadius;
            float step = config.sparkFlySpeed * Time.deltaTime;

            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var spark = sparks[i];

                var delta = center - spark.Position;
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

                if (sqr <= pickup) CollectAt(i);
            }
        }

        void CollectAt(int index)
        {
            int value = sparks[index].Value;
            Recycle(index);
            Collected?.Invoke(value);
        }

        void Recycle(int index)
        {
            var view = sparks[index].View;
            if (view != null)
            {
                view.gameObject.SetActive(false);
                idle.Push(view);
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
        /// Плоскость с картинкой, стоящая в плоскости XY.
        ///
        /// Разворачивает её к камере уже трансформ объекта. Пропорции взяты
        /// с самой картинки — квадрат сплющил бы бутылку.
        /// </summary>
        Mesh BuildQuad()
        {
            float height = Mathf.Max(0.2f, sparkHeight);
            float halfHeight = height * 0.5f;
            float halfWidth = halfHeight * SparkAspect;

            var mesh = new Mesh { name = "Бутылка" };
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
