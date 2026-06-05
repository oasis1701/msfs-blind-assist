// Generic display leaf-text scrape for any Coherent view. Returns JSON rows
// (cluster leaf text by Y, sort by X) — used to recon HS787 displays.
(function () {
  var out = [];
  var all = document.querySelectorAll('*');
  for (var i = 0; i < all.length; i++) {
    var e = all[i];
    if (e.children.length === 0) {
      var t = (e.textContent || '').replace(/\s+/g, ' ').trim();
      if (t) {
        var r = e.getBoundingClientRect();
        if (r.width === 0 && r.height === 0) continue;
        out.push([Math.round(r.top), Math.round(r.left), t]);
      }
    }
  }
  out.sort(function (a, b) { return (a[0] - b[0]) || (a[1] - b[1]); });
  var rows = [];
  var cy = -9999, cur = null;
  for (var k = 0; k < out.length; k++) {
    if (Math.abs(out[k][0] - cy) > 12) { if (cur) rows.push(cur.join('  ')); cur = []; cy = out[k][0]; }
    cur.push(out[k][2]);
  }
  if (cur) rows.push(cur.join('  '));
  return JSON.stringify({ n: rows.length, rows: rows });
})()
