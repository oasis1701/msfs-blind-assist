using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's MCP windows: the Ctrl+P autopilot window's table and presses, Ctrl+P's A/T Arm (BlueBird's
/// armed flag) and the Ctrl+H/S/A/V value windows' buttons. Moved verbatim from
/// Bbs757FlightReadinessTests (gap plan 01, Task 2) so the plans that own these windows (05, then 14)
/// own their tests; the fixture helpers are copies, not shared.
/// Whether the aircraft moves is checked in the sim.
/// </summary>
public class Bbs757McpWindowTests
{
    private static readonly BBS757Definition Def = new();


    /// <summary>A simulated sim behind the definition's writes: fresh and cached reads by key, the writes made, the resyncs asked.</summary>
    private sealed class FakeSim
    {
        public bool CanWrite = true;
        public readonly Dictionary<string, double?> Fresh = new(StringComparer.Ordinal);
        public readonly Dictionary<string, double?> Cache = new(StringComparer.Ordinal);
        public readonly List<string> Writes = new();
        public readonly List<string> Resyncs = new();
        public Action<string>? OnWrite;

        public Bbs757SimIo Io => new()
        {
            CanWrite = () => CanWrite,
            ReadFresh = (key, ct) => Task.FromResult(Fresh.TryGetValue(key, out double? v) ? v : null),
            Cached = key => Cache.TryGetValue(key, out double? v) ? v : null,
            Write = rpn => { Writes.Add(rpn); OnWrite?.Invoke(rpn); },
            RequestRead = key => Resyncs.Add(key),
            Delay = (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
        };
    }

    private static string LightOf(string node) => Bbs757McpLights.ForNode(node)!.Key;

    [Theory]
    [InlineData(0.0, 0.0, "CMD L did not engage")]
    [InlineData(1.0, 1.0, "CMD L did not disengage")]
    public async Task A_window_press_the_aircraft_refused_says_so_once(double before, double after, string said)
    {
        var sim = new FakeSim();
        sim.Cache[LightOf("B757_MCP_CMD_1_BUTTON")] = before;
        sim.Fresh[LightOf("B757_MCP_CMD_1_BUTTON")] = after;
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();

        await def.PressFromWindowAsync("B757_MCP_CMD_1_BUTTON", new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { said }, speech.All);
        Assert.Equal(2, sim.Writes.Count);   // the press and its release, once
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 0.0)]
    public async Task A_window_press_that_took_is_silent(double before, double after)
    {
        // The button's caption changes, and the screen reader reads that (ruling R29).
        var sim = new FakeSim();
        sim.Cache[LightOf("B757_MCP_LNAV_BUTTON")] = before;
        sim.Fresh[LightOf("B757_MCP_LNAV_BUTTON")] = after;
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();

        await def.PressFromWindowAsync("B757_MCP_LNAV_BUTTON", new SimConnectManager(IntPtr.Zero), speech);

        Assert.Empty(speech.All);
    }

    [Fact]
    public async Task A_window_press_that_cannot_land_is_refused_aloud_and_sends_nothing()
    {
        var sim = new FakeSim { CanWrite = false };
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();

        await def.PressFromWindowAsync("B757_MCP_CMD_2_BUTTON", new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { "CMD C unavailable" }, speech.All);
        Assert.Empty(sim.Writes);
    }

    // ---- The autopilot window's table ------------------------------------------------------------

    [Fact]
    public void Autopilot_rows_in_order()
    {
        // The sixteen engage and mode controls, then Speed Intervene, A/T Disconnect and the captain yoke A/P disconnect (plan 19).
        Assert.Equal(new[]
        {
            "B757_MCP_CMD_1_BUTTON", "B757_MCP_CMD_2_BUTTON", "B757_MCP_CMD_3_BUTTON",
            "B757_MCP_SW_AP_throttle", "B757_MCP_SW_FD_Cap", "B757_MCP_SW_FD_FO",
            "B757_MCP_THR_BUTTON", "B757_MCP_SPD_BUTTON", "B757_MCP_LNAV_BUTTON", "B757_MCP_VNAV_BUTTON",
            "B757_MCP_FLCH_BUTTON", "B757_MCP_HOLD_1_BUTTON", "B757_MCP_HOLD_2_BUTTON", "B757_MCP_VS_BUTTON",
            "B757_MCP_LOC_BUTTON", "B757_MCP_APP_BUTTON",
            "B757_MCP_AP_SPD_HOLD_SW", "B757_AT_DISC_1", "btn_yoke_autopilot_disengage_pilot",
        }, Bbs757AutopilotLayout.Nodes);
    }

