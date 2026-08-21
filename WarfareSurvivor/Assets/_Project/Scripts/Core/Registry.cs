using System.Collections.Generic;

namespace WarfareSurvivor
{
    /// <summary>
    /// Списки живых юнитов. Поиск цели идёт по ним, а не через физику:
    /// перебор 8×200 раз в 0.2 с дешевле, чем оверлап-сферы каждый кадр,
    /// и не требует настраивать коллайдеры и слои под каждый новый тип.
    /// </summary>
    public static class Registry
    {
        public static readonly List<Survivor> Survivors = new List<Survivor>();
        public static readonly List<Zombie> Zombies = new List<Zombie>();

        /// <summary>Статичные препятствия. Заполняется самими препятствиями при включении.</summary>
        public static readonly List<Obstacle> Obstacles = new List<Obstacle>();

        public static void Clear()
        {
            Survivors.Clear();
            Zombies.Clear();
            Obstacles.Clear();
            ZombieGrid.Clear();
        }
    }
}
