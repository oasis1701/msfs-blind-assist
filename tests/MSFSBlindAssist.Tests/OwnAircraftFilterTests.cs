// A traffic sweep reports the user aircraft under its REAL object id, never the
// SIMCONNECT_OBJECT_ID_USER alias 0, so the filter learns that id from the answers to our own
// requests. Live 2026-09-29: a FlyByWire A320 with no ATC ID was spoken to its own pilot as
// "Stop, Fly By Wire A320 very close, ahead, 0 feet."

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class OwnAircraftFilterTests
{
    [Fact]
    public void The_learned_object_id_is_own_aircraft_even_with_no_callsign()
    {
        var filter = new OwnAircraftFilter();
        filter.Observe(1);

        Assert.True(filter.IsOwnAircraft(1, "", ""));
        Assert.False(filter.IsOwnAircraft(7, "", ""));
    }

    [Fact]
    public void Before_an_id_is_learned_only_a_matching_callsign_filters()
    {
        var filter = new OwnAircraftFilter();

        Assert.True(filter.IsOwnAircraft(1, "G-NPTD", "g-nptd "));
        Assert.False(filter.IsOwnAircraft(1, "", ""));
        Assert.False(filter.IsOwnAircraft(1, "ASXGSA", "ASXGS"));
    }

    [Fact]
    public void Two_empty_callsigns_are_not_the_same_aircraft()
    {
        Assert.False(OwnAircraftFilter.IsOwnAircraft(5, userObjectId: 1, "", ""));
        Assert.False(OwnAircraftFilter.IsOwnAircraft(5, userObjectId: 1, null, "  "));
    }

    [Fact]
    public void The_request_alias_zero_is_never_learned_and_always_filtered()
    {
        var filter = new OwnAircraftFilter();

        Assert.False(filter.Observe(0));
        Assert.Equal(0u, filter.UserObjectId);
        Assert.True(filter.IsOwnAircraft(0, "N123", "G-ABCD"));
    }

    [Fact]
    public void Observe_reports_only_a_change_and_follows_a_new_id()
    {
        var filter = new OwnAircraftFilter();

        Assert.True(filter.Observe(1));
        Assert.False(filter.Observe(1));
        Assert.True(filter.Observe(3));
        Assert.True(filter.IsOwnAircraft(3, "", ""));
        Assert.False(filter.IsOwnAircraft(1, "", ""));
    }

    [Fact]
    public void Reset_forgets_the_id()
    {
        var filter = new OwnAircraftFilter();
        filter.Observe(1);
        filter.Reset();

        Assert.Equal(0u, filter.UserObjectId);
        Assert.False(filter.IsOwnAircraft(1, "", ""));
    }
}
