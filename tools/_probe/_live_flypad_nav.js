(function () {
  var r = JSON.parse(__MSFSBA_FLYPAD.scrape());
  if (!r.ok) return "scrape failed: " + (r.error || "");
  var out = ["page: " + r.page];
  for (var i = 0; i < r.elements.length; i++) {
    var e = r.elements[i];
    if (e.kind === "link" || e.kind === "button")
      out.push(e.idx + " [" + e.kind + "] " + String(e.text || "").substring(0, 40));
  }
  return out.slice(0, 45).join(" | ");
})()
