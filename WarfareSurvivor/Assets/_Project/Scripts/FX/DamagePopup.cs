using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Всплывающая цифра урона. Пул с жёстким потолком стоит с самого начала —
    /// это ровно тот случай, где дорисовывать ограничение в готовую систему
    /// дороже, чем заложить сразу (§8 project.md).
    ///
    /// Сделано на TextMesh, а не на TextMeshPro: TMP тянет за собой импорт
    /// Essentials, а прототипу нужна цифра над трупом, а не типографика.
    /// </summary>
    public class DamagePopup : MonoBehaviour
    {
        static readonly List<DamagePopup> All = new List<DamagePopup>();
        static ArenaConfig config;
        static Transform root;
        static Camera view;
        static int nextVictim;

        TextMesh label;
        float dieTime;
        float bornTime;

        public static void Configure(ArenaConfig cfg, Camera camera)
        {
            config = cfg;
            view = camera;

            if (root == null)
            {
                root = new GameObject("DamagePopups").transform;
                All.Clear();
            }
        }

        public static void Spawn(Vector3 position, float amount)
        {
            if (config == null || root == null) return;

            var popup = Rent();
            if (popup == null) return;

            popup.transform.position = position;
            popup.label.text = Mathf.RoundToInt(amount).ToString();
            popup.label.characterSize = config.popupScale;
            popup.bornTime = Time.time;
            popup.dieTime = Time.time + config.popupLifetime;
            popup.gameObject.SetActive(true);
        }

        static DamagePopup Rent()
        {
            for (int i = 0; i < All.Count; i++)
                if (!All[i].gameObject.activeSelf)
                    return All[i];

            if (All.Count < config.maxDamagePopups)
            {
                var created = Create();
                All.Add(created);
                return created;
            }

            // Потолок выбран: забираем самую старую цифру по кругу.
            // Пропускать новый урон нельзя — пропадёт обратная связь именно
            // в тот момент, когда на экране жарче всего.
            nextVictim = (nextVictim + 1) % All.Count;
            return All[nextVictim];
        }

        static DamagePopup Create()
        {
            var go = new GameObject("DamagePopup");
            go.transform.SetParent(root, false);

            var text = go.AddComponent<TextMesh>();
            text.font = UIFont.Get(config);
            text.fontSize = 64;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            go.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;

            var popup = go.AddComponent<DamagePopup>();
            popup.label = text;
            go.SetActive(false);
            return popup;
        }

        void LateUpdate()
        {
            if (Time.time >= dieTime)
            {
                gameObject.SetActive(false);
                return;
            }

            transform.position += Vector3.up * (config.popupRiseSpeed * Time.deltaTime);
            if (view != null) transform.rotation = view.transform.rotation;

            float life = Mathf.InverseLerp(bornTime, dieTime, Time.time);
            var color = Color.white;
            color.a = 1f - life * life;
            label.color = color;
        }
    }
}
