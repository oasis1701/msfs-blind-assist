namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Panel → control-key mapping, mirroring the manual's cockpit geography.
/// Lamp vars are deliberately ABSENT (A380 invariant: a pilot navigates panels to
/// operate controls, not to scan annunciator rows — lamps announce via the monitor).
/// </summary>
public partial class SynapticA220Definition
{
    protected override Dictionary<string, List<string>> BuildPanelControls() => new()
    {
        ["Electrical"] = new List<string>
        {
            "A22X_BATTERY_1", "A22X_BATTERY_2", "A22X_EXT_PWR", "A22X_GPU_AVAIL",
            "A22X_L_GEN_OFF", "A22X_R_GEN_OFF", "A22X_APU_GEN_OFF",
            "A22X_CABIN_POWER_OFF", "A22X_BUS_ISOLATION",
            "A22X_RAT_GEN", "A22X_L_GEN_DISC", "A22X_R_GEN_DISC"
        },
        ["APU"] = new List<string> { "A22X_APU_SWITCH" },
        ["Hydraulics"] = new List<string>
        {
            "A22X_PTU", "A22X_ACMP_2B", "A22X_ACMP_3A", "A22X_ACMP_3B",
            "A22X_HYD_1_SOV", "A22X_HYD_2_SOV"
        },
        ["Fuel"] = new List<string>
        {
            "A22X_L_BOOST_PUMP", "A22X_R_BOOST_PUMP",
            "A22X_MANUAL_TRANSFER", "A22X_GRAVITY_TRANSFER"
        },
        ["Air Conditioning and Bleed"] = new List<string>
        {
            "A22X_L_BLEED_OFF", "A22X_APU_BLEED_OFF", "A22X_R_BLEED_OFF", "A22X_CROSSBLEED",
            "A22X_L_PACK_OFF", "A22X_R_PACK_OFF", "A22X_PACK_FLOW", "A22X_RAM_AIR",
            "A22X_TRIM_AIR_OFF", "A22X_RECIRC_OFF", "A22X_MANUAL_TEMP",
            "A22X_COCKPIT_AIR", "A22X_FWD_CABIN_AIR", "A22X_AFT_CABIN_AIR",
            "A22X_FWD_CARGO_AIR", "A22X_AFT_CARGO_AIR"
        },
        ["Anti-Ice and Heat"] = new List<string>
        {
            "A22X_L_COWL_AI", "A22X_R_COWL_AI", "A22X_WING_AI", "A22X_PROBE_HEAT",
            "A22X_L_WINDOW_HEAT_OFF", "A22X_R_WINDOW_HEAT_OFF",
            "A22X_L_WSHLD_HEAT_OFF", "A22X_R_WSHLD_HEAT_OFF"
        },
        ["Pressurization"] = new List<string>
        {
            "A22X_MAN_PRESS", "A22X_MANUAL_RATE",
            "A22X_EMER_DEPRESS", "A22X_DITCHING", "A22X_PAX_OXYGEN"
        },
        ["Fire and Emergency"] = new List<string>
        {
            "A22X_L_ENG_FIRE", "A22X_R_ENG_FIRE", "A22X_ELT", "A22X_EVAC"
        },
        ["Flight Controls and TAWS"] = new List<string>
        {
            "A22X_PFCC_1_OFF", "A22X_PFCC_2_OFF", "A22X_PFCC_3_OFF",
            "A22X_AURAL_INHIBIT", "A22X_TAWS_GEAR_INHIBIT", "A22X_TAWS_TERRAIN_INHIBIT",
            "A22X_TAWS_FLAPS_INHIBIT", "A22X_TAWS_GS_INHIBIT"
        },
        ["Exterior Lights"] = new List<string>
        {
            "A22X_NAV_LIGHTS", "A22X_BEACON_LIGHTS", "A22X_STROBE_LIGHTS",
            "A22X_LOGO_LIGHTS", "A22X_WING_INSP_LIGHTS", "A22X_TAXI_LIGHTS",
            "A22X_L_LANDING_LIGHTS", "A22X_R_LANDING_LIGHTS", "A22X_NOSE_LANDING_LIGHTS"
        },
        ["Signs and Interior"] = new List<string>
        {
            "A22X_EMERGENCY_LIGHTS", "A22X_SEAT_BELT_LIGHTS", "A22X_NO_PED_LIGHTS",
            "A22X_DOME_LIGHTS", "A22X_ANNUN_LIGHTS", "A22X_LAMP_TEST"
        },
        ["CTP Captain"] = new List<string>
        {
            "A22X_L_BARO_STD", "A22X_L_NAV_SOURCE", "A22X_L_CROSSTUNE"
        },
        ["CTP First Officer"] = new List<string>
        {
            "A22X_R_BARO_STD", "A22X_R_NAV_SOURCE", "A22X_R_CROSSTUNE"
        },
        ["Warnings and Chrono"] = new List<string>
        {
            "A22X_MASTER_CW_CANCEL", "A22X_L_CHRONO", "A22X_R_CHRONO"
        },
        ["Flight Directors"] = new List<string> { "A22X_L_FD", "A22X_R_FD" },
        ["Engines and Ignition"] = new List<string>
        {
            "A22X_ENG_MASTER_1", "A22X_ENG_MASTER_2",
            "A22X_ENG_START_MODE", "A22X_CONT_IGNITION", "A22X_PARKING_BRAKE"
        },
        ["Trim"] = new List<string> { "A22X_STAB_TRIM_SET", "RUDDER_TRIM_LEFT", "RUDDER_TRIM_RIGHT" },
        ["Gear and Brakes"] = new List<string>
        {
            "A22X_GEAR_LEVER", "A22X_ALTERNATE_GEAR", "A22X_AUTOBRAKE",
            "A22X_NOSE_STEER_OFF", "A22X_ALTERNATE_BRAKE", "A22X_GEAR_AURAL_CANCEL"
        },
        ["Flaps and Speedbrake"] = new List<string>
        {
            "A22X_FLAP_LEVER", "A22X_ALTERNATE_FLAP",
            "A22X_SPOILERS_RETRACT", "A22X_SPOILERS_DEPLOY"
        },
        ["COM Radios"] = new List<string>
        {
            "A22X_COM1_STBY_SET", "A22X_COM1_SWAP",
            "A22X_COM2_STBY_SET", "A22X_COM2_SWAP"
        },
        ["NAV Radios"] = new List<string>
        {
            // Standby-only by design: manual ACTIVE ILS tuning breaks the A220's
            // NAV-to-NAV transfer (docs/a220-plan.md W8).
            "A22X_NAV1_STBY_SET", "A22X_NAV2_STBY_SET"
        },
        ["Transponder"] = new List<string> { "TRANSPONDER_CODE_SET", "A22X_XPDR_IDENT" },
        ["Simulation Options"] = new List<string> { "A22X_TOD_PAUSE", "A22X_TOD_DISTANCE_SET" },
        ["Engine Data"] = new List<string>()
    };
}
