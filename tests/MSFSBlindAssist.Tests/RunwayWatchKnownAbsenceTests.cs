// GroundTrafficLogic.ForgetAbsent — how long the runway watch remembers an occupant or a final it has
// already announced. PR #247 B2 review Minor 7: the known sets were intersected with each evaluation's
// seen sets, so an aircraft missing for ONE evaluation (a climb-rate sample over the 300 fpm line, an
// on-ground flag flicker, a lateral boundary) was announced again — interrupting when the pilot is on
// the runway — and "no traffic seen on the runway now" could be said of an occupant still there.

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

// GroundTrafficLogic.IdsOutOfScope — PR #247 B5 follow-up K3. Since PR #247 final review H3, the
// runway watch widens its scan for a runway the aircraft is merely on, without changing the watch's
// identity. Once the aircraft leaves that runway's pavement, the runway drops out of the scan
// entirely — its known occupant is not "unseen", the watch simply stopped scanning it — so it must be
// told apart from a runway that genuinely emptied. IdsOutOfScope answers "which known ids were last
// seen under a runway no longer in this evaluation's scan" from each id's recorded runway
// (_knownRunwayOf) against the current scan's designators.

public class RunwayWatchIdsOutOfScopeTests
{
    [Fact]
    public void An_id_recorded_under_a_still_scanned_runway_stays_in_scope()
    {
        var known = new HashSet<uint> { 7 };
        var runwayOf = new Dictionary<uint, string> { [7] = "27" };
        var scope = new HashSet<string> { "27" };
        Assert.Empty(GroundTrafficLogic.IdsOutOfScope(known, runwayOf, scope));
    }

    [Fact]
    public void An_id_recorded_under_a_runway_no_longer_scanned_is_out_of_scope()
    {
        // The aircraft was on an intersecting runway (33) that widened the scan; the watch has since
        // narrowed back down to just 27, so 33's occupant must be forgotten silently.
        var known = new HashSet<uint> { 7 };
        var runwayOf = new Dictionary<uint, string> { [7] = "33" };
        var scope = new HashSet<string> { "27" };
        Assert.Equal(new uint[] { 7 }, GroundTrafficLogic.IdsOutOfScope(known, runwayOf, scope));
    }

    [Fact]
    public void An_id_with_no_recorded_runway_is_out_of_scope()
    {
        var known = new HashSet<uint> { 7 };
        var runwayOf = new Dictionary<uint, string>();
        var scope = new HashSet<string> { "27" };
        Assert.Equal(new uint[] { 7 }, GroundTrafficLogic.IdsOutOfScope(known, runwayOf, scope));
    }

    [Fact]
    public void Only_the_out_of_scope_ids_are_returned()
    {
        var known = new HashSet<uint> { 7, 8, 9 };
        var runwayOf = new Dictionary<uint, string> { [7] = "27", [8] = "33", [9] = "27" };
        var scope = new HashSet<string> { "27" };
        Assert.Equal(new uint[] { 8 }, GroundTrafficLogic.IdsOutOfScope(known, runwayOf, scope));
    }

    [Fact]
    public void Nothing_known_is_nothing_out_of_scope()
        => Assert.Empty(GroundTrafficLogic.IdsOutOfScope(Array.Empty<uint>(),
            new Dictionary<uint, string>(), new HashSet<string> { "27" }));
}
