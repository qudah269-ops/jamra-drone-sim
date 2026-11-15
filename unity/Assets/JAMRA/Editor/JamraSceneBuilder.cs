// ════════════════════════════════════════════════════════════
//  جَمرة — بانٍ المشهد بضغطة واحدة
//
//  يبني كل شيء بالكود: التضاريس من SRTM، الغابة من حدود OSM،
//  الدرون، النار، الإضاءة، الكاميرا واللوحة. لا سحب ولا إفلات.
//
//  السبب: ضبط مشهد يونتي بالمحرّر خطوات كثيرة تُنسى واحدة منها
//  فينكسر كل شيء بلا رسالة. سكربت واحد يُعاد تشغيله متى شئت
//  أوثق من قائمة تعليمات.
//
//  القائمة:  JAMRA ▸ Build Bargash Scene
// ════════════════════════════════════════════════════════════

using UnityEditor;
using UnityEngine;
using Jamra;

public static class JamraSceneBuilder
{
    const string DataPath = "Assets/JAMRA/Data/bargash_terrain.json";
    const int    HeightRes = 257;          // يونتي تشترط 2^n + 1
    const int    TreeCount = 220000;
    const float  DroneScale = 4f;          // مقياس الدرون في المشهد (الموديل بالمتر)
    // السماء كما في sun_and_sky(): لون العالم (0.42, 0.56, 0.78) × شدّته 0.55
    static readonly Color SkyColor = new Color(0.62f, 0.76f, 0.90f);

    [MenuItem("JAMRA/Build Bargash Scene")]
    public static void Build()
    {
        var json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
        if (json == null)
        {
            EditorUtility.DisplayDialog("JAMRA",
                "لم أجد " + DataPath + "\nتأكّد أن مجلّد JAMRA كامل.", "حسناً");
            return;
        }

        var data = BargashData.Load(json);
        if (data == null) return;

        // ── تنظيف ما بناه تشغيل سابق ────────────────────────
        var old = GameObject.Find("JAMRA");
        if (old != null) Object.DestroyImmediate(old);
        // أصول التشغيل السابق تبقى على القرص وتُفشل CreateAsset
        foreach (var p in new string[] {
            "Assets/JAMRA/Data/BargashGround.terrainlayer",
            "Assets/JAMRA/Data/GroundTex.asset",
            "Assets/JAMRA/Data/BargashTerrainData.asset",
            "Assets/JAMRA/Data/Oak.prefab",
            "Assets/JAMRA/Data/OakBark.mat",
            "Assets/JAMRA/Data/OakLeaf.mat",
            "Assets/JAMRA/Data/OakMesh.asset",
            "Assets/JAMRA/Data/ParticleAdd.mat",
            "Assets/JAMRA/Data/ParticleAlpha.mat",
            "Assets/JAMRA/Data/ParticleSoft.asset",
            "Assets/JAMRA/Data/BargashForestFloor.terrainlayer",
            "Assets/JAMRA/Data/BargashSoil.terrainlayer",
            "Assets/JAMRA/Data/GroundForest.asset",
            "Assets/JAMRA/Data/GroundSoil.asset",
            "Assets/JAMRA/Data/ForestMask.asset" })
            AssetDatabase.DeleteAsset(p);
        for (int k = 0; k < OakVariants; k++)
        {
            AssetDatabase.DeleteAsset("Assets/JAMRA/Data/Oak_" + k + ".prefab");
            AssetDatabase.DeleteAsset("Assets/JAMRA/Data/OakLeaf_" + k + ".mat");
            AssetDatabase.DeleteAsset("Assets/JAMRA/Data/OakMesh_" + k + ".asset");
        }
        var root = new GameObject("JAMRA");

        EditorUtility.DisplayProgressBar("JAMRA", "بناء التضاريس…", 0.1f);
        Terrain terrain = BuildTerrain(data, root.transform);

        EditorUtility.DisplayProgressBar("JAMRA", "زراعة الغابة…", 0.45f);
        BuildForest(terrain, data, root.transform);

        EditorUtility.DisplayProgressBar("JAMRA", "الدرون والنار…", 0.8f);
        Light sun = BuildSun(root.transform);
        GameObject drone = BuildDrone(root.transform, data);
        FireController fire = BuildFire(root.transform);
        Camera cam = BuildCamera(root.transform);

        // ── التوصيل ─────────────────────────────────────────
        var game = root.AddComponent<JamraGame>();
        game.terrainJson = json;
        game.drone = drone.transform;
        game.fire = fire;

        var pilot = drone.AddComponent<DroneController>();
        pilot.game = game;
        pilot.cam = cam;
        pilot.rotors = CollectRotors(drone.transform);

        var hud = root.AddComponent<MissionHUD>();
        hud.game = game;
        hud.pilot = pilot;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 500f;
        RenderSettings.fogEndDistance = 5000f;
        RenderSettings.fogColor = SkyColor;
        BuildPostFx(root.transform);

        AssetDatabase.SaveAssets();
        EditorUtility.ClearProgressBar();
        Selection.activeGameObject = root;
        Debug.Log("JAMRA: المشهد جاهز — اضغط Play ثم «ابدأ المهمّة». "
                  + "المصدر: " + data.Source);
    }

