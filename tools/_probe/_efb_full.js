(function () {
  var s = window.__MSFSBA_HS787_EFB.scrape();
  var out = ['TITLE: ' + (s.title||'?') + '   ELEMENTS: ' + s.elements.length];
  for (var i = 0; i < s.elements.length; i++) {
    var e = s.elements[i];
    out.push(i + ' [' + e.kind + (e.disabled?' DIM':'') + '] ' + e.label + (e.value?' = '+e.value:''));
  }
  return out.join('\n');
})()
