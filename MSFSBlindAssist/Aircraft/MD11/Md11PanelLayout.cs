using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.MD11;

public sealed record Md11LayoutPanel(string Name, IReadOnlyList<string> Keys);
public sealed record Md11LayoutSection(string Name, IReadOnlyList<Md11LayoutPanel> Panels);

/// <summary>
/// Result of placing a control map onto the layout: the three dictionaries MainForm consumes —
/// control rows, display (lamp) rows, and the section→panel tree — plus what the table missed.
/// </summary>
public sealed record Md11Placement(
    Dictionary<string, List<string>> Structure,
    Dictionary<string, List<string>> Controls,
    Dictionary<string, List<string>> Displays,
    List<string> Unplaced,
    List<string> MissingKeys);

/// <summary>
/// The MD-11 panel tree (spec §3.8): sections in the order a preparation flows, panels named as
/// TFDi's Systems Guide names them, controls in the physical panel order the guide numbers, a
/// guard cover immediately before the control it covers WHERE it still has a row at all (most do
/// not — the auto-open lifts them; see IsAutoOpenedGuard), standalone lamps as the panel's Status
/// Display rows (never tab stops).
///
/// The table is the ONLY source of panel order. A control it does not name is still appended
/// (never dropped) to a "{Area} (other)" panel — the test suite pins that the shipped map needs
/// no such panel, so a regenerated map that adds a control fails loudly rather than hiding it.
/// The MCDU keys are not rows at all: the MCDU window presses them (IsMcduControl).
/// </summary>
public static class Md11PanelLayout
{
    // ---- Overhead -------------------------------------------------------------------------
    private static readonly string[] Electrical =
    {
        "MD11_OVHD_ELEC_BATT_GRD", "MD11_OVHD_ELEC_BATT_BT",
        "MD11_OVHD_ELEC_EXT_PWR_BT", "MD11_OVHD_ELEC_APU_PWR_BT", "MD11_OVHD_ELEC_EMER_PWR_KB",
        "MD11_OVHD_ELEC_GEN1_BT", "MD11_OVHD_ELEC_GEN2_BT", "MD11_OVHD_ELEC_GEN3_BT",
        "MD11_OVHD_ELEC_GEN1_DRIVE_GRD", "MD11_OVHD_ELEC_GEN1_DRIVE_BT",
        "MD11_OVHD_ELEC_GEN2_DRIVE_GRD", "MD11_OVHD_ELEC_GEN2_DRIVE_BT",
        "MD11_OVHD_ELEC_GEN3_DRIVE_GRD", "MD11_OVHD_ELEC_GEN3_DRIVE_BT",
        "MD11_OVHD_ELEC_AC_TIE1_BT", "MD11_OVHD_ELEC_AC_TIE2_BT", "MD11_OVHD_ELEC_AC_TIE3_BT",
        "MD11_OVHD_ELEC_DC_TIE1_BT", "MD11_OVHD_ELEC_DC_TIE3_BT",
        "MD11_OVHD_ELEC_ADG_ELEC_BT", "MD11_OVHD_ELEC_SYSTEM_SEL_BT", "MD11_OVHD_ELEC_SMOKE_ELEC_AIR_KB",
        "MD11_OVHD_ELEC_CAB_BUS_GRD", "MD11_OVHD_ELEC_CAB_BUS_BT", "MD11_OVHD_ELEC_GLY_EXT_PWR_BT",
        "MD11_OVHD_GALLEY_BUS_1_BT", "MD11_OVHD_GALLEY_BUS_2_BT", "MD11_OVHD_GALLEY_BUS_3_BT",
        "MD11_OVHD_GEN_BUS_1_RESET_GRD", "MD11_OVHD_GEN_BUS_1_RESET_BT",
        "MD11_OVHD_GEN_BUS_2_RESET_GRD", "MD11_OVHD_GEN_BUS_2_RESET_BT",
        "MD11_OVHD_GEN_BUS_3_RESET_GRD", "MD11_OVHD_GEN_BUS_3_RESET_BT",
        // status
        "MD11_OVHD_ELEC_EMER_PWR_OFF_LT", "MD11_OVHD_ELEC_EMER_PWR_ON_LT",
        "MD11_OVHD_ELEC_AC1_OFF_LT", "MD11_OVHD_ELEC_AC2_OFF_LT", "MD11_OVHD_ELEC_AC3_OFF_LT",
        "MD11_OVHD_ELEC_DC1_BUS_OFF_LT", "MD11_OVHD_ELEC_DC2_BUS_OFF_LT", "MD11_OVHD_ELEC_DC3_BUS_OFF_LT",
        "MD11_OVHD_ELEC_BATT_BUS_OFF_LT",
        "MD11_OVHD_ELEC_L_EMER_AC_OFF_LT", "MD11_OVHD_ELEC_R_EMER_AC_OFF_LT",
        "MD11_OVHD_ELEC_L_EMER_DC_OFF_LT", "MD11_OVHD_ELEC_R_EMER_DC_OFF_LT",
        "MD11_OVHD_ELEC_AC_GND_SVC_OFF_LT", "MD11_OVHD_ELEC_DC_GND_SVC_OFF_LT",
    };

    private static readonly string[] Irs =
    {
        "MD11_OVHD_IRS_1_KB", "MD11_OVHD_IRS_2_KB", "MD11_OVHD_IRS_3_KB",
        "MD11_OVHD_IRS_1_LT", "MD11_OVHD_IRS_2_LT", "MD11_OVHD_IRS_3_LT",
    };

    private static readonly string[] Fuel =
    {
        "MD11_OVHD_FUEL_SYSTEM_SEL_BT",
        "MD11_OVHD_FUEL_PUMP_TANK_1_BT", "MD11_OVHD_FUEL_PUMP_TANK_2_BT", "MD11_OVHD_FUEL_PUMP_TANK_3_BT",
        "MD11_OVHD_FUEL_ALT_PUMP_BT",
        "MD11_OVHD_FUEL_TRANS_TANK_1_BT", "MD11_OVHD_FUEL_TRANS_TANK_2_BT", "MD11_OVHD_FUEL_TRANS_TANK_3_BT",
        "MD11_OVHD_FUEL_XFEED_TANK_1_BT", "MD11_OVHD_FUEL_XFEED_TANK_2_BT", "MD11_OVHD_FUEL_XFEED_TANK_3_BT",
        "MD11_OVHD_FUEL_FILL_TANK_1_BT", "MD11_OVHD_FUEL_FILL_TANK_2_BT", "MD11_OVHD_FUEL_FILL_TANK_3_BT",
        "MD11_OVHD_FUEL_LEFT_TRANS_BT", "MD11_OVHD_FUEL_RIGHT_TRANS_BT", "MD11_OVHD_FUEL_TAIL_TRANS_BT",
        "MD11_OVHD_FUEL_FWDAUX_L_TRANS_BT", "MD11_OVHD_FUEL_FWDAUX_R_TRANS_BT",
        "MD11_OVHD_FUEL_UPRAUX_BYP_GRD", "MD11_OVHD_FUEL_UPRAUX_BYP_BT",
        "MD11_OVHD_FUEL_MANF_DRAIN_GRD", "MD11_OVHD_FUEL_MANF_DRAIN_BT",
        "MD11_OVHD_FUEL_DUMP_GRD", "MD11_OVHD_FUEL_DUMP_BT",
        "MD11_OVHD_FUEL_DUMP_STOP_GRD", "MD11_OVHD_FUEL_DUMP_STOP_BT",
        "MD11_OVHD_FUEL_QTY_TEST_BT", "MD11_OVHD_FUELUSEDRESET_BT",
    };

