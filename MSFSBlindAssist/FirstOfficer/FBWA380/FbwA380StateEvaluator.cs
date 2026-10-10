using System;
using System.Collections.Generic;
using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.FBWA380;

/// <summary>
/// Reads FlyByWire A380 control state from the SimConnect cache for the First
/// Officer's checklist auto-detection. Mirrors <see cref="Fenix.FenixStateEvaluator"/>.
/// </summary>
public sealed class FbwA380StateEvaluator : LVarStateEvaluator
{
    /// <summary>The CONF 3 landing selection the auto-flap cap reads (FOA-6): the A380X's own
    /// name; nothing in FlyByWire writes A32NX_SPEEDS_LANDING_CONF3 any more.</summary>
    internal const string LandingConf3Field = "A380X_FM_LANDING_CONF3";

    private static readonly string[] PollFields =
    {
        "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO",
        "A32NX_OVHD_ELEC_BAT_ESS_PB_IS_AUTO", "A32NX_OVHD_ELEC_BAT_APU_PB_IS_AUTO",
        "A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", "A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON",
        "A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", "A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON",
        "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB",
        "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB",
        "PUSH_OVHD_OXYGEN_CREW",
        "LIGHT_NAV", "LIGHT_LOGO", "LIGHT_BEACON", "LIGHT_STROBE", "LIGHT_WING",
        // Nose light is the 3-position selector state (LIGHTING_LANDING_1: 0=T.O./1=Taxi/2=Off);
        // the old LIGHT_TAXI_OVHD on/off key was removed with the faithful-switch rework.
        "LIGHT_LANDING", "NOSE_LIGHT",
        // SEATBELT_SIGN is the 3-position SWITCH (0=On/1=Auto/2=Off); the actual sign
        // illumination is the separate SEATBELT_SIGN_LIGHT readout — detection keys on
        // the light so AUTO with the sign lit also satisfies "signs on".
        "SEATBELT_SIGN", "SEATBELT_SIGN_LIGHT",
        "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position", "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position",
        // Engine anti-ice state = the stock ENG_ANTI_ICE:n readouts. The ENGn_ANTI_ICE
        // write keys are Act() combos with NO backing L:var — polling them reads junk.
        "WING_ANTI_ICE_OVHD", "ENG_ANTI_ICE:1", "ENG_ANTI_ICE:2", "ENG_ANTI_ICE:3", "ENG_ANTI_ICE:4",
        "A32NX_OVHD_COND_PACK_1_PB_IS_ON", "A32NX_OVHD_COND_PACK_2_PB_IS_ON",
        "A32NX_KNOB_OVHD_AIRCOND_XBLEED_POSITION", "A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_POSITION",
        "A32NX_OVHD_COND_HOT_AIR_1_PB_IS_ON", "A32NX_OVHD_COND_HOT_AIR_2_PB_IS_ON",
        "ANTISKID_BRAKES_ACTIVE", "A32NX_PARK_BRAKE_LEVER_POS", "A32NX_SPOILERS_ARMED",
        "A32NX_GEAR_HANDLE_POSITION", "ENGINE_MODE_SELECTOR",
        "ENG_VALVE_SWITCH:1", "ENG_VALVE_SWITCH:2", "ENG_VALVE_SWITCH:3", "ENG_VALVE_SWITCH:4",
        "XMLVAR_A320_WeatherRadar_Sys", "A32NX_SWITCH_RADAR_PWS_Position",
        "FUELPUMP_FEEDTK1_MAIN", "FUELPUMP_FEEDTK1_STBY", "FUELPUMP_FEEDTK2_MAIN", "FUELPUMP_FEEDTK2_STBY",
        "FUELPUMP_FEEDTK3_MAIN", "FUELPUMP_FEEDTK3_STBY", "FUELPUMP_FEEDTK4_MAIN", "FUELPUMP_FEEDTK4_STBY",
        // Transfer-tank pumps (outer/mid/inner/trim) — checklist "Fuel pumps: ON/OFF" detects
        // all 20; these are Continuous-cached, listed here for parity with the feed pumps above.
        "FUELPUMP_OUTR_L", "FUELPUMP_MID_L_FWD", "FUELPUMP_MID_L_AFT", "FUELPUMP_INR_L_FWD", "FUELPUMP_INR_L_AFT",
        "FUELPUMP_OUTR_R", "FUELPUMP_MID_R_FWD", "FUELPUMP_MID_R_AFT", "FUELPUMP_INR_R_FWD", "FUELPUMP_INR_R_AFT",
        "FUELPUMP_TRIM_L", "FUELPUMP_TRIM_R",
        "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
        "A32NX_OVHD_APU_START_PB_IS_ON",
        "A32NX_COCKPIT_DOOR_LOCKED", "A32NX_AUTOBRAKES_SELECTED_MODE",
        "A32NX_EFIS_L_ND_MODE", "A32NX_EFIS_R_ND_MODE",
        "A32NX_EFIS_L_ND_RANGE", "A32NX_EFIS_R_ND_RANGE",
        // FBW #10855: the baro unit is the EFIS-CP's BARO_IS_INHG (1 = inHg) and the flight
        // directors are ONE FCU pushbutton, read from its light (A380FlightDirector.StateKey).
        "A32NX_FCU_EFIS_L_BARO_IS_INHG", "A32NX_FCU_EFIS_R_BARO_IS_INHG",
        MSFSBlindAssist.Aircraft.A380FlightDirector.StateKey,
        "ELEC_APU_GEN:1", "ELEC_APU_GEN:2",
        // Read-back sources (FOA-9): what the self-ticking lines and the spoken values read. The
        // OnRequest ones (the SEC rudder-trim words, the gear legs, total fuel, flex) are delivered only
        // by this poll; polling a Continuous one is harmless.
        "A32NX_SEC_1_RUDDER_ACTUAL_POSITION", "A32NX_SEC_3_RUDDER_ACTUAL_POSITION",
        "A32NX_GEAR_CENTER_POSITION", "A32NX_GEAR_LEFT_POSITION", "A32NX_GEAR_RIGHT_POSITION",
        "A32NX_TOTAL_FUEL_QUANTITY", "A32NX_EFB_USING_METRIC_UNIT",
        "PFD_V1", "PFD_VR", "PFD_V2", "A32NX_AIRLINER_TO_FLEX_TEMP",
        "AIRLINER_MINIMUM_DESCENT_ALTITUDE", "AIRLINER_DECISION_HEIGHT",
        "A32NX_FCU_LEFT_EIS_BARO_IS_STD", "A32NX_FCU_LEFT_EIS_BARO_HPA",
        "A32NX_AUTOBRAKES_ARMED_MODE",
        // Auto-flap schedule inputs (speed tape + landing config + flaps handle).
        "A32NX_SPEEDS_GD", "A32NX_SPEEDS_S", "A32NX_SPEEDS_F", "A32NX_SPEEDS_VFEN",
        LandingConf3Field, "A32NX_FLAPS_HANDLE_INDEX",
    };

