// Visible-only: speed-table cells + interactive input containers with label context.
(function () {
  var A = window.__MSFSBA_A380;
  var root = A.findRoot(A.activeMcdu);
  function cl(s) { return (s || "").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, ""); }
  var out = ["TITLE " + cl(A.activePageLabel(root))];

  out.push("=== VISIBLE speed-table cells (innerText) ===");
  var cells = root.querySelectorAll(".mfd-fms-perf-speed-table-cell, .mfd-fms-perf-speed-presel-managed-table-cell");
  for (var i = 0; i < cells.length; i++) {
    if (!A.isVisible(cells[i])) continue;
    out.push("CELL[" + cls(cells[i]) + "] = [" + cl(cells[i].innerText) + "]");
  }

  out.push("=== VISIBLE interactive inputs: own value + enclosing label-value-container label ===");
  var inps = root.querySelectorAll(".mfd-input-field-container, .mfd-dropdown-container");
  for (var j = 0; j < inps.length; j++) {
    var n = inps[j];
    if (!A.isVisible(n)) continue;
    var lvc = n.closest ? n.closest(".mfd-label-value-container") : null;
    var lab = lvc ? lvc.querySelector(".mfd-label") : null;
    var val = A.readInputValue(n);
    out.push("INP val=[" + val + "] lvcLabel=[" + (lab ? cl(lab.textContent) : "(none)") + "] inCellLabelless=" + (!!n.closest(".mfd-fms-perf-speed-table-cell")));
  }
  function cls(e) { return (e.className && e.className.toString) ? e.className.toString() : ""; }
  return out.join("\n");
})();
