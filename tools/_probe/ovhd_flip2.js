(function(){ try {
  var vars=[
    'A32NX_OVHD_PNEU_ENG_1_BLEED_PB_IS_AUTO','A32NX_OVHD_PNEU_ENG_3_BLEED_PB_IS_AUTO',
    'A32NX_OVHD_HYD_EPUMPGA_ON_PB_IS_AUTO','A32NX_OVHD_HYD_EPUMPYA_ON_PB_IS_AUTO',
    'XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position'
  ];
  var before={};
  for (var i=0;i<vars.length;i++){
    var v=SimVar.GetSimVarValue('L:'+vars[i],'number');
    before[vars[i]]=v;
    SimVar.SetSimVarValue('L:'+vars[i],'number',(v>0.5)?0:1);
  }
  return JSON.stringify(before);
} catch(e){ return 'ERR '+e; } })()