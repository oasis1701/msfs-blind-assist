using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The Ctrl+P "757 Autopilot" window: one button per MCP engage control (<see cref="Bbs757AutopilotLayout"/>),
/// each captioned with its live state and pressed by its fixed Alt key. A mode or CMD button shows its
/// MCP light (from the 1 Hz batch), Speed Intervene the speed window's blank flag, A/T Disconnect
/// nothing; A/P Disconnect is the captain's yoke button (a held press, no state; a second press silences
/// the warning on BlueBird, the owner, 2026-11-05); the two F/D switches show their map position and A/T ARM BlueBird's armed flag
/// (<c>L:BBS_AUTO_THROTTLE</c>, the A/T's real state, ruling R40), each read on request about once a
/// second while the window is shown (they are OnRequest variables, and a status row of their own
/// would cost a data definition and a Ctrl+M row nobody asked for).
///
/// A press goes through the same writer as the panel: a button replays its click code
/// (<see cref="PressAsync"/>) without speaking when its light changes (the caption's change is the
/// feedback, as in the iFly, HS787 and MD-11 windows), its light change being that press's echo (a
/// button with no light says "Speed Intervene pressed", as the panel does); when
/// the light reads back as it was, the caption cannot change, so the press says "CMD L did not
/// engage" or "… did not disengage" (the iFly window's words). A switch is moved to its other
/// position (<see cref="SetPositionAsync"/>); A/T Arm then reads its flag back and, when it did not
/// follow, says "A/T Arm did not arm" or "… did not disarm" once. Nothing that can land: "{label}
/// unavailable".
/// </summary>
public partial class BBS757Definition
{
    private BBS757AutopilotWindow? _autopilotWindow;

    /// <summary>The window's sim and speech, the latest a Ctrl+P handed in (a reconnect hands a new manager).</summary>
    private SimConnectManager? _autopilotSim;
    private ScreenReaderAnnouncer? _autopilotAnnouncer;

    private void ShowAutopilotWindow(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _autopilotSim = sim;
        _autopilotAnnouncer = announcer;
        if (_autopilotWindow == null || _autopilotWindow.IsDisposed)
            _autopilotWindow = new BBS757AutopilotWindow(
                AutopilotButtons(key => _autopilotSim?.GetCachedVariableValue(key)), RequestAutopilotSwitches, Fma.View);
        _autopilotWindow.ShowForm();
    }

    /// <summary>The window's buttons, each state read through <paramref name="cached"/> (the sim's cache; a test's own).</summary>
    internal List<Bbs757AutopilotButton> AutopilotButtons(Func<string, double?> cached)
    {
        var buttons = new List<Bbs757AutopilotButton>();
        foreach (string node in Bbs757AutopilotLayout.Nodes)
        {
            if (!_byNode.TryGetValue(node, out var control))
            {
                Log.Warn("BBS757", $"Autopilot window: {node} is not in the control map; its button is left out.");
                continue;
            }
            Func<string> state = Bbs757McpLights.ForNode(node) is { } light
                ? () => Bbs757McpLights.State(cached(light.Key))
                : node switch
                {
                    Bbs757AutopilotLayout.AutothrottleArmNode
                        => () => Bbs757AutopilotLayout.AutothrottleState(cached(Bbs757AutopilotLayout.AutothrottleArmedVar)),
                    Bbs757AutopilotLayout.SpeedInterveneNode => () => SpeedInterveneState(cached(Bbs757Mcp.IasBlankedVar)),
                    _ when Bbs757AutopilotLayout.SwitchNodes.Contains(node)
                        => () => Bbs757AutopilotLayout.SwitchState(control, cached(node)),
                    _ => () => "",   // A/T Disconnect, A/P Disconnect: a press with nothing to show
                };
            string label = Bbs757McpWindowLayout.CaptionOverride(node) ?? control.Label;
            buttons.Add(new Bbs757AutopilotButton(label, state, () =>
            {
                if (_autopilotSim is { } sim && _autopilotAnnouncer is { } announcer)
                    _ = PressFromWindowAsync(control.Node, sim, announcer);   // never throws: it catches and logs
            }, Bbs757AutopilotLayout.AccessKeyFor(node)));
        }
        return buttons;
    }

    /// <summary>
    /// The switches' states and the A/T's armed flag are read on request: ask for them (the window does,
    /// about once a second while shown).
    /// </summary>
    private void RequestAutopilotSwitches()
    {
        var sim = _autopilotSim;
        if (sim == null || !sim.IsConnected || _lifetime.IsCancellationRequested) return;
        foreach (string key in Bbs757AutopilotLayout.RequestedKeys) sim.RequestVariable(key);
    }

