(function () {
  if (!window.__MSFSBA_HS787_EFB) return 'AGENT MISSING';
  var s = window.__MSFSBA_HS787_EFB.scrape();
  var out = ['TITLE: ' + (s.title || '(none)'), 'ELEMENTS: ' + s.elements.length];
  for (var i = 0; i < s.elements.length && i < 25; i++) {
    var e = s.elements[i];
    out.push(e.idx + ' [' + e.kind + (e.disabled ? ' DIM' : '') + '] ' + e.label + (e.value ? ' = ' + e.value : ''));
  }
  return out.join('\n');
})()
