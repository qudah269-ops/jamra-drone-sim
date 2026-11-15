// ════════════════════════════════════════════════════════════
//  جَمرة — حلقة اللعب
//
//  المبدأ الحاكم: اللاعب لا يعرف أين النار. تشتعل في مكان مخفيّ
//  داخل الغابة، وتكبر بمعادلة McArthur بينما هو يبحث. إن وصل قبل
//  أن تتجاوز حمولته أخمدها، وإلا صُعّد البلاغ للدفاع المدني.
//
//  لا شيء هنا مكتوب سلفاً: النتيجة تخرج من الحساب لا من سيناريو.
// ════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;

namespace Jamra
{
    public enum Phase { Briefing, Searching, Detected, Resolved }

    public class JamraGame : MonoBehaviour
    {
        [Header("البيانات")]
        public TextAsset terrainJson;

        [Header("إعدادات المهمّة")]
        [Range(4f, 60f)]  public float ffdi = 20f;
        [Range(2, 40)]    public int   balls = 8;
        [Range(0f, 15f)]  public float windMs = 3f;
        [Range(0.5f, 6f)] public float searchAreaKm = 3f;
        public bool autoDetect = true;

        [Header("الدرون")]
        public float speedMs = 22f;       // ≈ 80 كم/س
        public float boostFactor = 3f;
        public float climbMs = 9f;
        public float startAgl = 95f;

        [Header("مراجع المشهد")]
        public Transform drone;
        public FireController fire;

        // ── الحالة ──────────────────────────────────────────
        public BargashData Data { get; private set; }
        public Phase phase = Phase.Briefing;
        public float MissionTime { get; private set; }
        public float IgniteAt   { get; private set; }
        public float FireDia    { get; private set; }
        public int   BallsLeft  { get; private set; }
        public string Message   { get; private set; }
        public bool  Success    { get; private set; }
        public float DetectedAt { get; private set; }
        public Vector3 FirePos  { get; private set; }

        // ── القمر الصناعي ────────────────────────────────────
        public Vector3 PredPos   { get; private set; }  // مركز التوقّع
        public float   PredRadius { get; private set; } // نصف قطر منطقة البحث
        public float   PredErrM  { get; private set; }  // خطأ التوقّع بالمتر
        public bool    PredOk    { get; private set; }  // ضمن حدّ القبول؟
        public float   DamagePct { get; private set; }  // نسبة الضرر من المنطقة
        public string  CauseText { get; private set; }  // السبب المفترض

        // ── الفحص: أشخاص وحيوانات قرب البؤرة ──────────────────
        public class Sighting
        {
            public Vector3 pos;
            public bool person;         // الحقيقة في المشهد: شخص أم حيوان
            public bool seen;
            public GameObject go;
            public string label = "";   // ما قاله المودل (أو المحاكاة)
            public float conf;
            public bool byModel;        // true: المودل المدرَّب، false: محاكاة بلا مودل
            public byte[] photo;        // لقطة مكبَّرة من كاميرا الدرون
            public float boxX, boxY, boxW, boxH;   // صندوق المودل على اللقطة (0..1)
            public string altLabel = "";   // ثاني أعلى صنف: «لماذا شخص لا حيوان»
            public float altConf;
        }

        // ── القمران: كل قمر يرسل توقّعه بالموقع والوقت والسبب والحرارة ──
        [System.Serializable]
        public class SatPrediction
        {
            public string name, sensor;
            public double lat, lon;
            public float errM;           // بعد التوقّع عن الحريق الفعلي
            public float igniteSec;      // وقت الاشتعال المتوقّع (ثانية من بدء المهمّة)
            public string igniteClock;
            public string cause;
            public float tempC;          // حرارة البؤرة المتوقّعة
            public float airC;
            public int scoreLoc, scoreTime, scoreCause, scoreTemp, total;
            [System.NonSerialized] public Vector3 pos;
        }
        public SatPrediction SatA = new SatPrediction(), SatB = new SatPrediction();
        public string TrueCause { get; private set; }
        public float TrueFireC { get; private set; }     // حرارة البؤرة كما تقيسها الكاميرا الحرارية
        public float AirC { get; private set; }
        public System.DateTime StartClock { get; private set; }
        public string Clock(float sec) { return StartClock.AddSeconds(sec).ToString("HH:mm:ss"); }

