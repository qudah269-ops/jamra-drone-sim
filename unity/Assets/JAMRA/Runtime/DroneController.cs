// ════════════════════════════════════════════════════════════
//  جَمرة — قيادة الدرون وكاميراته
//  الحركة نسبةً إلى اتّجاه نظر الكاميرا لا إلى محاور العالم:
//  الطيّار يدفع «للأمام» بما يراه، لا بما تقوله المحاور.
// ════════════════════════════════════════════════════════════

using UnityEngine;

namespace Jamra
{
    public enum CamMode { Chase, Fpv, Top }

    public class DroneController : MonoBehaviour
    {
        public JamraGame game;
        public Camera cam;
        public Transform[] rotors;

        [Header("حدود الارتفاع فوق الأرض")]
        public float minAgl = 12f;
        public float maxAgl = 400f;

        public CamMode mode = CamMode.Chase;
        public bool autopilot;          // الدرون يطير وحده إلى النار ويُسقط
        float searchT, nextDrop;
        Light beacon;
        Renderer beaconDot;
        float yaw;
        Vector3 vel;

        void Start()
        {
            if (cam == null) cam = Camera.main;
            yaw = transform.eulerAngles.y;
            // الأسطوانتان على الجانبين (يمين ويسار الكاميرا) والشعار باتّجاه المطاردة
            Transform model = transform.Find("JAMRA_Hexacopter");
            if (model != null) model.localRotation = Quaternion.Euler(0f, 90f, 0f) * model.localRotation;
            SetupRotors();
            SetupLights();
        }

        Vector3[] rotorPivot;       // مركز كل مروحة بإحداثيات الدرون المحلّية
        Transform[] rotorDisc;

        // مركز الدوران يُحسب من هندسة المروحة نفسها (المحور + الشفرتان) لا من
        // أصل الكائن: أصول FBX المستوردة من Blender ليست دائماً عند المحور.
        void SetupRotors()
        {
            if (rotors == null) return;
            rotorPivot = new Vector3[rotors.Length];
            rotorDisc = new Transform[rotors.Length];
            Shader sh = Shader.Find("Sprites/Default");
            Material discMat = sh != null ? new Material(sh) : null;
            if (discMat != null) discMat.color = new Color(0.08f, 0.08f, 0.09f, 0.22f);

            for (int i = 0; i < rotors.Length; i++)
            {
                if (rotors[i] == null) continue;
                var rs = rotors[i].GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) { rotorPivot[i] = transform.InverseTransformPoint(rotors[i].position); continue; }
                Bounds b = rs[0].bounds;
                for (int k = 1; k < rs.Length; k++) b.Encapsulate(rs[k].bounds);
                rotorPivot[i] = transform.InverseTransformPoint(b.center);

                if (discMat == null) continue;
                // قرص شفّاف: المروحة السريعة تُرى هالةً لا شفرتين
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "RotorBlur" + i;
                Destroy(disc.GetComponent<Collider>());
                disc.GetComponent<Renderer>().sharedMaterial = discMat;
                disc.GetComponent<Renderer>().shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                float dia = Mathf.Max(b.size.x, b.size.z);
                disc.transform.position = b.center;
                disc.transform.rotation = transform.rotation;
                disc.transform.localScale = new Vector3(dia, 0.004f, dia);
                disc.transform.SetParent(transform, true);
                rotorDisc[i] = disc.transform;
            }
        }

        void Update()
        {
            if (game == null || game.Data == null) return;

            bool live = game.phase == Phase.Searching || game.phase == Phase.Detected;
            if (live) Move();
            SpinRotors(live);
            Aim();

            if (Input.GetKeyDown(KeyCode.Space)) game.TryDrop();
            if (Input.GetKeyDown(KeyCode.T)) autopilot = !autopilot;
            if (game.phase == Phase.Briefing) searchT = 0f;
            if (beacon != null)
            {
                // ضوء أحمر واحد أعلى الدرون: يشتغل ويطفي كل نصف ثانية
                bool flash = Time.time % 1.0f < 0.5f;
                beacon.intensity = flash ? 2.5f : 0f;
                if (beaconDot != null) beaconDot.enabled = flash;
            }
            if (Input.GetKeyDown(KeyCode.Tab))
                mode = (CamMode)(((int)mode + 1) % 3);
        }

