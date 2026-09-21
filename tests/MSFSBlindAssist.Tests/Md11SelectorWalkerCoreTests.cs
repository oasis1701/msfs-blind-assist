using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The walker's step protocol against a fake switch the walker cannot see into: only a state var,
/// only after the aircraft's own lag, only through the same IO seam production uses. These pin
/// the behaviour a blind pilot experiences at the combo: how many clicks a selection costs, that
/// a wrong polarity guess is learned once and persisted, that an end stop is not mistaken for an
/// inhibited control, and that a lagging read-back is not mistaken for "no movement".
/// </summary>
public class Md11SelectorWalkerCoreTests : IDisposable
{
    private const int Left = 90248, Right = 90249;

    // The walker's polarity hooks are process-wide statics that production wires to the real
    // settings file (TFDiMD11Definition.Attach). Pin them per test so no walk here can persist a
    // TEST_ id into the developer's settings, and so a concurrently running test class cannot
    // clobber a test that pins its own.
    public Md11SelectorWalkerCoreTests()
    {
        Md11SelectorWalker.LoadPolarity = _ => null;
        Md11SelectorWalker.SavePolarity = (_, _) => { };
    }

    public void Dispose()
    {
        Md11SelectorWalker.LoadPolarity = null;
        Md11SelectorWalker.SavePolarity = null;
    }

    /// <summary>A three-position switch (0 Off / 1 Auto / 2 On) with a virtual clock.</summary>
    private sealed class FakeSwitch
    {
        public int Position;
        public int Max = 2;
        public bool LeftIncreases = true;       // the walker's conventional guess is right by default
        public bool Inhibited;
        public int LagReads;                    // reads after a click that still show the old value
        public int DetentsPerClick = 1;         // 2 = the click lands twice
        public bool Fresh = true;               // false = the legacy cache-poll protocol
        public bool BlockUp;                    // clicks in the increasing direction are ignored (a one-way inhibit)
        public bool ToggleOnLeft;               // TFDi's single-click template: Left flips 0 <-> Max
        public bool FreshTimesOut;              // every fresh read times out (null); the cache still answers
        public int PendingQueue;                // CEVENTs queued ahead of ours on the bus
        public int ReadFreshCalls;
        public int RequestReadCalls;
        public readonly List<int> Clicks = new();
        public readonly List<long> ClickTimes = new();
        public long Clock;                      // virtual milliseconds
        public long FirstReadAt = -1;
        private int _visible;
        private int _pendingReads;

        public FakeSwitch(int start) { Position = start; _visible = start; }

        private void Fire(int id)
        {
            Clicks.Add(id);
            ClickTimes.Add(Clock);
            if (Inhibited) return;
            if (ToggleOnLeft)
            {
                if (id == Left) { Position = Position == 0 ? Max : 0; _pendingReads = LagReads; }
                return;
            }
            var up = (id == Left) == LeftIncreases;
            if (up && BlockUp) return;
            Position = Math.Clamp(Position + (up ? 1 : -1) * DetentsPerClick, 0, Max);
            _pendingReads = LagReads;
        }

        private double? Read()
        {
            if (FirstReadAt < 0) FirstReadAt = Clock;
            if (_pendingReads > 0) { _pendingReads--; return _visible; }
            _visible = Position;
            return _visible;
        }

        public Md11WalkIo Io() => new()
        {
            ReadFresh = _ => { ReadFreshCalls++; return Task.FromResult(FreshTimesOut ? (double?)null : Read()); },
            ReadCached = () => _visible,
            RequestRead = () => { RequestReadCalls++; Read(); },
            Fire = Fire,
            Pending = () => PendingQueue,
            Delay = (ms, _) => { Clock += ms; return Task.CompletedTask; },
            Now = () => Clock,
            FreshReads = Fresh,
        };
    }

    private static string NewId() => "TEST_" + Guid.NewGuid().ToString("N");

