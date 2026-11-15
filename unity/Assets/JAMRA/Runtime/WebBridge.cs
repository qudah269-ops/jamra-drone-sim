// ════════════════════════════════════════════════════════════
//  جَمرة — جسر الويب
//  سيرفر HTTP صغير داخل يونتي على http://localhost:8090/
//   /            لوحة البلاغات            /detect     صفحة المودل المدرَّب
//   /live.json   حالة المهمّة الحيّة        /cam.jpg    كاميرا الدرون الآن
//   /fire.png    صورة الحريق              /sighting/N.jpg  لقطة شخص/حيوان
//   POST /det    نتائج المودل من المتصفّح   POST /email  إرسال بريد حقيقي (SMTP)
//  الملفات الثابتة (الصفحات، onnxruntime، المودلات) في مجلد JamraWeb
//  بجانب Assets، كي لا يستوردها يونتي.
//
//  نسخة الويب (WebGL): المتصفّح لا يسمح بسيرفر ولا SMTP ولا خيوط،
//  فتُرسَل الحالة والصور إلى JavaScript عبر Plugins/WebGL/JamraWeb.jslib،
//  وتصل للوحة وصفحة الكشف عبر BroadcastChannel (انظر WebGLTemplates/Jamra).
//  نتائج المودل ترجع عبر SendMessage("JAMRA", "WebDet", json).
// ════════════════════════════════════════════════════════════

#if UNITY_WEBGL && !UNITY_EDITOR
#define JAMRA_WEBGL
#endif

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
#if JAMRA_WEBGL
using System.Runtime.InteropServices;
#else
using System.Net;
using System.Net.Mail;
#endif

namespace Jamra
{
    public class WebBridge : MonoBehaviour
    {
        public JamraGame game;
        public const int Port = 8090;

#if JAMRA_WEBGL
        [DllImport("__Internal")] static extern void JamraWebPushLive(string json);
        [DllImport("__Internal")] static extern void JamraWebPushImage(string key, byte[] data, int len, string type);
        [DllImport("__Internal")] static extern int JamraWebCamWanted();
        readonly Dictionary<int, byte[]> pushedSightings = new Dictionary<int, byte[]>();
#else
        HttpListener listener;
        Thread thread;
#endif
        volatile string liveJson = "{}";
        volatile byte[] firePng;
        volatile byte[] camJpg;
        readonly object detLock = new object();
        List<Det> latest = new List<Det>();
        Det bestLife;
        long lastDetTicks;               // آخر وصول من المودل (UTC) — يُكتب من خيط السيرفر
        string webRoot, fallbackRoot;
        float nextPush, nextCam;
        RenderTexture camRT;
        Texture2D camTex;

        [Serializable]
        public class Det
        {
            public string label;   // fire, smoke, person, dog, cat, bird …
            public float conf;
            public float x, y, w, h;   // 0..1 من إطار الكاميرا (الزاوية العليا اليسرى)
        }
        [Serializable] class DetPost { public Det[] items; }

        static readonly HashSet<string> Life = new HashSet<string> {
            "person", "bird", "cat", "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe" };

        // المودل في المتصفّح يحتاج 1–2 ث لكل لقطة على المعالج: نعدّه متّصلاً ضمن 5 ث
        public bool ModelLive
        {
            get { return DateTime.UtcNow.Ticks - Interlocked.Read(ref lastDetTicks) < TimeSpan.TicksPerSecond * 5; }
        }

#if JAMRA_WEBGL
        void Start()
        {
            Debug.Log("JAMRA: نسخة الويب — اللوحة وصفحة الكشف تتّصل عبر BroadcastChannel");
        }

        public void SetCamera(RenderTexture rt) { camRT = rt; }
        public void SetFireImage(Texture2D tex)
        {
            if (tex == null) return;
            firePng = tex.EncodeToPNG();
            JamraWebPushImage("fire.png", firePng, firePng.Length, "image/png");
        }

        // يناديها JavaScript: unityInstance.SendMessage("JAMRA", "WebDet", json)
        public void WebDet(string json) { ApplyDets(json); }

        // الكاميرا تُلتقط فقط إن كانت صفحة الكشف مفتوحة (ReadPixels مكلف في المتصفّح)،
        // أو إن احتاجتها المحاكاة للقطة شخص/حيوان
        bool WantCam() { return JamraWebCamWanted() != 0 || (game != null && game.phase == Phase.Detected); }

