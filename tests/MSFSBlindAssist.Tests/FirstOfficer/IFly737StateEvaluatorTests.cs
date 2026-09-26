// Characterization tests for IFly737StateEvaluator — the read side of the iFly 737 MAX8 FO
// profile: routes plain SDK field reads through IFly737MAXDefinition.ReadRawField, and composes
// the FO-only synthetics (engine N2 cache, pressurization plan match, merged Before-Start fuel
// pumps, center tank quantity). See .superpowers/sdd/task-3-brief.md. The evaluator is exercised
// sim-less via its internal SnapshotSource/ReadySource seam (InternalsVisibleTo covers this
// project) — no live SDK client or plugin is involved.

namespace MSFSBlindAssist.Tests.FirstOfficer;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.SimConnect.IFly;

public class IFly737StateEvaluatorTests
{
    private static byte[] Buf() => new byte[IFlySdkOffsets.StructSize];

    private static IFly737StateEvaluator NotReady(byte[]? data = null)
    {
        var eval = new IFly737StateEvaluator();
        var snap = new IFlySdkSnapshot(data ?? Buf());
        eval.SnapshotSource = () => snap;
        eval.ReadySource = () => false;
        return eval;
    }

    private static IFly737StateEvaluator Ready(byte[] data)
    {
        var eval = new IFly737StateEvaluator();
        var snap = new IFlySdkSnapshot(data);
        eval.SnapshotSource = () => snap;
        eval.ReadySource = () => true;
        return eval;
    }

    // ------------------------------------------------------------------
    // NaN gate: not ready -> every plain field AND every SDK-derived synthetic is NaN.
    // FO_ENG1_N2/FO_ENG2_N2 are asserted NaN here too, but that passes VACUOUSLY —
    // SetEngineN2 is never called in this test, so the cache is simply at its
    // pre-push default. N2 is deliberately NOT gated on ReadySource(): it comes from
    // stock SimConnect TURB ENG N2 (pushed independently by the FO background timer
    // via SetEngineN2), not from the iFly SDK snapshot, so it must stay valid through
    // an SDK hiccup — see EngineN2_Synthetics for the real not-ready-independent
    // coverage. Do not "fix" the evaluator to gate N2 on ReadySource(); that would
    // break engine-start detection during exactly the kind of brief SDK dropout this
    // independence exists to survive.
    // ------------------------------------------------------------------
    [Fact]
    public void GetValue_NotReady_IsNaN()
    {
        var b = Buf();
        b[IFlySdkOffsets.Fuel_L_FWD_Switch_Status] = 1; // a real switch would read ON if ready
        var eval = NotReady(b);
        eval.SetPlannedPressurizationAltitudes(37000, 450); // even with a plan loaded

        Assert.True(double.IsNaN(eval.GetValue("Fuel_L_FWD_Switch_Status")));
        Assert.True(double.IsNaN(eval.GetValue("FO_ENG1_N2")));
        Assert.True(double.IsNaN(eval.GetValue("FO_ENG2_N2")));
        Assert.True(double.IsNaN(eval.GetValue("FO_PRESS_ALTS_MATCH")));
        Assert.True(double.IsNaN(eval.GetValue("FO_PRESS_LAND_ALT_MATCH")));
        Assert.True(double.IsNaN(eval.GetValue("FO_FUEL_PUMPS_BS_OK")));
        Assert.True(double.IsNaN(eval.GetValue("FO_CENTER_QTY_LBS")));
    }

    // ------------------------------------------------------------------
    // Plain field routes through ReadRawField
    // ------------------------------------------------------------------
    [Fact]
    public void GetValue_ReadsSdkField()
    {
        var b = Buf();
        b[IFlySdkOffsets.Fuel_L_FWD_Switch_Status] = 1;
        var eval = Ready(b);

        Assert.Equal(1.0, eval.GetValue("Fuel_L_FWD_Switch_Status"));
        Assert.True(eval.IsOn("Fuel_L_FWD_Switch_Status"));

        b[IFlySdkOffsets.Fuel_L_FWD_Switch_Status] = 0;
        var evalOff = Ready(b);
        Assert.Equal(0.0, evalOff.GetValue("Fuel_L_FWD_Switch_Status"));
        Assert.False(evalOff.IsOn("Fuel_L_FWD_Switch_Status"));
    }

