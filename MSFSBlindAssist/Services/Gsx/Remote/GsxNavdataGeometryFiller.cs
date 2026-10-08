using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// Fills the stand GEOMETRY GSX's Remote API leaves out for a stand no GSX profile section covers
/// (its heading and its size) from the SAME stand in navdata [DCK-42].
///
/// <para>
/// <b>Why it exists.</b> GSX publishes <c>heading</c>, <c>type</c>, <c>hasJetway</c> and
/// <c>airlineCodes</c> only for a stand a profile section covers. Every other stand arrives with a
/// position, <c>uiType</c> and <c>maxWingspan</c> 999 alone. Live at KSAN (2026-10-07) that was 75
/// of 79 stands; the installed profile is LatinVFR's, written for a different scenery, and its 4
/// matching sections are exactly the 4 stands with a heading. At KSFO, with no profile, it was 208
/// of 208. <c>GateDataSource.DropUnusableHeadings</c> threw every one of them away, so a pilot was
/// left with 4 stands after touchdown and SayIntentions' assigned Gate 115 could not be found.
/// </para>
///
/// <para>
/// <b>Why navdata is the right donor, measured.</b> Navdata stays authoritative for stand GEOMETRY
/// (see <see cref="GsxConcourseLetterFiller"/>'s doc for the one measured exception, the concourse
/// LETTER). All 75 KSAN stands sit 0.36-4.50 m (median 2.16) from a same-numbered navdata stand.
/// Where GSX and navdata BOTH publish a heading (187 KJFK stands), they agree to a median 0.24
/// degrees, max 6.68, with no 180-degree flips.
/// </para>
///
/// <para>
/// <b>Only gaps, never a second opinion.</b> A heading GSX published, or one the <c>.ini</c> join
/// recovered (GSX's own <c>this_parking_pos</c>, joined first), is never touched; the same for a
/// size GSX published. The match is the shared stand rule: same NUMBER, within
/// <see cref="MatchRadiusMetres"/> (<see cref="GsxStandLetterMatch.MatchRadiusMetres"/>, whose doc
/// carries the measurement). In-range candidates that disagree with the NEAREST one on heading by
/// more than <see cref="MaxHeadingDisagreementDegrees"/> are REFUSED, never arbitrated, and nothing
/// is taken from a refused match. A stand neither source can orient stays NaN, and
/// <c>DropUnusableHeadings</c> still drops it.
/// </para>
///
/// <para>
/// <b>The jet bridge and the airlines, unconfigured stands only [DCK-44].</b> A stand flagged
/// <see cref="ParkingSpot.GsxUnconfigured"/> (GSX sent no <c>heading</c> and no <c>hasJetway</c>:
/// no profile covers it) has no GSX opinion on either, so an accepted donor also lends
/// <see cref="ParkingSpot.HasJetway"/> and, when it has any, <see cref="ParkingSpot.AirlineCodes"/>.
/// Without it 53 of KSAN's 75 recovered gates read "no jetway": the label loses "(Jetway)" and
/// docking says "Door on your left" for a jet bridge, a regression against the navdata list the
/// recovered one replaces at a profile-less airport. NEVER for a stand that is not flagged: KJFK's
/// Gate 1A lacks only its heading, GSX published its jet-bridge flag and airline codes, and only the
/// heading is borrowed.
/// </para>
///
/// <para>
/// <b>The size.</b> Navdata's <see cref="ParkingSpot.Radius"/> is FEET and a GSX spot's is METRES
/// (DCK-7), so it is converted. <see cref="ParkingSpot.MaxWingspanMeters"/> becomes twice that
/// radius, because <see cref="ParkingSpot.FitsAircraft"/>'s navdata rule is "the radius holds the
/// half-span". The fit filter therefore answers exactly what it answers on a navdata list, and
/// SayIntentions' position match (SI-7) gets a real radius rather than 999/2 m.
/// </para>
///
/// <para>
/// <b>Pure, static, never throws.</b> The navdata read is a delegate invoked AT MOST ONCE, and not
/// at all when no stand needs anything (this runs on the UI thread while a gate dropdown is
/// built). <c>GateDataSource</c> hands this filler and <see cref="GsxConcourseLetterFiller"/> the
/// same lazy read, so the path still costs one query. Mutates and returns the SAME instances, like
/// <see cref="GsxStopPositionJoiner"/>.
/// </para>
/// </summary>
public static class GsxNavdataGeometryFiller
{
    /// <summary>The shared same-stand radius; see <see cref="GsxStandLetterMatch.MatchRadiusMetres"/>.</summary>
    internal const double MatchRadiusMetres = GsxStandLetterMatch.MatchRadiusMetres;

    /// <summary>
    /// How far an in-range navdata candidate may disagree with the NEAREST one on heading before the
    /// match is refused (each candidate is compared with the nearest, not with every other).
    /// Two rows for one physical stand (a duplicated row, a MARS pair) point the same way; the
    /// widest same-stand disagreement measured between GSX and navdata is 6.68 degrees (KJFK).
    /// </summary>
    internal const double MaxHeadingDisagreementDegrees = 10.0;

    private const double FeetToMetres = 0.3048;

