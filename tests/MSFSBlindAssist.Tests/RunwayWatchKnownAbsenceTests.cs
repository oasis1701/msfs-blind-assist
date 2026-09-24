// GroundTrafficLogic.ForgetAbsent — how long the runway watch remembers an occupant or a final it has
// already announced. PR #247 B2 review Minor 7: the known sets were intersected with each evaluation's
// seen sets, so an aircraft missing for ONE evaluation (a climb-rate sample over the 300 fpm line, an
// on-ground flag flicker, a lateral boundary) was announced again — interrupting when the pilot is on
// the runway — and "no traffic seen on the runway now" could be said of an occupant still there.

using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RunwayWatchKnownAbsenceTests
{
    private static readonly DateTime T0 = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IReadOnlySet<uint> Nothing = new HashSet<uint>();

    [Fact]
    public void An_id_unseen_for_less_than_the_grace_period_is_kept()
    {
        var known = new HashSet<uint> { 7 };
        var absentSince = new Dictionary<uint, DateTime>();
        Assert.Empty(GroundTrafficLogic.ForgetAbsent(known, Nothing, absentSince, T0));                  // first miss
        Assert.Empty(GroundTrafficLogic.ForgetAbsent(known, Nothing, absentSince, T0.AddSeconds(2.9)));
        Assert.Contains(7u, known);
    }

    [Fact]
    public void An_id_unseen_for_the_grace_period_is_forgotten_and_returned()
    {
        var known = new HashSet<uint> { 7, 8 };
        var absentSince = new Dictionary<uint, DateTime>();
        var seen8 = new HashSet<uint> { 8 };
        GroundTrafficLogic.ForgetAbsent(known, seen8, absentSince, T0);
        var forgotten = GroundTrafficLogic.ForgetAbsent(known, seen8, absentSince, T0.AddSeconds(3.0));
        Assert.Equal(new uint[] { 7 }, forgotten);
        Assert.Equal(new uint[] { 8 }, known);
        Assert.Empty(absentSince);
    }

    [Fact]
    public void Being_seen_again_clears_the_absence()
    {
        var known = new HashSet<uint> { 7 };
        var absentSince = new Dictionary<uint, DateTime>();
        GroundTrafficLogic.ForgetAbsent(known, Nothing, absentSince, T0);                                // missed at 0 s
        GroundTrafficLogic.ForgetAbsent(known, new HashSet<uint> { 7 }, absentSince, T0.AddSeconds(2));  // seen at 2 s
        Assert.Empty(absentSince);
        GroundTrafficLogic.ForgetAbsent(known, Nothing, absentSince, T0.AddSeconds(2.5));                // missed again
        // 5.4 s after the first miss, but only 2.9 s into this one: kept.
        Assert.Empty(GroundTrafficLogic.ForgetAbsent(known, Nothing, absentSince, T0.AddSeconds(5.4)));
        Assert.Contains(7u, known);
    }

    [Fact]
    public void Ids_that_are_not_known_are_ignored()
    {
        var known = new HashSet<uint> { 7 };
        var absentSince = new Dictionary<uint, DateTime>();
        Assert.Empty(GroundTrafficLogic.ForgetAbsent(known, new HashSet<uint> { 7, 9 }, absentSince, T0));
        Assert.Equal(new uint[] { 7 }, known);    // seen, but never announced: not known
        Assert.Empty(absentSince);
    }

    [Fact]
    public void Seen_ids_are_never_removed()
    {
        var known = new HashSet<uint> { 7, 8 };
        var absentSince = new Dictionary<uint, DateTime>();
        var seen = new HashSet<uint> { 7, 8 };
        for (int s = 0; s <= 10; s++)
            Assert.Empty(GroundTrafficLogic.ForgetAbsent(known, seen, absentSince, T0.AddSeconds(s)));
        Assert.Equal(new uint[] { 7, 8 }, known.OrderBy(id => id));
        Assert.Empty(absentSince);
    }
}

