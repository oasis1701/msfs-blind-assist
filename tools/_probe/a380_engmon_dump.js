(function () {
  if (!window.__MON) return 'NO_MON';
  var m = window.__MON;
  // Final snapshot of the key per-engine vars for a clean 1/2 vs 3/4 comparison.
  function snap(prefix) {
    var o = {};
    var keys = Object.keys(m.live);
    for (var i = 0; i < keys.length; i++) o[keys[i]] = m.live[keys[i]];
    return o;
  }
  return JSON.stringify({
    logLen: m.log.length, stopped: m.stopped,
    finalMode: m.live['L:XMLVAR_ENG_MODE_SEL'],
    final: m.live,
    log: m.log
  });
})()
