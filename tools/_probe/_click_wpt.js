(function () {
  var A = window.__MSFSBA_A380;
  var s = JSON.parse(A.scrape());
  var m = (s.elements || []).filter(function (e) { return e.kind === 'input' && /WPT/i.test(e.text); })[0];
  if (!m) return JSON.stringify({ err: 'no WPT input', els: (s.elements || []).map(function (e) { return e.kind + '|' + e.text; }) });
  A.clickElement(m.idx);
  return JSON.stringify({ clicked: m.text, idx: m.idx });
})();
