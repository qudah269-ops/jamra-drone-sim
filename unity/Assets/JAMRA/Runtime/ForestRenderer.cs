// ════════════════════════════════════════════════════════════
//  جَمرة — غابة برقش
//  الأشجار تُرسم بالـ GPU instancing مباشرة لا عبر أشجار التيرين:
//  لوحات (billboards) التيرين تظهر سوداء تحت URP، وحدّها عشرة آلاف
//  شجرة كاملة لا يكفي لغابة مغلقة المظلّة كغابة برقش الحقيقية.
//  المواقع تُولَّد من قناع الغابة المأخوذ من صورة Sentinel-2.
// ════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Jamra
{
    public class ForestRenderer : MonoBehaviour
    {
        public Texture2D mask;          // R = وزن الغابة؛ u على x و v على z
        public Terrain terrain;
        public Material[] leafMats;
        public Material trunkMat;
        public int treeCount = 220000;
        public int seed = 3;
        public float drawDistance = 2600f;
        public float nearDistance = 420f;
        public float trunkDistance = 230f;

        public static ForestRenderer Instance;

        const int G = 48;               // خلايا الشبكة على كل محور
        const int Batch = 1023;         // حدّ DrawMeshInstanced

        Bounds[] cellBounds;
        Matrix4x4[][][] cellMats;       // [خليّة][متغيّر][شجرة]
        Vector3[] treePos;
        Mesh[] crownNear;
        Mesh crownFar, trunk;
        int variants;

        Matrix4x4[][] nearBuf, farBuf;
        int[] nearN, farN;
        Matrix4x4[] trunkBuf;
        int trunkN;
        Plane[] planes = new Plane[6];

        // في المتصفّح كل شيء على خيط واحد: نسخ مصفوفات 220 ألف شجرة كل إطار
        // مع ظلالها نزل بالإطارات إلى ~1.5/ث على Intel UHD. غابة أخفّ للويب فقط.
        ShadowCastingMode nearShadows = ShadowCastingMode.On;

        void Awake()
        {
            Instance = this;
#if UNITY_WEBGL && !UNITY_EDITOR
            treeCount = Mathf.Min(treeCount, Mathf.Max(5000, WebTuning.Trees));
            drawDistance = Mathf.Min(drawDistance, WebTuning.Far);
            nearDistance = Mathf.Min(nearDistance, 260f);
            trunkDistance = Mathf.Min(trunkDistance, 150f);
            nearShadows = ShadowCastingMode.Off;
#endif
            Generate();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>شجرة عشوائية داخل مربّع نصف ضلعه halfM حول المركز.</summary>
        public bool RandomTree(System.Random rng, float halfM, out Vector3 p)
        {
            p = Vector3.zero;
            if (treePos == null || treePos.Length == 0) return false;
            for (int i = 0; i < 6000; i++)
            {
                Vector3 t = treePos[rng.Next(treePos.Length)];
                if (Mathf.Abs(t.x) > halfM || Mathf.Abs(t.z) > halfM) continue;
                p = t;
                return true;
            }
            return false;
        }

        void Generate()
        {
            if (terrain == null || mask == null || leafMats == null || leafMats.Length == 0)
                return;
            variants = leafMats.Length;
            BuildMeshes();

            Vector3 org = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            var rng = new System.Random(seed);

            var lists = new List<Matrix4x4>[G * G * variants];
            var pts = new List<Vector3>(treeCount);
            int guard = 0;
            while (pts.Count < treeCount && guard++ < treeCount * 40)
            {
                float u = (float)rng.NextDouble();
                float v = (float)rng.NextDouble();
                float w = mask.GetPixelBilinear(u, v).r;
                if (rng.NextDouble() > w) continue;

                float wx = org.x + u * size.x, wz = org.z + v * size.z;
                float wy = org.y + terrain.SampleHeight(new Vector3(wx, 0f, wz));
                var p = new Vector3(wx, wy, wz);
                pts.Add(p);

                float s = 0.75f + 0.55f * (float)rng.NextDouble();
                float sy = s * (0.85f + 0.3f * (float)rng.NextDouble());
                var m = Matrix4x4.TRS(p,
                    Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                    new Vector3(s, sy, s));

                int cx = Mathf.Min(G - 1, (int)(u * G));
                int cz = Mathf.Min(G - 1, (int)(v * G));
                int k = ((cz * G + cx) * variants) + rng.Next(variants);
                if (lists[k] == null) lists[k] = new List<Matrix4x4>(64);
                lists[k].Add(m);
            }
            treePos = pts.ToArray();

            cellBounds = new Bounds[G * G];
            cellMats = new Matrix4x4[G * G][][];
            float cw = size.x / G, cd = size.z / G;
            for (int cz = 0; cz < G; cz++)
                for (int cx = 0; cx < G; cx++)
                {
                    int c = cz * G + cx;
                    var centre = new Vector3(org.x + (cx + 0.5f) * cw,
                                             org.y + size.y * 0.5f,
                                             org.z + (cz + 0.5f) * cd);
                    cellBounds[c] = new Bounds(centre, new Vector3(cw + 16f, size.y + 40f, cd + 16f));
                    cellMats[c] = new Matrix4x4[variants][];
                    for (int v = 0; v < variants; v++)
                    {
                        var l = lists[c * variants + v];
                        cellMats[c][v] = l != null ? l.ToArray() : null;
                    }
                }

            nearBuf = new Matrix4x4[variants][];
            farBuf = new Matrix4x4[variants][];
            nearN = new int[variants];
            farN = new int[variants];
            for (int v = 0; v < variants; v++)
            {
                nearBuf[v] = new Matrix4x4[Batch];
                farBuf[v] = new Matrix4x4[Batch];
            }
            trunkBuf = new Matrix4x4[Batch];
            Debug.Log("JAMRA: غابة برقش — " + treePos.Length + " شجرة بلوط من قناع Sentinel-2");
        }

        void LateUpdate()
        {
            if (cellMats == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 cp = cam.transform.position;
            float far2 = drawDistance * drawDistance;
            float near2 = nearDistance * nearDistance;
            float trunk2 = trunkDistance * trunkDistance;

            for (int c = 0; c < cellMats.Length; c++)
            {
                float d2 = cellBounds[c].SqrDistance(cp);
                if (d2 > far2) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, cellBounds[c])) continue;
                bool near = d2 < near2;
                bool trunks = d2 < trunk2;
                for (int v = 0; v < variants; v++)
                {
                    var arr = cellMats[c][v];
                    if (arr == null) continue;
                    if (near) Push(arr, nearBuf[v], ref nearN[v], crownNear[v % crownNear.Length],
                                   leafMats[v], nearShadows);
                    else Push(arr, farBuf[v], ref farN[v], crownFar, leafMats[v],
                              ShadowCastingMode.Off);
                    if (trunks && trunkMat != null)
                        Push(arr, trunkBuf, ref trunkN, trunk, trunkMat, ShadowCastingMode.Off);
                }
            }

            for (int v = 0; v < variants; v++)
            {
                Flush(nearBuf[v], ref nearN[v], crownNear[v % crownNear.Length], leafMats[v],
                      nearShadows);
                Flush(farBuf[v], ref farN[v], crownFar, leafMats[v], ShadowCastingMode.Off);
            }
            if (trunkMat != null) Flush(trunkBuf, ref trunkN, trunk, trunkMat, ShadowCastingMode.Off);
        }

        void Push(Matrix4x4[] src, Matrix4x4[] buf, ref int n, Mesh mesh, Material mat,
                  ShadowCastingMode sh)
        {
            int i = 0;
            while (i < src.Length)
            {
                int take = Mathf.Min(src.Length - i, Batch - n);
                System.Array.Copy(src, i, buf, n, take);
                n += take;
                i += take;
                if (n == Batch) Flush(buf, ref n, mesh, mat, sh);
            }
        }

        void Flush(Matrix4x4[] buf, ref int n, Mesh mesh, Material mat, ShadowCastingMode sh)
        {
            if (n > 0)
                Graphics.DrawMeshInstanced(mesh, 0, mat, buf, n, null, sh, true, gameObject.layer);
            n = 0;
        }

        // ── الهندسة: بلوط برقش قصير عريض مستدير التاج ────────────
        void BuildMeshes()
        {
            var rng = new System.Random(101);
            crownNear = new Mesh[3];
            for (int k = 0; k < crownNear.Length; k++)
            {
                var vs = new List<Vector3>();
                var ts = new List<int>();
                int blobs = 4 + (k % 2);
                for (int b = 0; b < blobs; b++)
                {
                    float a = 2f * Mathf.PI * b / blobs + Rnd(rng, -0.5f, 0.5f);
                    float off = Rnd(rng, 0.6f, 2.0f);
                    float r = Rnd(rng, 2.1f, 3.1f);
                    AddBlob(vs, ts, rng,
                        new Vector3(Mathf.Cos(a) * off, Rnd(rng, 3.3f, 4.6f), Mathf.Sin(a) * off),
                        new Vector3(r * Rnd(rng, 0.9f, 1.2f), r * Rnd(rng, 0.62f, 0.8f),
                                    r * Rnd(rng, 0.9f, 1.2f)), 0.16f);
                }
                crownNear[k] = MakeMesh("OakCrown_" + k, vs, ts);
            }
            {
                var vs = new List<Vector3>();
                var ts = new List<int>();
                AddBlob(vs, ts, rng, new Vector3(0f, 3.9f, 0f), new Vector3(4.2f, 2.6f, 4.2f), 0.12f);
                crownFar = MakeMesh("OakCrownFar", vs, ts);
            }
            {
                var vs = new List<Vector3>();
                var ts = new List<int>();
                const int S = 6;
                for (int i = 0; i < S; i++)
                {
                    float a = 2f * Mathf.PI * i / S;
                    vs.Add(new Vector3(Mathf.Cos(a) * 0.32f, 0f, Mathf.Sin(a) * 0.32f));
                    vs.Add(new Vector3(Mathf.Cos(a) * 0.2f, 3.6f, Mathf.Sin(a) * 0.2f));
                }
                for (int i = 0; i < S; i++)
                {
                    int a0 = i * 2, a1 = i * 2 + 1, b0 = ((i + 1) % S) * 2, b1 = ((i + 1) % S) * 2 + 1;
                    ts.Add(a0); ts.Add(a1); ts.Add(b0);
                    ts.Add(b0); ts.Add(a1); ts.Add(b1);
                }
                trunk = MakeMesh("OakTrunk", vs, ts);
            }
        }

        static float Rnd(System.Random rng, float a, float b)
        {
            return a + (float)rng.NextDouble() * (b - a);
        }

        static Mesh MakeMesh(string name, List<Vector3> vs, List<int> ts)
        {
            var m = new Mesh();
            m.name = name;
            m.SetVertices(vs);
            m.SetTriangles(ts, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // كرة عشرينية مقسَّمة مرّة واحدة (42 رأساً) بتشويه خفيف
        static void AddBlob(List<Vector3> vs, List<int> ts, System.Random rng,
                            Vector3 centre, Vector3 radii, float jitter)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var p = new List<Vector3> {
                new Vector3(-1,  t, 0), new Vector3(1,  t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1,  t), new Vector3(0, 1,  t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3( t, 0, -1), new Vector3( t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
            int[] f = {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            for (int i = 0; i < p.Count; i++) p[i] = p[i].normalized;

            var mid = new Dictionary<long, int>();
            var tri = new List<int>(240);
            for (int i = 0; i < f.Length; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(p, mid, a, b), bc = Mid(p, mid, b, c), ca = Mid(p, mid, c, a);
                tri.Add(a); tri.Add(ab); tri.Add(ca);
                tri.Add(b); tri.Add(bc); tri.Add(ab);
                tri.Add(c); tri.Add(ca); tri.Add(bc);
                tri.Add(ab); tri.Add(bc); tri.Add(ca);
            }

            int baseIndex = vs.Count;
            for (int i = 0; i < p.Count; i++)
            {
                float k = 1f + Rnd(rng, -jitter, jitter);
                vs.Add(centre + new Vector3(p[i].x * radii.x * k, p[i].y * radii.y * k,
                                            p[i].z * radii.z * k));
            }
            for (int i = 0; i < tri.Count; i++) ts.Add(baseIndex + tri[i]);
        }

        static int Mid(List<Vector3> p, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            int idx;
            if (cache.TryGetValue(key, out idx)) return idx;
            p.Add(((p[a] + p[b]) * 0.5f).normalized);
            idx = p.Count - 1;
            cache[key] = idx;
            return idx;
        }
    }
}
