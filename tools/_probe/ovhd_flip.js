(function(){ try {
  var vars=[
    'A32NX_OVHD_COND_HOT_AIR_1_PB_IS_ON','A32NX_OVHD_COND_HOT_AIR_2_PB_IS_ON',
    'A32NX_OVHD_COND_PACK_2_PB_IS_ON',
    'A32NX_OVHD_ELEC_BUS_TIE_PB_IS_AUTO','A32NX_OVHD_ELEC_GALY_AND_CAB_PB_IS_AUTO',
    'A32NX_OVHD_HYD_PTU_PB_IS_AUTO',
    'A32NX_OVHD_HYD_ENG_1A_PUMP_PB_IS_AUTO','A32NX_OVHD_HYD_ENG_1B_PUMP_PB_IS_AUTO',
    'A32NX_OVHD_VENT_CAB_FANS_PB_IS_ON','A32NX_OVHD_VENT_AIR_EXTRACT_PB_IS_ON'
  ];
  var before={};
  for (var i=0;i<vars.length;i++){
    var v=SimVar.GetSimVarValue('L:'+vars[i],'number');
    before[vars[i]]=v;
    SimVar.SetSimVarValue('L:'+vars[i],'number',(v>0.5)?0:1);
  }
  return JSON.stringify(before);
} catch(e){ return 'ERR '+e; } })()