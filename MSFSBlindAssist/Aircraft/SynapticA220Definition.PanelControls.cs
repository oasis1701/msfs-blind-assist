namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Panel → control-key mapping in the cockpit's own order (docs/a220.md "Panels and
/// hotkeys"): inside a panel the controls read the way the panel is drawn — overhead aft to
/// forward then left to right, glareshield left to right, center panel and pedestal top to
/// bottom (tools/a220-gen/reference/cockpit-layout.txt). Lamp vars are deliberately ABSENT
/// (A380 invariant: a pilot navigates panels to operate controls, not to scan annunciator
/// rows — lamps announce via the monitor). Pinned by A220PanelStructureTests.
/// </summary>
public partial class SynapticA220Definition
{
    protected override Dictionary<string, List<string>> BuildPanelControls() => new()
    {
        // ---- Overhead -------------------------------------------------------------------
        ["Electrical"] = new List<string>
        {
            "A22X_BUS_ISOLATION", "A22X_CABIN_POWER_OFF", "A22X_RAT_GEN",
            "A22X_BATTERY_1", "A22X_BATTERY_2",
            // GPU (EFB ground equipment, no cockpit node) sits by the EXT PWR korry it feeds.
            "A22X_L_GEN_OFF", "A22X_EXT_PWR", "A22X_GPU_AVAIL", "A22X_APU_GEN_OFF", "A22X_R_GEN_OFF",
            "A22X_L_GEN_DISC", "A22X_R_GEN_DISC"
        },
        ["Hydraulics"] = new List<string>
        {
            "A22X_HYD_1_SOV", "A22X_ACMP_3A", "A22X_HYD_2_SOV",
            "A22X_PTU", "A22X_ACMP_3B", "A22X_ACMP_2B"
        },
        ["APU"] = new List<string> { "A22X_APU_SWITCH" },
        ["Equipment Cooling"] = new List<string> { "A22X_COOLING_INLET", "A22X_COOLING_EXHAUST" },
        ["Fire Protection"] = new List<string>
        {
            "A22X_L_ENG_FIRE", "A22X_APU_FIRE", "A22X_R_ENG_FIRE",
            "A22X_L_ENG_FIRE_BOTTLE_1", "A22X_L_ENG_FIRE_BOTTLE_2", "A22X_APU_BOTTLE_1",
            "A22X_R_ENG_FIRE_BOTTLE_1", "A22X_R_ENG_FIRE_BOTTLE_2",
            "A22X_CARGO_FWD_FIRE", "A22X_CARGO_FIRE_BOTTLE", "A22X_CARGO_AFT_FIRE"
        },
        ["Fuel"] = new List<string>
        {
            "A22X_MANUAL_TRANSFER", "A22X_L_BOOST_PUMP", "A22X_GRAVITY_TRANSFER", "A22X_R_BOOST_PUMP"
        },
        ["Air Conditioning"] = new List<string>
        {
            "A22X_COCKPIT_AIR", "A22X_FWD_CABIN_AIR", "A22X_AFT_CABIN_AIR",
            "A22X_MANUAL_TEMP", "A22X_FWD_CARGO_AIR", "A22X_AFT_CARGO_AIR",
            "A22X_TRIM_AIR_OFF", "A22X_PACK_FLOW", "A22X_RECIRC_OFF", "A22X_RAM_AIR"
        },
        ["Bleed and Packs"] = new List<string>
        {
            "A22X_L_PACK_OFF", "A22X_CROSSBLEED", "A22X_R_PACK_OFF",
            "A22X_L_BLEED_OFF", "A22X_APU_BLEED_OFF", "A22X_R_BLEED_OFF"
        },
        ["Anti-Ice"] = new List<string> { "A22X_L_COWL_AI", "A22X_WING_AI", "A22X_R_COWL_AI" },
        ["Pressurization"] = new List<string>
        {
            "A22X_EMER_DEPRESS", "A22X_MAN_PRESS", "A22X_DITCHING", "A22X_MANUAL_RATE", "A22X_PAX_OXYGEN"
        },
        ["Emergency"] = new List<string> { "A22X_ELT", "A22X_EVAC", "A22X_EMERGENCY_LIGHTS" },
        ["Window and Probe Heat"] = new List<string>
        {
            "A22X_PROBE_HEAT",
            "A22X_L_WINDOW_HEAT_OFF", "A22X_L_WSHLD_HEAT_OFF", "A22X_R_WSHLD_HEAT_OFF", "A22X_R_WINDOW_HEAT_OFF"
        },
        ["Flight Controls and Recorder"] = new List<string>
        {
            "A22X_PFCC_1_OFF", "A22X_PFCC_2_OFF", "A22X_PFCC_3_OFF", "A22X_CVR_TEST", "A22X_CVR_ERASE"
        },
        ["Calls"] = new List<string> { "A22X_SERVICE_INTERCOM", "A22X_MECH_CALL" },
        // The inhibit panel; Aural Warning Inhibit (left outboard, beside probe heat) joins it.
        ["TAWS"] = new List<string>
        {
            "A22X_TAWS_GEAR_INHIBIT", "A22X_TAWS_TERRAIN_INHIBIT", "A22X_TAWS_FLAPS_INHIBIT",
            "A22X_TAWS_GS_INHIBIT", "A22X_AURAL_INHIBIT"
        },
        ["Wipers"] = new List<string> { "A22X_WIPER_L", "A22X_WIPER_R" },
        ["Interior Lighting"] = new List<string> { "A22X_DOME_LIGHTS", "A22X_ANNUN_LIGHTS" },
        ["Exterior Lights"] = new List<string>
        {
            "A22X_NAV_LIGHTS", "A22X_BEACON_LIGHTS", "A22X_STROBE_LIGHTS", "A22X_LOGO_LIGHTS",
            "A22X_WING_INSP_LIGHTS", "A22X_TAXI_LIGHTS",
            "A22X_L_LANDING_LIGHTS", "A22X_NOSE_LANDING_LIGHTS", "A22X_R_LANDING_LIGHTS"
        },
        ["Signs"] = new List<string> { "A22X_SEAT_BELT_LIGHTS", "A22X_NO_PED_LIGHTS" },

        // ---- Glareshield ----------------------------------------------------------------
        ["Captain Side Panel"] = new List<string> { "A22X_L_CHRONO", "A22X_MASTER_CW_CANCEL" },
        ["CTP Captain"] = new List<string>
        {
            "A22X_L_NAV_SOURCE",
            "A22X_L_QNH_SET", "A22X_L_BARO_UNIT", "A22X_L_BARO_STD",
            "A22X_L_MINIMUMS_MODE", "A22X_L_MINIMUMS_SET",
            // Standby-only by design: manual ACTIVE ILS tuning breaks the A220's NAV-to-NAV
            // transfer ([A220-5]).
            "A22X_COM1_STBY_SET", "A22X_COM1_SWAP", "A22X_COM2_STBY_SET", "A22X_COM2_SWAP",
            "A22X_NAV1_STBY_SET", "A22X_NAV2_STBY_SET",
            "A22X_L_CROSSTUNE"
        },
        ["Transponder and TCAS"] = new List<string>
        {
            "A22X_XPDR_MODE", "A22X_TCAS_ALT_LIMIT", "A22X_XPDR_CODE_SET", "A22X_XPDR_IDENT", "A22X_XPDR_FLIGHT_ID"
        },
        ["Flight Directors"] = new List<string> { "A22X_L_FD", "A22X_R_FD" },
        ["CTP First Officer"] = new List<string>
        {
            "A22X_R_NAV_SOURCE",
            "A22X_R_QNH_SET", "A22X_R_BARO_UNIT", "A22X_R_BARO_STD",
            "A22X_R_MINIMUMS_MODE", "A22X_R_MINIMUMS_SET",
            "A22X_R_CROSSTUNE", "A22X_R_CHRONO"
        },

        // ---- Center panel ---------------------------------------------------------------
        ["Standby Instrument"] = new List<string> { "A22X_ISI_BARO_SET", "A22X_ISI_BARO_STD" },
        ["Gear and Brakes"] = new List<string>
        {
            "A22X_GEAR_LEVER", "A22X_GEAR_AURAL_CANCEL", "A22X_ALTERNATE_GEAR",
            "A22X_NOSE_STEER_OFF", "A22X_AUTOBRAKE", "A22X_ALTERNATE_BRAKE"
        },

        // ---- Pedestal -------------------------------------------------------------------
        ["Engines"] = new List<string>
        {
            "A22X_ENG_MASTER_1", "A22X_ENG_MASTER_2", "A22X_CONT_IGNITION", "A22X_ENG_START_MODE"
        },
        ["Flaps and Speedbrake"] = new List<string> { "A22X_FLAP_LEVER", "A22X_SPOILER_LEVER", "A22X_ALTERNATE_FLAP" },
        ["Trim and Parking Brake"] = new List<string>
        {
            "A22X_STAB_TRIM_SET", "RUDDER_TRIM_LEFT", "RUDDER_TRIM_RIGHT", "A22X_PARKING_BRAKE"
        },
        ["Audio Control Panel"] = new List<string> { "A22X_ACP_VHF1_TX", "A22X_ACP_VHF2_TX", "A22X_ACP_VHF3_TX",
            "A22X_ACP_VHF1_VOLUME", "A22X_ACP_VHF1_RX", "A22X_ACP_VHF2_VOLUME", "A22X_ACP_VHF2_RX",
            "A22X_ACP_VHF3_VOLUME", "A22X_ACP_VHF3_RX" },

        // ---- Utilities --------------------------------------------------------------------
        ["Simulation Options"] = new List<string> { "A22X_TOD_PAUSE", "A22X_TOD_DISTANCE_SET" },
        // Display-only panels still need an entry ([HS-5]).
        ["Engine Data"] = new List<string>(),
        ["Doors"] = new List<string>(),
        ["IRS"] = new List<string>()
    };
}
