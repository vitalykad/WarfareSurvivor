using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Ракета гранатомётчика: летит НАВЕСОМ и взрывается по площади.
    ///
    /// Навесом, а не по прямой. Прямая трасса — это выстрел, и от пули её
    /// не отличить; дуга же читается как «сейчас упадёт вон туда», и игрок
    /// успевает связать вылет с последующим взрывом. Задержка между
    /// выстрелом и уроном — не помеха, а сама суть роли: она бьёт по тому,
    /// где толпа БУДЕТ, а не где она была.
    ///
    /// Взрыв не пишется заново: он поручен ударной волне, которая уже умеет
    /// расходиться кольцом и раздавать урон по мере того, как фронт доходит
    /// до цели. Дублировать это ради ракеты значило бы завести вторую
    /// площадную механику с теми же ошибками.
    /// </summary>
    public class Rocket : MonoBehaviour
    {
        static readonly List<Rocket> All = new List<Rocket>();
        static ArenaConfig config;
        static Transform root;
        static Mesh rocketMesh;
        static Material rocketMaterial;
        static int nextVictim;

        Vector3 from;
        Vector3 to;
        float flightTime;
        float elapsed;
        float arc;

        float damage;
        float blastRadius;
        float knockDistance;
        float knockDuration;

        public static void Configure(ArenaConfig cfg, Mesh mesh, Material material)
        {
            config = cfg;
            if (mesh != null) rocketMesh = mesh;
            if (material != null) rocketMaterial = material;

            if (root == null)
            {
                root = new GameObject("Rockets").transform;
                All.Clear();
            }
        }

        public static void Fire(Vector3 from, Vector3 to, float damage,
                                float blastRadius, float knockDistance, float knockDuration)
        {
            if (config == null || root == null || rocketMesh == null) return;

            var rocket = Rent();
            if (rocket == null) return;

            rocket.from = from;
            rocket.to = to;
            rocket.damage = damage;
            rocket.blastRadius = Mathf.Max(0.5f, blastRadius);
            rocket.knockDistance = knockDistance;
            rocket.knockDuration = knockDuration;

            // Время полёта от РАССТОЯНИЯ, а не постоянное: иначе ближний
            // выстрел висит в воздухе так же долго, как дальний, и роль
            // вблизи перестаёт работать вовсе.
            float distance = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(to.x, 0f, to.z));
            rocket.flightTime = Mathf.Max(0.15f, distance / Mathf.Max(1f, config.rocketSpeed));
            rocket.elapsed = 0f;

            // Дуга тем выше, чем дальше бросок: ровная высота на коротком
            // броске выглядит подскоком, а на длинном — настильной трассой.
            rocket.arc = distance * Mathf.Max(0f, config.rocketArc);

            rocket.transform.position = from;
            rocket.gameObject.SetActive(true);
        }

        static Rocket Rent()
        {
            for (int i = 0; i < All.Count; i++)
                if (!All[i].gameObject.activeSelf)
                    return All[i];

            if (All.Count < Mathf.Max(2, config.maxRockets))
            {
                var created = Create();
                All.Add(created);
                return created;
            }

            nextVictim = (nextVictim + 1) % All.Count;
            return All[nextVictim];
        }

        static Rocket Create()
        {
            var go = new GameObject("Rocket");
            go.transform.SetParent(root, false);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = rocketMesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = rocketMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            var rocket = go.AddComponent<Rocket>();
            go.SetActive(false);
            return rocket;
        }

        void Update()
        {
            if (config == null) { gameObject.SetActive(false); return; }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flightTime);

            var was = transform.position;
            var flat = Vector3.Lerp(from, to, t);

            // Парабола: ноль на концах, максимум в середине.
            flat.y += arc * 4f * t * (1f - t);
            transform.position = flat;

            // Носом по движению: ракета, летящая боком, читается мусором.
            var step = flat - was;
            if (step.sqrMagnitude > 0.000001f)
                transform.rotation = Quaternion.LookRotation(step.normalized, Vector3.up);

            if (t < 1f) return;

            Explode();
            gameObject.SetActive(false);
        }

        void Explode()
        {
            var at = new Vector3(to.x, 0f, to.z);

            // Урон и кольцо — ударной волной: она уже умеет расходиться
            // и бить по мере того, как фронт доходит до цели.
            ShockWave.Burst(at, blastRadius, damage, knockDistance, knockDuration,
                            Vector3.forward, 360f, 0f, 0f);

            // Огонь поверх волны: пыльное кольцо само по себе читается
            // ударом, но не взрывом.
            int puffs = Mathf.Max(1, config.rocketFirePuffs);
            for (int i = 0; i < puffs; i++)
            {
                var direction = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                FlameJet.Puff(at + Vector3.up * 0.3f, direction, blastRadius, 360f, 1f);
            }
        }
    }
}
