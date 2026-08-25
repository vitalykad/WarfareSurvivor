using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Показывает зомби запечённой анимацией вместо скиннера.
    ///
    /// Скиннер и аниматор выключаются, на их месте обычный меш-рендерер
    /// с меш-фильтром; номер кадра уходит в шейдер блоком свойств.
    /// Логика зомби к этому не имеет отношения и не меняется — он по-прежнему
    /// бежит, бьёт и умирает своими скриптами.
    ///
    /// Блок свойств здесь неизбежен: у сотни зомби кадры разные, а блок
    /// задаётся на каждый рендерер отдельно. Прежний довод против блоков —
    /// они выбивают из общей пачки отрисовки — больше не работает: стенд
    /// намерил, что вызовы отрисовки стоят три десятых миллисекунды
    /// на сотню (PERFORMANCE.md §6).
    /// </summary>
    [DisallowMultipleComponent]
    public class BakedZombieView : MonoBehaviour
    {
        static readonly int RowsId = Shader.PropertyToID("_AnimRows");

        /// <summary>Материал тира -> его двойник на шейдере запечённой анимации.</summary>
        static readonly Dictionary<Material, Material> Twins = new Dictionary<Material, Material>();

        BakedAnimationSet set;
        Shader vatShader;
        MeshRenderer view;
        MaterialPropertyBlock block;

        int clip = -1;
        float time;

        /// <summary>
        /// Переводит готового зомби на запечённую анимацию.
        /// Возвращает null, если переводить нечего или нечем.
        /// </summary>
        public static BakedZombieView Convert(GameObject root, BakedAnimationSet set, Shader vatShader)
        {
            if (root == null || set == null || set.mesh == null || vatShader == null) return null;

            var skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skin == null) return null;

            var host = skin.gameObject;

            var filter = host.GetComponent<MeshFilter>();
            if (filter == null) filter = host.AddComponent<MeshFilter>();
            filter.sharedMesh = set.mesh;

            var renderer = host.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = host.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = skin.shadowCastingMode;
            renderer.receiveShadows = skin.receiveShadows;
            renderer.enabled = true;

            skin.enabled = false;

            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.enabled = false;

            var view = host.GetComponent<BakedZombieView>();
            if (view == null) view = host.AddComponent<BakedZombieView>();
            view.set = set;
            view.vatShader = vatShader;
            view.view = renderer;
            view.block = new MaterialPropertyBlock();
            return view;
        }

        /// <summary>
        /// Ставит клип с начала. У зациклённых фаза случайная: иначе сотня
        /// зомби бежит нога в ногу, и толпа выглядит строем.
        /// </summary>
        /// <summary>Во сколько раз быстрее играть текущий клип.</summary>
        float speed = 1f;

        public void Play(string clipName, float playbackSpeed = 1f)
        {
            int index = set != null ? set.IndexOf(clipName) : -1;
            if (index < 0) return;

            clip = index;
            speed = Mathf.Max(0.05f, playbackSpeed);
            time = set.clips[index].looping ? Random.value * set.clips[index].Length : 0f;
            Apply();
        }

        public void SetMaterial(Material source)
        {
            if (view == null || source == null) return;
            view.sharedMaterial = Twin(source);
        }

        Material Twin(Material source)
        {
            if (Twins.TryGetValue(source, out var ready) && ready != null) return ready;

            // Копия материала-ОСНОВЫ, а не голый материал на шейдере.
            //
            // Иначе настройки туна — цвет тени, граница света, мягкость —
            // существуют только в коде, и поменять вид зомби негде: тот
            // материал, что лежит на префабе, при запечённой анимации
            // не используется вовсе.
            var twin = set.material != null
                ? new Material(set.material)
                : new Material(vatShader);

            twin.name = source.name + "_ЗапечённыйТун";

            // От материала тира берём только то, что различает тиры.
            if (source.HasProperty("_BaseMap")) twin.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
            if (source.HasProperty("_BaseColor")) twin.SetColor("_BaseColor", source.GetColor("_BaseColor"));

            twin.SetTexture("_PosTex", set.positions);
            twin.SetTexture("_NrmTex", set.normals);
            twin.enableInstancing = true;

            Twins[source] = twin;
            return twin;
        }

        /// <summary>
        /// Выключается стендом, чтобы отделить цену обновления кадров
        /// от цены логики зомби. Обновление идёт каждому зомби каждый кадр
        /// независимо от видимости, и на нескольких сотнях это заметно.
        /// </summary>
        public static bool UpdateFrames = true;

        void LateUpdate()
        {
            if (clip < 0 || set == null || !UpdateFrames) return;
            time += Time.deltaTime * speed;
            Apply();
        }

        /// <summary>
        /// Считает пару соседних строк и долю перехода между ними.
        ///
        /// Печём тридцать кадров в секунду, показываем шестьдесят, поэтому
        /// между строками смешиваем — иначе движение рубленое. У зациклённого
        /// клипа последняя строка соседствует с нулевой, у одноразового
        /// упирается в себя же: поза смерти должна остаться, а не перескочить
        /// в начало.
        /// </summary>
        void Apply()
        {
            var info = set.clips[clip];
            float frame = time * info.fps;

            int a, b;
            if (info.looping)
            {
                frame = Mathf.Repeat(frame, info.frames);
                a = Mathf.FloorToInt(frame);
                b = (a + 1) % info.frames;
            }
            else
            {
                frame = Mathf.Min(frame, info.frames - 1);
                a = Mathf.FloorToInt(frame);
                b = Mathf.Min(a + 1, info.frames - 1);
            }

            float blend = frame - a;

            block.SetVector(RowsId, new Vector4(info.startRow + a, info.startRow + b, blend, 0f));
            view.SetPropertyBlock(block);
        }
    }
}