    public static List<ParkingSpot> Fill(IReadOnlyList<ParkingSpot>? apiSpots,
                                         Func<IReadOnlyList<ParkingSpot>?>? navdata)
    {
        var result = new List<ParkingSpot>();
        if (apiSpots == null) return result;

        var needy = new List<ParkingSpot>();
        foreach (var spot in apiSpots)
        {
            if (spot == null) continue;
            result.Add(spot);
            if (NeedsHeading(spot) || NeedsSize(spot)) needy.Add(spot);
        }

        if (needy.Count == 0) return result;   // navdata is never even asked for

        var donors = LoadDonors(navdata);
        int headingsNeeded = 0, headingsFilled = 0, sizesNeeded = 0, sizesFilled = 0, jetwaysFilled = 0, refused = 0;

        foreach (var spot in needy)
        {
            bool needsHeading = NeedsHeading(spot);
            bool needsSize = NeedsSize(spot);
            if (needsHeading) headingsNeeded++;
            if (needsSize) sizesNeeded++;

            ParkingSpot? donor = AgreedDonor(spot, donors, out bool wasRefused);
            if (wasRefused) { refused++; continue; }
            if (donor == null) continue;

            if (needsHeading)
            {
                spot.Heading = GsxProfileParser.NormalizeHeading(donor.Heading);
                headingsFilled++;
            }

            if (needsSize && donor.Radius > 0)
            {
                double radiusMetres = donor.Radius * FeetToMetres;   // navdata FEET -> GSX METRES (DCK-7)
                spot.Radius = radiusMetres;
                spot.MaxWingspanMeters = 2.0 * radiusMetres;         // "the radius holds the half-span"
                sizesFilled++;
            }

            // GSX has no opinion on these two for a stand no profile covers [DCK-44]; for any other
            // stand (KJFK's Gate 1A) it published them, and they are never replaced.
            if (spot.GsxUnconfigured)
            {
                spot.HasJetway = donor.HasJetway;
                if (donor.HasJetway) jetwaysFilled++;
                if (!string.IsNullOrEmpty(donor.AirlineCodes)) spot.AirlineCodes = donor.AirlineCodes;
            }
        }

        LogSummary(headingsNeeded, headingsFilled, sizesNeeded, sizesFilled, jetwaysFilled, donors.Count, refused);
        return result;
    }

    /// <summary>A number is required: it is half of the same-stand evidence, as in GsxStandLetterMatch.</summary>
    private static bool NeedsHeading(ParkingSpot spot)
        => spot.Number > 0 && !GsxRemoteParkingReader.HasUsableHeading(spot);

    private static bool NeedsSize(ParkingSpot spot)
        => spot.Number > 0 && !spot.MaxWingspanMeters.HasValue;

    /// <summary>Navdata rows with a real number, coordinate and heading, read once.</summary>
    private static List<ParkingSpot> LoadDonors(Func<IReadOnlyList<ParkingSpot>?>? navdata)
    {
        var donors = new List<ParkingSpot>();
        if (navdata == null) return donors;

        IReadOnlyList<ParkingSpot>? spots;
        try
        {
            spots = navdata();
        }
        catch (Exception ex)
        {
            // A failed navdata read must never cost the pilot the API list; every stand simply
            // keeps what it had, and DropUnusableHeadings decides as it did before this filler.
            Log.Debug("Gsx", $"navdata geometry: navdata lookup failed, nothing filled: {ex.Message}");
            return donors;
        }

        if (spots == null) return donors;
        foreach (var s in spots)
        {
            if (s == null || s.Number <= 0) continue;
            if (double.IsNaN(s.Heading)) continue;
            if (double.IsNaN(s.Latitude) || double.IsNaN(s.Longitude)) continue;
            if (s.Latitude == 0.0 && s.Longitude == 0.0) continue;   // null island is not a position
            donors.Add(s);
        }
        return donors;
    }

    /// <summary>
    /// The nearest same-numbered donor within <see cref="MatchRadiusMetres"/>, or null when there is
    /// none. When an in-range donor disagrees with the NEAREST one on heading by more than
    /// <see cref="MaxHeadingDisagreementDegrees"/>, <paramref name="wasRefused"/> is set and null is
    /// returned: refuse, never arbitrate.
    /// </summary>
    private static ParkingSpot? AgreedDonor(ParkingSpot spot, List<ParkingSpot> donors, out bool wasRefused)
    {
        wasRefused = false;
        ParkingSpot? nearest = null;
        double nearestMetres = double.MaxValue;
        var inRange = new List<ParkingSpot>();

        foreach (var donor in donors)
        {
            if (donor.Number != spot.Number) continue;
            double metres = TaxiGeo.HaversineMeters(spot.Latitude, spot.Longitude, donor.Latitude, donor.Longitude);
            if (metres > MatchRadiusMetres) continue;
            inRange.Add(donor);
            if (metres < nearestMetres) { nearestMetres = metres; nearest = donor; }
        }

        if (nearest == null) return null;

        foreach (var donor in inRange)
        {
            if (AngleBetween(donor.Heading, nearest.Heading) > MaxHeadingDisagreementDegrees)
            {
                wasRefused = true;
                return null;
            }
        }
        return nearest;
    }

    private static double AngleBetween(double a, double b)
    {
        double d = Math.Abs(a - b) % 360.0;
        return d > 180.0 ? 360.0 - d : d;
    }

    /// <summary>ONE line per call, never per stand. Warn only when a match was refused.</summary>
    private static void LogSummary(int headingsNeeded, int headingsFilled, int sizesNeeded, int sizesFilled,
                                   int jetwaysFilled, int donorCount, int refused)
    {
        string summary =
            $"navdata geometry: {headingsNeeded} stand(s) had no GSX heading and {sizesNeeded} no GSX size; " +
            $"filled {headingsFilled} heading(s), {sizesFilled} size(s) and {jetwaysFilled} jet-bridge flag(s) " +
            $"from the same-numbered navdata stand within {MatchRadiusMetres:0.#} m ({donorCount} candidate stand(s)).";

        if (refused > 0)
            Log.Warn("Gsx", summary + $" {refused} stand(s) had navdata candidates disagreeing by more than " +
                            $"{MaxHeadingDisagreementDegrees:0.#} degrees and were left alone rather than guessed at.");
        else
            Log.Debug("Gsx", summary);
    }
}
