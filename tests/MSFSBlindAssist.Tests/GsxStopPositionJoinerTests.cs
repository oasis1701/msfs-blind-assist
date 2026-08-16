// Tests for MSFSBlindAssist.Services.Gsx.Remote.GsxStopPositionJoiner -- the SAFETY-CRITICAL
// join that fills a Remote-API-sourced ParkingSpot's StopLatitude/StopLongitude/StopHeading
// from the matching GSX .ini gate's parkingsystem_stopposition. Docking guidance parks the
// aircraft on whatever this join produces, so the sharpest tests here exist to catch exactly
// one class of bug: the parking position (lat/lon) leaking into the stop fields, or the stop
// fields leaking back into the parking position. The two points are ~11.62 m apart at the
// real KJFK gate used below -- far outside docking's 0.3 m StopToleranceMetres.
//
// No .ini fixture is committed to this repo (none existed before this task), so every GsxGate
// below is a hand-authored model object built in code -- never a fixture edit. The golden case
// uses the REAL KJFK "[gate a 6]" numbers from the design spec/task brief (verified live
// capture, 2026-08-12), paired against the real, already-committed API fixture
// (Fixtures/gsx-handlerdata-parkings-kjfk.json, from Task 3) for the API side.

using System.Text.Json;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

public class GsxStopPositionJoinerTests
{
    private static JsonElement KjfkFixture()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gsx-handlerdata-parkings-kjfk.json"));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static ParkingSpot Spot(double lat, double lon, double heading = 90.0, string? gsxId = "Gate Test")
        => new ParkingSpot
        {
            AirportICAO = "TEST",
            Name = "Test Terminal",
            GsxIdentifier = gsxId,
            Latitude = lat,
            Longitude = lon,
            Heading = heading,
            Source = GateSource.Gsx,
        };

    private static GsxGate IniGate(
        double lat, double lon, double heading = 90.0,
        double? stopLat = null, double? stopLon = null, double? stopHeading = null,
        bool hasParkingPos = true, string rawSectionName = "gate test 1")
        => new GsxGate
        {
            RawSectionName = rawSectionName,
            HasParkingPos = hasParkingPos,
            Latitude = lat,
            Longitude = lon,
            Heading = heading,
            StopLatitude = stopLat,
            StopLongitude = stopLon,
            StopHeading = stopHeading,
        };

    // ── The golden case: real KJFK "[gate a 6]" numbers ─────────────────────
    // Spec / task brief: API lat 40.6421016650217 / lon -73.7787394243692 / heading
    // 26.3036148834228, verified byte-identical to the .ini's this_parking_pos. The .ini's
    // parkingsystem_stopposition for the SAME gate is lat 40.6421951021146 /
    // lon -73.7786780495867 / heading 26.3036148834228 -- 11.62 m away from this_parking_pos.
    // This is the test that would catch the catastrophic version of this bug: substituting the
    // parking position for the stop.

    [Fact]
    public void Golden_KJFK_gate_a_6_joins_the_real_stop_position_not_lat_lon()
    {
        var apiSpot = GsxRemoteParkingReader.Read(KjfkFixture(), "KJFK")
            .Single(s => s.GsxIdentifier == "Gate 6" && s.TerminalName == "Terminal 4 - Concourse A");

        // Sanity: confirm the committed fixture really is the real, documented KJFK capture
        // before trusting anything else this test does.
        Assert.Equal(40.6421016650217, apiSpot.Latitude);
        Assert.Equal(-73.7787394243692, apiSpot.Longitude);
        Assert.Equal(26.3036148834228, apiSpot.Heading);

        // Hand-authored .ini model for "[gate a 6]" -- derives its this_parking_pos lat/lon
        // FROM apiSpot rather than retyping the literal, so this test cannot pass merely
        // because two independently-typed literals happened to parse to the same double; the
        // sanity asserts above are what pin the literal itself.
        var iniGate = new GsxGate
        {
            Category = "gate",
            Concourse = "A",
            Number = 6,
            RawSectionName = "gate a 6",
            HasParkingPos = true,
            Latitude = apiSpot.Latitude,
            Longitude = apiSpot.Longitude,
            Heading = apiSpot.Heading,
            StopLatitude = 40.6421951021146,
            StopLongitude = -73.7786780495867,
            StopHeading = 26.3036148834228,
        };

        var result = GsxStopPositionJoiner.Join(new List<ParkingSpot> { apiSpot }, new List<GsxGate> { iniGate });
        var joined = Assert.Single(result);

        Assert.Equal(40.6421951021146, joined.StopLatitude);
        Assert.Equal(-73.7786780495867, joined.StopLongitude);
        Assert.Equal(26.3036148834228, joined.StopHeading);

        // The actual catastrophic bug this test exists to catch.
        Assert.NotEqual(joined.Latitude, joined.StopLatitude);
        Assert.NotEqual(joined.Longitude, joined.StopLongitude);

        // The spot's own displayed/taxi position must be completely untouched by this join.
        Assert.Equal(40.6421016650217, joined.Latitude);
        Assert.Equal(-73.7787394243692, joined.Longitude);
        Assert.Equal(26.3036148834228, joined.Heading); // already usable -- unchanged
    }

