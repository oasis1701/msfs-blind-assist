// WRITE TEST 1 — fire an FMC keypad H-event (the bridge's type_key path) from a
// top-level Runtime.evaluate. Types letter 'A' into the scratchpad. Returns the
// scratchpad text read back immediately (may lag one frame) + the call result.
(function () {
  var r = null;
  try { r = SimVar.SetSimVarValue('H:AS01B_FMC_1_BTN_A', 'number', 1); } catch (e) { return 'THREW: ' + e.message; }
  var cdu = document.querySelector('.wt787-cdu');
  var sp = cdu && cdu.querySelector('.wt787-cdu-scratchpad');
  return JSON.stringify({ setResult: String(r), scratchpadNow: sp ? (sp.textContent || '').trim() : 'NA' });
})()
