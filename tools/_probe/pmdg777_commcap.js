(function () {
  try {
    var log = [];
    var L = window.RegisterCommBusListener(function () {});
    var proto = Object.getPrototypeOf(L);
    // Wrap the dispatch entry so EVERY incoming CommBus event (to any listener
    // sharing this prototype) is logged by name + a short data sample.
    var hooked = false;
    if (proto && typeof proto.onEventToAllSubscribers === 'function') {
      var orig = proto.onEventToAllSubscribers;
      proto.onEventToAllSubscribers = function (name, data) {
        try { log.push({ n: '' + name, d: ('' + data).slice(0, 120) }); } catch (e) {}
        return orig.apply(this, arguments);
      };
      hooked = true;
    }
    // Also hook Coherent-level events if available (raw layer)
    var coherentHook = false;
    try {
      if (window.Coherent && typeof window.Coherent.on === 'function') {
        // can't enumerate; skip blind subscription
      }
    } catch (e) {}
    return new Promise(function (resolve) {
      setTimeout(function () {
        // dedupe by name, keep first sample + count
        var byName = {};
        for (var i = 0; i < log.length; i++) {
          var n = log[i].n;
          if (!byName[n]) byName[n] = { count: 0, sample: log[i].d };
          byName[n].count++;
        }
        resolve(JSON.stringify({ hooked: hooked, totalEvents: log.length, distinct: byName }));
      }, 3500);
    });
  } catch (e) { return 'PROBE_ERR ' + e + ' @ ' + (e.stack || ''); }
})()
