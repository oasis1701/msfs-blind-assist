using System.Globalization;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11's COM radios, read from and tuned through the simulator's own COM variables and
/// events. TFDi's radio panel drives the stock variables (COM1 read 135.500 on a live aircraft,
/// 2026-09-06, through the calculator path — the plain data-definition read the probing tool
/// used returned 0 for the same variable, so a zero from that tool proves nothing here), and the
/// aircraft honours the stock tuning events: <c>COM3_STBY_RADIO_SET_HZ</c> set COM3's standby and
/// the value held for 40 s, <c>COM3_RADIO_SWAP</c> swapped it into the active slot (probed live
/// the same day). Wording mirrors the FBW and HS787 definitions: "COM 1 active 135.500",
/// standby included so a tuner click and an XFER both read back.
/// </summary>
public static class Md11Radios
{
    /// <summary>The VHF airband; anything outside it is an unpowered or garbage value and stays silent.</summary>
    public const double AirbandLowKhz = 118000;
    public const double AirbandHighKhz = 137000;

    /// <summary>What a pilot may type into a standby field, in MHz (the sim's own COM range).</summary>
    public const double TuneLowMhz = 118.000;
    public const double TuneHighMhz = 136.975;
    public const string InvalidFrequencyMessage = "Invalid frequency. Range: 118.000 to 136.975.";

    /// <summary>Definition keys, active then standby per radio — the read-only rows.</summary>
    public static readonly string[] Keys =
    {
        "COM_ACTIVE_FREQUENCY:1", "COM_STANDBY_FREQUENCY:1",
        "COM_ACTIVE_FREQUENCY:2", "COM_STANDBY_FREQUENCY:2",
        "COM_ACTIVE_FREQUENCY:3", "COM_STANDBY_FREQUENCY:3",
    };

    /// <summary>
    /// The Radios panel's CONTROL rows, radio by radio: the typed standby entry (MainForm's
    /// "_SET" text box + Set button) and the transfer button. The active/standby read-backs
    /// (<see cref="Keys"/>) are the panel's Status Display rows, not tab stops.
    /// </summary>
    public static readonly string[] PanelKeys =
    {
        StandbySetKey(1), SwapKey(1),
        StandbySetKey(2), SwapKey(2),
        StandbySetKey(3), SwapKey(3),
    };

    public static string StandbySetKey(int idx) => $"COM_STANDBY_FREQUENCY_SET:{idx}";
    public static string SwapKey(int idx) => $"COM{idx}_RADIO_SWAP";

    public static bool IsComKey(string key)
        => key.StartsWith("COM_ACTIVE_FREQUENCY:", StringComparison.Ordinal)
        || key.StartsWith("COM_STANDBY_FREQUENCY:", StringComparison.Ordinal);

    public static bool IsStandbySetKey(string key) => key.StartsWith("COM_STANDBY_FREQUENCY_SET:", StringComparison.Ordinal);

    public static bool IsSwapKey(string key)
        => key is "COM1_RADIO_SWAP" or "COM2_RADIO_SWAP" or "COM3_RADIO_SWAP";

    /// <summary>1, 2 or 3 — the radio a key names (":n" suffix, or the digit in "COMn_RADIO_SWAP").</summary>
    public static int RadioIndex(string key)
        => key.EndsWith(":2", StringComparison.Ordinal) || key.StartsWith("COM2_", StringComparison.Ordinal) ? 2
         : key.EndsWith(":3", StringComparison.Ordinal) || key.StartsWith("COM3_", StringComparison.Ordinal) ? 3 : 1;

    /// <summary>
    /// The stock standby-set event. COM1's is the un-numbered "COM_STBY_RADIO_SET_HZ"; COM2/COM3
    /// use the numbered form, so a COM2 set never writes COM1's standby.
    /// </summary>
    public static string StandbySetEvent(int idx) => idx == 1 ? "COM_STBY_RADIO_SET_HZ" : $"COM{idx}_STBY_RADIO_SET_HZ";

    /// <summary>The stock swap event: standby into active, active into standby.</summary>
    public static string SwapEvent(int idx) => $"COM{idx}_RADIO_SWAP";

    /// <summary>The stock SimVar name behind a key: "COM_ACTIVE_FREQUENCY:1" → "COM ACTIVE FREQUENCY:1".</summary>
    public static string SimVarName(string key) => key.Replace('_', ' ');

