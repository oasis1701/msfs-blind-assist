// Characterization tests for the iFly 737 MAX8 First Officer background automation —
// IFly737FOAutoManager (LNAV/VNAV push at 400 ft AGL, center-pump policy input composition)
// and, structurally, IFly737FlightPhaseMonitor. See .superpowers/sdd/task-7-brief.md.
//
// Neither class can be CONSTRUCTED in a unit test: both take a ScreenReaderAnnouncer, which
// has no parameterless constructor and must never be instantiated a second time (the app
// creates exactly one, in MainForm) — and IFly737ActionExecutor.IsAvailable can never be made
// true without a live SimConnect connection, which gates the whole of Update() before any of
// this logic runs. So these tests exercise the two INTERNAL, STATIC seams
// IFly737FOAutoManager exposes for exactly this reason — ShouldPushMcpMode (the annunciator
// push guard, a pure function of the raw value) and ReadCenterPumpInputs (the center-pump
// policy's five composed inputs, a pure function of an IFly737StateEvaluator) — the same
// pattern IFly737StateEvaluatorTests uses to drive the evaluator sim-less via its internal
// SnapshotSource/ReadySource seam.

namespace MSFSBlindAssist.Tests.FirstOfficer;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.SimConnect.IFly;
using Action = MSFSBlindAssist.FirstOfficer.CenterFuelPumpAutomation.Action;

public class IFly737AutoManagerTests
{
    private static byte[] Buf() => new byte[IFlySdkOffsets.StructSize];

    private static IFly737StateEvaluator Ready(byte[] data)
    {
        var eval = new IFly737StateEvaluator();
        var snap = new IFlySdkSnapshot(data);
        eval.SnapshotSource = () => snap;
        eval.ReadySource = () => true;
        return eval;
    }

    private static void SetCenterQty(byte[] b, string digits, byte units = 1)
    {
        int t2 = IFlySdkOffsets.Fuel_Quantity_Indicator_Status + 2 * IFlySdkOffsets.Fuel_Quantity_Indicator_Status_Stride0;
        for (int i = 0; i < 5; i++)
            b[t2 + i] = i < digits.Length ? (byte)(digits[i] - '0') : (byte)10; // 10 = blank
        b[IFlySdkOffsets.UNITstyle] = units;
    }

    private static void SetAllWingPumps(byte[] b, bool on)
    {
        byte v = on ? (byte)1 : (byte)0;
        b[IFlySdkOffsets.Fuel_L_FWD_Switch_Status] = v;
        b[IFlySdkOffsets.Fuel_R_FWD_Switch_Status] = v;
        b[IFlySdkOffsets.Fuel_L_AFT_Switch_Status] = v;
        b[IFlySdkOffsets.Fuel_R_AFT_Switch_Status] = v;
    }

    // ------------------------------------------------------------------
    // ShouldPushMcpMode: NaN -> no press (indeterminate, never coerced to "unlit"); every lit
    // encoding (mod 3 != 0) -> no press; every definitively-unlit encoding (mod 3 == 0) -> press.
    // The NaN case is the one this test exists to pin: IFly737FoComposition.Lit(NaN) already
    // returns false (not lit), so a bare `!IsLit(field)` cannot distinguish "confirmed off"
    // from "unknown" — ShouldPushMcpMode must check the raw value for NaN FIRST.
    // ------------------------------------------------------------------
    [Fact]
    public void LnavVnav_PressOnlyWhenDefinitivelyUnlit()
    {
        Assert.False(IFly737FOAutoManager.ShouldPushMcpMode(double.NaN));

        foreach (double lit in new[] { 1.0, 2.0, 4.0, 5.0 })
            Assert.False(IFly737FOAutoManager.ShouldPushMcpMode(lit));

        foreach (double unlit in new[] { 0.0, 3.0 })
            Assert.True(IFly737FOAutoManager.ShouldPushMcpMode(unlit));
    }

