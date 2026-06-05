// Read just the scratchpad + title row (to confirm a prior action took effect a frame later).
(function () {
  var cdu = document.querySelector('.wt787-cdu');
  if (!cdu) return JSON.stringify({ cdu: false });
  var sp = cdu.querySelector('.wt787-cdu-scratchpad');
  var screen = cdu.querySelector('#fmc-container') || cdu.querySelector('.wt787-cdu-screen');
  var rows = screen ? screen.querySelectorAll('.fmc-row') : [];
  var title = rows.length ? (rows[0].textContent || '').replace(/\s+/g, ' ').trim() : '';
  return JSON.stringify({ scratchpad: sp ? (sp.textContent || '').trim() : 'NA', title: title });
})()
