using System.Globalization;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11 transponder's code entry, done the way the aircraft does it: the panel's own digit
/// keys pressed in order over CEVENT (the only supported way in), then the simulator's
/// <c>TRANSPONDER CODE:1</c> read back to confirm — TFDi drives that stock variable (it read
/// 0x5473, squawk 5473, on a live aircraft on 2026-09-06).
///
/// On the panel a typed four-digit code replaces the eight digit buttons: one field with a Set
/// button is one control and one spoken confirmation, where the buttons were four presses with no
/// read-back and no way to hear what had been entered so far. The digit buttons stay registered
/// (the entry presses them); they are only no longer listed.
/// </summary>
public static class Md11Squawk
{
    /// <summary>The panel's text field. A key containing "_SET" is what MainForm renders as a text box plus Set button.</summary>
    public const string SetKey = "MD11_SQUAWK_SET";

    /// <summary>The read-back row: the stock <c>TRANSPONDER CODE:1</c>, BCO16, decoded to four digits.</summary>
    public const string CodeKey = "MD11_SQUAWK";

    /// <summary>Guidance spoken when the entry is refused; the field stays untouched.</summary>
    public const string InvalidMessage = "Squawk must be four digits, each 0 to 7.";
    public const string EmptyMessage = "Type a four-digit squawk first.";

    /// <summary>The eight keypad buttons, digit 0 to 7, as the control map names them.</summary>
    public static readonly string[] DigitButtons =
        Enumerable.Range(0, 8).Select(d => DigitButton((char)('0' + d))).ToArray();

    public static string DigitButton(char digit) => $"MD11_PED_XPNDR_{digit}_BT";

    /// <summary>
    /// Parses what the pilot typed. MainForm hands the box's text over as a double, so "0421"
    /// arrives as 421 and is padded back to four digits; an empty or unparseable box arrives as
    /// 0, which is refused rather than sent as 0000 (a code nobody types by accident is worth a
    /// refusal, a blanked box set to 0000 is not).
    /// </summary>
    public static bool TryParse(double typed, out string code, out string error)
    {
        code = ""; error = "";
        if (double.IsNaN(typed) || typed <= 0) { error = EmptyMessage; return false; }
        if (typed > 7777 || Math.Abs(typed - Math.Round(typed)) > 1e-9) { error = InvalidMessage; return false; }
        var digits = ((int)Math.Round(typed)).ToString("0000", CultureInfo.InvariantCulture);
        if (digits.Any(ch => ch > '7')) { error = InvalidMessage; return false; }
        code = digits;
        return true;
    }

    /// <summary><c>TRANSPONDER CODE:1</c> in BCO16 is one octal digit per hex nibble: 0x5473 → "5473".</summary>
    public static string Decode(double bco16)
    {
        int w = (int)Math.Round(bco16);
        return $"{(w >> 12) & 0xF}{(w >> 8) & 0xF}{(w >> 4) & 0xF}{w & 0xF}";
    }

    /// <summary>The control's spoken name — the confirmations and its undeliverable refusal share it.</summary>
    public const string Name = "Squawk";

    /// <summary>What the confirmation says once the aircraft has been read back.</summary>
    public static string Confirmation(string requested, string? readBack)
    {
        if (readBack == null) return $"{Name} {requested} entered, the transponder did not report back.";
        if (readBack == requested) return $"{Name} {requested}.";
        return $"{Name} entry did not take, the transponder reads {readBack}.";
    }
}

/// <summary>
/// Speaks the squawk when it CHANGES, whichever way it was set — the panel field, a hardware
/// transponder, the sim's own keys, an ATC assignment — the way the A380 and the PMDGs do it
/// (<c>TRANSPONDER CODE:1</c> rides the 1 Hz batch). Pure: the definition feeds it deliveries on
/// the UI thread and does the speaking.
///
/// Baseline-first: the first code after connecting is remembered, never spoken. An unchanged
/// redelivery (the status display's per-second force-read while the Transponder panel is open)
/// is silent. While a typed entry is in progress (<see cref="EntryInProgress"/>) every delivery
/// is tracked but none is spoken: the entry presses four keypad digits, so the transponder may
/// show intermediate codes as they land, and its own confirmation ("Squawk 1200.") is the
/// sentence — the final code must not be spoken a second time after it. Entries are COUNTED,
/// not flagged: a second Set pressed inside the first entry's window must not have the first
/// entry's end unmute the second one mid-flight, and a reconnect's <see cref="Reset"/> leaves a
/// running entry's silence in place — the entry ends it itself.
/// </summary>
public sealed class Md11SquawkAnnouncer
{
    private int _last = -1;
    private int _entries;

    /// <summary>True while at least one typed entry is running; deliveries are tracked, not spoken.</summary>
    public bool EntryInProgress => _entries > 0;

    /// <summary>A typed entry started (UI thread).</summary>
    public void BeginEntry() => _entries++;

    /// <summary>A typed entry ended, however it ended (UI thread, posted from the entry's finally).</summary>
    public void EndEntry()
    {
        if (_entries > 0) _entries--;
    }

    /// <summary>Seeds the baseline when there is none (a flight load re-delivers only what changed); true when it did.</summary>
    public bool SeedIfEmpty(double bco16)
    {
        if (_last >= 0) return false;
        _last = (int)Math.Round(bco16);
        return true;
    }

    /// <summary>A code arrived: "Squawk 1234" when it is a change worth speaking, else null.</summary>
    public string? OnUpdate(double bco16)
    {
        int bcd = (int)Math.Round(bco16);
        bool first = _last < 0;
        bool changed = bcd != _last;
        _last = bcd;
        if (first || !changed || EntryInProgress) return null;
        return $"Squawk {Md11Squawk.Decode(bcd)}";
    }

    /// <summary>
    /// The next code is a baseline again. Called on the DISCONNECT (OnSimContextReset), never on
    /// the reconnect — there the first batch has already re-fired the code, so a reset would eat
    /// the next real change; an aircraft switch builds a fresh definition. A running entry keeps
    /// its silence until it ends.
    /// </summary>
    public void Reset() => _last = -1;
}
