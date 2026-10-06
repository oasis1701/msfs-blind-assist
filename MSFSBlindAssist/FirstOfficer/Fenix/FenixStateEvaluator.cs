namespace MSFSBlindAssist.FirstOfficer.Fenix;

using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.Generic;

/// <summary>
/// Fenix A320 First Officer state evaluator. All aircraft state is plain L:vars read from
/// the SimConnect cache. Nearly every Fenix S_/A_ control var is registered OnRequest, so
/// they are listed in OnRequestPollFields and polled onto the cache by the FO window's
/// 1 s timer. Indicator lights (I_*) and S_FC_FLAPS are Continuous — cached automatically.
///
/// Engine running is detected from the stock TURB ENG N2 SimVars pushed in by the form
/// timer (RequestFOEngineN2 → SetEngineN2), NOT from Fenix L:vars: CFM56 idle N2 is
/// ~58–60 %, so ≥ 55 % is running.
/// </summary>
public sealed class FenixStateEvaluator : LVarStateEvaluator
{
    /// <summary>N2 percent at/above which an engine counts as running (CFM56 idle ≈ 58–60).</summary>
    public const double EngineRunningN2 = 55.0;

    private static readonly string[] PollFields =
    {
        // Electrical / APU
        "S_OH_ELEC_BAT1", "S_OH_ELEC_BAT2", "S_OH_ELEC_APU_MASTER",
        // Pneumatic / air / anti-ice
        "S_OH_PNEUMATIC_APU_BLEED", "S_OH_PNEUMATIC_PACK_1", "S_OH_PNEUMATIC_PACK_2",
        "S_OH_PNEUMATIC_XBLEED_SELECTOR", "S_OH_PNEUMATIC_PACK_FLOW",
        "S_OH_PNEUMATIC_HOT_AIR", "S_OH_PNEUMATIC_PRESS_MODE",
        "S_OH_PNEUMATIC_ENG1_ANTI_ICE", "S_OH_PNEUMATIC_ENG2_ANTI_ICE",
        "S_OH_PNEUMATIC_WING_ANTI_ICE",
        // Fuel
        "S_OH_FUEL_LEFT_1", "S_OH_FUEL_LEFT_2", "S_OH_FUEL_CENTER_1",
        "S_OH_FUEL_CENTER_2", "S_OH_FUEL_RIGHT_1", "S_OH_FUEL_RIGHT_2",
        // Signs / interior
        "S_OH_SIGNS", "S_OH_SIGNS_SMOKING", "S_OH_INT_LT_EMER",
        // Exterior lights
        "S_OH_EXT_LT_BEACON", "S_OH_EXT_LT_STROBE", "S_OH_EXT_LT_NAV_LOGO",
        "S_OH_EXT_LT_WING", "S_OH_EXT_LT_LANDING_L", "S_OH_EXT_LT_LANDING_R",
        "S_OH_EXT_LT_RWY_TURNOFF", "S_OH_EXT_LT_NOSE",
        // ADIRS / oxygen
        "S_OH_NAV_IR1_MODE", "S_OH_NAV_IR2_MODE", "S_OH_NAV_IR3_MODE",
        "S_OH_OXYGEN_CREW_OXYGEN",
        // Engine panel / pedestal
        "S_ENG_MODE", "S_ENG_MASTER_1", "S_ENG_MASTER_2",
        "S_MIP_PARKING_BRAKE", "S_MIP_GEAR", "A_FC_SPEEDBRAKE",
        "A_FC_THROTTLE_LEFT_INPUT", "A_FC_THROTTLE_RIGHT_INPUT",
        // Transponder / TCAS
        "S_XPDR_MODE", "S_XPDR_OPERATION", "S_XPDR_ALTREPORTING", "S_TCAS_RANGE",
        // Baro STD readback
        "S_FCU_EFIS1_BARO_STD", "S_FCU_EFIS2_BARO_STD",
        // Wipers (safety check)
        "S_MISC_WIPER_CAPT", "S_MISC_WIPER_FO",
        // Weather radar
        "S_WR_SYS", "S_WR_PRED_WS",
        // Read-back live values and lines (Airbus checklist): the stock total fuel weight
        // (OnRequest), the GPWS LDG FLAP 3 switch and the captain's baro unit selector.
        // Polling a Continuous var onto the cache is harmless, so the ones below it are
        // listed too: nothing then depends on which batch they were registered in.
        "FUEL TOTAL QUANTITY WEIGHT", "S_OH_GPWS_LDG_FLAP3", "S_FCU_EFIS1_BARO_MODE",
        "N_FCU_EFIS1_BARO_HPA", "N_FCU_EFIS1_BARO_INCH",
        "FNX2PLD_speedV1", "FNX2PLD_speedVR", "FNX2PLD_speedV2", "N_MISC_PERF_TO_FLEX",
        "I_MIP_AUTOBRAKE_LO_L", "I_MIP_AUTOBRAKE_MED_L", "I_MIP_AUTOBRAKE_MAX_L",
    };

