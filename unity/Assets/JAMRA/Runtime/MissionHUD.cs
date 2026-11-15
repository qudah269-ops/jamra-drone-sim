// ════════════════════════════════════════════════════════════
//  جَمرة — واجهة المراقبة (بالعربي)
//  IMGUI عمداً: بلا Canvas ولا prefabs، فيبقى المشروع مولَّداً
//  من السكربتات وحدها. الأسلوب: شاشة مراقبة مهنية — زوايا
//  سماوية، قراءات الدرون، شريط الخطر، خريطة مصغّرة، وعلامات
//  على الحريق المكشوف.
// ════════════════════════════════════════════════════════════

using UnityEngine;

namespace Jamra
{
    public class MissionHUD : MonoBehaviour
    {
        public JamraGame game;
        public DroneController pilot;

        static readonly Color Cyan   = new Color(0.36f, 0.92f, 1.00f, 0.95f);
        static readonly Color CyanDim= new Color(0.36f, 0.92f, 1.00f, 0.30f);
        static readonly Color Amber  = new Color(1.00f, 0.66f, 0.25f, 1.00f);
        static readonly Color Red    = new Color(0.98f, 0.28f, 0.20f, 1.00f);
        static readonly Color Green  = new Color(0.40f, 0.95f, 0.55f, 1.00f);
        static readonly Color Panel  = new Color(0.02f, 0.06f, 0.09f, 0.55f);

        GUIStyle big, small, tiny, box;
        Texture2D white;
        Vector3 lastPos;
        float speed;

