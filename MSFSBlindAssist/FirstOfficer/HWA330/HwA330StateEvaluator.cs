using System;
using System.Collections.Generic;
using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.HWA330;

/// <summary>
/// Reads HeadwindSim A330-900neo control state from the SimConnect cache for the First
/// Officer's checklist auto-detection. ORIGINATES as a duplicate of
/// <see cref="FBWA320.FbwA320StateEvaluator"/> — the A339X is built on the A32NX systems,
/// so most poll keys keep their <c>A32NX_</c> names — but the poll list is this airframe's,
/// confirmed against <see cref="MSFSBlindAssist.Aircraft.HeadwindA330Definition.GetVariables"/>,
/// and carries the A339X divergences (nav/logo, SD page enum, 3-position seatbelt switch,
/// ganged landing lights, ceiling/map pots). Never read it as the A320's.
/// </summary>
public sealed class HwA330StateEvaluator : LVarStateEvaluator
{
    private static readonly string[] PollFields =
    {
        // Batteries (A320 has only 2 — no ESS/APU battery like the A380's 4).
        "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO",
        // External power (single un-indexed PB on the A320, unlike the A380's 4).
        "A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON", "EXT_PWR_AVAILABLE",
        "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB",
        "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB",
        "PUSH_OVHD_OXYGEN_CREW",
        // Lights: NAV+LOGO are one combined switch on the A320 (A32NX_LIGHTS_NAV_LOGO);
        // strobe is LIGHTING_STROBE_0; beacon is the stock "LIGHT BEACON" simvar.
        "A32NX_LIGHTS_NAV_LOGO", "LIGHTING_STROBE_0", "LIGHT BEACON", "WING_LIGHTS_SET",
        "LANDING_LIGHTS_ON_THIRD_PARTY",
        "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION", "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION",
        // Wing anti-ice reads back the same L:var the PB writes (never _SYSTEM_*, which is a
        // Rust per-frame OUTPUT). Engine anti-ice state = the stock ENG_ANTI_ICE:n readouts.
        "A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION", "ENG_ANTI_ICE:1", "ENG_ANTI_ICE:2",
        "A32NX_OVHD_COND_PACK_1_PB_IS_ON", "A32NX_OVHD_COND_PACK_2_PB_IS_ON",
        "A32NX_KNOB_OVHD_AIRCOND_XBLEED_Position", "A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_Position",
        "A32NX_OVHD_COND_HOT_AIR_PB_IS_ON", "A32NX_OVHD_PRESS_MODE_SEL_PB_IS_AUTO",
        // Task 7 additions: checklist Auto items read these state-readback fields, which
        // differ from (or are additional to) the write keys the flow uses.
        "LIGHT WING", "CABIN SEATBELTS ALERT SWITCH",
        "A32NX_SWITCH_TCAS_TRAFFIC_POSITION", "A32NX_SWITCH_TCAS_POSITION", "A32NX_TRANSPONDER_MODE",
        // A339X reads the stock LIGHT LANDING:2 — L:LIGHTING_LANDING_2 is the A32NX
        // Retractable-switch position, which this airframe never writes.
        "LIGHT TAXI:2", "LIGHTING_LANDING_1", "LIGHT LANDING:2",
        "A32NX_EFIS_L_LS_BUTTON_IS_ON", "A32NX_EFIS_R_LS_BUTTON_IS_ON",
        "A32NX_PARK_BRAKE_LEVER_POS", "A32NX_SPOILERS_ARMED",
        "GEAR_HANDLE_POSITION", "A32NX_FLAPS_HANDLE_INDEX", "ENGINE_MODE_SELECTOR",
        // Engine masters: dict keys (cache is keyed by GetVariables() key, not the underlying
        // SimVar Name "FUELSYSTEM VALVE SWITCH:n").
        "ENGINE_1_MASTER", "ENGINE_2_MASTER", "A32NX_ENGINE_STATE:1", "A32NX_ENGINE_STATE:2",
        "XMLVAR_A320_WeatherRadar_Sys", "XMLVAR_A320_WeatherRadar_Mode", "A32NX_SWITCH_RADAR_PWS_POSITION",
        "FUEL_PUMP_L1", "FUEL_PUMP_L2", "FUEL_PUMP_R1", "FUEL_PUMP_R2", "FUEL_PUMP_C1", "FUEL_PUMP_C2",
        "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
        "A32NX_OVHD_APU_START_PB_IS_ON", "A32NX_OVHD_APU_START_PB_IS_AVAILABLE",
        "A32NX_COCKPIT_DOOR_LOCKED", "A32NX_AUTOBRAKES_ARMED_MODE",
        "A32NX_EFIS_L_ND_MODE",
        "A32NX_EFIS_L_ND_RANGE",
        "A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE",
        "A32NX_SWITCH_ATC_ALT",
        "A32NX_FMGC_1_FD_ENGAGED", "A32NX_FMGC_2_FD_ENGAGED",
        // The FD PUSH events toggle, so FlyByWireA320Definition.FlightDirectorPushEvent
        // refuses to push a button whose light already reads the target — and refuses again
        // when the light is UNKNOWN. These two are what keep it known: they are OnRequest and
        // not IsAnnounced, so this poll is their only route onto the cache the guard reads.
        // Without them the guard was permanently blind for any pilot who had not opened the
        // Ctrl+P autopilot window, and the FD steps silently did nothing.
        "A32NX_FCU_EFIS_L_FD_LIGHT_ON", "A32NX_FCU_EFIS_R_FD_LIGHT_ON",
        "A32NX_OVHD_INTLT_ANN", "A32NX_OVHD_INTLT_DOME", "A32NX_STBY_COMPASS_LIGHT_TOGGLE",
        // Auto-flap schedule inputs (speed tape + flaps handle). A32NX_SPEEDS_LANDING_CONF3
        // was previously absent here AND unregistered in the definition, so `conf3` was
        // permanently NaN and the CONF 3 landing cap could never engage (fixed 2026-08-30).
        "A32NX_SPEEDS_GD", "A32NX_SPEEDS_S", "A32NX_SPEEDS_F", "A32NX_SPEEDS_VFEN",
        "A32NX_SPEEDS_LANDING_CONF3",
        "WIPER_LEFT", "WIPER_RIGHT",
        "A32NX_RCDR_GROUND_CONTROL_ON",
        // Gear legs (FbwA320GearConfirmation)
        "A32NX_GEAR_CENTER_POSITION", "A32NX_GEAR_LEFT_POSITION", "A32NX_GEAR_RIGHT_POSITION",
        // Read-back live values (the Airbus card's "SET"/"CHECKED" lines speak the value the
        // First Officer reads). Polling a Continuous var is harmless; the OnRequest ones need it.
        "FUEL_QUANTITY_KG", "A32NX_EFB_USING_METRIC_UNIT",
        "PFD_V1", "PFD_VR", "PFD_V2", "A32NX_AIRLINER_TO_FLEX_TEMP",
        "A32NX_FAC_1_RUDDER_TRIM_POS", "A32NX_GPWS_FLAPS3",
        // A339X baro: the FBW EIS baro words (A32NX_FCU_LEFT_EIS_BARO*) never reach the cache on
        // this airframe (HeadwindA330Definition notes), so the baro line reads the stock
        // Kohlsman settings and the FCU unit preference instead.
        "KOHLSMAN SETTING STD:1", "KOHLSMAN SETTING MB:1", "A32NX_FCU_EFIS_L_BARO_IS_INHG",
    };

