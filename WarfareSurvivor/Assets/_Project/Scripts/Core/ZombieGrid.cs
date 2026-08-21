using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Сетка для расталкивания зомби.
    ///
    /// Зачем вообще сетка: расталкивание требует знать соседей, а «каждый с
    /// каждым» на двух сотнях — это сорок тысяч пар в кадр. Разложив толпу по
    /// клеткам размером с зону влияния, каждый смотрит только 3×3 клетки
    /// вокруг себя, и стоимость становится линейной по числу зомби.
    ///
    /// Перестраивается ЛЕНИВО, один раз за кадр: первый обратившийсязомби
    /// её и строит. Так не нужно ни отдельного менеджера, ни возни с порядком
    /// выполнения скриптов — при любом порядке сетка окажется свежей.
    /// </summary>
    public static class ZombieGrid
    {
        static readonly Dictionary<long, List<Zombie>> Cells = new Dictionary<long, List<Zombie>>();
        static readonly Stack<List<Zombie>> Spare = new Stack<List<Zombie>>();

        static int builtFrame = -1;
        static float cellSize = 2f;

        public static void EnsureBuilt(float interactionDistance)
        {
            if (builtFrame == Time.frameCount) return;
            builtFrame = Time.frameCount;

            // Клетка не меньше зоны влияния: иначе пара, попавшая в соседние
            // клетки через одну, друг друга не увидит и зомби слипнутся именно
            // там, где это заметнее всего.
            cellSize = Mathf.Max(0.5f, interactionDistance);

            foreach (var pair in Cells)
            {
                pair.Value.Clear();
                Spare.Push(pair.Value);
            }
            Cells.Clear();

            var zombies = Registry.Zombies;
            for (int i = 0; i < zombies.Count; i++)
            {
                var zombie = zombies[i];
                if (zombie == null) continue;

                var key = KeyOf(zombie.transform.position);
                if (!Cells.TryGetValue(key, out var bucket))
                {
                    bucket = Spare.Count > 0 ? Spare.Pop() : new List<Zombie>(8);
                    Cells[key] = bucket;
                }
                bucket.Add(zombie);
            }
        }

        /// <summary>Соседи из 3×3 клеток вокруг точки. Список переиспользуется вызывающим.</summary>
        public static void Neighbours(Vector3 position, List<Zombie> result)
        {
            result.Clear();

            int cx = Mathf.FloorToInt(position.x / cellSize);
            int cz = Mathf.FloorToInt(position.z / cellSize);

            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!Cells.TryGetValue(Key(cx + dx, cz + dz), out var bucket)) continue;
                for (int i = 0; i < bucket.Count; i++) result.Add(bucket[i]);
            }
        }

        public static void Clear()
        {
            Cells.Clear();
            Spare.Clear();
            builtFrame = -1;
        }

        static long KeyOf(Vector3 p) => Key(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.z / cellSize));

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}