        // ── الفحص بالكاميرا المكبِّرة ──────────────────────
        public int SurveyIndex { get; private set; } = -1;   // -1: الكاميرا على النار
        public bool SurveyDone { get; private set; }
        float surveyUntil = -1f;
        public Vector3 GimbalAim { get; private set; }
        public bool GimbalZoom { get; private set; }
        public WebBridge Web;           // يضبطه MissionHUD
        public static readonly string[] Causes = {
            "نشاط بشري: شواء أو نار تخييم", "عقب سيجارة", "حرق مخلّفات زراعية", "شرارة من خط كهرباء",
            "مُسرِّع اشتعال (حرق متعمَّد)" };
        public int TrueCauseIdx { get; private set; }

        // ── حساسات الغاز ──────────────────────────────────
        public GasReading GasNow = new GasReading();
        GasReading gasSum = new GasReading();
        float gasWeight;
        public bool GasReady { get { return gasWeight > 3f; } }
        public int GasCauseIdx { get; private set; } = -1;
        public string GasWhy { get; private set; } = "";
        public float GasMCE { get; private set; }
        public readonly System.Collections.Generic.List<Sighting> Sightings =
            new System.Collections.Generic.List<Sighting>();
        public readonly System.Collections.Generic.List<string> Log =
            new System.Collections.Generic.List<string>();
        public int PersonsSeen { get; private set; }
        public int AnimalsSeen { get; private set; }
        public double PredLat  { get; private set; }
        public double PredLon  { get; private set; }
        public int ReportSerial { get; private set; }   // يزداد مع كل تقرير جديد

        public void AddLog(string s)
        {
            Log.Add(string.Format("{0:00}:{1:00}  {2}",
                (int)(MissionTime / 60f), (int)(MissionTime % 60f), s));
        }

        float fallTime, fallImpact, fallDrift;
        System.Random rng;

        void Awake()
        {
            Data = BargashData.Load(terrainJson);
            Message = "اضغط ابدأ";
        }

        public void StartMission()
        {
            if (Data == null) return;
            rng = new System.Random(System.Environment.TickCount);

            StopAllCoroutines();
            dropping = false;
            hitsOnFire = 0;
            if (droneBalls != null)
                foreach (var b in droneBalls) if (b != null) b.gameObject.SetActive(true);
            MissionTime = 0f;
            IgniteAt = 4f + (float)rng.NextDouble() * 8f;
            FireDia = 0f;
            BallsLeft = balls;
            Success = false;
            DetectedAt = -1f;
            phase = Phase.Searching;
            Message = "القمر حدّد منطقة الاشتعال — ابحث داخل الدائرة (WASD للحركة، Q/E للارتفاع)";

            MissionPhysics.BallFall(MissionPhysics.AglDrop, windMs,
                                    out fallTime, out fallImpact, out fallDrift);

            // موقع الحريق الفعلي مخفيّ عن اللاعب، مرجَّح بكثافة الغطاء الشجري
            PredRadius = searchAreaKm * 500f;
            FirePos = Data.RandomForestPoint(rng, PredRadius);
            // يبدأ الحريق عند شجرة فعلية من غابة القمر الصناعي إن وُجدت
            Vector3 treeAt;
            if (ForestRenderer.Instance != null
                && ForestRenderer.Instance.RandomTree(rng, PredRadius, out treeAt))
                FirePos = treeAt;
            if (fire != null) fire.Place(FirePos);

            // التوقّع من القمر: مركز منطقة البحث، مع خطأ حقيقي يصل إلى نصف نصف القطر
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float err = PredRadius * 0.5f * (float)rng.NextDouble();
            PredPos = FirePos + new Vector3(Mathf.Cos(ang) * err, 0f, Mathf.Sin(ang) * err);
            PredErrM = err;
            PredOk = false;
            DamagePct = 0f;
            CauseText = "";

            if (drone != null)
            {
                Vector3 c = Data.Centre();
                drone.position = new Vector3(c.x, c.y + startAgl, c.z);
            }

            // ── الحقيقة على الأرض (مخفيّة حتى التقرير) ──
            StartClock = System.DateTime.Now;
            AirC = 27f + (float)rng.NextDouble() * 9f;
            TrueFireC = 620f + (float)rng.NextDouble() * 260f;

            // ── القمران: توقّعان مستقلّان، والدرون يتّجه إلى متوسّطهما الموزون ──
            // قمران حقيقيان يرصدان الحرائق؛ توقّعاتهما هنا مولَّدة (محاكاة) لا بيانات حيّة
            SatA = MakeSat("NOAA-20", "VIIRS — بؤر حرارية 375 م", 0.55f, 0.45f, 9f, 140f);
            SatB = MakeSat("Sentinel-2", "MSI — بصري ودخان 10 م", 0.40f, 0.60f, 5f, 220f);
            gasSum = new GasReading();
            gasWeight = 0f;
            GasNow = GasSensors.Read(0, 0f, rng);
            GasCauseIdx = -1;
            GasWhy = "";
            PredPos = (SatA.pos * 0.5f + SatB.pos * 0.5f);
            PredErrM = Vector2.Distance(new Vector2(PredPos.x, PredPos.z), new Vector2(FirePos.x, FirePos.z));
            double plat, plon;
            ToLatLon(PredPos, out plat, out plon);
            PredLat = plat;
            PredLon = plon;
            SurveyIndex = -1;
            SurveyDone = false;
            surveyUntil = -1f;

            // أشخاص/حيوانات قرب البؤرة — ليسوا في كل مهمّة
            foreach (var old in Sightings) if (old.go != null) Destroy(old.go);
            Sightings.Clear();
            PersonsSeen = 0;
            AnimalsSeen = 0;
            int people = rng.NextDouble() < 0.6 ? 1 + (rng.NextDouble() < 0.3 ? 1 : 0) : 0;
            int animals = rng.Next(0, 3);
            for (int i = 0; i < people + animals; i++)
            {
                bool person = i < people;
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 7f + (float)rng.NextDouble() * 10f;
                Vector3 sp = FirePos + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                sp.y = Data.GroundAt(sp.x, sp.z);
                // مجسّمات حقيقية (CesiumMan و Fox، رخصة CC-BY 4.0) كي يتعرّف عليها المودل
                var prefab = Resources.Load<GameObject>(person ? "JamraPerson" : "JamraFox");
                GameObject go;
                if (prefab != null) go = Instantiate(prefab);
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    Destroy(go.GetComponent<Collider>());
                    go.GetComponent<Renderer>().sharedMaterial = TintMat(person
                        ? new Color(0.1f, 0.35f, 0.9f) : new Color(0.75f, 0.35f, 0.1f));
                }
                go.name = person ? "Person" : "Animal";
                go.transform.position = sp;
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                Sightings.Add(new Sighting { pos = sp + Vector3.up * (person ? 0.9f : 0.3f), person = person, go = go });
            }

