(function () {
  var A = window.__MSFSBA_A380;
  var mfd = document.querySelector("a380x-mfd");
  if (!mfd || !mfd.fsInstrument || !mfd.fsInstrument.fmcService) return JSON.stringify({ err: "no fms" });
  var m = mfd.fsInstrument.fmcService.master;
  var gc = m && m.guidanceController;
  if (!gc) return JSON.stringify({ err: "no gc" });
  var pw = gc.currentPseudoWaypoints || [];
  var out = [];
  for (var p = 0; p < pw.length; p++) {
    var w = pw[p];
    if (!w) { out.push({ slot: p, nullslot: true }); continue; }
    var fpi = w.flightPlanInfo;
    out.push({
      slot: p, ident: w.ident, mcduIdent: w.mcduIdent, reason: w.reason,
      distanceFromStart: w.distanceFromStart, distanceFromAircraft: w.distanceFromAircraft,
      fpiSecs: fpi ? fpi.secondsFromPresent : null, fpiReason: fpi ? fpi.reason : null
    });
  }
  return JSON.stringify({ flightInfo: JSON.parse(A.flightInfo()), pwCount: pw.length, pw: out });
})();
