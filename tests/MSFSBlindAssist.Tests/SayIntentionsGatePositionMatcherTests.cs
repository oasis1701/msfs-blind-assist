// Characterization for matching SayIntentions' published gate COORDINATE against the
// stands the Taxi Guidance form offers — the fallback for when its published gate NAME
// names a stand this scenery does not have.
//
// What that failure costs is the reason the fallback exists. A name miss does not stop
// the import: destination resolution runs its whole candidate chain and takes the last
// thing it has, the ARRIVAL RUNWAY, so a just-landed aircraft is routed at the runway it
// landed on with the taxiway half of the route perfectly correct. Nothing in the
// announcement sounds wrong.
//
// A stand is ADMISSIBLE when the point falls within a multiple of its own radius, and the
// NEAREST admissible stand wins. These tests pin that shape rather than a tuned number:
// a stand states its own scale, so a Gate Extra gets ~50 m of radius and a packed GA spot
// a few metres, where any single metre constant is either too tight for the first or too
// loose for the second.
//
// The multiple is 2.0 and it is calibrated on TWO real arrivals — the EDDB and KDTW blocks
// at the foot of this file, which are also what would have to be re-checked if a third
// ever disagrees. It started at plain containment, which the KDTW capture disproved: the
// published point is the NOSE-STOP, its offset scales with the parked aircraft rather than
// with the navdata radius, and at KDTW it sat 30.1 m out from a stand with a 22.9 m radius.