    private static Md11Control Switch(string id) => new()
    {
        NodeId = id,
        Kind = Md11Kinds.Switch,
        StateVar = id,
        ValueMap = new Dictionary<string, string> { ["0"] = "Off", ["1"] = "Auto", ["2"] = "On" },
        Events = new Dictionary<string, int> { ["LEFT_BUTTON_DOWN"] = Left, ["RIGHT_BUTTON_DOWN"] = Right },
    };

    /// <summary>TFDi's single-click template: one LEFT_BUTTON_DOWN event, two positions.</summary>
    private static Md11Control SingleClick(string id, string off = "Off", string on = "Nav") => new()
    {
        NodeId = id,
        Kind = Md11Kinds.Switch,
        StateVar = id,
        ValueMap = new Dictionary<string, string> { ["0"] = off, ["1"] = on },
        Events = new Dictionary<string, int> { ["LEFT_BUTTON_DOWN"] = Left },
    };

    private static Task<bool> Walk(Md11Control c, double target, FakeSwitch sw)
        => Md11SelectorWalker.WalkCoreAsync(c, target, sw.Io());

    [Fact]
    public async Task ConventionalControl_TwoDetentsUp_CostsExactlyTwoClicks()
    {
        var id = NewId();
        var sw = new FakeSwitch(0);

        Assert.True(await Walk(Switch(id), 2, sw));

        Assert.Equal(new[] { Left, Left }, sw.Clicks);
        Assert.Equal(2, sw.Position);
        Assert.True(Md11SelectorWalker.PolarityFor(id) ?? true);   // never flipped
        Assert.Equal(0, sw.RequestReadCalls);        // the fresh protocol never polls the cache
        Assert.True(sw.ReadFreshCalls > 0);
    }

    [Fact]
    public async Task ConventionalControl_OneDetent_SettlesInWellUnderASecond()
    {
        var sw = new FakeSwitch(0);

        Assert.True(await Walk(Switch(NewId()), 1, sw));

        Assert.Single(sw.Clicks);
        Assert.InRange(sw.Clock, 1, 400);   // one step: a poll or two after the click, no 300 ms settle reads
    }

    [Fact]
    public async Task InvertedControl_MidRange_LearnsOnce_PersistsIt_AndLands()
    {
        var id = NewId();
        var saved = new List<(string Id, bool Conventional)>();
        Md11SelectorWalker.SavePolarity = (n, c) => { if (n == id) saved.Add((n, c)); };
        try
        {
            var sw = new FakeSwitch(1) { LeftIncreases = false };

            Assert.True(await Walk(Switch(id), 2, sw));

            // One wrong-way click (1 -> 0), then the learned direction twice (0 -> 1 -> 2).
            Assert.Equal(new[] { Left, Right, Right }, sw.Clicks);
            Assert.Equal(2, sw.Position);
            Assert.False(Md11SelectorWalker.PolarityFor(id));
            Assert.Equal(new[] { (id, false) }, saved);
        }
        finally { Md11SelectorWalker.SavePolarity = null; }
    }

    [Fact]
    public async Task InvertedControl_AtAnEndStop_ProbesTheOtherWay_ThenLands()
    {
        var id = NewId();
        var sw = new FakeSwitch(0) { LeftIncreases = false };

        Assert.True(await Walk(Switch(id), 2, sw));

        // Left at the bottom stop does nothing (the cap elapses), the probe Right moves toward the
        // target and teaches the polarity, then one more Right lands.
        Assert.Equal(new[] { Left, Right, Right }, sw.Clicks);
        Assert.Equal(2, sw.Position);
        Assert.False(Md11SelectorWalker.PolarityFor(id));
        Assert.True(sw.ClickTimes[1] - sw.ClickTimes[0] >= Md11SelectorWalker.StepCapMs);
    }

