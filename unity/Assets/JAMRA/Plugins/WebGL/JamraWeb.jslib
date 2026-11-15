// جَمرة — جسر نسخة الويب: يونتي ← JavaScript
// يستدعيها WebBridge.cs في بناء WebGL فقط. window.JamraHost معرَّف في
// Assets/WebGLTemplates/Jamra/jamra-host.js، وهو يوزّع على اللوحة وصفحة الكشف.
mergeInto(LibraryManager.library, {
  JamraWebPushLive: function (json) {
    if (window.JamraHost) window.JamraHost.live(UTF8ToString(json));
  },
  JamraWebPushImage: function (key, ptr, len, type) {
    if (!window.JamraHost) return;
    // slice ينسخ البايتات خارج ذاكرة يونتي قبل أن تتغيّر
    window.JamraHost.image(UTF8ToString(key), HEAPU8.slice(ptr, ptr + len), UTF8ToString(type));
  },
  JamraWebCamWanted: function () {
    return window.JamraHost && window.JamraHost.camWanted() ? 1 : 0;
  },
  // إعدادات الأداء (WebTuning.cs) من window.JAMRA_TUNING في صفحة اللعبة
  JamraWebTuning: function () {
    var s = JSON.stringify(window.JAMRA_TUNING || {});
    var n = lengthBytesUTF8(s) + 1;
    var b = _malloc(n);
    stringToUTF8(s, b, n);
    return b;
  }
});
