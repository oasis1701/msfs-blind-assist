// Read the A380 FWS (Flight Warning System) state directly from the systems-host
// to enumerate active failures / abnormals + the engine N values the FWS sees.
(function () {
  var sh = document.querySelector('systems-host');
  if (!sh) return JSON.stringify({ systemsHost: false });
  var f = sh.fwsCore || sh.fwsCore_ || (sh.instance && sh.instance.fwsCore);
  if (!f) {
    // list candidate props so we can find the FWS handle
    var props = [];
    for (var k in sh) { try { if (/fws|fwc|core/i.test(k)) props.push(k); } catch (e) {} }
    return JSON.stringify({ systemsHost: true, fwsCore: false, candidateProps: props });
  }
  function val(s) {
    try {
      if (s == null) return null;
      if (typeof s.get === 'function') return s.get();
      if (typeof s.getArray === 'function') return s.getArray();
      if ('value' in s) return s.value;
      return s;
    } catch (e) { return 'ERR'; }
  }
  // gather fwsCore property names mentioning the systems we care about
  var names = [];
  for (var k in f) { try { if (/phase|inop|abnormal|status|limit|memo|warning|caution|eng|n1|n2|fuel/i.test(k)) names.push(k); } catch (e) {} }
  var out = { systemsHost: true, fwsCore: true, relevantProps: names };
  // try the documented Subjects
  ['fwcFlightPhase', 'flightPhase', 'ecamStatusNormal', 'presentedAbnormalProceduresList',
   'activeAbnormalNonSensedKeys', 'inopSysAllPhasesKeys', 'limitationsAllPhasesKeys',
   'masterWarning', 'masterCaution'].forEach(function (key) {
    if (key in f) { var v = val(f[key]); out[key] = (v && v.length !== undefined && typeof v !== 'string') ? v : v; }
  });
  return JSON.stringify(out);
})()
