// جَمرة — طرف اللعبة في نسخة الويب.
// يونتي يدفع الحالة والصور هنا (JamraWeb.jslib)، ونحن نبثّها على BroadcastChannel
// إلى أي تبويب مفتوح من نفس الموقع: dashboard.html و detect.html.
// ونتائج المودل من صفحة الكشف ترجع إلى يونتي عبر SendMessage.
window.JamraHost = (function () {
  const ch = 'BroadcastChannel' in window ? new BroadcastChannel('jamra') : null;
  const images = {};          // آخر نسخة من كل صورة (لتبويب يُفتح متأخّراً)
  let unity = null, camUntil = 0;

  if (ch) ch.onmessage = e => {
    const m = e.data || {};
    if (m.t === 'wantCam') camUntil = Date.now() + 2000;
    else if (m.t === 'hello') for (const k in images) ch.postMessage({ t: 'img', key: k, blob: images[k] });
    else if (m.t === 'det' && unity && typeof m.json === 'string') unity.SendMessage('JAMRA', 'WebDet', m.json);
  };

  return {
    attach(u) { unity = u; },
    live(json) { if (ch) ch.postMessage({ t: 'live', json }); },
    image(key, bytes, type) {
      const blob = new Blob([bytes], { type });
      if (key !== 'cam.jpg') images[key] = blob;
      if (ch) ch.postMessage({ t: 'img', key, blob });
    },
    camWanted() { return Date.now() < camUntil; }
  };
})();