    public override IReadOnlyList<string> OnRequestPollFields => PollFields;

    /// <summary>
    /// SEC rudder-trim word (ARINC 429 degrees, positive = nose-left): 1 when the trim is within
    /// <see cref="RudderTrimNeutralDegrees"/> of neutral either way, 0 when it is not, NaN when neither
    /// word carries data. SEC 1, else SEC 3, the order FlyByWire's warning system reads them in
    /// (FwsCore.ts L4837-4846 at 255546cb). An unread (NaN) word is skipped; failure warning, no
    /// computed data and the 0 a never-written L:var reads are not a trim (ARINC-1).
    /// </summary>
    internal static double DecodeRudderTrimNeutral(double sec1Raw, double sec3Raw)
    {
        foreach (double raw in new[] { sec1Raw, sec3Raw })
        {
            if (double.IsNaN(raw)) continue;
            var word = new Arinc429Word(raw);
            if (word.HasData) return Math.Abs(word.Value) < RudderTrimNeutralDegrees ? 1.0 : 0.0;
        }
        return double.NaN;
    }

    /// <summary>FlyByWire's own ECL threshold for RUDDER TRIM NEUTRAL (FwsNormalChecklists.ts L545 at
    /// 255546cb). FlyByWire compares the signed trim, which lets a large nose-right trim pass; neutral
    /// here is the magnitude.</summary>
    internal const double RudderTrimNeutralDegrees = 0.35;

    /// <summary>True when the flap lever (<c>A32NX_FLAPS_HANDLE_INDEX</c>, 0..4) reads a takeoff
    /// position, 1-3 (CONF 1+F, 2, 3): the Taxi memo's "Flaps: T.O" (FwsNormalChecklists.ts L555).
    /// 0, FULL and an unread lever (NaN) are not.</summary>
    internal static bool IsTakeoffFlapsLever(double lever) => lever > 0.5 && lever < 3.5;

    /// <summary>Total fuel in the EFB's unit (<c>A32NX_EFB_USING_METRIC_UNIT</c> 1 = kg, 0 = lb), from
    /// <c>A32NX_TOTAL_FUEL_QUANTITY</c> (kilograms, "the total physical quantity of fuel in the tanks").
    /// Null when the fuel or the unit is unknown.</summary>
    public string? FuelText()
    {
        double metric = GetValue("A32NX_EFB_USING_METRIC_UNIT");
        if (double.IsNaN(metric)) return null;
        return AirbusReadbackFormat.FuelQuantity(GetValue("A32NX_TOTAL_FUEL_QUANTITY"), pounds: metric < 0.5);
    }

