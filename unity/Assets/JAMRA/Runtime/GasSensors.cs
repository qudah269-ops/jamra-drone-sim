// ════════════════════════════════════════════════════════════
//  جَمرة — حساسات الغاز على الدرون (محاكاة لقراءات حساسات حقيقية)
//  الحساسات المختارة موجودة في السوق وتُركَّب على درونات رصد الهواء:
//    CO   : Alphasense CO-B4      (كهروكيميائي، ppm)
//    CO₂  : Sensirion SCD41       (NDIR ضوئي، ppm)
//    PM2.5: Sensirion SPS30       (جسيمات دقيقة، µg/m³)
//    VOC  : Sensirion SGP41       (مؤشّر مركّبات عضوية متطايرة 0–500)
//    NO₂  : Alphasense NO2-B43F   (كهروكيميائي، ppb)
//    O₃   : Alphasense OX-B431    (كهروكيميائي، ppb)
//  القراءات مولَّدة من «بصمة غاز» لكل سبب حريق، تضعف مع البعد عن البؤرة.
//  الاستنتاج قواعد مبسّطة من كيمياء الاحتراق:
//    كفاءة الاحتراق MCE = ΔCO₂ / (ΔCO₂ + ΔCO): لهب ≈ 0.95+، اشتعال خامد ≈ 0.80–0.87
//    VOC مرتفع جداً     ← مُسرِّع اشتعال (بنزين/سولار)
//    NO₂ و O₃ مرتفعان  ← قوس كهربائي (شرارة خط كهرباء)
//    اشتعال خامد        ← عقب سيجارة
//    جسيمات كثيفة جداً  ← حرق مخلّفات زراعية
//    غير ذلك            ← شواء أو نار تخييم (فحم)
// ════════════════════════════════════════════════════════════

using UnityEngine;

namespace Jamra
{
    [System.Serializable]
    public class GasReading
    {
        public float co, co2, pm25, voc, no2, o3;     // القيم المطلقة كما يقرؤها الحساس
    }

    public static class GasSensors
    {
        // الخلفية في هواء غابة نظيف
        public static readonly GasReading Background = new GasReading
            { co = 0.2f, co2 = 420f, pm25 = 12f, voc = 100f, no2 = 8f, o3 = 35f };

        // الزيادة فوق الخلفية عند تعرّض كامل للدخان، لكل سبب (بنفس ترتيب JamraGame.Causes)
        static readonly float[,] Excess = {
            //  CO    CO₂    PM2.5  VOC   NO₂   O₃
            {  90f,  900f,   900f, 150f,  15f,   0f },   // شواء / نار تخييم (فحم)
            { 110f,  500f,   600f,  80f,   5f,   0f },   // عقب سيجارة (اشتعال خامد)
            { 120f, 1400f,  1800f, 120f,  25f,   0f },   // مخلّفات زراعية
            {  40f, 1000f,   400f,  60f, 120f,  80f },   // شرارة خط كهرباء
            {  50f, 1300f,   500f, 380f,  20f,   5f },   // مُسرِّع اشتعال
        };

        public static readonly string[] Models = {
            "CO — Alphasense CO-B4", "CO₂ — Sensirion SCD41", "PM2.5 — Sensirion SPS30",
            "VOC — Sensirion SGP41", "NO₂ — Alphasense NO2-B43F", "O₃ — Alphasense OX-B431" };

        /// <summary>قراءة لحظية: exposure بين 0 و1 حسب البعد عن عمود الدخان.</summary>
        public static GasReading Read(int cause, float exposure, System.Random rng)
        {
            float n() { return 1f + ((float)rng.NextDouble() * 2f - 1f) * 0.08f; }   // ضجيج ±8٪
            var b = Background;
            return new GasReading
            {
                co   = b.co   + Excess[cause, 0] * exposure * n(),
                co2  = b.co2  + Excess[cause, 1] * exposure * n(),
                pm25 = b.pm25 + Excess[cause, 2] * exposure * n(),
                voc  = Mathf.Min(500f, b.voc + Excess[cause, 3] * exposure * n()),
                no2  = b.no2  + Excess[cause, 4] * exposure * n(),
                o3   = b.o3   + Excess[cause, 5] * exposure * n(),
            };
        }

        /// <summary>
        /// استنتاج السبب من متوسّط الزيادات فوق الخلفية. يعيد رقم السبب
        /// وشرحاً بالأرقام لماذا اختير.
        /// </summary>
        public static int Infer(GasReading avg, out string why, out float mce)
        {
            var b = Background;
            float dCO = Mathf.Max(0.01f, avg.co - b.co), dCO2 = Mathf.Max(0.01f, avg.co2 - b.co2);
            float dPM = avg.pm25 - b.pm25, dVOC = avg.voc - b.voc, dNO2 = avg.no2 - b.no2, dO3 = avg.o3 - b.o3;
            mce = dCO2 / (dCO2 + dCO);
            // نسب مستقلّة عن شدّة التعرّض: كل زيادة مقسومة على ΔCO₂
            float rVOC = dVOC / dCO2 * 1000f, rNO2 = dNO2 / dCO2 * 1000f, rPM = dPM / dCO2;
            string m = string.Format("MCE={0:0.00}", mce);
            if (rVOC > 220f)
            { why = m + string.Format("، VOC مرتفع جداً ({0:0} لكل 1000 ppm CO₂) — بصمة وقود سائل", rVOC); return 4; }
            if (rNO2 > 70f || dO3 > 20f)
            { why = m + string.Format("، NO₂ و O₃ مرتفعان (NO₂ {0:0} ppb) — بصمة قوس كهربائي", dNO2); return 3; }
            if (mce < 0.87f)
            { why = m + " — احتراق خامد بلا لهب، يبدأ عادة من عقب سيجارة"; return 1; }
            if (rPM > 1.15f)
            { why = m + string.Format("، جسيمات كثيفة جداً (PM2.5 {0:0} µg/m³) — حرق مخلّفات زراعية", avg.pm25); return 2; }
            why = m + " — احتراق مختلط مع CO مرتفع وبلا مسرِّع — بصمة فحم شواء أو نار تخييم";
            return 0;
        }
    }
}
