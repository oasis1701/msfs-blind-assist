(function(){ try {
  function g(n){ try { return SimVar.GetSimVarValue('L:'+n,'number'); } catch(e){ return 'ERR'; } }
  var ks=['A32NX_OANS_BTV_DRY_DISTANCE_ESTIMATED','A32NX_OANS_BTV_WET_DISTANCE_ESTIMATED','A32NX_OANS_BTV_STOP_BAR_DISTANCE_ESTIMATED','A32NX_BTV_STATE','A32NX_OANS_BTV_REQ_STOPPING_DISTANCE'];
  var out=[]; for(var i=0;i<ks.length;i++) out.push(ks[i]+' = '+g(ks[i]));
  return out.join('\n');
} catch(e){ return 'ERR '+e; } })()