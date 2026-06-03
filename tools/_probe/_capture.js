// Generic MFD capture: our emitted scrape lines + the raw rendered innerText.
(function () {
  var A = window.__MSFSBA_A380;
  var s = JSON.parse(A.scrape());
  var root = A.findRoot(A.activeMcdu);
  return JSON.stringify({
    mcdu: A.activeMcdu,
    title: s.title,
    scratch: s.scratchpad,
    emitted: (s.elements || []).map(function (e) { return { k: e.kind, i: e.idx, t: e.text, x: e.expandstate }; }),
    rawText: root ? root.innerText.replace(/ /g, ' ') : null
  }, null, 1);
})();