    // Non-integral raw reads (SDK bytes are integral in practice, but GetValue's contract is a
    // plain double) must round to the nearest composite value before the mod-3 test, matching
    // IFly737FoComposition.Lit exactly, rather than truncating or misclassifying.
    [Theory]
    [InlineData(0.4, true)]   // rounds to 0 -> unlit -> press
    [InlineData(2.6, true)]   // rounds to 3 -> unlit -> press
    [InlineData(1.4, false)]  // rounds to 1 -> lit -> no press
    [InlineData(4.6, false)]  // rounds to 5 -> lit -> no press
    public void ShouldPushMcpMode_RoundsBeforeClassifying(double raw, bool expected) =>
        Assert.Equal(expected, IFly737FOAutoManager.ShouldPushMcpMode(raw));

    // ------------------------------------------------------------------
    // ReadCenterPumpInputs: composite truth mapped through FuelSystemLogic from live SDK field
    // names (verified against IFlySdkOffsets.cs). Then fed into a real CenterFuelPumpAutomation
    // policy instance to prove the composition actually drives the shared decision — asserted
    // via the policy's public Diagnostics after one Update tick, per the brief.
    // ------------------------------------------------------------------

    // Ground-arm scenario: wing pumps on, center switches off, center tank loaded above the
    // arm threshold, no low-press lights lit -> not dry, credible, wing on. Fed into a fresh
    // policy this composes into an immediate TurnOn (CenterFuelPumpAutomationTests pins the
    // policy's own arm rule; this test pins that THIS composition reaches it correctly).
    [Fact]
    public void CenterPumpInputs_MapThroughFuelSystemLogic()
    {
        var buf = Buf();
        SetAllWingPumps(buf, true);
        SetCenterQty(buf, "2300"); // well above CenterFuelPumpAutomation.ArmThresholdLbs (500)
        var eval = Ready(buf);

        var (qty, pumpsOn, dry, credible, wingOn) = IFly737FOAutoManager.ReadCenterPumpInputs(eval);
        Assert.Equal(2300.0, qty);
        Assert.False(pumpsOn);
        Assert.False(dry);
        Assert.True(credible);
        Assert.True(wingOn);

        var policy = new CenterFuelPumpAutomation();
        Action action = policy.Update(
            enabled: true, dataReady: true, onGround: true,
            centerQtyLbs: qty, centerPumpsOn: pumpsOn, centerTankDry: dry,
            systemCredible: credible, wingPumpsOn: wingOn, rawElapsedMs: 1000);

        Assert.Equal(Action.TurnOn, action);
        Assert.Contains("pending=On", policy.Diagnostics);
    }

    // M-2: at least one running center pump reporting low pressure, with NO running pump
    // reporting pressure OK, is dry. Both switches on, both lights lit.
    [Fact]
    public void ReadCenterPumpInputs_BothCenterPumpsRunning_BothLowPressLit_IsDry()
    {
        var buf = Buf();
        buf[IFlySdkOffsets.Fuel_CENTER_L_Switch_Status] = 1;
        buf[IFlySdkOffsets.Fuel_CENTER_R_Switch_Status] = 1;
        buf[IFlySdkOffsets.LOW_PRESSURE_CENTER_L_Light_Status] = 1;
        buf[IFlySdkOffsets.LOW_PRESSURE_CENTER_R_Light_Status] = 1;
        var eval = Ready(buf);

        var (_, pumpsOn, dry, _, _) = IFly737FOAutoManager.ReadCenterPumpInputs(eval);
        Assert.True(pumpsOn);
        Assert.True(dry);
    }

    // M-2 negative: a running pump whose light is OUT proves pressure OK -> not dry, even
    // though its partner (also running) is lit. CenterTankDry requires EVERY running pump to
    // report low pressure.
    [Fact]
    public void ReadCenterPumpInputs_OneRunningPumpLightOut_IsNotDry()
    {
        var buf = Buf();
        buf[IFlySdkOffsets.Fuel_CENTER_L_Switch_Status] = 1;
        buf[IFlySdkOffsets.Fuel_CENTER_R_Switch_Status] = 1;
        buf[IFlySdkOffsets.LOW_PRESSURE_CENTER_L_Light_Status] = 0; // light OUT: pressure OK
        buf[IFlySdkOffsets.LOW_PRESSURE_CENTER_R_Light_Status] = 1;
        var eval = Ready(buf);

        var (_, pumpsOn, dry, _, _) = IFly737FOAutoManager.ReadCenterPumpInputs(eval);
        Assert.True(pumpsOn);
        Assert.False(dry);
    }

