(function () {
  var A = window.__MSFSBA_A380;
  function cl(s) { return (s || "").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, ""); }
  var out = { ok: true };

  // 1) RAW DOM: per F-PLN line, the upper-row cells (track/dist live here).
  var lines = document.querySelectorAll(".mfd-fms-fpln-line");
  var rows = [];
  for (var i = 0; i < lines.length; i++) {
    var L = lines[i];
    var identEl = L.querySelector(".mfd-fms-fpln-line-ident");
    var ident = identEl ? cl(identEl.textContent) : "";
    var ups = L.querySelectorAll(".mfd-fms-fpln-leg-upper-row");
    var cells = [];
    for (var u = 0; u < ups.length; u++) {
      var c = cl(ups[u].textContent);
      cells.push({
        clean: c,
        deg: (c.indexOf("°") >= 0),
        isInt: (/^\d+$/.test(c)),
        codes: c.length ? c.split("").map(function (ch) { return ch.charCodeAt(0); }) : []
      });
    }
    var vis = (A && A.isVisible) ? A.isVisible(L) : true;
    if (ident || ups.length) rows.push({ i: i, ident: ident, vis: vis, nUpper: ups.length, upper: cells });
  }
  out.rawLines = rows;

  // 2) What scrape() actually emits (the screen-reader text).
  try {
    var sc = JSON.parse(A.scrape());
    out.scrapeOk = sc.ok; out.title = sc.title;
    out.emitted = (sc.elements || []).map(function (e) { return { kind: e.kind, idx: e.idx, text: e.text }; });
  } catch (e) { out.scrapeErr = String(e); }

  return JSON.stringify(out);
})();
