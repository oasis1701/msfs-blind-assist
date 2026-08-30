namespace MSFSBlindAssist.Database;

/// <summary>
/// Decides which ORPHANED <c>ils</c> row belongs to a given runway end.
///
/// An orphan is a row whose <c>loc_airport_ident</c> / <c>loc_runway_name</c> /
/// <c>loc_runway_end_id</c> join columns navdatareader left NULL. The row itself is
/// correct — right ident, frequency, position and localizer course — only the link to a
/// runway is missing, so it has to be recovered geometrically. fs2024 has 192 such rows;
/// fs2020 has none, so this whole path is a no-op there.
///
/// <para><b>Why cross-track and not range.</b> The predecessor rule picked the orphan
/// whose antenna was nearest the runway THRESHOLD, on the reasoning that "localizer
/// antennas sit on the runway centerline beyond the far end, so the closest unlinked ILS
/// to a given threshold is the right one". That holds at an airport with one runway per
/// direction and breaks at every parallel-runway airport: the antenna serving a threshold
/// is a full runway length away ALONG track (~3 km), while a parallel runway's antenna is
/// only ~1-2 km away LATERALLY, so straight-line range is dominated by the along-track
/// term and barely sees the offset that actually tells two parallels apart. Measured at
/// KATL (five parallels, all ~090/270): runway 08L's own localizer is 3,027 m from its
/// threshold and runway 09R's is 3,011 m — a SIXTEEN-METRE margin decided it, and 08L,
/// 08R and 09L all took 09R's 108.90, while 27L and 27R both took 26L's 108.70. Measured
/// across the whole fs2024 database, the range rule mis-assigned 46 of 230 runway ends.
///
/// <para>Cross-track distance from the runway's centerline separates them by roughly two
/// orders of magnitude instead: at KATL the correct localizer is 0.1-3.4 m off centerline
/// and the nearest wrong one is 305 m off.
///
/// <para><b>Why mutual-best as well.</b> A cross-track minimum alone still hands a
/// closely-spaced parallel its neighbour's localizer when it has none of its own — KPHX
/// 25L/25R are 246 m apart, and 25R would take 25L's. So a candidate is only accepted for
/// a runway end if it is not closer to some OTHER end's centerline at the same airport;
/// the runway with no localizer of its own then gets nothing, which the caller renders as
/// "no ILS". Showing a blind pilot the wrong ILS frequency is worse than showing none:
/// they would tune and fly the localizer for the runway beside them.
/// </summary>
public static class OrphanIlsMatcher
{
    /// <summary>A runway end, as stored in <c>runway_end</c>. Heading is TRUE degrees.</summary>
    public readonly record struct RunwayEnd(string Name, double Latitude, double Longitude, double HeadingTrue);

    /// <summary>An orphaned <c>ils</c> row. <paramref name="LocalizerHeadingTrue"/> is <c>loc_heading</c>.</summary>
    public readonly record struct IlsCandidate(string Ident, double Latitude, double Longitude, double LocalizerHeadingTrue);

    /// <summary>
    /// How far a candidate's localizer course may differ from the runway heading. Unchanged
    /// from the predecessor rule; it is what gates out the reciprocal end's localizer.
    /// </summary>
    public const double HeadingToleranceDeg = 5.0;

    /// <summary>
    /// Absolute ceiling on how far off the runway centerline a localizer may sit and still
    /// be accepted. This is a backstop for the UNCONTESTED case, not the discriminator —
    /// the mutual-best rule does that work, and the whole-database result is identical for
    /// any value from 250 m to 1,000 m. It exists to refuse an antenna that plainly serves
    /// nothing at this airport when no other runway competes for it (VVLO's would otherwise
    /// have been handed to a runway 9,971 m off its centerline; KDTW's 1,901 m, ZLLL's
    /// 2,229 m). The widest LEGITIMATE offset measured across fs2024 is 216 m (BIAR 19),
    /// so 300 m keeps every genuine match with headroom.
    /// </summary>
    public const double MaxCrossTrackMetres = 300.0;

    private const double MetresPerDegreeLatitude = 111320.0;

