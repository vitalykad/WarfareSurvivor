using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Ударная волна от удара молота: кольцо, разбегающееся по земле, и урон
    /// ровно тем, до кого фронт уже дошёл.
    ///
    /// Урон нанесён НЕ РАЗОМ. Мгновенный круг и расходящаяся волна выглядят
    /// одинаково на бумаге, но читаются противоположно: при мгновенном ударе
    /// вся толпа дёргается в один кадр, и картинка не объясняет, почему
    /// дальние пострадали. Когда же урон идёт вслед за кольцом, видно
    /// причину — вот фронт дошёл, вот зомби улетел, — и радиус удара
    /// становится понятен без единой цифры.
    ///
    /// Каждому достаётся ровно один раз: волна проходит сквозь, а не
    /// перемалывает стоящих у края.
    ///
    /// Кольцо ЛЕЖИТ НА ЗЕМЛЕ. Развёрнутое к камере читается вторым телом
    /// или вспышкой, лежащее — волной, то есть тем, чем и является.
    /// </summary>
    public class ShockWave : MonoBehaviour
    {
        static readonly List<ShockWave> All = new List<ShockWave>();
        static ArenaConfig config;
        static Transform root;
        static Material ringMaterial;

        /// <summary>Материалы секторов по раствору: текстуру на каждый угол лепим один раз.</summary>
        static readonly Dictionary<int, Material> ArcMaterials = new Dictionary<int, Material>();

        static int nextVictim;

        MeshRenderer view;
        Mesh mesh;
        readonly Color[] corners = new Color[4];

        /// <summary>Кого уже задело. Переиспользуется вместе с самим объектом.</summary>
        readonly List<Zombie> struck = new List<Zombie>();

        float bornTime;
        float dieTime;
        float radius;
        float damage;
        float knockDistance;
        float knockDuration;

        Vector3 forward;
        float halfAngle;
        float stunChance;
        float stunSeconds;

        public static void Configure(ArenaConfig cfg)
        {
            config = cfg;

            if (root == null)
            {
                root = new GameObject("ShockWaves").transform;
                All.Clear();
            }
        }

        /// <summary>
        /// Запускает волну из точки удара.
        ///
        /// Раствор в 360 градусов означает круг, меньше — сектор в сторону
        /// удара. Сектор и рисуется, и бьёт по одному и тому же углу: разойдись
        /// они, игрок увидел бы одно, а получил другое.
        /// </summary>
        public static void Burst(Vector3 at, float radius, float damage,
                                 float knockDistance, float knockDuration,
                                 Vector3 forward, float angleDegrees,
                                 float stunChance, float stunSeconds)
        {
            if (config == null || root == null) return;

            var wave = Rent();
            if (wave == null) return;

            wave.radius = Mathf.Max(0.2f, radius);
            wave.damage = damage;
            wave.knockDistance = knockDistance;
            wave.knockDuration = knockDuration;
            wave.stunChance = stunChance;
            wave.stunSeconds = stunSeconds;
            wave.struck.Clear();

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            wave.forward = forward.normalized;

            float angle = Mathf.Clamp(angleDegrees, 20f, 360f);
            wave.halfAngle = angle * 0.5f;

            wave.bornTime = Time.time;
            wave.dieTime = Time.time + Mathf.Max(0.05f, config.shockWaveTime);

            bool full = angle >= 359f;
            wave.view.sharedMaterial = full ? RingMaterial() : ArcMaterial(angle);

            // Чуть над землёй: вровень с ней кольцо тонет в текстуре песка.
            wave.transform.position = new Vector3(at.x, 0.04f, at.z);

            // Квад кладём плашмя и разворачиваем так, чтобы верх текстуры
            // смотрел туда, куда ударили. Для круга разворот безразличен.
            float yaw = Mathf.Atan2(wave.forward.x, wave.forward.z) * Mathf.Rad2Deg;
            wave.transform.rotation = Quaternion.Euler(90f, 0f, -yaw);

            wave.transform.localScale = Vector3.one * 0.2f;
            wave.gameObject.SetActive(true);
        }

        static ShockWave Rent()
        {
            for (int i = 0; i < All.Count; i++)
                if (!All[i].gameObject.activeSelf)
                    return All[i];

            if (All.Count < Mathf.Max(4, config.maxShockWaves))
            {
                var created = Create();
                All.Add(created);
                return created;
            }

            // Волн больше, чем позволено: отнимаем самую старую по кругу.
            nextVictim = (nextVictim + 1) % All.Count;
            return All[nextVictim];
        }

        static ShockWave Create()
        {
            var go = new GameObject("ShockWave");
            go.transform.SetParent(root, false);

            var filter = go.AddComponent<MeshFilter>();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            var wave = go.AddComponent<ShockWave>();
            wave.view = renderer;
            wave.mesh = Quad();
            filter.sharedMesh = wave.mesh;

            go.SetActive(false);
            return wave;
        }

        static Mesh Quad()
        {
            var quad = new Mesh { name = "ShockWaveQuad" };
            quad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f)
            };
            quad.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quad.RecalculateBounds();
            quad.MarkDynamic();
            return quad;
        }

        static Material RingMaterial()
        {
            if (ringMaterial != null) return ringMaterial;

            // НЕ аддитивный: волна это поднятая пыль, а не свет. Аддитивное
            // кольцо на песке выбеливает землю и читается вспышкой.
            var shader = Shader.Find("WarfareSurvivor/GlowSprite");
            ringMaterial = new Material(shader) { name = "ShockWaveRing", mainTexture = Ring(360f) };
            return ringMaterial;
        }

        /// <summary>Материал сектора. Текстура лепится по раствору и переиспользуется.</summary>
        static Material ArcMaterial(float angleDegrees)
        {
            int key = Mathf.RoundToInt(angleDegrees / 5f) * 5;
            if (ArcMaterials.TryGetValue(key, out var ready) && ready != null) return ready;

            var shader = Shader.Find("WarfareSurvivor/GlowSprite");
            var made = new Material(shader)
            {
                name = "ShockWaveArc" + key,
                mainTexture = Ring(key)
            };

            ArcMaterials[key] = made;
            return made;
        }

        /// <summary>
        /// Кольцо или его сектор: пусто в середине, полоса у края.
        ///
        /// Сектор обрезается ПО УГЛУ и смягчается у краёв: резаный край
        /// читается куском картинки, а не фронтом волны.
        /// </summary>
        static Texture2D Ring(float angleDegrees)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "ShockWaveRing", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };

            float width = config != null ? Mathf.Clamp(config.shockWaveWidth, 0.03f, 0.5f) : 0.14f;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);

                    // Снаружи фронт резкий, внутрь спадает мягко — так же,
                    // как выглядит настоящая волна пыли.
                    float outer = 1f - Mathf.Clamp01((r - (1f - width)) / width);
                    float inner = Mathf.Clamp01((r - (1f - width * 2.4f)) / (width * 1.4f));
                    float a = r > 1f ? 0f : Mathf.Clamp01(outer * inner);

                    if (angleDegrees < 359f)
                    {
                        // Ноль градусов смотрит вверх текстуры — туда же,
                        // куда развёрнут квад при ударе.
                        float bearing = Mathf.Abs(Mathf.Atan2(u, v) * Mathf.Rad2Deg);
                        float half = angleDegrees * 0.5f;
                        const float Soft = 12f;
                        a *= Mathf.Clamp01((half - bearing) / Soft);
                    }

                    pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        void LateUpdate()
        {
            if (config == null || Time.time >= dieTime)
            {
                gameObject.SetActive(false);
                return;
            }

            float life = Mathf.InverseLerp(bornTime, dieTime, Time.time);

            // Разбегается С ЗАМЕДЛЕНИЕМ: рывок в первые кадры и торможение
            // к краю. Ровное расширение читается раздувающимся кругом,
            // а не ударом.
            float grow = 1f - (1f - life) * (1f - life);
            float front = radius * grow;

            transform.localScale = Vector3.one * (front * 2f);

            var color = config.shockWaveColor;
            color.a *= 1f - life * life;
            for (int i = 0; i < corners.Length; i++) corners[i] = color;
            mesh.colors = corners;

            Damage(front);
        }

        /// <summary>Бьёт тех, кого фронт накрыл именно сейчас.</summary>
        void Damage(float front)
        {
            float frontSqr = front * front;
            var zombies = Registry.Zombies;

            // С КОНЦА: убитый вычёркивает себя из реестра прямо в TakeHit,
            // и обход с начала на этом ломается — список укорачивается,
            // индексы съезжают, следующий за убитым пропускается.
            for (int i = zombies.Count - 1; i >= 0; i--)
            {
                if (i >= zombies.Count) continue;

                var zombie = zombies[i];
                if (zombie == null || zombie.IsDead || !zombie.isActiveAndEnabled) continue;
                if (struck.Contains(zombie)) continue;

                var to = zombie.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > frontSqr) continue;

                // Сектор бьёт только по своему углу — ровно по тому же,
                // по которому он нарисован.
                if (halfAngle < 179f && to.sqrMagnitude > 0.0001f &&
                    Vector3.Angle(forward, to) > halfAngle) continue;

                struck.Add(zombie);
                zombie.TakeHit(damage);

                if (stunSeconds > 0f && Random.value < stunChance)
                    zombie.Stun(stunSeconds);

                if (knockDistance > 0f)
                {
                    // Толкаем ОТ центра волны. Стоящего ровно в центре —
                    // в произвольную сторону: направление там не определено,
                    // а оставить его на месте значит показать, что удар
                    // сильнее всего мимо того, кто ближе всех.
                    var push = to.sqrMagnitude > 0.0001f
                        ? to.normalized
                        : new Vector3(Random.value - 0.5f, 0f, Random.value - 0.5f).normalized;

                    zombie.Knockback(push, knockDistance, knockDuration);
                }
            }
        }
    }
}
