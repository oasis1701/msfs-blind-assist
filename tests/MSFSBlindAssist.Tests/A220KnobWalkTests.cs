using System.Globalization;
using MSFSBlindAssist.Aircraft.A220;
using static MSFSBlindAssist.Aircraft.A220.A220Afdx;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The FCP knob walks' pure decisions and speech (the walks themselves are sim paths, checked
/// live). Which knob may stand on a stock var when the aircraft's own block is missing is the
/// [A220-8] / [A220-13] decision; every walk ends in one spoken line, never silence.
/// </summary>
public class A220KnobWalkTests
{
    // ---- [A220-8] / [A220-13]: the stock-var fallback ----------------------------------

    // FcpKnob is internal, so the theories name it as text (a public test method cannot take it).
    [Theory]
    [InlineData("Heading", true)]    // AUTOPILOT HEADING LOCK DIR tracks the bug (2026-09-21, -24)
    [InlineData("Altitude", true)]   // AUTOPILOT ALTITUDE LOCK VAR tracks the selector (2026-09-21)
    [InlineData("Vertical", false)]  // R3: the FCP never writes AUTOPILOT VERTICAL HOLD VAR for V/S, and
                                     // L:A22X Selected FPA was never shown to track the wheel
    [InlineData("Speed", false)]     // 445 kt read with 142 in the window (2026-09-24)
    [InlineData("StabTrim", false)]  // [A220-13]: never walk a stabilizer on the animation ratio
    public void OnlyKnobsWhoseStockVarTracksMayFallBackToIt(string knob, bool expected)
        => Assert.Equal(expected, KnobWalkMayUseStockVar(Enum.Parse<FcpKnob>(knob)));

    [Fact]
    public void EveryKnobHasAFallbackDecision()
        => Assert.Equal(5, Enum.GetValues<FcpKnob>().Length);

    // A store that has been quiet for 3 s reads as missing while the link is UP (the agent calls it
    // stale; the WASM broadcasts only changes): the trim at rest on the ground. So a missing field is
    // never "the link is down" until the read has asked for a whole copy and tried again.
    [Fact]
    public void AQuietStoreIsResyncedBeforeAReadGivesUp()
    {
        var resyncs = Enumerable.Range(0, AfdxReadAttempts).Where(AfdxResyncAfterMiss).ToList();
        Assert.True(AfdxResyncAfterMiss(0));                 // the first miss asks for a whole copy
        Assert.Equal(new[] { 0, 4 }, resyncs);                // and once more halfway
        Assert.True(resyncs[^1] < AfdxReadAttempts - 1);     // with attempts left to see the copy land
    }

    // A hung-but-open Coherent view answers no eval for 5 s each (EvalTimeoutMs): the whole read has a
    // wall-clock deadline, so a walk is never kept silent for tens of seconds. The deadline still
    // leaves room for every attempt and its 150 ms pause when the view answers at once.
    [Fact]
    public void AnAfdxReadHasADeadline_ThatStillFitsEveryAttempt()
    {
        Assert.Equal(3000, AfdxReadDeadlineMs);
        Assert.True(AfdxReadAttempts * 150 < AfdxReadDeadlineMs);
    }

    // ---- R3: V/S and FPA read back only from the FCP block -------------------------------

    // The FCP shows its wheel from vs_sel and vs_mode (instrument.js, FCP/Components/Vertical.tsx, and
    // the PFD's FMA): mode 0 is V/S in feet per minute, mode 1 is FPA in TENTHS of a degree
    // ("Math.abs(t / 10).toFixed(1)"), and a null selection is a wheel with no target.
    [Theory]
    [InlineData(0, -700.0, -700.0, null)]
    [InlineData(0, 1500.0, 1500.0, null)]
    [InlineData(1, -30.0, null, -3.0)]
    [InlineData(1, 25.0, null, 2.5)]
    [InlineData(1, -5.0, null, -0.5)]     // the old readout's trap: |vs_sel| <= 9.9 read as degrees said "-5.0"
    [InlineData(null, null, null, null)]  // no vertical mode: the wheel is dead
    [InlineData(0, null, null, null)]
    public void TheVerticalWheel_IsReadFromTheFcpBlock_FpaInTenths(int? mode, double? sel, double? vs, double? fpa)
    {
        var fcp = new FcpBlock { vs_mode = mode, vs_sel = sel };
        Assert.Equal(vs, SelectedVsFpm(fcp));
        Assert.Equal(fpa, SelectedFpaDegrees(fcp));
        Assert.Null(SelectedVsFpm(null));
        Assert.Null(SelectedFpaDegrees(null));
    }

