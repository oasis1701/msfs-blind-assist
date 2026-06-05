JSON.stringify({
  reg: typeof window.RegisterCommBusListener,
  hits: Object.keys(window).filter(function (k) { return /pmdg|commbus|comm_bus|tablet|eicas|alert|listener|broadcast|simvarpub/i.test(k); }),
  coherentOn: (typeof window.Coherent === 'object') ? typeof window.Coherent.on : 'noCoherent',
  listenerShape: (function () {
    try {
      var L = window.RegisterCommBusListener(function () {});
      var keys = [];
      for (var k in L) { try { keys.push(k + ':' + typeof L[k]); } catch (e) {} }
      return keys;
    } catch (e) { return 'ERR ' + e; }
  })()
})
