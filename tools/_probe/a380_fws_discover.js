// Discovery probe: confirm the FWS handle, test SimVar access from systems-host,
// and enumerate every fwsCore field name relevant to engines/failures/cautions so
// the 10-min monitor can target real keys (field names differ between builds).
(function () {
  var out = { ok: true };
  var sh = document.querySelector('systems-host');
  out.systemsHost = !!sh;
  var f = sh && sh.fwsCore;
  out.fwsCore = !!f;

  // 1) SimVar reachability from this page
  try {
    out.simvarTest = {
      eng1State: SimVar.GetSimVarValue('L:A32NX_ENGINE_STATE:1', 'number'),
      eng1N2: SimVar.GetSimVarValue('L:A32NX_ENGINE_N2:1', 'number'),
      ign1: SimVar.GetSimVarValue('TURB ENG IGNITION SWITCH EX1:1', 'enum'),
      modeSelKnob: SimVar.GetSimVarValue('L:A32NX_ENGINE_MODE', 'number'),
      masterCautionLvar: SimVar.GetSimVarValue('L:A32NX_MASTER_CAUTION', 'bool'),
      masterWarningLvar: SimVar.GetSimVarValue('L:A32NX_MASTER_WARNING', 'bool')
    };
  } catch (e) { out.simvarTest = 'ERR ' + e; }

  // 2) Enumerate fwsCore field names by category
  if (f) {
    var cats = {
      caution: /caution/i,
      warning: /warning/i,
      master: /master/i,
      fail: /fail/i,
      abnormal: /abnormal/i,
      presented: /presented|active|recall|current/i,
      eng: /eng|n1|n2|n3|igniti|start|combust/i,
      fuel: /fuel|pump/i,
      phase: /phase/i,
      memo: /memo|status|inop|limit/i
    };
    var hits = {};
    for (var c in cats) hits[c] = [];
    for (var k in f) {
      try {
        for (var c2 in cats) { if (cats[c2].test(k)) hits[c2].push(k); }
      } catch (e) {}
    }
    out.fwsKeys = hits;

    // 3) Sample-read a few likely subjects so we know the value shape
    function rd(key) {
      try {
        var s = f[key]; if (s == null) return '<<null>>';
        if (typeof s.get === 'function') return s.get();
        if (typeof s.getArray === 'function') return s.getArray();
        if ('value' in s) return s.value;
        var t = typeof s;
        if (t === 'object') return '<<obj:' + (s.constructor && s.constructor.name) + '>>';
        return s;
      } catch (e) { return 'ERR'; }
    }
    var probe = ['masterCaution', 'masterWarning', 'fwcFlightPhase', 'flightPhase',
      'ecamStatusNormal', 'presentedAbnormalProceduresList', 'activeAbnormalNonSensedKeys',
      'presentedFailures', 'allCurrentFailures', 'recallFailures'];
    var sample = {};
    probe.forEach(function (key) { if (key in f) sample[key] = rd(key); });
    out.fwsSample = sample;
  }
  return JSON.stringify(out);
})()
