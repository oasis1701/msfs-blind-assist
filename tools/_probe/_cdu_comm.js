(function () {
  var A = window.__MSFSBA_HS787;
  if (!A) return 'NO-CDU-AGENT';
  // FMC_COMM page key = 9 per the agent PAGE enum
  try { A.clickPage(9); } catch (e) {}
  return 'clicked-FMC-COMM';
})()
