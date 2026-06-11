// coherent-oans-agent.js
//
// DATA-ONLY, ZERO-RENDER in-page agent for the FlyByWire A380X ND OANS / BTV.
// Installed via the Coherent GT debugger into the ND view ("A380X_ND_1"); NO injection.
//
// CRITICAL: this agent NEVER forces the ND to render the airport map. It does NOT write
// A32NX_EFIS_L_ND_RANGE, never sets element .style.visibility, and does no DOM scrape.
// (The earlier "ensureAirportZoom + scrape" design forced a continuous full-airport map
// render every poll and exhausted host memory.) Everything is read from the live ND JS
// instance objects + OANS L:vars, and written via btvUtils methods + the msfs-sdk EventBus.
//
//   __MSFSBA_OANS.ping()                      -> "MSFSBA_OANS_OK"
//   __MSFSBA_OANS.snapshot()                  -> JSON {ok, available, airport, fms, btv, manual, airac}
//   __MSFSBA_OANS.armRunway(name)             -> status string
//   __MSFSBA_OANS.armExit(name)               -> status string
//   __MSFSBA_OANS.clearBtv()                  -> status string
//   __MSFSBA_OANS.displayAirport(icao)        -> status string (one-shot AMDB load; user action only)
//   __MSFSBA_OANS.setManualStopDistance(m)    -> status string (no-Navigraph manual BTV)
//
// Target engine: Coherent GT (Chromium 49). ES5 ONLY (var, no arrow funcs, no String.includes).