    private static readonly string[] Hydraulic =
    {
        "MD11_OVHD_HYD_SYSTEM_SEL_BT",
        "MD11_OVHD_HYD_EDP_1_L_BT", "MD11_OVHD_HYD_EDP_1_R_BT", "MD11_OVHD_HYD_EDP_2_L_BT",
        "MD11_OVHD_HYD_EDP_2_R_BT", "MD11_OVHD_HYD_EDP_3_L_BT", "MD11_OVHD_HYD_EDP_3_R_BT",
        "MD11_OVHD_HYD_1_3_RMP_BT", "MD11_OVHD_HYD_2_3_RMP_BT",
        "MD11_OVHD_HYD_AUX_PUMP_1_BT", "MD11_OVHD_HYD_AUX_PUMP_2_BT",
        "MD11_OVHD_HYD_HYD_TEST_GRD", "MD11_OVHD_HYD_HYD_TEST_BT",
        "MD11_OVHD_HYD_SYS_1_PRESS_LT", "MD11_OVHD_HYD_SYS_2_PRESS_LT", "MD11_OVHD_HYD_SYS_3_PRESS_LT",
        "MD11_OVHD_HYD_TEST_LT",   // the test's "running" light — its only feedback
    };

    private static readonly string[] Air =
    {
        "MD11_OVHD_PNEU_SYSTEM_SEL_BT",
        "MD11_OVHD_PNEU_PACK_1_BT", "MD11_OVHD_PNEU_PACK_2_BT", "MD11_OVHD_PNEU_PACK_3_BT",
        "MD11_OVHD_PNEU_TRIM_AIR_BT", "MD11_OVHD_PNEU_ECON_BT",
        "MD11_OVHD_PNEU_BLEED_1_OFF_BT", "MD11_OVHD_PNEU_BLEED_2_OFF_BT", "MD11_OVHD_PNEU_BLEED_3_OFF_BT",
        "MD11_OVHD_PNEU_BLEED_1_MANF_TEMP_HI_BT", "MD11_OVHD_PNEU_BLEED_2_MANF_TEMP_HI_BT", "MD11_OVHD_PNEU_BLEED_3_MANF_TEMP_HI_BT",
        "MD11_OVHD_PNEU_1_2_ISOL_BT", "MD11_OVHD_PNEU_1_3_ISOL_BT",
        "MD11_OVHD_PNEU_APU_BLEED_BT", "MD11_OVHD_PNEU_AVIONICS_FAN_BT",
        "MD11_OVHD_PNEU_CAB_AIR_GRD", "MD11_OVHD_PNEU_CAB_AIR_BT",
        "MD11_OVHD_PNEU_COCKPIT_TEMP", "MD11_OVHD_PNEU_FWD_CAB_TEMP", "MD11_OVHD_PNEU_MID_CAB_TEMP",
        "MD11_OVHD_PNEU_AFT_CAB_TEMP", "MD11_OVHD_PNEU_FWD_CARGO_TEMP", "MD11_OVHD_PNEU_AFT_CARGO_TEMP",
        "MD11_OVHD_PNEU_MASKS_GRD", "MD11_OVHD_PNEU_MASKS_BT",
        "MD11_OVHD_PNEU_NO_MASKS_LT",
    };

    private static readonly string[] CabinPressurization =
    {
        "MD11_OVHD_PNEU_CABIN_SYSTEM_SEL_BT", "MD11_OVHD_PNEU_OUTFLOW_VALVE_POS_SW",
        "MD11_OVHD_PNEU_MLDG_ALT_KB", "MD11_OVHD_PNEU_CLBDES_KB",
        "MD11_OVHD_PNEU_DITCHING_GRD", "MD11_OVHD_PNEU_DITCHING_BT",
        "MD11_OVHD_PNEU_OUTFLOW_CLOSED_LT",
    };

    private static readonly string[] AntiIce =
    {
        "MD11_OVHD_AICE_ENG1_BT", "MD11_OVHD_AICE_ENG2_BT", "MD11_OVHD_AICE_ENG3_BT",
        "MD11_OVHD_AICE_WING_BT", "MD11_OVHD_AICE_TAIL_BT",
        "MD11_OVHD_AICE_SYSTEM_SEL_BT", "MD11_OVHD_AICE_AUTO_BT",
        "MD11_OVHD_WNDSHLD_AICE_L_BT", "MD11_OVHD_WNDSHLD_AICE_R_BT",
        "MD11_OVHD_WNDSHLD_AICE_BT", "MD11_OVHD_WNDSHLD_AICE_DEFOG_BT",
    };

    private static readonly string[] EnginesAndIgnition =
    {
        "MD11_OVHD_ENG_A_BT", "MD11_OVHD_ENG_B_BT", "MD11_OVHD_ENG_IGN_OVRD_BT",
        "MD11_OVHD_ENG_FADEC_1_GRD", "MD11_OVHD_ENG_FADEC_1_BT",
        "MD11_OVHD_ENG_FADEC_2_GRD", "MD11_OVHD_ENG_FADEC_2_BT",
        "MD11_OVHD_ENG_FADEC_3_GRD", "MD11_OVHD_ENG_FADEC_3_BT",
        "MD11_OVHD_ENGMAXPTRRESET_BT",
        "MD11_OVHD_ENG_IGN_OFF_LT",
    };

    private static readonly string[] FlightControls =
    {
        "MD11_OVHD_FLTCTL_FLAPLIM_KB", "MD11_OVHD_FLTCTL_ELEVFEEL_KB",
        "MD11_OVHD_FLTCTL_LLI_BT", "MD11_OVHD_FLTCTL_LLO_BT", "MD11_OVHD_FLTCTL_RLI_BT", "MD11_OVHD_FLTCTL_RLO_BT",
        "MD11_OVHD_FLTCTL_UYDA_BT", "MD11_OVHD_FLTCTL_UYDB_BT", "MD11_OVHD_FLTCTL_LYDA_BT", "MD11_OVHD_FLTCTL_LYDB_BT",
        "MD11_OVHD_AIL_DEFL_OVRD_GRD", "MD11_OVHD_AIL_DEFL_OVRD_BT",
        "MD11_OVHD_FLTCTL_FLAPLIM_LT", "MD11_OVHD_FLTCTL_ELEVFEEL_LT",
    };

