using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.FirstOfficer.Generic;

namespace MSFSBlindAssist.FirstOfficer.MD11;

/// <summary>
/// The TFDi MD-11 First Officer's state evaluator. Reads the SimConnect cache by the definition's
/// node-id keys (switches and latches OnRequest — polled here each second by the FO window —
/// lamps on the 1 Hz batch, exports continuous). NaN until read, never 0. Lamp-derived fields are
/// NaN while the annunciators are unpowered, because a dark lamp then means nothing.
/// Engine 3's N2 arrives through <see cref="IFoEngine3N2Sink"/>.
/// </summary>
public sealed class Md11FoStateEvaluator : LVarStateEvaluator, IFoEngine3N2Sink, IMd11FoFlightState
{
    public const double EngineRunningN2 = Md11FoSwitching.EngineRunningN2;

    private double _eng3N2 = double.NaN;

    public void SetEngine3N2(double eng3N2) => Volatile.Write(ref _eng3N2, eng3N2);

    public double EngineN2(int engine) => engine switch
    {
        1 => Eng1N2,
        2 => Eng2N2,
        3 => Volatile.Read(ref _eng3N2),
        _ => double.NaN,
    };

    public bool? OnGround
    {
        get
        {
            double v = GetValue("SIM_ON_GROUND");
            return double.IsNaN(v) ? null : v > 0.5;
        }
    }

    /// <summary>The SimBrief takeoff flap as a Dial-A-Flap setting, or null (no plan / out of 10–25°).</summary>
    public int? TakeoffDialAFlap
        => GetTakeoffFlaps() is int f && Md11FoSwitching.IsDialAFlapDegrees(f) ? f : null;

    private static readonly string[] PollFields =
    {
        "MD11_OVHD_ELEC_BATT_BT", "MD11_OVHD_ELEC_EMER_PWR_KB", "MD11_OVHD_ELEC_SYSTEM_SEL_BT",
        "MD11_OVHD_ELEC_SMOKE_ELEC_AIR_KB",
        "MD11_OVHD_FUEL_SYSTEM_SEL_BT", "MD11_OVHD_HYD_SYSTEM_SEL_BT", "MD11_OVHD_PNEU_SYSTEM_SEL_BT",
        "MD11_OVHD_PNEU_CABIN_SYSTEM_SEL_BT", "MD11_OVHD_PNEU_APU_BLEED_BT",
        "MD11_OVHD_IRS_1_KB", "MD11_OVHD_IRS_2_KB", "MD11_OVHD_IRS_3_KB",
        "MD11_OVHD_FUEL_DUMP_BT", "MD11_OVHD_FUEL_DUMP_GRD", "MD11_OVHD_FUEL_MANF_DRAIN_GRD",
        "MD11_OVHD_LTS_LDG_L_SW", "MD11_OVHD_LTS_LDG_R_SW", "MD11_OVHD_LTS_NOSE_SW",
        "MD11_OVHD_LTS_NO_SMOKE_SW", "MD11_OVHD_LTS_SEAT_BELTS_SW", "MD11_OVHD_LTS_EMER_SW",
        "MD11_OVHD_WNDSHLD_AICE_L_BT", "MD11_OVHD_WNDSHLD_AICE_R_BT", "MD11_OVHD_WNDSHLD_AICE_BT",
        "MD11_OVHD_WNDSHLD_AICE_DEFOG_BT",
        "MD11_OVHD_PNEU_FWD_CARGO_TEMP", "MD11_OVHD_PNEU_AFT_CARGO_TEMP",
        "MD11_AOVHD_EVAC_SW", "MD11_AOVHD_EVAC_GRD", "MD11_AOVHD_GPWS_SW", "MD11_AOVHD_GPWS_GRD",
        "MD11_THR_L_START_SW", "MD11_THR_C_START_SW", "MD11_THR_R_START_SW",
        "MD11_THR_L_FUEL_SW", "MD11_THR_C_FUEL_SW", "MD11_THR_R_FUEL_SW",
        "MD11_THR_PARK_LVR", "MD11_MIP_GEAR_SW", "MD11_CTR_AUTOBRAKE_SW",
        "MD11_PED_XPNDR_MODE_KB", "MD11_PED_XPNDR_ALT_RPTG_KB",
        "MD11_OVHD_FLTCTL_FLAPLIM_KB",
        TFDiMD11Definition.FoWxrOffReadKey,
    };