    [Fact]
    public void TheVerticalWheel_IsReadyOnlyInTheModeAsked_ElseTheWalkSaysWhy()
    {
        var vs = new FcpBlock { vs_mode = 0, vs_sel = 0, alt_sel_ft = 5000 };
        var fpa = new FcpBlock { vs_mode = 1, vs_sel = 0, alt_sel_ft = 5000 };
        var dead = new FcpBlock { alt_sel_ft = 5000 };
        // Before the engage write: a block with anything in it is a link up; none, or the store at {}, refuses.
        Assert.True(FcpBlockHasData(dead));
        Assert.False(FcpBlockHasData(new FcpBlock()));
        Assert.False(FcpBlockHasData(null));
        Assert.Null(VerticalWheelRefusal(vs, isFpa: false));
        Assert.Null(VerticalWheelRefusal(fpa, isFpa: true));
        // No block, or an FCP store still at {} before a resync: the link gives no read-back, so refuse as speed does.
        Assert.Equal(KnobWalkNoReadText("Vertical speed"), VerticalWheelRefusal(null, isFpa: false));
        Assert.Equal(KnobWalkNoReadText("Flight path angle"), VerticalWheelRefusal(new FcpBlock(), isFpa: true));
        Assert.Equal("The vertical speed wheel has no target — VS mode did not engage. "
            + "Check flight director or autopilot state, then try again.", VerticalWheelRefusal(dead, isFpa: false));
        Assert.Equal("The vertical speed wheel has no target — FPA mode did not engage. "
            + "Check flight director or autopilot state, then try again.", VerticalWheelRefusal(dead, isFpa: true));
        Assert.Equal("Vertical speed not set: VS mode did not engage — the wheel is in FPA mode. "
            + "Check flight director or autopilot state, then try again.", VerticalWheelRefusal(fpa, isFpa: false));
        Assert.Equal("Flight path angle not set: FPA mode did not engage — the wheel is in VS mode. "
            + "Check flight director or autopilot state, then try again.", VerticalWheelRefusal(vs, isFpa: true));
    }

    // ---- R2: the shared step decision ------------------------------------------------------

    private static KnobWalkStep Decide(double delta, double prevDelta, double? step, int lastClicks, double tolerance)
        => KnobWalkDecision(delta, prevDelta, step, lastClicks, tolerance);

    [Fact]
    public void BeforeAStepIsMeasured_TheFirstMoveIsOneProbeClick()
    {
        // [A220-10]: "nothing closer" needs a step measured on THIS walk; no step is kept from an earlier one.
        Assert.Equal(new KnobWalkStep(KnobWalkAction.Click, 1), Decide(1500, double.NaN, null, 0, 55));
        Assert.Equal(new KnobWalkStep(KnobWalkAction.Click, 1), Decide(-3.0, double.NaN, null, 0, 0.06));
        Assert.Equal(new KnobWalkStep(KnobWalkAction.Done), Decide(40, double.NaN, null, 0, 55));
    }

    /// <summary>
    /// The walk-reliability report's V/S model (section 4): 0 to 1500 with clicks lost at the old 8 ms pace
    /// and the step re-derived from each lossy burst (24.4 fpm a click), so a 4-click burst carried it to
    /// 1600 and the old walk stopped there as "1600 — nearest selectable to 1500". A sign change after a
    /// BURST is an overshoot: the decision aims back.
    /// </summary>
    [Fact]
    public void TheReportsVsOvershoot_AfterABurst_AimsBack_NeverNearestSelectable()
        => Assert.Equal(new KnobWalkStep(KnobWalkAction.Click, 4), Decide(-100, 100, 24.4, lastClicks: 4, tolerance: 55));