    public static string Radio(string key) => $"COM {RadioIndex(key)}";

    public static string Kind(string key)
        => key.Contains("ACTIVE", StringComparison.Ordinal) ? "active" : "standby";

    /// <summary>"COM 1 Active Frequency" — the panel row and Ctrl+M label.</summary>
    public static string DisplayName(string key)
        => $"{Radio(key)} {(Kind(key) == "active" ? "Active" : "Standby")} Frequency";

    public static bool InAirband(double khz) => khz >= AirbandLowKhz && khz <= AirbandHighKhz;

    /// <summary>kHz → "135.500", invariant culture (spoken, so never a comma decimal).</summary>
    public static string FormatMhz(double khz) => (khz / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>
    /// The tuning read-back's sentence: null when the DELIVERED frequency is the target (within
    /// half a kHz of float noise) or when nothing was delivered — a verdict rests on the radio's
    /// report, never on a sleep that guessed when the batch would land — else the failure, with
    /// what the radio shows when that is an airband frequency ("…, still 124.850.").
    /// </summary>
    public static string? TuneReadBack(double targetKhz, double? deliveredKhz, string failure)
    {
        if (deliveredKhz is not double khz) return null;
        if (Math.Abs(khz - targetKhz) <= 0.5) return null;
        string still = InAirband(khz) ? $", still {FormatMhz(khz)}" : "";
        return $"{failure}{still}.";
    }

    /// <summary>The read-out row's value: the frequency, or dashes while the radio reads nothing sensible.</summary>
    public static string Display(double khz) => InAirband(khz) ? FormatMhz(khz) : "--";

    public static string Describe(string key, double khz) => $"{Radio(key)} {Kind(key)} {FormatMhz(khz)}";

    /// <summary>
    /// What the pilot typed into a standby field, in MHz (MainForm parses the box invariant, so
    /// "124,9" and "124.9" both arrive as 124.9), to the Hz the stock set event wants. Rounded to
    /// the kHz so 124.9 becomes exactly 124,900,000 rather than a float's idea of it.
    /// </summary>
    public static bool TryParseMhz(double mhz, out uint hz, out string error)
    {
        hz = 0; error = "";
        if (double.IsNaN(mhz) || mhz < TuneLowMhz || mhz > TuneHighMhz) { error = InvalidFrequencyMessage; return false; }
        hz = (uint)(Math.Round(mhz * 1000.0) * 1000.0);
        return true;
    }
}

/// <summary>
/// Baseline-first change detector for the COM keys: the first sample of each key seeds silently
/// (connecting must not read the whole radio stack aloud), and a later change of more than half
/// a kHz inside the airband is spoken. A value OUTSIDE the airband (an unpowered radio's 0) is
/// ignored outright — never spoken and never stored as the baseline: stored, it made the
/// power-up that follows a "change", and all six keys were read aloud every time the radios
/// came back. Reset on a context reset (a disconnect or a flight load) so the next delivery
/// re-seeds (never on the reconnect itself: that first delivery has already landed by then).
/// </summary>
public sealed class Md11ComAnnouncer
{
    private readonly Dictionary<string, double> _last = new(StringComparer.Ordinal);

    /// <summary>The sentence to speak for this update, or null when nothing should be said.</summary>
    public string? OnUpdate(string key, double khz)
    {
        if (!Md11Radios.InAirband(khz)) return null;   // BEFORE the store: an out-of-band value is never a baseline
        bool seeded = _last.TryGetValue(key, out double prev);
        _last[key] = khz;
        if (!seeded) return null;
        if (Math.Abs(khz - prev) <= 0.5) return null;
        return Md11Radios.Describe(key, khz);
    }

    /// <summary>The last airband value seen for a key, if any — what a tuning read-back compares against.</summary>
    public double? Last(string key) => _last.TryGetValue(key, out var v) ? v : null;

    /// <summary>
    /// Seeds a key that has no baseline (a flight load re-delivers only what changed); true when it
    /// did. An out-of-band value seeds nothing, by <see cref="OnUpdate"/>'s rule: a cold-and-dark
    /// load's cached 0, seeded here, made the power-up a change on all six keys.
    /// </summary>
    public bool SeedIfEmpty(string key, double khz)
    {
        if (!Md11Radios.InAirband(khz) || _last.ContainsKey(key)) return false;
        _last[key] = khz;
        return true;
    }

    public void Reset() => _last.Clear();
}
