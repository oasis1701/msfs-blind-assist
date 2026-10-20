using System;
using MSFSBlindAssist.Aircraft.G1000;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A CAS message flickering on its threshold is ONE message. XLS over the sea off VCBI,
/// 2026-10-15: the left tank sloshing on its low line said "Caution: L FUEL LOW" and
/// "L FUEL LOW cleared" twelve times in 25 seconds, and a starving engine flapped FUEL PRES
/// and PTRM the same way. A message that goes is cleared only once it has stayed gone for
/// <see cref="G1000CasWatcher.CasClearDebounce.ClearSettleMs"/>; back inside that, it was
/// never gone, and nothing is said either way.
/// </summary>
public class CowsDA40CasClearDebounceTests
{
    private const string Low = "Caution: L FUEL LOW";
    private static readonly DateTime T0 = new(2026, 10, 15, 18, 53, 27, DateTimeKind.Utc);

    [Fact]
    public void AFlickerIsSaidOnceAndNeverCleared()
    {
        var d = new G1000CasWatcher.CasClearDebounce();
        d.Baseline(Array.Empty<string>());

        var (appeared, cleared) = d.Step(new[] { Low }, T0);
        Assert.Equal(new[] { Low }, appeared);
        Assert.Empty(cleared);

        // The poll pattern measured: gone 1.5 s, back, gone 3 s, back.
        foreach (var (present, at) in new[] { (false, 1.5), (true, 6.3), (false, 8.0), (true, 9.6), (false, 11.0), (true, 12.5) })
        {
            (appeared, cleared) = d.Step(present ? new[] { Low } : Array.Empty<string>(), T0.AddSeconds(at));
            Assert.Empty(appeared);
            Assert.Empty(cleared);
        }
    }

    [Fact]
    public void AMessageThatStaysGoneIsClearedOnce()
    {
        var d = new G1000CasWatcher.CasClearDebounce();
        d.Baseline(new[] { Low });

        Assert.Empty(d.Step(Array.Empty<string>(), T0).Cleared);
        Assert.Empty(d.Step(Array.Empty<string>(), T0.AddMilliseconds(G1000CasWatcher.CasClearDebounce.ClearSettleMs - 1)).Cleared);
        Assert.Equal(new[] { Low }, d.Step(Array.Empty<string>(), T0.AddMilliseconds(G1000CasWatcher.CasClearDebounce.ClearSettleMs)).Cleared);
        Assert.Empty(d.Step(Array.Empty<string>(), T0.AddSeconds(30)).Cleared);

        // And a genuine return after it was cleared is news again.
        Assert.Equal(new[] { Low }, d.Step(new[] { Low }, T0.AddSeconds(40)).Appeared);
    }

    [Fact]
    public void TheHoldOutlastsTheGapsMeasured()
        // The widest measured gap between "cleared" and the return was 3.1 s, at a ~1.5 s poll.
        => Assert.True(G1000CasWatcher.CasClearDebounce.ClearSettleMs > 3100 + 1500 / 2);
}
