// Comprehensive A380 engine-start + FWS capture, run every tick by the persistent-
// socket monitor (tools/a380_engine_monitor.ps1). Reads engine L:vars/simvars AND
// the FWS core's master-caution sources + the actual presented EWD failure/abnormal
// content, so we can see WHY a master caution fires when nothing shows on the EWD.
(function () {
  var sh = document.querySelector('systems-host');
  var f = sh && sh.fwsCore;
  function sv(n, u) { try { var v = SimVar.GetSimVarValue(n, u || 'number'); return v; } catch (e) { return null; } }
  function r1(x) { return (x == null) ? null : Math.round(x * 10) / 10; }
  function g(key) {
    try {
      var s = f && f[key]; if (s == null) return null;
      var t = typeof s;
      if (t === 'boolean' || t === 'number' || t === 'string') return s; // raw value
      if (typeof s.get === 'function') return s.get();
      if ('value' in s) return s.value;
      return s;
    } catch (e) { return 'ERR'; }
  }
  // Return the KEYS/contents of a list/map subject (the failure & abnormal lists).
  function lst(key) {
    try {
      var s = f && f[key]; if (s == null) return null;
      var v = (typeof s.get === 'function') ? s.get() : (typeof s.getArray === 'function' ? s.getArray() : s);
      if (v == null) return null;
      if (Array.isArray(v)) return v;
      if (typeof v.size === 'number' && typeof v.forEach === 'function') { var o = []; v.forEach(function (val, k) { o.push(k); }); return o; }
      if (typeof v === 'object') return Object.keys(v);
      return v;
    } catch (e) { return 'ERR'; }
  }
  var o = {};
  if (!f) { o.fwsCore = false; return JSON.stringify(o); }

  // ---- per-engine: physical state (L:var/simvar) + FWS's view ----
  o.E = {};
  for (var n = 1; n <= 4; n++) {
    o.E[n] = {
      st: sv('L:A32NX_ENGINE_STATE:' + n),
      n1: r1(sv('L:A32NX_ENGINE_N1:' + n)),
      n2: r1(sv('L:A32NX_ENGINE_N2:' + n)),
      n3: r1(sv('L:A32NX_ENGINE_N3:' + n)),
      egt: Math.round(sv('L:A32NX_ENGINE_EGT:' + n) || 0),
      ff: Math.round(sv('L:A32NX_ENGINE_FF:' + n) || 0),
      ign: sv('TURB ENG IGNITION SWITCH EX1:' + n, 'enum'),
      comb: sv('GENERAL ENG COMBUSTION:' + n, 'bool'),
      strt: sv('GENERAL ENG STARTER:' + n, 'bool'),
      // FWS view
      fSt: g('engine' + n + 'State'),
      fRun: g('engine' + n + 'Running'),
      fMaster: g('engine' + n + 'Master'),
      fFail: g('eng' + n + 'Fail'),
      fOut: g('eng' + n + 'Out'),
      fShut: g('eng' + n + 'ShutDown'),
      fShutAbn: g('eng' + n + 'ShutdownAbnormalSensed'),
      fNoStart: g('eng' + n + 'NotStartingConfNode'),
      fAbnParm: g('eng' + n + 'PrimaryAbnormalParams'),
      fBleedOff: g('eng' + n + 'BleedAbnormalOff'),
      fApFault: g('eng' + n + 'APumpFault'),
      fBpFault: g('eng' + n + 'BPumpFault'),
      fPumpDisc: g('eng' + n + 'PumpDisc'),
      fAboveIdle: g('engine' + n + 'AboveIdle'),
      fCoreIdle: g('engine' + n + 'CoreAtOrAboveMinIdle')
    };
  }
  o.modeKnob = sv('L:A32NX_ENGINE_MODE', 'number');
  o.engSelPos = g('engSelectorPosition');
  o.oneEngRun = g('oneEngineRunning');
  o.allEngOff = g('allEngineSwitchOff');

  // ---- master caution / warning + every request source ----
  o.MC = g('masterCaution'); o.MCo = g('masterCautionOutput');
  o.MW = g('masterWarning'); o.MWo = g('masterWarningOutput');
  o.mcFaults = g('requestMasterCautionFromFaults');
  o.mcAbrk = g('requestMasterCautionFromABrkOff');
  o.mcAthr = g('requestMasterCautionFromAThrOff');
  o.mwFaults = g('requestMasterWarningFromFaults');
  o.mwApOff = g('requestMasterWarningFromApOff');
  o.mcLvar = sv('L:A32NX_MASTER_CAUTION', 'bool');
  o.mwLvar = sv('L:A32NX_MASTER_WARNING', 'bool');
  o.crc = g('auralCrcActive');
  o.nonCancWarn = g('nonCancellableWarningCount');

  // ---- the ACTUAL EWD content (this is the heart of it) ----
  o.presentedFailures = lst('presentedFailures');
  o.allCurrentFailures = lst('allCurrentFailures');
  o.recallFailures = lst('recallFailures');
  o.presentedAbn = lst('presentedAbnormalProceduresList');
  o.abnNonSensed = lst('activeAbnormalNonSensedKeys');
  o.clearedAbn = lst('clearedAbnormalProceduresList');
  o.deferred = lst('activeDeferredProceduresList');
  o.memos = lst('memos');
  o.inop = lst('inopSys');
  o.limits = lst('limitations');
  o.ecamNormal = g('ecamStatusNormal');
  o.phase = g('flightPhase');
  o.thrustLocked = g('engineThrustLocked');
  o.thrustLockedAbn = g('engineThrustLockedAbnormalSensed');
  o.ewdShowFailPending = g('ecamEwdShowFailurePendingIndication');

  return JSON.stringify(o);
})()
