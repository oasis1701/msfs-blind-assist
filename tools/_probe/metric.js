(function(){ try {
  var out=[];
  function g(n){ try { return SimVar.GetSimVarValue('L:'+n,'number'); } catch(e){ return 'ERR'; } }
  out.push('L:A32NX_CONFIG_USING_METRIC_UNIT = '+g('A32NX_CONFIG_USING_METRIC_UNIT'));
  out.push('L:A380X_CONFIG_USING_METRIC_UNIT = '+g('A380X_CONFIG_USING_METRIC_UNIT'));
  out.push('L:CONFIG_USING_METRIC_UNIT = '+g('CONFIG_USING_METRIC_UNIT'));
  // EFB store, if reachable from this view
  try { out.push('GetStoredData A380X_CONFIG_USING_METRIC_UNIT = '+(typeof GetStoredData!=='undefined'?GetStoredData('A380X_CONFIG_USING_METRIC_UNIT'):'no GetStoredData')); } catch(e){ out.push('GetStoredData ERR '+e); }
  return out.join('\n');
} catch(e){ return 'ERR '+e; } })()