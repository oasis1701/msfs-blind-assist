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

    // Minor 4: the two recalculation refusal call sites can otherwise produce the exact same
    // key. Site 1 ("no start node found at all") never has a runway, so it always passes an
    // empty runwayDesignator; site 2's CrossesUnnamedRunway branch ("a start node WAS found,
    // but the first leg crosses a runway with no designator at either end") also passes an
    // empty runwayDesignator. Without a site tag, the same destination refused for both
    // reasons under the same verdict would collide and the second, materially different
    // refusal would be silently swallowed as a "repeat."
    [Fact]
    public void The_two_call_sites_never_collide_even_with_the_same_verdict_destination_and_empty_runway()
    {
        string noStartNodeKey = ReachabilityRefusalGate.KeyFor(
            ReachabilityClass.DestinationNotConnected, "Parking 40", "", site: "no-start-node");
        string crossesUnnamedRunwayKey = ReachabilityRefusalGate.KeyFor(
            ReachabilityClass.DestinationNotConnected, "Parking 40", "", site: "crosses-runway");
        Assert.NotEqual(noStartNodeKey, crossesUnnamedRunwayKey);
        Assert.True(ReachabilityRefusalGate.ShouldAnnounce(noStartNodeKey, crossesUnnamedRunwayKey));
    }

    [Fact]
    public void Omitting_the_site_tag_keeps_existing_single_site_comparisons_unaffected()
    {
        // The default ("") must behave identically for two keys that both omit it -- this is
        // what keeps every pre-existing 3-argument KeyFor call (both call sites' own OTHER
        // key, and every test above) byte-for-byte unaffected by adding the parameter.
        string a = ReachabilityRefusalGate.KeyFor(ReachabilityClass.DestinationNotConnected, "Parking 40", "");
        string b = ReachabilityRefusalGate.KeyFor(ReachabilityClass.DestinationNotConnected, "Parking 40", "");
        Assert.Equal(a, b);
        Assert.False(ReachabilityRefusalGate.ShouldAnnounce(a, b));
    }

    // Important 2: the latch must live for the OFF-ROUTE EPISODE it was raised for, not the
    // whole route -- TaxiGuidanceManager now clears _lastReachabilityRefusalKey back to null
    // both when the off-route condition ends (aircraft back on route) and when a
    // recalculation succeeds. This is a contract test on the gate's own null-handling: it
    // pins the exact sequence that wiring depends on, since TaxiGuidanceManager itself has no
    // constructible test double (it requires a live ScreenReaderAnnouncer window handle) --
    // see the group-C report for why Important 2's manager-side wiring is otherwise
    // unverifiable outside a live sim session.
    [Fact]
    public void After_the_episode_ends_and_the_latch_is_cleared_the_same_refusal_speaks_again()
    {
        string key = ReachabilityRefusalGate.KeyFor(
            ReachabilityClass.DestinationNotConnected, "Parking 40", "");
        string? latch = null;
        Assert.True(ReachabilityRefusalGate.ShouldAnnounce(latch, key));
        latch = key; // first episode: refusal spoken and latched

        // A 15 s cooldown retry while STILL in the same off-route episode: silent, which is
        // the whole point of Task 2's original fix.
        Assert.False(ReachabilityRefusalGate.ShouldAnnounce(latch, key));

        latch = null; // the episode ends (back on route, or a recalculation succeeded)

        // A NEW episode later, refused for the exact same reason: must speak again -- a
        // latch that survived the episode boundary would leave the pilot in total silence
        // the second time, which is the failure Important 2 exists to close.
        Assert.True(ReachabilityRefusalGate.ShouldAnnounce(latch, key));
    }
}