    /// <summary>
    /// Perpendicular distance in metres from the candidate's antenna to the runway
    /// centerline through <paramref name="end"/>, extended in both directions.
    /// </summary>
    public static double CrossTrackMetres(RunwayEnd end, IlsCandidate candidate)
    {
        Project(end, candidate, out _, out double crossTrack);
        return crossTrack;
    }

    /// <summary>
    /// Distance in metres from the threshold along the runway heading — positive ahead of
    /// the threshold (where a localizer serving this end must lie), negative behind it.
    /// </summary>
    public static double AlongTrackMetres(RunwayEnd end, IlsCandidate candidate)
    {
        Project(end, candidate, out double alongTrack, out _);
        return alongTrack;
    }

    /// <summary>
    /// Returns the index into <paramref name="candidates"/> of the localizer serving
    /// <paramref name="target"/>, or -1 when none does.
    /// </summary>
    /// <param name="airportEnds">
    /// Every runway end at the airport, <paramref name="target"/> included. These are the
    /// competitors for the mutual-best test; passing only the target degrades this to a
    /// plain nearest-centerline match.
    /// </param>
    public static int SelectBest(RunwayEnd target, IReadOnlyList<IlsCandidate> candidates, IReadOnlyList<RunwayEnd> airportEnds)
    {
        int best = -1;
        double bestCrossTrack = double.MaxValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];

            if (!HeadingMatches(candidate.LocalizerHeadingTrue, target.HeadingTrue))
                continue;

            Project(target, candidate, out double alongTrack, out double crossTrack);

            // A localizer serving this end sits beyond the far end, so it is ahead of the
            // threshold. Anything behind belongs to something else.
            if (alongTrack <= 0.0)
                continue;

            if (crossTrack > MaxCrossTrackMetres)
                continue;

            if (crossTrack >= bestCrossTrack)
                continue;

            if (IsClaimedByAnotherRunwayEnd(target, candidate, crossTrack, airportEnds))
                continue;

            bestCrossTrack = crossTrack;
            best = i;
        }

        return best;
    }

    /// <summary>
    /// True when some other runway end at the airport lies closer to this antenna's
    /// centerline than <paramref name="target"/> does — i.e. the localizer is that
    /// runway's, not this one's.
    /// </summary>
    private static bool IsClaimedByAnotherRunwayEnd(
        RunwayEnd target, IlsCandidate candidate, double targetCrossTrack, IReadOnlyList<RunwayEnd> airportEnds)
    {
        foreach (var other in airportEnds)
        {
            if (string.Equals(other.Name, target.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!HeadingMatches(candidate.LocalizerHeadingTrue, other.HeadingTrue))
                continue;

            Project(other, candidate, out double otherAlong, out double otherCross);

            // A runway the antenna sits BEHIND cannot be served by it, so it has no claim.
            if (otherAlong <= 0.0)
                continue;

            if (otherCross < targetCrossTrack)
                return true;
        }

        return false;
    }

    private static bool HeadingMatches(double localizerHeading, double runwayHeading)
        => Math.Abs(NormalizeHeadingDelta(localizerHeading - runwayHeading)) <= HeadingToleranceDeg;

    /// <summary>
    /// Resolves the antenna's offset from the threshold into runway-relative metres. The
    /// local flat-earth conversion is exact enough at this scale (antennas sit a few km
    /// from the threshold) and scales longitude by cos(latitude) so high-latitude airports
    /// — ENSB at 78°N is the extreme in this database — are not stretched east-west.
    /// </summary>
    private static void Project(RunwayEnd end, IlsCandidate candidate, out double alongTrack, out double crossTrack)
    {
        double metresPerDegreeLongitude = MetresPerDegreeLatitude * Math.Cos(end.Latitude * Math.PI / 180.0);

        double east = (candidate.Longitude - end.Longitude) * metresPerDegreeLongitude;
        double north = (candidate.Latitude - end.Latitude) * MetresPerDegreeLatitude;

        double headingRad = end.HeadingTrue * Math.PI / 180.0;
        double unitEast = Math.Sin(headingRad);
        double unitNorth = Math.Cos(headingRad);

        alongTrack = east * unitEast + north * unitNorth;
        crossTrack = Math.Abs(north * unitEast - east * unitNorth);
    }

    /// <summary>Signed heading delta wrapped into [-180, 180], so 359° vs 1° is 2°.</summary>
    private static double NormalizeHeadingDelta(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }
}