    public override IReadOnlyList<string> OnRequestPollFields => PollFields;

    // ------------------------------------------------------------------ synthetic fields

    private delegate double Fn(Func<string, double> read);
    private sealed record Synth(string[] Inputs, Fn Compute);

    private static double B(bool b) => b ? 1 : 0;
    private static bool Known(params double[] v) => v.All(x => !double.IsNaN(x));
    private static double? N(double v) => double.IsNaN(v) ? null : v;
    private static bool Lit(double v) => v > Md11ControlState.LitThreshold;

    private static readonly string[] PowerInputs = { TFDiMD11Definition.DcPowerKey, TFDiMD11Definition.Dc1BusOffKey };

    private static bool Powered(Func<string, double> r)
        => Md11ControlState.IsPowered(N(r(TFDiMD11Definition.DcPowerKey)), N(r(TFDiMD11Definition.Dc1BusOffKey)));

    /// <summary>A field computed from lamps: NaN unless powered and every lamp read.</summary>
    private static Synth Lamps(Func<double[], bool> ok, params string[] lamps)
        => new(lamps.Concat(PowerInputs).ToArray(), r =>
        {
            if (!Powered(r)) return double.NaN;
            var v = lamps.Select(r).ToArray();
            return Known(v) ? B(ok(v)) : double.NaN;
        });

    /// <summary>A field computed from switch/export values: NaN unless every one was read.</summary>
    private static Synth Vars(Func<double[], bool> ok, params string[] keys)
        => new(keys, r =>
        {
            var v = keys.Select(r).ToArray();
            return Known(v) ? B(ok(v)) : double.NaN;
        });

    private static bool Pos(double v, double p) => Md11FoSwitching.AtPosition(v, p);

    private static readonly string[] Alts = { "MD11_CAP_ALTIMETER", "MD11_FO_ALTIMETER", "MD11_STBY_ALTIMETER" };

