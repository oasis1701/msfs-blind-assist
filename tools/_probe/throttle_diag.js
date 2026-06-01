// Read the live FBW throttle calibration mapping for all 4 throttles, so we can
// see whether the forward detents (IDLE/CLIMB/FLEX/TOGA) are sane (ordered,
// non-overlapping) or broken — which would explain "forward does nothing, only
// reverse works". L:vars from ThrottleSimVar.tsx: <DETENT>_LOW/HIGH:<n>.
(function(){
  function g(name){ try { return SimVar.GetSimVarValue(name,'number'); } catch(e){ return 'ERR'; } }
  var dets=['REVERSE','REVERSE_IDLE','IDLE','CLIMB','FLEXMCT','TOGA'];
  var out=[];
  out.push('THROTTLE_AXIS pref = '+g('L:A32NX_THROTTLE_MAPPING_LOADED_FROM_FILE'));
  for(var t=1;t<=4;t++){
    out.push('--- Throttle '+t+'  INPUT='+g('L:A32NX_THROTTLE_MAPPING_INPUT:'+t).toFixed?(g('L:A32NX_THROTTLE_MAPPING_INPUT:'+t)).toFixed(3):g('L:A32NX_THROTTLE_MAPPING_INPUT:'+t));
    for(var d=0;d<dets.length;d++){
      var lo=g('L:A32NX_THROTTLE_MAPPING_'+dets[d]+'_LOW:'+t);
      var hi=g('L:A32NX_THROTTLE_MAPPING_'+dets[d]+'_HIGH:'+t);
      var lof=(typeof lo==='number')?lo.toFixed(3):lo, hif=(typeof hi==='number')?hi.toFixed(3):hi;
      out.push('   '+dets[d]+'  low='+lof+'  high='+hif);
    }
  }
  out.push('USE_REVERSE_ON_AXIS:1='+g('L:A32NX_THROTTLE_MAPPING_USE_REVERSE_ON_AXIS:1')
          +'  USE_TOGA_ON_AXIS:1='+g('L:A32NX_THROTTLE_MAPPING_USE_TOGA_ON_AXIS:1'));
  // Live engine response sanity: TLA (thrust lever angle) + N1 for each engine.
  for(var e=1;e<=4;e++){
    out.push('ENG'+e+' TLA='+g('L:A32NX_AUTOTHRUST_TLA:'+e)+'  N1cmd='+g('L:A32NX_AUTOTHRUST_N1_COMMANDED:'+e));
  }
  return out.join('\n');
})()
