using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The engine instruments and the ECAM system pages, each a status box in the Displays section.
/// A page lists the values its ECAM page PRINTS as a number, read from the page's own drawing
/// routine (ECAM_ENG::Draw and its siblings, v1.0.11); a needle with no printed number (generator
/// load, the hydraulic reservoirs) and a value whose unit the page does not establish (oil
/// quantity, pack flow, cabin vertical speed, the engine fuel pressure) are left out rather than
/// guessed. The engine instruments are the simulator's own engine values behind the round gauges.
/// The fuel tank weights are kilograms (they add up to the simulator's total), and fuel reads in
/// the weight unit set on the tablet (L:INI_IS_METRIC), which the definition's display text applies.
/// Every value is an aircraft variable; nothing reads the simulator's memory.
/// </summary>
public static class A300EcamPages
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string WeightUnitKey = "A300_SD_WEIGHT_UNIT";
    public const string Engine1BleedPressureKey = "A300_SD_ENG1_BLEED_PSI";
    public const string Engine2BleedPressureKey = "A300_SD_ENG2_BLEED_PSI";
    public const string ApuGeneratorVoltageKey = "A300_SD_APU_GEN_V";
    public const string ApuGeneratorFrequencyKey = "A300_SD_APU_GEN_HZ";

    public const double PoundsPerKilogram = 2.20462262185;

    /// <summary>Keys whose value is a weight in kilograms.</summary>
    public static readonly IReadOnlySet<string> KilogramKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "A300_SD_FUEL_L_OUTER", "A300_SD_FUEL_L_INNER", "A300_SD_FUEL_CENTER", "A300_SD_FUEL_R_INNER",
        "A300_SD_FUEL_R_OUTER", "A300_SD_FUEL_TRIM", "A300_SD_FUEL_TOTAL",
    };

    /// <summary>Keys whose value is a fuel flow in pounds per hour.</summary>
    public static readonly IReadOnlySet<string> PoundsPerHourKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "A300_SD_ENG1_FF", "A300_SD_ENG2_FF",
    };

    public static readonly IReadOnlyList<A300Readout> Readouts = new[]
    {
        L(WeightUnitKey, "Weight unit", "INI_IS_METRIC", WeightUnit),

        // Engine instruments: the simulator's engines behind the round gauges.
        S("A300_SD_ENG1_N1", "N1 1", "TURB ENG N1:1", "percent", Percent),
        S("A300_SD_ENG2_N1", "N1 2", "TURB ENG N1:2", "percent", Percent),
        S("A300_SD_ENG1_EGT", "EGT 1", "ENG EXHAUST GAS TEMPERATURE:1", "celsius", Celsius),
        S("A300_SD_ENG2_EGT", "EGT 2", "ENG EXHAUST GAS TEMPERATURE:2", "celsius", Celsius),
        S("A300_SD_ENG1_N2", "N2 1", "TURB ENG N2:1", "percent", Percent),
        S("A300_SD_ENG2_N2", "N2 2", "TURB ENG N2:2", "percent", Percent),
        S("A300_SD_ENG1_FF", "Fuel flow 1", "ENG FUEL FLOW PPH:1", "pounds per hour", v => FlowPerHour(v, metric: false)),
        S("A300_SD_ENG2_FF", "Fuel flow 2", "ENG FUEL FLOW PPH:2", "pounds per hour", v => FlowPerHour(v, metric: false)),

        // ECAM ENG.
        L("A300_SD_ENG1_OIL_PSI", "Oil pressure 1", "INI_eng1_oil_pressure", Psi),
        L("A300_SD_ENG2_OIL_PSI", "Oil pressure 2", "INI_eng2_oil_pressure", Psi),
        L("A300_SD_ENG1_OIL_TEMP", "Oil temperature 1", "INI_eng1_oil_temperature", Celsius),
        L("A300_SD_ENG2_OIL_TEMP", "Oil temperature 2", "INI_eng2_oil_temperature", Celsius),
        L("A300_SD_ENG1_NACELLE", "Nacelle temperature 1", "INI_eng1_nacelle_temp", Celsius),
        L("A300_SD_ENG2_NACELLE", "Nacelle temperature 2", "INI_eng2_nacelle_temp", Celsius),
        L(Engine1BleedPressureKey, "Bleed pressure 1", "INI_eng1_producing_bleed_psi", Psi),
        L(Engine2BleedPressureKey, "Bleed pressure 2", "INI_eng2_producing_bleed_psi", Psi),
        L("A300_SD_ENG1_VIB_N1", "N1 vibration 1", "INI_eng1_vibration_N1", Tenths),
        L("A300_SD_ENG2_VIB_N1", "N1 vibration 2", "INI_eng2_vibration_N1", Tenths),
        L("A300_SD_ENG1_VIB_N2", "N2 vibration 1", "INI_eng1_vibration_N2", Tenths),
        L("A300_SD_ENG2_VIB_N2", "N2 vibration 2", "INI_eng2_vibration_N2", Tenths),

        // ECAM ELEC AC.
        L("A300_SD_GEN1_V", "Generator 1 voltage", "INI_elec_gen1_voltage", Volts),
        L("A300_SD_GEN1_HZ", "Generator 1 frequency", "INI_elec_gen1_frequency", Hertz),
        L("A300_SD_IDG1_TEMP", "IDG 1 temperature", "INI_elec_idg1_temperature", Celsius),
        L("A300_SD_GEN2_V", "Generator 2 voltage", "INI_elec_gen2_voltage", Volts),
        L("A300_SD_GEN2_HZ", "Generator 2 frequency", "INI_elec_gen2_frequency", Hertz),
        L("A300_SD_IDG2_TEMP", "IDG 2 temperature", "INI_elec_idg2_temperature", Celsius),
        L(ApuGeneratorVoltageKey, "APU generator voltage", "INI_apu_gen_voltage", Volts),
        L(ApuGeneratorFrequencyKey, "APU generator frequency", "INI_apu_gen_frequency", Hertz),

        // ECAM ELEC DC.
        L("A300_SD_BAT1_V", "Battery 1 voltage", "INI_BATTERY1_CURR_VOLTAGE", Volts),
        L("A300_SD_BAT1_A", "Battery 1 current", "INI_BATTERY1_CURR_AMPS", Amps),
        L("A300_SD_BAT2_V", "Battery 2 voltage", "INI_BATTERY2_CURR_VOLTAGE", Volts),
        L("A300_SD_BAT2_A", "Battery 2 current", "INI_BATTERY2_CURR_AMPS", Amps),
        L("A300_SD_BAT3_V", "Battery 3 voltage", "INI_BATTERY3_CURR_VOLTAGE", Volts),
        L("A300_SD_BAT3_A", "Battery 3 current", "INI_BATTERY3_CURR_AMPS", Amps),
        L("A300_SD_TR1_V", "TR 1 voltage", "INI_TR1_VOLTAGE", Volts),
        L("A300_SD_TR1_A", "TR 1 current", "INI_TR1_AMPS", Amps),
        L("A300_SD_TR2_V", "TR 2 voltage", "INI_TR2_VOLTAGE", Volts),
        L("A300_SD_TR2_A", "TR 2 current", "INI_TR2_AMPS", Amps),
        L("A300_SD_TR3_V", "TR 3 voltage", "INI_TR3_VOLTAGE", Volts),
        L("A300_SD_TR3_A", "TR 3 current", "INI_TR3_AMPS", Amps),

        // ECAM HYD.
        L("A300_SD_HYD_BLUE", "Blue pressure", "INI_hyd_blue_pressure", Psi),
        L("A300_SD_HYD_GREEN", "Green pressure", "INI_hyd_green_pressure", Psi),
        L("A300_SD_HYD_YELLOW", "Yellow pressure", "INI_hyd_yellow_pressure", Psi),

        // ECAM BLEED (the bleed pressures are shared with ECAM ENG).
        L("A300_SD_ENG1_BLEED_TEMP", "Bleed temperature 1", "INI_eng1_temperature", Celsius),
        L("A300_SD_ENG2_BLEED_TEMP", "Bleed temperature 2", "INI_eng2_temperature", Celsius),
        L("A300_SD_APU_BLEED_PSI", "APU bleed pressure", "INI_apu_PSI", Psi),
        L("A300_SD_PACK1_TEMP", "Pack 1 temperature", "INI_bleed_pack1_temperature", Celsius),
        L("A300_SD_PACK1_DISCH", "Pack 1 discharge temperature", "INI_PACK1_DISCHARGE_TEMPERATURE", Celsius),
        L("A300_SD_PACK2_TEMP", "Pack 2 temperature", "INI_bleed_pack2_temperature", Celsius),
        L("A300_SD_PACK2_DISCH", "Pack 2 discharge temperature", "INI_PACK2_DISCHARGE_TEMPERATURE", Celsius),

        // ECAM COND.
        L("A300_SD_COND_CKPT", "Cockpit temperature", "INI_cockpit_temperature", Celsius),
        L("A300_SD_COND_FWD", "Forward cabin temperature", "INI_forward_temperature", Celsius),
        L("A300_SD_COND_MID", "Mid cabin temperature", "INI_mid_temperature", Celsius),
        L("A300_SD_COND_AFT", "Aft cabin temperature", "INI_aft_temperature", Celsius),
        L("A300_SD_COND_BULK", "Bulk cargo temperature", "INI_bulk_temperature", Celsius),

        // ECAM PRESS.
        L("A300_SD_CABIN_ALT", "Cabin altitude", "INI_cabin_alt", Feet),
        L("A300_SD_DELTA_P", "Differential pressure", "INI_Delta_PSI", PsiTenths),

        // ECAM FUEL: tank weights in kilograms.
        L("A300_SD_FUEL_L_OUTER", "Left outer tank", "INI_FUEL_WEIGHT_LEFT_OUTER", v => Weight(v, metric: true)),
        L("A300_SD_FUEL_L_INNER", "Left inner tank", "INI_FUEL_WEIGHT_LEFT_INNER", v => Weight(v, metric: true)),
        L("A300_SD_FUEL_CENTER", "Center tank", "INI_FUEL_WEIGHT_CENTER", v => Weight(v, metric: true)),
        L("A300_SD_FUEL_R_INNER", "Right inner tank", "INI_FUEL_WEIGHT_RIGHT_INNER", v => Weight(v, metric: true)),
        L("A300_SD_FUEL_R_OUTER", "Right outer tank", "INI_FUEL_WEIGHT_RIGHT_OUTER", v => Weight(v, metric: true)),
        L("A300_SD_FUEL_TRIM", "Trim tank", "INI_FUEL_WEIGHT_TRIM", v => Weight(v, metric: true)),
        S("A300_SD_FUEL_TOTAL", "Total fuel", "FUEL TOTAL QUANTITY WEIGHT", "kilograms", v => Weight(v, metric: true)),

        // ECAM APU (its generator is shared with ECAM ELEC AC).
        L("A300_SD_APU_N", "APU speed", "INI_apu_n1", WholePercent),
        L("A300_SD_APU_EGT", "APU EGT", "INI_apu_egt", Celsius),
    };

    /// <summary>The pages, in the order the Displays section lists them, each with its lines.</summary>
    public static readonly IReadOnlyList<(string Panel, IReadOnlyList<string> Keys)> Pages = new (string, IReadOnlyList<string>)[]
    {
        ("ECAM Engine", new[]
        {
            "A300_SD_ENG1_OIL_PSI", "A300_SD_ENG2_OIL_PSI", "A300_SD_ENG1_OIL_TEMP", "A300_SD_ENG2_OIL_TEMP",
            "A300_SD_ENG1_NACELLE", "A300_SD_ENG2_NACELLE", Engine1BleedPressureKey, Engine2BleedPressureKey,
            "A300_SD_ENG1_VIB_N1", "A300_SD_ENG2_VIB_N1", "A300_SD_ENG1_VIB_N2", "A300_SD_ENG2_VIB_N2",
        }),
        ("ECAM Electrical AC", new[]
        {
            "A300_SD_GEN1_V", "A300_SD_GEN1_HZ", "A300_SD_IDG1_TEMP", "A300_SD_GEN2_V", "A300_SD_GEN2_HZ", "A300_SD_IDG2_TEMP",
            ApuGeneratorVoltageKey, ApuGeneratorFrequencyKey,
        }),
        ("ECAM Electrical DC", new[]
        {
            "A300_SD_BAT1_V", "A300_SD_BAT1_A", "A300_SD_BAT2_V", "A300_SD_BAT2_A", "A300_SD_BAT3_V", "A300_SD_BAT3_A",
            "A300_SD_TR1_V", "A300_SD_TR1_A", "A300_SD_TR2_V", "A300_SD_TR2_A", "A300_SD_TR3_V", "A300_SD_TR3_A",
        }),
        ("ECAM Hydraulics", new[] { "A300_SD_HYD_BLUE", "A300_SD_HYD_GREEN", "A300_SD_HYD_YELLOW" }),
        ("ECAM Bleed", new[]
        {
            Engine1BleedPressureKey, "A300_SD_ENG1_BLEED_TEMP", Engine2BleedPressureKey, "A300_SD_ENG2_BLEED_TEMP",
            "A300_SD_APU_BLEED_PSI", "A300_SD_PACK1_TEMP", "A300_SD_PACK1_DISCH", "A300_SD_PACK2_TEMP", "A300_SD_PACK2_DISCH",
        }),
        ("ECAM Air Conditioning", new[]
        {
            "A300_SD_COND_CKPT", "A300_SD_COND_FWD", "A300_SD_COND_MID", "A300_SD_COND_AFT", "A300_SD_COND_BULK",
        }),
        ("ECAM Pressurization", new[] { "A300_SD_CABIN_ALT", "A300_SD_DELTA_P" }),
        ("ECAM Fuel", new[]
        {
            WeightUnitKey, "A300_SD_FUEL_L_OUTER", "A300_SD_FUEL_L_INNER", "A300_SD_FUEL_CENTER", "A300_SD_FUEL_R_INNER",
            "A300_SD_FUEL_R_OUTER", "A300_SD_FUEL_TRIM", "A300_SD_FUEL_TOTAL",
        }),
        ("ECAM APU", new[] { "A300_SD_APU_N", "A300_SD_APU_EGT", "A300_SD_APU_BLEED_PSI", ApuGeneratorVoltageKey, ApuGeneratorFrequencyKey }),
    };

    /// <summary>The engine instruments box (the round gauges on the center panel).</summary>
    public static readonly IReadOnlyList<string> EngineInstruments = new[]
    {
        WeightUnitKey, "A300_SD_ENG1_N1", "A300_SD_ENG2_N1", "A300_SD_ENG1_EGT", "A300_SD_ENG2_EGT",
        "A300_SD_ENG1_N2", "A300_SD_ENG2_N2", "A300_SD_ENG1_FF", "A300_SD_ENG2_FF",
    };

    /// <summary>A weight stored in kilograms, in the tablet's unit.</summary>
    public static string Weight(double kilograms, bool metric) => metric
        ? $"{Math.Round(kilograms).ToString("#,0", Inv)} kilograms"
        : $"{Math.Round(kilograms * PoundsPerKilogram).ToString("#,0", Inv)} pounds";

    /// <summary>A fuel flow given in pounds per hour, in the tablet's unit.</summary>
    public static string FlowPerHour(double poundsPerHour, bool metric) => metric
        ? $"{Math.Round(poundsPerHour / PoundsPerKilogram).ToString("#,0", Inv)} kilograms per hour"
        : $"{Math.Round(poundsPerHour).ToString("#,0", Inv)} pounds per hour";

    public static string WeightUnit(double metric) => metric >= 0.5 ? "kilograms" : "pounds";

    private static string Whole(double value)
    {
        string text = Math.Round(value).ToString("#,0", Inv);
        return text == "-0" ? "0" : text;
    }

    private static string Psi(double value) => $"{Whole(value)} psi";
    private static string PsiTenths(double value) => $"{value.ToString("0.0", Inv)} psi";
    private static string Celsius(double value) => $"{Whole(value)} degrees Celsius";
    private static string Volts(double value) => $"{Whole(value)} volts";
    private static string Hertz(double value) => $"{Whole(value)} hertz";
    private static string Amps(double value) => $"{value.ToString("0.0", Inv)} amps";
    private static string Percent(double value) => $"{value.ToString("0.0", Inv)} percent";
    private static string WholePercent(double value) => $"{Whole(value)} percent";
    private static string Tenths(double value) => value.ToString("0.0", Inv);
    private static string Feet(double value) => $"{Whole(value)} feet";

    // A page readout's Panel is the Displays section: the section's panels list their own lines
    // (A300DisplayPanels), and one value can be on several pages.
    private static A300Readout L(string key, string name, string var, Func<double, string> format) =>
        new(key, name, A300DisplayPanels.Section, var, false, "number", format);

    private static A300Readout S(string key, string name, string var, string units, Func<double, string> format) =>
        new(key, name, A300DisplayPanels.Section, var, true, units, format);
}
