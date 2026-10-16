namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Panel → control-key mapping (docs/a220.md "Panels and hotkeys"): every panel opens on the
/// controls a normal flight uses, in the order the day reaches them (the aircraft's normal
/// checklists and the pilot-guide flows, power-on to power-off); the rest follow in cockpit
/// order and guarded switches come last. Fire Protection reads per fire (push, then its
/// bottles) and the Audio Control Panel per radio (transmit, volume, receiver); cockpit
/// positions for the rest: tools/a220-gen/reference/cockpit-layout.txt. Lamp vars are deliberately ABSENT
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
            "A22X_BATTERY_1", "A22X_BATTERY_2",
            // GPU (EFB ground equipment, no cockpit node) first, then the EXT PWR korry it feeds.
            "A22X_GPU_AVAIL", "A22X_EXT_PWR", "A22X_APU_GEN_OFF", "A22X_L_GEN_OFF", "A22X_R_GEN_OFF",
            "A22X_BUS_ISOLATION", "A22X_CABIN_POWER_OFF", "A22X_RAT_GEN",
            "A22X_L_GEN_DISC", "A22X_R_GEN_DISC"
        },
        ["Hydraulics"] = new List<string>
        {
            "A22X_PTU", "A22X_ACMP_3A", "A22X_ACMP_3B", "A22X_ACMP_2B",
            "A22X_HYD_1_SOV", "A22X_HYD_2_SOV"
        },
        ["APU"] = new List<string> { "A22X_APU_SWITCH" },
        ["Equipment Cooling"] = new List<string> { "A22X_COOLING_INLET", "A22X_COOLING_EXHAUST" },
        ["Fire Protection"] = new List<string>
        {
            // Per fire: the push, then its bottles (in a fire the pilot presses the push first).
            "A22X_L_ENG_FIRE", "A22X_L_ENG_FIRE_BOTTLE_1", "A22X_L_ENG_FIRE_BOTTLE_2",
            "A22X_R_ENG_FIRE", "A22X_R_ENG_FIRE_BOTTLE_1", "A22X_R_ENG_FIRE_BOTTLE_2",
            "A22X_APU_FIRE", "A22X_APU_BOTTLE_1",
            "A22X_CARGO_FWD_FIRE", "A22X_CARGO_AFT_FIRE", "A22X_CARGO_FIRE_BOTTLE"
        },
        ["Fuel"] = new List<string>
        {
            "A22X_L_BOOST_PUMP", "A22X_R_BOOST_PUMP", "A22X_MANUAL_TRANSFER", "A22X_GRAVITY_TRANSFER"
        },
        ["Air Conditioning"] = new List<string>
        {
            // Trim air is off before every start and back to auto after.
            "A22X_TRIM_AIR_OFF", "A22X_COCKPIT_AIR", "A22X_FWD_CABIN_AIR", "A22X_AFT_CABIN_AIR",
            "A22X_FWD_CARGO_AIR", "A22X_AFT_CARGO_AIR", "A22X_PACK_FLOW", "A22X_RECIRC_OFF",
            "A22X_MANUAL_TEMP", "A22X_RAM_AIR"
        },
        ["Bleed and Packs"] = new List<string>
        {
            // APU bleed is touched before take-off, after take-off, after landing and at shutdown.
            "A22X_APU_BLEED_OFF", "A22X_L_PACK_OFF", "A22X_R_PACK_OFF",
            "A22X_L_BLEED_OFF", "A22X_R_BLEED_OFF", "A22X_CROSSBLEED"
        },
        ["Anti-Ice"] = new List<string> { "A22X_L_COWL_AI", "A22X_WING_AI", "A22X_R_COWL_AI" },
        ["Pressurization"] = new List<string>
        {
            "A22X_MAN_PRESS", "A22X_MANUAL_RATE", "A22X_EMER_DEPRESS", "A22X_DITCHING", "A22X_PAX_OXYGEN"
        },
        // Emergency lights: ARM at every preflight, OFF at power-off.
        ["Emergency"] = new List<string> { "A22X_EMERGENCY_LIGHTS", "A22X_ELT", "A22X_EVAC" },
        ["Window and Probe Heat"] = new List<string>
        {
            // Windshields together, then the side windows.
            "A22X_PROBE_HEAT",
            "A22X_L_WSHLD_HEAT_OFF", "A22X_R_WSHLD_HEAT_OFF", "A22X_L_WINDOW_HEAT_OFF", "A22X_R_WINDOW_HEAT_OFF"
        },
        ["Flight Controls and Recorder"] = new List<string>
        {
            "A22X_CVR_TEST", "A22X_CVR_ERASE", "A22X_PFCC_1_OFF", "A22X_PFCC_2_OFF", "A22X_PFCC_3_OFF"
        },
        ["Calls"] = new List<string> { "A22X_SERVICE_INTERCOM", "A22X_MECH_CALL" },
        // The inhibit panel; Aural Warning Inhibit (left outboard, beside probe heat) joins it
        // and, as the one unguarded switch, comes first.
        ["TAWS"] = new List<string>
        {
            "A22X_TAWS_GS_INHIBIT", "A22X_TAWS_GEAR_INHIBIT", "A22X_TAWS_TERRAIN_INHIBIT",
            "A22X_TAWS_FLAPS_INHIBIT", "A22X_AURAL_INHIBIT"
        },
        ["Wipers"] = new List<string> { "A22X_WIPER_L", "A22X_WIPER_R" },
        ["Interior Lighting"] = new List<string> { "A22X_DOME_LIGHTS", "A22X_ANNUN_LIGHTS" },
        ["Exterior Lights"] = new List<string>
        {
            // Exterior check, before start, taxi, runway, line-up, take-off clearance; then the night lights.
            "A22X_NAV_LIGHTS", "A22X_BEACON_LIGHTS", "A22X_TAXI_LIGHTS", "A22X_STROBE_LIGHTS",
            "A22X_L_LANDING_LIGHTS", "A22X_R_LANDING_LIGHTS", "A22X_NOSE_LANDING_LIGHTS",
            "A22X_LOGO_LIGHTS", "A22X_WING_INSP_LIGHTS"
        },
        ["Signs"] = new List<string> { "A22X_SEAT_BELT_LIGHTS", "A22X_NO_PED_LIGHTS" },

        // ---- Glareshield ----------------------------------------------------------------
        ["Captain Side Panel"] = new List<string> { "A22X_L_CHRONO", "A22X_MASTER_CW_CANCEL" },
        ["CTP Captain"] = new List<string>
        {
            "A22X_L_QNH_SET", "A22X_L_BARO_STD", "A22X_L_BARO_UNIT",
            "A22X_L_MINIMUMS_SET", "A22X_L_MINIMUMS_MODE",
            "A22X_COM1_STBY_SET", "A22X_COM1_SWAP", "A22X_COM2_STBY_SET", "A22X_COM2_SWAP",
            // Standby-only by design: manual ACTIVE ILS tuning breaks the A220's NAV-to-NAV
            // transfer ([A220-5]).
            "A22X_NAV1_STBY_SET", "A22X_NAV2_STBY_SET",
            "A22X_L_NAV_SOURCE", "A22X_L_CROSSTUNE"
        },
        ["Transponder and TCAS"] = new List<string>
        {
            "A22X_XPDR_CODE_SET", "A22X_XPDR_MODE", "A22X_XPDR_IDENT", "A22X_TCAS_ALT_LIMIT", "A22X_XPDR_FLIGHT_ID"
        },
        ["Flight Directors"] = new List<string> { "A22X_L_FD", "A22X_R_FD" },
        ["CTP First Officer"] = new List<string>
        {
            "A22X_R_QNH_SET", "A22X_R_BARO_STD", "A22X_R_BARO_UNIT",
            "A22X_R_MINIMUMS_SET", "A22X_R_MINIMUMS_MODE",
            "A22X_R_NAV_SOURCE", "A22X_R_CROSSTUNE", "A22X_R_CHRONO"
        },

        // ---- Center panel ---------------------------------------------------------------
        ["Standby Instrument"] = new List<string> { "A22X_ISI_BARO_SET", "A22X_ISI_BARO_STD" },
        ["Gear and Brakes"] = new List<string>
        {
            "A22X_GEAR_LEVER", "A22X_AUTOBRAKE", "A22X_NOSE_STEER_OFF", "A22X_GEAR_AURAL_CANCEL",
            "A22X_ALTERNATE_GEAR", "A22X_ALTERNATE_BRAKE"
        },

        // ---- Pedestal -------------------------------------------------------------------
        ["Engines"] = new List<string>
        {
            // Start mode first, then the left and right run switches.
            "A22X_ENG_START_MODE", "A22X_ENG_MASTER_1", "A22X_ENG_MASTER_2", "A22X_CONT_IGNITION"
        },
        ["Flaps and Speedbrake"] = new List<string> { "A22X_FLAP_LEVER", "A22X_SPOILER_LEVER", "A22X_ALTERNATE_FLAP" },
        ["Trim and Parking Brake"] = new List<string>
        {
            // The parking brake is the day's first step and every stop's.
            "A22X_PARKING_BRAKE", "A22X_STAB_TRIM_SET", "RUDDER_TRIM_LEFT", "RUDDER_TRIM_RIGHT"
        },
        // Per radio: transmit, volume, receiver.
        ["Audio Control Panel"] = new List<string> { "A22X_ACP_VHF1_TX", "A22X_ACP_VHF1_VOLUME", "A22X_ACP_VHF1_RX",
            "A22X_ACP_VHF2_TX", "A22X_ACP_VHF2_VOLUME", "A22X_ACP_VHF2_RX",
            "A22X_ACP_VHF3_TX", "A22X_ACP_VHF3_VOLUME", "A22X_ACP_VHF3_RX" },

        // ---- Utilities --------------------------------------------------------------------
        ["Simulation Options"] = new List<string> { "A22X_TOD_PAUSE", "A22X_TOD_DISTANCE_SET" },
        // Display-only panels still need an entry ([HS-5]).
        ["Engine Data"] = new List<string>(),
        ["Doors"] = new List<string>(),
        ["IRS"] = new List<string>()
    };
}
