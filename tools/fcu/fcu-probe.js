// FCU read/verify agent for the FlyByWire A380X. Run via tools/coherent-eval.ps1
// against any cockpit Coherent view (e.g. -Title A380X_FCU). Reads every FCU
// output the windows depend on and returns JSON. Optionally fires a set then
// re-reads (see fcu-set.ps1). ES5 only (Coherent GT = Chromium 49).
(function () {
  try {
    function L(name) { return SimVar.GetSimVarValue('L:' + name, 'number'); }
    function S(name, unit) { return SimVar.GetSimVarValue(name, unit || 'number'); }
    // ARINC429 word -> engineering value (low 32 bits = IEEE754 float; SSM in 32-33).
    function arinc(raw) {
      if (raw < 4294967296) return null; // not a 429 word (SSM not set)
      var lo = raw % 4294967296;
      var buf = new ArrayBuffer(4); new DataView(buf).setUint32(0, lo);
      return new DataView(buf).getFloat32(0);
    }
    var out = {
      heading_selected: L('A32NX_AUTOPILOT_HEADING_SELECTED'),
      hdg_managed_dashes: L('A32NX_FCU_HDG_MANAGED_DASHES'),
      speed_selected: L('A32NX_AUTOPILOT_SPEED_SELECTED'),
      spd_managed_dot: L('A32NX_FCU_SPD_MANAGED_DOT'),
      mach_mode: S('A:AUTOPILOT MANAGED SPEED IN MACH', 'bool'),
      alt_value: S('A:AUTOPILOT ALTITUDE LOCK VAR:3', 'feet'),
      alt_managed: L('A32NX_FCU_ALT_MANAGED'),
      vs_selected: L('A32NX_AUTOPILOT_VS_SELECTED'),
      fpa_selected: L('A32NX_AUTOPILOT_FPA_SELECTED'),
      trk_fpa_mode: L('A32NX_TRK_FPA_MODE_ACTIVE'),
      metric_alt: L('A32NX_METRIC_ALT_TOGGLE'),
      ap1: L('A32NX_AUTOPILOT_1_ACTIVE'),
      ap2: L('A32NX_AUTOPILOT_2_ACTIVE'),
      loc: L('A32NX_FCU_LOC_MODE_ACTIVE'),
      appr: L('A32NX_FCU_APPR_MODE_ACTIVE'),
      exped: L('A32NX_FMA_EXPEDITE_MODE'),
      athr_status: L('A32NX_AUTOTHRUST_STATUS'),
      fd_l: L('A32NX_FCU_EFIS_L_FD_ACTIVE'),
      fd_r: L('A32NX_FCU_EFIS_R_FD_ACTIVE'),
      baro_l_hpa: arinc(L('A32NX_FCU_LEFT_EIS_BARO_HPA')),
      baro_r_hpa: arinc(L('A32NX_FCU_RIGHT_EIS_BARO_HPA')),
      baro_l_std: L('A32NX_FCU_LEFT_EIS_BARO_IS_STD'),
      baro_r_std: L('A32NX_FCU_RIGHT_EIS_BARO_IS_STD'),
      baro_l_hpa_unit: L('XMLVAR_Baro_Selector_HPA_1'),
      baro_r_hpa_unit: L('XMLVAR_Baro_Selector_HPA_2')
    };
    return JSON.stringify(out);
  } catch (e) { return 'FCU_PROBE_ERR: ' + e; }
})();