    [Fact]
    public void The_autopilot_window_buttons_are_labelled_in_order()
    {
        var labels = Def.AutopilotButtons(_ => null).Select(b => b.Label);

        Assert.Equal(new[]
        {
            "CMD L", "CMD C", "CMD R", "A/T Arm", "Captain F/D", "F/O F/D",
            "THR", "SPD", "LNAV", "VNAV", "FLCH", "HDG HOLD", "ALT HOLD", "V/S", "LOC", "APP",
            "Speed Intervene", "A/T Disconnect", "A/P Disconnect",
        }, labels);
    }

    [Fact]
    public void Autopilot_keys_unique_and_letters()
    {
        var keys = Bbs757AutopilotLayout.Nodes.Select(Bbs757AutopilotLayout.AccessKeyFor).ToList();

        Assert.All(keys, k => Assert.True(k is >= 'A' and <= 'Z', $"key {k}"));
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal("LCRAFOTSNVHDEYGPIXB", string.Concat(keys));
        Assert.All(Def.AutopilotButtons(_ => null), b => Assert.NotNull(b.Key));
        Assert.Null(Bbs757AutopilotLayout.AccessKeyFor("B757_MCP_AP_HDG_SEL_SW"));
    }

    [Fact]
    public void Every_autopilot_button_is_a_lit_button_a_two_position_switch_or_a_press()
    {
        var map = Bbs757ControlMap.Load();
        string[] unlit = { Bbs757AutopilotLayout.SpeedInterveneNode, Bbs757AutopilotLayout.AutothrottleDisconnectNode, Bbs757AutopilotLayout.AutopilotDisconnectNode };
        foreach (string node in Bbs757AutopilotLayout.Nodes)
        {
            var control = map.Controls.Single(c => c.Node == node);
            if (Bbs757AutopilotLayout.SwitchNodes.Contains(node))
            {
                Assert.Equal(Bbs757Kinds.Toggle, control.Kind);
                Assert.Equal(2, control.Positions.Count);
                Assert.Null(Bbs757McpLights.ForNode(node));
            }
            else
            {
                if (node == Bbs757AutopilotLayout.AutopilotDisconnectNode) Assert.Equal(Bbs757Kinds.Held, control.Kind);
                else Assert.Equal(Bbs757Kinds.Button, control.Kind);
                Assert.Equal(!unlit.Contains(node), Bbs757McpLights.ForNode(node) != null);
            }
        }
    }

    [Theory]
    [InlineData(0.0, "Window open")]
    [InlineData(1.0, "Window blank")]
    [InlineData(null, "")]
    public void Ctrl_p_speed_intervene_shows_the_speed_window(double? blanked, string state)
    {
        var cache = new Dictionary<string, double>(StringComparer.Ordinal);
        if (blanked is double b) cache[Bbs757Mcp.IasBlankedVar] = b;
        var intervene = Def.AutopilotButtons(key => cache.TryGetValue(key, out double v) ? v : null)
            .Single(x => x.Label == "Speed Intervene");

        Assert.Equal(state, intervene.State());
    }

    [Fact]
    public void Ctrl_p_a_t_disconnect_has_no_state()
    {
        var disconnect = Def.AutopilotButtons(_ => 1).Single(x => x.Label == "A/T Disconnect");

        Assert.Equal("", disconnect.State());
    }

    [Theory]
    [InlineData(Bbs757AutopilotLayout.AutothrottleDisconnectNode, "A/T Disconnect pressed")]
    [InlineData(Bbs757AutopilotLayout.SpeedInterveneNode, "Speed Intervene pressed")]
    [InlineData(Bbs757AutopilotLayout.AutopilotDisconnectNode, "A/P Disconnect pressed")]
    public async Task Ctrl_p_press_with_no_light_says_pressed_once(string node, string said)
    {
        var sim = new FakeSim();
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();

        await def.PressFromWindowAsync(node, new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { said }, speech.All);
        Assert.NotEmpty(sim.Writes);
    }

