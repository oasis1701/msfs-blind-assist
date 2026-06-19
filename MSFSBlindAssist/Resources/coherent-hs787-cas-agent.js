// HorizonSim 787-9 CAS (Crew Alerting System) agent — reads the EICAS alert messages over the
// Coherent debugger (the MFD_1 / EICAS view). The WT 787 renders each active alert as a
// .cas-warning (red), .cas-caution (amber), or .cas-advisory (white/cyan) element. MSFSBA's
// CAS monitor (CoherentHS787CasClient) polls this + announces NEW alerts by severity, so a blind
// pilot hears cautions/warnings as they post.
//
// ES5 ONLY (Coherent GT = old Chromium): var, no arrow funcs, top-level try/catch.
(function () {
  try {
    var A = {};

    function listOf(sel) {
      var n = document.querySelectorAll(sel), out = [], seen = {};
      for (var i = 0; i < n.length; i++) {
        // skip a parent container that concatenates everything (we want the per-message leaves)
        if ((n[i].className || '').toString().indexOf('container') >= 0) continue;
        var t = (n[i].textContent || '').replace(/\s+/g, ' ').replace(/^\s+|\s+$/g, '');
        if (!t || seen[t]) continue;
        seen[t] = 1;
        out.push(t);
      }
      return out;
    }

    A.cas = function () {
      return {
        ok: true,
        warnings: listOf('.cas-warning'),
        cautions: listOf('.cas-caution'),
        advisories: listOf('.cas-advisory')
      };
    };

    A.ping = function () { return { ok: true, present: !!document.querySelector('.cas-alerts-container, .cas-caution, .cas-warning') }; };

    window.__MSFSBA_HS787_CAS = A;
    return 'MSFSBA_HS787_CAS_INSTALLED';
  } catch (e) {
    return 'MSFSBA_HS787_CAS_ERROR:' + (e && e.message);
  }
})()