    // ------------------------------------------------------------------
    // FO_ENG1_N2 / FO_ENG2_N2 come from SetEngineN2, NaN before first push
    // ------------------------------------------------------------------
    [Fact]
    public void EngineN2_Synthetics()
    {
        var eval = Ready(Buf());

        Assert.True(double.IsNaN(eval.GetValue("FO_ENG1_N2")));
        Assert.True(double.IsNaN(eval.GetValue("FO_ENG2_N2")));

        eval.SetEngineN2(55.5, 12.0);

        Assert.Equal(55.5, eval.GetValue("FO_ENG1_N2"));
        Assert.Equal(12.0, eval.GetValue("FO_ENG2_N2"));
        Assert.True(eval.GetValue("FO_ENG1_N2") >= IFly737StateEvaluator.EngineRunningN2);
        Assert.True(eval.GetValue("FO_ENG2_N2") < IFly737StateEvaluator.EngStartFuelN2);
    }

    // ------------------------------------------------------------------
    // Rounding: SetPlannedPressurizationAltitudes(37123, 433) -> FLT 37000, LAND 450
    // ------------------------------------------------------------------
    [Fact]
    public void PressurizationPlan_RoundsToKnobSteps()
    {
        var eval = Ready(Buf());
        eval.SetPlannedPressurizationAltitudes(37123, 433);

        Assert.Equal(37000, eval.PlannedFltAltFt);
        Assert.Equal(450, eval.PlannedLandAltFt);
        Assert.True(eval.HasPressurizationPlan);
    }

    // ------------------------------------------------------------------
    // FO_PRESS_ALTS_MATCH: NaN with no plan; 1 when both windows within a step; 0 otherwise
    // ------------------------------------------------------------------
    private static void SetFltWindow(byte[] b, int feet)
    {
        b[IFlySdkOffsets.Flight_Altitude_Indicator_10000_Status] = (byte)(feet / 10000 % 10);
        b[IFlySdkOffsets.Flight_Altitude_Indicator_1000_Status] = (byte)(feet / 1000 % 10);
        b[IFlySdkOffsets.Flight_Altitude_Indicator_100_Status] = (byte)(feet / 100 % 10);
        b[IFlySdkOffsets.Flight_Altitude_Indicator_10_Status] = (byte)(feet / 10 % 10);
        b[IFlySdkOffsets.Flight_Altitude_Indicator_1_Status] = (byte)(feet % 10);
    }

    private static void SetLandWindow(byte[] b, int feet)
    {
        b[IFlySdkOffsets.Landing_Altitude_Indicator_10000_Status] = (byte)(feet / 10000 % 10);
        b[IFlySdkOffsets.Landing_Altitude_Indicator_1000_Status] = (byte)(feet / 1000 % 10);
        b[IFlySdkOffsets.Landing_Altitude_Indicator_100_Status] = (byte)(feet / 100 % 10);
        b[IFlySdkOffsets.Landing_Altitude_Indicator_10_Status] = (byte)(feet / 10 % 10);
        b[IFlySdkOffsets.Landing_Altitude_Indicator_1_Status] = (byte)(feet % 10);
    }

    [Fact]
    public void PressMatch_SyntheticTruthTable()
    {
        // No plan loaded -> NaN, even though the windows show real values.
        var noPlanBuf = Buf();
        SetFltWindow(noPlanBuf, 37000);
        SetLandWindow(noPlanBuf, 450);
        var noPlan = Ready(noPlanBuf);
        Assert.True(double.IsNaN(noPlan.GetValue("FO_PRESS_ALTS_MATCH")));

        // Plan loaded, both windows within a step -> 1.
        var matchBuf = Buf();
        SetFltWindow(matchBuf, 37000);
        SetLandWindow(matchBuf, 450);
        var matching = Ready(matchBuf);
        matching.SetPlannedPressurizationAltitudes(37123, 433); // rounds to 37000 / 450
        Assert.Equal(1.0, matching.GetValue("FO_PRESS_ALTS_MATCH"));
        Assert.True(matching.FltAltMatches());
        Assert.True(matching.LandAltMatches());

        // Plan loaded, FLT window off by more than a step -> 0.
        var mismatchBuf = Buf();
        SetFltWindow(mismatchBuf, 38000);
        SetLandWindow(mismatchBuf, 450);
        var mismatching = Ready(mismatchBuf);
        mismatching.SetPlannedPressurizationAltitudes(37123, 433);
        Assert.Equal(0.0, mismatching.GetValue("FO_PRESS_ALTS_MATCH"));
        Assert.False(mismatching.FltAltMatches());

        // Plan loaded, window blanked -> NaN, not 0 (indeterminate, never coerced to "no match").
        var blankBuf = Buf();
        SetBlankFltWindow(blankBuf);
        SetLandWindow(blankBuf, 450);
        var blanked = Ready(blankBuf);
        blanked.SetPlannedPressurizationAltitudes(37123, 433);
        Assert.True(double.IsNaN(blanked.GetValue("FO_PRESS_ALTS_MATCH")));

        static void SetBlankFltWindow(byte[] buf)
        {
            buf[IFlySdkOffsets.Flight_Altitude_Indicator_10000_Status] = 11; // blank
            buf[IFlySdkOffsets.Flight_Altitude_Indicator_1000_Status] = 11;
            buf[IFlySdkOffsets.Flight_Altitude_Indicator_100_Status] = 11;
            buf[IFlySdkOffsets.Flight_Altitude_Indicator_10_Status] = 11;
            buf[IFlySdkOffsets.Flight_Altitude_Indicator_1_Status] = 11;
        }
    }