    public override IReadOnlyList<string> OnRequestPollFields => PollFields;

    /// <summary>
    /// FAC 1 rudder-trim word (ARINC429 degrees, positive = nose-left): 1 when the trim is
    /// neutral (under 0.1 degrees, the panel's own "Neutral" threshold), 0 when it is not, NaN
    /// when the word carries no data (failure warning / no computed data, and the 0 a
    /// never-written L:var reads) or was never read, so an unread trim can never tick the line.
    /// </summary>
    internal static double DecodeRudderTrimNeutral(double raw)
    {
        if (double.IsNaN(raw)) return double.NaN;
        var word = new Arinc429Word(raw);
        return word.HasData ? (Math.Abs(word.Value) < 0.1 ? 1.0 : 0.0) : double.NaN;
    }

    /// <summary>Millibars per inch of mercury: the inverse of the 0.0295299830714 the A339X
    /// altimeter readout (<c>HeadwindA330Definition.HwBaroPhrase</c>) multiplies by.</summary>
    private const double HpaPerInHg = 33.8639;

    /// <summary>Captain-side baro for the read-back: STD, or the QNH from the stock Kohlsman
    /// settings in the unit the FCU shows. Null when unknown.</summary>
    public string? BaroText() => BaroFrom(GetValue("KOHLSMAN SETTING STD:1"),
        GetValue("KOHLSMAN SETTING MB:1"), GetValue("A32NX_FCU_EFIS_L_BARO_IS_INHG"));

    /// <summary>The baro phrase from the three raw reads: the STD flag (0 QNH / 1 Std), the QNH in
    /// millibars, and the FCU unit (0 hPa / 1 inHg). STD needs no value; otherwise the QNH and the
    /// unit must both be known, and a QNH of 0 is an altimeter that has not been set up yet.</summary>
    internal static string? BaroFrom(double std, double mb, double inHgFlag)
    {
        if (double.IsNaN(std)) return null;
        if (std > 0.5) return AirbusReadbackFormat.Baro(1, double.NaN, false);
        if (double.IsNaN(mb) || mb <= 0 || double.IsNaN(inHgFlag)) return null;
        bool inHg = inHgFlag > 0.5;
        return AirbusReadbackFormat.Baro(std, inHg ? mb / HpaPerInHg : mb, inHg);
    }

