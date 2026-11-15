// ════════════════════════════════════════════════════════════
//  جَمرة — النار والدخان
//  أنظمة جُسيمات تُبنى بالكود، لا أصولاً جاهزة: المشروع كلّه
//  يجب أن يُولَّد من سكربت واحد كي لا يحتاج ضبطاً يدوياً.
//  الحجم مربوط بقطر النار الذي يحسبه المحرّك، لا بقيمة جمالية.
// ════════════════════════════════════════════════════════════

using UnityEngine;

namespace Jamra
{
    public class FireController : MonoBehaviour
    {
        public ParticleSystem flame;
        public ParticleSystem smoke;
        public ParticleSystem embers;   // شرر يصعد مع الريح
        public Light glow;

        float dia;
        bool  dead;
        float baseGlow;

        void Update()
        {
            // وميض اللهب: الضوء لا يثبت على شدّة واحدة
            if (glow != null && glow.enabled && !dead)
                glow.intensity = baseGlow * (0.7f + 0.6f * Mathf.PerlinNoise(Time.time * 9f, 0.3f));
        }

        /// <summary>
        /// انفجار كرة الإطفاء: وميض، حلقة غاز سريعة تندفع للخارج، ثم سحابة
        /// مسحوق بيضاء أبطأ تغطّي البؤرة وتتلاشى.
        /// </summary>
        public void Puff(Vector3 p)
        {
            if (smoke == null) return;
            Burst(p, 55, 15f, 0.85f, 1.7f, 0.5f, ParticleSystemShapeType.Hemisphere);
            Burst(p, 95, 3.6f, 4.2f, 4.8f, 1.6f, ParticleSystemShapeType.Sphere);
            StartCoroutine(Flash(p));
        }

        void Burst(Vector3 p, int count, float speed, float life, float size, float radius,
                   ParticleSystemShapeType type)
        {
            var ps = Instantiate(smoke, p + Vector3.up * 0.6f, Quaternion.Euler(-90f, 0f, 0f));
            var m = ps.main;
            m.startColor = new Color(0.97f, 0.97f, 0.95f, 0.92f);
            m.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            m.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            m.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size);
            m.maxParticles = count + 10;
            var em = ps.emission;
            em.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = false;
            var vel = ps.velocityOverLifetime;
            vel.enabled = false;
            var sh = ps.shape;
            sh.shapeType = type;
            sh.radius = radius;
            ps.Clear();
            ps.Play();
            ps.Emit(count);
            Destroy(ps.gameObject, life + 1.5f);
        }

        System.Collections.IEnumerator Flash(Vector3 p)
        {
            var go = new GameObject("BurstFlash");
            go.transform.position = p + Vector3.up * 1.5f;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.9f, 0.95f, 1f);
            l.range = 30f;
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                l.intensity = 18f * (1f - t / 0.3f);
                yield return null;
            }
            Destroy(go);
        }

        public void Place(Vector3 p)
        {
            transform.position = p;
            dead = false;
            if (flame != null) { flame.Clear(); flame.Play(); }
            if (smoke != null) { smoke.Clear(); smoke.Play(); }
            if (embers != null) { embers.Clear(); embers.Play(); }
        }

        public void Extinguish()
        {
            dead = true;
            if (flame != null) flame.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (smoke != null) smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (embers != null) embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (glow != null) glow.intensity = 0f;
        }

        /// <summary>
        /// يربط حجم الجُسيمات بقطر النار الفعلي وعمود الدخان
        /// بنسبة 8× — وهي النسبة نفسها المستعملة في حساب مدى الكشف،
        /// فما يراه اللاعب هو ما «تراه» الكاميرا في النموذج.
        /// </summary>
        public void SetDiameter(float d, float windMs)
        {
            dia = d;
            bool on = d > 0.01f && !dead;

            if (flame != null)
            {
                var em = flame.emission;
                em.enabled = on;
                var sh = flame.shape;
                sh.radius = Mathf.Max(0.6f, d * 0.45f);
                var main = flame.main;
                main.startSizeMultiplier = Mathf.Max(1.4f, d * 0.8f);
                main.startSpeedMultiplier = Mathf.Max(2.2f, d * 0.5f);
                em.rateOverTimeMultiplier = Mathf.Clamp(d * 16f, 40f, 420f);
            }

            if (smoke != null)
            {
                var em = smoke.emission;
                em.enabled = on;
                var sh = smoke.shape;
                sh.radius = Mathf.Max(0.3f, d * 0.6f);
                var main = smoke.main;
                // عمود الدخان يُرى من بعيد حتى والنار صغيرة — كما في الواقع
                main.startSizeMultiplier = Mathf.Max(5f, d * 1.5f);
                main.startSpeedMultiplier = Mathf.Max(5f, d * 0.85f);
                // العمود يرتفع 8× القطر: العمر = الارتفاع ÷ السرعة
                main.startLifetimeMultiplier = Mathf.Clamp(
                    d * MissionPhysics.PlumeRatio
                    / Mathf.Max(1.5f, d * 0.85f), 9f, 16f);
                em.rateOverTimeMultiplier = Mathf.Clamp(d * 9f, 30f, 260f);

                var vel = smoke.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(windMs * 0.75f);
            }

            if (embers != null)
            {
                // الشرر: كثافته تتبع القطر، ويصعد ويتحرّك مع الريح
                var em = embers.emission;
                em.enabled = on;
                em.rateOverTimeMultiplier = Mathf.Clamp(d * 6f, 4f, 120f);
                var sh = embers.shape;
                sh.radius = Mathf.Max(0.3f, d * 0.5f);
                var vel = embers.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(windMs * 1.2f);
            }

            if (glow != null)
            {
                glow.enabled = on;
                // توهّج خفيف يلوّن الأرض المحيطة دون أن يصبغ المشهد كلّه
                baseGlow = on ? Mathf.Clamp(d * 0.5f, 2f, 8f) : 0f;
                glow.intensity = baseGlow;
                glow.range = 50f + d * 16f;
            }
        }

        public float Diameter { get { return dia; } }
    }
}