    // ------------------------------------------------------------------
    // Tolerance boundary: the match test requires the window to be STRICTLY LESS
    // than one full knob step away from the plan (FLT 500 ft, LAND 50 ft) — a gap
    // of exactly one step (or more) must NOT match, even though the old test's
    // mismatch case (a 1000 ft/two-step gap) would also pass under a looser `<=`,
    // `< 1000`, or a doubled-step comparison. These cases are chosen specifically
    // to fail under any of those looser comparisons and only pass under the exact
    // "< one step" rule.
    // ------------------------------------------------------------------
    [Fact]
    public void FltAltMatches_ExactlyOneStepAway_DoesNotMatch()
    {
        var buf = Buf();
        SetFltWindow(buf, 37500); // plan 37000 + exactly one 500 ft step
        var eval = Ready(buf);
        eval.SetPlannedPressurizationAltitudes(37000, null);

        Assert.False(eval.FltAltMatches());
        Assert.Equal(0.0, eval.GetValue("FO_PRESS_ALTS_MATCH"));
    }

    [Fact]
    public void FltAltMatches_LessThanOneStepAway_Matches()
    {
        var buf = Buf();
        SetFltWindow(buf, 37100); // plan 37000 + 100 ft, well under one 500 ft step
        var eval = Ready(buf);
        eval.SetPlannedPressurizationAltitudes(37000, null);

        Assert.True(eval.FltAltMatches());
        Assert.Equal(1.0, eval.GetValue("FO_PRESS_ALTS_MATCH"));
    }

    [Fact]
    public void LandAltMatches_ExactlyOneStepAway_DoesNotMatch()
    {
        var buf = Buf();
        SetLandWindow(buf, 500); // plan 450 + exactly one 50 ft step
        var eval = Ready(buf);
        eval.SetPlannedPressurizationAltitudes(null, 450);

        Assert.False(eval.LandAltMatches());
        Assert.Equal(0.0, eval.GetValue("FO_PRESS_LAND_ALT_MATCH"));
    }

    [Fact]
    public void LandAltMatches_LessThanOneStepAway_Matches()
    {
        var buf = Buf();
        SetLandWindow(buf, 460); // plan 450 + 10 ft, well under one 50 ft step
        var eval = Ready(buf);
        eval.SetPlannedPressurizationAltitudes(null, 450);

        Assert.True(eval.LandAltMatches());
        Assert.Equal(1.0, eval.GetValue("FO_PRESS_LAND_ALT_MATCH"));
    }

    // ------------------------------------------------------------------
    // FO_FUEL_PUMPS_BS_OK: wing on + center matches fuel state (reuses FuelSystemLogic)
    // ------------------------------------------------------------------
    private static void SetCenterQty(byte[] b, string digits, byte units = 1)
    {
        int t2 = IFlySdkOffsets.Fuel_Quantity_Indicator_Status + 2 * IFlySdkOffsets.Fuel_Quantity_Indicator_Status_Stride0;
        for (int i = 0; i < 5; i++)
            b[t2 + i] = i < digits.Length ? (byte)(digits[i] - '0') : (byte)10; // 10 = blank
        b[IFlySdkOffsets.UNITstyle] = units;
    }

