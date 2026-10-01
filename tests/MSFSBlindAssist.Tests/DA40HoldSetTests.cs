using System.Collections.Generic;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ The DA40 used to hold ONE momentary control at a time, so pressing a trim nudge, the
/// gyro cage or the fuel wire during the 26-second ECU test ended the test silently.
/// </summary>
public class DA40HoldSetTests
{
    [Fact]
    public void AnotherControlDoesNotEndTheEcuTest()
    {
        var holds = new DA40HoldSet();
        bool testDone = false;
        holds.Start("ECU_TEST:1", 26_000, () => testDone = true);
        holds.Start("INPUT_TRIM_UP", 1_000, null);

        var (write, release, _) = holds.Tick(1_500, connected: true);

        Assert.Contains("ECU_TEST:1", write);
        Assert.Equal(new[] { "INPUT_TRIM_UP" }, release);
        Assert.False(testDone);
        Assert.True(holds.IsHeld("ECU_TEST:1"));
    }

    [Fact]
    public void AHoldThatRunsItsTimeCompletes()
    {
        var holds = new DA40HoldSet();
        int done = 0;
        holds.Start("ECU_TEST:1", 26_000, () => done++);

        var (_, release, complete) = holds.Tick(26_000, connected: true);
        foreach (var c in complete) c();

        Assert.Equal(new[] { "ECU_TEST:1" }, release);
        Assert.Equal(1, done);
        Assert.True(holds.IsEmpty);
    }

    [Fact]
    public void PressingTheSameControlAgainReplacesItAndTheFirstNeverCompletes()
    {
        var holds = new DA40HoldSet();
        int first = 0, second = 0;
        holds.Start("ECU_TEST:1", 26_000, () => first++);
        Assert.True(holds.Start("ECU_TEST:1", 10_000, () => second++));

        var (_, _, complete) = holds.Tick(10_000, connected: true);
        foreach (var c in complete) c();

        Assert.Equal(0, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public void ADisconnectReleasesEverythingAndCompletesNothing()
    {
        var holds = new DA40HoldSet();
        int done = 0;
        holds.Start("ECU_TEST:1", 26_000, () => done++);
        holds.Start("ATT_CAGE", 700, () => done++);

        var (write, release, complete) = holds.Tick(100, connected: false);

        Assert.Empty(write);
        Assert.Equal(2, release.Count);
        Assert.Empty(complete);
        Assert.True(holds.IsEmpty);
    }

    [Fact]
    public void ReleaseAllCompletesNothing()
    {
        var holds = new DA40HoldSet();
        holds.Start("FUEL_WIRE", 1_500, () => throw new System.InvalidOperationException("must not run"));

        Assert.Equal(new List<string> { "FUEL_WIRE" }, holds.ReleaseAll());
        Assert.True(holds.IsEmpty);
    }
}
