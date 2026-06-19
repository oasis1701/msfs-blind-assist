(function () {
  var el = document.querySelector('.time-to-align');
  if (!el) return 'NO .time-to-align in this view';
  var hidden = el.classList.contains('hidden');
  var kids = el.querySelectorAll('div');
  var last = kids.length ? (kids[kids.length - 1].textContent || '').trim() : '(no child div)';
  var knob1 = 0, knob2 = 0, pos1 = 0;
  try {
    knob1 = SimVar.GetSimVarValue('L:B787_IRS_Knob_State:1', 'number');
    knob2 = SimVar.GetSimVarValue('L:B787_IRS_Knob_State:2', 'number');
    pos1 = SimVar.GetSimVarValue('L:WT_IRS_POS_SET_1', 'number');
  } catch (e) {}
  return JSON.stringify({
    found: true, hidden: hidden, lastChildText: last,
    knob1: knob1, knob2: knob2, posSet1: pos1,
    outer: (el.outerHTML || '').slice(0, 220)
  }, null, 1);
})()
