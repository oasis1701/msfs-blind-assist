(function(){ try {
  function g(n){ try{ return SimVar.GetSimVarValue('L:'+n,'number'); }catch(e){ return 'ERR'; } }
  var ks=[
    'A32NX_OVHD_HYD_EPUMPB_PB_IS_AUTO','A32NX_OVHD_HYD_EPUMPY_PB_IS_AUTO',
    'A32NX_OVHD_HYD_EPUMPGA_ON_PB_IS_AUTO','A32NX_OVHD_HYD_EPUMPGA_OFF_PB_IS_AUTO',
    'A32NX_OVHD_HYD_ENG_1AB_PUMP_DISC_PB_IS_AUTO','A32NX_HYD_ENG_1AB_PUMP_DISC',
    'A32NX_OVHD_HYD_ENG_2AB_PUMP_DISC_PB_IS_AUTO',
    'PUSH_OVHD_OXYGEN_CREW','A32NX_OXYGEN_PASSENGER_LIGHT_ON','A32NX_OXYGEN_MASKS_DEPLOYED',
    'A32NX_DLS_ON','A32NX_OVHD_HYD_EPUMPB_PB_HAS_FAULT'
  ];
  var out=[]; for(var i=0;i<ks.length;i++) out.push(ks[i]+' = '+g(ks[i]));
  return out.join('\n');
} catch(e){ return 'ERR '+e; } })()