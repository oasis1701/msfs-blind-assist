// MSFSBA — one-shot DCDU scrape for the FlyByWire A32NX (CPDLC display).
// Evaluated via CoherentEvalClient against the "DCDU" Coherent view (no
// persistent socket on the A32NX by policy — each refresh is a fresh eval).
// ES5 only (Coherent GT = Chromium 49).
//
// The DCDU renders SVG <text> elements: message/status content plus the four
// soft keys (class "button button-left/right", e.g. "WILCO*" / "*STBY").
// Button slot mapping: within each side, the upper text is slot 1 and the
// lower slot 2 (Button.tsx positions L1 at y=2240 and L2 at y=2240+480).
// Rows are rebuilt by Y-clustering with a tolerance derived from the text's
// own height so the scrape is resolution-independent.
(function () {
  try {
    var btnLeft = [];
    var btnRight = [];
    var items = [];
    var texts = document.querySelectorAll('text');
    for (var i = 0; i < texts.length; i++) {
      var t = texts[i];
      var txt = (t.textContent || '').replace(/\s+/g, ' ').trim();
      if (!txt) continue;
      var cls = t.getAttribute('class') || '';
      var r = t.getBoundingClientRect();
      if (cls.indexOf('button') !== -1) {
        var rec = { y: r.top, txt: txt };
        if (cls.indexOf('button-left') !== -1) btnLeft.push(rec);
        else btnRight.push(rec);
      } else {
        items.push({ x: r.left, y: r.top, h: r.height || 0, txt: txt });
      }
    }
    function byY(a, b) { return a.y - b.y; }
    btnLeft.sort(byY);
    btnRight.sort(byY);
    var btns = {
      L1: btnLeft[0] ? btnLeft[0].txt : '',
      L2: btnLeft[1] ? btnLeft[1].txt : '',
      R1: btnRight[0] ? btnRight[0].txt : '',
      R2: btnRight[1] ? btnRight[1].txt : ''
    };
    items.sort(function (a, b) { return (a.y - b.y) || (a.x - b.x); });
    var rows = [];
    var line = '';
    var lastY = -1e9;
    for (var j = 0; j < items.length; j++) {
      var it = items[j];
      var tol = it.h > 0 ? it.h * 0.7 : 40;
      if (line && (it.y - lastY) > tol) { rows.push(line); line = ''; }
      lastY = it.y;
      line += (line ? ' ' : '') + it.txt;
    }
    if (line) rows.push(line);
    return JSON.stringify({ ok: true, rows: rows, btns: btns });
  } catch (e) {
    return JSON.stringify({ ok: false, err: '' + e.message });
  }
})()
