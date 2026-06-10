(function () {
  try {
    var el = document.querySelector("a32nx-mcdu");
    var fms = el && el.fsInstrument && el.fsInstrument.legacyFms;
    var gc = fms && fms.guidanceController;
    if (!gc) return "no gc";
    var vd = gc.vnavDriver;
    var out = {};
    var plan = null;
    try { plan = gc.flightPlanService.active; } catch (e) {}
    if (!plan) try { plan = vd.flightPlanService.active; } catch (e) {}
    out.hasPlan = !!plan;
    out.destLegIdx = plan && typeof plan.destinationLegIndex === "number" ? plan.destinationLegIndex : null;
    var pmgr = vd && vd.profileManager;
    out.pmgrKeys = pmgr ? Object.keys(pmgr) : null;
    var prof = pmgr && pmgr.mcduProfile;
    out.hasMcduProfile = !!prof;
    var preds = prof && prof.waypointPredictions;
    out.hasPreds = !!preds;
    if (preds && preds.get && out.destLegIdx != null) {
      var dpr = preds.get(out.destLegIdx);
      out.destPred = dpr ? { secondsFromPresent: dpr.secondsFromPresent, distanceFromAircraft: dpr.distanceFromAircraft } : null;
    }
    return JSON.stringify(out);
  } catch (e) { return "ERR " + e.message; }
})()
