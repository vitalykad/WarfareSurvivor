using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Печёт анимацию скелета в текстуру: строка — кадр, столбец — вершина,
    /// в пикселе лежит позиция вершины в этом кадре.
    ///
    /// Никаких сторонних инструментов не нужно. AnimationMode ставит риг
    /// в позу так же, как это делает окно анимации, BakeMesh снимает
    /// результат, а дальше остаётся переложить вершины в текстуру.
    ///
    /// Почему AnimationMode, а не clip.SampleAnimation: клипы гуманоидные,
    /// и для них выборка идёт через ретаргетинг. SampleAnimation на голом
    /// объекте отдаёт исходные кривые, а не то, во что они превращаются
    /// на нашем скелете, — то есть не то, что видно в игре.
    /// </summary>
    public static class VertexAnimationBaker
    {
        const string ZombiePrefab = "Assets/_Project/Prefabs/Monsters/Monster_Zombie.prefab";
        const string OutputFolder = "Assets/_Project/Art/Baked";

        [MenuItem("WarfareSurvivor/Bake Zombie Animation")]
        public static void BakeZombie() => Bake(ZombiePrefab, "Zombie");

        public static BakedAnimationSet Bake(string prefabPath, string label)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError("[Запекание] Не найден префаб " + prefabPath);
                return null;
            }

            var instance = Object.Instantiate(prefab);
            instance.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                return BakeInstance(instance, label);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        static BakedAnimationSet BakeInstance(GameObject instance, string label)
        {
            var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var animator = instance.GetComponentInChildren<Animator>(true);

            if (skin == null || animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogError("[Запекание] У префаба нет скиннера или аниматора с контроллером");
                return null;
            }

            var clips = UniqueClips(animator.runtimeAnimatorController.animationClips);
            if (clips.Count == 0)
            {
                Debug.LogError("[Запекание] В контроллере нет клипов");
                return null;
            }

            int width = skin.sharedMesh.vertexCount;
            var baked = new List<BakedClip>(clips.Count);
            int rows = 0;
            foreach (var clip in clips)
            {
                int frames = FrameCount(clip);
                baked.Add(new BakedClip
                {
                    name = clip.name,
                    startRow = rows,
                    frames = frames,
                    fps = clip.frameRate,
                    looping = clip.isLooping
                });
                rows += frames;
            }

            var positions = new Color[width * rows];
            var normals = new Color[width * rows];
            var bounds = new Bounds();
            bool boundsStarted = false;
            Mesh firstFrame = null;

            var scratch = new Mesh { hideFlags = HideFlags.HideAndDontSave };

            AnimationMode.StartAnimationMode();
            try
            {
                for (int c = 0; c < clips.Count; c++)
                {
                    var clip = clips[c];
                    var info = baked[c];

                    for (int f = 0; f < info.frames; f++)
                    {
                        float time = Mathf.Min(f / info.fps, clip.length);

                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(instance, clip, time);
                        AnimationMode.EndSampling();

                        // Без масштаба: меш живёт под тем же объектом, что
                        // и скиннер, и масштаб тира применит сам transform.
                        skin.BakeMesh(scratch, false);

                        var verts = scratch.vertices;
                        var norms = scratch.normals;
                        if (verts.Length != width)
                        {
                            Debug.LogError("[Запекание] Число вершин поехало: ждали " +
                                           width + ", получили " + verts.Length);
                            return null;
                        }

                        int row = (info.startRow + f) * width;
                        for (int v = 0; v < width; v++)
                        {
                            var p = verts[v];
                            var n = v < norms.Length ? norms[v] : Vector3.up;
                            positions[row + v] = new Color(p.x, p.y, p.z, 1f);
                            normals[row + v] = new Color(n.x, n.y, n.z, 1f);
                        }

                        if (!boundsStarted)
                        {
                            bounds = scratch.bounds;
                            boundsStarted = true;
                            firstFrame = Object.Instantiate(scratch);
                        }
                        else bounds.Encapsulate(scratch.bounds);
                    }
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(scratch);
            }

            return SaveAssets(label, firstFrame, positions, normals, baked, width, rows, bounds);
        }

        /// <summary>
        /// Сколько строк занимает клип.
        ///
        /// У зациклённого последний кадр совпадает с первым, и хранить его
        /// незачем: воспроизведение всё равно вернётся к нулевой строке.
        /// У одноразового последний кадр нужен целиком — на нём поза
        /// и остаётся.
        /// </summary>
        static int FrameCount(AnimationClip clip)
        {
            int frames = Mathf.RoundToInt(clip.length * clip.frameRate);
            if (!clip.isLooping) frames += 1;
            return Mathf.Max(1, frames);
        }

        static List<AnimationClip> UniqueClips(AnimationClip[] source)
        {
            var seen = new HashSet<string>();
            var result = new List<AnimationClip>();
            foreach (var clip in source)
            {
                if (clip == null || !seen.Add(clip.name)) continue;
                result.Add(clip);
            }
            return result;
        }

        static BakedAnimationSet SaveAssets(string label, Mesh source,
            Color[] positions, Color[] normals, List<BakedClip> clips,
            int width, int rows, Bounds bounds)
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Art", "Baked");

            var mesh = BuildStaticMesh(source, width, bounds);
            mesh.name = label + "_VAT_Mesh";

            var posTex = BuildTexture(label + "_VAT_Pos", positions, width, rows);
            var nrmTex = BuildTexture(label + "_VAT_Nrm", normals, width, rows);

            var set = ScriptableObject.CreateInstance<BakedAnimationSet>();
            set.mesh = mesh;
            set.positions = posTex;
            set.normals = nrmTex;
            set.clips = clips.ToArray();
            set.vertexCount = width;
            set.totalRows = rows;

            string setPath = OutputFolder + "/" + label + "_VAT.asset";
            AssetDatabase.CreateAsset(set, setPath);
            AssetDatabase.AddObjectToAsset(mesh, set);
            AssetDatabase.AddObjectToAsset(posTex, set);
            AssetDatabase.AddObjectToAsset(nrmTex, set);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(setPath);

            var report = new System.Text.StringBuilder();
            report.Append("[Запекание] ").Append(label).Append(": текстура ")
                  .Append(width).Append('x').Append(rows)
                  .Append(", ").Append((width * rows * 8 * 2 / 1024f / 1024f).ToString("F1"))
                  .Append(" МБ на пару, клипов ").Append(clips.Count);
            foreach (var clip in clips)
                report.Append("\n    ").Append(clip.name).Append(": строки ")
                      .Append(clip.startRow).Append("..").Append(clip.startRow + clip.frames - 1)
                      .Append(clip.looping ? " (зациклен)" : " (одноразовый)");
            Debug.Log(report.ToString());

            return set;
        }

        /// <summary>
        /// Меш без костей. Позиции вершин в нём не используются — шейдер
        /// берёт их из текстуры, — но нужны развёртка, номер вершины
        /// и габариты.
        ///
        /// Номер кладём во второй набор UV сразу как координату текстуры:
        /// иначе шейдеру пришлось бы делить на ширину в каждой вершине.
        /// Половина пикселя добавлена, чтобы попадать в его середину,
        /// а не на границу между соседними.
        ///
        /// Габариты — объединение всех кадров, иначе отсечение выбросит
        /// зомби с экрана на замахе, когда он выходит за габариты первого
        /// кадра.
        /// </summary>
        static Mesh BuildStaticMesh(Mesh source, int width, Bounds bounds)
        {
            var mesh = Object.Instantiate(source);
            // Копия унаследовала бы «не сохранять» от временного меша,
            // и запись в ассет молча ничего бы не дала.
            mesh.hideFlags = HideFlags.None;

            var index = new Vector2[width];
            for (int v = 0; v < width; v++) index[v] = new Vector2((v + 0.5f) / width, 0f);
            mesh.uv2 = index;

            mesh.bounds = bounds;
            mesh.UploadMeshData(false);
            return mesh;
        }

        static Texture2D BuildTexture(string name, Color[] pixels, int width, int rows)
        {
            // Половинная точность: позиции лежат в пределах пары метров,
            // и шаг у half там около миллиметра — глазу не видно, а памяти
            // вдвое меньше, чем на полной.
            var texture = new Texture2D(width, rows, TextureFormat.RGBAHalf, false, true)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}
