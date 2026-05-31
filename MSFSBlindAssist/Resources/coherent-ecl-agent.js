// coherent-ecl-agent.js
//
// In-page agent for the FlyByWire A380X Electronic Checklist (ECL), installed via
// the Coherent GT debugger (NO injection) in the A380X_EWD view. The ECL (normal
// checklists + the abnormal-procedure menu) is rendered as `.EclLine` rows.
//
// This agent READS the lines into structured rows (including which line carries the
// FWS cursor, via the `.EclLine.Selected` class). The ACTIONS are done in C# by
// pulsing the real ECP button L-vars (A32NX_BTN_CL / _CHECK_LH / _UP / _DOWN /
// _CLR / _ABNPROC) through the MobiFlight calculator path — FwsCore buffers those
// inputs and drives the checklist, so the buttons FULLY navigate the menu and tick
// items (verified live: C/L shows the menu, UP/DOWN move the cursor, CHECK opens
// the selected checklist or ticks the selected manual item, sensed items auto-tick).
// The DOM lines themselves have no click handlers, which is why interaction goes
// through the ECP L-vars rather than synthetic clicks. CoherentEclClient.cs sends
// this file, then calls __MSFSBA_ECL.scrape() -> JSON { ok, rows:[{text,type,
// checked,style,selected}] }.
//
// ES5 ONLY (Coherent GT = Chromium 49): var, no arrow funcs, no String.includes.

(function () {
  "use strict";
  var A = {};

  function clean(s) { return (s || "").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, ""); }
  function has(n, t) { try { return n.classList && n.classList.contains(t); } catch (e) { return false; } }

  // Clean text of an EclLine: the visible SVG render (clean) else the .EclLineText
  // span (which carries the raw FWC colour codes <Nm/Nm/m and the □ checkbox glyph).
  function lineText(n) {
    var svg = n.querySelector("svg");
    if (svg) { var t = clean(svg.textContent); if (t) return t; }
    var span = n.querySelector(".EclLineText");
    var s = (span ? span.textContent : n.textContent) || "";
    s = s.replace(/[▯□☐◻◼▮]/g, "").replace(/<\d+m/g, "").replace(/\d+m/g, "");
    return clean(s);
  }

  A.scrape = function () {
    try {
      var rows = [], i;
      var lines = document.querySelectorAll(".EclLine");
      for (i = 0; i < lines.length; i++) {
        var n = lines[i], c = n.getAttribute("class") || "";
        if (c.indexOf("HiddenElement") >= 0 || c.indexOf("Invisible") >= 0 || c.indexOf("Inactive") >= 0) continue;
        var t = lineText(n);
        var type =
          has(n, "Headline") || c.indexOf("Headline") >= 0 ? "headline" :
          c.indexOf("AbnormalItem") >= 0 ? "abnormal" :
          has(n, "ChecklistCompleted") ? "completed" :
          has(n, "ChecklistItem") ? "item" : "line";
        if (!t && type !== "completed") continue;
        // Colour/style → sensed-vs-manual hint (Cyan = sensed/pending action,
        // White = manual item, Amber = caution, Green/Checked = done).
        var style =
          has(n, "Checked") || c.indexOf("ChecklistCompleted") >= 0 ? "done" :
          c.indexOf("Cyan") >= 0 ? "action" :
          c.indexOf("Amber") >= 0 ? "caution" :
          c.indexOf("White") >= 0 ? "manual" : "";
        rows.push({
          text: t,
          type: type,
          checked: has(n, "Checked") || c.indexOf("ChecklistCompleted") >= 0,
          style: style,
          selected: has(n, "Selected") || c.indexOf("Cursor") >= 0
        });
      }
      // Is the checklist overlay actually shown? (else the EWD shows memos/engine)
      var shown = rows.length > 0;
      return JSON.stringify({ ok: true, shown: shown, rows: rows });
    } catch (e) {
      return JSON.stringify({ ok: false, error: (e && e.message) ? e.message : String(e) });
    }
  };

  A.ping = function () { return "MSFSBA_ECL_OK"; };
  window.__MSFSBA_ECL = A;
  return "MSFSBA_ECL_INSTALLED";
})();
