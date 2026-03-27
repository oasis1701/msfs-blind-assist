using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Aircraft definition scaffold for the PMDG 777.
/// Panel structure and event ID dictionary are defined here.
/// Variables and panel controls will be populated in subsequent tasks.
/// </summary>
public class PMDG777Definition : BaseAircraftDefinition
{
    public override string AircraftName => "PMDG 777";
    public override string AircraftCode => "PMDG_777";

    // PMDG 777 MCP uses increment/decrement selectors for speed/heading/altitude/VS
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    // =========================================================================
    // Panel Structure
    // =========================================================================

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Overhead"] = new List<string>
            {
                "Electrical", "Hydraulic", "Fuel", "Engines", "Bleed Air",
                "Air Conditioning", "Pressurization", "Anti-Ice", "Fire",
                "Lights", "Signs", "Wipers", "Panel Lighting", "Cargo Temperature"
            },
            ["Overhead Maintenance"] = new List<string>
            {
                "Flight Controls", "Backup Systems", "EEC/APU Maintenance"
            },
            ["Glareshield"] = new List<string>
            {
                "EFIS Captain", "EFIS First Officer", "Mode Control Panel", "Display Select Panel"
            },
            ["Forward Panel"] = new List<string>
            {
                "Landing Gear", "Brakes", "GPWS", "Instruments", "Chronometers"
            },
            ["Pedestal"] = new List<string>
            {
                "Control Stand", "Transponder/TCAS", "Weather Radar",
                "Communication", "CDU", "Warning"
            }
        };
    }

    // =========================================================================
    // Variables — scaffold (populated in Tasks 6-8)
    // =========================================================================

    public override Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
    {
        var variables = GetBaseVariables();
        var pmdgVars = GetPMDGVariables();
        foreach (var kvp in pmdgVars)
            variables[kvp.Key] = kvp.Value;
        return variables;
    }

    private Dictionary<string, SimConnect.SimVarDefinition> GetPMDGVariables()
    {
        return new Dictionary<string, SimConnect.SimVarDefinition>
        {
            // Will be populated in Tasks 6-8
        };
    }

    // =========================================================================
    // Panel Controls — scaffold (populated in Tasks 6-8)
    // =========================================================================

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        return new Dictionary<string, List<string>>
        {
            // Will be populated in Tasks 6-8
        };
    }

    // =========================================================================
    // Optional overrides — stubs
    // =========================================================================

    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new();
    public override Dictionary<string, string> GetButtonStateMapping() => new();

    // =========================================================================
    // Event handling overrides — scaffold (populated in Tasks 9-11)
    // =========================================================================

    public override bool HandleUIVariableSet(
        string varKey, double value,
        SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer) => false;

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    public override bool HandleHotkeyAction(
        HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm,
        HotkeyManager hotkeyManager) => false;

    // =========================================================================
    // Static PMDG 777 Event ID Dictionary
    // Source: pmdg_777.json event catalog (event_base = 69632)
    // =========================================================================

    /// <summary>
    /// Maps PMDG 777 event names to their numeric event IDs.
    /// Used when sending events via the PMDG SDK control area.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> EventIds =
        new Dictionary<string, int>
        {
            // -----------------------------------------------------------------
            // OVERHEAD — Electrical
            // -----------------------------------------------------------------
            ["EVT_OH_ELEC_BATTERY_SWITCH"]        = 69633,
            ["EVT_OH_ELEC_APU_GEN_SWITCH"]        = 69634,
            ["EVT_OH_ELEC_APU_SEL_SWITCH"]        = 69635,
            ["EVT_OH_ELEC_BUS_TIE1_SWITCH"]       = 69637,
            ["EVT_OH_ELEC_BUS_TIE2_SWITCH"]       = 69638,
            ["EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH"]   = 69640,
            ["EVT_OH_ELEC_GRD_PWR_SEC_SWITCH"]    = 69639,
            ["EVT_OH_ELEC_GEN1_SWITCH"]            = 69641,
            ["EVT_OH_ELEC_GEN2_SWITCH"]            = 69642,
            ["EVT_OH_ELEC_BACKUP_GEN1_SWITCH"]     = 69643,
            ["EVT_OH_ELEC_BACKUP_GEN2_SWITCH"]     = 69644,
            ["EVT_OH_ELEC_DISCONNECT1_SWITCH"]     = 69645,
            ["EVT_OH_ELEC_DISCONNECT1_GUARD"]      = 69646,
            ["EVT_OH_ELEC_DISCONNECT2_SWITCH"]     = 69647,
            ["EVT_OH_ELEC_DISCONNECT2_GUARD"]      = 69648,
            ["EVT_OH_ELEC_IFE"]                    = 69649,
            ["EVT_OH_ELEC_CAB_UTIL"]               = 69650,
            ["EVT_OH_ELEC_STBY_PWR_SWITCH"]        = 69713,
            ["EVT_OH_ELEC_STBY_PWR_GUARD"]         = 69714,
            ["EVT_OH_ELEC_GND_TEST_SWITCH"]        = 69784,
            ["EVT_OH_ELEC_GND_TEST_GUARD"]         = 69785,
            ["EVT_OH_ELEC_TOWING_PWR_SWITCH"]      = 69782,
            ["EVT_OH_ELEC_TOWING_PWR_GUARD"]       = 69783,

            // -----------------------------------------------------------------
            // OVERHEAD — Hydraulic
            // -----------------------------------------------------------------
            ["EVT_OH_HYD_DEMAND_ELEC1"]            = 69667,
            ["EVT_OH_HYD_AIR1"]                    = 69668,
            ["EVT_OH_HYD_AIR2"]                    = 69669,
            ["EVT_OH_HYD_DEMAND_ELEC2"]            = 69670,
            ["EVT_OH_HYD_ENG1"]                    = 69671,
            ["EVT_OH_HYD_ELEC1"]                   = 69672,
            ["EVT_OH_HYD_ELEC2"]                   = 69673,
            ["EVT_OH_HYD_ENG2"]                    = 69674,
            ["EVT_OH_HYD_RAM_AIR"]                 = 69675,
            ["EVT_OH_HYD_RAM_AIR_COVER"]           = 69676,
            ["EVT_OH_HYD_VLV_PWR_WING_L"]          = 69692,
            ["EVT_OH_HYD_VLV_PWR_WING_L_GUARD"]    = 69693,
            ["EVT_OH_HYD_VLV_PWR_WING_C"]          = 69695,
            ["EVT_OH_HYD_VLV_PWR_WING_C_GUARD"]    = 69696,
            ["EVT_OH_HYD_VLV_PWR_WING_R"]          = 69698,
            ["EVT_OH_HYD_VLV_PWR_WING_R_GUARD"]    = 69699,
            ["EVT_OH_HYD_VLV_PWR_TAIL_L"]          = 69701,
            ["EVT_OH_HYD_VLV_PWR_TAIL_L_GUARD"]    = 69702,
            ["EVT_OH_HYD_VLV_PWR_TAIL_C"]          = 69703,
            ["EVT_OH_HYD_VLV_PWR_TAIL_C_GUARD"]    = 69704,
            ["EVT_OH_HYD_VLV_PWR_TAIL_R"]          = 69706,
            ["EVT_OH_HYD_VLV_PWR_TAIL_R_GUARD"]    = 69707,

            // -----------------------------------------------------------------
            // OVERHEAD — Fuel
            // -----------------------------------------------------------------
            ["EVT_OH_FUEL_JETTISON_NOZZLE_L"]      = 69729,
            ["EVT_OH_FUEL_JETTISON_NOZZLE_L_GUARD"] = 69730,
            ["EVT_OH_FUEL_JETTISON_NOZZLE_R"]      = 69731,
            ["EVT_OH_FUEL_JETTISON_NOZZLE_R_GUARD"] = 69732,
            ["EVT_OH_FUEL_TO_REMAIN_ROTATE"]       = 69733,
            ["EVT_OH_FUEL_TO_REMAIN_PULL"]         = 70643,
            ["EVT_OH_FUEL_JETTISON_ARM"]           = 69734,
            ["EVT_OH_FUEL_PUMP_1_FORWARD"]         = 69735,
            ["EVT_OH_FUEL_PUMP_2_FORWARD"]         = 69736,
            ["EVT_OH_FUEL_PUMP_1_AFT"]             = 69737,
            ["EVT_OH_FUEL_PUMP_2_AFT"]             = 69738,
            ["EVT_OH_FUEL_CROSSFEED_FORWARD"]      = 69739,
            ["EVT_OH_FUEL_CROSSFEED_AFT"]          = 69740,
            ["EVT_OH_FUEL_PUMP_L_CENTER"]          = 69741,
            ["EVT_OH_FUEL_PUMP_R_CENTER"]          = 69742,
            ["EVT_OH_FUEL_PUMP_AUX"]               = 70669,

            // -----------------------------------------------------------------
            // OVERHEAD — Engines
            // -----------------------------------------------------------------
            ["EVT_OH_EEC_L_SWITCH"]                = 69722,
            ["EVT_OH_EEC_L_GUARD"]                 = 69723,
            ["EVT_OH_EEC_R_SWITCH"]                = 69724,
            ["EVT_OH_EEC_R_GUARD"]                 = 69725,
            ["EVT_OH_ENGINE_L_START"]              = 69726,
            ["EVT_OH_ENGINE_R_START"]              = 69727,
            ["EVT_OH_ENGINE_AUTOSTART"]            = 69728,
            ["EVT_OH_EEC_TEST_L_SWITCH"]           = 69793,
            ["EVT_OH_EEC_TEST_L_SWITCH_GUARD"]     = 69794,
            ["EVT_OH_EEC_TEST_R_SWITCH"]           = 69795,
            ["EVT_OH_EEC_TEST_R_SWITCH_GUARD"]     = 69796,

            // -----------------------------------------------------------------
            // OVERHEAD — Bleed Air
            // -----------------------------------------------------------------
            ["EVT_OH_BLEED_ENG_1_SWITCH"]          = 69761,
            ["EVT_OH_BLEED_ENG_2_SWITCH"]          = 69762,
            ["EVT_OH_BLEED_APU_SWITCH"]            = 69763,
            ["EVT_OH_BLEED_ISOLATION_VALVE_SWITCH_L"] = 69764,
            ["EVT_OH_BLEED_ISOLATION_VALVE_SWITCH_C"] = 69765,
            ["EVT_OH_BLEED_ISOLATION_VALVE_SWITCH_R"] = 69766,

            // -----------------------------------------------------------------
            // OVERHEAD — Air Conditioning
            // -----------------------------------------------------------------
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_FLT_DECK"]      = 69771,
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_CABIN"]         = 69772,
            ["EVT_OH_AIRCOND_RESET_SWITCH"]                = 69773,
            ["EVT_OH_AIRCOND_RECIRC_FAN_UPP_SWITCH"]       = 69774,
            ["EVT_OH_AIRCOND_RECIRC_FAN_LWR_SWITCH"]       = 69775,
            ["EVT_OH_AIRCOND_EQUIP_COOLING_SWITCH"]        = 69776,
            ["EVT_OH_AIRCOND_GASPER_SWITCH"]               = 69777,
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_CARGO_AFT"]     = 69780,
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_CARGO_BULK"]    = 69781,
            ["EVT_OH_AIRCOND_PACK_SWITCH_L"]               = 69767,
            ["EVT_OH_AIRCOND_PACK_SWITCH_R"]               = 69768,
            ["EVT_OH_AIRCOND_TRIM_AIR_SWITCH_L"]           = 69769,
            ["EVT_OH_AIRCOND_TRIM_AIR_SWITCH_R"]           = 69770,
            ["EVT_OH_AIRCOND_RECIRC_FANS_SWITCH"]          = 70684,
            ["EVT_OH_AIRCOND_MAIN_DECK_FLOW_SWITCH"]       = 70685,
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_LWR_CARGO_FWD"] = 70682,
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_LWR_CARGO_AFT"] = 70683,
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_MAIN_CARGO_FWD"] = 70686,
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_MAIN_CARGO_AFT"] = 70687,
            ["EVT_OH_AIRCOND_ALT_VENT_SWITCH"]             = 70689,
            ["EVT_OH_AIRCOND_ALT_VENT_GUARD"]              = 70743,

            // -----------------------------------------------------------------
            // OVERHEAD — Pressurization
            // -----------------------------------------------------------------
            ["EVT_OH_PRESS_VALVE_SWITCH_MANUAL_1"] = 69756,
            ["EVT_OH_PRESS_VALVE_SWITCH_MANUAL_2"] = 69757,
            ["EVT_OH_PRESS_LAND_ALT_KNOB_ROTATE"]  = 69758,
            ["EVT_OH_PRESS_LAND_ALT_KNOB_PULL"]    = 70893,
            ["EVT_OH_PRESS_VALVE_SWITCH_1"]        = 69759,
            ["EVT_OH_PRESS_VALVE_SWITCH_2"]        = 69760,
            ["EVT_OH_PRESS_VALVE_SWITCHES_MANUAL"] = 70883,

            // -----------------------------------------------------------------
            // OVERHEAD — Anti-Ice
            // -----------------------------------------------------------------
            ["EVT_OH_ICE_WING_ANTIICE"]            = 69743,
            ["EVT_OH_ICE_ENGINE_ANTIICE_1"]        = 69744,
            ["EVT_OH_ICE_ENGINE_ANTIICE_2"]        = 69745,
            ["EVT_OH_ICE_WINDOW_HEAT_1"]           = 69677,
            ["EVT_OH_ICE_WINDOW_HEAT_2"]           = 69678,
            ["EVT_OH_ICE_WINDOW_HEAT_3"]           = 69679,
            ["EVT_OH_ICE_WINDOW_HEAT_4"]           = 69680,
            ["EVT_OH_ICE_BU_WINDOW_HEAT_L"]        = 69709,
            ["EVT_OH_ICE_BU_WINDOW_HEAT_L_GUARD"]  = 69710,
            ["EVT_OH_ICE_BU_WINDOW_HEAT_R"]        = 69711,
            ["EVT_OH_ICE_BU_WINDOW_HEAT_R_GUARD"]  = 69712,

            // -----------------------------------------------------------------
            // OVERHEAD — Fire
            // -----------------------------------------------------------------
            ["EVT_OH_FIRE_CARGO_ARM_FWD"]          = 69717,
            ["EVT_OH_FIRE_CARGO_ARM_AFT"]          = 69718,
            ["EVT_OH_FIRE_CARGO_ARM_MAIN_DECK"]    = 70706,
            ["EVT_OH_FIRE_CARGO_DISCH"]            = 69719,
            ["EVT_OH_FIRE_CARGO_DISCH_GUARD"]      = 69720,
            ["EVT_OH_FIRE_CARGO_DISCH_DEPR"]       = 70707,
            ["EVT_OH_FIRE_OVHT_TEST"]              = 69721,
            ["EVT_OH_FIRE_HANDLE_APU_TOP"]         = 69716,
            ["EVT_OH_FIRE_HANDLE_APU_BOTTOM"]      = 78033,
            ["EVT_OH_FIRE_UNLOCK_SWITCH_APU"]      = 78034,
            ["EVT_FIRE_HANDLE_ENGINE_1_TOP"]       = 70283,
            ["EVT_FIRE_HANDLE_ENGINE_1_BOTTOM"]    = 76143,
            ["EVT_FIRE_UNLOCK_SWITCH_ENGINE_1"]    = 76144,
            ["EVT_FIRE_HANDLE_ENGINE_2_TOP"]       = 70284,
            ["EVT_FIRE_HANDLE_ENGINE_2_BOTTOM"]    = 76153,
            ["EVT_FIRE_UNLOCK_SWITCH_ENGINE_2"]    = 76154,

            // -----------------------------------------------------------------
            // OVERHEAD — Lights
            // -----------------------------------------------------------------
            ["EVT_OH_LIGHTS_NAV"]                  = 69747,
            ["EVT_OH_LIGHTS_BEACON"]               = 69746,
            ["EVT_OH_LIGHTS_STROBE"]               = 69754,
            ["EVT_OH_LIGHTS_LOGO"]                 = 69748,
            ["EVT_OH_LIGHTS_WING"]                 = 69749,
            ["EVT_OH_LIGHTS_IND_LTS_SWITCH"]       = 69750,
            ["EVT_OH_LIGHTS_L_TURNOFF"]            = 69751,
            ["EVT_OH_LIGHTS_R_TURNOFF"]            = 69752,
            ["EVT_OH_LIGHTS_LR_TURNOFF"]           = 70833,
            ["EVT_OH_LIGHTS_TAXI"]                 = 69753,
            ["EVT_OH_LIGHTS_LANDING_L"]            = 69654,
            ["EVT_OH_LIGHTS_LANDING_R"]            = 69656,
            ["EVT_OH_LIGHTS_LANDING_NOSE"]         = 69655,
            ["EVT_OH_LIGHTS_LANDING_LNR"]          = 71973,
            ["EVT_OH_LIGHTS_STORM"]                = 69659,
            ["EVT_OH_CAMERA_LTS_SWITCH"]           = 69651,

            // -----------------------------------------------------------------
            // OVERHEAD — Signs
            // -----------------------------------------------------------------
            ["EVT_OH_FASTEN_BELTS_LIGHT_SWITCH"]   = 69662,
            ["EVT_OH_NO_SMOKING_LIGHT_SWITCH"]     = 69661,
            ["EVT_OH_EMER_EXIT_LIGHT_SWITCH"]      = 69681,
            ["EVT_OH_EMER_EXIT_LIGHT_GUARD"]       = 69682,

            // -----------------------------------------------------------------
            // OVERHEAD — Wipers
            // -----------------------------------------------------------------
            ["EVT_OH_WIPER_LEFT_SWITCH"]           = 69652,
            ["EVT_OH_WIPER_RIGHT_SWITCH"]          = 69755,

            // -----------------------------------------------------------------
            // OVERHEAD — Panel Lighting
            // -----------------------------------------------------------------
            ["EVT_OH_PANEL_LIGHT_CONTROL"]         = 69657,
            ["EVT_OH_DOME_SWITCH"]                 = 69658,
            ["EVT_OH_MASTER_BRIGHT_ROTATE"]        = 69660,
            ["EVT_OH_MASTER_BRIGHT_PUSH"]          = 72433,
            ["EVT_OH_GS_PANEL_LIGHT_CONTROL"]      = 69653,
            ["EVT_OH_GS_FLOOD_LIGHT_CONTROL"]      = 71733,
            ["EVT_OH_CB_LIGHT_CONTROL"]            = 72133,

            // -----------------------------------------------------------------
            // OVERHEAD — Miscellaneous (maintenance-area panels)
            // -----------------------------------------------------------------
            ["EVT_OH_ADIRU_SWITCH"]                = 69691,
            ["EVT_OH_THRUST_ASYM_COMP"]            = 69686,
            ["EVT_OH_PRIM_FLT_COMPUTERS"]          = 69687,
            ["EVT_OH_PRIM_FLT_COMPUTERS_GUARD"]    = 69688,
            ["EVT_OH_SERVICE_INTERPHONE_SWITCH"]   = 69683,
            ["EVT_OH_OXY_PASS_SWITCH"]             = 69684,
            ["EVT_OH_OXY_PASS_GUARD"]              = 69685,
            ["EVT_OH_OXY_SUPRNMRY_SWITCH"]         = 70708,
            ["EVT_OH_OXY_SUPRNMRY_GUARD"]          = 70709,
            ["EVT_OH_APU_TEST_SWITCH"]             = 69791,
            ["EVT_OH_APU_TEST_SWITCH_GUARD"]       = 69792,
            ["EVT_OH_CVR_TEST"]                    = 69788,
            ["EVT_OH_CVR_ERASE"]                   = 69789,

            // -----------------------------------------------------------------
            // GLARESHIELD — MCP (Mode Control Panel)
            // -----------------------------------------------------------------
            ["EVT_MCP_FD_SWITCH_L"]                = 69834,
            ["EVT_MCP_FD_SWITCH_R"]                = 69862,
            ["EVT_MCP_AT_ARM_SWITCH_L"]            = 69836,
            ["EVT_MCP_AT_ARM_SWITCH_R"]            = 69837,
            ["EVT_MCP_AT_SWITCH"]                  = 69839,
            ["EVT_MCP_CLB_CON_SWITCH"]             = 69838,
            ["EVT_MCP_LNAV_SWITCH"]                = 69843,
            ["EVT_MCP_VNAV_SWITCH"]                = 69844,
            ["EVT_MCP_LVL_CHG_SWITCH"]             = 69845,
            ["EVT_MCP_HDG_HOLD_SWITCH"]            = 69851,
            ["EVT_MCP_VS_FPA_SWITCH"]              = 69852,
            ["EVT_MCP_ALT_HOLD_SWITCH"]            = 69858,
            ["EVT_MCP_LOC_SWITCH"]                 = 69859,
            ["EVT_MCP_APP_SWITCH"]                 = 69860,
            ["EVT_MCP_AP_L_SWITCH"]                = 69835,
            ["EVT_MCP_AP_R_SWITCH"]                = 69861,
            ["EVT_MCP_IAS_SET"]                    = 84134,
            ["EVT_MCP_MACH_SET"]                   = 84135,
            ["EVT_MCP_HDGTRK_SET"]                 = 84136,
            ["EVT_MCP_ALT_SET"]                    = 84137,
            ["EVT_MCP_VS_SET"]                     = 84138,
            ["EVT_MCP_FPA_SET"]                    = 84139,
            ["EVT_MCP_SPEED_PUSH_SWITCH"]          = 71732,
            ["EVT_MCP_HEADING_PUSH_SWITCH"]        = 69850,
            ["EVT_MCP_ALTITUDE_PUSH_SWITCH"]       = 71883,
            ["EVT_MCP_VS_SWITCH"]                  = 69855,
            ["EVT_MCP_IAS_MACH_SWITCH"]            = 69840,
            ["EVT_MCP_HDG_TRK_SWITCH"]             = 69848,
            ["EVT_MCP_BANK_ANGLE_SELECTOR"]        = 71813,
            ["EVT_MCP_ALT_INCR_SELECTOR"]          = 69857,
            ["EVT_MCP_DISENGAGE_BAR"]              = 69846,
            ["EVT_MCP_SPEED_SELECTOR"]             = 69842,
            ["EVT_MCP_HEADING_SELECTOR"]           = 71812,
            ["EVT_MCP_ALTITUDE_SELECTOR"]          = 71882,
            ["EVT_MCP_VS_SELECTOR"]                = 69854,
            ["EVT_MCP_TOGA_SCREW_L"]               = 74633,
            ["EVT_MCP_TOGA_SCREW_R"]               = 74634,

            // -----------------------------------------------------------------
            // GLARESHIELD — EFIS Captain
            // -----------------------------------------------------------------
            ["EVT_EFIS_CPT_MINIMUMS_RADIO_BARO"]   = 69813,
            ["EVT_EFIS_CPT_MINIMUMS"]              = 69814,
            ["EVT_EFIS_CPT_MINIMUMS_RST"]          = 69815,
            ["EVT_EFIS_CPT_VOR_ADF_SELECTOR_L"]    = 69816,
            ["EVT_EFIS_CPT_MODE"]                  = 69817,
            ["EVT_EFIS_CPT_MODE_CTR"]              = 69818,
            ["EVT_EFIS_CPT_RANGE"]                 = 69819,
            ["EVT_EFIS_CPT_RANGE_TFC"]             = 69820,
            ["EVT_EFIS_CPT_VOR_ADF_SELECTOR_R"]    = 69821,
            ["EVT_EFIS_CPT_BARO_IN_HPA"]           = 69822,
            ["EVT_EFIS_CPT_BARO"]                  = 69823,
            ["EVT_EFIS_CPT_BARO_STD"]              = 69824,
            ["EVT_EFIS_CPT_FPV"]                   = 69825,
            ["EVT_EFIS_CPT_MTRS"]                  = 69826,
            ["EVT_EFIS_CPT_WXR"]                   = 69827,
            ["EVT_EFIS_CPT_STA"]                   = 69828,
            ["EVT_EFIS_CPT_WPT"]                   = 69829,
            ["EVT_EFIS_CPT_ARPT"]                  = 69830,
            ["EVT_EFIS_CPT_DATA"]                  = 69831,
            ["EVT_EFIS_CPT_POS"]                   = 69832,
            ["EVT_EFIS_CPT_TERR"]                  = 69833,

            // -----------------------------------------------------------------
            // GLARESHIELD — EFIS First Officer
            // -----------------------------------------------------------------
            ["EVT_EFIS_FO_MINIMUMS_RADIO_BARO"]    = 69880,
            ["EVT_EFIS_FO_MINIMUMS"]               = 69881,
            ["EVT_EFIS_FO_MINIMUMS_RST"]           = 69882,
            ["EVT_EFIS_FO_VOR_ADF_SELECTOR_L"]     = 69883,
            ["EVT_EFIS_FO_MODE"]                   = 69884,
            ["EVT_EFIS_FO_MODE_CTR"]               = 69885,
            ["EVT_EFIS_FO_RANGE"]                  = 69886,
            ["EVT_EFIS_FO_RANGE_TFC"]              = 69887,
            ["EVT_EFIS_FO_VOR_ADF_SELECTOR_R"]     = 69888,
            ["EVT_EFIS_FO_BARO_IN_HPA"]            = 69889,
            ["EVT_EFIS_FO_BARO"]                   = 69890,
            ["EVT_EFIS_FO_BARO_STD"]               = 69891,
            ["EVT_EFIS_FO_FPV"]                    = 69892,
            ["EVT_EFIS_FO_MTRS"]                   = 69893,
            ["EVT_EFIS_FO_WXR"]                    = 69894,
            ["EVT_EFIS_FO_STA"]                    = 69895,
            ["EVT_EFIS_FO_WPT"]                    = 69896,
            ["EVT_EFIS_FO_ARPT"]                   = 69897,
            ["EVT_EFIS_FO_POS"]                    = 69899,
            ["EVT_EFIS_FO_TERR"]                   = 69900,
            ["EVT_EFIS_FO_DATA"]                   = 72293,
            ["EVT_EFIS_HDG_REF_SWITCH"]            = 69945,
            ["EVT_EFIS_HDG_REF_GUARD"]             = 69946,

            // -----------------------------------------------------------------
            // GLARESHIELD — Display Select Panel (DSP)
            // -----------------------------------------------------------------
            ["EVT_DSP_L_INBD_SWITCH"]              = 69863,
            ["EVT_DSP_R_INBD_SWITCH"]              = 69864,
            ["EVT_DSP_LWR_CTR_SWITCH"]             = 69865,
            ["EVT_DSP_ENG_SWITCH"]                 = 69866,
            ["EVT_DSP_STAT_SWITCH"]                = 69867,
            ["EVT_DSP_ELEC_SWITCH"]                = 69868,
            ["EVT_DSP_HYD_SWITCH"]                 = 69869,
            ["EVT_DSP_FUEL_SWITCH"]                = 69870,
            ["EVT_DSP_AIR_SWITCH"]                 = 69871,
            ["EVT_DSP_DOOR_SWITCH"]                = 69872,
            ["EVT_DSP_GEAR_SWITCH"]                = 69873,
            ["EVT_DSP_FCTL_SWITCH"]                = 69874,
            ["EVT_DSP_CAM_SWITCH"]                 = 69875,
            ["EVT_DSP_CHKL_SWITCH"]                = 69876,
            ["EVT_DSP_COMM_SWITCH"]                = 69877,
            ["EVT_DSP_NAV_SWITCH"]                 = 69878,
            ["EVT_DSP_CANC_RCL_SWITCH"]            = 69879,
            ["EVT_DSP_INDB_DSPL_L"]                = 69947,
            ["EVT_DSP_INDB_DSPL_R"]                = 69922,

            // -----------------------------------------------------------------
            // FORWARD PANEL — Landing Gear
            // -----------------------------------------------------------------
            ["EVT_GEAR_LEVER"]                     = 69927,
            ["EVT_GEAR_LEVER_UNLOCK"]              = 69928,
            ["EVT_GEAR_ALTN_GEAR_DOWN"]            = 69925,
            ["EVT_GEAR_ALTN_GEAR_DOWN_GUARD"]      = 69926,

            // -----------------------------------------------------------------
            // FORWARD PANEL — Brakes / Autobrake
            // -----------------------------------------------------------------
            ["EVT_ABS_AUTOBRAKE_SELECTOR"]         = 69924,

            // -----------------------------------------------------------------
            // FORWARD PANEL — GPWS
            // -----------------------------------------------------------------
            ["EVT_GPWS_TERR_OVRD_SWITCH"]          = 69929,
            ["EVT_GPWS_TERR_OVRD_GUARD"]           = 69930,
            ["EVT_GPWS_GEAR_OVRD_SWITCH"]          = 69931,
            ["EVT_GPWS_GEAR_OVRD_GUARD"]           = 69932,
            ["EVT_GPWS_FLAP_OVRD_SWITCH"]          = 69933,
            ["EVT_GPWS_FLAP_OVRD_GUARD"]           = 69934,
            ["EVT_GPWS_GS_INHIBIT_SWITCH"]         = 69935,
            ["EVT_GPWS_RWY_OVRD_SWITCH"]           = 70741,
            ["EVT_GPWS_RWY_OVRD_GUARD"]            = 70742,

            // -----------------------------------------------------------------
            // FORWARD PANEL — Instruments (ISFD)
            // -----------------------------------------------------------------
            ["EVT_ISFD_APP"]                       = 70442,
            ["EVT_ISFD_HP_IN"]                     = 70443,
            ["EVT_ISFD_PLUS"]                      = 70444,
            ["EVT_ISFD_MINUS"]                     = 70445,
            ["EVT_ISFD_ATT_RST"]                   = 70446,
            ["EVT_ISFD_BARO"]                      = 70447,
            ["EVT_ISFD_BARO_PUSH"]                 = 70448,

            // -----------------------------------------------------------------
            // PEDESTAL — Control Stand
            // -----------------------------------------------------------------
            ["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER"]        = 70130,
            ["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN"]   = 74613,
            ["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM"]    = 74614,
            ["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_UP"]     = 74615,
            ["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_50"]     = 74616,
            ["EVT_CONTROL_STAND_REV_THRUST1_LEVER"]        = 70131,
            ["EVT_CONTROL_STAND_TOGA1_SWITCH"]             = 70132,
            ["EVT_CONTROL_STAND_FWD_THRUST1_LEVER"]        = 70133,
            ["EVT_CONTROL_STAND_AT1_DISENGAGE_SWITCH"]     = 70134,
            ["EVT_CONTROL_STAND_REV_THRUST2_LEVER"]        = 70135,
            ["EVT_CONTROL_STAND_TOGA2_SWITCH"]             = 70136,
            ["EVT_CONTROL_STAND_FWD_THRUST2_LEVER"]        = 70137,
            ["EVT_CONTROL_STAND_AT2_DISENGAGE_SWITCH"]     = 70138,
            ["EVT_CONTROL_STAND_FLAPS_LEVER"]              = 70139,
            ["EVT_CONTROL_STAND_FLAPS_LEVER_0"]            = 74703,
            ["EVT_CONTROL_STAND_FLAPS_LEVER_1"]            = 74704,
            ["EVT_CONTROL_STAND_FLAPS_LEVER_5"]            = 74705,
            ["EVT_CONTROL_STAND_FLAPS_LEVER_15"]           = 74706,
            ["EVT_CONTROL_STAND_FLAPS_LEVER_20"]           = 74707,
            ["EVT_CONTROL_STAND_FLAPS_LEVER_25"]           = 74708,
            ["EVT_CONTROL_STAND_FLAPS_LEVER_30"]           = 74709,
            ["EVT_CONTROL_STAND_ALT_PITCH_TRIM_LEVER"]     = 70128,
            ["EVT_CONTROL_STAND_PARK_BRAKE_LEVER"]         = 70147,
            ["EVT_CONTROL_STAND_STABCUTOUT_SWITCH_C"]      = 70149,
            ["EVT_CONTROL_STAND_STABCUTOUT_SWITCH_C_GUARD"] = 70148,
            ["EVT_CONTROL_STAND_STABCUTOUT_SWITCH_R"]      = 70151,
            ["EVT_CONTROL_STAND_STABCUTOUT_SWITCH_R_GUARD"] = 70150,
            ["EVT_CONTROL_STAND_ENG1_START_LEVER"]         = 70152,
            ["EVT_CONTROL_STAND_ENG2_START_LEVER"]         = 70153,

            // -----------------------------------------------------------------
            // PEDESTAL — Alternate Flaps
            // -----------------------------------------------------------------
            ["EVT_ALTN_FLAPS_ARM"]                 = 70142,
            ["EVT_ALTN_FLAPS_ARM_GUARD"]           = 70143,
            ["EVT_ALTN_FLAPS_POS"]                 = 70144,

            // -----------------------------------------------------------------
            // PEDESTAL — CDU Left
            // -----------------------------------------------------------------
            ["EVT_CDU_L_L1"]                       = 69960,
            ["EVT_CDU_L_L2"]                       = 69961,
            ["EVT_CDU_L_L3"]                       = 69962,
            ["EVT_CDU_L_L4"]                       = 69963,
            ["EVT_CDU_L_L5"]                       = 69964,
            ["EVT_CDU_L_L6"]                       = 69965,
            ["EVT_CDU_L_R1"]                       = 69966,
            ["EVT_CDU_L_R2"]                       = 69967,
            ["EVT_CDU_L_R3"]                       = 69968,
            ["EVT_CDU_L_R4"]                       = 69969,
            ["EVT_CDU_L_R5"]                       = 69970,
            ["EVT_CDU_L_R6"]                       = 69971,
            ["EVT_CDU_L_INIT_REF"]                 = 69972,
            ["EVT_CDU_L_RTE"]                      = 69973,
            ["EVT_CDU_L_DEP_ARR"]                  = 69974,
            ["EVT_CDU_L_ALTN"]                     = 69975,
            ["EVT_CDU_L_VNAV"]                     = 69976,
            ["EVT_CDU_L_FIX"]                      = 69977,
            ["EVT_CDU_L_LEGS"]                     = 69978,
            ["EVT_CDU_L_HOLD"]                     = 69979,
            ["EVT_CDU_L_PROG"]                     = 69980,
            ["EVT_CDU_L_EXEC"]                     = 69981,
            ["EVT_CDU_L_MENU"]                     = 69982,
            ["EVT_CDU_L_NAV_RAD"]                  = 69983,
            ["EVT_CDU_L_PREV_PAGE"]                = 69984,
            ["EVT_CDU_L_NEXT_PAGE"]                = 69985,
            ["EVT_CDU_L_1"]                        = 69986,
            ["EVT_CDU_L_2"]                        = 69987,
            ["EVT_CDU_L_3"]                        = 69988,
            ["EVT_CDU_L_4"]                        = 69989,
            ["EVT_CDU_L_5"]                        = 69990,
            ["EVT_CDU_L_6"]                        = 69991,
            ["EVT_CDU_L_7"]                        = 69992,
            ["EVT_CDU_L_8"]                        = 69993,
            ["EVT_CDU_L_9"]                        = 69994,
            ["EVT_CDU_L_DOT"]                      = 69995,
            ["EVT_CDU_L_0"]                        = 69996,
            ["EVT_CDU_L_PLUS_MINUS"]               = 69997,
            ["EVT_CDU_L_A"]                        = 69998,
            ["EVT_CDU_L_B"]                        = 69999,
            ["EVT_CDU_L_C"]                        = 70000,
            ["EVT_CDU_L_D"]                        = 70001,
            ["EVT_CDU_L_E"]                        = 70002,
            ["EVT_CDU_L_F"]                        = 70003,
            ["EVT_CDU_L_G"]                        = 70004,
            ["EVT_CDU_L_H"]                        = 70005,
            ["EVT_CDU_L_I"]                        = 70006,
            ["EVT_CDU_L_J"]                        = 70007,
            ["EVT_CDU_L_K"]                        = 70008,
            ["EVT_CDU_L_L"]                        = 70009,
            ["EVT_CDU_L_M"]                        = 70010,
            ["EVT_CDU_L_N"]                        = 70011,
            ["EVT_CDU_L_O"]                        = 70012,
            ["EVT_CDU_L_P"]                        = 70013,
            ["EVT_CDU_L_Q"]                        = 70014,
            ["EVT_CDU_L_R"]                        = 70015,
            ["EVT_CDU_L_S"]                        = 70016,
            ["EVT_CDU_L_T"]                        = 70017,
            ["EVT_CDU_L_U"]                        = 70018,
            ["EVT_CDU_L_V"]                        = 70019,
            ["EVT_CDU_L_W"]                        = 70020,
            ["EVT_CDU_L_X"]                        = 70021,
            ["EVT_CDU_L_Y"]                        = 70022,
            ["EVT_CDU_L_Z"]                        = 70023,
            ["EVT_CDU_L_SPACE"]                    = 70024,
            ["EVT_CDU_L_DEL"]                      = 70025,
            ["EVT_CDU_L_SLASH"]                    = 70026,
            ["EVT_CDU_L_CLR"]                      = 70027,
            ["EVT_CDU_L_BRITENESS"]                = 70032,
            ["EVT_CDU_L_FMCCOMM"]                  = 73103,

            // -----------------------------------------------------------------
            // PEDESTAL — Misc
            // -----------------------------------------------------------------
            ["EVT_PED_DSPL_CTRL_SOURCE_C"]         = 70110,
            ["EVT_PED_EICAS_EVENT_RCD"]            = 70111,
            ["EVT_PED_UPPER_BRIGHT_CONTROL"]       = 70112,
            ["EVT_PED_LOWER_BRIGHT_CONTROL"]       = 70113,
            ["EVT_PED_LOWER_TERR_BRIGHT_CONTROL"]  = 74443,
            ["EVT_PED_L_CCD_SIDE"]                 = 70114,
            ["EVT_PED_L_CCD_INBD"]                 = 70115,
            ["EVT_PED_L_CCD_LWR"]                  = 70116,
            ["EVT_PED_R_CCD_SIDE"]                 = 70123,
            ["EVT_PED_R_CCD_INBD"]                 = 70122,
            ["EVT_PED_R_CCD_LWR"]                  = 70121,
            ["EVT_PED_OBS_AUDIO_SELECTOR"]         = 70280,
            ["EVT_PED_FLOOR_LIGHTS"]               = 70367,
            ["EVT_PED_PANEL_LIGHT_CONTROL"]        = 70368,
            ["EVT_PED_FLOOD_LIGHT_CONTROL"]        = 70369,
            ["EVT_PED_EVAC_SWITCH"]                = 70371,
            ["EVT_PED_EVAC_SWITCH_GUARD"]          = 70372,
            ["EVT_PED_EVAC_HORN_SHUTOFF"]          = 70373,
            ["EVT_PED_EVAC_TEST_SWITCH"]           = 70374,
            ["EVT_PED_CALL_GND"]                   = 70710,
            ["EVT_PED_CALL_CREW_REST"]             = 70711,
            ["EVT_PED_CALL_SUPRNMRY"]              = 70712,
            ["EVT_PED_CALL_CARGO"]                 = 70713,
            ["EVT_PED_CALL_CARGO_AUDIO"]           = 70714,
            ["EVT_PED_CALL_MAIN_DK_ALERT"]         = 70715,

            // -----------------------------------------------------------------
            // FORWARD PANEL — FMC Selector
            // -----------------------------------------------------------------
            ["EVT_FWD_FMC_SELECTOR"]               = 69923,
        };
}
