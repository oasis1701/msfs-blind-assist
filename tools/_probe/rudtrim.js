(function(){ try {
  function g(n){ try { return SimVar.GetSimVarValue('L:'+n,'number'); } catch(e){ return 'ERR'; } }
  function gs(n){ try { return SimVar.GetSimVarValue(n,'number'); } catch(e){ return 'ERR'; } }
  var ks=['A32NX_SEC_1_RUDDER_ACTUAL_POSITION','A32NX_SEC_3_RUDDER_ACTUAL_POSITION','A32NX_SEC_1_RUDDER_STATUS_WORD','A32NX_SEC_3_RUDDER_STATUS_WORD'];
  var out=[]; for(var i=0;i<ks.length;i++) out.push(ks[i]+' = '+g(ks[i]));
  out.push('RUDDER TRIM PCT = '+gs('RUDDER TRIM PCT'));
  return out.join('\n');
} catch(e){ return 'ERR '+e; } })()