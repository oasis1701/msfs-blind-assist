(function () {
  try {
    SimVar.SetSimVarValue('K:A32NX.FCU_EFIS_L_BARO_DEC', 'number', 0);
    return 'sent L BARO_DEC (dotted K)';
  } catch (e) { return 'ERR: ' + e; }
})()
