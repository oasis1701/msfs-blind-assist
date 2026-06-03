(function () {
  var s = JSON.parse(window.__MSFSBA_A380.scrape());
  return JSON.stringify({
    title: s.title,
    scratch: s.scratchpad,
    els: (s.elements || []).map(function (e) { return { k: e.kind, i: e.idx, t: e.text, x: e.expandstate }; })
  });
})();
