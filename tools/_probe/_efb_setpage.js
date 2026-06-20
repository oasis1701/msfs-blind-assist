(function(){
  var el=null, all=document.querySelectorAll('*');
  for(var i=0;i<all.length;i++){ if(all[i].fsInstrument){ el=all[i]; break; } }
  if(!el) return 'no instance';
  var inst = el.fsInstrument;
  // navigate to MainMenu via the enum (fall back to 0 if name differs)
  var E = window.EfbPages || {};
  var target = (E.MainMenu!==undefined)?E.MainMenu:(E.Menu!==undefined?E.Menu:0);
  inst.visiblePage.set(target);
  return 'set visiblePage to '+target;
})()
