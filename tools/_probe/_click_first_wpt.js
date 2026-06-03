(function () {
  var A = window.__MSFSBA_A380;
  var s = JSON.parse(A.scrape());
  // skip pseudo-waypoints like "(T/C)", "(T/D)", "(DECEL)" — they have no revision menu
  var w = (s.elements || []).filter(function (e) {
    return e.kind === 'fplnwpt' && !/^\(/.test((e.text || '').replace(/^\s+/, ''));
  })[0];
  if (!w) return JSON.stringify({ err: 'no real waypoint' });
  A.clickElement(w.idx);
  return JSON.stringify({ clicked: w.text, idx: w.idx });
})();
