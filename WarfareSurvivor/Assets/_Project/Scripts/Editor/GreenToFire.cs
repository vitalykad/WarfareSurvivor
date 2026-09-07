using UnityEditor;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Перекрашивает зелёный эффект в огненный, меняя местами красный
    /// и зелёный каналы во всех градиентах частиц.
    ///
    /// Зачем именно перестановка, а не подбор цветов вручную: в паке
    /// FXIFIED каждый эффект лежит в нескольких цветовых вариантах, и
    /// зелёный получен из огненного ровно этой перестановкой. Обратная
    /// операция возвращает исходную палитру автора — белое остаётся белым,
    /// (0.16, 1, 0) становится (1, 0.16, 0), то есть тем самым
    /// оранжево-красным. Подбирая цвета руками, попасть в его палитру
    /// нельзя, а промахи видны сразу: огонь на песке живёт в узком
    /// диапазоне, где он ещё не бурый и уже не жёлтый.
    ///
    /// Огнемёт в паке лежит ТОЛЬКО в зелёном варианте — оранжевого автор
    /// не положил, поэтому его и приходится получать так.
    /// </summary>
    public static class GreenToFire
    {
        [MenuItem("WarfareSurvivor/FX/Перекрасить зелёный эффект в огонь")]
        public static void Recolor()
        {
            var target = Selection.activeGameObject;
            if (target == null)
            {
                Debug.LogError("[FX] Выдели префаб эффекта в проекте или объект в сцене.");
                return;
            }

            int touched = Repaint(target);
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FX] Перекрашено градиентов: {touched} в «{target.name}».");
        }

        /// <summary>Меняет R и G во всех градиентах и цветах систем частиц.</summary>
        public static int Repaint(GameObject root)
        {
            int touched = 0;

            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                var start = main.startColor;
                if (Swap(ref start)) { main.startColor = start; touched++; }

                var over = ps.colorOverLifetime;
                if (over.enabled) { var c = over.color; if (Swap(ref c)) { over.color = c; touched++; } }

                var speed = ps.colorBySpeed;
                if (speed.enabled) { var c = speed.color; if (Swap(ref c)) { speed.color = c; touched++; } }

                var trails = ps.trails;
                if (trails.enabled) { var c = trails.colorOverLifetime; if (Swap(ref c)) { trails.colorOverLifetime = c; touched++; } }
            }

            return touched;
        }

        static bool Swap(ref ParticleSystem.MinMaxGradient value)
        {
            switch (value.mode)
            {
                case ParticleSystemGradientMode.Color:
                    value.color = Swap(value.color);
                    return true;

                case ParticleSystemGradientMode.TwoColors:
                    value.colorMin = Swap(value.colorMin);
                    value.colorMax = Swap(value.colorMax);
                    return true;

                case ParticleSystemGradientMode.Gradient:
                    value.gradient = Swap(value.gradient);
                    return true;

                case ParticleSystemGradientMode.TwoGradients:
                    value.gradientMin = Swap(value.gradientMin);
                    value.gradientMax = Swap(value.gradientMax);
                    return true;
            }
            return false;
        }

        static Gradient Swap(Gradient source)
        {
            if (source == null) return null;

            var keys = source.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color = Swap(keys[i].color);

            // Альфу не трогаем: она задаёт вспышку и угасание, а не цвет.
            var result = new Gradient { mode = source.mode };
            result.SetKeys(keys, source.alphaKeys);
            return result;
        }

        static Color Swap(Color c) => new Color(c.g, c.r, c.b, c.a);
    }
}