    private static readonly string[] LightsAndSigns =
    {
        "MD11_OVHD_LTS_NAV_BT", "MD11_OVHD_LTS_BCN_BT", "MD11_OVHD_LTS_HI_INT_BT", "MD11_OVHD_LTS_LOGO_BT",
        "MD11_OVHD_LTS_LDG_L_SW", "MD11_OVHD_LTS_LDG_R_SW", "MD11_OVHD_LTS_NOSE_SW",
        "MD11_OVHD_LTS_RWY_TURNOFF_L_BT", "MD11_OVHD_LTS_RWY_TURNOFF_R_BT",
        "MD11_OVHD_LTS_NO_SMOKE_SW", "MD11_OVHD_LTS_SEAT_BELTS_SW",
        "MD11_OVHD_LTS_EMER_SW", "MD11_OVHD_LTS_EMER_TEST_BT",
        "MD11_OVHD_LTS_PA_BT", "MD11_OVHD_LTS_MAINT_INTP_BT", "MD11_OVHD_LTS_MECH_BT",
        "MD11_OVHD_LTS_FWD_ATTND_BT", "MD11_OVHD_LTS_MID_ATTND_BT", "MD11_OVHD_LTS_AFT_ATTND_BT",
        "MD11_OVHD_LTS_OVW_ATTND_BT", "MD11_OVHD_LTS_CREW_REST_BT", "MD11_OVHD_LTS_ALL_STA_BT",
        "MD11_OVHD_CALL_RESET_BT",
        "MD11_OVHD_LTS_PAINUSE_LT", "MD11_OVHD_LTS_MOVIE_LT",
    };

    private static readonly string[] CockpitLights =
    {
        "MD11_OVHD_LTS_DOME_BT", "MD11_OVHD_LTS_THNDRSTRM_SW", "MD11_OVHD_LTS_STBY_COMP_BT",
        "MD11_OVHD_LTS_OUTER_OVHD_PNL_FLOOD_KB", "MD11_OVHD_LTS_INNER_OVHD_PNL_FLOOD_KB",
        "MD11_OVHD_LTS_OUTER_INSTR_PED_PNL_FLOOD_KB", "MD11_OVHD_LTS_INNER_INSTR_PED_PNL_FLOOD_KB",
        "MD11_OBS_CKTBKR_LT_KB",
        "MD11_OVHD_ANNUNLT_BRTDIM_BT", "MD11_OVHD_ANNUNLT_TEST_BT",
        "MD11_LTS_MAP_1", "MD11_LTS_MAP_2", "MD11_LTS_MAP_3",
    };

    private static readonly string[] WindshieldWipers =
    {
        "MD11_OVHD_L_WIPER_KB", "MD11_OVHD_R_WIPER_KB", "MD11_OVHD_L_RAIN_REPLNT_BT", "MD11_OVHD_R_RAIN_REPLNT_BT",
    };

    private static readonly string[] Miscellaneous =
    {
        "MD11_OVHD_CVR_TEST_BT", "MD11_OVHD_CVR_ERASE_BT", "MD11_OVHD_CRG_DOOR_TEST_BT", "MD11_OVHD_STBY_CMPS_SW",
        "MD11_OVHD_LOCK_AUTO_LT", "MD11_OVHD_LOCK_FAIL_LT", "MD11_OVHD_CRG_DOOR_TEST_LT",
    };

    // ---- Aft Overhead ---------------------------------------------------------------------
    private static readonly string[] CargoFire =
    {
        "MD11_AOVHD_CRGSMK_FWD_VENT_SW", "MD11_AOVHD_CRGSMK_AFT_VENT_SW",
        "MD11_AOVHD_CRGSMK_FWD_AGNT1_GRD", "MD11_AOVHD_CRGSMK_FWD_AGNT1_BT",
        "MD11_AOVHD_CRGSMK_FWD_AGNT2_GRD", "MD11_AOVHD_CRGSMK_FWD_AGNT2_BT",
        "MD11_AOVHD_CRGSMK_AFT_AGNT1_GRD", "MD11_AOVHD_CRGSMK_AFT_AGNT1_BT",
        "MD11_AOVHD_CRGSMK_AFT_AGNT2_GRD", "MD11_AOVHD_CRGSMK_AFT_AGNT2_BT",
        "MD11_AOVHD_CRGSMK_TEST_BT",
        "MD11_AOVHD_CRGSMK_FWD_HEAT_LT", "MD11_AOVHD_CRGSMK_FWD_SMOKE_LT",
        "MD11_AOVHD_CRGSMK_AFT_HEAT_LT", "MD11_AOVHD_CRGSMK_AFT_SMOKE_LT",
        "MD11_AOVHD_CRGSMK_FWD_VENTDISAG_LT", "MD11_AOVHD_CRGSMK_FWD_VENTOFF_LT",
        "MD11_AOVHD_CRGSMK_AFT_VENTDISAG_LT", "MD11_AOVHD_CRGSMK_AFT_VENTOFF_LT", "MD11_AOVHD_CRGSMK_TEST_LT",
    };

    private static readonly string[] EngineFire =
    {
        "MD11_AOVHD_ENG1FIRE_GRD", "MD11_AOVHD_ENG1FIRE_KB",
        "MD11_AOVHD_ENG2FIRE_GRD", "MD11_AOVHD_ENG2FIRE_KB",
        "MD11_AOVHD_ENG3FIRE_GRD", "MD11_AOVHD_ENG3FIRE_KB",
        "MD11_AOVHD_FIRETEST_BT",
        "MD11_AOVHD_ENG1FIRE_LT", "MD11_AOVHD_ENG2FIRE_LT", "MD11_AOVHD_ENG3FIRE_LT",
        "MD11_AOVHD_ENG1AGENT1LO_LT", "MD11_AOVHD_ENG1AGENT2LO_LT",
        "MD11_AOVHD_ENG2AGENT1LO_LT", "MD11_AOVHD_ENG2AGENT2LO_LT",
        "MD11_AOVHD_ENG3AGENT1LO_LT", "MD11_AOVHD_ENG3AGENT2LO_LT",
    };

