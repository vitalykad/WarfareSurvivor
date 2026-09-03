using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WarfareSurvivor.EditorTools
{
    /// <summary>
    /// Общие операции над синергиями: где лежат, как найти, как завести.
    /// Нужны и окну библиотеки, и инспектору класса — держим в одном месте,
    /// чтобы папка и правила именования не разъехались между ними.
    /// </summary>
    public static class SynergyLibrary
    {
        public const string Folder = "Assets/_Project/Configs/Synergies";

        public static List<SynergySO> All()
        {
            var list = new List<SynergySO>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(SynergySO)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<SynergySO>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) list.Add(asset);
            }
            list.Sort((a, b) =>
            {
                int byStat = ((int)a.stat).CompareTo((int)b.stat);
                return byStat != 0 ? byStat : ((int)a.kind).CompareTo((int)b.kind);
            });
            return list;
        }

        public static List<SurvivorClassSO> AllClasses()
        {
            var list = new List<SurvivorClassSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(SurvivorClassSO)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<SurvivorClassSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) list.Add(asset);
            }
            return list;
        }

        /// <summary>Кто ссылается на эту синергию. Показывается перед удалением.</summary>
        public static List<SurvivorClassSO> UsedBy(SynergySO synergy)
        {
            var users = new List<SurvivorClassSO>();
            if (synergy == null) return users;

            foreach (var klass in AllClasses())
                if (klass.synergies != null && klass.synergies.Contains(synergy))
                    users.Add(klass);

            return users;
        }

        /// <summary>
        /// Кто чем пользуется — одним проходом по всем классам.
        ///
        /// Существует потому, что UsedBy на каждую синергию по отдельности
        /// перечитывает все классы проекта заново. На тринадцати синергиях
        /// это тринадцать полных обходов, 210 мс, и всё это внутри отрисовки
        /// окна: переключение вкладки занимало две секунды.
        /// </summary>
        public static Dictionary<SynergySO, List<SurvivorClassSO>> UsageMap(List<SynergySO> synergies)
        {
            var map = new Dictionary<SynergySO, List<SurvivorClassSO>>();
            if (synergies == null) return map;

            foreach (var synergy in synergies)
                if (synergy != null) map[synergy] = new List<SurvivorClassSO>();

            foreach (var klass in AllClasses())
            {
                if (klass.synergies == null) continue;

                foreach (var synergy in klass.synergies)
                    if (synergy != null && map.TryGetValue(synergy, out var users) && !users.Contains(klass))
                        users.Add(klass);
            }

            return map;
        }

        public static SynergySO Create(SynergyKind kind, SquadStat stat, SynergyAmount amountType,
                                       float amount, bool scalesWithCount, string note)
        {
            EnsureFolder(Folder);

            var synergy = ScriptableObject.CreateInstance<SynergySO>();
            synergy.kind = kind;
            synergy.stat = stat;
            synergy.amountType = amountType;
            synergy.amountPerUnit = Mathf.Max(0f, amount);
            synergy.scalesWithCount = scalesWithCount;
            synergy.note = note ?? "";

            string path = AssetDatabase.GenerateUniqueAssetPath(
                Folder + "/" + synergy.SuggestedFileName() + ".asset");

            AssetDatabase.CreateAsset(synergy, path);
            AssetDatabase.SaveAssets();
            return synergy;
        }

        /// <summary>
        /// Убирает синергию отовсюду и удаляет ассет.
        ///
        /// Именно в таком порядке: удалить ассет, не почистив ссылки, значит
        /// оставить в списках классов пустые строки, которые потом ищешь
        /// глазами по всем ролям.
        /// </summary>
        public static void Delete(SynergySO synergy)
        {
            if (synergy == null) return;

            foreach (var klass in AllClasses())
            {
                if (klass.synergies == null || !klass.synergies.Contains(synergy)) continue;
                Undo.RecordObject(klass, "Убрать синергию");
                klass.synergies.RemoveAll(s => s == synergy);
                EditorUtility.SetDirty(klass);
            }

            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(synergy));
            AssetDatabase.SaveAssets();
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            int cut = path.LastIndexOf('/');
            string parent = path.Substring(0, cut);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(cut + 1));
        }

        /// <summary>Русские подписи параметров для выпадающих списков.</summary>
        public static string[] StatLabels()
        {
            var values = (SquadStat[])System.Enum.GetValues(typeof(SquadStat));
            var labels = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
                labels[i] = SquadStatInfo.Label(values[i]);
            return labels;
        }

        public static readonly string[] KindLabels = { "+  прибавка", "−  убавка", "◆  другое" };
        public static readonly string[] AmountLabels = { "проценты", "единицы" };
    }
}