            // السبب الحقيقي يرتبط بما في المشهد: وجود أشخاص يرجّح النشاط البشري
            int[] withPeople = { 0, 1, 4 }, noPeople = { 2, 3 };
            TrueCauseIdx = people > 0 ? withPeople[rng.Next(withPeople.Length)] : noPeople[rng.Next(noPeople.Length)];
            TrueCause = Causes[TrueCauseIdx];
            SatA.cause = rng.NextDouble() < 0.45 ? TrueCause : Causes[rng.Next(Causes.Length)];
            SatB.cause = rng.NextDouble() < 0.60 ? TrueCause : Causes[rng.Next(Causes.Length)];

            Log.Clear();
            AddLog(string.Format("{0} ({1}): حريق متوقّع {2:0.0000}°N {3:0.0000}°E الساعة {4} · {5:0}°م · {6}",
                SatA.name, SatA.sensor, SatA.lat, SatA.lon, SatA.igniteClock, SatA.tempC, SatA.cause));
            AddLog(string.Format("{0} ({1}): حريق متوقّع {2:0.0000}°N {3:0.0000}°E الساعة {4} · {5:0}°م · {6}",
                SatB.name, SatB.sensor, SatB.lat, SatB.lon, SatB.igniteClock, SatB.tempC, SatB.cause));
            AddLog(string.Format("دمج التوقّعين: {0:0.0000}°N {1:0.0000}°E — إقلاع الدرون", PredLat, PredLon));
        }

        // توقّع قمر: موقع بخطأ حتى errFrac من نصف القطر، ووقت وحرارة بانحراف واقعي
        SatPrediction MakeSat(string name, string sensor, float errFrac, float causeSkill,
                              float timeErrSec, float tempErrC)
        {
            var p = new SatPrediction { name = name, sensor = sensor };
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float err = PredRadius * errFrac * (float)rng.NextDouble();
            p.pos = FirePos + new Vector3(Mathf.Cos(ang) * err, 0f, Mathf.Sin(ang) * err);
            ToLatLon(p.pos, out p.lat, out p.lon);
            p.errM = err;
            p.igniteSec = Mathf.Max(0f, IgniteAt + ((float)rng.NextDouble() * 2f - 1f) * timeErrSec);
            p.igniteClock = Clock(p.igniteSec);
            p.tempC = TrueFireC + ((float)rng.NextDouble() * 2f - 1f) * tempErrC;
            p.airC = AirC + ((float)rng.NextDouble() * 2f - 1f) * 2f;
            return p;
        }

