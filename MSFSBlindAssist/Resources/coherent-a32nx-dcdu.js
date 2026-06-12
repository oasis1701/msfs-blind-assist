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
    // A multi-line DCDU message is ONE <text> whose <tspan> children are the
    // visual lines — textContent concatenates them WITHOUT separators ("RECALL
    // EMPTYCONSULT MSG RECORD"), so content tspans are emitted as their own
    // items with their own rects and cluster into proper rows.
    function wholeText(node) {
      var spans = node.querySelectorAll('tspan');
      if (spans.length === 0) return (node.textContent || '').replace(/\s+/g, ' ').trim();
      var parts = [];
      for (var s = 0; s < spans.length; s++) {
        var st = (spans[s].textContent || '').replace(/\s+/g, ' ').trim();
        if (st) parts.push(st);
      }
      return parts.join(' ');
    }
    var texts = document.querySelectorAll('text');
    for (var i = 0; i < texts.length; i++) {
      var t = texts[i];
      var cls = t.getAttribute('class') || '';
      var r = t.getBoundingClientRect();
      if (cls.indexOf('button') !== -1) {
        var txt = wholeText(t);
        if (!txt) continue;
        var side = cls.indexOf('button-left') !== -1 ? 'L' : 'R';
        var rel = sr && sr.height > 0 ? (r.top - sr.top) / sr.height : 1;
        var slot = rel > 0.86 ? '2' : '1';
        var key = side + slot;
        if (btns[key]) { slot = slot === '1' ? '2' : '1'; key = side + slot; }
        // Strip the asterisk — it is the unit's own "key armed" marker (real
        // Airbus DCDU convention); the accessible rendering replaces it with
        // the MCDU arrow convention ("<" = left key, ">" = right key) plus the
        // key number at the line start, so the marker would be noise.
        var label = txt.replace(/\*/g, '').trim();
        btns[key] = label;
        entries.push({ x: r.left, y: r.top, h: r.height || 0, txt: label, btn: side, slot: slot });
      } else {
        var spans2 = t.querySelectorAll('tspan');
        if (spans2.length > 1) {
          for (var k = 0; k < spans2.length; k++) {
            var stxt = (spans2[k].textContent || '').replace(/\s+/g, ' ').trim();
            if (!stxt) continue;
            var srct = spans2[k].getBoundingClientRect();
            entries.push({ x: srct.left, y: srct.top, h: srct.height || 0, txt: stxt, btn: '' });
          }
        } else {
          var ptxt = (t.textContent || '').replace(/\s+/g, ' ').trim();
          if (!ptxt) continue;
          entries.push({ x: r.left, y: r.top, h: r.height || 0, txt: ptxt, btn: '' });
        }
      }
    }
    entries.sort(function (a, b) { return (a.y - b.y) || (a.x - b.x); });
    // Y-cluster into rows (tolerance from the text's own height so the scrape
    // is resolution-independent), folding any soft key into its row.
    // Key rows render as "1 <STBY" (left key 1) / "2          RECALL>" (right
    // key 2, right-aligned): the key NUMBER leads the line, "<" marks a left
    // key and ">" a right key — the chord is the number with Ctrl (left) or
    // Alt (right), or F1/F2 + F7/F8 in alternate mode.
    var rows = [];
    var cur = null;
    var lastY = -1e9;
    function flush() {
      if (!cur) return;
      if (cur.lTxt || cur.rTxt) {
        var digit = cur.lTxt ? cur.lSlot : cur.rSlot;
        var l = cur.lTxt ? digit + ' <' + cur.lTxt : digit;
        var r = cur.rTxt ? cur.rTxt + '>' : '';
        rows.push({ t: 'keys', l: l, c: cur.c.join(' '), r: r });
      } else if (cur.c.length) {
        rows.push({ t: 'plain', txt: cur.c.join(' ') });
      }
      cur = null;
    }
    for (var j = 0; j < entries.length; j++) {
      var it = entries[j];
      var tol = it.h > 0 ? it.h * 0.7 : 40;
      if (!cur || (it.y - lastY) > tol) { flush(); cur = { lTxt: '', lSlot: '', rTxt: '', rSlot: '', c: [] }; }
      lastY = it.y;
      if (it.btn === 'L') { cur.lTxt = it.txt; cur.lSlot = it.slot; }
      else if (it.btn === 'R') { cur.rTxt = it.txt; cur.rSlot = it.slot; }
      else cur.c.push(it.txt);
    }
    flush();
    return JSON.stringify({ ok: true, rows: rows, btns: btns });
  } catch (e) {
    return JSON.stringify({ ok: false, err: '' + e.message });
  }
})()
