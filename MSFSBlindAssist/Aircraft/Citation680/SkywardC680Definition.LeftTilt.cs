using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// Left Tilt Panel: Electrical, APU, Engine Start, Anti-Ice, Exterior Lights, Interior Lighting.
///
/// The vendor checklist's own words name every control (BATT buttons, STBY PWR switch, ELEC
/// NORM/EMER, TRU, GEN, EXT PWR, APU GEN, BUS TIE, INTERIOR, EMER LTS; APU knob OFF/ON/START,
/// APU BLEED AIR, MAX COOL; ENGINE STARTER, STARTER DISENG, RUN/STOP; PITOT/STATIC, the ANTI-ICE
/// buttons; LDG, TAXI, RECOG, PULSE, ANTI COLL).
///
/// MEASURED 2026-09-09 (cold and dark at the gate): the BATT buttons are the vendor L:vars
/// SW_SOV_ELEC_BATT_1/2 — the plugin's own electrical model closes the contactors from them, and
/// the stock B:ELECTRICAL_Battery_n events set the stock master flag WITHOUT powering the buses
/// (avionics stayed dark until the L:vars were written). The APU knob is the stock template's
/// XMLVAR_APU_StarterKnob_Pos (0 Off, 1 On, 2 Start) plus K:APU_STARTER for the start pulse:
/// knob 1, then 2 + APU_STARTER, back to 1 — RPM 17 → 63 → 100 within 15 s.
/// </summary>
public partial class SkywardC680Definition
{
    private static Dictionary<string, SimVarDefinition> BuildLeftTiltVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---- Electrical
        AddSwitch(v, "C680_BATT_L", "SW_SOV_ELEC_BATT_1", "Left BATT Button");
        AddSwitch(v, "C680_BATT_R", "SW_SOV_ELEC_BATT_2", "Right BATT Button");
        AddSimSwitch(v, "C680_STBY_PWR", "ELECTRICAL MASTER BATTERY:3", "STBY PWR Switch",
            help: "The standby display battery. On before the APU or an engine start; verify the standby instrument initialises.");
        AddSwitch(v, "C680_AVN_L", "SW_SOV_ELEC_AVN_1", "Left AVN Button");
        AddSwitch(v, "C680_AVN_R", "SW_SOV_ELEC_AVN_2", "Right AVN Button");
        AddSwitch(v, "C680_ELEC_L", "SW_SOV_ELEC_ELEC_L", "Left ELEC Button", "Emer", "Norm");
        AddSwitch(v, "C680_ELEC_R", "SW_SOV_ELEC_ELEC_R", "Right ELEC Button", "Emer", "Norm");
        AddSwitch(v, "C680_TRU_L", "SW_SOV_ELEC_TRU_1", "Left TRU Button");
        AddSwitch(v, "C680_TRU_R", "SW_SOV_ELEC_TRU_2", "Right TRU Button");
        AddSimSwitch(v, "C680_GEN_L", "LINE CONNECTION ON:409", "Left GEN Switch", help: "Reads back the generator's connection to its bus, so it shows On only once the generator is on line. Off then On again is the reset.");
        AddSimSwitch(v, "C680_GEN_R", "LINE CONNECTION ON:86", "Right GEN Switch", help: "Reads back the generator's connection to its bus, so it shows On only once the generator is on line. Off then On again is the reset.");
        AddButton(v, "C680_EXT_PWR", "EXT PWR Button", "Connects or disconnects external power. Needs the GPU cart placed (Ground Equipment) and its door open.");
        AddSimSwitch(v, "C680_APU_GEN", "LINE CONNECTION ON:639", "APU GEN Switch",
            help: "Reads back the APU generator's bus connection. The aircraft puts it on line only with the APU at 100 percent, the left generator off line and no engine start in progress.");
        AddButton(v, "C680_BUS_TIE", "BUS TIE Button", "The aircraft only answers this button airborne; on the ground the bus tie is automatic.");
        AddSwitch(v, "C680_INTERIOR", "SW_SOV_ELEC_CABIN_PWR", "INTERIOR Button");
        AddSelector(v, "C680_EMER_LTS", "SW_SOV_SAFETY_LIGHTS_POSITION", "EMER LTS Switch", new[] { "Off", "Arm", "On" });
        AddSimReadout(v, "C680_BATT_L_V", "ELECTRICAL BATTERY VOLTAGE:1", "Left Battery Volts", "volts", "F1");
        AddSimReadout(v, "C680_BATT_R_V", "ELECTRICAL BATTERY VOLTAGE:2", "Right Battery Volts", "volts", "F1");
        AddSimReadout(v, "C680_BATT_L_A", "ELECTRICAL BATTERY LOAD:1", "Left Battery Amps", "amperes", "F0");
        AddSimReadout(v, "C680_BATT_R_A", "ELECTRICAL BATTERY LOAD:2", "Right Battery Amps", "amperes", "F0");
        AddSimReadout(v, "C680_GEN_L_V", "ELECTRICAL GENERATOR VOLTAGE:1", "Left Generator Volts", "volts", "F1");
        AddSimReadout(v, "C680_GEN_R_V", "ELECTRICAL GENERATOR VOLTAGE:2", "Right Generator Volts", "volts", "F1");
        AddSimReadout(v, "C680_GEN_L_A", "ELECTRICAL GENERATOR AMPS:1", "Left Generator Amps", "amperes", "F0");
        AddSimReadout(v, "C680_GEN_R_A", "ELECTRICAL GENERATOR AMPS:2", "Right Generator Amps", "amperes", "F0");
        AddFlag(v, "C680_GEN_L_ON", "LINE CONNECTION ON:409", "Left Generator", "Off line", "On line", simvar: true);
        AddFlag(v, "C680_GEN_R_ON", "LINE CONNECTION ON:86", "Right Generator", "Off line", "On line", simvar: true);
        AddFlag(v, "C680_EXT_PWR_ON", "LINE CONNECTION ON:637", "External Power", "Disconnected", "Connected", simvar: true);
        AddReadout(v, "C680_EXT_PWR_V", "SW_SOV_EXT_POWER_VOLTS", "External Power Volts", "number", "F1");
        AddFlag(v, "C680_EXT_PWR_AVAIL", "SW_SOV_EXT_GEN_AVAIL", "External Power Available", "No", "Yes");
        AddFlag(v, "C680_BUS_TIE_CONN", "LINE CONNECTION ON:473", "Bus Tie", "Open", "Closed", simvar: true);
        AddFlag(v, "C680_AVN_POWER", "SW_SOV_AVIONICS_POWER_ACTIVE", "Avionics Power", "Off", "Active");
        AddFlag(v, "C680_STBY_LED_G", "STBY_PWR_LED_GREEN", "Standby Power Green LED", "Off", "On");
        AddFlag(v, "C680_STBY_LED_A", "STBY_PWR_LED_AMBER", "Standby Power Amber LED", "Off", "On");
        AddSimReadout(v, "C680_APU_V", "ELECTRICAL GENERATOR VOLTAGE:3", "APU Generator Volts", "volts", "F1");
        AddSimReadout(v, "C680_APU_A", "ELECTRICAL GENERATOR AMPS:3", "APU Generator Amps", "amperes", "F0");

