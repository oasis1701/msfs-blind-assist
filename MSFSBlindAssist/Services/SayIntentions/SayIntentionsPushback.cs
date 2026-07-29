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
    /// How far off dead-astern still counts as a straight push.
    ///
    /// Sized by SayIntentions' own resolution, not by taste. The one capture used an
    /// eight-point compass ("South-West"), so SI is choosing the nearest of eight
    /// directions and its answer can be up to 22.5 degrees from the truth. A genuinely
    /// straight push can therefore arrive reading as much as 22.5 degrees off, and any
    /// tighter band would call it a turn. Nothing finer is recoverable: at this
    /// resolution a small turn and a straight push are the same message.
    /// </summary>
    private const double StraightBandDegrees = 25;

    /// <summary>How close to a half-turn counts as neither side. At exactly 180 the two
    /// are equally valid and the answer would be decided by noise, so it is not
    /// claimed.</summary>
    private const double AmbiguousTurnBandDegrees = 25;

    /// <summary>
    /// Which pushback to ask for: <c>"straight"</c>, <c>"tail left, nose right"</c>,
    /// <c>"tail right, nose left"</c>, <c>"about turn"</c>, or null when the
    /// transmission is not a pushback approval, carries no direction, or there is no
    /// heading to compare against.
    ///
    /// The wording matches how the pushback is actually CHOSEN. GSX offers the options
    /// as "nose right, tail left" and so on, and naming both ends means the answer maps
    /// onto that menu whichever end it happens to lead with. "Straight" is one of those
    /// options and therefore one of these answers — it is a real instruction, not the
    /// absence of one.
    ///
    /// <paramref name="aircraftHeading"/> MUST be in the same reference as SI's compass
    /// points, which is why the caller prefers SayIntentions' own
    /// <c>flight_details.heading</c> over the SimConnect heading. Whether SI means true
    /// or magnetic is still unknown and, read this way, does not need to be: its
    /// heading and its cardinals come from the same place, so the difference between
    /// them is exact. Comparing against a SimConnect heading instead would be wrong by
    /// one magnetic variation — 14 degrees at KBOS, more elsewhere — which is over half
    /// the straight band.
    /// </summary>
    public static string? DescribeTurnDirection(string? message, double? aircraftHeading)
    {
        var tail = ParseTailDirection(message);
        if (tail == null || aircraftHeading == null) return null;

        // A straight push sends the tail dead astern. Everything is measured as the
        // tail's departure from that.
        double straightBack = Normalize(aircraftHeading.Value + 180);
        double swing = SignedDifference(straightBack, tail.Value.Bearing);
        double magnitude = Math.Abs(swing);

        if (magnitude <= StraightBandDegrees) return "straight";
        if (magnitude >= 180 - AmbiguousTurnBandDegrees) return "about turn";

        // Clockwise — the tail swinging toward the pilot's left — puts the nose right.
        // The aircraft is rigid: both ends rotate the same way, and it is the SIDE each
        // ends up on that the pushback menu names.
        return swing > 0 ? "tail left, nose right" : "tail right, nose left";
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
