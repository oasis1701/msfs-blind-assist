(function () {
  function shape(v, depth) {
    if (v === null) return 'null';
    var t = typeof v;
    if (t !== 'object' && t !== 'function') return t + ':' + ('' + v).slice(0, 40);
    if (t === 'function') return 'fn';
    if (Array.isArray(v)) return 'array[' + v.length + ']' + (v.length ? ' e0=' + shape(v[0], 0) : '');
    // object: list up to 30 keys with value-shape
    var keys = [];
    var n = 0;
    for (var k in v) {
      if (n++ > 40) { keys.push('...'); break; }
      try { keys.push(k + '=' + (depth > 0 ? shape(v[k], depth - 1) : (typeof v[k]))); } catch (e) { keys.push(k + '=ERR'); }
    }
    return '{' + keys.join(', ') + '}';
  }
  try {
    var out = {};
    out.g_externalVariables = (typeof window.g_externalVariables !== 'undefined') ? shape(window.g_externalVariables, 1) : 'undef';
    out.g_globalVarMgr = (typeof window.g_globalVarMgr !== 'undefined') ? shape(window.g_globalVarMgr, 1) : 'undef';
    out.globalVars = (typeof window.globalVars !== 'undefined') ? shape(window.globalVars, 1) : 'undef';
    out.globalPanelData = (typeof window.globalPanelData !== 'undefined') ? shape(window.globalPanelData, 1) : 'undef';
    try { out.dataStore = (typeof window.GetGlobalDataStore === 'function') ? shape(window.GetGlobalDataStore(), 1) : 'no-fn'; } catch (e) { out.dataStore = 'ERR ' + e; }
    return JSON.stringify(out);
  } catch (e) { return 'PROBE_ERR ' + e + ' @ ' + (e.stack || ''); }
})()
