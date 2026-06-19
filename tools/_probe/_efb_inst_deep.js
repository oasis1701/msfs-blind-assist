(function(){
  var el=null, all=document.querySelectorAll('*');
  for(var i=0;i<all.length;i++){ if(all[i].fsInstrument){ el=all[i]; break; } }
  if(!el) return 'no instance';
  var inst = el.fsInstrument;
  var out = {};
  // all keys on the instance
  var keys=[]; for(var k in inst){ keys.push(k); }
  out.allKeys = keys;
  // look for page/menu/route/ui members + drill one level
  var interesting = keys.filter(function(k){ return /page|menu|route|ui|nav|screen|view|app|stack/i.test(k); });
  out.interesting = {};
  interesting.forEach(function(k){
    try{ var v=inst[k]; var t=typeof v;
      if(v && t==='object'){ var sub=[]; for(var s in v){ sub.push(s); } out.interesting[k]='OBJ{'+sub.slice(0,15).join(',')+'}'; }
      else out.interesting[k]=t;
    }catch(e){ out.interesting[k]='ERR'; }
  });
  // also check the custom element itself for page methods
  var elKeys=[]; for(var e2 in el){ if(/page|menu|goto|nav|onInteraction/i.test(e2)) elKeys.push(e2); }
  out.elementMethods = elKeys.slice(0,20);
  return JSON.stringify(out, null, 1);
})()
