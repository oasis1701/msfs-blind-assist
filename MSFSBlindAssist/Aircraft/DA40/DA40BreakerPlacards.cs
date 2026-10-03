namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// ⚠️ A BREAKER IS NAMED BY ITS PLACARD, VERBATIM — the text the cockpit shows on it — and
/// never by an expansion of the abbreviation. The pilot's ruling: names appear exactly as
/// they appear in the cockpit, and nothing is simplified.
///
/// The placard is the breaker's own <c>TOOLTIPID</c> in each model's
/// <c>*_CircuitBreakers.xml</c>, and <c>CowsDA40BreakerPlacardTests</c> reads it back out of
/// the installed package so this table cannot drift from the aeroplane.
///
/// ⚠️ EXPANDING THEM WAS NOT MERELY WORDY, IT WAS WRONG ON FOUR XLS BREAKERS. The XLS names
/// were taken from the real Diamond AFM section 1.5.6 (ANNUN., AUTOPILOT, FAN/OAT, T&amp;B),
/// but THIS model wires those four L:vars to other things, and every source inside the
/// package agrees on it: the tooltip, the failure that pops the breaker, and the circuit the
/// breaker cuts. <c>CB_APT</c> is placarded ALTPROT, is popped by FAILURES_CB_ALT_PROT and
/// gates circuit 24 "ALT PROT" — so the row MSFSBA called "Autopilot Breaker" cut alternator
/// protection, beside a second "Autopilot Breaker" (<c>CB_AFC</c>, placarded AFCS) that is
/// the autopilot. Likewise <c>CB_ACN</c> ALTCONT (not an annunciator panel), <c>CB_FAN</c>
/// CDUFAN (circuit 21 "CDU FAN", not a fan and OAT), and <c>CB_TAS</c> TAS (circuit 35
/// "FIS (DATA)", not a turn and bank). The AFM describes the real aeroplane; the pilot is
/// flying this one.
/// </summary>
internal static class DA40BreakerPlacards
{
    /// <summary>Each <c>L:CB_*</c> and the placard the cockpit shows on it.</summary>
    private static readonly Dictionary<string, string> Placards = new(StringComparer.Ordinal)
    {
        // Both airframes.
        ["CB_ADC"] = "ADC",
        ["CB_AFC"] = "AFCS",
        ["CB_AHR"] = "AHRS",
        ["CB_AUD"] = "AUDIO",
        ["CB_AVF"] = "AVFAN",
        ["CB_AVN"] = "AVBUS",
        ["CB_BAT"] = "BATT",
        ["CB_CM1"] = "COM1",
        ["CB_CM2"] = "COM2",
        ["CB_ENG"] = "ENG INST",
        ["CB_ESS"] = "ESS Tie",
        ["CB_FLD"] = "FLOOD",
        ["CB_FLP"] = "FLAPS",
        ["CB_GP1"] = "GPS/NAV1",
        ["CB_GP2"] = "GPS/NAV2",
        ["CB_HOR"] = "HORIZON",
        ["CB_INT"] = "INST.",
        ["CB_LDL"] = "LANDING",
        ["CB_MAN"] = "MAIN TIE",
        ["CB_MFD"] = "MFD",
        ["CB_MTC"] = "MASTER CONTROL",
        ["CB_PFD"] = "PFD",
        ["CB_PIT"] = "PITOT",
        ["CB_POS"] = "POSITION",
        ["CB_STB"] = "STROBE",
        ["CB_STR"] = "START",
        ["CB_TXM"] = "TAXI/MAP",
        ["CB_XPR"] = "XPDR",

        // The NG's Austro.
        ["CB_ECA"] = "ECU A",
        ["CB_ECB"] = "ECU B",
        ["CB_FPA"] = "FUEL PUMP A",
        ["CB_FPB"] = "FUEL PUMP B",
        ["CB_PWR"] = "PWR",
        ["CB_XFR"] = "XFR",

        // The XLS's Lycoming.
        ["CB_ACN"] = "ALTCONT",
        ["CB_ALT"] = "ALT",
        ["CB_APT"] = "ALTPROT",
        ["CB_FAN"] = "CDUFAN",
        ["CB_FUP"] = "FUELPUMP",
        ["CB_TAS"] = "TAS",
    };

