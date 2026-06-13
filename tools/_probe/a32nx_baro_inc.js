(function () {
  try {
    SimVar.SetSimVarValue('K:A32NX.FCU_EFIS_L_BARO_INC', 'number', 0);
    return 'sent L BARO_INC';
  } catch (e) { return 'ERR: ' + e; }
})()
