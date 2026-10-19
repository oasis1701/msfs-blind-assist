// Fixture capture for the sim tablet reader (MSFS 2024's own EFB, Coherent view
// "Electronic Flight Bag"). Run inside the view; it returns the tablet's markup as a jsdom
// fixture with the live state baked in, the same convention as tools/md11-efb-test:
//   data-vis="1"         the element is rendered (no attribute = hidden)
//   data-rect="t,l,r,b"  its live bounding box
//   data-h="click"       the element carries an onclick handler ("mousedown" for onmousedown;
//                        both names, comma-joined, when it has both)
// Inputs carry their LIVE value/checked as attributes. SVGs are reduced to one empty <svg> naming
// the icon (data-icon = the first id inside it, data-title = its <title>), which keeps a page to
// tens of kilobytes. ES5: Coherent GT is Chromium 49.
//
//   pwsh -File tools/coherent-eval.ps1 -Title "Electronic Flight Bag" -ExprFile tools/sim-tablet-test/capture.js > out.txt
// then keep the last line of out.txt as fixtures/<page>.html.
(function () {
  try {
    function hidden(el) {
      var r = el.getBoundingClientRect();
      if (r.width <= 0 && r.height <= 0) return true;
      var st = window.getComputedStyle(el);
      return !st || st.display === 'none' || st.visibility === 'hidden' || st.opacity === '0';
    }
    function esc(s) {
      return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }
    function attrs(el, visible) {
      var out = '';
      var keep = ['class', 'id', 'type', 'placeholder', 'disabled', 'inputmode', 'arguments', 'icon-url', 'src'];
      for (var i = 0; i < keep.length; i++) {
        var v = el.getAttribute(keep[i]);
        if (v !== null && v !== '') out += ' ' + keep[i] + '="' + esc(v) + '"';
      }
      if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT') {
        out += ' value="' + esc(el.value == null ? '' : el.value) + '"';
        if (el.checked) out += ' checked';
      }
      var h = [];
      if (typeof el.onclick === 'function') h.push('click');
      if (typeof el.onmousedown === 'function') h.push('mousedown');
      if (h.length) out += ' data-h="' + h.join(',') + '"';
      if (visible) {
        var r = el.getBoundingClientRect();
        out += ' data-vis="1" data-rect="' + Math.round(r.top) + ',' + Math.round(r.left) + ',' + Math.round(r.right) + ',' + Math.round(r.bottom) + '"';
      }
      return out;
    }
    function ser(el, hiddenAbove) {
      var tag = el.tagName.toLowerCase();
      var visible = !hiddenAbove && !hidden(el);
      if (tag === 'svg') {
        var idEl = el.querySelector('[id]');
        var t = el.querySelector('title');
        var a = attrs(el, visible).replace(/ id="[^"]*"/, '');
        if (el.id) a += ' data-icon="' + esc(el.id) + '"';
        else if (idEl) a += ' data-icon="' + esc(idEl.id) + '"';
        if (t) a += ' data-title="' + esc(t.textContent) + '"';
        return '<svg' + a + '></svg>';
      }
      if (tag === 'script' || tag === 'style' || tag === 'canvas') return '<' + tag + '></' + tag + '>';
      var s = '<' + tag + attrs(el, visible) + '>';
      // An app stays mounted once opened, hidden behind the one on screen, and so does every page an
      // app has shown (a hidden .ui-view) and every Flight Planner panel (a hidden .view in a
      // .view-stack). Their subtrees are dropped (the element is kept, empty, marked data-elided):
      // the reader never walks a hidden view, and keeping them made every fixture ~375 KB. A hidden
      // .list is NOT elided: dropdown options and search results are read while hidden.
      var cls = ' ' + el.className + ' ';
      var parentCls = ' ' + ((el.parentElement && el.parentElement.className) || '') + ' ';
      var hiddenView = cls.indexOf(' efb-view ') >= 0 || cls.indexOf(' ui-view ') >= 0 ||
        (cls.indexOf(' view ') >= 0 && parentCls.indexOf(' view-stack ') >= 0);
      if (!visible && hiddenView) return s.replace(/>$/, ' data-elided="1">') + '</' + tag + '>';
      for (var i = 0; i < el.childNodes.length; i++) {
        var n = el.childNodes[i];
        if (n.nodeType === 3) s += esc(n.nodeValue);
        else if (n.nodeType === 1) s += ser(n, !visible);
      }
      if (tag === 'input' || tag === 'img' || tag === 'br') return s;
      return s + '</' + tag + '>';
    }
    var root = document.querySelector('.efb') || document.body;
    return ser(root, false);
  } catch (e) {
    return 'ERR ' + e + ' ' + (e && e.stack);
  }
})()
