// coherent-display-agent.js
//
// GENERIC in-page agent for any FlyByWire A380X cockpit DISPLAY rendered in a
// Coherent GT view (SD / EWD / PFD / ND / ISIS). Installed via the Coherent GT
// debugger's Runtime.evaluate (NO injection).
//
// A380 displays are SVG/HTML; the decoded engineering values the crew sees
// (oxygen PSI, GW/FOB, TAT/SAT, engine N1/EGT, baro, FMA, …) are RENDERED text
// positioned by x/y, so flat innerText loses the label<->value association. This
// agent reconstructs READABLE ROWS by clustering the leaf text elements on their
// Y coordinate and sorting each cluster left-to-right by X — exactly how a
// sighted pilot reads the screen. CoherentDisplayClient.cs sends this file once
// per connection, then calls __MSFSBA_DISP.scrape().
//
// Returns JSON: { ok, rows:[ "TAT +26 °C   G LOAD +1.0   GW -- KG", ... ] }
//
// ES5 ONLY (Coherent GT = Chromium 49): var, no arrow funcs, no String.includes,
// no Array.find, top-level try/catch.

(function () {
  "use strict";
  var A = {};

  function clean(s) { return (s || "").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, ""); }

  function isVisible(n, r) {
    try {
      var st = window.getComputedStyle(n);
      if (st.display === "none" || st.visibility === "hidden") return false;
      if (parseFloat(st.opacity) === 0) return false;
    } catch (e) { /* SVG nodes may throw — fall back to geometry */ }
    if (r.width === 0 && r.height === 0) return false;
    // Off-screen / scrolled-away content (the display viewport is ~768x1024).
    if (r.bottom < -50 || r.top > 1600 || r.right < -50 || r.left > 1400) return false;
    return true;
  }

  // Collect every LEAF text node (an element that carries text and has no element
  // children — so a <text> wrapping <tspan>s is skipped and its tspans counted).
  A.leaves = function () {
    var els = document.querySelectorAll("text, tspan, div, span, p, td, th, li");
    var out = [];
    var seen = {};
    for (var i = 0; i < els.length; i++) {
      var e = els[i];
      if (e.children && e.children.length > 0) continue;     // not a leaf
      var tx = clean(e.textContent);
      if (!tx) continue;
      var r;
      try { r = e.getBoundingClientRect(); } catch (_) { continue; }
      if (!isVisible(e, r)) continue;
      var x = Math.round(r.left), y = Math.round(r.top);
      // De-dup exact text rendered at (almost) the same spot (SVG sometimes
      // stacks an outline + fill copy of the same glyphs).
      var key = tx + "@" + Math.round(x / 6) + "," + Math.round(y / 6);
      if (seen[key]) continue;
      seen[key] = 1;
      out.push({ t: tx, x: x, y: y, rt: Math.round(r.right) });
    }
    return out;
  };

  // Cluster leaves into rows by Y (tolerance), sort each row by X, join.
  A.scrape = function () {
    try {
      var leaves = A.leaves();
      // Sort by Y then X.
      leaves.sort(function (a, b) { return (a.y - b.y) || (a.x - b.x); });
      var rows = [];
      var cur = [];
      var curY = null;
      var TOL = 12; // px — same visual row
      for (var i = 0; i < leaves.length; i++) {
        var L = leaves[i];
        if (curY === null || Math.abs(L.y - curY) <= TOL) {
          cur.push(L);
          // Track the row's anchor Y as the first element's Y (stable).
          if (curY === null) curY = L.y;
        } else {
          rows.push(cur);
          cur = [L];
          curY = L.y;
        }
      }
      if (cur.length) rows.push(cur);

      var lines = [];
      for (var j = 0; j < rows.length; j++) {
        rows[j].sort(function (a, b) { return a.x - b.x; });
        // GAP-AWARE join: fragments that are touching (split decimals/colons like
        // "64" "." "8" or "06:48:" "30") have a tiny x-gap and join with NO space →
        // "64.8" / "06:48:30"; genuinely separate values/labels get a single space.
        var line = "";
        for (var k = 0; k < rows[j].length; k++) {
          var tok = rows[j][k].t;
          if (k === 0) { line = tok; continue; }
          var gap = rows[j][k].x - rows[j][k - 1].rt;
          line += (gap < 6 ? "" : " ") + tok;
        }
        line = line.replace(/^\s+|\s+$/g, "");
        if (line) lines.push(line);
      }
      return JSON.stringify({ ok: true, rows: lines });
    } catch (e) {
      return JSON.stringify({ ok: false, error: (e && e.message) ? e.message : String(e) });
    }
  };

  A.ping = function () { return "MSFSBA_DISP_OK"; };

  window.__MSFSBA_DISP = A;
  return "MSFSBA_DISP_INSTALLED";
})();
