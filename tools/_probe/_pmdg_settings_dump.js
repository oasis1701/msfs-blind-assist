// Dump the full PMDG EFB scrape as readable lines (one per element) for analysis.
(function () {
  var r = JSON.parse(window.__MSFSBA_PMDG_EFB.scrape());
  if (!r.ok) return 'SCRAPE_ERR: ' + r.error;
  var lines = ['side=' + r.side + ' n=' + r.elements.length];
  for (var i = 0; i < r.elements.length; i++) {
    var e = r.elements[i];
    var role = e.controlType ? ('[' + e.controlType + ']') : ('<' + (e.kind || '?') + '>');
    var val = (e.value !== undefined && e.value !== '') ? ('  ={' + e.value + '}') : '';
    var opt = (e.options && e.options.length) ? ('  opts=[' + e.options.join('|') + ']') : '';
    var lvl = e.level ? ('  h' + e.level) : '';
    var dis = e.disabled ? '  DISABLED' : '';
    lines.push(('  ' + i).slice(-4) + ' ' + role + lvl + ' "' + (e.text || '') + '"' + val + opt + dis);
  }
  return lines.join('\n');
})();
