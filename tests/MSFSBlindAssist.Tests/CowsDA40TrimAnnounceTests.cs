using System;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Who speaks a trim movement. The DA40's own trim controls confirm where the trim arrived;
/// the shared trim announcer must not say the same movement again in other words. Measured on
/// the XLS at VCBI (2026-10-15): one nose-up press said "Trim up 0.7" AND "Trim 9 percent nose
/// up, 0.7 degrees", one nose-down said "Trim up 0.0" AND "Trim centred".
/// </summary>
public class CowsDA40TrimAnnounceTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 17, 10, 40, DateTimeKind.Utc);

    [Fact]
    public void AMoveInsideTheOwnWriteGraceIsOurs()
    {
        Assert.True(CowsDA40Definition.TrimMoveIsOurs(apEngaged: false, Now, ownTrimUntil: Now.AddMilliseconds(1)));
        Assert.False(CowsDA40Definition.TrimMoveIsOurs(apEngaged: false, Now, ownTrimUntil: Now));
    }

    [Fact]
    public void TheAutopilotsTrimIsAlwaysSilent()
        => Assert.True(CowsDA40Definition.TrimMoveIsOurs(apEngaged: true, Now, ownTrimUntil: DateTime.MinValue));

    [Fact]
    public void APilotsOwnTrimWheelStillSpeaks()
        => Assert.False(CowsDA40Definition.TrimMoveIsOurs(apEngaged: false, Now, ownTrimUntil: Now.AddSeconds(-5)));

    [Fact]
    public void TheGraceOutlastsTheHeldNudge()
        // The axis moves for the whole hold and the nudge's own read-back comes at its end, so
        // the shared announcer must stay quiet past both.
        => Assert.True(CowsDA40Definition.TrimOwnWriteGraceMs > CowsDA40Definition.TrimNudgeHoldMs + 1000);
}
