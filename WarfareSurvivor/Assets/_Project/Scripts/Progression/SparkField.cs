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
    /// </summary>
    public class SparkField : MonoBehaviour
    {
        [SerializeField] ArenaConfig config;
        [SerializeField] SquadController squad;
        [SerializeField] ZombieSpawner spawner;
        [SerializeField] Material sparkMaterial;

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

            position.y = 0.35f;

            var view = idle.Count > 0 ? idle.Pop() : CreateView();
            view.position = position;
            view.gameObject.SetActive(true);

            sparks.Add(new Spark { View = view, Position = position, Value = value });
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
        /// Плоский квадрат, лежащий НА ЗЕМЛЕ.
        ///
        /// Не билборд к камере: камера смотрит сверху-сбоку под фиксированным
        /// углом, и лежащий квадрат с неё виден так же хорошо, а стоит
        /// дешевле — его не надо разворачивать каждый кадр.
        /// </summary>
        static Mesh BuildQuad()
        {
            const float half = 0.28f;
            var mesh = new Mesh { name = "Искра" };
            mesh.vertices = new[]
            {
                new Vector3(-half, 0f, -half), new Vector3(-half, 0f, half),
                new Vector3(half, 0f, half), new Vector3(half, 0f, -half)
            };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };

            // Цвет живёт в ВЕРШИНАХ: аддитивный шейдер берёт оттенок оттуда,
            // и без него искра выходит белым квадратом, неотличимым от блика.
            // Тёплое золото читается на песке и не спорит с зеленью зомби.
            var gold = new Color(1f, 0.82f, 0.30f, 1f);
            mesh.colors = new[] { gold, gold, gold, gold };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
