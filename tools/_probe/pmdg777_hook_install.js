(function () {
  try {
    if (window.__pmdgCommLog) return 'ALREADY_INSTALLED count=' + window.__pmdgCommLog.length;
    window.__pmdgCommLog = [];
    var L = window.RegisterCommBusListener(function () {});
    var proto = Object.getPrototypeOf(L);
    var hooked = false;
    if (proto && typeof proto.onEventToAllSubscribers === 'function') {
      var orig = proto.onEventToAllSubscribers;
      proto.onEventToAllSubscribers = function (name, data) {
        try { if (window.__pmdgCommLog.length < 2000) window.__pmdgCommLog.push({ n: '' + name, d: ('' + data).slice(0, 160) }); } catch (e) {}
        return orig.apply(this, arguments);
      };
      hooked = true;
    }
    // Also wrap CheckCoherentEvent (the lower dispatch) to widen the net
    var hooked2 = false;
    if (proto && typeof proto.CheckCoherentEvent === 'function') {
      var orig2 = proto.CheckCoherentEvent;
      proto.CheckCoherentEvent = function (name) {
        try { if (window.__pmdgCommLog.length < 2000) window.__pmdgCommLog.push({ cc: '' + name }); } catch (e) {}
        return orig2.apply(this, arguments);
      };
      hooked2 = true;
    }
    return 'INSTALLED hooked=' + hooked + ' cc=' + hooked2;
  } catch (e) { return 'PROBE_ERR ' + e; }
})()
