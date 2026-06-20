(function () {
  var out = { found: [] };
  var btns = document.querySelectorAll('.boeing-efb-button, .boeing-efb-dropdown-button, .boeing-efb-textfield-button');
  for (var i = 0; i < btns.length; i++) {
    var b = btns[i];
    var txt = (b.textContent || '').replace(/\s+/g, ' ').replace(/^\s+|\s+$/g, '');
    if (txt.indexOf('INITIALIZE') >= 0) {
      out.found.push({
        tag: b.tagName,
        cls: b.className,
        hasOnclick: !!b.onclick,
        attrs: (function () { var a = []; for (var k = 0; k < b.attributes.length; k++) a.push(b.attributes[k].name + '=' + b.attributes[k].value); return a; })(),
        childCount: b.children.length,
        outer: b.outerHTML.slice(0, 600)
      });
    }
  }
  return JSON.stringify(out);
})()