    [Fact]
    public async Task InvertedControl_AtTheTopEndStop_ProbesTheOtherWay_ThenLands()
    {
        var id = NewId();
        var sw = new FakeSwitch(2) { LeftIncreases = false };

        Assert.True(await Walk(Switch(id), 0, sw));

        // Wanting down, the conventional guess sends Right; at the top stop under inverted polarity
        // that is "up" and stalls. The probe Left moves 2 -> 1, toward the target: learned. One
        // more Left lands.
        Assert.Equal(new[] { Right, Left, Left }, sw.Clicks);
        Assert.Equal(0, sw.Position);
        Assert.False(Md11SelectorWalker.PolarityFor(id));
    }

    [Fact]
    public async Task InhibitedControl_GivesUpAfterTryingBothDirections_WithoutLearning()
    {
        var id = NewId();
        var sw = new FakeSwitch(0) { Inhibited = true };

        Assert.False(await Walk(Switch(id), 1, sw));

        Assert.Equal(new[] { Left, Right }, sw.Clicks);
        Assert.True(Md11SelectorWalker.PolarityFor(id) ?? true);   // still conventional
    }

    /// <summary>
    /// A stall at mid-range is a dropped or refused click, never a polarity question (a wrong guess
    /// would have moved the control), so the walker must NOT probe the other direction there —
    /// on the engine fire handles that probe would discharge a bottle. It gives up at once, the
    /// polarity untouched, and the control stays where it was.
    /// </summary>
    [Fact]
    public async Task AMidRangeStall_IsADroppedClick_GivesUpWithoutProbing_OrFlipping()
    {
        var id = NewId();
        var sw = new FakeSwitch(1) { BlockUp = true };

        Assert.False(await Walk(Switch(id), 2, sw));

        Assert.Equal(new[] { Left }, sw.Clicks);
        Assert.Equal(1, sw.Position);
        Assert.True(Md11SelectorWalker.PolarityFor(id) ?? true);
    }

    /// <summary>
    /// The APU fire handle's shape as mapped today: two positions {0 Bottle 1, 2 Bottle 2}, walked on
    /// its wheel pair — so every index is an end stop. <paramref name="kind"/> is the only difference
    /// between the tests below, which is what pins the KIND as the discriminator.
    /// </summary>
    private static Md11Control TwoPosition(string id, string kind) => new()
    {
        NodeId = id,
        Kind = kind,
        StateVar = id,
        ValueMap = new Dictionary<string, string> { ["0"] = "Bottle 1", ["2"] = "Bottle 2" },
        Events = new Dictionary<string, int> { ["WHEEL_UP"] = Left, ["WHEEL_DOWN"] = Right },
    };

    /// <summary>
    /// A fire HANDLE never probes, at any index: its walked axis is the bottle discharge, so the
    /// "other" event fired at an end stop can discharge a bottle the pilot never asked for — and on a
    /// two-position handle every index IS an end stop, so the end-stop rule alone always allowed it.
    /// One event, then give up, polarity untouched.
    /// </summary>
    [Theory]
    [InlineData(0, 2)]   // the bottom end stop, walking up
    [InlineData(2, 0)]   // the top end stop, walking down
    public async Task AHandle_StallingAtAnEndStop_FiresOneEvent_NeverTheProbe(int start, int target)
    {
        var id = NewId();
        var sw = new FakeSwitch(start) { Inhibited = true };

        Assert.False(await Walk(TwoPosition(id, Md11Kinds.Handle), target, sw));

        Assert.Single(sw.Clicks);
        Assert.True(Md11SelectorWalker.PolarityFor(id) ?? true);
    }

