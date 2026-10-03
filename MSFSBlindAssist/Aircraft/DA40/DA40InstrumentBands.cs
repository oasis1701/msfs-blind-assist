namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>Which arc of a gauge a value is sitting in.</summary>
public enum GaugeBand
{
    /// <summary>Lower prohibited range — red arc below the scale's usable region.</summary>
    LowerRed,
    /// <summary>Lower caution range — yellow arc below green.</summary>
    LowerCaution,
    /// <summary>Normal operating range — green arc.</summary>
    Normal,
    /// <summary>Upper caution range — yellow arc above green.</summary>
    UpperCaution,
    /// <summary>Upper prohibited range — red arc above the scale.</summary>
    UpperRed
}

/// <summary>
/// One gauge's arcs, as boundaries between them. A null boundary means that arc does not
/// exist on this instrument — the RPM gauge has no lower red or lower caution at all, and
/// Load has no upper red.
///
/// Boundaries follow the AFM's own wording: a value exactly ON a boundary belongs to the
/// HIGHER band, because the table reads "50° to 135°C" for green with 135-140 as caution.
/// </summary>
public sealed record BandRange(
    double? LowerRedBelow,
    double? GreenFrom,
    double? GreenTo,
    double? UpperRedFrom)
{
    public GaugeBand Classify(double value)
    {
        if (LowerRedBelow.HasValue && value < LowerRedBelow.Value) return GaugeBand.LowerRed;
        if (GreenFrom.HasValue && value < GreenFrom.Value) return GaugeBand.LowerCaution;
        if (GreenTo.HasValue && value > GreenTo.Value)
        {
            if (UpperRedFrom.HasValue && value > UpperRedFrom.Value) return GaugeBand.UpperRed;
            return GaugeBand.UpperCaution;
        }
        return GaugeBand.Normal;
    }
}