    [Fact]
    public async Task Ctrl_p_ap_disconnect_presses_the_captains_yoke_button()
    {
        var button = Def.AutopilotButtons(_ => 1).Single(x => x.Label == "A/P Disconnect");
        Assert.Equal("", button.State());
        Assert.DoesNotContain("B757_MCP_btn_autopilot_disengage", Bbs757AutopilotLayout.RequestedKeys);

        var sim = new FakeSim();
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();

        await def.PressFromWindowAsync(Bbs757AutopilotLayout.AutopilotDisconnectNode, new SimConnectManager(IntPtr.Zero), speech);

        Assert.True(sim.Writes.Count >= 2, "a press and then its release");
        Assert.Contains("btn_yoke_autopilot_disengage_pilot", sim.Writes[0]);
        Assert.Contains("K:AUTOPILOT_OFF", sim.Writes[0]);
        Assert.Equal(new[] { "A/P Disconnect pressed" }, speech.All);
    }

    [Fact]
    public void A_switch_shows_its_position_and_flips_to_the_other()
    {
        var atArm = Bbs757ControlMap.Load().Controls.Single(c => c.Node == Bbs757AutopilotLayout.AutothrottleArmNode);

        // L:AT_ARM_SWITCH: 0 On, 10 Off (the map's positions).
        Assert.Equal("On", Bbs757AutopilotLayout.SwitchState(atArm, 0));
        Assert.Equal("Off", Bbs757AutopilotLayout.SwitchState(atArm, 10));
        Assert.Equal("unavailable", Bbs757AutopilotLayout.SwitchState(atArm, null));
        Assert.Equal("unavailable", Bbs757AutopilotLayout.SwitchState(atArm, 5));
        Assert.Equal(10, Bbs757AutopilotLayout.FlipTarget(atArm, 0));
        Assert.Equal(0, Bbs757AutopilotLayout.FlipTarget(atArm, 10));
        Assert.Null(Bbs757AutopilotLayout.FlipTarget(atArm, 5));      // a toggle's press flips: never on an unknown reading
        Assert.Null(Bbs757AutopilotLayout.FlipTarget(atArm, null));
    }

    // ---- Ctrl+P's A/T Arm: BlueBird's armed flag (task 11b, ruling R40) ---------------------------

    [Fact]
    public void A_t_arm_shows_blueBirds_armed_flag_not_the_switch_position()
    {
        // The switch reads ON (0) in both cases; the caption follows L:BBS_AUTO_THROTTLE (1 On, 0 Off).
        var cache = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [Bbs757AutopilotLayout.AutothrottleArmNode] = 0,
            [Bbs757AutopilotLayout.AutothrottleArmedVar] = 0,
        };
        var buttons = Def.AutopilotButtons(key => cache.TryGetValue(key, out double v) ? v : null);
        var atArm = buttons.Single(b => b.Label == "A/T Arm");

