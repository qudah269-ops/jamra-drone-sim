// ════════════════════════════════════════════════════════════
//  جَمرة — بناء نسخة الويب (WebGL) بضغطة واحدة
//
//  القائمة:  JAMRA ▸ Build Web (WebGL)
//  أو من سطر الأوامر:
//    Unity.exe -batchmode -quit -buildTarget WebGL -projectPath <المشروع>
//              -executeMethod JamraWebBuild.CommandLine -logFile build_web.log
//
//  يبني مشهد برقش من جديد (JamraSceneBuilder) ويحفظه في Assets/Scenes/Jamra.unity،
//  ثم يبني WebGL في Builds/WebGL مع قالب WebGLTemplates/Jamra، وينسخ إليه
//  JamraWeb: اللوحة (dashboard.html) وصفحة الكشف (detect.html) والمودلات.
//  الناتج ملفات ثابتة: ارفع محتوى Builds/WebGL على أي سيرفر ويب (HTTPS للكاميرا).
// ════════════════════════════════════════════════════════════

using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class JamraWebBuild
{
    const string ScenePath = "Assets/Scenes/Jamra.unity";
    const string OutDir = "Builds/WebGL";

    [MenuItem("JAMRA/Build Web (WebGL)")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Run(false);
    }

    public static void CommandLine() { Run(true); }

    static void Run(bool batch)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
        {
            Fail(batch, "وحدة Web Build Support غير مثبّتة لهذه النسخة من يونتي. ثبّتها من Unity Hub ▸ Installs ▸ Add modules.");
            return;
        }

        // ── 1) المشهد: يُبنى بالكود كما في JAMRA ▸ Build Bargash Scene ثم يُحفظ ──
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        JamraSceneBuilder.Build();
        if (GameObject.Find("JAMRA") == null) { Fail(batch, "فشل بناء المشهد (انظر السجل)."); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        // ── 2) ما يُحمَّل بـ Shader.Find وقت التشغيل لازم يدخل البناء ──
        IncludeShader("Jamra/Thermal");

        // ── 3) إعدادات الويب ──
        PlayerSettings.WebGL.template = "PROJECT:Jamra";
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;   // يعمل على أي سيرفر بلا ضبط ترويسات
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.nameFilesAsHashes = false;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        // "PC" نزل بالإطارات إلى ~1.5/ث في المتصفّح على Intel UHD؛ Mobile أخفّ (ظلال وما بعد المعالجة)
        UseQualityOnWebGL("Mobile");
        AssetDatabase.SaveAssets();

        // ── 4) البناء ──
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = OutDir,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None
        });
        var s = report.summary;
        if (s.result != BuildResult.Succeeded)
        {
            Fail(batch, "فشل البناء: " + s.result + " · أخطاء: " + s.totalErrors);
            return;
        }
        Debug.Log("JAMRA: نسخة الويب جاهزة في " + Path.GetFullPath(OutDir)
                  + " · الحجم " + (s.totalSize / (1024f * 1024f)).ToString("0.0") + " MB · "
                  + s.totalTime.TotalMinutes.ToString("0.0") + " دقيقة");
        if (!batch) EditorUtility.RevealInFinder(OutDir);
    }

    // بعد أي بناء WebGL (من القائمة أو من File ▸ Build): اللوحة وصفحة الكشف والمودلات
    [PostProcessBuild(100)]
    public static void CopyWebPages(BuildTarget target, string outPath)
    {
        if (target != BuildTarget.WebGL) return;
        string src = Path.GetFullPath(Path.Combine(Application.dataPath, "../JamraWeb"));
        if (!Directory.Exists(src)) { Debug.LogWarning("JAMRA: لم أجد مجلد JamraWeb"); return; }
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            string rel = f.Substring(src.Length + 1).Replace('\\', '/');
            if (rel == "index.html") rel = "dashboard.html";   // index.html في الناتج = اللعبة
            string dst = Path.Combine(outPath, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(f, dst, true);
        }
        Debug.Log("JAMRA: نُسخت اللوحة وصفحة الكشف إلى " + outPath);
    }

    static void IncludeShader(string name)
    {
        var sh = Shader.Find(name);
        if (sh == null) { Debug.LogWarning("JAMRA: لم أجد الشيدر " + name); return; }
        var so = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        for (int i = 0; i < arr.arraySize; i++)
            if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) return;
        arr.InsertArrayElementAtIndex(arr.arraySize);
        arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
        so.ApplyModifiedProperties();
    }

    static void UseQualityOnWebGL(string levelName)
    {
        int level = System.Array.IndexOf(QualitySettings.names, levelName);
        if (level < 0) return;
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
        if (assets.Length == 0) return;
        var so = new SerializedObject(assets[0]);
        var map = so.FindProperty("m_PerPlatformDefaultQuality");
        if (map == null) return;
        for (int i = 0; i < map.arraySize; i++)
        {
            var e = map.GetArrayElementAtIndex(i);
            if (e.FindPropertyRelative("first").stringValue == "WebGL")
                e.FindPropertyRelative("second").intValue = level;
        }
        so.ApplyModifiedProperties();
    }

    static void Fail(bool batch, string msg)
    {
        Debug.LogError("JAMRA: " + msg);
        if (batch) EditorApplication.Exit(1);
        else EditorUtility.DisplayDialog("JAMRA", msg, "حسناً");
    }
}