    // The metres-mode altitude selector, 50 m (164 ft) a click, asked for 6000 ft.
    [Theory]
    [InlineData(-70.0, 94.0, "NearestHere")]   // 5906 -> 6070: 6070 is the nearer of the two
    [InlineData(-104.0, 60.0, "StepBack")]     // 5940 -> 6104: the value before the click was nearer
    public void AStraddleAfterOneClick_EndsOnTheNearerValue(double delta, double prevDelta, string expected)
        => Assert.Equal(Enum.Parse<KnobWalkAction>(expected), Decide(delta, prevDelta, 164.04, 1, 55).Action);

    // ---- R2: the shared loop, on models of the wheel -----------------------------------------

    /// <summary>A knob on a uniform step: a lone click always lands; a burst lands round(clicks x
    /// <see cref="BurstDelivery"/>) of its clicks, at least one (clicks are lost, never added).</summary>
    private sealed class Wheel(double start, double step)
    {
        private int _pos;
        public double BurstDelivery { get; init; } = 1.0;
        public bool IncLowers { get; init; }
        public int Moves { get; private set; }
        public double Highest { get; private set; } = double.MinValue;
        public double Value => start + _pos * step;

        public Task<double?> Move(KnobMove m)
        {
            Moves++;
            int landed = m.Clicks == 1 ? 1 : Math.Max(1, (int)Math.Round(m.Clicks * BurstDelivery));
            _pos += (IncLowers ? -m.Direction : m.Direction) * landed;
            Highest = Math.Max(Highest, Value);
            return Task.FromResult<double?>(Value);
        }
    }

    [Fact]
    public async Task VsWalk_OnTheReportsLossyWheel_LandsOn1500_NeverPastIt()
    {
        // The report's delivery: about 4 of 15 and 10 of 41 clicks landed. The step is measured on the
        // probe click and a lossy burst never shrinks it, so every aim is short, never long.
        var wheel = new Wheel(0, 100) { BurstDelivery = 0.27 };
        var r = await RunKnobWalkAsync(1500, wheel.Value, FcpWalkPolicy(55), wheel.Move);
        Assert.Equal(KnobWalkEnd.OnTarget, r.End);
        Assert.Equal(1500, r.Value);
        Assert.Equal(1500, wheel.Highest);
        Assert.Equal(100, r.Step);
    }

    [Theory]
    [InlineData(1500.0)]
    [InlineData(-700.0)]
    public async Task VsWalk_WithEveryClickLanding_IsAProbeThenOneBurst(double target)
    {
        var wheel = new Wheel(0, 100);
        var r = await RunKnobWalkAsync(target, wheel.Value, FcpWalkPolicy(55), wheel.Move);
        Assert.Equal(KnobWalkEnd.OnTarget, r.End);
        Assert.Equal(target, r.Value);
        Assert.Equal(2, wheel.Moves);
    }

    [Fact]
    public async Task FpaWalk_LandsOnMinus3()
    {
        var wheel = new Wheel(0, 0.1);
        var r = await RunKnobWalkAsync(-3.0, wheel.Value, FcpWalkPolicy(0.06), wheel.Move);
        Assert.Equal(KnobWalkEnd.OnTarget, r.End);
        Assert.Equal(-3.0, r.Value, 9);
    }

    [Fact]
    public async Task AKnobWiredBackwards_IsCalibratedOnTheProbe_AndStillLands()
    {
        // The stab trim: which event raises it is measured on the first move, never assumed.
        var wheel = new Wheel(3.1, 0.1) { IncLowers = true };
        var r = await RunKnobWalkAsync(5.0, wheel.Value, FcpWalkPolicy(0.05), wheel.Move);
        Assert.Equal(KnobWalkEnd.OnTarget, r.End);
        Assert.Equal(5.0, r.Value, 9);
        Assert.Equal(2, wheel.Moves);
    }

    private const double FeetPerMetre = 1 / 0.3048;

    [Fact]
    public async Task MetresAltitude_LandsOnTheNearestSelectable_AndSaysSo()
    {
        // 1550 m is 5085 ft (the live [A220-9] case); 6000 ft lies between 1800 m and 1850 m.
        var wheel = new Wheel(1550 * FeetPerMetre, 50 * FeetPerMetre);
        var r = await RunKnobWalkAsync(6000, wheel.Value, FcpWalkPolicy(55), wheel.Move);
        Assert.Equal(KnobWalkEnd.NearestSelectable, r.End);
        Assert.Equal(1850 * FeetPerMetre, r.Value, 6);
    }