    /// <summary>
    /// Which breaker each trip failure POPS, read from each model's Failures.xml
    /// (<c>(L:FAILURES_CB_x) … 1 (&gt;L:CB_y)</c>). A trip failure that pops nothing is an
    /// OVERLOAD — it adds a large draw to a bus instead (FAILURES_CB_AP and _FLAP on both,
    /// and _CDU_FAN and _TAS on the NG) — and carries the vendor's own designation, because
    /// there is no placard to name it by.
    /// </summary>
    private static readonly Dictionary<string, string> SharedPops = new(StringComparer.Ordinal)
    {
        ["FAILURES_CB_ADC"] = "CB_ADC",
        ["FAILURES_CB_AFCS"] = "CB_AFC",
        ["FAILURES_CB_AHRS"] = "CB_AHR",
        ["FAILURES_CB_AUD"] = "CB_AUD",
        ["FAILURES_CB_AV_FAN"] = "CB_AVF",
        ["FAILURES_CB_COM1"] = "CB_CM1",
        ["FAILURES_CB_COM2"] = "CB_CM2",
        ["FAILURES_CB_ENGINST"] = "CB_ENG",
        ["FAILURES_CB_FLAPS"] = "CB_FLP",
        ["FAILURES_CB_HORIZON"] = "CB_HOR",
        ["FAILURES_CB_MAST"] = "CB_MTC",
        ["FAILURES_CB_MFD"] = "CB_MFD",
        ["FAILURES_CB_NAV1"] = "CB_GP1",
        ["FAILURES_CB_NAV2"] = "CB_GP2",
        ["FAILURES_CB_PFD"] = "CB_PFD",
        ["FAILURES_CB_PITOT"] = "CB_PIT",
        ["FAILURES_CB_START"] = "CB_STR",
        ["FAILURES_CB_XPDR"] = "CB_XPR",
    };

    private static readonly Dictionary<string, string> NgPops = new(SharedPops, StringComparer.Ordinal)
    {
        ["FAILURES_CB_ECA"] = "CB_ECA",
        ["FAILURES_CB_ECB"] = "CB_ECB",
        ["FAILURES_CB_FPA"] = "CB_FPA",
        ["FAILURES_CB_FPB"] = "CB_FPB",
        ["FAILURES_CB_XFR"] = "CB_XFR",
    };

    private static readonly Dictionary<string, string> XlsPops = new(SharedPops, StringComparer.Ordinal)
    {
        ["FAILURES_CB_ALT"] = "CB_ALT",
        ["FAILURES_CB_ALT_CONT"] = "CB_ACN",
        ["FAILURES_CB_ALT_PROT"] = "CB_APT",
        ["FAILURES_CB_BATT"] = "CB_BAT",
        ["FAILURES_CB_CDU_FAN"] = "CB_FAN",
        ["FAILURES_CB_ESS_TIE"] = "CB_ESS",
        ["FAILURES_CB_FUEL_PUMP"] = "CB_FUP",
        ["FAILURES_CB_MAIN_TIE"] = "CB_MAN",
        ["FAILURES_CB_TAS"] = "CB_TAS",
    };

    internal static IReadOnlyDictionary<string, string> AllPlacards => Placards;

    internal static IReadOnlyDictionary<string, string> PopsFor(bool isNg) => isNg ? NgPops : XlsPops;

    /// <summary>The placard of the breaker on <paramref name="lvar"/>.</summary>
    internal static string For(string lvar) => Placards[lvar];

    /// <summary>
    /// A trip failure is named after the breaker it pops; an overload, which pops none, by
    /// the vendor's designation for it (FAILURES_CB_CDU_FAN → "CDU FAN").
    /// </summary>
    internal static string TripLabel(string failureVar, bool isNg)
        => PopsFor(isNg).TryGetValue(failureVar, out var cb)
            ? For(cb)
            : failureVar["FAILURES_CB_".Length..].Replace('_', ' ');
}