        // الصور قبل الحالة: حين تقرأ اللوحة hasPhoto تكون الصورة قد وصلت
        void PushWeb()
        {
            if (game != null)
            for (int i = 0; i < game.Sightings.Count; i++)
            {
                var ph = game.Sightings[i].photo;
                byte[] sent;
                if (ph == null || (pushedSightings.TryGetValue(i, out sent) && ReferenceEquals(sent, ph))) continue;
                pushedSightings[i] = ph;
                JamraWebPushImage("sighting/" + i + ".jpg", ph, ph.Length, "image/jpeg");
            }
            JamraWebPushLive(liveJson);
        }
#else
        void Start()
        {
            webRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../JamraWeb"));
            fallbackRoot = Path.Combine(Application.dataPath, "JAMRA/Web");
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://localhost:" + Port + "/");
                listener.Start();
                thread = new Thread(Serve) { IsBackground = true };
                thread.Start();
                Debug.Log("JAMRA: لوحة الويب http://localhost:" + Port + "/  ·  المودل http://localhost:" + Port + "/detect");
            }
            catch (Exception e)
            {
                Debug.LogWarning("JAMRA: تعذّر تشغيل سيرفر الويب: " + e.Message);
                listener = null;
            }
        }

        void OnDestroy()
        {
            try { if (listener != null) { listener.Stop(); listener.Close(); } } catch { }
            listener = null;
        }

        public void SetCamera(RenderTexture rt) { camRT = rt; }
        public void SetFireImage(Texture2D tex) { if (tex != null) firePng = tex.EncodeToPNG(); }
        bool WantCam() { return true; }
#endif
        public byte[] LastFrameJpg() { return camJpg; }

        public void BeginSighting()
        {
            lock (detLock) bestLife = null;
        }

        public Det BestLifeDetection()
        {
            lock (detLock) return bestLife;
        }

        public List<Det> LatestDetections()
        {
            lock (detLock) return new List<Det>(latest);
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (camRT != null && now >= nextCam && WantCam())
            {
                nextCam = now + 0.25f;
                if (camTex == null) camTex = new Texture2D(camRT.width, camRT.height, TextureFormat.RGB24, false);
                var prev = RenderTexture.active;
                RenderTexture.active = camRT;
                camTex.ReadPixels(new Rect(0, 0, camRT.width, camRT.height), 0, 0);
                camTex.Apply();
                RenderTexture.active = prev;
                camJpg = camTex.EncodeToJPG(82);
#if JAMRA_WEBGL
                if (JamraWebCamWanted() != 0) JamraWebPushImage("cam.jpg", camJpg, camJpg.Length, "image/jpeg");
#endif
            }
            if (game != null && now >= nextPush)
            {
                nextPush = now + 0.5f;
                liveJson = JsonUtility.ToJson(Snapshot());
#if JAMRA_WEBGL
                PushWeb();
#endif
            }
        }

        // نتائج المودل من المتصفّح (من السيرفر على الكمبيوتر، أو من SendMessage على الويب)
        void ApplyDets(string json)
        {
            var post = JsonUtility.FromJson<DetPost>(json);
            var items = post != null && post.items != null ? new List<Det>(post.items) : new List<Det>();
            lock (detLock)
            {
                latest = items;
                // أفضل كائن حيّ قرب مركز الإطار خلال نافذة الفحص الحالية
                foreach (var d in items)
                {
                    if (!Life.Contains(d.label) || d.conf < 0.25f) continue;
                    float cx = d.x + d.w * 0.5f - 0.5f, cy = d.y + d.h * 0.5f - 0.5f;
                    if (cx * cx + cy * cy > 0.16f) continue;
                    if (bestLife == null || d.conf > bestLife.conf) bestLife = d;
                }
            }
            Interlocked.Exchange(ref lastDetTicks, DateTime.UtcNow.Ticks);
        }

        // ── حالة المهمّة كما تراها اللوحة ─────────────────────
        [Serializable]
        class SightOut
        {
            public int index;
            public string label, labelAr, source, altLabel, altLabelAr;
            public float altConf;
            public float conf, distM, boxX, boxY, boxW, boxH;
            public double lat, lon;
            public bool person, hasPhoto;
        }

        [Serializable]
        class Live
        {
            public string phase;
            public float missionTime, igniteAt, fireDia, predRadiusM, predErrM, damagePct, burnAreaM2;
            public float uncheckedDia60, uncheckedPct60, windMs, windToDeg, airC, fireC;
            public double predLat, predLon, fireLat, fireLon, droneLat, droneLon;
            public bool burning, success, predOk, hasImage, modelLive, surveyDone;
            public int persons, animals, ballsLeft, balls, reportSerial, surveyIndex;
            public string message, cause, trueCause, startClock, igniteClock;
            public JamraGame.SatPrediction satA, satB;
            public GasReading gas;
            public string[] gasSensors;
            public string gasCause, gasWhy;
            public float gasMCE;
            public bool gasReady;
            public SightOut[] sightings;
            public Det[] model;
            public string[] log;
        }

