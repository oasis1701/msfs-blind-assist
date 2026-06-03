(function () {
  try {
    function rd(n) { try { return SimVar.GetSimVarValue('L:' + n, 'number'); } catch (e) { return 'ERR'; } }
    // Collect visible leaf text nodes with positions (mini-scrape), like the display agent.
    var leaves = [];
    var all = document.body ? document.body.querySelectorAll('*') : [];
    for (var i = 0; i < all.length; i++) {
      var el = all[i];
      if (el.children && el.children.length > 0) continue; // leaf only
      var t = (el.textContent || '').replace(/\s+/g, ' ').trim();
      if (!t) continue;
      var r = el.getBoundingClientRect();
      leaves.push({ t: t, x: Math.round(r.left), y: Math.round(r.top), cls: (el.getAttribute('class') || '').slice(0, 40) });
    }
    leaves.sort(function (a, b) { return (a.y - b.y) || (a.x - b.x); });
    return JSON.stringify({
      state1: rd('A32NX_RMP_1_STATE'),
      bright1: rd('A380X_RMP_1_BRIGHTNESS'),
      bodyLen: document.body ? document.body.innerText.length : -1,
      leafCount: leaves.length,
      leaves: leaves.slice(0, 60)
    });
  } catch (e) { return 'PROBE_ERR: ' + e; }
})()
