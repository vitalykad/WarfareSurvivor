using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WarfareSurvivor.EditorTools
{
    /// <summary>
    /// Стенд для разглядывания персонажа: один боец, крупно, без боя.
    ///
    /// Нужен ровно для таких случаев, как подламывающаяся ступня: в бою
    /// боец занимает полсотни пикселей среди толпы, и понять, что именно
    /// не так с ногой, нельзя. Здесь он один, камера сзади и вплотную,
    /// анимация бега играет на месте.
    ///
    /// Камера СЗАДИ по умолчанию: именно оттуда видно разворот ступни,
    /// а спереди он прячется за второй ногой.
    /// </summary>
    public static class CharacterLabBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Sandbox/CharacterLab.unity";

        [MenuItem("WarfareSurvivor/Build Character Lab")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Земля";
            var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            groundMaterial.color = new Color(0.55f, 0.55f, 0.58f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            var lightGo = new GameObject("Свет");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(45f, 20f, 0f);

            var cameraGo = new GameObject("Камера", typeof(Camera));
            var view = cameraGo.GetComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(0.32f, 0.34f, 0.38f);
            view.fieldOfView = 40f;
            cameraGo.tag = "MainCamera";

            // Сзади и чуть выше пояса: разворот ступни виден только со спины.
            cameraGo.transform.position = new Vector3(0f, 1.1f, -2.6f);
            cameraGo.transform.rotation = Quaternion.Euler(8f, 0f, 0f);

            var labGo = new GameObject("Стенд персонажа");
            var lab = labGo.AddComponent<CharacterLab>();
            lab.who = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Survivors/Survivor_ShovelFarmer.prefab");

            EnsureFolder("Assets/_Project/Scenes/Sandbox");
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log("[CharacterLab] Сцена собрана: " + ScenePath +
                      ". Камера сзади в 2.6 м, боец бежит на месте.");
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
