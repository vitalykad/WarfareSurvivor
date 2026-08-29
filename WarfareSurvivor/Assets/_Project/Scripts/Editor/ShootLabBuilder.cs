using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WarfareSurvivor.EditorTools
{
    /// <summary>
    /// Собирает стенд наведения: стрелок, цель по кругу и три линии.
    ///
    /// Отдельная сцена по той же причине, что и у плевка: в бою вопрос
    /// «куда развёрнут коп» неразрешим — цель за кадром, фигура мелкая,
    /// а трасса рисуется от дула до жертвы и выглядит правильной всегда,
    /// даже когда ствол смотрит в сторону.
    /// </summary>
    public static class ShootLabBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Sandbox/ShootLab.unity";

        [MenuItem("WarfareSurvivor/Build Shoot Lab")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Земля";
            ground.transform.localScale = Vector3.one * 3f;
            var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            groundMaterial.color = new Color(0.62f, 0.62f, 0.66f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            var lightGo = new GameObject("Свет");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, 20f, 0f);

            var cameraGo = new GameObject("Камера", typeof(Camera));
            var view = cameraGo.GetComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(0.30f, 0.32f, 0.36f);
            view.fieldOfView = 40f;
            cameraGo.tag = "MainCamera";

            var labGo = new GameObject("Стенд стрельбы");
            var lab = labGo.AddComponent<ShootLab>();
            lab.who = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Survivors/Survivor_Police.prefab");
            lab.klass = AssetDatabase.LoadAssetAtPath<SurvivorClassSO>(
                "Assets/_Project/Configs/Classes/Police.asset");

            // Сверху, как в игре: ошибку разворота ловим в том же ракурсе,
            // в котором её видит игрок, а не в удобном виде сбоку.
            lab.viewPitch = 58f;
            lab.viewYaw = 180f;
            lab.viewDistance = 7f;
            lab.viewHeight = 1f;

            EnsureFolder("Assets/_Project/Scenes/Sandbox");
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log("[ShootLab] Сцена собрана: " + ScenePath +
                      ". Синяя линия — корень, красная — ствол, зелёная — цель.");
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
