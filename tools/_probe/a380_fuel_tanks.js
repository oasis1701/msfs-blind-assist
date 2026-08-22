(function () {
  try {
    var names = ['', 'LeftOuter', 'Feed1', 'LeftMid', 'LeftInner', 'Feed2', 'Feed3', 'RightInner', 'RightMid', 'Feed4', 'RightOuter', 'Trim'];
    var out = [];
    var ppg = SimVar.GetSimVarValue('FUEL WEIGHT PER GALLON', 'pounds');
    for (var i = 1; i <= 11; i++) {
      var gal = SimVar.GetSimVarValue('FUELSYSTEM TANK QUANTITY:' + i, 'gallons');
      var wlb = SimVar.GetSimVarValue('FUELSYSTEM TANK WEIGHT:' + i, 'pounds');
      out.push(names[i] + ' gal=' + Math.round(gal) + ' wlb=' + Math.round(wlb));
    }
    var totalLbs = SimVar.GetSimVarValue('FUEL TOTAL QUANTITY WEIGHT', 'pounds');
    return JSON.stringify({ ppg: ppg, tanks: out, totalLbs: Math.round(totalLbs) });
  } catch (e) { return 'ERR ' + e + ' ' + (e && e.stack); }
})()
