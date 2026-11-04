using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Decides what to say about the captain's ND when a message comes or goes, or the active waypoint changes. The announcer
/// host's sampler hands it each tick's snapshot of the ND and it answers with the line to speak, or null. It speaks
/// nothing itself and reads no clock.
/// </summary>
/// <remarks>
/// <para>Which messages: exactly the ones the ND window lists (<see cref="FenixChannelNdRows.DrawnMessages"/>, the one selection), compared
/// by their text without the format letters, except "RANGE CHANGE" and "MODE CHANGE", which the ND holds for about half a
/// second at every EFIS mode or range change, the pilot's own, which the screen reader already reads ([CORE-7]).</para>
/// <para>Which waypoint: a change of the ident in line 1 of the next-waypoint box, only while the ND draws that box (ARC,
/// ROSE NAV and PLAN, and line 1 PPOS or a waypoint). The waypoint is said as drawn, "ND: 1500, 2.7 NM", PPOS as "ND: no
/// active waypoint". A waypoint seen when the box becomes drawn again is only remembered.</para>
/// <para>The baseline: the first good reading after the start, a new connection or a gap (the ND not trusted, or its read
/// with a problem) is only remembered; the sample that shows the gap or the new connection is not itself the baseline.
/// Muted, it keeps tracking and says nothing, so unmuting replays nothing.</para>
/// <para>One line per sample: new messages ("ND message: A, B"), gone ones ("ND message gone: C") and the waypoint
/// ("ND: ..."), joined with ". " in that order. Text is channel text spoken by <see cref="FenixChannelNdSpeech"/> (only the colors that tell something); the
/// distance is the ND's own digits, so the PC's culture changes none of it. UI thread only.</para>
/// </remarks>
internal sealed class FenixChannelNdMessages
{
    private const string NoWaypoint = "no active waypoint";

    private static readonly string[] NotAnnounced = ["RANGE CHANGE", "MODE CHANGE"];

    private static readonly Regex DistanceForm = new(@"^([0-9]+(\.[0-9]+)?)\s*NM$", RegexOptions.CultureInvariant);

    private int? _lastConnection;
    private bool _baselined;

    // The messages on the ND at the last sample: stripped text, and the coded text to speak it from.
    private Dictionary<string, Drawn> _messages = new(StringComparer.Ordinal);

    // A message as drawn: its coded text and the usual colors of its field.
    private readonly record struct Drawn(string Coded, IReadOnlySet<FenixTextColor>? Usual);

    private static string? Speak(Drawn drawn) => FenixChannelNdSpeech.Line(drawn.Coded, drawn.Usual);

    // The ident of the drawn waypoint at the last sample; null while the box is not drawn.
    private string? _ident;

    /// <summary>
    /// One reading of the ND. Returns the line to speak, or null (see the class remarks).
    /// </summary>
    public string? Sample(FenixChannelSnapshot s, bool muted)
    {
        bool reconnected = _lastConnection is { } last && last != s.Connection;
        _lastConnection = s.Connection;

        FenixNdData? read = s.Verdict.Trusted && FenixChannelNd.Read(s) is { IsOk: true } section ? section.Value : null;
        if (read is not { } nd || reconnected)
        {
            _baselined = false;
            return null;
        }

        Dictionary<string, Drawn> messages = MessagesOf(nd);
        (string Ident, string Spoken)? waypoint = WaypointOf(nd);

        if (!_baselined)
        {
            _baselined = true;
            Remember(messages, waypoint);
            return null;
        }

        var parts = new List<string>();

        string[] added = [.. messages.Where(m => !_messages.ContainsKey(m.Key))
            .Select(m => Speak(m.Value)).OfType<string>()];
        if (added.Length > 0) parts.Add("ND message: " + string.Join(", ", added));

        string[] gone = [.. _messages.Where(m => !messages.ContainsKey(m.Key))
            .Select(m => Speak(m.Value)).OfType<string>()];
        if (gone.Length > 0) parts.Add("ND message gone: " + string.Join(", ", gone));

        // A waypoint is news only when the box was drawn at the last sample too and its ident moved.
        if (waypoint is { } now && _ident is not null && now.Ident != _ident) parts.Add("ND: " + now.Spoken);

        Remember(messages, waypoint);
        return muted || parts.Count == 0 ? null : string.Join(". ", parts);
    }

    private void Remember(Dictionary<string, Drawn> messages, (string Ident, string Spoken)? waypoint)
    {
        _messages = messages;
        _ident = waypoint?.Ident;
    }

    // The drawn messages by stripped text (the first of equal ones), less the two the pilot's own knob turn brings.
    private static Dictionary<string, Drawn> MessagesOf(FenixNdData nd)
    {
        var messages = new Dictionary<string, Drawn>(StringComparer.Ordinal);
        foreach ((string coded, IReadOnlySet<FenixTextColor>? usual) in FenixChannelNdRows.DrawnMessages(nd))
        {
            string plain = Plain(coded);
            if (plain.Length == 0 || NotAnnounced.Contains(plain, StringComparer.OrdinalIgnoreCase)) continue;

            messages.TryAdd(plain, new Drawn(coded, usual));
        }

        return messages;
    }

    // The ident and the sentence of the waypoint box while the ND draws it; null otherwise.
    private static (string Ident, string Spoken)? WaypointOf(FenixNdData nd)
    {
        if (nd.Mode is not ("ARC" or "NAV" or "PLAN") || nd.Waypoint is not { } lines) return null;
        if (!FenixChannelNdWaypoint.IsShown(lines)) return null;

        string one = Plain(lines.Line1);
        if (one == "PPOS") return ("PPOS", NoWaypoint);

        string ident = one.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        Match distance = DistanceForm.Match(Plain(lines.Line2));
        return (ident, distance.Success ? $"{ident}, {distance.Groups[1].Value} NM" : ident);
    }

    private static string Plain(string coded) => FenixChannelText.Parse(coded).JoinedText.Trim();
}
