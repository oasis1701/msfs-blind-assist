using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Services.SayIntentions;

/// <summary>
/// One stand the position match may choose, in the shape the destination list already
/// holds it: the label the form will select, the stand's own centre, and how far its
/// own circle reaches.
///
/// <paramref name="RadiusMetres"/> is METRES, and nowhere else in this app is that safe
/// to assume: a navdata spot carries its physical parking radius in FEET while a
/// GSX-sourced one carries metres, and mixing the two is a mistake this codebase has
/// already made once — see <c>ParkingSpot.FitsAircraft</c>, where a feet threshold met a
/// metres radius and "filtered almost everything out". The conversion belongs to the
/// caller, which is the only layer that knows a spot's source; this type takes metres so
/// the unit can never be in doubt at the point the comparison is made.
/// </summary>
public readonly record struct GatePositionCandidate(
    string Label, double Latitude, double Longitude, double RadiusMetres);

/// <summary>
/// Which stand SayIntentions' published gate COORDINATE falls on, for when its published
/// gate NAME matched nothing.
///
/// The name is the primary match and stays so. But sceneries label stands differently
/// from SayIntentions, and when the name misses, destination resolution runs its whole
/// candidate chain and takes the last thing it has — the ARRIVAL RUNWAY. A just-landed
/// aircraft is then routed at the runway it landed on, with the taxiway half of the
/// import perfectly correct, which is the dangerous shape: everything else sounds right.
/// <c>current_flight</c> publishes <c>assigned_gate_lat</c>/<c>assigned_gate_lon</c>
/// alongside the name, so there is a second, language-free way to ask the same question.
///
/// THE ACCEPTANCE TEST IS CONTAINMENT IN THE STAND'S OWN CIRCLE, not a distance
/// constant. That is what makes it discriminating without anything to tune: a Gate Extra
/// gets ~50 m of tolerance, a medium gate ~21 m, a packed GA spot a few metres — each
/// stand states its own scale, and a metre constant loose enough for the first is loose
/// enough to pick the wrong one of the last. Measured against a live EDDB arrival and
/// the owner's fs2024 navdata: of 139 spots exactly ONE contained SayIntentions' point,
/// and it was the correct stand (18.9 m from its centre against a 71 ft = 21.6 m radius;
/// the runner-up sat 47.5 m out, well outside its own 21.6 m).
///
/// The point is the NOSE-STOP position, not the stand datum. At EDDB it sat 18.9 m from
/// the navdata centre on bearing 68.6° against a stand heading of 68.8° — i.e. straight
/// out along the stand's own axis, exactly the distinction CLAUDE.md already records for
/// GSX stop positions ("a VDGS nose-stop reference, not an aircraft-datum location"). So
/// the point is EXPECTED to sit off-centre by most of the stand's radius, and a tighter
/// "near the centre" test would reject the very stand it is standing on.
/// </summary>
public static class SayIntentionsGatePositionMatcher
{
    /// <summary>
    /// Distance sanity backstop, in metres. This is NOT the discriminator — containment
    /// in the stand's own radius is — it is the guard for pathological navdata: a spot
    /// whose stored radius is absurd (a whole-apron polygon recorded as one stand) would
    /// otherwise swallow a point hundreds of metres away and hand the pilot a stand they
    /// are nowhere near. It plays exactly the role <see cref="GateAliasResolver"/>'s
    /// 150 m backstop plays for gate aliases: past this, a match is a data error rather
    /// than a stand.
    /// </summary>
    public const double MaxMatchMetres = 150.0;

    /// <summary>The label of the stand whose own circle contains the published point and
    /// whose centre is nearest to it, or null when the point falls inside no stand — in
    /// which case the caller must keep failing rather than route somewhere plausible.
    /// A candidate with no usable radius is skipped: it states no scale, and accepting it
    /// would need the hand-tuned metre constant this test exists to avoid.</summary>
    public static string? Match(
        IReadOnlyList<GatePositionCandidate> candidates, double latitude, double longitude)
    {
        if (candidates == null || candidates.Count == 0) return null;

        string? best = null;
        double bestMetres = double.MaxValue;

        foreach (var candidate in candidates)
        {
            if (candidate.RadiusMetres <= 0) continue;

            double metres = TaxiGeo.HaversineMeters(
                candidate.Latitude, candidate.Longitude, latitude, longitude);
            if (metres > Math.Min(candidate.RadiusMetres, MaxMatchMetres)) continue;

            // Strict <, so an exact tie keeps the EARLIER candidate and the same keypress
            // always yields the same stand. Two spots at identical centre distance are
            // nearly always ONE piece of pavement listed twice under variant names
            // (C16 alongside C16S, a stand and its stub), so either label taxis the pilot
            // to the same place — what would actually hurt is the answer changing between
            // presses, or between navdata imports.
            if (metres < bestMetres)
            {
                bestMetres = metres;
                best = candidate.Label;
            }
        }

        return best;
    }
}
