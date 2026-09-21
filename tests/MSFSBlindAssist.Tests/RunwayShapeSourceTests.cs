using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The pure half of TaxiGuidanceManager.IsOnRunwayPavement's source choice. The manager itself
/// cannot be constructed here (its constructor builds a TaxiSteeringTone), so the DECISION lives
/// out here where it can be pinned; the manager holds only the lock, the fields and the geometry.
/// </summary>
public class RunwayShapeSourceTests
{
    [Fact]
    public void The_active_guidance_graph_answers_for_its_own_airport()
        => Assert.Equal(RunwayShapeSourceKind.ActiveGraph, RunwayShapeSource.Choose("KTIW", "ktiw", "KSEA", "KSEA"));

    [Fact]
    public void The_where_am_i_graph_answers_when_guidance_is_elsewhere()
        => Assert.Equal(RunwayShapeSourceKind.WhereAmIGraph, RunwayShapeSource.Choose("KTIW", "KSEA", "KTIW", null));

    [Fact]
    public void The_memo_answers_after_the_taxiway_name_fetch_drops_the_graph()
    {
        // The sequence this exists for: the first quiet tick warms the probe, the warm-up's own
        // GetTaxiPaths starts the online taxiway-name fetch, the fetch lands a few seconds later
        // and OnAirportDataUpdated nulls the Where-Am-I graph. Without the memo the probe then
        // answered null — and null does not silence — so building callouts were permitted ON A
        // RUNWAY until the warm-up retry came round a minute later.
        Assert.Equal(RunwayShapeSourceKind.Memo, RunwayShapeSource.Choose("KTIW", null, null, "ktiw"));
    }

    [Fact]
    public void A_memo_for_another_airport_answers_nothing()
        => Assert.Equal(RunwayShapeSourceKind.None, RunwayShapeSource.Choose("KTIW", null, null, "KSEA"));

    [Fact]
    public void Nothing_cached_and_no_airport_both_answer_nothing()
    {
        Assert.Equal(RunwayShapeSourceKind.None, RunwayShapeSource.Choose("KTIW", null, null, null));
        Assert.Equal(RunwayShapeSourceKind.None, RunwayShapeSource.Choose(" ", "KTIW", "KTIW", "KTIW"));
    }

    [Fact]
    public void A_graph_always_outranks_the_memo_it_would_replace()
    {
        // The memo is only ever a stand-in. A graph present for this airport is both fresher and
        // the thing the memo is rebuilt from, so it must win whichever airport the memo holds.
        Assert.Equal(RunwayShapeSourceKind.ActiveGraph, RunwayShapeSource.Choose("KTIW", "KTIW", null, "KTIW"));
        Assert.Equal(RunwayShapeSourceKind.WhereAmIGraph, RunwayShapeSource.Choose("KTIW", null, "KTIW", "KTIW"));
    }
}
