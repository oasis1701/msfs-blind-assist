(function(){ try {
  function g(){ return SimVar.GetSimVarValue('L:A32NX_OVHD_COND_PACK_1_PB_IS_ON','number'); }
  var before=g();
  var target=(before>0.5)?0:1;
  // MobiFlight calculator path (reliable for FBW L:vars)
  SimVar.SetSimVarValue('L:A32NX_OVHD_COND_PACK_1_PB_IS_ON','number',target);
  return 'before='+before+' wrote='+target+' immediate='+g();
} catch(e){ return 'ERR '+e; } })()