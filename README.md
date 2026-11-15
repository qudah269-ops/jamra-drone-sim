# jamra-drone-sim — firefighting drone mission over a real forest

Team TFC · NASA Space Apps 2026 · part of [JAMRA](https://jamra.tfs.ink). Try it: **https://jamra.tfs.ink/static/game/index.html**
(desktop Chrome; about 28 MB the first time).

[العربية ↓](#العربية)

## What it is
A Unity simulation of JAMRA's drone mission over **Burqush forest, Ajloun (Jordan)**, on real terrain:

1. A satellite report (VIIRS NOAA-20, Sentinel-2) gives an area where a fire probably started. The player does not
   know where it is.
2. The fire grows with the **McArthur Mk5** spread equation while the drone searches (autopilot spirals around the
   prediction, or you fly it).
3. The drone's colour and thermal cameras find it; the real fire/smoke model can judge the drone's frames in the
   camera page.
4. **Life check:** people and animals are placed around the fire. No ball is dropped while one is under the drone.
5. **Drop:** from 28 m above the canopy, corrected for wind drift. If the fire is already bigger than the 8 balls can
   cover, the report is escalated to civil defence.
6. **Gas sensors** (CO, CO₂, PM2.5, VOC, NO₂, O₃; real sensor models, simulated readings) suggest the cause.
7. Everything goes to the **reports centre** page: map, fire photos, what the camera saw.

Nothing is scripted: the outcome comes from the equations in `MissionPhysics.cs` (the same constants as JAMRA's
`mission.py`).

## Run the web build
```bash
cd web
sh get_models.sh            # once: the camera detectors and onnxruntime-web (~80 MB, not kept in git)
python3 -m http.server 8092
```
Open http://127.0.0.1:8092 in Chrome. **Start the mission**, then: `T` autopilot · `W/S` forward/back · `A/D` turn ·
`E/Q` up/down · `Shift` ×3 speed · `Tab` camera view · `Space` drop a ball. Open the reports centre beside the game,
not on top of it (browsers slow down hidden tabs).

Slow machine: `index.html?trees=40000&gimbalHz=3&post=0&shadows=0&scale=0.8`.

The hosted version on jamra.tfs.ink adds a fullscreen button and **phone control** (pair a phone with a QR code);
those need JAMRA's server, so they are not in this standalone build.

## Rebuild from Unity
Unity **6000.3.28f1** with Web Build Support. Open `unity/`, then menu **JAMRA ▸ Build Web (WebGL)**. The output goes
to `unity/Builds/WebGL` (first build ~45 min). Web performance settings live in `WebTuning.cs` and can be changed from
the URL without rebuilding.

## Inside
| | |
|---|---|
| `unity/Assets/JAMRA/Runtime/` | game loop (`JamraGame.cs`), drone flight and cameras (`DroneController.cs`), fire (`FireController.cs`), physics (`MissionPhysics.cs`), gas sensors (`GasSensors.cs`), HUD, forest renderer, web bridge |
| `unity/Assets/JAMRA/Editor/` | scene builder (terrain, forest, drone) and the web build button |
| `unity/Assets/JAMRA/Data/` | Burqush terrain, land cover, ground image, the drone model (`JamraDrone.fbx`) |
| `web/` | the built game, the reports centre (`dashboard.html`) and the camera page (`detect.html`) |

The drone's 3D design on its own, with every part explained: [jamra-drone](https://github.com/qudah269-ops/jamra-drone).

## Honest limits
- The realism factors for spotting flame and smoke (`RealFlame`, `RealSmoke`) are estimates, **not measured in the field**.
- Gas readings are generated from a per-cause fingerprint; the sensor models are real, the numbers are not measurements.
- Email from inside the web build is not set up yet (EmailJS keys are empty in `jamra-shim.js`).
- Heavy on phones; the camera page works well there.

<div dir="rtl">

## العربية

# jamra-drone-sim — مهمة درون إطفاء فوق غابة حقيقية

محاكاة Unity لمهمة درون جمرة فوق **غابة برقش بعجلون**، على تضاريس حقيقية:
1. بلاغ من الأقمار (VIIRS NOAA-20 وSentinel-2) بيعطي منطقة متوقّعة للحريق، واللاعب ما بيعرف وين بالزبط.
2. النار بتكبر بمعادلة **McArthur** وهو عم يدوّر (الطيار الآلي بيمسح حلزوني، أو بتطيّره إنت).
3. الكاميرا الملوّنة والحرارية بيلاقوها.
4. **فحص الأرواح:** في ناس وحيوانات حوالين النار، وما في إسقاط وفي حدا تحت الدرون.
5. **الإسقاط** من 28 م فوق الشجر مع تعويض الريح. إذا النار أكبر من الـ 8 كرات، البلاغ بيتصعّد للدفاع المدني.
6. **حساسات الغاز** (6 حساسات حقيقية، قراءات محاكاة) بتقرّب السبب.
7. كل إشي بيوصل لـ **مركز البلاغات**.

ما في إشي مكتوب سلفاً: النتيجة طالعة من المعادلات.

### التشغيل
```bash
cd web
sh get_models.sh
python3 -m http.server 8092
```
وافتح http://127.0.0.1:8092 بـ Chrome. النسخة على jamra.tfs.ink فيها كمان ملء الشاشة والتحكّم من التلفون.

### حدود بصراحة
- معاملات واقعية رؤية النار والدخان تقديرية، ما انقاست ميدانياً.
- قراءات الغاز مولّدة، والحساسات موديلات حقيقية.
- البريد من جوّا نسخة الويب لسّا مش مفعّل.

</div>

## License
Code and the drone model: MIT, see [LICENSE](LICENSE). Third-party assets and data keep their own terms, see
[CREDITS.md](CREDITS.md).