    [Fact]
    public void BeforeStartFuelPumps_Synthetic()
    {
        // Wing pumps on, both center pumps on, center tank has fuel above the arm threshold -> OK (1).
        var okBuf = Buf();
        okBuf[IFlySdkOffsets.Fuel_L_FWD_Switch_Status] = 1;
        okBuf[IFlySdkOffsets.Fuel_L_AFT_Switch_Status] = 1;
        okBuf[IFlySdkOffsets.Fuel_R_FWD_Switch_Status] = 1;
        okBuf[IFlySdkOffsets.Fuel_R_AFT_Switch_Status] = 1;
        okBuf[IFlySdkOffsets.Fuel_CENTER_L_Switch_Status] = 1;
        okBuf[IFlySdkOffsets.Fuel_CENTER_R_Switch_Status] = 1;
        SetCenterQty(okBuf, "2300"); // 2300 lb US units, well above the 500 lb arm threshold
        var ok = Ready(okBuf);
        Assert.Equal(1.0, ok.GetValue("FO_FUEL_PUMPS_BS_OK"));

        // Wing pumps on, center pumps left ON with the tank empty -> mismatch (0).
        var badBuf = Buf();
        badBuf[IFlySdkOffsets.Fuel_L_FWD_Switch_Status] = 1;
        badBuf[IFlySdkOffsets.Fuel_L_AFT_Switch_Status] = 1;
        badBuf[IFlySdkOffsets.Fuel_R_FWD_Switch_Status] = 1;
        badBuf[IFlySdkOffsets.Fuel_R_AFT_Switch_Status] = 1;
        badBuf[IFlySdkOffsets.Fuel_CENTER_L_Switch_Status] = 1;
        badBuf[IFlySdkOffsets.Fuel_CENTER_R_Switch_Status] = 1;
        SetCenterQty(badBuf, "00000");
        var bad = Ready(badBuf);
        Assert.Equal(0.0, bad.GetValue("FO_FUEL_PUMPS_BS_OK"));

        // Center quantity unreadable (blank gauge) -> NaN, never coerced to "no fuel".
        var blankBuf = Buf();
        blankBuf[IFlySdkOffsets.Fuel_L_FWD_Switch_Status] = 1;
        blankBuf[IFlySdkOffsets.Fuel_L_AFT_Switch_Status] = 1;
        blankBuf[IFlySdkOffsets.Fuel_R_FWD_Switch_Status] = 1;
        blankBuf[IFlySdkOffsets.Fuel_R_AFT_Switch_Status] = 1;
        SetCenterQty(blankBuf, ""); // all cells blank
        var blank = Ready(blankBuf);
        Assert.True(double.IsNaN(blank.GetValue("FO_FUEL_PUMPS_BS_OK")));
    }

    // ------------------------------------------------------------------
    // A disagreeing center-pump pair (one ON, the other OFF) must NEVER auto-tick
    // "Fuel pumps" — in EITHER fuel state. A bare `l && r` for centerOn collapses a
    // disagreeing pair to "center off" (false), which against a DRY tank satisfies
    // FuelSystemLogic.BeforeStartFuelPumpsOk's `centerOn == hasFuel` (false == false)
    // and false-ticks the checklist while one center pump is running dry-run against
    // the tank. Both fuel states must return 0, never 1.
    // ------------------------------------------------------------------
    [Fact]
    public void BeforeStartFuelPumps_DisagreeingCenterPumps_DryTank_IsZero()
    {
        var buf = Buf();
        buf[IFlySdkOffsets.Fuel_L_FWD_Switch_Status] = 1;
        buf[IFlySdkOffsets.Fuel_L_AFT_Switch_Status] = 1;
        buf[IFlySdkOffsets.Fuel_R_FWD_Switch_Status] = 1;
        buf[IFlySdkOffsets.Fuel_R_AFT_Switch_Status] = 1;
        buf[IFlySdkOffsets.Fuel_CENTER_L_Switch_Status] = 1; // ON
        buf[IFlySdkOffsets.Fuel_CENTER_R_Switch_Status] = 0; // OFF — disagreeing pair
        SetCenterQty(buf, "00000"); // dry tank
        var eval = Ready(buf);

        Assert.Equal(0.0, eval.GetValue("FO_FUEL_PUMPS_BS_OK"));
    }

    [Fact]
    public void BeforeStartFuelPumps_DisagreeingCenterPumps_LoadedTank_IsZero()
    {
        var buf = Buf();
        buf[IFlySdkOffsets.Fuel_L_FWD_Switch_Status] = 1;
        buf[IFlySdkOffsets.Fuel_L_AFT_Switch_Status] = 1;
        buf[IFlySdkOffsets.Fuel_R_FWD_Switch_Status] = 1;
        buf[IFlySdkOffsets.Fuel_R_AFT_Switch_Status] = 1;
        buf[IFlySdkOffsets.Fuel_CENTER_L_Switch_Status] = 0; // OFF — disagreeing pair
        buf[IFlySdkOffsets.Fuel_CENTER_R_Switch_Status] = 1; // ON
        SetCenterQty(buf, "2300"); // loaded tank, above the arm threshold
        var eval = Ready(buf);

        Assert.Equal(0.0, eval.GetValue("FO_FUEL_PUMPS_BS_OK"));
    }