    public override IReadOnlyList<string> OnRequestPollFields => PollFields;

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        switch (field)
        {
            // Radar ON (S_WR_SYS 0 = system 1, 2 = system 2; 1 = OFF) AND predictive windshear
            // AUTO (S_WR_PRED_WS 1).
            case "FO_WXR_ON_AUTO":
                value = Both(GetValue("S_WR_SYS"), GetValue("S_WR_PRED_WS"), WxrOnAuto);
                return true;
            // Radar OFF AND predictive windshear OFF.
            case "FO_WXR_PWS_OFF":
                value = Both(GetValue("S_WR_SYS"), GetValue("S_WR_PRED_WS"), WxrPwsOff);
                return true;
            // The landing memo's flaps line: FULL (4), or 3 with the GPWS LDG FLAP 3 switch on.
            case "FO_LDG_FLAPS_SET":
                value = Both(GetValue("S_FC_FLAPS"), GetValue("S_OH_GPWS_LDG_FLAP3"), LdgFlapsSet);
                return true;
            case "FO_ENG1_N2": value = Eng1N2; return true;
            case "FO_ENG2_N2": value = Eng2N2; return true;
            case "FO_ENGINES_OFF":
                value = (Eng1N2 < 20 && Eng2N2 < 20) ? 1 : 0;
                return true;
            // "Landing gear: UP" — gear up, lights out (FenixGearConfirmation), never the
            // lever alone (owner decision 2026-09-22).
            case FenixGearConfirmation.UpField:
                value = FenixGearConfirmation.UpValue(GetValue);
                return true;
            // "Landing gear: DOWN" — three green, no red (FenixGearConfirmation; the green
            // legends measured live 2026-09-25), never the lever alone.
            case FenixGearConfirmation.DownField:
                value = FenixGearConfirmation.DownValue(GetValue);
                return true;
            default:
                value = double.NaN;
                return false;
        }
    }

    /// <summary>Gear lever position (S_MIP_GEAR 1 = Down). NaN-safe: unknown reads as down,
    /// so auto-gear-up never fires on missing data (auto-gear-down would be a no-op).</summary>
    public bool IsGearDown()
    {
        double v = GetValue("S_MIP_GEAR");
        return double.IsNaN(v) || v > 0.5;
    }

    /// <summary>SimBrief takeoff flaps (1..3) mapped to the Fenix flap lever index (same
    /// numbering), or -1 when not loaded / out of range.</summary>
    public int TakeoffFlapsLeverIndex()
    {
        int f = GetTakeoffFlaps();
        return f is >= 1 and <= 3 ? f : -1;
    }

    // ---- Read-back conditions and live values --------------------------------------------
    // Pure, so the cache-free tests can reach them: the synthetic fields above and the three
    // text helpers below feed them from the live cache (Both(...) keeps NaN as "unknown").

    /// <summary>S_WR_SYS 0 = system 1, 1 = OFF, 2 = system 2; S_WR_PRED_WS 0 = OFF, 1 = AUTO.</summary>
    internal static bool WxrOnAuto(double sys, double pws) => Math.Abs(sys - 1) > 0.5 && pws > 0.5;

    internal static bool WxrPwsOff(double sys, double pws) => Math.Abs(sys - 1) < 0.5 && pws < 0.5;

    /// <summary>The landing memo's flaps: FULL (4), or 3 when the GPWS LDG FLAP 3 switch is on.</summary>
    internal static bool LdgFlapsSet(double flaps, double conf3) =>
        conf3 > 0.5 ? Math.Abs(flaps - 3) < 0.5 : Math.Abs(flaps - 4) < 0.5;

    /// <summary>Captain-side baro: STD, else the value in the unit the EFIS shows
    /// (S_FCU_EFIS1_BARO_MODE 0 = inHg, 1 = hPa). Null whenever an input is unknown.</summary>
    public string? BaroText() => BaroText(GetValue);

    internal static string? BaroText(Func<string, double> read)
    {
        double std = read("S_FCU_EFIS1_BARO_STD");
        if (double.IsNaN(std) || std > 0.5) return AirbusReadbackFormat.Baro(std, double.NaN, false);
        double mode = read("S_FCU_EFIS1_BARO_MODE");
        if (double.IsNaN(mode)) return null;
        bool inHg = mode < 0.5;
        // N_FCU_EFIS1_BARO_INCH is in inHg (29.92), as the Fenix altimeter readout speaks it
        // straight; N_FCU_EFIS1_BARO_HPA is in millibars.
        return AirbusReadbackFormat.Baro(0, read(inHg ? "N_FCU_EFIS1_BARO_INCH" : "N_FCU_EFIS1_BARO_HPA"), inHg);
    }

    /// <summary>Total fuel in kilograms (the Airbus card's unit), from the stock weight in pounds.</summary>
    public string? FuelText() => FuelText(GetValue);

    internal static string? FuelText(Func<string, double> read)
    {
        double lbs = read("FUEL TOTAL QUANTITY WEIGHT");
        return double.IsNaN(lbs) ? null : AirbusReadbackFormat.FuelQuantity(lbs * 0.45359237, pounds: false);
    }

    /// <summary>The landing autobrake read from the three lamps (LO / MED / MAX; none lit = off).</summary>
    public string? AutobrakeText() => AutobrakeText(GetValue);

    internal static string? AutobrakeText(Func<string, double> read) =>
        AirbusReadbackFormat.Autobrake(AirbusReadbackFormat.AutobrakeModeFromLamps(
            read("I_MIP_AUTOBRAKE_LO_L"), read("I_MIP_AUTOBRAKE_MED_L"), read("I_MIP_AUTOBRAKE_MAX_L")));
}