/// <summary>
/// The DA40-NG's instrument arcs, transcribed from AFM section 2.5 ENGINE INSTRUMENT
/// MARKINGS and section 2.3 AIRSPEED INDICATOR MARKINGS.
///
/// This exists because a sighted pilot does not read "87 degrees" off the oil temperature
/// gauge — they see the needle sitting in the green. Reporting only the number gives a
/// blind pilot strictly less than the glance gives everyone else, so every gauge with
/// published arcs reports its band alongside its value.
///
/// The table, verbatim from the AFM:
///
///   Indication     lower red      lower caution   green          upper caution   upper red
///   RPM            --             --              up to 2100     2100-2300       above 2300
///   Oil pressure   below 0.9 bar  0.9-2.5         2.5-6.0        6.0-6.5         above 6.5
///   Oil temp       below -30 C    -30 to 50       50-135         135-140         above 140
///   Coolant temp   below -30 C    -30 to 60       60-100         100-105         above 105
///   Gearbox temp   below -30 C    -30 to 35       35-115         115-120         above 120
///   Load           --             --              up to 92%      92-100%         --
///   Fuel temp      below -25 C    -25 to -20      -20 to 55      55-60           above 60
///   Ammeter        --             --              up to 60 A     60-70           above 70
///   Voltmeter      below 24.1 V   24.1-25         25-30          30-32           above 32
///   Fuel quantity  below 1 USG    --              1-14 USG       --              --
///
/// Fuel quantity has no lower CAUTION band, so its lower-red boundary and its green
/// floor are the same number: below 1 gallon is red, and there is nothing in between.
///
/// Airspeed (section 2.3): green 66-130 KIAS, yellow 130-172 (smooth air only),
/// red line 172 KIAS = Vne.
/// </summary>
public static class DA40InstrumentBands
{
    /// <summary>Arcs keyed by the MSFSBA variable key they annotate.</summary>
    private static readonly Dictionary<string, BandRange> Bands = new()
    {
        // Propeller RPM. No lower arcs; max continuous 2100, take-off 2300.
        ["DA40_START_RPM"]            = new BandRange(null, null, 2100, 2300),
        ["DA40_ECU_PROP_SENSED"]      = new BandRange(null, null, 2100, 2300),
        ["DA40_POWER_RPM"]            = new BandRange(null, null, 2100, 2300),

        // Oil pressure, bar.
        ["DA40_START_OIL_PRESSURE"]   = new BandRange(0.9, 2.5, 6.0, 6.5),

        // Oil temperature, degrees C.
        ["DA40_START_OIL_TEMP"]       = new BandRange(-30, 50, 135, 140),

        // Coolant temperature, degrees C.
        ["DA40_START_COOLANT_TEMP"]   = new BandRange(-30, 60, 100, 105),

        // Gearbox temperature, degrees C. Note the AFM says the yellow arc here is not an
        // engine-manufacturer limit — it was added purely to draw attention to the
        // approaching maximum, and carries no time limit.
        ["DA40_START_GEARBOX_TEMP"]   = new BandRange(-30, 35, 115, 120),
        ["DA40_ECU_PRE_GEARBOX"]      = new BandRange(-30, 35, 115, 120),

        // Engine load, percent. Max continuous 92, take-off 100. No upper red.
        ["DA40_START_LOAD"]           = new BandRange(null, null, 92, null),
        ["DA40_POWER_LOAD"]           = new BandRange(null, null, 92, null),

        // Electrical. Ammeter has no lower arcs; voltmeter has the full set.
        ["DA40_ELEC_DISP_AMPS"]       = new BandRange(null, null, 60, 70),
        ["DA40_ELEC_DISP_VOLTS"]      = new BandRange(24.1, 25, 30, 32),
        ["DA40_START_VOLTS"]          = new BandRange(24.1, 25, 30, 32),

        // Fuel quantity, US gallons. Red below 1, green to 14. There is no yellow and no
        // upper red — the gauge simply stops at 14 (AFM 2.14.4), which the Fuel System
        // panel reports in words because a saturated gauge is not a quantity.
        ["DA40_FUEL_MAIN_IND"]        = new BandRange(1, 1, 14, null),
        ["DA40_FUEL_AUX_IND"]         = new BandRange(1, 1, 14, null),

        // Fuel temperature, degrees C. Above 60 costs high-pressure pump efficiency and
        // the AFM has you reduce power and increase airspeed.
        ["DA40_FUEL_MAIN_TEMP"]       = new BandRange(-25, -20, 55, 60),
        ["DA40_FUEL_AUX_TEMP"]        = new BandRange(-25, -20, 55, 60),

        // ---- DA40-XLS (Lycoming IO-360-M1A), AFM 6.01.01-E rev 10, section 2.5 ----
        // Its own keys, its own arcs: the Austro's 2300-rpm red line is the Lycoming's
        // yellow band, and a shared key would have flagged a normal cruise.
        // RPM: green 500-2400, yellow 2400-2700, red above 2700. No lower arcs.
        ["DA40_XLS_RPM"]              = new BandRange(null, null, 2400, 2700),
        // Oil pressure, psi: red below 25, yellow 25-55, green 56-95, yellow 96-97, red above 97.
        // Measured 73.7 running, 0 stopped.
        ["DA40_XLS_OIL_PRESSURE"]     = new BandRange(25, 56, 95, 97),
        // ⚠️ OIL TEMPERATURE, IN THE GAUGE'S OWN FAHRENHEIT, AND THESE ARE THE AEROPLANE'S
        // NUMBERS RATHER THAN A CONVERSION. The XLS's panel.xml declares the gauge against
        // L:DISP_OT: scale -31 to 295, red to -22, yellow to 122, green 122 to 275, yellow
        // to 285, red above. That is 50 to 135 C. The table here used to read 65 / 110 /
        // 118 C - the AUSTRO's arcs on a LYCOMING - so a caution was called at 111 C on an
        // engine its own gauge keeps green to 135, and the planned arithmetic conversion of
        // those numbers (149 / 230 / 244 F) would have kept calling it. Read the gauge.
        ["DA40_XLS_OIL_TEMP"]         = new BandRange(-22, 122, 275, 285),
        // Fuel flow, US gal/h: green 1-20, caution above 20. Measured 10.6 at 2200 rpm.
        ["DA40_XLS_FUEL_FLOW"]        = new BandRange(null, 1, 20, null),
        // Fuel pressure, in the gauge's own PSI now that the source is DISP_FP: red below
        // 14, green 14 to 35, red above. No yellow. Straight off panel.xml, which declares
        // the gauge 0 to 40 against L:DISP_FP; measured 27.5 running.
        ["DA40_XLS_FUEL_PRESSURE"]    = new BandRange(14, 14, 35, 35),

        // The XLS cylinder heads, in the gauge's own Fahrenheit: the MFD plugin draws
        // green 150-475, caution 475-500, red above (Da40MfdPlugin 'CHT °F' bars). No
        // lower arc - a cold head is a cold head.
        ["DA40_XLS_CHT_HOT"] = new BandRange(null, null, 475, 500),
        ["DA40_XLS_CHT_1"] = new BandRange(null, null, 475, 500),
        ["DA40_XLS_CHT_2"] = new BandRange(null, null, 475, 500),
        ["DA40_XLS_CHT_3"] = new BandRange(null, null, 475, 500),
        ["DA40_XLS_CHT_4"] = new BandRange(null, null, 475, 500),

        // Exhaust has no AFM limit; the COWS POH recommends not exceeding 1350 F, so that
        // is a caution and nothing is red.
        ["DA40_XLS_EGT_HOT"] = new BandRange(null, null, 1350, null),
        ["DA40_XLS_EGT_1"] = new BandRange(null, null, 1350, null),
        ["DA40_XLS_EGT_2"] = new BandRange(null, null, 1350, null),
        ["DA40_XLS_EGT_3"] = new BandRange(null, null, 1350, null),
        ["DA40_XLS_EGT_4"] = new BandRange(null, null, 1350, null)
    };

    /// <summary>The arcs for a variable, or null when that gauge has no published arcs.</summary>
    public static BandRange? For(string variableKey)
        => Bands.TryGetValue(variableKey, out var range) ? range : null;

    /// <summary>Spoken name of a band, as a pilot would describe the arc.</summary>
    public static string Describe(GaugeBand band) => band switch
    {
        GaugeBand.LowerRed     => "red, below minimum",
        GaugeBand.LowerCaution => "yellow, low",
        GaugeBand.Normal       => "green",
        GaugeBand.UpperCaution => "yellow, high",
        GaugeBand.UpperRed     => "red, above maximum",
        _ => ""
    };

    /// <summary>
    /// Appends the band to an already-formatted reading, e.g. "87 celsius" becomes
    /// "87 celsius, green". Returns the text unchanged for a gauge with no arcs, so
    /// callers can apply this unconditionally.
    /// </summary>
    public static string Annotate(string variableKey, double value, string formattedValue)
    {
        var range = For(variableKey);
        if (range is null) return formattedValue;

        return $"{formattedValue}, {Describe(range.Classify(value))}";
    }

    /// <summary>Every variable key that carries arcs — used by tests and diagnostics.</summary>
    public static IReadOnlyCollection<string> AnnotatedKeys => Bands.Keys;
}
