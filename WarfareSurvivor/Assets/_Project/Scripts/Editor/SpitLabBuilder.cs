using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WarfareSurvivor.EditorTools
{
    /// <summary>
    /// Собирает сцену-стенд для кислотного плевка.
    ///
    /// Отдельная сцена, а не уголок боевой: в бою снаряд занимает пару
    /// десятков пикселей среди толпы, цифр урона и бутылок, и по такому
    /// кадру нельзя понять, что именно нарисовано. Здесь снаряд летит
    /// поперёк пустого кадра, камера стоит вплотную.
    /// </summary>
    public static class SpitLabBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Sandbox/SpitLab.unity";
        const string ConfigPath = "Assets/_Project/Configs/RunConfig.asset";

        [MenuItem("WarfareSurvivor/Build Spit Lab")]
        public static void Build()
        {
            var config = AssetDatabase.LoadAssetAtPath<ArenaConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogError("[SpitLab] Не найден конфиг " + ConfigPath);
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Земля того же цвета, что арена: на белом фоне аддитивное
            // свечение врёт, а вся эта возня как раз про то, как оно
            // ложится на песок.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Земля";
            ground.transform.localScale = Vector3.one * 4f;
            var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            groundMaterial.color = new Color(0.98f, 0.72f, 0.45f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            var lightGo = new GameObject("Свет");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cameraGo = new GameObject("Камера", typeof(Camera));
            var view = cameraGo.GetComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(0.98f, 0.72f, 0.45f);
            view.fieldOfView = config.cameraFieldOfView;
            cameraGo.tag = "MainCamera";

            // Тот же наклон, что в игре, но ВПЛОТНУЮ: снаряд должен занимать
            // половину кадра, иначе стенд не отличается от боевой сцены.
            cameraGo.transform.rotation = Quaternion.Euler(config.cameraPitch, config.cameraYaw, 0f);
            cameraGo.transform.position = new Vector3(0f, 1.2f, 0f)
                                          - cameraGo.transform.forward * 14f;

            var labGo = new GameObject("Стенд плевка");
            var lab = labGo.AddComponent<SpitLab>();

            // Присваиваем НАПРЯМУЮ. Через SerializedObject ссылка в сцену
            // не сохранилась: стенд поднимался с пустым конфигом и падал
            // каждый кадр, не сделав ни выстрела.
            lab.config = config;

            EnsureFolder("Assets/_Project/Scenes/Sandbox");
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log("[SpitLab] Сцена собрана: " + ScenePath +
                      ". Снаряд летит поперёк кадра, камера в четырнадцати метрах.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            int cut = path.LastIndexOf('/');
            string parent = path.Substring(0, cut);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(cut + 1));
        }
    }
}
