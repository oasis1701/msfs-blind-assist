using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class FlyByWireA380Definition
{
    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var p = new Dictionary<string, List<string>>();

        p["ELEC"] = new List<string>
        {
            "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO",
            "A32NX_OVHD_ELEC_BAT_ESS_PB_IS_AUTO", "A32NX_OVHD_ELEC_BAT_APU_PB_IS_AUTO",
            // Ground / external power kept next to the battery controls (user request).
            "A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", "A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON",
            "A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", "A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON",
            "A32NX_OVHD_ELEC_BUS_TIE_PB_IS_AUTO", "A32NX_OVHD_ELEC_AC_ESS_FEED_PB_IS_NORMAL",
            "A32NX_OVHD_ELEC_GALY_AND_CAB_PB_IS_AUTO", "A32NX_OVHD_ELEC_COMMERCIAL_PB_IS_ON",
            "A32NX_OVHD_ELEC_IDG_1_PB_IS_RELEASED", "A32NX_OVHD_ELEC_IDG_2_PB_IS_RELEASED",
            "A32NX_OVHD_ELEC_IDG_3_PB_IS_RELEASED", "A32NX_OVHD_ELEC_IDG_4_PB_IS_RELEASED",
            "ELEC_ENG_GEN:1", "ELEC_ENG_GEN:2", "ELEC_ENG_GEN:3", "ELEC_ENG_GEN:4",
            "ELEC_APU_GEN:1", "ELEC_APU_GEN:2",
            "A32NX_OVHD_EMER_ELEC_GEN_1_LINE_PB_IS_ON", "A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_IS_PRESSED",
            "A32NX_EMERELECPWR_GEN_TEST", "A380X_OVHD_ELEC_BAT_SELECTOR_KNOB"
        };
        p["APU"] = new List<string>
        {
            "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", "A32NX_OVHD_APU_START_PB_IS_ON",
            "A32NX_APU_AUTOEXITING_TEST_ON"
        };
        p["Fuel"] = new List<string>
        {
            "A380X_OVHD_FUEL_OUTRTK_XFR_PB_IS_AUTO", "A380X_OVHD_FUEL_MIDTK_XFR_PB_IS_AUTO",
            "A380X_OVHD_FUEL_INRTK_XFR_PB_IS_AUTO", "A380X_OVHD_FUEL_TRIMTK_XFR_PB_IS_AUTO",
            "A380X_OVHD_FUEL_EMER_OUTR_XFR_PB_IS_ON", "A380X_OVHD_FUEL_JETTISON_ARM_PB_IS_ON",
            "A380X_OVHD_FUEL_JETTISON_ACTIVE_PB_IS_ON",
            "FUELPUMP_FEEDTK1_MAIN", "FUELPUMP_FEEDTK1_STBY",
            "FUELPUMP_FEEDTK2_MAIN", "FUELPUMP_FEEDTK2_STBY",
            "FUELPUMP_FEEDTK3_MAIN", "FUELPUMP_FEEDTK3_STBY",
            "FUELPUMP_FEEDTK4_MAIN", "FUELPUMP_FEEDTK4_STBY",
            "FUELPUMP_OUTR_L", "FUELPUMP_MID_L_FWD", "FUELPUMP_MID_L_AFT",
            "FUELPUMP_INR_L_FWD", "FUELPUMP_INR_L_AFT",
            "FUELPUMP_OUTR_R", "FUELPUMP_MID_R_FWD", "FUELPUMP_MID_R_AFT",
            "FUELPUMP_INR_R_FWD", "FUELPUMP_INR_R_AFT",
            "FUELPUMP_TRIM_L", "FUELPUMP_TRIM_R"
        };
        p["Hydraulics"] = new List<string>
        {
            "A32NX_OVHD_HYD_ENG_1A_PUMP_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_1B_PUMP_PB_IS_AUTO",
            "A32NX_OVHD_HYD_ENG_2A_PUMP_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_2B_PUMP_PB_IS_AUTO",
            "A32NX_OVHD_HYD_ENG_3A_PUMP_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_3B_PUMP_PB_IS_AUTO",
            "A32NX_OVHD_HYD_ENG_4A_PUMP_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_4B_PUMP_PB_IS_AUTO",
            "A32NX_OVHD_HYD_EPUMPGA_ON_PB_IS_AUTO", "A32NX_OVHD_HYD_EPUMPGA_OFF_PB_IS_AUTO",
            "A32NX_OVHD_HYD_EPUMPGB_ON_PB_IS_AUTO", "A32NX_OVHD_HYD_EPUMPGB_OFF_PB_IS_AUTO",
            "A32NX_OVHD_HYD_EPUMPYA_ON_PB_IS_AUTO", "A32NX_OVHD_HYD_EPUMPYA_OFF_PB_IS_AUTO",
            "A32NX_OVHD_HYD_EPUMPYB_ON_PB_IS_AUTO", "A32NX_OVHD_HYD_EPUMPYB_OFF_PB_IS_AUTO",
            "A32NX_OVHD_HYD_EPUMPB_PB_IS_AUTO", "A32NX_OVHD_HYD_EPUMPY_PB_IS_AUTO",
            "A32NX_OVHD_HYD_ENG_1AB_PUMP_DISC_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_2AB_PUMP_DISC_PB_IS_AUTO",
            "A32NX_OVHD_HYD_ENG_3AB_PUMP_DISC_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_4AB_PUMP_DISC_PB_IS_AUTO",
            "A32NX_OVHD_HYD_RAT_MAN_ON_IS_PRESSED"
        };
        p["Bleed Air"] = new List<string>
        {
            "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
            "A32NX_OVHD_PNEU_ENG_1_BLEED_PB_IS_AUTO", "A32NX_OVHD_PNEU_ENG_2_BLEED_PB_IS_AUTO",
            "A32NX_OVHD_PNEU_ENG_3_BLEED_PB_IS_AUTO", "A32NX_OVHD_PNEU_ENG_4_BLEED_PB_IS_AUTO",
            "A32NX_KNOB_OVHD_AIRCOND_XBLEED_POSITION", "A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_POSITION",
            "A32NX_OVHD_COND_PACK_1_PB_IS_ON", "A32NX_OVHD_COND_PACK_2_PB_IS_ON",
            "A32NX_OVHD_COND_HOT_AIR_1_PB_IS_ON", "A32NX_OVHD_COND_HOT_AIR_2_PB_IS_ON",
            "A32NX_OVHD_COND_RAM_AIR_PB_IS_ON"
        };
        p["Air Conditioning"] = new List<string>
        {
            "COND_CKPT_TEMP_SET", "COND_CABIN_TEMP_SET"
        };
        p["Pressurization"] = new List<string>
        {
            "A32NX_OVHD_PRESS_MAN_ALTITUDE_PB_IS_AUTO", "PRESS_MAN_ALT_SET",
            "A32NX_OVHD_PRESS_MAN_VS_CTL_PB_IS_AUTO", "PRESS_MAN_VS_SET",
            "A32NX_OVHD_PRESS_DITCHING_PB_IS_ON"
        };
        p["Ventilation"] = new List<string>
        {
            "A32NX_OVHD_VENT_CAB_FANS_PB_IS_ON", "A32NX_OVHD_VENT_AIR_EXTRACT_PB_IS_ON",
            "A32NX_OVHD_VENT_BLOWER_PB_IS_ON"
        };
        p["Cargo Air"] = new List<string>
        {
            "CARGO_FWD_TEMP_SET", "CARGO_BULK_TEMP_SET",
            "A32NX_OVHD_CARGO_AIR_ISOL_VALVES_FWD_PB_IS_ON", "A32NX_OVHD_CARGO_AIR_ISOL_VALVES_BULK_PB_IS_ON",
            "A32NX_OVHD_CARGO_AIR_HEATER_PB_IS_ON"
        };
        p["Anti Ice"] = new List<string>
        {
            "A32NX_MAN_PITOT_HEAT", "WING_ANTI_ICE_OVHD",
            "ENG1_ANTI_ICE", "ENG2_ANTI_ICE", "ENG3_ANTI_ICE", "ENG4_ANTI_ICE"
        };
        p["Fire"] = new List<string>
        {
            "A380X_OVHD_ENG1_FIRE_GUARD", "A32NX_FIRE_BUTTON_ENG1",
            "A380X_OVHD_ENG2_FIRE_GUARD", "A32NX_FIRE_BUTTON_ENG2",
            "A380X_OVHD_ENG3_FIRE_GUARD", "A32NX_FIRE_BUTTON_ENG3",
            "A380X_OVHD_ENG4_FIRE_GUARD", "A32NX_FIRE_BUTTON_ENG4",
            "A380X_OVHD_APU_FIRE_GUARD", "A32NX_FIRE_BUTTON_APU",
            "A32NX_OVHD_FIRE_AGENT_1_ENG_1_IS_PRESSED", "A32NX_OVHD_FIRE_AGENT_2_ENG_1_IS_PRESSED",
            "A32NX_OVHD_FIRE_AGENT_1_ENG_2_IS_PRESSED", "A32NX_OVHD_FIRE_AGENT_2_ENG_2_IS_PRESSED",
            "A32NX_OVHD_FIRE_AGENT_1_ENG_3_IS_PRESSED", "A32NX_OVHD_FIRE_AGENT_2_ENG_3_IS_PRESSED",
            "A32NX_OVHD_FIRE_AGENT_1_ENG_4_IS_PRESSED", "A32NX_OVHD_FIRE_AGENT_2_ENG_4_IS_PRESSED",
            "A32NX_OVHD_FIRE_AGENT_1_APU_1_IS_PRESSED",
            "A32NX_CARGOSMOKE_DISCH1LOCK_TOGGLE", "A32NX_CARGOSMOKE_DISCH2LOCK_TOGGLE",
            "A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED", "A32NX_FIRE_TEST_CARGO"
        };
        p["Oxygen"] = new List<string>
        {
            "PUSH_OVHD_OXYGEN_CREW", "A32NX_OXYGEN_MASKS_DEPLOYED", "A32NX_OXYGEN_TMR_RESET"
        };
        p["Calls"] = new List<string>
        {
            "A32NX_CALLS_EMER_ON", "A32NX_EVAC_COMMAND_TOGGLE", "A32NX_EVAC_CAPT_TOGGLE",
            // "Signal Cabin Ready" action button (pulses CALLS ALL → FWS sets CABIN_READY).
            // Replaces the old read-only A32NX_CABIN_READY combo that did nothing when set;
            // the live status stays the read-only readout in d["Cockpit"].
            "A380X_MSFSBA_SIGNAL_CABIN_READY",
            "PUSH_OVHD_CALLS_ALL", "PUSH_OVHD_CALLS_FWD", "PUSH_OVHD_CALLS_AFT", "PUSH_OVHD_CALLS_MECH"
        };
        // UNIFIED Cockpit panel — the cockpit door, the sliding windows + shades, and the
        // openable cockpit panels/seats are now one organized group (was three separate panels:
        // "Cockpit Door", "Windows and Shades", "Cockpit"). Ordered: door → windows → shades →
        // openable panels → seats/armrests.
        p["Cockpit"] = new List<string>
        {
            // ---- Door ----
            "A32NX_COCKPIT_DOOR_LOCKED", "A32NX_OVHD_COCKPITDOORVIDEO_TOGGLE",
            // ---- Sliding windows ----
            "CPT_SLIDING_WINDOW", "FO_SLIDING_WINDOW",
            // ---- Sunshades / visors (accessible 0-100% drag sliders) ----
            "SUNSHADE_CPT_OPENING", "SUNSHADE_FO_OPENING",
            "SUNSHADE_FWD_LH", "SUNSHADE_FWD_CTR", "SUNSHADE_FWD_RH",
            "AFT_LH_SUNSHADE_OPENING", "AFT_RH_SUNSHADE_OPENING",
            "CPT_SMALL_SHADE", "FO_SMALL_SHADE",
            // ---- Openable cockpit panels (oxygen, tables, footrests, LG-pins door) ----
            "CPT_OXY_FWD_OPENING", "AFT_OXY_OPENING",
            "A380_CPT_TABLE", "A380_FO_TABLE",
            "A380_CPT_FOOTREST", "A380_FO_FOOTREST",
            "A380_LGPIN_DOOR",
            // ---- MSFS-2024-native-rebuild openables (meal tables, keyboard trays, CAS/OIT
            // panels, physical cockpit-door open — distinct from the door LOCK above) ----
            "A380_CPT_MEALTABLE", "A380_FO_MEALTABLE",
            "A380_CPT_KEYBOARD", "A380_FO_KEYBOARD",
            "CAS_LH_OPENING", "CAS_RH_OPENING", "AFT_OIT_OPENING", "COCKPITDOOR_OPEN",
            // ---- Crew seats (start/stop motor toggle BUTTONS only) + armrests ----
            // The 4 SEAT_*_MOVE_* position read-outs were REMOVED from the panel (user request):
            // the moving buttons + the spoken position-on-stop are enough. The position L:vars stay
            // REGISTERED (OnRequest) so ToggleSeatMotor can seed/read them and AnnounceSeatPosition
            // can speak the band when a motor stops — they are just no longer listed as panel fields.
            "SEATBTN_CPT_UP", "SEATBTN_CPT_DOWN", "SEATBTN_CPT_FWD", "SEATBTN_CPT_AFT",
            "SEATBTN_FO_UP", "SEATBTN_FO_DOWN", "SEATBTN_FO_FWD", "SEATBTN_FO_AFT",
            "BIGARMREST_CPT_UP_DOWN", "BIGARMREST_CPT_TILT", "SMALLARMREST_CPT_FWD",
            "BIGARMREST_FO_UP_DOWN", "BIGARMREST_FO_TILT", "SMALLARMREST_FO_FWD",
            // Armrest STOW toggles (fold the armrest away) — 2024-native-rebuild additions.
            "BIGARMREST_CPT_STOW", "BIGARMREST_FO_STOW",
            "SMALLARMREST_CPT_STOW", "SMALLARMREST_FO_STOW",
            // ---- Laptops / access-panel lock / visual model toggles (completeness pass) ----
            "A380X_SWITCH_LAPTOP_POWER_LEFT", "A380X_SWITCH_LAPTOP_POWER_RIGHT",
            "A32NX_SWITCH_DOORPANEL_LOCK",
            "A380X_CABIN_HIDDEN", "A380X_CPT_SIDESTICK_HIDDEN", "A380X_FO_SIDESTICK_HIDDEN",
            "A380X_CPT_EFB_HIDDEN", "A380X_FO_EFB_HIDDEN"
        };
        p["Signs"] = new List<string>
        {
            "SEATBELT_SIGN", "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position",
            "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position"
        };
        p["ADIRS"] = new List<string>
        {
            "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB",
            "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", "A32NX_OVHD_ADIRS_IR_1_PB_IS_ON",
            "A32NX_OVHD_ADIRS_IR_2_PB_IS_ON", "A32NX_OVHD_ADIRS_IR_3_PB_IS_ON",
            "A32NX_OVHD_ADIRS_ADR_1_PB_IS_ON", "A32NX_OVHD_ADIRS_ADR_2_PB_IS_ON",
            "A32NX_OVHD_ADIRS_ADR_3_PB_IS_ON"
        };
        p["Flight Control Computers"] = new List<string>
        {
            "A32NX_PRIM_1_PUSHBUTTON_PRESSED", "A32NX_PRIM_2_PUSHBUTTON_PRESSED", "A32NX_PRIM_3_PUSHBUTTON_PRESSED",
            "A32NX_SEC_1_PUSHBUTTON_PRESSED", "A32NX_SEC_2_PUSHBUTTON_PRESSED", "A32NX_SEC_3_PUSHBUTTON_PRESSED",
            "A32NX_RUDDER_TRIM_RESET", "RUDDER_TRIM_LEFT", "RUDDER_TRIM_RIGHT",
            "A32NX_TILLER_PEDAL_DISCONNECT"
        };
        // Overhead engine panel — both groups are physically on the A380 overhead
        // (FBW model nodes PUSH_OVHD_ENG_FADEC1..4 under "ENG FADEC GND PWR", and
        // PUSH_OVHD_ENGMANSTART_1..4 + ALTN in the Eng_Man_Start group). Named to
        // reflect BOTH families (the FADEC ground-power PBs are not "start" controls).
        p["Engine FADEC and Manual Start"] = new List<string>
        {
            "A32NX_OVHD_FADEC_1", "A32NX_OVHD_FADEC_2", "A32NX_OVHD_FADEC_3", "A32NX_OVHD_FADEC_4",
            "A32NX_ENGMANSTART1_TOGGLE", "A32NX_ENGMANSTART2_TOGGLE",
            "A32NX_ENGMANSTART3_TOGGLE", "A32NX_ENGMANSTART4_TOGGLE",
            // ALTN manual start belongs to the same overhead Eng_Man_Start group
            // (node PUSH_OVHD_ENGMANSTARTALTN), not "Recorder and Misc".
            "A32NX_ENGMANSTARTALTN_TOGGLE"
        };
        p["Recorder and Misc"] = new List<string>
        {
            "A32NX_AVIONICS_COMPLT_ON",
            "A380X_OVHD_STORM_LT",   // cockpit door video moved to the unified p["Cockpit"]
            "A32NX_ACMS_TRIGGER_ON", "A32NX_CREW_HEAD_SET", "A32NX_SVGEINT_OVRD_ON",
            // ENGMANSTARTALTN moved to the "Engine FADEC and Manual Start" overhead panel.
            "A32NX_ENTERTAINMENT_CWS_OFF",
            "A32NX_ENTERTAINMENT_IFEC_OFF", "A380X_REMOTE_CB_CTRL",
            // Completeness pass — ELT + DLS (inop but present), rain repellent (inop).
            "A32NX_ELT_ON", "A32NX_DLS_ON",
            "A32NX_RAIN_REPELLENT_LEFT_ON", "A32NX_RAIN_REPELLENT_RIGHT_ON",
        };
        // VHF-only — the non-VHF audio channels were dead L:vars (unmodelled by FBW),
        // pruned 2026-06-13. See the OnOff/Slider block above for the full rationale.
        p["Audio Control Panel Captain"] = new List<string>
        {
            "A380X_RMP_1_VHF_VOL_RX_SWITCH_1", "A380X_RMP_1_VHF_VOL_RX_SWITCH_2", "A380X_RMP_1_VHF_VOL_RX_SWITCH_3",
            "A380X_RMP_1_VHF_VOL_1", "A380X_RMP_1_VHF_VOL_2", "A380X_RMP_1_VHF_VOL_3",
            "A380X_RMP_1_BRIGHTNESS_KNOB",
            "A380X_RMP_1_VHF_TX_1", "A380X_RMP_1_VHF_TX_2", "A380X_RMP_1_VHF_TX_3"
        };
        p["Audio Control Panel First Officer"] = new List<string>
        {
            "A380X_RMP_2_VHF_VOL_RX_SWITCH_1", "A380X_RMP_2_VHF_VOL_RX_SWITCH_2", "A380X_RMP_2_VHF_VOL_RX_SWITCH_3",
            "A380X_RMP_2_VHF_VOL_1", "A380X_RMP_2_VHF_VOL_2", "A380X_RMP_2_VHF_VOL_3",
            "A380X_RMP_2_BRIGHTNESS_KNOB",
            "A380X_RMP_2_VHF_TX_1", "A380X_RMP_2_VHF_TX_2", "A380X_RMP_2_VHF_TX_3"
        };
        // (Radio Management Panel removed — the RMP is now the dedicated accessible RMP WINDOW,
        // Ctrl+Shift+R in input mode → FBWA380RmpForm, scraping A380X_RMP_1/2 + firing the keypad H-events.)
        p["Interior Lighting"] = new List<string>
        {
            "A380X_OVHD_ANN_LT_POSITION", "A32NX_OVHD_INTLT_ANN",
            "A32NX_LIGHTING_PRESET_LOAD", "A32NX_LIGHTING_PRESET_SAVE",
            // Passenger-cabin lighting (moved here from the flyPad Quick Controls, which
            // can't be set through the injected agent — see CLAUDE.md flyPad note).
            "CABIN_BRIGHTNESS_SET", "A32NX_CABIN_USING_AUTOBRIGHTNESS", "A32NX_CABIN_AUTOBRIGHTNESS"
        };
        p["Exterior Lighting"] = new List<string>
        {
            "LIGHT_BEACON", "LIGHT_STROBE", "LIGHT_NAV", "LIGHT_WING", "LIGHT_LOGO",
            "LIGHT_LANDING", "NOSE_LIGHT", "LIGHT_RWY_TURNOFF"
        };

        p["Warnings"] = new List<string>
        {
            "PUSH_AUTOPILOT_MASTERAWARN_L", "PUSH_AUTOPILOT_MASTERAWARN_R",
            "PUSH_AUTOPILOT_MASTERCAUT_L", "PUSH_AUTOPILOT_MASTERCAUT_R",
            "A32NX_MASTER_WARNING", "A32NX_MASTER_CAUTION"
        };
        // EFIS Control Panel split per side (Captain / First Officer), PMDG-style.
        p["EFIS Captain"] = new List<string>
        {
            "A380X_EFIS_L_LS_BUTTON_IS_ON", "A380X_EFIS_L_VV_BUTTON_IS_ON", "A380X_EFIS_L_CSTR_BUTTON_IS_ON",
            "A380X_EFIS_L_ARPT_BUTTON_IS_ON", "A380X_EFIS_L_TRAF_BUTTON_IS_ON",
            "A32NX_EFIS_L_ND_MODE", "A32NX_EFIS_L_ND_RANGE",
            "A380X_EFIS_L_ACTIVE_FILTER", "A380X_EFIS_L_ACTIVE_OVERLAY",
            "A32NX_EFIS_L_NAVAID_1_MODE", "A32NX_EFIS_L_NAVAID_2_MODE",
            "A32NX_FCU_LEFT_EIS_BARO_IS_STD", "CAPT_QNH_SET", "XMLVAR_Baro_Selector_HPA_1",
            "A32NX_EFIS_L_OANS_RANGE",
            // Flight Director 1 (captain). The earlier removal said writes "fail",
            // but the engage-state L:var IS settable and HOLDS via the calculator
            // path (re-verified live: set 1 → still 1 after 2.5 s).
            "FD_1_CTL"
        };
        p["EFIS First Officer"] = new List<string>
        {
            "A380X_EFIS_R_LS_BUTTON_IS_ON", "A380X_EFIS_R_VV_BUTTON_IS_ON", "A380X_EFIS_R_CSTR_BUTTON_IS_ON",
            "A380X_EFIS_R_ARPT_BUTTON_IS_ON", "A380X_EFIS_R_TRAF_BUTTON_IS_ON",
            "A32NX_EFIS_R_ND_MODE", "A32NX_EFIS_R_ND_RANGE",
            "A380X_EFIS_R_ACTIVE_FILTER", "A380X_EFIS_R_ACTIVE_OVERLAY",
            "A32NX_EFIS_R_NAVAID_1_MODE", "A32NX_EFIS_R_NAVAID_2_MODE",
            "A32NX_FCU_RIGHT_EIS_BARO_IS_STD", "FO_QNH_SET", "XMLVAR_Baro_Selector_HPA_2",
            "A32NX_EFIS_R_OANS_RANGE",
            "FD_2_CTL"   // Flight Director 2 (F/O) — see captain side
        };
        p["FCU"] = new List<string>
        {
            // Engage/mode controls as stateful combos (show live state, pick to
            // toggle) instead of blind buttons — see HandleUIVariableSet.
            "A32NX_AUTOPILOT_1_ACTIVE", "A32NX_AUTOPILOT_2_ACTIVE", "A32NX_AUTOTHRUST_STATUS",
            "A32NX_FCU_LOC_MODE_ACTIVE", "A32NX_FCU_APPR_MODE_ACTIVE", "A32NX_FMA_EXPEDITE_MODE",
            "A32NX_TRK_FPA_MODE_ACTIVE",
            // Genuine momentary knob push/pulls stay as buttons.
            "A32NX.FCU_TO_AP_HDG_PUSH", "A32NX.FCU_TO_AP_HDG_PULL",
            "A32NX.FCU_SPD_PUSH", "A32NX.FCU_SPD_PULL",
            "A32NX.FCU_ALT_PUSH", "A32NX.FCU_ALT_PULL", "XMLVAR_AUTOPILOT_ALTITUDE_INCREMENT",
            "A32NX.FCU_VS_PUSH", "A32NX.FCU_TO_AP_VS_PULL",
            "A32NX.FCU_SPD_MACH_TOGGLE_PUSH",
            "A32NX.FCU_AP_DISCONNECT_PUSH", "A32NX.FCU_ATHR_DISCONNECT_PUSH",
            "A32NX_METRIC_ALT_TOGGLE"
        };
        p["OIT"] = new List<string> { "A380X_SWITCH_OIT_SIDE_LEFT", "A380X_SWITCH_OIT_SIDE_RIGHT" };

        p["Gear"] = new List<string> { "A32NX_GEAR_HANDLE_POSITION", "A32NX_LG_GRVTY_SWITCH_POS",
            "A32NX_LG_GRVTY_MASTER_SWITCH_GUARD", "A32NX_LG_GRVTY_SWITCH_GUARD_1", "A32NX_LG_GRVTY_SWITCH_GUARD_2" };
        // Computer-reset (CB) overhead panel — the 10 latching reset pushbuttons.
        p["Reset"] = _resetPanelVars.Select(t => t.key).ToList();
        p["Autobrake"] = new List<string>
        {
            // ANTISKID_BRAKES_ACTIVE is settable (K:ANTISKID_BRAKES_TOGGLE) and
            // lives in p["Flaps and Brakes"]; the Autobrake read-out (d[...])
            // keeps a status copy.
            "A32NX_AUTOBRAKES_SELECTED_MODE", "A32NX_OVHD_AUTOBRK_RTO_ARM_IS_PRESSED"
        };
        p["Source Switching"] = new List<string>
        {
            "A32NX_ATT_HDG_SWITCHING_KNOB", "A32NX_AIR_DATA_SWITCHING_KNOB",
            "A32NX_EIS_DMC_SWITCHING_KNOB", "A32NX_PUSH_TRUE_REF"
        };
        // Clock panel: the chronometer start/stop + reset buttons and the elapsed-time
        // (ET) Run/Stop/Reset knob are the controls; the elapsed-time readouts are the
        // status display (d["Clock"]).
        p["Clock"] = new List<string>
        {
            "A32NX_CHRONO_TOGGLE", "A32NX_CHRONO_RST", "A32NX_CHRONO_ET_SWITCH_POS"
        };

        p["Engines"] = new List<string>
        {
            "ENGINE_MODE_SELECTOR",
            // ENG MASTER 1-4 are now Off/On combos (state from the fuel-valve SimVar,
            // set fires FUELSYSTEM_VALVE_OPEN/CLOSE) — no separate On/Off buttons.
            "ENG_VALVE_SWITCH:1", "ENG_VALVE_SWITCH:2", "ENG_VALVE_SWITCH:3", "ENG_VALVE_SWITCH:4"
            // ENG MAN START 1-4 live in the overhead "Engine FADEC and Manual Start" panel (not duplicated here).
        };
        p["Thrust Levers"] = new List<string>
        {
            "THROTTLE_ALL_DETENT", "THROTTLE_1_DETENT", "THROTTLE_2_DETENT",
            "THROTTLE_3_DETENT", "THROTTLE_4_DETENT"
        };
        p["Flaps and Brakes"] = new List<string> { "A32NX_FLAPS_HANDLE_INDEX", "A32NX_PARK_BRAKE_LEVER_POS", "ANTISKID_BRAKES_ACTIVE" };
        p["Speed Brake"] = new List<string> { "A380X_MSFSBA_SPEEDBRAKE", "A380X_MSFSBA_SPEEDBRAKE_SLIDER", "A380X_MSFSBA_SPOILERS_ARM" };
        p["ECAM Control Panel"] = new List<string>
        {
            "A32NX_ECAM_SD_CURRENT_PAGE_INDEX", "A32NX_BTN_ALL", "A32NX_BTN_ABNPROC", "A32NX_BTN_CL",
            "A32NX_BTN_CLR", "A32NX_BTN_CLR2", "A32NX_BTN_RCL", "A32NX_BTN_TOCONFIG",
            "A32NX_BTN_EMERCANC", "A32NX_BTN_UP", "A32NX_BTN_DOWN", "A32NX_BTN_MORE"
        };
        p["Weather Radar"] = new List<string>
        {
            "XMLVAR_A320_WeatherRadar_Sys", "XMLVAR_A320_WeatherRadar_Mode",
            "A32NX_SWITCH_RADAR_PWS_Position", "A32NX_RADAR_MULTISCAN_AUTO", "A32NX_RADAR_GCS_AUTO"
        };
        p["Transponder"] = new List<string>
        {
            // Only squawk code + IDENT are settable here (working stock events). The transponder
            // MODE is AUTO-managed by the FBW systems-host (Standby on ground / Mode C airborne)
            // and not externally settable — it is surfaced as the read-only auto-announcing
            // "Transponder Mode" read-out (XPNDR_STATE in d["Transponder"]). The AUTO/STBY +
            // ALT RPTG + TCAS toggles are EventBus-only -> the MFD SURV CONTROLS page (rarely
            // needed; AUTO + TA/RA are the correct VATSIM defaults). See the GetVariables note.
            "TRANSPONDER_CODE_SET", "XPNDR_IDENT_ON"
        };
        // "Radios" (stock COM standby-set + swap) REMOVED — the FBW A380 ignores the stock
        // COM_STBY_RADIO_SET_HZ / COM*_RADIO_SWAP events (live-verified: setting COM1 standby
        // to 119.000 left it at 121.95). All radio tuning goes through the RMP, so the dedicated
        // RMP window (Ctrl+Shift+R) is the real interface. "RMP" stays as a read-only quick-glance.
        p["RMP"] = new List<string>
        {
            "A380X_RMP_1_STATE", "A380X_RMP_2_STATE", "A380X_RMP_3_STATE"
        };
        p["Minimums"] = new List<string>();
        // Cockpit Door lock + door video live in the unified p["Cockpit"] now (see above).
        // Cabin Ready is read-only (auto-announced via Mon) — surfaced as a status readout in
        // d["Cockpit"] (display section below), not a settable control.

        // ---- Ground Services panels REMOVED (everything ground/handling via the flyPad) ----
        // No "Doors", "Ground Equipment" or "Ground Services" panels: the jetway / stairs /
        // fuel-truck / baggage / catering ACTIONS are done on the flyPad Ground page, not here.
        // The door / jetway-motion / chocks / cones / external-power STATE still auto-announces
        // on change (those vars stay registered + IsAnnounced) — there's just no panel to Tab to.

        p["Status"] = new List<string>
        {
            // FMS_PAX_NUMBER and ECAM_FAILURE_ACTIVE are read-only — they belong in
            // the Status read-out (d["Status"]), not here where they'd render as
            // pointlessly-settable controls. Only the FMS switching knob is settable.
            "A32NX_FMS_SWITCHING_KNOB"
        };

        // ---- A32NX shared gap controls folded into panels ----
        p["Fuel"].AddRange(new[]
        {
            "XFEED_1_STATE", "XFEED_2_STATE", "XFEED_3_STATE", "XFEED_4_STATE"
        });
        p["Hydraulics"].Add("A32NX_OVHD_HYD_PTU_PB_IS_AUTO");
        // (Brake Fan control removed — not modelled on the A380 dev build; see the Brakes section.)
        p["Recorder and Misc"].AddRange(new[]
        {
            "A32NX_DFDR_EVENT_ON",
            "A32NX_OVHD_NSS_DATA_TO_AVNCS_TOGGLE", "A32NX_NSS_MASTER_OFF"
        });
        p["GPWS"] = new List<string>
        {
            "A32NX_GPWS_SYS_OFF", "A32NX_GPWS_GS_OFF", "A32NX_GPWS_FLAPS_OFF",
            "A32NX_GPWS_TERR_OFF", "A32NX_GPWS_TEST"
        };
        // "PFD" is NOT a navigable control panel — it's the variable set the
        // (now-retired) PFD-window hotkey requested/read. Intentionally absent from
        // GetPanelStructure so it isn't shown as a UI panel.
        // PFD / ND are status-box-only display panels (the read-out lives in
        // GetPanelDisplayVariables); no interactive controls.
        p["PFD"] = new List<string>();
        p["ND"] = new List<string>();
        p["Interior Lighting"].Add("A380X_OVHD_EXTLT_STBY_COMPASS_ICE_IND_SWITCH_POS");
        // (EFIS filter/overlay/baro-unit/OANS folded into the per-side EFIS panels above.)
        p["ECAM Control Panel"].AddRange(new[] { "A32NX_BTN_CHECK_LH", "A32NX_BTN_CHECK_RH" });
        // (A32NX_DCDU_ATC_MSG_ACK removed — the A380 has no DCDU to acknowledge on.)

        // ---- new panels ----
        p["ISIS"] = new List<string> { "A32NX_ISIS_LS_ACTIVE", "A32NX_ISIS_BARO_MODE", "A32NX_ISIS_BARO_UNIT_INHG" };
        p["Wipers"] = new List<string> { "WIPER_LEFT", "WIPER_RIGHT" };
        p["Speeds"] = new List<string>();
        // KCCU (keyboard/cursor control unit) is the MCDU's input device — it is
        // driven through the MCDU form (Coherent agent), not as a standalone
        // control panel, so it is intentionally NOT exposed as a panel here.
        // (Aircraft-preset loading + pushback panels REMOVED per user request — both are done
        // from the flyPad. MSFSBA auto-announces the preset LOAD PROGRESS instead, see the
        // A32NX_AIRCRAFT_PRESET_LOAD_PROGRESS branch in ProcessSimVarUpdate.)

        return p;
    }

    // ===================================================================
    // Read-only display variables per panel (auto-refreshing readouts)
    // ===================================================================
    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        var d = new Dictionary<string, List<string>>();

        // ELEC: battery voltages + per-unit faults + bus-powered flags.
        var elec = new List<string>();
        for (int n = 1; n <= 4; n++) elec.Add($"A32NX_ELEC_BAT_{n}_POTENTIAL");
        foreach (var id in new[] { "1", "2", "ESS", "APU" }) elec.Add($"A32NX_OVHD_ELEC_BAT_{id}_PB_HAS_FAULT");
        elec.Add("A32NX_OVHD_ELEC_AC_ESS_FEED_PB_HAS_FAULT");
        elec.Add("A32NX_OVHD_ELEC_GALY_AND_CAB_PB_HAS_FAULT");
        elec.Add("A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_HAS_FAULT");
        for (int n = 1; n <= 4; n++)
        {
            elec.Add($"A32NX_OVHD_ELEC_IDG_{n}_PB_HAS_FAULT");
            elec.Add($"A32NX_OVHD_ELEC_IDG_{n}_PB_IS_DISC");
            elec.Add($"A32NX_OVHD_ELEC_ENG_GEN_{n}_PB_HAS_FAULT");
            elec.Add($"A32NX_ELEC_ENG_GEN_{n}_IDG_IS_CONNECTED");
            // (A32NX_EXT_PWR_AVAIL:{n} shows once as "GPU {n} Available" in Ground
            //  Services — not duplicated in the ELEC panel.)
        }
        foreach (var bus in new[] { "AC_1", "AC_2", "AC_3", "AC_4", "AC_ESS", "AC_ESS_SHED", "247XP",
                                    "DC_1", "DC_2", "DC_ESS", "247PP", "DC_HOT_1", "DC_HOT_2", "DC_HOT_3", "DC_HOT_4", "DC_GND_FLT_SVC" })
            elec.Add($"A32NX_ELEC_{bus}_BUS_IS_POWERED");
        d["ELEC"] = elec;

        d["APU"] = new List<string>
        {
            "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", "A32NX_OVHD_APU_MASTER_SW_PB_HAS_FAULT",
            "A32NX_APU_LOW_FUEL_PRESSURE_FAULT", "A32NX_APU_BLEED_AIR_VALVE_OPEN", "A32NX_APU_N_RAW",
            "A32NX_APU_N2", "A32NX_APU_EGT", "A32NX_APU_FLAP_OPEN_PERCENTAGE", "A32NX_APU_FUEL_USED"
        };

        d["Fuel"] = new List<string> { "A32NX_TOTAL_FUEL_QUANTITY" };

        var hyd = new List<string>();
        for (int n = 1; n <= 4; n++)
        {
            hyd.Add($"A32NX_OVHD_HYD_ENG_{n}AB_PUMP_DISC_PB_HAS_FAULT");
            hyd.Add($"A32NX_HYD_ENG_{n}AB_PUMP_DISC");
        }
        hyd.Add("A32NX_HYD_GREEN_SYSTEM_1_SECTION_PRESSURE_SWITCH");
        hyd.Add("A32NX_HYD_YELLOW_SYSTEM_1_SECTION_PRESSURE_SWITCH");
        d["Hydraulics"] = hyd;

        var bleed = new List<string>();
        for (int n = 1; n <= 4; n++) bleed.Add($"A32NX_OVHD_PNEU_ENG_{n}_BLEED_PB_HAS_FAULT");
        bleed.Add("A32NX_OVHD_PNEU_APU_BLEED_PB_HAS_FAULT");
        foreach (var s in new[] { "L", "C", "R" }) bleed.Add($"A32NX_PNEU_XBLEED_VALVE_{s}_OPEN");
        d["Bleed Air"] = bleed;

        var cond = new List<string> { "A32NX_COND_CKPT_TEMP" };
        for (int n = 1; n <= 8; n++) cond.Add($"A32NX_COND_MAIN_DECK_{n}_TEMP");
        for (int n = 1; n <= 7; n++) cond.Add($"A32NX_COND_UPPER_DECK_{n}_TEMP");
        for (int n = 1; n <= 2; n++)
        {
            cond.Add($"A32NX_OVHD_COND_PACK_{n}_PB_HAS_FAULT");
            cond.Add($"A32NX_COND_PACK_{n}_IS_OPERATING");
            cond.Add($"A32NX_COND_PACK_{n}_FLOW_VALVE_1_IS_OPEN");
            cond.Add($"A32NX_COND_PACK_{n}_FLOW_VALVE_2_IS_OPEN");
            cond.Add($"A32NX_OVHD_COND_HOT_AIR_{n}_PB_HAS_FAULT");
            for (int ch = 1; ch <= 2; ch++) cond.Add($"A32NX_COND_FDAC_{n}_CHANNEL_{ch}_FAILURE");
        }
        foreach (var z in new[] { "FWD", "BULK" })
        {
            cond.Add($"A32NX_OVHD_CARGO_AIR_ISOL_VALVES_{z}_PB_HAS_FAULT");
            cond.Add($"A32NX_OVHD_CARGO_AIR_ISOL_VALVES_{z}_IS_ON");
        }
        cond.Add("A32NX_OVHD_CARGO_AIR_HEATER_PB_HAS_FAULT");
        d["Air Conditioning"] = cond;

        var press = new List<string>();
        for (int n = 1; n <= 4; n++)
        {
            for (int ch = 1; ch <= 2; ch++) press.Add($"A32NX_PRESS_OCSM_{n}_CHANNEL_{ch}_FAILURE");
            press.Add($"A32NX_PRESS_OCSM_{n}_AUTO_PARTITION_FAILURE");
        }
        press.Add("A32NX_PRESS_CABIN_ALTITUDE_B1");
        press.Add("A32NX_PRESS_CABIN_VS_B1");
        press.Add("A32NX_PRESS_CABIN_DELTA_PRESSURE_B1");
        press.Add("A32NX_FM1_LANDING_ELEVATION");
        for (int v = 1; v <= 4; v++) press.Add($"A32NX_PRESS_OUTFLOW_VALVE_{v}_OPEN_PERCENTAGE_B1");
        d["Pressurization"] = press;

        var vent = new List<string>();
        foreach (var id in new[] { "FWD", "AFT" })
            for (int ch = 1; ch <= 2; ch++) vent.Add($"A32NX_VENT_{id}_VCM_CHANNEL_{ch}_FAILURE");
        vent.Add("A32NX_VENT_OVERPRESSURE_RELIEF_VALVE_IS_OPEN");
        d["Ventilation"] = vent;

        d["Anti Ice"] = new List<string> { "A32NX_ICING_STATE_ICING_STICK_INDICATOR" };

        var fire = new List<string> { "A32NX_FIRE_DETECTED_MLG" };
        for (int n = 1; n <= 4; n++)
        {
            fire.Add($"A32NX_ENG_{n}_ON_FIRE");
            for (int b = 1; b <= 2; b++)
            {
                fire.Add($"A32NX_FIRE_SQUIB_{b}_ENG_{n}_IS_ARMED");
                fire.Add($"A32NX_FIRE_SQUIB_{b}_ENG_{n}_IS_DISCHARGED");
            }
        }
        fire.Add("A32NX_FIRE_SQUIB_1_APU_1_IS_DISCHARGED");
        d["Fire"] = fire;

        var adirs = new List<string> { "A32NX_ADIRS_REMAINING_IR_ALIGNMENT_TIME" };
        for (int n = 1; n <= 3; n++) adirs.Add($"A32NX_ADIRS_ADIRU_{n}_STATE");
        adirs.Add("A32NX_OVHD_ADIRS_ON_BAT_IS_ILLUMINATED");
        d["ADIRS"] = adirs;

        var fcc = new List<string>();
        for (int n = 1; n <= 3; n++) { fcc.Add($"A32NX_PRIM_{n}_HEALTHY"); fcc.Add($"A32NX_SEC_{n}_HEALTHY"); }
        fcc.Add("A32NX_FWS1_IS_HEALTHY"); fcc.Add("A32NX_FWS2_IS_HEALTHY");
        d["Flight Control Computers"] = fcc;

        var gear = new List<string>();
        foreach (var lc in new[] { "1", "2" })
            foreach (var sd in new[] { "LEFT", "RIGHT" }) gear.Add($"A32NX_LGCIU_{lc}_{sd}_GEAR_DOWNLOCKED");
        gear.Add("A32NX_LGCIU_1_NOSE_GEAR_COMPRESSED");
        d["Gear"] = gear;

        d["Warnings"] = new List<string>
        {
            "A32NX_MASTER_WARNING", "A32NX_MASTER_CAUTION", "A32NX_AUTOPILOT_AUTOLAND_WARNING"
        };

        var ab = new List<string> { "A32NX_AUTOBRAKES_ARMED_MODE", "A32NX_BTV_STATE" };
        for (int n = 1; n <= 16; n++) ab.Add($"A32NX_BRAKE_TEMPERATURE_{n}");
        d["Autobrake"] = ab;

        // Engines: state + per-engine N1/N2/N3/EGT/FF/oil/reverser.
        var eng = new List<string>();
        for (int n = 1; n <= 4; n++) eng.Add($"A32NX_ENGINE_STATE:{n}");
        for (int n = 1; n <= 4; n++)
        {
            eng.Add($"A32NX_ENGINE_N1:{n}"); eng.Add($"A32NX_ENGINE_N2:{n}"); eng.Add($"A32NX_ENGINE_N3:{n}");
            eng.Add($"A32NX_ENGINE_EGT:{n}"); eng.Add($"A32NX_ENGINE_FF:{n}");
            eng.Add($"ENG_OIL_TEMP:{n}"); // oil pressure omitted — not modelled (see GetVariables)
            if (n == 2 || n == 3) eng.Add($"A32NX_REVERSER_{n}_DEPLOYED"); // only inboard engines have reversers
        }
        d["Engines"] = eng;
        // Live per-engine thrust-lever angle readouts (the detent is also spoken
        // automatically as the levers move). The set combos in the panel are
        // write-only, so these read-outs are the actual current state.
        d["Thrust Levers"] = new List<string>
        {
            "A32NX_AUTOTHRUST_TLA:1", "A32NX_AUTOTHRUST_TLA:2",
            "A32NX_AUTOTHRUST_TLA:3", "A32NX_AUTOTHRUST_TLA:4",
            "A32NX_AUTOTHRUST_THRUST_LIMIT"   // EWD green N1-limit % for the current mode
        };

        // Flaps handle is a settable, auto-announced combo in the panel; the speed-brake
        // readouts moved to their own panel below.
        d["Speed Brake"] = new List<string> { "A32NX_SPOILERS_HANDLE_POSITION", "A32NX_SPOILERS_ARMED" };
        // Exterior lights are now On/Off combos in the panel itself (auto-announced),
        // so they are NOT duplicated as read-only display variables here.
        d["RMP"] = new List<string>
        {
            // Reliable STOCK COM freqs (the FBW_RMP_FREQUENCY L:vars read ~19 MHz garbage).
            "COM_ACTIVE_1", "COM_STANDBY_1",
            "COM_ACTIVE_2", "COM_STANDBY_2",
            "COM_ACTIVE_3", "COM_STANDBY_3"
        };
        d["Status"] = new List<string> { "A32NX_FMGC_FLIGHT_PHASE" };
        d["FCU"] = new List<string>
        {
            // AP1/AP2/ATHR/LOC/APPR/EXPED/TRK-FPA are now stateful combos in the
            // FCU control panel, so they're not duplicated here as readouts.
            "A32NX_FMA_LATERAL_MODE", "A32NX_FMA_VERTICAL_MODE",
            "FD_ACTIVE"
        };
        // The EIS baro value is an ARINC429 word — NOT shown as a raw display field
        // (it reads ~14 billion) — but TryGetDisplayOverride decodes it to clean
        // text ("1013 hPa" / "29.92 inHg" / "Standard"), so the same value the
        // pilot hears auto-announced now also reads in the panel. (The preselect QNH
        // read-out was removed — see the baro-preselect note above.)
        d["EFIS Captain"] = new List<string> { "A32NX_FCU_LEFT_EIS_BARO_HPA" };
        d["EFIS First Officer"] = new List<string> { "A32NX_FCU_RIGHT_EIS_BARO_HPA" };
        // d["Radios"] removed with the dead "Radios" panel — the RMP active/standby freqs are
        // in d["RMP"] (FBW L:vars) and the RMP window.
        d["Transponder"] = new List<string> { "XPNDR_CODE", "XPNDR_STATE", "A32NX_DCDU_ATC_MSG_WAITING" };
        // Minimums are the plain-feet L:vars the MFD PERF page writes — TryGetDisplayOverride
        // renders "200 feet" / "Not set". They also auto-announce when set. (The ARINC429
        // FM1 words were dropped: NCD until approach range, so they read "Not set" at cruise.)
        d["Minimums"] = new List<string> { "AIRLINER_MINIMUM_DESCENT_ALTITUDE", "AIRLINER_DECISION_HEIGHT" };

        // ---- A32NX shared gap readouts ----
        d["Autobrake"].AddRange(new[]
        {
            "A32NX_BRAKES_HOT",
            "A32NX_HYD_BRAKE_NORM_LEFT_PRESS", "A32NX_HYD_BRAKE_NORM_RIGHT_PRESS",
            "A32NX_HYD_BRAKE_ALTN_LEFT_PRESS", "A32NX_HYD_BRAKE_ALTN_RIGHT_PRESS", "A32NX_HYD_BRAKE_ALTN_ACC_PRESS"
        });
        d["Gear"].Add("A32NX_GEAR_LEVER_LOCKED");   // master guard moved to p["Gear"] as a control
        d["Pressurization"].AddRange(new[] { "A32NX_OVHD_PRESS_MAN_ALTITUDE_KNOB", "A32NX_OVHD_PRESS_MAN_VS_CTL_KNOB" });
        d["Fuel"].AddRange(new[] { "A380X_OVHD_FUEL_JETTISON_IS_OPEN", "A32NX_TOTAL_FUEL_VOLUME" });
        d["Hydraulics"].Add("A32NX_OVHD_HYD_PTU_PB_HAS_FAULT");
        d["Anti Ice"].AddRange(new[] { "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON", "A32NX_PNEU_WING_ANTI_ICE_HAS_FAULT" });
        d["Anti Ice"].AddRange(new[] { "ENG_ANTI_ICE:1", "ENG_ANTI_ICE:2", "ENG_ANTI_ICE:3", "ENG_ANTI_ICE:4" });
        d["Fire"].AddRange(new[] { "A32NX_CARGOSMOKE_FWD_DISCHARGED", "A32NX_CARGOSMOKE_AFT_DISCHARGED" });
        d["Status"].AddRange(new[] { "A380X_FMS_DEST_EFOB_BELOW_MIN", "A32NX_FMS_PAX_NUMBER", "A32NX_ECAM_FAILURE_ACTIVE" });
        // ANTISKID status copy (the settable control is in Flaps and Brakes).
        d["Autobrake"].Add("ANTISKID_BRAKES_ACTIVE");
        d["Autobrake"].Add("A32NX_AUTOBRAKES_DECEL_LIGHT");   // DECEL light (auto-announced)
        // (Ground Equipment + Doors read-out panels REMOVED — ground handling is flyPad-only.
        // The chocks / cones / jetway-motion / external-power / door vars stay registered and
        // IsAnnounced, so every change still auto-announces; there's just no panel to read.)

        // Clock readouts (the chrono + elapsed-time fields shown read-only in the
        // Clock panel; the controls live in p["Clock"]).
        d["Clock"] = new List<string> { "A32NX_CHRONO_ELAPSED_TIME", "A32NX_CHRONO_ET_ELAPSED_TIME" };
        d["Cockpit"] = new List<string> { "A32NX_CABIN_READY" };
        // ISIS standby-instrument snapshot (attitude/heading/speed/altitude/baro +
        // ILS), decoded in TryGetDisplayOverride. Standby simvars read in DEGREES on
        // the A380 (registered with "degrees" units), unlike the A320 (radians).
        d["ISIS"] = new List<string>
        {
            "PLANE PITCH DEGREES", "PLANE BANK DEGREES", "PLANE HEADING DEGREES MAGNETIC",
            "AIRSPEED INDICATED", "INDICATED ALTITUDE",
            "A32NX_ISIS_BARO_MODE", "A32NX_ISIS_BUGS_ACTIVE"   // LS moved to p["ISIS"] as a control
        };
        // PFD accessible snapshot — FMA modes + armed, autothrust, approach capability,
        // attitude/heading/speed/altitude, and the PFD message line. Single status box.
        d["PFD"] = new List<string>
        {
            "A32NX_FMA_VERTICAL_MODE", "A32NX_FMA_VERTICAL_ARMED",
            "A32NX_FMA_LATERAL_MODE", "A32NX_FMA_LATERAL_ARMED",
            "A32NX_AUTOTHRUST_MODE", "A32NX_AUTOTHRUST_STATUS",
            "A32NX_AUTOPILOT_1_ACTIVE", "A32NX_AUTOPILOT_2_ACTIVE",
            "PLANE PITCH DEGREES", "PLANE BANK DEGREES", "PLANE HEADING DEGREES MAGNETIC",
            "AIRSPEED INDICATED", "INDICATED ALTITUDE",
            "A32NX_PFD_MSG_SET_HOLD_SPEED", "A32NX_PFD_MSG_TD_REACHED",
            "A32NX_PFD_MSG_CHECK_SPEED_MODE", "A32NX_PFD_LINEAR_DEVIATION_ACTIVE", "A32NX_PFD_TARGET_ALTITUDE",
            // Glideslope (ILS) vertical deviation — the OTHER vertical deviation, used on an
            // ILS approach (FMS V/DEV is only active in managed descent). Cached for the PFD
            // window's combined V/DEV readout.
            "A32NX_RADIO_RECEIVER_GS_IS_VALID", "A32NX_RADIO_RECEIVER_GS_DEVIATION",
            // Source-confirmed PFD additions: weight/CG, takeoff V-speeds, Mach, track, ILS.
            "PFD_GROSS_WEIGHT", "A32NX_AIRFRAME_GW_CG_PERCENT_MAC",
            "PFD_V1", "PFD_VR", "PFD_V2", "PFD_MACH", "PFD_TRACK",
            "PFD_RA", "PFD_VS", "PFD_TRANS_ALT", "PFD_TRANS_LVL",
            "FCU_SEL_ALT", "FCU_SEL_HDG", "PFD_SAT", "PFD_TAT",
            "A32NX_BETA_TARGET", "A32NX_TCAS_VSPEED_GREEN:1", "A32NX_TCAS_VSPEED_RED:1",
            "PFD_ILS_FREQ", "PFD_ILS_DME", "A32NX_FM_LS_COURSE", "MARKER_BEACON",
            "PFD_VMAX", "PFD_VLS", "PFD_VALPHAPROT", "PFD_VALPHAMAX", "PFD_VSW",
            "PFD_GREENDOT", "PFD_V3", "PFD_V4", "PFD_VFENEXT",
            // Target/preselect speeds + selected V/S + expedite + flight directors + autobrake.
            "A32NX_SPEEDS_MANAGED_PFD", "A32NX_SpeedPreselVal", "A32NX_MachPreselVal",
            "A32NX_AUTOPILOT_VS_SELECTED", "A32NX_FMA_EXPEDITE_MODE", "FD_1", "FD_2",
            "A32NX_AUTOBRAKES_ARMED_MODE", "PFD_AUTOLAND"
        };
        // ND accessible snapshot — mode/range, TO waypoint (decoded ident + distance/
        // bearing/ETA), cross-track, RNP, and ILS LOC/GS validity + deviation.
        d["ND"] = new List<string>
        {
            "A32NX_EFIS_L_ND_MODE", "A32NX_EFIS_L_ND_RANGE",
            "A32NX_EFIS_L_TO_WPT_IDENT_0", "A32NX_EFIS_L_TO_WPT_DISTANCE",
            "A32NX_EFIS_L_TO_WPT_BEARING", "A32NX_EFIS_L_TO_WPT_ETA",
            "A32NX_FG_CROSS_TRACK_ERROR", "A32NX_FMGC_L_RNP",
            "A32NX_RADIO_RECEIVER_LOC_IS_VALID", "A32NX_RADIO_RECEIVER_LOC_DEVIATION",
            "A32NX_RADIO_RECEIVER_GS_IS_VALID", "A32NX_RADIO_RECEIVER_GS_DEVIATION",
            // Tuned nav radios (frequencies + DME). The tuned-station IDENTS are read by the
            // Output+N "nav radio" hotkey (NAV IDENT string struct), which the numeric display
            // pipeline can't carry.
            "ND_VOR1_FREQ", "ND_VOR1_DME", "ND_VOR2_FREQ", "ND_VOR2_DME",
            "ND_ADF1_FREQ", "ND_ADF2_FREQ",
            // Velocities + wind + heading reference (ARINC, decoded; "not available" on the ground).
            "A32NX_ADIRS_IR_1_GROUND_SPEED", "A32NX_ADIRS_ADR_1_TRUE_AIRSPEED",
            "A32NX_ADIRS_IR_1_WIND_DIRECTION_BNR", "A32NX_ADIRS_IR_1_WIND_SPEED_BNR",
            "A32NX_PUSH_TRUE_REF"
        };
        d["Oxygen"] = new List<string> { "A32NX_OXYGEN_TMR_RESET_FAULT" };
        d["Calls"] = new List<string> { "A32NX_EVAC_COMMAND_FAULT" };
        // The ECP "Status display" box shows the SELECTED SD page's live CONTENT,
        // scraped on each page switch (see RefreshSdPageDisplayAsync + the
        // TryGetDisplayOverride case for this var). The page name + rows render there;
        // the old A32NX_SD_MORE_SHOWN "more flag" line was dropped (it read as the
        // useless "SD more: no").
        d["ECAM Control Panel"] = new List<string> { "A32NX_ECAM_SD_CURRENT_PAGE_INDEX" };
        d["Wipers"] = new List<string> { "WIPER_LEFT", "WIPER_RIGHT" };
        d["Speeds"] = new List<string> { "A32NX_SPEEDS_VLS", "A32NX_SPEEDS_VAPP", "A32NX_SPEEDS_GD", "A32NX_SPEEDS_F", "A32NX_SPEEDS_S" };

        // ---- plain SD-page scalar readouts ----
        for (int n = 1; n <= 4; n++)
        {
            d["ELEC"].AddRange(new[]
            {
                $"A32NX_ELEC_ENG_GEN_{n}_POTENTIAL", $"A32NX_ELEC_ENG_GEN_{n}_LOAD",
                $"A32NX_ELEC_ENG_GEN_{n}_FREQUENCY", $"A32NX_ELEC_ENG_GEN_{n}_IDG_OIL_OUTLET_TEMPERATURE",
                $"A32NX_ELEC_TR_{n}_POTENTIAL", $"A32NX_ELEC_TR_{n}_CURRENT", $"A32NX_ELEC_BAT_{n}_CURRENT"
            });
        }
        for (int n = 1; n <= 2; n++)
            d["ELEC"].AddRange(new[] { $"A32NX_ELEC_APU_GEN_{n}_POTENTIAL", $"A32NX_ELEC_APU_GEN_{n}_LOAD", $"A32NX_ELEC_APU_GEN_{n}_FREQUENCY" });
        d["ELEC"].AddRange(new[] { "A32NX_ELEC_EXT_PWR_POTENTIAL", "A32NX_ELEC_EXT_PWR_FREQUENCY", "A32NX_ELEC_EMER_GEN_POTENTIAL", "A32NX_ELEC_STAT_INV_POTENTIAL" });

        foreach (var sys in new[] { "GREEN", "YELLOW" })
            d["Hydraulics"].AddRange(new[] { $"A32NX_HYD_{sys}_SYSTEM_1_SECTION_PRESSURE", $"A32NX_HYD_{sys}_RESERVOIR_LEVEL", $"A32NX_HYD_{sys}_RESERVOIR_LEVEL_IS_LOW" });

        for (int n = 1; n <= 4; n++)
            d["Bleed Air"].AddRange(new[] { $"A32NX_PNEU_ENG_{n}_PRECOOLER_OUTLET_TEMPERATURE", $"A32NX_PNEU_ENG_{n}_REGULATED_TRANSDUCER_PRESSURE", $"A32NX_PNEU_ENG_{n}_STARTER_VALVE_OPEN" });
        d["Bleed Air"].AddRange(new[] { "A32NX_COND_PACK_1_OUTLET_TEMPERATURE", "A32NX_COND_PACK_2_OUTLET_TEMPERATURE", "A32NX_PNEU_APU_BLEED_CONTAINER_PRESSURE" });

        for (int n = 1; n <= 4; n++)
            // ENG_VALVE_SWITCH:n is now a settable control combo in the Engines panel,
            // so it is no longer listed here as a read-only display field.
            d["Engines"].AddRange(new[] { $"A32NX_ENGINE_OIL_QTY:{n}", $"ENG_FUEL_USED:{n}", $"ENG_VIBRATION:{n}", $"ENG_IGN_POS:{n}" });

        d["Air Conditioning"].AddRange(new[] { "A32NX_COND_CARGO_FWD_TEMP", "A32NX_COND_CARGO_BULK_TEMP", "A32NX_COND_CKPT_DUCT_TEMP" });

        d["Flight Control Computers"].AddRange(new[]
        {
            "A32NX_LEFT_FLAPS_POSITION_PERCENT", "A32NX_RIGHT_FLAPS_POSITION_PERCENT",
            "A32NX_LEFT_SLATS_POSITION_PERCENT", "A32NX_RIGHT_SLATS_POSITION_PERCENT",
            "ELEVATOR_TRIM", "A32NX_TO_PITCH_TRIM", "A32NX_SEC_1_RUDDER_ACTUAL_POSITION",
            "A32NX_NOSE_WHEEL_POSITION", "A32NX_TILLER_HANDLE_POSITION",
            "ELEVATOR_DEFLECTION", "AILERON_DEFLECTION", "RUDDER_DEFLECTION",
            "SPOILERS_LEFT_POSITION", "SPOILERS_RIGHT_POSITION",
            "A32NX_PRIORITY_TAKEOVER:1", "A32NX_PRIORITY_TAKEOVER:2"
        });

        d["Gear"].AddRange(new[] { "GEAR_LEFT_POS", "GEAR_CENTER_POS", "GEAR_RIGHT_POS", "A32NX_AUTOBRAKES_RTO_ARMED" });

        // NOTE: annunciators (faults + non-fault state lights) ARE listed here on
        // purpose. They render into the panel's single READ-ONLY "Status Display"
        // text field (MainForm.UpdateDisplayText) — never as a settable combo — so
        // they read on demand AND auto-announce on change. They legitimately overlap
        // some E/WD memos; that twin call-out (annunciator + memo) is fine.
        return d;
    }

    public override Dictionary<string, string> GetButtonStateMapping()
    {
        return new Dictionary<string, string>
        {
            ["A32NX_AUTOBRAKES_SELECTED_MODE"] = "A32NX_AUTOBRAKES_ARMED_MODE",
            // FCU push/pull buttons → the engagement/mode state they drive.
            ["A32NX.FCU_AP_1_PUSH"] = "A32NX_AUTOPILOT_1_ACTIVE",
            ["A32NX.FCU_AP_2_PUSH"] = "A32NX_AUTOPILOT_2_ACTIVE",
            ["A32NX.FCU_ATHR_PUSH"] = "A32NX_AUTOTHRUST_STATUS",
            ["A32NX.FCU_LOC_PUSH"] = "A32NX_FCU_LOC_MODE_ACTIVE",
            ["A32NX.FCU_APPR_PUSH"] = "A32NX_FCU_APPR_MODE_ACTIVE",
            ["A32NX.FCU_EXPED_PUSH"] = "A32NX_FMA_EXPEDITE_MODE",
            ["A32NX.FCU_TRK_FPA_TOGGLE_PUSH"] = "A32NX_TRK_FPA_MODE_ACTIVE"
        };
    }

    // Input-mode FCU push/pull + AP chords → the A380 FCU events (HDG/VS pull use
    // the _TO_AP_ variants). The base HandleHotkeyAction consults this map for any
    // action our switch doesn't handle.
    protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
    {
        return new Dictionary<HotkeyAction, string>
        {
            // FCU knob push/pull are handled in HandleHotkeyAction (event + spoken
            // readback), so they are intentionally NOT mapped here — a map entry
            // would also fire a redundant post-action state announcement.
            [HotkeyAction.ToggleAutopilot1] = "A32NX.FCU_AP_1_PUSH",
            [HotkeyAction.ToggleAutopilot2] = "A32NX.FCU_AP_2_PUSH",
            [HotkeyAction.ToggleApproachMode] = "A32NX.FCU_APPR_PUSH",
            // Phase 4 parity with the A320: A/THR (Ctrl+J) + LOC (Ctrl+L). The A380 WASM
            // handles the same FBW input events (SimConnectInterface.cpp).
            [HotkeyAction.ToggleAutothrust] = "A32NX.FCU_ATHR_PUSH",
            [HotkeyAction.ToggleLocalizer] = "A32NX.FCU_LOC_PUSH",
        };
    }
}
