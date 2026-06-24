(function () {
  function txtOf(root) {
    var s = '';
    var t = root.querySelectorAll('text, tspan, .fmc-letter, .fmc-row');
    for (var i = 0; i < t.length && s.length < 700; i++) {
      var x = (t[i].textContent || '').trim();
      if (x) s += x + ' ';
    }
    return s.replace(/\s+/g, ' ').trim();
  }
  return JSON.stringify({
    title: document.title,
    bodyInner: (document.body ? document.body.innerText.replace(/\s+/g,' ').trim().length : 0),
    fmcRows: document.querySelectorAll('.fmc-row').length,
    fmcLetters: document.querySelectorAll('.fmc-letter').length,
    svgTexts: document.querySelectorAll('text,tspan').length,
    sampleText: txtOf(document).slice(0, 600)
  });
})()
