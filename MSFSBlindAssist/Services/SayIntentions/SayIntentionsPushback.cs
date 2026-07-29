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

    /// <summary>
    /// The advisory to append to a pushback approval, or null when the transmission is
    /// not one.
    ///
    /// <paramref name="headingMagnetic"/> and <paramref name="magneticVariation"/> come
    /// from the aircraft; pass null for the heading when no position is available and
    /// the advisory degrades to the part that needs no aircraft state. That matters
    /// because the last-transmission hotkey is allowed to work with SimConnect
    /// disconnected.
    ///
    /// ASSUMPTION, not yet verified in the sim: SI's compass points are TRUE bearings,
    /// as compass points conventionally are. The final heading is therefore converted
    /// to MAGNETIC before it is spoken, because magnetic is what the pilot's heading
    /// indicator shows. If a live pushback finishes on a heading about one variation
    /// away from the one announced, that assumption is the thing to revisit — at KBOS
    /// the variation is roughly 14 degrees west, so the error would be obvious. The
    /// turn is deliberately rounded to the nearest 5 and described as "about", which
    /// keeps it honest either way.
    /// </summary>
    public static string? DescribeApproval(
        string? message, double? headingMagnetic, double magneticVariation)
    {
        var tail = ParseTailDirection(message);
        if (tail == null) return null;

        // The nose finishes opposite the tail.
        double noseTrue = Normalize(tail.Value.Bearing + 180);
        // true = magnetic + variation (east positive), so magnetic = true - variation.
        double noseMagnetic = Normalize(noseTrue - magneticVariation);
        string noseSpoken = SpokenPoint(noseTrue);

        if (headingMagnetic == null)
            return $"Tail to the {tail.Value.Spoken}. You will finish facing {noseSpoken}.";

        double turn = SignedDifference(Normalize(headingMagnetic.Value), noseMagnetic);
        string direction = turn >= 0 ? "right" : "left";
        int degrees = (int)(Math.Round(Math.Abs(turn) / 5.0) * 5);

        string result =
            $"Tail to the {tail.Value.Spoken}. You will finish facing {noseSpoken}, " +
            $"heading {(int)Math.Round(noseMagnetic) % 360:000}.";

        // Under about 5 degrees there is no turn worth describing, and "about 0 degrees
        // right" is worse than saying nothing.
        if (degrees >= 5)
            result += $" That is about {degrees} degrees {direction} of your current heading.";

        return result;
    }

    /// <summary>Nearest of the 16 points to a bearing.</summary>
    internal static string SpokenPoint(double bearing)
    {
        double normalized = Normalize(bearing);
        var nearest = Points[0];
        double best = double.MaxValue;

        foreach (var point in Points)
        {
            double delta = Math.Abs(SignedDifference(normalized, point.Bearing));
            if (delta < best) { best = delta; nearest = point; }
        }

        return nearest.Spoken;
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
