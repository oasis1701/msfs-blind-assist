// coherent-rmp-agent.js — A380X RMP (Radio Management Panel) accessible scrape.
// ES5 ONLY (Coherent GT = Chromium 49: var, no arrow funcs, no String.includes,
// top-level try/catch). Installed by CoherentDisplayClient on the A380X_RMP_1 /
// A380X_RMP_2 view; exposes window.__MSFSBA_DISP.scrape() -> {ok, rows[]} (the same
// contract the generic display agent uses, so the existing client reads it unchanged).
//
// The RMP renders one page at a time (VHF/HF/TEL = transceiver rows; SQWK =
// transponder; MENU/NAV). For the frequency pages each transceiver row is a flat
// fragment of .active-frequency + .transceiver-ident + .stby-frequency-cell, rendered
// in DOM order, so we zip the three class lists by index. The .stby-frequency div's
// textContent already concatenates its 6 digit children + the dot into "121.950",
// and during entry it shows the live scratchpad ("118.000") — exactly what a blind
// pilot needs to hear as they type. reverse-video on the ident = transmit-selected;
// .selected on the cell = the row the keypad types into.
(function () {
  try {
    function vis(el) {
      if (!el) return false;
      var r = el.getBoundingClientRect();
      return r.width > 0 && r.height > 0;
    }
    function tight(el) { return el ? (el.textContent || '').replace(/\s+/g, '').trim() : ''; }
    function loose(el) { return el ? (el.textContent || '').replace(/\s+/g, ' ').trim() : ''; }

    function scrape() {
      try {
        var rows = [];
        var actives = document.querySelectorAll('.active-frequency');
        var idents = document.querySelectorAll('.transceiver-ident');
        var cells = document.querySelectorAll('.stby-frequency-cell');
        var n = Math.min(actives.length, idents.length, cells.length);
        for (var i = 0; i < n; i++) {
          var ident = tight(idents[i]);            // VHF1 / VHF2 / VHF3 / HF1 / TEL1 ...
          var active = tight(actives[i]);          // 122.800 / DATA / ---.---
          // standby = the VISIBLE .stby-frequency in this cell (VHF3 DATA hides the digits one)
          var sf = cells[i].querySelectorAll('.stby-frequency');
          var standby = '';
          for (var j = 0; j < sf.length; j++) { if (vis(sf[j])) { standby = tight(sf[j]); break; } }
          if (!standby && sf.length) standby = tight(sf[0]);
          var transmit = (idents[i].className || '').indexOf('reverse-video') >= 0;
          var selected = (cells[i].className || '').indexOf('selected') >= 0;
          var line = ident + ': active ' + active + ', standby ' + standby;
          if (transmit) line += ', transmit';
          if (selected) line += ', selected';
          rows.push(line);
        }

        // Transponder page (SQWK): mode + code + ident, if those leaves are visible.
        var tMode = document.querySelector('.transponder-mode');
        if (tMode && vis(tMode)) {
          var code = '';
          var codeEl = document.querySelector('.transponder-code, .squawk-code');
          if (codeEl) code = tight(codeEl);
          var tline = 'Transponder ' + tight(tMode);
          if (code) tline += ', code ' + code;
          var ident = document.querySelector('.transponder-ident');
          if (ident && vis(ident)) tline += ', ' + tight(ident);
          rows.push(tline);
        }

        // Bottom message line (e.g. "SQUAWK : 2000 - STBY", tuning errors).
        var msg = document.querySelector('.rmp-messages');
        if (msg && vis(msg)) { var m = loose(msg); if (m) rows.push('Message: ' + m); }

        if (rows.length === 0) rows.push('RMP page has no readable rows (powered? on VHF/HF/TEL/SQWK?).');
        return JSON.stringify({ ok: true, rows: rows });
      } catch (e) {
        return JSON.stringify({ ok: true, rows: ['RMP scrape error: ' + e] });
      }
    }

    window.__MSFSBA_DISP = { __rmp: true, scrape: scrape, ping: function () { return 'ok'; } };
    return 'MSFSBA_DISP_INSTALLED';
  } catch (e) {
    return 'MSFSBA_DISP_ERROR: ' + e;
  }
})();
