namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// One of the 757's announced lights or states: the variable key the app reads it under, BlueBird's L:var,
/// the Ctrl+M label (also its status row's name), the phrase a value maps to, the map panel whose status
/// display shows it (null: on no display), and the words that display shows per value.
/// <para>
/// The phrase's second argument is the last delivered value of <paramref name="RelatedKey"/>'s row (null when
/// there is none, or none was delivered). A null phrase is not tracked (an unreadable value, a door between
/// its ends); a <see cref="Bbs757Annunciators.Quiet"/> phrase is a state that is tracked but never spoken (a
/// blank autoland status, an alert going out), so the change out of it is still heard.
/// </para>
/// A <paramref name="Silent"/> row is only read for another row's phrase: it speaks nothing and has no Ctrl+M row.
/// </summary>
public sealed record Bbs757AnnunciatorRow(string Key, string LVar, string Label, Func<double, double?, string?> Phrase,
    string? Panel, IReadOnlyDictionary<double, string>? States = null, string? RelatedKey = null, bool Silent = false);

/// <summary>
/// The 757's announced lights and states (plan 06): BlueBird's published L:vars, each spoken on change
/// through the definition's <see cref="Bbs757ChangeTracker"/> (the cockpit as found is a silent baseline):
/// EXT PWR, the autoland status annunciator, the altitude alert, the configuration warning, each IRS aligned,
/// the engine start valves and the doors; and the words of plan 14's squawk, TCAS mode and altimeter
/// watches. Pure: the table and the words.
/// </summary>
public static class Bbs757Annunciators
{
    /// <summary>The EXT PWR button's map node, whose press reads back <see cref="Bbs757SharedKeys.ExtPwrOn"/>.</summary>
    public const string ExtPwrButtonNode = "B757_OVHD_EXT_PWR_BUTTON";

    /// <summary>The map's panel with the EXT PWR button.</summary>
    public const string ElectricalPanel = "Electrical";

    /// <summary>
    /// The prefix of a phrase that is a tracked state but never spoken (<see cref="IsSpoken"/>). The change
    /// tracker speaks a change from a remembered phrase, so a state the pilot need not hear still needs one:
    /// without it, the first spoken state after it would be taken for a baseline and lost.
    /// </summary>
    public const string Quiet = "quiet: ";

    /// <summary>The IRS mode selector's NAV position (OFF 0, ALIGN 1, NAV 2, ATT 3).</summary>
    private const double IrsNav = 2;

    public static IReadOnlyList<Bbs757AnnunciatorRow> Rows { get; } = BuildRows();

    private static readonly Dictionary<string, Bbs757AnnunciatorRow> ByKey = Rows.ToDictionary(r => r.Key, StringComparer.Ordinal);

    /// <summary>The row read under this variable key, or null.</summary>
    public static Bbs757AnnunciatorRow? ForKey(string key) => ByKey.GetValueOrDefault(key);

    /// <summary>Whether a tracked phrase is said aloud (not a <see cref="Quiet"/> state).</summary>
    public static bool IsSpoken(string phrase) => !phrase.StartsWith(Quiet, StringComparison.Ordinal);

    /// <summary>The EXT PWR read-back: "EXT PWR on", "EXT PWR off", or "EXT PWR unavailable" when it was not read.</summary>
    public static string ExtPwrPhrase(double? value)
        => value is double v && ByKey[Bbs757SharedKeys.ExtPwrOn].Phrase(v, null) is { } phrase ? phrase : "EXT PWR unavailable";

    // ---- The squawk, TCAS mode and altimeter watches (plan 14) ------------------------------------
    // Spoken on a change from any source; not annunciator rows (two are stock variables, and the
    // altimeter speaks once it rests), so the definition registers and speaks them itself.

    /// <summary>
    /// The squawk watch: the squawk field's own read-back row (stock <c>TRANSPONDER CODE:1</c>, BCO16), made
    /// announced on the 1 Hz batch rather than a second row: two continuous rows of one name would shift
    /// every later batch slot ([VAR-7]).
    /// </summary>
    public const string SquawkWatchKey = Bbs757Squawk.CodeKey;

    /// <summary>The TCAS mode watch: <c>L:B757_PED_TCAS_MODE_KNOB</c> (STBY 0 to TA/RA 4) on the 1 Hz batch.</summary>
    public const string TcasModeWatchKey = "BBS757_TCAS_MODE_WATCH";

    /// <summary>The altimeter watch: stock <c>KOHLSMAN SETTING MB:1</c> on the 1 Hz batch, spoken once it rests.</summary>
    public const string AltimeterWatchKey = "BBS757_ALTIMETER_WATCH";

