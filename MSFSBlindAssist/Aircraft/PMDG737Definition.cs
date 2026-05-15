using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Aircraft definition for the PMDG 737-800 (NG3). Variables, panels, hotkey
/// routing, MCP direct-set dialogs, and announcement logic. Tasks C2–C13 fill
/// in the data dictionaries and behavior methods.
/// </summary>
public class PMDG737Definition : BaseAircraftDefinition, IPMDGAircraft
{
    public override string AircraftName => "PMDG 737-800";
    public override string AircraftCode => "PMDG_737";

    // Cached merged variables dictionary — built once on first access.
    // All callers are read-only so sharing a single instance is safe.
    private Dictionary<string, SimConnect.SimVarDefinition>? _cachedVariables;

    // Cached set of RenderAsButton keys that are NOT annunciators.
    // Used in ProcessSimVarUpdate to suppress raw value announcements
    // without re-allocating GetVariables() on every call.
    private HashSet<string>? _suppressedButtonKeys;

    private HashSet<string> SuppressedButtonKeys =>
        _suppressedButtonKeys ??= BuildSuppressedButtonKeys();

    private HashSet<string> BuildSuppressedButtonKeys()
    {
        var set = new HashSet<string>();
        foreach (var kvp in GetVariables())
        {
            if (kvp.Value.RenderAsButton && !kvp.Value.Name.Contains("_annun"))
                set.Add(kvp.Key);
        }
        return set;
    }

