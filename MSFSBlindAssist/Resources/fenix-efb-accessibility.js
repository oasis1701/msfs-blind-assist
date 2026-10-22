// Fenix EFB accessibility layer, injected into the Fenix EFB web page by FenixEFBForm.
// It only adds roles, names, states, Tab stops and key handlers that call the page's own click --
// never a request, a removed element or a written value. Plain ES2017, no modules.
(function () {
  if (window.__msfsbaFenixEfb) { return; }

  // Placeholders: later tasks fill these in. Tests reach the layer through window.__msfsbaFenixEfb.
  function pass() { }
  function textOf(el) { return ''; }

  window.__msfsbaFenixEfb = { pass: pass, textOf: textOf };
})();
