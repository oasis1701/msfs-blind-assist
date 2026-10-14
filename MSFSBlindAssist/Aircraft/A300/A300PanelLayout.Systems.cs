namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The panels iniBuilds groups by place but the fleet's Airbuses group by system (design decision,
/// 2026-10-09): the overhead lights panel is Signs, Interior Lighting and Exterior Lighting; the bleed
/// panel is Bleed and Air Conditioning; the fire panel gives up Cargo Smoke; the main instrument panel and
/// the throttle quadrant are sorted into the FBW Airbuses' Instrument and Pedestal panels. The window and
/// probe heat panel and the landing elevation knob stay where the A300 has them (design decision, same day).
/// </summary>
public static partial class A300PanelLayout
{
    /// <summary>Input event id (without <c>AIRLINER_</c>) to its panel, for the controls of a split iniBuilds
    /// panel; it wins over <see cref="PanelNames"/>.</summary>
    private static readonly Dictionary<string, string> ControlPanels = new(StringComparer.Ordinal)
    {
        // Overhead lights panel: the signs and the exterior lights; the rest is interior lighting.
        ["SEATBELT"] = "Signs",
        ["NOSMOKING"] = "Signs",
        ["EMERGEXIT_SWITCH"] = "Signs",
        ["NOSELIGHTSWITCH"] = "Exterior Lighting",
        ["LANDINGLEFTSWITCH"] = "Exterior Lighting",
        ["LANDINGRIGHTSWITCH"] = "Exterior Lighting",
        ["WINGLIGHTSWITCH"] = "Exterior Lighting",
        ["STROBESWITCH"] = "Exterior Lighting",
        ["BEACONSWITCH"] = "Exterior Lighting",
        ["RWY_TOFF_L"] = "Exterior Lighting",
        ["RWY_TOFF_R"] = "Exterior Lighting",
        ["NAVLIGHT_SWITCH"] = "Exterior Lighting",

        // Air bleed panel: the bleeds and their valves; the rest is air conditioning.
        ["AIR_XFEED"] = "Bleed",
        ["AIR_XFEED_AUTO"] = "Bleed",
        ["ENG1_HP_VALVE"] = "Bleed",
        ["ENG2_HP_VALVE"] = "Bleed",
        ["ENG1_WING_BLEED_VALVE"] = "Bleed",
        ["ENG2_WING_BLEED_VALVE"] = "Bleed",
        ["ISO_VALVE_LEFT"] = "Bleed",
        ["ISO_VALVE_RIGHT"] = "Bleed",
        ["APU_BLEEDSWITCH"] = "Bleed",

        // Fire panel: the cargo and main deck smoke detection; the rest is fire.
        ["SMOKE_MID1_1"] = "Cargo Smoke",
        ["SMOKE_MID1_2"] = "Cargo Smoke",
        ["SMOKE_MID2_1"] = "Cargo Smoke",
        ["SMOKE_MID2_2"] = "Cargo Smoke",
        ["SMOKE_AFT_1"] = "Cargo Smoke",
        ["SMOKE_AFT_2"] = "Cargo Smoke",
        ["CARGO_FWD_SMOKE_TOGGLE"] = "Cargo Smoke",
        ["CARGO_FWD_SMOKE_TOGGLE_GUARD"] = "Cargo Smoke",
        ["CARGO_AFT_SMOKE_TOGGLE"] = "Cargo Smoke",
        ["CARGO_AFT_SMOKE_TOGGLE_GUARD"] = "Cargo Smoke",
        ["SMOKE_TEST"] = "Cargo Smoke",

        // Main instrument panel, both sides: what both pilots have; the rest is each side's own panel.
        ["MASTER_WARNING_CPT"] = "Warnings",
        ["MASTER_CAUTION_CPT"] = "Warnings",
        ["MASTER_WARNING_FO"] = "Warnings",
        ["MASTER_CAUTION_FO"] = "Warnings",
        ["CPT_CLOCK_DATE"] = "Clock",
        ["CPT_CLOCK_MODE"] = "Clock",
        ["CPT_CLOCK_START"] = "Clock",
        ["CPT_CLOCK_RUN"] = "Clock",
        ["FO_CLOCK_DATE"] = "Clock",
        ["FO_CLOCK_MODE"] = "Clock",
        ["FO_CLOCK_START"] = "Clock",
        ["FO_CLOCK_RUN"] = "Clock",
        ["CPT_ATT_HDG"] = "Source Switching",
        ["CPT_ADC_INST"] = "Source Switching",
        ["CPT_FD_PUSH"] = "Source Switching",
        ["CPT_EFIS_SGU"] = "Source Switching",
        ["CPT_PFD_XFR"] = "Source Switching",
        ["FO_SW_ATT"] = "Source Switching",
        ["FO_SW_ADC"] = "Source Switching",
        ["FO_SW_FD"] = "Source Switching",
        ["FO_SW_EFIS"] = "Source Switching",
        ["FO_PFD_XFR"] = "Source Switching",
        ["GPWS_CPT"] = "GPWS",
        ["GPWS_FO"] = "GPWS",
        ["TERR_CPT"] = "GPWS",
        ["TERR_FO"] = "GPWS",
        ["CPT_TERR_MODE"] = "GPWS",
        ["FO_TERR_MODE"] = "GPWS",
        ["GPWS_FLAP_SWITCH"] = "GPWS",
        ["GPWS_FLAPS_CONFIG"] = "GPWS",

        // Main instrument panel, center.
        ["GEAR_LEVER"] = "Gear",
        ["LDG_TEST"] = "Gear",
        ["LDG_POS_DET"] = "Gear",
        ["MAN_GEAR_HANDLE_EXT"] = "Gear",
        ["AUTO_BRK_LO"] = "Autobrake",
        ["AUTO_BRK_MID"] = "Autobrake",
        ["AUTO_BRK_MAX"] = "Autobrake",
        ["ANTI_SKID"] = "Autobrake",
        ["BRAKE_FAN"] = "Autobrake",
        ["TRP_TOGA"] = "Thrust Rating Panel",
        ["TRP_MCT"] = "Thrust Rating Panel",
        ["TRP_CL"] = "Thrust Rating Panel",
        ["TRP_CR"] = "Thrust Rating Panel",
        ["TRP_AUTO"] = "Thrust Rating Panel",
        ["TRP_FLEXTO"] = "Thrust Rating Panel",
        ["FLEX_TEMP"] = "Thrust Rating Panel",
        ["LANDING_ELEV_SET"] = "Landing Elevation",
        ["ADI_CAGE"] = "Standby Instruments",

        // Throttle quadrant and the go levers (iniBuilds puts TOGA on the captain's EFIS panel).
        ["ENG1_CUTOFF"] = "Engines",
        ["ENG2_CUTOFF"] = "Engines",
        ["AT_DISCO1"] = "Thrust Levers",
        ["TOGA_SEL"] = "Thrust Levers",
        ["PARKINGBRAKE"] = "Parking Brake",
        ["PARKBRAKE_PRESS_PUSH"] = "Parking Brake",
        ["NORM_CANCEL"] = "ECAM Control Panel",
        ["EMER_CANCEL_BUTTON"] = "ECAM Control Panel",
        ["TO_CONFIG_TEST"] = "ECAM Control Panel",
    };

    /// <summary>The section of each panel that is not in its controls' own iniBuilds area: the instrument
    /// panel's system panels (the gear gravity handle sits in iniBuilds' cockpit area, the GPWS landing flaps
    /// switch on the pedestal) and the pedestal's (the go levers sit on the captain's EFIS panel).</summary>
    private static readonly Dictionary<string, string> PanelSections = new(StringComparer.Ordinal)
    {
        ["Warnings"] = "Instrument",
        ["Gear"] = "Instrument",
        ["Autobrake"] = "Instrument",
        ["Thrust Rating Panel"] = "Instrument",
        ["Landing Elevation"] = "Instrument",
        ["Standby Instruments"] = "Instrument",
        ["Source Switching"] = "Instrument",
        ["Clock"] = "Instrument",
        ["GPWS"] = "Instrument",
        ["Captain Side"] = "Instrument",
        ["First Officer Side"] = "Instrument",
        ["Engines"] = "Pedestal",
        ["Thrust Levers"] = "Pedestal",
        ["Flaps and Speed Brake"] = "Pedestal",
        ["Parking Brake"] = "Pedestal",
        ["ECAM Control Panel"] = "Pedestal",
    };
}
