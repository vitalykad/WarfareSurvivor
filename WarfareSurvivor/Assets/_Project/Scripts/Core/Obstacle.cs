using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Непроходимое препятствие. Тело описывается ПОВЁРНУТОЙ КОРОБКОЙ, а не
    /// кругом: руины вытянутые, и круг вокруг такой стены отъедал бы вдвое
    /// больше прохода, чем занимает сама стена.
    ///
    /// Столкновения решаются сдвигом позиции, а не физикой: юниты двигаются
    /// напрямую через transform, и добавлять ради стен Rigidbody каждому из
    /// двух сотен зомби значило бы менять всю модель движения. Тем же способом
    /// уже расталкиваются зомби между собой.
    ///
    /// Побочный эффект сдвига приятный: упершийся юнит продолжает давить
    /// вперёд, коррекция гасит только составляющую внутрь стены, и он
    /// съезжает вдоль неё — то есть обходит препятствие сам, без поиска пути.
    /// </summary>
    public class Obstacle : MonoBehaviour
    {
        Vector2 localCenter;
        Vector2 localHalfExtents;

        Vector3 worldCenter;
        float worldRadius;

        void OnEnable()
        {
            Measure();
            if (!Registry.Obstacles.Contains(this)) Registry.Obstacles.Add(this);
        }

        void OnDisable() => Registry.Obstacles.Remove(this);

        /// <summary>
        /// Габарит снимается с мешей в локальных осях объекта — тогда поворот
        /// и масштаб учитываются сами собой через трансформ.
        /// </summary>
        void Measure()
        {
            var filters = GetComponentsInChildren<MeshFilter>();
            if (filters.Length == 0) return;

            bool started = false;
            var bounds = new Bounds();

            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;

                var meshBounds = mesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));

                    var local = transform.InverseTransformPoint(filter.transform.TransformPoint(point));
                    if (started) bounds.Encapsulate(local);
                    else { bounds = new Bounds(local, Vector3.zero); started = true; }
                }
            }

            if (!started) return;

            localCenter = new Vector2(bounds.center.x, bounds.center.z);
            localHalfExtents = new Vector2(bounds.extents.x, bounds.extents.z);

            worldCenter = transform.TransformPoint(new Vector3(localCenter.x, 0f, localCenter.y));
            worldCenter.y = 0f;

            // Радиус для грубого отсева. Берём по описанной окружности,
            // чтобы отсев никогда не отбросил настоящее пересечение.
            var extentWorld = transform.TransformVector(
                new Vector3(localHalfExtents.x, 0f, localHalfExtents.y));
            worldRadius = new Vector2(extentWorld.x, extentWorld.z).magnitude;
        }

        /// <summary>Выталкивает точку из всех препятствий. Возвращает исправленную позицию.</summary>
        public static Vector3 Resolve(Vector3 position, float radius)
        {
            var all = Registry.Obstacles;
            for (int i = 0; i < all.Count; i++)
            {
                var obstacle = all[i];
                if (obstacle == null) continue;
                position = obstacle.PushOut(position, radius);
            }
            return position;
        }

        Vector3 PushOut(Vector3 world, float radius)
        {
            // Грубый отсев: без него каждый из двух сотен зомби считал бы
            // полную геометрию каждой руины каждый кадр.
            var flat = new Vector2(world.x - worldCenter.x, world.z - worldCenter.z);
            float reach = worldRadius + radius;
            if (flat.sqrMagnitude > reach * reach) return world;

            var local = transform.InverseTransformPoint(world);
            float lx = local.x - localCenter.x;
            float lz = local.z - localCenter.y;
            float hx = localHalfExtents.x;
            float hz = localHalfExtents.y;

            bool inside = Mathf.Abs(lx) < hx && Mathf.Abs(lz) < hz;

            if (!inside)
            {
                var nearestLocal = new Vector3(
                    Mathf.Clamp(lx, -hx, hx) + localCenter.x,
                    local.y,
                    Mathf.Clamp(lz, -hz, hz) + localCenter.y);

                var nearest = transform.TransformPoint(nearestLocal);
                var away = world - nearest;
                away.y = 0f;

                float distance = away.magnitude;
                if (distance >= radius) return world;
                if (distance > 0.0001f) return nearest + away / distance * radius + Vector3.up * (world.y - nearest.y);

                // Ровно на грани — направления нет, дальше работает ветка «внутри».
                inside = true;
            }

            // Внутри: выходим по оси наименьшего проникновения, так путь короче
            // и юнита не протаскивает сквозь всю стену насквозь.
            float penetrationX = hx - Mathf.Abs(lx);
            float penetrationZ = hz - Mathf.Abs(lz);

            var localNormal = penetrationX < penetrationZ
                ? new Vector3(Mathf.Sign(lx == 0f ? 1f : lx), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(lz == 0f ? 1f : lz));

            var normal = transform.TransformDirection(localNormal);
            normal.y = 0f;
            if (normal.sqrMagnitude < 0.0001f) return world;
            normal.Normalize();

            float depth = Mathf.Min(penetrationX, penetrationZ);
            var scaleAlongNormal = transform.TransformVector(localNormal).magnitude;

            return world + normal * (depth * scaleAlongNormal + radius);
        }
    }
}