using MSFSBlindAssist.Services.SayIntentions;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsGatePositionMatcherTests
{
    // A published point somewhere unremarkable, and stands placed at exact distances from
    // it. A pure-latitude offset makes the haversine distance independent of the longitude
    // terms, so "metresAway" is the centre distance to within a millimetre — the assertions
    // below are about containment, and a fuzzy distance would blur what is being tested.
    private const double PointLat = 52.0;
    private const double PointLon = 13.0;
    private const double MetresPerDegreeLatitude = 111194.93; // TaxiGeo's mean-Earth radius

    private static GatePositionCandidate Stand(string label, double metresAway, double radiusMetres)
        => new(label, PointLat + metresAway / MetresPerDegreeLatitude, PointLon, radiusMetres);

    private static string? Match(params GatePositionCandidate[] candidates)
        => SayIntentionsGatePositionMatcher.Match(candidates, PointLat, PointLon);

    [Fact]
    public void The_nearest_stand_loses_to_one_whose_own_scale_reaches_the_point()
    {
        // This is the whole design in one case. Nearest-centre alone picks the GA spot,
        // four times outside anything it could hold; the radius test picks the wide stand
        // the aircraft is actually parked on. Every stand carries its own scale, so no
        // metre constant can express this — the one that admits the 40 m stand at 30 m
        // admits the 5 m one six times over.
        Assert.Equal(
            "Gate Extra",
            Match(Stand("GA spot", metresAway: 20, radiusMetres: 5),
                  Stand("Gate Extra", metresAway: 30, radiusMetres: 40)));
    }

    [Fact]
    public void A_point_no_stands_tolerance_reaches_matches_nothing()
    {
        // Silence is the required answer. The caller keeps failing and the pilot hears
        // that the destination is not set, which is recoverable; routing them to a
        // plausible-sounding stand they were never assigned is not.
        //
        // 47 m against a 21.6 m radius is EDDB's real runner-up (47.5 m, 71 ft), the
        // stand the doubled radius still has to exclude — 21.6 x 2 = 43.2. Loosening the
        // factor past ~2.2 puts it back in contention with the correct stand.
        Assert.Null(
            Match(Stand("B 5", metresAway: 47, radiusMetres: 21.6),
                  Stand("B 6", metresAway: 60, radiusMetres: 21.6)));
    }

    [Fact]
    public void A_stand_just_inside_twice_its_radius_answers_and_just_outside_does_not()
    {
        // The factor itself, isolated from any airport. Either side of 2 x 10 m, 10 cm
        // out — the fixture places a stand to within a millimetre, so the gap is real
        // without asserting the boundary to the metre.
        Assert.Equal("edge", Match(Stand("edge", metresAway: 19.9, radiusMetres: 10)));
        Assert.Null(Match(Stand("edge", metresAway: 20.1, radiusMetres: 10)));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void A_stand_with_no_radius_is_skipped(double radiusMetres)
    {
        // A spot that states no size states no scale, and the only way to accept it would
        // be the hand-tuned metre constant containment exists to avoid. It sits 1 m from
        // the point here — as close as a match ever gets — and still loses.
        Assert.Null(Match(Stand("no size", metresAway: 1, radiusMetres: radiusMetres)));
    }

    [Fact]
    public void ANaNRadiusCandidateNeverMatches()
    {
        // NaN slides through both raw comparisons in the guard: `NaN <= 0` is false (so
        // the no-radius skip missed it) and `distance > NaN * factor` is also false (so
        // the tolerance check admitted it at ANY distance). A corrupt-radius stand would
        // therefore have matched every point on Earth.
        var candidates = new[] { new GatePositionCandidate("NaN stand", 52.0, 13.0, double.NaN) };
        Assert.Null(SayIntentionsGatePositionMatcher.Match(candidates, 52.0, 13.0));
    }

    [Fact]
    public void The_backstop_rejects_a_distant_stand_with_an_absurd_radius()
    {
        // Not the discriminator — the guard for pathological navdata, where a whole apron
        // has been recorded as one stand. A 500 m circle would otherwise swallow a point
        // 200 m away and hand the pilot somewhere they are nowhere near.
        Assert.Null(Match(Stand("whole apron", metresAway: 200, radiusMetres: 500)));
    }

    [Fact]
    public void Inside_the_backstop_an_oversized_stand_still_matches()
    {
        // The other side of the same guard: 150 m is a sanity limit on the data, not a
        // second opinion about the stand. Within it, the stand's own radius decides.
        Assert.Equal(
            "whole apron",
            Match(Stand("whole apron", metresAway: 140, radiusMetres: 500)));
    }

    [Fact]
    public void An_empty_candidate_list_matches_nothing()
    {
        Assert.Null(SayIntentionsGatePositionMatcher.Match(
            Array.Empty<GatePositionCandidate>(), PointLat, PointLon));
    }

    [Fact]
    public void The_nearest_of_several_admissible_stands_wins()
    {
        // Overlapping tolerances are ordinary on a pier of wide stands — and more so now
        // that each reaches twice its radius — so admissibility alone does not settle it;
        // the nearest centre does.
        Assert.Equal(
            "B 6",
            Match(Stand("B 4", metresAway: 40, radiusMetres: 50),
                  Stand("B 6", metresAway: 10, radiusMetres: 50),
                  Stand("B 8", metresAway: 25, radiusMetres: 50)));
    }

    [Fact]
    public void An_exact_tie_keeps_the_earlier_candidate()
    {
        // Two spots at one centre are nearly always ONE piece of pavement listed twice
        // under variant names, so either label taxis the pilot to the same place. What
        // would hurt is the answer changing between keypresses or between navdata
        // imports, so the tie resolves on list order and nothing else.
        var first = new GatePositionCandidate("C16", PointLat, PointLon, 30);
        var second = new GatePositionCandidate("C16S", PointLat, PointLon, 30);

        Assert.Equal("C16", Match(first, second));
        Assert.Equal("C16S", Match(second, first));
    }

    // --- The live EDDB arrival --------------------------------------------------------
    //
    // Measured 2026-07-30 against the owner's fs2024.sqlite. SayIntentions assigned
    // "Gate B06"; navdata stores that stand as name='GB', number=6, which the form
    // renders "B 6", so the NAME match had already failed and the chain was one candidate
    // away from taking the arrival runway.
    //
    // The published point is the NOSE-STOP position, not the stand datum: 18.9 m out from
    // the spot centre on bearing 68.6° against a stand heading of 68.8°, i.e. straight
    // along the stand's own axis — the same distinction CLAUDE.md records for GSX stop
    // positions. So it is EXPECTED to sit off-centre by most of the radius, and a "near
    // the centre" test would reject the stand the aircraft is standing on.
    //
    // Of the airport's 139 spots exactly one CONTAINED the point, and it was the right
    // one. That is no longer the property being relied on — doubled, the wide B 7A is
    // admissible too and loses on centre distance instead — but the margin it measures
    // is: the correct stand is nearest by more than 2.5x. Only the assigned stand's
    // coordinates are reproduced here; the two nearest others were measured as centre
    // DISTANCES from the published point (47.5 m and 65.1 m), so they are placed due
    // north of it — a radius test does not care about bearing.

    private const double EddbPointLat = 52.3647127959562;   // assigned_gate_lat, published as a string
    private const double EddbPointLon = 13.5055538061652;   // assigned_gate_lon
    private const double FeetToMetres = 0.3048;

    private static GatePositionCandidate EddbStandNorthOfPoint(
        string label, double metresAway, double radiusFeet)
        => new(label,
               EddbPointLat + metresAway / MetresPerDegreeLatitude,
               EddbPointLon,
               radiusFeet * FeetToMetres);

    // B 6 as navdata actually stores it. B 5 and B 7A at their measured distances.
    private static readonly GatePositionCandidate EddbB6 =
        new("B 6", 52.3646507, 13.5052948, 71 * FeetToMetres);
    private static readonly GatePositionCandidate EddbB5 =
        EddbStandNorthOfPoint("B 5", metresAway: 47.5, radiusFeet: 71);
    private static readonly GatePositionCandidate EddbB7A =
        EddbStandNorthOfPoint("B 7A", metresAway: 65.1, radiusFeet: 164);

    [Fact]
    public void The_live_EDDB_point_seats_the_stand_SayIntentions_assigned()
    {
        Assert.Equal(
            "B 6",
            SayIntentionsGatePositionMatcher.Match(
                new[] { EddbB5, EddbB6, EddbB7A }, EddbPointLat, EddbPointLon));
    }

    [Fact]
    public void At_EDDB_the_runner_up_is_inadmissible_and_the_wider_neighbour_loses_on_distance()
    {
        // Each stand alone, so the nearest-centre rule cannot mask which of them qualified
        // at all — the two halves of the answer fail differently and both matter.
        //
        // B 5 is the one the tolerance has to keep out: 47.5 m against 21.6 x 2 = 43.2.
        // B 7A now gets IN (65.1 m against 50 x 2 = 100) where containment kept it out,
        // and that is fine — it loses to B 6 by 46 m of centre distance. "Exactly one of
        // 139 spots" has become "nearest among the admissible", which is why the combined
        // case below is the one that matters.
        Assert.Equal("B 6", SayIntentionsGatePositionMatcher.Match(
            new[] { EddbB6 }, EddbPointLat, EddbPointLon));
        Assert.Null(SayIntentionsGatePositionMatcher.Match(
            new[] { EddbB5 }, EddbPointLat, EddbPointLon));
        Assert.Equal("B 7A", SayIntentionsGatePositionMatcher.Match(
            new[] { EddbB7A }, EddbPointLat, EddbPointLon));

        Assert.Equal("B 6", SayIntentionsGatePositionMatcher.Match(
            new[] { EddbB7A, EddbB5, EddbB6 }, EddbPointLat, EddbPointLon));
    }

    [Fact]
    public void The_live_EDDB_point_sits_along_the_stand_axis_not_at_its_centre()
    {
        // Pins the nose-stop offset the design rests on. Should a later capture put the
        // point at the datum instead, this is the test that says so — and the radius
        // margin above is what would have to be re-checked.
        Assert.Equal(18.9, TaxiGeo.HaversineMeters(
            EddbB6.Latitude, EddbB6.Longitude, EddbPointLat, EddbPointLon), 1);
        Assert.Equal(68.6, TaxiGeo.BearingDeg(
            EddbB6.Latitude, EddbB6.Longitude, EddbPointLat, EddbPointLon), 1);
    }

    [Fact]
    public void Feet_read_as_metres_dissolve_the_margin_the_EDDB_match_rests_on()
    {
        // Why the caller converts by SOURCE before building a candidate: a navdata radius
        // is FEET and a GSX one metres, the mix-up ParkingSpot.FitsAircraft already
        // records. Left raw, every tolerance is 3.28 times too wide — and the stand this
        // is supposed to exclude, the 47.5 m runner-up, is admitted along with everything
        // else. On a pier where the inflated neighbour is the NEARER of the two it then
        // wins outright, so the right answer would be luck rather than discrimination.
        Assert.Null(SayIntentionsGatePositionMatcher.Match(
            new[] { EddbB5 }, EddbPointLat, EddbPointLon));
        Assert.NotNull(SayIntentionsGatePositionMatcher.Match(
            new[] { EddbB5 with { RadiusMetres = 71 } }, EddbPointLat, EddbPointLon));

        Assert.NotNull(SayIntentionsGatePositionMatcher.Match(
            new[] { EddbB6 with { RadiusMetres = 71 } }, EddbPointLat, EddbPointLon));
        Assert.NotNull(SayIntentionsGatePositionMatcher.Match(
            new[] { EddbB7A with { RadiusMetres = 164 } }, EddbPointLat, EddbPointLon));
    }

    // --- The live KDTW arrival --------------------------------------------------------
    //
    // Measured 2026-07-31. SayIntentions assigned "South Terminal Gate A24" with
    // assigned_gate_lat/lon below; KDTW's scenery calls that stand A24A (navdata parking
    // name='GA', number=24, suffix='A'), so the NAME failed and the coordinate was asked
    // instead — and CONTAINMENT dropped it too, because the point sits 30.1 m out from a
    // stand of 75 ft = 22.9 m radius. Destination resolution then ran its whole chain and
    // took the ARRIVAL RUNWAY: a landed aircraft routed at 04L, along exactly the
    // A5/A/R the controller had given for the gate.
    //
    // This is the capture that turned containment into a radius MULTIPLE. The offset is
    // the nose-stop, which scales with the parked aircraft rather than with the navdata
    // radius, so how much of the radius it eats is not a property of the stand at all.
    // The neighbours were measured as centre distances (75.0 m and 75.9 m) and are placed
    // due north of the point, as in the EDDB block — a radius test ignores bearing. Radii
    // are in metres here because that is what the caller converts navdata's feet to.

    private const double KdtwPointLat = 42.2052647490552;    // assigned_gate_lat
    private const double KdtwPointLon = -83.3606651929504;   // assigned_gate_lon
    private const double KdtwA24ARadiusMetres = 22.9;        // 75 ft

    private static GatePositionCandidate KdtwStandNorthOfPoint(
        string label, double metresAway, double radiusMetres)
        => new(label,
               KdtwPointLat + metresAway / MetresPerDegreeLatitude,
               KdtwPointLon,
               radiusMetres);

    private static readonly GatePositionCandidate KdtwA24A =
        KdtwStandNorthOfPoint("A 24A", metresAway: 30.1, radiusMetres: KdtwA24ARadiusMetres);
    private static readonly GatePositionCandidate KdtwA21A =
        KdtwStandNorthOfPoint("A 21A", metresAway: 75.0, radiusMetres: 14.0);
    private static readonly GatePositionCandidate KdtwA28A =
        KdtwStandNorthOfPoint("A 28A", metresAway: 75.9, radiusMetres: 22.9);

    [Fact]
    public void The_live_KDTW_point_seats_the_stand_containment_dropped()
    {
        Assert.Equal(
            "A 24A",
            SayIntentionsGatePositionMatcher.Match(
                new[] { KdtwA21A, KdtwA24A, KdtwA28A }, KdtwPointLat, KdtwPointLon));
    }

    [Fact]
    public void The_KDTW_point_lies_outside_the_stands_own_radius_and_inside_twice_it()
    {
        // The measurement the factor is calibrated on, stated as the arithmetic rather
        // than as an outcome: this is what a third capture would have to contradict.
        const double outMetres = 30.1;

        Assert.True(outMetres > KdtwA24ARadiusMetres);
        Assert.True(outMetres <= KdtwA24ARadiusMetres
                                 * SayIntentionsGatePositionMatcher.NoseStopRadiusFactor);
    }

    [Fact]
    public void Neither_KDTW_neighbour_is_admissible_at_all()
    {
        // The other half of the margin: at 75 m both are more than twice their own
        // radius away, so the doubled tolerance buys them nothing. Tested one at a time
        // so nearest-centre cannot hide a neighbour that qualified.
        Assert.Equal("A 24A", SayIntentionsGatePositionMatcher.Match(
            new[] { KdtwA24A }, KdtwPointLat, KdtwPointLon));
        Assert.Null(SayIntentionsGatePositionMatcher.Match(
            new[] { KdtwA21A }, KdtwPointLat, KdtwPointLon));
        Assert.Null(SayIntentionsGatePositionMatcher.Match(
            new[] { KdtwA28A }, KdtwPointLat, KdtwPointLon));
    }
}
