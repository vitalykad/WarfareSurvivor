using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>Имена слоёв и раскладка объектов по ним.</summary>
    public static class LayerUtility
    {
        public const string Survivors = "Survivors";
        public const string Zombies = "Zombies";
        public const string Environment = "Environment";

        /// <summary>
        /// Земля отдельно от остального окружения: это один квад во весь
        /// экран, и платится он чистой заливкой, а руины — геометрией.
        /// Мерить их вместе значит не узнать ни того, ни другого.
        /// </summary>
        public const string Ground = "Ground";

        /// <summary>
        /// Кладёт объект и всех потомков на слой. Потомков обязательно:
        /// маска камеры смотрит на слой КАЖДОГО рендерера, а у персонажа
        /// меш висит на дочернем объекте.
        /// </summary>
        public static void Apply(GameObject root, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0 || root == null) return;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }
    }
}
