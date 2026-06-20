(function () {
  function g(n){ try { return SimVar.GetSimVarValue('L:'+n,'number'); } catch(e){ return 'ERR'; } }
  function s(n,v){ try { SimVar.SetSimVarValue('L:'+n,'number',v); return 'ok'; } catch(e){ return 'ERR'; } }
  // 1) round-trip test: write a synthetic L-var from this top-level eval
  s('MSFSBA_IRS_ALIGN_STATE', 2);
  s('MSFSBA_IRS_ALIGN_MINUTES', 9);
  // 2) try to drive the IRS knobs to NAV (2). Both knob-state L-vars + the
  //    likely knob events; read back what stuck.
  s('B787_IRS_Knob_State:1', 2);
  s('B787_IRS_Knob_State:2', 2);
  var el = document.querySelector('.time-to-align');
  var kids = el ? el.querySelectorAll('div') : [];
  return JSON.stringify({
    wroteState: g('MSFSBA_IRS_ALIGN_STATE'),
    wroteMin: g('MSFSBA_IRS_ALIGN_MINUTES'),
    knob1: g('B787_IRS_Knob_State:1'),
    knob2: g('B787_IRS_Knob_State:2'),
    posSet1: g('WT_IRS_POS_SET_1'),
    posSet2: g('WT_IRS_POS_SET_2'),
    alignHidden: el ? el.classList.contains('hidden') : 'no-el',
    alignText: kids.length ? (kids[kids.length-1].textContent||'').trim() : 'n/a'
  }, null, 1);
})()