    /// <summary>Total fuel in the EFB's unit (A32NX_EFB_USING_METRIC_UNIT 1 = kg, 0 = lb);
    /// <c>FUEL_QUANTITY_KG</c> is read in kilograms. Null when the fuel or the unit is unknown.</summary>
    public string? FuelText()
    {
        double metric = GetValue("A32NX_EFB_USING_METRIC_UNIT");
        if (double.IsNaN(metric)) return null;
        return AirbusReadbackFormat.FuelQuantity(GetValue("FUEL_QUANTITY_KG"), pounds: metric < 0.5);
    }

    /// <summary>1 when FlyByWire's own engine state (0 Off / 1 On / 2 Starting / 3 Shutting
    /// down) reads On, 0 for any other state, NaN when the state is unknown.</summary>
    internal static double RunningFrom(double state)
        => double.IsNaN(state) ? double.NaN : Math.Abs(state - 1) < 0.5 ? 1.0 : 0.0;

    /// <summary>SimBrief takeoff flaps (1..3) as the A32NX flap handle index (same numbering),
    /// or -1 when not loaded / out of range — the Fenix evaluator's twin.</summary>
    public int TakeoffFlapsLeverIndex()
    {
        int f = GetTakeoffFlaps();
        return f is >= 1 and <= 3 ? f : -1;
    }

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        switch (field)
        {
            // FlyByWire's own engine state: 0 Off / 1 On / 2 Starting / 3 Shutting down.
            // "Running" = On. Used instead of a raw N2 threshold (the A339X's idle N2 is unknown).
            case "FO_ENG1_RUNNING":
                value = RunningFrom(GetValue("A32NX_ENGINE_STATE:1"));
                return true;
            case "FO_ENG2_RUNNING":
                value = RunningFrom(GetValue("A32NX_ENGINE_STATE:2"));
                return true;
            // Radar ON (system 1 or 2 — 1 is OFF) AND predictive windshear AUTO (1).
            case "FO_WXR_ON_AUTO":
                value = Both(GetValue("XMLVAR_A320_WeatherRadar_Sys"), GetValue("A32NX_SWITCH_RADAR_PWS_POSITION"),
                    (sys, pws) => Math.Abs(sys - 1) > 0.5 && Math.Abs(pws - 1) < 0.5);
                return true;
            // Radar OFF (1) AND predictive windshear OFF (0).
            case "FO_WXR_PWS_OFF":
                value = Both(GetValue("XMLVAR_A320_WeatherRadar_Sys"), GetValue("A32NX_SWITCH_RADAR_PWS_POSITION"),
                    (sys, pws) => Math.Abs(sys - 1) < 0.5 && pws < 0.5);
                return true;
            // The memo's SIGNS line: seat-belt sign lit AND no smoking ON or AUTO (0 On / 1 Auto / 2 Off).
            // The sign LAMP, never the 3-position switch (the A339X's 0 On / 1 Auto / 2 Off).
            case "FO_SIGNS_ON":
                value = Both(GetValue("CABIN SEATBELTS ALERT SWITCH"), GetValue("XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION"),
                    (belts, smoking) => belts > 0.5 && smoking < 1.5);
                return true;
            // The LDG memo's flaps line: FULL, or 3 when the GPWS LDG FLAP 3 switch is on.
            case "FO_LDG_FLAPS_SET":
                value = Both(GetValue("A32NX_FLAPS_HANDLE_INDEX"), GetValue("A32NX_GPWS_FLAPS3"),
                    (flaps, conf3) => conf3 > 0.5 ? Math.Abs(flaps - 3) < 0.5 : Math.Abs(flaps - 4) < 0.5);
                return true;
            // FAC 1 rudder trim word: neutral when |trim| < 0.1°; no data → NaN.
            case "FO_RUDDER_TRIM_NEUTRAL":
                value = DecodeRudderTrimNeutral(GetValue("A32NX_FAC_1_RUDDER_TRIM_POS"));
                return true;
        }
        if (field == "FO_ENGINES_OFF")
        {
            double e1 = GetValue("A32NX_ENGINE_STATE:1"), e2 = GetValue("A32NX_ENGINE_STATE:2");
            // Cold cache = indeterminate, not "engines running": propagate NaN so the
            // ChecklistManager neither ticks nor reverts until the states have been read.
            if (double.IsNaN(e1) || double.IsNaN(e2))
            {
                value = double.NaN;
                return true;
            }
            value = (e1 < 0.5 && e2 < 0.5) ? 1 : 0;
            return true;
        }
        if (field == "FO_VFE_NEXT")
        {
            // The A339X still publishes the plain A32NX_SPEEDS_VFEN L:var (its A32NX_Speeds.ts
            // predates FBW #10890) and its FACs publish no characteristic speeds at all
            // (docs/a32nx.md) — never switch this one to the FAC words.
            value = GetValue("A32NX_SPEEDS_VFEN");
            return true;
        }
        if (field == FbwA320GearConfirmation.UpField)
        {
            value = FbwA320GearConfirmation.UpValue(GetValue);
            return true;
        }
        if (field == FbwA320GearConfirmation.DownField)
        {
            value = FbwA320GearConfirmation.DownValue(GetValue);
            return true;
        }
        value = double.NaN;
        return false;
    }
}
