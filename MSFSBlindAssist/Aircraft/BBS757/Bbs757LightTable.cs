namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// One 757 annunciator light the cockpit-wide announcements speak (plan 13): its L:var (without
/// <c>L:</c>), its Ctrl+M label, and what is said as it comes on and as it goes out (null: going
/// out is silent).
/// </summary>
public sealed record Bbs757Light(string LVar, string Label, string OnPhrase, string? OffPhrase);

/// <summary>
/// The 757's annunciator lights worth a pilot's attention, curated from the 518 light nodes of BlueBird's
/// model XML (the test fixture <c>bbs757-lights.json</c> holds their names), and
/// the set of L:vars the tablet agent watches for the cockpit-wide announcements.
///
/// Left out on purpose: backlights, display brightness and test lights; the 511 breakers; the
/// legends that only follow their own switch (the "ON", "OFF", "ALTN" and bar legends of a push
/// button, whose switch is announced itself); valve-in-transit lights, which flash on every normal
/// switch move as the start valve decal does (plan 06); the first officer's master lights, which
/// repeat the captain's; the CDU message, display and offset lights (plan 09 speaks CDU messages);
/// and the gear position lights (the gear call-out speaks the gear). A light another row already
/// announces (the captain's master caution, the IRS align lights, the autopilot disconnect light,
/// the configuration warning) is still listed when it is a fault light: <see cref="WatchSet"/>
/// drops it at start from the definition's own announced names.
/// </summary>
public static class Bbs757LightTable
{
    /// <summary>A fault light: said as it comes on and as it goes out.</summary>
    private static Bbs757Light Fault(string lvar, string label) =>
        new(lvar, label, label + " light on", label + " light off");

    /// <summary>An indication: said as it comes on only.</summary>
    private static Bbs757Light Indication(string lvar, string label) =>
        new(lvar, label, label + " light on", null);

