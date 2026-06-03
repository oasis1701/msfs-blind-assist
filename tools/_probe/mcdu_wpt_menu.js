(async function () {
  var A = window.__MSFSBA_A380;
  function delay(ms) { return new Promise(function (r) { setTimeout(r, ms); }); }
  var s1 = JSON.parse(A.scrape());            // stamp idx
  var wp = (s1.elements || []).filter(function (e) { return e.kind === 'fplnwpt'; })[0];
  if (!wp) return JSON.stringify({ err: 'no fplnwpt', title: s1.title, els: (s1.elements || []).map(function (e) { return { kind: e.kind, text: e.text }; }) });
  A.clickElement(wp.idx);                      // open the lateral-revision menu
  await delay(700);
  var s2 = JSON.parse(A.scrape());
  return JSON.stringify({
    clickedWaypoint: wp.text,
    menuTitle: s2.title,
    menu: (s2.elements || []).map(function (e) { return { kind: e.kind, idx: e.idx, text: e.text, expand: e.expandstate }; })
  });
})();
