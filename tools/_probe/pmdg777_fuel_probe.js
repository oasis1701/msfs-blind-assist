(function () {
  try {
    var modern = [];
    for (var i = 1; i <= 8; i++) {
      modern.push(Math.round(SimVar.GetSimVarValue('FUELSYSTEM TANK WEIGHT:' + i, 'pounds')));
    }
    var ppg = SimVar.GetSimVarValue('FUEL WEIGHT PER GALLON', 'pounds');
    var legacy = {
      leftMain: Math.round(SimVar.GetSimVarValue('FUEL TANK LEFT MAIN QUANTITY', 'gallons') * ppg),
      center: Math.round(SimVar.GetSimVarValue('FUEL TANK CENTER QUANTITY', 'gallons') * ppg),
      rightMain: Math.round(SimVar.GetSimVarValue('FUEL TANK RIGHT MAIN QUANTITY', 'gallons') * ppg),
      centre2: Math.round(SimVar.GetSimVarValue('FUEL TANK CENTER2 QUANTITY', 'gallons') * ppg)
    };
    var total = Math.round(SimVar.GetSimVarValue('FUEL TOTAL QUANTITY WEIGHT', 'pounds'));
    return JSON.stringify({ modern: modern, legacy: legacy, totalLbs: total, ppg: ppg });
  } catch (e) { return 'ERR ' + e; }
})()
