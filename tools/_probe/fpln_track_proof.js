(function () {
  // Encoding-immune: build the degree needle from its char code (176 = U+00B0),
  // so this probe does NOT depend on how PowerShell encodes a literal "°".
  // If this finds the track but the agent (injected via coherent-eval) did not,
  // the agent's "°" literal was corrupted by the injector — NOT a real-app bug.
  var deg = String.fromCharCode(176);
  function cl(s) { return (s || "").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, ""); }
  var lines = document.querySelectorAll(".mfd-fms-fpln-line");
  var res = [];
  for (var i = 0; i < lines.length; i++) {
    var L = lines[i];
    var identEl = L.querySelector(".mfd-fms-fpln-line-ident");
    var ident = identEl ? cl(identEl.textContent) : "";
    if (!ident) continue;
    var ups = L.querySelectorAll(".mfd-fms-fpln-leg-upper-row");
    var track = "";
    for (var u = 0; u < ups.length; u++) { var t = cl(ups[u].textContent); if (t.indexOf(deg) >= 0) track = t; }
    res.push({ ident: ident, trackViaCharCode: track });
  }
  return JSON.stringify({ needleCharCode: deg.charCodeAt(0), lines: res });
})();
