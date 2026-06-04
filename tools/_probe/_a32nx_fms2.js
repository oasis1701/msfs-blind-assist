(function () {
  var el = document.querySelector('a32nx-mcdu');
  var inst = el && el.fsInstrument;
  var fms = inst && inst.legacyFms;
  if (!fms) return JSON.stringify({ err: "no legacyFms" });
  var out = { fmsKeys: Object.keys(fms).slice(0, 90) };
  function probe(label, obj) { try { out[label] = obj ? Object.keys(obj).slice(0, 50) : null; } catch (e) { out[label] = "err:" + e.message; } }
  probe('fms.guidanceController', fms.guidanceController);
  probe('fms.flightPlanService', fms.flightPlanService);
  probe('inst.guidanceController', inst.guidanceController);
  // guidance controller's distance/pseudo data
  var gc = fms.guidanceController || inst.guidanceController;
  if (gc) {
    probe('gc.alongTrackDistancesToDestination', gc.alongTrackDistancesToDestination);
    try { out['gc.currentPseudoWaypoints.len'] = (gc.currentPseudoWaypoints || []).length; } catch (e) { out['gc.cpw'] = "err:" + e.message; }
    try { out['gc.atdToDest.get0'] = gc.alongTrackDistancesToDestination && gc.alongTrackDistancesToDestination.get ? gc.alongTrackDistancesToDestination.get(0) : "no get"; } catch (e) { out['gc.atd0'] = "err:" + e.message; }
  }
  return JSON.stringify(out);
})();
