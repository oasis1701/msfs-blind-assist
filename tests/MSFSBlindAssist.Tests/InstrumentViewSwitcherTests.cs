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

        /// <summary>
        /// False models a sim nothing reaches: SimConnect down, so the write never leaves the
        /// process. Distinct from <see cref="HonoursWrites"/>, which models a write that WAS sent
        /// and the sim declined — the distinction the restore's "did we move the pilot" decision
        /// now rests on.
        /// </summary>
        public bool DispatchesWrites = true;

        /// <summary>False models only the TYPE register refusing dispatch, so the index must not follow it.</summary>
        public bool DispatchesViewType = true;

        /// <summary>
        /// Per-view-type index ceiling, modelling the sim's PAIR validation: a TYPE write is
        /// refused when the index register currently holds a value out of range for the type
        /// being written. Measured on the live PMDG 737-800 (2026-09-20): sitting on instrument
        /// view index 7 with CAMERA VIEW TYPE AND INDEX MAX:1 = 6, writing type 1 was refused
        /// every time, with a 3 s settle and no other traffic; dropping the index to 3 first made
        /// the identical type write succeed. Null = this fake does not model the ceiling.
        /// </summary>
        public Dictionary<int, int>? MaxIndexByType;

        /// <summary>Reads that return nothing, counted down — a timed-out camera read, not a reverted camera.</summary>
        public int NullReads;

        /// <summary>After this many further reads, every read returns nothing. int.MaxValue = never.</summary>
        public int GoodReadsBeforeNull = int.MaxValue;
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

        /// <summary>Every register write in order, so a test can pin that the restore SPACED the pair.</summary>
        public readonly List<(string Register, int Value)> RegisterWrites = new();

        // Models the sim accepting a write, reading back correct, and only then reverting — the
        // live failure the first restore shipped with (iFly 737 MAX8, 2026-09-20). Arm it AFTER
        // the entry so only the restore meets it.
        public bool RevertArmed;
        public int RevertAfterReads;
        public CameraViewReading? RevertTo;
        private int _readsSinceArmed;

        public Task<CameraViewReading?> ReadAsync(int timeoutMs)
        {
            if (ThrowOnRead) throw new InvalidOperationException("SimConnect down");
            Clock += ReadCostMs;
            if (NullReads > 0)
            {
                NullReads--;
                return Task.FromResult<CameraViewReading?>(null);
            }
            if (GoodReadsBeforeNull <= 0) return Task.FromResult<CameraViewReading?>(null);
            if (GoodReadsBeforeNull != int.MaxValue) GoodReadsBeforeNull--;
            if (RevertArmed && _readsSinceArmed++ >= RevertAfterReads)
            {
                Current = RevertTo;
                RevertArmed = false;
            }
            return Task.FromResult(Current);
        }

        public bool Set(int viewType, int viewIndex)
        {
            if (ThrowOnSet) throw new InvalidOperationException("SimConnect down");
            if (!DispatchesWrites) return false;
            Writes.Add((viewType, viewIndex));
            if (HonoursWrites && Current is { } c)
                Current = c with { ViewType = viewType, ViewIndex = ClampIndexTo ?? viewIndex };
            AfterSet?.Invoke();
            return true;
        }

        public bool SetViewType(int viewType)
        {
            if (ThrowOnSet) throw new InvalidOperationException("SimConnect down");
            if (!DispatchesWrites || !DispatchesViewType) return false;
            RegisterWrites.Add(("type", viewType));
            // The write is DISPATCHED either way -- the sim simply declines to act on a pair it
            // considers out of range, exactly as it does for a clamped index.
            if (HonoursWrites && Current is { } c && PairIsInRange(viewType, c.ViewIndex))
                Current = c with { ViewType = viewType };
            AfterSet?.Invoke();
            return true;
        }

        private bool PairIsInRange(int viewType, int viewIndex)
            => MaxIndexByType is not { } ceilings
               || !ceilings.TryGetValue(viewType, out int max)
               || viewIndex <= max;

        public bool SetViewIndex(int viewIndex)
        {
            if (ThrowOnSet) throw new InvalidOperationException("SimConnect down");
            if (!DispatchesWrites) return false;
            RegisterWrites.Add(("index", viewIndex));
            if (HonoursWrites && Current is { } c)
                Current = c with { ViewIndex = ClampIndexTo ?? viewIndex };
            AfterSet?.Invoke();
            return true;
        }
    }

    private static (InstrumentViewSwitcher Switcher, List<int> Delays, FakeCamera Camera) Make(
        FakeCamera camera, CameraHome? home = null)
    {
        var delays = new List<int>();
        camera.Clock = 0;
        var switcher = new InstrumentViewSwitcher(
            camera,
            delay: ms => { delays.Add(ms); camera.Clock += ms; return Task.CompletedTask; },
            now: () => camera.Clock,
            // Its own memory, never the app-wide one: a test that owed a home would otherwise
            // leak it into every later test in the class.
            home: home ?? new CameraHome());
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

        // The entry is one back-to-back Set; the restore is the SPACED sequence, and it drops the
        // index to a universally valid one first -- see NeutralViewIndex.
        Assert.Equal(new[] { (2, 0) }, camera.Writes);
        Assert.Equal(new[] { ("index", 0), ("type", 1), ("index", 7) }, camera.RegisterWrites);
        Assert.Equal(new CameraViewReading(2, 1, 7), camera.Current);
    }

    [Fact]
    public async Task RestoreAsync_PutsAQuickviewBack()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 3, 2) };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(1);

        Assert.True(await switcher.RestoreAsync(session));

        Assert.Equal(new[] { (2, 1) }, camera.Writes);
        Assert.Equal(new[] { ("index", 0), ("type", 3), ("index", 2) }, camera.RegisterWrites);
    }

    [Fact]
    public async Task RestoreAsync_SpacesTheTwoWrites_AndConfirmsTheViewHeld()
    {
        // Both delays are the fix for a live failure (iFly 737 MAX8, 2026-09-20): written on one
        // frame the type write is refused, and the refused write reads back as success for a
        // moment, so an immediate poll reported a restore that had not happened.
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 7) };
        var (switcher, delays, _) = Make(camera);
        var session = await switcher.EnterAsync(0);
        delays.Clear();

        Assert.True(await switcher.RestoreAsync(session));

        // A gap after the neutral index, a gap after the type, then the confirm settle. There is
        // no FOURTH delay: the
        // restore used to settle before its poll AND again before the confirm, outlasting the same
        // ~150 ms transient twice -- and the first of those reused DefaultSettleMs, the "render a
        // frame before a screenshot" constant, four lines below the doc saying not to.
        Assert.Equal(new[] { 250, 250, 250 }, delays);
    }

    [Fact]
    public async Task RestoreAsync_WhenTheWriteVerifiesThenReverts_ReportsFailure()
    {
        // THE live defect. The sim took the write, read back correct, then snapped the camera
        // back to the instrument view; the shipped code matched that transient and announced
        // nothing, so the pilot was left on the instrument view in silence.
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 7) };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(0);

        camera.RevertArmed = true;
        camera.RevertAfterReads = 1;                                // first read agrees, then it reverts
        camera.RevertTo = new CameraViewReading(2, 2, 0);

        Assert.False(await switcher.RestoreAsync(session));
        Assert.Equal(new[] { ("index", 0), ("type", 1), ("index", 7) }, camera.RegisterWrites);
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
        Assert.Equal(new[] { ("index", 0), ("type", 1), ("index", 7) }, camera.RegisterWrites);
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
        Assert.Empty(camera.RegisterWrites);
    }

    [Fact]
    public async Task RestoreAsync_AfterAnExternalCamera_WritesNothing_AndReportsSuccess()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(3, 0, 0) };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(2);

        Assert.True(await switcher.RestoreAsync(session));
        Assert.Empty(camera.Writes);
        Assert.Empty(camera.RegisterWrites);
    }

    [Fact]
    public async Task RestoreAsync_WhenTheWriteLandedButNothingCouldBeRead_ReportsFailure()
    {
        // THE silent strand, and why the restore no longer reads Verified as a proxy for "the
        // write landed". The camera definition can fail to register on its own -- it has its own
        // catch -- while writes keep landing, because SetSimVar builds its own temporary data
        // definition and never touches the camera one. Every read then returns nothing, so the
        // outcome is Unknown and unverified, yet the camera really did move. The old rule called
        // that "nothing moved" and returned true, leaving the pilot on the instrument view on
        // every read of the session, in silence.
        var camera = new FakeCamera { Current = null };
        var (switcher, _, _) = Make(camera);

        var session = await switcher.EnterAsync(3);

        Assert.Equal(InstrumentViewOutcome.Unknown, session.Outcome);
        Assert.False(session.Verified);
        Assert.True(session.Moved);
        Assert.False(await switcher.RestoreAsync(session));
    }

    [Fact]
    public async Task RestoreAsync_WhenNoWriteWasEverDispatched_ReportsSuccess()
    {
        // The companion, and why the fix is the DISPATCH fact rather than "always warn on
        // Unknown": with SimConnect down nothing left the process, so the camera is exactly where
        // the pilot put it. Warning here would be a false alarm over nothing that moved.
        var camera = new FakeCamera { Current = null, DispatchesWrites = false };
        var (switcher, _, _) = Make(camera);

        var session = await switcher.EnterAsync(3);

        Assert.Equal(InstrumentViewOutcome.Unknown, session.Outcome);
        Assert.False(session.Moved);
        Assert.True(await switcher.RestoreAsync(session));
        Assert.Empty(camera.Writes);
        Assert.Empty(camera.RegisterWrites);
    }

    [Fact]
    public async Task EnterAsync_RetriesTheFirstReadOnce_SoOneTimeoutIsNotAnUnknown()
    {
        // A single timed-out read used to cost the pilot the way back for the whole read, and
        // could make the app confess to a move that never happened. The retry turns it into an
        // ordinary Switch with a reading to restore to.
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 7), NullReads = 1 };
        var (switcher, _, _) = Make(camera);

        var session = await switcher.EnterAsync(0);

        Assert.Equal(InstrumentViewOutcome.Switch, session.Outcome);
        Assert.Equal(new CameraViewReading(2, 1, 7), session.RestoreTarget);
    }

    [Fact]
    public async Task RestoreAsync_WhenTheConfirmingReadTimesOut_DoesNotReportFailure()
    {
        // A read that returns nothing is a SimConnect timeout, a disconnect or an aircraft
        // switch -- none of which say anything about the camera. The poll has already seen it
        // home. Scoring the unreadable confirm as a failure is how a CORRECT restore came to
        // announce "Could not return to your previous view.", the false alarm that teaches a
        // pilot to ignore the real one.
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 7) };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(0);

        // One good read for the restore's poll to match on, then nothing for both confirm reads.
        camera.GoodReadsBeforeNull = 1;

        Assert.True(await switcher.RestoreAsync(session));
    }

    [Fact]
    public async Task RestoreAsync_WhenTheTypeWriteIsNotDispatched_DoesNotSendTheIndexAlone()
    {
        // Writing the index alone slides the camera to THAT instrument view -- the documented
        // worse case. If the type write never went out, the index must not follow it.
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 7) };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(0);
        camera.DispatchesViewType = false;

        Assert.False(await switcher.RestoreAsync(session));

        // The neutral index goes out -- it is what makes the type write legal -- and then nothing
        // else: an undispatched write is not recorded, and the pilot's OWN index must not follow
        // a type write that never left the process, or it lands in the instrument type and slides
        // them to an instrument view they never chose.
        Assert.Equal(new[] { ("index", 0) }, camera.RegisterWrites);
    }

    [Fact]
    public async Task AFailedRestore_LeavesTheNextReadAimingAtTheOwedHome()
    {
        // The strand made permanent, which a per-read memory structurally cannot see: the failed
        // restore leaves the camera on the instrument view, so the NEXT read reads THAT as "where
        // the pilot was", puts them back on it and reports success. The pilot is never told again
        // and the app can never get them home. With the home owed, the next read aims at the
        // cabin view and finishes the job.
        var home = new CameraHome();
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 7) };
        var (switcher, _, _) = Make(camera, home);
        var first = await switcher.EnterAsync(0);
        camera.HonoursWrites = false;                        // the restore will not take

        Assert.False(await switcher.RestoreAsync(first));
        Assert.Equal(new CameraViewReading(2, 1, 7), home.Owed);

        camera.HonoursWrites = true;
        camera.Current = new CameraViewReading(2, 2, 0);     // stranded on the instrument view
        var second = await switcher.EnterAsync(0);

        Assert.Equal(new CameraViewReading(2, 1, 7), second.RestoreTarget);
        Assert.True(await switcher.RestoreAsync(second));
        Assert.Null(home.Owed);
    }

    [Fact]
    public async Task ThePilotTakingTheirOwnView_SettlesTheOwedHome()
    {
        // The other way a debt ends: the pilot got themselves out. Dragging them back to a stale
        // home would be the same defect pointed the other way.
        var home = new CameraHome();
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 7) };
        var (switcher, _, _) = Make(camera, home);
        var first = await switcher.EnterAsync(0);
        camera.HonoursWrites = false;
        Assert.False(await switcher.RestoreAsync(first));

        camera.HonoursWrites = true;
        camera.Current = new CameraViewReading(2, 3, 2);     // the pilot picked a quickview
        var second = await switcher.EnterAsync(0);

        Assert.Equal(new CameraViewReading(2, 3, 2), second.RestoreTarget);
        Assert.Null(home.Owed);
    }

    [Fact]
    public async Task RestoreAsync_GetsHomeWhenTheInstrumentIndexIsOutOfRangeForThePilotType()
    {
        // THE PMDG 737 case, measured live 2026-09-20. Its PFD/ND read uses instrument view index
        // 7, and CAMERA VIEW TYPE AND INDEX MAX:1 advertises 6 pilot views -- so at the moment the
        // restore writes the TYPE, the index register holds 7, which is out of range for type 1.
        // The sim validates the PAIR and refuses the type write. Confirmed with a 3 s settle and
        // no other traffic, so it is not the ~150 ms transient and not a timing confound.
        //
        // The old two-write restore therefore left the camera in the instrument TYPE and then
        // wrote the pilot's index into it -- sliding the pilot to an instrument view they never
        // chose, on every single Alt+P and Alt+N read of that aircraft. Dropping the index to a
        // value valid for both types first is what makes the pair legal for the type write.
        var camera = new FakeCamera
        {
            Current = new CameraViewReading(2, 1, 7),
            MaxIndexByType = new Dictionary<int, int> { [1] = 6, [2] = 15 },
        };
        var (switcher, _, _) = Make(camera);
        var session = await switcher.EnterAsync(7);
        Assert.Equal(new CameraViewReading(2, 2, 7), camera.Current);   // on the instrument view

        Assert.True(await switcher.RestoreAsync(session));

        Assert.Equal(new CameraViewReading(2, 1, 7), camera.Current);   // and home again
        Assert.Equal(
            new[] { ("index", 0), ("type", 1), ("index", 7) },
            camera.RegisterWrites);
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
        Assert.Equal(new[] { ("index", 0), ("type", 1), ("index", 7) }, camera.RegisterWrites);
    }

    [Fact]
    public void TheRestoreTimings_AreTheMeasuredNumbers()
    {
        Assert.Equal(250, InstrumentViewSwitcher.DefaultWriteGapMs);
        Assert.Equal(250, InstrumentViewSwitcher.DefaultHoldConfirmMs);
    }
}
