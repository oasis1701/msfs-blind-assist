(function () {
  try {
    return JSON.stringify({
      act1: SimVar.GetSimVarValue('COM ACTIVE FREQUENCY:1', 'MHz'),
      stb1: SimVar.GetSimVarValue('COM STANDBY FREQUENCY:1', 'MHz'),
      act2: SimVar.GetSimVarValue('COM ACTIVE FREQUENCY:2', 'MHz'),
      stb2: SimVar.GetSimVarValue('COM STANDBY FREQUENCY:2', 'MHz'),
      tx1: SimVar.GetSimVarValue('COM TRANSMIT:1', 'number'),
      tx2: SimVar.GetSimVarValue('COM TRANSMIT:2', 'number'),
      baroModeL: SimVar.GetSimVarValue('L:A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE', 'number'),
      baroModeR: SimVar.GetSimVarValue('L:A32NX_FCU_EFIS_R_DISPLAY_BARO_VALUE_MODE', 'number'),
      baroValL: SimVar.GetSimVarValue('L:A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE', 'number'),
      deadIsStdL: SimVar.GetSimVarValue('L:A32NX_FCU_LEFT_EIS_BARO_IS_STD', 'number'),
      onGround: SimVar.GetSimVarValue('SIM ON GROUND', 'bool')
    });
  } catch (e) { return 'ERR: ' + e; }
})()