    public static IReadOnlyList<Bbs757Light> All { get; } = new[]
    {
        // Master lights (the captain's; the first officer's repeat them).
        Fault("BBS_CA_MASTER_WARNING", "Master warning"),
        Fault("BBS_CA_MASTER_CAUTION", "Master caution"),

        // Main panel warning and caution lights.
        Fault("B757_MAIN_FIRE_1_DECAL", "Fire"),
        Fault("B757_MAIN_WINDSHEAR_1_DECAL", "Windshear"),
        Fault("B757_MAIN_PULLUP_1_DECAL", "Pull up"),
        Fault("B757_MAIN_GNDPROX_1_DECAL", "Ground proximity"),
        Fault("B757_MAIN_CABINALT_1_DECAL", "Cabin altitude"),
        Fault("B757_MAIN_CONFIG_1_DECAL", "Configuration"),
        Fault("B757_MAIN_BRAKE_SOURCE_1_DECAL", "Brake source"),
        Fault("B757_MAIN_AUTOBRAKES_1_DECAL", "Autobrakes"),
        Fault("B757_MAIN_SPEEDBRAKES_1_DECAL", "Speedbrakes"),
        Fault("B757_MAIN_LEADINGEDGE_1_DECAL", "Leading edge"),
        Fault("B757_MAIN_TRAILINGEDGE_1_DECAL", "Trailing edge"),
        Fault("B757_MAIN_WHEELWELL_1_DECAL", "Wheel well"),
        Fault("B757_MAIN_DOORS_1_DECAL", "Doors"),
        Fault("B757_MAIN_FMC_1_DECAL", "FMC"),
        Fault("B757_MAIN_LENGOILPRESS_1_DECAL", "Left engine oil pressure"),
        Fault("B757_MAIN_RENGOILPRESS_1_DECAL", "Right engine oil pressure"),
        Fault("B757_MAIN_APDISC_1_DECAL", "Autopilot disconnect"),
        Fault("B757_MAIN_ATDISC_1_DECAL", "Autothrottle disconnect"),
        Fault("B757_TERR_FAIL_DECAL3", "Terrain fail"),

        // Fire protection (pedestal).
        Fault("B757_L_ENG_FIRE_ACTIVE", "Left engine fire handle"),
        Fault("B757_R_ENG_FIRE_ACTIVE", "Right engine fire handle"),
        Fault("B757_APU_FIRE_ACTIVE", "APU fire handle"),
        Fault("B757_PED_LENG_OVHT_DECAL", "Left engine overheat"),
        Fault("B757_PED_RENG_OVHT_DECAL", "Right engine overheat"),
        Fault("B757_PED_ENGBLT1_DISCH_DECAL", "Engine bottle 1 discharge"),
        Fault("B757_PED_ENGBLT2_DISCH_DECAL", "Engine bottle 2 discharge"),
        Fault("B757_PED_APUBLT1_DISCH_DECAL", "APU bottle discharge"),
        Fault("B757_PED_FIRE_FWD_DECAL", "Forward cargo fire"),
        Fault("B757_PED_FIRE_AFT_DECAL", "Aft cargo fire"),
        Fault("B757_PED_FAIL_BAR_DECAL", "Fire detection fail"),

        // Overhead warning panel.
        Fault("B757_OVHD_WARNING_ENTRY_DOORS_DECAL", "Entry doors"),
        Fault("B757_OVHD_WARNING_EMER_DOORS_DECAL", "Emergency doors"),
        Fault("B757_OVHD_WARNING_ACCESS_DOORS_DECAL", "Access doors"),
        Fault("B757_OVHD_WARNING_CAPT_PITOT_DECAL", "Captain pitot"),
        Fault("B757_OVHD_WARNING_FO_PITOT_DECAL", "First officer pitot"),
        Fault("B757_OVHD_WARNING_L_AOA_DECAL", "Left alpha vane"),
        Fault("B757_OVHD_WARNING_R_AOA_DECAL", "Right alpha vane"),
        Fault("B757_OVHD_WARNING_L_AUX_DECAL", "Left aux pitot"),
        Fault("B757_OVHD_WARNING_R_AUX_DECAL", "Right aux pitot"),
        Fault("B757_OVHD_WARNING_L_TAT_DECAL", "TAT probe"),
        Fault("B757_OVHD_WARNING_STAB_TRIM_DECAL", "Stabilizer trim"),
        Fault("B757_OVHD_WARNING_UNSCHED_STAB_TRIM_DECAL", "Unscheduled stabilizer trim"),
        Fault("B757_OVHD_WARNING_MACH_SPD_TRIM_DECAL", "Mach speed trim"),
        Fault("B757_OVHD_WARNING_RUDDER_RATIO_DECAL", "Rudder ratio"),
        Fault("B757_OVHD_WARNING_SPOILERS_DECAL", "Spoilers"),
        Fault("B757_OVHD_WARNING_AUTO_SPDBRK_DECAL", "Auto speedbrake"),
        Fault("B757_OVHD_WARNING_ANTI_SKID_DECAL", "Antiskid"),
        Fault("B757_OVHD_WARNING_LAV_SMOKE_DECAL", "Lavatory smoke"),
        Fault("B757_DECAL_LOCK_FAIL", "Flight deck door lock fail"),

        // Electrical.
        Fault("B757_OVHD_STBYPOWER_BAT_DISCH_DECAL", "Battery discharge"),
        Fault("B757_OVHD_STBYPOWER_BAT_OFF_DECAL", "Standby power off"),
        Fault("B757_OVHD_APUGENEXTPWR_L_OFF_DECAL", "APU generator off"),
        Fault("B757_OVHD_GENCONT_L_OFF_DECAL", "Left generator off"),
        Fault("B757_OVHD_GENCONT_R_OFF_DECAL", "Right generator off"),
        Fault("B757_OVHD_GENDRIVEDISC_L_DRIVE_DECAL", "Left generator drive"),
        Fault("B757_OVHD_GENDRIVEDISC_R_DRIVE_DECAL", "Right generator drive"),
        Fault("B757_OVHD_APUGENEXTPWR_L_BUS_BUS_OFF_DECAL", "Left bus off"),
        Fault("B757_OVHD_APUGENEXTPWR_R_BUS_BUS_OFF_DECAL", "Right bus off"),
        Fault("B757_OVHD_APUGENEXTPWR_L_UTIL_BUS_OFF_DECAL", "Left utility bus off"),
        Fault("B757_OVHD_APUGENEXTPWR_R_UTIL_BUS_OFF_DECAL", "Right utility bus off"),
        Fault("B757_OVHD_APUGENEXTPWR_L_BUS_TIE_ISLN_DECAL", "Left bus tie isolated"),
        Fault("B757_OVHD_APUGENEXTPWR_R_BUS_TIE_ISLN_DECAL", "Right bus tie isolated"),
        Fault("B757_OVHD_RAT_UNLKD_DECAL", "RAT unlocked"),

        // APU.
        Fault("B757_OVHD_APU_FAULT_DECAL", "APU fault"),
        Indication("B757_OVHD_APU_RUN_DECAL", "APU run"),

        // Engine control, yaw dampers and limiters.
        Fault("B757_OVHD_L_ELEC_ENG_INOP_DECAL", "Left engine electronic control inop"),
        Fault("B757_OVHD_R_ELEC_ENG_INOP_DECAL", "Right engine electronic control inop"),
        Fault("B757_OVHD_L_YAW_DAMPER_INOP_DECAL", "Left yaw damper inop"),
        Fault("B757_OVHD_R_YAW_DAMPER_INOP_DECAL", "Right yaw damper inop"),
        Fault("B757_OVHD_L_ENG_LIMITER_INOP_DECAL", "Left engine limiter inop"),
        Fault("B757_OVHD_R_ENG_LIMITER_INOP_DECAL", "Right engine limiter inop"),

        // Hydraulics.
        Fault("B757_OVHD_L_HYD_PUMPS_SYS_PRESS_DECAL", "Left hydraulic system pressure"),
        Fault("B757_OVHD_C_HYD_PUMPS_SYS_PRESS_DECAL", "Center hydraulic system pressure"),
        Fault("B757_OVHD_R_HYD_PUMPS_SYS_PRESS_DECAL", "Right hydraulic system pressure"),
        Fault("B757_OVHD_L_HYD_PUMPS_RSVR_DECAL", "Left hydraulic reservoir"),
        Fault("B757_OVHD_C_HYD_PUMPS_RSVR_DECAL", "Center hydraulic reservoir"),
        Fault("B757_OVHD_R_HYD_PUMPS_RSVR_DECAL", "Right hydraulic reservoir"),
        Fault("B757_OVHD_L_HYD_PUMPS_ENG_PRESS_DECAL", "Left engine hydraulic pump pressure"),
        Fault("B757_OVHD_R_HYD_PUMPS_ENG_PRESS_DECAL", "Right engine hydraulic pump pressure"),
        Fault("B757_OVHD_L_HYD_PUMPS_ENG_OVHT_DECAL", "Left engine hydraulic pump overheat"),
        Fault("B757_OVHD_R_HYD_PUMPS_ENG_OVHT_DECAL", "Right engine hydraulic pump overheat"),
        Fault("B757_OVHD_L_HYD_PUMPS_ELEC_PRESS_DECAL", "Left electric hydraulic pump pressure"),
        Fault("B757_OVHD_R_HYD_PUMPS_ELEC_PRESS_DECAL", "Right electric hydraulic pump pressure"),
        Fault("B757_OVHD_L_HYD_PUMPS_ELEC_OVHT_DECAL", "Left electric hydraulic pump overheat"),
        Fault("B757_OVHD_R_HYD_PUMPS_ELEC_OVHT_DECAL", "Right electric hydraulic pump overheat"),
        Fault("B757_OVHD_C_HYD_PUMPS_ENG1_PRESS_DECAL", "Center 1 electric pump pressure"),
        Fault("B757_OVHD_C_HYD_PUMPS_ENG2_PRESS_DECAL", "Center 2 electric pump pressure"),
        Fault("B757_OVHD_C_HYD_PUMPS_ENG1_OVHT_DECAL", "Center 1 electric pump overheat"),
        Fault("B757_OVHD_C_HYD_PUMPS_ENG2_OVHT_DECAL", "Center 2 electric pump overheat"),

        // Fuel.
        Fault("B757_OVHD_L_PUMPS_FWD_PRESS_DECAL", "Left forward fuel pump pressure"),
        Fault("B757_OVHD_L_PUMPS_AFT_PRESS_DECAL", "Left aft fuel pump pressure"),
        Fault("B757_OVHD_R_PUMPS_FWD_PRESS_DECAL", "Right forward fuel pump pressure"),
        Fault("B757_OVHD_R_PUMPS_AFT_PRESS_DECAL", "Right aft fuel pump pressure"),
        Fault("B757_OVHD_C_PUMPS_FWD_PRESS_DECAL", "Center left fuel pump pressure"),
        Fault("B757_OVHD_C_PUMPS_AFT_PRESS_DECAL", "Center right fuel pump pressure"),
        Fault("B757_OVHD_FUEL_CONFIG_DECAL", "Fuel config"),

        // Air conditioning, bleed and pressurization.
        Fault("B757_OVHD_L_PACK_RESET_INOP_DECAL", "Left pack inop"),
        Fault("B757_OVHD_R_PACK_RESET_INOP_DECAL", "Right pack inop"),
        Fault("B757_OVHD_FLT_DK_INOP_DECAL", "Flight deck temperature inop"),
        Fault("B757_OVHD_FWD_CAB_INOP_DECAL", "Forward cabin temperature inop"),
        Fault("B757_OVHD_AFT_CAB_INOP_DECAL", "Aft cabin temperature inop"),
        Fault("B757_OVHD_RECIRC_FAN_L_INOP_DECAL", "Left recirculation fan inop"),
        Fault("B757_OVHD_RECIRC_FAN_R_INOP_DECAL", "Right recirculation fan inop"),
        Fault("B757_OVHD_BLEED_AIR_L_DUCT_LEAK_DECAL", "Left duct leak"),
        Fault("B757_OVHD_BLEED_AIR_R_DUCT_LEAK_DECAL", "Right duct leak"),
        Fault("B757_OVHD_BLEED_AIR_L_BLEED_DECAL", "Left bleed"),
        Fault("B757_OVHD_BLEED_AIR_R_BLEED_DECAL", "Right bleed"),
        Fault("B757_OVHD_BLEED_AIR_L_HI_STAGE_DECAL", "Left high stage"),
        Fault("B757_OVHD_BLEED_AIR_R_HI_STAGE_DECAL", "Right high stage"),
        Fault("B757_OVHD_CAB_ALT_CTRL_AUTO_INOP_DECAL", "Cabin altitude auto inop"),
        Fault("B757_OVHD_EQUIP_COOLING_SMOKE_DECAL", "Equipment cooling smoke"),
        Fault("B757_OVHD_EQUIP_COOLING_OVHT_DECAL", "Equipment cooling overheat"),

        // Window heat.
        Fault("B757_OVHD_WINDOW_HEAT_L_SIDE_INOP_DECAL", "Left side window heat inop"),
        Fault("B757_OVHD_WINDOW_HEAT_L_FWD_INOP_DECAL", "Left forward window heat inop"),
        Fault("B757_OVHD_WINDOW_HEAT_R_FWD_INOP_DECAL", "Right forward window heat inop"),
        Fault("B757_OVHD_WINDOW_HEAT_R_SIDE_INOP_DECAL", "Right side window heat inop"),

        // IRS (the align lights are announced already, BBS757Definition.Readouts).
        Fault("B757_OVHD_IRS_L_ONDC_DECAL", "Left IRS on DC"),
        Fault("B757_OVHD_IRS_C_ONDC_DECAL", "Center IRS on DC"),
        Fault("B757_OVHD_IRS_R_ONDC_DECAL", "Right IRS on DC"),
        Fault("B757_OVHD_IRS_L_DCFAIL_DECAL", "Left IRS DC fail"),
        Fault("B757_OVHD_IRS_C_DCFAIL_DECAL", "Center IRS DC fail"),
        Fault("B757_OVHD_IRS_R_DCFAIL_DECAL", "Right IRS DC fail"),
        Fault("B757_OVHD_IRS_L_DCFAULT_DECAL", "Left IRS fault"),
        Fault("B757_OVHD_IRS_C_DCFAULT_DECAL", "Center IRS fault"),
        Fault("B757_OVHD_IRS_R_DCFAULT_DECAL", "Right IRS fault"),

        // Passenger oxygen.
        Fault("B757_OVHD_PASS_OXY_UNARMED_DECAL", "Passenger oxygen unarmed"),
        Fault("B757_OVHD_PASS_OXY_ON_DECAL", "Passenger oxygen on"),

        // FMC and transponder.
        Fault("B757_CDU1_FAIL_decal", "Left CDU fail"),
        Fault("B757_CDU2_FAIL_decal", "Right CDU fail"),
        Fault("B757_PED_TCAS_XPNDR_FAIL_SWITCH", "Transponder fail"),
    };

    /// <summary>
    /// The L:vars the tablet agent watches: every light's, plus the state var of every map control
    /// a pilot can leave in a position (not a momentary or held button, nor a control with no state
    /// var), less every name in <paramref name="announcedElsewhere"/> (the names of the definition's
    /// announced variables: one owner per spoken row). Each name once. Pure.
    /// </summary>
    public static IReadOnlyList<string> WatchSet(IEnumerable<Bbs757Light> lights, IEnumerable<Bbs757Control> controls, ISet<string> announcedElsewhere)
    {
        var set = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || announcedElsewhere.Contains(name)) return;
            if (seen.Add(name)) set.Add(name);
        }
        foreach (var light in lights) Add(light.LVar);
        foreach (var control in controls)
        {
            if (Bbs757Write.IsMomentary(control) || string.IsNullOrWhiteSpace(control.StateVar)) continue;
            Add(control.StateVar!);
        }
        return set;
    }
}