    private static readonly Dictionary<string, Synth> Synthetics = new(StringComparer.Ordinal)
    {
        ["FO_POWERED"] = new(PowerInputs, r => B(Powered(r))),

        // ---- lamps (need power) ----
        ["FO_NAV_ON"] = Lamps(v => !Lit(v[0]), "MD11_OVHD_LTS_NAV_LT"),                   // OFF legend dark
        ["FO_BEACON_ON"] = Lamps(v => !Lit(v[0]), "MD11_OVHD_LTS_BCN_LT"),
        ["FO_STROBES_ON"] = Lamps(v => !Lit(v[0]), "MD11_OVHD_LTS_HI_INT_LT"),
        ["FO_LOGO_ON"] = Lamps(v => Lit(v[0]), "MD11_OVHD_LTS_LOGO_ON_LT"),
        ["FO_RWY_TURNOFF_OFF"] = Lamps(v => !Lit(v[0]) && !Lit(v[1]),
            "MD11_OVHD_LTS_RWY_TURNOFF_L_LT", "MD11_OVHD_LTS_RWY_TURNOFF_R_LT"),
        ["FO_DOME_OFF"] = Lamps(v => !Lit(v[0]), "MD11_LTS_DOME"),
        ["FO_EXT_POWER_ON"] = Lamps(v => Lit(v[0]), "MD11_OVHD_ELEC_EXT_PWR_ON_LT"),
        ["FO_APU_POWER_ON"] = Lamps(v => Lit(v[0]), "MD11_OVHD_ELEC_APU_PWR_ON_LT"),
        ["FO_AUX_PUMP_1_ON"] = Lamps(v => Lit(v[0]), "MD11_OVHD_HYD_AUX_PUMP_1_ON_LT"),
        ["FO_IGNITION_SELECTED"] = Lamps(v => Lit(v[0]) || Lit(v[1]), "MD11_OVHD_ENG_A_LT", "MD11_OVHD_ENG_B_LT"),
        ["FO_IGNITION_OFF"] = Lamps(v => Lit(v[0]), "MD11_OVHD_ENG_IGN_OFF_LT"),
        ["FO_PACKS_OFF"] = Lamps(v => v.All(Lit),
            "MD11_OVHD_PNEU_PACK_1_OFF_LT", "MD11_OVHD_PNEU_PACK_2_OFF_LT", "MD11_OVHD_PNEU_PACK_3_OFF_LT"),
        ["FO_ECON_ON"] = Lamps(v => !Lit(v[0]), "MD11_OVHD_PNEU_ECON_OFF_LT"),
        ["FO_HYD_TEST_RUNNING"] = Lamps(v => Lit(v[0]), "MD11_OVHD_HYD_TEST_LT"),
        ["FO_ANTI_ICE_OFF"] = Lamps(v => !v.Any(Lit),
            "MD11_OVHD_AICE_ENG1_ON_LT", "MD11_OVHD_AICE_ENG2_ON_LT", "MD11_OVHD_AICE_ENG3_ON_LT",
            "MD11_OVHD_AICE_WING_ON_LT", "MD11_OVHD_AICE_TAIL_ON_LT"),
        ["FO_WING_ANTI_ICE_OFF"] = Lamps(v => !Lit(v[0]), "MD11_OVHD_AICE_WING_ON_LT"),
        ["FO_IRS_ALIGNED"] = new(new[] { "MD11_OVHD_IRS_1_KB", "MD11_OVHD_IRS_2_KB", "MD11_OVHD_IRS_3_KB",
                                         "MD11_OVHD_IRS_1_LT", "MD11_OVHD_IRS_2_LT", "MD11_OVHD_IRS_3_LT" }
                                 .Concat(PowerInputs).ToArray(), r =>
        {
            if (!Powered(r)) return double.NaN;
            var sw = new[] { r("MD11_OVHD_IRS_1_KB"), r("MD11_OVHD_IRS_2_KB"), r("MD11_OVHD_IRS_3_KB") };
            var lt = new[] { r("MD11_OVHD_IRS_1_LT"), r("MD11_OVHD_IRS_2_LT"), r("MD11_OVHD_IRS_3_LT") };
            if (!Known(sw) || !Known(lt)) return double.NaN;
            return B(sw.All(s => Pos(s, 1)) && !lt.Any(Lit));                      // NAV, NAV OFF dark
        }),
        ["FO_EXTERIOR_PARKED"] = new(new[] { "MD11_OVHD_LTS_LDG_L_SW", "MD11_OVHD_LTS_LDG_R_SW", "MD11_OVHD_LTS_NOSE_SW",
                                             "MD11_OVHD_LTS_HI_INT_LT", "MD11_OVHD_LTS_BCN_LT", "MD11_OVHD_LTS_LOGO_ON_LT",
                                             "MD11_OVHD_LTS_RWY_TURNOFF_L_LT", "MD11_OVHD_LTS_RWY_TURNOFF_R_LT",
                                             "MD11_OVHD_LTS_NAV_LT" }.Concat(PowerInputs).ToArray(), r =>
        {
            if (!Powered(r)) return double.NaN;
            var v = new[] { r("MD11_OVHD_LTS_LDG_L_SW"), r("MD11_OVHD_LTS_LDG_R_SW"), r("MD11_OVHD_LTS_NOSE_SW"),
                            r("MD11_OVHD_LTS_HI_INT_LT"), r("MD11_OVHD_LTS_BCN_LT"), r("MD11_OVHD_LTS_LOGO_ON_LT"),
                            r("MD11_OVHD_LTS_RWY_TURNOFF_L_LT"), r("MD11_OVHD_LTS_RWY_TURNOFF_R_LT"), r("MD11_OVHD_LTS_NAV_LT") };
            if (!Known(v)) return double.NaN;
            return B(Pos(v[0], 0) && Pos(v[1], 0) && Pos(v[2], 0)      // landing retracted, nose off
                     && Lit(v[3]) && Lit(v[4])                         // strobes/beacon OFF legends lit
                     && !Lit(v[5]) && !Lit(v[6]) && !Lit(v[7])         // logo, turnoffs off
                     && !Lit(v[8]));                                    // nav ON (OFF legend dark)
        }),

        // ---- switches / exports ----
        ["FO_WXR_OFF"] = Vars(v => v[0] > 0.5, TFDiMD11Definition.FoWxrOffReadKey),
        ["FO_FUEL_SWITCHES_OFF"] = Vars(v => v.All(x => Pos(x, 0)), "MD11_THR_L_FUEL_SW", "MD11_THR_C_FUEL_SW", "MD11_THR_R_FUEL_SW"),
        ["FO_START_SWITCHES_IN"] = Vars(v => v.All(x => Pos(x, 0)), "MD11_THR_L_START_SW", "MD11_THR_C_START_SW", "MD11_THR_R_START_SW"),
        ["FO_IRS_NAV"] = Vars(v => v.All(x => Pos(x, 1)), "MD11_OVHD_IRS_1_KB", "MD11_OVHD_IRS_2_KB", "MD11_OVHD_IRS_3_KB"),
        ["FO_IRS_OFF"] = Vars(v => v.All(x => Pos(x, 0)), "MD11_OVHD_IRS_1_KB", "MD11_OVHD_IRS_2_KB", "MD11_OVHD_IRS_3_KB"),
        ["FO_FLAPS_UP"] = Vars(v => Md11FoSwitching.FlapDetentIndex(v[0]) == 0, "MD11_FLAP_LATCH"),
        ["FO_FLAPS_DAF"] = Vars(v => Md11FoSwitching.FlapDetentIndex(v[0]) == 2, "MD11_FLAP_LATCH"),
        ["FO_DAF_15"] = Vars(v => Math.Abs(v[0] - Md11FoSwitching.DialRawFor(15)) <= 1.0, "MD11_DIALAFLAP_WHEEL_RNG"),
        ["FO_GEAR_DOWN"] = Vars(v => Md11GearLever.IsDown(v[0]), "MD11_MIP_GEAR_SW"),
        ["FO_GEAR_UP"] = Vars(v => v[0] < 5, "MD11_MIP_GEAR_SW"),
        ["FO_FUEL_DUMP_SAFE"] = Vars(v => Pos(v[0], 0) && Pos(v[1], 0) && !Lit(v[2]),
            "MD11_OVHD_FUEL_DUMP_GRD", "MD11_OVHD_FUEL_DUMP_BT", "MD11_OVHD_FUEL_DUMP_LT"),
        ["FO_MANF_DRAIN_SAFE"] = Vars(v => Pos(v[0], 0) && !Lit(v[1]), "MD11_OVHD_FUEL_MANF_DRAIN_GRD", "MANF_DRAIN_LT"),
        ["FO_LANDING_LIGHTS_ON"] = Vars(v => Pos(v[0], 2) && Pos(v[1], 2), "MD11_OVHD_LTS_LDG_L_SW", "MD11_OVHD_LTS_LDG_R_SW"),
        ["FO_LANDING_LIGHTS_OFF"] = Vars(v => v[0] < 1.5 && v[1] < 1.5, "MD11_OVHD_LTS_LDG_L_SW", "MD11_OVHD_LTS_LDG_R_SW"),
        ["FO_LANDING_LIGHTS_RETRACTED"] = Vars(v => Pos(v[0], 0) && Pos(v[1], 0), "MD11_OVHD_LTS_LDG_L_SW", "MD11_OVHD_LTS_LDG_R_SW"),
        ["FO_SPOILERS_ARMED"] = Vars(v => Pos(v[0], 1), Md11SpeedbrakeSystem.ArmKey),
        ["FO_SPOILERS_DOWN"] = Vars(v => Pos(v[0], 0), Md11SpeedbrakeSystem.ArmKey),
        ["FO_WINDSHIELD_ON"] = Vars(v => Pos(v[0], 1) && Pos(v[1], 1) && Pos(v[2], 0) && Pos(v[3], 0),
            "MD11_OVHD_WNDSHLD_AICE_L_BT", "MD11_OVHD_WNDSHLD_AICE_R_BT", "MD11_OVHD_WNDSHLD_AICE_BT", "MD11_OVHD_WNDSHLD_AICE_DEFOG_BT"),
        ["FO_WINDSHIELD_OFF"] = Vars(v => Pos(v[0], 0) && Pos(v[1], 0) && Pos(v[2], 1),
            "MD11_OVHD_WNDSHLD_AICE_L_BT", "MD11_OVHD_WNDSHLD_AICE_R_BT", "MD11_OVHD_WNDSHLD_AICE_DEFOG_BT"),
        ["FO_CARGO_TEMPS_OFF"] = Vars(v => Pos(v[0], 0) && Pos(v[1], 0), "MD11_OVHD_PNEU_FWD_CARGO_TEMP", "MD11_OVHD_PNEU_AFT_CARGO_TEMP"),
        ["FO_EVAC_ARMED_GUARDED"] = Vars(v => Pos(v[0], 1) && Pos(v[1], 0), "MD11_AOVHD_EVAC_SW", "MD11_AOVHD_EVAC_GRD"),
        ["FO_GPWS_NORMAL_GUARDED"] = Vars(v => Pos(v[0], 1) && Pos(v[1], 0), "MD11_AOVHD_GPWS_SW", "MD11_AOVHD_GPWS_GRD"),
        ["FO_APU_OFF"] = Vars(v => v[0] < 0.5 || v[0] > 2.5, "MD11_APU_STATE"),              // off or stopping
        ["FO_APU_RUNNING"] = Vars(v => Pos(v[0], 2), "MD11_APU_STATE"),
        ["FO_AUTO_FLIGHT_ON"] = Vars(v => Md11FoSwitching.AutoFlightOn(v[0], v[1]),
            Md11AutopilotEngage.ApStateKey, Md11AutopilotEngage.AtsStateKey),
        ["FO_START_AIR_READY"] = Vars(v => Pos(v[0], 2) && Pos(v[1], 1), "MD11_APU_STATE", "MD11_OVHD_PNEU_APU_BLEED_BT"),
        ["FO_ALTIMETERS_STD"] = new(Alts, r =>
        {
            var v = Alts.Select(r).ToArray();
            if (!Known(v) || v.Any(x => x <= 0)) return double.NaN;                         // 0 = not yet published
            return B(v.All(Md11Fcp.IsStandard));
        }),
        ["FO_ENGINES_STOPPED"] = Vars(v => v.All(x => x < 10), "FO_ENG1_N2", "FO_ENG2_N2", "FO_ENG3_N2"),
        ["FO_ENG3_START_CUTOUT"] = Vars(v => Pos(v[0], 0) && v[1] >= 45, "MD11_THR_R_START_SW", "FO_ENG3_N2"),
    };

