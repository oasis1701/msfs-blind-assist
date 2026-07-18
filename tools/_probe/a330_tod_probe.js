// Diagnose why `]Shift+D` (distance to Top of Descent) reads "not yet computed" on
// the Headwind A330neo while plain `]D` (distance to destination) works.
//
// The D/Shift+D readout (coherent-a32nx-flightinfo.js) was copied from the FBW A32NX:
// it resolves <a339x-mcdu>.fsInstrument.legacyFms.guidanceController (gc), reads
// gc.alongTrackDistancesToDestination.get(0) for plain-D, and finds TOD by scanning
// gc.currentPseudoWaypoints for an ident containing "T/D" then reading
// flightPlanInfo.distanceFromAircraft. The A330 fork may differ in (a) the FMS path
// (legacyFms vs fmcService), (b) whether currentPseudoWaypoints exists / is empty,
// (c) the T/D ident name, or (d) where the dist-to-go lives. This probe dumps ALL of
// those in one shot so the fix targets the real field — no guessing.
//
// Run IN CRUISE, with a flight plan + cruise FL entered (so the FMS has computed a
// descent and a real T/D exists), against the A339X_MCDU view:
//   tools/coherent-eval.ps1 -Title A339X_MCDU -ExprFile tools/_probe/a330_tod_probe.js
// ES5 only (Coherent GT / Chromium 49).
(function () {
  function keys(o) { try { return o ? Object.keys(o) : null; } catch (e) { return "ERR " + e; } }
  function num(v) { return (typeof v === "number" && isFinite(v)) ? v : null; }
  try {
    var el = document.querySelector("a32nx-mcdu") || document.querySelector("a339x-mcdu")
             || document.querySelector("a330-mcdu");
    var out = {
      foundElement: !!el,
      elementTag: el ? el.tagName.toLowerCase() : null,
      hasFsInstrument: !!(el && el.fsInstrument),
      fsInstrumentKeys: el ? keys(el.fsInstrument) : null
    };
    if (!el || !el.fsInstrument) return JSON.stringify(out, null, 1);

    var fi = el.fsInstrument;
    var fms = fi.legacyFms;
    out.hasLegacyFms = !!fms;
    out.hasFmcService = !!fi.fmcService;              // newer FBW/A380-style arch
    var gc = (fms && fms.guidanceController)
             || (fi.fmcService && fi.fmcService.master && fi.fmcService.master.guidanceController)
             || fi.guidanceController;
    out.gcFrom = (fms && fms.guidanceController) ? "legacyFms.guidanceController"
               : (fi.fmcService ? "fmcService.master.guidanceController" : "fsInstrument.guidanceController");
    out.hasGc = !!gc;
    if (!gc) return JSON.stringify(out, null, 1);

    out.gcKeys = keys(gc);

    // Plain-D path (the one the user reports DOES work).
    var map = gc.alongTrackDistancesToDestination;
    out.distToDest = (map && map.get) ? num(map.get(0)) : "no alongTrackDistancesToDestination";

    out.flightPhase = (function () {
      try { return SimVar.GetSimVarValue("L:A32NX_FMGC_FLIGHT_PHASE", "number"); } catch (e) { return "err"; }
    })();

    // (1) The pseudo-waypoint array — the current TOD source.
    var pw = gc.currentPseudoWaypoints;
    out.hasCurrentPseudoWaypoints = (pw != null);
    out.pwLength = (pw && typeof pw.length === "number") ? pw.length : null;
    out.pseudoWaypoints = [];
    if (pw && pw.length) {
      for (var i = 0; i < pw.length; i++) {
        var p = pw[i] || {};
        var fpi = p.flightPlanInfo || null;
        out.pseudoWaypoints.push({
          ident: p.ident != null ? ("" + p.ident) : null,
          mcduIdent: p.mcduIdent != null ? ("" + p.mcduIdent) : null,
          pwpKeys: keys(p),
          hasFlightPlanInfo: !!fpi,
          flightPlanInfoKeys: keys(fpi),
          distanceFromAircraft: fpi ? num(fpi.distanceFromAircraft) : null,
          secondsFromPresent: fpi ? num(fpi.secondsFromPresent) : null,
          // in case the fork put the distance on the pwp itself:
          distanceFromStart: num(p.distanceFromStart),
          distanceAlongTrack: num(p.distanceAlongTrack)
        });
      }
    }

    // (2) The VNAV driver — TOD may be exposed directly here on the A330.
    var vd = gc.vnavDriver;
    out.hasVnavDriver = !!vd;
    if (vd) {
      out.vnavDriverKeys = keys(vd);
      out.vnav_topOfDescent = (typeof vd.topOfDescent !== "undefined") ? vd.topOfDescent : null;
      try { out.vnav_currentDescentProfileKeys = keys(vd.currentDescentProfile); } catch (e) {}
      try {
        var pm = vd.profileManager;
        out.hasProfileManager = !!pm;
        out.profileManagerKeys = keys(pm);
        var mp = pm && pm.mcduProfile;
        out.hasMcduProfile = !!mp;
        out.mcduProfileKeys = keys(mp);
        if (mp) {
          // dump any key whose name hints at descent/top-of-descent:
          var mk = keys(mp) || [];
          out.mcduProfile_todish = [];
          for (var k = 0; k < mk.length; k++) {
            if (/desc|tod|t_?d|topof/i.test(mk[k])) {
              out.mcduProfile_todish.push({ key: mk[k], value: (typeof mp[mk[k]] === "object" ? keys(mp[mk[k]]) : mp[mk[k]]) });
            }
          }
        }
      } catch (e) { out.vnavErr = "" + e; }
    }

    // (3) fmcService master (newer arch) — guidance/vnav may hang here instead.
    if (fi.fmcService && fi.fmcService.master) {
      out.fmcMasterKeys = keys(fi.fmcService.master);
    }

    return JSON.stringify(out, null, 1);
  } catch (e) { return "ERR " + (e && e.message ? e.message : String(e)); }
})()
