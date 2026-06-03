(function () {
  var A = window.__MSFSBA_A380;
  window.__seen = window.__seen || {};
  var s = JSON.parse(A.scrape());
  var subs = (s.elements || []).filter(function (e) { return e.kind === 'subtab'; });
  function nm(e) { return (e.text || '').replace(/\s*\(active\)/i, '').trim(); }
  subs.forEach(function (e) { if (/\(active\)/i.test(e.text || '')) window.__seen[nm(e)] = true; });
  var next = null;
  for (var i = 0; i < subs.length; i++) { if (!window.__seen[nm(subs[i])]) { next = subs[i]; break; } }
  if (!next) return JSON.stringify({ done: true, subs: subs.map(function (e) { return e.text; }) });
  A.clickElement(next.idx);
  return JSON.stringify({ clicked: next.text, idx: next.idx });
})();
