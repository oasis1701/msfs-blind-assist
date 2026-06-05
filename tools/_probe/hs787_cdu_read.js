// Self-contained CDU read + control inventory for the HS787 WT Boeing FMC,
// driven from the Coherent debugger (no bridge dependency). Mirrors the bridge's
// readScreen selectors to prove the debugger can read the CDU directly.
(function () {
  var cdu = document.querySelector('.wt787-cdu');
  if (!cdu) return JSON.stringify({ cdu: false });
  var screen = cdu.querySelector('#fmc-container') || cdu.querySelector('.wt787-cdu-screen');
  var rows = screen ? screen.querySelectorAll('.fmc-row') : [];
  var lines = [];
  for (var r = 0; r < rows.length; r++) {
    lines.push((rows[r].textContent || '').replace(/\s+$/, ''));
  }
  var sp = cdu.querySelector('.wt787-cdu-scratchpad');
  var lskL = cdu.querySelectorAll('.wt787-cdu-lsk-column-left .wt787-cdu-lsk').length;
  var lskR = cdu.querySelectorAll('.wt787-cdu-lsk-column-right .wt787-cdu-lsk').length;
  // page buttons = wt787-cdu-button outside the LSK columns
  var allBtns = cdu.querySelectorAll('.wt787-cdu-button');
  var pageBtns = 0;
  for (var i = 0; i < allBtns.length; i++) {
    var b = allBtns[i];
    if (!(b.closest('.wt787-cdu-lsk-column-left') || b.closest('.wt787-cdu-lsk-column-right'))) pageBtns++;
  }
  return JSON.stringify({
    cdu: true,
    rowCount: lines.length,
    scratchpad: sp ? (sp.textContent || '').trim() : '(no scratchpad el)',
    lskLeft: lskL, lskRight: lskR, pageButtons: pageBtns, totalButtons: allBtns.length,
    rows: lines
  });
})()
