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
//   { ok, warnings:[{text,sev,colour,headline,selected}], memos:[{text,colour}],
//     pfd:[{text,colour}], status:[text] }
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

  // The EWD colour NAME (what a sighted pilot sees), appended to every auto-announced
  // line. Warnings carry the colour class on the .EclLine element itself, but MEMOS render
  // their text via FormattedFwcText which puts the colour on a child <tspan class="Amber
  // EWDWarn">. So check the element first, then its subtree.
  A.colour = function (n) {
    var cls = ["Red", "Amber", "Cyan", "White", "Green"], i, j, kids;
    for (i = 0; i < cls.length; i++) { if (hasTok(n, cls[i])) return cls[i]; }
    // Memos colour their text on a child <tspan class="Amber EWDWarn"> (FormattedFwcText),
    // so search descendants. Use getElementsByTagName + classList (hasTok) rather than a
    // querySelector class selector, which is unreliable on SVG nodes in Coherent GT (Chromium 49).
    try {
      kids = n.getElementsByTagName ? n.getElementsByTagName("*") : [];
      for (j = 0; j < kids.length; j++) {
        for (i = 0; i < cls.length; i++) { if (hasTok(kids[j], cls[i])) return cls[i]; }
      }
    } catch (e) { /* not searchable */ }
    return "";
  };

  // The EWD colour NAME from the raw FWC colour code (<2m..<7m) embedded in the
  // PFD memo/limitation string SimVars (these carry no CSS class). Same code->colour
  // map the C# GetMessagePriority uses, read BEFORE the codes are stripped.
  function fwcColour(s) {
    s = s || "";
    if (s.indexOf("<2m") >= 0) return "Red";
    if (s.indexOf("<4m") >= 0) return "Amber";
    if (s.indexOf("<3m") >= 0) return "Green";
    if (s.indexOf("<5m") >= 0) return "Cyan";
    if (s.indexOf("<6m") >= 0) return "Magenta";
    if (s.indexOf("<7m") >= 0) return "White";
    return "";
  }

  // PFD memo (3) + limitations (8) lines are FBW 'string' SimVars (NOT numeric
  // codes), so MSFSBA's numeric SimConnect read returns 0 for them — only a string
  // read from the Coherent JS context works. Read them here so the same monitor can
  // announce e.g. "SET HOLD SPD" / "SPEED LIM" when they appear. Strip the FWC codes.
  A.pfdLines = function () {
    var out = [];
    try {
      var i, raw, s;
      for (i = 1; i <= 3; i++) {
        raw = SimVar.GetSimVarValue("L:A32NX_PFD_MEMO_LINE_" + i, "string");
        s = stripFwc(raw);
        if (s) out.push({ text: s, colour: fwcColour(raw) });
      }
      for (i = 1; i <= 8; i++) {
        raw = SimVar.GetSimVarValue("L:A32NX_PFD_LIMITATIONS_LINE_" + i, "string");
        s = stripFwc(raw);
        if (s) out.push({ text: s, colour: fwcColour(raw) });
      }
    } catch (e) { /* SimVar may be unavailable on some views */ }
    return out;
  };

  // EWD status-area indications (bottom of the display) + display self-test.
  // - .StsArea .FailurePendingBox / .StsBox / .AdvBox : visibility-toggled boxes
  //   that read "FAILURE PENDING" / "STS" (or "STS & DEFRD PROC") / "ADV" — the
  //   reminders a sighted pilot sees at the bottom of the E/WD (STS = check the
  //   STATUS page, ADV = an advisory is on the SD, FAILURE PENDING = the FWS is
  //   still processing a failure).
  // - .SelfTest / .MaintenanceMode / .EngineeringTestMode : the CdsDisplayUnit
  //   power-up overlays ("SELF TEST IN PROGRESS (MAX 40 SECONDS)", etc.),
  //   display-toggled. Returned as stable class tokens so the C# side speaks a
  //   fixed phrase and edge-detects appear/clear.
  A.statusBoxes = function () {
    var out = [], i;
    try {
      var boxes = document.querySelectorAll(".StsArea .FailurePendingBox, .StsArea .StsBox, .StsArea .AdvBox");
      for (i = 0; i < boxes.length; i++) {
        if (!A.isVisible(boxes[i])) continue;
        var t = clean(boxes[i].textContent);
        if (t) out.push(t);
      }
      var ov = document.querySelectorAll(".SelfTest, .MaintenanceMode, .EngineeringTestMode");
      for (i = 0; i < ov.length; i++) {
        if (!A.isVisible(ov[i])) continue;
        if (ov[i].classList && ov[i].classList.contains("SelfTest")) out.push("SELF TEST");
        else if (ov[i].classList && ov[i].classList.contains("MaintenanceMode")) out.push("MAINTENANCE MODE");
        else out.push("ENGINEERING TEST");
      }
    } catch (e) { /* view may lack the area */ }
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
        if (hasTok(n, "HiddenElement") || hasTok(n, "Invisible")) continue;
        var inactive = hasTok(n, "Inactive");
        var t = A.lineText(n);
        if (!t) continue;
        warnings.push({
          text: t,
          sev: A.severity(n),
          colour: A.colour(n),
          headline: hasTok(n, "Headline"),
          selected: hasTok(n, "Selected"),
          inactive: inactive
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
        if (mt && !seen[mt]) { seen[mt] = 1; memos.push({ text: mt, colour: A.colour(m) }); }
      }
      return JSON.stringify({ ok: true, warnings: warnings, memos: memos, pfd: A.pfdLines(), status: A.statusBoxes() });
    } catch (e) {
      return JSON.stringify({ ok: false, error: (e && e.message) ? e.message : String(e) });
    }
  };

  A.ping = function () { return "MSFSBA_EWD_OK"; };

  window.__MSFSBA_EWD = A;
  return "MSFSBA_EWD_INSTALLED";
})();
