// ════════════════════════════════════════════════════════════
//  جَمرة — بيانات تضاريس برقش
//  شبكة SRTM 30 م حقيقية + أوزان المظلّة من حدود OpenStreetMap.
//  نحمّل الأوزان لا مواقع الأشجار: المواقع مئات الألوف من الأرقام،
//  أما الأوزان فبايت واحد لكل خليّة، وتُنثَر منها الأشجار بالقاعدة
//  نفسها المستعملة في بلندر — فلا تتفرّع غابتان تختلفان بصمت.
// ════════════════════════════════════════════════════════════

using System;
using UnityEngine;

namespace Jamra
{
    [Serializable]
    public class BargashJson
    {
        public int nr;
        public int nc;
        public float[] span_m;      // [امتداد الصفوف, امتداد الأعمدة] بالمتر
        public double[] bbox;       // [lat0, lon0, lat1, lon1]
        public float z_min;
        public float z_max;
        public float cell_m;
        public int[] heights;       // مكمَّمة 0..65535
        public int[] canopy;        // 0..255
        public string source;
    }

    public class BargashData
    {
        public int Nr, Nc;
        public float SpanX, SpanY;      // الصفوف على x، الأعمدة على y
        public float ZMin, ZMax, CellM;
        public double[] Bbox;
        public float[] Height;          // بالمتر فوق ZMin
        public byte[] Canopy;
        public string Source;

        public float StepX { get { return SpanX / (Nr - 1); } }
        public float StepY { get { return SpanY / (Nc - 1); } }

        public static BargashData Load(TextAsset asset)
        {
            if (asset == null)
            {
                Debug.LogError("JAMRA: ملف التضاريس مفقود");
                return null;
            }
            BargashJson j = JsonUtility.FromJson<BargashJson>(asset.text);
            var d = new BargashData();
            d.Nr = j.nr;
            d.Nc = j.nc;
            d.SpanX = j.span_m[0];
            d.SpanY = j.span_m[1];
            d.ZMin = j.z_min;
            d.ZMax = j.z_max;
            d.CellM = j.cell_m;
            d.Bbox = j.bbox;
            d.Source = j.source;

            float range = j.z_max - j.z_min;
            d.Height = new float[j.heights.Length];
            for (int i = 0; i < j.heights.Length; i++)
                d.Height[i] = j.heights[i] / 65535f * range;

            d.Canopy = new byte[j.canopy.Length];
            for (int i = 0; i < j.canopy.Length; i++)
                d.Canopy[i] = (byte)j.canopy[i];
            return d;
        }

        public float HeightAt(int r, int c)
        {
            r = Mathf.Clamp(r, 0, Nr - 1);
            c = Mathf.Clamp(c, 0, Nc - 1);
            return Height[r * Nc + c];
        }

        /// <summary>
        /// الارتفاع عند إحداثيات العالم، باستكمال ثنائي الخطّية.
        /// يونتي y هو الأعلى: نضع صفوف الشبكة على x وأعمدتها على z.
        /// </summary>
        public float GroundAt(float x, float z)
        {
            float fr = x / StepX + (Nr - 1) * 0.5f;
            float fc = z / StepY + (Nc - 1) * 0.5f;
            int i = Mathf.Clamp(Mathf.FloorToInt(fr), 0, Nr - 2);
            int k = Mathf.Clamp(Mathf.FloorToInt(fc), 0, Nc - 2);
            float u = Mathf.Clamp01(fr - i), v = Mathf.Clamp01(fc - k);
            return HeightAt(i, k)     * (1 - u) * (1 - v)
                 + HeightAt(i + 1, k) * u       * (1 - v)
                 + HeightAt(i, k + 1) * (1 - u) * v
                 + HeightAt(i + 1, k + 1) * u   * v;
        }

        public float CanopyAt(int r, int c)
        {
            r = Mathf.Clamp(r, 0, Nr - 1);
            c = Mathf.Clamp(c, 0, Nc - 1);
            return Canopy[r * Nc + c] / 255f;
        }

        /// <summary>مركز المشهد بإحداثيات العالم.</summary>
        public Vector3 Centre()
        {
            return new Vector3(0f, GroundAt(0f, 0f), 0f);
        }

        /// <summary>
        /// نقطة عشوائية داخل مربّع نصف ضلعه halfM حول المركز،
        /// مرجَّحة بكثافة المظلّة: الحرائق تبدأ حيث يوجد وقود.
        /// </summary>
        public Vector3 RandomForestPoint(System.Random rng, float halfM)
        {
            for (int tries = 0; tries < 400; tries++)
            {
                float x = (float)(rng.NextDouble() * 2 - 1) * halfM;
                float z = (float)(rng.NextDouble() * 2 - 1) * halfM;
                int r = Mathf.RoundToInt(x / StepX + (Nr - 1) * 0.5f);
                int c = Mathf.RoundToInt(z / StepY + (Nc - 1) * 0.5f);
                if (rng.NextDouble() <= CanopyAt(r, c))
                    return new Vector3(x, GroundAt(x, z), z);
            }
            return new Vector3(0f, GroundAt(0f, 0f), 0f);
        }
    }
}
