using System.Text.RegularExpressions;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Ctrl+W: the ND's next-waypoint box read aloud, in the ND's order and with no colors: the ident, "<bearing>
/// degrees", "<distance> nautical miles" and the time, joined by ", ". The figures are spoken as the ND draws them,
/// never re-formatted, so a culture's decimal separator never reaches the sentence.
/// </summary>
internal static class FenixChannelNdWaypoint
{
    /// <summary>What the box says with no flight plan: line 1 reads PPOS.</summary>
    public const string NoActiveWaypoint = "No active waypoint";

    private const string Ppos = "PPOS";

    // Line 1: the ident, then an optional bearing of up to three digits, with the degree sign the gate's INTCPT line draws.
    private static readonly Regex Line1Form = new(@"^(\S+)(\s+([0-9]{1,3})°?)?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex Line2Form = new(@"^([0-9]+(\.[0-9]+)?)\s*NM$", RegexOptions.CultureInvariant);

    private static readonly Regex Line3Form = new(@"^([0-9]{1,2}:[0-9]{2})$", RegexOptions.CultureInvariant);

    /// <summary>True when line 1 is PPOS or a waypoint: the box shows something. "-----" and nothing are not shown.</summary>
    public static bool IsShown(FenixNdWaypointLines lines)
    {
        string one = FenixChannelText.TrimmedText(lines.Line1);
        return one == Ppos || Waypoint(one) is not null;
    }

    /// <summary>
    /// The sentence for <paramref name="s"/>: null when the ND is not trusted, when it cannot be read (the header, or a
    /// letter the app does not know in any line the ND reads at its mode, <see cref="FenixChannelNd.ReadChecked"/>; the
    /// problem goes to <paramref name="reportProblem"/>), when the box is absent and when line 1 is neither PPOS nor a
    /// waypoint; <see cref="NoActiveWaypoint"/> for PPOS. A line 2 or 3 the ND does not show leaves its part out.
    /// </summary>
    public static string? Readout(FenixChannelSnapshot s, Action<string> reportProblem)
    {
        if (!s.Verdict.Trusted) return null;

        FenixChannelSection<FenixNdData> section = FenixChannelNd.ReadChecked(s);
        if (!section.IsOk)
        {
            reportProblem(section.Problem ?? "no data");
            return null;
        }

        FenixNdWaypointLines? lines = section.Value!.Waypoint;
        if (lines is null) return null;

        string one = FenixChannelText.TrimmedText(lines.Line1);
        if (one == Ppos) return NoActiveWaypoint;

        (string Ident, string? Bearing)? waypoint = Waypoint(one);
        if (waypoint is null) return null;

        List<string> parts = [waypoint.Value.Ident];
        if (waypoint.Value.Bearing is not null) parts.Add($"{waypoint.Value.Bearing} degrees");

        Match distance = Line2Form.Match(FenixChannelText.TrimmedText(lines.Line2));
        if (distance.Success) parts.Add($"{distance.Groups[1].Value} nautical miles");

        Match time = Line3Form.Match(FenixChannelText.TrimmedText(lines.Line3));
        if (time.Success) parts.Add(time.Groups[1].Value);

        return string.Join(", ", parts);
    }

    // The ident and the bearing (as drawn) of a waypoint line; null when it is not the waypoint form.
    private static (string Ident, string? Bearing)? Waypoint(string line)
    {
        Match match = Line1Form.Match(line);
        if (!match.Success) return null;

        string ident = match.Groups[1].Value;
        if (ident.All(c => c == '-')) return null;

        Group bearing = match.Groups[3];
        if (!bearing.Success) return (ident, null);

        // A bearing is 0 to 360; anything else is not the waypoint form.
        if (!NumberParsing.TryParseExternal(bearing.Value, out double degrees) || degrees is < 0 or > 360) return null;

        return (ident, bearing.Value);
    }
}