    private static readonly string[] Apu =
    {
        "MD11_AOVHD_APU_START_BT", "MD11_AOVHD_APU_GEN_BT", "MD11_AOVHD_APUFIRE_KB",
        "MD11_AOVHD_APU_FUEL_LT", "MD11_AOVHD_APU_DOOR_LT", "MD11_AOVHD_APU_FAIL_LT", "MD11_AOVHD_APUFIRE_LT",
    };

    private static readonly string[] Evacuation =
    {
        "MD11_AOVHD_EVAC_GRD", "MD11_AOVHD_EVAC_SW", "MD11_AOVHD_EVAC_HORNSHUT_SW", "MD11_AOVHD_EMER_LT",
    };

    private static readonly string[] Gpws =
    {
        "MD11_AOVHD_GPWS_GRD", "MD11_AOVHD_GPWS_SW", "MD11_AOVHD_GPWS_TERROVRD_BT",
    };

    // ---- Glareshield ----------------------------------------------------------------------
    private static readonly string[] FlightControlPanel =
    {
        "MD11_CGS_HDGTRK_BT", "MD11_CGS_HDG_KB", "MD11_CGS_HDG_BASE_KB", "MD11_CGS_NAV_BT",
        "MD11_CGS_IASMACH_BT", "MD11_CGS_SPD_KB", "MD11_CGS_FMSSPD_BT",
        "MD11_CGS_FTM_BT", "MD11_CGS_ALT_KB", "MD11_CGS_PROF_BT",
        "MD11_CGS_VS_FPA_BT", "MD11_CGS_VS_KB",
        "MD11_CGS_APPRLAND_BT", "MD11_CGS_AUTOFLIGHT_BT",
        "MD11_CGS_AFSOVRD1_SW", "MD11_CGS_AFSOVRD2_SW",
        "MD11_CGS_PNL_LT_KB", "MD11_CGS_FLOOD_LT_KB",
    };

    private static string[] Efis(string p) => new[]
    {
        $"MD11_{p}_BAROSET_CAP", $"MD11_{p}_BAROSET_KB", $"MD11_{p}_INHP_BT",
        $"MD11_{p}_MINIMUMS_CAP", $"MD11_{p}_MINIMUMS_KB", $"MD11_{p}_MAGTRU_BT",
        $"MD11_{p}_VOR_BT", $"MD11_{p}_APPR_BT", $"MD11_{p}_TCAS_BT", $"MD11_{p}_MAP_BT", $"MD11_{p}_PLAN_BT",
        $"MD11_{p}_INCR_BT", $"MD11_{p}_DECR_BT", $"MD11_{p}_WXBRT_KB",
        $"MD11_{p}_VOR1_BT", $"MD11_{p}_ADF1_BT", $"MD11_{p}_VOR2_BT", $"MD11_{p}_ADF2_BT",
        $"MD11_{p}_TRFC_BT", $"MD11_{p}_DATA_BT", $"MD11_{p}_WPT_BT", $"MD11_{p}_VORNDB_BT", $"MD11_{p}_ARPT_BT",
    };

    private static string[] Warnings(string p) => new[]
    {
        $"MD11_{p}_MST_WRN_BT", $"MD11_{p}_MST_CAUT_BT", $"MD11_{p}_GS_BT",
        $"MD11_{p}_ABS_DISARM_LT", $"MD11_{p}_BELOW_GS_LT", $"MD11_{p}_ENG_FAIL_LT",
    };

    // ---- Instrument Panel -----------------------------------------------------------------
    private static string[] SourceInput(string p) => new[]
    {
        $"MD11_{p}_INP_FLTDIROFF_BT", $"MD11_{p}_INP_FLTDIR_BT", $"MD11_{p}_INP_FMS_BT", $"MD11_{p}_INP_IRS_BT",
        $"MD11_{p}_INP_CADC_BT", $"MD11_{p}_INP_VOR_BT", $"MD11_{p}_INP_APPR_BT", $"MD11_{p}_INP_EIS_KB",
        $"MD11_{p}_INP_FLTDIROFF_LT",
        $"MD11_{p}_INP_FLTDIRCAP2_LT", $"MD11_{p}_INP_FLTDIRFO1_LT", $"MD11_{p}_INP_FMSCAP2_LT", $"MD11_{p}_INP_FMSFO1_LT",
        $"MD11_{p}_INP_IRS_CAPTAUX_LT", $"MD11_{p}_INP_IRS_FOAUX_LT", $"MD11_{p}_INP_CADCCAP2_LT", $"MD11_{p}_INP_CADCFO1_LT",
        $"MD11_{p}_INP_VORCAP2_LT", $"MD11_{p}_INP_VORFO1_LT", $"MD11_{p}_INP_APPRCAP2_LT", $"MD11_{p}_INP_APPRFO1_LT",
        $"MD11_{p}_INP_EIS_CAP2_LT", $"MD11_{p}_INP_EIS_CAPAUX_LT", $"MD11_{p}_INP_EIS_FO1_LT", $"MD11_{p}_INP_EIS_FOAUX_LT",
    };

    private static readonly string[] LandingGear =
    {
        "MD11_MIP_GEAR_SW", "MD11_MIP_HANDLEREL_BT", "MD11_MIP_CTR_GEAR_GRD", "MD11_MIP_CTR_GEAR_BT",
        "MD11_MIP_NOSE_GREEN_LT", "MD11_MIP_NOSE_RED_LT", "MD11_MIP_LEFT_GREEN_LT", "MD11_MIP_LEFT_RED_LT",
        "MD11_MIP_RIGHT_GREEN_LT", "MD11_MIP_RIGHT_RED_LT", "MD11_MIP_CTR_GREEN_LT", "MD11_MIP_CTR_RED_LT",
    };

    private static readonly string[] BrakesAndAntiskid =
    {
        "MD11_CTR_AUTOBRAKE_SW", "MD11_CTR_ANTISKID_BT", "MD11_CTR_AUX_HYD_PUMP_BT",
        "MD11_CTR_SLAT_STOW_GRD", "MD11_CTR_SLAT_STOW_BT",
    };

    private static readonly string[] StandbyInstruments =
    {
        "MD11_MIP_STBY_AI_CAGE_BT", "MD11_MIP_ISFD_BARO_KB", "MD11_MIP_ISFD_INHP_BT",
        "MD11_MIP_ISFD_STD_BT", "MD11_MIP_ISFD_TEST_BT", "knob_kohlsman",
    };

    private static readonly string[] CaptainSide =
    {
        "MD11_LSIDE_OXY_FLOW_SW", "MD11_LSIDE_OXY_TEST_BT", "MD11_LSIDE_TIMER_SW", "MD11_LSIDE_TIMER_BT",
        "MD11_LSIDE_FLOOR_SW", "MD11_LSIDE_BRIEFCASE_KB", "MD11_LSIDE_PTT_BT", "MD11_MIP_CAPT_EVTMKR_SW",
        "MD11_CTR_FLTNO1_SW", "MD11_CTR_FLTNO2_SW", "MD11_CTR_FLTNO3_SW", "MD11_CTR_FLTNO4_SW",
        "MD11_LSIDE_WINDOW", "l_window_shade_pull",
        "MD11_LSIDE_OXY_FLOW_IND",
    };