        // ---- APU
        AddSelector(v, "C680_APU_KNOB", "XMLVAR_APU_StarterKnob_Pos", "APU Knob", new[] { "Off", "On", "Start" },
            "Start is momentary: the knob returns to On by itself after a second. Monitor RPM to 100 percent, then APU GEN on.");
        AddSwitch(v, "C680_APU_BLEED", "SW_SOV_APU_BLEED", "APU BLEED AIR Button");
        AddSwitch(v, "C680_MAX_COOL", "SW_SOV_MAX_COOL", "MAX COOL Button");
        AddSimReadout(v, "C680_APU_RPM", "APU PCT RPM", "APU RPM", "percent", "F0");
        AddSimReadout(v, "C680_APU_EGT", "APU EGT", "APU EGT", "celsius", "F0");
        AddFlag(v, "C680_APU_COMB", "GENERAL_APU_COMBUSTION", "APU", "Not running", "Running");
        AddFlag(v, "C680_APU_GEN_ON", "LINE CONNECTION ON:639", "APU Generator", "Off line", "On line", simvar: true);
        AddFlag(v, "C680_APU_FIRE", "SW_SOV_APU_FIRE_LIGHT", "APU FIRE Light", "Out", "Lit");
        AddFlag(v, "C680_APU_BLEED_ON", "ELECTRICAL_APU_Bleed", "APU Bleed", "Closed", "Open");

