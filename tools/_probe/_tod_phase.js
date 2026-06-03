(function () {
  var mfd = document.querySelector("a380x-mfd");
  var m = mfd.fsInstrument.fmcService.master;
  var gc = m.guidanceController;
  var pw = gc.currentPseudoWaypoints || [];
  var tdInfo = [];
  for (var p = 0; p < pw.length; p++) {
    var w = pw[p]; if (!w) continue;
    var id = ((w.ident || "") + "").toUpperCase();
    if (id.indexOf("T/D") >= 0 || id.indexOf("DECEL") >= 0 || id.indexOf("T/C") >= 0) {
      var fpi = w.flightPlanInfo;
      tdInfo.push({ ident: w.ident, hasFpi: !!fpi, fpiSecs: fpi ? fpi.secondsFromPresent : null, fpiDistAc: fpi ? fpi.distanceFromAircraft : null, distFromStart: w.distanceFromStart });
    }
  }
  var phaseVar = null, phaseObj = null;
  try { phaseVar = SimVar.GetSimVarValue('L:A32NX_FMGC_FLIGHT_PHASE', 'number'); } catch (e) {}
  try { phaseObj = (m.fmgc && m.fmgc.getFlightPhase) ? m.fmgc.getFlightPhase() : (m.flightPhaseManager ? m.flightPhaseManager.phase : "n/a"); } catch (e) { phaseObj = "err " + e.message; }
  return JSON.stringify({ phaseVar: phaseVar, phaseObj: phaseObj, tdInfo: tdInfo });
})();
