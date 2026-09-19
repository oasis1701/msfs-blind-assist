using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A hold-to-test button must be DOWN for its hold time from the moment the DOWN is WRITTEN,
/// not from the moment it is queued: the CEVENT bus paces one write per MinGapMs, so a DOWN
/// queued behind N pending events lands N gaps later, and a wait measured from enqueue would
/// release it that much early — behind a guard lift or a burst of walker clicks, a 3 s hold
/// could shrink below what the 1 Hz lamp batch needs to show the lights at all.
///
/// And the bus must END clean. Dispose stops the pump, then writes on the calling thread until the
/// drain bound: FIRST the UP of every HELD button (<c>PressAndHoldAsync</c>) whose DOWN the pump
/// wrote, whether the hold was still running when Dispose began or had just ended with its release
/// still queued; then the rest of the queue in order, less the DOWN of a running hold that never left
/// the queue. A pump stuck inside a write gets nothing written beside it. Cancelling the pump and
/// discarding the queue once left a test button pressed whenever the MD-11 was re-selected inside the
/// 3 s hold; draining through the pump then put the release at the TAIL, where a long backlog (an
/// MCDU scratchpad send, 48 ids) pushed it past the drain bound.
/// </summary>
public class Md11EventBusTests
{
    [Fact]
    public void HoldDelay_IsTheHoldItself_WhenTheQueueIsIdle()
    {
        Assert.Equal(3000, Md11EventBus.HoldDelayMs(3000, backlogMs: 0));
    }

    [Fact]
    public void HoldDelay_AddsTheBacklogTheDownWaitsBehind()
    {
        var backlog = 5 * Md11EventBus.MinGapMs;
        Assert.Equal(3000 + backlog, Md11EventBus.HoldDelayMs(3000, backlog));
    }

    [Fact]
    public void HoldDelay_NeverReleasesInsideOnePacingGap()
    {
        // A hold shorter than one gap would let the UP be written in the same pump tick as the DOWN.
        Assert.Equal(Md11EventBus.MinGapMs, Md11EventBus.HoldDelayMs(1, backlogMs: 0));
        Assert.Equal(Md11EventBus.MinGapMs + 120, Md11EventBus.HoldDelayMs(0, backlogMs: 120));
    }

    /// <summary>
    /// Pins the SHAPE of the read-back rule the walker, <c>PressAndHoldAsync</c> and the three
    /// spoken read-backs now share: a read-back after a QUEUED click waits the aircraft's settle
    /// from the moment the click is WRITTEN, which is the settle plus whatever backlog the click
    /// waits behind. An idle queue therefore costs the pilot nothing extra. (What actually removed
    /// the false "Ground spoilers did not arm." is reading on DELIVERY rather than after a fixed
    /// sleep — see the ArmReadBack / TuneReadBack rows; today's backlogs are at most a couple of
    /// events, the MCDU scratchpad send being the one real burst.)
    /// </summary>
    [Theory]
    [InlineData(700, 0, 700)]
    [InlineData(700, 5 * Md11EventBus.MinGapMs, 700 + 5 * Md11EventBus.MinGapMs)]
    [InlineData(0, 120, 120)]
    public void ReadBackDelay_IsTheSettleMeasuredFromTheClicksWrite(int settleMs, int backlogMs, int expected)
    {
        Assert.Equal(expected, Md11EventBus.ReadBackDelayMs(settleMs, backlogMs));
    }

    // ---- Dispose: drain and release -------------------------------------------------------

    /// <summary>Records every calc string the pump writes; ids parsed off the "{seq} 0 * {id} (>L:CEVENT)" shape.</summary>
    private sealed class Recorder
    {
        private readonly List<string> _lines = new();

        public void Write(string rpn) { lock (_lines) _lines.Add(rpn); }

        public IReadOnlyList<int> Ids
        {
            get
            {
                lock (_lines)
                    return _lines.Where(l => l.EndsWith(" (>L:CEVENT)", StringComparison.Ordinal))
                                 .Select(l => int.Parse(l.Split(' ')[3]))
                                 .ToList();
            }
        }

        public int CountOf(int id) => Ids.Count(i => i == id);