        void Ensure()
        {
            if (big != null) return;
            // العربي من اليمين: محاذاة يمنى وبلا التفاف (الالتفاف يقلب ترتيب الأسطر)
            big = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperRight, wordWrap = false };
            small = new GUIStyle(GUI.skin.label) { fontSize = 14,
                alignment = TextAnchor.UpperRight, wordWrap = false };
            tiny = new GUIStyle(GUI.skin.label) { fontSize = 12,
                alignment = TextAnchor.UpperRight, wordWrap = false };
            box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(14, 14, 11, 11) };
            white = Texture2D.whiteTexture;
        }

        Camera gimbal;
        WebBridge web;
        RenderTexture gimbalRT;
        Material thermalMat;

        // كاميرا الحمولة تحت الدرون: تُعرض مرّتين — مرئية وحرارية
        void Start()
        {
            if (game == null || game.drone == null) return;
            web = gameObject.GetComponent<WebBridge>();
            if (web == null) web = gameObject.AddComponent<WebBridge>();
            web.game = game;
            game.Web = web;
            gimbalRT = new RenderTexture(640, 360, 16);
            var go = new GameObject("GimbalCamera");
            go.transform.SetParent(game.drone, false);
            go.transform.localPosition = new Vector3(0f, -0.12f, 0.14f);
            go.transform.localRotation = Quaternion.Euler(42f, 0f, 0f);
            gimbal = go.AddComponent<Camera>();
            gimbal.fieldOfView = 55f;
            gimbal.nearClipPlane = 1.2f;
            gimbal.farClipPlane = 6000f;
            gimbal.depth = -5f;
            gimbal.targetTexture = gimbalRT;
            web.SetCamera(gimbalRT);
            Shader sh = Shader.Find("Jamra/Thermal");
            if (sh != null) thermalMat = new Material(sh);
            WebTuning.ApplyRendering();
        }

        // في المتصفّح: كاميرا الدرون ترسم المشهد كلّه مرّة ثانية؛ تكفيها بضع مرّات في الثانية
        float nextGimbal;
        void ThrottleGimbal()
        {
            if (!WebTuning.Active || gimbal == null || WebTuning.GimbalHz <= 0f) return;
            float now = Time.unscaledTime;
            bool due = now >= nextGimbal;
            if (due) nextGimbal = now + 1f / WebTuning.GimbalHz;
            gimbal.enabled = due;
        }

        void OnDestroy()
        {
            if (gimbalRT != null) gimbalRT.Release();
        }

        void DrawCameras()
        {
            if (gimbalRT == null) return;
            float pw = Mathf.Clamp(Screen.width * 0.23f, 190f, 380f);
            float ph = pw * 9f / 16f;
            float x = Screen.width - pw - 92f;
            float y = 208f;
            for (int k = 0; k < 2; k++)
            {
                Rect r = new Rect(x, y + k * (ph + 34f), pw, ph);
                Panel_(new Rect(r.x - 5, r.y - 24, r.width + 10, r.height + 29));
                if (Event.current.type == EventType.Repaint)
                {
                    if (k == 1 && thermalMat != null) Graphics.DrawTexture(r, gimbalRT, thermalMat);
                    else GUI.DrawTexture(r, gimbalRT, ScaleMode.StretchToFill, false);
                }
                GUI.color = k == 0 ? Cyan : Amber;
                Frame(r, 1f);
                GUI.color = k == 0 ? Cyan : Amber;
                Label(new Rect(r.x, r.y - 22, r.width, 20),
                      k == 0 ? "كاميرا الكشف — نار ودخان" : "الكاميرا الحرارية", tiny);
                // مؤشّر التصويب في المنتصف
                Bar(new Rect(r.center.x - 8, r.center.y, 16, 1), GUI.color);
                Bar(new Rect(r.center.x, r.center.y - 8, 1, 16), GUI.color);
                GUI.color = Color.white;
                DrawDetections(r, k == 1);
            }
        }

        // ── كاشف النار والدخان على صورة الكاميرا ───────────────
        // يحاكي مخرجات نموذج الكشف: صندوق حول اللهب وآخر حول عمود الدخان،
        // كلٌّ بثقته. يظهر الصندوق فقط حين يكون الهدف ضمن مدى الكشف
        // الذي يحسبه النموذج الفيزيائي وداخل مجال رؤية الكاميرا.
        void DrawDetections(Rect panel, bool thermal)
        {
            if (gimbal == null || game.drone == null) return;

            // صفحة المودل مفتوحة: نرسم ما يراه المودل المدرَّب فعلاً على هذه الصورة
            if (web != null && web.ModelLive)
            {
                foreach (var md in web.LatestDetections())
                {
                    bool life = md.label != "fire" && md.label != "smoke";
                    if (thermal && md.label == "smoke") continue;
                    Rect mb = new Rect(panel.x + md.x * panel.width, panel.y + md.y * panel.height,
                                      Mathf.Max(8f, md.w * panel.width), Mathf.Max(8f, md.h * panel.height));
                    Color mc = md.label == "fire" ? Red : md.label == "smoke" ? new Color(0.85f, 0.9f, 0.95f, 1f)
                            : md.label == "person" ? Cyan : Green;
                    string mn = md.label == "fire" ? "نار" : md.label == "smoke" ? "دخان" : JamraGame.LabelAr(md.label);
                    DetBox(panel, mb, mc, string.Format("{0} {1:0}٪ · المودل", mn, md.conf * 100f));
                }
                GUI.color = Green;
                Label(new Rect(panel.x, panel.yMax + 2, panel.width, 18), "المودل المدرَّب يعمل على هذه الصورة", tiny);
                GUI.color = Color.white;
                return;
            }
            if (game.MissionTime < game.IgniteAt || game.FireDia <= 0.01f) return;
            if (game.phase == Phase.Resolved && game.Success) return;

            float d = game.FireDia;
            Vector3 b = game.FirePos;
            float slant = Vector3.Distance(game.drone.position, b);
            Vector3 right = gimbal.transform.right;

            float flameR = MissionPhysics.FlameDetectRange(d);
            // الحراري يرى البؤرة من مسافة أبعد من المرئي
            if (slant <= flameR * (thermal ? 1.6f : 1f))
            {
                float w = Mathf.Max(1.6f, d * 0.75f), h = Mathf.Max(3f, d * 1.5f);
                Rect box;
                if (ProjBox(panel, out box, b - right * w, b + right * w,
                            b + Vector3.up * h - right * w, b + Vector3.up * h + right * w))
                {
                    float conf = Mathf.Clamp(0.62f + 0.37f * (1f - slant / Mathf.Max(flameR, 1f)), 0.5f, 0.99f);
                    DetBox(panel, box, Red, string.Format(thermal ? "بؤرة حرارية {0:0}٪" : "نار {0:0}٪", conf * 100f));
                }
            }

            // الأشخاص والحيوانات المرصودون: يظهرون في المرئي والحراري
            foreach (var sg in game.Sightings)
            {
                if (!sg.seen || sg.label == "غير محدَّد") continue;
                float ds = Vector3.Distance(game.drone.position, sg.pos);
                if (ds > 220f) continue;
                Rect sb;
                Vector3 half = right * 1.6f;
                if (ProjBox(panel, out sb, sg.pos - half - Vector3.up * 1.8f, sg.pos + half - Vector3.up * 1.8f,
                            sg.pos - half + Vector3.up * 1.8f, sg.pos + half + Vector3.up * 1.8f))
                {
                    float conf = Mathf.Clamp(0.97f - ds / 600f, 0.6f, 0.97f);
                    DetBox(panel, sb, sg.label == "person" ? Cyan : Green,
                           string.Format("{0} · محاكاة", JamraGame.LabelAr(sg.label)));
                }
            }

            if (thermal) return;        // الدخان بارد: لا يظهر في الحراري
            float smokeR = MissionPhysics.SmokeDetectRange(d);
            if (slant <= smokeR)
            {
                float H = Mathf.Clamp(d * MissionPhysics.PlumeRatio, 40f, 110f);
                float w = Mathf.Max(5f, d * 1.6f);
                Vector3 top = b + new Vector3(game.windMs * 6f, H, 0f);
                Vector3 baseP = b + Vector3.up * Mathf.Max(3f, d * 1.5f);
                Rect box;
                if (ProjBox(panel, out box, baseP - right * w, baseP + right * w,
                            top - right * w * 1.8f, top + right * w * 1.8f))
                {
                    float conf = Mathf.Clamp(0.58f + 0.38f * (1f - slant / Mathf.Max(smokeR, 1f)), 0.5f, 0.98f);
                    DetBox(panel, box, new Color(0.85f, 0.9f, 0.95f, 1f), string.Format("دخان {0:0}٪", conf * 100f));
                }
            }
        }

        // يُسقط نقاطاً عالمية على لوحة الكاميرا ويعيد الصندوق المحيط بها مقصوصاً على اللوحة
        bool ProjBox(Rect panel, out Rect box, params Vector3[] pts)
        {
            box = new Rect();
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
            foreach (Vector3 p in pts)
            {
                Vector3 v = gimbal.WorldToViewportPoint(p);
                if (v.z <= 0.5f) return false;
                float px = panel.x + v.x * panel.width;
                float py = panel.y + (1f - v.y) * panel.height;
                x0 = Mathf.Min(x0, px); x1 = Mathf.Max(x1, px);
                y0 = Mathf.Min(y0, py); y1 = Mathf.Max(y1, py);
            }
            x0 = Mathf.Max(x0, panel.x); y0 = Mathf.Max(y0, panel.y);
            x1 = Mathf.Min(x1, panel.xMax); y1 = Mathf.Min(y1, panel.yMax);
            if (x1 - x0 < 4f || y1 - y0 < 4f) return false;
            // صندوق أصغر من 8 بكسل يُكبَّر ليبقى مقروءاً
            if (x1 - x0 < 10f) { float c = (x0 + x1) * 0.5f; x0 = c - 5f; x1 = c + 5f; }
            if (y1 - y0 < 10f) { float c = (y0 + y1) * 0.5f; y0 = c - 5f; y1 = c + 5f; }
            box = Rect.MinMaxRect(x0, y0, x1, y1);
            return true;
        }

        void DetBox(Rect panel, Rect box, Color c, string label)
        {
            GUI.color = c;
            Frame(box, 2f);
            GUI.color = c;
            float ly = box.y - 18f < panel.y ? box.yMax + 1f : box.y - 18f;
            Label(new Rect(box.xMax - 130f, ly, 130f, 18f), label, tiny);
            GUI.color = Color.white;
        }

        void Update()
        {
            ThrottleGimbal();
            if (game != null) CaptureAndExport();
            // الحامل يتتبّع الحريق بعد كشفه، وقبل ذلك ينظر للأمام والأسفل
            if (gimbal != null && game != null)
            {
                float k = 1f - Mathf.Exp(-4f * Time.deltaTime);
                bool track = (game.phase == Phase.Detected || game.phase == Phase.Resolved)
                             && game.MissionTime >= game.IgniteAt;
                // التكبير: 12° عند فحص شخص أو حيوان، و34° عند تتبّع النار، و55° أثناء البحث
                float wantFov = game.GimbalZoom ? 12f : track ? 34f : 55f;
                gimbal.fieldOfView = Mathf.Lerp(gimbal.fieldOfView, wantFov, k);
                if (track)
                {
                    Vector3 dir = game.GimbalAim - gimbal.transform.position;
                    if (dir.sqrMagnitude > 0.01f)
                        gimbal.transform.rotation = Quaternion.Slerp(
                            gimbal.transform.rotation, Quaternion.LookRotation(dir), k);
                }
                else
                    gimbal.transform.localRotation = Quaternion.Slerp(
                        gimbal.transform.localRotation, Quaternion.Euler(42f, 0f, 0f), k);
            }
            if (game != null && game.drone != null)
            {
                speed = Vector3.Distance(game.drone.position, lastPos) / Mathf.Max(Time.deltaTime, 0.0001f);
                lastPos = game.drone.position;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // في المتصفّح لا خطوط نظام يرجع إليها يونتي، وخطّه الافتراضي بلا حروف عربية:
        // DejaVu Sans (Resources/JamraUIFont.ttf) فيه العربي وأشكاله واللاتيني والأرقام.
        static Font webFont;
#endif

        void OnGUI()
        {
            if (game == null) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (webFont == null) webFont = Resources.Load<Font>("JamraUIFont");
            if (webFont != null) GUI.skin.font = webFont;
#endif
            Ensure();

            if (game.phase == Phase.Briefing)
            {
                DrawBriefing();
                return;
            }

            DrawCorners();
            DrawTopLeft();
            DrawTopRight();
            DrawRiskBar();
            DrawMinimap();
            DrawCameras();
            DrawFireMarker();
            DrawMessageBar();
            DrawLog();
            DrawGas();
            DrawReport();
            DrawHints();
        }

        // ── شاشة الافتتاح ──────────────────────────────────
        void DrawBriefing()
        {
            GUILayout.BeginArea(new Rect(14, 14, 480, 380), GUIContent.none, box);
            GUILayout.Label(ArabicText.Fix("جَمرة — محاكاة مهمّة إطفاء · غابة برقش"), big);
            GUILayout.Space(6);
            Line(string.Format("سرعة انتشار النار المتوقّعة: {0:0.0} م/دقيقة",
                    MissionPhysics.RosMetresPerMin(game.ffdi)));
            Line(string.Format("سقف حمولة الدرون: قطر {0:0.0} م ({1} كرات)",
                    game.CapDia, game.balls));
            Line(string.Format("نافذة الإطفاء: {0:0.0} دقيقة من الاشتعال", game.WindowMin));
            Line(string.Format("مدى الضربة: {0:0.00} كم", game.StrikeKm));
            GUILayout.Space(8);
            Line("محاكاة: توقّعات القمرين NOAA-20 (VIIRS) و Sentinel-2 (MSI) مولَّدة،");
            Line("وكذلك قراءات حساسات الغاز. المودل الحقيقي يُجرَّب في صفحة الكشف.");
            Line("بلاغ من القمرين: احتمال حريق في غابة برقش.");
            Line("الدرون يقلع إلى الموقع، يكشف النار، يفحص وجود أشخاص وحيوانات، ثم يطفئ.");
            GUILayout.Space(8);
            if (GUILayout.Button(ArabicText.Fix("ابدأ المهمّة"), GUILayout.Height(34)))
                game.StartMission();
            GUILayout.EndArea();
        }

        // ── الزوايا السماوية ──────────────────────────────
        void DrawCorners()
        {
            float m = 18f, L = 46f, t = 2f;
            Rect sw = new Rect(0, 0, Screen.width, Screen.height);
            Corner(m, m, L, t, false, false);
            Corner(sw.width - m, m, L, t, true, false);
            Corner(m, sw.height - m, L, t, false, true);
            Corner(sw.width - m, sw.height - m, L, t, true, true);
        }

        void Corner(float x, float y, float L, float t, bool right, bool bottom)
        {
            float dx = right ? -1f : 1f, dy = bottom ? -1f : 1f;
            Bar(new Rect(dx > 0 ? x : x - L, bottom ? y - t : y, L, t), Cyan);
            Bar(new Rect(right ? x - t : x, dy > 0 ? y : y - L, t, L), Cyan);
        }

        // ── أعلى اليسار: الكشف والحريق ───────────────────
        void DrawTopLeft()
        {
            float x = 40f, y = 40f, w = 380f;
            Panel_(new Rect(x - 10, y - 8, w + 20, 116));
            GUI.color = Cyan;
            Label(new Rect(x, y, w, 24), "احتمال الكشف", small);
            float prob = Detection01();
            Bar(new Rect(x, y + 26, w, 10), CyanDim);
            Bar(new Rect(x, y + 26, w * prob, 10), Cyan);
            Label(new Rect(x, y + 40, w, 22),
                game.phase == Phase.Detected || game.phase == Phase.Resolved
                    ? "مكشوف — النار داخل المدى" : string.Format("{0:0}٪", prob * 100f), small);

            string state = game.MissionTime < game.IgniteAt
                ? "لا حريق بعد — ابحث داخل منطقة القمر"
                : string.Format("قطر النار {0:0.0} م · الحمولة {1:0.0} م", game.FireDia, game.CapDia);
            Label(new Rect(x, y + 66, w, 22), state, small);
            if (game.MissionTime >= game.IgniteAt)
                Label(new Rect(x, y + 88, w, 20),
                    string.Format("الكرات المتبقية: {0} · منذ الاشتعال {1:0} ث",
                        game.BallsLeft, game.MissionTime - game.IgniteAt), tiny);
            GUI.color = Color.white;
        }

        float Detection01()
        {
            if (game.drone == null) return 0f;
            if (game.phase == Phase.Detected || game.phase == Phase.Resolved) return 1f;
            if (game.MissionTime < game.IgniteAt) return 0f;
            float slant = Vector3.Distance(game.drone.position, game.FirePos);
            float r = Mathf.Max(MissionPhysics.SmokeDetectRange(game.FireDia),
                                MissionPhysics.FlameDetectRange(game.FireDia));
            return Mathf.Clamp01(1f - slant / Mathf.Max(r, 1f));
        }

        // ── أعلى اليمين: قراءات الدرون ───────────────────
        void DrawTopRight()
        {
            float w = 220f, x = Screen.width - w - 40f, y = 40f;
            Panel_(new Rect(x - 10, y - 8, w + 20, 150));
            float agl = 0f;
            if (game.drone != null && game.Data != null)
                agl = game.drone.position.y - game.Data.GroundAt(game.drone.position.x, game.drone.position.z);
            GUI.color = Cyan;
            Label(new Rect(x, y, w, 22), string.Format("الارتفاع: {0:0} م", agl), small);
            Label(new Rect(x, y + 24, w, 22), string.Format("السرعة: {0:0.0} م/ث", speed), small);
            Label(new Rect(x, y + 48, w, 22), "الوضع: " + CamName(pilot != null ? pilot.mode.ToString() : ""), small);
            Label(new Rect(x, y + 72, w, 22), "نافذة الإطفاء: " + game.WindowMin.ToString("0.0") + " د", small);
            GUI.color = Color.white;
            if (pilot != null && GUI.Button(new Rect(x, y + 102, w, 30), ArabicText.Fix(
                    pilot.autopilot ? "الطيار الآلي يعمل — إيقاف (T)" : "تشغيل الطيار الآلي (T)")))
                pilot.autopilot = !pilot.autopilot;
        }

        // ── الشريط العمودي: مستوى الخطر ─────────────────
        void DrawRiskBar()
        {
            float h = 300f, w = 12f;
            float x = Screen.width - 60f, y = Screen.height * 0.5f - h * 0.5f;
            float risk = game.CapDia > 0f ? Mathf.Clamp01(game.FireDia / game.CapDia) : 0f;
            GUI.color = new Color(1, 1, 1, 0.15f);
            GUI.DrawTexture(new Rect(x, y, w, h), white);
            GUI.color = risk < 0.6f ? Green : risk < 1f ? Amber : Red;
            GUI.DrawTexture(new Rect(x, y + h * (1f - risk), w, h * risk), white);
            GUI.color = Cyan;
            Label(new Rect(x - 70, y + h * 0.5f - 10, 66, 22), "الخطر", small);
            GUI.color = Color.white;
        }

        // ── أسفل اليسار: الخريطة المصغّرة ────────────────
        void DrawMinimap()
        {
            if (game.Data == null) return;
            float s = 200f;
            float x = 40f, y = Screen.height - s - 110f;
            Panel_(new Rect(x - 6, y - 6, s + 12, s + 12));
            GUI.color = new Color(1, 1, 1, 0.08f);
            GUI.DrawTexture(new Rect(x, y, s, s), white);
            GUI.color = CyanDim;
            GUI.DrawTexture(new Rect(x, y, s, 1), white);
            GUI.DrawTexture(new Rect(x, y + s - 1, s, 1), white);

            if (game.drone != null)
            {
                Vector2 d = ToMini(game.drone.position, x, y, s);
                GUI.color = Cyan;
                GUI.DrawTexture(new Rect(d.x - 4, d.y - 4, 8, 8), white);
            }
            if (game.phase == Phase.Detected || game.phase == Phase.Resolved)
            {
                Vector2 f = ToMini(game.FirePos, x, y, s);
                GUI.color = Red;
                GUI.DrawTexture(new Rect(f.x - 5, f.y - 5, 10, 10), white);
            }
            GUI.color = Cyan;
            Label(new Rect(x, y - 24, s, 20), "خريطة الموقع", tiny);
            GUI.color = Color.white;
        }

        Vector2 ToMini(Vector3 p, float x, float y, float s)
        {
            Vector3 c = game.Data.Centre();
            float u = Mathf.Clamp01((p.x - (c.x - game.Data.SpanX * 0.5f)) / game.Data.SpanX);
            float v = Mathf.Clamp01((p.z - (c.z - game.Data.SpanY * 0.5f)) / game.Data.SpanY);
            return new Vector2(x + u * s, y + (1f - v) * s);
        }

        // ── علامة على الحريق بعد كشفه ─────────────────────
        void DrawFireMarker()
        {
            if (Camera.main == null) return;
            if (game.phase != Phase.Detected && game.phase != Phase.Resolved) return;
            Vector3 sp = Camera.main.WorldToScreenPoint(game.FirePos);
            if (sp.z <= 0f) return;
            float y = Screen.height - sp.y;
            float r = Mathf.Clamp(game.FireDia * 6f, 40f, 260f);
            GUI.color = game.phase == Phase.Resolved
                ? (game.Success ? Green : Red) : Amber;
            Frame(new Rect(sp.x - r * 0.5f, y - r * 0.5f, r, r), 2f);
            Label(new Rect(sp.x - r * 0.5f, y - r * 0.5f - 22, 240, 20),
                string.Format("حريق · قطر {0:0.0} م", game.FireDia), small);
            GUI.color = Color.white;
        }

        // ── شريط الرسالة والحالة أسفل الوسط ───────────────
        // ── سجل البلاغات: آخر ما أُرسل واستُقبل ────────────────
        // ── حساسات الغاز على الدرون ───────────────────────
        void DrawGas()
        {
            if (game.phase == Phase.Resolved) return;
            var g = game.GasNow;
            if (g == null) return;
            float w = 380f, lh = 18f, x = 40f, y = 164f + 4 * 19f + 40f;
            Panel_(new Rect(x - 10, y - 6, w + 20, 4 * lh + 30));
            GUI.color = Cyan;
            Label(new Rect(x, y, w, 20), "حساسات الغاز على الدرون (محاكاة لحساسات حقيقية)", tiny);
            GUI.color = new Color(0.85f, 0.95f, 1f, 0.95f);
            Label(new Rect(x, y + 20, w, 18), string.Format("CO {0:0.0} ppm · CO₂ {1:0} ppm · PM2.5 {2:0} µg/m³", g.co, g.co2, g.pm25), tiny);
            Label(new Rect(x, y + 20 + lh, w, 18), string.Format("VOC {0:0} · NO₂ {1:0} ppb · O₃ {2:0} ppb", g.voc, g.no2, g.o3), tiny);
            GUI.color = game.GasCauseIdx >= 0 ? Amber : CyanDim;
            Label(new Rect(x, y + 20 + 2 * lh, w, 18), game.GasCauseIdx >= 0
                ? "السبب من الغاز: " + JamraGame.Causes[game.GasCauseIdx]
                : (game.GasReady ? "" : "يجمع عيّنات الدخان…"), tiny);
            GUI.color = Color.white;
        }

        void DrawLog()
        {
            if (game.Log.Count == 0 || game.phase == Phase.Resolved) return;
            int n = Mathf.Min(4, game.Log.Count);
            float w = 380f, lh = 19f;
            float x = 40f, y = 164f;
            Panel_(new Rect(x - 10, y - 6, w + 20, n * lh + 32));
            GUI.color = Cyan;
            Label(new Rect(x, y, w, 20), "سجل البلاغات", tiny);
            for (int i = 0; i < n; i++)
            {
                string line = game.Log[game.Log.Count - n + i];
                GUI.color = line.Contains("تحذير") ? Red : new Color(0.85f, 0.95f, 1f, 0.95f);
                Label(new Rect(x, y + 22 + i * lh, w, 20), line, tiny);
            }
            GUI.color = Color.white;
        }

        // ── تقرير نهاية المهمّة: ما يُرسل للدفاع المدني ويُعرض في الويب ──
        void DrawReport()
        {
            if (game.phase != Phase.Resolved) return;
            float w = Mathf.Min(620f, Screen.width - 80f), h = 388f;
            float x = (Screen.width - w) * 0.5f, y = Mathf.Max(20f, Screen.height * 0.5f - h * 0.5f - 40f);
            GUI.color = new Color(0.02f, 0.05f, 0.08f, 0.9f);
            GUI.DrawTexture(new Rect(x, y, w, h), white);
            GUI.color = game.Success ? Green : Red;
            Frame(new Rect(x, y, w, h), 2f);
            GUI.color = game.Success ? Green : Red;
            Label(new Rect(x + 16, y + 10, w - 32, 26),
                  game.Success ? "تقرير المهمة — أُخمد الحريق · أُرسل للدفاع المدني"
                               : "تقرير المهمة — تعذّر الإخماد · طلب إسناد عاجل", big);

            // صورة الحريق الملتقطة لحظة الكشف
            float iw = 250f, ih = iw * 9f / 16f;
            Rect img = new Rect(x + 16, y + 48, iw, ih);
            if (snapshot != null) GUI.color = Color.white;
            if (snapshot != null) GUI.DrawTexture(img, snapshot, ScaleMode.StretchToFill, false);
            GUI.color = Cyan;
            Frame(img, 1f);
            GUI.color = Cyan;
            Label(new Rect(img.x, img.yMax + 2, iw, 18), "صورة الحريق من كاميرا الدرون", tiny);

            float tx = x + iw + 32, tw = w - iw - 48, ty = y + 48;
            GUI.color = Color.white;
            Label(new Rect(tx, ty, tw, 22), string.Format("المحترق: {0:0.0} م² · {1:0.00000}٪ من غابات برقش", game.BurnAreaM2, game.DamagePct), small);
            Label(new Rect(tx, ty + 24, tw, 22), string.Format("قطر النار عند الانتهاء: {0:0.0} م", game.FireDia), small);
            GUI.color = game.PredOk ? Green : Amber;
            Label(new Rect(tx, ty + 48, tw, 22), game.PredOk
                ? string.Format("توقّع القمر: صحيح (الخطأ {0:0} م)", game.PredErrM)
                : string.Format("توقّع القمر: غير دقيق (الخطأ {0:0} م)", game.PredErrM), small);
            GUI.color = game.PersonsSeen > 0 ? Red : Color.white;
            Label(new Rect(tx, ty + 72, tw, 22), string.Format("أشخاص رُصدوا: {0} · حيوانات: {1}",
                  game.PersonsSeen, game.AnimalsSeen), small);
            GUI.color = Color.white;
            Label(new Rect(tx, ty + 96, tw, 22), string.Format("الكرات المستعملة: {0} من {1}",
                  game.balls - game.BallsLeft, game.balls), small);
            Label(new Rect(tx, ty + 120, tw, 22), string.Format("نقاط القمرين: {0} / {1} من 100",
                  game.SatA.total, game.SatB.total), small);

            GUI.color = Amber;
            Label(new Rect(x + 16, y + 218, w - 32, 20), "السبب المتوقّع (تقدير آلي):", small);
            GUI.color = Color.white;
            // السبب نصّ طويل: يُقسَّم يدوياً لأن الالتفاف التلقائي يقلب ترتيب الأسطر العربية
            string[] words = (game.CauseText ?? "").Split(' ');
            string cur = "";
            float cy = y + 240;
            foreach (string wd in words)
            {
                if ((cur + " " + wd).Length > 62 && cur.Length > 0)
                {
                    Label(new Rect(x + 16, cy, w - 32, 20), cur, tiny);
                    cy += 18;
                    cur = wd;
                }
                else cur = cur.Length == 0 ? wd : cur + " " + wd;
            }
            if (cur.Length > 0) { Label(new Rect(x + 16, cy, w - 32, 20), cur, tiny); cy += 18; }

            GUI.color = Cyan;
            cy = Mathf.Max(cy + 6, y + 286);
            Label(new Rect(x + 16, cy, w - 32, 20), "سجل البلاغات:", tiny);
            int n = Mathf.Min(4, game.Log.Count);
            GUI.color = new Color(0.85f, 0.95f, 1f, 0.95f);
            for (int i = 0; i < n; i++)
                Label(new Rect(x + 16, cy + 18 + i * 17, w - 32, 18), game.Log[game.Log.Count - n + i], tiny);
            GUI.color = Color.white;
        }

        Texture2D snapshot;
        float snapAt = -1f;
        int exportedSerial;

        // يلتقط صورة الحريق بعد ثانيتين من الكشف (حين يكون الحامل قد استقرّ على الهدف)
        void CaptureAndExport()
        {
            if (game.phase == Phase.Searching) { snapAt = -1f; }
            if (game.phase == Phase.Detected && snapAt < 0f) snapAt = Time.time + 2f;
            if (game.phase == Phase.Detected && snapAt > 0f && Time.time >= snapAt && gimbalRT != null)
            {
                snapAt = float.MaxValue;
                if (snapshot == null) snapshot = new Texture2D(gimbalRT.width, gimbalRT.height, TextureFormat.RGB24, false);
                var prev = RenderTexture.active;
                RenderTexture.active = gimbalRT;
                snapshot.ReadPixels(new Rect(0, 0, gimbalRT.width, gimbalRT.height), 0, 0);
                snapshot.Apply();
                RenderTexture.active = prev;
                if (web != null) web.SetFireImage(snapshot);
            }
            // عند كل تقرير جديد: يُكتب ملف JSON وصورة ليقرأهما موقع الويب
            if (game.phase == Phase.Resolved && exportedSerial != game.ReportSerial)
            {
                exportedSerial = game.ReportSerial;
                try
                {
                    string dir = System.IO.Path.Combine(Application.dataPath, "../WebOut");
                    System.IO.Directory.CreateDirectory(dir);
                    var rep = new WebReport
                    {
                        success = game.Success,
                        damagePct = game.DamagePct,
                        fireDiameterM = game.FireDia,
                        predictionOk = game.PredOk,
                        predictionErrorM = game.PredErrM,
                        lat = game.PredLat,
                        lon = game.PredLon,
                        persons = game.PersonsSeen,
                        animals = game.AnimalsSeen,
                        ballsUsed = game.balls - game.BallsLeft,
                        cause = game.CauseText,
                        message = game.Message,
                        log = game.Log.ToArray(),
                        image = snapshot != null ? "fire.png" : "",
                        time = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    };
                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "mission_report.json"),
                                                JsonUtility.ToJson(rep, true));
                    if (snapshot != null)
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "fire.png"),
                                                     snapshot.EncodeToPNG());
                }
                catch (System.Exception e) { Debug.LogWarning("JAMRA: تعذّر حفظ تقرير الويب: " + e.Message); }
            }
        }

        [System.Serializable]
        class WebReport
        {
            public bool success;
            public float damagePct, fireDiameterM;
            public bool predictionOk;
            public float predictionErrorM;
            public double lat, lon;
            public int persons, animals, ballsUsed;
            public string cause, message, time, image;
            public string[] log;
        }

        void DrawMessageBar()
        {
            float w = Mathf.Min(760f, Screen.width - 380f);
            float x = (Screen.width - w) * 0.5f, y = Screen.height - 96f;
            Panel_(new Rect(x - 12, y - 10, w + 24, 56));
            Color c = game.phase == Phase.Resolved
                ? (game.Success ? Green : Red) : Amber;
            GUI.color = c;
            Label(new Rect(x, y, w, 36), game.Message, small);
            GUI.color = Color.white;

            if (game.phase == Phase.Resolved)
            {
                if (GUI.Button(new Rect(Screen.width * 0.5f - 60, y + 58, 120, 28), ArabicText.Fix("مهمّة جديدة")))
                    game.StartMission();
            }
        }

        // ── تعليمات المفاتيح ────────────────────────────
        void DrawHints()
        {
            GUI.color = Cyan;
            Label(new Rect(40, Screen.height - 30, Screen.width - 80, 22),
                "WASD تحرّك · Q/E ارتفاع · SHIFT سرعة · SPACE إسقاط · T طيار آلي · TAB الكاميرا", tiny);
            GUI.color = Color.white;
        }

        // ── أدوات رسم ───────────────────────────────────
        void Line(string s) { GUILayout.Label(ArabicText.Fix(s), small); }

        void Label(Rect r, string s, GUIStyle st) { GUI.Label(r, ArabicText.Fix(s), st); }

        void Bar(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = Color.white;
        }

        void Frame(Rect r, float t)
        {
            Bar(new Rect(r.x, r.y, r.width, t), GUI.color);
            Bar(new Rect(r.x, r.yMax - t, r.width, t), GUI.color);
            Bar(new Rect(r.x, r.y, t, r.height), GUI.color);
            Bar(new Rect(r.xMax - t, r.y, t, r.height), GUI.color);
        }

        void Panel_(Rect r)
        {
            GUI.color = Panel;
            GUI.DrawTexture(r, white);
            GUI.color = Color.white;
        }

        string CamName(string mode)
        {
            switch (mode)
            {
                case "Chase": return "خلف الدرون";
                case "Fpv":   return "عين الدرون";
                case "Top":   return "من الأعلى";
                default:      return mode;
            }
        }
    }
}