    // PMDG 737 MCP supports direct SetValue events for SPD/HDG/ALT/VS (NG3 SDK
    // exposes EVT_*_SET style events just like the 777).
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
                "ADIRU", "Service Interphone", "Engine (Aft)", "Oxygen", "Flight Recorder",
                "Flight Controls", "NAVDIS", "Fuel", "Electrical", "APU", "Wipers",
                "Center Overhead", "Anti-Ice", "Hydraulics", "Air Systems", "Doors",
                "Bottom Overhead"
            },
            ["Glareshield"] = new List<string>
            {
                "Warnings", "EFIS Captain", "EFIS First Officer", "MCP"
            },
            ["Forward Panel"] = new List<string>
            {
                "Landing Gear", "Autobrake", "Display Select", "GPWS", "Speed Reference",
                "Brightness"
            },
            ["Pedestal"] = new List<string>
            {
                "Control Stand", "Fire Protection", "Cargo Fire", "Transponder",
                "Pedestal Lights", "FltDk Door", "Trim", "Communication"
            },
        };
    }

    // =========================================================================
    // Variables — scaffold (populated in Tasks C5–C8)
    // =========================================================================

    public override Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
    {
        if (_cachedVariables != null)
            return _cachedVariables;

        var variables = GetBaseVariables();
        var pmdgVars = GetPMDGVariables();
        foreach (var kvp in pmdgVars)
            variables[kvp.Key] = kvp.Value;
        _cachedVariables = variables;
        return variables;
    }

    private Dictionary<string, SimConnect.SimVarDefinition> GetPMDGVariables()
    {
        return new Dictionary<string, SimConnect.SimVarDefinition>();
    }

    // =========================================================================
    // Panel Controls — scaffold (populated in Task C9)
    // =========================================================================

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        return new Dictionary<string, List<string>>();
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new();
    public override Dictionary<string, string> GetButtonStateMapping() => new();

    // =========================================================================
    // Event ID dictionary — scaffold (populated in Task C2)
    // PMDG 737 NG3 SDK third-party event range starts at THIRD_PARTY_EVENT_ID_MIN.
    // =========================================================================
    private const int event_base = 0x00011000;  // THIRD_PARTY_EVENT_ID_MIN

    /// <summary>
    /// Maps PMDG 737 event names to their numeric event IDs.
    /// Used when sending events via the PMDG SDK control area.
    /// Source: PMDG_NG3_SDK.h — all <c>#define EVT_*</c> constants in SDK order.
    /// Each entry's offset matches <c>THIRD_PARTY_EVENT_ID_MIN + N</c> from the header.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> EventIds =
        new Dictionary<string, int>
        {
            // ===== Overhead — Electric =====
            { "EVT_OH_ELEC_BATTERY_SWITCH",                  event_base + 1 },
            { "EVT_OH_ELEC_BATTERY_GUARD",                   event_base + 2 },
            { "EVT_OH_ELEC_DC_METER",                        event_base + 3 },
            { "EVT_OH_ELEC_AC_METER",                        event_base + 4 },
            { "EVT_OH_ELEC_GALLEY",                          event_base + 974 },   // -600/700 only
            { "EVT_OH_ELEC_CAB_UTIL",                        event_base + 5 },     // -800/900 only
            { "EVT_OH_ELEC_IFE",                             event_base + 6 },     // -800/900 only
            { "EVT_OH_ELEC_STBY_PWR_SWITCH",                 event_base + 10 },
            { "EVT_OH_ELEC_STBY_PWR_GUARD",                  event_base + 11 },
            { "EVT_OH_ELEC_DISCONNECT_1_SWITCH",             event_base + 12 },
            { "EVT_OH_ELEC_DISCONNECT_1_GUARD",              event_base + 13 },
            { "EVT_OH_ELEC_DISCONNECT_2_SWITCH",             event_base + 14 },
            { "EVT_OH_ELEC_DISCONNECT_2_GUARD",              event_base + 15 },
            { "EVT_OH_ELEC_GRD_PWR_SWITCH",                  event_base + 17 },
            { "EVT_OH_ELEC_BUS_TRANSFER_SWITCH",             event_base + 18 },
            { "EVT_OH_ELEC_BUS_TRANSFER_GUARD",              event_base + 19 },
            { "EVT_OH_ELEC_GEN1_SWITCH",                     event_base + 27 },
            { "EVT_OH_ELEC_APU_GEN1_SWITCH",                 event_base + 28 },
            { "EVT_OH_ELEC_APU_GEN2_SWITCH",                 event_base + 29 },
            { "EVT_OH_ELEC_GEN2_SWITCH",                     event_base + 30 },
            { "EVT_OH_ELEC_MAINT_SWITCH",                    event_base + 93 },

            // ===== Overhead — Fuel =====
            { "EVT_OH_FUEL_PUMP_1_AFT",                      event_base + 37 },
            { "EVT_OH_FUEL_PUMP_1_FORWARD",                  event_base + 38 },
            { "EVT_OH_FUEL_PUMP_2_FORWARD",                  event_base + 39 },
            { "EVT_OH_FUEL_PUMP_2_AFT",                      event_base + 40 },
            { "EVT_OH_FUEL_PUMP_L_CENTER",                   event_base + 45 },
            { "EVT_OH_FUEL_PUMP_R_CENTER",                   event_base + 46 },
            { "EVT_OH_FUEL_CROSSFEED",                       event_base + 49 },
            { "EVT_OH_FUEL_AUX_FWD_A",                       event_base + 2009 },
            { "EVT_OH_FUEL_AUX_FWD_B",                       event_base + 2010 },
            { "EVT_OH_FUEL_AUX_AFT_A",                       event_base + 2011 },
            { "EVT_OH_FUEL_AUX_AFT_B",                       event_base + 2012 },
            { "EVT_OH_FUEL_FWD_BLD",                         event_base + 2013 },
            { "EVT_OH_FUEL_AFT_BLD",                         event_base + 2014 },
            { "EVT_OH_FUEL_GND_XFR_GUARD",                   event_base + 2018 },
            { "EVT_OH_FUEL_GND_XFR_SW",                      event_base + 2019 },

            // ===== Overhead — Lights =====
            { "EVT_OH_LAND_LIGHTS_GUARD",                    event_base + 110 },
            { "EVT_OH_LIGHTS_L_RETRACT",                     event_base + 111 },
            { "EVT_OH_LIGHTS_R_RETRACT",                     event_base + 112 },
            { "EVT_OH_LIGHTS_L_FIXED",                       event_base + 113 },
            { "EVT_OH_LIGHTS_R_FIXED",                       event_base + 114 },
            { "EVT_OH_LIGHTS_L_TURNOFF",                     event_base + 115 },
            { "EVT_OH_LIGHTS_R_TURNOFF",                     event_base + 116 },
            { "EVT_OH_LIGHTS_TAXI",                          event_base + 117 },
            { "EVT_OH_LIGHTS_APU_START",                     event_base + 118 },
            { "EVT_OH_LIGHTS_L_ENGINE_START",                event_base + 119 },
            { "EVT_OH_LIGHTS_IGN_SEL",                       event_base + 120 },
            { "EVT_OH_LIGHTS_R_ENGINE_START",                event_base + 121 },
            { "EVT_OH_LIGHTS_LOGO",                          event_base + 122 },
            { "EVT_OH_LIGHTS_POS_STROBE",                    event_base + 123 },
            { "EVT_OH_LIGHTS_ANT_COL",                       event_base + 124 },
            { "EVT_OH_LIGHTS_WING",                          event_base + 125 },
            { "EVT_OH_LIGHTS_WHEEL_WELL",                    event_base + 126 },
            { "EVT_OH_LIGHTS_L_ENGINE_START_INOUT",          event_base + 127 },
            { "EVT_OH_LIGHTS_R_ENGINE_START_INOUT",          event_base + 128 },
            { "EVT_OH_LIGHTS_COMPASS",                       event_base + 982 },

            // ===== Overhead — Center Part =====
            { "EVT_OH_CB_LIGHT_CONTROL",                     event_base + 94 },
            { "EVT_OH_PANEL_LIGHT_CONTROL",                  event_base + 95 },
            { "EVT_OH_EC_SUPPLY_SWITCH",                     event_base + 96 },
            { "EVT_OH_EC_EXHAUST_SWITCH",                    event_base + 97 },
            { "EVT_OH_EMER_EXIT_LIGHT_SWITCH",               event_base + 100 },
            { "EVT_OH_EMER_EXIT_LIGHT_GUARD",                event_base + 101 },
            { "EVT_OH_NO_SMOKING_LIGHT_SWITCH",              event_base + 103 },
            { "EVT_OH_FASTEN_BELTS_LIGHT_SWITCH",            event_base + 104 },

            // ===== Overhead — Miscellaneous =====
            { "EVT_OH_ATTND_CALL_SWITCH",                    event_base + 105 },
            { "EVT_OH_GRND_CALL_SWITCH",                     event_base + 106 },
            { "EVT_OH_WIPER_LEFT_CONTROL",                   event_base + 36 },
            { "EVT_OH_WIPER_RIGHT_CONTROL",                  event_base + 109 },
            { "EVT_OH_EFIS_HDG_REF_TOGGLE",                  event_base + 6920 },  // BBJ polar nav option

            // ===== Overhead — NAVDSP =====
            { "EVT_OH_NAVDSP_DISPLAYS_SOURCE_SEL",           event_base + 58 },
            { "EVT_OH_NAVDSP_CONTROL_PANEL_SEL",             event_base + 59 },
            { "EVT_OH_NAVDSP_FMC_SEL",                       event_base + 60 },
            { "EVT_OH_NAVDSP_IRS_SEL",                       event_base + 61 },
            { "EVT_OH_NAVDSP_VHF_NAV_SEL",                   event_base + 62 },

            // ===== Overhead — Flight Controls =====
            { "EVT_OH_YAW_DAMPER",                           event_base + 63 },
            { "EVT_OH_ALT_FLAPS_MASTER_SWITCH",              event_base + 73 },
            { "EVT_OH_ALT_FLAPS_MASTER_GUARD",               event_base + 74 },
            { "EVT_OH_SPOILER_A_SWITCH",                     event_base + 65 },
            { "EVT_OH_SPOILER_A_GUARD",                      event_base + 66 },
            { "EVT_OH_SPOILER_B_SWITCH",                     event_base + 67 },
            { "EVT_OH_SPOILER_B_GUARD",                      event_base + 68 },
            { "EVT_OH_ALT_FLAPS_POS_SWITCH",                 event_base + 75 },
            { "EVT_OH_FCTL_A_SWITCH",                        event_base + 78 },
            { "EVT_OH_FCTL_A_GUARD",                         event_base + 79 },
            { "EVT_OH_FCTL_B_SWITCH",                        event_base + 80 },
            { "EVT_OH_FCTL_B_GUARD",                         event_base + 81 },

            // ===== Overhead — CVR =====
            { "EVT_OH_CVR_TEST",                             event_base + 178 },
            { "EVT_OH_CVR_ERASE",                            event_base + 180 },

            // ===== Overhead — Hydraulics =====
            { "EVT_OH_HYD_ENG1",                             event_base + 165 },
            { "EVT_OH_HYD_ELEC2",                            event_base + 167 },
            { "EVT_OH_HYD_ELEC1",                            event_base + 168 },
            { "EVT_OH_HYD_ENG2",                             event_base + 166 },

            // ===== Overhead — Ice =====
            { "EVT_OH_ICE_WINDOW_HEAT_1",                    event_base + 135 },
            { "EVT_OH_ICE_WINDOW_HEAT_2",                    event_base + 136 },
            { "EVT_OH_ICE_WINDOW_HEAT_3",                    event_base + 138 },
            { "EVT_OH_ICE_WINDOW_HEAT_4",                    event_base + 139 },
            { "EVT_OH_ICE_WINDOW_HEAT_TEST",                 event_base + 137 },
            { "EVT_OH_ICE_PROBE_HEAT_1",                     event_base + 140 },
            { "EVT_OH_ICE_PROBE_HEAT_2",                     event_base + 141 },
            { "EVT_OH_ICE_TAT_TEST",                         event_base + 142 },
            { "EVT_OH_ICE_WING_ANTIICE",                     event_base + 156 },
            { "EVT_OH_ICE_ENGINE_ANTIICE_1",                 event_base + 157 },
            { "EVT_OH_ICE_ENGINE_ANTIICE_2",                 event_base + 158 },

            // ===== Overhead — Pneumatics / Air Cond =====
            // --- -600/700 panel only ---
            { "EVT_OH_AIRCOND_TEMP_SOURCE_SELECTOR",         event_base + 187 },
            { "EVT_OH_AIRCOND_TEMP_SELECTOR_CONT",           event_base + 191 },
            { "EVT_OH_AIRCOND_TEMP_SELECTOR_CABIN",          event_base + 192 },
            // --- -800/900 panel only ---
            { "EVT_OH_AIRCOND_TEMP_SOURCE_SELECTOR_800",     event_base + 313 },
            { "EVT_OH_AIRCOND_TEMP_SELECTOR_CONT_800",       event_base + 305 },
            { "EVT_OH_AIRCOND_TEMP_SELECTOR_FWD_800",        event_base + 306 },
            { "EVT_OH_AIRCOND_TEMP_SELECTOR_AFT_800",        event_base + 307 },
            { "EVT_OH_AIRCOND_TRIM_AIR_SWITCH_800",          event_base + 311 },
            // --- Bleed Air ---
            { "EVT_OH_BLEED_RECIRC_FAN_L_SWITCH",            event_base + 872 },
            { "EVT_OH_BLEED_RECIRC_FAN_R_SWITCH",            event_base + 196 },
            { "EVT_OH_BLEED_OVHT_TEST_BUTTON",               event_base + 199 },
            { "EVT_OH_BLEED_PACK_L_SWITCH",                  event_base + 200 },
            { "EVT_OH_BLEED_PACK_R_SWITCH",                  event_base + 201 },
            { "EVT_OH_BLEED_ISOLATION_VALVE_SWITCH",         event_base + 202 },
            { "EVT_OH_BLEED_TRIP_RESET_BUTTON",              event_base + 209 },
            { "EVT_OH_BLEED_ENG_1_SWITCH",                   event_base + 210 },
            { "EVT_OH_BLEED_APU_SWITCH",                     event_base + 211 },
            { "EVT_OH_BLEED_ENG_2_SWITCH",                   event_base + 212 },

            // ===== Overhead — Cabin Pressurization =====
            { "EVT_OH_PRESS_FLT_ALT_KNOB",                   event_base + 218 },
            { "EVT_OH_PRESS_LAND_ALT_KNOB",                  event_base + 220 },
            { "EVT_OH_PRESS_VALVE_SWITCH",                   event_base + 222 },
            { "EVT_OH_PRESS_SELECTOR",                       event_base + 223 },

            // ===== Overhead — Cabin Altitude =====
            { "EVT_OH_CAB_ALT_HORN_CUTOUT_BUTTON",           event_base + 183 },

            // ===== Aft Overhead — LE Devices =====
            { "EVT_OH_LE_DEVICES_TEST_SWITCH",               event_base + 224 },

            // ===== Aft Overhead — Service Interphone =====
            { "EVT_OH_SERVICE_INTERPHONE_SWITCH",            event_base + 257 },

            // ===== Aft Overhead — Dome =====
            { "EVT_OH_DOME_SWITCH",                          event_base + 258 },

            // ===== Aft Overhead — ISDU =====
            { "EVT_ISDU_DSPL_SEL",                           event_base + 229 },
            { "EVT_ISDU_DSPL_SEL_BRT",                       event_base + 230 },
            { "EVT_ISDU_SYS_DSPL",                           event_base + 231 },
            { "EVT_ISDU_KBD_1",                              event_base + 232 },
            { "EVT_ISDU_KBD_2",                              event_base + 233 },
            { "EVT_ISDU_KBD_3",                              event_base + 234 },
            { "EVT_ISDU_KBD_4",                              event_base + 235 },
            { "EVT_ISDU_KBD_5",                              event_base + 236 },
            { "EVT_ISDU_KBD_6",                              event_base + 237 },
            { "EVT_ISDU_KBD_7",                              event_base + 238 },
            { "EVT_ISDU_KBD_8",                              event_base + 239 },
            { "EVT_ISDU_KBD_9",                              event_base + 240 },
            { "EVT_ISDU_KBD_ENT",                            event_base + 241 },
            { "EVT_ISDU_KBD_0",                              event_base + 243 },
            { "EVT_ISDU_KBD_CLR",                            event_base + 244 },
            { "EVT_IRU_MSU_LEFT",                            event_base + 255 },
            { "EVT_IRU_MSU_LEFT_INOUT",                      event_base + 259 },
            { "EVT_IRU_MSU_RIGHT",                           event_base + 256 },
            { "EVT_IRU_MSU_RIGHT_INOUT",                     event_base + 260 },
            { "EVT_WLAN_SWITCH",                             event_base + 888 },
            { "EVT_WLAN_GUARD",                              event_base + 889 },

            // ===== Aft Overhead — Engine Control =====
            { "EVT_OH_EEC_L_GUARD",                          event_base + 267 },
            { "EVT_OH_EEC_L_SWITCH",                         event_base + 268 },
            { "EVT_OH_EEC_R_GUARD",                          event_base + 270 },
            { "EVT_OH_EEC_R_SWITCH",                         event_base + 271 },

            // ===== Aft Overhead — Oxygen =====
            { "EVT_OH_OXY_PASS_SWITCH",                      event_base + 264 },
            { "EVT_OH_OXY_PASS_GUARD",                       event_base + 265 },
            { "EVT_OH_OXY_TEST_RESET_SWITCH_L",              event_base + 983 },
            { "EVT_OH_OXY_TEST_RESET_SWITCH_R",              event_base + 9832 },
            { "EVT_OH_OXY_RED_BUTTON_L",                     event_base + 9831 },
            { "EVT_OH_OXY_RED_BUTTON_R",                     event_base + 9833 },

            // ===== Aft Overhead — Flight Recorder & Warning =====
            { "EVT_OH_FLTREC_SWITCH",                        event_base + 298 },
            { "EVT_OH_FLTREC_GUARD",                         event_base + 299 },
            { "EVT_OH_WARNING_TEST_MACH_IAS_1_PUSH",         event_base + 301 },
            { "EVT_OH_WARNING_TEST_MACH_IAS_2_PUSH",         event_base + 302 },
            { "EVT_OH_WARNING_TEST_STALL_1_PUSH",            event_base + 303 },
            { "EVT_OH_WARNING_TEST_STALL_2_PUSH",            event_base + 304 },
            { "EVT_OH_VOICEREC_SWITCH",                      event_base + 2981 },

            // ===== Overhead — Test Gauge =====
            { "EVT_OH_TRIM_AIR_SWITCH_TOGGLE",               event_base + 15200 },
            { "EVT_OH_WING_BODY_OVERHEAT_TEST_PUSH",         event_base + 15201 },

            // ===== Integrated Standby Flight Display (ISFD) =====
            { "EVT_ISFD_APP",                                event_base + 987 },
            { "EVT_ISFD_HP_IN",                              event_base + 986 },
            { "EVT_ISFD_PLUS",                               event_base + 988 },
            { "EVT_ISFD_MINUS",                              event_base + 989 },
            { "EVT_ISFD_ATT_RST",                            event_base + 990 },
            { "EVT_ISFD_BARO",                               event_base + 991 },
            { "EVT_ISFD_BARO_PUSH",                          event_base + 993 },
            { "EVT_ISFD_MENU",                               event_base + 2021 },
            { "EVT_ISFD_ADJUST",                             event_base + 2022 },
            { "EVT_ISFD_ADJUST_PUSH",                        event_base + 2023 },

            // ===== Analog Standby Instruments =====
            { "EVT_STANDBY_ADI_APPR_MODE",                   event_base + 474 },
            { "EVT_STANDBY_ADI_CAGE_KNOB",                   event_base + 476 },
            { "EVT_STANDBY_ALT_BARO_KNOB",                   event_base + 492 },
            { "EVT_RMI_LEFT_SELECTOR",                       event_base + 497 },
            { "EVT_RMI_RIGHT_SELECTOR",                      event_base + 498 },

            // ===== Glareshield — MCP =====
            { "EVT_MCP_COURSE_SELECTOR_L",                   event_base + 376 },
            { "EVT_MCP_FD_SWITCH_L",                         event_base + 378 },
            { "EVT_MCP_AT_ARM_SWITCH",                       event_base + 380 },
            { "EVT_MCP_N1_SWITCH",                           event_base + 381 },
            { "EVT_MCP_SPEED_SWITCH",                        event_base + 382 },
            { "EVT_MCP_CO_SWITCH",                           event_base + 383 },
            { "EVT_MCP_SPEED_SELECTOR",                      event_base + 384 },
            { "EVT_MCP_VNAV_SWITCH",                         event_base + 386 },
            { "EVT_MCP_SPD_INTV_SWITCH",                     event_base + 387 },
            { "EVT_MCP_BANK_ANGLE_SELECTOR",                 event_base + 389 },
            { "EVT_MCP_HEADING_SELECTOR",                    event_base + 390 },
            { "EVT_MCP_LVL_CHG_SWITCH",                      event_base + 391 },
            { "EVT_MCP_HDG_SEL_SWITCH",                      event_base + 392 },
            { "EVT_MCP_APP_SWITCH",                          event_base + 393 },
            { "EVT_MCP_ALT_HOLD_SWITCH",                     event_base + 394 },
            { "EVT_MCP_VS_SWITCH",                           event_base + 395 },
            { "EVT_MCP_VOR_LOC_SWITCH",                      event_base + 396 },
            { "EVT_MCP_LNAV_SWITCH",                         event_base + 397 },
            { "EVT_MCP_ALTITUDE_SELECTOR",                   event_base + 400 },
            { "EVT_MCP_VS_SELECTOR",                         event_base + 401 },
            { "EVT_MCP_CMD_A_SWITCH",                        event_base + 402 },
            { "EVT_MCP_CMD_B_SWITCH",                        event_base + 403 },
            { "EVT_MCP_CWS_A_SWITCH",                        event_base + 404 },
            { "EVT_MCP_CWS_B_SWITCH",                        event_base + 405 },
            { "EVT_MCP_DISENGAGE_BAR",                       event_base + 406 },
            { "EVT_MCP_FD_SWITCH_R",                         event_base + 407 },
            { "EVT_MCP_COURSE_SELECTOR_R",                   event_base + 409 },
            { "EVT_MCP_ALT_INTV_SWITCH",                     event_base + 885 },

            // ===== Glareshield — EFIS Captain Control Panel =====
            { "EVT_EFIS_CPT_MINIMUMS",                       event_base + 355 },
            { "EVT_EFIS_CPT_MINIMUMS_RADIO_BARO",            event_base + 356 },
            { "EVT_EFIS_CPT_MINIMUMS_RST",                   event_base + 357 },
            { "EVT_EFIS_CPT_VOR_ADF_SELECTOR_L",             event_base + 358 },
            { "EVT_EFIS_CPT_MODE",                           event_base + 359 },
            { "EVT_EFIS_CPT_MODE_CTR",                       event_base + 360 },
            { "EVT_EFIS_CPT_RANGE",                          event_base + 361 },
            { "EVT_EFIS_CPT_RANGE_TFC",                      event_base + 362 },
            { "EVT_EFIS_CPT_FPV",                            event_base + 363 },
            { "EVT_EFIS_CPT_MTRS",                           event_base + 364 },
            { "EVT_EFIS_CPT_BARO",                           event_base + 365 },
            { "EVT_EFIS_CPT_BARO_IN_HPA",                    event_base + 366 },
            { "EVT_EFIS_CPT_BARO_STD",                       event_base + 367 },
            { "EVT_EFIS_CPT_VOR_ADF_SELECTOR_R",             event_base + 368 },
            { "EVT_EFIS_CPT_WXR",                            event_base + 369 },
            { "EVT_EFIS_CPT_STA",                            event_base + 370 },
            { "EVT_EFIS_CPT_WPT",                            event_base + 371 },
            { "EVT_EFIS_CPT_ARPT",                           event_base + 372 },
            { "EVT_EFIS_CPT_DATA",                           event_base + 373 },
            { "EVT_EFIS_CPT_POS",                            event_base + 374 },
            { "EVT_EFIS_CPT_TERR",                           event_base + 375 },

            // ===== Glareshield — EFIS F/O Control Panel =====
            { "EVT_EFIS_FO_MINIMUMS",                        event_base + 411 },
            { "EVT_EFIS_FO_MINIMUMS_RADIO_BARO",             event_base + 412 },
            { "EVT_EFIS_FO_MINIMUMS_RST",                    event_base + 413 },
            { "EVT_EFIS_FO_VOR_ADF_SELECTOR_L",              event_base + 414 },
            { "EVT_EFIS_FO_MODE",                            event_base + 415 },
            { "EVT_EFIS_FO_MODE_CTR",                        event_base + 416 },
            { "EVT_EFIS_FO_RANGE",                           event_base + 417 },
            { "EVT_EFIS_FO_RANGE_TFC",                       event_base + 418 },
            { "EVT_EFIS_FO_FPV",                             event_base + 419 },
            { "EVT_EFIS_FO_MTRS",                            event_base + 420 },
            { "EVT_EFIS_FO_BARO",                            event_base + 421 },
            { "EVT_EFIS_FO_BARO_IN_HPA",                     event_base + 422 },
            { "EVT_EFIS_FO_BARO_STD",                        event_base + 423 },
            { "EVT_EFIS_FO_VOR_ADF_SELECTOR_R",              event_base + 424 },
            { "EVT_EFIS_FO_WXR",                             event_base + 425 },
            { "EVT_EFIS_FO_STA",                             event_base + 426 },
            { "EVT_EFIS_FO_WPT",                             event_base + 427 },
            { "EVT_EFIS_FO_ARPT",                            event_base + 428 },
            { "EVT_EFIS_FO_DATA",                            event_base + 429 },
            { "EVT_EFIS_FO_POS",                             event_base + 430 },
            { "EVT_EFIS_FO_TERR",                            event_base + 431 },

            // ===== Pushback Tug =====
            { "EVT_RELEASE_PUSHBACK_TUG",                    event_base + 995 },

            // ===== Display Select Panel — Captain =====
            { "EVT_DSP_CPT_BELOW_GS_INHIBIT_SWITCH",         event_base + 327 },
            { "EVT_DSP_CPT_MAIN_DU_SELECTOR",                event_base + 335 },
            { "EVT_DSP_CPT_LOWER_DU_SELECTOR",               event_base + 336 },
            { "EVT_DSP_CPT_DISENGAGE_TEST_SWITCH",           event_base + 342 },
            { "EVT_DSP_CPT_AP_RESET_SWITCH",                 event_base + 339 },
            { "EVT_DSP_CPT_AT_RESET_SWITCH",                 event_base + 340 },
            { "EVT_DSP_CPT_FMC_RESET_SWITCH",                event_base + 341 },
            { "EVT_DSP_CPT_MASTER_LIGHTS_SWITCH",            event_base + 346 },

            // ===== Display Select Panel — F/O =====
            { "EVT_DSP_FO_MAIN_DU_SELECTOR",                 event_base + 440 },
            { "EVT_DSP_FO_LOWER_DU_SELECTOR",                event_base + 441 },
            { "EVT_DSP_FO_DISENGAGE_TEST_SWITCH",            event_base + 442 },
            { "EVT_DSP_FO_FMC_RESET_SWITCH",                 event_base + 443 },
            { "EVT_DSP_FO_AT_RESET_SWITCH",                  event_base + 444 },
            { "EVT_DSP_FO_AP_RESET_SWITCH",                  event_base + 445 },
            { "EVT_DSP_FO_BELOW_GS_INHIBIT_SWITCH",          event_base + 446 },

            // ===== Main Panel Misc =====
            { "EVT_MPM_AUTOBRAKE_SELECTOR",                  event_base + 460 },
            { "EVT_MPM_AUTOBRAKE_SELECTOR_INOUT",            event_base + 461 },
            { "EVT_MPM_MFD_SYS_BUTTON",                      event_base + 462 },
            { "EVT_MPM_MFD_ENG_BUTTON",                      event_base + 463 },
            { "EVT_MPM_MFD_C_R_BUTTON",                      event_base + 4621 },
            { "EVT_MPM_SPEED_REFERENCE_SELECTOR",            event_base + 464 },
            { "EVT_MPM_SPEED_REFERENCE_CONTROL",             event_base + 465 },
            { "EVT_MPM_N1SET_SELECTOR",                      event_base + 466 },
            { "EVT_MPM_N1SET_CONTROL",                       event_base + 467 },
            { "EVT_MPM_FUEL_FLOW_SWITCH",                    event_base + 468 },

            // ===== Aux Fuel Cockpit Display =====
            { "EVT_AUX_FUEL_LEFT_TEST_SWITCH",               event_base + 2030 },
            { "EVT_AUX_FUEL_RIGHT_TEST_SWITCH",              event_base + 2031 },
            { "EVT_AUX_FUEL_LEFT_ALERT_SWITCH",              event_base + 2032 },
            { "EVT_AUX_FUEL_RIGHT_ALERT_SWITCH",             event_base + 2033 },
            { "EVT_AUX_FUEL_LEFT_MAINT_SWITCH",              event_base + 2034 },
            { "EVT_AUX_FUEL_RIGHT_MAINT_SWITCH",             event_base + 2035 },

            // ===== 737MAX =====
            { "EVT_MAX_MFD_INFO_BUTTON",                     event_base + 2040 },
            { "EVT_MAX_MFD_ENG_TFR_BUTTON",                  event_base + 2041 },
            { "EVT_MAX_LWR_SEL_LEFT_OUTER_KNOB",             event_base + 2042 },
            { "EVT_MAX_LWR_SEL_LEFT_INNER_KNOB",             event_base + 2043 },
            { "EVT_MAX_LWR_SEL_LEFT_SEL_PUSH",               event_base + 2044 },
            { "EVT_MAX_LWR_SEL_RIGHT_OUTER_KNOB",            event_base + 2045 },
            { "EVT_MAX_LWR_SEL_RIGHT_INNER_KNOB",            event_base + 2046 },
            { "EVT_MAX_LWR_SEL_RIGHT_SEL_PUSH",              event_base + 2047 },
            { "EVT_MAX_GEAR_UNLOCK",                         event_base + 2048 },

            // ===== Gear Panel =====
            { "EVT_GEAR_LEVER",                              event_base + 455 },
            { "EVT_GEAR_LEVER_OFF",                          event_base + 4551 },
            { "EVT_GEAR_LEVER_UNLOCK",                       event_base + 4552 },

            // ===== Nose Wheel Steering =====
            { "EVT_NOSE_WHEEL_STEERING_SWITCH",              event_base + 325 },
            { "EVT_NOSE_WHEEL_STEERING_SWITCH_GUARD",        event_base + 326 },
            { "EVT_TILLER",                                  event_base + 975 },

            // ===== Warning / Caution =====
            { "EVT_FIRE_WARN_LIGHT_LEFT",                    event_base + 347 },
            { "EVT_MASTER_CAUTION_LIGHT_LEFT",               event_base + 348 },
            { "EVT_FIRE_WARN_LIGHT_RIGHT",                   event_base + 439 },
            { "EVT_MASTER_CAUTION_LIGHT_RIGHT",              event_base + 438 },
            { "EVT_SYSTEM_ANNUNCIATOR_PANEL_LEFT",           event_base + 349 },
            { "EVT_SYSTEM_ANNUNCIATOR_PANEL_RIGHT",          event_base + 437 },

            // ===== Lower Main — Brightness =====
            { "EVT_LWRMAIN_CAPT_MAIN_PANEL_BRT",             event_base + 328 },
            { "EVT_LWRMAIN_CAPT_OUTBD_DU_BRT",               event_base + 329 },
            { "EVT_LWRMAIN_CAPT_INBD_DU_BRT",                event_base + 330 },
            { "EVT_LWRMAIN_CAPT_INBD_DU_INNER_BRT",          event_base + 331 },
            { "EVT_LWRMAIN_CAPT_LOWER_DU_BRT",               event_base + 332 },
            { "EVT_LWRMAIN_CAPT_LOWER_DU_INNER_BRT",         event_base + 333 },
            { "EVT_LWRMAIN_CAPT_UPPER_DU_BRT",               event_base + 334 },
            { "EVT_LWRMAIN_CAPT_BACKGROUND_BRT",             event_base + 337 },
            { "EVT_LWRMAIN_CAPT_AFDS_BRT",                   event_base + 338 },
            { "EVT_LWRMAIN_FO_INBD_DU_BRT",                  event_base + 507 },
            { "EVT_LWRMAIN_FO_INBD_DU_INNER_BRT",            event_base + 508 },
            { "EVT_LWRMAIN_FO_MAIN_PANEL_BRT",               event_base + 510 },
            { "EVT_LWRMAIN_FO_OUTBD_DU_BRT",                 event_base + 509 },

            // ===== GPWS =====
            { "EVT_GPWS_SYS_TEST_BTN",                       event_base + 500 },
            { "EVT_GPWS_FLAP_INHIBIT_SWITCH",                event_base + 501 },
            { "EVT_GPWS_FLAP_INHIBIT_GUARD",                 event_base + 502 },
            { "EVT_GPWS_GEAR_INHIBIT_SWITCH",                event_base + 503 },
            { "EVT_GPWS_GEAR_INHIBIT_GUARD",                 event_base + 504 },
            { "EVT_GPWS_TERR_INHIBIT_SWITCH",                event_base + 505 },
            { "EVT_GPWS_TERR_INHIBIT_GUARD",                 event_base + 506 },

            // ===== Chronometers =====
            { "EVT_CHRONO_L_CHR",                            event_base + 314 },
            { "EVT_CHRONO_L_TCSR",                           event_base + 3141 },
            { "EVT_CHRONO_L_TIME_DATE",                      event_base + 315 },
            { "EVT_CHRONO_L_SET",                            event_base + 316 },
            { "EVT_CHRONO_L_PLUS",                           event_base + 317 },
            { "EVT_CHRONO_L_MINUS",                          event_base + 318 },
            { "EVT_CHRONO_L_RESET",                          event_base + 320 },
            { "EVT_CHRONO_L_ET",                             event_base + 321 },
            { "EVT_CHRONO_R_CHR",                            event_base + 523 },
            { "EVT_CHRONO_R_TCSR",                           event_base + 5231 },
            { "EVT_CHRONO_R_TIME_DATE",                      event_base + 524 },
            { "EVT_CHRONO_R_SET",                            event_base + 525 },
            { "EVT_CHRONO_R_PLUS",                           event_base + 526 },
            { "EVT_CHRONO_R_MINUS",                          event_base + 527 },
            { "EVT_CHRONO_R_RESET",                          event_base + 529 },
            { "EVT_CHRONO_R_ET",                             event_base + 530 },
            { "EVT_CLOCK_L",                                 event_base + 890 },
            { "EVT_MIC_L",                                   event_base + 891 },
            { "EVT_MIC_R",                                   event_base + 892 },
            { "EVT_CLOCK_R",                                 event_base + 893 },

            // ===== Side Panel — Chart & Map =====
            { "EVT_CHART_BRT_L",                             event_base + 319 },
            { "EVT_CHART_BRT_R",                             event_base + 322 },
            { "EVT_MAP_BRT_L",                               event_base + 323 },
            { "EVT_MAP_BRT_R",                               event_base + 324 },
            { "EVT_MAP_BRT_L_PUSHPULL",                      event_base + 895 },
            { "EVT_MAP_BRT_R_PUSHPULL",                      event_base + 896 },

            // ===== Control Stand =====
            { "EVT_CONTROL_STAND_TRIM_WHEEL",                event_base + 678 },
            { "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER",         event_base + 679 },
            { "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN",    event_base + 6791 },
            { "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM",     event_base + 6792 },
            { "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_50PCT",   event_base + 6793 },
            { "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_FLT_DET", event_base + 6794 },
            { "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_UP",      event_base + 6795 },
            { "EVT_CONTROL_STAND_REV_THRUST1_LEVER",         event_base + 680 },
            { "EVT_CONTROL_STAND_REV_THRUST2_LEVER",         event_base + 681 },
            { "EVT_CONTROL_STAND_FWD_THRUST1_LEVER",         event_base + 683 },
            { "EVT_CONTROL_STAND_FWD_THRUST2_LEVER",         event_base + 686 },
            { "EVT_CONTROL_STAND_TOGA1_SWITCH",              event_base + 684 },
            { "EVT_CONTROL_STAND_TOGA2_SWITCH",              event_base + 687 },
            { "EVT_CONTROL_STAND_AT1_DISENGAGE_SWITCH",      event_base + 682 },
            { "EVT_CONTROL_STAND_AT2_DISENGAGE_SWITCH",      event_base + 685 },
            { "EVT_CONTROL_STAND_ENG1_START_LEVER",          event_base + 688 },
            { "EVT_CONTROL_STAND_ENG2_START_LEVER",          event_base + 689 },
            { "EVT_CONTROL_STAND_PARK_BRAKE_LEVER",          event_base + 693 },
            { "EVT_CONTROL_STAND_STABTRIM_ELEC_SWITCH",      event_base + 709 },
            { "EVT_CONTROL_STAND_STABTRIM_ELEC_SWITCH_GUARD",event_base + 710 },
            { "EVT_CONTROL_STAND_STABTRIM_AP_SWITCH",        event_base + 711 },
            { "EVT_CONTROL_STAND_STABTRIM_AP_SWITCH_GUARD",  event_base + 712 },
            { "EVT_CONTROL_STAND_HORN_CUTOUT_SWITCH",        event_base + 713 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER",               event_base + 714 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_0",             event_base + 7141 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_1",             event_base + 7142 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_2",             event_base + 7143 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_5",             event_base + 7144 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_10",            event_base + 7145 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_15",            event_base + 7146 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_25",            event_base + 7147 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_30",            event_base + 7148 },
            { "EVT_CONTROL_STAND_FLAPS_LEVER_40",            event_base + 7149 },

            // ===== Flight Deck Door Panel =====
            { "EVT_FLT_DK_DOOR_KNOB",                        event_base + 834 },
            { "EVT_STAB_TRIM_OVRD_SWITCH",                   event_base + 830 },
            { "EVT_STAB_TRIM_OVRD_SWITCH_GUARD",             event_base + 831 },

            // ===== VHF NAV Panels =====
            { "EVT_NAV1_TRANSFER_SWITCH",                    event_base + 729 },
            { "EVT_NAV1_TEST_SWICTH",                        event_base + 731 },
            { "EVT_NAV1_INNER_SELECTOR",                     event_base + 732 },
            { "EVT_NAV1_OUTER_SELECTOR",                     event_base + 733 },
            { "EVT_NAV2_TRANSFER_SWITCH",                    event_base + 845 },
            { "EVT_NAV2_TEST_SWICTH",                        event_base + 847 },
            { "EVT_NAV2_OUTER_SELECTOR",                     event_base + 848 },
            { "EVT_NAV2_INNER_SELECTOR",                     event_base + 849 },

            // ===== MMR Panels =====
            { "EVT_MMR1_TRANSFER_SWITCH",                    event_base + 7210 },
            { "EVT_MMR1_TEST_SWITCH",                        event_base + 7211 },
            { "EVT_MMR1_MODE_DN_SWITCH",                     event_base + 7212 },
            { "EVT_MMR1_MODE_UP_SWITCH",                     event_base + 7213 },
            { "EVT_MMR1_KEYPAD_1",                           event_base + 7214 },
            { "EVT_MMR1_KEYPAD_2",                           event_base + 7215 },
            { "EVT_MMR1_KEYPAD_3",                           event_base + 7216 },
            { "EVT_MMR1_KEYPAD_4",                           event_base + 7217 },
            { "EVT_MMR1_KEYPAD_5",                           event_base + 7218 },
            { "EVT_MMR1_KEYPAD_6",                           event_base + 7219 },
            { "EVT_MMR1_KEYPAD_7",                           event_base + 7220 },
            { "EVT_MMR1_KEYPAD_8",                           event_base + 7221 },
            { "EVT_MMR1_KEYPAD_9",                           event_base + 7222 },
            { "EVT_MMR1_KEYPAD_0",                           event_base + 7223 },
            { "EVT_MMR1_KEYPAD_CLR",                         event_base + 7224 },
            { "EVT_MMR2_TRANSFER_SWITCH",                    event_base + 7225 },
            { "EVT_MMR2_TEST_SWITCH",                        event_base + 7226 },
            { "EVT_MMR2_MODE_DN_SWITCH",                     event_base + 7227 },
            { "EVT_MMR2_MODE_UP_SWITCH",                     event_base + 7228 },
            { "EVT_MMR2_KEYPAD_1",                           event_base + 7229 },
            { "EVT_MMR2_KEYPAD_2",                           event_base + 7230 },
            { "EVT_MMR2_KEYPAD_3",                           event_base + 7231 },
            { "EVT_MMR2_KEYPAD_4",                           event_base + 7232 },
            { "EVT_MMR2_KEYPAD_5",                           event_base + 7233 },
            { "EVT_MMR2_KEYPAD_6",                           event_base + 7234 },
            { "EVT_MMR2_KEYPAD_7",                           event_base + 7235 },
            { "EVT_MMR2_KEYPAD_8",                           event_base + 7236 },
            { "EVT_MMR2_KEYPAD_9",                           event_base + 7237 },
            { "EVT_MMR2_KEYPAD_0",                           event_base + 7238 },
            { "EVT_MMR2_KEYPAD_CLR",                         event_base + 7239 },

            // ===== ADF Panel =====
            { "EVT_ADF_MODE_SELECTOR",                       event_base + 818 },
            { "EVT_ADF_TONE_SWITCH",                         event_base + 820 },
            { "EVT_ADF_INNER_SELECTOR",                      event_base + 822 },
            { "EVT_ADF_MIDDLE_SELECTOR",                     event_base + 823 },
            { "EVT_ADF_OUTER_SELECTOR",                      event_base + 824 },
            { "EVT_ADF_TRANSFER_SWITCH",                     event_base + 827 },

            // ===== SELCAL Panel =====
            { "EVT_SELCAL_VHF1_SWITCH",                      event_base + 812 },
            { "EVT_SELCAL_VHF2_SWITCH",                      event_base + 813 },
            { "EVT_SELCAL_VHF3_SWITCH",                      event_base + 814 },
            { "EVT_SELCAL_HF1_SWITCH",                       event_base + 937 },
            { "EVT_SELCAL_HF2_SWITCH",                       event_base + 938 },

            // ===== COMM Panels =====
            // --- COM1 ---
            { "EVT_COM1_TRANSFER_SWITCH",                    event_base + 721 },
            { "EVT_COM1_HF_SENSOR_KNOB",                     event_base + 724 },
            { "EVT_COM1_TEST_SWICTH",                        event_base + 725 },
            { "EVT_COM1_OUTER_SELECTOR",                     event_base + 726 },
            { "EVT_COM1_INNER_SELECTOR",                     event_base + 727 },
            { "EVT_COM1_PNL_OFF_SWITCH",                     event_base + 903 },
            { "EVT_COM1_VHF1_SWITCH",                        event_base + 904 },
            { "EVT_COM1_VHF2_SWITCH",                        event_base + 906 },
            { "EVT_COM1_VHF3_SWITCH",                        event_base + 908 },
            { "EVT_COM1_HF1_SWITCH",                         event_base + 910 },
            { "EVT_COM1_AM_SWITCH",                          event_base + 912 },
            { "EVT_COM1_HF2_SWITCH",                         event_base + 914 },
            // --- COM2 ---
            { "EVT_COM2_TRANSFER_SWITCH",                    event_base + 837 },
            { "EVT_COM2_HF_SENSOR_KNOB",                     event_base + 840 },
            { "EVT_COM2_TEST_SWICTH",                        event_base + 841 },
            { "EVT_COM2_OUTER_SELECTOR",                     event_base + 842 },
            { "EVT_COM2_INNER_SELECTOR",                     event_base + 843 },
            { "EVT_COM2_PNL_OFF_SWITCH",                     event_base + 924 },
            { "EVT_COM2_VHF1_SWITCH",                        event_base + 925 },
            { "EVT_COM2_VHF2_SWITCH",                        event_base + 927 },
            { "EVT_COM2_VHF3_SWITCH",                        event_base + 929 },
            { "EVT_COM2_HF1_SWITCH",                         event_base + 931 },
            { "EVT_COM2_AM_SWITCH",                          event_base + 933 },
            { "EVT_COM2_HF2_SWITCH",                         event_base + 935 },
            // --- COM3 ---
            { "EVT_COM3_TRANSFER_SWITCH",                    event_base + 946 },
            { "EVT_COM3_HF_SENSOR_KNOB",                     event_base + 949 },
            { "EVT_COM3_TEST_SWICTH",                        event_base + 950 },
            { "EVT_COM3_OUTER_SELECTOR",                     event_base + 951 },
            { "EVT_COM3_INNER_SELECTOR",                     event_base + 952 },
            { "EVT_COM3_PNL_OFF_SWITCH",                     event_base + 953 },
            { "EVT_COM3_VHF1_SWITCH",                        event_base + 954 },
            { "EVT_COM3_VHF2_SWITCH",                        event_base + 956 },
            { "EVT_COM3_VHF3_SWITCH",                        event_base + 958 },
            { "EVT_COM3_HF1_SWITCH",                         event_base + 960 },
            { "EVT_COM3_AM_SWITCH",                          event_base + 962 },
            { "EVT_COM3_HF2_SWITCH",                         event_base + 964 },

            // ===== Audio Control Panel — Captain =====
            { "EVT_ACP_CAPT_MIC_VHF1",                       event_base + 734 },
            { "EVT_ACP_CAPT_MIC_VHF2",                       event_base + 735 },
            { "EVT_ACP_CAPT_MIC_VHF3",                       event_base + 877 },
            { "EVT_ACP_CAPT_MIC_HF1",                        event_base + 878 },
            { "EVT_ACP_CAPT_MIC_HF2",                        event_base + 879 },
            { "EVT_ACP_CAPT_MIC_FLT",                        event_base + 736 },
            { "EVT_ACP_CAPT_MIC_SVC",                        event_base + 737 },
            { "EVT_ACP_CAPT_MIC_PA",                         event_base + 738 },
            { "EVT_ACP_CAPT_REC_VHF1",                       event_base + 739 },
            { "EVT_ACP_CAPT_REC_VHF2",                       event_base + 740 },
            { "EVT_ACP_CAPT_REC_VHF3",                       event_base + 741 },
            { "EVT_ACP_CAPT_REC_HF1",                        event_base + 742 },
            { "EVT_ACP_CAPT_REC_HF2",                        event_base + 880 },
            { "EVT_ACP_CAPT_REC_FLT",                        event_base + 743 },
            { "EVT_ACP_CAPT_REC_SVC",                        event_base + 744 },
            { "EVT_ACP_CAPT_REC_PA",                         event_base + 745 },
            { "EVT_ACP_CAPT_REC_NAV1",                       event_base + 746 },
            { "EVT_ACP_CAPT_REC_NAV2",                       event_base + 747 },
            { "EVT_ACP_CAPT_REC_ADF1",                       event_base + 748 },
            { "EVT_ACP_CAPT_REC_ADF2",                       event_base + 749 },
            { "EVT_ACP_CAPT_REC_MKR",                        event_base + 750 },
            { "EVT_ACP_CAPT_REC_SPKR",                       event_base + 751 },
            { "EVT_ACP_CAPT_RT_IC_SWITCH",                   event_base + 752 },
            { "EVT_ACP_CAPT_MASK_BOOM_SWITCH",               event_base + 753 },
            { "EVT_ACP_CAPT_FILTER_SWITCH",                  event_base + 754 },
            { "EVT_ACP_CAPT_ALT_NORM_SWITCH",                event_base + 755 },

            // ===== Audio Control Panel — F/O =====
            { "EVT_ACP_FO_MIC_VHF1",                         event_base + 850 },
            { "EVT_ACP_FO_MIC_VHF2",                         event_base + 851 },
            { "EVT_ACP_FO_MIC_VHF3",                         event_base + 881 },
            { "EVT_ACP_FO_MIC_HF1",                          event_base + 882 },
            { "EVT_ACP_FO_MIC_HF2",                          event_base + 883 },
            { "EVT_ACP_FO_MIC_FLT",                          event_base + 852 },
            { "EVT_ACP_FO_MIC_SVC",                          event_base + 853 },
            { "EVT_ACP_FO_MIC_PA",                           event_base + 854 },
            { "EVT_ACP_FO_REC_VHF1",                         event_base + 855 },
            { "EVT_ACP_FO_REC_VHF2",                         event_base + 856 },
            { "EVT_ACP_FO_REC_VHF3",                         event_base + 857 },
            { "EVT_ACP_FO_REC_HF1",                          event_base + 858 },
            { "EVT_ACP_FO_REC_HF2",                          event_base + 884 },
            { "EVT_ACP_FO_REC_FLT",                          event_base + 859 },
            { "EVT_ACP_FO_REC_SVC",                          event_base + 860 },
            { "EVT_ACP_FO_REC_PA",                           event_base + 861 },
            { "EVT_ACP_FO_REC_NAV1",                         event_base + 862 },
            { "EVT_ACP_FO_REC_NAV2",                         event_base + 863 },
            { "EVT_ACP_FO_REC_ADF1",                         event_base + 864 },
            { "EVT_ACP_FO_REC_ADF2",                         event_base + 865 },
            { "EVT_ACP_FO_REC_MKR",                          event_base + 866 },
            { "EVT_ACP_FO_REC_SPKR",                         event_base + 867 },
            { "EVT_ACP_FO_VOL_NAV1",                         event_base + 1862 },
            { "EVT_ACP_FO_VOL_NAV2",                         event_base + 1863 },
            { "EVT_ACP_FO_VOL_ADF1",                         event_base + 1864 },
            { "EVT_ACP_FO_VOL_ADF2",                         event_base + 1865 },
            { "EVT_ACP_FO_VOL_MKR",                          event_base + 1866 },
            { "EVT_ACP_FO_RT_IC_SWITCH",                     event_base + 868 },
            { "EVT_ACP_FO_MASK_BOOM_SWITCH",                 event_base + 869 },
            { "EVT_ACP_FO_FILTER_SWITCH",                    event_base + 870 },
            { "EVT_ACP_FO_ALT_NORM_SWITCH",                  event_base + 871 },

            // ===== Audio Control Panel — Observer =====
            { "EVT_ACP_OBS_MIC_VHF1",                        event_base + 291 },
            { "EVT_ACP_OBS_MIC_VHF2",                        event_base + 292 },
            { "EVT_ACP_OBS_MIC_VHF3",                        event_base + 293 },
            { "EVT_ACP_OBS_MIC_HF1",                         event_base + 294 },
            { "EVT_ACP_OBS_MIC_HF2",                         event_base + 295 },
            { "EVT_ACP_OBS_MIC_FLT",                         event_base + 296 },
            { "EVT_ACP_OBS_MIC_SVC",                         event_base + 297 },
            { "EVT_ACP_OBS_MIC_PA",                          event_base + 873 },
            { "EVT_ACP_OBS_REC_VHF1",                        event_base + 286 },
            { "EVT_ACP_OBS_REC_VHF2",                        event_base + 287 },
            { "EVT_ACP_OBS_REC_VHF3",                        event_base + 874 },
            { "EVT_ACP_OBS_REC_HF1",                         event_base + 875 },
            { "EVT_ACP_OBS_REC_HF2",                         event_base + 876 },
            { "EVT_ACP_OBS_REC_FLT",                         event_base + 288 },
            { "EVT_ACP_OBS_REC_SVC",                         event_base + 289 },
            { "EVT_ACP_OBS_REC_PA",                          event_base + 290 },
            { "EVT_ACP_OBS_REC_NAV1",                        event_base + 280 },
            { "EVT_ACP_OBS_REC_NAV2",                        event_base + 281 },
            { "EVT_ACP_OBS_REC_ADF1",                        event_base + 282 },
            { "EVT_ACP_OBS_REC_ADF2",                        event_base + 283 },
            { "EVT_ACP_OBS_REC_MKR",                         event_base + 284 },
            { "EVT_ACP_OBS_REC_SPKR",                        event_base + 285 },
            { "EVT_ACP_OBS_VOL_NAV1",                        event_base + 1280 },
            { "EVT_ACP_OBS_VOL_NAV2",                        event_base + 1281 },
            { "EVT_ACP_OBS_VOL_ADF1",                        event_base + 1282 },
            { "EVT_ACP_OBS_VOL_ADF2",                        event_base + 1283 },
            { "EVT_ACP_OBS_VOL_MKR",                         event_base + 1284 },
            { "EVT_ACP_OBS_RT_IC_SWITCH",                    event_base + 276 },
            { "EVT_ACP_OBS_MASK_BOOM_SWITCH",                event_base + 277 },
            { "EVT_ACP_OBS_FILTER_SWITCH",                   event_base + 278 },
            { "EVT_ACP_OBS_ALT_NORM_SWITCH",                 event_base + 279 },

            // ===== WX Radar Panel =====
            { "EVT_WXR_L_TFR",                               event_base + 790 },
            { "EVT_WXR_L_WX",                                event_base + 791 },
            { "EVT_WXR_L_WX_T",                              event_base + 916 },
            { "EVT_WXR_L_MAP",                               event_base + 792 },
            { "EVT_WXR_L_GC",                                event_base + 793 },
            { "EVT_WXR_AUTO",                                event_base + 917 },
            { "EVT_WXR_TEST",                                event_base + 918 },
            { "EVT_WXR_R_TFR",                               event_base + 919 },
            { "EVT_WXR_R_WX",                                event_base + 796 },
            { "EVT_WXR_R_WX_T",                              event_base + 920 },
            { "EVT_WXR_R_MAP",                               event_base + 797 },
            { "EVT_WXR_R_GC",                                event_base + 921 },
            { "EVT_WXR_L_TILT_CONTROL",                      event_base + 794 },
            { "EVT_WXR_L_GAIN_CONTROL",                      event_base + 923 },
            { "EVT_WXR_R_TILT_CONTROL",                      event_base + 795 },
            { "EVT_WXR_R_GAIN_CONTROL",                      event_base + 922 },

            // ===== TCAS =====
            { "EVT_TCAS_XPNDR",                              event_base + 798 },
            { "EVT_TCAS_MODE",                               event_base + 800 },
            { "EVT_TCAS_TEST",                               event_base + 801 },
            { "EVT_TCAS_ALTSOURCE",                          event_base + 803 },
            { "EVT_TCAS_KNOB1",                              event_base + 804 },
            { "EVT_TCAS_KNOB2",                              event_base + 805 },
            { "EVT_TCAS_IDENT",                              event_base + 806 },
            { "EVT_TCAS_KNOB3",                              event_base + 807 },
            { "EVT_TCAS_KNOB4",                              event_base + 808 },

            // ===== HUD Control Panel =====
            { "EVT_HUD_MODE",                                event_base + 770 },
            { "EVT_HUD_STB",                                 event_base + 771 },
            { "EVT_HUD_RWY",                                 event_base + 772 },
            { "EVT_HUD_GS",                                  event_base + 773 },
            { "EVT_HUD_CLR",                                 event_base + 775 },
            { "EVT_HUD_BRT",                                 event_base + 776 },
            { "EVT_HUD_DIM",                                 event_base + 777 },
            { "EVT_HUD_1",                                   event_base + 778 },
            { "EVT_HUD_2",                                   event_base + 779 },
            { "EVT_HUD_3",                                   event_base + 780 },
            { "EVT_HUD_4",                                   event_base + 781 },
            { "EVT_HUD_5",                                   event_base + 782 },
            { "EVT_HUD_6",                                   event_base + 783 },
            { "EVT_HUD_7",                                   event_base + 784 },
            { "EVT_HUD_8",                                   event_base + 785 },
            { "EVT_HUD_9",                                   event_base + 786 },
            { "EVT_HUD_0",                                   event_base + 788 },
            { "EVT_HUD_ENTER",                               event_base + 787 },
            { "EVT_HUD_TEST",                                event_base + 789 },
            { "EVT_HUD_STOW",                                event_base + 979 },
            { "EVT_HUD_BRIGTHNESS",                          event_base + 980 },
            { "EVT_HUD_AUTO_MAN",                            event_base + 981 },
            { "EVT_HGS_EYEPOINT",                            event_base + 984 },
            { "EVT_NON_HGS_EYEPOINT",                        event_base + 985 },

            // ===== HUD Annunciator Panel =====
            { "EVT_HGS_FAIL_SWITCH",                         event_base + 522 },

            // ===== CDU L (Captain) =====
            { "EVT_CDU_L_L1",                                event_base + 534 },
            { "EVT_CDU_L_L2",                                event_base + 535 },
            { "EVT_CDU_L_L3",                                event_base + 536 },
            { "EVT_CDU_L_L4",                                event_base + 537 },
            { "EVT_CDU_L_L5",                                event_base + 538 },
            { "EVT_CDU_L_L6",                                event_base + 539 },
            { "EVT_CDU_L_R1",                                event_base + 540 },
            { "EVT_CDU_L_R2",                                event_base + 541 },
            { "EVT_CDU_L_R3",                                event_base + 542 },
            { "EVT_CDU_L_R4",                                event_base + 543 },
            { "EVT_CDU_L_R5",                                event_base + 544 },
            { "EVT_CDU_L_R6",                                event_base + 545 },
            { "EVT_CDU_L_INIT_REF",                          event_base + 546 },
            { "EVT_CDU_L_RTE",                               event_base + 547 },
            { "EVT_CDU_L_CLB",                               event_base + 548 },
            { "EVT_CDU_L_CRZ",                               event_base + 549 },
            { "EVT_CDU_L_DES",                               event_base + 550 },
            { "EVT_CDU_L_MENU",                              event_base + 551 },
            { "EVT_CDU_L_LEGS",                              event_base + 552 },
            { "EVT_CDU_L_DEP_ARR",                           event_base + 553 },
            { "EVT_CDU_L_HOLD",                              event_base + 554 },
            { "EVT_CDU_L_PROG",                              event_base + 555 },
            { "EVT_CDU_L_EXEC",                              event_base + 556 },
            { "EVT_CDU_L_N1_LIMIT",                          event_base + 557 },
            { "EVT_CDU_L_FIX",                               event_base + 558 },
            { "EVT_CDU_L_PREV_PAGE",                         event_base + 559 },
            { "EVT_CDU_L_NEXT_PAGE",                         event_base + 560 },
            { "EVT_CDU_L_1",                                 event_base + 561 },
            { "EVT_CDU_L_2",                                 event_base + 562 },
            { "EVT_CDU_L_3",                                 event_base + 563 },
            { "EVT_CDU_L_4",                                 event_base + 564 },
            { "EVT_CDU_L_5",                                 event_base + 565 },
            { "EVT_CDU_L_6",                                 event_base + 566 },
            { "EVT_CDU_L_7",                                 event_base + 567 },
            { "EVT_CDU_L_8",                                 event_base + 568 },
            { "EVT_CDU_L_9",                                 event_base + 569 },
            { "EVT_CDU_L_DOT",                               event_base + 570 },
            { "EVT_CDU_L_0",                                 event_base + 571 },
            { "EVT_CDU_L_PLUS_MINUS",                        event_base + 572 },
            { "EVT_CDU_L_A",                                 event_base + 573 },
            { "EVT_CDU_L_B",                                 event_base + 574 },
            { "EVT_CDU_L_C",                                 event_base + 575 },
            { "EVT_CDU_L_D",                                 event_base + 576 },
            { "EVT_CDU_L_E",                                 event_base + 577 },
            { "EVT_CDU_L_F",                                 event_base + 578 },
            { "EVT_CDU_L_G",                                 event_base + 579 },
            { "EVT_CDU_L_H",                                 event_base + 580 },
            { "EVT_CDU_L_I",                                 event_base + 581 },
            { "EVT_CDU_L_J",                                 event_base + 582 },
            { "EVT_CDU_L_K",                                 event_base + 583 },
            { "EVT_CDU_L_L",                                 event_base + 584 },
            { "EVT_CDU_L_M",                                 event_base + 585 },
            { "EVT_CDU_L_N",                                 event_base + 586 },
            { "EVT_CDU_L_O",                                 event_base + 587 },
            { "EVT_CDU_L_P",                                 event_base + 588 },
            { "EVT_CDU_L_Q",                                 event_base + 589 },
            { "EVT_CDU_L_R",                                 event_base + 590 },
            { "EVT_CDU_L_S",                                 event_base + 591 },
            { "EVT_CDU_L_T",                                 event_base + 592 },
            { "EVT_CDU_L_U",                                 event_base + 593 },
            { "EVT_CDU_L_V",                                 event_base + 594 },
            { "EVT_CDU_L_W",                                 event_base + 595 },
            { "EVT_CDU_L_X",                                 event_base + 596 },
            { "EVT_CDU_L_Y",                                 event_base + 597 },
            { "EVT_CDU_L_Z",                                 event_base + 598 },
            { "EVT_CDU_L_SPACE",                             event_base + 599 },
            { "EVT_CDU_L_DEL",                               event_base + 600 },
            { "EVT_CDU_L_SLASH",                             event_base + 601 },
            { "EVT_CDU_L_CLR",                               event_base + 602 },
            { "EVT_CDU_L_BRITENESS",                         event_base + 605 },

            // ===== CDU R (F/O) =====
            { "EVT_CDU_R_L1",                                event_base + 606 },
            { "EVT_CDU_R_L2",                                event_base + 607 },
            { "EVT_CDU_R_L3",                                event_base + 608 },
            { "EVT_CDU_R_L4",                                event_base + 609 },
            { "EVT_CDU_R_L5",                                event_base + 610 },
            { "EVT_CDU_R_L6",                                event_base + 611 },
            { "EVT_CDU_R_R1",                                event_base + 612 },
            { "EVT_CDU_R_R2",                                event_base + 613 },
            { "EVT_CDU_R_R3",                                event_base + 614 },
            { "EVT_CDU_R_R4",                                event_base + 615 },
            { "EVT_CDU_R_R5",                                event_base + 616 },
            { "EVT_CDU_R_R6",                                event_base + 617 },
            { "EVT_CDU_R_INIT_REF",                          event_base + 618 },
            { "EVT_CDU_R_RTE",                               event_base + 619 },
            { "EVT_CDU_R_CLB",                               event_base + 620 },
            { "EVT_CDU_R_CRZ",                               event_base + 621 },
            { "EVT_CDU_R_DES",                               event_base + 622 },
            { "EVT_CDU_R_MENU",                              event_base + 623 },
            { "EVT_CDU_R_LEGS",                              event_base + 624 },
            { "EVT_CDU_R_DEP_ARR",                           event_base + 625 },
            { "EVT_CDU_R_HOLD",                              event_base + 626 },
            { "EVT_CDU_R_PROG",                              event_base + 627 },
            { "EVT_CDU_R_EXEC",                              event_base + 628 },
            { "EVT_CDU_R_N1_LIMIT",                          event_base + 629 },
            { "EVT_CDU_R_FIX",                               event_base + 630 },
            { "EVT_CDU_R_PREV_PAGE",                         event_base + 631 },
            { "EVT_CDU_R_NEXT_PAGE",                         event_base + 632 },
            { "EVT_CDU_R_1",                                 event_base + 633 },
            { "EVT_CDU_R_2",                                 event_base + 634 },
            { "EVT_CDU_R_3",                                 event_base + 635 },
            { "EVT_CDU_R_4",                                 event_base + 636 },
            { "EVT_CDU_R_5",                                 event_base + 637 },
            { "EVT_CDU_R_6",                                 event_base + 638 },
            { "EVT_CDU_R_7",                                 event_base + 639 },
            { "EVT_CDU_R_8",                                 event_base + 640 },
            { "EVT_CDU_R_9",                                 event_base + 641 },
            { "EVT_CDU_R_DOT",                               event_base + 642 },
            { "EVT_CDU_R_0",                                 event_base + 643 },
            { "EVT_CDU_R_PLUS_MINUS",                        event_base + 644 },
            { "EVT_CDU_R_A",                                 event_base + 645 },
            { "EVT_CDU_R_B",                                 event_base + 646 },
            { "EVT_CDU_R_C",                                 event_base + 647 },
            { "EVT_CDU_R_D",                                 event_base + 648 },
            { "EVT_CDU_R_E",                                 event_base + 649 },
            { "EVT_CDU_R_F",                                 event_base + 650 },
            { "EVT_CDU_R_G",                                 event_base + 651 },
            { "EVT_CDU_R_H",                                 event_base + 652 },
            { "EVT_CDU_R_I",                                 event_base + 653 },
            { "EVT_CDU_R_J",                                 event_base + 654 },
            { "EVT_CDU_R_K",                                 event_base + 655 },
            { "EVT_CDU_R_L",                                 event_base + 656 },
            { "EVT_CDU_R_M",                                 event_base + 657 },
            { "EVT_CDU_R_N",                                 event_base + 658 },
            { "EVT_CDU_R_O",                                 event_base + 659 },
            { "EVT_CDU_R_P",                                 event_base + 660 },
            { "EVT_CDU_R_Q",                                 event_base + 661 },
            { "EVT_CDU_R_R",                                 event_base + 662 },
            { "EVT_CDU_R_S",                                 event_base + 663 },
            { "EVT_CDU_R_T",                                 event_base + 664 },
            { "EVT_CDU_R_U",                                 event_base + 665 },
            { "EVT_CDU_R_V",                                 event_base + 666 },
            { "EVT_CDU_R_W",                                 event_base + 667 },
            { "EVT_CDU_R_X",                                 event_base + 668 },
            { "EVT_CDU_R_Y",                                 event_base + 669 },
            { "EVT_CDU_R_Z",                                 event_base + 670 },
            { "EVT_CDU_R_SPACE",                             event_base + 671 },
            { "EVT_CDU_R_DEL",                               event_base + 672 },
            { "EVT_CDU_R_SLASH",                             event_base + 673 },
            { "EVT_CDU_R_CLR",                               event_base + 674 },
            { "EVT_CDU_R_BRITENESS",                         event_base + 677 },

            // ===== Fire Protection Panel =====
            { "EVT_FIRE_OVHT_DET_SWITCH_1",                  event_base + 694 },
            { "EVT_FIRE_DETECTION_TEST_SWITCH",              event_base + 696 },
            { "EVT_FIRE_HANDLE_ENGINE_1_TOP",                event_base + 697 },
            { "EVT_FIRE_HANDLE_ENGINE_1_BOTTOM",             event_base + 6971 },
            { "EVT_FIRE_HANDLE_APU_TOP",                     event_base + 698 },
            { "EVT_FIRE_HANDLE_APU_BOTTOM",                  event_base + 6981 },
            { "EVT_FIRE_HANDLE_ENGINE_2_TOP",                event_base + 699 },
            { "EVT_FIRE_HANDLE_ENGINE_2_BOTTOM",             event_base + 6991 },
            { "EVT_FIRE_BELL_CUTOUT_SWITCH",                 event_base + 704 },
            { "EVT_FIRE_OVHT_DET_SWITCH_2",                  event_base + 705 },
            { "EVT_FIRE_EXTINGUISHER_TEST_SWITCH",           event_base + 715 },
            { "EVT_FIRE_UNLOCK_SWITCH_ENGINE_1",             event_base + 976 },
            { "EVT_FIRE_UNLOCK_SWITCH_APU",                  event_base + 977 },
            { "EVT_FIRE_UNLOCK_SWITCH_ENGINE_2",             event_base + 978 },

            // ===== Cargo Fire =====
            { "EVT_CARGO_FIRE_DET_SEL_SWITCH_FWD",           event_base + 760 },
            { "EVT_CARGO_FIRE_DET_SEL_SWITCH_AFT",           event_base + 761 },
            { "EVT_CARGO_FIRE_DET_SEL_SWITCH_MAIN",          event_base + 762 },
            { "EVT_CARGO_FIRE_ARM_SWITCH_FWD",               event_base + 763 },
            { "EVT_CARGO_FIRE_ARM_SWITCH_AFT",               event_base + 765 },
            { "EVT_CARGO_FIRE_ARM_SWITCH_MAIN",              event_base + 7651 },
            { "EVT_CARGO_FIRE_DISC_SWITCH_GUARD",            event_base + 768 },
            { "EVT_CARGO_FIRE_DISC_SWITCH",                  event_base + 767 },
            { "EVT_CARGO_FIRE_TEST_SWITCH",                  event_base + 769 },

            // ===== Lav / Supernumerary Smoke (Freighter 700/800) =====
            { "EVT_CARGO_SMOKE_TEST",                        event_base + 905 },
            { "EVT_CARGO_SMOKE_BELL_CUTOUT",                 event_base + 907 },
            { "EVT_CARGO_SMOKE",                             event_base + 909 },
            { "EVT_CARGO_SMOKE_GUARD",                       event_base + 911 },
            { "EVT_LAV_SMOKE_TEST",                          event_base + 913 },
            { "EVT_LAV_SMOKE_BELL_CUTOUT",                   event_base + 915 },

            // ===== Flight Controls — Pedestal =====
            { "EVT_FCTL_AILERON_TRIM",                       event_base + 810 },
            { "EVT_FCTL_RUDDER_TRIM",                        event_base + 811 },

            // ===== Pedestal Lights =====
            { "EVT_PED_FLOOD_CONTROL",                       event_base + 756 },
            { "EVT_PED_PANEL_CONTROL",                       event_base + 757 },

            // ===== EFB L (Captain) — Hardware Buttons =====
            { "EVT_EFB_L_MENU",                              event_base + 1700 },
            { "EVT_EFB_L_BACK",                              event_base + 1701 },
            { "EVT_EFB_L_PAGE_UP",                           event_base + 1702 },
            { "EVT_EFB_L_PAGE_DOWN",                         event_base + 1703 },
            { "EVT_EFB_L_XFR",                               event_base + 1704 },
            { "EVT_EFB_L_ENTER",                             event_base + 1705 },
            { "EVT_EFB_L_ZOOM_IN",                           event_base + 1706 },
            { "EVT_EFB_L_ZOOM_OUT",                          event_base + 1707 },
            { "EVT_EFB_L_ARROW_UP",                          event_base + 1708 },
            { "EVT_EFB_L_ARROW_DOWN",                        event_base + 1709 },
            { "EVT_EFB_L_ARROW_LEFT",                        event_base + 1710 },
            { "EVT_EFB_L_ARROW_RIGHT",                       event_base + 1711 },
            { "EVT_EFB_L_LSK_1L",                            event_base + 1712 },
            { "EVT_EFB_L_LSK_2L",                            event_base + 1713 },
            { "EVT_EFB_L_LSK_3L",                            event_base + 1714 },
            { "EVT_EFB_L_LSK_4L",                            event_base + 1715 },
            { "EVT_EFB_L_LSK_5L",                            event_base + 1716 },
            { "EVT_EFB_L_LSK_6L",                            event_base + 1717 },
            { "EVT_EFB_L_LSK_7L",                            event_base + 1718 },
            { "EVT_EFB_L_LSK_8L",                            event_base + 1719 },
            { "EVT_EFB_L_LSK_1R",                            event_base + 1720 },
            { "EVT_EFB_L_LSK_2R",                            event_base + 1721 },
            { "EVT_EFB_L_LSK_3R",                            event_base + 1722 },
            { "EVT_EFB_L_LSK_4R",                            event_base + 1723 },
            { "EVT_EFB_L_LSK_5R",                            event_base + 1724 },
            { "EVT_EFB_L_LSK_6R",                            event_base + 1725 },
            { "EVT_EFB_L_LSK_7R",                            event_base + 1726 },
            { "EVT_EFB_L_LSK_8R",                            event_base + 1727 },
            { "EVT_EFB_L_BRIGHT_UP",                         event_base + 1728 },
            { "EVT_EFB_L_BRIGHT_DN",                         event_base + 1729 },
            { "EVT_EFB_L_POWER",                             event_base + 1730 },

            // ===== EFB L (Captain) — On-Screen Keyboard =====
            { "EVT_EFB_L_KEY_A",                             event_base + 1731 },
            { "EVT_EFB_L_KEY_B",                             event_base + 1732 },
            { "EVT_EFB_L_KEY_C",                             event_base + 1733 },
            { "EVT_EFB_L_KEY_D",                             event_base + 1734 },
            { "EVT_EFB_L_KEY_E",                             event_base + 1735 },
            { "EVT_EFB_L_KEY_F",                             event_base + 1736 },
            { "EVT_EFB_L_KEY_G",                             event_base + 1737 },
            { "EVT_EFB_L_KEY_H",                             event_base + 1738 },
            { "EVT_EFB_L_KEY_I",                             event_base + 1739 },
            { "EVT_EFB_L_KEY_J",                             event_base + 1740 },
            { "EVT_EFB_L_KEY_K",                             event_base + 1741 },
            { "EVT_EFB_L_KEY_L",                             event_base + 1742 },
            { "EVT_EFB_L_KEY_M",                             event_base + 1743 },
            { "EVT_EFB_L_KEY_N",                             event_base + 1744 },
            { "EVT_EFB_L_KEY_O",                             event_base + 1745 },
            { "EVT_EFB_L_KEY_P",                             event_base + 1746 },
            { "EVT_EFB_L_KEY_Q",                             event_base + 1747 },
            { "EVT_EFB_L_KEY_R",                             event_base + 1748 },
            { "EVT_EFB_L_KEY_S",                             event_base + 1749 },
            { "EVT_EFB_L_KEY_T",                             event_base + 1750 },
            { "EVT_EFB_L_KEY_U",                             event_base + 1751 },
            { "EVT_EFB_L_KEY_V",                             event_base + 1752 },
            { "EVT_EFB_L_KEY_W",                             event_base + 1753 },
            { "EVT_EFB_L_KEY_X",                             event_base + 1754 },
            { "EVT_EFB_L_KEY_Y",                             event_base + 1755 },
            { "EVT_EFB_L_KEY_Z",                             event_base + 1756 },
            { "EVT_EFB_L_KEY_0",                             event_base + 1757 },
            { "EVT_EFB_L_KEY_1",                             event_base + 1758 },
            { "EVT_EFB_L_KEY_2",                             event_base + 1759 },
            { "EVT_EFB_L_KEY_3",                             event_base + 1760 },
            { "EVT_EFB_L_KEY_4",                             event_base + 1761 },
            { "EVT_EFB_L_KEY_5",                             event_base + 1762 },
            { "EVT_EFB_L_KEY_6",                             event_base + 1763 },
            { "EVT_EFB_L_KEY_7",                             event_base + 1764 },
            { "EVT_EFB_L_KEY_8",                             event_base + 1765 },
            { "EVT_EFB_L_KEY_9",                             event_base + 1766 },
            { "EVT_EFB_L_KEY_SPACE",                         event_base + 1767 },
            { "EVT_EFB_L_KEY_PLUS",                          event_base + 1768 },
            { "EVT_EFB_L_KEY_MINUS",                         event_base + 1769 },
            { "EVT_EFB_L_KEY_DOT",                           event_base + 1770 },
            { "EVT_EFB_L_KEY_SLASH",                         event_base + 1771 },
            { "EVT_EFB_L_KEY_BACKSPACE",                     event_base + 1772 },
            { "EVT_EFB_L_KEY_DEL",                           event_base + 1773 },
            { "EVT_EFB_L_KEY_EQUAL",                         event_base + 1774 },
            { "EVT_EFB_L_KEY_MULTIPLY",                      event_base + 1775 },
            { "EVT_EFB_L_KEY_LEFT_PAR",                      event_base + 1776 },
            { "EVT_EFB_L_KEY_RIGHT_PAR",                     event_base + 1777 },
            { "EVT_EFB_L_KEY_QUEST",                         event_base + 1778 },
            { "EVT_EFB_L_KEY_QUOTE",                         event_base + 1779 },
            { "EVT_EFB_L_KEY_COMMA",                         event_base + 1780 },
            { "EVT_EFB_L_KEY_PAGE_UP",                       event_base + 1781 },
            { "EVT_EFB_L_KEY_PAGE_DOWN",                     event_base + 1782 },
            { "EVT_EFB_L_KEY_ENTER",                         event_base + 1783 },
            { "EVT_EFB_L_KEY_ARROW_UP",                      event_base + 1784 },
            { "EVT_EFB_L_KEY_ARROW_DOWN",                    event_base + 1785 },

            // ===== EFB R (F/O) — Hardware Buttons =====
            // EVT_EFB_R_START = EVT_EFB_L_END + 1 = 1731 + 54 + 1 = 1786
            { "EVT_EFB_R_MENU",                              event_base + 1786 },
            { "EVT_EFB_R_BACK",                              event_base + 1787 },
            { "EVT_EFB_R_PAGE_UP",                           event_base + 1788 },
            { "EVT_EFB_R_PAGE_DOWN",                         event_base + 1789 },
            { "EVT_EFB_R_XFR",                               event_base + 1790 },
            { "EVT_EFB_R_ENTER",                             event_base + 1791 },
            { "EVT_EFB_R_ZOOM_IN",                           event_base + 1792 },
            { "EVT_EFB_R_ZOOM_OUT",                          event_base + 1793 },
            { "EVT_EFB_R_ARROW_UP",                          event_base + 1794 },
            { "EVT_EFB_R_ARROW_DOWN",                        event_base + 1795 },
            { "EVT_EFB_R_ARROW_LEFT",                        event_base + 1796 },
            { "EVT_EFB_R_ARROW_RIGHT",                       event_base + 1797 },
            { "EVT_EFB_R_LSK_1L",                            event_base + 1798 },
            { "EVT_EFB_R_LSK_2L",                            event_base + 1799 },
            { "EVT_EFB_R_LSK_3L",                            event_base + 1800 },
            { "EVT_EFB_R_LSK_4L",                            event_base + 1801 },
            { "EVT_EFB_R_LSK_5L",                            event_base + 1802 },
            { "EVT_EFB_R_LSK_6L",                            event_base + 1803 },
            { "EVT_EFB_R_LSK_7L",                            event_base + 1804 },
            { "EVT_EFB_R_LSK_8L",                            event_base + 1805 },
            { "EVT_EFB_R_LSK_1R",                            event_base + 1806 },
            { "EVT_EFB_R_LSK_2R",                            event_base + 1807 },
            { "EVT_EFB_R_LSK_3R",                            event_base + 1808 },
            { "EVT_EFB_R_LSK_4R",                            event_base + 1809 },
            { "EVT_EFB_R_LSK_5R",                            event_base + 1810 },
            { "EVT_EFB_R_LSK_6R",                            event_base + 1811 },
            { "EVT_EFB_R_LSK_7R",                            event_base + 1812 },
            { "EVT_EFB_R_LSK_8R",                            event_base + 1813 },
            // SDK header has these two pointing to EVT_EFB_L_START offsets (+ 28 / + 29 = 1728 / 1729).
            // Not a typo on our side — that's what the header declares.
            { "EVT_EFB_R_BRIGH_UP",                          event_base + 1728 },
            { "EVT_EFB_R_BRIGHT_DN",                         event_base + 1729 },
            { "EVT_EFB_R_POWER",                             event_base + 1816 },

            // ===== EFB R (F/O) — On-Screen Keyboard =====
            // EVT_EFB_R_KEY_START = EVT_EFB_R_START + 31 = 1786 + 31 = 1817
            { "EVT_EFB_R_KEY_A",                             event_base + 1817 },
            { "EVT_EFB_R_KEY_B",                             event_base + 1818 },
            { "EVT_EFB_R_KEY_C",                             event_base + 1819 },
            { "EVT_EFB_R_KEY_D",                             event_base + 1820 },
            { "EVT_EFB_R_KEY_E",                             event_base + 1821 },
            { "EVT_EFB_R_KEY_F",                             event_base + 1822 },
            { "EVT_EFB_R_KEY_G",                             event_base + 1823 },
            { "EVT_EFB_R_KEY_H",                             event_base + 1824 },
            { "EVT_EFB_R_KEY_I",                             event_base + 1825 },
            { "EVT_EFB_R_KEY_J",                             event_base + 1826 },
            { "EVT_EFB_R_KEY_K",                             event_base + 1827 },
            { "EVT_EFB_R_KEY_L",                             event_base + 1828 },
            { "EVT_EFB_R_KEY_M",                             event_base + 1829 },
            { "EVT_EFB_R_KEY_N",                             event_base + 1830 },
            { "EVT_EFB_R_KEY_O",                             event_base + 1831 },
            { "EVT_EFB_R_KEY_P",                             event_base + 1832 },
            { "EVT_EFB_R_KEY_Q",                             event_base + 1833 },
            { "EVT_EFB_R_KEY_R",                             event_base + 1834 },
            { "EVT_EFB_R_KEY_S",                             event_base + 1835 },
            { "EVT_EFB_R_KEY_T",                             event_base + 1836 },
            { "EVT_EFB_R_KEY_U",                             event_base + 1837 },
            { "EVT_EFB_R_KEY_V",                             event_base + 1838 },
            { "EVT_EFB_R_KEY_W",                             event_base + 1839 },
            { "EVT_EFB_R_KEY_X",                             event_base + 1840 },
            { "EVT_EFB_R_KEY_Y",                             event_base + 1841 },
            { "EVT_EFB_R_KEY_Z",                             event_base + 1842 },
            { "EVT_EFB_R_KEY_0",                             event_base + 1843 },
            { "EVT_EFB_R_KEY_1",                             event_base + 1844 },
            { "EVT_EFB_R_KEY_2",                             event_base + 1845 },
            { "EVT_EFB_R_KEY_3",                             event_base + 1846 },
            { "EVT_EFB_R_KEY_4",                             event_base + 1847 },
            { "EVT_EFB_R_KEY_5",                             event_base + 1848 },
            { "EVT_EFB_R_KEY_6",                             event_base + 1849 },
            { "EVT_EFB_R_KEY_7",                             event_base + 1850 },
            { "EVT_EFB_R_KEY_8",                             event_base + 1851 },
            { "EVT_EFB_R_KEY_9",                             event_base + 1852 },
            { "EVT_EFB_R_KEY_SPACE",                         event_base + 1853 },
            { "EVT_EFB_R_KEY_PLUS",                          event_base + 1854 },
            { "EVT_EFB_R_KEY_MINUS",                         event_base + 1855 },
            { "EVT_EFB_R_KEY_DOT",                           event_base + 1856 },
            { "EVT_EFB_R_KEY_SLASH",                         event_base + 1857 },
            { "EVT_EFB_R_KEY_BACKSPACE",                     event_base + 1858 },
            { "EVT_EFB_R_KEY_DEL",                           event_base + 1859 },
            { "EVT_EFB_R_KEY_EQUAL",                         event_base + 1860 },
            { "EVT_EFB_R_KEY_MULTIPLY",                      event_base + 1861 },
            { "EVT_EFB_R_KEY_LEFT_PAR",                      event_base + 1862 },
            { "EVT_EFB_R_KEY_RIGHT_PAR",                     event_base + 1863 },
            { "EVT_EFB_R_KEY_QUEST",                         event_base + 1864 },
            { "EVT_EFB_R_KEY_QUOTE",                         event_base + 1865 },
            { "EVT_EFB_R_KEY_COMMA",                         event_base + 1866 },
            { "EVT_EFB_R_KEY_PAGE_UP",                       event_base + 1867 },
            { "EVT_EFB_R_KEY_PAGE_DOWN",                     event_base + 1868 },
            { "EVT_EFB_R_KEY_ENTER",                         event_base + 1869 },
            { "EVT_EFB_R_KEY_ARROW_UP",                      event_base + 1870 },
            { "EVT_EFB_R_KEY_ARROW_DOWN",                    event_base + 1871 },

            // ===== EFB — Screen Action =====
            { "EVT_EFB_L_SCREEN_ACTION",                     event_base + 1900 },
            { "EVT_EFB_R_SCREEN_ACTION",                     event_base + 1901 },

            // ===== Various =====
            { "EVT_JUMPSEAT_STOW_EXTEND",                    event_base + 2001 },
            { "EVT_ALT_GEAR_EXT_DOOR",                       event_base + 2002 },
            { "EVT_ALT_GEAR_EXT_HANDLE_RIGHT",               event_base + 2003 },
            { "EVT_ALT_GEAR_EXT_HANDLE_LEFT",                event_base + 2004 },
            { "EVT_ALT_GEAR_EXT_HANDLE_NOSE",                event_base + 2005 },
            { "EVT_COMBINER_COVER",                          event_base + 2006 },
            { "EVT_HIDE_YOKE_CAPT",                          event_base + 2007 },
            { "EVT_HIDE_YOKE_FO",                            event_base + 2008 },
            { "EVT_SPOTLIGHT_L",                             event_base + 2015 },
            { "EVT_SPOTLIGHT_R",                             event_base + 2016 },
            { "EVT_SPOTLIGHT_OBS",                           event_base + 2017 },

            // ===== Grimes Light =====
            { "EVT_GRIMES_LIGHT_CA",                         event_base + 2020 },

            // ===== Yoke Animations =====
            { "EVT_YOKE_L_COUNTER_1",                        event_base + 998 },
            { "EVT_YOKE_L_COUNTER_2",                        event_base + 999 },
            { "EVT_YOKE_L_COUNTER_3",                        event_base + 1000 },
            { "EVT_YOKE_R_COUNTER_1",                        event_base + 1001 },
            { "EVT_YOKE_R_COUNTER_2",                        event_base + 1002 },
            { "EVT_YOKE_R_COUNTER_3",                        event_base + 1003 },
            { "EVT_YOKE_L_AP_DISC_SWITCH",                   event_base + 1004 },
            { "EVT_YOKE_R_AP_DISC_SWITCH",                   event_base + 1005 },
            { "EVT_YOKE_CHECKLIST_L_SWITCH",                 event_base + 7521 },
            { "EVT_YOKE_CHECKLIST_R_SWITCH",                 event_base + 7522 },

            // ===== Captain / F/O Armrests =====
            { "EVT_CA_ARMREST_LEFT_SWITCH",                  event_base + 1006 },
            { "EVT_CA_ARMREST_RIGHT_SWITCH",                 event_base + 1007 },
            { "EVT_FO_ARMREST_LEFT_SWITCH",                  event_base + 1008 },
            { "EVT_FO_ARMREST_RIGHT_SWITCH",                 event_base + 1009 },

            // ===== Custom Shortcuts =====
            { "EVT_LDG_LIGHTS_TOGGLE",                       event_base + 14000 },
            { "EVT_TURNOFF_LIGHTS_TOGGLE",                   event_base + 14001 },
            { "EVT_COCKPIT_LIGHTS_TOGGLE",                   event_base + 14002 },
            { "EVT_COCKPIT_LIGHTS_ON",                       event_base + 14003 },
            { "EVT_COCKPIT_LIGHTS_OFF",                      event_base + 14004 },
            { "EVT_DOOR_FWD_L",                              event_base + 14005 },
            { "EVT_DOOR_FWD_R",                              event_base + 14006 },
            { "EVT_DOOR_AFT_L",                              event_base + 14007 },
            { "EVT_DOOR_AFT_R",                              event_base + 14008 },
            { "EVT_DOOR_OVERWING_EXIT_L",                    event_base + 14009 },
            { "EVT_DOOR_OVERWING_EXIT_R",                    event_base + 14010 },
            { "EVT_DOOR_CARGO_FWD",                          event_base + 14013 },
            { "EVT_DOOR_CARGO_AFT",                          event_base + 14014 },
            { "EVT_DOOR_CARGO_MAIN",                         event_base + 14015 },
            { "EVT_DOOR_EQUIPMENT_HATCH",                    event_base + 14016 },
            { "EVT_DOOR_AIRSTAIR",                           event_base + 14017 },
            { "EVT_LOGO_LIGHTS_TOGGLE",                      event_base + 14018 },

            // ===== MCP Direct Control =====
            { "EVT_MCP_CRS_L_SET",                           event_base + 14500 },
            { "EVT_MCP_CRS_R_SET",                           event_base + 14501 },
            { "EVT_MCP_IAS_SET",                             event_base + 14502 },
            { "EVT_MCP_MACH_SET",                            event_base + 14503 },
            { "EVT_MCP_HDG_SET",                             event_base + 14504 },
            { "EVT_MCP_ALT_SET",                             event_base + 14505 },
            { "EVT_MCP_VS_SET",                              event_base + 14506 },

            // ===== Pressurization Direct Control =====
            { "EVT_OH_PRESS_FLT_ALT_SET",                    event_base + 14507 },
            { "EVT_OH_PRESS_LAND_ALT_SET",                   event_base + 14508 },

            // ===== Panel System Events =====
            // Note: SDK declares EVT_CTRL_ACCELERATION_DISABLE and _ENABLE at the same offset (14600).
            // Both names map to the same numeric ID — the dictionary holds both keys.
            { "EVT_CTRL_ACCELERATION_DISABLE",               event_base + 14600 },
            { "EVT_CTRL_ACCELERATION_ENABLE",                event_base + 14600 },

            // ===== 2D Panel Offset =====
            // Note: NOT relative to event_base — absolute 20000 per SDK header.
            { "EVT_2D_PANEL_OFFSET",                         20000 },
        };

    // =========================================================================
    // Variable → event name mapping (simple toggle and momentary controls)
    //
    // Key format:
    //   - Scalar field "Foo" → key "Foo"
    //   - Array field "Foo[i]" → key "Foo_{i}"
    //
    // Cross-referenced against EventIds at write-time; every event name here
    // must be a key in EventIds.
    // =========================================================================
    private static readonly IReadOnlyDictionary<string, string> _simpleEventMap =
        new Dictionary<string, string>
        {
            // --- Aft overhead: ADIRU (IRS) ---
            ["IRS_DisplaySelector"]        = "EVT_ISDU_DSPL_SEL",
            ["IRS_SysDisplay_R"]           = "EVT_ISDU_SYS_DSPL",
            ["IRS_ModeSelector_0"]         = "EVT_IRU_MSU_LEFT",
            ["IRS_ModeSelector_1"]         = "EVT_IRU_MSU_RIGHT",

            // --- Aft overhead: Service Interphone / Dome ---
            ["COMM_ServiceInterphoneSw"]   = "EVT_OH_SERVICE_INTERPHONE_SWITCH",
            ["LTS_DomeWhiteSw"]            = "EVT_OH_DOME_SWITCH",

            // --- Aft overhead: Engine EEC (guarded — see _guardedMap) ---
            // (no simple entries; ENG_EECSwitch[0/1] is guarded)

            // --- Aft overhead: Oxygen (guarded — see _guardedMap) ---
            // OXY_SwNormal is guarded

            // --- Aft overhead: Flight Recorder (guarded — see _guardedMap) ---
            // FLTREC_SwNormal is guarded

            // --- Forward overhead: Flight Controls ---
            ["FCTL_YawDamper_Sw"]          = "EVT_OH_YAW_DAMPER",
            ["FCTL_AltnFlaps_Control_Sw"]  = "EVT_OH_ALT_FLAPS_POS_SWITCH",
            // FCTL_FltControl_Sw_0/1, FCTL_Spoiler_Sw_0/1, FCTL_AltnFlaps_Sw_ARM
            // are all guarded — see _guardedMap

            // --- Forward overhead: Navigation/Displays selectors ---
            ["NAVDIS_VHFNavSelector"]      = "EVT_OH_NAVDSP_VHF_NAV_SEL",
            ["NAVDIS_IRSSelector"]         = "EVT_OH_NAVDSP_IRS_SEL",
            ["NAVDIS_FMCSelector"]         = "EVT_OH_NAVDSP_FMC_SEL",
            ["NAVDIS_SourceSelector"]      = "EVT_OH_NAVDSP_DISPLAYS_SOURCE_SEL",
            ["NAVDIS_ControlPaneSelector"] = "EVT_OH_NAVDSP_CONTROL_PANEL_SEL",

            // --- Forward overhead: Fuel pumps & crossfeed ---
            ["FUEL_PumpFwdSw_0"]           = "EVT_OH_FUEL_PUMP_1_FORWARD",
            ["FUEL_PumpFwdSw_1"]           = "EVT_OH_FUEL_PUMP_2_FORWARD",
            ["FUEL_PumpAftSw_0"]           = "EVT_OH_FUEL_PUMP_1_AFT",
            ["FUEL_PumpAftSw_1"]           = "EVT_OH_FUEL_PUMP_2_AFT",
            ["FUEL_PumpCtrSw_0"]           = "EVT_OH_FUEL_PUMP_L_CENTER",
            ["FUEL_PumpCtrSw_1"]           = "EVT_OH_FUEL_PUMP_R_CENTER",
            ["FUEL_CrossFeedSw"]           = "EVT_OH_FUEL_CROSSFEED",
            ["FUEL_AuxFwd_0"]              = "EVT_OH_FUEL_AUX_FWD_A",
            ["FUEL_AuxFwd_1"]              = "EVT_OH_FUEL_AUX_FWD_B",
            ["FUEL_AuxAft_0"]              = "EVT_OH_FUEL_AUX_AFT_A",
            ["FUEL_AuxAft_1"]              = "EVT_OH_FUEL_AUX_AFT_B",
            ["FUEL_FWDBleed"]              = "EVT_OH_FUEL_FWD_BLD",
            ["FUEL_AFTBleed"]              = "EVT_OH_FUEL_AFT_BLD",
            // FUEL_GNDXfr is guarded — see _guardedMap

            // --- Forward overhead: Electrical (non-guarded) ---
            ["ELEC_DCMeterSelector"]       = "EVT_OH_ELEC_DC_METER",
            ["ELEC_ACMeterSelector"]       = "EVT_OH_ELEC_AC_METER",
            ["ELEC_CabUtilSw"]             = "EVT_OH_ELEC_CAB_UTIL",
            ["ELEC_IFEPassSeatSw"]         = "EVT_OH_ELEC_IFE",
            ["ELEC_GrdPwrSw"]              = "EVT_OH_ELEC_GRD_PWR_SWITCH",
            ["ELEC_GenSw_0"]               = "EVT_OH_ELEC_GEN1_SWITCH",
            ["ELEC_GenSw_1"]               = "EVT_OH_ELEC_GEN2_SWITCH",
            ["ELEC_APUGenSw_0"]            = "EVT_OH_ELEC_APU_GEN1_SWITCH",
            ["ELEC_APUGenSw_1"]            = "EVT_OH_ELEC_APU_GEN2_SWITCH",
            // ELEC_BatSelector, ELEC_StandbyPowerSelector, ELEC_BusTransSw_AUTO,
            // ELEC_IDGDisconnectSw_0/1 are guarded — see _guardedMap.

            // --- Forward overhead: Wipers ---
            ["OH_WiperLSelector"]          = "EVT_OH_WIPER_LEFT_CONTROL",
            ["OH_WiperRSelector"]          = "EVT_OH_WIPER_RIGHT_CONTROL",

            // --- Center overhead: Equipment cooling / pax signs ---
            ["AIR_EquipCoolingSupplyNORM"] = "EVT_OH_EC_SUPPLY_SWITCH",
            ["AIR_EquipCoolingExhaustNORM"]= "EVT_OH_EC_EXHAUST_SWITCH",
            ["COMM_NoSmokingSelector"]     = "EVT_OH_NO_SMOKING_LIGHT_SWITCH",
            ["COMM_FastenBeltsSelector"]   = "EVT_OH_FASTEN_BELTS_LIGHT_SWITCH",
            // LTS_EmerExitSelector is guarded — see _guardedMap

            // --- Anti-ice ---
            ["ICE_WindowHeatSw_0"]         = "EVT_OH_ICE_WINDOW_HEAT_1",
            ["ICE_WindowHeatSw_1"]         = "EVT_OH_ICE_WINDOW_HEAT_2",
            ["ICE_WindowHeatSw_2"]         = "EVT_OH_ICE_WINDOW_HEAT_3",
            ["ICE_WindowHeatSw_3"]         = "EVT_OH_ICE_WINDOW_HEAT_4",
            ["ICE_WindowHeatTestSw"]       = "EVT_OH_ICE_WINDOW_HEAT_TEST",
            ["ICE_ProbeHeatSw_0"]          = "EVT_OH_ICE_PROBE_HEAT_1",
            ["ICE_ProbeHeatSw_1"]          = "EVT_OH_ICE_PROBE_HEAT_2",
            ["ICE_WingAntiIceSw"]          = "EVT_OH_ICE_WING_ANTIICE",
            ["ICE_EngAntiIceSw_0"]         = "EVT_OH_ICE_ENGINE_ANTIICE_1",
            ["ICE_EngAntiIceSw_1"]         = "EVT_OH_ICE_ENGINE_ANTIICE_2",

            // --- Hydraulics ---
            ["HYD_PumpSw_eng_0"]           = "EVT_OH_HYD_ENG1",
            ["HYD_PumpSw_eng_1"]           = "EVT_OH_HYD_ENG2",
            ["HYD_PumpSw_elec_0"]          = "EVT_OH_HYD_ELEC1",
            ["HYD_PumpSw_elec_1"]          = "EVT_OH_HYD_ELEC2",

            // --- Air conditioning / bleed / pack ---
            ["AIR_TempSourceSelector"]     = "EVT_OH_AIRCOND_TEMP_SOURCE_SELECTOR_800",
            ["AIR_TrimAirSwitch"]          = "EVT_OH_AIRCOND_TRIM_AIR_SWITCH_800",
            ["AIR_RecircFanSwitch_0"]      = "EVT_OH_BLEED_RECIRC_FAN_L_SWITCH",
            ["AIR_RecircFanSwitch_1"]      = "EVT_OH_BLEED_RECIRC_FAN_R_SWITCH",
            ["AIR_PackSwitch_0"]           = "EVT_OH_BLEED_PACK_L_SWITCH",
            ["AIR_PackSwitch_1"]           = "EVT_OH_BLEED_PACK_R_SWITCH",
            ["AIR_BleedAirSwitch_0"]       = "EVT_OH_BLEED_ENG_1_SWITCH",
            ["AIR_BleedAirSwitch_1"]       = "EVT_OH_BLEED_ENG_2_SWITCH",
            ["AIR_APUBleedAirSwitch"]      = "EVT_OH_BLEED_APU_SWITCH",
            ["AIR_IsolationValveSwitch"]   = "EVT_OH_BLEED_ISOLATION_VALVE_SWITCH",
            ["AIR_OutflowValveSwitch"]     = "EVT_OH_PRESS_VALVE_SWITCH",
            ["AIR_PressurizationModeSelector"] = "EVT_OH_PRESS_SELECTOR",

            // --- Bottom overhead: Lights ---
            ["LTS_LandingLtRetractableSw_0"] = "EVT_OH_LIGHTS_L_RETRACT",
            ["LTS_LandingLtRetractableSw_1"] = "EVT_OH_LIGHTS_R_RETRACT",
            ["LTS_LandingLtFixedSw_0"]     = "EVT_OH_LIGHTS_L_FIXED",
            ["LTS_LandingLtFixedSw_1"]     = "EVT_OH_LIGHTS_R_FIXED",
            ["LTS_RunwayTurnoffSw_0"]      = "EVT_OH_LIGHTS_L_TURNOFF",
            ["LTS_RunwayTurnoffSw_1"]      = "EVT_OH_LIGHTS_R_TURNOFF",
            ["LTS_TaxiSw"]                 = "EVT_OH_LIGHTS_TAXI",
            ["LTS_LogoSw"]                 = "EVT_OH_LIGHTS_LOGO",
            ["LTS_PositionSw"]             = "EVT_OH_LIGHTS_POS_STROBE",
            ["LTS_AntiCollisionSw"]        = "EVT_OH_LIGHTS_ANT_COL",
            ["LTS_WingSw"]                 = "EVT_OH_LIGHTS_WING",
            ["LTS_WheelWellSw"]            = "EVT_OH_LIGHTS_WHEEL_WELL",

            // --- Bottom overhead: Engine start / APU / ignition ---
            ["APU_Selector"]               = "EVT_OH_LIGHTS_APU_START",
            ["ENG_StartSelector_0"]        = "EVT_OH_LIGHTS_L_ENGINE_START",
            ["ENG_StartSelector_1"]        = "EVT_OH_LIGHTS_R_ENGINE_START",
            ["ENG_IgnitionSelector"]       = "EVT_OH_LIGHTS_IGN_SEL",

            // --- Glareshield: EFIS Captain ---
            ["EFIS_MinsSelBARO_0"]         = "EVT_EFIS_CPT_MINIMUMS_RADIO_BARO",
            ["EFIS_BaroSelHPA_0"]          = "EVT_EFIS_CPT_BARO_IN_HPA",
            ["EFIS_VORADFSel1_0"]          = "EVT_EFIS_CPT_VOR_ADF_SELECTOR_L",
            ["EFIS_VORADFSel2_0"]          = "EVT_EFIS_CPT_VOR_ADF_SELECTOR_R",
            ["EFIS_ModeSel_0"]             = "EVT_EFIS_CPT_MODE",
            ["EFIS_RangeSel_0"]            = "EVT_EFIS_CPT_RANGE",

            // --- Glareshield: EFIS First Officer ---
            ["EFIS_MinsSelBARO_1"]         = "EVT_EFIS_FO_MINIMUMS_RADIO_BARO",
            ["EFIS_BaroSelHPA_1"]          = "EVT_EFIS_FO_BARO_IN_HPA",
            ["EFIS_VORADFSel1_1"]          = "EVT_EFIS_FO_VOR_ADF_SELECTOR_L",
            ["EFIS_VORADFSel2_1"]          = "EVT_EFIS_FO_VOR_ADF_SELECTOR_R",
            ["EFIS_ModeSel_1"]             = "EVT_EFIS_FO_MODE",
            ["EFIS_RangeSel_1"]            = "EVT_EFIS_FO_RANGE",

            // --- Glareshield: MCP (non-knob switches) ---
            ["MCP_FDSw_0"]                 = "EVT_MCP_FD_SWITCH_L",
            ["MCP_FDSw_1"]                 = "EVT_MCP_FD_SWITCH_R",
            ["MCP_ATArmSw"]                = "EVT_MCP_AT_ARM_SWITCH",
            ["MCP_BankLimitSel"]           = "EVT_MCP_BANK_ANGLE_SELECTOR",
            ["MCP_DisengageBar"]           = "EVT_MCP_DISENGAGE_BAR",

            // --- Forward panel: NWS / displays / disengage test / lights ---
            // MAIN_NoseWheelSteeringSwNORM is guarded — see _guardedMap
            ["MAIN_MainPanelDUSel_0"]      = "EVT_DSP_CPT_MAIN_DU_SELECTOR",
            ["MAIN_MainPanelDUSel_1"]      = "EVT_DSP_FO_MAIN_DU_SELECTOR",
            ["MAIN_LowerDUSel_0"]          = "EVT_DSP_CPT_LOWER_DU_SELECTOR",
            ["MAIN_LowerDUSel_1"]          = "EVT_DSP_FO_LOWER_DU_SELECTOR",
            ["MAIN_DisengageTestSelector_0"] = "EVT_DSP_CPT_DISENGAGE_TEST_SWITCH",
            ["MAIN_DisengageTestSelector_1"] = "EVT_DSP_FO_DISENGAGE_TEST_SWITCH",
            ["MAIN_LightsSelector"]        = "EVT_DSP_CPT_MASTER_LIGHTS_SWITCH",
            ["MAIN_annunBELOW_GS_0"]       = "EVT_DSP_CPT_BELOW_GS_INHIBIT_SWITCH",
            ["MAIN_annunBELOW_GS_1"]       = "EVT_DSP_FO_BELOW_GS_INHIBIT_SWITCH",

            // --- Forward panel: RMI / autobrake / spd ref / N1 set / fuel flow ---
            ["MAIN_RMISelector1_VOR"]      = "EVT_RMI_LEFT_SELECTOR",
            ["MAIN_RMISelector2_VOR"]      = "EVT_RMI_RIGHT_SELECTOR",
            ["MAIN_AutobrakeSelector"]     = "EVT_MPM_AUTOBRAKE_SELECTOR",
            ["MAIN_SpdRefSelector"]        = "EVT_MPM_SPEED_REFERENCE_SELECTOR",
            ["MAIN_N1SetSelector"]         = "EVT_MPM_N1SET_SELECTOR",
            ["MAIN_FuelFlowSelector"]      = "EVT_MPM_FUEL_FLOW_SWITCH",
            ["MAIN_GearLever"]             = "EVT_GEAR_LEVER",

            // --- Lower forward panel: GPWS (all guarded — see _guardedMap) ---
            // GPWS_FlapInhibitSw_NORM, GPWS_GearInhibitSw_NORM, GPWS_TerrInhibitSw_NORM
            // are all guarded.

            // --- Control Stand: stab trim / parking / fire / xpdr ---
            ["TRIM_StabTrimMainElecSw_NORMAL"] = "EVT_CONTROL_STAND_STABTRIM_ELEC_SWITCH",
            ["TRIM_StabTrimAutoPilotSw_NORMAL"] = "EVT_CONTROL_STAND_STABTRIM_AP_SWITCH",
            ["TRIM_StabTrimSw_NORMAL"]     = "EVT_STAB_TRIM_OVRD_SWITCH",
            ["FIRE_OvhtDetSw_0"]           = "EVT_FIRE_OVHT_DET_SWITCH_1",
            ["FIRE_OvhtDetSw_1"]           = "EVT_FIRE_OVHT_DET_SWITCH_2",
            ["FIRE_DetTestSw"]             = "EVT_FIRE_DETECTION_TEST_SWITCH",
            ["FIRE_ExtinguisherTestSw"]    = "EVT_FIRE_EXTINGUISHER_TEST_SWITCH",
            ["CARGO_DetSelect_0"]          = "EVT_CARGO_FIRE_DET_SEL_SWITCH_FWD",
            ["CARGO_DetSelect_1"]          = "EVT_CARGO_FIRE_DET_SEL_SWITCH_AFT",
            // CARGO_ArmedSw_0/1 are guarded — see _guardedMap
            ["XPDR_XpndrSelector_2"]       = "EVT_TCAS_XPNDR",
            ["XPDR_AltSourceSel_2"]        = "EVT_TCAS_ALTSOURCE",
            ["XPDR_ModeSel"]               = "EVT_TCAS_MODE",
            ["PED_FltDkDoorSel"]           = "EVT_FLT_DK_DOOR_KNOB",
        };

    // =========================================================================
    // Guarded switch table: varKey → (guardEvent, switchEvent)
    //
    // The UI layer fires the guard event first (open the guard), then the
    // switch event (move the switch), then the guard event again (close the
    // guard) when toggling a guarded switch. This dict supplies the two event
    // names for each guarded variable.
    //
    // Fire handles (FIRE_HandlePos[3]) are NOT here — they are 5-state and
    // handled with a dedicated branch in HandleUIVariableSet (Task C10).
    // =========================================================================
    private static readonly IReadOnlyDictionary<string, (string Guard, string Switch)> _guardedMap =
        new Dictionary<string, (string, string)>
        {
            // --- Electrical ---
            ["ELEC_BatSelector"]           = ("EVT_OH_ELEC_BATTERY_GUARD",      "EVT_OH_ELEC_BATTERY_SWITCH"),
            ["ELEC_IDGDisconnectSw_0"]     = ("EVT_OH_ELEC_DISCONNECT_1_GUARD", "EVT_OH_ELEC_DISCONNECT_1_SWITCH"),
            ["ELEC_IDGDisconnectSw_1"]     = ("EVT_OH_ELEC_DISCONNECT_2_GUARD", "EVT_OH_ELEC_DISCONNECT_2_SWITCH"),
            ["ELEC_StandbyPowerSelector"]  = ("EVT_OH_ELEC_STBY_PWR_GUARD",     "EVT_OH_ELEC_STBY_PWR_SWITCH"),
            ["ELEC_BusTransSw_AUTO"]       = ("EVT_OH_ELEC_BUS_TRANSFER_GUARD", "EVT_OH_ELEC_BUS_TRANSFER_SWITCH"),

            // --- Fuel ---
            ["FUEL_GNDXfr"]                = ("EVT_OH_FUEL_GND_XFR_GUARD",      "EVT_OH_FUEL_GND_XFR_SW"),

            // --- Flight Controls ---
            ["FCTL_FltControl_Sw_0"]       = ("EVT_OH_FCTL_A_GUARD",            "EVT_OH_FCTL_A_SWITCH"),
            ["FCTL_FltControl_Sw_1"]       = ("EVT_OH_FCTL_B_GUARD",            "EVT_OH_FCTL_B_SWITCH"),
            ["FCTL_Spoiler_Sw_0"]          = ("EVT_OH_SPOILER_A_GUARD",         "EVT_OH_SPOILER_A_SWITCH"),
            ["FCTL_Spoiler_Sw_1"]          = ("EVT_OH_SPOILER_B_GUARD",         "EVT_OH_SPOILER_B_SWITCH"),
            ["FCTL_AltnFlaps_Sw_ARM"]      = ("EVT_OH_ALT_FLAPS_MASTER_GUARD",  "EVT_OH_ALT_FLAPS_MASTER_SWITCH"),

            // --- Engine EEC ---
            ["ENG_EECSwitch_0"]            = ("EVT_OH_EEC_L_GUARD",             "EVT_OH_EEC_L_SWITCH"),
            ["ENG_EECSwitch_1"]            = ("EVT_OH_EEC_R_GUARD",             "EVT_OH_EEC_R_SWITCH"),

            // --- Oxygen / Flight Recorder / Emergency exit lights ---
            ["OXY_SwNormal"]               = ("EVT_OH_OXY_PASS_GUARD",          "EVT_OH_OXY_PASS_SWITCH"),
            ["FLTREC_SwNormal"]            = ("EVT_OH_FLTREC_GUARD",            "EVT_OH_FLTREC_SWITCH"),
            ["LTS_EmerExitSelector"]       = ("EVT_OH_EMER_EXIT_LIGHT_GUARD",   "EVT_OH_EMER_EXIT_LIGHT_SWITCH"),

            // --- GPWS inhibits ---
            ["GPWS_FlapInhibitSw_NORM"]    = ("EVT_GPWS_FLAP_INHIBIT_GUARD",    "EVT_GPWS_FLAP_INHIBIT_SWITCH"),
            ["GPWS_GearInhibitSw_NORM"]    = ("EVT_GPWS_GEAR_INHIBIT_GUARD",    "EVT_GPWS_GEAR_INHIBIT_SWITCH"),
            ["GPWS_TerrInhibitSw_NORM"]    = ("EVT_GPWS_TERR_INHIBIT_GUARD",    "EVT_GPWS_TERR_INHIBIT_SWITCH"),

            // --- Nose wheel steering ---
            ["MAIN_NoseWheelSteeringSwNORM"] = ("EVT_NOSE_WHEEL_STEERING_SWITCH_GUARD", "EVT_NOSE_WHEEL_STEERING_SWITCH"),

            // --- Cargo fire arm switches ---
            // EVT_CARGO_FIRE_DISC_SWITCH_GUARD covers the discharge switch — the
            // arm switches themselves don't have explicit guard events in the SDK,
            // so they're treated as simple switches (see _simpleEventMap above
            // for the DET sel; the arm switches go here only if guarded). The
            // 737 cargo fire arm switches sit under the cargo fire disch guard
            // — model them as guarded by the disch guard for parity with the
            // pilot mental model (the guard physically covers the entire
            // arm/disch group). If this turns out to be wrong in-sim, the entry
            // can move down to _simpleEventMap.
            ["CARGO_ArmedSw_0"]            = ("EVT_CARGO_FIRE_DISC_SWITCH_GUARD", "EVT_CARGO_FIRE_ARM_SWITCH_FWD"),
            ["CARGO_ArmedSw_1"]            = ("EVT_CARGO_FIRE_DISC_SWITCH_GUARD", "EVT_CARGO_FIRE_ARM_SWITCH_AFT"),
        };

    // =========================================================================
    // Behavior overrides — scaffold (populated in Tasks C10–C12)
    // =========================================================================

    public override bool HandleUIVariableSet(
        string varKey, double value,
        SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        // Populated in Task C10
        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
    }

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        // Populated in Task C11
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    public override bool HandleHotkeyAction(
        HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm,
        HotkeyManager hotkeyManager)
    {
        // Populated in Task C12
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    // RequestFCUHeading / RequestFCUSpeed / RequestFCUAltitude /
    // RequestFCUVerticalSpeed overrides are added in Task C13.
}
