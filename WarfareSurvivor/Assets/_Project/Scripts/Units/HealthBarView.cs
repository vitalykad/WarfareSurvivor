using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Полоска здоровья над бойцом. Показывается только по факту урона и гаснет
    /// сама: восемь постоянных прямоугольников над отрядом читаются как мусор
    /// и мешают видеть сам отряд.
    ///
    /// Собрана из двух квадов, а не из world-space Canvas: канвас на каждого
    /// юнита тянет перестройку UI-меша, а нам нужен прямоугольник.
    /// </summary>
    public class HealthBarView : MonoBehaviour
    {
        static Material backgroundMaterial;
        static Material fillMaterial;

        [SerializeField] float width = 1.1f;
        [SerializeField] float height = 0.12f;

        ArenaConfig config;
        Health health;
        Transform pivot;
        Transform fill;
        Camera view;
        float hideTime;

        public void Bind(Health source, ArenaConfig cfg)
        {
            config = cfg;
            health = source;
            view = Camera.main;

            Build();
            health.Damaged += OnDamaged;
            SetVisible(false);
        }

        void OnDestroy()
        {
            if (health != null) health.Damaged -= OnDamaged;
        }

        void Build()
        {
            float top = MeasureHeight();

            pivot = new GameObject("HealthBar").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = Vector3.up * top;

            var background = CreateQuad("Background", ref backgroundMaterial, new Color(0.08f, 0.08f, 0.09f, 1f));
            background.SetParent(pivot, false);
            background.localScale = new Vector3(width, height, 1f);

            fill = CreateQuad("Fill", ref fillMaterial, new Color(0.35f, 0.85f, 0.35f, 1f));
            fill.SetParent(pivot, false);
            fill.localPosition = new Vector3(0f, 0f, -0.01f);
            fill.localScale = new Vector3(width, height * 0.75f, 1f);
        }

        float MeasureHeight()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 2f;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            // Относительно габарита модели, а не в абсолютных метрах:
            // масштаб персонажей ещё будет меняться.
            return bounds.size.y * 1.15f;
        }

        static Transform CreateQuad(string name, ref Material shared, Color color)
        {
            if (shared == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                shared = new Material(shader) { name = "HealthBar_" + name };
                // URP зовёт цвет _BaseColor, встроенный Unlit — _Color.
                // Пишем в то, что шейдер реально объявляет.
                if (shared.HasProperty("_BaseColor")) shared.SetColor("_BaseColor", color);
                if (shared.HasProperty("_Color")) shared.SetColor("_Color", color);
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Destroy(quad.GetComponent<Collider>());
            quad.GetComponent<MeshRenderer>().sharedMaterial = shared;
            quad.GetComponent<MeshRenderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            return quad.transform;
        }

        void OnDamaged(float amount, Vector3 point)
        {
            hideTime = Time.time + config.healthBarVisibleTime;
            SetVisible(true);
        }

        void LateUpdate()
        {
            if (pivot == null || !pivot.gameObject.activeSelf) return;

            if (Time.time >= hideTime)
            {
                SetVisible(false);
                return;
            }

            if (view != null) pivot.rotation = view.transform.rotation;

            float fraction = health.Fraction;
            fill.localScale = new Vector3(width * fraction, fill.localScale.y, 1f);
            // Полоска убывает влево, а не сжимается к центру.
            fill.localPosition = new Vector3(-width * (1f - fraction) * 0.5f, 0f, -0.01f);
        }

        void SetVisible(bool visible)
        {
            if (pivot != null) pivot.gameObject.SetActive(visible);
        }
    }
}
