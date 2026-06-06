// Serialize the current flyPad page as an offline jsdom fixture: stamp each
// element with live visibility (data-vis) and geometry (data-rect) so the
// scrape replays faithfully without a layout engine. Mirrors the MFD
// _capture_fixture.js but for the flyPad agent (window.__MSFSBA_FLYPAD).
(function () {
  var A = window.__MSFSBA_FLYPAD;
  if (!A) return "NO_AGENT";
  var page = A.findRoot();
  if (!page) return "NO_ROOT";
  function stamp(el) {
    if (A.isVisible(el)) el.setAttribute("data-vis", "1");
    var r = el.getBoundingClientRect();
    el.setAttribute("data-rect", Math.round(r.top) + "," + Math.round(r.left) + "," + Math.round(r.right) + "," + Math.round(r.bottom));
  }
  stamp(page);
  var all = page.getElementsByTagName("*");
  for (var i = 0; i < all.length; i++) stamp(all[i]);
  return page.outerHTML;
})();
