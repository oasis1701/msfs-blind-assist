namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// ⚠️ THE NG'S AUSTRO VARIABLES THE XLS DOES NOT HAVE, AND THE ONE PLACE THAT REMOVES THEM.
///
/// Much of this definition is built by shared builders, and those builders were written on
/// the NG. An L:var nothing writes is not an error to SimConnect: it reads 0, for ever. So on
/// the XLS these were rows reading a plausible zero about systems a Lycoming has not got,
/// and a pilot scans a row like that and is reassured by it. Measured live on the XLS: the
/// battery bus, the ECU battery, the ECU bus, the water jacket and the turbocharger summed to
/// exactly 0 while its own main and essential buses read 23.4 and 23.7 volts. Worse than a
/// silent row, the master's power-up read-back SPOKE one of them — "battery 0.0" — every time
/// the XLS was switched on.
///
/// Every key here was confirmed absent from the XLS package by whole-name search, and is
/// present in the NG's. Removing them here, rather than gating each builder, keeps the split
/// auditable in one list; <c>CowsDA40PackagePresenceTests</c> is what keeps the list honest,
/// because it checks every L:var each variant binds against that variant's OWN package.
/// </summary>
public partial class CowsDA40Definition
{
    private static readonly HashSet<string> NgOnlyKeys = new(StringComparer.Ordinal)
    {
        // The Austro's electrical system. The XLS has main, essential, hot and emergency
        // buses and no battery bus between them, and no ECU to give a battery or a bus of
        // its own. Nor an ESS BUS lamp: the NG draws ESSBUS_LIGHT from the battery bus, the
        // XLS model has no such component.
        "DA40_ELEC_BUS_BATT_VOLT",
        "DA40_ELEC_BUS_ECU1_VOLT",
        "DA40_ELEC_BATT_ECU_VOLT",
        "DA40_ELEC_BATT_ECU_PERCENT",
        "DA40_ELEC_BATT_ECU_CAPACITY",
        "DA40_ANN_ESS_BUS_VOLTS",

        // The FADEC. The XLS is started on a magneto key.
        "DA40_ECU_A_STARTING",
        "DA40_ECU_B_STARTING",
        "DA40_ECU_A_FAIL_TIME",
        "DA40_ECU_B_FAIL_TIME",
        "DA40_ECU_TEST_HELD",

        // The water jacket. The Lycoming is air-cooled.
        "DA40_ENG_BLOCK_TEMP",
        "DA40_ENG_RAD_TEMP",
        "DA40_ENG_THERMOSTAT",
        "DA40_ENG_COOLANT_LEAK_RATE",

        // The turbocharger and its wastegates. The Lycoming is normally aspirated.
        "DA40_DAMAGE_TURBO_RAW",
        "DA40_DAMAGE_TURBO_FRICTION",
        "DA40_FAIL_WASTEGATE_A",
        "DA40_FAIL_WASTEGATE_B",

        // The Austro's high-pressure fuel system and its two pumps. The XLS's own fuel
        // failures (injectors per cylinder, a leak per tank, the pump, the spring) are bound
        // in CowsDA40Definition.XlsFailures.
        "DA40_DAMAGE_FUEL_RAW",
        "DA40_DAMAGE_FUEL_PUMP_1",
        "DA40_DAMAGE_FUEL_PUMP_2",
        "DA40_DAMAGE_FUEL_OSCILLATION",
        // ⚠️ FAILURES_FUEL_L passed the first search for this list only because the XLS's
        // FAILURES_FUEL_LEAK_L contains it. Whole names, never substrings.
        "DA40_FAIL_FUEL_LEFT",
        "DA40_FAIL_FUEL_RIGHT",

        // The FADEC's propeller control. The XLS has a pitch LEVER and a governor pump, bound
        // as DA40_XLS_FAIL_PROP_LEVER and DA40_XLS_FAIL_PROP_PUMP.
        "DA40_FAIL_PROP_COMBINED",

        // Breaker trips for breakers only the NG has. Its physical breakers were already split
        // per airframe; these were their trips, left shared.
        "DA40_FAIL_CBT_ECA",
        "DA40_FAIL_CBT_ECB",
        "DA40_FAIL_CBT_FPA",
        "DA40_FAIL_CBT_FPB",
        "DA40_FAIL_CBT_XFR",

        // The NG's induction-air factor. The XLS has an ALTERNATE_AIR control but models no
        // factor behind it.
        "DA40_ICE_ALT_AIR_FACTOR",
    };

    /// <summary>
    /// The other direction: shared rows that DO NOTHING on the NG. The variable exists there,
    /// so the package-presence test cannot see it — but only the random-failure picker writes
    /// it and nothing reads it, so no breaker pops and no circuit changes. On the XLS each
    /// pops its breaker (ALT, ESS Tie, MAIN TIE).
    /// </summary>
    private static readonly HashSet<string> XlsOnlyKeys = new(StringComparer.Ordinal)
    {
        "DA40_FAIL_CBT_ALT",
        "DA40_FAIL_CBT_ESS_TIE",
        "DA40_FAIL_CBT_MAIN_TIE",
    };

    internal static IReadOnlyCollection<string> NgOnlyVariableKeys => NgOnlyKeys;
    internal static IReadOnlyCollection<string> XlsOnlyVariableKeys => XlsOnlyKeys;

    private HashSet<string> ForeignKeys => IsNG ? XlsOnlyKeys : NgOnlyKeys;

    /// <summary>Drops the other airframe's variables.</summary>
    private void RemoveForeignVariables(Dictionary<string, SimConnect.SimVarDefinition> vars)
    {
        foreach (string key in ForeignKeys) vars.Remove(key);
    }

    /// <summary>Drops the other airframe's rows from every panel list.</summary>
    private void RemoveForeignRows(Dictionary<string, List<string>> panels)
    {
        var foreign = ForeignKeys;
        foreach (var rows in panels.Values) rows.RemoveAll(foreign.Contains);
    }
}
