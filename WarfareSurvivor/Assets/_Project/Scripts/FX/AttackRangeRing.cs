using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Круг досягаемости под бойцом, вспыхивающий в момент удара.
    ///
    /// Решает две задачи разом. Показывает, откуда докуда достаёт оружие —
    /// без этого дальность класса остаётся числом в карточке, которое
    /// не с чем сопоставить. И даёт удару отдачу: лопата бьёт по площади,
    /// а до сих пор попадание читалось только по цифре урона на враге.
    ///
    /// Гаснет сам. Постоянно горящие круги под полутора десятками бойцов
    /// превратили бы поле в кашу, а вспышка привязывает круг к событию.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class AttackRangeRing : MonoBehaviour
    {
        static readonly int FadeId = Shader.PropertyToID("_RingFade");

        MeshRenderer view;
        MaterialPropertyBlock block;
        float fade;
        float fadeTime = 0.35f;

        /// <summary>
        /// Собирает круг под бойцом.
        ///
        /// Плоскость кладётся ПОД самого бойца дочерним объектом, поэтому
        /// ездит с ним сама и не требует обновления позиции каждый кадр.
        /// </summary>
        public static AttackRangeRing Attach(Transform owner, float radius, Material material, float fadeSeconds)
        {
            if (owner == null || material == null || radius <= 0f) return null;

            var go = new GameObject("КругДосягаемости",
                typeof(MeshFilter), typeof(MeshRenderer), typeof(AttackRangeRing));
            go.transform.SetParent(owner, false);

            // Чуть выше земли: вровень с ней плоскость спорит за пиксель
            // и мерцает, хотя шейдер и смещён к камере.
            go.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            go.GetComponent<MeshFilter>().sharedMesh = BuildQuad(radius);

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var ring = go.GetComponent<AttackRangeRing>();
            ring.view = renderer;
            ring.block = new MaterialPropertyBlock();
            ring.fadeTime = Mathf.Max(0.05f, fadeSeconds);
            ring.Apply(0f);
            return ring;
        }

        public void Flash()
        {
            fade = 1f;
            Apply(fade);
        }

        void LateUpdate()
        {
            if (fade <= 0f) return;

            fade -= Time.deltaTime / fadeTime;
            Apply(Mathf.Max(0f, fade));
        }

        void Apply(float value)
        {
            if (view == null) return;

            // Совсем погасший круг выключаем целиком: прозрачная плоскость
            // всё равно рисуется и льёт пиксели, а их у нас считанные.
            bool visible = value > 0.001f;
            if (view.enabled != visible) view.enabled = visible;
            if (!visible) return;

            block.SetFloat(FadeId, value);
            view.SetPropertyBlock(block);
        }

        /// <summary>
        /// Плоскость размером с диаметр досягаемости. Круг рисует шейдер
        /// по развёртке — своя геометрия кольца тут была бы лишней.
        /// </summary>
        static Mesh BuildQuad(float radius)
        {
            var mesh = new Mesh { name = "КругДосягаемости" };
            mesh.vertices = new[]
            {
                new Vector3(-radius, -radius, 0f), new Vector3(-radius, radius, 0f),
                new Vector3(radius, radius, 0f), new Vector3(radius, -radius, 0f)
            };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
