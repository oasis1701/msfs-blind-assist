(function(){ try {
  // before-state JSON is embedded by the runner via replace of __BEFORE__
  var before=__BEFORE__;
  var out=[];
  for (var k in before){ if(!before.hasOwnProperty(k)) continue;
    var want=(before[k]>0.5)?0:1;
    var now=SimVar.GetSimVarValue('L:'+k,'number');
    out.push((now===want?'STUCK ':'REVERT')+' '+k+' (was '+before[k]+' set '+want+' now '+now+')');
    // restore original
    SimVar.SetSimVarValue('L:'+k,'number',before[k]);
  }
  return out.join('\n');
} catch(e){ return 'ERR '+e; } })()