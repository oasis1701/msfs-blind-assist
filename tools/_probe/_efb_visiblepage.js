(function(){
  var el=null, all=document.querySelectorAll('*');
  for(var i=0;i<all.length;i++){ if(all[i].fsInstrument){ el=all[i]; break; } }
  if(!el) return 'no instance';
  var inst = el.fsInstrument;
  var vp = inst.visiblePage;
  var out = { currentValue: null, valueType: null, hasSet: false, sub: [] };
  try{
    var v = vp.get ? vp.get() : vp.value;
    out.currentValue = (typeof v === 'object') ? JSON.stringify(v).slice(0,200) : v;
    out.valueType = typeof v;
  }catch(e){ out.currentValue = 'ERR '+e.message; }
  out.hasSet = typeof vp.set === 'function';
  // list Subject members to confirm it's settable
  for(var s in vp){ out.sub.push(s); }
  out.sub = out.sub.slice(0,12);
  // try to find the page enum/constants — search window for EfbShowPages-ish
  var enums = [];
  try{ for(var g in window){ if(/EfbShowPages|EfblShowPages|EfbPages|ShowPages/.test(g)) enums.push(g); } }catch(e){}
  out.windowEnums = enums.slice(0,10);
  return JSON.stringify(out,null,1);
})()
