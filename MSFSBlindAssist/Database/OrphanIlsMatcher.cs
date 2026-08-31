using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Database;

/// <summary>
/// Decides which ORPHANED <c>ils</c> row belongs to a given runway end.
///
/// An orphan is a row whose <c>loc_airport_ident</c> / <c>loc_runway_name</c> /
/// <c>loc_runway_end_id</c> join columns navdatareader left NULL. The row itself is
/// correct — right ident, frequency, position and localizer course — only the link to a
/// runway is missing, so it has to be recovered geometrically.
///
/// <para><b>How many orphans.</b> Roughly two hundred, and THIS FILE IS THE ONE PLACE
/// THAT NUMBER IS STATED — every other mention defers here rather than carrying its own
/// figure, because it had drifted to four different values across the tree. Measured 217
/// in a 2026-08-30 fs2024 extraction; earlier builds counted 192 and 213. The count is
/// not a constant: it varies with the installed scenery, so treat any exact figure as a
/// measurement with a date on it and never as an invariant. fs2020 has zero, so this
/// whole path is a no-op there.
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
///
/// <para><b>The competitor set is the requested airport's ends only.</b> The caller's
/// candidate bounding box spans ~11 km and can reach a neighbouring airport's localizer,
/// which therefore has nobody to claim it. Widening the competitor scan to every airport
/// in the box was measured and REJECTED: the 32 localizers currently accepted by ends at
/// two idents are all DUPLICATE AIRPORT RECORDS for one physical field (UZTT/UTTT,
/// UZSS/UTSS, OJMS/OJ40, FVBU/FVJN, FNLF/FNBJ, ORSJ/ORSU, VAJA/VEDO…), where both records
/// describe the same runway and both must keep the localizer. Widening the scan makes the
/// second record's runway report NO ILS — 32 regressions to close a gap with zero
/// observed instances. Do not "fix" this without a real case in hand.
/// </summary>
public static class OrphanIlsMatcher
{
    /// <summary>
    /// A runway end, as stored in <c>runway_end</c>. Heading is TRUE degrees.
    /// <paramref name="LengthMetres"/> is the parent runway's length (0 = unknown, which
    /// disables the along-track ceiling for that end).
    /// </summary>
    public readonly record struct RunwayEnd(
        string Name, double Latitude, double Longitude, double HeadingTrue, double LengthMetres = 0.0);

    /// <summary>
    /// How far a candidate's localizer course may differ from the runway heading. Unchanged
    /// from the predecessor rule; it is what gates out the reciprocal end's localizer.
    /// </summary>
    private const double HeadingToleranceDeg = 5.0;

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

    /// <summary>
    /// How far BEYOND the far end of the runway a localizer may sit. The centerline is
    /// extended forward without limit, so without this an aligned antenna arbitrarily far
    /// downfield — a second airport on the same bearing, or a relocated antenna — is
    /// admitted on cross-track alone, which the cross-track backstop cannot catch because
    /// the two runways are collinear. Bounding it by the runway's OWN length keeps the
    /// rule per-runway instead of adding a second tuned distance: an accepted antenna must
    /// lie within <c>LengthMetres + MaxLocalizerSetbackMetres</c> of the threshold. The
    /// widest legitimate setback measured across fs2024 is 1,650 m (UBFI 11), and the
    /// whole-database result is identical for any allowance from 2,000 m to 4,000 m, so
    /// 3,000 m is comfortably clear of every genuine match. Ends whose runway length is
    /// unknown (0) skip the ceiling entirely rather than risk dropping a real ILS.
    /// </summary>
    public const double MaxLocalizerSetbackMetres = 3000.0;

    /// <summary>
    /// Perpendicular distance in metres from an antenna to the runway centerline through
    /// <paramref name="end"/>, extended in both directions.
    /// </summary>
    public static double CrossTrackMetres(RunwayEnd end, double latitude, double longitude)
    {
        Project(FrameFor(end), latitude, longitude, out _, out double crossTrack);
        return crossTrack;
    }

