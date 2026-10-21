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
    /// <summary>Synthetic: the CONF 3 landing selection the auto-flap cap reads (FOA-6), from the
    /// running build's own L:var. FlyByWire #10890 (2026-09-11) renamed it to A32NX_FM_LANDING_CONF3,
    /// which is what the FS2024 build (2024.2.0) writes; the FS2020 build (fs2020-master 2020.16.0)
    /// predates #10890 and still writes A32NX_SPEEDS_LANDING_CONF3 (MCDU/mcdu.js). NaN while the
    /// simulator build or the value is unknown - the cap treats that as FULL, as before.</summary>
    internal const string LandingConf3Field = "FO_LANDING_CONF3";

    /// <summary>The FS2020 build's CONF 3 L:var.</summary>
    internal const string Fs2020LandingConf3Var = "A32NX_SPEEDS_LANDING_CONF3";
    /// <summary>The FS2024 build's CONF 3 L:var (FBW #10890).</summary>
    internal const string Fs2024LandingConf3Var = "A32NX_FM_LANDING_CONF3";

    /// <summary>The CONF 3 L:var of <paramref name="build"/>, or null when unknown.</summary>
    internal static string? LandingConf3VarFor(MSFSBlindAssist.Aircraft.FbwSimBuild build) => build switch
    {
        MSFSBlindAssist.Aircraft.FbwSimBuild.Fs2020 => Fs2020LandingConf3Var,
        MSFSBlindAssist.Aircraft.FbwSimBuild.Fs2024 => Fs2024LandingConf3Var,
        _ => null,
    };

    /// <summary>1 when the APU is available or the external power pushbutton is ON, 0 when both
    /// read off, NaN while either is unread. Shutdown waits for it before the engine masters go off.</summary>
    internal const string ApuOrExtPowerField = "FO_APU_OR_EXT_POWER";

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
        // Securing's APU flap wait (FOA-10): OnRequest and silent, registered by the SD APU page rows.
        "A32NX_APU_FLAP_OPEN_PERCENTAGE",
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
        // Auto-flap schedule inputs (speed tape + flaps handle). The CONF 3 landing selection
        // was absent here AND unregistered until 2026-08-30, and read FBW's pre-#10890 name until
        // 2026-10-09: either way `conf3` was NaN and the cap took flaps FULL (FOA-6).
        // VFE-next comes from the FAC's V_FE_NEXT word because FBW #10890 stopped publishing
        // A32NX_SPEEDS_VFEN (docs/a32nx.md).
        "A32NX_SPEEDS_GD", "A32NX_SPEEDS_S", "A32NX_SPEEDS_F",
        "FAC_1_V_FE_NEXT", "FAC_2_V_FE_NEXT",
        Fs2020LandingConf3Var, Fs2024LandingConf3Var,
        // The wiper circuit switches the Power Up safety check reads (never the synthetic
        // WIPER_LEFT/RIGHT combos, which nothing writes back).
        "WIPER_L_SW", "WIPER_R_SW",
        "A32NX_RCDR_GROUND_CONTROL_ON",
        // Gear legs (FbwA320GearConfirmation)
        "A32NX_GEAR_CENTER_POSITION", "A32NX_GEAR_LEFT_POSITION", "A32NX_GEAR_RIGHT_POSITION",
        // Read-back live values (the Airbus card's "SET"/"CHECKED" lines speak the value the
        // First Officer reads). Polling a Continuous var is harmless; the OnRequest ones need it.
        // Fuel is FUEL_TOTAL_WEIGHT_KG, never the fuel hotkey's FUEL_QUANTITY_KG, which MainForm
        // speaks on every delivery (FoPollSilenceTests).
        "FUEL_TOTAL_WEIGHT_KG", "A32NX_EFB_USING_METRIC_UNIT",
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
    /// <c>FUEL_TOTAL_WEIGHT_KG</c> is read in kilograms. Null when the fuel or the unit is unknown.</summary>
    public string? FuelText()
    {
        double metric = GetValue("A32NX_EFB_USING_METRIC_UNIT");
        if (double.IsNaN(metric)) return null;
        return AirbusReadbackFormat.FuelQuantity(GetValue("FUEL_TOTAL_WEIGHT_KG"), pounds: metric < 0.5);
    }

    /// <summary>1 when FlyByWire's own engine state (0 Off / 1 On / 2 Starting / 3 Shutting
    /// down) reads On, 0 for any other state, NaN when the state is unknown.</summary>
    internal static double RunningFrom(double state)
        => double.IsNaN(state) ? double.NaN : Math.Abs(state - 1) < 0.5 ? 1.0 : 0.0;

    /// <summary>The Taxi flow's takeoff-memo check (FO_TAXI_MEMO_OK): ground spoilers armed
    /// (A32NX_SPOILERS_ARMED), the flap handle in a takeoff position (1-3, the AS_FLAPS test) and
    /// the memo's seat-belts line on (FO_SIGNS_ON) — the conditions of TXC_MEMO_SPLRS,
    /// TXC_MEMO_FLAPS and TXC_MEMO_SIGNS. NaN when any input is unknown (unknown is not false,
    /// Both's contract), else 1 when all three hold and 0 when any does not.</summary>
    internal static double TaxiMemoOk(double spoilersArmed, double flapsHandle, double signsOn) =>
        double.IsNaN(spoilersArmed) || double.IsNaN(flapsHandle) || double.IsNaN(signsOn)
            ? double.NaN
            : spoilersArmed > 0.5 && FbwA320FlowDefinitions.IsTakeoffFlapsLever(flapsHandle) && signsOn > 0.5
                ? 1.0 : 0.0;

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
            // "Running" = On. Used instead of a raw N2 threshold (the A330's idle N2 is unknown).
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
            case "FO_SIGNS_ON":
                value = Both(GetValue("CABIN SEATBELTS ALERT SWITCH"), GetValue("XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION"),
                    (belts, smoking) => belts > 0.5 && smoking < 1.5);
                return true;
            // The Taxi flow's takeoff-memo check (TX_MEMO_CHECK): ground spoilers armed, a takeoff
            // flap handle and the memo's seat-belts line on, from the fields and conditions of the
            // three Auto Taxi checklist lines the check completes (the fourth, TXC_FLAPS, is a
            // Reminder).
            case "FO_TAXI_MEMO_OK":
                value = TaxiMemoOk(GetValue("A32NX_SPOILERS_ARMED"), GetValue("A32NX_FLAPS_HANDLE_INDEX"),
                    GetValue("FO_SIGNS_ON"));
                return true;
            // The LDG memo's flaps line: FULL, or 3 when the GPWS LDG FLAP 3 switch is on.
            case "FO_LDG_FLAPS_SET":
                value = Both(GetValue("A32NX_FLAPS_HANDLE_INDEX"), GetValue("A32NX_GPWS_FLAPS3"),
                    (flaps, conf3) => conf3 > 0.5 ? Math.Abs(flaps - 3) < 0.5 : Math.Abs(flaps - 4) < 0.5);
                return true;
            // The Landing flow's memo check (LD_MEMO_CHECK, FOA-11): the seat belts on and the flaps at
            // the landing setting, the conditions of LDC_MEMO_SIGNS and LDC_MEMO_FLAPS.
            case "FO_LDG_MEMO_OK":
                value = Both(GetValue("CABIN SEATBELTS ALERT SWITCH"), GetValue("FO_LDG_FLAPS_SET"),
                    (belts, flaps) => belts > 0.5 && flaps > 0.5);
                return true;
            // FAC 1 rudder trim word: neutral when |trim| < 0.1°; no data → NaN.
            case "FO_RUDDER_TRIM_NEUTRAL":
                value = DecodeRudderTrimNeutral(GetValue("A32NX_FAC_1_RUDDER_TRIM_POS"));
                return true;
            // Something to carry the buses once the engine generators go (Shutdown's
            // SD_POWER_SOURCE): APU AVAIL, or the external power pushbutton ON.
            case ApuOrExtPowerField:
                value = AnyLit(GetValue("A32NX_OVHD_APU_START_PB_IS_AVAILABLE"), GetValue("A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON"));
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
        if (field == LandingConf3Field)
        {
            string? key = LandingConf3VarFor(MSFSBlindAssist.Aircraft.FbwSimBuilds.From(Sc));
            value = key == null ? double.NaN : GetValue(key);
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