    /// <summary>The TCAS mode knob's map node, whose pick moves <see cref="TcasModeWatchKey"/>.</summary>
    public const string TcasModeNode = "B757_PED_TCAS_MODE_KNOB";

    private static readonly string[] TcasModes = { "STBY", "ALT RPTG OFF", "XPNDR", "TA ONLY", "TA/RA" };

    /// <summary>"Squawk 4743", the squawk field's own read-back words; null when the code is not a number.</summary>
    public static string? SquawkPhrase(double bco16)
        => double.IsFinite(bco16) && bco16 >= 0 ? $"Squawk {Bbs757Squawk.Decode(bco16)}" : null;

    /// <summary>"TCAS STBY", "TCAS ALT RPTG OFF", "TCAS XPNDR", "TCAS TA ONLY", "TCAS TA/RA"; null off the knob's five positions.</summary>
    public static string? TcasModePhrase(double value)
    {
        if (!double.IsFinite(value)) return null;
        double i = Math.Round(value);
        return Math.Abs(value - i) < 0.01 && i >= 0 && i < TcasModes.Length ? $"TCAS {TcasModes[(int)i]}" : null;
    }

    /// <summary>
    /// The captain's altimeter in the B key's words ("Altimeter: 1020, 30.12", "Altimeter standard"); null
    /// when it is not a pressure (unpowered reads 0).
    /// </summary>
    public static string? AltimeterPhrase(double millibars)
        => double.IsFinite(millibars) && millibars > 0
            ? Aircraft.AltimeterPhrase.Sentence(millibars / Aircraft.AltimeterPhrase.HpaPerInHg)
            : null;

    private static List<Bbs757AnnunciatorRow> BuildRows()
    {
        var rows = new List<Bbs757AnnunciatorRow>
        {
            // The press's read-back and the row's change say the same words: the read-back rebaselines the row
            // with LinkedPhrase, so a different phrase would announce the press a second time.
            new(Bbs757SharedKeys.ExtPwrOn, "B757_OVHD_EXT_PWR_BUTTON_ON", "External Power On",
                (v, _) => OnOff(v, "EXT PWR on", "EXT PWR off"), ElectricalPanel, States("Off", "On")),
            new(Bbs757SharedKeys.ExtPwrAvail, "B757_OVHD_APUGENEXTPWR_R_AVAIL_DECAL", "External Power Available",
                (v, _) => OnOff(v, "External power available", "External power not available"), ElectricalPanel,
                States("Not available", "Available")),

            // The autoland status annunciator's two lines. Measured on an ILS autoland on 2026-10-27: both pairs read
            // 1 with APP armed and the upper went to 0 at LAND 3 (docs/b757.md, "Announced lights and states"). NOT
            // measured: the value 2 and what they read cold and dark and unpowered: these L:vars drive the
            // annunciator's animation, and if one rests at 0 the Status display would read LAND 3 / NO LAND 3 with
            // nothing armed.
            new("BBS757_ASA_UPPER", "B757_MAIN_L_AUTOLAND_1_BUTTON_ON", "Autoland Status Upper",
                (v, _) => Asa(v, "LAND 3", "LAND 2", "upper blank"), BBS757Definition.StatusSection,
                new Dictionary<double, string> { [0] = "LAND 3", [1] = "Blank", [2] = "LAND 2" }),
            new("BBS757_ASA_LOWER", "B757_MAIN_L_AUTOLAND_2_BUTTON_ON", "Autoland Status Lower",
                (v, _) => Asa(v, "NO LAND 3", "NO AUTOLAND", "lower blank"), BBS757Definition.StatusSection,
                new Dictionary<double, string> { [0] = "NO LAND 3", [1] = "Blank", [2] = "NO AUTOLAND" }),

            new("BBS757_ALT_ALERT", "BBS757_CA_ALTIMETER_ALT_ALERT_LIGHT", "Altitude Alert",
                (v, _) => OnOff(v, "Altitude alert", Quiet + "altitude alert out"), null),
            new("BBS757_CONFIG_WARNING", "B757_MAIN_CONFIG_1_DECAL", "Configuration Warning",
                (v, _) => OnOff(v, "Configuration warning", Quiet + "configuration warning out"), null),
        };

        // Each IRS: the ALIGN light going out says "aligned" only with that IRS at NAV. A switch turned off also
        // puts the light out, so the switch's last value rides along (a silent row of its own). The batch
        // delivers in name order, so B757_IRS_x_SWITCH arrives before B757_OVHD_IRS_x_ALIGN_DECAL.
        foreach (string irs in new[] { "L", "C", "R" })
        {
            string watch = $"BBS757_IRS_{irs}_SWITCH_WATCH";
            rows.Add(new($"BBS757_IRS_{irs}_ALIGN", $"B757_OVHD_IRS_{irs}_ALIGN_DECAL", $"IRS {irs} Aligned",
                (v, sw) => !double.IsFinite(v) ? null
                    : v > 0.5 ? Quiet + $"IRS {irs} aligning"
                    : sw == IrsNav ? $"IRS {irs} aligned"
                    : Quiet + $"IRS {irs} align light out",
                null, RelatedKey: watch));
            rows.Add(new(watch, $"B757_IRS_{irs}_SWITCH", $"IRS {irs} Switch", (_, _) => null, null, Silent: true));
        }

        // Each start valve follows its start switch, not a light: the switch is held at GND while the valve is
        // open and BlueBird releases it to AUTO at starter cutout, about 46 % N2. The VALVE decal only flashes
        // for 2 s whenever the switch moves, either way (measured 2026-10-27, both engines, three starts).
        // "Open" is tracked but quiet (the pilot just picked GND and heard it); "closed" is the cutout.
        rows.Add(new("BBS757_L_START_VALVE", "B757_OVHD_L_ENG_START_SWITCH", "Left Start Valve",
            (v, _) => StartValve(v, "Left"), BBS757Definition.EnginesPanel, StartValveStates));
        rows.Add(new("BBS757_R_START_VALVE", "B757_OVHD_R_ENG_START_SWITCH", "Right Start Valve",
            (v, _) => StartValve(v, "Right"), BBS757Definition.EnginesPanel, StartValveStates));

        // The doors animate 0-100 (passenger) or 0-200 (cargo): only the ends speak.
        foreach (string door in new[] { "L1", "L2", "L3", "R1", "R2", "R3" })
            rows.Add(new($"BBS757_DOOR_{door}", $"B757_DOORS_PAX_{door}", $"Door {door}",
                (v, _) => Door(v, $"Door {door}", open: 90, closed: 5), null));
        rows.Add(new("BBS757_DOOR_CARGO_FWD", "B757_DOORS_CARGO_FWD", "Forward Cargo Door",
            (v, _) => Door(v, "Forward cargo door", open: 180, closed: 10), null));
        rows.Add(new("BBS757_DOOR_CARGO_AFT", "B757_DOORS_CARGO_AFT", "Aft Cargo Door",
            (v, _) => Door(v, "Aft cargo door", open: 180, closed: 10), null));
        return rows;
    }

