(function () {
  function dumpAttrs(el) {
    var a = {};
    if (!el.attributes) return a;
    for (var i = 0; i < el.attributes.length; i++) a[el.attributes[i].name] = el.attributes[i].value;
    return a;
  }
  try {
    var out = { title: document.title };
    var insts = document.querySelectorAll('wasm-instrument');
    out.instruments = [];
    for (var i = 0; i < insts.length; i++) {
      var inst = insts[i];
      var canvas = inst.querySelector('wasm-sim-canvas');
      out.instruments.push({
        instAttrs: dumpAttrs(inst),
        canvasAttrs: canvas ? dumpAttrs(canvas) : null,
        innerTextLen: (inst.innerText || '').length,
        childTags: (function () { var t = {}; var c = inst.querySelectorAll('*'); for (var j = 0; j < c.length; j++) { var n = c[j].tagName.toLowerCase(); t[n] = (t[n] || 0) + 1; } return t; })()
      });
    }
    // Does any wasm-instrument JS element expose an instrument data object?
    out.instJsKeys = [];
    for (var i = 0; i < insts.length; i++) {
      var keys = [];
      for (var k in insts[i]) {
        try { if (/instrument|fmc|cdu|data|fsInstrument|gps|flightplan|bus/i.test(k)) keys.push(k); } catch (e) {}
      }
      out.instJsKeys.push(keys.slice(0, 30));
    }
    // CommBus presence
    out.commBus = {
      RegisterCommBusListener: typeof window.RegisterCommBusListener,
      hasGetCommBus: typeof window.GetCommBus,
      coherent: typeof window.Coherent
    };
    return JSON.stringify(out);
  } catch (e) { return 'PROBE_ERR ' + e + ' @ ' + (e.stack || ''); }
})()