        // ---- Engine Start
        AddButton(v, "C680_STARTER_L", "Left ENGINE STARTER Button", "Cranks the left engine; at 9 percent N2 press the left RUN/STOP to Run. The starter drops out itself.");
        AddButton(v, "C680_STARTER_R", "Right ENGINE STARTER Button", "Cranks the right engine; at 9 percent N2 press the right RUN/STOP to Run. The starter drops out itself.");
        AddButton(v, "C680_STARTER_DISENG", "STARTER DISENG Button");
        AddSimSwitch(v, "C680_RUN_L", "GENERAL ENG FUEL VALVE:1", "Left RUN/STOP Button", "Stop", "Run");
        AddSimSwitch(v, "C680_RUN_R", "GENERAL ENG FUEL VALVE:2", "Right RUN/STOP Button", "Stop", "Run");
        AddButton(v, "C680_FADEC_RESET_L", "Left FADEC RESET Button");
        AddButton(v, "C680_FADEC_RESET_R", "Right FADEC RESET Button");
        AddSwitch(v, "C680_TR_STOW_L", "SW_SOV_TR_EMER_STOW_L", "Left T/R EMER STOW Button");
        AddSwitch(v, "C680_TR_STOW_R", "SW_SOV_TR_EMER_STOW_R", "Right T/R EMER STOW Button");
        AddFlag(v, "C680_STARTER_L_ON", "GENERAL ENG STARTER:1", "Left Starter", "Off", "Cranking", simvar: true);
        AddFlag(v, "C680_STARTER_R_ON", "GENERAL ENG STARTER:2", "Right Starter", "Off", "Cranking", simvar: true);
        AddFlag(v, "C680_COMB_L", "GENERAL ENG COMBUSTION:1", "Left Engine", "Not running", "Running", simvar: true);
        AddFlag(v, "C680_COMB_R", "GENERAL ENG COMBUSTION:2", "Right Engine", "Not running", "Running", simvar: true);
        AddSimReadout(v, "C680_N1_L", "TURB ENG N1:1", "Left N1", "percent", "F1");
        AddSimReadout(v, "C680_N1_R", "TURB ENG N1:2", "Right N1", "percent", "F1");
        AddSimReadout(v, "C680_N2_L", "TURB ENG N2:1", "Left N2", "percent", "F1");
        AddSimReadout(v, "C680_N2_R", "TURB ENG N2:2", "Right N2", "percent", "F1");
        AddSimReadout(v, "C680_ITT_L", "TURB ENG ITT:1", "Left ITT", "celsius", "F0");
        AddSimReadout(v, "C680_ITT_R", "TURB ENG ITT:2", "Right ITT", "celsius", "F0");
        AddSimReadout(v, "C680_FF_L", "TURB ENG FUEL FLOW PPH:1", "Left Fuel Flow", "pounds per hour", "F0");
        AddSimReadout(v, "C680_FF_R", "TURB ENG FUEL FLOW PPH:2", "Right Fuel Flow", "pounds per hour", "F0");
        AddSimReadout(v, "C680_OIL_P_L", "GENERAL ENG OIL PRESSURE:1", "Left Oil Pressure", "psi", "F0");
        AddSimReadout(v, "C680_OIL_P_R", "GENERAL ENG OIL PRESSURE:2", "Right Oil Pressure", "psi", "F0");
        AddSimReadout(v, "C680_OIL_T_L", "GENERAL ENG OIL TEMPERATURE:1", "Left Oil Temperature", "celsius", "F0");
        AddSimReadout(v, "C680_OIL_T_R", "GENERAL ENG OIL TEMPERATURE:2", "Right Oil Temperature", "celsius", "F0");
        AddReadout(v, "C680_FADEC_TGT_L", "FADEC_TGT_N1_1", "Left FADEC Target N1", "number", "F1");
        AddReadout(v, "C680_FADEC_TGT_R", "FADEC_TGT_N1_2", "Right FADEC Target N1", "number", "F1");
        AddReadout(v, "C680_FADEC_MAX_L", "FADEC_MAX_N1_1", "Left FADEC Max N1", "number", "F1");
        AddReadout(v, "C680_FADEC_MAX_R", "FADEC_MAX_N1_2", "Right FADEC Max N1", "number", "F1");
        AddFlag(v, "C680_ENG_RUN_L", "SW_SOV_ENGINE_RUN:1", "Left Engine Run Logic", "Stopped", "Run");
        AddFlag(v, "C680_ENG_RUN_R", "SW_SOV_ENGINE_RUN:2", "Right Engine Run Logic", "Stopped", "Run");

