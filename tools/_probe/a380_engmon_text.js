(function () {
  if (!window.__MON) return 'NO_MON';
  var m = window.__MON;
  var out = '=== FINAL STATE (per engine) ===\n';
  var fields = ['L:A32NX_ENGINE_STATE:', 'TURB ENG N2:', 'L:A32NX_ENGINE_N2:', 'L:A32NX_ENGINE_EGT:',
    'L:A32NX_ENGINE_FF:', 'GENERAL ENG COMBUSTION:', 'GENERAL ENG STARTER:', 'GENERAL ENG STARTER ACTIVE:',
    'GENERAL ENG FUEL VALVE:', 'FUELSYSTEM VALVE OPEN:', 'TURB ENG IGNITION SWITCH EX1:',
    'L:A32NX_FADEC_IGNITER_A_ACTIVE_ENG', 'L:A32NX_FADEC_IGNITER_B_ACTIVE_ENG', 'L:A32NX_OVHD_FADEC_'];
  out += 'mode(XMLVAR_ENG_MODE_SEL)=' + m.live['L:XMLVAR_ENG_MODE_SEL'] + '\n';
  for (var f = 0; f < fields.length; f++) {
    var row = fields[f] + '  ';
    for (var e = 1; e <= 4; e++) {
      var k = fields[f].charAt(fields[f].length - 1) === 'G' ? fields[f] + e : fields[f] + e; // ENGn vs :n both append e
      var v = m.live[k];
      row += '[' + e + ']=' + (v === undefined ? '-' : v) + '  ';
    }
    out += row + '\n';
  }
  out += '\n=== CHANGE LOG (' + m.log.length + ' entries) ===\n' + m.log.join('\n');
  return out;
})()
