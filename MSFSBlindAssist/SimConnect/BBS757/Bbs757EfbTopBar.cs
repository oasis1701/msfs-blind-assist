using System.Text.RegularExpressions;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The tablet's top bar (<see cref="Bbs757EfbScreenModel.TopBar"/>, the window's "Status bar"). From memory (plan 21) it is the top
/// bar app's visible labels, each keyed by its control name (<see cref="KeyFor"/>): the time line, and the departure label when it shows
/// (<see cref="Bbs757EfbModelReader"/>). Plan 18's keys and words for the departure countdown, the boarding control and the
/// notification control (<see cref="DepartureKey"/>, <see cref="BoardingKey"/>, <see cref="NotificationsKey"/>,
/// <see cref="DepartureText"/>, <see cref="BoardingText"/>) are kept for when they are measured in memory. The departure countdown is now
/// measured (2026-11-07): the top bar app keeps "DEP 00:28" as a string of its own (the layout's <c>OverlayFlightTimeText</c>), no label,
/// and the model's status bar reads it after the time line under <see cref="DepartureKey"/> (other words, "ETA 1234Z", as written under
/// <see cref="FlightTimeKey"/>). The boarding box is measured too (2026-11-07): the top bar app draws it from its own fields, not as a
/// control, and the model reads it after the countdown under <see cref="BoardingKey"/>, in the words BlueBird draws
/// (<see cref="BoardingWords"/>): a button while it offers to start, text while a loading runs or is paused. The notification button is
/// still not fed (BlueBird's notifications are read from their records). The keys stay the same whatever the words, so the window keeps the
/// screen reader's focus on an item while it changes. Pure.
/// </summary>
public static class Bbs757EfbTopBar
{
    public const string DepartureKey = "top:dep", BoardingKey = "top:boarding", NotificationsKey = "top:notifications";

    /// <summary>The key of the flight-time text when it is no departure countdown ("ETA 1234Z"): shown as written.</summary>
    public const string FlightTimeKey = "top:flight_time";

    /// <summary>The key of the top bar label named <paramref name="controlName"/> (<c>time_label</c> is <c>top:time_label</c>): stable
    /// while its words change (the clock ticks).</summary>
    internal static string KeyFor(string controlName) => "top:" + controlName;

    /// <summary>A minus read as the minus sign (U+2212) or an en dash (U+2013), and the tablet's ellipsis (U+2026).</summary>
    private const char MinusSign = (char)0x2212, EnDash = (char)0x2013, Ellipsis = (char)0x2026;

    private static readonly Regex Departure = new(@"^\s*DEP\s*([+\-" + MinusSign + EnDash + @"]?)\s*(\d{1,2}:\d{2})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The departure countdown as the tablet writes it, "DEP -00:46", from a read of it ("DEP-00:46"; a minus
    /// read as a dash or U+2212); null when the read is no countdown.</summary>
    internal static string? DepartureText(string read)
    {
        var m = Departure.Match(read ?? "");
        if (!m.Success) return null;
        string sign = m.Groups[1].Value switch { "" => "", "+" => "+", _ => "-" };
        return "DEP " + sign + m.Groups[2].Value;
    }

    /// <summary>
    /// The words BlueBird draws in its boarding box, as its top bar draw chooses them (decoded from BBS752.wasm, 2026-11-07): a running or
    /// paused deboarding "DEBOARDING..." or "DEBOARDING PAUSED"; a running boarding "BOARDING... N MIN", N the largest remaining time rounded
    /// up to whole minutes, or "BOARDING..." with none left; a paused one "BOARDING PAUSED"; otherwise the offer, "START DEBOARDING" once the
    /// aircraft has flown since its boarding and "START BOARDING" before. <see cref="BoardingText"/> makes them the window's item.
    /// </summary>
    internal static string BoardingWords(Bbs757EfbMemory.BoardingBox box)
    {
        bool running = box.State is 1 or 2 or 3;
        if (box.Deboarding && running) return box.State == 3 ? "DEBOARDING PAUSED" : "DEBOARDING...";
        if (box.State is 1 or 2)
            return box.RemainingSeconds > 0
                ? "BOARDING... " + ((box.RemainingSeconds + 59) / 60).ToString(System.Globalization.CultureInfo.InvariantCulture) + " MIN"
                : "BOARDING...";
        if (box.State == 3) return "BOARDING PAUSED";
        return box.Flown ? "START DEBOARDING" : "START BOARDING";
    }

    private static readonly Regex Minutes = new(@"^(\d+)\s*MIN$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// What the boarding box says, from a read of it: a button while it offers to start ("» START BOARDING" is "Start
    /// boarding"), text while boarding runs ("» BOARDING… 10 MIN" is "Boarding… 10 min"), any other words as read, as
    /// text. The box's icon, read as a glyph or two ("3)", "30"), is no word of its own, and a misread of START or
    /// BOARDING of their own length ("B0ARDIHG") is the word. Null when no words are left.
    /// </summary>
    internal static (bool IsButton, string Text)? BoardingText(string read)
    {
        var words = (read ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 0 && words[0].Length <= 2 && (words.Count > 1 || !words[0].Any(char.IsLetter))) words.RemoveAt(0);
        if (words.Count == 0) return null;
        for (int i = 0; i < words.Count; i++)
        {
            string bare = words[i].TrimEnd('.', Ellipsis);
            string up = bare.ToUpperInvariant();
            if (bare.Length == 8 && Bbs757EfbVocabulary.Distance(up, "BOARDING") <= 2) words[i] = "BOARDING" + words[i][bare.Length..];
            else if (bare.Length == 5 && Bbs757EfbVocabulary.Distance(up, "START") <= 1) words[i] = "START" + words[i][bare.Length..];
        }
        if (words[0] == "START") return (true, Bbs757EfbVocabulary.Pretty(string.Join(" ", words).ToUpperInvariant()));
        var minutes = Minutes.Match(string.Join(" ", words.Skip(1)));
        if (words[0].TrimEnd('.', Ellipsis) == "BOARDING" && minutes.Success)
            return (false, "Boarding" + Ellipsis + " " + minutes.Groups[1].Value + " min");
        return (false, Bbs757EfbVocabulary.Pretty(string.Join(" ", words)));
    }
}
