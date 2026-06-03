(function () {
  var A = window.__MSFSBA_A380;
  var s = JSON.parse(A.scrape());            // re-stamp idx
  var m = (s.elements || []).filter(function (e) { return /STEP ALT/i.test(e.t || e.text || ""); })[0];
  if (!m) return JSON.stringify({ err: "no STEP ALTs in menu", title: s.title, els: (s.elements || []).map(function (e) { return e.text; }) });
  A.clickElement(m.idx);
  return JSON.stringify({ clicked: m.text, idx: m.idx });
})();
