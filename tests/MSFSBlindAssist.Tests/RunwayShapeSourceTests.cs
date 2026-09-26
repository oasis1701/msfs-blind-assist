using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Tests.RunwayFixture;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The pure half of TaxiGuidanceManager.IsOnRunwayPavement. The manager itself cannot be
/// constructed here (its constructor builds a TaxiSteeringTone), so the DECISION lives out here
/// where it can be pinned — which source answers (<see cref="RunwayShapeSource.Choose"/>) and the
/// whole probe step with the memo it leaves behind (<see cref="RunwayShapeSource.Resolve"/>); the
/// manager holds only the lock, the fields and the database generation.
/// </summary>
public class RunwayShapeSourceTests
{
    private static RunwayShapeMemo Memo(string icao, long generation = 0)
        => new(icao, generation, null, Array.Empty<RunwayShape>());

    /// <summary>A 3,000 m 09/27 built from its start rows alone: centrelines, no taxiways.</summary>
    private static TaxiGraph RunwayGraph() => TaxiGraph.Build(new List<TaxiPath>(), new List<ParkingSpot>(),
        new List<StartPosition>
        {
            new() { RunwayName = "09", Latitude = Lat(0), Longitude = Lon(0), Heading = 90 },
            new() { RunwayName = "27", Latitude = Lat(0), Longitude = Lon(3000), Heading = 270 },
        });

    [Fact]
    public void The_active_guidance_graph_answers_for_its_own_airport()
        => Assert.Equal(RunwayShapeSourceKind.ActiveGraph, RunwayShapeSource.Choose("KTIW", 0, "ktiw", 0, "KSEA", Memo("KSEA")));

    [Fact]
    public void The_where_am_i_graph_answers_when_guidance_is_elsewhere()
        => Assert.Equal(RunwayShapeSourceKind.WhereAmIGraph, RunwayShapeSource.Choose("KTIW", 0, "KSEA", 0, "KTIW", null));

    [Fact]
    public void The_memo_answers_after_the_taxiway_name_fetch_drops_the_graph()
    {
        // The sequence this exists for: a Where-Am-I graph answers, the GetTaxiPaths that built it
        // starts the online taxiway-name fetch, the fetch lands a few seconds later and
        // OnAirportDataUpdated nulls the graph. Without the memo the probe then answered null — and
        // null does not silence — so building callouts were permitted ON A RUNWAY until the warm-up
        // retry came round a minute later.
        Assert.Equal(RunwayShapeSourceKind.Memo, RunwayShapeSource.Choose("KTIW", 0, null, 0, null, Memo("ktiw")));
    }

    [Fact]
    public void A_memo_for_another_airport_answers_nothing()
        => Assert.Equal(RunwayShapeSourceKind.None, RunwayShapeSource.Choose("KTIW", 0, null, 0, null, Memo("KSEA")));

    [Fact]
    public void Nothing_cached_and_no_airport_both_answer_nothing()
    {
        Assert.Equal(RunwayShapeSourceKind.None, RunwayShapeSource.Choose("KTIW", 0, null, 0, null, null));
        Assert.Equal(RunwayShapeSourceKind.None, RunwayShapeSource.Choose(" ", 0, "KTIW", 0, "KTIW", Memo("KTIW")));
    }

    [Fact]
    public void A_graph_always_outranks_the_memo_it_would_replace()
    {
        // The memo is only ever a stand-in. A graph present for this airport is both fresher and
        // the thing the memo is rebuilt from, so it must win whichever airport the memo holds.
        Assert.Equal(RunwayShapeSourceKind.ActiveGraph, RunwayShapeSource.Choose("KTIW", 0, "KTIW", 0, null, Memo("KTIW")));
        Assert.Equal(RunwayShapeSourceKind.WhereAmIGraph, RunwayShapeSource.Choose("KTIW", 0, null, 0, "KTIW", Memo("KTIW")));
    }

    // ---- A database switch moves the generation; what was built before it stops answering ----

    [Fact]
    public void An_active_graph_installed_before_a_database_switch_no_longer_answers()
    {
        // Guidance keeps flying its route across a database switch — its graph is deliberately left
        // in place — but that graph was built from the database the switch replaced.
        Assert.Equal(RunwayShapeSourceKind.None, RunwayShapeSource.Choose("KTIW", 1, "KTIW", 0, null, null));
        Assert.Equal(RunwayShapeSourceKind.WhereAmIGraph, RunwayShapeSource.Choose("KTIW", 1, "KTIW", 0, "KTIW", null));
    }

    [Fact]
    public void A_memo_from_another_database_generation_is_never_read()
        => Assert.Equal(RunwayShapeSourceKind.None, RunwayShapeSource.Choose("KTIW", 2, null, 2, null, Memo("KTIW", generation: 1)));

    // ---- The whole probe step ----

    [Fact]
    public void A_graph_that_answers_re_seeds_the_memo_once_and_the_memo_answers_after_the_graph_is_gone()
    {
        var graph = RunwayGraph();
        var (shapes, memo) = RunwayShapeSource.Resolve("KTIW", 0, null, null, 0, graph, "KTIW", null);
        Assert.NotNull(shapes);
        Assert.Single(shapes!);
        Assert.Same(graph, memo!.SourceGraph);
        Assert.Equal(0L, memo.Generation);
        Assert.True(RunwayPavement.IsOnPavement(Lat(0), Lon(1500), shapes!));

        // The same graph on the next tick: the memo is reused, not rebuilt.
        var (again, memoAgain) = RunwayShapeSource.Resolve("KTIW", 0, null, null, 0, graph, "KTIW", memo);
        Assert.Same(memo, memoAgain);
        Assert.Same(shapes, again);

        // The taxiway-name fetch drops the Where-Am-I graph: the memo answers on its own.
        var (fromMemo, memoKept) = RunwayShapeSource.Resolve("KTIW", 0, null, null, 0, null, null, memo);
        Assert.Same(shapes, fromMemo);
        Assert.Same(memo, memoKept);
    }