    internal static IReadOnlyCollection<string> SyntheticKeys { get; } =
        Synthetics.Keys.Concat(new[] { "FO_ENG1_N2", "FO_ENG2_N2", "FO_ENG3_N2", "FO_DAF_TAKEOFF" }).ToArray();

    internal static IReadOnlyCollection<string> SyntheticInputs { get; } =
        Synthetics.Values.SelectMany(s => s.Inputs).Append("MD11_DIALAFLAP_WHEEL_RNG").Distinct().ToArray();

    /// <summary>Pure computation of one synthetic field (tests drive it with a dictionary).</summary>
    internal static double Compute(string field, Func<string, double> read, int takeoffFlaps = -1)
    {
        if (field == "FO_DAF_TAKEOFF")
        {
            if (!Md11FoSwitching.IsDialAFlapDegrees(takeoffFlaps)) return double.NaN;
            double wheel = read("MD11_DIALAFLAP_WHEEL_RNG");
            return double.IsNaN(wheel) ? double.NaN : B(Math.Abs(wheel - Md11FoSwitching.DialRawFor(takeoffFlaps)) <= 1.0);
        }
        return Synthetics.TryGetValue(field, out var s) ? s.Compute(read) : double.NaN;
    }

    private double Raw(string key) => key switch
    {
        "FO_ENG1_N2" => Eng1N2,
        "FO_ENG2_N2" => Eng2N2,
        "FO_ENG3_N2" => Volatile.Read(ref _eng3N2),
        _ => Sc?.GetCachedVariableValue(key) ?? double.NaN,
    };

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        switch (field)
        {
            case "FO_ENG1_N2": value = Eng1N2; return true;
            case "FO_ENG2_N2": value = Eng2N2; return true;
            case "FO_ENG3_N2": value = Volatile.Read(ref _eng3N2); return true;
            case "FO_DAF_TAKEOFF": value = Compute(field, Raw, GetTakeoffFlaps()); return true;
        }
        if (Synthetics.ContainsKey(field)) { value = Compute(field, Raw); return true; }
        value = double.NaN;
        return false;
    }
}
