(function(){
  if(!window.__MSFSBA_HS787_IRS) return 'AGENT MISSING';
  var r = window.__MSFSBA_HS787_IRS.poll();
  function g(n){ try { return SimVar.GetSimVarValue('L:'+n,'number'); } catch(e){ return 'ERR'; } }
  return JSON.stringify({ poll:r, writtenState:g('MSFSBA_IRS_ALIGN_STATE'), writtenMin:g('MSFSBA_IRS_ALIGN_MINUTES') });
})()