    // ------------------------------------------------------------------
    // FO_CENTER_QTY_LBS delegates to composition (metric converts)
    // ------------------------------------------------------------------
    [Fact]
    public void CenterQty_Synthetic()
    {
        var usBuf = Buf();
        SetCenterQty(usBuf, "2300", units: 1); // US units: 2300 lb, already pounds
        var us = Ready(usBuf);
        Assert.Equal(2300.0, us.GetValue("FO_CENTER_QTY_LBS"));
        Assert.Equal(2300.0, us.CenterQtyLbs());

        var metricBuf = Buf();
        SetCenterQty(metricBuf, "1000", units: 0); // metric: 1000 kg -> converted to lb
        var metric = Ready(metricBuf);
        Assert.Equal(1000.0 * IFly737FoComposition.KgToLb, metric.GetValue("FO_CENTER_QTY_LBS"), 3);
    }

    // ------------------------------------------------------------------
    // SimConnect fallback for the center quantity. Live finding (2026-08-17 flight,
    // center_pumps.log + IFlySdkProbe `fuel` mode): the iFly MAX8 writes BLANK (code 10)
    // into every Fuel_Quantity_Indicator_Status cell for the whole powered flight, so the
    // gauge-text read is NaN forever, the FO automation can never arm the center pumps and
    // the executor's CenterPumpGate suppresses the Before-Start flow's center-pump ON
    // writes (debug.log 10:26:00 "centre-pump ON suppressed"). The stock sim fuel system
    // IS live for this aircraft (FUEL TANK CENTER QUANTITY x FUEL WEIGHT PER GALLON), so
    // the FO background timer pushes that product via SetSimCenterFuelLbs — same
    // SimConnect-sourced push pattern (and SDK-dropout independence) as SetEngineN2.
    // The gauge text still WINS when it is readable: it is the aircraft's own gauge.
    // ------------------------------------------------------------------
    [Fact]
    public void CenterQty_BlankGauge_FallsBackToPushedSimFuel()
    {
        var buf = Buf();
        SetCenterQty(buf, "", units: 1); // gauge blank — the live MAX8 behaviour
        var eval = Ready(buf);
        eval.SetSimCenterFuelLbs(2300.0);

        Assert.Equal(2300.0, eval.CenterQtyLbs());
        Assert.Equal(2300.0, eval.GetValue("FO_CENTER_QTY_LBS"));
    }

    [Fact]
    public void CenterQty_ReadableGauge_WinsOverPushedSimFuel()
    {
        var buf = Buf();
        SetCenterQty(buf, "2300", units: 1);
        var eval = Ready(buf);
        eval.SetSimCenterFuelLbs(9999.0);

        Assert.Equal(2300.0, eval.CenterQtyLbs());
    }

    [Fact]
    public void CenterQty_BlankGauge_NoPush_StaysNaN()
    {
        var buf = Buf();
        SetCenterQty(buf, "", units: 1);
        var eval = Ready(buf);

        Assert.True(double.IsNaN(eval.CenterQtyLbs()));
    }

    // Blank-gauge fallback feeds the merged Before-Start fuel-pumps synthetic too — with a
    // pushed quantity the synthetic must resolve instead of staying indeterminate (this is
    // the exact "wing pumps on, center loaded, center pumps still off" tick that must
    // auto-tick only once the center pumps come on).
    [Fact]
    public void BeforeStartFuelPumps_BlankGauge_UsesPushedSimFuel()
    {
        var buf = Buf();
        buf[IFlySdkOffsets.Fuel_L_FWD_Switch_Status] = 1;
        buf[IFlySdkOffsets.Fuel_L_AFT_Switch_Status] = 1;
        buf[IFlySdkOffsets.Fuel_R_FWD_Switch_Status] = 1;
        buf[IFlySdkOffsets.Fuel_R_AFT_Switch_Status] = 1;
        SetCenterQty(buf, ""); // gauge blank
        var eval = Ready(buf);
        eval.SetSimCenterFuelLbs(2300.0); // loaded center tank, pumps still off

        Assert.Equal(0.0, eval.GetValue("FO_FUEL_PUMPS_BS_OK")); // resolves (not NaN): pumps should be on

        buf[IFlySdkOffsets.Fuel_CENTER_L_Switch_Status] = 1;
        buf[IFlySdkOffsets.Fuel_CENTER_R_Switch_Status] = 1;
        var evalOn = Ready(buf);
        evalOn.SetSimCenterFuelLbs(2300.0);
        Assert.Equal(1.0, evalOn.GetValue("FO_FUEL_PUMPS_BS_OK"));
    }
}