    [Fact]
    public void An_active_graph_from_before_a_database_switch_neither_answers_nor_re_seeds_the_memo()
    {
        // The switch cleared the memo (null here) and moved the generation to 1. The route's graph
        // (generation 0) must not put the previous database's runways back into the memo — it once
        // did, and that memo then outlived StopGuidance for the rest of the session.
        var (shapes, memo) = RunwayShapeSource.Resolve("KTIW", 1, RunwayGraph(), "KTIW", 0, null, null, null);
        Assert.Null(shapes);
        Assert.Null(memo);
    }

    [Fact]
    public void After_a_database_switch_the_where_am_i_graph_seeds_the_memo_not_the_route_s_graph()
    {
        var routeGraph = RunwayGraph();      // installed at generation 0
        var fresh = RunwayGraph();           // a Where-Am-I graph built after the switch
        var (shapes, memo) = RunwayShapeSource.Resolve("KTIW", 1, routeGraph, "KTIW", 0, fresh, "KTIW", null);
        Assert.NotNull(shapes);
        Assert.Same(fresh, memo!.SourceGraph);
        Assert.Equal(1L, memo.Generation);
    }

    [Fact]
    public void Nothing_held_for_the_airport_answers_null_and_leaves_the_memo_alone()
    {
        var other = Memo("KSEA");
        var (shapes, memo) = RunwayShapeSource.Resolve("KTIW", 0, null, null, 0, null, null, other);
        Assert.Null(shapes);
        Assert.Same(other, memo);
    }

    [Fact]
    public void An_empty_memo_is_an_answer_not_on_a_runway_rather_than_nothing()
    {
        // An airport with no runways: null would mean "unknown" and permit a callout anywhere, an
        // empty list says "not on a runway", which is simply true there.
        var empty = Memo("KXYZ");
        var (shapes, _) = RunwayShapeSource.Resolve("KXYZ", 0, null, null, 0, null, null, empty);
        Assert.NotNull(shapes);
        Assert.False(RunwayPavement.IsOnPavement(Lat(0), Lon(100), shapes!));
    }

    // ---- The warm-up's publish rule: nothing read before a database switch is stored ----

    [Theory]
    [InlineData(3L, 3L, true)]
    [InlineData(2L, 3L, false)]
    public void Only_what_was_read_under_the_current_generation_may_be_stored(long readUnder, long current, bool expected)
        => Assert.Equal(expected, RunwayShapeSource.MayStore(readUnder, current));

    [Fact]
    public void A_warm_up_read_under_the_current_generation_is_published_as_a_rows_only_memo_and_answers()
    {
        var shapes = RunwayPavement.BuildShapes(RunwayGraph().RunwayCenterlines);
        var memo = RunwayShapeSource.Publish(null, "KTIW", 4, 4, shapes, trackedIcao: "KTIW");
        Assert.NotNull(memo);
        Assert.Equal("KTIW", memo!.Icao);
        Assert.Equal(4L, memo.Generation);
        Assert.Null(memo.SourceGraph);
        Assert.Same(shapes, memo.Shapes);

        var (answer, kept) = RunwayShapeSource.Resolve("KTIW", 4, null, null, 0, null, null, memo);
        Assert.Same(shapes, answer);
        Assert.Same(memo, kept);
        Assert.True(RunwayPavement.IsOnPavement(Lat(0), Lon(1500), answer!));
    }

    [Fact]
    public void A_warm_up_that_straddled_a_database_switch_is_never_stored()
    {
        // Its provider was captured before the switch, so it read the PREVIOUS database. Stored, the
        // probe would answer from those runways for the rest of the session: the switch had already
        // cleared the memo, and a probe that answers is never warmed again.
        var shapes = RunwayPavement.BuildShapes(RunwayGraph().RunwayCenterlines);
        var held = Memo("KSEA", generation: 5);
        Assert.Same(held, RunwayShapeSource.Publish(held, "KTIW", 4, 5, shapes, trackedIcao: "KTIW"));
        Assert.Null(RunwayShapeSource.Publish(null, "KTIW", 4, 5, shapes, trackedIcao: "KTIW"));
    }

    [Fact]
    public void A_warm_up_for_an_airport_the_probe_is_no_longer_asked_about_is_never_stored()
    {
        // The monitor moved from KTIW to KSEA while KTIW's warm-up was still reading; KSEA's own
        // warm-up has already published. Stored, KTIW's late answer would evict KSEA's memo, and
        // the probe would answer null — which does not silence — until the retry a minute later.
        var shapes = RunwayPavement.BuildShapes(RunwayGraph().RunwayCenterlines);
        var current = Memo("KSEA", generation: 4);
        Assert.Same(current, RunwayShapeSource.Publish(current, "KTIW", 4, 4, shapes, trackedIcao: "KSEA"));
        Assert.Null(RunwayShapeSource.Publish(null, "KTIW", 4, 4, shapes, trackedIcao: null));           // asked about nothing yet
        Assert.NotNull(RunwayShapeSource.Publish(current, "KTIW", 4, 4, shapes, trackedIcao: "ktiw"));   // case never matters
    }
}
