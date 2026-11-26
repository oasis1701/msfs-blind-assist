using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.BBS757;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;
using K = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoKeys;
using X = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoActionExecutor;

namespace MSFSBlindAssist.Tests.FirstOfficer.BBS757;

/// <summary>
/// Plan 10, task 5: the 757 First Officer's executor against a fake aircraft
/// (<see cref="FakeBbs757FoTransport"/>): a refused write is reported, never done (FO-9); the
/// starter guard and the abort of a stopped flow's start; the APU start; the fuel control switches
/// and engine controls by variant.
/// </summary>
public class Bbs757FoExecutorTests
{
    private readonly FakeBbs757FoTransport _io = new();
    private readonly X _exec = new();
    private readonly List<string> _said = new();
    private Bbs757FoStateEvaluator _state = new();
    private MSFSBlindAssist.SimConnect.SimConnectManager? _sc;

    public Bbs757FoExecutorTests()
    {
        _exec.SetTransport(_io);
        _exec.SetState(_state);
        _exec.SetSpeech(_said.Add);
    }

    /// <summary>The evaluator the executor reads, over a cache holding <paramref name="cache"/> (no aircraft title).</summary>
    private void Cockpit(params (string Key, double Value)[] cache) => Cockpit("", cache);

    private void Cockpit(string title, params (string Key, double Value)[] cache)
    {
        _state = new Bbs757FoStateEvaluator();
        _sc = Bbs757TestTitles.With(title, cache);
        _state.SetSimConnect(_sc);
        _exec.SetState(_state);
    }

    /// <summary>The evaluator's cache, for a test that makes the cockpit move.</summary>
    private System.Collections.Concurrent.ConcurrentDictionary<string, double> Cache => SeededSimConnectCache.Cache(_sc!);

    // A PW by its L:var (1, measured); a Rolls-Royce by its title alone (an unwritten L:var reads 0 too).
    private void Pw(params (string Key, double Value)[] cache) => Cockpit(cache.Append((K.EngineVariant, 1.0)).ToArray());
    private void Rr(params (string Key, double Value)[] cache) => Cockpit(Bbs757TestTitles.Rr, cache);

    private static Bbs757Step Sw(string key, int? target) => new() { EventName = key, TargetValue = target };

    // ------------------------------------------------------------------ availability and refusal

    [Fact]
    public async Task NoTransport_IsUnavailableAndRefuses()
    {
        var exec = new X();
        Assert.False(exec.IsAvailable);
        Assert.False(await exec.ExecuteStepAsync(Sw(K.Battery, 1)));
        Assert.False(await exec.Set(K.Battery, 1));
    }

    [Fact]
    public async Task ATransportThatCannotLand_IsUnavailable_AndSendsNothing()
    {
        _io.Ready = false;
        Assert.False(_exec.IsAvailable);
        Assert.False(await _exec.ExecuteStepAsync(Sw(K.Battery, 1)));
        Assert.Empty(_io.Events);
    }

    [Fact]
    public async Task AKeyTheMapDoesNotKnow_IsRefusedWithNoWrite()
    {
        _io.KnownNodes = new HashSet<string> { K.Battery };
        Assert.False(await _exec.Set("B757_NOT_A_CONTROL", 1));
        Assert.Empty(_io.Events);
    }

    [Fact]
    public async Task ASetThatLands_ReportsDone()
    {
        Assert.True(await _exec.ExecuteStepAsync(Sw(K.Battery, 1)));
        Assert.Equal(new[] { (K.Battery, 1.0) }, _io.Sets);
    }

    /// <summary>FO-9: the step whose switch BlueBird refuses fails; it is never reported done.</summary>
    [Fact]
    public async Task ASetTheAircraftRefuses_FailsTheStep()
    {
        _io.Accepts = (_, _) => false;
        Assert.False(await _exec.ExecuteStepAsync(Sw(K.SeatBelts, (int)K.SignOn)));
        Assert.False(await _exec.Set(K.SeatBelts, (int)K.SignOn));
    }

    [Fact]
    public async Task AMultiStepFails_WhenAnyOfItsWritesIsRefused_ButSendsEveryOne()
    {
        _io.Accepts = (node, _) => node != K.RightYawDamper;
        var step = new Bbs757Step
        {
            ActionType = FlowStepActionType.SetSwitchMultiple,
            MultiActions = new List<(string, int?)> { (K.LeftYawDamper, 1), (K.RightYawDamper, 1) },
        };
        Assert.False(await _exec.ExecuteStepAsync(step));
        Assert.Equal(new[] { K.LeftYawDamper, K.RightYawDamper }, _io.Sets.Select(s => s.Node));
    }

    [Fact]
    public async Task AnEmptyMultiStep_AndAnUnknownActionType_Fail()
    {
        Assert.False(await _exec.ExecuteStepAsync(new Bbs757Step { ActionType = FlowStepActionType.SetSwitchMultiple }));
        Assert.False(await _exec.ExecuteStepAsync(new Bbs757Step { ActionType = FlowStepActionType.WaitForCondition }));
        Assert.Empty(_io.Events);
    }

    [Fact]
    public async Task AButton_IsPressed_NotSet()
    {
        _io.Buttons.Add(K.ExtPwrButton);
        Assert.True(await _exec.Set(K.ExtPwrButton, 1));
        Assert.Equal(new[] { K.ExtPwrButton }, _io.Presses);
        Assert.Empty(_io.Sets);
    }

