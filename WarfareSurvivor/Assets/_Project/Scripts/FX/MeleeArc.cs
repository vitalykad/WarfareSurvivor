using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Дуга замаха под бойцом ближнего боя.
    ///
    /// Решает сразу две задачи, которые по отдельности решались плохо.
    ///
    /// Показывает ДИСТАНЦИЮ: внешний край дуги проходит ровно по дальности
    /// удара. Постоянный круг эту же дистанцию показывал, но выглядел
    /// разметкой и висел на экране, когда его никто не спрашивал. Дуга
    /// появляется в момент замаха и гаснет — то есть отвечает тогда, когда
    /// вопрос возникает.
    ///
    /// И даёт удару ВЕС. Это важнее: игрок инстинктивно убегает от толпы,
    /// хотя лопата работает только вплотную. Широкий светящийся взмах,
    /// накрывающий полдесятка врагов, — довод за то, чтобы войти внутрь,
    /// а не объяснение, почему это не страшно.
    ///
    /// След по самому оружию тут не годится: полотно лопаты уходит на метр
    /// с небольшим, а достаёт боец на четыре, и такой след соврал бы про
    /// дистанцию ровно там, где игрок ему поверит.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class MeleeArc : MonoBehaviour
    {
        static readonly int SweepId = Shader.PropertyToID("_Sweep");

        MeshRenderer view;
        MaterialPropertyBlock block;

        float sweep = 1f;
        float sweepSpeed = 4f;

        /// <summary>
        /// Докуда доводить счётчик взмаха.
        ///
        /// Единица — дуга прочерчена и погасла. Само прочерчивание занимает
        /// малую долю хода: медленно ползущая полоса читается как полоса,
        /// а не как удар, поэтому дуга вспыхивает почти сразу.
        /// </summary>
        const float Overshoot = 1f;

        public static MeleeArc Attach(Transform owner, float range, Material material,
                                      float arcDegrees, float innerFraction,
                                      float height, float tilt)
        {
            if (owner == null || material == null || range <= 0f) return null;

            var go = new GameObject("ДугаЗамаха",
                typeof(MeshFilter), typeof(MeshRenderer), typeof(MeleeArc));
            go.transform.SetParent(owner, false);

            // В ВОЗДУХЕ, на высоте оружия, а не на земле.
            //
            // Лежащая дуга читается как наклейка на полу: пол неподвижен,
            // и всё, что на нём нарисовано, воспринимается как разметка.
            // Поднятая на высоту пояса она попадает туда, где рука и ведёт
            // лопату, и становится изображением удара.
            go.transform.localPosition = new Vector3(0f, height, 0f);

            // Поворот на 90 по X ставит плоскость меша горизонтально:
            // местное «вперёд» (+Y) становится направлением взгляда бойца.
            // Небольшой завал по Z даёт взмаху наклон — совсем ровный
            // выглядит чертежом.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, tilt);

            go.GetComponent<MeshFilter>().sharedMesh = BuildArc(range, arcDegrees, innerFraction);

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var arc = go.GetComponent<MeleeArc>();
            arc.view = renderer;
            arc.block = new MaterialPropertyBlock();
            arc.Apply();
            return arc;
        }

        /// <summary>Запускает взмах длиной в указанное число секунд.</summary>
        public void Swing(float seconds)
        {
            sweepSpeed = Overshoot / Mathf.Max(0.05f, seconds);
            sweep = 0f;
            Apply();
        }

        void LateUpdate()
        {
            if (sweep >= Overshoot) return;

            sweep += sweepSpeed * Time.deltaTime;
            Apply();
        }

        void Apply()
        {
            if (view == null) return;

            // Догоревшую дугу выключаем целиком: прозрачная полоса всё равно
            // рисуется и льёт пиксели, а их у нас считанные.
            bool visible = sweep < Overshoot;
            if (view.enabled != visible) view.enabled = visible;
            if (!visible) return;

            block.SetFloat(SweepId, sweep);
            view.SetPropertyBlock(block);
        }

        /// <summary>
        /// Полоса-сектор: от внутреннего радиуса до дальности удара.
        ///
        /// Развёртка несёт смысл. uv.x — доля пройденной дуги, по ней шейдер
        /// и разворачивает взмах; uv.y — поперёк полосы, по ней смягчаются
        /// края. Поэтому меш строится один раз, а весь взмах — одно число.
        ///
        /// Дуга идёт СИММЕТРИЧНО вокруг взгляда бойца: он бьёт перед собой,
        /// и взмах, начинающийся сбоку, читался бы как чужой.
        /// </summary>
        static Mesh BuildArc(float range, float arcDegrees, float innerFraction)
        {
            const int segments = 24;

            float inner = range * Mathf.Clamp01(innerFraction);
            float half = Mathf.Clamp(arcDegrees, 20f, 340f) * 0.5f * Mathf.Deg2Rad;

            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = Mathf.Lerp(-half, half, t);

                // Плоскость лежит в XY и кладётся плашмя поворотом объекта,
                // поэтому «вперёд» здесь это +Y.
                var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));

                vertices[i * 2] = direction * inner;
                vertices[i * 2 + 1] = direction * range;

                uv[i * 2] = new Vector2(t, 0f);
                uv[i * 2 + 1] = new Vector2(t, 1f);
            }

            for (int i = 0; i < segments; i++)
            {
                int v = i * 2;
                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;
            }

            var mesh = new Mesh { name = "ДугаЗамаха" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