    // ── التضاريس ────────────────────────────────────────────
    static Terrain BuildTerrain(BargashData d, Transform parent)
    {
        var td = new TerrainData();
        td.heightmapResolution = HeightRes;
        td.size = new Vector3(d.SpanX, d.ZMax - d.ZMin, d.SpanY);

        // إعادة أخذ عيّنات من 141×144 إلى 257×257 باستكمال خطّي:
        // يونتي تقبل 2^n+1 فقط، وشبكتنا ليست كذلك.
        float[,] h = new float[HeightRes, HeightRes];
        float range = Mathf.Max(1e-4f, d.ZMax - d.ZMin);
        for (int y = 0; y < HeightRes; y++)
        {
            float fc = (float)y / (HeightRes - 1) * (d.Nc - 1);
            int c0 = Mathf.Clamp(Mathf.FloorToInt(fc), 0, d.Nc - 2);
            float v = fc - c0;
            for (int x = 0; x < HeightRes; x++)
            {
                float fr = (float)x / (HeightRes - 1) * (d.Nr - 1);
                int r0 = Mathf.Clamp(Mathf.FloorToInt(fr), 0, d.Nr - 2);
                float u = fr - r0;
                float z = d.HeightAt(r0, c0)         * (1 - u) * (1 - v)
                        + d.HeightAt(r0 + 1, c0)     * u       * (1 - v)
                        + d.HeightAt(r0, c0 + 1)     * (1 - u) * v
                        + d.HeightAt(r0 + 1, c0 + 1) * u       * v;
                h[y, x] = z / range;       // TerrainData تتوقّع [y, x]
            }
        }
        td.SetHeights(0, 0, h);

        // طبقة أرضية: أرض غابة برقش — عشب وأوراق متحلّلة وتربة داكنة
        var layer = new TerrainLayer();
        layer.diffuseTexture = GroundTexture(d);
        // صورة القمر تُفرَش مرّة واحدة على كامل المجسّم؛ النسيج الإجرائي يتكرّر
        layer.tileSize = satSource != null ? new Vector2(d.SpanX, d.SpanY) : new Vector2(420f, 420f);
        layer.smoothness = 0.05f;          // roughness 0.95 في Blender
        layer.metallic = 0f;
        AssetDatabase.CreateAsset(layer, "Assets/JAMRA/Data/BargashGround.terrainlayer");
        if (satSource != null)
        {
            // صورة القمر 10 م/بكسل: صادقة من بعيد وضبابية من قرب. نمزج معها
            // طبقتي تفصيل متكرّرتين (أرض غابة / تربة وحجر) يوزّعهما قناع الغابة.
            var floor = new TerrainLayer();
            floor.diffuseTexture = DetailTexture("GroundForest", 11,
                new Color(0.12f, 0.16f, 0.08f), new Color(0.21f, 0.25f, 0.13f),
                new Color(0.31f, 0.28f, 0.18f));
            floor.tileSize = new Vector2(26f, 26f);
            floor.smoothness = 0.04f;
            AssetDatabase.CreateAsset(floor, "Assets/JAMRA/Data/BargashForestFloor.terrainlayer");
            var soil = new TerrainLayer();
            soil.diffuseTexture = DetailTexture("GroundSoil", 29,
                new Color(0.47f, 0.35f, 0.24f), new Color(0.60f, 0.50f, 0.36f),
                new Color(0.80f, 0.76f, 0.66f));
            soil.tileSize = new Vector2(34f, 34f);
            soil.smoothness = 0.04f;
            AssetDatabase.CreateAsset(soil, "Assets/JAMRA/Data/BargashSoil.terrainlayer");

            td.alphamapResolution = 1024;
            td.terrainLayers = new TerrainLayer[] { layer, floor, soil };
        }
        else td.terrainLayers = new TerrainLayer[] { layer };

        AssetDatabase.CreateAsset(td, "Assets/JAMRA/Data/BargashTerrainData.asset");
        if (satSource != null)
        {
            // بعد الحفظ كأصل: خرائط المزج تُخزَّن أصولاً فرعية داخل TerrainData
            const int A = 1024;
            var alpha = new float[A, A, 3];
            for (int z = 0; z < A; z++)
                for (int x = 0; x < A; x++)
                {
                    float m = ForestWeight((x + 0.5f) / A, (z + 0.5f) / A, d);
                    alpha[z, x, 0] = 0.56f;
                    alpha[z, x, 1] = 0.44f * m;
                    alpha[z, x, 2] = 0.44f * (1f - m);
                }
            td.SetAlphamaps(0, 0, alpha);
        }
        var go = Terrain.CreateTerrainGameObject(td);
        go.name = "Bargash_Terrain";
        go.transform.SetParent(parent, false);
        // نُوسِّط المجسّم على نقطة الأصل كي تتطابق إحداثياته مع BargashData
        go.transform.position = new Vector3(-d.SpanX * 0.5f, 0f, -d.SpanY * 0.5f);

        var t = go.GetComponent<Terrain>();
        t.heightmapPixelError = 4f;
        t.basemapDistance = 2200f;
        t.treeDistance = 850f;
        t.treeBillboardDistance = 850f;   // بلا لوحات: لوحات URP تظهر سوداء
        t.treeMaximumFullLODCount = 10000;
        t.detailObjectDistance = 0f;
        return t;
    }

    // أرض برقش: ضجيج قيمي دوري يُمرَّر على تدرّج ألوان scene_bargash.py نفسه
    // صورة Sentinel-2 الحقيقية لبرقش (شمال للأعلى)، أو null إن تعذّر التنزيل
    static Texture2D satSource;