    /// <summary>A light's words: above 0.5 is lit; null when the value is not a number.</summary>
    private static string? OnOff(double value, string lit, string dark)
        => double.IsFinite(value) ? (value > 0.5 ? lit : dark) : null;

    /// <summary>The start switch's GND position, the only one that holds the start valve open (AUTO 3, OFF 4, CONT 5, FLT 6).</summary>
    private const double StartSwitchGnd = 1;

    // A property, not a field: Rows is built before any field declared below it is initialized.
    private static IReadOnlyDictionary<double, string> StartValveStates => new Dictionary<double, string>
    {
        [StartSwitchGnd] = "Open", [3] = "Closed", [4] = "Closed", [5] = "Closed", [6] = "Closed",
    };

    /// <summary>A start valve from its switch: open (quiet) at GND, closed anywhere else; null when not a number.</summary>
    private static string? StartValve(double value, string side)
        => !double.IsFinite(value) ? null
            : Math.Abs(value - StartSwitchGnd) < 0.01 ? Quiet + $"{side} start valve open"
            : $"{side} start valve closed";

    /// <summary>An autoland status line: 0 and 2 are its two messages, 1 blank (tracked, never spoken).</summary>
    private static string? Asa(double value, string at0, string at2, string blank)
        => !double.IsFinite(value) ? null : Math.Round(value) switch
        {
            0 => at0,
            2 => at2,
            _ => Quiet + blank,
        };

    /// <summary>A door at either end; null between them, so a door moving says only where it stops.</summary>
    private static string? Door(double value, string name, double open, double closed)
        => !double.IsFinite(value) ? null : value >= open ? $"{name} open" : value <= closed ? $"{name} closed" : null;

    private static IReadOnlyDictionary<double, string> States(string dark, string lit)
        => new Dictionary<double, string> { [0] = dark, [1] = lit };
}