    // ── Degradation: no .ini, no match, or no stop -> stop stays null ───────

    [Fact]
    public void No_matching_ini_gate_leaves_stop_null()
    {
        var spot = Spot(10.0, 20.0);
        var gates = new List<GsxGate> { IniGate(50.0, 60.0, stopLat: 51.0, stopLon: 61.0, stopHeading: 90.0) };

        var joined = Assert.Single(GsxStopPositionJoiner.Join(new List<ParkingSpot> { spot }, gates));

        Assert.Null(joined.StopLatitude);
        Assert.Null(joined.StopLongitude);
        Assert.Null(joined.StopHeading);
    }

    [Fact]
    public void Matched_ini_gate_with_no_stop_position_leaves_stop_null()
    {
        var spot = Spot(10.0, 20.0);
        // Exact coordinate match, but this .ini section never had a parkingsystem_stopposition
        // line (StopLatitude/Longitude/Heading all null) -- a real, common case (KJFK: 227/231
        // gates have one, so 4 do not).
        var gates = new List<GsxGate> { IniGate(10.0, 20.0) };

        var joined = Assert.Single(GsxStopPositionJoiner.Join(new List<ParkingSpot> { spot }, gates));

        Assert.Null(joined.StopLatitude);
        Assert.Null(joined.StopLongitude);
        Assert.Null(joined.StopHeading);
    }

    [Fact]
    public void Empty_ini_list_leaves_every_stop_null_and_does_not_throw()
    {
        var spots = new List<ParkingSpot> { Spot(10.0, 20.0), Spot(30.0, 40.0) };

        var result = GsxStopPositionJoiner.Join(spots, new List<GsxGate>());

        Assert.Equal(2, result.Count);
        Assert.All(result, s => Assert.Null(s.StopLatitude));
    }

    [Fact]
    public void Null_ini_list_leaves_every_stop_null_and_does_not_throw()
    {
        var spots = new List<ParkingSpot> { Spot(10.0, 20.0) };

        var result = GsxStopPositionJoiner.Join(spots, null);

        var joined = Assert.Single(result);
        Assert.Null(joined.StopLatitude);
    }

    [Fact]
    public void Null_api_spots_returns_empty_list_and_does_not_throw()
    {
        var result = GsxStopPositionJoiner.Join(null, new List<GsxGate> { IniGate(10.0, 20.0) });

        Assert.Empty(result);
    }

    [Fact]
    public void A_gate_with_no_this_parking_pos_is_never_a_join_candidate_even_at_null_island()
    {
        // GsxGate.Latitude/Longitude default to 0/0 when HasParkingPos is false (no
        // this_parking_pos line was present in that .ini section) -- (0,0) must never be
        // treated as a real coordinate to match against, or a spot that itself happens to sit
        // near (0,0) could spuriously pick up a stop position from an unrelated, position-less
        // .ini section.
        var spot = Spot(0.0, 0.0);
        var gates = new List<GsxGate>
        {
            IniGate(0.0, 0.0, hasParkingPos: false, stopLat: 1.0, stopLon: 2.0, stopHeading: 3.0),
        };

        var joined = Assert.Single(GsxStopPositionJoiner.Join(new List<ParkingSpot> { spot }, gates));

        Assert.Null(joined.StopLatitude);
    }

    // ── Exact vs. tolerance matching ─────────────────────────────────────────

    [Fact]
    public void Exact_match_is_preferred_over_a_tolerance_match_when_both_are_possible()
    {
        var spot = Spot(10.0, 20.0);
        var gates = new List<GsxGate>
        {
            // Within tolerance (deltas ~5e-7 deg, well under the 1e-6 deg fallback) but NOT
            // exact -- carries an obviously-wrong sentinel stop. Placed FIRST in the list so a
            // buggy "stop at the first close-enough candidate" implementation would pick this
            // one instead of scanning the whole list for an exact match first.
            IniGate(10.0000005, 20.0000003, stopLat: 999.0, stopLon: 999.0, stopHeading: 999.0, rawSectionName: "wrong close gate"),
            // Exact match -- the correct candidate.
            IniGate(10.0, 20.0, stopLat: 55.0, stopLon: 65.0, stopHeading: 75.0, rawSectionName: "gate test 1"),
        };

        var joined = Assert.Single(GsxStopPositionJoiner.Join(new List<ParkingSpot> { spot }, gates));

        Assert.Equal(55.0, joined.StopLatitude);
        Assert.Equal(65.0, joined.StopLongitude);
        Assert.Equal(75.0, joined.StopHeading);
    }

