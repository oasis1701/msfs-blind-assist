// Click the first clickable flyPad element whose text matches NEEDLE (case-insensitive,
// trimmed exact OR startsWith). Re-scrapes first so stamps are current, then clicks.
(function () {
  var A = window.__MSFSBA_FLYPAD;
  if (!A) return "NO_AGENT";
  var needle = "NEEDLE_PLACEHOLDER".toLowerCase();
  var els = JSON.parse(A.scrape()).elements;
  for (var i = 0; i < els.length; i++) {
    var t = (els[i].text || "").trim().toLowerCase();
    if (!els[i].idx) continue;
    if (t === needle || t.indexOf(needle) === 0) { return A.clickElement(els[i].idx) + " :: " + els[i].text; }
  }
  return "NO_MATCH for " + needle;
})();