    /// <summary>
    /// Registers the A/T's armed flag as an OnRequest L:var, so the window's request can deliver it:
    /// a window's cache, never spoken, so no Ctrl+M row ([VAR-9]) and on no panel.
    /// </summary>
    private static void AddAutopilotVariables(Dictionary<string, SimVarDefinition> vars)
    {
        if (vars.ContainsKey(Bbs757AutopilotLayout.AutothrottleArmedVar)) return;   // never shadow a map control or a base var
        vars[Bbs757AutopilotLayout.AutothrottleArmedVar] = new SimVarDefinition
        {
            Name = Bbs757AutopilotLayout.AutothrottleArmedVar, DisplayName = "Autothrottle Armed",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
            ExcludeFromMonitorManager = true,
        };
    }

    /// <summary>
    /// A window button: a button presses without speaking on success (its caption changes) and says so
    /// when the aircraft refused it ("CMD L did not engage"); a switch moves to its other position.
    /// Refused aloud when nothing can land. Never throws.
    /// </summary>
    internal async Task PressFromWindowAsync(string node, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (_lifetime.IsCancellationRequested || !_byNode.TryGetValue(node, out var control)) return;
        var io = WriteIoFor(sim);

        if (Bbs757Write.IsMomentary(control))
        {
            var press = Bbs757Write.PressFor(control);
            if (press == null || !io.CanWrite())
            {
                SayUnavailable(control, announcer);
                return;
            }
            // A lit button's caption is its feedback; Speed Intervene, A/T Disconnect and A/P Disconnect have no light, so
            // they say "… pressed" once, as the panel does (CORE-7).
            await PressAsync(control, press, io, announcer, speak: Bbs757McpLights.ForNode(node) == null,
                label: Bbs757McpWindowLayout.CaptionOverride(node));
            return;
        }
        await FlipFromWindowAsync(control, io, announcer);
    }

    /// <summary>
    /// A switch pressed in the window: its other position, from the cache or else a fresh read, then the
    /// panel's own pick (which refuses aloud, reads fresh again and writes only what is owed).
    /// A/T Arm's caption is BlueBird's armed flag, not the switch (R40), and the click flips the switch:
    /// after its pick wrote, the flag is read fresh, and when it reads as the caption showed it before the
    /// press, the caption cannot change, so the press says so once ("A/T Arm did not arm"). A flag that
    /// changed says nothing: the caption's change is the feedback (R29).
    /// </summary>
    private async Task FlipFromWindowAsync(Bbs757Control control, Bbs757SimIo io, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;
            // Taken once, before any await: a switch away disposes _lifetime, whose Token then throws.
            var ct = _lifetime.Token;
            double? now = io.Cached(control.Node) ?? await io.ReadFresh(control.Node, ct);
            if (Bbs757AutopilotLayout.FlipTarget(control, now) is not double target)
            {
                SayUnavailable(control, announcer);
                return;
            }
            bool atArm = control.Node == Bbs757AutopilotLayout.AutothrottleArmNode;
            double? armedBefore = atArm ? io.Cached(Bbs757AutopilotLayout.AutothrottleArmedVar) : null;
            if (!await SetPositionAsync(control, target, io, announcer) || !atArm) return;

            if (_lifetime.IsCancellationRequested) return;   // switched away meanwhile
            double? armedAfter = await io.ReadFresh(Bbs757AutopilotLayout.AutothrottleArmedVar, ct);
            ct.ThrowIfCancellationRequested();
            if (Bbs757AutopilotLayout.AutothrottleRefusedPhrase(control.Label, armedBefore, armedAfter) is { } refused)
                announcer.Announce(refused);
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.Node}: window press failed: {ex.Message}");
        }
    }

    /// <summary>A profile switch closes the Ctrl+B and Ctrl+P windows: their buttons write through this definition, which no longer may.</summary>
    private void CloseAutoflightWindows()
    {
        try
        {
            if (_autopilotWindow != null && !_autopilotWindow.IsDisposed) _autopilotWindow.Dispose();
        }
        catch (Exception ex) { Log.Debug("BBS757", $"Autopilot window did not close: {ex.Message}"); }
        _autopilotWindow = null;
        try
        {
            if (_altimeterForm != null && !_altimeterForm.IsDisposed) _altimeterForm.Close();
        }
        catch (Exception ex) { Log.Debug("BBS757", $"Altimeter window did not close: {ex.Message}"); }
        _altimeterForm = null;
    }
}
