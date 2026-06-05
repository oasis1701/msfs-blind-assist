(function () {
  var sh = document.querySelector('systems-host');
  var f = sh && sh.fwsCore;
  if (!f) return JSON.stringify({ fwsCore: false });
  function v(key) {
    try {
      var s = f[key]; if (s == null) return null;
      var raw = (typeof s.get === 'function') ? s.get() : ('value' in s ? s.value : s);
      if (raw == null) return null;
      var t = typeof raw;
      if (t === 'boolean' || t === 'number' || t === 'string') return raw;
      if (Array.isArray(raw)) return 'arr[' + raw.length + ']';
      return t; // object etc — just report the type
    } catch (e) { return 'ERR'; }
  }
  var keys = [
    'masterCaution', 'masterWarning',
    'requestMasterCautionFromFaults', 'requestMasterCautionFromABrkOff', 'requestMasterCautionFromAThrOff',
    'requestMasterWarningFromFaults',
    'oneEngineRunning', 'allEnginesFailure', 'allEngineSwitchOff',
    'engine1Running', 'engine2Running', 'engine3Running', 'engine4Running',
    'engine1State', 'engine2State', 'engine3State', 'engine4State',
    'N1Eng1', 'N1Eng2', 'N1Eng3', 'N1Eng4',
    'engine1AboveIdle', 'engine3AboveIdle',
    'eng1Fail', 'eng2Fail', 'eng3Fail', 'eng4Fail',
    'eng1Out', 'eng3Out', 'eng1ShutDown', 'eng3ShutDown',
    'engine1Master', 'engine3Master',
    'allFuelPumpsOff', 'rightFuelLow', 'rightFuelLowConfirm', 'fuelOnBoard',
    'eng1APumpFault', 'eng3APumpFault',
    'autoThrustEngaged', 'flightPhase'
  ];
  var out = {};
  for (var i = 0; i < keys.length; i++) out[keys[i]] = v(keys[i]);
  return JSON.stringify(out);
})()