    /// <summary>
    /// Returns the localizer serving <paramref name="airportEnds"/>[<paramref name="targetIndex"/>],
    /// or null when none does.
    /// </summary>
    /// <param name="airportEnds">
    /// Every runway end at the airport, the target included. These are the competitors for
    /// the mutual-best test; a single-element list degrades this to a plain
    /// nearest-centerline match.
    /// </param>
    /// <param name="targetIndex">
    /// Index of the end being matched. The target is identified by POSITION, not by name,
    /// so an airport carrying two ends with the same name (add-on scenery layered over
    /// stock runways) still has the second one compete — under a name compare it was
    /// skipped as "the target itself" and the parallel-borrowing failure came back.
    /// </param>
    public static ILSData? SelectBest(
        IReadOnlyList<RunwayEnd> airportEnds, int targetIndex, IReadOnlyList<ILSData> candidates)
    {
        if (targetIndex < 0 || targetIndex >= airportEnds.Count)
            return null;

        // One frame per end, built once: Project is called O(candidates x ends) times and
        // each frame costs three trig calls.
        var frames = new RunwayFrame[airportEnds.Count];
        for (int i = 0; i < airportEnds.Count; i++)
            frames[i] = FrameFor(airportEnds[i]);

        var target = airportEnds[targetIndex];
        double alongTrackCeiling = target.LengthMetres > 0
            ? target.LengthMetres + MaxLocalizerSetbackMetres
            : double.MaxValue;

        ILSData? best = null;
        double bestCrossTrack = double.MaxValue;

        foreach (var candidate in candidates)
        {
            if (!HeadingMatches(candidate.LocalizerHeading, target.HeadingTrue))
                continue;

            Project(frames[targetIndex], candidate.AntennaLatitude, candidate.AntennaLongitude,
                out double alongTrack, out double crossTrack);

            // A localizer serving this end sits beyond the far end, so it is ahead of the
            // threshold. Anything behind belongs to something else, and anything past the
            // far end by more than a localizer's setback serves something further away.
            if (alongTrack <= 0.0 || alongTrack > alongTrackCeiling)
                continue;

            if (crossTrack > MaxCrossTrackMetres)
                continue;

            if (crossTrack >= bestCrossTrack)
                continue;

            if (IsClaimedByAnotherRunwayEnd(airportEnds, frames, targetIndex, candidate, crossTrack))
                continue;

            bestCrossTrack = crossTrack;
            best = candidate;
        }

        return best;
    }

    /// <summary>
    /// True when some other runway end at the airport lies closer to this antenna's
    /// centerline than the target does — i.e. the localizer is that runway's, not this one's.
    /// </summary>
    private static bool IsClaimedByAnotherRunwayEnd(
        IReadOnlyList<RunwayEnd> airportEnds, RunwayFrame[] frames, int targetIndex,
        ILSData candidate, double targetCrossTrack)
    {
        for (int i = 0; i < airportEnds.Count; i++)
        {
            if (i == targetIndex)
                continue;

            if (!HeadingMatches(candidate.LocalizerHeading, airportEnds[i].HeadingTrue))
                continue;

            Project(frames[i], candidate.AntennaLatitude, candidate.AntennaLongitude,
                out double otherAlong, out double otherCross);

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
    /// The runway-aligned frame for <paramref name="end"/>. Longitude is scaled at the
    /// end's own latitude, so high-latitude airports — ENSB at 78°N is the extreme in this
    /// database — are not stretched east-west.
    /// </summary>
    private static RunwayFrame FrameFor(RunwayEnd end)
        => RunwayFrame.For(end.Latitude, end.Longitude, end.HeadingTrue, end.Latitude);

    /// <summary>
    /// Resolves an antenna's offset from the threshold into runway-relative metres:
    /// <paramref name="alongTrack"/> positive ahead of the threshold, <paramref name="crossTrack"/>
    /// unsigned. The projection itself belongs to <see cref="RunwayFrame"/> — this file must
    /// never grow its own copy of it.
    /// </summary>
    private static void Project(RunwayFrame frame, double latitude, double longitude,
        out double alongTrack, out double crossTrack)
    {
        alongTrack = frame.Along(latitude, longitude);
        crossTrack = Math.Abs(frame.SignedCrossTrack(latitude, longitude));
    }

    /// <summary>Signed heading delta wrapped into [-180, 180], so 359° vs 1° is 2°.</summary>
    private static double NormalizeHeadingDelta(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }
}
