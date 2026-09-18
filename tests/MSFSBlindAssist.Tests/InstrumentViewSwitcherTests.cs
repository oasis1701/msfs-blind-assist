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
        public int? ClampIndexTo;
        public readonly List<(int Type, int Index)> Writes = new();

        /// <summary>
        /// Fires after a successful <see cref="Set"/>, before this method returns — the only point
        /// in the switcher's synchronous await-chain that sits between a write and the poll read
        /// that follows it. Lets a test simulate a write reaching the sim on a camera <see
        /// cref="Current"/> could not previously read (the entry-read-timed-out-once scenario):
        /// <c>Current is {} c</c> below only updates an ALREADY-readable camera, by design, so a
        /// null <see cref="Current"/> needs this hook instead.
        /// </summary>
        public Action? AfterSet;

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
                Current = c with { ViewType = viewType, ViewIndex = ClampIndexTo ?? viewIndex };
            AfterSet?.Invoke();
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

    [Fact]
    public async Task RestoreAsync_PutsACustomPilotViewBack()
    {
        // The wing/cabin views blind pilots sit in read as type 1 at an index past the advertised
        // pilot-view count. Measured 2026-09-18 on the live iFly 737 MAX8: 1/7 writes back fine.
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 7) };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(0);

        Assert.True(await switcher.RestoreAsync(session));

        Assert.Equal(new[] { (2, 0), (1, 7) }, camera.Writes);
        Assert.Equal(new CameraViewReading(2, 1, 7), camera.Current);
    }

    [Fact]
    public async Task RestoreAsync_PutsAQuickviewBack()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 3, 2) };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(1);

        Assert.True(await switcher.RestoreAsync(session));

        Assert.Equal(new[] { (2, 1), (3, 2) }, camera.Writes);
    }

    [Fact]
    public async Task RestoreAsync_DoesNotSettle()
    {
        // Nothing is captured after the restore, so the 250 ms the entry spends waiting for a
        // rendered frame is not owed here. A read-back that matches at once costs no delay at all.
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 7) };
        var (switcher, delays, _) = Make(camera);
        var session = await switcher.EnterAsync(0);
        delays.Clear();

        await switcher.RestoreAsync(session);

        Assert.Empty(delays);
    }

    [Fact]
    public async Task RestoreAsync_WhenTheSimClampsToADifferentView_ReportsFailure()
    {
        // The sim CLAMPS an index it will not take rather than ignoring the write, and the
        // read-back reports the clamped value — measured 2026-09-18: a write of 20 landed on 8.
        // That is exactly what makes the read-back verification work.
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 7) };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(0);
        camera.ClampIndexTo = 5;

        // False alone would also pass an implementation that returned false WITHOUT attempting
        // the restore write — that distinction is the whole point of the feature.
        Assert.False(await switcher.RestoreAsync(session));
        Assert.Equal(new[] { (2, 0), (1, 7) }, camera.Writes);
    }

    [Fact]
    public async Task RestoreAsync_WhenTheSimIgnoresTheWrite_ReportsFailure()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 7) };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(0);
        camera.HonoursWrites = false;

        Assert.False(await switcher.RestoreAsync(session));
    }

    [Fact]
    public async Task RestoreAsync_AfterAlreadyThere_WritesNothing_AndReportsSuccess()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 2, 2) };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(2);

        Assert.True(await switcher.RestoreAsync(session));
        Assert.Empty(camera.Writes);
    }

    [Fact]
    public async Task RestoreAsync_AfterAnExternalCamera_WritesNothing_AndReportsSuccess()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(3, 0, 0) };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(2);

        Assert.True(await switcher.RestoreAsync(session));
        Assert.Empty(camera.Writes);
    }

    [Fact]
    public async Task RestoreAsync_WhenTheCameraWasUnreadable_WritesNothing_AndReportsSuccess()
    {
        // Unknown never had a reading to remember, so there is nothing to claim failure about.
        var camera = new FakeCamera { Current = null };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(3);
        camera.Writes.Clear();

        Assert.True(await switcher.RestoreAsync(session));
        Assert.Empty(camera.Writes);
    }

    [Fact]
    public async Task RestoreAsync_WhenTheEntryWasAVerifiedUnknown_ReportsFailure_AndWritesNothingFurther()
    {
        // The failure this pins: SimConnect is connected, the entry read times out once (Unknown,
        // nothing to remember), the write lands, and the NEXT poll read succeeds — so the entry
        // reports Verified even though InstrumentViewPlan.RestoreWrites has nothing to go back to.
        // Before the fix RestoreAsync returned true here and the pilot's replaced view — a custom
        // cabin/wing camera this app cannot recall — was never reported as un-restorable.
        var camera = new FakeCamera { Current = null };
        camera.AfterSet = () => camera.Current = new CameraViewReading(2, 2, 3);
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(3);
        Assert.Equal(InstrumentViewOutcome.Unknown, session.Outcome);
        Assert.True(session.Verified);
        camera.Writes.Clear();

        Assert.False(await switcher.RestoreAsync(session));
        Assert.Empty(camera.Writes);
    }

    [Fact]
    public async Task RestoreAsync_WhenTheEntryWasAnUnverifiedUnknown_ReportsSuccess()
    {
        // The companion case: the camera never became readable at all, so EnterAsync's own
        // "Could not confirm the cockpit view switch" already covered it for the pilot —
        // RestoreAsync must stay silent rather than raise a second, false alarm.
        var camera = new FakeCamera { Current = null };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(3);
        Assert.Equal(InstrumentViewOutcome.Unknown, session.Outcome);
        Assert.False(session.Verified);
        camera.Writes.Clear();

        Assert.True(await switcher.RestoreAsync(session));
        Assert.Empty(camera.Writes);
    }

    [Fact]
    public async Task RestoreAsync_AThrowingWrite_DoesNotEscape()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 7) };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(0);
        camera.ThrowOnSet = true;

        Assert.False(await switcher.RestoreAsync(session));
    }

    [Fact]
    public async Task RestoreAsync_AThrowingRead_DoesNotEscape()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 7) };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(0);
        camera.ThrowOnRead = true;

        // A throwing read-back must not stop the restore WRITE from being attempted — only the
        // verification poll that follows it fails, over and over, until the cap gives up on it.
        Assert.False(await switcher.RestoreAsync(session));
        Assert.Equal(new[] { (2, 0), (1, 7) }, camera.Writes);
    }
}
