// جَمرة — طرف اللوحة وصفحة الكشف في نسخة الويب.
// على الكمبيوتر يخدم يونتي هذه الصفحات من سيرفره (localhost:8090) ويردّ على
// /live.json و /cam.jpg و /det ... — وهذا الملف هناك فارغ.
// في نسخة الويب لا سيرفر: نعترض fetch وصور <img> ونأخذ البيانات من تبويب
// اللعبة عبر BroadcastChannel، بلا تعديل على منطق الصفحتين.
// البريد على الويب: EmailJS يرسل من Gmail الفريق (مربوط بإذن Google في لوحة
// EmailJS — لا كلمة مرور في الصفحة). القيم الثلاث من لوحة EmailJS، وكلها عامة
// بطبيعتها (Public Key مصمَّم ليكون في المتصفّح).
window.JAMRA_WEB = {
  sender: 'jamrainfo0@gmail.com',
  emailjs: { service: '', template: '', publicKey: '' }
};

(function () {
  if (!('BroadcastChannel' in window)) return;
  const ch = new BroadcastChannel('jamra');
  const S = { live: null, liveAt: 0, blobs: {}, urls: {} };

  ch.onmessage = e => {
    const m = e.data || {};
    if (m.t === 'live') { S.live = m.json; S.liveAt = Date.now(); }
    else if (m.t === 'img') {
      if (S.urls[m.key]) URL.revokeObjectURL(S.urls[m.key]);
      S.blobs[m.key] = m.blob;
      S.urls[m.key] = URL.createObjectURL(m.blob);
    }
  };
  ch.postMessage({ t: 'hello' });   // اطلب الصور الموجودة إن كانت اللعبة تعمل

  const ROUTES = /\/(live\.json|cam\.jpg|fire\.png|sighting\/\d+\.jpg|det|email)$/;
  function route(u) {
    try { const m = new URL(u, location.href).pathname.match(ROUTES); return m ? m[1] : null; }
    catch (e) { return null; }
  }
  const json = (o, status) => new Response(JSON.stringify(o),
    { status: status || 200, headers: { 'Content-Type': 'application/json' } });

  const realFetch = window.fetch.bind(window);
  window.fetch = async function (input, init) {
    const k = route(typeof input === 'string' ? input : input && input.url);
    if (!k) return realFetch(input, init);
    if (k === 'live.json') {
      // مثل السيرفر المتوقّف: الصفحة تعرض «المحاكاة متوقّفة»
      if (!S.live || Date.now() - S.liveAt > 3000) throw new TypeError('JAMRA: تبويب اللعبة غير مفتوح');
      return new Response(S.live, { headers: { 'Content-Type': 'application/json' } });
    }
    if (k === 'det') { ch.postMessage({ t: 'det', json: init && typeof init.body === 'string' ? init.body : '' }); return json({ ok: true }); }
    if (k === 'email') return json(await sendEmail(init && init.body));
    if (k === 'cam.jpg') ch.postMessage({ t: 'wantCam' });
    const b = S.blobs[k];
    return b ? new Response(b, { headers: { 'Content-Type': b.type } }) : new Response(null, { status: 404 });
  };

  // نفس طلب /email الذي يفهمه سيرفر يونتي ← EmailJS
  // مرفق واحد (حدّ الحجم في الخطة المجانية): صورة الكاميرا، أو لقطة الشخص، أو صورة الحريق
  const asDataUrl = b => new Promise(r => { const f = new FileReader(); f.onload = () => r(f.result); f.onerror = () => r(''); f.readAsDataURL(b); });
  async function sendEmail(raw) {
    const cfg = window.JAMRA_WEB.emailjs;
    if (!cfg.service || !cfg.template || !cfg.publicKey)
      return { ok: false, error: 'البريد غير مفعّل بعد في نسخة الويب (إعداد EmailJS ناقص).' };
    let e = {};
    try { e = JSON.parse(raw || '{}'); } catch (x) {}
    let image = e.imageB64 ? 'data:image/jpeg;base64,' + e.imageB64 : '';
    if (!image && e.sightings && e.sightings.length && S.blobs['sighting/' + e.sightings[0] + '.jpg'])
      image = await asDataUrl(S.blobs['sighting/' + e.sightings[0] + '.jpg']);
    if (!image && e.attachFire && S.blobs['fire.png']) image = await asDataUrl(S.blobs['fire.png']);
    try {
      const r = await realFetch('https://api.emailjs.com/api/v1.0/email/send', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ service_id: cfg.service, template_id: cfg.template, user_id: cfg.publicKey,
          template_params: { to_email: e.to || window.JAMRA_WEB.sender, subject: e.subject || 'جمرة', message: e.body || '', image } })
      });
      return r.ok ? { ok: true } : { ok: false, error: 'EmailJS: ' + (await r.text()) };
    } catch (x) { return { ok: false, error: x.message }; }
  }

  // <img src="/fire.png?..."> و "/sighting/N.jpg?..." ← رابط blob من اللعبة
  const d = Object.getOwnPropertyDescriptor(HTMLImageElement.prototype, 'src');
  Object.defineProperty(HTMLImageElement.prototype, 'src', {
    configurable: true, enumerable: d.enumerable,
    get() { return d.get.call(this); },
    set(v) {
      const k = typeof v === 'string' && v ? route(v) : null;
      d.set.call(this, k && S.urls[k] ? S.urls[k] : v);
    }
  });
})();
