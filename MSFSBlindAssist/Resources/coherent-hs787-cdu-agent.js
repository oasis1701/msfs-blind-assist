// HorizonSim 787-9 CDU agent — read/drive the WT Boeing FMC over the Coherent GT
// remote debugger (port 19999), REPLACING the old HTTP-injection bridge
// (hs787-mfd-bridge.js + EFBBridgeServer). Installed into the HSB789_MFD_3 view via
// CoherentDebuggerClient; exposes window.__MSFSBA_HS787.
//
// ES5 ONLY (Coherent GT = old Chromium): var, no arrow funcs, no String.includes,
// top-level try/catch. Same DOM contract as the legacy bridge (.wt787-cdu / .fmc-row /
// .fmc-letter / .wt787-cdu-scratchpad / .wt787-cdu-button), just over a different
// transport — so the proven scrape/click logic carries over verbatim.
(function () {
  try {
    var A = {};

    // Read one .fmc-row into a string, marking selectable (LSK-arrow) highlighted cells with "X ".
    A.readLetters = function (container, allowHighlight) {
      var letters = container.querySelectorAll('.fmc-letter');
      if (!letters || letters.length === 0) return (container.textContent || '').replace(/\s+$/, '');
      var line = '', prevHi = false, marked = false;
      for (var c = 0; c < letters.length; c++) {
        var el = letters[c], cls = el.className || '';
        var hi = cls.indexOf('green') !== -1 || cls.indexOf('magenta') !== -1;
        var ch = el.textContent || ' ';
        if (hi !== prevHi) marked = false;
        if (hi && !marked && ch.replace(/\s/g, '') !== '' && allowHighlight) { line += 'X '; marked = true; }
        prevHi = hi; line += ch;
      }
      return line.replace(/\s+$/, '');
    };

    A.cduRoot = function () { return document.querySelector('.wt787-cdu'); };

    // Returns the 13 screen rows + scratchpad (14 entries), or null if the CDU isn't shown here.
    A.scrape = function () {
      var cdu = A.cduRoot();
      if (!cdu) return { ok: true, visible: false, rows: [] };
      var screen = cdu.querySelector('#fmc-container') || cdu.querySelector('.wt787-cdu-screen');
      if (!screen) return { ok: true, visible: false, rows: [] };
      var rows = screen.querySelectorAll('.fmc-row');
      if (!rows || rows.length === 0) return { ok: true, visible: false, rows: [] };
      var n = Math.min(rows.length, 13), lines = [];
      for (var r = 0; r < n; r++) {
        var t = rows[r].textContent || '';
        var arrow = t.indexOf('<') !== -1 || t.indexOf('>') !== -1;
        lines.push(A.readLetters(rows[r], arrow));
      }
      while (lines.length < 13) lines.push('');
      var sp = '', spEl = cdu.querySelector('.wt787-cdu-scratchpad');
      if (spEl) sp = (spEl.textContent || '').replace(/^\s+|\s+$/g, '');
      lines.push(sp);
      return { ok: true, visible: true, rows: lines, scratchpad: sp };
    };

    A.clickElement = function (el) {
      if (!el) return;
      var o = { bubbles: true, cancelable: true, view: window };
      // The WT boeing-mfd-button component (LSKs AND the page-key buttons) reacts to MOUSE
      // events, not pointer/click alone — the page keys (INIT REF / RTE / LEGS / DEP ARR / …)
      // only navigate on mousedown+mouseup. Dispatch the full sequence so every CDU button works.
      try { el.dispatchEvent(new PointerEvent('pointerdown', o)); } catch (e) {}
      try { el.dispatchEvent(new MouseEvent('mousedown', o)); } catch (e) {}
      try { el.dispatchEvent(new PointerEvent('pointerup', o)); } catch (e) {}
      try { el.dispatchEvent(new MouseEvent('mouseup', o)); } catch (e) {}
      try { el.dispatchEvent(new MouseEvent('click', o)); } catch (e) {}
    };

    A.clickLsk = function (side, n) {
      var cdu = A.cduRoot(); if (!cdu) return false;
      var col = side === 'R' ? '.wt787-cdu-lsk-column-right' : '.wt787-cdu-lsk-column-left';
      var lsks = cdu.querySelectorAll(col + ' .wt787-cdu-lsk');
      if (lsks.length < n) return false;
      var lsk = lsks[n - 1];
      A.clickElement(lsk.querySelector('.wt787-cdu-button') || lsk);
      return true;
    };

    // Page buttons outside the LSK columns, DOM order (from the WT render source):
    // 0 INIT/REF 1 RTE 2 DEP/ARR 3 ALTN 4 VNAV 5 EXEC 6 FIX 7 LEGS 8 HOLD
    // 9 FMC/COMM 10 PROG 11 NAV/RAD 12 OFST* 13 RTA* 14 PREV PAGE 15 NEXT PAGE
    // The named pages 0..11 are stable absolute indices (always rendered, ahead of the optional
    // ones). PREV/NEXT PAGE, however, are the LAST two buttons in DOM order — resolve them relative
    // to the END of the list, NOT by a fixed 14/15, because the OFST*/RTA* buttons (12/13) are
    // conditional: when WT omits them the absolute 14/15 fall off the end and PREV/NEXT silently
    // no-op (the user can't page multi-page screens like LEGS/FIX).
    A.PAGE = { INIT_REF: 0, RTE: 1, DEP_ARR: 2, ALTN: 3, VNAV: 4, EXEC: 5, FIX: 6, LEGS: 7,
               HOLD: 8, FMC_COMM: 9, PROG: 10, NAV_RAD: 11 };
    A.clickPage = function (key) {
      var cdu = A.cduRoot(); if (!cdu) return false;
      var all = cdu.querySelectorAll('.wt787-cdu-button'), page = [];
      for (var i = 0; i < all.length; i++) {
        var b = all[i];
        if (!(b.closest('.wt787-cdu-lsk-column-left') || b.closest('.wt787-cdu-lsk-column-right'))) page.push(b);
      }
      var idx;
      if (key === 'PREV_PAGE') idx = page.length - 2;
      else if (key === 'NEXT_PAGE') idx = page.length - 1;
      else idx = A.PAGE[key];
      if (idx === undefined || idx < 0 || page.length <= idx) return false;
      A.clickElement(page[idx]); return true;
    };

    // Fire a CDU alphanumeric/function key H-event (the WT FMC consumes these even when the
    // DOM key isn't trivially clickable). type_key -> AS01B_FMC_1_BTN_<KEY>.
    A.typeKey = function (key) {
      try { SimVar.SetSimVarValue('H:AS01B_FMC_1_BTN_' + key, 'number', 1); return true; } catch (e) { return false; }
    };
    A.fcuKey = function (key) {
      try { SimVar.SetSimVarValue('H:AS01B_FMC_1_' + key, 'number', 1); return true; } catch (e) { return false; }
    };

    A.ping = function () { return { ok: true, cdu: !!A.cduRoot() }; };

    window.__MSFSBA_HS787 = A;
    return 'MSFSBA_HS787_INSTALLED';
  } catch (e) {
    return 'MSFSBA_HS787_ERROR:' + (e && e.message);
  }
})()
