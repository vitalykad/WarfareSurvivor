using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Болт арбалета: летит и прошивает нескольких насквозь.
    ///
    /// НАСТОЯЩИЙ ЛЕТЯЩИЙ СНАРЯД, а не мгновенное попадание с трассой, как
    /// у пистолета. Разница здесь смысловая: пробитие читается только если
    /// видно, как один и тот же болт входит в первого и выходит из второго.
    /// Мгновенный выстрел с уроном сразу троим выглядит очередью, и роль
    /// перестаёт отличаться от скорострельного стрелка.
    ///
    /// Попадание считается по ОТРЕЗКУ, пройденному за кадр, а не по точке.
    /// Болт летит быстро, за кадр проходит больше полуметра, и проверка
    /// по текущей точке просто перепрыгивала бы через тонкие цели.
    /// </summary>
    public class CrossbowBolt : MonoBehaviour
    {
        static readonly List<CrossbowBolt> All = new List<CrossbowBolt>();
        static ArenaConfig config;
        static Transform root;
        static Mesh boltMesh;
        static Material boltMaterial;
        static int nextVictim;

        readonly List<Zombie> struck = new List<Zombie>();

        Vector3 velocity;
        float damage;
        int pierceLeft;
        float travelLeft;

        public static void Configure(ArenaConfig cfg, Mesh mesh, Material material)
        {
            config = cfg;
            if (mesh != null) boltMesh = mesh;
            if (material != null) boltMaterial = material;

            if (root == null)
            {
                root = new GameObject("CrossbowBolts").transform;
                All.Clear();
            }
        }

        /// <summary>Выпускает болт. Направление задаёт полёт, длина — дальность.</summary>
        public static void Fire(Vector3 from, Vector3 direction, float damage,
                                int pierce, float range)
        {
            if (config == null || root == null || boltMesh == null) return;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            direction.Normalize();

            var bolt = Rent();
            if (bolt == null) return;

            bolt.velocity = direction * Mathf.Max(1f, config.boltSpeed);
            bolt.damage = damage;
            bolt.pierceLeft = Mathf.Max(1, pierce);
            bolt.travelLeft = Mathf.Max(1f, range);
            bolt.struck.Clear();

            bolt.transform.position = from;
            bolt.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

            // Толще и длиннее модели: стрела в ней три сантиметра, с игровой
            // дистанции это меньше пикселя. Толщину и длину крутим порознь —
            // раздутый во все стороны болт превращается в бревно.
            bolt.transform.localScale = new Vector3(
                Mathf.Max(0.1f, config.boltThickness),
                Mathf.Max(0.1f, config.boltThickness),
                Mathf.Max(0.1f, config.boltLengthScale));

            bolt.gameObject.SetActive(true);
        }

        static CrossbowBolt Rent()
        {
            for (int i = 0; i < All.Count; i++)
                if (!All[i].gameObject.activeSelf)
                    return All[i];

            if (All.Count < Mathf.Max(4, config.maxBolts))
            {
                var created = Create();
                All.Add(created);
                return created;
            }

            // Потолок выбран — отнимаем самый старый по кругу. Пропустить
            // выстрел нельзя: пропадёт обратная связь ровно тогда, когда
            // стреляют чаще всего.
            nextVictim = (nextVictim + 1) % All.Count;
            return All[nextVictim];
        }

        static CrossbowBolt Create()
        {
            var go = new GameObject("Bolt");
            go.transform.SetParent(root, false);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = boltMesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = boltMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            var bolt = go.AddComponent<CrossbowBolt>();
            go.SetActive(false);
            return bolt;
        }

        void Update()
        {
            if (config == null) { gameObject.SetActive(false); return; }

            var step = velocity * Time.deltaTime;
            float length = step.magnitude;
            if (length < 0.0001f) { gameObject.SetActive(false); return; }

            var from = transform.position;
            var to = from + step;

            Pierce(from, to);

            // След кладём ЗА снарядом отрезком, пройденным за кадр: он
            // ложится в общую сетку трасс и не стоит ни одного лишнего
            // вызова отрисовки.
            if (config.boltTrailWidth > 0f)
                TracerLayer.Streak(from, to, config.boltTrailWidth,
                                   config.boltTrailColor, config.boltTrailLife);

            transform.position = to;
            travelLeft -= length;

            if (travelLeft <= 0f || pierceLeft <= 0) gameObject.SetActive(false);
        }

        /// <summary>Кого задело на отрезке, пройденном за этот кадр.</summary>
        void Pierce(Vector3 from, Vector3 to)
        {
            float radius = Mathf.Max(0.1f, config.boltHitRadius);
            var step = to - from;
            float length = step.magnitude;
            var direction = step / length;

            var zombies = Registry.Zombies;
            for (int i = zombies.Count - 1; i >= 0 && pierceLeft > 0; i--)
            {
                if (i >= zombies.Count) continue;

                var zombie = zombies[i];
                if (zombie == null || zombie.IsDead || !zombie.isActiveAndEnabled) continue;
                if (struck.Contains(zombie)) continue;

                var toZombie = zombie.transform.position - from;
                toZombie.y = 0f;

                // Проекция на отрезок: попадание считаем только в пределах
                // пройденного за кадр, иначе болт «задевал» бы тех, кто
                // остался далеко позади.
                float along = Vector3.Dot(toZombie, direction);
                if (along < -radius || along > length + radius) continue;

                float side = (toZombie - direction * along).magnitude;
                if (side > radius + zombie.BodyRadius) continue;

                struck.Add(zombie);
                zombie.TakeHit(damage);
                pierceLeft--;
            }
        }
    }
}
