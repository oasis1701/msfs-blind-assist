(function () {
  try {
    var report = { step: 'start' };
    // 1. Broad window-global scan for anything PMDG / data / bus / tablet related
    report.windowHits = Object.keys(window).filter(function (k) {
      return /pmdg|commbus|comm_bus|tablet|eicas|alert|message|listener|databroadcast|simvarpub/i.test(k);
    });
    // 2. Introspect RegisterCommBusListener result
    report.regType = typeof window.RegisterCommBusListener;
    var captured = [];
    var listener = null;
    if (typeof window.RegisterCommBusListener === 'function') {
      try {
        listener = window.RegisterCommBusListener(function () { /* ready */ });
        report.listenerKeys = [];
        for (var k in listener) { try { report.listenerKeys.push(k + ':' + (typeof listener[k])); } catch (e) {} }
        // try to attach to a spread of plausible PMDG / generic keys
        var keys = ['PMDG_CDU_DATA', 'PMDG_DATA', 'PMDG_NG3_DATA', 'PMDG777_DATA',
          'EICAS', 'EICAS_MESSAGES', 'ALERT', 'ALERTS', 'MESSAGE',
          'PMDG_777_CDU_0', 'PMDG_777_CDU_1', 'PMDG_777_CDU_2',
          'COMM_BUS_BROADCAST', 'tablet', 'TABLET'];
        if (typeof listener.on === 'function') {
          for (var i = 0; i < keys.length; i++) {
            (function (key) {
              try { listener.on(key, function (data) { captured.push({ key: key, data: ('' + data).slice(0, 200) }); }); } catch (e) {}
            })(keys[i]);
          }
        }
      } catch (e) { report.listenerErr = '' + e; }
    }
    // 3. Coherent introspection — what events can be listened to
    report.coherent = {};
    if (typeof window.Coherent === 'object') {
      report.coherentKeys = [];
      for (var k in window.Coherent) { try { report.coherentKeys.push(k + ':' + (typeof window.Coherent[k])); } catch (e) {} }
    }
    // 4. wait ~2.5s capturing, then resolve
    return new Promise(function (resolve) {
      setTimeout(function () {
        report.captured = captured;
        report.capturedCount = captured.length;
        resolve(JSON.stringify(report));
      }, 2500);
    });
  } catch (e) { return 'PROBE_ERR ' + e + ' @ ' + (e.stack || ''); }
})()
