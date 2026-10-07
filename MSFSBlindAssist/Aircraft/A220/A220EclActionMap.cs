namespace MSFSBlindAssist.Aircraft.A220;

/// <summary>Why an ECL sensed variable is deliberately NOT auto-actuatable.</summary>
public enum A220EclExclusion
{
    /// <summary>Mapped — actuate via the def control key + target value.</summary>
    None,
    /// <summary>Hardware axis / physical lever or stick input MSFSBA must not fight
    /// (thrust levers, sidestick, intermediate speedbrake detents, TOGA paddles).</summary>
    HardwareAxis,
    /// <summary>Guarded destructive/irreversible action (fire pushes, extinguisher
    /// bottles, IDG disconnect, RAT deploy, pax oxygen deploy, EDM) — the pilot
    /// operates these deliberately from the panel, never via "do this item".</summary>
    GuardedDestructive,
    /// <summary>"As required" judgment item — present-and-confirm only.</summary>
    Judgment,
    /// <summary>No MSFSBA control exists for this system yet (doors, cockpit door
    /// panel, reversion panel, avionics cooling, CVR, composite multi-switch
    /// states, unverified autobrake RTO detent…).</summary>
    NotYetMapped,
}

/// <summary>One ECL sensed variable's actuation: a def control key + target value,
/// or an explicit exclusion reason.</summary>
public readonly record struct A220EclAction(string? ControlKey, double Value, A220EclExclusion Exclusion)
{
    public bool IsMapped => Exclusion == A220EclExclusion.None && ControlKey != null;
    public static A220EclAction Map(string key, double value) => new(key, value, A220EclExclusion.None);
    public static A220EclAction Excluded(A220EclExclusion reason) => new(null, 0, reason);
}

/// <summary>
/// ECL "first officer" actuation map (docs/a220-plan.md W6): every one of the 215
/// <see cref="SynapticA220EclData.EclVariableNames"/> either maps to a
/// <see cref="SynapticA220Definition"/> control key (driven through
/// HandleUIVariableSet, so timing quirks like the APU hold-to-start are shared with
/// the panel) or carries an explicit exclusion. Coverage-pinned by
/// A220EclActionMapTests — zero unaccounted entries.
///
/// Scope rulings (2026-07-27): engine start + APU start ARE mapped; thrust levers,
/// sidestick/flight-control checks and fire handles/bottles are excluded.
/// MSFSBA never ticks a checklist box — it actuates the mapped control and lets the
/// aircraft's own sensing confirm.
/// </summary>
public static class A220EclActionMap
{
    public static bool TryGet(string eclVariableName, out A220EclAction action)
        => Actions.TryGetValue(eclVariableName, out action);

    public static readonly IReadOnlyDictionary<string, A220EclAction> Actions = Build();