    /// <summary>The Captain-side baro: "standard", or the QNH in the EFIS-CP's unit. Null when unknown.</summary>
    public string? BaroText() => FbwA380ReadbackFormat.Baro(GetValue("A32NX_FCU_LEFT_EIS_BARO_IS_STD"),
        GetValue("A32NX_FCU_LEFT_EIS_BARO_HPA"), GetValue("A32NX_FCU_EFIS_L_BARO_IS_INHG"));

    /// <summary>The approach minimum set on the MFD PERF page (A380F-15). Null when unknown.</summary>
    public string? MinimumText() => FbwA380ReadbackFormat.Minimum(
        GetValue("AIRLINER_MINIMUM_DESCENT_ALTITUDE"), GetValue("AIRLINER_DECISION_HEIGHT"));

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        switch (field)
        {
            case "FO_ENGINES_OFF":
            {
                double v1 = GetValue("ENG_VALVE_SWITCH:1"), v2 = GetValue("ENG_VALVE_SWITCH:2"),
                       v3 = GetValue("ENG_VALVE_SWITCH:3"), v4 = GetValue("ENG_VALVE_SWITCH:4");
                // Cold cache = indeterminate, not "engines running": propagate NaN so the
                // ChecklistManager neither ticks nor reverts until the valves have been read.
                value = double.IsNaN(v1) || double.IsNaN(v2) || double.IsNaN(v3) || double.IsNaN(v4)
                    ? double.NaN
                    : (v1 < 0.5 && v2 < 0.5 && v3 < 0.5 && v4 < 0.5) ? 1 : 0;
                return true;
            }
            // After Start "Rudder trim: NEUTRAL": the SEC trim word within 0.35 degrees.
            case "FO_RUDDER_TRIM_NEUTRAL":
                value = DecodeRudderTrimNeutral(GetValue("A32NX_SEC_1_RUDDER_ACTUAL_POSITION"),
                    GetValue("A32NX_SEC_3_RUDDER_ACTUAL_POSITION"));
                return true;
            // Landing "Flaps: LDG": FULL, or 3 when the MFD PERF APPR page selects CONF 3
            // (LandingConf3Field, A380X_FM_LANDING_CONF3), FlyByWire's own ECL sensing
            // (FwsNormalChecklists.ts L587-590).
            case "FO_LDG_FLAPS_SET":
                value = Both(GetValue("A32NX_FLAPS_HANDLE_INDEX"), GetValue(LandingConf3Field),
                    (flaps, conf3) => conf3 > 0.5 ? Math.Abs(flaps - 3) < 0.5 : Math.Abs(flaps - 4) < 0.5);
                return true;
            // The Taxi flow's takeoff-memo check: seat-belt sign lit AND ground spoilers armed AND the
            // flap lever in a takeoff position — the fields and conditions of the lines it completes.
            case "FO_TAXI_MEMO_OK":
                value = Both(
                    Both(GetValue("SEATBELT_SIGN_LIGHT"), GetValue("A32NX_SPOILERS_ARMED"),
                        (belts, spoilers) => belts > 0.5 && spoilers > 0.5),
                    GetValue("A32NX_FLAPS_HANDLE_INDEX"),
                    (beltsAndSpoilers, flaps) => beltsAndSpoilers > 0.5 && IsTakeoffFlapsLever(flaps));
                return true;
            // The Landing flow's landing-memo check: seat-belt sign lit AND the flaps at the landing setting.
            case "FO_LDG_MEMO_OK":
                value = Both(GetValue("SEATBELT_SIGN_LIGHT"), GetValue("FO_LDG_FLAPS_SET"),
                    (belts, flaps) => belts > 0.5 && flaps > 0.5);
                return true;
            // The Line-up flow's packs check: both packs on.
            case "FO_PACKS_ON":
                value = Both(GetValue("A32NX_OVHD_COND_PACK_1_PB_IS_ON"), GetValue("A32NX_OVHD_COND_PACK_2_PB_IS_ON"),
                    (pack1, pack2) => pack1 > 0.5 && pack2 > 0.5);
                return true;
            // Landing "Landing gear: DOWN" and the Landing flow's last step (FO-13).
            case FbwA380GearConfirmation.DownField:
                value = FbwA380GearConfirmation.DownValue(GetValue);
                return true;
        }
        value = double.NaN;
        return false;
    }
}
