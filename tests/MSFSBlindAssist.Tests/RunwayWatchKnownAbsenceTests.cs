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
