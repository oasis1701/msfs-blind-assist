namespace MSFSBlindAssist.FirstOfficer.Fenix;

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
    };

    public override IReadOnlyList<string> OnRequestPollFields => PollFields;

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        switch (field)
        {
            case "FO_ENG1_N2": value = Eng1N2; return true;
            case "FO_ENG2_N2": value = Eng2N2; return true;
            case "FO_ENGINES_OFF":
                value = (Eng1N2 < 20 && Eng2N2 < 20) ? 1 : 0;
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
}