        // ---- Anti-Ice
        AddSwitch(v, "C680_PITOT_L", "SW_SOV_AI_PITOT_L", "Left PITOT/STATIC Button");
        AddSwitch(v, "C680_PITOT_R", "SW_SOV_AI_PITOT_R", "Right PITOT/STATIC Button");
        AddSwitch(v, "C680_AI_ENG_L", "SW_SOV_AI_ENG_STAB_L", "Left ENG/STAB Anti-Ice Button");
        AddSwitch(v, "C680_AI_ENG_R", "SW_SOV_AI_ENG_STAB_R", "Right ENG/STAB Anti-Ice Button");
        AddSwitch(v, "C680_AI_WING_L", "SW_SOV_AI_WING_L", "Left WING Anti-Ice Button");
        AddSwitch(v, "C680_AI_WING_R", "SW_SOV_AI_WING_R", "Right WING Anti-Ice Button");
        AddSwitch(v, "C680_WING_XFLOW", "SW_SOV_WING_XFLOW", "WING XFLOW Button");
        AddSwitch(v, "C680_WS_FAN", "SW_SOV_AI_WS_FAN", "Windshield Fan Button");
        AddFlag(v, "C680_PITOT_HEAT_L", "PITOT HEAT SWITCH:1", "Left Pitot Heat", "Off", "On", simvar: true);
        AddFlag(v, "C680_PITOT_HEAT_R", "PITOT HEAT SWITCH:2", "Right Pitot Heat", "Off", "On", simvar: true);
        AddFlag(v, "C680_ENG_AI_L", "ENG ANTI ICE:1", "Left Engine Anti-Ice", "Off", "On", simvar: true);
        AddFlag(v, "C680_ENG_AI_R", "ENG ANTI ICE:2", "Right Engine Anti-Ice", "Off", "On", simvar: true);
        AddFlag(v, "C680_STRUCT_AI", "STRUCTURAL DEICE SWITCH", "Wing Anti-Ice System", "Off", "On", simvar: true);
        AddReadout(v, "C680_AI_BOOST_L", "SW_SOV_AI_N1_BOOST_1", "Left Anti-Ice N1 Boost", "number", "F2");
        AddReadout(v, "C680_AI_BOOST_R", "SW_SOV_AI_N1_BOOST_2", "Right Anti-Ice N1 Boost", "number", "F2");
        AddSimReadout(v, "C680_OAT", "AMBIENT TEMPERATURE", "Outside Air Temperature", "celsius", "F0");

        // ---- Exterior Lights
        AddSwitch(v, "C680_LDG_L", "SW_SOV_LIGHTS_LANDING_1", "Left LDG Light");
        AddSwitch(v, "C680_LDG_R", "SW_SOV_LIGHTS_LANDING_2", "Right LDG Light");
        AddSwitch(v, "C680_TAXI", "SW_SOV_LIGHTS_TAXI", "TAXI Light");
        AddSwitch(v, "C680_RECOG", "SW_SOV_LIGHTS_RECOG", "RECOG Light");
        AddSwitch(v, "C680_PULSE", "SW_SOV_LIGHTS_PULSE", "PULSE Light");
        AddSwitch(v, "C680_ANTI_COLL", "SW_SOV_LIGHTS_STROBE", "ANTI COLL Light");
        AddSwitch(v, "C680_WING_LT", "SW_SOV_LIGHTS_WING", "WING Inspection Light");
        AddSwitch(v, "C680_LOGO", "SW_SOV_LIGHTS_LOGO", "LOGO Light");
        AddSimSwitch(v, "C680_NAV_LT", "LIGHT NAV", "NAV Light");
        AddSimSwitch(v, "C680_BEACON", "LIGHT BEACON", "BEACON Light");
        AddSwitch(v, "C680_TAIL_FLOOD", "SW_SOV_LIGHTS_FLOOD_ON", "TAIL FLOOD Light");
        AddFlag(v, "C680_LDG_STATE", "LIGHT LANDING", "Landing Lights", "Off", "On", simvar: true);
        AddFlag(v, "C680_TAXI_STATE", "LIGHT TAXI", "Taxi Light", "Off", "On", simvar: true);
        AddFlag(v, "C680_STROBE_STATE", "LIGHT STROBE", "Anti-Collision Lights", "Off", "On", simvar: true);

