using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The switch-verify-settle sequence against a fake camera. The delays are recorded, not slept,
/// and advance a virtual clock, so "gives up after the cap" is elapsed virtual time — the same
/// wall-clock rule production uses — without a real wait.
/// </summary>
public class InstrumentViewSwitcherTests
{
    private sealed class FakeCamera : ICameraViewIo
    {
        public CameraViewReading? Current;
        public bool HonoursWrites = true;
        public bool ThrowOnSet;
        public bool ThrowOnRead;
        public long Clock;
        public int ReadCostMs;
        public readonly List<(int Type, int Index)> Writes = new();

        public Task<CameraViewReading?> ReadAsync(int timeoutMs)
        {
            if (ThrowOnRead) throw new InvalidOperationException("SimConnect down");
            Clock += ReadCostMs;
            return Task.FromResult(Current);
        }

        public void Set(int viewType, int viewIndex)
        {
            if (ThrowOnSet) throw new InvalidOperationException("SimConnect down");
            Writes.Add((viewType, viewIndex));
            if (HonoursWrites && Current is { } c)
                Current = c with { ViewType = viewType, ViewIndex = viewIndex };
        }
    }

    private static (InstrumentViewSwitcher Switcher, List<int> Delays, FakeCamera Camera) Make(FakeCamera camera)
    {
        var delays = new List<int>();
        camera.Clock = 0;
        var switcher = new InstrumentViewSwitcher(
            camera,
            delay: ms => { delays.Add(ms); camera.Clock += ms; return Task.CompletedTask; },
            now: () => camera.Clock);
        return (switcher, delays, camera);
    }

    [Fact]
    public async Task AlreadyOnTheView_WritesNothing()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 2, 2) };
        var (switcher, delays, _) = Make(camera);

        var session = await switcher.EnterAsync(2);

        Assert.Equal(InstrumentViewOutcome.AlreadyThere, session.Outcome);
        Assert.True(session.Verified);
        Assert.Empty(camera.Writes);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task FromThePilotView_Switches_VerifiesOnTheFirstRead_AndSettlesOnce()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 0) };
        var (switcher, delays, _) = Make(camera);

        var session = await switcher.EnterAsync(2);

        Assert.Equal(InstrumentViewOutcome.Switch, session.Outcome);
        Assert.True(session.Verified);
        Assert.Equal(new[] { (2, 2) }, camera.Writes);
        Assert.Equal(new[] { 250 }, delays);
    }

    [Fact]
    public async Task NothingIsWrittenAfterTheSwitch_ThePilotKeepsTheInstrumentView()
    {
        // A saved cabin view reads as a pilot-view index the sim would refuse on the way back
        // (measured 2026-09-09), so the switcher never tries to put a previous view back.
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 7) };
        var (switcher, _, _) = Make(camera);

        await switcher.EnterAsync(0);

        Assert.Equal(new[] { (2, 0) }, camera.Writes);
        Assert.Equal(new CameraViewReading(2, 2, 0), camera.Current);
    }

    [Fact]
    public async Task AWriteTheSimIgnores_GivesUpAfterTheCap_AndReportsUnverified()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 0), HonoursWrites = false };
        var (switcher, delays, _) = Make(camera);

        var session = await switcher.EnterAsync(2);

        Assert.Equal(InstrumentViewOutcome.Switch, session.Outcome);
        Assert.False(session.Verified);
        Assert.Equal(Enumerable.Repeat(100, 10), delays);   // 1000 ms cap / 100 ms steps, no settle
        Assert.Equal(new[] { (2, 2) }, camera.Writes);
    }

    [Fact]
    public async Task AReadThatWaitsOutItsTimeout_CountsAgainstTheCap()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 0), HonoursWrites = false, ReadCostMs = 500 };
        var (switcher, delays, _) = Make(camera);

        var session = await switcher.EnterAsync(2);

        Assert.False(session.Verified);
        Assert.Equal(new[] { 100 }, delays);   // read 500 → delay 100 → read 500: 1100 ms, budget spent
    }

    [Fact]
    public async Task AnExternalCamera_IsNotInCockpit_AndWritesNothing()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(3, 0, 0) };
        var (switcher, delays, _) = Make(camera);

        var session = await switcher.EnterAsync(2);

        Assert.Equal(InstrumentViewOutcome.NotInCockpit, session.Outcome);
        Assert.False(session.Verified);
        Assert.Empty(camera.Writes);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task AnUnreadableCamera_WritesTheView_AndReportsUnverified()
    {
        var camera = new FakeCamera { Current = null };
        var (switcher, _, _) = Make(camera);

        var session = await switcher.EnterAsync(3);

        Assert.Equal(InstrumentViewOutcome.Unknown, session.Outcome);
        Assert.False(session.Verified);
        Assert.Equal(new[] { (2, 3) }, camera.Writes);
    }

    [Fact]
    public async Task AThrowingWrite_DoesNotEscape()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 0), ThrowOnSet = true };
        var (switcher, _, _) = Make(camera);

        var session = await switcher.EnterAsync(2);

        Assert.Equal(InstrumentViewOutcome.Switch, session.Outcome);
        Assert.False(session.Verified);
    }

    [Fact]
    public async Task AThrowingRead_DoesNotEscape()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 0), ThrowOnRead = true };
        var (switcher, _, _) = Make(camera);

        var session = await switcher.EnterAsync(2);

        Assert.Equal(InstrumentViewOutcome.Unknown, session.Outcome);
        Assert.False(session.Verified);
        Assert.Equal(new[] { (2, 2) }, camera.Writes);
    }

    [Fact]
    public void TheDefaults_AreTheSpecsNumbers()
    {
        Assert.Equal(500, InstrumentViewSwitcher.DefaultReadTimeoutMs);
        Assert.Equal(100, InstrumentViewSwitcher.DefaultPollStepMs);
        Assert.Equal(1000, InstrumentViewSwitcher.DefaultVerifyCapMs);
        Assert.Equal(250, InstrumentViewSwitcher.DefaultSettleMs);
    }
}