        Live Snapshot()
        {
            var g = game;
            bool found = g.phase == Phase.Detected || g.phase == Phase.Resolved;
            bool done = g.phase == Phase.Resolved;
            double fla, flo, dla, dlo;
            g.ToLatLon(g.FirePos, out fla, out flo);
            g.ToLatLon(g.drone != null ? g.drone.position : Vector3.zero, out dla, out dlo);

            var sights = new List<SightOut>();
            for (int i = 0; i < g.Sightings.Count; i++)
            {
                var s = g.Sightings[i];
                if (!s.seen) continue;
                double la, lo;
                g.ToLatLon(s.pos, out la, out lo);
                sights.Add(new SightOut
                {
                    index = i, label = s.label, labelAr = JamraGame.LabelAr(s.label),
                    source = "sim", altLabel = s.altLabel, altLabelAr = JamraGame.LabelAr(s.altLabel), altConf = s.altConf,
                    conf = s.conf, distM = Vector3.Distance(s.pos, g.FirePos),
                    boxX = s.boxX, boxY = s.boxY, boxW = s.boxW, boxH = s.boxH,
                    lat = la, lon = lo, person = s.label == "person", hasPhoto = s.photo != null
                });
            }

            return new Live
            {
                phase = g.phase.ToString(),
                missionTime = g.MissionTime,
                igniteAt = g.IgniteAt,
                fireDia = g.FireDia,
                predRadiusM = g.PredRadius,
                predErrM = g.PredErrM,
                damagePct = g.DamagePct,
                burnAreaM2 = g.BurnAreaM2,
                uncheckedDia60 = g.UncheckedDia60,
                uncheckedPct60 = g.UncheckedPct60,
                windMs = g.windMs,
                windToDeg = 180f,             // الريح في المشهد تدفع على +x = نحو الجنوب
                airC = done ? g.AirC : 0f,
                fireC = done ? g.TrueFireC : 0f,
                predLat = g.PredLat, predLon = g.PredLon,
                fireLat = found ? fla : 0, fireLon = found ? flo : 0,
                droneLat = dla, droneLon = dlo,
                burning = g.MissionTime >= g.IgniteAt,
                success = g.Success,
                predOk = g.PredOk,
                hasImage = firePng != null,
                modelLive = ModelLive,
                surveyDone = g.SurveyDone,
                surveyIndex = g.SurveyIndex,
                persons = g.PersonsSeen,
                animals = g.AnimalsSeen,
                ballsLeft = g.BallsLeft,
                balls = g.balls,
                reportSerial = g.ReportSerial,
                message = g.Message,
                cause = g.CauseText,
                trueCause = done ? g.TrueCause : "",
                startClock = g.StartClock.ToString("HH:mm:ss"),
                igniteClock = g.MissionTime >= g.IgniteAt ? g.Clock(g.IgniteAt) : "",
                satA = g.SatA, satB = g.SatB,
                gas = g.GasNow,
                gasSensors = GasSensors.Models,
                gasCause = g.GasCauseIdx >= 0 ? JamraGame.Causes[g.GasCauseIdx] : "",
                gasWhy = g.GasWhy,
                gasMCE = g.GasMCE,
                gasReady = g.GasCauseIdx >= 0,
                sightings = sights.ToArray(),
                model = LatestDetections().ToArray(),
                log = g.Log.ToArray()
            };
        }

#if !JAMRA_WEBGL
        // ── السيرفر ────────────────────────────────────────
        void Serve()
        {
            while (listener != null && listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch { break; }
                // كل طلب في خيط: إرسال البريد لا يوقف تحديث اللوحة
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { Handle(ctx); }
                    catch { }
                    finally { try { ctx.Response.OutputStream.Close(); } catch { } }
                });
            }
        }

        static void Send(HttpListenerResponse res, byte[] body, string type)
        {
            res.ContentType = type;
            res.ContentLength64 = body.Length;
            res.OutputStream.Write(body, 0, body.Length);
        }

        void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            string path = req.Url.AbsolutePath;
            res.AddHeader("Cache-Control", "no-store");

            if (req.HttpMethod == "POST" && path == "/det") { PostDet(req, res); return; }
            if (req.HttpMethod == "POST" && path == "/email") { PostEmail(req, res); return; }

            if (path == "/live.json") { Send(res, Encoding.UTF8.GetBytes(liveJson), "application/json; charset=utf-8"); return; }
            if (path == "/cam.jpg")
            {
                var b = camJpg;
                if (b == null) { res.StatusCode = 404; return; }
                Send(res, b, "image/jpeg"); return;
            }
            if (path == "/fire.png")
            {
                var b = firePng;
                if (b == null) { res.StatusCode = 404; return; }
                Send(res, b, "image/png"); return;
            }
            if (path.StartsWith("/sighting/"))
            {
                int i;
                string n = Path.GetFileNameWithoutExtension(path);
                var list = game != null ? game.Sightings : null;
                if (list != null && int.TryParse(n, out i) && i >= 0 && i < list.Count && list[i].photo != null)
                { Send(res, list[i].photo, "image/jpeg"); return; }
                res.StatusCode = 404; return;
            }

            // jamra-shim.js لنسخة الويب فقط؛ هنا السيرفر الحقيقي يردّ على كل شيء
            if (path == "/jamra-shim.js") { Send(res, new byte[0], "text/javascript"); return; }

            // ملفات ثابتة (dashboard.html = اسم اللوحة في نسخة الويب)
            string rel = path == "/" || path == "/dashboard.html" ? "index.html"
                       : path == "/detect" ? "detect.html" : path.TrimStart('/');
            if (rel.Contains("..")) { res.StatusCode = 400; return; }
            string f = Path.Combine(webRoot, rel);
            if (!File.Exists(f)) f = Path.Combine(fallbackRoot, rel);
            if (!File.Exists(f)) { res.StatusCode = 404; return; }
            Send(res, File.ReadAllBytes(f), TypeOf(f));
        }

        static string TypeOf(string f)
        {
            switch (Path.GetExtension(f).ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": case ".mjs": return "text/javascript";
                case ".wasm": return "application/wasm";
                case ".json": return "application/json";
                case ".png": return "image/png";
                case ".jpg": return "image/jpeg";
                case ".css": return "text/css";
                default: return "application/octet-stream";
            }
        }

        static string ReadBody(HttpListenerRequest req)
        {
            using (var r = new StreamReader(req.InputStream, Encoding.UTF8)) return r.ReadToEnd();
        }

        void PostDet(HttpListenerRequest req, HttpListenerResponse res)
        {
            ApplyDets(ReadBody(req));
            Send(res, Encoding.UTF8.GetBytes("{\"ok\":true}"), "application/json");
        }

        // ── بريد حقيقي عبر SMTP (مثلاً Gmail مع كلمة مرور تطبيق) ──
        [Serializable]
        class EmailPost
        {
            public string host, user, pass, to, subject, body, imageB64;
            public int port;
            public bool attachFire;
            public int[] sightings;
        }

        [Serializable] class EmailResult { public bool ok; public string error; }

        void PostEmail(HttpListenerRequest req, HttpListenerResponse res)
        {
            var result = new EmailResult();
            try
            {
                var e = JsonUtility.FromJson<EmailPost>(ReadBody(req));
                using (var msg = new MailMessage(e.user, e.to, e.subject, e.body))
                using (var smtp = new SmtpClient(string.IsNullOrEmpty(e.host) ? "smtp.gmail.com" : e.host,
                                                 e.port > 0 ? e.port : 587))
                {
                    msg.BodyEncoding = Encoding.UTF8;
                    msg.SubjectEncoding = Encoding.UTF8;
                    var streams = new List<Stream>();
                    if (e.attachFire && firePng != null)
                    {
                        var ms = new MemoryStream(firePng); streams.Add(ms);
                        msg.Attachments.Add(new Attachment(ms, "fire.png", "image/png"));
                    }
                    if (!string.IsNullOrEmpty(e.imageB64))
                    {
                        var ms = new MemoryStream(Convert.FromBase64String(e.imageB64)); streams.Add(ms);
                        msg.Attachments.Add(new Attachment(ms, "camera.jpg", "image/jpeg"));
                    }
                    if (e.sightings != null && game != null)
                        foreach (int i in e.sightings)
                            if (i >= 0 && i < game.Sightings.Count && game.Sightings[i].photo != null)
                            {
                                var ms = new MemoryStream(game.Sightings[i].photo); streams.Add(ms);
                                msg.Attachments.Add(new Attachment(ms, "sighting_" + i + ".jpg", "image/jpeg"));
                            }
                    smtp.EnableSsl = true;
                    smtp.Timeout = 20000;
                    smtp.Credentials = new NetworkCredential(e.user, e.pass);
                    smtp.Send(msg);
                    foreach (var s in streams) s.Dispose();
                }
                result.ok = true;
            }
            catch (Exception ex)
            {
                result.ok = false;
                result.error = ex.Message;
            }
            Send(res, Encoding.UTF8.GetBytes(JsonUtility.ToJson(result)), "application/json; charset=utf-8");
        }
#endif
    }
}
