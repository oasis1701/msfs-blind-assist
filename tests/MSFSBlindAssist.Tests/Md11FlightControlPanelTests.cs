using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The Flight Control Panel window's node ids and event names, checked against the embedded map.
///
/// Same rationale as the MCDU key tests: a wrong node id or event name does not throw, it just
/// makes a control silently do nothing — and this is the autoflight panel, so "silently did
/// nothing" is a pilot believing they engaged a mode they did not.
/// </summary>
public class Md11FlightControlPanelTests
{
    private static readonly Md11ControlMap Map = Md11ControlMap.Load();

    private static Md11Control? Find(string nodeId) => Map.Controls.FirstOrDefault(
        c => string.Equals(c.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every button the FCP window offers, exactly as the window names it.</summary>
    [Theory]
    [InlineData("MD11_CGS_AUTOFLIGHT_BT")]
    [InlineData("MD11_CGS_PROF_BT")]
    [InlineData("MD11_CGS_NAV_BT")]
    [InlineData("MD11_CGS_APPRLAND_BT")]
    [InlineData("MD11_CGS_FMSSPD_BT")]
    [InlineData("MD11_CGS_IASMACH_BT")]
    [InlineData("MD11_CGS_HDGTRK_BT")]
    [InlineData("MD11_CGS_FTM_BT")]
    [InlineData("MD11_CGS_VS_FPA_BT")]
    [InlineData("MD11_THR_GA_BT")]
    [InlineData("MD11_THR_L_ATS_BT")]
    [InlineData("MD11_THR_R_ATS_BT")]
    public void FcpButtons_ExistAndArePressable(string nodeId)
    {
        var c = Find(nodeId);

        Assert.NotNull(c);
        Assert.Contains("LEFT_BUTTON_DOWN", c!.Events.Keys);
        Assert.Contains("LEFT_BUTTON_UP", c.Events.Keys);
    }

    /// <summary>
    /// The three push-pull knobs must carry BOTH event pairs. A knob that lost its PULL pair would
    /// leave the window with a Pull button that quietly does nothing.
    /// </summary>
    [Theory]
    [InlineData("MD11_CGS_SPD_KB")]
    [InlineData("MD11_CGS_HDG_KB")]
    [InlineData("MD11_CGS_ALT_KB")]
    public void PushPullKnobs_CarryBothPushAndPullEventPairs(string nodeId)
    {
        var c = Find(nodeId);

        Assert.NotNull(c);
        Assert.Equal(Md11Kinds.KnobPushPull, c!.Kind);
        foreach (var e in new[] { "PUSH_DOWN", "PUSH_UP", "PULL_DOWN", "PULL_UP" })
            Assert.True(c.Event(e) is > 0, $"{nodeId} missing {e}");
    }

    /// <summary>
    /// The V/S knob is deliberately NOT push-pull — the real one does not push or pull, and the
    /// window must not offer buttons for an action the aircraft has no events for.
    /// </summary>
    [Fact]
    public void VerticalSpeedKnob_IsNotPushPull()
    {
        var c = Find("MD11_CGS_VS_KB");

        Assert.NotNull(c);
        Assert.Null(c!.Event("PUSH_DOWN"));
        Assert.Null(c.Event("PULL_DOWN"));
    }

    /// <summary>
    /// The V/S knob DOES turn, and turning it is the only way to engage V/S / FPA on the MD-11 (no
    /// engage button exists). The FCP window and the V/S dialog fire these two events directly, so
    /// a rename that dropped them would leave the pilot with no way to activate the mode. The
    /// constant on <see cref="Md11Fcp"/> must point at this same node.
    /// </summary>
    [Fact]
    public void VerticalSpeedKnob_CarriesBothWheelEvents()
    {
        var c = Find(Md11Fcp.VerticalSpeedKnob);

        Assert.NotNull(c);
        Assert.Equal("MD11_CGS_VS_KB", Md11Fcp.VerticalSpeedKnob);
        Assert.True(c!.Event("WHEEL_UP") is > 0, "V/S knob missing WHEEL_UP");
        Assert.True(c.Event("WHEEL_DOWN") is > 0, "V/S knob missing WHEEL_DOWN");
    }

    /// <summary>
    /// The FCP windows' MODE vars. Each selected value is meaningless without its mode — "250"
    /// is a speed or a Mach number depending on IAS_MACH — so the window speaks both, and these
    /// are the aircraft's own vars rather than anything inferred.
    /// </summary>
    [Theory]
    [InlineData("MD11_AP_IAS_MACH")]
    [InlineData("MD11_AP_HDG_TRK")]
    [InlineData("MD11_AP_VS_FPA")]
    [InlineData("MD11_AP_FT_M")]
    [InlineData("MD11_AFS_SPD")]
    [InlineData("MD11_AFS_HDG")]
    [InlineData("MD11_AFS_ALT")]
    [InlineData("MD11_AFS_VS")]
    [InlineData("MD11_AP_STATE")]
    public void FcpValueAndModeVars_AreExported(string varName)
    {
        Assert.Contains(varName, Map.ExportVars);
    }

    /// <summary>
    /// Go Around is ONE function reachable from two clickspots: the throttle-lever button and a
    /// glareshield clickspot share event ids 77851/77852. Pinning this stops the window growing a
    /// second "Go Around" button that fires the identical event.
    /// </summary>
    [Fact]
    public void GoAround_ThrottleAndGlareshieldClickspots_ShareOneEventPair()
    {
        var thr = Find("MD11_THR_GA_BT");
        var alt = Find("GA_BT_ALT");

        Assert.NotNull(thr);
        Assert.NotNull(alt);
        Assert.Equal(thr!.Event("LEFT_BUTTON_DOWN"), alt!.Event("LEFT_BUTTON_DOWN"));
        Assert.Equal(thr.Event("LEFT_BUTTON_UP"), alt.Event("LEFT_BUTTON_UP"));
    }

    /// <summary>The two autothrust disconnects are genuinely separate buttons, unlike Go Around.</summary>
    [Fact]
    public void AutothrustDisconnects_AreTwoDistinctButtons()
    {
        var l = Find("MD11_THR_L_ATS_BT");
        var r = Find("MD11_THR_R_ATS_BT");

        Assert.NotNull(l);
        Assert.NotNull(r);
        Assert.NotEqual(l!.Event("LEFT_BUTTON_DOWN"), r!.Event("LEFT_BUTTON_DOWN"));
    }

    /// <summary>
    /// FTM is the aircraft's name for the ALTITUDE UNIT select (feet/metres) — TFDi's own tooltip
    /// says "Altitude Unit Select". Read as an abbreviation it looks like a flight-test mode, and
    /// mislabelling it would put a nonsense button on the autoflight panel.
    /// </summary>
    [Fact]
    public void FtmButton_IsTheAltitudeUnitSelect()
    {
        var c = Find("MD11_CGS_FTM_BT");

        Assert.NotNull(c);
        Assert.Equal("tooltip", c!.LabelSource);
        Assert.Contains("Altitude Unit", c.Label ?? "");
    }

    /// <summary>The bank limiter's six positions, from the aircraft's own value map.</summary>
    [Fact]
    public void BankAngleLimiter_HasAutoPlusFiveFixedLimits()
    {
        var c = Find("MD11_CGS_HDG_BASE_KB");

        Assert.NotNull(c);
        Assert.Equal("Auto", c!.ValueMap["0"]);
        Assert.Equal("25 degrees", c.ValueMap["5"]);
        Assert.Equal(6, c.ValueMap.Count);
    }
}