    /// <summary>
    /// The price of the rule above, pinned so nobody "fixes" it back: a handle whose polarity guess is
    /// wrong cannot learn it at an end stop, because learning it there takes the probe. The walk fails
    /// (SafeWalk then tries the direct write, and speaks "did not move" if that fails too) rather than
    /// risk a discharge.
    /// </summary>
    [Fact]
    public async Task AnInvertedHandle_AtAnEndStop_StillDoesNotProbe_AndLearnsNothing()
    {
        var id = NewId();
        var sw = new FakeSwitch(0) { LeftIncreases = false };

        Assert.False(await Walk(TwoPosition(id, Md11Kinds.Handle), 2, sw));

        Assert.Equal(new[] { Left }, sw.Clicks);
        Assert.Equal(0, sw.Position);
        Assert.True(Md11SelectorWalker.PolarityFor(id) ?? true);
    }

    /// <summary>The same two-position shape as a SWITCH keeps the end-stop probe (existing behaviour): the kind decides, not the position count.</summary>
    [Fact]
    public async Task ATwoPositionSwitch_AtAnEndStop_StillProbesTheOtherWay()
    {
        var sw = new FakeSwitch(0) { Inhibited = true };

        Assert.False(await Walk(TwoPosition(NewId(), Md11Kinds.Switch), 2, sw));

        Assert.Equal(new[] { Left, Right }, sw.Clicks);
    }

    [Fact]
    public async Task LaggingReadBack_IsNotMistakenForNoMovement()
    {
        var sw = new FakeSwitch(0) { LagReads = 4 };   // the aircraft shows the old value for four reads

        Assert.True(await Walk(Switch(NewId()), 1, sw));

        Assert.Equal(new[] { Left }, sw.Clicks);      // exactly one click: no retry, no probe
    }

    [Fact]
    public async Task PersistedInvertedPolarity_IsUsedFromTheFirstClick()
    {
        var id = NewId();
        var saves = 0;
        Md11SelectorWalker.LoadPolarity = n => n == id ? false : null;
        Md11SelectorWalker.SavePolarity = (n, _) => { if (n == id) saves++; };
        try
        {
            var sw = new FakeSwitch(0) { LeftIncreases = false };

            Assert.True(await Walk(Switch(id), 1, sw));

            Assert.Equal(new[] { Right }, sw.Clicks);   // the inverted mapping, straight away
            Assert.Equal(0, saves);                       // nothing new was learned
        }
        finally { Md11SelectorWalker.LoadPolarity = null; Md11SelectorWalker.SavePolarity = null; }
    }

    [Fact]
    public async Task ARecentClickOnTheSameControl_IsAllowedToSettleBeforeTheFirstRead()
    {
        var id = NewId();
        var control = Switch(id);
        var sw = new FakeSwitch(0);
        Assert.True(await Walk(control, 1, sw));
        var lastClick = sw.ClickTimes[^1];

        sw.FirstReadAt = -1;                          // the very next walk on this node
        Assert.True(await Walk(control, 2, sw));

        Assert.True(sw.FirstReadAt >= lastClick + Md11SelectorWalker.ClickSettleMs,
            $"first read at {sw.FirstReadAt}, last click at {lastClick}");
    }

    [Fact]
    public async Task BatchCoveredVar_KeepsTheLegacyProtocol_NoProbeOnNoMovement()
    {
        var id = NewId();
        var sw = new FakeSwitch(0) { LeftIncreases = false, Fresh = false };

        Assert.False(await Walk(Switch(id), 2, sw));

        Assert.Equal(new[] { Left }, sw.Clicks);       // one no-movement ends the walk, as before
        Assert.True(Md11SelectorWalker.PolarityFor(id) ?? true);
        Assert.Equal(0, sw.ReadFreshCalls);          // the legacy protocol never takes a fresh read
        Assert.True(sw.RequestReadCalls > 0);
    }

    [Fact]
    public async Task AClickThatLandsTwice_MovesTowardTheTarget_WithoutFlippingPolarity()
    {
        var id = NewId();
        var sw = new FakeSwitch(0) { DetentsPerClick = 2 };

        Assert.True(await Walk(Switch(id), 2, sw));

        Assert.Equal(new[] { Left }, sw.Clicks);
        Assert.True(Md11SelectorWalker.PolarityFor(id) ?? true);
    }

