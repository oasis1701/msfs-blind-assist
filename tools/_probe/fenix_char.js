(function () {
  function dumpAttrs(el) { var a = {}; if (!el.attributes) return a; for (var i = 0; i < el.attributes.length; i++) a[el.attributes[i].name] = el.attributes[i].value; return a; }
  try {
    var out = { title: document.title, url: location.href };
    var all = document.querySelectorAll('*');
    out.elemCount = all.length;
    out.canvases = document.querySelectorAll('canvas').length;
    var tags = {};
    for (var i = 0; i < all.length; i++) { var t = all[i].tagName.toLowerCase(); tags[t] = (tags[t] || 0) + 1; }
    var custom = {};
    for (var k in tags) { if (k.indexOf('-') >= 0) custom[k] = tags[k]; }
    out.customTags = custom;
    out.bodyTextLen = document.body ? (document.body.innerText || '').length : 0;
    out.bodyTextSample = document.body ? (document.body.innerText || '').replace(/\s+/g, ' ').slice(0, 300) : '';
    // dump any instrument-host elements + their gauge url
    out.hosts = [];
    var hostSel = 'wasm-instrument, fenix-wasm-instrument, fenix-live-view, [url*="wasm_gauge"], [url*="Instruments"]';
    var hosts = document.querySelectorAll(hostSel);
    for (var i = 0; i < hosts.length && i < 12; i++) {
      var h = hosts[i];
      out.hosts.push({ tag: h.tagName.toLowerCase(), url: (h.getAttribute && h.getAttribute('url')) || '', innerTextLen: (h.innerText || '').length });
    }
    // window globals of interest (Fenix, live view, efb)
    out.globals = Object.keys(window).filter(function (k) { return /fenix|liveview|live_view|efb|instrument|gauge|nd|pfd|ecam|mcdu|display|bus/i.test(k); }).slice(0, 50);
    return JSON.stringify(out);
  } catch (e) { return 'PROBE_ERR ' + e + ' @ ' + (e.stack || ''); }
})()