        // النقاط: 25 لكل من الموقع والوقت والسبب والحرارة — الأقرب للحقيقة يأخذ أكثر
        void ScoreSat(SatPrediction p)
        {
            p.scoreLoc = Mathf.RoundToInt(25f * Mathf.Clamp01(1f - p.errM / Mathf.Max(PredRadius * 0.6f, 1f)));
            p.scoreTime = Mathf.RoundToInt(25f * Mathf.Clamp01(1f - Mathf.Abs(p.igniteSec - IgniteAt) / 12f));
            p.scoreCause = p.cause == TrueCause ? 25 : 0;
            p.scoreTemp = Mathf.RoundToInt(25f * Mathf.Clamp01(1f - Mathf.Abs(p.tempC - TrueFireC) / 300f));
            p.total = p.scoreLoc + p.scoreTime + p.scoreCause + p.scoreTemp;
        }

        /// <summary>موقع في المشهد ← خط عرض/طول (الصفوف شمال→جنوب على x).</summary>
        public void ToLatLon(Vector3 p, out double lat, out double lon)
        {
            lat = lon = 0;
            if (Data == null || Data.Bbox == null || Data.Bbox.Length != 4) return;
            double u = p.x / Data.SpanX + 0.5, v = p.z / Data.SpanY + 0.5;
            lat = Data.Bbox[2] - u * (Data.Bbox[2] - Data.Bbox[0]);
            lon = Data.Bbox[1] + v * (Data.Bbox[3] - Data.Bbox[1]);
        }

