(function () {
  var b = document.body;
  var txt = b ? b.innerText.replace(/\s+/g, ' ').trim() : '';
  return JSON.stringify({
    title: document.title,
    bodyLen: txt.length,
    sample: txt.slice(0, 500),
    // CDU/FMC markers the existing hs787-mfd-bridge.js keys off:
    fmcRows: document.querySelectorAll('.fmc-row').length,
    fmcLetters: document.querySelectorAll('.fmc-letter').length,
    buttons: document.querySelectorAll('button,[role=button],.button,[onclick]').length,
    inputs: document.querySelectorAll('input,textarea,select').length,
    divs: document.querySelectorAll('div').length,
    canvases: document.querySelectorAll('canvas').length,
    svgs: document.querySelectorAll('svg').length
  });
})()