    /// <summary>
    /// ينزّل مرّة واحدة صورة Sentinel-2 cloudless (EOX، CC BY 4.0، بيانات
    /// Copernicus Sentinel 2016) لإطار برقش نفسه ويحفظها بجانب البيانات.
    /// </summary>
    static Texture2D LoadSatellite(BargashData d)
    {
        const string file = "Assets/JAMRA/Data/BargashSat.jpg";
        byte[] bytes = null;
        if (System.IO.File.Exists(file)) bytes = System.IO.File.ReadAllBytes(file);
        else if (d.Bbox != null && d.Bbox.Length == 4)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string url = "https://tiles.maps.eox.at/wms?service=WMS&version=1.1.1&request=GetMap"
                + "&layers=s2cloudless&styles=&srs=EPSG:4326&bbox="
                + d.Bbox[1].ToString(ci) + "," + d.Bbox[0].ToString(ci) + ","
                + d.Bbox[3].ToString(ci) + "," + d.Bbox[2].ToString(ci)
                + "&width=1024&height=1024&format=image/jpeg";
            using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                req.timeout = 40;
                var op = req.SendWebRequest();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!op.isDone && sw.ElapsedMilliseconds < 45000)
                    System.Threading.Thread.Sleep(50);
                if (op.isDone && req.result == UnityEngine.Networking.UnityWebRequest.Result.Success
                    && req.downloadHandler.data != null && req.downloadHandler.data.Length > 20000)
                {
                    bytes = req.downloadHandler.data;
                    System.IO.File.WriteAllBytes(file, bytes);
                    Debug.Log("JAMRA: نُزّلت صورة Sentinel-2 لبرقش (" + bytes.Length / 1024 + " KB)");
                }
                else Debug.LogWarning("JAMRA: تعذّر تنزيل صورة القمر: " + req.error);
            }
        }
        if (bytes == null) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGB24, false);
        if (!ImageConversion.LoadImage(t, bytes, false) || t.width < 64) return null;
        t.wrapMode = TextureWrapMode.Clamp;
        return t;
    }

    /// <summary>
    /// وزن الغابة [0..1] عند (u على الصفوف شمال→جنوب، v على الأعمدة غرب→شرق).
    /// من الصورة: الغابة خضراء داكنة (r≈g)، والحقول حمراء التربة فاتحة.
    /// </summary>
    static float ForestWeight(float u, float v, BargashData d)
    {
        if (satSource == null)
            return d.CanopyAt(Mathf.RoundToInt(u * (d.Nr - 1)), Mathf.RoundToInt(v * (d.Nc - 1)));
        Color sc = satSource.GetPixelBilinear(v, 1f - u);
        float lum = 0.3f * sc.r + 0.59f * sc.g + 0.11f * sc.b;
        float green = Mathf.Clamp01((0.08f - (sc.r - sc.g)) / 0.05f);
        float dark = Mathf.Clamp01((0.50f - lum) / 0.12f);
        float byColour = green * dark;
        if (coverSource == null) return byColour;
        // WorldCover: الغطاء الشجري لونه (0,100,0)، والشجيرات (255,187,34)
        Color wc = coverSource.GetPixelBilinear(v, 1f - u);
        float dt = Mathf.Abs(wc.r) + Mathf.Abs(wc.g - 0.392f) + Mathf.Abs(wc.b);
        float tree = Mathf.Clamp01(1f - dt / 0.6f);
        float ds = Mathf.Abs(wc.r - 1f) + Mathf.Abs(wc.g - 0.733f) + Mathf.Abs(wc.b - 0.133f);
        float shrub = Mathf.Clamp01(1f - ds / 0.5f);
        // التصنيف يقرّر أين الغابة، ولون الصورة يدرّج كثافتها داخلها
        return Mathf.Clamp01(tree * (0.55f + 0.45f * byColour) + shrub * 0.12f);
    }

    // نسيج تفصيل متكرّر: لونان يتدرّجان بضجيج واسع، ورقَط ثالث بضجيج ناعم
    static Texture2D DetailTexture(string name, int seedShift, Color a, Color b, Color c)
    {
        const int N = 512;
        var tex = new Texture2D(N, N, TextureFormat.RGB24, true);
        var px = new Color[N * N];
        float o = seedShift * 0.0137f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (float)x / N, v = (float)y / N;
                float n1 = Fbm((u + o) % 1f, (v + o * 2f) % 1f);
                float n2 = Fbm((u * 8f + o) % 1f, (v * 8f) % 1f);
                float n3 = Fbm((u * 16f) % 1f, (v * 16f + o) % 1f);
                Color col = Color.Lerp(a, b, Mathf.SmoothStep(0.3f, 0.7f, n1 * 0.6f + n2 * 0.4f));
                col = Color.Lerp(col, c, Mathf.SmoothStep(0.58f, 0.72f, n3));
                float k = 0.85f + 0.3f * n2;
                px[y * N + x] = new Color(col.r * k, col.g * k, col.b * k);
            }
        tex.SetPixels(px);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.anisoLevel = 8;
        tex.Apply(true);
        AssetDatabase.CreateAsset(tex, "Assets/JAMRA/Data/" + name + ".asset");
        return tex;
    }

    /// <summary>
    /// الغابة: قناع من صورة القمر يُحفظ كأصل، ومكوّن ForestRenderer يولّد
    /// منه الأشجار عند التشغيل ويرسمها بالـ instancing.
    /// </summary>
    static void BuildForest(Terrain terrain, BargashData d, Transform parent)
    {
        const int N = 512;
        var mask = new Texture2D(N, N, TextureFormat.RGB24, false);
        var px = new Color[N * N];
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                float w = ForestWeight((i + 0.5f) / N, (j + 0.5f) / N, d);
                w = Mathf.Max(w, 0.012f);       // أشجار متفرّقة خارج الغابة
                px[j * N + i] = new Color(w, w, w);
            }
        mask.SetPixels(px);
        mask.wrapMode = TextureWrapMode.Clamp;
        mask.Apply(false);
        AssetDatabase.CreateAsset(mask, "Assets/JAMRA/Data/ForestMask.asset");

        var go = new GameObject("Forest");
        go.transform.SetParent(parent, false);
        var fr = go.AddComponent<ForestRenderer>();
        fr.mask = mask;
        fr.terrain = terrain;
        fr.treeCount = TreeCount;
        fr.leafMats = new Material[] {
            MakeMaterial("OakLeafA", new Color(0.10f, 0.19f, 0.06f)),
            MakeMaterial("OakLeafB", new Color(0.13f, 0.23f, 0.08f)),
            MakeMaterial("OakLeafC", new Color(0.17f, 0.27f, 0.10f)),
            MakeMaterial("OakLeafD", new Color(0.15f, 0.21f, 0.08f)) };
        fr.trunkMat = MakeMaterial("OakTrunk", new Color(0.20f, 0.15f, 0.11f));
    }

    // خريطة ESA WorldCover 2021 (10 م، من Sentinel-1/2): صنف «غطاء شجري» الفعلي
    static Texture2D coverSource;

    static Texture2D LoadWorldCover(BargashData d)
    {
        const string file = "Assets/JAMRA/Data/BargashWorldCover.png";
        byte[] bytes = null;
        if (System.IO.File.Exists(file)) bytes = System.IO.File.ReadAllBytes(file);
        else if (d.Bbox != null && d.Bbox.Length == 4)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string b0 = d.Bbox[0].ToString(ci), b1 = d.Bbox[1].ToString(ci),
                   b2 = d.Bbox[2].ToString(ci), b3 = d.Bbox[3].ToString(ci);
            const string baseUrl = "https://services.terrascope.be/wms/v2?service=WMS&request=GetMap"
                + "&layers=WORLDCOVER_2021_MAP&styles=&width=1024&height=1024&format=image/png";
            // الخادم متقلّب: نجرّب صيغتي WMS مع الزمن وبدونه
            string[] urls = {
                baseUrl + "&version=1.1.1&srs=EPSG:4326&bbox=" + b1 + "," + b0 + "," + b3 + "," + b2 + "&time=2021-12-31",
                baseUrl + "&version=1.3.0&crs=EPSG:4326&bbox=" + b0 + "," + b1 + "," + b2 + "," + b3 + "&time=2021-12-31",
                baseUrl + "&version=1.1.1&srs=EPSG:4326&bbox=" + b1 + "," + b0 + "," + b3 + "," + b2,
                baseUrl + "&version=1.3.0&crs=EPSG:4326&bbox=" + b0 + "," + b1 + "," + b2 + "," + b3 };
            foreach (string url in urls)
            {
                using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
                {
                    req.timeout = 30;
                    var op = req.SendWebRequest();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (!op.isDone && sw.ElapsedMilliseconds < 32000)
                        System.Threading.Thread.Sleep(50);
                    var data = op.isDone ? req.downloadHandler.data : null;
                    // PNG حقيقي يبدأ بالبايت 0x89؛ ردّ الخطأ من الخادم نصّ XML
                    if (op.isDone && req.result == UnityEngine.Networking.UnityWebRequest.Result.Success
                        && data != null && data.Length > 2000 && data[0] == 0x89)
                    {
                        bytes = data;
                        System.IO.File.WriteAllBytes(file, bytes);
                        Debug.Log("JAMRA: نُزّلت خريطة ESA WorldCover لبرقش (" + bytes.Length / 1024 + " KB)");
                        break;
                    }
                    Debug.LogWarning("JAMRA: محاولة WorldCover فشلت: " + req.error
                        + (data != null && data.Length < 600 ? " " + System.Text.Encoding.UTF8.GetString(data) : ""));
                }
            }
        }
        if (bytes == null) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGB24, false);
        if (!ImageConversion.LoadImage(t, bytes, false) || t.width < 64) return null;
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        return t;
    }

    static Texture2D GroundTexture(BargashData d)
    {
        satSource = LoadSatellite(d);
        coverSource = LoadWorldCover(d);
        if (satSource != null)
        {
            // محور u في التيرين = الصفوف (شمال→جنوب)، وv = الأعمدة (غرب→شرق)
            const int M = 2048;
            var st = new Texture2D(M, M, TextureFormat.RGB24, true);
            var sp = new Color[M * M];
            for (int j = 0; j < M; j++)
                for (int i = 0; i < M; i++)
                {
                    float u = (i + 0.5f) / M, v = (j + 0.5f) / M;
                    Color c = satSource.GetPixelBilinear(v, 1f - u);
                    // تفصيل ناعم يكسر ضبابية 10 م/بكسل عن قرب
                    float n = Fbm((u * 24f) % 1f, (v * 24f) % 1f);
                    float k = 0.86f + 0.28f * n;
                    sp[j * M + i] = new Color(c.r * k, c.g * k, c.b * k);
                }
            st.SetPixels(sp);
            st.wrapMode = TextureWrapMode.Clamp;
            // الكاميرا تنظر بزاوية مائلة: بلا ترشيح متباين تذوب الصورة إلى لون واحد
            st.anisoLevel = 16;
            st.filterMode = FilterMode.Trilinear;
            st.mipMapBias = -0.7f;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            st.Apply(true);
            AssetDatabase.CreateAsset(st, "Assets/JAMRA/Data/GroundTex.asset");
            return st;
        }
        const int N = 256;
        var tex = new Texture2D(N, N, TextureFormat.RGB24, true);
        var px = new Color[N * N];
        // محطّات التدرّج: 0.34 عشب داكن · 0.48 تربة حمراء · 0.62 تربة فاتحة
        Color c0 = new Color(0.19f, 0.27f, 0.11f);
        Color c1 = new Color(0.34f, 0.37f, 0.18f);
        Color c2 = new Color(0.68f, 0.62f, 0.47f);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float n = Fbm((float)x / N, (float)y / N);
                px[y * N + x] = n <= 0.44f ? c0
                    : n <= 0.60f ? Color.Lerp(c0, c1, (n - 0.44f) / 0.16f)
                    : n <= 0.76f ? Color.Lerp(c1, c2, (n - 0.60f) / 0.16f)
                    : c2;
            }
        tex.SetPixels(px);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.Apply(true);
        AssetDatabase.CreateAsset(tex, "Assets/JAMRA/Data/GroundTex.asset");
        return tex;
    }

    static float Fbm(float u, float v)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        int period = 4;
        for (int o = 0; o < 4; o++)
        {
            sum += amp * VNoise(u * period, v * period, period, 7 + o);
            norm += amp;
            amp *= 0.5f;
            period *= 2;
        }
        return sum / norm;
    }

    static float VNoise(float x, float y, int p, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        int x0 = Mod(xi, p), x1 = Mod(xi + 1, p), y0 = Mod(yi, p), y1 = Mod(yi + 1, p);
        float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed);
        float c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static int Mod(int a, int p) => ((a % p) + p) % p;

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }
    }

    // ── الغابة ──────────────────────────────────────────────
    // ستّة ألوان أوراق البلوط من foliage_materials() في scene_bargash.py
    static readonly Color[] OakFoliage = {
        new Color(0.11f, 0.20f, 0.07f), new Color(0.13f, 0.23f, 0.08f),
        new Color(0.16f, 0.26f, 0.09f), new Color(0.19f, 0.29f, 0.11f),
        new Color(0.21f, 0.31f, 0.13f), new Color(0.17f, 0.24f, 0.09f) };
    const int OakVariants = 6;

    static void PlantTrees(Terrain terrain, BargashData d)
    {
        var td = terrain.terrainData;
        var protos = new TreePrototype[OakVariants];
        for (int k = 0; k < OakVariants; k++)
            protos[k] = new TreePrototype { prefab = BuildOakPrefab(k) };
        td.treePrototypes = protos;
        td.RefreshPrototypes();

        var list = new System.Collections.Generic.List<TreeInstance>(TreeCount);
        var rng = new System.Random(3);
        int guard = 0;
        // رفض بالعيّنة: نرمي نقطة ونقبلها باحتمال يساوي وزن المظلّة
        while (list.Count < TreeCount && guard++ < TreeCount * 60)
        {
            float u = (float)rng.NextDouble();   // على امتداد الصفوف
            float v = (float)rng.NextDouble();   // على امتداد الأعمدة
            int r = Mathf.RoundToInt(u * (d.Nr - 1));
            int c = Mathf.RoundToInt(v * (d.Nc - 1));
            float w = d.CanopyAt(r, c);
            if (satSource != null)
            {
                // الغطاء الحقيقي: الغابة داكنة خضراء في الصورة، والأرض الجرداء فاتحة
                Color sc = satSource.GetPixelBilinear(v, 1f - u);
                float lum = 0.3f * sc.r + 0.59f * sc.g + 0.11f * sc.b;
                // الغابة خضراء داكنة (r≈g)، والحقول حمراء التربة (r أعلى من g بوضوح)
                float green = Mathf.Clamp01((0.08f - (sc.r - sc.g)) / 0.05f);
                float dark = Mathf.Clamp01((0.50f - lum) / 0.12f);
                w = Mathf.Max(green * dark, 0.02f);
            }
            if (rng.NextDouble() > w) continue;

            var ti = new TreeInstance();
            // TreeInstance.position نسبيّة [0..1]: x على عرض المجسّم
            // (وهو SpanX أي الصفوف) وz على عمقه (الأعمدة)
            ti.position = new Vector3(u, 0f, v);
            // Blender: size_random 0.42 يعطي حجماً بين 0.58 و1.0
            float s = 1f - 0.42f * (float)rng.NextDouble();
            ti.prototypeIndex = rng.Next(OakVariants);   // Blender: collection pick random
            ti.widthScale = s * 1.35f;   // بلوط برقش عريض التاج
            ti.heightScale = s;
            ti.color = Color.white;
            ti.lightmapColor = Color.white;
            ti.rotation = (float)rng.NextDouble() * 6.283f;
            list.Add(ti);
        }
        td.SetTreeInstances(list.ToArray(), true);
        Debug.Log("JAMRA: زُرعت " + list.Count + " شجرة بستّة متغيّرات بلوط");
    }

    static float RangeRnd(System.Random rng, float a, float b)
        => a + (float)rng.NextDouble() * (b - a);

    // اتّجاه Blender (x, y, z) يُحوَّل إلى يونتي (x, z, y)
    static Quaternion AxisRot(Vector3 bdir)
        => Quaternion.FromToRotation(Vector3.up, new Vector3(bdir.x, bdir.z, bdir.y));

    // اتّجاه محور أسطوانة من rotation_euler=(rx, ry, 0) في Blender (ترتيب XYZ)
    static Vector3 BlenderAxis(float rx, float ry)
        => new Vector3(Mathf.Sin(ry) * Mathf.Cos(rx), -Mathf.Sin(rx), Mathf.Cos(ry) * Mathf.Cos(rx));

    static void AddPrim(System.Collections.Generic.List<CombineInstance> list, PrimitiveType type,
                        Vector3 pos, Quaternion rot, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = scale;
        list.Add(new CombineInstance
        {
            mesh = go.GetComponent<MeshFilter>().sharedMesh,
            transform = go.transform.localToWorldMatrix
        });
        Object.DestroyImmediate(go);
    }

    /// <summary>
    /// بلوطة تطابق oak_proto() في Blender: جذع مائل، ثلاثة فروع أوّلية،
    /// 4–5 كتل تاج متداخلة. تُولَّد بطول 7 وحدات كما يفعل Blender
    /// (tree.scale = 7/h). مادة واحدة للشجرة كلها، كما في Blender.
    /// </summary>
    static GameObject BuildOakPrefab(int k)
    {
        string path = "Assets/JAMRA/Data/Oak_" + k + ".prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        var rng = new System.Random(101 + k);
        const float h = 7f;
        float r = h * RangeRnd(rng, 0.34f, 0.46f);       // CANOPY_FR
        var parts = new System.Collections.Generic.List<CombineInstance>();

        // الجذع: مخروط مستدقّ (0.055h → 0.030h) مائل قليلاً
        float lx = RangeRnd(rng, -0.09f, 0.09f), ly = RangeRnd(rng, -0.09f, 0.09f);
        AddPrim(parts, PrimitiveType.Cylinder, new Vector3(0f, h * 0.26f, 0f),
                AxisRot(BlenderAxis(lx, ly)),
                new Vector3(h * 0.085f, h * 0.26f, h * 0.085f));

        // ثلاثة فروع أوّلية (0.026h → 0.010h)
        for (int b = 0; b < 3; b++)
        {
            float a = 2f * Mathf.PI * b / 3f + RangeRnd(rng, -0.4f, 0.4f);
            float ln = h * RangeRnd(rng, 0.26f, 0.40f);
            float tilt = RangeRnd(rng, 0.55f, 0.95f);
            float rx = Mathf.Sin(a) * tilt, ry = -Mathf.Cos(a) * tilt;
            // موقع Blender (x, y, z) ← (cos·ln·0.34·sin, sin·ln·0.34·sin, 0.48h + 0.34·ln)
            Vector3 pos = new Vector3(
                Mathf.Cos(a) * ln * 0.34f * Mathf.Sin(tilt),
                h * 0.48f + ln * 0.34f,
                Mathf.Sin(a) * ln * 0.34f * Mathf.Sin(tilt));
            AddPrim(parts, PrimitiveType.Cylinder, pos, AxisRot(BlenderAxis(rx, ry)),
                    new Vector3(h * 0.036f, ln * 0.5f, h * 0.036f));
        }

        // المظلّة: 4–5 كتل بيضاوية غير منتظمة الحجم والموضع
        int blobs = rng.Next(4, 6);
        for (int c = 0; c < blobs; c++)
        {
            float a = 2f * Mathf.PI * c / 4.5f + RangeRnd(rng, -0.5f, 0.5f);
            float off = r * RangeRnd(rng, 0.14f, 0.52f);
            float rad = r * RangeRnd(rng, 0.70f, 1.0f);
            Vector3 pos = new Vector3(Mathf.Cos(a) * off, h * RangeRnd(rng, 0.52f, 0.80f),
                                      Mathf.Sin(a) * off);
            Vector3 sc = new Vector3(
                2f * rad * RangeRnd(rng, 0.82f, 1.25f),     // x
                2f * rad * RangeRnd(rng, 0.85f, 1.05f),     // ارتفاع (z في Blender)
                2f * rad * RangeRnd(rng, 0.82f, 1.25f));    // y في Blender
            AddPrim(parts, PrimitiveType.Sphere, pos, Quaternion.identity, sc);
        }

        var mesh = new Mesh { name = "OakMesh_" + k };
        mesh.CombineMeshes(parts.ToArray(), true, true);
        string meshPath = "Assets/JAMRA/Data/OakMesh_" + k + ".asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);

        var leaf = MakeMaterial("OakLeaf_" + k, OakFoliage[k]);
        var root = new GameObject("Oak_" + k);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterial = leaf;
        // محرّك التيرين يتطلّب مُصيّراً واحداً على الجذر
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // ── الدرون ──────────────────────────────────────────────
    static GameObject BuildDrone(Transform parent, BargashData d)
    {
        // الدرون من موديل Blender حقيقي (JamraDrone.fbx) مُصدّر من JAMRA_Hexacopter
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/JAMRA/Data/JamraDrone.fbx");
        if (model != null)
        {
            var inst = Object.Instantiate(model);
            inst.name = "JAMRA_Drone";
            inst.transform.SetParent(parent, false);
            // الموديل بالمتر الحقيقي (1.78 م باعاً) وهو صغير على مشهد بطول كيلومترات؛
            // هذا المقياس يُبقي الدرون مقروءة من كاميرا المطاردة بعد كل إعادة بناء.
            inst.transform.localScale = Vector3.one * DroneScale;
            Vector3 c0 = d.Centre();
            inst.transform.position = new Vector3(c0.x, c0.y + 95f, c0.z);
            return inst;
        }

        // بديل برمجي إن لم يوجد الموديل
        var root = new GameObject("JAMRA_Drone");
        root.transform.SetParent(parent, false);
        Vector3 c = d.Centre();
        root.transform.position = new Vector3(c.x, c.y + 95f, c.z);

        var dark = MakeMaterial("DroneBody", new Color(0.17f, 0.19f, 0.21f));
        var red  = MakeMaterial("DroneRed",  new Color(0.70f, 0.14f, 0.11f));
        var grey = MakeMaterial("DroneProp", new Color(0.55f, 0.57f, 0.59f));

        Add(root.transform, PrimitiveType.Cylinder, dark,
            Vector3.zero, new Vector3(0.47f, 0.06f, 0.47f), "Body");

        // ستّة أذرع ومراوح — نفس نسب hexa.py
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.PI * 2f * i / 6f;
            var arm = Add(root.transform, PrimitiveType.Cylinder, dark,
                new Vector3(Mathf.Cos(a) * 0.305f, 0f, Mathf.Sin(a) * 0.305f),
                new Vector3(0.056f, 0.305f, 0.056f), "Arm" + i);
            arm.transform.localRotation =
                Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)))
                * Quaternion.Euler(90f, 0f, 0f);

            Add(root.transform, PrimitiveType.Cylinder, grey,
                new Vector3(Mathf.Cos(a) * 0.61f, 0.055f, Mathf.Sin(a) * 0.61f),
                new Vector3(0.559f, 0.006f, 0.559f), "Rotor" + i);
        }

        // أسطوانتا كرات الإخماد
        for (int s = -1; s <= 1; s += 2)
        {
            var drum = Add(root.transform, PrimitiveType.Cylinder, red,
                new Vector3(0f, -0.10f, s * 0.345f),
                new Vector3(0.30f, 0.17f, 0.30f), "Drum" + s);
            drum.transform.localRotation = Quaternion.Euler(90f, 0f, s * 34f);
        }
        return root;
    }

    static Transform[] CollectRotors(Transform drone)
    {
        // مراوح الموديل الحقيقي تُسمّى Prop_1..Prop_6، والبديل البرمجي Rotor0..5
        var found = new System.Collections.Generic.List<Transform>();
        foreach (Transform t in drone.GetComponentsInChildren<Transform>())
            if (System.Text.RegularExpressions.Regex.IsMatch(t.name, @"^(Prop_|Rotor)\d+$"))
                found.Add(t);
        return found.ToArray();
    }

    static GameObject Add(Transform parent, PrimitiveType type, Material m,
                          Vector3 pos, Vector3 scale, string name)
    {
        var g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }

    // ── النار ───────────────────────────────────────────────
    static FireController BuildFire(Transform parent)
    {
        var root = new GameObject("Fire");
        root.transform.SetParent(parent, false);
        var fc = root.AddComponent<FireController>();

        fc.flame = MakeParticles(root.transform, "Flame",
            new Color(1f, 0.82f, 0.32f), 1.6f, 7f, true);
        fc.smoke = MakeParticles(root.transform, "Smoke",
            new Color(0.13f, 0.12f, 0.11f), 5.0f, 4f, false);

        // شرر: جُسيمات صغيرة مضيئة ترتفع (جاذبية سالبة) وتنجرف مع الريح
        fc.embers = MakeParticles(root.transform, "Embers",
            new Color(1f, 0.72f, 0.28f), 3.2f, 3.5f, true);
        var emMain = fc.embers.main;
        emMain.startSize = 0.22f;
        emMain.gravityModifier = -0.22f;
        emMain.maxParticles = 600;

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(root.transform, false);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.46f, 0.15f);
        l.range = 80f;
        l.intensity = 0f;
        fc.glow = l;
        return fc;
    }

    static ParticleSystem MakeParticles(Transform parent, string name,
                                        Color col, float life, float speed,
                                        bool additive)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.startColor = col;
        main.startLifetime = life;
        main.startSpeed = speed;
        // تفاوت الحجم والعمر والدوران يكسر انتظام الجُسيمات فيقرأ لهباً لا كرات
        main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.55f, life * 1.15f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.283f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = additive ? 1.5f : 2.4f;
        noise.frequency = additive ? 0.7f : 0.22f;
        noise.scrollSpeed = 0.7f;
        var spin = ps.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-1.3f, 1.3f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 1400;
        main.gravityModifier = additive ? -0.06f : -0.14f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = additive ? 20f : 24f;
        shape.radius = 1f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        var col2 = ps.colorOverLifetime;
        col2.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(col, 0f),
                new GradientColorKey(additive
                    ? new Color(1f, 0.38f, 0.05f)
                    : new Color(0.50f, 0.47f, 0.44f), 0.45f),
                new GradientColorKey(additive
                    ? new Color(0.60f, 0.10f, 0.02f)
                    : new Color(0.62f, 0.60f, 0.58f), 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(additive ? 1f : 0.82f, 0f),
                new GradientAlphaKey(additive ? 0.8f : 0.45f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        col2.color = new ParticleSystem.MinMaxGradient(grad);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(
            1f, AnimationCurve.EaseInOut(0f, additive ? 1f : 0.45f,
                                         1f, additive ? 0.25f : 1.9f));

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = ParticleMaterial(additive);
        return ps;
    }

    static Texture2D SoftParticleTexture()
    {
        const string path = "Assets/JAMRA/Data/ParticleSoft.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (saved != null) return saved;
        const int N = 64;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, true);
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a);
                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
        t.SetPixels(px);
        t.wrapMode = TextureWrapMode.Clamp;
        t.Apply(true);
        AssetDatabase.CreateAsset(t, path);
        return t;
    }

    static Material ParticleMaterial(bool additive)
    {
        // الخامة تُحفظ كأصل كي لا تفقد مرجعها بعد إعادة تحميل الدومين
        string path = additive
            ? "Assets/JAMRA/Data/ParticleAdd.mat"
            : "Assets/JAMRA/Data/ParticleAlpha.mat";
        var saved = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (saved != null) return saved;

        var tex = SoftParticleTexture();
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find(additive ? "Legacy Shaders/Particles/Additive"
                                                  : "Legacy Shaders/Particles/Alpha Blended");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        var m = new Material(sh);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        // خامة URP الشفافة: القيم وحدها لا تكفي، يلزم الوسم والكلمة المفتاحية
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", additive ? 2f : 0f);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", additive
            ? (float)UnityEngine.Rendering.BlendMode.One
            : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", additive
            ? (float)UnityEngine.Rendering.BlendMode.One
            : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.SetShaderPassEnabled("ShadowCaster", false);
        m.renderQueue = 3000;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ── الإضاءة والكاميرا ───────────────────────────────────
    static Light BuildSun(Transform parent)
    {
        var go = new GameObject("Sun");
        go.transform.SetParent(parent, false);
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = new Color(1f, 0.96f, 0.89f);
        l.intensity = 1.15f;
        l.shadows = LightShadows.Soft;
        // اتّجاه الضوء كما في sun_and_sky(48°, 135°) في Blender، محوّلاً (x, y, z) → (x, z, y)
        go.transform.rotation = Quaternion.LookRotation(new Vector3(-0.473f, -0.743f, -0.473f));
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.40f, 0.43f, 0.46f);
        return l;
    }

    static Camera BuildCamera(Transform parent)
    {
        var old = Camera.main;
        if (old != null && old.transform.root != parent.root)
            Object.DestroyImmediate(old.gameObject);

        var go = new GameObject("MissionCamera");
        go.tag = "MainCamera";
        go.transform.SetParent(parent, false);
        var c = go.AddComponent<Camera>();
        c.fieldOfView = 60f;
        c.nearClipPlane = 0.3f;
        c.farClipPlane = 9000f;
        c.backgroundColor = SkyColor;
        go.AddComponent<AudioListener>();

        // ما بعد المعالجة (URP): تُفعَّل على الكاميرا كي يعمل الـVolume
        var urp = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        urp.renderPostProcessing = true;
        urp.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        return c;
    }

    /// <summary>
    /// حجم عام (Global Volume) فيه: توهّج للنار، وتصحيح لوني دافئ،
    /// وتعتيم للأطراف — كلها تُحفظ كأصل فتبقى بعد إعادة البناء.
    /// </summary>
    static void BuildPostFx(Transform parent)
    {
        var go = new GameObject("PostFx");
        go.transform.SetParent(parent, false);
        var vol = go.AddComponent<UnityEngine.Rendering.Volume>();
        vol.isGlobal = true;
        vol.priority = 1f;

        const string path = "Assets/JAMRA/Data/JamraPostFx.asset";
        AssetDatabase.DeleteAsset(path);
        var prof = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        AssetDatabase.CreateAsset(prof, path);

        var bloom = prof.Add<UnityEngine.Rendering.Universal.Bloom>(true);
        bloom.intensity.overrideState = true;  bloom.intensity.value = 0.9f;
        bloom.threshold.overrideState = true;  bloom.threshold.value = 0.85f;
        bloom.scatter.overrideState = true;    bloom.scatter.value = 0.7f;

        var grade = prof.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
        grade.postExposure.overrideState = true; grade.postExposure.value = -0.1f;
        grade.contrast.overrideState = true;     grade.contrast.value = 12f;
        grade.saturation.overrideState = true;   grade.saturation.value = 14f;
        grade.colorFilter.overrideState = true;  grade.colorFilter.value = new Color(1f, 0.98f, 0.95f);

        var vig = prof.Add<UnityEngine.Rendering.Universal.Vignette>(true);
        vig.intensity.overrideState = true;  vig.intensity.value = 0.18f;
        vig.smoothness.overrideState = true; vig.smoothness.value = 0.5f;

        vol.sharedProfile = prof;
        EditorUtility.SetDirty(prof);
    }

    static Material MakeMaterial(string name, Color c)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Diffuse");
        var m = new Material(sh);
        m.name = name;
        m.enableInstancing = true;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.08f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.08f);

        // يُحفظ كأصل: البريفاب (وبالتالي التيرين) لا يقبل مواد في الذاكرة فقط
        string p = "Assets/JAMRA/Data/" + name + ".mat";
        AssetDatabase.DeleteAsset(p);
        AssetDatabase.CreateAsset(m, p);
        return m;
    }
}