    [Fact]
    public async Task AnUnreachableDetent_ExhaustsTheBudget_WithoutCorruptingPolarity()
    {
        var id = NewId();
        var sw = new FakeSwitch(0) { DetentsPerClick = 2 };   // 0 <-> 2 only; 1 is unreachable

        Assert.False(await Walk(Switch(id), 1, sw));

        Assert.Equal(24, sw.Clicks.Count);                       // MaxSteps, bounded
        Assert.True(Md11SelectorWalker.PolarityFor(id) ?? true); // direction-based learning never flipped
    }

    [Fact]
    public void OnDetent_PointDetentsOnly()
    {
        var control = Switch(NewId());
        var ordered = Md11SelectorWalker.OrderedValues(control);

        Assert.True(Md11SelectorWalker.OnDetent(control, ordered, 1.0));
        Assert.True(Md11SelectorWalker.OnDetent(control, ordered, 1.005));
        Assert.False(Md11SelectorWalker.OnDetent(control, ordered, 1.3));

        var lever = Md11ControlMap.Load().Controls.First(c => c.NodeId == Md11FlapSystem.LeverKey);
        var leverOrdered = Md11SelectorWalker.OrderedValues(lever);
        Assert.True(Md11SelectorWalker.OnDetent(lever, leverOrdered, 70));    // a point detent
        Assert.False(Md11SelectorWalker.OnDetent(lever, leverOrdered, 50));   // Dial-A-Flap is a range, never "settled" by value alone
    }

    // ---------------------------------------------------------------------------------
    // Single-click toggles (IRS selectors, fuel switches, starters, parking brake, gear lever …)
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task SingleClickSwitch_AlreadyAtTarget_ClicksNothing()
    {
        var sw = new FakeSwitch(1) { Max = 1, ToggleOnLeft = true };

        Assert.True(await Walk(SingleClick(NewId()), 1, sw));

        Assert.Empty(sw.Clicks);
    }

    [Fact]
    public async Task SingleClickSwitch_ThatDiffers_ClicksOnce_AndLands_WithoutLearningPolarity()
    {
        var id = NewId();
        var sw = new FakeSwitch(0) { Max = 1, ToggleOnLeft = true };

        Assert.True(await Walk(SingleClick(id), 1, sw));

        Assert.Equal(new[] { Left }, sw.Clicks);
        Assert.Equal(1, sw.Position);
        Assert.Null(Md11SelectorWalker.PolarityFor(id));   // a toggle has no direction to learn
        Assert.InRange(sw.Clock, 1, 400);                  // one click, confirmed within a poll or two
    }

    [Fact]
    public async Task SingleClickSwitch_BackToOff_ClicksOnce()
    {
        var sw = new FakeSwitch(1) { Max = 1, ToggleOnLeft = true };

        Assert.True(await Walk(SingleClick(NewId()), 0, sw));

        Assert.Equal(new[] { Left }, sw.Clicks);
        Assert.Equal(0, sw.Position);
    }

    /// <summary>
    /// The gear lever's var is the lever's 0-25 TRAVEL while the map says {0 Up, 1 Down}: the
    /// nearest-position rule reads 25 as Down, the click flips it, and the read-back settles on
    /// two agreeing reads because 25 sits on no point detent.
    /// </summary>
    [Fact]
    public async Task GearLikeTravelVar_TogglesUpToDown_AndConfirmsOnceSettled()
    {
        var sw = new FakeSwitch(0) { Max = 25, ToggleOnLeft = true, LagReads = 3 };

        Assert.True(await Walk(SingleClick(NewId(), "Up", "Down"), 1, sw));

        Assert.Equal(new[] { Left }, sw.Clicks);
        Assert.Equal(25, sw.Position);
        Assert.Equal(6, sw.ReadFreshCalls);   // 1 before the click, 3 lagged, then two agreeing reads of 25
        Assert.Equal(400, sw.Clock);          // the two-agreeing-reads settle, not the first index change
    }