(function () {
  "use strict";
  var A = {};

  // FeatureType enum (fbw-common amdb.ts): RunwayThreshold=2, runway centerline=4, RunwayExitLine=19.
  A.FT_RWY_CENTERLINE = 4;
  A.FT_RWY_THRESHOLD = 2;
  A.FT_RWY_EXIT_LINE = 19;

  A.oanc = function () {
    try {
      var nd = document.querySelector("a380x-nd");
      return (nd && nd.fsInstrument && nd.fsInstrument.oansRef && nd.fsInstrument.oansRef.instance) || null;
    } catch (e) { return null; }
  };

  A.bus = function () {
    try {
      var nd = document.querySelector("a380x-nd");
      var o = A.oanc();
      return (o && o.props && o.props.bus) || (nd && nd.fsInstrument && nd.fsInstrument.bus) || null;
    } catch (e) { return null; }
  };

  A.lvar = function (name) {
    try { return (typeof SimVar !== "undefined") ? SimVar.GetSimVarValue("L:" + name, "number") : null; }
    catch (e) { return null; }
  };

  // ARINC429 raw-double decode: low 32 bits = float32, bits 32-33 = SSM (3=NormalOp, 2=FuncTest).
  A.arinc = function (raw) {
    if (typeof raw !== "number" || !isFinite(raw)) return { valid: false, value: 0 };
    var ssm = Math.floor(raw / 4294967296) % 4;
    var bits = raw % 4294967296; if (bits < 0) bits += 4294967296;
    try {
      var dv = new DataView(new ArrayBuffer(4));
      dv.setUint32(0, bits >>> 0, false);
      return { valid: (ssm === 3 || ssm === 2), value: dv.getFloat32(0, false) };
    } catch (e) { return { valid: false, value: 0 }; }
  };

  A.get = function (sub) { try { return (sub && sub.get) ? sub.get() : null; } catch (e) { return null; } };

  // "Runway ahead" gate: the OANS_WORD_1 ARINC discrete, bit 11 (1-based), SSM-valid.
  // btvUtils.rwyAheadQfu goes stale when the monitor early-bails (stopped / >40 kt).
  // Bit 11 (1-based) = (low32 >> 10) & 1, matching C# Arinc429Word.BitValueOr(11).
  A.rwyAheadActive = function () {
    try {
      var raw = A.lvar("A32NX_OANS_WORD_1");
      if (typeof raw !== "number" || !isFinite(raw)) return false;
      var ssm = Math.floor(raw / 4294967296) % 4;
      if (ssm !== 3) return false;
      var bits = raw % 4294967296; if (bits < 0) bits += 4294967296;
      return (Math.floor(bits / 1024) % 2) === 1; // bit 11 = 2^10
    } catch (e) { return false; }
  };

  // Runway / exit pick-lists from the loaded AMDB map labels (populated on airport load,
  // independent of ND zoom — no render needed).
  A.listByFeat = function (feattype, runwayPattern) {
    var o = A.oanc(); if (!o || !o.labelManager || !o.labelManager.labels) return [];
    var L = o.labelManager.labels, seen = {}, out = [];
    for (var i = 0; i < L.length; i++) {
      var f = L[i].associatedFeature, t = L[i].text;
      if (f && f.properties && f.properties.feattype === feattype && t &&
          (!runwayPattern || /^[0-9]{1,2}[LRC]?$/.test(t)) && !seen[t]) {
        seen[t] = 1; out.push(t);
      }
    }
    return out.sort();
  };

  A.findLabel = function (feattype, name) {
    var o = A.oanc(); if (!o || !o.labelManager || !o.labelManager.labels) return null;
    var L = o.labelManager.labels;
    for (var i = 0; i < L.length; i++) {
      var f = L[i].associatedFeature;
      if (f && f.properties && f.properties.feattype === feattype && L[i].text === name) return L[i];
    }
    return null;
  };

  A.btvReady = function () {
    var o = A.oanc();
    return !!(o && o.btvUtils && o.data && o.data.features && o.labelManager && o.labelManager.labels);
  };

  // --- read-only exit-validity (mirrors OansBrakeToVacateSelection.selectExitFromOans) ----------
  // Pure airport-local-metre geometry; NO canvas draw, NO mutation. Used to filter the exit
  // pick-list to exits FBW would actually accept for the ARMED runway, so a blind pilot never
  // picks a silently-rejected exit. (BTV_MIN_TOUCHDOWN_ZONE_DISTANCE = 400 m; same 120deg / 50 m gates.)
  A.MIN_TDZ = 400;
  A._r2d = 180 / Math.PI;
  A._clampAngle = function (a) { a = a % 360; if (a < 0) a += 360; return a; };
  A._pointAngle = function (x1, y1, x2, y2) { return A._clampAngle(Math.atan2(y2 - y1, x2 - x1) * A._r2d); };
  A._pointDist = function (x1, y1, x2, y2) { var dx = x2 - x1, dy = y2 - y1; return Math.sqrt(dx * dx + dy * dy); };
  A._pointToLineDist = function (p, a, b) {
    var num = Math.abs((b[1] - a[1]) * p[0] - (b[0] - a[0]) * p[1] + b[0] * a[1] - b[1] * a[0]);
    var den = Math.sqrt((b[1] - a[1]) * (b[1] - a[1]) + (b[0] - a[0]) * (b[0] - a[0]));
    return den ? num / den : 0;
  };

  // Returns the exits valid for the currently-armed runway as [{name, dist}] sorted by distance
  // from the landing threshold (closest first). `dist` is the BTV exit distance in metres (raw
  // threshold->exit distance minus the 400 m touchdown zone == btvExitDistance for that exit, so
  // the pick-list and the armed-exit readout report the SAME number). NULL when the armed-runway
  // threshold references aren't available (caller then falls back to the full, unannotated list).
  //
  // A taxiway can contribute MORE THAN ONE exit-line feature under the same name (e.g. KSFO "Q"
  // has a segment ~230 m off 28R's centerline AND a valid one on it). We accept a NAME if ANY of
  // its features passes, and report the FIRST passing feature's distance. armExit() then arms that
  // same valid feature by trying every same-named feature until one is accepted — without this, the
  // first feature by name (often the off-runway one) was selected and silently rejected.
  A.validExitsForArmed = function () {
    var o = A.oanc(); if (!o || !o.btvUtils) return null;
    var b = o.btvUtils;
    var thr = b.btvThresholdPositionOansReference, opp = b.btvOppositeThresholdPositionOansReference;
    if (!thr || !opp || !o.labelManager || !o.labelManager.labels) return null;
    var L = o.labelManager.labels, seen = {}, out = [];
    for (var i = 0; i < L.length; i++) {
      var f = L[i].associatedFeature, t = L[i].text;
      if (!(f && f.properties && f.properties.feattype === A.FT_RWY_EXIT_LINE && t)) continue;
      if (seen[t]) continue;
      var coords = f.geometry && f.geometry.coordinates;
      if (!coords || coords.length < 2) continue;
      var last = coords.length - 1, e1 = coords[0], e2 = coords[last];
      var d1 = A._pointToLineDist(e1, thr, opp), d2 = A._pointToLineDist(e2, thr, opp);
      var startDist = (d1 < d2) ? A._pointDist(thr[0], thr[1], e1[0], e1[1]) : A._pointDist(thr[0], thr[1], e2[0], e2[1]);
      var angle = (d1 < d2)
        ? A._pointAngle(thr[0], thr[1], e1[0], e1[1]) - A._pointAngle(e1[0], e1[1], coords[1][0], coords[1][1])
        : A._pointAngle(thr[0], thr[1], e2[0], e2[1]) - A._pointAngle(e2[0], e2[1], coords[last - 1][0], coords[last - 1][1]);
      if (Math.abs(angle) > 120 || Math.min(d1, d2) > 50 || startDist < A.MIN_TDZ) continue; // FBW rejects (off-runway same-named feature)
      seen[t] = 1; out.push({ name: t, dist: Math.round(startDist - A.MIN_TDZ) });
    }
    out.sort(function (a, c) { return a.dist - c.dist; });
    return out;
  };

  // Cheap, side-effect-free read of the STATUS AIRAC cycle text node (textContent does not
  // force layout/render; the node exists even while the panel is visibility:hidden).
  A.airac = function () {
    try {
      var n = document.querySelector(".oans-cp-status-active .mfd-value");
      return (n && n.textContent) ? n.textContent.replace(/\s+/g, " ").replace(/^\s+|\s+$/g, "") : "";
    } catch (e) { return ""; }
  };

  A.snapshot = function () {
    try {
      var available = A.lvar("A32NX_OANS_AVAILABLE") ? true : false;
      var failed = A.lvar("A32NX_OANS_FAILED") ? true : false;
      var o = A.oanc();
      var ready = A.btvReady();
      var b = ready ? o.btvUtils : null;

      var rwyLenW = A.arinc(A.lvar("A32NX_OANS_RWY_LENGTH"));
      function m(v) { return (typeof v === "number" && v > 0) ? Math.round(v) : null; }

      var rwy = b ? A.get(b.btvRunway) : null;
      var exit = b ? A.get(b.btvExit) : null;

      // Exit pick-list (filtered to the armed runway, sorted closest-first) + a parallel distance
      // array (metres from threshold). Falls back to the full unannotated list if the refs aren't up.
      var ve = rwy ? A.validExitsForArmed() : null;
      var exitNames = [], exitDists = [];
      if (ve) { for (var ei = 0; ei < ve.length; ei++) { exitNames.push(ve[ei].name); exitDists.push(ve[ei].dist); } }
      else if (rwy) { exitNames = A.listByFeat(A.FT_RWY_EXIT_LINE, false); }

      var dry = A.lvar("A32NX_OANS_BTV_DRY_DISTANCE_ESTIMATED");
      var wet = A.lvar("A32NX_OANS_BTV_WET_DISTANCE_ESTIMATED");
      var stop = A.lvar("A32NX_OANS_BTV_STOP_BAR_DISTANCE_ESTIMATED");
      var computing = (dry > 0) || (wet > 0);
      var rot = A.arinc(A.lvar("A32NX_BTV_ROT"));
      var tMax = A.arinc(A.lvar("A32NX_BTV_TURNAROUND_MAX_REVERSE"));
      var tIdle = A.arinc(A.lvar("A32NX_BTV_TURNAROUND_IDLE_REVERSE"));
      function ar(w) { return (w.valid && w.value > 0) ? Math.round(w.value) : null; }

      var fms = o && o.fmsDataStore ? {
        origin: A.get(o.fmsDataStore.origin),
        dest: A.get(o.fmsDataStore.destination),
        altn: A.get(o.fmsDataStore.alternate),
        landingRunway: A.get(o.fmsDataStore.landingRunway)
      } : { origin: null, dest: null, altn: null, landingRunway: null };

      var reqStop = A.arinc(A.lvar("A32NX_OANS_BTV_REQ_STOPPING_DISTANCE"));

      return JSON.stringify({
        ok: true,
        available: available,
        failed: failed,
        airport: {
          icao: o ? A.get(o.dataAirportIcao) : null,
          name: o ? A.get(o.dataAirportName) : null
        },
        fms: fms,
        btv: {
          ready: ready,
          runways: A.listByFeat(A.FT_RWY_CENTERLINE, true),
          // Exits FBW would accept for the armed runway, sorted closest-first, with a parallel
          // distance-from-threshold array (exitDists, metres). Empty until a runway is armed.
          exits: exitNames,
          exitDists: exitDists,
          runway: rwy, exit: exit,
          lda: m(b ? A.get(b.btvRunwayLda) : null),
          exitDist: m(b ? A.get(b.btvExitDistance) : null),
          bearing: b ? A.get(b.btvRunwayBearingTrue) : null,
          dry: m(dry), wet: m(wet), stop: computing ? m(stop) : null,
          computing: computing,
          rot: ar(rot), turnMax: ar(tMax), turnIdle: ar(tIdle),
          rwyAheadQfu: (b && A.rwyAheadActive()) ? (b.rwyAheadQfu || "") : "",
          metric: A.lvar("A32NX_EFB_USING_METRIC_UNIT") ? true : false
        },
        manual: {
          runwayLengthM: rwyLenW.valid ? Math.round(rwyLenW.value) : null,
          manualStopDist: reqStop.valid ? Math.round(reqStop.value) : null,
          fmsLandingRunwaySelected: fms.landingRunway != null
        },
        airac: A.airac()
      });
    } catch (e) {
      return JSON.stringify({ ok: false, error: (e && e.message) ? e.message : String(e) });
    }
  };

  A.armRunway = function (name) {
    var o = A.oanc(); if (!o || !o.btvUtils) return "OANS not ready";
    var rl = A.findLabel(A.FT_RWY_CENTERLINE, name), thr = null, feats = (o.data && o.data.features) || [];
    for (var k = 0; k < feats.length; k++) {
      // Guard .properties: a malformed/partial AMDB feature can lack it, and this loop runs
      // OUTSIDE the try below — an unguarded deref would throw uncaught out of armRunway.
      var pk = feats[k] && feats[k].properties;
      if (pk && pk.feattype === A.FT_RWY_THRESHOLD && pk.idthr === name) { thr = feats[k]; break; }
    }
    if (!rl || !thr) return "runway " + name + " not found on map";
    try {
      var want = (A.get(o.dataAirportIcao) || "") + name;
      o.btvUtils.selectRunwayFromOans(want, rl.associatedFeature, thr);
      // selectRunwayFromOans is async — but the btvRunway observable is set
      // synchronously before its first await, so an immediate read-back is valid.
      var got = A.get(o.btvUtils.btvRunway);
      if (got != null && String(got) === String(want)) return "Armed BTV runway " + name;
      return "Runway " + name + " was not accepted";
    } catch (e) { return "ERR " + e; }
  };

  A.armExit = function (name) {
    var o = A.oanc(); if (!o || !o.btvUtils) return "OANS not ready";
    var b = o.btvUtils;
    if (!b.btvRunway || A.get(b.btvRunway) == null) return "Select a BTV runway first";
    if (!o.labelManager || !o.labelManager.labels) return "exit " + name + " not found on map";
    // A taxiway can expose MORE THAN ONE exit-line feature under the same name (e.g. KSFO "Q":
    // one segment off the runway, one valid on it). selectExitFromOans accepts only the
    // geometrically-valid feature, so try EVERY same-named exit feature until one is accepted —
    // mirroring a sighted pilot clicking the correct exit label on the map. Picking the first
    // feature by name silently rejected the valid exit (the user's KSFO 28R / Q bug).
    var L = o.labelManager.labels, found = false, lastErr = null;
    for (var i = 0; i < L.length; i++) {
      var f = L[i].associatedFeature;
      if (!(f && f.properties && f.properties.feattype === A.FT_RWY_EXIT_LINE && L[i].text === name)) continue;
      found = true;
      // Per-feature try/continue: a throw on one same-named feature must NOT abort the loop,
      // or a malformed earlier feature would block the valid later one (the KSFO 28R / Q bug).
      try {
        b.selectExitFromOans(name, f);
        var got = A.get(b.btvExit);
        // Success ONLY when btvExit now equals the requested name — a rejected
        // select leaves the PREVIOUS exit in place (re-arm false-success bug).
        if (got != null && String(got) === String(name)) return "Armed BTV exit " + got;
      } catch (e) { lastErr = e; }
    }
    if (!found) return "exit " + name + " not found on map";
    if (lastErr) return "ERR " + lastErr;
    return "Exit " + name + " not valid for this runway (wrong side or too close to threshold)";
  };

  A.clearBtv = function () {
    var o = A.oanc(); if (!o || !o.btvUtils) return "OANS not ready";
    try { o.btvUtils.clearSelection(); return "BTV selection cleared"; } catch (e) { return "ERR " + e; }
  };

  // One-shot AMDB load of a chosen airport. USER ACTION ONLY — never call on a poll. Loads data +
  // builds the runway/exit labels; does NOT force the ND to render the map.
  //
  // We call the Oanc's loadAirportMap() DIRECTLY rather than firing the bus event
  // `oans_display_airport`, because that event's handler (Oanc.tsx) only sets the ICAO and SKIPS
  // the load when the OANS is in performance-hide mode (FBW perf mode + the ND not showing the
  // OANS — the normal case for us since we never render). Direct loadAirportMap() bypasses that
  // gate and loads deterministically (verified: KLAX → 4789 features / 828 labels, persists).
  A.displayAirport = function (icao) {
    if (!icao) return "no icao";
    var name = String(icao).toUpperCase();
    var o = A.oanc();
    if (o && typeof o.loadAirportMap === "function") {
      try { o.loadAirportMap(name); return "loading " + name; }   // async; next snapshot poll sees the data
      catch (e) { /* fall through to the bus path */ }
    }
    var bus = A.bus(); if (!bus) return "no bus";
    try { bus.pub("oans_display_airport", name, true); return "displaying " + name; }
    catch (e2) { return "ERR " + e2; }
  };

  // No-Navigraph manual BTV: publish the requested stopping distance (metres). The fallback
  // BTV consumes oansManualStoppingDistance; user must also select the landing runway in the FMS.
  A.setManualStopDistance = function (metres) {
    var bus = A.bus(); if (!bus) return "no bus";
    var v = parseFloat(metres);
    if (!isFinite(v) || v <= 400 || v > 4000) return "Stop distance must be more than 400 and at most 4000 metres";
    try { bus.pub("oansManualStoppingDistance", v, true); return "manual stop distance " + Math.round(v) + " m"; }
    catch (e) { return "ERR " + e; }
  };

  A.ping = function () { return "MSFSBA_OANS_OK"; };

  window.__MSFSBA_OANS = A;
  return "MSFSBA_OANS_INSTALLED";
})();
