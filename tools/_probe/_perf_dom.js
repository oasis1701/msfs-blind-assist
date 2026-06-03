// Dump the REAL PERF DOM structure so the builder is written against runtime
// classes, not source assumptions. Shows label-value containers + perf grid cells.
(function () {
  var A = window.__MSFSBA_A380;
  var root = A.findRoot(A.activeMcdu);
  if (!root) return "no root";
  function cl(s) { return (s || "").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, ""); }
  function dt(n) { var t = ""; for (var i = 0; i < n.childNodes.length; i++) { var c = n.childNodes[i]; if (c.nodeType === 3) t += c.textContent; } return cl(t); }
  var out = [];
  out.push("TITLE " + cl(A.activePageLabel(root)));

  var lvc = root.querySelectorAll(".mfd-label-value-container");
  out.push("=== .mfd-label-value-container x" + lvc.length);
  for (var i = 0; i < lvc.length; i++) {
    var c = lvc[i];
    var label = c.querySelector(".mfd-label");
    var val = c.querySelector(".mfd-value");
    var unit = c.querySelector(".mfd-label-unit, .mfd-unit-leading, .mfd-unit-trailing");
    var svg = c.querySelector("svg");
    out.push("LVC label=[" + (label ? cl(label.textContent) : "") + "] val=[" + (val ? cl(val.textContent) : "") + "] unit=[" + (unit ? cl(unit.textContent) : "") + "]" + (svg ? " HAS_SVG" : "") + " cls=[" + c.className + "]");
  }

  var cells = root.querySelectorAll('[class*="mfd-fms-perf"]');
  out.push("=== [class*=mfd-fms-perf] x" + cells.length + " (first 70)");
  for (var j = 0; j < Math.min(cells.length, 70); j++) {
    out.push("P cls=[" + cells[j].className + "] dt=[" + dt(cells[j]) + "]");
  }
  return out.join("\n");
})();