    [Fact]
    public async Task SingleClickSwitch_Inhibited_ClicksOnce_ThenReportsFalse_ForTheFallback()
    {
        var sw = new FakeSwitch(0) { Max = 1, ToggleOnLeft = true, Inhibited = true };

        Assert.False(await Walk(SingleClick(NewId()), 1, sw));

        Assert.Equal(new[] { Left }, sw.Clicks);
        Assert.True(sw.ClickTimes.Count == 1 && sw.Clock - sw.ClickTimes[0] >= Md11SelectorWalker.StepCapMs);
    }

    [Fact]
    public async Task SingleClickControl_WithoutPositions_CannotToggle_AndClicksNothing()
    {
        var control = SingleClick(NewId());
        control.ValueMap = new Dictionary<string, string>();   // the FDR event marker shape
        var sw = new FakeSwitch(0) { Max = 1, ToggleOnLeft = true };

        Assert.False(await Walk(control, 1, sw));

        Assert.Empty(sw.Clicks);
    }

    /// <summary>
    /// The one property that matters on a fuel switch: no delivered read, no click. A stale cached
    /// "Off" on a running engine must never be turned into a click; the fallback's absolute write
    /// takes over instead.
    /// </summary>
    [Fact]
    public async Task SingleClickSwitch_WhenNoFreshDeliveryArrives_DoesNotClick()
    {
        var sw = new FakeSwitch(0) { Max = 1, ToggleOnLeft = true, FreshTimesOut = true };

        Assert.False(await Walk(SingleClick(NewId()), 1, sw));

        Assert.Empty(sw.Clicks);
        Assert.True(sw.ReadFreshCalls > 0);
    }

    /// <summary>
    /// A click queued behind a burst on the CEVENT bus is judged when it will land, not when it was
    /// queued: the no-movement deadline moves out by the backlog, and the next walk on the node
    /// waits for the landed click to settle.
    /// </summary>
    [Fact]
    public async Task AClickQueuedBehindABurst_IsJudgedWhenItLands()
    {
        var id = NewId();
        var control = SingleClick(id);
        var backlogMs = 20 * Md11EventBus.MinGapMs;

        var stuck = new FakeSwitch(0) { Max = 1, ToggleOnLeft = true, Inhibited = true, PendingQueue = 20 };
        Assert.False(await Walk(control, 1, stuck));
        Assert.True(stuck.Clock - stuck.ClickTimes[0] >= backlogMs + Md11SelectorWalker.StepCapMs,
            $"deadline not moved out by the backlog: waited {stuck.Clock - stuck.ClickTimes[0]} ms");

        var sw = new FakeSwitch(0) { Max = 1, ToggleOnLeft = true, PendingQueue = 20 };
        Assert.True(await Walk(control, 1, sw));
        var clickedAt = sw.ClickTimes[0];
        sw.FirstReadAt = -1;
        Assert.True(await Walk(control, 1, sw));   // already there: no click, but the read must wait for the landed one
        Assert.Empty(sw.Clicks.Skip(1));
        Assert.True(sw.FirstReadAt >= clickedAt + backlogMs + Md11SelectorWalker.ClickSettleMs,
            $"second walk read at {sw.FirstReadAt}, click queued at {clickedAt}");
    }

    /// <summary>A var without fresh reads cannot support an absolute decision; the toggle steps aside for the direct write.</summary>
    [Fact]
    public async Task SingleClickSwitch_WithoutFreshReads_DoesNotToggle_LeavesTheFallback()
    {
        var sw = new FakeSwitch(0) { Max = 1, ToggleOnLeft = true, Fresh = false };

        Assert.False(await Walk(SingleClick(NewId()), 1, sw));

        Assert.Empty(sw.Clicks);
    }
}
