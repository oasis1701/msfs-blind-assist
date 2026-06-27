// HorizonSim 787-9 IRS-alignment agent — reads the WT "TIME TO ALIGN" element over the
// Coherent GT debugger (the ND/PFD view, e.g. HSB789_PFD) and writes the synthetic L-vars
//   L:MSFSBA_IRS_ALIGN_STATE     0 = Off   1 = Aligning   2 = Aligned (Navigation)
//   L:MSFSBA_IRS_ALIGN_MINUTES   minutes remaining, -1 = n/a
// which the MSFSBA HorizonSim787Definition reads through its normal SimVar monitoring.
//
// This REPLACES the old hs787-mfd-bridge.js `pollIrsAlign` scrape (the HTTP bridge was
// retired). The state machine + latches are a faithful port — the WT 787 exposes true,
// Realistic-respecting alignment ONLY on this DOM element (the only IRS L-var WT itself
// sets is WT_IRS_POS_SET_N = "position accepted", NOT alignment complete).
//
// CRITICAL: a Coherent `Runtime.evaluate` writes L-vars fine (verified: a write here is
// read back via SimConnect/MobiFlight), but a SimVar READ in the SAME eval returns the
// pre-write value — so poll() never relies on reading back what it just wrote.
//
// ES5 ONLY (Coherent GT = old Chromium): var, no arrow funcs, top-level try/catch. The
// latches (sawVisible / posSetSeenZero / prev*) live on this window global so they persist
// across the client's repeated poll() calls (a fresh per-call eval would reset them).
(function () {
  try {
    var A = window.__MSFSBA_HS787_IRS || {};

    function getL(n) { try { return SimVar.GetSimVarValue('L:' + n, 'number'); } catch (e) { return 0; } }
    function setL(n, v) {
      try {
        if (typeof SimVar !== 'undefined' && SimVar.SetSimVarValue) { SimVar.SetSimVarValue('L:' + n, 'number', v); return true; }
      } catch (e) {}
      return false;
    }

    if (typeof A.prevState === 'undefined') A.prevState = -1;
    if (typeof A.prevMinutes === 'undefined') A.prevMinutes = -2;

    A.ping = function () { return { ok: true, hasElement: !!document.querySelector('.time-to-align') }; };

    A.poll = function () {
      try {
        var el = document.querySelector('.time-to-align');
        if (!el) return { ok: false, reason: 'no-element' };   // not the ND/PFD view

        var knobOn = getL('B787_IRS_Knob_State:1') > 0 || getL('B787_IRS_Knob_State:2') > 0;
        // "Position accepted" — guards the brief just-turned-on transient (panel not yet
        // rendered). Any IRU's POS SET counts.
        var posSet = getL('WT_IRS_POS_SET_1') > 0 || getL('WT_IRS_POS_SET_2') > 0 || getL('WT_IRS_POS_SET_3') > 0;
        var visible = !(el.classList && el.classList.contains('hidden'));

        // Latch-free state machine (handles a ready-to-fly already-aligned start too —
        // no dependency on having witnessed the alignment transition):
        //   panel visible            -> Aligning (parse the countdown)
        //   knobs off                -> Off
        //   knobs on + position set   -> Aligned (the panel hides when alignment completes)
        //   knobs on, no pos yet      -> Aligning (just turned on / POS INIT pending)
        var state, minutes = -1;
        if (visible) {
          state = 1;
          var kids = el.querySelectorAll('div');
          var txt = kids.length ? (kids[kids.length - 1].textContent || '') : '';
          txt = txt.replace(/^\s+|\s+$/g, '');
          if (txt === '' || txt === '--') minutes = -1;
          else if (txt.indexOf('+') >= 0) { var p = parseInt(txt, 10); minutes = isNaN(p) ? 7 : p; }
          else { var n = parseInt(txt, 10); minutes = isNaN(n) ? -1 : n; }
        } else if (!knobOn) {
          state = 0;
        } else if (posSet) {
          state = 2;
        } else {
          state = 1;
        }

        if (state !== A.prevState) { setL('MSFSBA_IRS_ALIGN_STATE', state); A.prevState = state; }
        if (minutes !== A.prevMinutes) { setL('MSFSBA_IRS_ALIGN_MINUTES', minutes); A.prevMinutes = minutes; }

        return { ok: true, state: state, minutes: minutes, visible: visible, knobOn: knobOn, posSet: posSet };
      } catch (e) {
        return { ok: false, reason: 'exception:' + (e && e.message) };
      }
    };

    window.__MSFSBA_HS787_IRS = A;
    return 'MSFSBA_HS787_IRS_INSTALLED';
  } catch (e) {
    return 'MSFSBA_HS787_IRS_ERROR:' + (e && e.message);
  }
})()