    // ── Ambiguous exact match (2026-08-14 review fix, I2) ────────────────────
    // Two distinct .ini sections publishing the literal same this_parking_pos -- e.g. a
    // copy-pasted section with an unedited this_parking_pos but an edited
    // parkingsystem_stopposition. FindExact used to return the first in list order with no
    // diagnostic; that is the one path in this file that could attach a genuinely WRONG stop
    // (from the other, un-picked gate) with nothing to reveal it happened. The fix refuses to
    // guess: it leaves the stop -- and any recoverable NaN heading -- untouched, the exact same
    // degrade as no match at all.

    [Fact]
    public void Ambiguous_exact_match_leaves_stop_and_heading_untouched()
    {
        var spot = Spot(10.0, 20.0, heading: double.NaN);
        var gates = new List<GsxGate>
        {
            IniGate(10.0, 20.0, heading: 111.0, stopLat: 11.0, stopLon: 21.0, stopHeading: 31.0, rawSectionName: "gate dup 1"),
            IniGate(10.0, 20.0, heading: 222.0, stopLat: 12.0, stopLon: 22.0, stopHeading: 32.0, rawSectionName: "gate dup 2"),
        };

        var joined = Assert.Single(GsxStopPositionJoiner.Join(new List<ParkingSpot> { spot }, gates));

        // Neither candidate's stop is picked -- not "gate dup 1"'s (11.0/21.0/31.0), not
        // "gate dup 2"'s (12.0/22.0/32.0). Both would be a silent wrong-stand-stop bug.
        Assert.Null(joined.StopLatitude);
        Assert.Null(joined.StopLongitude);
        Assert.Null(joined.StopHeading);

        // Heading recovery is refused too -- "same degrade as no match" means BOTH, not just
        // the stop. Neither candidate's heading (111.0 / 222.0) may be picked either.
        Assert.True(double.IsNaN(joined.Heading));
        Assert.False(GsxRemoteParkingReader.HasUsableHeading(joined));
    }

    [Fact]
    public void Tolerance_match_succeeds_when_exact_fails()
    {
        // 5e-7 deg on each axis: inside the 1e-6 deg fallback tolerance, but not bit-identical,
        // so FindExact must fail before the tolerance fallback even runs.
        var spot = Spot(10.0, 20.0);
        var gates = new List<GsxGate>
        {
            IniGate(10.0000005, 19.9999995, stopLat: 11.0, stopLon: 21.0, stopHeading: 123.0),
        };

        var joined = Assert.Single(GsxStopPositionJoiner.Join(new List<ParkingSpot> { spot }, gates));

        Assert.Equal(11.0, joined.StopLatitude);
        Assert.Equal(21.0, joined.StopLongitude);
        Assert.Equal(123.0, joined.StopHeading);

        // Sharpest possible check for lat/lon leakage: in this scenario the .ini gate's OWN
        // this_parking_pos (10.0000005, 19.9999995) genuinely differs from the spot's API
        // position (10.0, 20.0) by a tiny but nonzero amount. If the join ever assigned
        // spot.Latitude/Longitude = match.Latitude/Longitude (instead of only ever touching the
        // Stop* fields), this would be the one test shape where that mistake is observable --
        // the golden/exact-match tests can't catch it because there lat/lon are identical to
        // begin with, so an accidental overwrite would be silently unobservable there.
        Assert.Equal(10.0, joined.Latitude);
        Assert.Equal(20.0, joined.Longitude);

        // The tolerance-fallback branch must log every hit (Log.Warn, category "Gsx") so a
        // systematic fallback -- meaning the exact-coordinate-identity assumption this whole
        // join rests on has broken -- is visible rather than silently absorbed. There is no
        // test-observable seam for MSFSBlindAssist.Utils.Logging.Log anywhere in this codebase
        // (repo-wide search turned up zero tests asserting on Log output), so this call is
        // verified by code inspection/self-review, not by an automated assertion here -- the
        // correctness of the JOIN on this path is what the asserts above cover.
    }