    private static readonly string[] FirstOfficerSide =
    {
        "MD11_RSIDE_OXY_FLOW_SW", "MD11_RSIDE_OXY_TEST_BT", "MD11_RSIDE_TIMER_SW", "MD11_RSIDE_TIMER_BT",
        "MD11_RSIDE_FLOOR_SW", "MD11_RSIDE_BRIEFCASE_KB", "MD11_RSIDE_PTT_BT", "MD11_MIP_FO_EVTMKR_SW",
        // "Mirror_l_window_shade_pull" is the F/O's shade despite the name — FOAux_Light.xml,
        // ANIM_NAME MD11_RSIDE_WINDOW_SHADE, event 95518 next to MD11_RSIDE_WINDOW's 95517.
        "MD11_RSIDE_WINDOW", "Mirror_l_window_shade_pull",
        "MD11_RSIDE_OXY_FLOW_IND",
    };

    private static readonly string[] Yokes =
    {
        "MD11_LYOKE_AP_BT", "MD11_LYOKE_TRIM_SW", "MD11_RYOKE_AP_BT", "MD11_LYOKE_TRIM_SW001",
    };

    // ---- Pedestal -------------------------------------------------------------------------
    // Grouped by ENGINE — starter then fuel for each in turn (the owner's ruling, 2026-09-06),
    // then the items that are not per engine. The lamps follow the same order.
    private static readonly string[] ThrottleQuadrant =
    {
        "MD11_THR_L_START_SW", "MD11_THR_L_FUEL_SW",
        "MD11_THR_C_START_SW", "MD11_THR_C_FUEL_SW",
        "MD11_THR_R_START_SW", "MD11_THR_R_FUEL_SW",
        "MD11_THR_GA_BT", "MD11_THR_L_ATS_BT", "MD11_THR_R_ATS_BT",
        "MD11_THR_PARK_LVR", "MD11_THR_GEAR_HORN_BT",
        "MD11_THR_L_START_LT", "MD11_THR_L_FUEL_LT",
        "MD11_THR_C_START_LT", "MD11_THR_C_FUEL_LT",
        "MD11_THR_R_START_LT", "MD11_THR_R_FUEL_LT",
        "MD11_THR_PARK_LT",
    };

    private static readonly string[] Flaps = { "MD11_FLAP_LATCH", "MD11_DIALAFLAP_WHEEL_RNG" };
    private static readonly string[] Speedbrake = { "MD11_SPDBRK_HANDLE" };
    private static readonly string[] Trim = { "MD11_THR_LONG_TRIM_SW", "MD11_PED_RUD_TRIM_SW", "MD11_PED_AIL_TRIM_KB" };

    private static readonly string[] SystemDisplayControlPanel =
    {
        "MD11_PED_SD_ENG_BT", "MD11_PED_SD_HYD_BT", "MD11_PED_SD_ELEC_BT", "MD11_PED_SD_AIR_BT", "MD11_PED_SD_FUEL_BT",
        "MD11_PED_SD_CONFIG_BT", "MD11_PED_SD_MISC_BT", "MD11_PED_SD_STATUS_BT", "MD11_PED_SD_CONSEQ_BT", "MD11_PED_SD_ND_BT",
        "MD11_PED_DU1_BRT_KB", "MD11_PED_DU2_BRT_KB", "MD11_PED_DU3_BRT_KB", "MD11_PED_DU4_BRT_KB", "MD11_PED_DU5_BRT_KB", "MD11_PED_DU6_BRT_KB",
    };

    // The two frequency TUNER knobs are deliberately absent, for the reason the transponder's digit
    // keys are: the Radios panel carries a typed standby-frequency field and a transfer button per
    // radio (Md11Radios.PanelKeys), which is how a blind pilot tunes. They stay registered controls,
    // just not rows — see RadioFrequencyTuners.
    private static string[] RadioPanel(string p) => new[]
    {
        $"MD11_PED_{p}_RADIO_PNL_VHF1_BT", $"MD11_PED_{p}_RADIO_PNL_VHF2_BT", $"MD11_PED_{p}_RADIO_PNL_VHF3_BT",
        $"MD11_PED_{p}_RADIO_PNL_HF1_BT", $"MD11_PED_{p}_RADIO_PNL_HF2_BT",
        $"MD11_PED_{p}_RADIO_PNL_XFER_BT",
    };

    /// <summary>
    /// The six radio frequency tuner knobs — MHz (outer) and kHz (inner) for Captain, First Officer
    /// and Observer. Never rows, for TWO independent reasons, either of which is sufficient.
    ///
    /// They are ROTARY ENCODERS: the only events they carry are WHEEL_UP and WHEEL_DOWN, so the map
    /// gives them an empty value map, and a positional control with no positions renders as a
    /// READ-ONLY STATUS row. The pilot could therefore never operate one from the panel.
    ///
    /// And what that row showed was meaningless. The knob's own L:var is not a position — it is a
    /// SIGNED CUMULATIVE CLICK COUNTER. Measured live on a loaded MD-11 (2026-09-18): it read 0 with
    /// COM1 standby already tuned to 135.100, went 0 → 1 → 2 → 3 on three WHEEL_UP events (the
    /// standby stepping 135.100 → 138.100 alongside it) and back to 2 on a WHEEL_DOWN. It starts at
    /// zero on every load whatever the radios are set to, so the row a pilot saw read "0" forever and
    /// would have read an odometer if they ever had turned it in the cockpit.
    ///
    /// Tuning goes through <see cref="Md11Radios.PanelKeys"/> — the typed standby field and the
    /// transfer button — exactly as the squawk goes through its typed field rather than the keypad.
    /// </summary>
    public static readonly string[] RadioFrequencyTuners =
    {
        "MD11_PED_CPT_OUTER_RADIO_FREQ_SEL_KB", "MD11_PED_CPT_INNER_RADIO_FREQ_SEL_KB",
        "MD11_PED_FO_OUTER_RADIO_FREQ_SEL_KB", "MD11_PED_FO_INNER_RADIO_FREQ_SEL_KB",
        "MD11_PED_OBS_OUTER_RADIO_FREQ_SEL_KB", "MD11_PED_OBS_INNER_RADIO_FREQ_SEL_KB",
    };

