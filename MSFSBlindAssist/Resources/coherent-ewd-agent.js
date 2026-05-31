// coherent-ewd-agent.js
//
// In-page agent for the FlyByWire A380X E/WD (Engine & Warning Display),
// installed via the Coherent GT debugger's Runtime.evaluate (NO injection).
// The A380 FWS publishes the abnormal/warning PROCEDURES on an in-process
// EventBus (NOT SimVars) and the E/WD instrument renders them, so the ONLY way
// to read failures for a screen reader is to scrape this view's DOM.
//
// CoherentEWDClient.cs sends this file once per connection to define
// window.__MSFSBA_EWD, then calls __MSFSBA_EWD.scrape() -> JSON:
//   { ok, warnings:[{text,sev,headline,selected}], memos:[text] }
//
// ES5 ONLY (Coherent GT = Chromium 49): var, no arrow funcs, no String.includes.

(function () {
  "use strict";
  var A = {};

  function clean(s) { return (s || "").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, ""); }

  // The EclLine renders the text THREE ways: a hidden checkbox glyph, a
  // <span class=EclLineText> carrying the RAW FWC codes (<4m, 4m, m), and the
  // visible <svg> whose tspans are the clean rendered text. Take the svg text;
  // fall back to the span with the FWC codes stripped.
  function stripFwc(s) {
    s = (s || "").replace(/[▯□☐◻◼▮]/g, ""); // box/checkbox glyphs
    s = s.replace(/<\d+m/g, "").replace(/\d+m/g, "");                      // <Nm colour, Nm underline
    return clean(s);
  }
  A.lineText = function (n) {
    var svg = n.querySelector("svg");
    if (svg) { var t = clean(svg.textContent); if (t) return t; }
    var span = n.querySelector(".EclLineText");
    return stripFwc(span ? span.textContent : n.textContent);
  };

  A.isVisible = function (n) {
    try {
      var st = window.getComputedStyle(n);
      if (st.display === "none" || st.visibility === "hidden" || parseFloat(st.opacity) === 0) return false;
      return true;
    } catch (e) { return true; }
  };

  function hasTok(n, t) {
    try { return n.classList && n.classList.contains(t); } catch (e) { return false; }
  }

  // Severity word from the EclLine class tokens (Amber/Cyan/White/Green/Red) or
  // the Headline marker. Headlines are the failure TITLES; the rest are the
  // procedure action items / conditions.
  A.severity = function (n) {
    if (hasTok(n, "Red")) return "warning";
    if (hasTok(n, "Amber")) return "caution";
    if (hasTok(n, "Cyan")) return "action";
    if (hasTok(n, "White")) return "info";
    if (hasTok(n, "Green")) return "normal";
    return "";
  };

  // PFD memo (3) + limitations (8) lines are FBW 'string' SimVars (NOT numeric
  // codes), so MSFSBA's numeric SimConnect read returns 0 for them — only a string
  // read from the Coherent JS context works. Read them here so the same monitor can
  // announce e.g. "SET HOLD SPD" / "SPEED LIM" when they appear. Strip the FWC codes.
  A.pfdLines = function () {
    var out = [];
    try {
      var i, s;
      for (i = 1; i <= 3; i++) {
        s = stripFwc(SimVar.GetSimVarValue("L:A32NX_PFD_MEMO_LINE_" + i, "string"));
        if (s) out.push(s);
      }
      for (i = 1; i <= 8; i++) {
        s = stripFwc(SimVar.GetSimVarValue("L:A32NX_PFD_LIMITATIONS_LINE_" + i, "string"));
        if (s) out.push(s);
      }
    } catch (e) { /* SimVar may be unavailable on some views */ }
    return out;
  };

  A.scrape = function () {
    try {
      var warnings = [], memos = [];
      // 1) Abnormal/warning procedures: ordered .EclLine.AbnormalItem rows.
      var lines = document.querySelectorAll(".EclLine.AbnormalItem");
      for (var i = 0; i < lines.length; i++) {
        var n = lines[i];
        if (!A.isVisible(n)) continue;
        if (hasTok(n, "HiddenElement") || hasTok(n, "Invisible") || hasTok(n, "Inactive")) continue;
        var t = A.lineText(n);
        if (!t) continue;
        warnings.push({
          text: t,
          sev: A.severity(n),
          headline: hasTok(n, "Headline"),
          selected: hasTok(n, "Selected")
        });
      }
      // 2) Memos (left + right columns).
      var ms = document.querySelectorAll(".MemosLeft .EWDMemo, .MemosRight .EWDMemo, .MemosLeft text, .MemosRight text");
      var seen = {};
      for (var j = 0; j < ms.length; j++) {
        var m = ms[j];
        if (!A.isVisible(m)) continue;
        if (m.children && m.children.length > 0 && m.tagName.toLowerCase() !== "text") continue;
        var mt = clean(m.textContent);
        if (mt && !seen[mt]) { seen[mt] = 1; memos.push(mt); }
      }
      return JSON.stringify({ ok: true, warnings: warnings, memos: memos, pfd: A.pfdLines() });
    } catch (e) {
      return JSON.stringify({ ok: false, error: (e && e.message) ? e.message : String(e) });
    }
  };

  A.ping = function () { return "MSFSBA_EWD_OK"; };

  window.__MSFSBA_EWD = A;
  return "MSFSBA_EWD_INSTALLED";
})();
