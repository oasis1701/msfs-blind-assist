(function () {
  // Click a nav element by its visible TEXT (robust across re-stamped idx), then
  // return ok; a follow-up eval re-scrapes after the page settles.
  var TARGET = "Automatic Call Outs";
  var r = JSON.parse(__MSFSBA_FLYPAD.scrape());
  if (!r.ok) return "scrape failed: " + (r.error || "");
  for (var i = 0; i < r.elements.length; i++) {
    var e = r.elements[i];
    if ((e.kind === "link" || e.kind === "button") && String(e.text || "").indexOf(TARGET) === 0) {
      var res = __MSFSBA_FLYPAD.clickElement(e.idx);
      return "clicked '" + e.text + "' idx " + e.idx + " -> " + res;
    }
  }
  return "target not found: " + TARGET;
})()
