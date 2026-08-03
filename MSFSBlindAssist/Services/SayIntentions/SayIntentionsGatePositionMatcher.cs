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
/// THE ACCEPTANCE TEST IS A MULTIPLE OF THE STAND'S OWN RADIUS, and the winner is the
/// NEAREST stand that passes it. A radius multiple rather than a metre constant is what
/// keeps it self-scaling: a Gate Extra states ~50 m of scale, a medium gate ~21 m, a
/// packed GA spot a few metres, and any single metre constant loose enough for the first
/// is loose enough to pick the wrong one of the last.
///
/// The point is the NOSE-STOP position, not the stand datum. At EDDB it sat 18.9 m from
/// the navdata centre on bearing 68.6° against a stand heading of 68.8° — i.e. straight
/// out along the stand's own axis, exactly the distinction CLAUDE.md already records for
/// GSX stop positions ("a VDGS nose-stop reference, not an aircraft-datum location"). So
/// the point is EXPECTED to sit off-centre, and a tighter "near the centre" test would
/// reject the very stand the aircraft is standing on.
/// </summary>
public static class SayIntentionsGatePositionMatcher
{
    /// <summary>
    /// Distance sanity backstop, in metres. This is NOT the discriminator — the stand's
    /// own radius is — it is the guard for pathological navdata: a spot whose stored
    /// radius is absurd (a whole-apron polygon recorded as one stand) would otherwise
    /// swallow a point hundreds of metres away and hand the pilot a stand they are
    /// nowhere near. It plays exactly the role <see cref="GateAliasResolver"/>'s 150 m
    /// backstop plays for gate aliases: past this, a match is a data error rather than
    /// a stand.
    /// </summary>
    public const double MaxMatchMetres = 150.0;

    /// <summary>
    /// How far past its own radius a stand still answers for the published point.
    ///
    /// This started as plain containment, and a second live arrival disproved it. The
    /// offset is the NOSE-STOP: it scales with the parked AIRCRAFT, not with whatever
    /// radius the navdata records for the spot, so how much of the radius it eats is a
    /// property of the airframe the stand was drawn for, not of the stand.
    ///
    ///   EDDB 2026-07-30   GB 6    18.9 m out,  radius 21.6 m (71 ft)  — contained
    ///   KDTW 2026-07-31   GA 24A  30.1 m out,  radius 22.9 m (75 ft)  — NOT contained
    ///
    /// Containment therefore dropped the KDTW stand, and destination resolution ran its
    /// whole chain to the ARRIVAL RUNWAY with the taxiway half of the import perfect —
    /// exactly the failure this matcher exists to stop, reached by being too strict
    /// rather than by having no test at all.
    ///
    /// What both captures DO agree on is the margin: the correct stand is the nearest by
    /// roughly 2.5× (EDDB 18.9 m against 47.5 m, KDTW 30.1 m against 75.0 m). So the
    /// radius sets who is ADMISSIBLE and centre distance picks the winner among them.
    /// 2.0 admits both correct stands and neither runner-up (EDDB 21.6×2 = 43.2 < 47.5;
    /// KDTW 22.9×2 = 45.8 < 75.0).
    ///
    /// TWO arrivals is what this is calibrated on, and it is the number to re-check the
    /// moment a third disagrees — a stand that should seat and does not, or a neighbour
    /// that wins. It stays a multiple rather than a metre constant because the multiple
    /// still self-scales: a Gate Extra tolerates ~100 m where a GA spot tolerates ten.
    /// </summary>
    public const double NoseStopRadiusFactor = 2.0;

    /// <summary>The label of the stand NEAREST the published point among those whose own
    /// radius (times <see cref="NoseStopRadiusFactor"/>, capped by
    /// <see cref="MaxMatchMetres"/>) reaches it, or null when no stand qualifies — in
    /// which case the caller must keep failing rather than route somewhere plausible.
    /// A candidate with no usable radius is skipped: it states no scale, and accepting it
    /// would need the hand-tuned metre constant this test exists to avoid.
    ///
    /// NEAREST-AMONG-ADMISSIBLE is the property, not "exactly one stand qualifies". Under
    /// plain containment the live EDDB point fell inside exactly 1 of the airport's 139
    /// spots; doubled, its wide neighbour GB 7A (65.1 m out, 50 m radius) is admissible
    /// too and loses on centre distance instead. Overlapping tolerances are ordinary on a
    /// pier, so the second half of the rule carries as much weight as the first.</summary>
    public static string? Match(
        IReadOnlyList<GatePositionCandidate> candidates, double latitude, double longitude)
    {
        if (candidates == null || candidates.Count == 0) return null;

        string? best = null;
        double bestMetres = double.MaxValue;

        foreach (var candidate in candidates)
        {
            // A candidate with no usable radius states no scale; NaN additionally slides
            // through every comparison below (NaN <= 0 and NaN > x are both false), which
            // made a corrupt-radius stand admissible AT ANY DISTANCE.
            if (candidate.RadiusMetres <= 0 || double.IsNaN(candidate.RadiusMetres)) continue;

            double metres = TaxiGeo.HaversineMeters(
                candidate.Latitude, candidate.Longitude, latitude, longitude);
            if (double.IsNaN(metres)) continue;
            if (metres > Math.Min(candidate.RadiusMetres * NoseStopRadiusFactor, MaxMatchMetres))
                continue;

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
