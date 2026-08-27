using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Красный круг на земле — туда прилетит плевок, и его радиус равен
    /// радиусу поражения.
    ///
    /// Зона появляется в тот момент, когда плевун ОСТАНОВИЛСЯ и начал замах,
    /// то есть до выстрела. В этом весь смысл: дальнобойная атака, которую
    /// нельзя увидеть заранее, читается как несправедливость. Увидев круг,
    /// игрок успевает увести отряд — и тогда попадание становится его
    /// ошибкой, а не случайностью.
    ///
    /// Круг лежит на земле квадом, а сам круг рисует шейдер по развёртке:
    /// радиус у каждого плевка свой, и меш-диск пришлось бы перестраивать
    /// под каждый.
    /// </summary>
    public class AcidZone : MonoBehaviour
    {
        static readonly List<AcidZone> All = new List<AcidZone>();
        static ArenaConfig config;
        static Transform root;
        static Material shared;
        static Mesh quad;

        MeshRenderer view;
        MaterialPropertyBlock block;
        float bornTime;
        float dieTime;

        /// <summary>
        /// Метка под стрелком, а не зона поражения. Отличается цветом
        /// и тем, что отсчёт внутри не растёт: под ногами он ничего
        /// не значит — там ничего не взорвётся.
        /// </summary>
        bool mark;

        public static void Configure(ArenaConfig cfg)
        {
            config = cfg;

            if (root == null)
            {
                root = new GameObject("AcidZones").transform;
                All.Clear();
            }
        }

        /// <summary>Метка под стрелком: по ней его находят в толпе.</summary>
        public static AcidZone ShowMark(Vector3 groundPoint, float radius, float duration)
        {
            var zone = Show(groundPoint, radius, duration);
            if (zone != null) zone.mark = true;
            return zone;
        }

        /// <summary>
        /// Показать зону. Живёт заданное время и гаснет сама — но плевун
        /// держит на неё ссылку и снимает досрочно, если умер, не доплюнув.
        /// </summary>
        public static AcidZone Show(Vector3 groundPoint, float radius, float duration)
        {
            if (config == null || root == null) return null;

            var zone = Rent();
            if (zone == null) return null;

            groundPoint.y = 0.02f;
            zone.transform.position = groundPoint;

            // Квад лежит плашмя: развёртка круга — это его же развёртка.
            zone.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            zone.transform.localScale = Vector3.one * Mathf.Max(0.1f, radius * 2f);

            zone.mark = false;
            zone.bornTime = Time.time;
            zone.dieTime = Time.time + Mathf.Max(0.05f, duration);
            zone.gameObject.SetActive(true);
            return zone;
        }

        public void Hide()
        {
            if (this != null) gameObject.SetActive(false);
        }

        static AcidZone Rent()
        {
            for (int i = 0; i < All.Count; i++)
                if (!All[i].gameObject.activeSelf)
                    return All[i];

            if (All.Count >= Mathf.Max(4, config.maxAcidZones))
            {
                // Потолок выбран — забираем самую старую. Новую зону
                // не показать нельзя: тогда прилетит плевок без предупреждения,
                // а это ровно то, чего мы избегаем.
                var oldest = All[0];
                for (int i = 1; i < All.Count; i++)
                    if (All[i].bornTime < oldest.bornTime) oldest = All[i];
                return oldest;
            }

            var created = Create();
            All.Add(created);
            return created;
        }

        static AcidZone Create()
        {
            var go = new GameObject("AcidZone");
            go.transform.SetParent(root, false);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = Quad();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Shared();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            var zone = go.AddComponent<AcidZone>();
            zone.view = renderer;
            zone.block = new MaterialPropertyBlock();
            go.SetActive(false);
            return zone;
        }

        static Mesh Quad()
        {
            if (quad != null) return quad;

            quad = new Mesh { name = "AcidZoneQuad" };
            quad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f)
            };
            quad.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quad.RecalculateBounds();
            return quad;
        }

        static Material Shared()
        {
            if (shared != null) return shared;

            var shader = Shader.Find("WarfareSurvivor/AcidZone");
            if (shader == null)
            {
                Debug.LogError("[AcidZone] Не нашёлся шейдер WarfareSurvivor/AcidZone. " +
                               "Проверь список обязательных шейдеров: без него плевки " +
                               "прилетают без предупреждения.");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            shared = new Material(shader) { name = "AcidZone" };
            return shared;
        }

        void LateUpdate()
        {
            if (config == null || Time.time >= dieTime)
            {
                gameObject.SetActive(false);
                return;
            }

            float life = Mathf.InverseLerp(bornTime, dieTime, Time.time);

            // Цвет и отсчёт идут блоком свойств: зон на экране может быть
            // с десяток, и у каждой свой отсчёт, а материал один на всех.
            view.GetPropertyBlock(block);
            block.SetColor("_ZoneColor", mark ? config.spitterMarkColor : config.acidZoneColor);
            block.SetColor("_RimColor", mark ? config.spitterMarkRimColor : config.acidZoneRimColor);

            // Отсчёт растёт только у зоны поражения: он говорит, сколько
            // осталось до удара. Под ногами стрелка отсчитывать нечего.
            block.SetFloat("_Fill", mark ? 0f : life);

            // Гаснет только в самом конце: зона, тускнеющая всё время,
            // хуже всего видна ровно перед попаданием.
            block.SetFloat("_Fade", 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.88f, 1f, life)));
            view.SetPropertyBlock(block);
        }
    }
}
