// ════════════════════════════════════════════════════════════
//  جَمرة — إعدادات الأداء لنسخة الويب
//  تُقرأ من window.JAMRA_TUNING في صفحة اللعبة (WebGLTemplates/Jamra/index.html)
//  ويمكن تجاوزها من الرابط: index.html?trees=40000&gimbalHz=4&post=0&shadows=0&scale=0.8
//  فتُجرَّب التراكيب وتُضبط بلا إعادة بناء. على الكمبيوتر لا تأثير لها.
// ════════════════════════════════════════════════════════════

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Jamra
{
    public static class WebTuning
    {
        [Serializable]
        class Values
        {
            public int trees = 80000;        // عدد الأشجار
            public float far = 1600f;        // مسافة رسم الغابة (م)
            public float gimbalHz = 5f;      // كم مرّة/ث تُرسم كاميرا الدرون (0 = كل إطار)
            public int post = 1;             // ما بعد المعالجة على الكاميرا الرئيسية
            public int shadows = 1;          // ظلال الشمس
            public float scale = 1f;         // دقّة الرسم (renderScale)
        }

        static Values v;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern string JamraWebTuning();
        public const bool Active = true;
#else
        public const bool Active = false;
#endif

        static Values Get()
        {
            if (v != null) return v;
            v = new Values();
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                string json = JamraWebTuning();
                if (!string.IsNullOrEmpty(json)) JsonUtility.FromJsonOverwrite(json, v);
            }
            catch (Exception e) { Debug.LogWarning("JAMRA: إعدادات الأداء: " + e.Message); }
            Debug.Log("JAMRA: إعدادات الويب " + JsonUtility.ToJson(v));
#endif
            return v;
        }

        public static int Trees { get { return Get().trees; } }
        public static float Far { get { return Get().far; } }
        public static float GimbalHz { get { return Get().gimbalHz; } }

        /// <summary>ما بعد المعالجة والظلال ودقّة الرسم — يُستدعى مرّة عند البدء.</summary>
        public static void ApplyRendering()
        {
            if (!Active) return;
            var t = Get();
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) urp.renderScale = Mathf.Clamp(t.scale, 0.3f, 1f);
            var cam = Camera.main;
            if (cam != null)
            {
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data != null) data.renderPostProcessing = t.post != 0;
            }
            if (t.shadows == 0)
                foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    l.shadows = LightShadows.None;
        }
    }
}