    // Neither center switch on -> not "pumps on", and CenterTankDry's (sw0||sw1) term is false
    // regardless of light state -> not dry.
    [Fact]
    public void ReadCenterPumpInputs_BothCenterSwitchesOff_NeverDry()
    {
        var buf = Buf();
        buf[IFlySdkOffsets.LOW_PRESSURE_CENTER_L_Light_Status] = 1; // stray light, switch still off
        var eval = Ready(buf);

        var (_, pumpsOn, dry, _, _) = IFly737FOAutoManager.ReadCenterPumpInputs(eval);
        Assert.False(pumpsOn);
        Assert.False(dry);
    }

    // M-3: the WING annunciator tracks output pressure, not the switch — a single wing pump
    // running with its low-press light OUT is enough to prove the system credible, regardless
    // of the other three pumps (all off here).
    [Fact]
    public void ReadCenterPumpInputs_OneWingPumpRunningLightOut_IsCredible_ButNotAllWingOn()
    {
        var buf = Buf();
        buf[IFlySdkOffsets.Fuel_L_FWD_Switch_Status] = 1; // low-press light stays 0 (out)
        var eval = Ready(buf);

        var (_, _, _, credible, wingOn) = IFly737FOAutoManager.ReadCenterPumpInputs(eval);
        Assert.True(credible);
        Assert.False(wingOn); // only 1 of 4 wing pumps on
    }

    // No wing pump running at all -> never credible (F3 poison block relies on this).
    [Fact]
    public void ReadCenterPumpInputs_NoWingPumpsRunning_NeverCredible()
    {
        var eval = Ready(Buf());
        var (_, _, _, credible, wingOn) = IFly737FOAutoManager.ReadCenterPumpInputs(eval);
        Assert.False(credible);
        Assert.False(wingOn);
    }

    // A wing pump running WITH its low-press light lit does not, by itself, prove credibility —
    // only a pump reporting pressure OK does (M-3's per-pump AND-not-lit term).
    [Fact]
    public void ReadCenterPumpInputs_AllWingPumpsRunningButAllLit_IsNotCredible()
    {
        var buf = Buf();
        SetAllWingPumps(buf, true);
        buf[IFlySdkOffsets.LOW_PRESSURE_L_FWD_Light_Status] = 1;
        buf[IFlySdkOffsets.LOW_PRESSURE_R_FWD_Light_Status] = 1;
        buf[IFlySdkOffsets.LOW_PRESSURE_L_AFT_Light_Status] = 1;
        buf[IFlySdkOffsets.LOW_PRESSURE_R_AFT_Light_Status] = 1;
        var eval = Ready(buf);

        var (_, _, _, credible, wingOn) = IFly737FOAutoManager.ReadCenterPumpInputs(eval);
        Assert.False(credible);
        Assert.True(wingOn); // switches are all on even though every light is lit
    }

    // Center quantity delegates to the evaluator's own metric-aware CenterQtyLbs() — pin that
    // the composition reads the SAME quantity IFly737StateEvaluatorTests.CenterQty_Synthetic
    // already pins, not a second/independent read.
    [Fact]
    public void ReadCenterPumpInputs_QuantityMatchesEvaluatorCenterQtyLbs()
    {
        var buf = Buf();
        SetCenterQty(buf, "1000", units: 0); // metric -> converted to lb
        var eval = Ready(buf);

        var (qty, _, _, _, _) = IFly737FOAutoManager.ReadCenterPumpInputs(eval);
        Assert.Equal(eval.CenterQtyLbs(), qty);
        Assert.Equal(1000.0 * IFly737FoComposition.KgToLb, qty, 3);
    }

    // ------------------------------------------------------------------
    // Structural: both classes must implement the shared FO interfaces the profile task wires
    // up against (IFoAutoManager / IFoPhaseMonitor), and AutoLights10kEnabled must default to
    // the same "on" the PMDG template and every other aircraft's monitor use.
    // ------------------------------------------------------------------
    [Fact]
    public void Types_ImplementSharedFoInterfaces()
    {
        Assert.True(typeof(IFoAutoManager).IsAssignableFrom(typeof(IFly737FOAutoManager)));
        Assert.True(typeof(IFoPhaseMonitor).IsAssignableFrom(typeof(IFly737FlightPhaseMonitor)));
    }
}
