(function () {
  try {
    var out = { pumps: [], junctionsSample: [], trimLvars: [] };
    for (var i = 1; i <= 24; i++) {
      var on = SimVar.GetSimVarValue('FUELSYSTEM PUMP ACTIVE:' + i, 'Bool');
      var sw = SimVar.GetSimVarValue('FUELSYSTEM PUMP SWITCH:' + i, 'Bool');
      if (on || sw) out.pumps.push(i + ':on=' + on + ',sw=' + sw);
    }
    var openValves = [];
    for (var v = 1; v <= 60; v++) {
      var vo = SimVar.GetSimVarValue('FUELSYSTEM VALVE OPEN:' + v, 'percent over 100');
      if (vo > 0.01) openValves.push(v + ':' + Math.round(vo * 100));
    }
    out.openValves = openValves;
    var names = ['A32NX_FUEL_TRIM_XFR', 'A32NX_FQMS_TRIM_TRANSFER', 'A32NX_FUEL_TRANSFER_ACTIVE'];
    for (var n = 0; n < names.length; n++) {
      out.trimLvars.push(names[n] + '=' + SimVar.GetSimVarValue('L:' + names[n], 'number'));
    }
    return JSON.stringify(out);
  } catch (e) { return 'ERR ' + e; }
})()
