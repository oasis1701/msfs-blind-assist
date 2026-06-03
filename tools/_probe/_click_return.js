(function () {
  var A = window.__MSFSBA_A380;
  var s = JSON.parse(A.scrape());
  var m = (s.elements || []).filter(function (e) { return /^RETURN$/i.test((e.text || '').trim()); })[0];
  if (!m) return JSON.stringify({ err: 'no RETURN', title: s.title });
  A.clickElement(m.idx);
  return JSON.stringify({ clicked: m.text, idx: m.idx });
})();