    /// <summary>A metres selector left OFF its grid (1524 m): the first click only snaps up to the next
    /// 50 m, so the probe measures 85 ft where a click is 164 ft.</summary>
    private sealed class SnappingSelector(double startMetres)
    {
        private double _m = startMetres;
        public double Feet => _m * FeetPerMetre;

        public Task<double?> Move(KnobMove m)
        {
            for (int i = 0; i < m.Clicks; i++)
                _m = m.Direction > 0 ? Math.Floor(_m / 50 + 1e-9) * 50 + 50 : Math.Ceiling(_m / 50 - 1e-9) * 50 - 50;
            return Task.FromResult<double?>(Feet);
        }
    }

    [Fact]
    public async Task AProbeThatOnlySnapped_IsCorrectedByTheNextBurst_NoShuttling()
    {
        // A burst can lose clicks but never add any, so one that moved MORE per click than the probe
        // raises the step to what it showed; aiming on the probe's half step would shuttle across 6000.
        var selector = new SnappingSelector(1524);
        var r = await RunKnobWalkAsync(6000, selector.Feet, FcpWalkPolicy(55), selector.Move);
        Assert.Equal(KnobWalkEnd.NearestSelectable, r.End);
        Assert.Equal(1850 * FeetPerMetre, r.Value, 6);
        Assert.True(r.Rounds <= 4, $"took {r.Rounds} rounds");
    }

    [Fact]
    public async Task AWheelThatDoesNotMove_IsNotResponding_AfterOneProbe()
    {
        // A dead first click is no longer resent on another transport (9a): it is said at once.
        var wheel = new Wheel(0, 0);
        var r = await RunKnobWalkAsync(1500, wheel.Value, FcpWalkPolicy(55), wheel.Move);
        Assert.Equal(KnobWalkEnd.NotResponding, r.End);
        Assert.Equal(0, r.Value);
        Assert.Equal(1, wheel.Moves);
    }

    [Fact]
    public async Task AReadBackLostAfterAMove_ClaimsNoValue()
    {
        var r = await RunKnobWalkAsync(1500, 0, FcpWalkPolicy(55), _ => Task.FromResult<double?>(null));
        Assert.Equal(KnobWalkEnd.ReadLost, r.End);
    }

    [Fact]
    public async Task OutOfRounds_FarFromTheTarget_IsTheRoundLimit()
    {
        // Every burst lands just one click: ten moves reach 1000 of 1500.
        var wheel = new Wheel(0, 100) { BurstDelivery = 0 };
        var r = await RunKnobWalkAsync(1500, wheel.Value, FcpWalkPolicy(55), wheel.Move);
        Assert.Equal(KnobWalkEnd.RoundLimit, r.End);
        Assert.Equal(1000, r.Value);
        Assert.Equal(10, wheel.Moves);
    }

    [Fact]
    public async Task TheLastMoveIsJudged_SoALandingOnTargetIsNeverTheRoundLimit()
    {
        // Nine single-click moves and a last one that lands: on target, said plainly.
        var wheel = new Wheel(0, 100) { BurstDelivery = 0 };
        var r = await RunKnobWalkAsync(1000, wheel.Value, FcpWalkPolicy(55), wheel.Move);
        Assert.Equal(KnobWalkEnd.OnTarget, r.End);
        Assert.Equal(1000, r.Value);
        Assert.Equal(10, wheel.Moves);
    }

