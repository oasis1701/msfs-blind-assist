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
// The acceptance test is CONTAINMENT in the stand's own circle rather than a distance
// constant, and these tests pin that choice rather than a tuned number: a stand states
// its own scale, so a Gate Extra gets ~50 m of tolerance and a packed GA spot a few
// metres, where any single metre constant is either too tight for the first or too loose
// for the second.

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
    public void The_nearest_stand_loses_to_one_whose_own_circle_holds_the_point()
    {
        // This is the whole design in one case. Nearest-centre alone picks the GA spot,
        // which the point is nowhere near the inside of; containment picks the wide stand
        // it is actually parked on. Every stand carries its own scale, so no metre
        // constant can express this — the one that admits the 40 m stand also admits the
        // 5 m one twice over.
        Assert.Equal(
            "Gate Extra",
            Match(Stand("GA spot", metresAway: 20, radiusMetres: 5),
                  Stand("Gate Extra", metresAway: 30, radiusMetres: 40)));
    }

    [Fact]
    public void A_point_inside_no_stand_matches_nothing()
    {
        // Silence is the required answer. The caller keeps failing and the pilot hears
        // that the destination is not set, which is recoverable; routing them to a
        // plausible-sounding stand they were never assigned is not.
        Assert.Null(
            Match(Stand("B 5", metresAway: 47, radiusMetres: 21.6),
                  Stand("B 6", metresAway: 60, radiusMetres: 21.6)));
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
    public void The_nearest_of_several_containing_stands_wins()
    {
        // Overlapping circles are ordinary on a pier of wide stands, so containment alone
        // does not settle it; the nearest centre does.
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
    // Of the airport's 139 spots exactly one contained the point, and it was the right
    // one. Only the assigned stand's coordinates are reproduced here; the two nearest
    // others were measured as centre DISTANCES from the published point (47.5 m and
    // 65.1 m), so they are placed due north of it — a radius test does not care about
    // bearing.

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
    public void The_assigned_stand_is_the_only_EDDB_spot_that_contains_the_point()
    {
        // The margin is what makes containment trustworthy here rather than lucky: the
        // runner-up sits at more than twice its own radius. Tested one at a time so the
        // nearest-centre rule cannot mask a neighbour that also qualified.
        Assert.Equal("B 6", SayIntentionsGatePositionMatcher.Match(
            new[] { EddbB6 }, EddbPointLat, EddbPointLon));
        Assert.Null(SayIntentionsGatePositionMatcher.Match(
            new[] { EddbB5 }, EddbPointLat, EddbPointLon));
        Assert.Null(SayIntentionsGatePositionMatcher.Match(
            new[] { EddbB7A }, EddbPointLat, EddbPointLon));
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
        // records. Left raw, every circle is 3.28 times too wide — all three of these
        // stands then contain the point, so "exactly one of 139 spots" becomes "whichever
        // happens to be nearest", and on a pier where the inflated neighbour is the nearer
        // one it wins outright. The right answer here would be luck, not discrimination.
        Assert.NotNull(SayIntentionsGatePositionMatcher.Match(
            new[] { EddbB6 with { RadiusMetres = 71 } }, EddbPointLat, EddbPointLon));
        Assert.NotNull(SayIntentionsGatePositionMatcher.Match(
            new[] { EddbB5 with { RadiusMetres = 71 } }, EddbPointLat, EddbPointLon));
        Assert.NotNull(SayIntentionsGatePositionMatcher.Match(
            new[] { EddbB7A with { RadiusMetres = 164 } }, EddbPointLat, EddbPointLon));
    }
}
