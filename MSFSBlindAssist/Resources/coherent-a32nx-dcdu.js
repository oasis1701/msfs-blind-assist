// MSFSBA — one-shot DCDU scrape for the FlyByWire A32NX (CPDLC display).
// Evaluated via CoherentEvalClient against the "DCDU" Coherent view (no
// persistent socket on the A32NX by policy — each refresh is a fresh eval).
// ES5 only (Coherent GT = Chromium 49).
//
// Output mirrors the MCDU-window model: rows in screen order, with the soft
// keys folded INTO their row ({t:'keys', l, c, r} — left label, row content,
// right label; the form renders them positionally like FbwMcduFormat lines,
// so "RECALL*" right-aligns exactly as on the unit and the star marks the
// adjacent key). Content-only rows come back verbatim ({t:'plain', txt}).
//
// Soft-key SLOT mapping is by POSITION, not order: Button.tsx places slot-1
// text at y=2240 and slot-2 at y=2720 in the ~2880-unit view, so the slot is
// the key's Y normalized against the dcdu svg rect (threshold 0.86). Order-
// based mapping is WRONG when only one key renders on a side — the
// empty-state RECALL is alone on the right but lives on R2.
(function () {
  try {
    var btns = { L1: '', L2: '', R1: '', R2: '' };
    var entries = [];
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
        if (btns[key]) key = side + (slot === '1' ? '2' : '1');
        btns[key] = txt;
        entries.push({ x: r.left, y: r.top, h: r.height || 0, txt: txt, btn: side });
      } else {
        entries.push({ x: r.left, y: r.top, h: r.height || 0, txt: txt, btn: '' });
      }
    }
    entries.sort(function (a, b) { return (a.y - b.y) || (a.x - b.x); });
    // Y-cluster into rows (tolerance from the text's own height so the scrape
    // is resolution-independent), folding any soft key into its row.
    var rows = [];
    var cur = null;
    var lastY = -1e9;
    function flush() {
      if (!cur) return;
      if (cur.l || cur.r) rows.push({ t: 'keys', l: cur.l, c: cur.c.join(' '), r: cur.r });
      else if (cur.c.length) rows.push({ t: 'plain', txt: cur.c.join(' ') });
      cur = null;
    }
    for (var j = 0; j < entries.length; j++) {
      var it = entries[j];
      var tol = it.h > 0 ? it.h * 0.7 : 40;
      if (!cur || (it.y - lastY) > tol) { flush(); cur = { l: '', c: [], r: '' }; }
      lastY = it.y;
      if (it.btn === 'L') cur.l = it.txt;
      else if (it.btn === 'R') cur.r = it.txt;
      else cur.c.push(it.txt);
    }
    flush();
    return JSON.stringify({ ok: true, rows: rows, btns: btns });
  } catch (e) {
    return JSON.stringify({ ok: false, err: '' + e.message });
  }
})()
