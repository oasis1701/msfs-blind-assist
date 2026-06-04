(function () {
  var out = { tags: [], hints: {} };
  var seen = {};
  var all = document.getElementsByTagName('*');
  for (var i = 0; i < all.length; i++) { var t = all[i].tagName.toLowerCase(); if (t.indexOf('-') >= 0 && !seen[t]) { seen[t] = 1; out.tags.push(t); } }
  function probe(label, fn) {
    try {
      var v = fn();
      if (v == null) { out.hints[label] = "null"; return; }
      var keys = (typeof v === 'object') ? Object.keys(v).slice(0, 40) : [];
      out.hints[label] = (typeof v) + (keys.length ? " {" + keys.join(",") + "}" : "");
    } catch (e) { out.hints[label] = "err:" + e.message; }
  }
  out.tags.forEach(function (t) {
    probe(t + ".fsInstrument", function () { var el = document.querySelector(t); return el && el.fsInstrument; });
  });
  return JSON.stringify(out);
})();
