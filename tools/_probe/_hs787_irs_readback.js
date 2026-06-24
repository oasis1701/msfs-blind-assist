(function(){
  function g(n){ try { return SimVar.GetSimVarValue('L:'+n,'number'); } catch(e){ return 'ERR'; } }
  var el = document.querySelector('.time-to-align');
  var kids = el ? el.querySelectorAll('div') : [];
  return JSON.stringify({
    synthState: g('MSFSBA_IRS_ALIGN_STATE'),
    synthMin: g('MSFSBA_IRS_ALIGN_MINUTES'),
    alignHidden: el ? el.classList.contains('hidden') : 'no-el',
    alignText: kids.length ? (kids[kids.length-1].textContent||'').trim() : 'n/a'
  });
})()
