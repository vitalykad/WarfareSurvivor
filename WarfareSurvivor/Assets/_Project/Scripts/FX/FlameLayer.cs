using System.Collections.Generic;
using UnityEngine;

namespace WarfareSurvivor
{
    /// <summary>
    /// Общий слой огня: ОДИН набор систем частиц на весь отряд.
    ///
    /// Раньше каждый огнемётчик носил свою копию эффекта, а в копии
    /// четыре системы частиц. Шестнадцать огнемётчиков — это шестьдесят
    /// четыре системы, и каждая просит у движка своё: обновление,
    /// отсечение, сборку меша, отдельный вызов отрисовки. Замер показал
    /// сто восемьдесят два вызова и почти семнадцать миллисекунд на кадр
    /// там, где частиц всего тысяча с небольшим.
    ///
    /// Здесь систем ровно столько, сколько их в префабе, — четыре на всех.
    /// Бойцы не носят эффект, а ВЫБРАСЫВАЮТ в общий слой частицы со своей
    /// позицией и своим направлением. Картинка от этого не меняется:
    /// частицы те же, с теми же настройками из того же префаба, меняется
    /// лишь то, кто их держит.
    ///
    /// Тот же приём, что у трасс и звёзд оглушения: одна ноша на всех
    /// вместо ноши на каждого.
    /// </summary>
    public class FlameLayer : MonoBehaviour
    {
        static FlameLayer instance;

        /// <summary>Системы общего слоя и то, что нужно для ручного выброса.</summary>
        class Nozzle
        {
            public ParticleSystem System;
            public float Rate;          // частиц в секунду, как в префабе
            public float ConeAngle;     // разброс из формы, градусов
            public float Radius;        // радиус устья
            public float SpeedMin, SpeedMax;
            public float SizeMin, SizeMax;
            public Vector3 Offset;      // смещение системы внутри префаба
            public int Order;           // порядок в префабе: первая система — само пламя
        }

        readonly List<Nozzle> nozzles = new List<Nozzle>();

        /// <summary>Недовыброшенные доли частиц по каждому бойцу и соплу.</summary>
        readonly Dictionary<object, float[]> debts = new Dictionary<object, float[]>();

        /// <summary>
        /// Готовит слой по префабу эффекта. Префаб остаётся источником всех
        /// настроек: мы забираем из него системы как есть и только глушим
        /// их собственную эмиссию — выбрасывать частицы теперь наше дело.
        /// </summary>
        public static void Configure(GameObject prefab)
        {
            if (instance != null || prefab == null) return;

            var go = Instantiate(prefab);
            go.name = "Огонь (общий слой)";
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;

            instance = go.AddComponent<FlameLayer>();

            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                var em = ps.emission;
                var shape = ps.shape;

                var nozzle = new Nozzle
                {
                    System = ps,
                    Rate = em.rateOverTime.constant,
                    ConeAngle = shape.enabled ? shape.angle : 0f,
                    Radius = shape.enabled ? shape.radius : 0f,
                    SpeedMin = main.startSpeed.mode == ParticleSystemCurveMode.TwoConstants ? main.startSpeed.constantMin : main.startSpeed.constant,
                    SpeedMax = main.startSpeed.mode == ParticleSystemCurveMode.TwoConstants ? main.startSpeed.constantMax : main.startSpeed.constant,
                    SizeMin = main.startSize.mode == ParticleSystemCurveMode.TwoConstants ? main.startSize.constantMin : main.startSize.constant,
                    SizeMax = main.startSize.mode == ParticleSystemCurveMode.TwoConstants ? main.startSize.constantMax : main.startSize.constant,
                    Offset = ps.transform.localPosition,
                    Order = instance.nozzles.Count,
                };

                // Своя эмиссия больше не нужна: частицы приходят от бойцов.
                em.rateOverTime = 0f;
                em.SetBursts(new ParticleSystem.Burst[0]);

                // Потолок общий на всех, а не на одного: шестнадцать струй
                // и каждое горящее тело живут теперь в одной системе. Упереться
                // в потолок значит обглодать струи ради горящих или наоборот —
                // запас берём с большим избытком, память на частицу копеечная.
                main.maxParticles = Mathf.Max(main.maxParticles,
                                              instance.nozzles.Count == 0 ? 2400 : 1200);

                // Симулировать вне экрана незачем: слой один и виден почти
                // всегда, но когда бой уходит за край — пусть отдыхает.
                main.cullingMode = ParticleSystemCullingMode.Automatic;

                instance.nozzles.Add(nozzle);
                ps.Play();
            }
        }

        /// <summary>
        /// Выброс за кадр от одного бойца: столько частиц, сколько выбросил
        /// бы его собственный эффект.
        ///
        /// Доли копятся отдельно по каждому соплу и бойцу — при восьмидесяти
        /// частицах в секунду и шестидесяти кадрах целое число за кадр
        /// всегда ноль, и струи не было бы вовсе.
        /// </summary>
        public static void Emit(object owner, Vector3 origin, Vector3 forward, float heat, float scale, float deltaTime)
        {
            if (instance == null || owner == null || deltaTime <= 0f) return;
            instance.Spit(owner, origin, forward, heat, scale, deltaTime);
        }

