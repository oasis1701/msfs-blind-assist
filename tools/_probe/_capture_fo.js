(function () {
  var A = window.__MSFSBA_A380;
  var s = JSON.parse(A.scrape(2));            // FO root
  var root = A.findRoot(2);
  return JSON.stringify({
    mcdu: A.activeMcdu, title: s.title,
    emitted: (s.elements || []).slice(0, 14).map(function (e) { return e.kind + '|' + e.text; }),
    rawHead: root ? root.innerText.slice(0, 120).replace(/\n/g, ' / ') : null
  });
})();
