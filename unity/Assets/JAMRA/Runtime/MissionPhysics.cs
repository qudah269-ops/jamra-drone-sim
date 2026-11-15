// ════════════════════════════════════════════════════════════
//  جَمرة — فيزياء المهمّة
//  منقولة حرفياً عن mission.py: نفس المعادلات ونفس الثوابت.
//  لو اختلف رقم هنا عن هناك لكان أحد العرضين يكذب.
// ════════════════════════════════════════════════════════════

using UnityEngine;

namespace Jamra
{
    public static class MissionPhysics
    {
        // ── ثوابت فيزيائية ──────────────────────────────────
        public const float G        = 9.80665f;
        public const float RhoAir   = 1.14f;   // عند ~700 م ارتفاع

        public const float BallMass = 1.20f;
        public const float BallDia  = 0.125f;
        public const float BallCd   = 0.47f;   // كرة
        public const float AgentR   = 2.5f;    // نصف قطر انتشار المسحوق
        public const float PackEff  = 0.65f;   // الدوائر لا ترصف المستطيل

        public const float FuelLoad = 8.0f;    // t/ha لغابة بلوط متوسّطية
        public const float D0       = 1.5f;    // قطر النار لحظة الاشتعال
        public const float AglDrop  = 28.0f;   // ارتفاع الإسقاط فوق المظلّة

        // ── الكاميرا ────────────────────────────────────────
        public const int   CamW        = 1920;
        public const float CamHfovDeg  = 66.0f;
        // التقسيم 3×3 لا يزيد بكسلات الحسّاس؛ يمنع التصغير 1920→640
        // فينزل الحدّ الأدنى المقروء من ~24 بكسل إلى ~8.
        public const int   MinPxFlame  = 8;
        public const int   MinPxSmoke  = 10;
        // ⚠ CALIBRATE — معاملا الواقع (ضباب، ضغط الفيديو، نقص
        // استدعاء المودل) تقديران لم يُقاسا ميدانياً بعد.
        public const float RealFlame   = 0.55f;
        public const float RealSmoke   = 0.30f;
        public const float PlumeRatio  = 8.0f; // ارتفاع العمود ÷ قطر النار

        /// <summary>
        /// سرعة انتشار رأس النار — McArthur Mk5:
        /// R(km/h) = 0.0012 · FFDI · حِمل الوقود
        /// هذه المعادلة هي التي تربط الطقس بالوقت المتاح.
        /// </summary>
        public static float RosMetresPerMin(float ffdi, float fuel = FuelLoad)
        {
            return 0.0012f * Mathf.Max(0f, ffdi) * fuel * 1000f / 60f;
        }

        /// <summary>
        /// القطر المكافئ لدائرة مساحتها كمساحة البيضوي الحقيقي.
        /// الرأس ينتشر بـROS والمؤخّرة بعُشره، فنمو القطر ≈ 1.1 × ROS.
        /// </summary>
        public static float FireDiameter(float minutes, float ffdi)
        {
            return D0 + 1.1f * RosMetresPerMin(ffdi) * Mathf.Max(0f, minutes);
        }

        /// <summary>
        /// أبعد مسافة يُقرأ عندها جسم بعرض sizeM.
        /// px = (size / R) / HFOV · W  ⟹  R = size · W / (px · HFOV)
        /// </summary>
        public static float DetectRange(float sizeM, int minPx, float realism)
        {
            return sizeM * CamW / (minPx * Mathf.Deg2Rad * CamHfovDeg) * realism;
        }

        public static float SmokeDetectRange(float fireDia)
        {
            return DetectRange(fireDia * PlumeRatio, MinPxSmoke, RealSmoke);
        }

        public static float FlameDetectRange(float fireDia)
        {
            return DetectRange(fireDia, MinPxFlame, RealFlame);
        }

        /// <summary>
        /// سقوط الكرة بتكامل عددي مع السحب الهوائي والرياح.
        /// لا نستعمل t=√(2h/g): السرعة الحدّية لهذه الكرة ≈ 60 م/ث،
        /// والسقوط من 28 م يبلغ ثلثها — فالسحب يغيّر الزمن ونقطة
        /// الارتطام معاً، والانحراف هو ما يحدّد الدقّة.
        /// </summary>
        public static void BallFall(float hM, float windMs,
                                    out float timeS, out float impactMs,
                                    out float driftM)
        {
            float area = Mathf.PI * (BallDia * 0.5f) * (BallDia * 0.5f);
            float k = 0.5f * RhoAir * BallCd * area / BallMass;

            float x = 0f, z = hM, vx = 0f, vz = 0f, t = 0f;
            const float dt = 0.002f;
            while (z > 0f && t < 30f)
            {
                float rx = vx - windMs, rz = vz;
                float v = Mathf.Sqrt(rx * rx + rz * rz);
                if (v < 1e-9f) v = 1e-9f;
                vx += -k * v * rx * dt;
                vz += (-G - k * v * rz) * dt;
                x += vx * dt;
                z += vz * dt;
                t += dt;
            }
            timeS = t;
            impactMs = Mathf.Abs(vz);
            driftM = x;
        }

        /// <summary>كم كرة تلزم لتغطية قرص النار، بكفاءة رصف واقعية.</summary>
        public static int BallsNeeded(float diameterM)
        {
            float fireArea = Mathf.PI * (diameterM * 0.5f) * (diameterM * 0.5f);
            float perBall = Mathf.PI * AgentR * AgentR * PackEff;
            return Mathf.Max(1, Mathf.CeilToInt(fireArea / perBall));
        }

        /// <summary>أكبر حريق تستطيع حمولة واحدة إخماده — سقف المهمّة.</summary>
        public static float MaxDiameterFor(int nBalls)
        {
            float area = nBalls * Mathf.PI * AgentR * AgentR * PackEff;
            return 2f * Mathf.Sqrt(area / Mathf.PI);
        }

        /// <summary>
        /// النافذة بالدقائق من الاشتعال حتى يتجاوز الحريق الحمولة.
        /// هذا هو الرقم الذي يحكم التصميم: ليس «هل نكشف؟» بل
        /// «هل نصل قبل أن يكبر؟» — ويضيق كلّما ارتفع الخطر.
        /// </summary>
        public static float WindowMinutes(float ffdi, int nBalls)
        {
            float ros = RosMetresPerMin(ffdi);
            if (ros <= 0f) return float.PositiveInfinity;
            return (MaxDiameterFor(nBalls) - D0) / (1.1f * ros);
        }

        /// <summary>
        /// نصف قطر الضربة: أبعد نقطة يبلغها الدرون ويُطفئ قبل أن
        /// تتجاوز النارُ حمولته. مدى الكشف ~1.5 كم لكن مدى الضربة
        /// أقصر بكثير — القيد ليس الكاميرا بل السرعة.
        /// </summary>
        public static float StrikeRadiusM(float ffdi, int nBalls,
                                          float speedMs, float detectMin = 0.6f)
        {
            float win = WindowMinutes(ffdi, nBalls);
            float fly = Mathf.Max(0f, win - detectMin) * 60f - (95f - AglDrop) / 3f;
            return Mathf.Max(0f, fly * speedMs);
        }
    }
}
