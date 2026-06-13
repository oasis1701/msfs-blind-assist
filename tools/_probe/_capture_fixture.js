// Serialize the current MFD page as an offline test FIXTURE: stamp each element
// with its LIVE visibility (data-vis) and geometry (data-rect="top,left,right,bot")
// so a jsdom test can faithfully replay the real scrape without layout.
(function () {
  var A = window.__MSFSBA_A380;
  // Capture the EXACT root the live scrape uses (findRoot), NOT a broad ancestor —
  // .mfd-navigator-container spans all (mounted, visited) tabs and would leak
  // inactive-tab content into the fixture. findRoot scopes to the active page.
  var page = A.findRoot(A.activeMcdu);
  if (!page) return "NO_ROOT";
  var all = page.getElementsByTagName("*");
  function stamp(el) {
    if (A.isVisible(el)) el.setAttribute("data-vis", "1");
    var r = el.getBoundingClientRect();
    el.setAttribute("data-rect", Math.round(r.top) + "," + Math.round(r.left) + "," + Math.round(r.right) + "," + Math.round(r.bottom));
  }
  stamp(page);
  for (var i = 0; i < all.length; i++) stamp(all[i]);
  return page.outerHTML;
})();
