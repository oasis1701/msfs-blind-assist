(function () {
  try {
    SimVar.SetSimVarValue('K:A32NX.FCU_EFIS_L_BARO_INC', 'number', 0);
    SimVar.SetSimVarValue('K:A32NX.FCU_EFIS_L_BARO_PULL', 'number', 0);
    return 'sent L BARO_INC + BARO_PULL';
  } catch (e) { return 'ERR: ' + e; }
})()
