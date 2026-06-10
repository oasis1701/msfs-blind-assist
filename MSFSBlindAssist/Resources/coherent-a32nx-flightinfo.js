// FMS flight-progress for the A32NX D / Shift+D readouts. Self-contained IIFE that
// returns the SAME JSON shape as the A380 coherent-a380-agent.js flightInfo(), so
// MainForm can parse + announce both identically. Evaluated in the A32NX_MCDU Coherent
// view (where the <a32nx-mcdu> element lives) via CoherentEvalClient — no agent install.
// ES5 only (Coherent GT = Chromium 49: var, no arrow funcs, no String.includes, try/catch).
//   distToDest : NM to destination (alongTrackDistancesToDestination.get(0); works in flight)
//   distToTD/TC: NM to Top of Descent / Top of Climb from the (T/D)/(T/C) pseudo-waypoint's
//                flightPlanInfo.distanceFromAircraft (the FMS's own dist-to-go; vanishes once
//                passed). NOT (DECEL) — a separate decel point that lingers ahead.
//   timeToTD/TC: FMS time-to-go (flightPlanInfo.secondsFromPresent), accounts for the profile
//   timeToDest : FMS time-to-go to the destination (vertical-profile prediction, seconds;
//                same number the MCDU's DEST UTC is derived from; null until computed)
//   flightPhase: A32NX_FMGC_FLIGHT_PHASE (>=4 = descent/approach/... = past TOD)
(function () {
  try {
    var el = document.querySelector("a32nx-mcdu");
    var fms = el && el.fsInstrument && el.fsInstrument.legacyFms;
    var gc = fms && fms.guidanceController;
    if (!gc) return JSON.stringify({ ok: false, error: "FMS not ready" });

    var info = { ok: true, distToDest: null, distToTD: null, distToTC: null, timeToTD: null, timeToTC: null, timeToDest: null, flightPhase: null };

    var map = gc.alongTrackDistancesToDestination;
    var dtd = (map && map.get) ? map.get(0) : null;   // 0 = active plan
    if (typeof dtd === "number" && isFinite(dtd)) info.distToDest = dtd;

    // Time-to-destination (profile-aware): the same vertical-profile prediction the
    // MCDU's DEST UTC comes from. Live-verified on the A32NX:
    // vnavDriver.profileManager.mcduProfile.waypointPredictions is a Map keyed by leg
    // index; the destination leg's entry carries secondsFromPresent. Wrapped
    // defensively — a build without this structure just yields no time and the D
    // readout stays distance-only (mirrors the A380 agent's flightInfo()).
    try {
      var plan = null;
      try { plan = gc.flightPlanService.active; } catch (e) {}
      var ddi = (plan && typeof plan.destinationLegIndex === "number") ? plan.destinationLegIndex : -1;
      var pmgr = gc.vnavDriver && gc.vnavDriver.profileManager;
      var preds = pmgr && pmgr.mcduProfile && pmgr.mcduProfile.waypointPredictions;
      if (preds && preds.get && ddi >= 0) {
        var dpr = preds.get(ddi);
        if (dpr && typeof dpr.secondsFromPresent === "number" && isFinite(dpr.secondsFromPresent)) info.timeToDest = dpr.secondsFromPresent;
      }
    } catch (e) {}

    var pw = gc.currentPseudoWaypoints || [];
    for (var p = 0; p < pw.length; p++) {
      if (!pw[p]) continue;
      var id = ((pw[p].ident || pw[p].mcduIdent || "") + "").toUpperCase();
      var isTD = id.indexOf("T/D") >= 0, isTC = id.indexOf("T/C") >= 0;
      if (!isTD && !isTC) continue;   // ignore (DECEL) etc.
      var fpi = pw[p].flightPlanInfo;
      var secs = (fpi && typeof fpi.secondsFromPresent === "number" && isFinite(fpi.secondsFromPresent)) ? fpi.secondsFromPresent : null;
      var dGo = (fpi && typeof fpi.distanceFromAircraft === "number" && isFinite(fpi.distanceFromAircraft)) ? fpi.distanceFromAircraft : null;
      if (isTD) { if (info.distToTD == null) { info.distToTD = dGo; info.timeToTD = secs; } }
      else { if (info.distToTC == null) { info.distToTC = dGo; info.timeToTC = secs; } }
    }

    try { var ph = SimVar.GetSimVarValue("L:A32NX_FMGC_FLIGHT_PHASE", "number"); if (typeof ph === "number") info.flightPhase = ph; } catch (e) {}
    return JSON.stringify(info);
  } catch (e) { return JSON.stringify({ ok: false, error: (e && e.message) ? e.message : String(e) }); }
})();