        // ---- Interior Lighting (knobs 0-100; the WT backlights are what the screens read)
        AddKnob(v, "C680_KNOB_PANEL", "LIGHTING_Knob_Panel_raw", "Panel Backlight");
        AddKnob(v, "C680_KNOB_FLOOD", "LIGHTING_Knob_Flood", "Flood Light");
        AddKnob(v, "C680_KNOB_AUX", "LIGHTING_Knob_Aux_raw", "Auxiliary Backlight");
        AddKnob(v, "C680_KNOB_MAP_L", "LIGHTING_Knob_Map_1", "Left Map Light");
        AddKnob(v, "C680_KNOB_MAP_R", "LIGHTING_Knob_Map_2", "Right Map Light");
        AddKnob(v, "C680_KNOB_CKPT_L", "Cockpit_Light_L", "Left Cockpit Light");
        AddKnob(v, "C680_KNOB_CKPT_R", "Cockpit_Light_R", "Right Cockpit Light");
        AddKnob(v, "C680_KNOB_PFD_L", "WTG3000_Pfd_Backlight:1", "Left PFD Backlight");
        AddKnob(v, "C680_KNOB_PFD_R", "WTG3000_Pfd_Backlight:2", "Right PFD Backlight");
        AddKnob(v, "C680_KNOB_MFD", "WTG3000_Mfd_Backlight:1", "MFD Backlight");
        AddKnob(v, "C680_KNOB_GTC_L", "WTG3000_Gtc_Backlight:1", "Left PFD Touchscreen Backlight");
        AddKnob(v, "C680_KNOB_GTC_MFD", "WTG3000_Gtc_Backlight:2", "MFD Touchscreens Backlight");
        AddKnob(v, "C680_KNOB_GTC_R", "WTG3000_Gtc_Backlight:4", "Right PFD Touchscreen Backlight");
        AddSwitch(v, "C680_AMBIENT", "SW_SOV_GARMIN_AMBIENT_LIGHT", "Garmin Ambient Light Sensor");
        AddSwitch(v, "C680_LIGHT_ENTRY", "SW_SOV_LIGHT_ENTRY", "Entry Light");

