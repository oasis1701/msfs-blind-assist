using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services.SayIntentions;

/// <summary>
/// Turns a SayIntentions pushback approval into something a blind pilot can act on.
///
/// SI's wording, captured live at KBOS on 2026-07-29:
///
///     IN : Request pushback.
///     OUT: Push and start approved. Tail South-West.
///
/// It names the TAIL, and it names it as a compass point. Both matter. A sighted pilot
/// glances outside and knows what "tail south-west" looks like; a blind pilot knows
/// only their heading indicator, and a compass point on its own tells them nothing
/// about which way the aircraft is about to rotate or where it will end up pointing.
/// That is the whole gap this fills: the tail direction is converted to the heading the
/// NOSE will finish on, and to the turn that gets there from where the aircraft is
/// pointing right now.
///
/// Pure — covered by SayIntentionsPushbackTests.
/// </summary>
public static class SayIntentionsPushback
{
    /// <summary>
    /// The 16 compass points, longest name first so "south-south-west" is consumed
    /// before "south-west", and that before "south". A shorter name matching first
    /// would silently mis-read the direction by 22 or 45 degrees, which is exactly the
    /// error the pilot cannot see.
    /// </summary>
    private static readonly (string Name, string Spoken, double Bearing)[] Points =
    {
        ("NORTH-NORTH-EAST", "north-north-east", 22.5),
        ("EAST-NORTH-EAST",  "east-north-east",  67.5),
        ("EAST-SOUTH-EAST",  "east-south-east",  112.5),
        ("SOUTH-SOUTH-EAST", "south-south-east", 157.5),
        ("SOUTH-SOUTH-WEST", "south-south-west", 202.5),
        ("WEST-SOUTH-WEST",  "west-south-west",  247.5),
        ("WEST-NORTH-WEST",  "west-north-west",  292.5),
        ("NORTH-NORTH-WEST", "north-north-west", 337.5),
        ("NORTH-EAST",       "north-east",       45),
        ("SOUTH-EAST",       "south-east",       135),
        ("SOUTH-WEST",       "south-west",       225),
        ("NORTH-WEST",       "north-west",       315),
        ("NORTH",            "north",            0),
        ("EAST",             "east",             90),
        ("SOUTH",            "south",            180),
        ("WEST",             "west",             270)
    };

    private static readonly Regex Separators = new(@"[\s-]+", RegexOptions.Compiled);

    /// <summary>Matches the tail direction in an approval. Only "tail" is parsed —
    /// that is the phrasing SI was observed to use, and inventing a "nose" branch
    /// against no capture is how a parser ends up confidently wrong.</summary>
    private static readonly Regex TailDirection = new(
        @"\bTAIL\s+(?<dir>" +
        string.Join("|", Points.Select(p => p.Name.Replace("-", @"[\s-]*"))) +
        @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Recognizes an approval at all, so a mere mention of a tail elsewhere in
    /// a transmission cannot be read as clearance to push.</summary>
    private static readonly Regex Approval = new(
        @"\bPUSH(?:BACK)?\b[^.]*\bAPPROVED\b|\bAPPROVED\b[^.]*\bPUSH(?:BACK)?\b|\bPUSH\s+AND\s+START\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The compass bearing the tail is to end up on, or null.</summary>
    public static (string Spoken, double Bearing)? ParseTailDirection(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        if (!Approval.IsMatch(message)) return null;

        var match = TailDirection.Match(message);
        if (!match.Success) return null;

        // Separators are stripped from BOTH sides rather than normalized to hyphens:
        // the pattern accepts "South-West", "South West" and "Southwest" alike, so
        // rewriting the match to a hyphenated form leaves the last of those as
        // "SOUTHWEST" and matching no point at all.
        string normalized = Separators.Replace(match.Groups["dir"].Value, "").ToUpperInvariant();
        foreach (var point in Points)
        {
            if (Separators.Replace(point.Name, "").Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return (point.Spoken, point.Bearing);
        }

        return null;
    }

    /// <summary>A turn too small to be worth naming — the aircraft is already pointing
    /// where it will end up, so "right" would be describing nothing.</summary>
    private const double NegligibleTurnDegrees = 10;

    /// <summary>How close to a half-turn counts as neither direction. At 180 the two
    /// are equally valid, and near it the answer is decided by a few degrees of
    /// heading noise and by the unverified true-versus-magnetic assumption below —
    /// so it is not claimed at all.</summary>
    private const double AmbiguousTurnBandDegrees = 25;

    /// <summary>
    /// Which way the aircraft swings during the pushback: <c>"right"</c>, <c>"left"</c>,
    /// <c>"about turn"</c>, or null when the transmission is not a pushback approval,
    /// carries no direction, or there is no aircraft heading to compare against.
    ///
    /// One word is the whole advisory ON PURPOSE. It first spoke the finishing compass
    /// point, the finishing heading and the size of the turn — accurate, and too much.
    /// The controller has just said the useful part out loud; all the pilot is missing
    /// is which way that puts them, and a sentence of arithmetic between hearing the
    /// clearance and acting on it is a cost, not a service.
    ///
    /// It also makes the readout robust. The one unverified assumption here is that
    /// SI's compass points are TRUE bearings, as compass points conventionally are, so
    /// the comparison is done in true. If that is wrong the error is one magnetic
    /// variation — about 14 degrees at KBOS — which cannot flip left into right except
    /// within the two guard bands above, where nothing is claimed anyway.
    ///
    /// <paramref name="headingMagnetic"/> is null when no position is available: the
    /// last-transmission hotkey is on the offline allowlist and has to keep working
    /// with SimConnect disconnected, so the direction is simply omitted.
    /// </summary>
    public static string? DescribeTurnDirection(
        string? message, double? headingMagnetic, double magneticVariation)
    {
        var tail = ParseTailDirection(message);
        if (tail == null || headingMagnetic == null) return null;

        // The nose finishes opposite the tail. Compare in TRUE: the compass point is
        // true, the heading indicator is magnetic, and true = magnetic + variation
        // (variation east positive).
        double noseTrue = Normalize(tail.Value.Bearing + 180);
        double headingTrue = Normalize(headingMagnetic.Value + magneticVariation);

        double turn = SignedDifference(headingTrue, noseTrue);
        double magnitude = Math.Abs(turn);

        if (magnitude < NegligibleTurnDegrees) return null;
        if (magnitude > 180 - AmbiguousTurnBandDegrees) return "about turn";

        return turn >= 0 ? "right" : "left";
    }

    /// <summary>Shortest signed turn from <paramref name="from"/> to
    /// <paramref name="to"/>: positive right, negative left. Wrapping through 360 the
    /// naive way is what turns a 10-degree right turn into a 350-degree left one.</summary>
    internal static double SignedDifference(double from, double to)
    {
        double delta = (to - from + 540) % 360 - 180;
        return delta;
    }

    private static double Normalize(double degrees) => (degrees % 360 + 360) % 360;
}
