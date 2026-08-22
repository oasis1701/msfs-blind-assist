// End-to-end characterization tests for NavigationDatabaseProvider / FlightPlanManager
// against a synthetic SQLite fixture (NavDataFixtureDb). These exercise the REAL SELECT
// statements and REAL leg-parsing/coordinate-resolution/runway-matching logic together,
// unlike NavigationDatabaseProviderPureLogicTests which calls the promoted static helpers
// directly. Expected values were captured from actual method output; if a test fails
// against unchanged production code, the fixture rows or expectation are wrong — fix the
// test, never the provider (this is characterization, not spec verification).

using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class NavigationDatabaseProviderFixtureTests
{
    // --- Case 12: never-drop-fix-less-leg, end to end via GetSIDWaypoints -----

    [Fact]
    public void GetSIDWaypoints_fixless_CA_leg_is_kept_with_synthesized_label()
    {
        using var fixture = new NavDataFixtureDb();
        fixture.InsertApproach(1, "KTST", "RNAV", suffix: "D", fixIdent: "TEST1");
        fixture.InsertApproachLeg(1, approachId: 1, fixIdent: null, type: "CA", course: 71, altitude1: 600);
        fixture.Seal();

        var provider = new NavigationDatabaseProvider(fixture.DbPath);
        var waypoints = provider.GetSIDWaypoints(1);

        var wp = Assert.Single(waypoints);
        Assert.False(string.IsNullOrWhiteSpace(wp.Ident));
        Assert.Contains("600", wp.Ident);
        Assert.Contains("71", wp.Ident);
        // Matches BuildManeuverLabel("CA", 71, "", 600) captured in the pure-logic tests.
        Assert.Equal("Climb course 71° to 600 feet", wp.Ident);
        Assert.Equal(FlightPlanSection.SID, wp.Section);
        Assert.Equal("SID", wp.InboundAirway);
    }

    // --- Case 13: resolve-coords-across-all-fix-tables — VOR ------------------

    [Fact]
    public void GetApproachWaypoints_resolves_VOR_coords_when_no_waypoint_row_exists()
    {
        using var fixture = new NavDataFixtureDb();
        fixture.InsertApproach(1, "KTST", "RNAV", suffix: "");
        fixture.InsertVor("ABC", lonx: 10.5, laty: 20.5);
        fixture.InsertApproachLeg(1, approachId: 1, fixIdent: "ABC", fixType: "V", type: "TF");
        fixture.Seal();

        var provider = new NavigationDatabaseProvider(fixture.DbPath);
        var waypoints = provider.GetApproachWaypoints(1);

        var wp = Assert.Single(waypoints);
        Assert.Equal("ABC", wp.Ident);
        Assert.Equal(20.5, wp.Latitude);
        Assert.Equal(10.5, wp.Longitude);
    }

    // --- Case 14: resolve-coords-across-all-fix-tables — runway threshold -----

    [Fact]
    public void GetApproachWaypoints_resolves_runway_threshold_coords_via_runway_end()
    {
        using var fixture = new NavDataFixtureDb();
        fixture.InsertAirport(1, "KTST", "KTST");
        fixture.InsertRunwayEnd(1, "06L", lonx: 5.5, laty: 6.6);
        fixture.InsertRunway(1, airportId: 1, primaryEndId: 1);
        fixture.InsertApproach(1, "KTST", "ILS", suffix: "");
        fixture.InsertApproachLeg(1, approachId: 1, fixIdent: "RW06L", fixType: "R", fixAirportIdent: "KTST", type: "TF");
        fixture.Seal();

        var provider = new NavigationDatabaseProvider(fixture.DbPath);
        var waypoints = provider.GetApproachWaypoints(1);

        var wp = Assert.Single(waypoints);
        Assert.Equal(6.6, wp.Latitude);
        Assert.Equal(5.5, wp.Longitude);
    }

    // --- Case 15: unresolvable fix stays at (0,0) ------------------------------

    [Fact]
    public void GetApproachWaypoints_unresolvable_VOR_fix_stays_at_origin()
    {
        using var fixture = new NavDataFixtureDb();
        fixture.InsertApproach(1, "KTST", "RNAV", suffix: "");
        fixture.InsertApproachLeg(1, approachId: 1, fixIdent: "ZZZZZ", fixType: "V", type: "TF");
        fixture.Seal();

        var provider = new NavigationDatabaseProvider(fixture.DbPath);
        var waypoints = provider.GetApproachWaypoints(1);

        var wp = Assert.Single(waypoints);
        Assert.Equal(0.0, wp.Latitude);
        Assert.Equal(0.0, wp.Longitude);
    }

    // --- Case 16: circling-suffix-A distinction between GetApproaches/GetSTARs ---

    [Fact]
    public void GetApproaches_vs_GetSTARs_distinguish_circling_approach_from_STAR_by_missed_leg()
    {
        using var fixture = new NavDataFixtureDb();
        // Circling approach: suffix 'A', runway_name NULL, HAS a missed-approach leg.
        fixture.InsertApproach(10, "KTST", "VOR", suffix: "A", fixIdent: "CIRCLE");
        fixture.InsertApproachLeg(1, approachId: 10, fixIdent: "MISSEDFIX", type: "CF", isMissed: true);
        // STAR: suffix 'A', NO missed-approach leg.
        fixture.InsertApproach(11, "KTST", "RNAV", suffix: "A", fixIdent: "STARNAME");
        fixture.Seal();

        var provider = new NavigationDatabaseProvider(fixture.DbPath);
        var approaches = provider.GetApproaches("KTST");
        var stars = provider.GetSTARs("KTST");

        Assert.Contains(approaches, a => a.approachId == 10);
        Assert.DoesNotContain(approaches, a => a.approachId == 11);

        Assert.Contains(stars, s => s.fixIdent == "STARNAME");
        Assert.DoesNotContain(stars, s => s.fixIdent == "CIRCLE");
    }

    // --- Case 17: "ALL" picks the runway-independent body (runway_name keying) ---

    [Fact]
    public void GetSIDsForRunway_ALL_picks_runway_independent_body_over_runway_specific_row()
    {
        using var fixture = new NavDataFixtureDb();
        // A: generic (runway-independent) row for SID "TEST1".
        fixture.InsertApproach(1, "KTST", "RNAV", suffix: "D", fixIdent: "TEST1", runwayName: null, arincName: null);
        fixture.InsertApproachLeg(1, approachId: 1, fixIdent: "GENERIC", type: "TF");
        // B: runway-specific sibling row for the SAME SID name.
        fixture.InsertApproach(2, "KTST", "RNAV", suffix: "D", fixIdent: "TEST1", runwayName: "06L");
        fixture.InsertApproachLeg(2, approachId: 2, fixIdent: "SPECIFIC", type: "TF");
        fixture.Seal();

        var provider = new NavigationDatabaseProvider(fixture.DbPath);
        var sids = provider.GetSIDsForRunway("KTST", "ALL");

        var sid = Assert.Single(sids);
        Assert.Equal("TEST1", sid.fixIdent);
        Assert.Equal(1, sid.approachId);

        var waypoints = provider.GetSIDWaypoints(sid.approachId);
        Assert.Contains(waypoints, w => w.Ident == "GENERIC");
        Assert.DoesNotContain(waypoints, w => w.Ident == "SPECIFIC");
    }

    // --- Case 18: "ALL" Jeppesen-style — arinc_name distinguishes generic vs runway tag ---

    [Fact]
    public void GetSIDsForRunway_ALL_Jeppesen_picks_non_runway_arinc_tag_over_RW_tag()
    {
        using var fixture = new NavDataFixtureDb();
        // Both rows have runway_name NULL (Jeppesen-style); A's arinc_name is a non-runway
        // generic tag, B's is a concrete RW tag.
        fixture.InsertApproach(1, "OMDB", "RNAV", suffix: "D", fixIdent: "TEST2", runwayName: null, arincName: "ALL");
        fixture.InsertApproach(2, "OMDB", "RNAV", suffix: "D", fixIdent: "TEST2", runwayName: null, arincName: "RW30B");
        fixture.Seal();

        var provider = new NavigationDatabaseProvider(fixture.DbPath);
        var sids = provider.GetSIDsForRunway("OMDB", "ALL");

        var sid = Assert.Single(sids);
        Assert.Equal("TEST2", sid.fixIdent);
        Assert.Equal(1, sid.approachId);
    }

    // --- Case 19: concrete-runway filter via ARINC tag expansion (Jeppesen fallback) ---

    [Fact]
    public void GetSIDsForRunway_concrete_runway_filters_via_arinc_tag()
    {
        using var fixture = new NavDataFixtureDb();
        fixture.InsertApproach(1, "OMDB", "RNAV", suffix: "D", fixIdent: "TEST3", runwayName: null, arincName: "RW30B");
        fixture.Seal();

        var provider = new NavigationDatabaseProvider(fixture.DbPath);

        Assert.Single(provider.GetSIDsForRunway("OMDB", "30L"));
        Assert.Single(provider.GetSIDsForRunway("OMDB", "30R"));
        Assert.Empty(provider.GetSIDsForRunway("OMDB", "24L"));
    }

    // --- Case 20: FlightPlanManager.LoadSTAR end-to-end boundary-fix dedup -----

    [Fact]
    public void FlightPlanManager_LoadSTAR_dedups_shared_boundary_fix_between_transition_and_STAR()
    {
        using var fixture = new NavDataFixtureDb();
        fixture.InsertApproach(1, "KTST", "RNAV", suffix: "A", fixIdent: "STARX");
        // Transition ends at MERUV (single leg).
        fixture.InsertTransition(1, approachId: 1, fixIdent: "TRANSX", type: "TF");
        fixture.InsertTransitionLeg(1, transitionId: 1, fixIdent: "MERUV", type: "TF");
        // STAR begins at MERUV (the shared boundary fix), then continues to NEXTFIX.
        fixture.InsertApproachLeg(1, approachId: 1, fixIdent: "MERUV", type: "TF");
        fixture.InsertApproachLeg(2, approachId: 1, fixIdent: "NEXTFIX", type: "TF");
        fixture.Seal();

        var fpm = new FlightPlanManager(fixture.DbPath, null);
        fpm.LoadSTAR(starId: 1, transitionId: 1, starName: "STARX");

        var idents = fpm.CurrentFlightPlan.STARWaypoints.Select(w => w.Ident).ToList();
        Assert.Equal(new[] { "MERUV", "NEXTFIX" }, idents);
        Assert.Single(idents, i => i == "MERUV");
    }
}
