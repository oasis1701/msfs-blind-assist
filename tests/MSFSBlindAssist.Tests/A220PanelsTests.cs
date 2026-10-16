using System.Globalization;
using MSFSBlindAssist.Aircraft.A220;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Characterization tests for the A220 panel helpers (Plan B): the speedbrake lever's cockpit
/// write and detent read-back ([A220-40]) and the spoken press confirmations (CORE-7). CI runs
/// en-US, so every number that reaches the sim or the screen reader is also checked under a
/// comma-decimal culture.
/// </summary>
public class A220PanelsTests
{
    [Theory]
    [InlineData(0, "0 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(1, "0.2 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(2, "0.4 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(3, "0.6 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(4, "0.8 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(5, "1 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(-3, "0 0.5 - 32768 * (>K:SPOILERS_SET)")]
    [InlineData(9, "1 0.5 - 32768 * (>K:SPOILERS_SET)")]
    public void SpeedbrakeCalc_IsTheCockpitLeversOwnWrite(int detent, string expected)
        => Assert.Equal(expected, A220Panels.SpeedbrakeCalc(detent));

    [Fact]
    public void SpeedbrakeCalc_IsCultureInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("0.6 0.5 - 32768 * (>K:SPOILERS_SET)", A220Panels.SpeedbrakeCalc(3));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.09, 0)]
    [InlineData(0.13, 1)]
    [InlineData(0.31, 2)]
    [InlineData(0.6, 3)]
    [InlineData(0.79, 4)]
    [InlineData(1.0, 5)]
    [InlineData(-0.02, 0)]   // read-back a hair below RET
    [InlineData(1.03, 5)]    // a hair above MAX
    public void SpeedbrakeDetentForTravel_PicksTheNearestDetent(double travel, double detent)
        => Assert.Equal(detent, A220Panels.SpeedbrakeDetentForTravel(travel));

    [Fact]
    public void SpeedbrakeDetentForTravel_TreatsNaNAsRetracted()
        => Assert.Equal(0, A220Panels.SpeedbrakeDetentForTravel(double.NaN));

    [Fact]
    public void SpeedbrakeDetents_HaveOneLabelEach()
    {
        Assert.Equal(6, A220Panels.SpeedbrakeDetentTravel.Count);
        Assert.Equal(A220Panels.SpeedbrakeDetentTravel.Count, A220Panels.SpeedbrakeDetentLabels.Count);
        Assert.Equal("Retracted", A220Panels.SpeedbrakeDetentLabels[0]);
        Assert.Equal("Max (emergency)", A220Panels.SpeedbrakeDetentLabels[5]);
    }

    [Theory]
    [InlineData("Left Engine Fire (guarded)", "Left Engine Fire pressed")]
    [InlineData("CVR Test", "CVR Test pressed")]
    [InlineData("Mechanic Call", "Mechanic Call pressed")]
    public void PressedText_DropsTheGuardNote(string displayName, string expected)
        => Assert.Equal(expected, A220Panels.PressedText(displayName));

    [Theory]
    [InlineData(1, "Transmit VHF 1")]
    [InlineData(2, "Transmit VHF 2")]
    [InlineData(3, "Transmit VHF 3")]
    [InlineData(0, "No transmit radio selected")]
    [InlineData(5, "Transmit channel 5")]
    [InlineData(-1, "Transmit radio not available")]
    public void TxChannelText_NamesTheSelectedTransmitter(double value, string expected)
        => Assert.Equal(expected, A220Panels.TxChannelText(value));

    [Fact]
    public void PressConfirmKeys_AreTheStatelessPresses()
        => Assert.Equal(
            new[] { "A22X_CVR_ERASE", "A22X_CVR_TEST", "A22X_L_ENG_FIRE", "A22X_MECH_CALL", "A22X_R_ENG_FIRE" },
            A220Panels.PressConfirmKeys.OrderBy(k => k, StringComparer.Ordinal));

    /// <summary>
    /// Live 2026-10-16: the stock altimeter 3 the aircraft keeps for the ISI has a real STD flag —
    /// <c>KOHLSMAN SETTING STD:3</c> reads 1 after an STD press (MB:3 then 1013.25), 0 after the
    /// next knob click, and stays 1 on a second press (STD is a one-shot set, not a toggle). So the
    /// STD push reads it back instead of saying "pressed": STD when set, the setting the ISI kept
    /// when clear (the press did not land), and "not available" when nothing came back (CORE-7).
    /// </summary>
    [Theory]
    [InlineData(1.0, null, "Standby altimeter STD")]
    [InlineData(1.0, 1013.2504272460938, "Standby altimeter STD")]
    [InlineData(0.0, 1013.5625, "Standby altimeter 1014 hectopascals")]   // one click off STD, as the ISI shows it
    [InlineData(0.0, 1020.0, "Standby altimeter 1020 hectopascals")]
    [InlineData(0.0, 1012.875, "Standby altimeter 1013 hectopascals")]
    [InlineData(null, 1013.25, "Standby altimeter reading not available")] // no STD read: a 1013 is not claimed as STD or QNH
    [InlineData(0.0, null, "Standby altimeter reading not available")]
    [InlineData(double.NaN, 1013.25, "Standby altimeter reading not available")]
    [InlineData(0.0, double.NaN, "Standby altimeter reading not available")]
    public void IsiStdReadBackText_SaysStdOrTheSetting_NeverAGuess(double? std, double? mb, string expected)
        => Assert.Equal(expected, A220Panels.IsiStdReadBackText(std, mb));

    [Fact]
    public void StandbyStdPush_IsReadBack_NotAPressConfirmation()
        => Assert.DoesNotContain("A22X_ISI_BARO_STD", A220Panels.PressConfirmKeys);

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void IsiStdReadBackText_IsCultureInvariant(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("Standby altimeter 1014 hectopascals", A220Panels.IsiStdReadBackText(0.0, 1013.5625));
            Assert.Equal("Standby altimeter 1017 hectopascals", A220Panels.IsiStdReadBackText(0.0, 1016.625));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    // ---- Audio control panel: VHF receive and volume ([A220-44]) ------------------------

    [Fact]
    public void AcpVolume_HasTheKnobsTwentyNineDetents_InPercent()
    {
        Assert.Equal(28, A220Panels.AcpVolumeSteps);
        Assert.Equal(29, A220Panels.AcpVolumeLabels.Count);
        Assert.Equal("0%", A220Panels.AcpVolumeLabels[0]);
        Assert.Equal("4%", A220Panels.AcpVolumeLabels[1]);
        Assert.Equal("50%", A220Panels.AcpVolumeLabels[14]);
        Assert.Equal("100%", A220Panels.AcpVolumeLabels[28]);
        Assert.Equal(29, A220Panels.AcpVolumeLabels.Distinct().Count());   // every detent says something different
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.5, 14)]
    [InlineData(1.0, 28)]
    [InlineData(0.0357, 1)]     // 1/28 to four decimals still reads as detent 1
    [InlineData(0.99, 28)]
    [InlineData(-0.1, 0)]
    [InlineData(1.2, 28)]
    public void AcpVolumeStepFor_PicksTheNearestDetent(double raw, double step)
        => Assert.Equal(step, A220Panels.AcpVolumeStepFor(raw));

    [Fact]
    public void AcpVolumeStepFor_TreatsNaNAsZero()
        => Assert.Equal(0, A220Panels.AcpVolumeStepFor(double.NaN));

    [Theory]
    [InlineData(1, 0, "0 28 / (>L:A22X ACP VHF1 Volume)")]
    [InlineData(2, 14, "14 28 / (>L:A22X ACP VHF2 Volume)")]
    [InlineData(3, 28, "28 28 / (>L:A22X ACP VHF3 Volume)")]
    [InlineData(2, 40, "28 28 / (>L:A22X ACP VHF2 Volume)")]
    [InlineData(2, -3, "0 28 / (>L:A22X ACP VHF2 Volume)")]
    public void AcpVolumeCalc_IsTheKnobsOwnScale_NeverTheStepIndex(int vhf, int step, string expected)
        => Assert.Equal(expected, A220Panels.AcpVolumeCalc(vhf, step));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void AcpVolume_IsCultureInvariant(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("14 28 / (>L:A22X ACP VHF2 Volume)", A220Panels.AcpVolumeCalc(2, 14));
            Assert.Equal("VHF 2 volume did not change — reads 25%.", A220Panels.AcpVolumeReadBack(2, 14, 0.25));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(2, true, 1.0, null)]
    [InlineData(2, false, 0.0, null)]
    [InlineData(2, true, 0.0, "VHF 2 receiver did not change — still off.")]
    [InlineData(1, false, 1.0, "VHF 1 receiver did not change — still on.")]
    public void AcpReceiverReadBack_SpeaksOnlyWhenTheAircraftKeptAnotherState(int vhf, bool wantOn, double now, string? expected)
        => Assert.Equal(expected, A220Panels.AcpReceiverReadBack(vhf, wantOn, now));

    [Theory]
    [InlineData(2, 14, 0.5, null)]
    [InlineData(2, 28, 1.0, null)]
    [InlineData(2, 14, 0.25, "VHF 2 volume did not change — reads 25%.")]
    public void AcpVolumeReadBack_SpeaksOnlyWhenTheAircraftKeptAnotherLevel(int vhf, int step, double now, string? expected)
        => Assert.Equal(expected, A220Panels.AcpVolumeReadBack(vhf, step, now));

    [Fact]
    public void AcpReadBacks_WithNothingReadBack_AreNotConfirmed_NeverAClaimedState()
    {
        Assert.Equal("VHF 2 receiver not confirmed: the A220 did not report it.", A220Panels.AcpReceiverReadBack(2, true, null));
        Assert.Equal("VHF 3 volume not confirmed: the A220 did not report it.", A220Panels.AcpVolumeReadBack(3, 14, null));
        Assert.Equal("VHF 3 volume not confirmed: the A220 did not report it.", A220Panels.AcpVolumeReadBack(3, 14, double.NaN));
    }

    // ---- Standby altimeter (ISI, [A220-43]) ----------------------------------------------

    [Theory]
    [InlineData(1013, 1013.0)]
    [InlineData(1013.2, 1013.2)]
    [InlineData(940, 940.0)]
    [InlineData(1090, 1090.0)]
    [InlineData(29.92, 1013.21)]
    [InlineData(27.0, 914.33)]
    [InlineData(32.0, 1083.64)]
    public void IsiTargetHpa_AcceptsHectopascalsOrInches(double entry, double expectedHpa)
        => Assert.Equal(expectedHpa, A220Panels.IsiTargetHpa(entry)!.Value, 2);

    [Theory]
    [InlineData(2992)]        // inches typed without the decimal point
    [InlineData(33)]
    [InlineData(26.99)]
    [InlineData(939.9)]
    [InlineData(1090.1)]
    [InlineData(0)]           // a blank box reaches the handler as 0
    [InlineData(double.NaN)]  // an unparseable box (UnparseableTextAsNaN)
    public void IsiTargetHpa_RefusesEverythingElse(double entry)
        => Assert.Null(A220Panels.IsiTargetHpa(entry));

    [Theory]
    [InlineData(29.92, true)]
    [InlineData(27.0, true)]
    [InlineData(1013, false)]
    public void IsInHgEntry_TellsInchesFromHectopascals(double entry, bool inHg)
        => Assert.Equal(inHg, A220Panels.IsInHgEntry(entry));

    [Theory]
    [InlineData("{\"ok\":true,\"isi\":{\"powered\":true,\"qnh_hpa\":1013.25},\"diag\":{}}", 1013.25)]
    [InlineData("{\"ok\":true,\"isi\":{\"powered\":null,\"qnh_hpa\":1020},\"diag\":{}}", 1020.0)]
    public void ParseIsiRead_ReadsTheIsiStore(string json, double expected)
    {
        var read = A220Panels.ParseIsiRead(json);
        Assert.Equal(A220Panels.IsiReadStatus.Ok, read.Status);
        Assert.Equal(expected, read.QnhHpa!.Value, 3);
    }

    /// <summary>No QNH, and why: the ISI's own store saying it is unpowered is told apart from a
    /// missing link, store or value, so the walk can say "not powered" in a cold cockpit.</summary>
    [Theory]
    [InlineData(null, A220Panels.IsiReadStatus.NoReading)]
    [InlineData("", A220Panels.IsiReadStatus.NoReading)]
    [InlineData("not json", A220Panels.IsiReadStatus.NoReading)]
    [InlineData("{\"ok\":false}", A220Panels.IsiReadStatus.NoReading)]
    [InlineData("{\"ok\":false,\"isi\":null,\"diag\":{}}", A220Panels.IsiReadStatus.NoReading)]
    [InlineData("{\"ok\":true,\"isi\":null}", A220Panels.IsiReadStatus.NoReading)]
    [InlineData("{\"ok\":true,\"isi\":{\"powered\":true,\"qnh_hpa\":null}}", A220Panels.IsiReadStatus.NoReading)]
    [InlineData("{\"ok\":true,\"isi\":{\"powered\":null,\"qnh_hpa\":null}}", A220Panels.IsiReadStatus.NoReading)]
    [InlineData("{\"ok\":true,\"isi\":{\"powered\":false,\"qnh_hpa\":1013}}", A220Panels.IsiReadStatus.Unpowered)]
    [InlineData("{\"ok\":true,\"isi\":{\"powered\":false,\"qnh_hpa\":null}}", A220Panels.IsiReadStatus.Unpowered)]
    public void ParseIsiRead_SaysWhyThereIsNoValue(string? json, A220Panels.IsiReadStatus status)
    {
        var read = A220Panels.ParseIsiRead(json);
        Assert.Equal(status, read.Status);
        Assert.Null(read.QnhHpa);
    }

    /// <summary>The zero value of the status is NoReading, so an unset <c>IsiRead</c> is "no
    /// reading", never an Ok that carries no QNH.</summary>
    [Fact]
    public void IsiRead_DefaultIsNoReading_NeverOk()
    {
        var unset = default(A220Panels.IsiRead);
        Assert.Equal(A220Panels.IsiReadStatus.NoReading, unset.Status);
        Assert.Null(unset.QnhHpa);
        Assert.Equal(default(A220Panels.IsiReadStatus), A220Panels.IsiReadStatus.NoReading);
    }

    /// <summary>The walk reads its value from the stock altimeter 3 over SimConnect, so a missing
    /// reading no longer blames the display link (which the walk asks only whether the ISI is powered).</summary>
    [Fact]
    public void IsiUnavailableText_NamesTheReason_NeverAskingToSetItOnTheIsi()
    {
        Assert.Equal("Standby altimeter not powered.", A220Panels.IsiUnavailableText(A220Panels.IsiReadStatus.Unpowered));
        Assert.Equal("Standby altimeter reading not available.",
            A220Panels.IsiUnavailableText(A220Panels.IsiReadStatus.NoReading));
        Assert.DoesNotContain("Set it on the ISI", A220Panels.IsiUnavailableText(A220Panels.IsiReadStatus.NoReading));
        Assert.DoesNotContain("display link", A220Panels.IsiUnavailableText(A220Panels.IsiReadStatus.NoReading));
    }

    [Fact]
    public void IsiReadLostText_SaysNotPoweredWhenTheIsiSaysSo()
    {
        Assert.Equal("Standby altimeter not powered.", A220Panels.IsiReadLostText(A220Panels.IsiReadStatus.Unpowered));
        Assert.Equal("Standby altimeter reading lost — check the ISI.", A220Panels.IsiReadLostText(A220Panels.IsiReadStatus.NoReading));
    }

    [Theory]
    [InlineData(1020.0, 1019.0, 1.0, false, "Standby altimeter 1019 hectopascals — nearest selectable")]  // one step short
    [InlineData(1020.0, 1021.0, 1.0, false, "Standby altimeter 1021 hectopascals — nearest selectable")]  // one step over
    [InlineData(1013.21, 1014.0, 1.0, true, "Standby altimeter 29.94 inches — nearest selectable")]
    [InlineData(1020.0, 1009.0, 1.0, false, "Standby altimeter could not reach 1020 hectopascals, now 1009 hectopascals.")]
    [InlineData(1013.21, 1003.0, 1.0, true, "Standby altimeter could not reach 29.92 inches, now 29.62 inches.")]
    [InlineData(1020.0, 1019.0, null, false, "Standby altimeter could not reach 1020 hectopascals, now 1019 hectopascals.")] // no step measured
    public void IsiRoundLimitText_IsNearestOnlyWithinOneStep(double target, double current, double? step, bool inHg, string expected)
        => Assert.Equal(expected, A220Panels.IsiRoundLimitText(target, current, step, inHg));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void IsiRoundLimitText_IsCultureInvariant(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("Standby altimeter could not reach 29.92 inches, now 29.62 inches.",
                A220Panels.IsiRoundLimitText(1013.21, 1003.0, 1.0, inHg: true));
            Assert.Equal("Standby altimeter 30.08 inches — nearest selectable", A220Panels.IsiNearestText(1018.6, inHg: true));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(1013.25, false, "Standby altimeter 1013 hectopascals")]
    [InlineData(1013.21, true, "Standby altimeter 29.92 inches")]
    public void FormatIsiBaro_SpeaksTheUnitThePilotTyped(double hpa, bool inHg, string expected)
        => Assert.Equal(expected, A220Panels.FormatIsiBaro(hpa, inHg));

    [Fact]
    public void FormatIsiBaro_IsCultureInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("Standby altimeter 30.08 inches", A220Panels.FormatIsiBaro(1018.6, inHg: true));
            Assert.Equal("Standby altimeter 1019 hectopascals", A220Panels.FormatIsiBaro(1018.6, inHg: false));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    // ---- Standby baro walk: the per-round decision ([A220-43]) ---------------------------

    /// <summary>One round, the last move one click unless a test says otherwise.</summary>
    private static A220Panels.IsiWalkStep Decide(double delta, double prevDelta, double? step, int lastClicks = 1)
        => A220Panels.IsiWalkDecision(delta, prevDelta, step, lastClicks);

    private static A220Panels.IsiWalkStep Done => new(A220Panels.IsiWalkAction.Done);
    private static A220Panels.IsiWalkStep NearestHere => new(A220Panels.IsiWalkAction.NearestHere);
    private static A220Panels.IsiWalkStep StepBack => new(A220Panels.IsiWalkAction.StepBack);
    private static A220Panels.IsiWalkStep Click(int n) => new(A220Panels.IsiWalkAction.Click, n);

    [Fact]
    public void IsiWalkDecision_BlindStage_OnTarget_IncludingTheFloatingPointEdge()
    {
        // 1013.2 typed against an ISI reading 1013: in floating point the gap is
        // 0.20000000000004547, a hair over the 0.2 blind tolerance. It must still count as on target,
        // or the probe click overshoots to 1014 (trigger B of the review finding).
        double gap = 1013.2 - 1013.0;
        Assert.True(gap > 0.2, "the premise: the raw gap is above 0.2");
        Assert.Equal(Done, Decide(gap, double.NaN, null));
        Assert.Equal(Done, Decide(0.0, double.NaN, null));
        Assert.Equal(Done, Decide(0.2, double.NaN, null));
        Assert.Equal(Done, Decide(-0.2, double.NaN, null));
    }

    [Theory]
    [InlineData(0.21)]     // 29.92 inches (1013.21 hPa) against an ISI reading 1013: one probe click, not a guess
    [InlineData(-0.21)]
    [InlineData(5.0)]
    [InlineData(-120.0)]   // far away: still ONE click, because the step is not measured yet
    public void IsiWalkDecision_BlindStage_OffTarget_IsOneProbeClick(double delta)
        => Assert.Equal(Click(1), Decide(delta, double.NaN, null));

    [Fact]
    public void IsiWalkDecision_OvershootWhereThePreviousValueWasNearer_StepsBackOneClick()
    {
        // Entry 29.92 = 1013.21 hPa, ISI at 1013 -> probe click -> 1014, step measured 1.
        double target = A220Panels.IsiTargetHpa(29.92)!.Value;
        double prevDelta = target - 1013.0;   //  0.21
        double delta = target - 1014.0;       // -0.79
        Assert.Equal(StepBack, Decide(delta, prevDelta, 1.0));
        // ... and the mirror: from above, down to a value below the target.
        Assert.Equal(StepBack, Decide(0.79, -0.21, 1.0));
    }

    [Theory]
    [InlineData(-0.6, 1.7)]    // the last click carried it from 1.7 above to 0.6 below: here is nearer
    [InlineData(0.6, -1.7)]
    [InlineData(-0.6, 0.6)]    // a tie stays where it is, never a click back
    public void IsiWalkDecision_OvershootWhereTheCurrentValueIsNearer_StopsHere(double delta, double prevDelta)
        => Assert.Equal(NearestHere, Decide(delta, prevDelta, 1.0));

    /// <summary>
    /// A burst of several clicks that lands past the target is an overshoot, never a straddle: the
    /// value before the burst is several clicks away. Live 2026-10-16 one click is the ISI's
    /// 0.01-inch step (about 0.34 hPa), which altimeter 3 reports on a 1/16-hPa grid, so successive
    /// clicks read 0.3125 or 0.375 hPa apart: a step measured on the probe click can be 8 % short and
    /// a long burst lands clicks past the target. Walking 1013.25 to 1030: the probe reads 1013.5625,
    /// then 53 clicks land on 1031.5 — it must aim back, not stop there as "nearest selectable".
    /// </summary>
    [Theory]
    [InlineData(-1.5, 16.4375, 0.33844, 53, 4)]
    [InlineData(1.5, -16.4375, 0.33844, 53, 4)]
    [InlineData(-0.33, 2.3575, 0.33594, 8, 1)]   // 30.00 inches: 8 clicks land on 30.01, one back is 30.00
    public void IsiWalkDecision_OvershootAfterABurst_AimsBack(double delta, double prevDelta, double step, int lastClicks, int clicks)
        => Assert.Equal(Click(clicks), Decide(delta, prevDelta, step, lastClicks));

    // ---- The walk on altimeter 3's measured grid ----------------------------------------------

    /// <summary>KOHLSMAN SETTING MB:3 measured live 2026-10-16, one click below STD up to ten above.</summary>
    private static readonly double[] MeasuredAltimeter3 =
    {
        1012.875, 1013.2504272460938, 1013.5625, 1013.875, 1014.25, 1014.5625, 1014.9375, 1015.25,
        1015.5625, 1015.9375, 1016.25, 1016.625,
    };

    /// <summary>A model of altimeter 3 that fits every measured click: 0.01 inch per click from STD,
    /// floored to the sim's 1/16 hPa.</summary>
    private static double Altimeter3(int clicksFromStd)
        => Math.Floor((1013.25 + clicksFromStd * 0.01 * A220Panels.HpaPerInHg) * 16) / 16;

    [Fact]
    public void Altimeter3Model_FitsEveryMeasuredClick()
    {
        for (int n = -1; n <= 10; n++)
            Assert.Equal(MeasuredAltimeter3[n + 1], Altimeter3(n), 3);
    }

    /// <summary>
    /// Live 2026-10-16 the knob var applies any whole-number delta in one write (3, -10 and 7 moved
    /// the ISI by exactly that many 0.01-inch clicks), so a burst is ONE write of the signed count,
    /// never more than <see cref="A220Panels.IsiMaxClicksPerWrite"/> clicks and never none.
    /// </summary>
    [Theory]
    [InlineData(5, 1, 5)]
    [InlineData(5, -1, -5)]
    [InlineData(1, -1, -1)]
    [InlineData(100, 1, 100)]
    [InlineData(220, 1, 100)]     // AimClicks' own cap is 220: one write still carries at most 100
    [InlineData(220, -1, -100)]
    [InlineData(0, 1, 1)]         // never a write of 0
    public void IsiKnobWriteDelta_IsOneSignedWrite_Capped(int clicks, int sign, int expected)
        => Assert.Equal(expected, A220Panels.IsiKnobWriteDelta(clicks, sign));

    /// <summary>The walk's loop (StartIsiBaroWalk) on the model, the I/O replaced by it: each move
    /// is one write of n clicks (applied exactly, as measured); returns the value it lands on, what
    /// it says, and the rounds it took.</summary>
    private static (double Landed, string Said, int Rounds) WalkOnAltimeter3(int startClicks, double entry)
    {
        double target = A220Panels.IsiTargetHpa(entry)!.Value;
        bool inHg = A220Panels.IsInHgEntry(entry);
        int pos = startClicks;
        double cur = Altimeter3(pos), prevDelta = double.NaN;
        double? step = null;
        int lastClicks = 0;
        for (int round = 0; round < 8; round++)
        {
            double delta = target - cur;
            var move = Decide(delta, prevDelta, step, lastClicks);
            if (move.Action == A220Panels.IsiWalkAction.Done) return (cur, A220Panels.FormatIsiBaro(cur, inHg), round);
            if (move.Action == A220Panels.IsiWalkAction.NearestHere) return (cur, A220Panels.IsiNearestText(cur, inHg), round);
            bool stepBack = move.Action == A220Panels.IsiWalkAction.StepBack;
            prevDelta = delta;
            int write = A220Panels.IsiKnobWriteDelta(stepBack ? 1 : move.Clicks, delta > 0 ? 1 : -1);   // +1 raises (measured)
            int clicks = Math.Abs(write);
            lastClicks = clicks;
            pos += write;
            double now = Altimeter3(pos), moved = now - cur;
            cur = now;
            if (stepBack)
                return (cur, A220Panels.IsiWithinTolerance(target - cur, step)
                    ? A220Panels.FormatIsiBaro(cur, inHg) : A220Panels.IsiNearestText(cur, inHg), round + 1);
            step = Math.Abs(moved) / clicks;
        }
        return (cur, A220Panels.IsiRoundLimitText(target, cur, step, inHg), 8);
    }

    /// <summary>From STD (or a click either side of it), every entry lands on the selectable value
    /// nearest the target and says it plainly — including the long walks a probe-click step
    /// measured short used to stop a hectopascal or more past.</summary>
    [Theory]
    [InlineData(0, 1030.0, "Standby altimeter 1030 hectopascals")]
    [InlineData(0, 1020.0, "Standby altimeter 1020 hectopascals")]
    [InlineData(0, 1016.0, "Standby altimeter 1016 hectopascals")]
    [InlineData(0, 1013.0, "Standby altimeter 1013 hectopascals")]
    [InlineData(0, 1090.0, "Standby altimeter 1090 hectopascals")]
    [InlineData(0, 950.0, "Standby altimeter 950 hectopascals")]
    [InlineData(1, 1005.0, "Standby altimeter 1005 hectopascals")]
    [InlineData(-1, 1040.0, "Standby altimeter 1040 hectopascals")]
    [InlineData(0, 29.92, "Standby altimeter 29.92 inches")]
    [InlineData(0, 30.00, "Standby altimeter 30.00 inches")]
    [InlineData(1, 30.12, "Standby altimeter 30.12 inches")]
    [InlineData(0, 29.50, "Standby altimeter 29.50 inches")]
    [InlineData(-200, 1090.0, "Standby altimeter 1090 hectopascals")]   // 945.5 to the top of the range: capped writes
    [InlineData(200, 940.0, "Standby altimeter 940 hectopascals")]      // 1081 to the bottom
    public void IsiWalk_OnAltimeter3sMeasuredGrid_LandsOnTheNearestValue(int startClicks, double entry, string said)
    {
        var (landed, spoken, rounds) = WalkOnAltimeter3(startClicks, entry);
        Assert.True(rounds < 8, $"took {rounds} rounds: the 8-round cap must leave room");
        double target = A220Panels.IsiTargetHpa(entry)!.Value;
        double nearest = Enumerable.Range(-400, 800).Select(Altimeter3).MinBy(v => Math.Abs(v - target));
        Assert.Equal(nearest, landed);
        Assert.Equal(said, spoken);
    }

    [Theory]
    [InlineData(0.5, 3.0)]     // exactly half a step: on target
    [InlineData(-0.5, -3.0)]
    [InlineData(0.0, 1.0)]
    public void IsiWalkDecision_MeasuredStep_WithinHalfAStep_IsDone(double delta, double prevDelta)
        => Assert.Equal(Done, Decide(delta, prevDelta, 1.0));

    [Theory]
    [InlineData(25.0, 30.0, 1.0, 25)]       // the same sign as before: an ordinary aimed burst
    [InlineData(-25.0, -30.0, 1.0, 25)]
    [InlineData(7.0, 7.0, 0.34, 21)]        // round(7 / 0.34)
    [InlineData(0.6, 3.0, 1.0, 1)]          // just over half a step: one click, not zero
    [InlineData(10.4, double.NaN, 1.0, 10)] // no previous delta: nothing to straddle
    public void IsiWalkDecision_MeasuredStep_AimsRoundDeltaOverStep(double delta, double prevDelta, double step, int clicks)
        => Assert.Equal(Click(clicks), Decide(delta, prevDelta, step));

    [Fact]
    public void IsiWalkDecision_AStepThatCannotAim_NeverFiresAZeroClickBurst()
    {
        // A measured step is always above zero; if one ever is not, the walk stops where it is.
        Assert.Equal(NearestHere, Decide(0.4, double.NaN, 0.0));
    }

    [Theory]
    [InlineData(0.2, null, true)]
    [InlineData(1013.2 - 1013.0, null, true)]
    [InlineData(0.25, null, false)]
    [InlineData(0.5, 1.0, true)]
    [InlineData(-0.5, 1.0, true)]
    [InlineData(0.51, 1.0, false)]
    public void IsiWithinTolerance_IsHalfAStepOrTheBlindTolerance(double delta, double? step, bool expected)
        => Assert.Equal(expected, A220Panels.IsiWithinTolerance(delta, step));
}
