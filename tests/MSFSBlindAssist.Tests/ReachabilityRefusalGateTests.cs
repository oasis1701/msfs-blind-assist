// Whether a route-reachability RECALCULATION refusal should be spoken again, or is the
// same refusal already spoken since the route was loaded (or since the last different
// refusal). PR #238 review follow-up (Task 2 of the group-C fixes).
//
// TryRecalculateRoute's off-route detector re-fires every RECALCULATION_COOLDOWN_SEC
// (15 s) for as long as the aircraft stays off-route, and a DestinationNotConnected /
// LeavingUnconnectedPosition verdict is a standing property of the airport data -- it
// refuses IDENTICALLY every cycle. Before this gate, each refusal was spoken through
// _announcer.AnnounceImmediate with no latch at all, so a pilot stuck off a disconnected
// destination heard the same ~20-word interrupting sentence every 15 s for the rest of
// the taxi, cutting off hold-short and runway-crossing callouts each time. This gate does
// not decide WHETHER to refuse -- RouteReachability / TryRecalculateRoute still do that
// every cycle -- only whether the identical refusal has already been spoken.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ReachabilityRefusalGateTests
{
    [Fact]
    public void A_null_last_key_always_speaks()
    {
        string key = ReachabilityRefusalGate.KeyFor(
            ReachabilityClass.DestinationNotConnected, "Parking 40", "");
        Assert.True(ReachabilityRefusalGate.ShouldAnnounce(null, key));
    }

    [Fact]
    public void The_first_refusal_speaks()
    {
        // Same as the null case above, phrased the way the call site actually reads it:
        // _lastReachabilityRefusalKey starts null before any refusal has been spoken.
        string? lastSpokenKey = null;
        string key = ReachabilityRefusalGate.KeyFor(
            ReachabilityClass.LeavingUnconnectedPosition, "Parking 40", "13");
        Assert.True(ReachabilityRefusalGate.ShouldAnnounce(lastSpokenKey, key));
    }

    [Fact]
    public void An_identical_repeat_is_silent()
    {
        string firstKey = ReachabilityRefusalGate.KeyFor(
            ReachabilityClass.DestinationNotConnected, "Parking 40", "");
        // The recalculation cycle runs again 15 s later; same verdict, same destination,
        // same (absent) runway -- the exact standing-refusal scenario this gate exists for.
        string repeatKey = ReachabilityRefusalGate.KeyFor(
            ReachabilityClass.DestinationNotConnected, "Parking 40", "");
        Assert.False(ReachabilityRefusalGate.ShouldAnnounce(firstKey, repeatKey));
    }

    [Fact]
    public void A_different_destination_speaks_again()
    {
        string firstKey = ReachabilityRefusalGate.KeyFor(
            ReachabilityClass.DestinationNotConnected, "Parking 40", "");
        // The pilot reprogrammed to a different stand that is refused the same way --
        // this is new information and must be heard.
        string secondKey = ReachabilityRefusalGate.KeyFor(
            ReachabilityClass.DestinationNotConnected, "Gate B12", "");
        Assert.True(ReachabilityRefusalGate.ShouldAnnounce(firstKey, secondKey));
    }

    [Fact]
    public void A_different_runway_speaks_again()
    {
        string firstKey = ReachabilityRefusalGate.KeyFor(
            ReachabilityClass.LeavingUnconnectedPosition, "Parking 40", "13");
        // The aircraft drifted to a different disconnected position whose way onto the
        // network crosses a DIFFERENT runway -- a materially different fact.
        string secondKey = ReachabilityRefusalGate.KeyFor(
            ReachabilityClass.LeavingUnconnectedPosition, "Parking 40", "31");
        Assert.True(ReachabilityRefusalGate.ShouldAnnounce(firstKey, secondKey));
    }

    [Fact]
    public void The_same_destination_refused_for_a_different_reason_speaks_again()
    {
        // Same destination, but the verdict itself changed -- e.g. the aircraft moved
        // from a disconnected position (LeavingUnconnectedPosition) onto a piece of
        // network that puts the destination itself out of reach (DestinationNotConnected).
        string firstKey = ReachabilityRefusalGate.KeyFor(
            ReachabilityClass.LeavingUnconnectedPosition, "Parking 40", "13");
        string secondKey = ReachabilityRefusalGate.KeyFor(
            ReachabilityClass.DestinationNotConnected, "Parking 40", "13");
        Assert.True(ReachabilityRefusalGate.ShouldAnnounce(firstKey, secondKey));
    }

    [Fact]
    public void The_same_inputs_always_produce_the_same_key()
    {
        // KeyFor must be a pure function of its inputs -- the whole gate depends on it.
        string a = ReachabilityRefusalGate.KeyFor(
            ReachabilityClass.DestinationNotConnected, "Gate B12", "31");
        string b = ReachabilityRefusalGate.KeyFor(
            ReachabilityClass.DestinationNotConnected, "Gate B12", "31");
        Assert.Equal(a, b);
    }
}
