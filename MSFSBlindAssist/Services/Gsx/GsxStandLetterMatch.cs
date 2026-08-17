using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// "Are these two rows the same physical stand, and if so what concourse LETTER do they agree on?"
/// — the one matching rule shared by the two directions in which this app reconciles a GSX stand
/// list with a navdata one.
///
/// <para>
/// <b>Both directions, one rule.</b>
/// <list type="bullet">
/// <item><see cref="Gsx.Remote.GsxConcourseLetterFiller"/> borrows navdata's letter for an API
/// stand whose <c>uiGateName</c> carries none.</item>
/// <item><see cref="GsxStandNameOverlay"/> corrects navdata's letter from the authoritative gate
/// list, so a stand is called the same thing in every readout.</item>
/// </list>
/// They point opposite ways, but "same stand" means the same thing in both, and a second copy of
/// that judgement would be free to drift from the measurement that justifies it. This class exists
/// to make the drift impossible — it was EXTRACTED from the filler, not written beside it, so the
/// filler's behaviour is byte-identical to before the extraction.
/// </para>
/// </summary>
internal static class GsxStandLetterMatch
{
    /// <summary>
    /// How near two rows must be before either may speak for the other's concourse letter.
    ///
    /// <para>
    /// <b>Chosen from measured stand geometry, not from feel.</b> Across the 231 selectable stands
    /// of the committed KJFK capture, the two CLOSEST stands of any kind are 21.2 m apart (median
    /// nearest-neighbour separation 53.4 m; nothing at all under 15 m). 10 m is therefore under
    /// half the tightest real stand spacing measured at a dense major airport: the acceptance ball
    /// around one stand cannot reach the centre of another.
    /// </para>
    /// <para>
    /// The margin against the failure that actually matters — taking a letter from a stand on a
    /// DIFFERENT concourse — is far larger still, because the match also requires the stand NUMBER
    /// to agree. In the same capture the closest pair sharing a number while differing in concourse
    /// letter is <b>227.4 m</b> apart ("Stand H12" @ Terminal 5 - Remote vs "Gate 12" @ Terminal 4
    /// - Concourse A), so the guard has a ~22x margin on the case it exists for.
    /// </para>
    /// <para>
    /// Erring tight is deliberate and the asymmetry is not close. A radius too SMALL costs nothing
    /// worse than a stand keeping the name it already had — a supported shape in both directions. A
    /// radius too LARGE hands a stand its neighbour's letter, which is a WRONG STAND IDENTITY: it
    /// corrupts SayIntentions' assigned-gate match, mints a junk alias in <c>GateAliasResolver</c>,
    /// and can taxi a blind pilot to the wrong pier with every other part of the readout sounding
    /// correct.
    /// </para>
    /// </summary>
    internal const double MatchRadiusMetres = 10.0;

    /// <summary>
    /// The rows eligible to SPEAK for a concourse letter, filtered once rather than re-tested per
    /// stand.
    /// <para>
    /// A donor must carry a real stand number, a real coordinate, and a <see cref="ParkingSpot.Name"/>
    /// that is a SINGLE A-Z letter. That last filter is what structurally prevents either direction
    /// from putting non-identity prose into the identity slot, and it composes exactly with what
    /// navdata actually holds: <c>LittleNavMapProvider.MapParkingName</c> has already turned the
    /// MSFS <c>GATE_A</c>…<c>GATE_Z</c> enum into a bare letter ("GA" -> "A"), while every
    /// NON-concourse parking name it can produce is a WORD ("Parking", "North", "Southwest",
    /// "Dock") and is rejected here — a stand CATEGORY is not a concourse.
    /// </para>
    /// <para>
    /// (0,0) is rejected outright: null island is a real coordinate to a distance test, and a row
    /// with no position would otherwise sit 10 m from any stand that also lacked one. NaN
    /// coordinates are rejected for the same reason (every comparison against NaN is false, so they
    /// can never match, but excluding them keeps the donor count honest).
    /// </para>
    /// </summary>
    internal static List<ParkingSpot> EligibleDonors(IEnumerable<ParkingSpot>? spots)
    {
        var donors = new List<ParkingSpot>();
        if (spots == null) return donors;

        foreach (var s in spots)
        {
            if (s == null || s.Number <= 0) continue;
            if (!IsSingleLetter(s.Name)) continue;
            if (double.IsNaN(s.Latitude) || double.IsNaN(s.Longitude)) continue;
            if (s.Latitude == 0.0 && s.Longitude == 0.0) continue;
            donors.Add(s);
        }
        return donors;
    }

    /// <summary>
    /// The letter every in-range, same-numbered donor agrees on — or "" when none is in range, or
    /// when two of them DISAGREE.
    ///
    /// <para>
    /// <b>Number agreement is a SECOND, independent axis of evidence beside position</b>, and it is
    /// what lets the radius stay tight without losing real matches: two datasets agreeing both on
    /// where a stand is and on what it is numbered is what makes it the same stand. It is also how
    /// <c>GsxNavdataMerger</c>'s own borrow has always been constrained — its <c>FindNavMatch</c>
    /// buckets by number before anything else.
    /// </para>
    /// <para>
    /// <b>Disagreement is REFUSED, never arbitrated</b> — the same guard <c>GateAliasResolver</c>
    /// applies for the same reason ("if two surviving candidates carry DIFFERENT non-empty
    /// concourse letters … the gate's real concourse is unknown, so adopting either would let the
    /// pilot 'find' gate 51 by the wrong concourse"), and it is why this returns an AGREED letter
    /// rather than the nearest donor's: two rows describing one physical stand (a duplicated row, a
    /// MARS pair "232N"/"232S") both name the same concourse, so agreement is the property that
    /// matters, not proximity ranking. It is also why no SUFFIX test is needed — a MARS pair agrees
    /// on its letter, and any pair that does not agree is refused outright.
    /// </para>
    /// </summary>
    internal static string AgreedLetter(ParkingSpot spot, List<ParkingSpot> donors, out bool wasAmbiguous)
    {
        wasAmbiguous = false;
        string agreed = string.Empty;

        foreach (var donor in donors)
        {
            if (donor.Number != spot.Number) continue;
            if (TaxiGeo.HaversineMeters(spot.Latitude, spot.Longitude,
                                        donor.Latitude, donor.Longitude) > MatchRadiusMetres) continue;

            string letter = donor.Name.Trim().ToUpperInvariant();
            if (agreed.Length == 0) { agreed = letter; continue; }
            if (!string.Equals(agreed, letter, StringComparison.Ordinal))
            {
                wasAmbiguous = true;
                return string.Empty;   // two concourses in range: refuse, never arbitrate
            }
        }

        return agreed;
    }

    /// <summary>ASCII A-Z, exactly one character — never <c>char.IsLetter</c>, which would admit a
    /// non-ASCII letter that no stand-id consumer in this app can compare.</summary>
    internal static bool IsSingleLetter(string? value)
    {
        if (value == null) return false;
        string v = value.Trim();
        if (v.Length != 1) return false;
        char c = char.ToUpperInvariant(v[0]);
        return c >= 'A' && c <= 'Z';
    }
}
