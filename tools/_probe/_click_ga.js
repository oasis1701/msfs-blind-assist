(function () {
  var A = window.__MSFSBA_A380;
  var s = JSON.parse(A.scrape());
  var m = (s.elements || []).filter(function (e) { return e.kind === 'subtab' && /^GA\b/.test((e.text || '').replace(/\s*\(active\)/, '').trim()); })[0];
  if (!m) return JSON.stringify({ err: 'no GA subtab' });
  A.clickElement(m.idx);
  return JSON.stringify({ clicked: m.text });
})();
