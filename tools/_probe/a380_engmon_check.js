(function () {
  if (!window.__MON) return 'NO_MON';
  var m = window.__MON;
  return JSON.stringify({
    alive: !!m.timer, stopped: m.stopped, logLen: m.log.length, specs: m.specs,
    sample: {
      mode: m.live['L:XMLVAR_ENG_MODE_SEL'],
      state1: m.live['L:A32NX_ENGINE_STATE:1'], state3: m.live['L:A32NX_ENGINE_STATE:3'],
      n2_1: m.live['TURB ENG N2:1'], n2_3: m.live['TURB ENG N2:3'],
      comb1: m.live['GENERAL ENG COMBUSTION:1'], comb3: m.live['GENERAL ENG COMBUSTION:3'],
      fadec1: m.live['L:A32NX_OVHD_FADEC_1'], fadec3: m.live['L:A32NX_OVHD_FADEC_3'],
      ign1: m.live['TURB ENG IGNITION SWITCH EX1:1'], ign3: m.live['TURB ENG IGNITION SWITCH EX1:3']
    }
  });
})()
