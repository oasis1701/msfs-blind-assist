(function () {
  try {
    var out = { title: document.title };
    // 1. JS data objects on the Fenix instrument host elements?
    var hosts = document.querySelectorAll('fenix-wasm-instrument, live-view-instrument, wasm-instrument');
    out.hostJs = [];
    for (var i = 0; i < hosts.length; i++) {
      var keys = [];
      for (var k in hosts[i]) {
        try { if (/fenix|instrument|data|fsInstrument|gauge|display|bus|model|readout|fmc|mcdu|ecam|state/i.test(k)) keys.push(k); } catch (e) {}
      }
      out.hostJs.push({ tag: hosts[i].tagName.toLowerCase(), gauge: (hosts[i].getAttribute('url') || '').replace(/^.*wasm_gauge=/, '').replace(/^.*type=/, 'live:'), keys: keys.slice(0, 25) });
    }
    // 2. Non-standard window globals (objects) that could be a Fenix data hub
    var STD = /^(diffAndSet|on[a-z]|SetB|Set[A-Z]|Register|is[A-Z]|set[A-Z]|update[A-Z]|window|find|openDatabase|g_|globalInstrument|KeyNav|OnData|BackgroundSlider)/;
    out.windowObjs = Object.keys(window).filter(function (k) {
      try { var v = window[k]; return (typeof v === 'object' && v !== null && !STD.test(k) && k.length > 2); } catch (e) { return false; }
    }).slice(0, 60);
    // 3. specifically look for a Fenix/EFB data global
    out.fenixHits = Object.keys(window).filter(function (k) { return /fenix|fnx|efb|aircraft|simvar|lvar|datastore|comm/i.test(k); }).slice(0, 40);
    return JSON.stringify(out);
  } catch (e) { return 'PROBE_ERR ' + e + ' @ ' + (e.stack || ''); }
})()
