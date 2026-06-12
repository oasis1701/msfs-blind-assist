// MSFSBA — one-shot DCDU scrape for the FlyByWire A32NX (CPDLC display).
// Evaluated via CoherentEvalClient against the "DCDU" Coherent view (no
// persistent socket on the A32NX by policy — each refresh is a fresh eval).
// ES5 only (Coherent GT = Chromium 49).
//
// The DCDU renders SVG <text> elements: message/status content plus the four
// soft keys (class "button button-left/right", e.g. "WILCO*" / "*STBY").
// Button slot mapping is by POSITION, not order: Button.tsx places slot-1 text
// at y=2240 and slot-2 at y=2720 in the ~2880-unit view, so the slot is read
// from the key's Y normalized against the dcdu svg's bounding rect (threshold
// 0.86). Order-based mapping is WRONG when only one key renders on a side —
// e.g. the empty-state RECALL is alone on the right but lives on slot R2
// (RecallButtons.tsx index="R2"), and firing R1 for it presses nothing.
// Rows are rebuilt by Y-clustering with a tolerance derived from the text's
// own height so the scrape is resolution-independent.
(function () {
  try {
    var btns = { L1: '', L2: '', R1: '', R2: '' };
    var items = [];
    var svg = document.querySelector('svg.dcdu');
    var sr = svg ? svg.getBoundingClientRect() : null;
    var texts = document.querySelectorAll('text');
    for (var i = 0; i < texts.length; i++) {
      var t = texts[i];
      var txt = (t.textContent || '').replace(/\s+/g, ' ').trim();
      if (!txt) continue;
      var cls = t.getAttribute('class') || '';
      var r = t.getBoundingClientRect();
      if (cls.indexOf('button') !== -1) {
        var side = cls.indexOf('button-left') !== -1 ? 'L' : 'R';
        var rel = sr && sr.height > 0 ? (r.top - sr.top) / sr.height : 1;
        var slot = rel > 0.86 ? '2' : '1';
        var key = side + slot;
        // Collision safety: if the computed slot is taken, use the other one.
        if (btns[key]) key = side + (slot === '1' ? '2' : '1');
        btns[key] = txt;
      } else {
        items.push({ x: r.left, y: r.top, h: r.height || 0, txt: txt });
      }
    }
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
