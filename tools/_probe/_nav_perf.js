(function () {
  var A = window.__MSFSBA_A380;
  window.__seen = {};                 // reset subtab tracker for the PERF walk
  A.navigateUri('fms/active/perf');
  return JSON.stringify({ nav: 'fms/active/perf' });
})();