        foreach (var k in new[] { "C680_EXT_PWR_ON", "C680_APU_RPM", "C680_COMB_L", "C680_COMB_R",
            "C680_N1_L", "C680_N1_R", "C680_N2_L", "C680_N2_R", "C680_ITT_L", "C680_ITT_R", "C680_FF_L", "C680_FF_R",
            "C680_OIL_P_L", "C680_OIL_P_R", "C680_OIL_T_L", "C680_OIL_T_R", "C680_AVN_POWER" })
            Cache(v, k);
        return v;
    }

    private static readonly List<string> ElectricalControls = new()
    {
        "C680_BATT_L", "C680_BATT_R", "C680_STBY_PWR", "C680_AVN_L", "C680_AVN_R", "C680_ELEC_L", "C680_ELEC_R",
        "C680_TRU_L", "C680_TRU_R", "C680_GEN_L", "C680_GEN_R", "C680_EXT_PWR", "C680_APU_GEN", "C680_BUS_TIE",
        "C680_INTERIOR", "C680_EMER_LTS"
    };
    private static readonly List<string> ElectricalDisplay = new()
    {
        "C680_BATT_L_V", "C680_BATT_L_A", "C680_BATT_R_V", "C680_BATT_R_A",
        "C680_GEN_L_ON", "C680_GEN_L_V", "C680_GEN_L_A", "C680_GEN_R_ON", "C680_GEN_R_V", "C680_GEN_R_A", "C680_APU_V", "C680_APU_A",
        "C680_EXT_PWR_AVAIL", "C680_EXT_PWR_ON", "C680_EXT_PWR_V", "C680_BUS_TIE_CONN", "C680_AVN_POWER",
        "C680_STBY_LED_G", "C680_STBY_LED_A"
    };
    private static readonly List<string> ApuControls = new() { "C680_APU_KNOB", "C680_APU_BLEED", "C680_MAX_COOL" };
    private static readonly List<string> ApuDisplay = new() { "C680_APU_COMB", "C680_APU_RPM", "C680_APU_EGT", "C680_APU_GEN_ON", "C680_APU_BLEED_ON", "C680_APU_FIRE" };
    private static readonly List<string> StartControls = new()
    {
        "C680_STARTER_L", "C680_RUN_L", "C680_STARTER_R", "C680_RUN_R", "C680_STARTER_DISENG",
        "C680_FADEC_RESET_L", "C680_FADEC_RESET_R", "C680_TR_STOW_L", "C680_TR_STOW_R"
    };
    private static readonly List<string> StartDisplay = new()
    {
        "C680_COMB_L", "C680_STARTER_L_ON", "C680_N1_L", "C680_N2_L", "C680_ITT_L", "C680_FF_L", "C680_OIL_P_L", "C680_OIL_T_L", "C680_FADEC_TGT_L", "C680_FADEC_MAX_L", "C680_ENG_RUN_L",
        "C680_COMB_R", "C680_STARTER_R_ON", "C680_N1_R", "C680_N2_R", "C680_ITT_R", "C680_FF_R", "C680_OIL_P_R", "C680_OIL_T_R", "C680_FADEC_TGT_R", "C680_FADEC_MAX_R", "C680_ENG_RUN_R"
    };
    private static readonly List<string> AntiIceControls = new() { "C680_PITOT_L", "C680_PITOT_R", "C680_AI_ENG_L", "C680_AI_ENG_R", "C680_AI_WING_L", "C680_AI_WING_R", "C680_WING_XFLOW", "C680_WS_FAN" };
    private static readonly List<string> AntiIceDisplay = new() { "C680_PITOT_HEAT_L", "C680_PITOT_HEAT_R", "C680_ENG_AI_L", "C680_ENG_AI_R", "C680_STRUCT_AI", "C680_AI_BOOST_L", "C680_AI_BOOST_R", "C680_OAT" };
    private static readonly List<string> ExteriorLightsControls = new() { "C680_LDG_L", "C680_LDG_R", "C680_TAXI", "C680_RECOG", "C680_PULSE", "C680_ANTI_COLL", "C680_WING_LT", "C680_LOGO", "C680_NAV_LT", "C680_BEACON", "C680_TAIL_FLOOD" };
    private static readonly List<string> ExteriorLightsDisplay = new() { "C680_LDG_STATE", "C680_TAXI_STATE", "C680_STROBE_STATE" };
    private static readonly List<string> InteriorLightingControls = new() { "C680_KNOB_PANEL", "C680_KNOB_FLOOD", "C680_KNOB_AUX", "C680_KNOB_MAP_L", "C680_KNOB_MAP_R", "C680_KNOB_CKPT_L", "C680_KNOB_CKPT_R", "C680_KNOB_PFD_L", "C680_KNOB_PFD_R", "C680_KNOB_MFD", "C680_KNOB_GTC_L", "C680_KNOB_GTC_MFD", "C680_KNOB_GTC_R", "C680_AMBIENT", "C680_LIGHT_ENTRY" };

    /// <summary>Keys whose write is simply the L:var the definition reads (the vendor switch shape).</summary>
    private static readonly HashSet<string> LeftTiltPlainLVars = new(StringComparer.Ordinal)
    {
        "C680_BATT_L", "C680_BATT_R", "C680_AVN_L", "C680_AVN_R", "C680_ELEC_L", "C680_ELEC_R", "C680_TRU_L", "C680_TRU_R",
        "C680_INTERIOR", "C680_EMER_LTS", "C680_APU_BLEED", "C680_MAX_COOL", "C680_TR_STOW_L", "C680_TR_STOW_R",
        "C680_PITOT_L", "C680_PITOT_R", "C680_AI_ENG_L", "C680_AI_ENG_R", "C680_AI_WING_L", "C680_AI_WING_R", "C680_WING_XFLOW", "C680_WS_FAN",
        "C680_LDG_L", "C680_LDG_R", "C680_TAXI", "C680_RECOG", "C680_PULSE", "C680_ANTI_COLL", "C680_WING_LT", "C680_LOGO", "C680_TAIL_FLOOD",
        "C680_KNOB_PANEL", "C680_KNOB_FLOOD", "C680_KNOB_AUX", "C680_KNOB_MAP_L", "C680_KNOB_MAP_R", "C680_KNOB_CKPT_L", "C680_KNOB_CKPT_R",
        "C680_KNOB_PFD_L", "C680_KNOB_PFD_R", "C680_KNOB_MFD", "C680_KNOB_GTC_L", "C680_KNOB_GTC_MFD", "C680_KNOB_GTC_R", "C680_AMBIENT", "C680_LIGHT_ENTRY"
    };

    private bool HandleLeftTiltSet(string varKey, double value, SimConnectManager sc)
    {
        if (LeftTiltPlainLVars.Contains(varKey))
        {
            sc.SetLVar(GetVariables()[varKey].Name, value);
            return true;
        }
        switch (varKey)
        {
            case "C680_STBY_PWR": sc.ExecuteCalculatorCode($"(A:ELECTRICAL MASTER BATTERY:3, Bool) {(value > 0.5 ? 0 : 1)} == if{{ 3 (>K:TOGGLE_MASTER_BATTERY) }}"); return true;
            case "C680_GEN_L": sc.ExecuteCalculatorCode($"1 {Rpn(value)} (>K:2:ALTERNATOR_SET)"); return true;
            case "C680_GEN_R": sc.ExecuteCalculatorCode($"2 {Rpn(value)} (>K:2:ALTERNATOR_SET)"); return true;
            case "C680_EXT_PWR": sc.ExecuteCalculatorCodeUnique("(>H:SW_SOV_ELEC_EXT_PWR)"); return true;
            case "C680_APU_GEN": sc.ExecuteCalculatorCode($"3 {Rpn(value)} (>K:2:APU_GENERATOR_SWITCH_SET)"); return true;
            case "C680_BUS_TIE": sc.ExecuteCalculatorCodeUnique("(>H:SW_SOV_ELEC_BUS_TIE)"); return true;
            case "C680_APU_KNOB": SetApuKnob(sc, (int)Math.Round(value)); return true;
            case "C680_STARTER_L": sc.ExecuteCalculatorCode("(>B:ENGINE_Starter_1_On)"); return true;
            case "C680_STARTER_R": sc.ExecuteCalculatorCode("(>B:ENGINE_Starter_2_On)"); return true;
            case "C680_STARTER_DISENG":
                sc.ExecuteCalculatorCode("(A:GENERAL ENG STARTER:1, Bool) if{ (>K:TOGGLE_STARTER1) } (A:GENERAL ENG STARTER:2, Bool) if{ (>K:TOGGLE_STARTER2) }");
                return true;
            case "C680_RUN_L": sc.ExecuteCalculatorCode($"(A:GENERAL ENG FUEL VALVE:1, Bool) {(value > 0.5 ? 0 : 1)} == if{{ (>B:FUEL_RunStop_1_Toggle) }}"); return true;
            case "C680_RUN_R": sc.ExecuteCalculatorCode($"(A:GENERAL ENG FUEL VALVE:2, Bool) {(value > 0.5 ? 0 : 1)} == if{{ (>B:FUEL_RunStop_2_Toggle) }}"); return true;
            case "C680_FADEC_RESET_L": Pulse(sc, "FADEC_RESET_L", 600); return true;
            case "C680_FADEC_RESET_R": Pulse(sc, "FADEC_RESET_R", 600); return true;
            case "C680_NAV_LT": ToggleTo(sc, "LIGHT NAV", "Bool", value > 0.5, "TOGGLE_NAV_LIGHTS"); return true;
            case "C680_BEACON": ToggleTo(sc, "LIGHT BEACON", "Bool", value > 0.5, "TOGGLE_BEACON_LIGHTS"); return true;
        }
        return false;
    }

    /// <summary>
    /// The APU knob is the stock ASOBO_ELECTRICAL_Switch_APU_Starter_Template (0 Off, 1 On,
    /// 2 Start, momentary). Measured: knob 1, then 2 with K:APU_STARTER, back to 1 a second
    /// later — the APU accelerated 17 → 63 → 100 percent within 15 s.
    /// </summary>
    private static void SetApuKnob(SimConnectManager sc, int pos)
    {
        sc.SetLVar("XMLVAR_APU_StarterKnob_Pos", pos);
        if (pos == 2)
        {
            sc.ExecuteCalculatorCodeUnique("(>K:APU_STARTER)");
            var t = new System.Windows.Forms.Timer { Interval = 1000 };
            t.Tick += (_, _) => { t.Stop(); t.Dispose(); sc.SetLVar("XMLVAR_APU_StarterKnob_Pos", 1); };
            t.Start();
        }
        else if (pos == 0) sc.ExecuteCalculatorCodeUnique("(>K:APU_OFF_SWITCH)");
    }
}
