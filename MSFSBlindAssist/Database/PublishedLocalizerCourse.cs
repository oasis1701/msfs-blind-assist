using Microsoft.Data.Sqlite;

namespace MSFSBlindAssist.Database;

/// <summary>
/// A localizer's course as the approach chart gives it, read from the airport's own approach
/// procedures: the final approach leg that is flown on that localizer.
///
/// <para>The navdata `ils` row has no published course, only a true heading and a magnetic
/// variation. In the contributor's MSFS 2020 build that variation is the station declination,
/// so true less it is the charted course (a whole degree for 4,508 of 4,515 rows). In an MSFS
/// 2024 navdatareader 1.2.4 build with a Navigraph update it is today's variation instead
/// (a whole degree for 100 of 4,272), and the result matched the procedure's final course for
/// only 43% of localizers: 37% were 1 degree off, 13% 2 degrees, 8% 3 or more (KJFK 04L 43
/// against 44, KFTW 16 169 against 164, measured 2026-11-01). A blind pilot sets the course
/// they are read, so it has to be the chart's. The procedure gives one for 4,033 of the 4,102
/// localizers linked to an airport in that build; the rest keep
/// <see cref="LandingAidText.MagneticCourse"/>.</para>
///
/// <para>Which leg: a CF leg, not in the missed approach, with a magnetic course, whose
/// recommended navaid is the localizer, on an approach to the localizer's own runway (so a back
/// course approach to the other end never answers). An ILS, LOC, LDA, IGS or SDF approach is
/// preferred: an NDB approach can name the localizer as its navaid and fly the NDB's course
/// (TTCP 11: the ILS says 109, the NDB/DME 115). Within an approach the leg into the runway
/// threshold ("RW" + runway) wins, else its last such leg, since an LDA's final leg often ends
/// at a missed approach point short of the runway (KDCA 19, 147). Ties go to the lowest
/// approach id, so two ILS procedures a degree apart (CYYR 08: 75 and 76) always give the same
/// answer.</para>
/// </summary>
public static class PublishedLocalizerCourse
{
    /// <summary>One candidate leg: a non-missed CF leg with a magnetic course and a
    /// recommended navaid, at one airport.</summary>
    public sealed record Leg(string RunwayName, string NavaidIdent, string ApproachType,
        string FixIdent, long ApproachId, long LegId, double Course);

    /// <summary>Every candidate leg at one airport, keyed on the indexed
    /// <c>approach.airport_id</c>. One query, read once per airport and shared by its runway
    /// ends.</summary>
    public static List<Leg> ReadAirport(SqliteConnection connection, int airportId)
    {
        var legs = new List<Leg>();
        using var command = new SqliteCommand(@"
            SELECT ap.runway_name, l.recommended_fix_ident, ap.type, l.fix_ident,
                   ap.approach_id, l.approach_leg_id, l.course
            FROM approach ap
            JOIN approach_leg l ON l.approach_id = ap.approach_id
            WHERE ap.airport_id = @AirportId
              AND l.is_missed = 0 AND l.type = 'CF' AND l.is_true_course = 0
              AND l.course IS NOT NULL
              AND l.recommended_fix_ident IS NOT NULL AND l.recommended_fix_ident <> ''", connection);
        command.Parameters.AddWithValue("@AirportId", airportId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            legs.Add(new Leg(
                reader.IsDBNull(0) ? "" : reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetDouble(6)));
        }
        return legs;
    }

    /// <summary>The published final approach course of the localizer <paramref name="localizerIdent"/>
    /// serving <paramref name="runwayName"/>, as a whole degree from 1 to 360, or null when no
    /// procedure flies it.</summary>
    public static int? Select(IEnumerable<Leg> legs, string runwayName, string localizerIdent)
    {
        if (string.IsNullOrWhiteSpace(runwayName) || string.IsNullOrWhiteSpace(localizerIdent))
            return null;

        string threshold = "RW" + runwayName.Trim();
        var mine = legs.Where(l => Same(l.RunwayName, runwayName) && Same(l.NavaidIdent, localizerIdent));

        var approach = mine
            .GroupBy(l => l.ApproachId)
            .OrderBy(g => IsLocalizerApproach(g.First().ApproachType) ? 0 : 1)
            .ThenBy(g => g.Any(l => Same(l.FixIdent, threshold)) ? 0 : 1)
            .ThenBy(g => g.Key)
            .FirstOrDefault();
        if (approach == null)
            return null;

        Leg leg = approach.FirstOrDefault(l => Same(l.FixIdent, threshold))
            ?? approach.OrderByDescending(l => l.LegId).First();
        return LandingAidText.WholeCourse(leg.Course);
    }

    private static bool IsLocalizerApproach(string approachType)
        => approachType.Trim().ToUpperInvariant() is "ILS" or "LOC" or "LDA" or "IGS" or "SDF";

    private static bool Same(string a, string b)
        => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