    [Fact]
    public void Nearest_within_tolerance_wins_over_a_farther_first_listed_candidate()
    {
        // (2026-08-14 review fix, I3.) Every OTHER tolerance test has exactly one candidate
        // inside the box, so a regression from "nearest wins" back to "first candidate within
        // tolerance wins" -- precisely the mistake FindNearestWithinTolerance's own doc comment
        // says it avoids -- would still pass every one of them. This is the one test that
        // cannot: two candidates both fall inside the 1e-6 deg box, the FARTHER one listed
        // FIRST, each carrying a distinguishable stop.
        var spot = Spot(10.0, 20.0);
        var gates = new List<GsxGate>
        {
            // Farther (delta ~9e-7 deg on each axis -- inside tolerance, close to the 1e-6
            // boundary), listed FIRST. A "first within tolerance" bug would pick this one.
            IniGate(10.0000009, 20.0000009, stopLat: 111.0, stopLon: 211.0, stopHeading: 11.0, rawSectionName: "farther gate"),
            // Nearer (delta ~2e-7 deg on each axis), listed SECOND -- the correct pick.
            IniGate(10.0000002, 20.0000002, stopLat: 222.0, stopLon: 222.0, stopHeading: 22.0, rawSectionName: "nearer gate"),
        };

        var joined = Assert.Single(GsxStopPositionJoiner.Join(new List<ParkingSpot> { spot }, gates));

        Assert.Equal(222.0, joined.StopLatitude);
        Assert.Equal(222.0, joined.StopLongitude);
        Assert.Equal(22.0, joined.StopHeading);
    }

    [Fact]
    public void Coordinate_just_outside_tolerance_does_not_match()
    {
        // 1.5e-6 deg on the latitude axis: outside the 1e-6 deg fallback tolerance.
        var spot = Spot(10.0, 20.0);
        var gates = new List<GsxGate>
        {
            IniGate(10.0000015, 20.0, stopLat: 11.0, stopLon: 21.0, stopHeading: 123.0),
        };

        var joined = Assert.Single(GsxStopPositionJoiner.Join(new List<ParkingSpot> { spot }, gates));

        Assert.Null(joined.StopLatitude);
        Assert.Null(joined.StopLongitude);
        Assert.Null(joined.StopHeading);
    }

    // ── Heading recovery (this_parking_pos -> a NaN heading only) ───────────

    [Fact]
    public void NaN_heading_is_filled_from_this_parking_pos_when_ini_matches()
    {
        var spot = Spot(10.0, 20.0, heading: double.NaN);
        var gates = new List<GsxGate> { IniGate(10.0, 20.0, heading: 271.5) };

        var joined = Assert.Single(GsxStopPositionJoiner.Join(new List<ParkingSpot> { spot }, gates));

        Assert.Equal(271.5, joined.Heading);
        Assert.True(GsxRemoteParkingReader.HasUsableHeading(joined));
    }

    [Fact]
    public void Published_heading_is_never_overwritten_by_the_ini()
    {
        var spot = Spot(10.0, 20.0, heading: 123.4); // a real, non-NaN heading the API published
        var gates = new List<GsxGate> { IniGate(10.0, 20.0, heading: 99.9) }; // deliberately different

        var joined = Assert.Single(GsxStopPositionJoiner.Join(new List<ParkingSpot> { spot }, gates));

        Assert.Equal(123.4, joined.Heading);
    }

    [Fact]
    public void NaN_heading_stays_NaN_when_no_ini_match()
    {
        var spot = Spot(10.0, 20.0, heading: double.NaN);
        var gates = new List<GsxGate> { IniGate(50.0, 60.0, heading: 271.5) }; // does not match

        var joined = Assert.Single(GsxStopPositionJoiner.Join(new List<ParkingSpot> { spot }, gates));

        Assert.True(double.IsNaN(joined.Heading));
        Assert.False(GsxRemoteParkingReader.HasUsableHeading(joined));
    }

    // ── Shape invariants ──────────────────────────────────────────────────

    [Fact]
    public void Join_preserves_order_and_count_across_multiple_spots()
    {
        var spots = new List<ParkingSpot>
        {
            Spot(10.0, 20.0, gsxId: "A"),
            Spot(30.0, 40.0, gsxId: "B"),
            Spot(50.0, 60.0, gsxId: "C"),
        };
        var gates = new List<GsxGate> { IniGate(30.0, 40.0, stopLat: 31.0, stopLon: 41.0, stopHeading: 5.0) };

        var result = GsxStopPositionJoiner.Join(spots, gates);

        Assert.Equal(3, result.Count);
        Assert.Equal("A", result[0].GsxIdentifier);
        Assert.Equal("B", result[1].GsxIdentifier);
        Assert.Equal("C", result[2].GsxIdentifier);
        Assert.Null(result[0].StopLatitude);
        Assert.Equal(31.0, result[1].StopLatitude);
        Assert.Null(result[2].StopLatitude);
    }
}
