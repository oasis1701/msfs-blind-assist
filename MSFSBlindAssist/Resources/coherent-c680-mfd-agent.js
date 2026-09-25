// coherent-c680-mfd-agent.js — MSFSBA in-page agent for the Skyward Citation Sovereign+ MFD's
// engine indication strip. Installed by CoherentDisplayClient through Runtime.evaluate (no
// injection) and read on demand through the shared __MSFSBA_DISP.scrape() contract.
//
// Measured 2026-09-10 on the live aircraft: the strip is .eis; the primary gauges are
// .arc-gauge (title .gauge-title, values .arc-gauge-digital-readout), the secondary rows
// .sec-eng-data-row (.sec-eng-data-title, .sec-eng-data-value), and each system group is a
// *-section carrying an .eis-title-text (TRIM, FUEL QTY, FLAPS, GEAR, APU, HYDRAULICS,
// ELECTRICAL). A group's row is its visible leaf texts read left to right, line by line, so
// the left engine's value comes before the label and the right engine's after it, as on the screen.
// ES5 ONLY (Chromium 49). Returns "MSFSBA_DISP_INSTALLED".
(function () {
  "use strict";
  var A = {}; A.VERSION = 1;
  function txt(e) { return (e.innerText || e.textContent || "").replace(/\s+/g, " ").trim(); }
  function visible(e) {
    var r = e.getBoundingClientRect(); if (!(r.width > 0 && r.height > 0)) return false;
    try { return window.getComputedStyle(e).visibility !== "hidden"; } catch (x) { return true; }
  }
  // Every visible leaf inside `root`, sorted top-to-bottom then left-to-right, joined line by line.
  function leavesInReadingOrder(root, skipText) {
    var items = []; var els = root.querySelectorAll("*");
    for (var i = 0; i < els.length; i++) {
      var e = els[i]; if (e.children.length || !visible(e)) continue;
      var t = txt(e); if (!t || t === skipText) continue;
      var r = e.getBoundingClientRect(); items.push({ t: t, x: Math.round(r.left), y: Math.round(r.top + r.height / 2) });
    }
    items.sort(function (a, b) { return (Math.round(a.y / 10) - Math.round(b.y / 10)) || (a.x - b.x); });
    var lines = []; var cur = null; var cy = -999;
    for (var j = 0; j < items.length; j++) {
      if (Math.abs(items[j].y - cy) > 10) { if (cur !== null) lines.push(cur); cur = items[j].t; cy = items[j].y; }
      else cur += " " + items[j].t;
    }
    if (cur !== null) lines.push(cur);
    return lines.join(", ");
  }
  // Four strip groups need reading by class, not by position (measured 2026-09-15, in flight):
  // TRIM is three pointer gauges of which only the stabilizer has a number (.elevator-trim-readout);
  // FUEL QTY names every figure (.fuel-total-value, .fuel-qty-left/right, .fuel-temp-left/right);
  // HYDRAULICS' values are spans with a child, which a leaf walk skipped ("PRESSURE VOLUME, PSI CU IN"
  // with no numbers); ELECTRICAL puts each .electrical-label BETWEEN its left and right value, which a
  // position sort jumbled ("28 28 GEN V"). Null means "read the group generically".
  function one(root, sel) { var e = root.querySelector(sel); return e && visible(e) ? txt(e) : ""; }
  function eisGroup(group) {
    var c = " " + group.className + " ";
    if (c.indexOf(" trim-section ") >= 0) {
      var stab = one(group, ".elevator-trim-readout");
      return stab ? "stabilizer " + stab.replace("−", "-") : "";
    }
    if (c.indexOf(" fuel-section ") >= 0) {
      return "total " + one(group, ".fuel-total-value") + " " + (one(group, ".fuel-lbs-label") || "LBS") +
        ", left " + one(group, ".fuel-qty-left") + ", right " + one(group, ".fuel-qty-right") +
        ", fuel temperature left " + one(group, ".fuel-temp-left") + ", right " + one(group, ".fuel-temp-right") + " °C";
    }
    if (c.indexOf(" hydraulics-section ") >= 0) {
      var vals = group.querySelectorAll(".hydraulics-value, .hydraulics-value-white"), parts = [];
      for (var i = 0; i < vals.length; i++) {
        if (!visible(vals[i])) continue;
        var unit = vals[i].nextElementSibling && / hydraulics-label /.test(" " + vals[i].nextElementSibling.className + " ") ? txt(vals[i].nextElementSibling) : "";
        // The unit can be a child of the value span ("3000<span>PSI</span>"): join the pieces with a space.
        var pieces = [], w = document.createTreeWalker(vals[i], NodeFilter.SHOW_TEXT, null, false), n;
        while ((n = w.nextNode())) { var pt = String(n.nodeValue || "").replace(/\s+/g, " ").trim(); if (pt) pieces.push(pt); }
        parts.push(pieces.join(" ") + (unit ? " " + unit : ""));
      }
      return parts.length ? "pressure " + (parts[0] || "") + (parts[1] ? ", volume " + parts[1] : "") : null;
    }
    if (c.indexOf(" electrical-section ") >= 0) {
      var labels = group.querySelectorAll(".electrical-label"), rows = [];
      for (var k = 0; k < labels.length; k++) {
        if (!visible(labels[k])) continue;
        var l = labels[k].previousElementSibling, r = labels[k].nextElementSibling;
        rows.push(txt(labels[k]) + " left " + (l ? txt(l) : "") + ", right " + (r ? txt(r) : ""));
      }
      return rows.length ? rows.join("; ") : null;
    }
    return null;
  }
  A.eis = function () {
    try {
      var rows = [];
      // The primary arcs come in pairs (left engine, right engine) per kind; their titles ("N1%",
      // "ITT°C") are separate .gauge-title elements in the strip, so pair them by kind.
      var titles = {}; var tl = document.querySelectorAll(".gauge-title");
      for (var q = 0; q < tl.length; q++) { var tt = txt(tl[q]); if (/^N1/i.test(tt)) titles.n1 = tt; else if (/^ITT/i.test(tt)) titles.itt = tt; }
      var byKind = {}; var order = [];
      var arcs = document.querySelectorAll(".arc-gauge");
      for (var i = 0; i < arcs.length; i++) {
        var g = arcs[i]; var c = String(g.className);
        var kind = /n1-gauge/.test(c) ? "n1" : /itt-gauge/.test(c) ? "itt" : c.replace(/\s+/g, " ").trim();
        var reads = g.querySelectorAll(".arc-gauge-digital-readout"); var val = "";
        for (var k = 0; k < reads.length; k++) { var rv = txt(reads[k]); if (rv) { val = rv; break; } }
        if (!byKind[kind]) { byKind[kind] = []; order.push(kind); }
        byKind[kind].push(val || "--");
      }
      for (var o = 0; o < order.length; o++) rows.push((titles[order[o]] || order[o].toUpperCase()) + " " + byKind[order[o]].join(" "));
      var secs = document.querySelectorAll(".sec-eng-data-row");
      for (var s = 0; s < secs.length; s++) {
        var row = secs[s]; if (!visible(row)) continue;
        var st = row.querySelector(".sec-eng-data-title"); var sn = st ? txt(st) : "";
        var sv = []; var svs = row.querySelectorAll(".sec-eng-data-value");
        for (var v = 0; v < svs.length; v++) if (visible(svs[v])) sv.push(txt(svs[v]));
        if (sn || sv.length) rows.push((sn ? sn + " " : "") + sv.join(" "));
      }
      var titles = document.querySelectorAll(".eis-title-text");
      for (var t = 0; t < titles.length; t++) {
        var el = titles[t]; if (!visible(el)) continue;
        var group = el.parentElement; if (!group) continue;
        var special = eisGroup(group);
        rows.push(txt(el) + ": " + (special !== null ? special : leavesInReadingOrder(group, txt(el))));
      }
      return JSON.stringify({ ok: true, rows: rows });
    } catch (e) { return JSON.stringify({ ok: false, error: String(e), rows: [] }); }
  };
  A.scrape = A.eis;
  // Distance to the destination. The G3000 publishes no such SimVar (GPS FLIGHT PLAN TOTAL DISTANCE
  // reads 0 and the stock GPS waypoint list is empty), but the MFD instrument holds the live FMS
  // (measured 2026-09-15): the primary plan's legs carry calculated.cumulativeDistanceWithTransitions
  // in METRES, so what remains is the last leg's cumulative distance minus the active leg's, plus
  // the distance still to fly to the active waypoint (GPS WP DISTANCE).
  A.dest = function () {
    try {
      var el = document.querySelector("wtg3000-mfd"), inst = el && (el.fsInstrument || el.instrument), fms = inst && inst.fms;
      if (!fms || !fms.hasPrimaryFlightPlan || !fms.hasPrimaryFlightPlan()) return JSON.stringify({ ok: true, plan: false });
      var plan = fms.getPrimaryFlightPlan(), destIcao = String(plan.destinationAirport || "").trim().split(/\s+/).pop() || "";
      var ai = plan.activeLateralLeg, act = plan.tryGetLeg(ai), last = plan.tryGetLeg(plan.length - 1);
      var nextNm = SimVar.GetSimVarValue("GPS WP DISTANCE", "nautical miles");
      var ca = act && act.calculated, cl = last && last.calculated;
      if (!ca || !cl) return JSON.stringify({ ok: true, plan: true, dest: destIcao, remainingNm: null, nextNm: nextNm });
      var afterActiveNm = (cl.cumulativeDistanceWithTransitions - ca.cumulativeDistanceWithTransitions) / 1852;
      return JSON.stringify({ ok: true, plan: true, dest: destIcao, remainingNm: Math.max(0, afterActiveNm) + nextNm, nextNm: nextNm, next: act.name || "" });
    } catch (e) { return JSON.stringify({ ok: false, error: String(e) }); }
  };
  // One MFD half: its title bar text, then the pane's content as reading-order lines. The left
  // pane is the pilot's (GTC 2 drives it), the right the copilot's (GTC 3). Measured 2026-09-10:
  // .display-pane.display-pane-left / -right, .display-pane-title-text, .display-pane-content.
  // ---- Synoptics as sentences -------------------------------------------------------------------
  // The Electrical / Fuel / Hydraulic synoptics are SVG diagrams whose labels and values are
  // unclassed <tspan>s, so a reading-order walk gave "28 | V | L | WSHLD | R | WSHLD | 28 | V".
  // Measured 2026-09-15 (in flight): the diagram's GROUP IDS are stable and name each part
  // (L-GEN-OUTPUT, L-AVN-BUS, LEFT-BOOST-PUMP, FUEL-TRANSFER-VALVE, Frame 6 …), and state is drawn
  // as colour — green #00BF4A = powered / running / open, white = off / closed; a valve is a circle
  // with ONE visible line, green through the pipe when open, white across it when closed; the amber
  // GEN OFF box shows only when that generator is off. The fuel tank figures are HTML
  // (.fuel-qty-display-value / .fuel-temp-display-value, left / right).
  function colorOf(e) {
    var v = (e.getAttribute("stroke") || "") + " " + (e.getAttribute("fill") || "");
    try { var cs = window.getComputedStyle(e); v += " " + cs.stroke + " " + cs.fill; } catch (x) { }
    v = v.toLowerCase();
    if (/00bf4a|0, 191, 74/.test(v)) return "green";
    if (/ffe160|c8c800|255, 225, 96|200, 200, 0/.test(v)) return "amber";
    if (/ff3232|255, 50, 50|red/.test(v)) return "red";
    if (/white|#fff|255, 255, 255/.test(v)) return "white";
    return "";
  }
  function g(root, id) {
    var list = root.querySelectorAll("g[id]");
    for (var i = 0; i < list.length; i++) if (list[i].getAttribute("id") === id && visible(list[i])) return list[i];
    return null;
  }
  function shapeColor(group, sel) {
    var s = group ? group.querySelector(sel) : null; return s ? colorOf(s) : "";
  }
  // Numbers with the unit drawn beside them, top to bottom: "28 V, 45 A".
  function valuesIn(group) {
    var ts = group.querySelectorAll("text"), nums = [], units = [];
    for (var i = 0; i < ts.length; i++) {
      if (!visible(ts[i])) continue;
      var t = (ts[i].textContent || "").replace(/\s+/g, " ").trim(); if (!t) continue;
      var r = ts[i].getBoundingClientRect(), c = (r.top + r.bottom) / 2;
      if (/^[-−+]?\d+(\.\d+)?$/.test(t)) nums.push({ t: t.replace("−", "-"), y: c, x: r.left });
      else if (/^(V|A|°C|PSI|LBS|CU IN|%)$/.test(t)) units.push({ t: t, y: c, x: r.left });
    }
    nums.sort(function (a, b) { return a.y - b.y; });
    var out = [];
    for (var n = 0; n < nums.length; n++) {
      var u = null; for (var k = 0; k < units.length; k++) if (Math.abs(units[k].y - nums[n].y) < 8 && units[k].x > nums[n].x) { if (!u || units[k].x < u.x) u = units[k]; }
      out.push(nums[n].t + (u ? " " + u.t : ""));
    }
    return out.join(", ");
  }
  // A valve group: its visible line's colour. Green = open, white = closed.
  function valveState(group) {
    if (!group) return "";
    var lines = group.querySelectorAll("line");
    for (var i = 0; i < lines.length; i++) {
      var cs = window.getComputedStyle(lines[i]); if (cs.display === "none" || cs.visibility === "hidden") continue;
      var c = colorOf(lines[i]); return c === "green" ? "open" : c === "white" ? "closed" : c;
    }
    return "";
  }
  function onOff(c) { return c === "green" ? "on" : c === "white" ? "off" : c || "unknown"; }
  function synElectrical(root, rows) {
    function out(id, name) {
      var grp = g(root, id); if (!grp) return;
      var c = shapeColor(grp, "rect"), off = /-GEN-OUTPUT$/.test(id) ? g(root, id.replace("-OUTPUT", "-OFF")) : null;   // only the two engine generators have a GEN OFF box
      var state = off ? "GEN OFF" : c === "green" ? "online" : c === "white" ? "offline" : "";
      var v = valuesIn(grp);
      rows.push(name + ": " + [state, v].filter(function (x) { return !!x; }).join(", "));
    }
    out("L-GEN-OUTPUT", "Left generator"); out("R-GEN-OUTPUT", "Right generator"); out("APU-GEN-OUTPUT", "APU generator");
    out("L-BATT-OUTPUT", "Left battery"); out("R-BATT-OUTPUT", "Right battery");
    out("L-TRU", "Left TRU"); out("R-TRU", "Right TRU");
    var buses = ["L-AVN", "L-INT", "L-ELEC", "L-EMER", "R-AVN", "R-INT", "R-ELEC", "R-EMER"], on = [], offl = [];
    for (var i = 0; i < buses.length; i++) {
      var b = g(root, buses[i] + "-BUS"); if (!b) continue;
      (shapeColor(b, "rect") === "green" ? on : offl).push(buses[i].replace("-", " "));
    }
    if (on.length) rows.push("Powered buses: " + on.join(", "));
    if (offl.length) rows.push("Unpowered buses: " + offl.join(", "));
    var lw = g(root, "L-WSHLD"), rw = g(root, "R-WSHLD");
    if (lw || rw) rows.push("Windshield heat: left " + onOff(shapeColor(lw, "rect")) + ", right " + onOff(shapeColor(rw, "rect")));
    var ts = root.querySelectorAll("text");
    for (var t = 0; t < ts.length; t++) { var tt = (ts[t].textContent || "").replace(/\s+/g, " ").trim(); if (/^BUS\s*TIE/.test(tt) && visible(ts[t])) rows.push("Bus tie: " + tt.replace(/^BUS\s*TIE\s*/, "")); }
  }
  function synFuel(root, rows) {
    function side(which) {
      var q = root.querySelector(".fuel-qty-display-value." + which), tmp = root.querySelector(".fuel-temp-display-value." + which);
      if (q && visible(q)) rows.push((which === "left" ? "Left" : "Right") + " tank: " + txt(q) + " LBS" + (tmp ? ", " + txt(tmp) : ""));
    }
    side("left"); side("right");
    function pump(id, name, onBox) {
      var p = g(root, id); if (!p) return;
      var boxOn = onBox ? g(root, onBox) : null;
      rows.push(name + ": " + (boxOn ? "on" : onOff(shapeColor(p, "circle, path"))));
    }
    pump("LEFT-BOOST-PUMP", "Left boost pump", "L-BST-PMP-ON"); pump("1-PUMP", "Right boost pump", "R-BST-PMP-ON");
    pump("LEFT-MOTIVE-PUMP", "Left motive flow pump"); pump("RIGHT-MOTIVE-PUMP", "Right motive flow pump");
    function valve(id, name) { var v = valveState(g(root, id)); if (v) rows.push(name + ": " + v); }
    valve("LEFT-SHUTOFF-VALVE", "Left fuel shutoff valve"); valve("RIGHT-SHUTOFF-VALVE", "Right fuel shutoff valve");
    valve("FUEL-TRANSFER-VALVE", "Crossfeed valve"); valve("APU-SHUTOFF-VALVE", "APU fuel valve");
  }
  function synHydraulic(root, rows) {
    var f1 = g(root, "Frame 1"); if (f1) { var v = valuesIn(f1); if (v) rows.push("System pressure: " + v); }
    var ts = root.querySelectorAll("text"), qty = "", unit = "";
    for (var i = 0; i < ts.length; i++) {
      if (!visible(ts[i])) continue; var t = (ts[i].textContent || "").trim();
      if (t === "CU IN") { unit = t; var prev = ts[i].previousElementSibling || ts[i].nextElementSibling; if (prev && /^\d/.test((prev.textContent || "").trim())) qty = prev.textContent.trim(); }
    }
    if (qty) rows.push("Reservoir: " + qty + " " + unit);
    function pump(id, name) { var p = g(root, id); if (p) rows.push(name + ": " + onOff(shapeColor(p, "circle"))); }
    pump("Frame 6", "Left engine hydraulic pump"); pump("Frame 6_2", "Right engine hydraulic pump"); pump("Frame 9", "Electric hydraulic pump");
    function valve(id, name) { var v = valveState(g(root, id)); if (v) rows.push(name + ": " + v); }
    valve("LEFT-FW-SHUTOFF-VALVE", "Left hydraulic firewall valve"); valve("RIGHT-FW-SHUTOFF-VALVE", "Right hydraulic firewall valve");
    valve("LEFT-ENGINE-VALVE", "Left engine hydraulic valve"); valve("RIGHT-ENGINE-VALVE", "Right engine hydraulic valve");
    var list = g(root, "HYDRAULIC-LIST");
    if (list) { var names = []; var lt = list.querySelectorAll("text"); for (var k = 0; k < lt.length; k++) { var n = (lt[k].textContent || "").trim(); if (n) names.push(n); } if (names.length) rows.push("Hydraulic users: " + names.join(", ")); }
  }

  A.pane = function (side) {
    try {
      var pane = document.querySelector(".display-pane.display-pane-" + (side === "right" ? "right" : "left"));
      if (!pane) return JSON.stringify({ ok: false, error: "no " + side + " pane", rows: [] });
      var title = pane.querySelector(".display-pane-title-text"); var content = pane.querySelector(".display-pane-content") || pane;
      var rows = ["Pane: " + (title ? txt(title) : "")];
      // The checklist pane (measured 2026-09-15): .checklist-pane-item rows, an actionable one being
      // label + a dot leader + action, with complete / incomplete checkbox icons and
      // checklist-pane-item-selected on the current item. Read "Fuel: Check; Verify Reserves, done".
      var clItems = content.querySelectorAll(".checklist-pane-item");
      var clVisible = []; for (var ci = 0; ci < clItems.length; ci++) if (visible(clItems[ci])) clVisible.push(clItems[ci]);
      if (clVisible.length) {
        var intro = content.querySelectorAll("[class*='checklist-pane-title'], [class*='checklist-pane-header']");
        for (var hi = 0; hi < intro.length; hi++) {
          if (!visible(intro[hi]) || !txt(intro[hi])) continue;
          var nested = false; for (var hj = 0; hj < intro.length; hj++) if (hj !== hi && intro[hi].contains(intro[hj])) { nested = true; break; }
          if (!nested) rows.push(txt(intro[hi]));   // the header container and its title lines both match: keep the innermost
        }
        for (var k = 0; k < clVisible.length; k++) {
          var it = clVisible[k];
          var lab = it.querySelector(".checklist-pane-item-content-actionable-label"), act = it.querySelector(".checklist-pane-item-content-actionable-action");
          var line = lab ? txt(lab) + (act && txt(act) ? ": " + txt(act) : "") : txt(it).replace(/(\s*\.){3,}/g, " ").replace(/\s+/g, " ").trim();
          if (!line) continue;
          var done = it.querySelector(".checklist-pane-item-checkbox-complete");
          if (lab && done) line += visible(done) ? ", done" : ", not done";
          if ((" " + it.className + " ").indexOf(" checklist-pane-item-selected ") >= 0) line += ", current item";
          rows.push(line);
        }
        return JSON.stringify({ ok: true, rows: rows });
      }
      if (g(content, "Electrical")) { synElectrical(content, rows); return JSON.stringify({ ok: true, rows: rows }); }
      if (g(content, "Fuel")) { synFuel(content, rows); return JSON.stringify({ ok: true, rows: rows }); }
      if (g(content, "Hydraulic")) { synHydraulic(content, rows); return JSON.stringify({ ok: true, rows: rows }); }
      var items = []; var els = content.querySelectorAll("*");
      for (var i = 0; i < els.length; i++) {
        var e = els[i]; if (e.children.length || !visible(e)) continue;
        var t = txt(e); if (!t) continue;
        var r = e.getBoundingClientRect(); items.push({ t: t, x: Math.round(r.left), y: Math.round(r.top + r.height / 2) });
      }
      items.sort(function (a, b) { return (Math.round(a.y / 10) - Math.round(b.y / 10)) || (a.x - b.x); });
      var cur = null; var cy = -999;
      for (var j = 0; j < items.length; j++) {
        if (Math.abs(items[j].y - cy) > 10) { if (cur !== null) rows.push(cur); cur = items[j].t; cy = items[j].y; }
        else cur += " | " + items[j].t;
      }
      if (cur !== null) rows.push(cur);
      return JSON.stringify({ ok: true, rows: rows });
    } catch (e) { return JSON.stringify({ ok: false, error: String(e), rows: [] }); }
  };
  window.__MSFSBA_C680_EIS = A; window.__MSFSBA_DISP = A;
  return "MSFSBA_DISP_INSTALLED";
})();