// GroundTrafficLogic.IdsOutOfScope — PR #247 B5 follow-up K3, keyed by RUNWAY since the PR #247
// re-review (Critical 1, Important 2). Since PR #247 final review H3, the runway watch widens its scan
// for a runway the aircraft is merely on, without changing the watch's identity. Once the aircraft
// leaves that runway's pavement, the runway drops out of the scan entirely — its known occupant is not
// "unseen", the watch simply stopped scanning it — so it must be told apart from a runway that
// genuinely emptied. IdsOutOfScope answers "which known ids were last seen under a runway no longer in
// this evaluation's scan" from each id's recorded runway KEY (both ends, "09/27") against the current
// scan's keys. Two things it must never do, each once a double announcement: judge an id with NO
// recorded key out of scope (a landing aircraft's final record was removed 3 s after touchdown and,
// sharing one map, took its occupant record with it — the occupant was purged and announced again),
// and compare SPOKEN designators (a position-only watch names the nearer end, which flips at
// mid-runway: the same runway's traffic was purged and announced again).

public class RunwayWatchIdsOutOfScopeTests
{
    private static readonly TaxiGraph.RunwayCenterline[] Runways =
    {
        new() { Name1 = "09", Name2 = "27", Lat1 = 50.0, Lon1 = 0.0, Lat2 = 50.0, Lon2 = 0.042, HeadingDeg1 = 90, HalfWidthMeters = 22.5 },
    };

    [Fact]
    public void An_id_recorded_under_a_still_scanned_runway_stays_in_scope()
    {
        var known = new HashSet<uint> { 7 };
        var runwayOf = new Dictionary<uint, string> { [7] = "09/27" };
        var scope = new HashSet<string> { "09/27" };
        Assert.Empty(GroundTrafficLogic.IdsOutOfScope(known, runwayOf, scope));
    }

    [Fact]
    public void An_id_recorded_under_a_runway_no_longer_scanned_is_out_of_scope()
    {
        // The aircraft was on an intersecting runway (15/33) that widened the scan; the watch has since
        // narrowed back down to just 09/27, so 15/33's occupant must be forgotten silently.
        var known = new HashSet<uint> { 7 };
        var runwayOf = new Dictionary<uint, string> { [7] = "15/33" };
        var scope = new HashSet<string> { "09/27" };
        Assert.Equal(new uint[] { 7 }, GroundTrafficLogic.IdsOutOfScope(known, runwayOf, scope));
    }

    [Fact]
    public void An_id_missing_from_the_map_is_kept()
    {
        // No recorded key says nothing about the scan: it is left to ForgetAbsent's grace timer.
        var known = new HashSet<uint> { 7 };
        var runwayOf = new Dictionary<uint, string>();
        var scope = new HashSet<string> { "09/27" };
        Assert.Empty(GroundTrafficLogic.IdsOutOfScope(known, runwayOf, scope));
    }

    [Fact]
    public void A_designator_flip_on_the_same_key_keeps_it()
    {
        // Announced while the watch named the runway "09"; the aircraft has since passed mid-runway and
        // the watch now names it "27". Both are the one runway, 09/27.
        string recorded = RunwayWatchScopes.RunwayKey(Runways, "09");
        string scanned = RunwayWatchScopes.RunwayKey(Runways, "27");
        Assert.Equal("09/27", recorded);
        Assert.Equal("09/27", scanned);
        var known = new HashSet<uint> { 7 };
        var runwayOf = new Dictionary<uint, string> { [7] = recorded };
        Assert.Empty(GroundTrafficLogic.IdsOutOfScope(known, runwayOf, new HashSet<string> { scanned }));
    }

    [Fact]
    public void Only_the_out_of_scope_ids_are_returned()
    {
        var known = new HashSet<uint> { 7, 8, 9, 10 };
        var runwayOf = new Dictionary<uint, string> { [7] = "09/27", [8] = "15/33", [9] = "09/27" };   // 10: none
        var scope = new HashSet<string> { "09/27" };
        Assert.Equal(new uint[] { 8 }, GroundTrafficLogic.IdsOutOfScope(known, runwayOf, scope));
    }

    [Fact]
    public void Nothing_known_is_nothing_out_of_scope()
        => Assert.Empty(GroundTrafficLogic.IdsOutOfScope(Array.Empty<uint>(),
            new Dictionary<uint, string>(), new HashSet<string> { "09/27" }));
}
