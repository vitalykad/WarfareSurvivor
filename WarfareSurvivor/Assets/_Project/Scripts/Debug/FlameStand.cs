using UnityEngine;

namespace WarfareSurvivor
{
    /// Замерочный стенд: N источников огня по кругу, без боя и без ИИ.
    /// Нужен, чтобы сравнивать настройки пламени в одинаковых условиях —
    /// в живом забое количество стреляющих плавает и числа не сходятся.
    public class FlameStand : MonoBehaviour
    {
        public GameObject flameEffect;
        public float scale = 1.2f;
        public int sources = 16;
        public float ring = 3f;
        public float ahead = 26f;

        Vector3[] origins;
        Vector3[] dirs;
        object[] owners;

        void Start()
        {
            FlameLayer.Configure(flameEffect);

            QualitySettings.vSyncCount = 0;   // иначе кадр упирается в 16.7 мс и разницы не видно

            // Куда камера смотрит на земле — иначе кольцо уезжает за край кадра.
            var cam = Camera.main;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            var ground = new Plane(Vector3.up, Vector3.zero);
            var focus = ground.Raycast(ray, out float hit)
                ? ray.GetPoint(hit)
                : cam.transform.position + cam.transform.forward * ahead;
            focus.y = 0f;

            origins = new Vector3[sources];
            dirs = new Vector3[sources];
            owners = new object[sources];
            for (int i = 0; i < sources; i++)
            {
                float a = i / (float)sources * Mathf.PI * 2f;
                var side = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                origins[i] = focus + side * ring + Vector3.up * 0.9f;
                dirs[i] = side;
                owners[i] = this.GetInstanceID() + ":" + i;
            }
        }

        void Update()
        {
            // Панель сборки отряда держит игру на паузе, а строится она уже
            // после нас — снимать паузу приходится каждый кадр, пока не уйдёт.
            if (Time.timeScale == 0f)
            {
                var picker = GameObject.Find("Канва стенда");
                if (picker != null) picker.SetActive(false);
                Time.timeScale = 1f;
            }

            for (int i = 0; i < sources; i++)
                FlameLayer.Emit(owners[i], origins[i], dirs[i], 1f, scale, Time.deltaTime);
        }
    }
}
