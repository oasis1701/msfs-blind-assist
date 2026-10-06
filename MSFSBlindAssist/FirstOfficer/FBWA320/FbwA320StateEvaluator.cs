using System;
using System.Collections.Generic;
using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.FBWA320;

/// <summary>
/// Reads FlyByWire A32NX control state from the SimConnect cache for the First
/// Officer's checklist auto-detection. Mirrors <see cref="FBWA380.FbwA380StateEvaluator"/>
/// with the A320 two-engine key set (confirmed against FlyByWireA320Definition.GetVariables()).
/// </summary>
public sealed class FbwA320StateEvaluator : LVarStateEvaluator
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
        "LIGHT TAXI:2", "LIGHTING_LANDING_1", "LIGHTING_LANDING_2",
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
        // VFE-next comes from the FAC's V_FE_NEXT word because FBW #10890 stopped publishing
        // A32NX_SPEEDS_VFEN (docs/a32nx.md).
        "A32NX_SPEEDS_GD", "A32NX_SPEEDS_S", "A32NX_SPEEDS_F",
        "FAC_1_V_FE_NEXT", "FAC_2_V_FE_NEXT",
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
        "A32NX_FCU_LEFT_EIS_BARO_HPA", "A32NX_FCU_LEFT_EIS_BARO",
    };

    public override IReadOnlyList<string> OnRequestPollFields => PollFields;

    /// <summary>
    /// VFE of the next flap configuration from the FAC's V_FE_NEXT ARINC429 word: FAC 1, else FAC 2
    /// (the PFD's order). An unread (NaN) word is skipped; a word without data (no computed data,
    /// failure warning — and the 0 a never-written L:var reads decodes as failure warning) is not a
    /// speed. Neither → NaN, which the auto manager treats as "unknown → hold".
    /// </summary>
    internal static double DecodeVfeNext(double fac1Raw, double fac2Raw)
    {
        foreach (double raw in new[] { fac1Raw, fac2Raw })
        {
            if (double.IsNaN(raw)) continue;
            var word = new Arinc429Word(raw);
            if (word.HasData) return word.Value;
        }
        return double.NaN;
    }

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

    /// <summary>Captain-side baro for the read-back: STD, or the QNH value from the FCU's
    /// EIS words (mode 0 STD / 1 hPa / 2 inHg). Null when unknown.</summary>
    public string? BaroText()
    {
        double mode = GetValue("A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE");
        if (double.IsNaN(mode)) return null;
        if (mode < 0.5) return AirbusReadbackFormat.Baro(1, double.NaN, false);
        bool inHg = mode > 1.5;
        double raw = GetValue(inHg ? "A32NX_FCU_LEFT_EIS_BARO" : "A32NX_FCU_LEFT_EIS_BARO_HPA");
        if (double.IsNaN(raw)) return null;
        var word = new Arinc429Word(raw);
        return word.HasData ? AirbusReadbackFormat.Baro(0, word.Value, inHg) : null;
    }

    /// <summary>Total fuel in the EFB's unit (A32NX_EFB_USING_METRIC_UNIT 1 = kg, 0 = lb);
    /// <c>FUEL_QUANTITY_KG</c> is read in kilograms. Null when the fuel or the unit is unknown.</summary>
    public string? FuelText()
    {
        double metric = GetValue("A32NX_EFB_USING_METRIC_UNIT");
        if (double.IsNaN(metric)) return null;
        return AirbusReadbackFormat.FuelQuantity(GetValue("FUEL_QUANTITY_KG"), pounds: metric < 0.5);
    }

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        switch (field)
        {
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
            value = DecodeVfeNext(GetValue("FAC_1_V_FE_NEXT"), GetValue("FAC_2_V_FE_NEXT"));
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
