(function () {
  // Full-page element dump: kind, text, value, disabled — enough to judge whether
  // every control a sighted user sees is reachable (esp. SelectInput dropdowns).
  var r = JSON.parse(__MSFSBA_FLYPAD.scrape());
  if (!r.ok) return "scrape failed: " + (r.error || "");
  var out = ["page: " + r.page, "count: " + r.elements.length];
  for (var i = 0; i < r.elements.length; i++) {
    var e = r.elements[i];
    out.push(
      e.idx + " [" + e.kind + (e.controlType ? "/" + e.controlType : "") + "]" +
      (e.disabled ? " (dimmed)" : "") + " " +
      String(e.text || "").substring(0, 48) +
      (e.value !== undefined && e.value !== "" ? " = " + String(e.value).substring(0, 20) : "")
    );
  }
  return out.join("\n");
})()