        void Move()
        {
            float boost = (Input.GetKey(KeyCode.LeftShift) ||
                           Input.GetKey(KeyCode.RightShift)) ? game.boostFactor : 1f;

            // يمين/يسار يلفّان الدرون، أمام/خلف يدفعانه
            float turn = (Input.GetKey(KeyCode.D) ? 1f : 0f)
                       - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float fwd = (Input.GetKey(KeyCode.W) ? 1f : 0f)
                      - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float up  = (Input.GetKey(KeyCode.E) ? 1f : 0f)
                      - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            // أي ضغطة قيادة تعيد التحكّم للطيّار
            if (autopilot && (turn != 0f || fwd != 0f || up != 0f)) autopilot = false;
            if (autopilot) AutoInputs(out turn, out fwd, out up, out boost);
            yaw += turn * 70f * Time.deltaTime;

            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 want = rot * Vector3.forward * fwd * game.speedMs * boost;
            want.y = up * game.climbMs * boost;

            // تنعيم: المروحيّة لها كتلة، ولا تنطلق وتقف فوراً
            vel = Vector3.Lerp(vel, want, 1f - Mathf.Exp(-3.2f * Time.deltaTime));
            Vector3 p = transform.position + vel * Time.deltaTime;

            float gz = game.Data.GroundAt(p.x, p.z);
            p.y = Mathf.Clamp(p.y, gz + minAgl, gz + maxAgl);
            transform.position = p;

            // ميل بصري باتّجاه الحركة — يقرأ كطيران لا كانزلاق
            Vector3 local = Quaternion.Inverse(rot) * vel;
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                rot * Quaternion.Euler(Mathf.Clamp(local.z, -14f, 14f), 0f,
                                       Mathf.Clamp(-local.x, -14f, 14f)),
                1f - Mathf.Exp(-6f * Time.deltaTime));
        }

        void SpinRotors(bool live)
        {
            if (rotors == null) return;
            if (rotorPivot == null) return;
            float w = live ? 2900f : 300f;
            for (int i = 0; i < rotors.Length; i++)
            {
                if (rotors[i] == null) continue;
                float dir = (i % 2 == 0) ? 1f : -1f;
                rotors[i].RotateAround(transform.TransformPoint(rotorPivot[i]), transform.up,
                                       w * dir * Time.deltaTime);
                if (rotorDisc[i] != null) rotorDisc[i].gameObject.SetActive(live);
            }
        }

        /// <summary>
        /// الطيّار الآلي: قبل الكشف يمسح حلزونياً حول توقّع القمر، وبعده يتّجه
        /// إلى نقطة الإسقاط (معوَّضةً بانحراف الريح) وينزل إلى ارتفاع الإسقاط ثم يُسقط.
        /// </summary>
        void AutoInputs(out float turn, out float fwd, out float up, out float boost)
        {
            Vector3 p = transform.position;
            float agl = p.y - game.Data.GroundAt(p.x, p.z);
            Vector3 target;
            float wantAgl;
            bool strike = game.phase == Phase.Detected;
            if (strike)
            {
                target = game.FirePos - new Vector3(game.FallDrift, 0f, 0f);
                wantAgl = MissionPhysics.AglDrop;
            }
            else
            {
                float R = Mathf.Min(game.PredRadius, 30f + searchT * 14f);
                float a = searchT * 0.30f;
                target = game.PredPos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * R;
                wantAgl = game.startAgl;
            }
            float dx = target.x - p.x, dz = target.z - p.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (!strike && dist < 90f) searchT += Time.deltaTime;

            float heading = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(yaw, heading);
            turn = Mathf.Clamp(delta / 22f, -1f, 1f);
            float align = Mathf.Abs(delta) < 50f ? 1f : 0.15f;
            fwd = dist > 1.5f ? Mathf.Clamp(dist / 30f, 0.12f, 1f) * align : 0f;
            up = Mathf.Clamp((wantAgl - agl) / 6f, -1f, 1f);
            boost = dist > 160f ? game.boostFactor : 1f;

            // لا إسقاط قبل انتهاء فحص الأشخاص والحيوانات
            if (strike && game.SurveyDone && dist < 3.5f && Mathf.Abs(agl - wantAgl) < wantAgl * 0.45f
                && Time.time > nextDrop)
            {
                game.TryDrop();
                nextDrop = Time.time + 1.2f;
            }
        }

        // أضواء الملاحة على الأذرع، ووميض تحذيري فوق الهيكل
        void SetupLights()
        {
            float top = transform.position.y;
            foreach (var r in GetComponentsInChildren<Renderer>())
                if (!r.name.StartsWith("RotorBlur")) top = Mathf.Max(top, r.bounds.max.y);

            var go = new GameObject("Beacon");
            go.transform.SetParent(transform, true);
            go.transform.position = new Vector3(transform.position.x, top + 0.25f, transform.position.z);
            beacon = go.AddComponent<Light>();
            beacon.type = LightType.Point;
            beacon.color = new Color(1f, 0f, 0f);
            beacon.range = 8f;

            Shader sh = Shader.Find("Sprites/Default");
            if (sh != null)
            {
                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = "BeaconLamp";
                Destroy(dot.GetComponent<Collider>());
                dot.transform.position = go.transform.position;
                dot.transform.localScale = Vector3.one * 0.42f;
                dot.transform.SetParent(go.transform, true);
                var m = new Material(sh);
                m.color = new Color(1f, 0f, 0f);
                beaconDot = dot.GetComponent<Renderer>();
                beaconDot.sharedMaterial = m;
                beaconDot.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        void Aim()
        {
            if (cam == null) return;
            Vector3 p = transform.position;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            if (mode == CamMode.Chase)
            {
                Vector3 want = p + rot * new Vector3(0f, 5.5f, -15f);
                cam.transform.position = Vector3.Lerp(
                    cam.transform.position, want,
                    1f - Mathf.Exp(-5f * Time.deltaTime));
                cam.transform.rotation = Quaternion.Slerp(
                    cam.transform.rotation,
                    Quaternion.LookRotation(p + Vector3.up * 2f - cam.transform.position),
                    1f - Mathf.Exp(-6f * Time.deltaTime));
            }
            else if (mode == CamMode.Fpv)
            {
                // عين الدرون: ميل 28° للأسفل كما في الهيكل الحقيقي
                cam.transform.position = p + rot * new Vector3(0f, -0.25f, 0.30f);
                cam.transform.rotation = rot * Quaternion.Euler(28f, 0f, 0f);
            }
            else
            {
                cam.transform.position = p + new Vector3(0f, 120f, -0.01f);
                cam.transform.rotation = Quaternion.Euler(89f, yaw, 0f);
            }
        }
    }
}