    [Fact]
    public async Task ACancelledWalk_Throws_SoItEndsInSilence()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var wheel = new Wheel(0, 100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RunKnobWalkAsync(1500, 0, FcpWalkPolicy(55), wheel.Move, cts.Token));
        Assert.Equal(0, wheel.Moves);
    }

    [Theory]
    [InlineData(10, 400.0, 100.0, 4.0)]   // a single-click step: moved 400 = 4 clicks
    [InlineData(10, 400.0, null, 10.0)]   // no step yet: counted as delivered
    [InlineData(10, -250.0, 100.0, 2.5)]
    public void APacesInputIsWhatABurstMovedInClicks(int clicks, double moved, double? step, double expected)
        => Assert.Equal(expected, KnobWalkMovedClicks(moved, step, clicks), 9);

    // ---- R2(c): what the walk says ------------------------------------------------------------

    private static string Ft(double d) => FormattableString.Invariant($"{Math.Round(d)} feet");

    [Fact]
    public void FcpWalkResult_IsOneLine_ForEveryEnd()
    {
        Func<double, string> fpm = VerticalSpeedText;
        Assert.Equal("Vertical speed 1500 feet per minute",
            FcpWalkResultText(new KnobWalkResult(KnobWalkEnd.OnTarget, 1500, 100, 2), "Vertical speed", 1500, fpm));
        Assert.Equal("Altitude 6070 feet — nearest selectable to 6000 feet",
            FcpWalkResultText(new KnobWalkResult(KnobWalkEnd.NearestSelectable, 6069.6, 164.04, 3), "Altitude", 6000, Ft));
        // Out of rounds is never "nearest selectable": the last landing was judged and still wanted to move.
        Assert.Equal("Vertical speed could not reach 1500 feet per minute, now 1400 feet per minute",
            FcpWalkResultText(new KnobWalkResult(KnobWalkEnd.RoundLimit, 1400, 100, 10), "Vertical speed", 1500, fpm));
        Assert.Equal("Vertical speed knob is not responding — value unchanged at -200 feet per minute.",
            FcpWalkResultText(new KnobWalkResult(KnobWalkEnd.NotResponding, -200, null, 1), "Vertical speed", 1500, fpm));
        Assert.Equal(KnobWalkNotConfirmedText("Stabilizer trim"),
            FcpWalkResultText(new KnobWalkResult(KnobWalkEnd.ReadLost, 4.1, 0.1, 3), "Stabilizer trim", 5.0, StabTrimUnitsText));
    }

    [Theory]
    [InlineData(-700.0, "-700 feet per minute")]
    [InlineData(1500.0, "1500 feet per minute")]
    [InlineData(-1449.6, "-1450 feet per minute")]
    public void VerticalSpeedValue_IsSpokenInFeetPerMinute(double fpm, string expected)
        => Assert.Equal(expected, VerticalSpeedText(fpm));

    // ---- Item 12 / [CORE-17]: what the pilot types --------------------------------------------

    [Theory]
    [InlineData(".78", true, 0.78)]
    [InlineData("M.78", true, 0.78)]
    [InlineData("0,78", true, 0.78)]      // a comma-decimal PC
    [InlineData("250", false, 250.0)]
    public void ATypedSpeed_ReadsTheSameOnAnyCulture(string typed, bool mach, double value)
    {
        Assert.True(MSFSBlindAssist.Aircraft.SynapticA220Definition.TryParseSpeed(typed, out bool isMach, out double v));
        Assert.Equal((mach, value), (isMach, v));
    }

    [Theory]
    [InlineData("1.500")]   // a thousands group on a de-DE PC, or 1.5: refused, never guessed
    [InlineData("0,05")]    // below Mach 0.10
    [InlineData("400")]
    [InlineData("fast")]
    public void ATypedSpeed_OutOfRangeOrAmbiguous_IsRefused(string typed)
        => Assert.False(MSFSBlindAssist.Aircraft.SynapticA220Definition.TryParseSpeed(typed, out _, out _));

    [Theory]
    [InlineData("-3,0", -3.0, true)]
    [InlineData("-3.0", -3.0, true)]
    [InlineData("2,5", 2.5, true)]
    [InlineData("1500", 1500.0, false)]
    [InlineData("-700", -700.0, false)]
    public void ATypedVerticalTarget_IsAnFpaWithinNinePointNine_ElseAVerticalSpeed(string typed, double target, bool fpa)
    {
        Assert.True(MSFSBlindAssist.Aircraft.SynapticA220Definition.TryParseVerticalTarget(typed, out double t, out bool isFpa));
        Assert.Equal((target, fpa), (t, isFpa));
    }

    [Theory]
    [InlineData("1.500")]   // 1500 fpm on a de-DE PC, or 1.5 degrees: refused, never guessed
    [InlineData("50")]      // neither an FPA nor a vertical speed of 100 or more
    [InlineData("9000")]
    [InlineData("")]
    public void ATypedVerticalTarget_OutOfRangeOrAmbiguous_IsRefused(string typed)
        => Assert.False(MSFSBlindAssist.Aircraft.SynapticA220Definition.TryParseVerticalTarget(typed, out _, out _));

    [Fact]
    public void TypedTargets_ParseTheSameUnderACommaCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.True(MSFSBlindAssist.Aircraft.SynapticA220Definition.TryParseVerticalTarget("-3.0", out double t, out bool fpa));
            Assert.Equal((-3.0, true), (t, fpa));
            Assert.True(MSFSBlindAssist.Aircraft.SynapticA220Definition.TryParseSpeed(".78", out bool mach, out double m));
            Assert.Equal((true, 0.78), (mach, m));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    // ---- Item 12: the dialogs' prompts --------------------------------------------------------

    [Fact]
    public void TheSpeedPrompt_SaysTheFcpsOwnSelection()
    {
        Assert.Equal("Mach 0.78", SpeedPromptNow(new FcpBlock { spd_in_mach = true, spd_sel_mach = 0.78 }, isMach: true));
        Assert.Equal("250 knots", SpeedPromptNow(new FcpBlock { spd_sel_ias = 250 }, isMach: false));
        Assert.Equal("unknown", SpeedPromptNow(null, isMach: false));
        Assert.Equal("unknown", SpeedPromptNow(new FcpBlock(), isMach: true));
    }

    [Fact]
    public void TheVerticalPrompt_SaysTheWheelsOwnSelection_ElseTheOldReads()
    {
        Assert.Equal("VS now -700 fpm", VerticalPromptNow(new FcpBlock { vs_mode = 0, vs_sel = -700 }, 0, 0));
        Assert.Equal("FPA now -3.0°", VerticalPromptNow(new FcpBlock { vs_mode = 1, vs_sel = -30 }, 0, 0));
        Assert.Equal("wheel inactive", VerticalPromptNow(new FcpBlock { alt_sel_ft = 5000 }, 0, 0));
        // No block: the dialog's old stock reads, which the walk itself never trusts (R3).
        Assert.Equal("VS now -500 fpm, FPA -2.5°", VerticalPromptNow(null, -500, -2.5));
    }

    [Fact]
    public void StabTrim_RefusesWithTheLinkDown_SayingWhyAndWhatToDo()
    {
        Assert.Equal(StabTrimLinkDownText, KnobWalkRefusalText(FcpKnob.StabTrim, "Stabilizer trim"));
        Assert.Equal("Stabilizer trim not set — the display link is down, so the trim cannot be read back. "
            + "Try again when the link is back.", StabTrimLinkDownText);
    }

    [Theory]
    [InlineData("Speed", "Speed", "Speed not set: the aircraft is not reporting its selected speed right now. Try again in a moment.")]
    [InlineData("Heading", "Heading", "Heading not set: the aircraft is not reporting its selected heading right now. Try again in a moment.")]
    [InlineData("Vertical", "Vertical speed", "Vertical speed not set: the aircraft is not reporting its selected vertical speed right now. Try again in a moment.")]
    public void NoReadingBeforeTheFirstClick_RefusesWithoutAGuess(string knob, string name, string expected)
        => Assert.Equal(expected, KnobWalkRefusalText(Enum.Parse<FcpKnob>(knob), name));

    // ---- One spoken line on every way out (R6, R7) ------------------------------------

    [Theory]
    [InlineData("Heading", "Not connected to the simulator — heading not changed.")]
    [InlineData("Stabilizer trim", "Not connected to the simulator — stabilizer trim not changed.")]
    public void NotConnected_RefusesBeforeAnyClick(string name, string expected)
        => Assert.Equal(expected, KnobWalkNotConnectedText(name));

    [Theory]
    [InlineData("Altitude", "Altitude not confirmed — the aircraft stopped reporting it. Check the altitude.")]
    [InlineData("Flight path angle", "Flight path angle not confirmed — the aircraft stopped reporting it. Check the flight path angle.")]
    public void LostReadBack_IsNotConfirmed_NeverAGuessedValue(string name, string expected)
        => Assert.Equal(expected, KnobWalkNotConfirmedText(name));

    [Theory]
    [InlineData("Speed", "Speed walk stopped — check the speed.")]
    [InlineData("Vertical speed", "Vertical speed walk stopped — check the vertical speed.")]
    public void AFailedWalk_SaysSo_InTheStandbyBaroPattern(string name, string expected)
        => Assert.Equal(expected, KnobWalkFailedText(name));

    // ---- The trim walk's spoken numbers -------------------------------------------------

    private static FlightControlBlock Fc(double? dn = null, double? up = null)
        => new() { pitch_trim_dn = dn, pitch_trim_up = up };

    [Theory]
    [InlineData(4.6, "4.6 units")]
    [InlineData(5.0, "5.0 units")]
    [InlineData(0.04, "0.0 units")]
    public void StabTrimUnits_AreTheEicasFigure(double units, string expected)
        => Assert.Equal(expected, StabTrimUnitsText(units));

    [Fact]
    public void StabTrimBandNote_QuotesTheTakeoffBand_OnlyWhenTheAircraftPublishesOne()
    {
        Assert.Equal(", in takeoff range", StabTrimWalkBandNote(5.0, Fc(4.2, 6.8)));
        Assert.Equal(", still OUTSIDE takeoff range 4.2 to 6.8", StabTrimWalkBandNote(3.1, Fc(4.2, 6.8)));
        Assert.Equal("", StabTrimWalkBandNote(5.0, null));
        Assert.Equal("", StabTrimWalkBandNote(5.0, Fc(dn: 4.2)));
        Assert.Equal("", StabTrimWalkBandNote(5.0, Fc(5.0, 5.0)));   // equal limits: the EICAS hides the band
    }

    // ---- The speed and FPA walks' spoken numbers ----------------------------------------

    [Theory]
    [InlineData(78, "Mach 0.78")]   // the Mach walk counts the knob in hundredths
    [InlineData(80, "Mach 0.80")]
    public void MachWalkValue_IsSpokenAsMach(double hundredths, string expected)
        => Assert.Equal(expected, MachHundredthsText(hundredths));

    [Theory]
    [InlineData(-3.0, "-3.0 degrees")]
    [InlineData(2.5, "2.5 degrees")]
    public void FpaWalkValue_IsSpokenInDegrees(double degrees, string expected)
        => Assert.Equal(expected, FpaDegreesText(degrees));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void WalkSpeech_IsCultureInvariant(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("4.6 units", StabTrimUnitsText(4.6));
            Assert.Equal(", still OUTSIDE takeoff range 4.2 to 6.8", StabTrimWalkBandNote(3.1, Fc(4.2, 6.8)));
            Assert.Equal("Mach 0.78", MachHundredthsText(78));
            // sv-SE writes a minus as U+2212: the reader must hear the ASCII sign MSFSBA speaks everywhere.
            Assert.Equal("-3.0 degrees", FpaDegreesText(-3.0));
            Assert.Equal("-700 feet per minute", VerticalSpeedText(-700));
            Assert.Equal("Vertical speed could not reach -1500 feet per minute, now -1400 feet per minute",
                FcpWalkResultText(new KnobWalkResult(KnobWalkEnd.RoundLimit, -1400, 100, 10), "Vertical speed", -1500, VerticalSpeedText));
            Assert.Equal("Flight path angle -2.9 degrees — nearest selectable to -3.0 degrees",
                FcpWalkResultText(new KnobWalkResult(KnobWalkEnd.NearestSelectable, -2.9, 0.1, 3), "Flight path angle", -3.0, FpaDegreesText));
            Assert.Equal("Mach 0.78", SpeedPromptNow(new FcpBlock { spd_in_mach = true, spd_sel_mach = 0.78 }, isMach: true));
            Assert.Equal("VS now -700 fpm", VerticalPromptNow(new FcpBlock { vs_mode = 0, vs_sel = -700 }, 0, 0));
            Assert.Equal("FPA now -3.0°", VerticalPromptNow(new FcpBlock { vs_mode = 1, vs_sel = -30 }, 0, 0));
            Assert.Equal("VS now -500 fpm, FPA -2.5°", VerticalPromptNow(null, -500, -2.5));
            Assert.Equal("Vertical speed not set: VS mode did not engage — the wheel is in FPA mode. "
                + "Check flight director or autopilot state, then try again.",
                VerticalWheelRefusal(new FcpBlock { vs_mode = 1, vs_sel = -30 }, isFpa: false));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
