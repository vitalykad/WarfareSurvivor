using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>Один запечённый клип: где лежит в текстуре и как быстро играть.</summary>
    [System.Serializable]
    public struct BakedClip
    {
        public string name;

        /// <summary>Строка текстуры, с которой начинается клип.</summary>
        public int startRow;

        /// <summary>Сколько строк занимает.</summary>
        public int frames;

        public float fps;
        public bool looping;

        public float Length => fps > 0f ? frames / fps : 0f;
    }

    /// <summary>
    /// Анимация, запечённая в текстуру: вместо сорока одной кости на вершину
    /// вершинный шейдер читает готовую позицию из текстуры.
    ///
    /// Зачем это вообще. Стенд показал (PERFORMANCE.md §6), что сотня зомби
    /// стоит около двадцати четырёх миллисекунд, и десять из них — скиннинг.
    /// Вызовы отрисовки при этом стоят три десятых миллисекунды, поэтому
    /// BatchRendererGroup не нужен: убрать надо кости, а не вызовы.
    ///
    /// Расплата — потеря контроля над костями. Оружие в руку не вложить,
    /// корпус к цели не довернуть. Для зомби это неважно: они бегут
    /// и умирают. Бойцам такое не подойдёт.
    /// </summary>
    public class BakedAnimationSet : ScriptableObject
    {
        [Tooltip("Меш без костей. Позиции вершин в нём не важны — шейдер их " +
                 "заменяет; важны развёртка, индекс вершины во втором наборе " +
                 "UV и габариты для отсечения.")]
        public Mesh mesh;

        [Tooltip("Позиции вершин: строка — кадр, столбец — вершина.")]
        public Texture2D positions;

        [Tooltip("Нормали в той же раскладке. Без них не посчитать освещение.")]
        public Texture2D normals;

        [Tooltip("Шейдер, умеющий читать эти текстуры. Ссылкой, а не поиском " +
                 "по имени: найденное через Shader.Find сборщик считает " +
                 "неиспользуемым и выбрасывает, и в билде зомби молча " +
                 "откатываются на кости. На этом проект уже подрывался.")]
        public Shader shader;

        [Tooltip("Материал-основа для запечённых зомби. Именно его и надо " +
                 "править, чтобы поменять их вид: цвет тени, границу света, " +
                 "мягкость, непрямой свет. Материалы тиров делаются его " +
                 "копиями, у которых заменён только оттенок.")]
        public Material material;

        public BakedClip[] clips;

        [Tooltip("Сколько вершин в меше — ширина текстуры.")]
        public int vertexCount;

        [Tooltip("Сколько всего кадров запечено — высота текстуры.")]
        public int totalRows;

        public int IndexOf(string clipName)
        {
            if (clips == null) return -1;
            for (int i = 0; i < clips.Length; i++)
                if (clips[i].name == clipName) return i;
            return -1;
        }
    }
}