        public async Task<bool> WaitFor(int id, int timeoutMs = 2000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                if (CountOf(id) > 0) return true;
                await Task.Delay(10);
            }
            return false;
        }
    }

    private static Md11Control TestButton(int? down, int up)
    {
        var events = new Dictionary<string, int> { ["LEFT_BUTTON_UP"] = up };
        if (down is > 0) events["LEFT_BUTTON_DOWN"] = down.Value;
        return new Md11Control { NodeId = "TEST_BT", Kind = Md11Kinds.Button, Events = events };
    }

    [Fact]
    public void Dispose_WritesEveryQueuedId_BeforeStoppingThePump()
    {
        var rec = new Recorder();
        var bus = new Md11EventBus(rec.Write);
        bus.Fire(11);
        bus.Fire(12);
        bus.Fire(13);

        bus.Dispose();

        // Drained in order, not discarded: a walk's last click and a held button's UP ride this queue.
        Assert.Equal(new[] { 11, 12, 13 }, rec.Ids);
    }

    [Fact]
    public async Task Dispose_ReleasesAHeldButton_AndTheLateHoldDoesNotReleaseItAgain()
    {
        var rec = new Recorder();
        var bus = new Md11EventBus(rec.Write);
        var hold = bus.PressAndHoldAsync(TestButton(down: 100, up: 101), holdMs: 400);
        Assert.True(await rec.WaitFor(100));      // the DOWN is on the aircraft
        Assert.Equal(0, rec.CountOf(101));        // and the hold is still running

        bus.Dispose();

        Assert.Equal(1, rec.CountOf(101));        // released by Dispose, not lost with the pump
        await hold;                               // the hold outlives the bus without throwing
        Assert.Equal(1, rec.CountOf(101));        // and does not write a second UP
    }

    /// <summary>
    /// A button with no DOWN was never pressed, so it can never be released — on a LIVE bus, which
    /// is where the property has to hold. The disposal case below cannot pin it: there, Fire is
    /// disposal-tolerant, so the UP is absent whether or not the hold tried to write it.
    /// </summary>
    [Fact]
    public async Task AHoldWithNoDown_WritesNeitherHalf()
    {
        var rec = new Recorder();
        using var bus = new Md11EventBus(rec.Write);

        await bus.PressAndHoldAsync(TestButton(down: null, up: 201), holdMs: 50);

        Assert.False(await rec.WaitFor(201, 300),
            "an UP was written for a button that was never pressed — the aircraft sees a release with no press");
    }

    [Fact]
    public async Task Dispose_DoesNotReleaseAButtonThatWasNeverPressed()
    {
        var rec = new Recorder();
        var bus = new Md11EventBus(rec.Write);
        var hold = bus.PressAndHoldAsync(TestButton(down: null, up: 201), holdMs: 100);

        bus.Dispose();

        await hold;                               // no DOWN was queued, so no UP is owed — and no throw
        Assert.Equal(0, rec.CountOf(201));
    }

    /// <summary>
    /// A GUARD on the invariant above, not a reproduction of the race it describes. The window the
    /// fix closed is a few instructions wide — inside the single <c>lock (_held)</c> that
    /// <c>TrackHeldAndFireDown</c> and <c>ReleaseHeldAndFireUp</c> take to register or take back
    /// and queue, and that <c>Dispose</c> takes to flip <c>_closed</c> — and nothing outside the bus
    /// can land inside it deterministically; doing that would need a production test seam planted
    /// exactly where the fix removed the gap (the old two-step register-then-queue this class used
    /// to have). Measured on this machine, all 12 rounds below run in around 145 ms total — far under
    /// even one <see cref="Md11EventBus.MinGapMs"/> write — so Dispose's close wins every round
    /// before a single hold reaches registration: the loop never actually provokes the
    /// interleaving. Its value is that across many attempts it can NEVER observe a button left held,
    /// not that it forces the race open. The real correctness argument is the LOCK'S SCOPE:
    /// register-and-queue-DOWN and take-back-and-queue-UP both run inside <c>lock (_held)</c>, and
    /// Dispose takes that same lock to close the table, so whichever wins, the two can never
    /// interleave. Since 2026-09-11 a hold that ends after the close leaves its entry to Dispose,
    /// which writes the UP only behind a DOWN the pump actually wrote and takes a DOWN still queued
    /// back out with nothing owed — so a hold may show NEITHER half, and never an UP without its
    /// DOWN. That is what the assertions below pin, whether or not any round lands in the gap.
    /// HoldsPerRound is kept small (down from an earlier 32) so that even the practically
    /// unreachable worst case — every hold registering before the close — drains well inside
    /// <see cref="Md11EventBus.DrainTimeoutMs"/> instead of discarding a queued write past that
    /// bound and flaking the assertions over a slow drain rather than a real bug.
    /// </summary>
    [Fact]
    public async Task AHoldRacingDispose_NeverLeavesItsButtonHeld()
    {
        const int Rounds = 12, HoldsPerRound = 6;

        for (int round = 0; round < Rounds; round++)
        {
            var rec = new Recorder();
            var bus = new Md11EventBus(rec.Write);
            var holds = Enumerable.Range(0, HoldsPerRound)
                .Select(i => Task.Run(() => bus.PressAndHoldAsync(TestButton(down: 400 + i, up: 500 + i), holdMs: 1)))
                .ToArray();

            bus.Dispose();                        // races every registration above
            await Task.WhenAll(holds);

            var ids = rec.Ids.ToList();
            for (int i = 0; i < HoldsPerRound; i++)
            {
                int down = ids.IndexOf(400 + i), up = ids.IndexOf(500 + i);
                if (down < 0 && up < 0) continue;              // refused outright: neither half written
                Assert.True(down >= 0, $"round {round}: UP {500 + i} was written for a DOWN that never was");
                Assert.True(up > down, $"round {round}: DOWN {400 + i} was written with no UP behind it — button left held");
            }
        }
    }

    /// <summary>
    /// The guarded hold-to-test button (MD11_OVHD_HYD_HYD_TEST_BT) lifts its cover on a pool
    /// thread first, so its DOWN can be queued AFTER Dispose has already swept the held table.
    /// Once the bus has closed, a hold must write neither half — a DOWN accepted after the sweep
    /// would leave the button held with no UP owed to anyone.
    /// </summary>
    [Fact]
    public async Task AHoldThatStartsAfterDispose_WritesNeitherHalf()
    {
        var rec = new Recorder();
        var bus = new Md11EventBus(rec.Write);

        bus.Dispose();
        await bus.PressAndHoldAsync(TestButton(down: 300, up: 301), holdMs: 50);

        Assert.Equal(0, rec.CountOf(300));
        Assert.Equal(0, rec.CountOf(301));
    }

    // ---- Dispose: release first, never ahead of the DOWN ------------------------------------

    /// <summary>
    /// A recorder that parks the PUMP inside its first write of <c>gateId</c> — how these tests hold
    /// the pump mid-backlog without a single sleep. The parked write returns only once the test has
    /// queued what it wants behind it (<see cref="BacklogQueued"/>) AND the bus reports nothing
    /// pending: with the pump parked here, nothing but <c>Dispose</c> taking the queue off the
    /// cancelled pump can empty it, so the pump is released exactly after Dispose has stopped it.
    /// <see cref="Seal"/>, called right after Dispose returns, counts any later write as a violation.
    /// </summary>
    private sealed class GatedRecorder
    {
        private readonly List<int> _ids = new();
        private readonly int _gateId;
        private bool _sealed;

        public GatedRecorder(int gateId) => _gateId = gateId;

        public Md11EventBus? Bus;
        public readonly ManualResetEventSlim Entered = new();
        public readonly ManualResetEventSlim BacklogQueued = new();
        public int WritesAfterSeal;

        public void Write(string rpn)
        {
            var id = int.Parse(rpn.Split(' ')[3]);
            if (id == _gateId && !Entered.IsSet)
            {
                Entered.Set();
                BacklogQueued.Wait(TimeSpan.FromSeconds(5));
                SpinWait.SpinUntil(() => Bus is { } bus && bus.Pending == 0, TimeSpan.FromSeconds(5));
            }
            lock (_ids)
            {
                if (_sealed) WritesAfterSeal++;
                _ids.Add(id);
            }
        }

        public void Seal() { lock (_ids) _sealed = true; }

        public IReadOnlyList<int> Ids { get { lock (_ids) return _ids.ToList(); } }
    }

    /// <summary>
    /// B2: a held button whose DOWN has reached the aircraft is released by the FIRST write Dispose
    /// makes — ahead of a 40-id backlog (an MCDU scratchpad send is 48). Drained through the pump,
    /// the UP sat at the tail, the drain bound (sixteen ids) dropped it, and a 3 s test button was
    /// left held in the aircraft. And the release never goes out ahead of its own DOWN.
    /// </summary>
    [Fact]
    public async Task Dispose_ReleasesAPressedHoldFirst_AheadOfTheBacklog_NeverAheadOfItsDown()
    {
        var rec = new GatedRecorder(gateId: 100);   // the pump is parked INSIDE the DOWN's write
        var bus = new Md11EventBus(rec.Write);
        rec.Bus = bus;
        var hold = bus.PressAndHoldAsync(TestButton(down: 100, up: 101), holdMs: 1500);
        Assert.True(rec.Entered.Wait(TimeSpan.FromSeconds(5)), "the pump never took the DOWN");

        var backlog = Enumerable.Range(1000, 40).ToArray();
        foreach (var id in backlog) bus.Fire(id);
        rec.BacklogQueued.Set();

        bus.Dispose();
        rec.Seal();
        await hold;                                  // its own release comes after the close: refused

        var ids = rec.Ids;
        Assert.Equal(new[] { 100, 101 }, ids.Take(2).ToArray());       // the DOWN, then its UP: Dispose's first write
        Assert.Equal(1, ids.Count(i => i == 101));
        var drained = ids.Skip(2).ToArray();
        Assert.NotEmpty(drained);                                       // the backlog still drains behind the release…
        Assert.Equal(backlog.Take(drained.Length).ToArray(), drained);  // …in FIFO order, a prefix of what was queued
        Assert.Equal(0, rec.WritesAfterSeal);                           // and nothing lands after Dispose returned
    }

    /// <summary>
    /// The same release for a hold that has already ENDED. Its UP was queued when the hold ended — at
    /// the TAIL, behind a burst queued during the hold — so the held table no longer has it; but its
    /// DOWN reached the aircraft, so it is owed exactly like a running hold's, and it is the FIRST write
    /// Dispose makes, once. Left in FIFO order it sat behind 39 ids, the drain bound (sixteen) dropped
    /// it, and the test button stayed pressed in the aircraft.
    /// </summary>
    [Fact]
    public async Task Dispose_ReleasesAHoldThatJustEnded_First_ItsUpQueuedBehindTheBacklog()
    {
        var burst = Enumerable.Range(1000, 40).ToArray();   // an MCDU scratchpad send is 48
        var rec = new GatedRecorder(gateId: burst[0]);      // the pump is parked inside the burst's FIRST write
        var bus = new Md11EventBus(rec.Write);
        rec.Bus = bus;
        var hold = bus.PressAndHoldAsync(TestButton(down: 100, up: 101), holdMs: 1500);
        Assert.True(SpinWait.SpinUntil(() => rec.Ids.Contains(100), TimeSpan.FromSeconds(5)), "the pump never wrote the DOWN");

        foreach (var id in burst) bus.Fire(id);             // queued DURING the hold, after its DOWN reached the aircraft
        Assert.True(rec.Entered.Wait(TimeSpan.FromSeconds(5)), "the pump never took the burst");
        await hold;                                          // the hold ENDS: its UP joins the queue behind the burst
        rec.BacklogQueued.Set();

        bus.Dispose();
        rec.Seal();

        var ids = rec.Ids;
        Assert.Equal(1, ids.Count(i => i == 101));                            // released, once — not dropped past the drain bound
        Assert.Equal(new[] { 100, burst[0], 101 }, ids.Take(3).ToArray());   // the DOWN, the parked write, then the UP: Dispose's first
        var drained = ids.Skip(3).ToArray();
        Assert.NotEmpty(drained);                                             // the burst still drains behind the release…
        Assert.Equal(burst.Skip(1).Take(drained.Length).ToArray(), drained);  // …in FIFO order
        Assert.Equal(0, rec.WritesAfterSeal);
    }

    /// <summary>
    /// A hold whose DOWN never left the queue pressed nothing, so it is owed nothing: Dispose takes the
    /// DOWN back out and writes neither half, while everything else keeps its order. Drained through
    /// the pump, the aircraft saw a press AND a release for a button the hold never reached.
    /// </summary>
    [Fact]
    public async Task Dispose_WritesNeitherHalf_OfAHoldWhoseDownNeverLeftTheQueue()
    {
        var rec = new GatedRecorder(gateId: 500);   // the pump is parked inside an EARLIER write
        var bus = new Md11EventBus(rec.Write);
        rec.Bus = bus;
        bus.Fire(500);
        Assert.True(rec.Entered.Wait(TimeSpan.FromSeconds(5)), "the pump never took the first id");

        var hold = bus.PressAndHoldAsync(TestButton(down: 100, up: 101), holdMs: 1500);   // DOWN queued behind it
        bus.Fire(600);
        bus.Fire(601);
        rec.BacklogQueued.Set();

        bus.Dispose();
        rec.Seal();
        await hold;

        Assert.Equal(new[] { 500, 600, 601 }, rec.Ids);
        Assert.Equal(0, rec.WritesAfterSeal);
    }

    /// <summary>
    /// The drop above is for a hold still REGISTERED. A hold that already ended has its DOWN and its
    /// UP both still queued, in order, and Dispose writes both — dropping the DOWN there would leave
    /// its UP a release with no press ahead of it.
    /// </summary>
    [Fact]
    public async Task Dispose_WritesAReleasedHoldStillQueued_DownThenUp()
    {
        var rec = new GatedRecorder(gateId: 500);
        var bus = new Md11EventBus(rec.Write);
        rec.Bus = bus;
        bus.Fire(500);
        Assert.True(rec.Entered.Wait(TimeSpan.FromSeconds(5)), "the pump never took the first id");

        await bus.PressAndHoldAsync(TestButton(down: 100, up: 101), holdMs: 0);   // ends in one pacing gap: UP queued behind its DOWN
        rec.BacklogQueued.Set();

        bus.Dispose();
        rec.Seal();

        Assert.Equal(new[] { 500, 100, 101 }, rec.Ids);
        Assert.Equal(0, rec.WritesAfterSeal);
    }
}