    [Fact]
    public async Task AStepAStopOvertook_IsNotSent()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.False(await _exec.ExecuteStepAsync(Sw(K.Battery, 1), stop.Token));
        Assert.Empty(_io.Events);
    }

    [Fact]
    public async Task TheDispatchGateDrains()
    {
        await _exec.WaitForDispatchDrainAsync();
        Assert.True(await _exec.Set(K.Battery, 1));
        await _exec.WaitForDispatchDrainAsync();
    }

    /// <summary>
    /// FO-9 end to end on the real 757 checklist: a hand-tick whose switch BlueBird refuses is
    /// un-ticked once the grace runs out, and raises ItemActionFailed ("Unable to complete").
    /// </summary>
    [Fact]
    public void AHandTickWhoseWriteIsRefused_UnticksAndRaisesItemActionFailed()
    {
        var sc = SeededSimConnectCache.ConnectedWith((K.SeatBelts, K.SignOff));
        var state = new Bbs757FoStateEvaluator();
        state.SetSimConnect(sc);
        _io.Accepts = (_, _) => false;
        _exec.SetState(state);
        var groups = Bbs757FoChecklistDefinitions.Build();
        var mgr = new ChecklistManager<X, Bbs757FoStateEvaluator>(state, _exec, groups);
        var failures = new List<string>();
        mgr.ItemActionFailed += (_, item) => failures.Add(item.Id);
        var item = groups.Single(g => g.Id == "BEFORE_START").Items.Single(i => i.Id == "BS_SEAT_BELTS");

        mgr.ToggleItem("BEFORE_START", "BS_SEAT_BELTS");
        Assert.Contains((K.SeatBelts, K.SignOn), _io.Sets);      // the tick fired its switch
        item.LastManualCheckUtc = DateTime.UtcNow - TimeSpan.FromSeconds(11);
        item.ClearActionGrace();
        mgr.EvaluateAutoDetection();                               // the switch never moved

        Assert.False(item.IsChecked);
        Assert.Equal(new[] { "BS_SEAT_BELTS" }, failures);
    }

    [Fact]
    public void AHandTickWhoseWriteLands_StaysTicked_Silently()
    {
        var sc = SeededSimConnectCache.ConnectedWith((K.SeatBelts, K.SignOff));
        var state = new Bbs757FoStateEvaluator();
        state.SetSimConnect(sc);
        _exec.SetState(state);
        var groups = Bbs757FoChecklistDefinitions.Build();
        var mgr = new ChecklistManager<X, Bbs757FoStateEvaluator>(state, _exec, groups);
        var failures = new List<string>();
        mgr.ItemActionFailed += (_, item) => failures.Add(item.Id);
        _io.OnSet = (node, target) => SeededSimConnectCache.Cache(sc)[node] = target;   // the cockpit follows
        var item = groups.Single(g => g.Id == "BEFORE_START").Items.Single(i => i.Id == "BS_SEAT_BELTS");

        mgr.ToggleItem("BEFORE_START", "BS_SEAT_BELTS");
        mgr.EvaluateAutoDetection();

        Assert.True(item.IsChecked);
        Assert.Empty(failures);
    }

    // ------------------------------------------------------------------ the starter guard

    [Theory]
    [InlineData(double.NaN, false)]   // N2 not read
    [InlineData(0.0, true)]
    [InlineData(19.9, true)]
    [InlineData(20.0, false)]          // the re-engage limit
    [InlineData(35.0, false)]
    [InlineData(47.0, false)]          // running
    public void TheStarterEngagesOnlyOnAReadN2Below20(double n2, bool allowed)
    {
        Assert.Equal(allowed, X.MayEngageStarter(n2, out string why));
        Assert.Equal(allowed, why.Length == 0);
    }

    [Fact]
    public async Task StartSelectorGnd_IsRefused_WithN2Unread()
    {
        Assert.False(await _exec.ExecuteStepAsync(Sw(K.RightStart, (int)K.StartGnd)));
        Assert.Empty(_io.Sets);
    }

    [Fact]
    public async Task StartSelectorGnd_IsRefused_AboveTheReengageLimit()
    {
        _state.SetEngineN2(0, 25);
        Assert.False(await _exec.ExecuteStepAsync(Sw(K.RightStart, (int)K.StartGnd)));
        Assert.Empty(_io.Sets);
        Assert.True(await _exec.ExecuteStepAsync(Sw(K.LeftStart, (int)K.StartGnd)));   // engine 1 is at 0
        Assert.Equal(new[] { (K.LeftStart, K.StartGnd) }, _io.Sets);
    }

    [Fact]
    public async Task StartSelectorAuto_NeedsNoN2()
    {
        Assert.True(await _exec.ExecuteStepAsync(Sw(K.RightStart, (int)K.StartAuto)));
        Assert.Equal(new[] { (K.RightStart, K.StartAuto) }, _io.Sets);
    }

    // ------------------------------------------------------------------ a stopped flow's start

    private async Task<CancellationTokenSource> FlowEngagesStarter(int engine)
    {
        var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Sw(engine == 1 ? K.LeftStart : K.RightStart, (int)K.StartGnd), stop.Token));
        return stop;
    }

    [Fact]
    public async Task AStoppedFlowsStarter_GoesBackToAuto_WhileTheFuelReadsCutoff()
    {
        Pw((K.LeftFuelPw, K.PwCutoff), (K.RightFuelPw, K.PwCutoff));
        _state.SetEngineN2(0, 0);
        using var stop = await FlowEngagesStarter(2);
        stop.Cancel();
        _io.Sets.Clear();

        await _exec.OnFlowStoppedAsync();

        Assert.Equal(new[] { (K.RightStart, K.StartAuto) }, _io.Sets);
        await _exec.OnFlowStoppedAsync();                     // once only
        Assert.Single(_io.Sets);
    }

    [Fact]
    public async Task ARunningFlowsStarter_IsNotTouched()
    {
        Pw((K.LeftFuelPw, K.PwCutoff), (K.RightFuelPw, K.PwCutoff));
        _state.SetEngineN2(0, 0);
        using var stop = await FlowEngagesStarter(2);       // never cancelled
        _io.Sets.Clear();

        await _exec.OnFlowStoppedAsync();

        Assert.Empty(_io.Sets);
    }

    [Fact]
    public async Task AChecklistTicksStarter_IsNeverUndone()
    {
        Pw((K.LeftFuelPw, K.PwCutoff), (K.RightFuelPw, K.PwCutoff));
        _state.SetEngineN2(0, 0);
        Assert.True(await _exec.Set(K.RightStart, (int)K.StartGnd));   // a tick, not a flow
        _io.Sets.Clear();

        await _exec.OnFlowStoppedAsync();
        _exec.ReleaseHeldPresses();

        Assert.Empty(_io.Sets);
        Assert.Empty(_io.WritesNow);
    }

    [Fact]
    public async Task AStoppedFlowsStarter_OverFuelOn_IsLeft()
    {
        Pw((K.LeftFuelPw, K.PwCutoff), (K.RightFuelPw, K.PwRun));
        _state.SetEngineN2(0, 0);
        using var stop = await FlowEngagesStarter(2);
        stop.Cancel();
        _io.Sets.Clear();

        await _exec.OnFlowStoppedAsync();

        Assert.Empty(_io.Sets);
    }

    [Fact]
    public async Task AStoppedFlowsStarter_OverUnreadFuel_IsLeft()
    {
        Cockpit();                                            // no variant, no fuel switch read
        _state.SetEngineN2(0, 0);
        using var stop = await FlowEngagesStarter(1);
        stop.Cancel();
        _io.Sets.Clear();

        await _exec.OnFlowStoppedAsync();

        Assert.Empty(_io.Sets);
    }

    [Fact]
    public async Task FuelOnByTheFlow_ForgetsItsStart()
    {
        Pw((K.LeftFuelPw, K.PwCutoff), (K.RightFuelPw, K.PwCutoff));
        _state.SetEngineN2(0, 0);
        using var stop = await FlowEngagesStarter(2);
        Assert.True(await _exec.ExecuteStepAsync(Sw(X.FuelControl2, X.FuelForStart), stop.Token));
        stop.Cancel();
        _io.Sets.Clear();

        await _exec.OnFlowStoppedAsync();      // the cache still says CUTOFF, but the start is no longer owed an abort

        Assert.Empty(_io.Sets);
    }

    [Fact]
    public async Task TheWindowClosing_PutsAStoppedFlowsStarterBack_FromTheCache()
    {
        Pw((K.LeftFuelPw, K.PwCutoff), (K.RightFuelPw, K.PwCutoff));
        _state.SetEngineN2(0, 0);
        using var stop = await FlowEngagesStarter(1);
        stop.Cancel();

        _exec.ReleaseHeldPresses();

        Assert.Equal(new[] { (K.LeftStart, K.StartAuto) }, _io.WritesNow);
        _exec.ReleaseHeldPresses();
        Assert.Single(_io.WritesNow);
    }

    [Theory]
    [InlineData(0.0, 1.0, true)]
    [InlineData(1.0, 1.0, false)]          // fuel on
    [InlineData(0.0, 3.0, false)]          // already released to AUTO
    [InlineData(double.NaN, 1.0, false)]
    [InlineData(0.0, double.NaN, false)]
    public void TheCloseAbortsOnlyOverFuelCutoffAndASelectorAtGnd(double fuelRun, double selector, bool abort)
        => Assert.Equal(abort, X.ShouldAbortStart(fuelRun, selector));

    // ------------------------------------------------------------------ the APU

    [Fact]
    public async Task ApuStart_HoldsStartThenOn_AndWaitsForStarting()
    {
        _io.Values[K.ApuState] = 0;
        _io.OnSet = (node, target) => { if (node == K.ApuSelector && target == 1) _io.Values[K.ApuState] = 1; };

        Assert.True(await _exec.Set(X.ApuStart, 1));

        Assert.Equal(new[] { (K.ApuSelector, 2.0), (K.ApuSelector, 1.0) }, _io.Sets);
        Assert.Equal(X.ApuStartHoldMs, _io.Delays.First());
        Assert.Equal(3000, X.ApuStartHoldMs);
    }

    [Fact]
    public async Task ApuStart_ThatNeverStarts_Fails()
    {
        _io.Values[K.ApuState] = 0;
        Assert.False(await _exec.Set(X.ApuStart, 1));
        Assert.Equal(new[] { (K.ApuSelector, 2.0), (K.ApuSelector, 1.0) }, _io.Sets);
    }

    /// <summary>
    /// Measured live 2026-10-28 (PW, from cold): BlueBird springs START back to ON before the read-back, so the
    /// START write reads as not landed, and the APU still starts. A selector reading ON after it is carried on.
    /// </summary>
    [Fact]
    public async Task ApuStart_WhoseStartSpringsBackToOnBeforeTheReadBack_StillStarts()
    {
        _io.Values[K.ApuState] = 0;
        _io.Values[K.ApuSelector] = 1;                                         // sprung back to ON
        _io.Accepts = (node, target) => !(node == K.ApuSelector && target == 2);
        _io.OnSet = (node, target) => { if (node == K.ApuSelector && target == 1) _io.Values[K.ApuState] = 1; };

        Assert.True(await _exec.Set(X.ApuStart, 1));
        Assert.Equal(new[] { (K.ApuSelector, 2.0), (K.ApuSelector, 1.0) }, _io.Sets);
    }

    [Theory]
    [InlineData(0.0)]          // OFF: the START write really did not take
    [InlineData(null)]         // unread
    public async Task ApuStart_WhoseStartDoesNotLand_AndTheSelectorIsNotOn_Fails(double? selector)
    {
        _io.Values[K.ApuState] = 0;
        if (selector != null) _io.Values[K.ApuSelector] = selector;
        _io.Accepts = (node, target) => !(node == K.ApuSelector && target == 2);

        Assert.False(await _exec.Set(X.ApuStart, 1));
        Assert.Equal(new[] { (K.ApuSelector, 2.0) }, _io.Sets);                 // never on to ON
    }

    [Theory]
    [InlineData(null, false)]      // never started on an unread state
    [InlineData(3.0, false)]       // shutting down
    public async Task ApuStart_IsRefused_UnreadOrShuttingDown(double? state, bool ok)
    {
        if (state != null) _io.Values[K.ApuState] = state;
        Assert.Equal(ok, await _exec.Set(X.ApuStart, 1));
        Assert.Empty(_io.Sets);
    }

    [Fact]
    public async Task ApuStart_AlreadyStarting_IsDone_AndRunning_KeepsTheSelectorOn()
    {
        _io.Values[K.ApuState] = 1;
        Assert.True(await _exec.Set(X.ApuStart, 1));
        Assert.Empty(_io.Sets);

        _io.Values[K.ApuState] = 2;
        Assert.True(await _exec.Set(X.ApuStart, 1));
        Assert.Equal(new[] { (K.ApuSelector, 1.0) }, _io.Sets);
    }

    [Fact]
    public async Task ApuStartAndWait_IsTrueOnceItRuns()
    {
        _io.Values[K.ApuState] = 0;
        _io.OnSet = (node, target) => { if (node == K.ApuSelector && target == 1) _io.Values[K.ApuState] = 1; };
        _io.OnDelay = n => { if (n >= 5) _io.Values[K.ApuState] = 2; };    // runs some polls after the start

        Assert.True(await _exec.StartApuAndWaitAsync());
        Assert.Equal(2, _io.Values[K.ApuState]);
    }

    [Fact]
    public async Task ApuStartAndWait_IsFalseWhenItNeverRuns()
    {
        _io.Values[K.ApuState] = 0;
        _io.OnSet = (node, target) => { if (node == K.ApuSelector && target == 1) _io.Values[K.ApuState] = 1; };

        Assert.False(await _exec.StartApuAndWaitAsync());
        // It polled the whole run wait (about 150 s) before giving up.
        Assert.True(_io.Delays.Count(d => d == X.PollMs) >= X.ApuRunWaitMs / X.PollMs);
    }

    // ------------------------------------------------------------------ fuel control by variant

    [Theory]
    [InlineData(1, X.FuelForStart, K.LeftFuelPw, K.PwRun)]
    [InlineData(2, X.FuelForStart, K.RightFuelPw, K.PwRun)]
    [InlineData(1, 1, K.LeftFuelPw, K.PwRun)]
    [InlineData(2, 0, K.RightFuelPw, K.PwCutoff)]
    public async Task FuelControl_OnAPw_DrivesItsOwnSwitch(int engine, int target, string node, double value)
    {
        Pw();
        Assert.True(await _exec.Set(engine == 1 ? X.FuelControl1 : X.FuelControl2, target));
        Assert.Equal(new[] { (node, value) }, _io.Sets);
    }

    [Theory]
    [InlineData(1, 0, -5.0, K.LeftFuelRr, K.RrCutoff)]
    [InlineData(1, 1, -5.0, K.LeftFuelRr, K.RrRun)]
    [InlineData(1, X.FuelForStart, -5.0, K.LeftFuelRr, K.RrRich)]   // cold start: RICH
    [InlineData(1, X.FuelForStart, 0.0, K.LeftFuelRr, K.RrRich)]    // at 0 °C: RICH
    [InlineData(2, X.FuelForStart, 0.1, K.RightFuelRr, K.RrRun)]    // warm: RUN
    [InlineData(2, X.FuelForStart, 30.0, K.RightFuelRr, K.RrRun)]
    public async Task FuelControl_OnAnRr_DrivesItsOwnSwitch_RichWhenCold(int engine, int target, double egt, string node, double value)
    {
        Rr((engine == 1 ? K.LeftEgt : K.RightEgt, egt));
        Assert.True(await _exec.Set(engine == 1 ? X.FuelControl1 : X.FuelControl2, target));
        Assert.Equal(new[] { (node, value) }, _io.Sets);
    }

    [Fact]
    public async Task FuelControl_ForAnRrStart_IsRefused_OnAnUnreadEgt()
    {
        Rr();
        Assert.False(await _exec.Set(X.FuelControl1, X.FuelForStart));
        Assert.Empty(_io.Sets);
        Assert.True(await _exec.Set(X.FuelControl1, 0));        // CUTOFF needs no EGT
    }

    [Fact]
    public async Task FuelControl_IsRefused_WhenTheVariantIsUnknown()
    {
        Cockpit();
        Assert.False(await _exec.Set(X.FuelControl1, 0));
        Assert.False(await _exec.Set(X.FuelControl2, X.FuelForStart));
        Assert.Empty(_io.Sets);
    }

    [Fact]
    public async Task FuelControl_ThatDoesNotLand_Fails()
    {
        Pw();
        _io.Accepts = (_, _) => false;
        Assert.False(await _exec.Set(X.FuelControl1, X.FuelForStart));
    }

    [Theory]
    [InlineData(-0.1, K.RrRich)]
    [InlineData(0.0, K.RrRich)]
    [InlineData(0.1, K.RrRun)]
    public void RrStartPosition_IsRichAtOrBelowZero(double egt, double expected)
        => Assert.Equal(expected, X.RrStartPosition(egt));

    [Fact]
    public void RrStartPosition_IsNullUnread() => Assert.Null(X.RrStartPosition(double.NaN));

    // ------------------------------------------------------------------ engine controls (EEC on the RR only)

    [Fact]
    public async Task EngineControls_OnAPw_AreTheLimitersOnly()
    {
        Pw();
        Assert.True(await _exec.Set(X.EngineControls, 1));
        Assert.Equal(new[] { K.LeftLimiter, K.RightLimiter }, _io.Sets.Select(s => s.Node));
        Assert.All(_io.Sets, s => Assert.Equal(1, s.Target));
    }

    [Fact]
    public async Task EngineControls_OnAnRr_AreTheEecsAndTheLimiters()
    {
        Rr();
        Assert.True(await _exec.Set(X.EngineControls, 1));
        Assert.Equal(new[] { K.LeftEec, K.RightEec, K.LeftLimiter, K.RightLimiter }, _io.Sets.Select(s => s.Node));
    }

    [Fact]
    public async Task EngineControls_AreRefused_WhenTheVariantIsUnknown()
    {
        Cockpit();
        Assert.False(await _exec.Set(X.EngineControls, 1));
        Assert.Empty(_io.Sets);
    }

    // ------------------------------------------------------------------ external power, speedbrake, altimeters

    [Fact]
    public async Task ExternalPower_UnreadIsNeverPressed()
    {
        Assert.False(await _exec.Set(X.ExtPower, 1));
        Assert.Empty(_io.Presses);
    }

    [Fact]
    public async Task ExternalPower_AlreadyThere_IsNotPressed()
    {
        _io.Values[K.ExtPwrOn] = 1;
        Assert.True(await _exec.Set(X.ExtPower, 1));
        _io.Values[K.ExtPwrOn] = 0;
        Assert.True(await _exec.Set(X.ExtPower, 0));
        Assert.Empty(_io.Presses);
    }

    [Fact]
    public async Task ExternalPower_On_NeedsAvail()
    {
        _io.Values[K.ExtPwrOn] = 0;
        _io.Values[K.ExtPwrAvail] = 0;
        Assert.False(await _exec.Set(X.ExtPower, 1));
        Assert.Empty(_io.Presses);
    }

    [Fact]
    public async Task ExternalPower_IsPressedAndConfirmedOnTheOnLight()
    {
        _io.Values[K.ExtPwrOn] = 0;
        _io.Values[K.ExtPwrAvail] = 1;
        _io.OnPress = node => { if (node == K.ExtPwrButton) _io.Values[K.ExtPwrOn] = 1; };
        Assert.True(await _exec.Set(X.ExtPower, 1));
        Assert.Equal(new[] { K.ExtPwrButton }, _io.Presses);
    }

    [Fact]
    public async Task ExternalPower_PressThatNeverLights_Fails()
    {
        _io.Values[K.ExtPwrOn] = 0;
        _io.Values[K.ExtPwrAvail] = 1;
        Assert.False(await _exec.Set(X.ExtPower, 1));
        Assert.Single(_io.Presses);
    }

    [Fact]
    public async Task Speedbrake_ArmIsRefused_OverAnExtendedLever()
    {
        _io.Values[K.Speedbrake] = 50;
        Assert.False(await _exec.Set(X.SpeedbrakeArm, 1));
        Assert.Empty(_io.Presses);
    }

    [Fact]
    public async Task Speedbrake_ArmIsPressedFromDown_AndConfirmedOnTheLever()
    {
        _io.Values[K.Speedbrake] = 0;
        _io.OnPress = node => { if (node == X.SpeedbrakeArmedNode) _io.Values[K.Speedbrake] = 10; };
        Assert.True(await _exec.Set(X.SpeedbrakeArm, 1));
        Assert.Equal(new[] { X.SpeedbrakeArmedNode }, _io.Presses);
    }

    [Fact]
    public async Task Speedbrake_DownFromExtended_IsPressed()
    {
        _io.Values[K.Speedbrake] = 100;
        _io.OnPress = node => { if (node == X.SpeedbrakeDownNode) _io.Values[K.Speedbrake] = 0; };
        Assert.True(await _exec.Set(X.SpeedbrakeDown, 1));
        Assert.Equal(new[] { X.SpeedbrakeDownNode }, _io.Presses);
    }

    [Fact]
    public async Task Speedbrake_Unread_IsNeverPressed()
    {
        Assert.False(await _exec.Set(X.SpeedbrakeDown, 1));
        Assert.Empty(_io.Presses);
    }

    [Fact]
    public async Task Altimeters_GoThroughTheDefinitionsStandardSet()
    {
        Assert.True(await _exec.SetAltimetersStandardAsync());
        _io.AltimetersLand = false;
        Assert.False(await _exec.SetAltimetersStandardAsync());
        Assert.Equal(2, _io.AltimeterSets);
    }

    [Fact]
    public async Task LandingLights_ReportBoth()
    {
        Assert.True(await _exec.SetLandingLights(true));
        Assert.Equal(new[] { (K.LeftLanding, 1.0), (K.RightLanding, 1.0) }, _io.Sets);
        _io.Accepts = (node, _) => node == K.LeftLanding;
        Assert.False(await _exec.SetLandingLights(false));
    }

    /// <summary>The executor's own keys, then the thirteen preflight tests' (plan 19, task 8).</summary>
    [Fact]
    public void PseudoKeys_AreTheExecutorsOwn()
        => Assert.Equal(new[] { X.ExtPower, X.ParkingBrake, X.ApuStart, X.FuelControl1, X.FuelControl2, X.EngineControls,
            X.SpeedbrakeArm, X.SpeedbrakeDown, X.AltimetersStandard, X.ArmAutothrottle,
            X.LightOff1, X.LightOff2, X.Cutout1, X.Cutout2 }.Concat(Bbs757FoTests.Preflight.Select(t => t.Key)), X.PseudoKeys);

    [Fact]
    public async Task ParkingBrake_IsSetThroughItsOwnWrite_AndNeverReleased()
    {
        Assert.True(await _exec.Set(X.ParkingBrake, 1));
        Assert.Equal(1, _io.ParkingBrakeSets);
        Assert.Empty(_io.Sets);                                  // never the plain lever write

        Assert.False(await _exec.Set(X.ParkingBrake, 0));        // releasing is the pilot's, on the pedals
        Assert.Equal(1, _io.ParkingBrakeSets);

        _io.ParkingBrakeLands = false;
        Assert.False(await _exec.Set(X.ParkingBrake, 1));
    }

    // ------------------------------------------------------------------ the autothrottle (BlueBird's flag, R40)

    [Fact]
    public async Task Autothrottle_AlreadyArmed_IsNotTouched()
    {
        _io.Values[K.AutothrottleArmedState] = 1;
        _io.Values[K.AutothrottleArm] = K.AutothrottleOff;      // whatever the switch says, the A/T is armed
        Assert.True(await _exec.Set(X.ArmAutothrottle, 1));
        Assert.Empty(_io.Sets);
    }

    [Fact]
    public async Task Autothrottle_WithItsFlagUnread_IsRefused()
    {
        _io.Values[K.AutothrottleArm] = K.AutothrottleOff;
        Assert.False(await _exec.Set(X.ArmAutothrottle, 1));
        Assert.Empty(_io.Sets);
    }

    [Fact]
    public async Task Autothrottle_SwitchOff_IsSetToArm_AndConfirmedOnTheFlag()
    {
        _io.Values[K.AutothrottleArmedState] = 0;
        _io.Values[K.AutothrottleArm] = K.AutothrottleOff;
        _io.OnSet = (node, target) => { if (node == K.AutothrottleArm && target == K.AutothrottleArmed) _io.Values[K.AutothrottleArmedState] = 1; };

        Assert.True(await _exec.Set(X.ArmAutothrottle, 1));
        Assert.Equal(new[] { (K.AutothrottleArm, K.AutothrottleArmed) }, _io.Sets);
    }

    /// <summary>The switch at ARM over a disarmed A/T: a write to ARM would change nothing, so it is cycled OFF then ARM.</summary>
    [Fact]
    public async Task Autothrottle_SwitchAtArmOverADisarmedAt_IsCycledOffThenArm()
    {
        _io.Values[K.AutothrottleArmedState] = 0;
        _io.Values[K.AutothrottleArm] = K.AutothrottleArmed;
        _io.OnSet = (node, target) =>
        {
            // BlueBird arms on the switch's move to ARM, not on a switch that only reads ARM.
            if (node == K.AutothrottleArm) _io.Values[K.AutothrottleArmedState] = target == K.AutothrottleArmed ? 1 : 0;
        };

        Assert.True(await _exec.Set(X.ArmAutothrottle, 1));
        Assert.Equal(new[] { (K.AutothrottleArm, K.AutothrottleOff), (K.AutothrottleArm, K.AutothrottleArmed) }, _io.Sets);
    }

    [Fact]
    public async Task Autothrottle_ThatNeverArms_Fails()
    {
        _io.Values[K.AutothrottleArmedState] = 0;
        _io.Values[K.AutothrottleArm] = K.AutothrottleOff;
        Assert.False(await _exec.Set(X.ArmAutothrottle, 1));
        Assert.Equal(new[] { (K.AutothrottleArm, K.AutothrottleArmed) }, _io.Sets);
    }

    // ------------------------------------------------------------------ who owns a start

    [Fact]
    public async Task AStartSelectorAlreadyAtGnd_IsThePilotsStart_AndNeverPutBack()
    {
        Pw((K.LeftFuelPw, K.PwCutoff), (K.RightFuelPw, K.PwCutoff));
        _state.SetEngineN2(0, 0);
        _io.Values[K.RightStart] = K.StartGnd;                  // the pilot's GND
        using var stop = await FlowEngagesStarter(2);
        stop.Cancel();
        _io.Sets.Clear();

        await _exec.OnFlowStoppedAsync();
        _exec.ReleaseHeldPresses();

        Assert.Empty(_io.Sets);
        Assert.Empty(_io.WritesNow);
    }

    // ------------------------------------------------------------------ an engine start's watches, and the aborted start (manual p. 204)

    /// <summary>A flow run's start of engine 2 (GND, then the fuel) on a PW, the EGT reading <paramref name="egt"/> at the fuel.</summary>
    private async Task<CancellationTokenSource> FlowStartsRightEngine(bool pw = true, double egt = 15)
    {
        if (pw) Pw((K.LeftFuelPw, K.PwCutoff), (K.RightFuelPw, K.PwCutoff), (K.RightEgt, egt));
        else Rr((K.LeftFuelRr, K.RrCutoff), (K.RightFuelRr, K.RrCutoff), (K.RightEgt, egt));
        _state.SetEngineN2(0, 0);
        var stop = await FlowEngagesStarter(2);
        _state.SetEngineN2(0, 26);                             // at the fuel point
        Assert.True(await _exec.ExecuteStepAsync(Sw(X.FuelControl2, X.FuelForStart), stop.Token));
        _io.Sets.Clear();
        _io.Delays.Clear();
        return stop;
    }

    [Fact]
    public async Task LightOff_WhenTheEgtRises_Passes_AndTouchesNothing()
    {
        using var stop = await FlowStartsRightEngine();
        _io.OnDelay = n => { if (n >= 8) Cache[K.RightEgt] = 15 + X.LightOffEgtRiseCelsius; };

        Assert.True(await _exec.ExecuteStepAsync(Sw(X.LightOff2, 1), stop.Token));
        Assert.Empty(_io.Sets);
        Assert.Empty(_said);
    }

    [Fact]
    public void LightOff_IsTheEgtRisenByTheThreshold_OrTheEnginePastItsStarter()
    {
        Assert.False(X.LitOff(15 + X.LightOffEgtRiseCelsius - 1, 15, 26));
        Assert.True(X.LitOff(15 + X.LightOffEgtRiseCelsius, 15, 26));
        Assert.True(X.LitOff(double.NaN, double.NaN, Bbs757FoStateEvaluator.EngineRunningN2));   // already past the starter
        Assert.False(X.LitOff(double.NaN, 15, 26));                 // unread: not yet
        Assert.False(X.LitOff(200, double.NaN, 26));                // no EGT at the fuel to rise from
    }

    /// <summary>
    /// No light-off within 30 s of the fuel, on this run's own start, on a PW: fuel CUTOFF, the starter kept at GND
    /// motoring 30 s, then AUTO, the Captain told why, and the flow stopped (the step fails).
    /// </summary>
    [Fact]
    public async Task LightOff_TimedOut_OnAPw_CutsTheFuel_Motors30Seconds_ThenAuto()
    {
        using var stop = await FlowStartsRightEngine();
        int delaysAtCutoff = -1, delaysAtAuto = -1;
        _io.OnSet = (node, target) =>
        {
            if (node == K.RightFuelPw && target == K.PwCutoff) delaysAtCutoff = _io.Delays.Count;
            if (node == K.RightStart && target == K.StartAuto) delaysAtAuto = _io.Delays.Count;
        };

        Assert.False(await _exec.ExecuteStepAsync(Sw(X.LightOff2, 1), stop.Token));

        Assert.Equal(new[] { (K.RightFuelPw, K.PwCutoff), (K.RightStart, K.StartAuto) }, _io.Sets);
        Assert.Equal(X.LightOffSeconds, _io.Delays.Take(delaysAtCutoff).Count(d => d == X.PollMs));   // the watch: a read a second
        Assert.Equal(X.AbortMotorSeconds, delaysAtAuto - delaysAtCutoff);                            // dry motoring, 30 s
        var said = Assert.Single(_said);
        Assert.StartsWith("Right engine start aborted: no light-off within 30 seconds of the fuel.", said);
        Assert.Contains("motoring 30 seconds", said);
        Assert.Equal(X.SpeechHoldMs, _io.Delays[^1]);              // heard before the flow's "flow stopped" interrupts it
    }

    /// <summary>
    /// The starter did not cut out within its time on a Rolls-Royce: fuel CUTOFF, motoring at least 30 s AND until
    /// the EGT reads 100 °C or less, then AUTO.
    /// </summary>
    [Fact]
    public async Task Cutout_TimedOut_OnAnRr_MotorsUntilTheEgtIs100OrLess()
    {
        using var stop = await FlowStartsRightEngine(pw: false, egt: 15);
        Cache[K.RightEgt] = 450;                                 // lit, but hung below cut-out
        int delaysAtCutoff = -1, delaysAtAuto = -1;
        _io.OnSet = (node, target) =>
        {
            if (node == K.RightFuelRr && target == K.RrCutoff) delaysAtCutoff = _io.Delays.Count;
            if (node == K.RightStart && target == K.StartAuto) delaysAtAuto = _io.Delays.Count;
        };
        _io.OnDelay = n => { if (delaysAtCutoff >= 0 && n - delaysAtCutoff >= 50) Cache[K.RightEgt] = 100; };

        Assert.False(await _exec.ExecuteStepAsync(Sw(X.Cutout2, 1), stop.Token));

        Assert.Equal(new[] { (K.RightFuelRr, K.RrCutoff), (K.RightStart, K.StartAuto) }, _io.Sets);
        Assert.Equal(X.CutoutSeconds, delaysAtCutoff);
        Assert.Equal(50, delaysAtAuto - delaysAtCutoff);          // past the 30 s, until the EGT read 100
        Assert.StartsWith("Right engine start aborted: the starter did not cut out within 90 seconds.", Assert.Single(_said));
    }

    [Fact]
    public async Task AnAbortedRrStart_WhoseEgtNeverCools_GoesToAutoAtTheLimit_AndSaysSo()
    {
        using var stop = await FlowStartsRightEngine(pw: false, egt: 15);
        Cache[K.RightEgt] = 450;
        int delaysAtCutoff = -1, delaysAtAuto = -1;
        _io.OnSet = (node, target) =>
        {
            if (node == K.RightFuelRr && target == K.RrCutoff) delaysAtCutoff = _io.Delays.Count;
            if (node == K.RightStart && target == K.StartAuto) delaysAtAuto = _io.Delays.Count;
        };

        Assert.False(await _exec.ExecuteStepAsync(Sw(X.Cutout2, 1), stop.Token));

        Assert.Equal(X.AbortMotorMaxSeconds, delaysAtAuto - delaysAtCutoff);
        Assert.Equal(2, _said.Count);
        Assert.Contains("not read at or below 100 degrees", _said[1]);
    }

    [Fact]
    public async Task Cutout_WhenTheSelectorReturnsToAuto_Passes_AndTheStartIsNoLongerOwed()
    {
        using var stop = await FlowStartsRightEngine();
        _io.OnDelay = n => { if (n >= 29) _io.Values[K.RightStart] = K.StartAuto; };

        Assert.True(await _exec.ExecuteStepAsync(Sw(X.Cutout2, 1), stop.Token));
        Assert.Empty(_io.Sets);

        // Started: a later timeout in the same run has nothing to abort.
        _io.Values[K.RightStart] = K.StartGnd;
        _io.OnDelay = null;
        Assert.False(await _exec.ExecuteStepAsync(Sw(X.Cutout2, 1), stop.Token));
        Assert.Empty(_io.Sets);
        Assert.Contains("not the First Officer's", Assert.Single(_said));
    }

    /// <summary>Never a starter the First Officer did not engage: a pilot's GND that times out is told, not touched.</summary>
    [Fact]
    public async Task AWatchOnAStartTheFirstOfficerDidNotEngage_TouchesNothing()
    {
        Pw((K.LeftFuelPw, K.PwCutoff), (K.RightFuelPw, K.PwCutoff), (K.RightEgt, 15));
        _state.SetEngineN2(0, 0);
        _io.Values[K.RightStart] = K.StartGnd;                  // the pilot's GND
        using var stop = await FlowEngagesStarter(2);
        _state.SetEngineN2(0, 26);
        Assert.True(await _exec.ExecuteStepAsync(Sw(X.FuelControl2, X.FuelForStart), stop.Token));
        _io.Sets.Clear();

        Assert.False(await _exec.ExecuteStepAsync(Sw(X.LightOff2, 1), stop.Token));
        Assert.Empty(_io.Sets);
        Assert.Contains("left to you", Assert.Single(_said));
        Assert.Equal(X.SpeechHoldMs, _io.Delays[^1]);
    }

    [Fact]
    public async Task AWatchWithItsInputUnread_AbortsNothing()
    {
        using var stop = await FlowStartsRightEngine();
        Cache.TryRemove(K.RightEgt, out _);
        _state.SetEngineN2(0, double.NaN);

        Assert.False(await _exec.ExecuteStepAsync(Sw(X.LightOff2, 1), stop.Token));
        Assert.Empty(_io.Sets);
        Assert.Contains("EGT not read", Assert.Single(_said));
    }

    /// <summary>A Stop during a watch ends it with no write and nothing said: a start on fuel runs on.</summary>
    [Fact]
    public async Task AStopDuringAWatch_AbortsNothing_AndTheStartRunsOn()
    {
        using var stop = await FlowStartsRightEngine();
        _io.OnDelay = n => { if (n == 3) stop.Cancel(); };

        Assert.False(await _exec.ExecuteStepAsync(Sw(X.LightOff2, 1), stop.Token));
        await _exec.OnFlowStoppedAsync();

        Assert.Empty(_io.Sets);
        Assert.Empty(_said);
    }

    [Fact]
    public async Task AWatchHoldsOnTheFlowsPause_EveryPoll()
    {
        int pauses = 0;
        _exec.AttachFlowPause(_ => { pauses++; return Task.CompletedTask; });
        using var stop = await FlowStartsRightEngine();
        _io.OnDelay = n => { if (n >= 4) Cache[K.RightEgt] = 200; };

        Assert.True(await _exec.ExecuteStepAsync(Sw(X.LightOff2, 1), stop.Token));
        Assert.Equal(5, pauses);
    }

    [Fact]
    public async Task AWatch_IsAFlowsOwn_NeverAChecklistTicks()
    {
        Assert.False(await _exec.Set(X.LightOff1, 1));
        Assert.False(await _exec.Set(X.Cutout2, 1));
        Assert.Empty(_io.Events);
    }

    /// <summary>A watch of a later run never aborts a start an earlier, stopped run fuelled: that start runs on.</summary>
    [Fact]
    public async Task AStoppedRunsFuelledStart_IsNotALaterRunsToAbort()
    {
        var first = await FlowStartsRightEngine();
        first.Cancel();
        await _exec.OnFlowStoppedAsync();
        using var second = new CancellationTokenSource();

        Assert.False(await _exec.ExecuteStepAsync(Sw(X.Cutout2, 1), second.Token));
        Assert.Empty(_io.Sets);
        first.Dispose();
    }

    // ------------------------------------------------------------------ the center pump ON gate (plan 29, FOB-10/FOB-18)

    /// <summary>
    /// At the executor's single chokepoint, as on the other Boeings: a center pump ON over a tank at or below 1,000 lb is
    /// done with nothing written; an OFF is never gated; with center fuel the ON is written; an unread tank is not gated.
    /// </summary>
    [Fact]
    public async Task A_center_pump_on_over_an_empty_tank_is_suppressed_but_off_never_is()
    {
        Cockpit((K.CenterFuelGallons, 100));                                   // 670 lb
        Assert.True(await _exec.Set(K.CenterLeftPump, 1));
        Assert.True(await _exec.ExecuteStepAsync(new Bbs757Step
        {
            ActionType = MSFSBlindAssist.FirstOfficer.Models.FlowStepActionType.SetSwitchMultiple,
            MultiActions = new (string, int?)[] { (K.CenterRightPump, 1) },
        }));
        Assert.Empty(_io.Sets);
        Assert.True(await _exec.Set(K.CenterLeftPump, 0));
        Assert.Equal(new[] { (K.CenterLeftPump, 0.0) }, _io.Sets);

        Cockpit((K.CenterFuelGallons, 2000));
        Assert.True(await _exec.Set(K.CenterRightPump, 1));
        Assert.Contains((K.CenterRightPump, 1.0), _io.Sets);

        Cockpit();                                                              // the tank unread
        Assert.True(await _exec.Set(K.CenterLeftPump, 1));
        Assert.Contains((K.CenterLeftPump, 1.0), _io.Sets);
    }

    [Fact]
    public async Task SetCenterFuelPumps_sets_both()
    {
        Cockpit((K.CenterFuelGallons, 2000));
        Assert.True(await _exec.SetCenterFuelPumps(1));
        Assert.Equal(new[] { (K.CenterLeftPump, 1.0), (K.CenterRightPump, 1.0) }, _io.Sets);
    }

    // ------------------------------------------------------------------ BlueBird's preflight tests (plan 19, task 8)

    private const Bbs757FoTestKind Held = Bbs757FoTestKind.HeldButton;
    private const Bbs757FoTestKind P61 = Bbs757FoTestKind.P61Switch;

    /// <summary>
    /// BlueBird's tests (User Procedures v1.01, simplified procedures pp. 36-42) shortest first, with no battery test
    /// (owner, plan 19) and no squib or landing config test (owner, plan 29).
    /// </summary>
    [Fact]
    public void ThePreflightTests_AreEleven_ShortestFirst()
    {
        var expected = new Bbs757FoTest[]
        {
            new("FO_TEST_STALL_L", "Left stall warning test — listen for the stick shaker", "BBS757_P61_LEFT_STALL_TEST", P61, null, 1000),
            new("FO_TEST_STALL_R", "Right stall warning test — listen for the stick shaker", "BBS757_P61_RIGHT_STALL_TEST", P61, null, 1000),
            new("FO_TEST_CVR", "CVR test", "B757_OVHD_SW_CVR_TEST", Held, null, 1000),
            new("FO_TEST_CONFIG_TO", "Takeoff config test — listen for the configuration warning", "BBS757_P61_TAKEOFF_CONFIG_TEST", P61, null, 2000),
            new("FO_TEST_FIRE_WHLWELL", "Wheel well fire test — listen for the fire bell", "B757_PED_FIRE_WHLWELL_BUTTON", Held, null, 3000),
            new("FO_TEST_FIRE_ENG", "Engine fire test — listen for the fire bell", "B757_PED_FIRE_ENG_BUTTON", Held, null, 3000),
            new("FO_TEST_FIRE_CARGO", "Engine, APU and cargo fire test — listen for the fire bell", "B757_PED_FIRE_CARGO_BUTTON", Held, null, 3000),
            new("FO_TEST_OXY_CAPT", "Captain oxygen test — listen for the oxygen flow", "OXY_CAPT_TEST_SWITCH001", Held, 10000, 1000),
            new("FO_TEST_OXY_FO", "First officer oxygen test — listen for the oxygen flow", "OXY_FO_TEST_SWITCH1", Held, 10000, 1000),
            new("FO_TEST_TCAS", "TCAS test — listen for TCAS test pass", "B757_PED_TCAS_MODE_KNOB_TEST_CLICK", Held, null, 8000),
            new("FO_TEST_GND_PROX", "Ground proximity test — listen for the callouts", "BBS757_P61_GROUND_PROX_TEST", P61, null, 15000),
        };
        Assert.Equal(expected, Bbs757FoTests.Preflight);
    }

    /// <summary>
    /// A held button is a momentary control of the map that is let go (its own hold 3 s unless the test gives one);
    /// a P61 test is a P61 test button (no readout, its switch held 3 s). Keys unique, each found by <c>For</c>.
    /// </summary>
    [Fact]
    public void EachPreflightTest_TargetsAHeldMapButtonOrAP61TestButton()
    {
        var def = new MSFSBlindAssist.Aircraft.BBS757Definition();
        foreach (var t in Bbs757FoTests.Preflight)
        {
            if (t.Kind == Held)
            {
                var c = def.ControlFor(t.Target);
                Assert.True(c != null && MSFSBlindAssist.Aircraft.BBS757.Bbs757Write.IsMomentary(c), $"{t.Key}: {t.Target} is not a button of the map");
                var press = MSFSBlindAssist.Aircraft.BBS757.Bbs757Write.PressFor(c!);
                Assert.True(press?.Up != null, $"{t.Key}: {t.Target} is never let go");
                Assert.Equal(3000, press!.HoldMs);
            }
            else
            {
                var b = MSFSBlindAssist.Aircraft.BBS757.Bbs757SpringControls.ForButtonKey(t.Target);
                Assert.True(b != null, $"{t.Key}: {t.Target} is not a spring button");
                Assert.Null(b!.Control.ReadoutKey);                  // a P61 test, not a trim
                Assert.Equal(3000, b.Control.HoldMs);
                Assert.Null(t.HoldMs);                               // its switch's own hold
            }
            Assert.Same(t, Bbs757FoTests.For(t.Key));
        }
        Assert.Equal(11, Bbs757FoTests.Preflight.Select(t => t.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Null(Bbs757FoTests.For(K.Battery));
        Assert.Null(Bbs757FoTests.For("B757_OVHD_SW_CVR_TEST"));     // a target is not a key
    }

    /// <summary>Each test goes to its kind with its hold, then waits its After; never through the map path (it knows nothing here).</summary>
    [Fact]
    public async Task EachPreflightTest_IsPressedByItsKind_WithItsHold_ThenWaitsItsAfter()
    {
        foreach (var t in Bbs757FoTests.Preflight)
        {
            var io = new FakeBbs757FoTransport { KnownNodes = new HashSet<string>() };
            var exec = new X();
            exec.SetTransport(io);

            Assert.True(await exec.ExecuteStepAsync(Sw(t.Key, 1)), t.Key);

            string sent = t.Kind == P61 ? $"p61 {t.Target}" : $"held {t.Target} {(t.HoldMs is int h ? h.ToString(System.Globalization.CultureInfo.InvariantCulture) : "map")}";
            Assert.Equal(new[] { sent }, io.Events);
            Assert.Equal(new[] { t.AfterMs }, io.Delays);
        }
    }

    [Theory]
    [InlineData("FO_TEST_OXY_CAPT", "held OXY_CAPT_TEST_SWITCH001 10000", 1000)]
    [InlineData("FO_TEST_CVR", "held B757_OVHD_SW_CVR_TEST map", 1000)]
    [InlineData("FO_TEST_GND_PROX", "p61 BBS757_P61_GROUND_PROX_TEST", 15000)]
    [InlineData("FO_TEST_TCAS", "held B757_PED_TCAS_MODE_KNOB_TEST_CLICK map", 8000)]
    public async Task APreflightTest_ByAChecklistTick_IsTheSamePressAndWait(string key, string sent, int after)
    {
        Assert.True(await _exec.Set(key, 1));
        Assert.Equal(new[] { sent }, _io.Events);
        Assert.Equal(new[] { after }, _io.Delays);
    }

    [Fact]
    public async Task APreflightTestThatCannotBePressed_Fails_AndSkipsItsWait()
    {
        _io.TestPressLands = false;
        foreach (var t in Bbs757FoTests.Preflight)
            Assert.False(await _exec.ExecuteStepAsync(Sw(t.Key, 1)), t.Key);
        Assert.Equal(11, _io.HeldPresses.Count + _io.P61Tests.Count);
        Assert.Empty(_io.Delays);
    }

    /// <summary>
    /// The executor over the definition's own test cores (the real control map, a fake sim with no clock): what a
    /// flow's Stop does to a test in flight.
    /// </summary>
    private sealed class DefinitionCores : IBbs757FoTransport
    {
        public readonly Bbs757FakeIo Sim = new();
        public readonly MSFSBlindAssist.Aircraft.BBS757Definition Def;
        public readonly List<int> AfterWaits = new();

        public DefinitionCores()
        {
            Def = new MSFSBlindAssist.Aircraft.BBS757Definition { NestRunning = () => true, WriteIo = _ => Sim.Io, NowMs = () => 0 };
        }

        public bool Ready => true;
        public bool IsButton(string node) => false;
        public bool Knows(string node) => false;

        public Task<bool> PressHeldAsync(string node, int? holdMs, CancellationToken ct)
            => Def.FoPressCoreAsync(Def.ControlFor(node)!, Sim.Io, ct, holdMs);

        public Task<bool> P61TestAsync(string buttonKey, CancellationToken ct)
            => Def.FoSpringTestCoreAsync(MSFSBlindAssist.Aircraft.BBS757.Bbs757SpringControls.ForButtonKey(buttonKey)!, Sim.Io, ct);

        public Task Delay(int ms, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            AfterWaits.Add(ms);
            return Task.CompletedTask;
        }

        public Task<bool> SetAsync(string node, double target, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> PressAsync(string node, CancellationToken ct) => throw new NotSupportedException();
        public Task<double?> ReadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public double? Cached(string key) => throw new NotSupportedException();
        public bool WriteNow(string node, double target) => throw new NotSupportedException();
        public Task<bool> SetAltimetersStandardAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> SetParkingBrakeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    /// <summary>
    /// The Stop fires during the hold: the hold runs its full length and the switch is let go (the last write is the
    /// release; no wait was canceled), and the test's After wait is not waited: the step reports false.
    /// </summary>
    [Theory]
    [InlineData("FO_TEST_OXY_CAPT")]
    [InlineData("FO_TEST_GND_PROX")]
    public async Task A_stop_during_a_test_still_releases_it(string key)
    {
        var test = Bbs757FoTests.For(key)!;
        var cores = new DefinitionCores();
        string hold, release;
        if (test.Kind == P61)
        {
            var button = MSFSBlindAssist.Aircraft.BBS757.Bbs757SpringControls.ForButtonKey(test.Target)!;
            (hold, release) = MSFSBlindAssist.Aircraft.BBS757.Bbs757SpringControls.Codes(button.Control, button.Positive);
            cores.Sim.Fresh[button.Control.LVar] = button.Control.Rest;   // the switch rests once let go
        }
        else
        {
            var press = MSFSBlindAssist.Aircraft.BBS757.Bbs757Write.PressFor(cores.Def.ControlFor(test.Target)!)!;
            (hold, release) = (press.Down, press.Up!);
        }
        cores.Sim.Hold = ordinal => ordinal == 0;   // the hold's first wait
        var exec = new X();
        exec.SetTransport(cores);
        using var stop = new CancellationTokenSource();

        var step = exec.ExecuteStepAsync(Sw(key, 1), stop.Token);
        Assert.False(step.IsCompleted);
        Assert.Equal(new[] { hold }, cores.Sim.Writes);
        stop.Cancel();
        cores.Sim.Release(0);

        Assert.False(await step.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(release, cores.Sim.Writes.Last());
        Assert.Equal(test.Kind == P61 ? 100 : 1, cores.Sim.Writes.Count(w => w == hold));   // never cut short
        Assert.Equal(test.Kind == P61 ? 3000 : 10_000, cores.Sim.Delays.TakeWhile(d => d != 250).Sum());
        Assert.Equal(0, cores.Sim.CanceledDelays);
        Assert.Empty(cores.AfterWaits);
    }
}