    private static Dictionary<string, A220EclAction> Build()
    {
        var m = new Dictionary<string, A220EclAction>(StringComparer.Ordinal);
        void Map(string ecl, string key, double value) => m[ecl] = A220EclAction.Map(key, value);
        void Excl(string ecl, A220EclExclusion reason) => m[ecl] = A220EclAction.Excluded(reason);

        // ---- Interior / misc ------------------------------------------------
        Map("DOME_ON", "A22X_DOME_LIGHTS", 1);
        Map("PROBE_HEAT_ON", "A22X_PROBE_HEAT", 1);
        Map("WINDOW_HEAT_L_WINDOW_OFF", "A22X_L_WINDOW_HEAT_OFF", 1);
        Map("WINDOW_HEAT_L_WSHLD_OFF", "A22X_L_WSHLD_HEAT_OFF", 1);
        Map("WINDOW_HEAT_R_WSHLD_OFF", "A22X_R_WSHLD_HEAT_OFF", 1);
        Map("WINDOW_HEAT_R_WINDOW_OFF", "A22X_R_WINDOW_HEAT_OFF", 1);
        Map("PFCC1_OFF", "A22X_PFCC_1_OFF", 1);
        Map("PFCC2_OFF", "A22X_PFCC_2_OFF", 1);
        Map("PFCC3_OFF", "A22X_PFCC_3_OFF", 1);
        Excl("CVR_TEST", A220EclExclusion.NotYetMapped);   // no CVR panel control in the def
        Excl("CVR_ERASE", A220EclExclusion.NotYetMapped);
        Map("ANNUN_DIM", "A22X_ANNUN_LIGHTS", 0);
        Map("ANNUN_BRT", "A22X_ANNUN_LIGHTS", 1);
        Map("ANNUN_STORM", "A22X_ANNUN_LIGHTS", 2);
        Excl("SERV_INT_ON", A220EclExclusion.NotYetMapped); // service interphone not modeled as a control
        Map("AURAL_WARN_INHIB", "A22X_AURAL_INHIBIT", 1);

        // ---- Hydraulics -----------------------------------------------------
        Map("HYD_1_SOV_CLSD", "A22X_HYD_1_SOV", 0);        // labels: Closed=0 / Open=1
        Map("HYD_2_SOV_CLSD", "A22X_HYD_2_SOV", 0);
        Map("HYD_3A_OFF", "A22X_ACMP_3A", 0);
        Map("HYD_3A_AUTO", "A22X_ACMP_3A", 1);
        Map("HYD_3A_ON", "A22X_ACMP_3A", 2);
        Map("HYD_3B_OFF", "A22X_ACMP_3B", 0);
        Map("HYD_3B_AUTO", "A22X_ACMP_3B", 1);
        Map("HYD_3B_ON", "A22X_ACMP_3B", 2);
        Map("HYD_2B_OFF", "A22X_ACMP_2B", 0);
        Map("HYD_2B_AUTO", "A22X_ACMP_2B", 1);
        Map("HYD_2B_ON", "A22X_ACMP_2B", 2);
        Map("HYD_PTU_OFF", "A22X_PTU", 0);
        Map("HYD_PTU_AUTO", "A22X_PTU", 1);
        Map("HYD_PTU_ON", "A22X_PTU", 2);

        // ---- Electrical -----------------------------------------------------
        Map("ELEC_BUS_ISOL_MAIN", "A22X_BUS_ISOLATION", 0);
        Map("ELEC_BUS_ISOL_AUTO", "A22X_BUS_ISOLATION", 1);
        Map("ELEC_BUS_ISOL_ESS", "A22X_BUS_ISOLATION", 2);
        Map("CABIN_PWR_OFF", "A22X_CABIN_POWER_OFF", 1);
        Excl("RAT_GEN_ON", A220EclExclusion.GuardedDestructive);          // RAT deploy is irreversible in flight
        Map("BATT_1_OFF", "A22X_BATTERY_1", 0);
        Map("BATT_1_AUTO", "A22X_BATTERY_1", 1);           // physical switch is OFF/AUTO; stock var reads 0/1
        Map("BATT_2_OFF", "A22X_BATTERY_2", 0);
        Map("BATT_2_AUTO", "A22X_BATTERY_2", 1);
        Map("ELEC_L_GEN_OFF", "A22X_L_GEN_OFF", 1);        // "Off" switches: 1 = selected OFF
        Excl("ELEC_L_DISC_DISCONNECT", A220EclExclusion.GuardedDestructive); // IDG disconnect is irreversible
        Map("ELEC_R_GEN_OFF", "A22X_R_GEN_OFF", 1);
        Excl("ELEC_R_DISC_DISCONNECT", A220EclExclusion.GuardedDestructive);
        Map("ELEC_EXT_PWR_ON", "A22X_EXT_PWR", 1);
        Map("APU_GEN_OFF", "A22X_APU_GEN_OFF", 1);

        // ---- APU (start IS in scope — hold-to-start lives in the control write) --
        Map("APU_OFF", "A22X_APU_SWITCH", 0);
        Map("APU_RUN", "A22X_APU_SWITCH", 1);
        Map("APU_START", "A22X_APU_SWITCH", 2);

        // ---- TAWS -----------------------------------------------------------
        Map("TAWS_GEAR_INHIB", "A22X_TAWS_GEAR_INHIBIT", 1);
        Map("TAWS_TERR_INHIB", "A22X_TAWS_TERRAIN_INHIBIT", 1);
        Map("TAWS_FLAP_INHIB", "A22X_TAWS_FLAPS_INHIBIT", 1);
        Map("TAWS_GS_CNCL", "A22X_TAWS_GS_INHIBIT", 1);

        // ---- Fuel -----------------------------------------------------------
        Map("MAN_XFR_OFF", "A22X_MANUAL_TRANSFER", 0);     // labels: Off/Right/Center/Left
        Map("MAN_XFR_L", "A22X_MANUAL_TRANSFER", 3);
        Map("MAN_XFR_R", "A22X_MANUAL_TRANSFER", 1);
        Map("MAN_XFR_CTR", "A22X_MANUAL_TRANSFER", 2);
        Map("GRAV_XFR_ON", "A22X_GRAVITY_TRANSFER", 1);
        Map("L_BOOST_PUMP_OFF", "A22X_L_BOOST_PUMP", 0);
        Map("L_BOOST_PUMP_AUTO", "A22X_L_BOOST_PUMP", 1);
        Map("L_BOOST_PUMP_ON", "A22X_L_BOOST_PUMP", 2);
        Map("R_BOOST_PUMP_OFF", "A22X_R_BOOST_PUMP", 0);
        Map("R_BOOST_PUMP_AUTO", "A22X_R_BOOST_PUMP", 1);
        Map("R_BOOST_PUMP_ON", "A22X_R_BOOST_PUMP", 2);
        Excl("ACT_XFR_OFF", A220EclExclusion.NotYetMapped);     // ACT transfer/SOV panel not in the documented L:var surface
        Excl("ACT_XFR_AUTO", A220EclExclusion.NotYetMapped);
        Excl("ACT_XFR_BACKUP", A220EclExclusion.NotYetMapped);
        Excl("ACT_SOV_CLOSE", A220EclExclusion.NotYetMapped);
        Excl("ACT_SOV_AUTO", A220EclExclusion.NotYetMapped);
        Excl("ACT_SOV_OPEN", A220EclExclusion.NotYetMapped);

        // ---- Air / bleed ----------------------------------------------------
        Map("MAN_TEMP_ON", "A22X_MANUAL_TEMP", 1);
        Map("FWD_CARGO_OFF", "A22X_FWD_CARGO_AIR", 0);
        Map("FWD_CARGO_VENT", "A22X_FWD_CARGO_AIR", 1);
        Map("FWD_CARGO_LO_HEAT", "A22X_FWD_CARGO_AIR", 2);
        Map("FWD_CARGO_HI_HEAT", "A22X_FWD_CARGO_AIR", 3);
        Map("AFT_CARGO_OFF", "A22X_AFT_CARGO_AIR", 0);
        Map("AFT_CARGO_VENT", "A22X_AFT_CARGO_AIR", 1);
        Map("TRIM_AIR_OFF", "A22X_TRIM_AIR_OFF", 1);
        Map("PACK_FLOW_HI", "A22X_PACK_FLOW", 1);
        Map("RECIRC_OFF", "A22X_RECIRC_OFF", 1);
        Map("RAM_AIR_OPEN", "A22X_RAM_AIR", 1);
        Map("XBLEED_MAN_CLSD", "A22X_CROSSBLEED", 0);
        Map("XBLEED_AUTO", "A22X_CROSSBLEED", 1);
        Map("XBLEED_MAN_OPEN", "A22X_CROSSBLEED", 2);
        Map("L_PACK_OFF", "A22X_L_PACK_OFF", 1);
        Map("L_BLEED_OFF", "A22X_L_BLEED_OFF", 1);
        Map("R_PACK_OFF", "A22X_R_PACK_OFF", 1);
        Map("R_BLEED_OFF", "A22X_R_BLEED_OFF", 1);
        Map("APU_BLEED_OFF", "A22X_APU_BLEED_OFF", 1);

        // ---- Anti-ice -------------------------------------------------------
        Map("L_COWL_OFF", "A22X_L_COWL_AI", 0);
        Map("L_COWL_AUTO", "A22X_L_COWL_AI", 1);
        Map("L_COWL_ON", "A22X_L_COWL_AI", 2);
        Map("WING_OFF", "A22X_WING_AI", 0);
        Map("WING_AUTO", "A22X_WING_AI", 1);
        Map("WING_ON", "A22X_WING_AI", 2);
        Map("R_COWL_OFF", "A22X_R_COWL_AI", 0);
        Map("R_COWL_AUTO", "A22X_R_COWL_AI", 1);
        Map("R_COWL_ON", "A22X_R_COWL_AI", 2);

        // ---- ELT / fire (fire pushes + bottles excluded per user ruling) ----
        Map("ELT_TEST", "A22X_ELT", 0);
        Map("ELT_ARM", "A22X_ELT", 1);
        Map("ELT_ON", "A22X_ELT", 2);
        Excl("FWD_CARGO_FIRE_PRESSED", A220EclExclusion.GuardedDestructive);
        Excl("AFT_CARGO_FIRE_PRESSED", A220EclExclusion.GuardedDestructive);
        Excl("CARGO_FIRE_BTL_PRESSED", A220EclExclusion.GuardedDestructive);

        // ---- Avionics cooling (no def controls yet) -------------------------
        Excl("INLET_OFF", A220EclExclusion.NotYetMapped);
        Excl("EXHAUST_VLV_ONLY", A220EclExclusion.NotYetMapped);
        Excl("EXHAUST_AUTO", A220EclExclusion.NotYetMapped);
        Excl("EXHAUST_ON", A220EclExclusion.NotYetMapped);

        // ---- Pressurization / emergency ------------------------------------
        Map("EMER_DEPRESS_ON", "A22X_EMER_DEPRESS", 1);
        Map("AUTO_PRESS_MAN", "A22X_MAN_PRESS", 1);
        Map("DITCHING_ON", "A22X_DITCHING", 1);
        Excl("PASS_OXY_DPLY", A220EclExclusion.GuardedDestructive);  // mask deploy is irreversible
        Map("EVAC_CMD_ON", "A22X_EVAC", 1);
        Map("EMER_LIGHTS_OFF", "A22X_EMERGENCY_LIGHTS", 0);
        Map("EMER_LIGHTS_ARM", "A22X_EMERGENCY_LIGHTS", 1);
        Map("EMER_LIGHTS_ON", "A22X_EMERGENCY_LIGHTS", 2);
        Excl("L_ENG_FIRE_PRESSED", A220EclExclusion.GuardedDestructive);
        Excl("R_ENG_FIRE_PRESSED", A220EclExclusion.GuardedDestructive);
        Excl("APU_FIRE_PRESSED", A220EclExclusion.GuardedDestructive);

        // ---- Exterior lights ------------------------------------------------
        Map("NAV_LTS_ON", "A22X_NAV_LIGHTS", 1);
        Map("BEACON_LTS_ON", "A22X_BEACON_LIGHTS", 1);
        Map("STROBE_LTS_ON", "A22X_STROBE_LIGHTS", 1);
        Excl("EXTERNAL_LTS_ON", A220EclExclusion.NotYetMapped);      // composite of several switches
        Map("LOGO_LTS_ON", "A22X_LOGO_LIGHTS", 1);
        Map("WING_INSP_LTS_ON", "A22X_WING_INSP_LIGHTS", 1);
        Map("TAXI_LTS_OFF", "A22X_TAXI_LIGHTS", 0);
        Map("TAXI_LTS_NARROW", "A22X_TAXI_LIGHTS", 1);
        Map("TAXI_LTS_WIDE", "A22X_TAXI_LIGHTS", 2);
        Map("L_LDG_LTS_ON", "A22X_L_LANDING_LIGHTS", 1);
        Map("NOSE_LDG_LTS_ON", "A22X_NOSE_LANDING_LIGHTS", 1);
        Map("R_LDG_LTS_ON", "A22X_R_LANDING_LIGHTS", 1);
        Excl("LANDING_LTS_ON", A220EclExclusion.NotYetMapped);       // composite of the three landing lights

        // ---- Signs ----------------------------------------------------------
        Map("SEAT_BELTS_OFF", "A22X_SEAT_BELT_LIGHTS", 0);
        Map("SEAT_BELTS_AUTO", "A22X_SEAT_BELT_LIGHTS", 1);
        Map("SEAT_BELTS_ON", "A22X_SEAT_BELT_LIGHTS", 2);
        Map("NO_PED_OFF", "A22X_NO_PED_LIGHTS", 0);
        Map("NO_PED_AUTO", "A22X_NO_PED_LIGHTS", 1);
        Map("NO_PED_ON", "A22X_NO_PED_LIGHTS", 2);

        // ---- Gear / brakes --------------------------------------------------
        Map("ALTN_GEAR_NORM", "A22X_ALTERNATE_GEAR", 0);
        Map("ALTN_GEAR_DOWN", "A22X_ALTERNATE_GEAR", 1);
        Map("GEAR_AURAL_CNCL", "A22X_GEAR_AURAL_CANCEL", 1);         // momentary pulse
        Map("NOSE_STEER_OFF", "A22X_NOSE_STEER_OFF", 1);
        Map("ALTN_BRAKE_ON", "A22X_ALTERNATE_BRAKE", 1);

        // ---- Engine start (in scope per user ruling) ------------------------
        Map("ENG_START_L_ENG_CRANK", "A22X_ENG_START_MODE", 0);
        Map("ENG_START_AUTO", "A22X_ENG_START_MODE", 1);
        Map("ENG_START_R_ENG_CRANK", "A22X_ENG_START_MODE", 2);
        Map("CONT_IGNITION_ON", "A22X_CONT_IGNITION", 1);
        Map("L_ENG_RUN_ON", "A22X_ENG_MASTER_1", 1);
        Map("R_ENG_RUN_ON", "A22X_ENG_MASTER_2", 1);

        // ---- Cockpit door / misc -------------------------------------------
        Excl("DOOR_UNLOCK_PRESSED", A220EclExclusion.NotYetMapped);  // cockpit door panel not in the def
        Excl("EMER_ACCESS_DENY", A220EclExclusion.NotYetMapped);
        Map("PARK_BRAKE_ON", "A22X_PARKING_BRAKE", 1);
        Excl("L_SIDESTICK_PTY", A220EclExclusion.HardwareAxis);      // sidestick/flight-control check
        Excl("R_SIDESTICK_PTY", A220EclExclusion.HardwareAxis);

        // ---- Thrust levers (hardware axis — never fight the pilot's hand) ---
        Excl("L_THRUST_LEVER_IDLE", A220EclExclusion.HardwareAxis);
        Excl("R_THRUST_LEVER_IDLE", A220EclExclusion.HardwareAxis);
        Excl("L_THRUST_LEVER_MAX", A220EclExclusion.HardwareAxis);
        Excl("R_THRUST_LEVER_MAX", A220EclExclusion.HardwareAxis);
        Excl("L_THRUST_LEVER_REV", A220EclExclusion.HardwareAxis);
        Excl("R_THRUST_LEVER_REV", A220EclExclusion.HardwareAxis);
        Excl("THRUST_LEVERS_IDLE", A220EclExclusion.HardwareAxis);
        Excl("THRUST_LEVERS_MAX", A220EclExclusion.HardwareAxis);
        Excl("THRUST_LEVERS_REV", A220EclExclusion.HardwareAxis);

        // ---- Speedbrake lever (0 = retracted, 5 = full: the two stock-event
        // endpoints; intermediate detents need the hardware axis) --------------
        Map("SPOILER_LEVER_0", "A22X_SPOILERS_RETRACT", 1);
        Excl("SPOILER_LEVER_1", A220EclExclusion.HardwareAxis);
        Excl("SPOILER_LEVER_2", A220EclExclusion.HardwareAxis);
        Excl("SPOILER_LEVER_3", A220EclExclusion.HardwareAxis);
        Excl("SPOILER_LEVER_4", A220EclExclusion.HardwareAxis);
        Map("SPOILER_LEVER_5", "A22X_SPOILERS_DEPLOY", 1);

        // ---- Flaps / gear lever / autobrake ---------------------------------
        Map("SLAT_FLAP_LEVER_0", "A22X_FLAP_LEVER", 0);
        Map("SLAT_FLAP_LEVER_1", "A22X_FLAP_LEVER", 1);
        Map("SLAT_FLAP_LEVER_2", "A22X_FLAP_LEVER", 2);
        Map("SLAT_FLAP_LEVER_3", "A22X_FLAP_LEVER", 3);
        Map("SLAT_FLAP_LEVER_4", "A22X_FLAP_LEVER", 4);
        Map("SLAT_FLAP_LEVER_5", "A22X_FLAP_LEVER", 5);
        Map("ALTN_FLAP_SW", "A22X_ALTERNATE_FLAP", 1);
        Map("LANDING_GEAR_LEVER_UP", "A22X_GEAR_LEVER", 0);
        Map("LANDING_GEAR_LEVER_DOWN", "A22X_GEAR_LEVER", 1);
        Map("AUTOBRAKE_LO", "A22X_AUTOBRAKE", 1);
        Map("AUTOBRAKE_MED", "A22X_AUTOBRAKE", 2);
        Map("AUTOBRAKE_HI", "A22X_AUTOBRAKE", 3);
        Excl("AUTOBRAKE_RTO", A220EclExclusion.NotYetMapped);        // RTO detent value unverified in-sim (docs/a220.md)
        Map("AUTOBRAKE_OFF", "A22X_AUTOBRAKE", 0);

        // ---- Doors (EFB Ground Equipment / GSX territory, not panel controls) --
        Excl("FWD_PAX_DOOR_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("FWD_SERV_DOOR_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("AFT_PAX_DOOR_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("AFT_SERV_DOOR_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("L_OWEED_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("R_OWEED_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("L_AFT_OWEED_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("R_AFT_OWEED_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("FWD_CARGO_DOOR_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("AFT_CARGO_DOOR_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("FWD_EQUIP_BAY_DOOR_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("MID_EQUIP_BAY_DOOR_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("AFT_EQUIP_BAY_DOOR_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("CABIN_DOORS_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("CARGO_DOORS_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("OWEED_DOORS_CLOSED", A220EclExclusion.NotYetMapped);
        Excl("ALL_DOORS_CLOSED", A220EclExclusion.NotYetMapped);

        // ---- Reversion / display panels, autoflight, misc -------------------
        Excl("L_CTP_INHIBIT", A220EclExclusion.NotYetMapped);
        Excl("R_CTP_INHIBIT", A220EclExclusion.NotYetMapped);
        Excl("DSPL_TUNE_INHIBIT", A220EclExclusion.NotYetMapped);
        Excl("AP_DISENGAGED", A220EclExclusion.NotYetMapped);        // AP disconnect is a hotkey/dialog action, not a panel key
        Excl("AT_DISCONNECTED", A220EclExclusion.NotYetMapped);
        Excl("EDM_SELECTED", A220EclExclusion.GuardedDestructive);   // emergency descent mode — never auto-engage
        Excl("RSP_DISPLAY_REV", A220EclExclusion.NotYetMapped);
        Excl("RSP_DISPLAY_SWAP", A220EclExclusion.NotYetMapped);
        Excl("RSP_DISPLAY_NORM", A220EclExclusion.NotYetMapped);
        Excl("STEEP_APPR_SELECTED", A220EclExclusion.NotYetMapped);
        Excl("TOGA_SELECTED", A220EclExclusion.HardwareAxis);        // TOGA paddles on the thrust levers (Ctrl+P dialog covers it manually)
        Excl("APU_BTL_SW_PRESSED", A220EclExclusion.GuardedDestructive);
        Excl("L_ENG_BTL_1_SW_PRESSED", A220EclExclusion.GuardedDestructive);
        Excl("L_ENG_BTL_2_SW_PRESSED", A220EclExclusion.GuardedDestructive);
        Excl("R_ENG_BTL_1_SW_PRESSED", A220EclExclusion.GuardedDestructive);
        Excl("R_ENG_BTL_2_SW_PRESSED", A220EclExclusion.GuardedDestructive);

        return m;
    }
}