        Assert.Equal("Off", atArm.State());
        cache[Bbs757AutopilotLayout.AutothrottleArmedVar] = 1;
        Assert.Equal("On", atArm.State());
        cache.Remove(Bbs757AutopilotLayout.AutothrottleArmedVar);
        Assert.Equal("unavailable", atArm.State());
    }

    [Fact]
    public void The_flight_director_switches_still_show_their_position()
    {
        // L:BBS_FD1_SWITCH: 0 Off, 1 On (the map's positions).
        var cache = new Dictionary<string, double>(StringComparer.Ordinal) { [Bbs757AutopilotLayout.CaptainFdNode] = 1 };
        var fd = Def.AutopilotButtons(key => cache.TryGetValue(key, out double v) ? v : null).Single(b => b.Label == "Captain F/D");

        Assert.Equal("On", fd.State());
        cache[Bbs757AutopilotLayout.CaptainFdNode] = 0;
        Assert.Equal("Off", fd.State());
    }

    [Theory]
    [InlineData(0.0, 0.0, "A/T Arm did not arm")]       // the caption read Off and still does
    [InlineData(1.0, 1.0, "A/T Arm did not disarm")]
    [InlineData(0.0, 1.0, null)]                        // it armed: the caption change says so
    [InlineData(1.0, 0.0, null)]
    [InlineData(null, 0.0, null)]                       // nothing known before: no claim
    [InlineData(1.0, null, null)]
    public void An_a_t_arm_press_the_flag_did_not_follow_says_so(double? before, double? after, string? phrase)
    {
        Assert.Equal(phrase, Bbs757AutopilotLayout.AutothrottleRefusedPhrase("A/T Arm", before, after));
    }

    private static FakeSim AtArmSim(double? flagBefore, double? flagAfter)
    {
        var sim = new FakeSim();
        sim.Cache[Bbs757AutopilotLayout.AutothrottleArmNode] = 0;   // the switch ON, as every panel state loads it
        sim.Fresh[Bbs757AutopilotLayout.AutothrottleArmNode] = 0;
        if (flagBefore is double b) sim.Cache[Bbs757AutopilotLayout.AutothrottleArmedVar] = b;
        sim.Fresh[Bbs757AutopilotLayout.AutothrottleArmedVar] = flagAfter;
        return sim;
    }

    [Theory]
    [InlineData(0.0, 0.0, "A/T Arm did not arm")]
    [InlineData(1.0, 1.0, "A/T Arm did not disarm")]
    public async Task An_a_t_arm_press_whose_flag_did_not_change_says_so_once(double before, double after, string said)
    {
        // A/T Arm's caption is BlueBird's armed flag; the press flips the switch (R40). With the switch ON and
        // the flag 0 (the two disagreeing), the click turns the switch OFF and the flag stays 0.
        var sim = AtArmSim(before, after);
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();

        await def.PressFromWindowAsync(Bbs757AutopilotLayout.AutothrottleArmNode, new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { said }, speech.All);
        Assert.Single(sim.Writes);   // the switch's own click, once
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(null, 1.0)]   // the caption had nothing to show: no claim
    public async Task An_a_t_arm_press_that_moved_the_flag_or_had_no_reading_is_silent(double? before, double after)
    {
        var sim = AtArmSim(before, after);
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();

        await def.PressFromWindowAsync(Bbs757AutopilotLayout.AutothrottleArmNode, new SimConnectManager(IntPtr.Zero), speech);

        Assert.Empty(speech.All);
        Assert.Single(sim.Writes);
    }

    [Fact]
    public async Task An_a_t_arm_press_that_cannot_land_is_refused_once_and_says_nothing_of_the_flag()
    {
        var sim = AtArmSim(0, 0);
        sim.CanWrite = false;
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();

        await def.PressFromWindowAsync(Bbs757AutopilotLayout.AutothrottleArmNode, new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { "A/T Arm unavailable" }, speech.All);
        Assert.Empty(sim.Writes);
    }

    [Fact]
    public async Task A_flight_director_press_says_nothing_of_the_armed_flag()
    {
        var sim = AtArmSim(0, 0);
        sim.Cache[Bbs757AutopilotLayout.CaptainFdNode] = 0;
        sim.Fresh[Bbs757AutopilotLayout.CaptainFdNode] = 0;
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();

        await def.PressFromWindowAsync(Bbs757AutopilotLayout.CaptainFdNode, new SimConnectManager(IntPtr.Zero), speech);

        Assert.Empty(speech.All);
        Assert.Single(sim.Writes);
    }

    [Fact]
    public void The_armed_flag_is_read_on_request_with_the_switches_and_mutes_nothing()
    {
        var flag = Def.GetVariables()[Bbs757AutopilotLayout.AutothrottleArmedVar];

        Assert.Equal("BBS_AUTO_THROTTLE", flag.Name);
        Assert.Equal(SimVarType.LVar, flag.Type);
        Assert.Equal(UpdateFrequency.OnRequest, flag.UpdateFrequency);
        Assert.False(flag.IsAnnounced);
        Assert.True(flag.ExcludeFromMonitorManager);   // a window's cache, never spoken: no Ctrl+M row ([VAR-9])
        Assert.Equal(
            Bbs757AutopilotLayout.SwitchNodes.Append(Bbs757AutopilotLayout.AutothrottleArmedVar),
            Bbs757AutopilotLayout.RequestedKeys);
    }

    // ---- The MCP value windows' buttons (task 11b, ruling R34; fixed keys and Speed Intervene: plan 05, R57) ----

    /// <summary>Today's map labels, held here so these tests never depend on the map's wording (plan 02 owns it).</summary>
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["B757_MCP_AP_HDG_SEL_SW"] = "HDG SEL", ["B757_MCP_HOLD_1_BUTTON"] = "HDG HOLD", ["B757_MCP_LNAV_BUTTON"] = "LNAV",
        ["B757_MCP_SPD_BUTTON"] = "SPD", ["B757_MCP_THR_BUTTON"] = "THR", ["B757_MCP_FLCH_BUTTON"] = "FLCH",
        ["B757_MCP_AP_SPD_MACH_SW"] = "IAS/MACH", ["B757_MCP_AP_SPD_HOLD_SW"] = "AP SPD Hold",
        ["B757_MCP_HOLD_2_BUTTON"] = "ALT HOLD", ["B757_MCP_VNAV_BUTTON"] = "VNAV", ["B757_MCP_VS_BUTTON"] = "V/S",
    };

    private static readonly HotkeyAction[] Windows =
        { HotkeyAction.FCUSetHeading, HotkeyAction.FCUSetSpeed, HotkeyAction.FCUSetAltitude, HotkeyAction.FCUSetVS };

    private static IReadOnlyList<string> WindowCaptions(HotkeyAction action) =>
        Bbs757McpWindowLayout.Captions(Bbs757McpWindowLayout.NodesFor(action), n => Labels[n]);

    [Fact]
    public void Each_mcp_window_carries_the_mode_buttons_that_go_with_its_value()
    {
        Assert.Equal(new[] { "B757_MCP_AP_HDG_SEL_SW", "B757_MCP_HOLD_1_BUTTON", "B757_MCP_LNAV_BUTTON" },
            Bbs757McpWindowLayout.NodesFor(HotkeyAction.FCUSetHeading));
        Assert.Equal(new[] { "B757_MCP_AP_SPD_HOLD_SW", "B757_MCP_AP_SPD_MACH_SW", "B757_MCP_SPD_BUTTON", "B757_MCP_THR_BUTTON", "B757_MCP_FLCH_BUTTON" },
            Bbs757McpWindowLayout.NodesFor(HotkeyAction.FCUSetSpeed));
        Assert.Equal(new[] { "B757_MCP_HOLD_2_BUTTON", "B757_MCP_VNAV_BUTTON", "B757_MCP_FLCH_BUTTON" },
            Bbs757McpWindowLayout.NodesFor(HotkeyAction.FCUSetAltitude));
        Assert.Equal(new[] { "B757_MCP_VS_BUTTON" }, Bbs757McpWindowLayout.NodesFor(HotkeyAction.FCUSetVS));
        Assert.Empty(Bbs757McpWindowLayout.NodesFor(HotkeyAction.FCUSetAutopilot));
    }

    [Fact]
    public void Every_window_button_is_a_map_button_and_only_hdg_sel_ias_mach_and_speed_intervene_have_no_light()
    {
        var map = Bbs757ControlMap.Load();
        var nodes = Windows.SelectMany(Bbs757McpWindowLayout.NodesFor).Distinct().ToList();

        Assert.All(nodes, n => Assert.Equal(Bbs757Kinds.Button, map.Controls.Single(c => c.Node == n).Kind));
        Assert.Equal(new[] { "B757_MCP_AP_HDG_SEL_SW", "B757_MCP_AP_SPD_HOLD_SW", "B757_MCP_AP_SPD_MACH_SW" },
            nodes.Where(n => Bbs757McpLights.ForNode(n) == null));
    }

    [Fact]
    public void Speed_window_keys()
    {
        // The PMDG windows' keys (R57): IAS/MACH moves from Alt+I to Alt+M, Speed Intervene takes Alt+I, FLCH is
        // Alt+L as the PMDG 737's Level Change.
        Assert.Equal(new[] { "Speed &Intervene", "IAS/&MACH", "&SPD", "&THR", "F&LCH" }, WindowCaptions(HotkeyAction.FCUSetSpeed));
    }

    [Fact]
    public void Heading_and_vs_keys_unchanged()
    {
        Assert.Equal(new[] { "HDG &SEL", "HDG &HOLD", "&LNAV" }, WindowCaptions(HotkeyAction.FCUSetHeading));
        Assert.Equal(new[] { "ALT &HOLD", "&VNAV", "F&LCH" }, WindowCaptions(HotkeyAction.FCUSetAltitude));
        Assert.Equal(new[] { "&V/S" }, WindowCaptions(HotkeyAction.FCUSetVS));
    }

    [Fact]
    public void No_window_repeats_a_key()
    {
        foreach (var window in Windows)
        {
            var keys = Bbs757McpWindowLayout.NodesFor(window).Select(Bbs757McpWindowLayout.AccessKeyFor).ToList();
            Assert.All(keys, k => Assert.NotNull(k));
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }
    }

    [Fact]
    public void A_key_follows_the_label_and_an_ampersand_shows_as_itself()
    {
        // A relabelled control keeps its key, marked on the word that starts with it, else on its first letter
        // that is the key; a label without the letter shows no key rather than a wrong one.
        Assert.Equal(new[] { "Heading &Select", "&LNAV && more" },
            Bbs757McpWindowLayout.Captions(new[] { "B757_MCP_AP_HDG_SEL_SW", "B757_MCP_LNAV_BUTTON" },
                n => n == "B757_MCP_LNAV_BUTTON" ? "LNAV & more" : "Heading Select"));
        Assert.Equal(new[] { "Flight &Level Change" }, Bbs757McpWindowLayout.Captions(new[] { "B757_MCP_FLCH_BUTTON" }, _ => "Flight Level Change"));
        Assert.Equal(new[] { "F&LCH" }, Bbs757McpWindowLayout.Captions(new[] { "B757_MCP_FLCH_BUTTON" }, _ => "FLCH"));
        Assert.Equal(new[] { "Rate" }, Bbs757McpWindowLayout.Captions(new[] { "B757_MCP_VS_BUTTON" }, _ => "Rate"));
        Assert.Equal("Speed Intervene", Bbs757McpWindowLayout.CaptionOverride("B757_MCP_AP_SPD_HOLD_SW"));
        Assert.Null(Bbs757McpWindowLayout.CaptionOverride("B757_MCP_SPD_BUTTON"));
        Assert.Null(Bbs757McpWindowLayout.AccessKeyFor("B757_MCP_CMD_1_BUTTON"));
    }

    [Theory]
    [InlineData(0.0, "Window open")]
    [InlineData(1.0, "Window blank")]
    [InlineData(null, "")]
    public void Speed_intervene_state_text(double? blanked, string state)
    {
        var sim = new FakeSim();
        if (blanked is double b) sim.Cache[Bbs757Mcp.IasBlankedVar] = b;
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };

        var intervene = def.McpWindowButtons(HotkeyAction.FCUSetSpeed, new SimConnectManager(IntPtr.Zero), new SpeechCapture())
            .Single(b => b.Label == "Speed &Intervene");

        Assert.Equal(state, intervene.GetCurrentState());
    }

    [Fact]
    public void Speed_intervene_press_is_the_panels_and_says_its_own_name()
    {
        var sim = new FakeSim();
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();
        var intervene = def.McpWindowButtons(HotkeyAction.FCUSetSpeed, new SimConnectManager(IntPtr.Zero), speech)
            .Single(b => b.Label == "Speed &Intervene");

        intervene.OnPressed();

        Assert.Equal(new[] { "Speed Intervene pressed" }, speech.All);
        Assert.Equal(2, sim.Writes.Count);   // the press and its release
        Assert.Contains("B757_MCP_IAS_MACH_KNOB_BUTTON", sim.Writes[1]);
    }

    [Fact]
    public void Speed_intervene_that_cannot_land_is_refused_by_its_own_name()
    {
        var sim = new FakeSim { CanWrite = false };
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();

        def.McpWindowButtons(HotkeyAction.FCUSetSpeed, new SimConnectManager(IntPtr.Zero), speech)
            .Single(b => b.Label == "Speed &Intervene").OnPressed();

        Assert.Equal(new[] { "Speed Intervene unavailable" }, speech.All);
        Assert.Empty(sim.Writes);
    }

    [Fact]
    public void The_speed_window_buttons_show_their_lights_and_ias_mach_shows_no_state()
    {
        var sim = new FakeSim();
        sim.Cache[LightOf("B757_MCP_SPD_BUTTON")] = 1;
        sim.Cache[LightOf("B757_MCP_THR_BUTTON")] = 0;   // FLCH's light not read yet
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };

        var buttons = def.McpWindowButtons(HotkeyAction.FCUSetSpeed, new SimConnectManager(IntPtr.Zero), new SpeechCapture());

        Assert.Equal(new[] { "Speed &Intervene", "IAS/&MACH", "&SPD", "&THR", "F&LCH" }, buttons.Select(b => b.Label));
        Assert.Equal(new[] { "", "", "On", "Off", "unavailable" }, buttons.Select(b => b.GetCurrentState()));
        // The press speaks its own read-back once; the window's 1.2 s read-out would say it again (CORE-7).
        Assert.All(buttons, b => Assert.True(b.SuppressStateAnnounce?.Invoke()));
    }

    [Fact]
    public void A_window_button_press_is_the_panels_and_says_the_light_once()
    {
        string light = LightOf("B757_MCP_LNAV_BUTTON");
        var sim = new FakeSim();
        sim.Cache[light] = 0;
        sim.Fresh[light] = 1;
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate(light, 0, speech);                       // the light's baseline
        sim.OnWrite = _ => def.ProcessSimVarUpdate(light, 1, speech);   // its own change arrives during the press
        var lnav = def.McpWindowButtons(HotkeyAction.FCUSetHeading, new SimConnectManager(IntPtr.Zero), speech)
            .Single(b => b.Label == "&LNAV");

        lnav.OnPressed();

        Assert.Equal(new[] { "LNAV on" }, speech.All);
        Assert.Equal(2, sim.Writes.Count);   // the press and its release
        Assert.Contains("B757_MCP_LNAV_BUTTON", sim.Resyncs);
    }

    [Fact]
    public void A_window_button_with_no_light_says_pressed()
    {
        var sim = new FakeSim();
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();
        var hdgSel = def.McpWindowButtons(HotkeyAction.FCUSetHeading, new SimConnectManager(IntPtr.Zero), speech)
            .Single(b => b.Label == "HDG &SEL");

        hdgSel.OnPressed();

        Assert.Equal(new[] { "HDG SEL pressed" }, speech.All);
        Assert.Equal(2, sim.Writes.Count);
    }

    [Theory]
    [InlineData(HotkeyAction.FCUSetSpeed, "B757_MCP_THR_BUTTON", 1.0)]
    [InlineData(HotkeyAction.FCUSetAltitude, "B757_MCP_VNAV_BUTTON", 0.0)]
    [InlineData(HotkeyAction.FCUSetSpeed, "B757_MCP_AP_SPD_MACH_SW", null)]
    public void A_window_button_press_writes_and_says_what_the_panels_press_does(HotkeyAction window, string node, double? readBack)
    {
        (List<string> Writes, List<string> Said) Run(Action<BBS757Definition, SpeechCapture> press)
        {
            var sim = new FakeSim();
            if (Bbs757McpLights.ForNode(node) is { } light) sim.Fresh[light.Key] = readBack;
            var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
            var speech = new SpeechCapture();
            press(def, speech);
            return (sim.Writes, speech.All);
        }

        var fromPanel = Run((def, speech) => def.HandleUIVariableSet(node, 1, def.GetVariables()[node], new SimConnectManager(IntPtr.Zero), speech));
        var fromWindow = Run((def, speech) =>
        {
            int i = Bbs757McpWindowLayout.NodesFor(window).ToList().IndexOf(node);
            def.McpWindowButtons(window, new SimConnectManager(IntPtr.Zero), speech)[i].OnPressed();
        });

        Assert.Equal(fromPanel.Writes, fromWindow.Writes);
        Assert.Equal(fromPanel.Said, fromWindow.Said);
        Assert.Single(fromWindow.Said);
    }

    [Fact]
    public void A_window_button_press_that_cannot_land_is_refused_aloud_and_sends_nothing()
    {
        var sim = new FakeSim { CanWrite = false };
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();

        def.McpWindowButtons(HotkeyAction.FCUSetVS, new SimConnectManager(IntPtr.Zero), speech).Single().OnPressed();

        Assert.Equal(new[] { "V/S unavailable" }, speech.All);
        Assert.Empty(sim.Writes);
    }

    [Fact]
    public void A_window_button_missing_from_the_map_is_left_out()
    {
        var map = new Bbs757ControlMap
        {
            Controls = Bbs757ControlMap.Load().Controls.Where(c => c.Node != "B757_MCP_THR_BUTTON").ToList(),
        };
        var def = new BBS757Definition(map) { NestRunning = () => true, WriteIo = _ => new FakeSim().Io };

        var buttons = def.McpWindowButtons(HotkeyAction.FCUSetSpeed, new SimConnectManager(IntPtr.Zero), new SpeechCapture());

        Assert.Equal(new[] { "Speed &Intervene", "IAS/&MACH", "&SPD", "F&LCH" }, buttons.Select(b => b.Label));
    }
}
