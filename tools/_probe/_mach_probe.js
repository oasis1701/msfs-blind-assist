(function () {
  var A = window.__MSFSBA_A380;
  var s = JSON.parse(A.scrape());   // stamps data-fbwa380-mcdu-idx
  var m = (s.elements || []).filter(function (e) { return /MACH/i.test(e.text) && e.kind === "input"; })[0];
  if (!m) return JSON.stringify({ err: "no MACH input", inputs: (s.elements || []).filter(function (e) { return e.kind === "input"; }).map(function (e) { return e.text; }) });
  var node = document.querySelector('[data-fbwa380-mcdu-idx="' + m.idx + '"]');
  if (!node) return JSON.stringify({ err: "node not found for idx " + m.idx, machText: m.text });
  function cl(s) { return (s || "").replace(/\s+/g, " ").trim(); }
  var cell = node.closest(".mfd-fms-perf-speed-table-cell");
  return JSON.stringify({
    machEmitted: m.text,
    nodeTag: node.tagName,
    nodeClass: node.className,
    hasRealTextInput: !!node.querySelector(".mfd-input-field-text-input"),
    hasValueSpan: !!node.querySelector(".mfd-value"),
    nodeInnerText: cl(node.innerText),
    enclosingSpeedCell: cell ? cl(cell.innerText) : "(not in speed-table cell)",
    cellClass: cell ? cell.className : ""
  }, null, 1);
})();
