// coherent-c680-cas-agent.js — MSFSBA in-page agent for the Skyward Citation Sovereign+ PFD's
// crew alerting system list (Working Title G3000 "CAS display 2"). Installed by
// CoherentDisplayClient through Runtime.evaluate (no injection) and polled through the shared
// __MSFSBA_DISP.scrape() contract.
//
// Measured 2026-09-09/10 on the live aircraft: the list is .full-cas-display-2-list, each row
// .cas-display-2-msg; a live row carries cas-display-2-msg-visible and its severity as
// cas-display-2-msg-warning / -caution / -advisory (a new unacknowledged caution also carries
// -new, an acknowledged one -acked); an empty slot is display:none with no text.
// ES5 ONLY (Chromium 49). Returns "MSFSBA_DISP_INSTALLED".
(function () {
  "use strict";
  var A = {}; A.VERSION = 1;
  var CAS_LIST = ".full-cas-display-2-list", CAS_ROW = ".cas-display-2-msg";
  function txt(e) { return (e.innerText || e.textContent || "").replace(/\s+/g, " ").trim(); }
  function severity(e) {
    var c = " " + String(e.className) + " ";
    if (c.indexOf("cas-display-2-msg-warning") >= 0) return "warning";
    if (c.indexOf("cas-display-2-msg-caution") >= 0) return "caution";
    if (c.indexOf("cas-display-2-msg-advisory") >= 0) return "advisory";
    return "status";
  }
  A.cas = function () {
    try {
      var list = document.querySelector(CAS_LIST);
      if (!list) return JSON.stringify({ ok: true, rows: [] });
      var out = []; var rows = list.querySelectorAll(CAS_ROW);
      for (var i = 0; i < rows.length; i++) {
        var r = rows[i]; var rect = r.getBoundingClientRect();
        if (rect.width === 0 || rect.height === 0) continue;
        var t = txt(r); if (!t) continue;
        out.push({ cls: severity(r), text: t });
      }
      return JSON.stringify({ ok: true, rows: out });
    } catch (e) { return JSON.stringify({ ok: false, error: String(e), rows: [] }); }
  };
  // The shared contract: "warning: ENGINE FIRE L" rows, one per message.
  A.scrape = function () {
    var r = JSON.parse(A.cas()); var rows = [];
    for (var i = 0; i < r.rows.length; i++) rows.push(r.rows[i].cls + ": " + r.rows[i].text);
    return JSON.stringify({ ok: r.ok, rows: rows, error: r.error });
  };
  window.__MSFSBA_C680_CAS = A; window.__MSFSBA_DISP = A;
  return "MSFSBA_DISP_INSTALLED";
})();
