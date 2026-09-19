using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.Forms.MD11;
using MSFSBlindAssist.SimConnect;

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
    /// An action that could not be DELIVERED is refused in ONE sentence, whichever surface the
    /// pilot reached it from — the Ctrl+P window, the Ctrl+H/S/A/V dialog toggles, the FCU
    /// push/pull hotkeys and the panel's altimeter STD rows all compose it through
    /// <see cref="Md11Fcp.Unavailable"/> over the knob names on <see cref="Md11Fcp"/>. The words
    /// are the pilot's only evidence that nothing happened, so two phrasings for one class of
    /// failure would leave them learning both; these pin the sentences.
    /// </summary>
    [Fact]
    public void AnUndeliverableAction_IsRefusedInOneSentence()
    {
        Assert.Equal("Heading push unavailable", Md11Fcp.Unavailable(Md11Fcp.PushAction(Md11Fcp.HeadingKnobName)));
        Assert.Equal("Heading pull unavailable", Md11Fcp.Unavailable(Md11Fcp.PullAction(Md11Fcp.HeadingKnobName)));
        Assert.Equal("Speed push unavailable", Md11Fcp.Unavailable(Md11Fcp.PushAction(Md11Fcp.SpeedKnobName)));
        Assert.Equal("Altitude pull unavailable", Md11Fcp.Unavailable(Md11Fcp.PullAction(Md11Fcp.AltitudeKnobName)));
        Assert.Equal("Vertical speed wheel up unavailable",
            Md11Fcp.Unavailable(Md11Fcp.WheelAction(Md11Fcp.VerticalSpeedName, up: true)));
        Assert.Equal("Vertical speed wheel down unavailable",
            Md11Fcp.Unavailable(Md11Fcp.WheelAction(Md11Fcp.VerticalSpeedName, up: false)));
        // The typed Ctrl+V value refuses on the knob's own name: the wheel nudge is what engages
        // the mode, so a value that cannot engage is not "set".
        Assert.Equal("Vertical speed unavailable", Md11Fcp.Unavailable(Md11Fcp.VerticalSpeedName));

        // The same family as the MCDU's refusal for a key it cannot deliver, so a pilot who has
        // heard one recognises the other.
        Assert.EndsWith("unavailable", Md11Fcp.Unavailable("Anything"));
        Assert.EndsWith("unavailable.", Md11McduKeys.UndeliverableRefusal("7"));
    }

    /// <summary>
    /// Every OTHER MD-11 write path that can now refuse names itself from its own owner rather
    /// than a literal at the call site, so the refusal and that path's own read-back sentences can
    /// never drift apart. These are the names the sweep of 2026-09-12 gave a voice.
    /// </summary>
    [Fact]
    public void EveryRefusableWritePath_NamesItselfFromItsOwnOwner()
    {
        Assert.Equal("Altimeters unavailable", Md11Fcp.Unavailable(Md11Fcp.AltimetersName));
        Assert.Equal("Ground spoilers unavailable", Md11Fcp.Unavailable(Md11SpeedbrakeSystem.ArmName));
        Assert.Equal("Squawk unavailable", Md11Fcp.Unavailable(Md11Squawk.Name));
        Assert.Equal("Dial-A-Flap unavailable", Md11Fcp.Unavailable(Md11FlapSystem.DialName));

        // Each name is the one that path's own sentences already used, so the pilot hears one name
        // for one control whether it refused, failed to move, or reported back.
        Assert.StartsWith(Md11SpeedbrakeSystem.ArmName, Md11SpeedbrakeSystem.ArmReadBack(1, 0));
        Assert.StartsWith(Md11Squawk.Name, Md11Squawk.Confirmation("1200", null));
        Assert.Contains(Md11FlapSystem.DialName, Md11FlapSystem.DialSetShortfall(20, 17));
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
    /// glareshield clickspot (<c>GA_BT_ALT</c>) fired the identical event pair 77851/77852. The
    /// generator now collapses any two nodes with byte-identical events into one row, keeping the
    /// aircraft's own MD11_-prefixed node id, so this pins the absence of the second row rather
    /// than the fact that it fired the same event.
    /// </summary>
    [Fact]
    public void GoAround_IsOneRow_NotTwoClickspotsOnTheSameEventPair()
    {
        Assert.NotNull(Find("MD11_THR_GA_BT"));
        Assert.Null(Find("GA_BT_ALT"));
        Assert.DoesNotContain(Map.Controls,
            c => c.NodeId != "MD11_THR_GA_BT" && c.Event("LEFT_BUTTON_DOWN") == 77851);
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

    /// <summary>
    /// The Ctrl+P window's bank-limiter combo is built from the DEFINITION's own descriptions for
    /// the knob and a pick writes their KEY — it was a hand-kept copy of the map written by row
    /// index. Pinned here is that source: six positions keyed 0-5, in the map's words.
    /// </summary>
    [Fact]
    public void BankAngleLimiter_TheDefinitionDescribesTheSixPositionsTheWindowOffers()
    {
        var d = new TFDiMD11Definition().GetVariables()["MD11_CGS_HDG_BASE_KB"].ValueDescriptions;

        Assert.Equal(new[] { 0.0, 1, 2, 3, 4, 5 }, d.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("Auto", d[0]);
        Assert.Equal("5 degrees", d[1]);
        Assert.Equal("25 degrees", d[5]);
    }

    /// <summary>
    /// The window writes its two combos through SetControl, past the panel path that marks a pick
    /// in MainForm's echo window, so it must be handed MainForm's SuppressUiEcho — the PMDG Ctrl+P
    /// window's arrangement. Pins the constructor the definition builds it with.
    /// </summary>
    [Fact]
    public void AutopilotWindow_IsBuiltWithTheEchoSuppressionCallback()
    {
        var ctor = typeof(Md11AutopilotWindow).GetConstructor(new[]
        {
            typeof(TFDiMD11Definition), typeof(SimConnectManager), typeof(ScreenReaderAnnouncer), typeof(Action<string, double>),
        });

        Assert.NotNull(ctor);
    }
}
