// Live A380 engine-start variable monitor. Polls a wide engine/ignition/fuel net
// every 250ms and logs every CHANGE (timestamped) into window.__MON.log, which
// survives the inspector socket closing. Auto-stops after 6 min. Read the dump
// later with: JSON.stringify(window.__MON.log).
(function () {
  if (window.__MON && window.__MON.timer) { try { clearInterval(window.__MON.timer); } catch (e) {} }

  var L = [];                 // L:vars (unit 'number')
  var n;
  // Global mode selector + ignition
  L.push('L:XMLVAR_ENG_MODE_SEL');
  L.push('L:A32NX_ENGINE_IGNITION');
  for (n = 1; n <= 4; n++) {
    L.push('L:A32NX_ENGINE_STATE:' + n);
    L.push('L:A32NX_ENGINE_N1:' + n);
    L.push('L:A32NX_ENGINE_N2:' + n);
    L.push('L:A32NX_ENGINE_N3:' + n);
    L.push('L:A32NX_ENGINE_EGT:' + n);
    L.push('L:A32NX_ENGINE_FF:' + n);
    L.push('L:A32NX_ENGINE_TIMER:' + n);
    L.push('L:A32NX_OVHD_FADEC_' + n);
    L.push('L:A32NX_FADEC_IGNITER_A_ACTIVE_ENG' + n);
    L.push('L:A32NX_FADEC_IGNITER_B_ACTIVE_ENG' + n);
    L.push('L:A32NX_ENGINE_IGNITER_' + n);
    L.push('L:A32NX_START_VALVE_' + n + '_OPEN');
    L.push('L:A32NX_PNEU_ENG_' + n + '_STARTER_VALVE_OPEN');
    L.push('L:A32NX_ENGMANSTART' + n + '_TOGGLE');
    L.push('L:A32NX_ENGINE_MASTER_' + n);
  }
  L.push('L:A32NX_ENGMANSTARTALTN_TOGGLE');

  // Stock A:vars with explicit units
  var A = [];
  for (n = 1; n <= 4; n++) {
    A.push(['TURB ENG IGNITION SWITCH EX1:' + n, 'enum']);
    A.push(['GENERAL ENG STARTER:' + n, 'bool']);
    A.push(['GENERAL ENG STARTER ACTIVE:' + n, 'bool']);
    A.push(['GENERAL ENG COMBUSTION:' + n, 'bool']);
    A.push(['GENERAL ENG FUEL VALVE:' + n, 'bool']);
    A.push(['GENERAL ENG FUEL PRESSURE:' + n, 'psi']);
    A.push(['TURB ENG N1:' + n, 'percent']);
    A.push(['TURB ENG N2:' + n, 'percent']);
    A.push(['TURB ENG IGNITION SWITCH:' + n, 'bool']);
    A.push(['TURB ENG IS IGNITING:' + n, 'bool']);
    A.push(['ENG COMBUSTION:' + n, 'bool']);
    A.push(['FUELSYSTEM VALVE SWITCH:' + n, 'bool']);
    A.push(['FUELSYSTEM VALVE OPEN:' + n, 'percent']);
    A.push(['GENERAL ENG MASTER ALTERNATOR:' + n, 'bool']);
  }

  var specs = [];
  var i;
  for (i = 0; i < L.length; i++) specs.push({ k: L[i], u: 'number' });
  for (i = 0; i < A.length; i++) specs.push({ k: A[i][0], u: A[i][1] });

  var last = {}, log = [], live = {};
  var t0 = Date.now();

  function rnd(k, v) {
    if (typeof v !== 'number') return v;
    // Coarse rounding to cut ramp noise while keeping the trend visible.
    if (/_N1:|_N2:|_N3:|TURB ENG N1|TURB ENG N2/.test(k)) return Math.round(v);
    if (/_EGT:/.test(k)) return Math.round(v);
    if (/_FF:|FUEL PRESSURE/.test(k)) return Math.round(v);
    if (/VALVE OPEN/.test(k)) return Math.round(v);
    return Math.round(v * 1000) / 1000;
  }

  function tick() {
    var ts = ((Date.now() - t0) / 1000).toFixed(1);
    for (var s = 0; s < specs.length; s++) {
      var v;
      try { v = SimVar.GetSimVarValue(specs[s].k, specs[s].u); } catch (e) { continue; }
      if (v === null || v === undefined) continue;
      var key = specs[s].k;
      var rv = rnd(key, v);
      live[key] = rv;
      if (last[key] === undefined) { last[key] = rv; continue; }   // silent baseline
      if (last[key] !== rv) {
        log.push(ts + '\t' + key + '\t' + last[key] + ' -> ' + rv);
        last[key] = rv;
        if (log.length > 12000) log.shift();
      }
    }
    if (Date.now() - t0 > 360000) { try { clearInterval(window.__MON.timer); } catch (e) {} window.__MON.stopped = true; }
  }

  window.__MON = { log: log, last: last, live: live, t0: t0, specs: specs.length, timer: setInterval(tick, 250), stopped: false };
  tick();  // establish baseline now
  return 'MON_STARTED specs=' + specs.length;
})()
