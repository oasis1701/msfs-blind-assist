using System.Collections.Generic;
using System.Linq;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Variable-name registries for the A380 EIS displays. The old read-only display WINDOWS
/// (System Display / Nav / ISIS) were removed — their values live on the panels + the
/// individual readout hotkeys, and only the Alt+E E/WD window remains. These lists are
/// the fallback registration set the aircraft def uses (FlyByWireA380Definition) so every
/// SD/ND/ISIS variable is still registered for the panels. Copied verbatim from the
/// deleted forms' AllVariableNames(), so no registration was dropped in the cleanup.
/// </summary>
public static class EisDisplayVars
{
    // Was FBWA380NavDisplayForm.Vars.
    public static readonly string[] NavVars =
    {
        "A32NX_EFIS_L_TO_WPT_IDENT_0", "A32NX_EFIS_L_TO_WPT_IDENT_1",
        "A32NX_EFIS_L_TO_WPT_DISTANCE", "A32NX_EFIS_L_TO_WPT_BEARING",
        "A32NX_EFIS_L_TO_WPT_TRUE_BEARING", "A32NX_EFIS_L_TO_WPT_ETA",
        "A32NX_EFIS_L_ND_MODE", "A32NX_EFIS_L_ND_RANGE", "A32NX_PUSH_TRUE_REF",
        "A32NX_ADIRS_IR_1_GROUND_SPEED", "A32NX_ADIRS_ADR_1_TRUE_AIRSPEED",
        "A32NX_ADIRS_IR_1_WIND_DIRECTION_BNR", "A32NX_ADIRS_IR_1_WIND_SPEED_BNR",
        "A32NX_EFIS_L_APPR_MSG_0", "A32NX_EFIS_L_APPR_MSG_1",
        "A32NX_FG_CROSS_TRACK_ERROR", "A32NX_FMGC_L_RNP",
        "A32NX_RADIO_RECEIVER_LOC_IS_VALID", "A32NX_RADIO_RECEIVER_LOC_DEVIATION",
        "A32NX_RADIO_RECEIVER_GS_IS_VALID", "A32NX_RADIO_RECEIVER_GS_DEVIATION",
        "A32NX_OANS_BTV_DRY_DISTANCE_ESTIMATED", "A32NX_OANS_BTV_WET_DISTANCE_ESTIMATED",
        "A32NX_OANS_BTV_STOP_BAR_DISTANCE_ESTIMATED",
    };

    // Was FBWA380ISISForm.Vars.
    public static readonly string[] IsisVars =
    {
        "PLANE PITCH DEGREES", "PLANE BANK DEGREES",
        "PLANE HEADING DEGREES MAGNETIC",
        "AIRSPEED INDICATED", "AIRSPEED MACH",
        "INDICATED ALTITUDE", "KOHLSMAN SETTING MB", "KOHLSMAN_SETTING_INHG",
        "ACCELERATION BODY X",
        "A32NX_ISIS_BARO_MODE", "A32NX_ISIS_LS_ACTIVE",
        "A32NX_RADIO_RECEIVER_LOC_IS_VALID", "A32NX_RADIO_RECEIVER_LOC_DEVIATION",
        "A32NX_RADIO_RECEIVER_GS_IS_VALID", "A32NX_RADIO_RECEIVER_GS_DEVIATION",
    };

    // Was FBWA380SystemDisplayForm.AllVariableNames() (the legacy SimVar SD fallback layout).
    public static IEnumerable<string> SystemDisplayVars()
    {
        var fuelTanks = new[] { "FEED_1", "FEED_2", "FEED_3", "FEED_4",
            "LEFT_OUTER", "LEFT_MID", "LEFT_INNER", "RIGHT_OUTER", "RIGHT_MID", "RIGHT_INNER", "TRIM" };
        foreach (var t in fuelTanks) yield return $"A32NX_FQMS_{t}_TANK_QUANTITY";
        yield return "A32NX_FQMS_TOTAL_FUEL_ON_BOARD";
        yield return "A32NX_FQMS_GROSS_WEIGHT";
        yield return "A32NX_FQMS_CENTER_OF_GRAVITY_MAC";
        yield return "A32NX_TOTAL_FUEL_QUANTITY";
        for (int e = 1; e <= 4; e++)
        {
            yield return $"A32NX_ENGINE_N2:{e}";
            yield return $"A32NX_ENGINE_N3:{e}";
            yield return $"A32NX_ENGINE_FF:{e}";
            yield return $"A32NX_ENGINE_OIL_QTY:{e}";
        }
        yield return "A32NX_PRESS_CABIN_ALTITUDE_B1";
        yield return "A32NX_PRESS_CABIN_VS_B1";
        yield return "A32NX_PRESS_CABIN_DELTA_PRESSURE_B1";
        yield return "A32NX_APU_N";
        yield return "A32NX_APU_EGT";
        yield return "A32NX_APU_FUEL_USED";
        for (int n = 1; n <= 4; n++)
        {
            yield return $"A32NX_ELEC_ENG_GEN_{n}_POTENTIAL";
            yield return $"A32NX_ELEC_ENG_GEN_{n}_LOAD";
        }
        yield return "A32NX_ELEC_APU_GEN_1_POTENTIAL";
        yield return "A32NX_ELEC_APU_GEN_2_POTENTIAL";
        foreach (var b in new[] { "1", "2", "3", "4", "ESS", "APU" })
            yield return $"A32NX_ELEC_BAT_{b}_POTENTIAL";
        yield return "A32NX_HYD_GREEN_SYSTEM_1_SECTION_PRESSURE";
        yield return "A32NX_HYD_GREEN_RESERVOIR_LEVEL";
        yield return "A32NX_HYD_YELLOW_SYSTEM_1_SECTION_PRESSURE";
        yield return "A32NX_HYD_YELLOW_RESERVOIR_LEVEL";
        yield return "A32NX_COND_CKPT_TEMP";
        for (int z = 1; z <= 8; z++) yield return $"A32NX_COND_MAIN_DECK_{z}_TEMP";
        for (int z = 1; z <= 7; z++) yield return $"A32NX_COND_UPPER_DECK_{z}_TEMP";
    }

    /// <summary>All SD + ND + ISIS variable names the panels register (deduped by the def).</summary>
    public static IEnumerable<string> All() => SystemDisplayVars().Concat(NavVars).Concat(IsisVars);
}