        /// <summary>
        /// Язык огня на горящем теле: тот же огонь, что и в струе, только
        /// медленный и вверх.
        ///
        /// Раньше горящие зомби получали клубы собственной рисовки, а струя
        /// шла из ассета. Рядом друг с другом это не читалось: на настоящем
        /// огне самодельные клубы выглядели разноцветными кружочками. Теперь
        /// текстура, цвет по времени жизни и кривая размера у них общие —
        /// горящий выглядит подожжённым той же струёй, что его подожгла.
        ///
        /// Пламя берём из первой системы префаба, угольки — из самой мелкой.
        /// Дым и ленты на теле не нужны: лента — это форма струи, а дым
        /// такого размера накрыл бы зомби целиком.
        /// </summary>
        public static void Burn(Vector3 at, float scale)
        {
            if (instance == null) return;
            instance.Lick(at, scale);
        }

        /// <summary>Есть ли куда выбрасывать: слой поднимается первой струёй.</summary>
        public static bool Ready => instance != null;

        /// <summary>Боец перестал жечь — забываем его долги.</summary>
        public static void Forget(object owner)
        {
            if (instance == null || owner == null) return;
            instance.debts.Remove(owner);
        }

        void Lick(Vector3 at, float scale)
        {
            Nozzle flame = null, ember = null;
            foreach (var n in nozzles)
            {
                if (n.System == null) continue;
                if (n.Order == 0) flame = n;
                if (ember == null || n.SizeMax < ember.SizeMax) ember = n;
            }
            if (flame == null) return;

            // Клуб пламени: мельче струйного втрое — на теле метровый шар
            // накрыл бы самого зомби, — и вверх, а не вперёд.
            var puff = new ParticleSystem.EmitParams
            {
                position = at + Random.insideUnitSphere * (0.22f * scale),
                velocity = new Vector3(Random.Range(-0.4f, 0.4f),
                                       Random.Range(1.3f, 2.2f),
                                       Random.Range(-0.4f, 0.4f)) * scale,
                startSize = Random.Range(flame.SizeMin, flame.SizeMax) * scale * 0.34f,
                applyShapeToPosition = false,
            };
            flame.System.Emit(puff, 1);

            // Угольки летят выше и реже: сплошной поток искр от каждого
            // горящего превратил бы толпу в фейерверк.
            if (ember == null || ember == flame || Random.value > 0.45f) return;

            var spark = new ParticleSystem.EmitParams
            {
                position = at + Random.insideUnitSphere * (0.3f * scale),
                velocity = new Vector3(Random.Range(-0.8f, 0.8f),
                                       Random.Range(2.2f, 3.6f),
                                       Random.Range(-0.8f, 0.8f)) * scale,
                startSize = Random.Range(ember.SizeMin, ember.SizeMax) * scale,
                applyShapeToPosition = false,
            };
            ember.System.Emit(spark, 1);
        }

        void Spit(object owner, Vector3 origin, Vector3 forward, float heat, float scale, float deltaTime)
        {
            if (!debts.TryGetValue(owner, out var debt) || debt.Length != nozzles.Count)
            {
                debt = new float[nozzles.Count];
                debts[owner] = debt;
            }

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();

            var side = Vector3.Cross(Vector3.up, forward);
            var rotation = Quaternion.LookRotation(forward, Vector3.up);

            for (int i = 0; i < nozzles.Count; i++)
            {
                var n = nozzles[i];
                if (n.System == null) continue;

                debt[i] += n.Rate * heat * deltaTime;
                int count = Mathf.FloorToInt(debt[i]);
                if (count <= 0) continue;
                debt[i] -= count;

                // Больше десятка за кадр от одного бойца не нужно никогда:
                // это значит, что кадр провалился, и досыпать в него ещё
                // частиц — верный способ провалить и следующий.
                count = Mathf.Min(count, 10);

                var muzzle = origin + rotation * (n.Offset * scale);

                for (int k = 0; k < count; k++)
                {
                    // Честный конус: отклоняем на случайный угол в случайную
                    // сторону вокруг оси. Два отклонения подряд, по бортам
                    // и по высоте, давали разброс вдвое шире заявленного —
                    // струя от этого расплывалась веером.
                    float tilt = Random.Range(0f, n.ConeAngle);
                    float roll = Random.Range(0f, 360f);
                    var direction = Quaternion.AngleAxis(roll, forward)
                                  * Quaternion.AngleAxis(tilt, side) * forward;

                    var p = new ParticleSystem.EmitParams
                    {
                        position = muzzle + Random.insideUnitSphere * (n.Radius * scale),
                        velocity = direction * (Random.Range(n.SpeedMin, n.SpeedMax) * scale),
                        startSize = Random.Range(n.SizeMin, n.SizeMax) * scale,
                        applyShapeToPosition = false,
                    };
                    n.System.Emit(p, 1);
                }
            }
        }
    }
}