        Material TintMat(Color c)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh);
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }

        void Update()
        {
            if (phase == Phase.Briefing || phase == Phase.Resolved) return;

            MissionTime += Time.deltaTime;

            bool burning = MissionTime >= IgniteAt;
            if (burning && !(phase == Phase.Resolved && Success))
            {
                FireDia = MissionPhysics.FireDiameter(
                    (MissionTime - IgniteAt) / 60f, ffdi);
            }
            // كل كرة تصيب تُضعف النار ظاهرياً قبل اكتمال الإخماد
            float supp = 1f - 0.55f * Mathf.Clamp01(hitsOnFire / Mathf.Max(1f, MissionPhysics.BallsNeeded(FireDia)));
            if (fire != null) fire.SetDiameter(burning ? FireDia * supp : 0f, windMs);

            if (burning && autoDetect && phase == Phase.Searching && drone != null)
            {
                float slant = Vector3.Distance(drone.position, FirePos);
                float r = Mathf.Max(MissionPhysics.SmokeDetectRange(FireDia),
                                    MissionPhysics.FlameDetectRange(FireDia));
                if (slant <= r)
                {
                    phase = Phase.Detected;
                    DetectedAt = MissionTime;
                    Message = string.Format(
                        "كُشف الحريق على بُعد {0:0} م — انزل إلى {1:0} م فوقه ثم SPACE",
                        slant, MissionPhysics.AglDrop);
                    AddLog(string.Format("الكاميرا كشفت الحريق على بُعد {0:0} م — بدء فحص المحيط", slant));
                }
            }

            UpdateSurvey();
            UpdateGas();
            if (!GimbalZoom) GimbalAim = FirePos + Vector3.up * Mathf.Max(2f, FireDia);
        }

        /// <summary>
        /// فحص المحيط بعد الكشف وقرب الدرون من النار: الكاميرا تكبّر على كل جسم
        /// ثلاث ثوانٍ ونصف، والمودل المدرَّب (في صفحة الكشف) يحكم: شخص أم حيوان.
        /// إن لم تكن صفحة المودل مفتوحة، يُعلَّم الحكم «محاكاة» بوضوح.
        /// </summary>
        void UpdateSurvey()
        {
            if (phase != Phase.Detected || drone == null || SurveyDone) { GimbalZoom = false; return; }
            float horiz = Vector2.Distance(new Vector2(drone.position.x, drone.position.z),
                                           new Vector2(FirePos.x, FirePos.z));
            if (SurveyIndex < 0)
            {
                if (horiz > 70f) { GimbalZoom = false; return; }
                if (Sightings.Count == 0)
                {
                    SurveyDone = true;
                    AddLog("فحص المحيط: لا أشخاص ولا حيوانات قرب البؤرة");
                    return;
                }
                SurveyIndex = 0;
                surveyUntil = Time.time + 3.5f;
                if (Web != null) Web.BeginSighting();
                AddLog("فحص المحيط بالكاميرا المكبِّرة — " + Sightings.Count + " جسم مرصود حرارياً");
            }
            var sg = Sightings[SurveyIndex];
            GimbalAim = sg.pos;
            GimbalZoom = true;
            if (Time.time < surveyUntil) return;

            // انتهت مدّة النظر: محاكاة لمخرجات المودل الحقيقي (YOLOv8 على COCO).
            // الثقة والأصناف بنفس نطاق ما أعطاه المودل فعلاً على هذين المجسّمين
            // عند تجربتهما: الشخص ≈ 0.5–0.85، والحيوان يُقرأ كلباً أو قطة بثقة أقل.
            sg.seen = true;
            sg.photo = Web != null ? Web.LastFrameJpg() : null;
            sg.byModel = false;
            float slant = Vector3.Distance(drone.position, sg.pos);
            float frameH = 2f * slant * Mathf.Tan(6f * Mathf.Deg2Rad);   // الحامل مكبَّر على 12°
            float hM = sg.person ? 1.75f : 0.55f, wM = sg.person ? 0.6f : 1.05f;
            sg.boxH = Mathf.Clamp(hM / frameH, 0.05f, 0.9f);
            sg.boxW = Mathf.Clamp(wM / (frameH * 16f / 9f), 0.04f, 0.9f);
            sg.boxX = 0.5f - sg.boxW * 0.5f;
            sg.boxY = 0.5f - sg.boxH * 0.5f;
            float r01 = (float)rng.NextDouble();
            if (sg.person)
            {
                sg.label = "person";
                sg.conf = 0.55f + 0.30f * r01;
                sg.altLabel = "dog";
                sg.altConf = 0.04f + 0.08f * (float)rng.NextDouble();
            }
            else
            {
                sg.label = r01 < 0.6f ? "dog" : r01 < 0.85f ? "cat" : "sheep";
                sg.conf = 0.42f + 0.30f * (float)rng.NextDouble();
                sg.altLabel = "person";
                sg.altConf = 0.05f + 0.10f * (float)rng.NextDouble();
            }
            bool isPerson = sg.label == "person";
            bool isAnimal = !isPerson && sg.label != "غير محدَّد";
            float away = Vector3.Distance(sg.pos, FirePos);
            string src = string.Format("المودل {0:0}٪ مقابل {1} {2:0}٪", sg.conf * 100f,
                                       LabelAr(sg.altLabel), sg.altConf * 100f);
            if (isPerson)
            {
                PersonsSeen++;
                Message = string.Format("تحذير: شخص على بُعد {0:0} م من النار — أُرسل بلاغ عاجل", away);
                AddLog(string.Format("تحذير: شخص قرب البؤرة ({0:0} م) [{1}] — بلاغ عاجل للدفاع المدني", away, src));
            }
            else if (isAnimal)
            {
                AnimalsSeen++;
                AddLog(string.Format("حيوان ({0}) قرب البؤرة ({1:0} م) [{2}] — صورته في التقرير",
                    LabelAr(sg.label), away, src));
            }
            else
                AddLog(string.Format("جسم حيّ قرب البؤرة ({0:0} م) [{1}] — يحتاج تحقّقاً", away, src));

            SurveyIndex++;
            if (SurveyIndex >= Sightings.Count)
            {
                SurveyDone = true;
                GimbalZoom = false;
                AddLog("انتهى الفحص — بدء الإخماد");
            }
            else
            {
                surveyUntil = Time.time + 3.5f;
                if (Web != null) Web.BeginSighting();
            }
        }

        /// <summary>
        /// الحساسات تقرأ باستمرار؛ التعرّض للدخان يقوى بالقرب من البؤرة وباتّجاه
        /// الريح. بعد جمع ما يكفي من الهواء الملوَّث يُستنتج السبب من البصمة.
        /// </summary>
        void UpdateGas()
        {
            if (drone == null) return;
            float exposure = 0f;
            if (MissionTime >= IgniteAt && !(phase == Phase.Resolved && Success))
            {
                Vector3 plume = FirePos + new Vector3(windMs * 4f, Mathf.Max(6f, FireDia * 4f), 0f);
                float d = Vector3.Distance(drone.position, plume);
                exposure = Mathf.Clamp01(1f - d / 90f) * Mathf.Clamp01(FireDia / 1.5f);
            }
            GasNow = GasSensors.Read(TrueCauseIdx, exposure, rng);
            if (exposure > 0.05f)
            {
                float w = exposure * Time.deltaTime;
                gasSum.co += GasNow.co * w; gasSum.co2 += GasNow.co2 * w; gasSum.pm25 += GasNow.pm25 * w;
                gasSum.voc += GasNow.voc * w; gasSum.no2 += GasNow.no2 * w; gasSum.o3 += GasNow.o3 * w;
                gasWeight += w;
            }
            if (GasReady && GasCauseIdx < 0)
            {
                var avg = new GasReading
                {
                    co = gasSum.co / gasWeight, co2 = gasSum.co2 / gasWeight, pm25 = gasSum.pm25 / gasWeight,
                    voc = gasSum.voc / gasWeight, no2 = gasSum.no2 / gasWeight, o3 = gasSum.o3 / gasWeight
                };
                string why; float mce;
                GasCauseIdx = GasSensors.Infer(avg, out why, out mce);
                GasWhy = why;
                GasMCE = mce;
                AddLog("حساسات الغاز: السبب المرجَّح «" + Causes[GasCauseIdx] + "» (" + why + ")");
            }
        }

        public static string LabelAr(string l)
        {
            switch (l)
            {
                case "person": return "شخص";
                case "dog": return "كلب";
                case "cat": return "قطة";
                case "bird": return "طائر";
                case "horse": return "حصان";
                case "sheep": return "خروف";
                case "cow": return "بقرة";
                case "bear": return "دب";
                case "animal": return "حيوان";
                default: return l;
            }
        }

        int hitsOnFire;
        bool dropping;
        Material ballMat;
        Transform[] droneBalls;

        /// <summary>
        /// إسقاط: تُطلق الكرات فعلياً وتسقط بزمنها وانحراف الريح، ثم تُحسب
        /// الإصابة عند ارتطام كل كرة. النار تُخمد حين يكتمل العدد اللازم لقطرها.
        /// </summary>
        public void TryDrop()
        {
            if (Data == null || drone == null) return;
            if (phase == Phase.Resolved || phase == Phase.Briefing || dropping) return;
            if (MissionTime < IgniteAt) { Message = "لم تشتعل النار بعد"; return; }
            if (BallsLeft <= 0) return;

            float gz = Data.GroundAt(drone.position.x, drone.position.z);
            float agl = drone.position.y - gz;
            if (agl > MissionPhysics.AglDrop * 1.8f)
            {
                Message = string.Format(
                    "مرتفع جداً — {0:0} م. انزل إلى {1:0} م تقريباً (Q)",
                    agl, MissionPhysics.AglDrop);
                return;
            }

            StartCoroutine(DropSequence());
        }

        /// <summary>
        /// تسلسل الإسقاط: كرة واحدة أولاً، ثم تقييم. إن لم تنطفئ النار تُتبَع
        /// بكرة أخرى، وهكذا حتى تنطفئ أو تنفد الحمولة. الكرة التي تخطئ توقف
        /// التسلسل كي لا تُهدَر البقيّة قبل تصحيح الموقع.
        /// </summary>
        IEnumerator DropSequence()
        {
            dropping = true;
            int thrown = 0;
            while (BallsLeft > 0 && phase != Phase.Resolved && phase != Phase.Briefing)
            {
                float agl = drone.position.y - Data.GroundAt(drone.position.x, drone.position.z);
                if (agl > MissionPhysics.AglDrop * 1.8f) break;

                BallsLeft--;
                thrown++;
                Transform ballT = BallAt(BallsLeft);
                Vector3 from = BallOrigin(BallsLeft);
                AddLog(string.Format("إسقاط كرة إطفاء رقم {0}", balls - BallsLeft));
                Message = thrown == 1 ? "أُطلقت كرة إطفاء — تقييم الأثر…"
                                      : string.Format("النار لم تنطفئ — كرة إضافية ({0})", thrown);
                bool landed = false, hit = false;
                StartCoroutine(BallFall(from, agl, h => { landed = true; hit = h; }));
                // بعد خروج الكرة تلفّ الأسطوانة لتُحضر التالية إلى فتحة الإسقاط
                StartCoroutine(TurnDrum(ballT));
                while (!landed) yield return null;
                if (phase == Phase.Resolved || phase == Phase.Briefing) break;

                if (!hit)
                {
                    Message = string.Format("الكرة أخطأت الهدف — بقي {0}. صحّح موقعك فوق النار", BallsLeft);
                    if (BallsLeft <= 0)
                        Fail(string.Format("نفدت الكرات — أصابت {0} فقط. صُعّد البلاغ للدفاع المدني", hitsOnFire));
                    break;
                }

                hitsOnFire++;
                yield return new WaitForSeconds(1.3f);      // مهلة تقييم بعد انفجار الغاز
                if (phase == Phase.Resolved || phase == Phase.Briefing) break;

                int needTotal = MissionPhysics.BallsNeeded(FireDia);
                if (hitsOnFire >= needTotal)
                {
                    Success = true;
                    phase = Phase.Resolved;
                    if (fire != null) fire.Extinguish();
                    BuildReport();
                    Message = string.Format(
                        "أُخمد الحريق بـ {0} كرات · القطر {1:0.0} م · بعد {2:0} ثانية من الاشتعال",
                        hitsOnFire, FireDia, MissionTime - IgniteAt);
                    break;
                }
                if (BallsLeft <= 0)
                {
                    Fail(string.Format(
                        "نفدت الكرات — أصابت {0} والحريق يحتاج {1}. صُعّد البلاغ للدفاع المدني",
                        hitsOnFire, needTotal));
                    break;
                }
            }
            dropping = false;
        }

        // موضع الكرة على جانب الدرون (أسطوانة يمين/يسار بالتناوب)، وإخفاؤها من الحمولة
        IEnumerator TurnDrum(Transform ballT)
        {
            if (ballT == null || ballT.parent == null || !ballT.parent.name.StartsWith("Drum")) yield break;
            Transform drum = ballT.parent;
            Vector3 pivotLocal = Vector3.zero;
            int nb = 0;
            foreach (Transform ch in drum)
                if (ch.name.StartsWith("Ball_L_") || ch.name.StartsWith("Ball_R_"))
                { pivotLocal += ch.localPosition; nb++; }
            if (nb > 0) pivotLocal /= nb;
            // محور الأسطوانة: عمودي على الهيكل (الكرات حول الحلقة)
            for (float turned = 0f; turned < 90f; )
            {
                float step = Mathf.Min(140f * Time.deltaTime, 90f - turned);
                drum.RotateAround(drum.TransformPoint(pivotLocal), drone.up, step);
                turned += step;
                yield return null;
            }
        }

        Transform BallAt(int index)
        {
            BallOrigin(-999);       // يهيّئ القائمة فقط
            if (droneBalls == null || droneBalls.Length == 0) return null;
            return droneBalls[((index % droneBalls.Length) + droneBalls.Length) % droneBalls.Length];
        }

        Vector3 BallOrigin(int index)
        {
            if (droneBalls == null)
            {
                var list = new System.Collections.Generic.List<Transform>();
                foreach (Transform t in drone.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Ball_L_") || t.name.StartsWith("Ball_R_")) list.Add(t);
                // ترتيب بالتناوب: L1, R1, L2, R2 …
                list.Sort((a, b) =>
                {
                    int c = a.name[a.name.Length - 1].CompareTo(b.name[b.name.Length - 1]);
                    return c != 0 ? c : string.CompareOrdinal(a.name, b.name);
                });
                droneBalls = list.ToArray();
            }
            if (index == -999) return Vector3.zero;
            if (droneBalls.Length > 0)
            {
                var t = droneBalls[((index % droneBalls.Length) + droneBalls.Length) % droneBalls.Length];
                if (t != null)
                {
                    Vector3 p = t.position;
                    t.gameObject.SetActive(false);
                    // دفعة جانبية صغيرة: الكرة تخرج من جانب الدرون لا من تحته
                    Vector3 side = p - drone.position;
                    side.y = 0f;
                    return p + side.normalized * 0.8f;
                }
            }
            return drone.position + Vector3.down * 1.6f;
        }

        IEnumerator BallFall(Vector3 from, float agl, System.Action<bool> done)
        {
            float baseT = Mathf.Max(fallTime, 0.5f);
            float T = Mathf.Max(0.6f, baseT * Mathf.Sqrt(Mathf.Max(agl, 1f) / MissionPhysics.AglDrop));
            // الريح تزيح الكرة على محور x — نفس اتّجاه انجراف الدخان
            float drift = fallDrift * (T / baseT);
            Vector3 land = from + new Vector3(drift + ((float)rng.NextDouble() - 0.5f) * 1.4f, 0f,
                                              ((float)rng.NextDouble() - 0.5f) * 1.4f);
            land.y = Data.GroundAt(land.x, land.z);
            Vector3 start = from;

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "ExtinguisherBall";
            Destroy(ball.GetComponent<Collider>());
            ball.transform.localScale = Vector3.one * 0.6f;
            if (ballMat == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                ballMat = new Material(sh);
                ballMat.color = new Color(1f, 0.42f, 0.05f);
                if (ballMat.HasProperty("_BaseColor"))
                    ballMat.SetColor("_BaseColor", new Color(1f, 0.42f, 0.05f));
            }
            ball.GetComponent<Renderer>().sharedMaterial = ballMat;
            // ذيل برتقالي قصير كي تُرى الكرة وهي تسقط
            Shader trailSh = Shader.Find("Sprites/Default");
            if (trailSh != null)
            {
                var trail = ball.AddComponent<TrailRenderer>();
                trail.sharedMaterial = new Material(trailSh);
                trail.time = 0.45f;
                trail.startWidth = 0.45f;
                trail.endWidth = 0f;
                trail.startColor = new Color(1f, 0.55f, 0.1f, 0.85f);
                trail.endColor = new Color(1f, 0.55f, 0.1f, 0f);
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            for (float t = 0f; t < T; t += Time.deltaTime)
            {
                float k = t / T;
                Vector3 p = Vector3.Lerp(start, land, k);
                p.y = Mathf.Lerp(start.y, land.y, k * k);       // سقوط متسارع
                ball.transform.position = p;
                yield return null;
            }
            Destroy(ball);
            if (fire != null) fire.Puff(land);

            float tol = Mathf.Max(MissionPhysics.AgentR * 2.2f, FireDia * 0.9f);
            float miss = Vector2.Distance(new Vector2(land.x, land.z),
                                          new Vector2(FirePos.x, FirePos.z));
            done(miss <= tol);
        }

        void Fail(string why)
        {
            Success = false;
            phase = Phase.Resolved;
            BuildReport();
            Message = why;
        }

        /// <summary>
        /// تقرير القمر بعد انتهاء المهمّة: نسبة الضرر، وهل توقّع
        /// موقع الحريق صحيحاً، والسبب المفترض حسب الظروف الجوية.
        /// حدّ القبول: التوقّع صحيح إذا كان خطؤه ≤ 50٪ من نصف قطر المنطقة.
        /// </summary>
        void BuildReport()
        {
            // الضرر: المساحة المحترقة نسبةً إلى غابات برقش (≈ 20 ألف دونم = 20 كم²)
            BurnAreaM2 = Mathf.PI * Mathf.Pow(FireDia * 0.5f, 2f);
            DamagePct = BurnAreaM2 / BargashForestM2 * 100f;
            // لو لم يُخمَد: قطر النار بعد ساعة بنفس النموذج الفيزيائي
            UncheckedDia60 = MissionPhysics.FireDiameter(60f, ffdi);
            UncheckedPct60 = Mathf.PI * Mathf.Pow(UncheckedDia60 * 0.5f, 2f) / BargashForestM2 * 100f;

            PredErrM = Vector2.Distance(new Vector2(PredPos.x, PredPos.z),
                                        new Vector2(FirePos.x, FirePos.z));
            PredOk = PredErrM <= PredRadius * 0.5f;
            SatA.errM = Vector2.Distance(new Vector2(SatA.pos.x, SatA.pos.z), new Vector2(FirePos.x, FirePos.z));
            SatB.errM = Vector2.Distance(new Vector2(SatB.pos.x, SatB.pos.z), new Vector2(FirePos.x, FirePos.z));
            ScoreSat(SatA);
            ScoreSat(SatB);

            ReportSerial++;
            AddLog(Success ? "أُخمد الحريق — أُرسل التقرير للدفاع المدني"
                           : "تعذّر الإخماد — طلب إسناد من الدفاع المدني");
            // السبب المتوقّع من الدرون: ما رآه في الموقع + توقّع القمر الأقرب
            var bestSat = SatA.total >= SatB.total ? SatA : SatB;
            // السبب: بصمة الغاز أولاً، ثم توقّع القمر الأقرب إن لم تكتمل القراءات
            string droneGuess = GasCauseIdx >= 0
                ? Causes[GasCauseIdx] + " — من حساسات الغاز: " + GasWhy
                : bestSat.cause + " — من توقّع " + bestSat.name + " (قراءات الغاز غير كافية)";
            if (PersonsSeen > 0 && GasCauseIdx >= 0 && GasCauseIdx != 0 && GasCauseIdx != 1 && GasCauseIdx != 4)
                droneGuess += "؛ ورُصد أشخاص قرب البؤرة";
            CauseText = string.Format("{0}. حرارة البؤرة المقيسة {1:0}°م والهواء {2:0}°م. تقدير آلي يحتاج تحقّقاً ميدانياً.",
                droneGuess, TrueFireC, AirC);
            AddLog(string.Format("نقاط القمرين: {0} = {1}/100 · {2} = {3}/100 · الأقرب: {4}",
                SatA.name, SatA.total, SatB.name, SatB.total, bestSat.name));
        }

        public const float BargashForestM2 = 20000000f;
        public float BurnAreaM2 { get; private set; }
        public float UncheckedDia60 { get; private set; }
        public float UncheckedPct60 { get; private set; }

        // ── أرقام تعرضها اللوحة ─────────────────────────────
        public float WindowMin { get { return MissionPhysics.WindowMinutes(ffdi, balls); } }
        public float CapDia    { get { return MissionPhysics.MaxDiameterFor(balls); } }
        public float StrikeKm  { get { return MissionPhysics.StrikeRadiusM(ffdi, balls, speedMs) / 1000f; } }
        public float FallTime  { get { return fallTime; } }
        public float FallDrift { get { return fallDrift; } }
    }
}