    // The eight digit keys are deliberately absent: the panel carries a typed squawk field
    // instead (Md11Squawk.SetKey, inserted after the TCAS filter by the definition), which
    // presses these keys itself. They stay registered controls, just not rows.
    private static readonly string[] Transponder =
    {
        "MD11_PED_XPNDR_MODE_KB", "MD11_PED_XPNDR_SEL_KB", "MD11_PED_XPNDR_ALT_RPTG_KB", "MD11_PED_XPNDR_ABV_BLW_SW",
        "MD11_PED_XPNDR_CLR_BT", "MD11_PED_XPNDR_IDENT_BT", "MD11_PED_XPNDR_TEST_BT",
        "MD11_PED_XPNDR_FAIL_LT",
    };

    /// <summary>
    /// Controls a panel row supersedes: driven by the app on the pilot's behalf, never listed, and
    /// exempt from the safety net that appends every unlisted control. The transponder keypad,
    /// driven by the typed squawk field, and the six radio frequency tuner knobs, driven by the
    /// typed standby field and the transfer button (<see cref="RadioFrequencyTuners"/>).
    /// </summary>
    public static readonly HashSet<string> SupersededByEntryField =
        new(Md11Squawk.DigitButtons.Concat(RadioFrequencyTuners), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Controls the MCDU window presses on the pilot's behalf: every key and the brightness knob
    /// of all three units. Registered (the window needs them), never a panel row — a panel of 74
    /// keys per unit was a second, worse keyboard for a screen a blind pilot reads in the window.
    /// </summary>
    public static bool IsMcduControl(string nodeId)
        => nodeId.StartsWith("MD11_LMCDU_", StringComparison.OrdinalIgnoreCase)
        || nodeId.StartsWith("MD11_CMCDU_", StringComparison.OrdinalIgnoreCase)
        || nodeId.StartsWith("MD11_RMCDU_", StringComparison.OrdinalIgnoreCase);

    /// <summary>A control the app's own UI supersedes — the squawk keypad, the radio tuners, the MCDU window's keys — never a row, exempt from the safety net.</summary>
    public static bool IsSuperseded(string nodeId) => SupersededByEntryField.Contains(nodeId) || IsMcduControl(nodeId);

    /// <summary>
    /// True when the transparent auto-open will lift this guard's cover for the pilot, so the guard
    /// needs no row of its own.
    ///
    /// <c>EnsureGuardOpenAsync</c> runs from the ACTUATION of the control underneath: a control that
    /// names this guard in its <c>guard_id</c> has its cover read, lifted if closed and settled
    /// before the press or walk goes out. Where that happens the guard row is pure noise — a second
    /// control for something already done, and one a pilot can leave in the wrong position.
    ///
    /// It does NOT always happen, which is why this is derived per guard rather than assumed for all
    /// of them. Two shapes keep their row, and both are read off the map so the rule stays true as
    /// the map changes:
    ///   • NO control names the guard at all. Three do this today — EVAC, GPWS and the Main Cargo
    ///     Door Operation arm switch are guarded in the aircraft, but the generator never linked
    ///     their covers, so nothing triggers an auto-open. Measured live (2026-09-18): both the EVAC
    ///     and GPWS covers sit CLOSED on a loaded aircraft, and firing the GPWS guard's own event
    ///     lifts it (0 → 1), so the row is the only way a pilot has to reach them.
    ///   • Every control naming it is one <c>SetControl</c> REFUSES — a read-only export
    ///     (<see cref="Md11ExportBacked.IsReadOnly"/>) or a composite. The three engine fire handles
    ///     are this: their row is read-only, so no actuation ever reaches the guard chain.
    /// Link a guard in the generator, or make a refused control operable, and its row disappears on
    /// its own — nothing here needs editing.
    /// </summary>
    public static bool IsAutoOpenedGuard(Md11Control guard, Md11ControlMap map)
    {
        if (guard == null || map == null || guard.Kind != Md11Kinds.Guard) return false;
        var exportVars = new HashSet<string>(map.ExportVars, StringComparer.OrdinalIgnoreCase);

        foreach (var covered in map.Controls)
        {
            if (!string.Equals(covered.GuardId, guard.NodeId, StringComparison.OrdinalIgnoreCase)) continue;
            if (Md11ExportBacked.IsReadOnly(covered, exportVars)) continue;   // SetControl refuses it
            if (covered.Composite != null) continue;                          // SetControl refuses it
            return true;                                                      // this one actuates, so the cover gets lifted
        }
        return false;
    }

    private static readonly string[] WeatherRadar =
    {
        "MD11_PED_WXR_OFF_BT", "MD11_PED_WXR_WX_BT", "MD11_PED_WXR_WXT_BT", "MD11_PED_WXR_MAP_BT", "MD11_PED_WXR_TEST_BT",
        "MD11_PED_WXR_STAB_BT", "MD11_PED_WXR_SYS_BT", "MD11_PED_WXR_IDNT_BT", "MD11_PED_WXR_GAIN_KB", "MD11_PED_WXR_TILT_KB",
    };

    /// <summary>
    /// One audio panel. <paramref name="p"/> is the node prefix; the F/O's SAT TEL lamp is spelled
    /// TELL in TFDi's XML. <paramref name="satVolLamp"/> is a second, independent asymmetry: only
    /// the Observer panel has a SAT Volume ON lamp — Captain and First Officer have the SAT volume
    /// knob but no lamp for it (verified against the control map: no
    /// <c>{p}_SAT_VOL_LT</c> node exists for either pilot position). Omitted (default null) for
    /// those two; passed for Observer.
    /// </summary>
    private static string[] AudioPanel(string p, string telLamp, string? satVolLamp = null)
    {
        var keys = new List<string>
        {
            $"{p}_VHF1_MIC_BT", $"{p}_VHF2_MIC_BT", $"{p}_VHF3_MIC_BT", $"{p}_HF1_MIC_BT", $"{p}_HF2_MIC_BT",
            $"{p}_SAT_MIC_BT", $"{p}_INT_MIC_BT", $"{p}_CAB_MIC_BT", $"{p}_IDENT_BT", $"{p}_INT_RADIO_SW",
            $"{p}_VHF1_VOL_KB", $"{p}_VHF2_VOL_KB", $"{p}_VHF3_VOL_KB", $"{p}_HF1_VOL_KB", $"{p}_HF2_VOL_KB",
            $"{p}_SAT_VOL_KB", $"{p}_INT_VOL_KB", $"{p}_CAB_VOL_KB", $"{p}_PA_VOL_KB",
            $"{p}_ILS1_VOL_KB", $"{p}_ILS2_VOL_KB", $"{p}_VOR1_VOL_KB", $"{p}_VOR2_VOL_KB",
            $"{p}_ADF1_VOL_KB", $"{p}_ADF2_VOL_KB", $"{p}_MKR_VOL_KB",
            $"{p}_VHF1_CALL_LT", $"{p}_VHF2_CALL_LT", $"{p}_VHF3_CALL_LT", $"{p}_HF1_CALL_LT", $"{p}_HF2_CALL_LT",
            $"{p}_CAB_CALL_LT", $"{p}_INT_MECH_LT", telLamp,
            $"{p}_VHF1_VOL_LT", $"{p}_VHF2_VOL_LT", $"{p}_VHF3_VOL_LT", $"{p}_HF1_VOL_LT", $"{p}_HF2_VOL_LT",
        };
        if (satVolLamp != null) keys.Add(satVolLamp);
        keys.AddRange(new[]
        {
            $"{p}_INT_VOL_LT", $"{p}_CAB_VOL_LT", $"{p}_PA_VOL_LT",
            $"{p}_ILS1_VOL_LT", $"{p}_ILS2_VOL_LT", $"{p}_VOR1_VOL_LT", $"{p}_VOR2_VOL_LT",
            $"{p}_ADF1_VOL_LT", $"{p}_ADF2_VOL_LT", $"{p}_MKR_VOL_LT",
        });
        return keys.ToArray();
    }

    private static readonly string[] CockpitDoor =
    {
        "MD11_PED_CKPTDOOR_LOCK_KB", "MD11_FLIGHTDECK_DOOR", "MD11_PED_CKPTDOOR_AUTO_LT", "MD11_PED_CKPTDOOR_FAIL_LT",
    };

    private static readonly string[] EmergencyControls = { "MD11_PED_MAN_GEAR_LVR", "MD11_PED_ADG_LVR" };

    // ---- Ground and Exterior --------------------------------------------------------------
    private static readonly string[] Doors =
    {
        "1L_UP", "1L_DN", "1R_UP", "1R_DN", "2L_UP", "2L_DN", "2R_UP", "2R_DN",
        "Object7525", "Object7524", "Object7527", "Object7526", "Object7537", "Object7536", "Object7530", "Object7531",
        "MD11_EXT_DOOR_PAXC_1L_OPEN_SW", "MD11_EXT_DOOR_PAXC_1R_OPEN_SW",
        "MD11_EXT_DOOR_PAX_1L_ARMED_LVR_OBJ", "MD11_EXT_DOOR_PAX_1R_ARMED_LVR_OBJ",
        // Doors 1L and 1R have a second clickspot each (Cylinder11904 / Cylinder11813) that
        // fires the same event as the _OBJ node above, so the generator keeps only one of each.
        "Cylinder12061", "Cylinder12038",
        "Cylinder12064", "Cylinder12058", "Cylinder12057_08", "Cylinder11762_03",
        "MD11_EXT_DOOR_CRG_MAIN_ARM_GRD", "MD11_EXT_DOOR_CRG_MAIN_ARM_SW",
        "MD11_EXT_DOOR_CRG_MAIN_OPEN_SW", "MD11_EXT_DOOR_CRG_MAIN_DOWN_TO_CAN_BT",
        "MD11_EXT_DOOR_PAXC_1L_DISARM_LT", "MD11_EXT_DOOR_PAXC_1R_DISARM_LT",
        "MD11_EXT_DOOR_CRG_MAIN_OPEN_LT", "MD11_EXT_DOOR_CRG_MAIN_CLSD_READY_LT",
        "MD11_EXT_DOOR_CRG_MAIN_LOCK_LT", "MD11_EXT_DOOR_CRG_MAIN_UNLOCK_LT", "MD11_EXT_DOOR_CRG_MAIN_PWR_LT",
    };

    private static readonly string[] Cabin = { "MD11_CABIN_OXY_MASKS_DOOR", "MD11_CABIN_OXY_MASKS", "MD11_CABIN_POWER" };
    private static readonly string[] AircraftOptions = { "MD11_OVHD_1_PAX_LOAD_SW", "MD11_OVHD_10_PAX_LOAD_SW", "MD11_OVHD_100_PAX_LOAD_SW" };
    // One toggle, not two: both nodes fired event 94465, so the generator keeps a single row.
    private static readonly string[] Efb = { "MD11_EFB_TOGGLE" };

    // ---- Circuit breakers (grid order: row letter, then column) ---------------------------
    private static readonly Regex Grid = new(@"_([A-Z])(\d+)$", RegexOptions.Compiled);

    private static string[] Breakers(Md11ControlMap map, string prefix) =>
        map.Controls.Where(c => c.NodeId.StartsWith(prefix, StringComparison.Ordinal))
            .Select(c => (c.NodeId, M: Grid.Match(c.NodeId)))
            .OrderBy(t => t.M.Success ? t.M.Groups[1].Value : "~")
            .ThenBy(t => t.M.Success ? int.Parse(t.M.Groups[2].Value) : int.MaxValue)
            .Select(t => t.NodeId).ToArray();

    // ---- The table ------------------------------------------------------------------------
    public static IReadOnlyList<Md11LayoutSection> Sections(Md11ControlMap map) => new[]
    {
        new Md11LayoutSection("Overhead", new[]
        {
            new Md11LayoutPanel("Electrical", Electrical), new Md11LayoutPanel("IRS", Irs), new Md11LayoutPanel("Fuel", Fuel),
            new Md11LayoutPanel("Hydraulic", Hydraulic), new Md11LayoutPanel("Air", Air),
            new Md11LayoutPanel("Cabin Pressurization", CabinPressurization), new Md11LayoutPanel("Anti-Ice", AntiIce),
            new Md11LayoutPanel("Engines and Ignition", EnginesAndIgnition), new Md11LayoutPanel("Flight Controls", FlightControls),
            new Md11LayoutPanel("Lights and Signs", LightsAndSigns), new Md11LayoutPanel("Cockpit Lights", CockpitLights),
            new Md11LayoutPanel("Windshield Wipers", WindshieldWipers), new Md11LayoutPanel("Miscellaneous", Miscellaneous),
        }),
        new Md11LayoutSection("Aft Overhead", new[]
        {
            new Md11LayoutPanel("Cargo Fire", CargoFire), new Md11LayoutPanel("Engine Fire", EngineFire),
            new Md11LayoutPanel("APU", Apu), new Md11LayoutPanel("Evacuation", Evacuation), new Md11LayoutPanel("GPWS", Gpws),
        }),
        new Md11LayoutSection("Glareshield", new[]
        {
            new Md11LayoutPanel("Flight Control Panel", FlightControlPanel),
            new Md11LayoutPanel("EFIS Captain", Efis("LECP")), new Md11LayoutPanel("EFIS First Officer", Efis("RECP")),
            new Md11LayoutPanel("Warnings Captain", Warnings("GSL")), new Md11LayoutPanel("Warnings First Officer", Warnings("GSR")),
        }),
        new Md11LayoutSection("Instrument Panel", new[]
        {
            new Md11LayoutPanel("Source Input Captain", SourceInput("LSIDE")),
            new Md11LayoutPanel("Source Input First Officer", SourceInput("RSIDE")),
            new Md11LayoutPanel("Landing Gear", LandingGear), new Md11LayoutPanel("Brakes and Antiskid", BrakesAndAntiskid),
            new Md11LayoutPanel("Standby Instruments", StandbyInstruments),
            new Md11LayoutPanel("Captain Side", CaptainSide), new Md11LayoutPanel("First Officer Side", FirstOfficerSide),
            new Md11LayoutPanel("Yokes", Yokes),
        }),
        new Md11LayoutSection("Pedestal", new[]
        {
            new Md11LayoutPanel("Throttle Quadrant", ThrottleQuadrant), new Md11LayoutPanel("Flaps", Flaps),
            new Md11LayoutPanel("Speedbrake", Speedbrake), new Md11LayoutPanel("Trim", Trim),
            new Md11LayoutPanel("System Display Control Panel", SystemDisplayControlPanel),
            new Md11LayoutPanel("Radios", RadioPanel("CPT").Concat(RadioPanel("FO")).Concat(RadioPanel("OBS")).ToArray()),
            new Md11LayoutPanel("Transponder", Transponder), new Md11LayoutPanel("Weather Radar", WeatherRadar),
            new Md11LayoutPanel("Audio Panel Captain", AudioPanel("MD11_PED_CPT_AUDIO_PNL", "MD11_PED_CPT_AUDIO_PNL_SAT_TEL_LT")),
            new Md11LayoutPanel("Audio Panel First Officer", AudioPanel("MD11_PED_FO_AUDIO_PNL", "MD11_PED_FO_AUDIO_PNL_SAT_TELL_LT")),
            new Md11LayoutPanel("Audio Panel Observer", AudioPanel("MD11_OBS_AUDIO_PNL", "MD11_OBS_AUDIO_PNL_SAT_TEL_LT", "MD11_OBS_AUDIO_PNL_SAT_VOL_LT")),
            new Md11LayoutPanel("Cockpit Door", CockpitDoor), new Md11LayoutPanel("Emergency Controls", EmergencyControls),
        }),
        new Md11LayoutSection("Ground and Exterior", new[]
        {
            new Md11LayoutPanel("Doors", Doors), new Md11LayoutPanel("Cabin", Cabin),
            new Md11LayoutPanel("Aircraft Options", AircraftOptions), new Md11LayoutPanel("EFB", Efb),
        }),
        new Md11LayoutSection("Circuit Breakers", new[]
        {
            new Md11LayoutPanel("Circuit Breakers Upper", Breakers(map, "MD11_BKR_BWU_")),
            new Md11LayoutPanel("Circuit Breakers Lower", Breakers(map, "MD11_BKR_BWL_")),
            new Md11LayoutPanel("Circuit Breakers Left Aft", Breakers(map, "MD11_BKR_LAP_")),
            new Md11LayoutPanel("Circuit Breakers Overhead", Breakers(map, "MD11_BKR_OVHD_")),
        }),
    };

    /// <summary>Where an unlisted control's map area lands in the fallback (spec §3.8 safety net).</summary>
    public static string SectionForArea(string area) => area switch
    {
        "Overhead" => "Overhead",
        "Aft Overhead" => "Aft Overhead",
        var a when a.StartsWith("Glareshield") || a.EndsWith("EFIS Control Panel") => "Glareshield",
        "Main Instrument Panel" or "Center Instrument" or "Captain Side Panel" or "F/O Side Panel" or "Captain Yoke" or "F/O Yoke" => "Instrument Panel",
        "Pedestal" or "Throttle Quadrant" or "Flaps" or "Dial-A-Flap" or "Speedbrake" or "Flight Deck Door" => "Pedestal",
        var a when a.StartsWith("Audio Panel") => "Pedestal",
        "Doors and Exterior" or "Cabin" or "Aircraft Options" or "EFB" or "Lighting" => "Ground and Exterior",
        var a when a.StartsWith("MCDU") => "Pedestal",
        "Circuit Breakers" => "Circuit Breakers",
        _ => "Other",
    };

    public static Md11Placement Place(Md11ControlMap map)
    {
        var byId = new Dictionary<string, Md11Control>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in map.Controls) byId.TryAdd(c.NodeId, c);

        var structure = new Dictionary<string, List<string>>();
        var controls = new Dictionary<string, List<string>>();
        var displays = new Dictionary<string, List<string>>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var section in Sections(map))
        {
            var names = new List<string>();
            foreach (var panel in section.Panels)
            {
                var keys = new List<string>();
                var lamps = new List<string>();
                foreach (var key in panel.Keys)
                {
                    if (!byId.TryGetValue(key, out var c)) { missing.Add(key); continue; }
                    if (c.Kind == Md11Kinds.Option) continue;
                    if (!placed.Add(c.NodeId)) continue;
                    // Registered, never a row. Marked placed FIRST (above), so the safety net does
                    // not turn round and append what this drops.
                    if (IsSuperseded(c.NodeId) || IsAutoOpenedGuard(c, map)) continue;
                    // A lamp is a Status Display row, never a tab stop (the Airbus pattern);
                    // everything operable is a control row. The table's order survives in both.
                    if (c.Kind == Md11Kinds.Annunciator) lamps.Add(c.NodeId);
                    else keys.Add(c.NodeId);
                }
                if (keys.Count == 0 && lamps.Count == 0) continue;
                names.Add(panel.Name);
                controls[panel.Name] = keys;   // possibly empty: MainForm needs the entry to build the panel at all
                if (lamps.Count > 0) displays[panel.Name] = lamps;
            }
            if (names.Count > 0) structure[section.Name] = names;
        }

        // Safety net: every operable control the table does not name is appended, never dropped.
        var unplaced = new List<string>();
        foreach (var c in map.Controls)
        {
            if (c.Kind is Md11Kinds.Annunciator or Md11Kinds.Option || placed.Contains(c.NodeId)) continue;
            if (IsSuperseded(c.NodeId) || IsAutoOpenedGuard(c, map)) continue;
            unplaced.Add(c.NodeId);
            var sectionName = SectionForArea(c.Area);
            var panelName = $"{c.Area} (other)";
            if (!structure.TryGetValue(sectionName, out var names)) structure[sectionName] = names = new List<string>();
            if (!names.Contains(panelName)) names.Add(panelName);
            if (!controls.TryGetValue(panelName, out var keys)) controls[panelName] = keys = new List<string>();
            keys.Add(c.NodeId);
        }

        return new Md11Placement(structure, controls, displays, unplaced, missing);
    }
}
