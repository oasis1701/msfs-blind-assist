using System.Collections.Concurrent;
using System.Text.Json;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;
using A = MSFSBlindAssist.Aircraft.BBS757.Bbs757AutoflightAnnouncements;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The MCP's heading, speed, altitude and vertical speed: the value windows (Ctrl+H/S/A/V in input
/// mode, the HS787's modeless <see cref="ValueInputForm"/>, with the mode buttons that go with each
/// value, as the PMDG 737's windows have) and the readouts (Shift+H/S/A/V in output mode). How each
/// value is set and why is in <see cref="Bbs757Mcp"/>; this partial is the sim side.
///
/// Every set runs from the UI thread (the window's callback), refuses aloud when the calculator
/// cannot land a write ([MD11-16]: the MCP is written only through
/// <see cref="SimConnectManager.ExecuteCalculatorCodeUnique"/>, never SendEvent, so [SIM-20] has
/// nothing to say here), never blocks (the waits are awaited), and checks the profile's lifetime
/// before it writes. A newer set of the same value supersedes one still in flight, so two windows
/// of corrections never fight over one variable. What is spoken is the value read back (CORE-7:
/// a numeric confirmation always speaks).
/// </summary>
public partial class BBS757Definition
{
    /// <summary>One set in flight: its token source and the value it is setting.</summary>
    private sealed record McpSetInFlight(CancellationTokenSource Cts, int Value);

    /// <summary>
    /// The set in flight per MCP variable; a newer set of the same variable cancels it, except a
    /// repeat of the same speed (see <see cref="RunMcpSetAsync"/>).
    /// </summary>
    private readonly ConcurrentDictionary<string, McpSetInFlight> _mcpSets = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether a set can land; replaced by tests at construction (init only), since a
    /// SimConnectManager cannot be connected in them. Nothing can swap it once the definition runs.
    /// </summary>
    internal Func<SimConnectManager, bool> CanWrite { get; init; } = sim => sim.CalcWriteCanLand;

    /// <summary>The open value windows, one per kind: a second hotkey press brings it forward, never stacks another.</summary>
    private readonly Dictionary<HotkeyAction, ValueInputForm> _mcpDialogs = new();

    private static readonly (string Key, string Label)[] McpVariables =
    {
        (Bbs757Mcp.HeadingVar, "MCP Heading"),
        (Bbs757Mcp.AltitudeVar, "MCP Altitude"),
        (Bbs757Mcp.SpeedVar, "MCP Speed"),
        (Bbs757Mcp.VsVar, "MCP Vertical Speed"),
        (Bbs757Mcp.MachModeVar, "MCP Speed Mach Mode"),
        (Bbs757Mcp.MachTenthsDrumVar, "MCP Speed Window Hundreds Wheel"),
        (Bbs757Mcp.MachHundredthsDrumVar, "MCP Speed Window Tens Wheel"),
    };

    /// <summary>
    /// Registers the MCP variables as OnRequest L:vars: <c>ReadFreshAsync</c> delivers only a
    /// registered key. Read for the readouts and the set loops only, so they carry no Ctrl+M row
    /// ([VAR-9]) and sit on no panel.
    /// </summary>
    private static void AddMcpVariables(Dictionary<string, SimVarDefinition> vars)
    {
        foreach (var (key, label) in McpVariables)
        {
            if (vars.ContainsKey(key)) continue;   // never shadow a map control or a base var
            vars[key] = new SimVarDefinition
            {
                Name = key, DisplayName = label,
                Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
                ExcludeFromMonitorManager = true,
            };
        }

        // Batched, not on request: the speed window's 400 ms button refresh reads it from the cache
        // (Speed Intervene's "Window blank"). Only a Continuous AND IsAnnounced var joins the batch; one
        // that is not announced gets no subscription and is never delivered (measured 2026-10-26).
        // Never spoken: ProcessAutoflightUpdate consumes it, and it has no Ctrl+M row ([VAR-9]).
        vars.TryAdd(Bbs757Mcp.IasBlankedVar, Watch(Bbs757Mcp.IasBlankedVar, "MCP Speed Window Blank", silent: true));

        // The autoflight call-outs (Task 4): batched rows on BlueBird's own variables, each sharing its Name
        // with an OnRequest key above at most ([VAR-7]: never two Continuous), so the sets' fresh reads are
        // unchanged ([SIM-1]). Two are silent: they only feed another row's call-out.
        vars.TryAdd(A.HeadingWatchKey, Watch(Bbs757Mcp.HeadingVar, "MCP Heading"));
        vars.TryAdd(A.AltitudeWatchKey, Watch(Bbs757Mcp.AltitudeVar, "MCP Altitude"));
        vars.TryAdd(A.VsWatchKey, Watch(Bbs757Mcp.VsVar, "MCP Vertical Speed"));
        vars.TryAdd(A.SpeedWatchKey, Watch(Bbs757Mcp.SpeedVar, "MCP Speed"));
        vars.TryAdd(A.MachWatchKey, Watch(Bbs757Mcp.MachHundredthsDrumVar, "MCP Mach"));
        vars.TryAdd(A.MachTenthsWatchKey, Watch(Bbs757Mcp.MachTenthsDrumVar, "MCP Mach Tenths", silent: true));
        vars.TryAdd(A.ModeWatchKey, Watch(Bbs757Mcp.MachModeVar, "MCP Speed Mode", silent: true));
        vars.TryAdd(A.AtArmedKey, Watch(A.AtArmedVar, "A/T Armed"));
        vars.TryAdd(A.ApDiscLightKey, Watch(A.ApDiscLightVar, "Autopilot Disconnect"));
        vars.TryAdd(A.AtDiscLightKey, Watch(A.AtDiscLightVar, "Autothrottle Disconnect"));
    }

    /// <summary>A batched announced L:var row with no panel; a silent one has no Ctrl+M row ([VAR-9]).</summary>
    private static SimVarDefinition Watch(string name, string label, bool silent = false) => new()
    {
        Name = name, DisplayName = label,
        Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true, ExcludeFromMonitorManager = silent,
    };

    // =================================================================================
    // Autoflight call-outs (Task 4)
    // =================================================================================

    /// <summary>One resting value per MCP row that speaks once it rests (<see cref="A.RestingRows"/>).</summary>
    private readonly Dictionary<string, Bbs757RestingValue> _resting =
        A.RestingRows.ToDictionary(k => k, _ => new Bbs757RestingValue(), StringComparer.Ordinal);

    // The speed rows' inputs, as last delivered: ProcessSimVarUpdate gets no SimConnectManager. A batch
    // sends them sorted by name, the speed before the mode flag and the blank flag, the Mach's hundredths
    // wheel before its tenths wheel (review findings 2 and 3), so they are judged together once the whole
    // batch is in (EvaluateSpeedRows), never as each arrives.
    private double? _lastBlanked, _lastMachFlag, _lastMachTenths, _lastSpeed, _lastMachHundredths;

    /// <summary>A speed row's input arrived since the last judging.</summary>
    private bool _speedRowsDirty;

    // The flags the speed rows were last judged under (null: unread), and whether they have been judged.
    private bool? _judgedBlank, _judgedMach;
    private bool _speedRowsJudged;

    // The phrases last fed to the speed and Mach rows, so an unchanged value is not fed again (which
    // would restart its rest).
    private string? _fedSpeed, _fedMach;

    /// <summary>
    /// How long after one of the app's own MCP sets the row it moved is the set's echo: the set spoke the
    /// value it read back ("Heading 090"), and the batch delivers the change up to a second later.
    /// </summary>
    internal const int OwnSetEchoMs = 3000;

    /// <summary>Per resting row, when its own-set echo window ends; during a set the row is the set's too.</summary>
    private readonly Dictionary<string, long> _ownSetEchoUntil = new(StringComparer.Ordinal);

    /// <summary>Whether a UI-thread timer releases the resting values; tests turn it off and call <see cref="ReleaseRestedAutoflight"/>.</summary>
    internal bool UseRestTimer { get; init; } = true;

    private System.Windows.Forms.Timer? _restTimer;
    private ScreenReaderAnnouncer? _restAnnouncer;

    /// <summary>The release's tick: often enough that a value is heard within a quarter second of resting.</summary>
    private const int RestTickMs = 250;

    /// <summary>
    /// Plan 05's deliveries (the seam in BBS757Definition.Hooks.cs). The heading, altitude and V/S rows feed
    /// their resting values at once. The speed rows' inputs (the speed, both Mach wheels, the Mach flag and
    /// the blank flag) are only kept: they are judged together by the release once the batch is in
    /// (<see cref="EvaluateSpeedRows"/>). Resting values are spoken by the release, outside MainForm's mute
    /// wrap, which checks each row's mute itself ([VAR-8]). A/T arm and the disconnect lights speak here,
    /// once per change, through the state tracker; MainForm's wrap mutes them.
    /// </summary>
    partial void ProcessAutoflightUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled)
    {
        switch (varName)
        {
            case Bbs757Mcp.IasBlankedVar:
                _lastBlanked = value;
                MarkSpeedRowsDirty(announcer);
                break;
            case A.ModeWatchKey:
                _lastMachFlag = value;
                MarkSpeedRowsDirty(announcer);
                break;
            case A.MachTenthsWatchKey:
                _lastMachTenths = value;
                MarkSpeedRowsDirty(announcer);
                break;
            case A.MachWatchKey:
                _lastMachHundredths = value;
                MarkSpeedRowsDirty(announcer);
                break;
            case A.SpeedWatchKey:
                _lastSpeed = value;
                MarkSpeedRowsDirty(announcer);
                break;
            case A.HeadingWatchKey:
                FeedNow(varName, Bbs757Mcp.HeadingVar, double.IsFinite(value) ? A.HeadingPhrase(value) : null, announcer);
                break;
            case A.AltitudeWatchKey:
                FeedNow(varName, Bbs757Mcp.AltitudeVar, double.IsFinite(value) ? A.AltitudePhrase(value) : null, announcer);
                break;
            case A.VsWatchKey:
                FeedNow(varName, Bbs757Mcp.VsVar, double.IsFinite(value) ? A.VsPhrase(value) : null, announcer);
                break;
            case A.AtArmedKey:
                SpeakChange(varName, A.AtArmedPhrase(value), announcer);
                break;
            case A.ApDiscLightKey:
            case A.AtDiscLightKey:
                string what = varName == A.ApDiscLightKey ? "Autopilot" : "Autothrottle";
                string? on = A.DiscLightPhrase(what, value);
                // The tracker sees both states, so the light coming on again is a change; only "on" is spoken.
                if (double.IsFinite(value) && _changes.Observe(varName, on ?? $"{what} disconnect off", NowMs()) is { } said && on != null)
                    announcer.Announce(said);
                break;
            default:
                return;
        }
        handled = true;
    }

    private void FeedNow(string row, string setKey, string? phrase, ScreenReaderAnnouncer announcer)
    {
        if (Feed(row, setKey, phrase, speakable: true, NowMs())) StartRestTimer(announcer);
    }

    private void MarkSpeedRowsDirty(ScreenReaderAnnouncer announcer)
    {
        _speedRowsDirty = true;
        StartRestTimer(announcer);
    }

    /// <summary>
    /// The speed and Mach rows, judged once a batch is in (the release's tick; a batch is delivered in one
    /// go on the UI thread, so a tick never falls inside one). A change of the blank or Mach flag is a
    /// changeover, VNAV taking or giving back the speed, or Speed Intervene: both rows take their new values
    /// as spoken and anything waiting is dropped. Otherwise a changed speed is fed while the window is open
    /// in IAS, a changed Mach while it is open in Mach; at any other time the value counts as spoken (a VNAV
    /// target behind a blank window, the Mach's IAS equivalent). The Mach is read from both wheels and is
    /// only a Mach at .40 or more, never the knots the wheels show in IAS.
    /// </summary>
    private void EvaluateSpeedRows(long nowMs)
    {
        if (!_speedRowsDirty) return;
        _speedRowsDirty = false;

        bool? blank = On(_lastBlanked), mach = On(_lastMachFlag);
        string? speed = _lastSpeed is double s && double.IsFinite(s) ? A.SpeedPhrase(s) : null;
        string? machPhrase = Bbs757Mcp.MachFromDrums(_lastMachTenths, _lastMachHundredths) is double m ? A.MachPhrase(m) : null;

        bool flagsChanged = !_speedRowsJudged || blank != _judgedBlank || mach != _judgedMach;
        _speedRowsJudged = true;
        _judgedBlank = blank;
        _judgedMach = mach;

        if (speed != null && (flagsChanged || speed != _fedSpeed))
        {
            _fedSpeed = speed;
            Feed(A.SpeedWatchKey, Bbs757Mcp.SpeedVar, speed, speakable: !flagsChanged && blank != true && mach != true, nowMs);
        }
        if (machPhrase != null && (flagsChanged || machPhrase != _fedMach))
        {
            _fedMach = machPhrase;
            Feed(A.MachWatchKey, Bbs757Mcp.SpeedVar, machPhrase, speakable: !flagsChanged && mach == true && blank != true, nowMs);
        }
        if (flagsChanged)
        {
            // A value that did not change still loses anything it was waiting to say.
            if (_fedSpeed != null) _resting[A.SpeedWatchKey].Rebaseline(_fedSpeed);
            if (_fedMach != null) _resting[A.MachWatchKey].Rebaseline(_fedMach);
        }

        static bool? On(double? v) => v is double d && double.IsFinite(d) ? d >= 0.5 : null;
    }

    /// <summary>
    /// One resting row's new value: a change while it may be spoken (true: it now waits to rest), else it
    /// counts as spoken. A row whose value is being set by the app (<paramref name="setKey"/> in flight), or
    /// was within <see cref="OwnSetEchoMs"/>, is the set's. An unreadable value does nothing.
    /// </summary>
    private bool Feed(string row, string setKey, string? phrase, bool speakable, long nowMs)
    {
        if (phrase == null) return false;
        var resting = _resting[row];
        bool ownSet = _mcpSets.ContainsKey(setKey)
                      || (_ownSetEchoUntil.TryGetValue(setKey, out long until) && nowMs < until);
        if (!speakable || ownSet)
        {
            resting.Rebaseline(phrase);
            return false;
        }
        resting.Changed(phrase, nowMs);
        return resting.Pending;
    }

    private void StartRestTimer(ScreenReaderAnnouncer announcer)
    {
        _restAnnouncer = announcer;
        if (!UseRestTimer) return;
        if (_restTimer == null)
        {
            _restTimer = new System.Windows.Forms.Timer { Interval = RestTickMs };
            _restTimer.Tick += (_, _) =>
            {
                if (_restAnnouncer is { } a) ReleaseRestedAutoflight(NowMs(), a);
            };
        }
        if (!_restTimer.Enabled) _restTimer.Start();
    }

    /// <summary>
    /// The release (the timer's tick, or a test with its clock): the speed rows are judged if a batch moved
    /// them (<see cref="EvaluateSpeedRows"/>), then each resting row whose value has rested is spoken, unless
    /// the pilot muted its Ctrl+M row, when it is dropped. The timer stops once nothing waits. Speaks outside
    /// MainForm's mute wrap, so the mute is checked here ([VAR-8]).
    /// </summary>
    internal void ReleaseRestedAutoflight(long nowMs, ScreenReaderAnnouncer announcer)
    {
        EvaluateSpeedRows(nowMs);
        foreach (string row in A.RestingRows)
        {
            if (_resting[row].TakeDue(nowMs) is { } phrase && !IsMonitorMuted(row))
                announcer.Announce(phrase);
        }
        if (!_speedRowsDirty && _resting.Values.All(r => !r.Pending)) _restTimer?.Stop();
    }

    /// <summary>A set of <paramref name="setKey"/> just ended: its row's next deliveries are its echo for a while.</summary>
    private void ArmOwnSetEcho(string setKey) => _ownSetEchoUntil[setKey] = NowMs() + OwnSetEchoMs;

    /// <summary>A flight load, a reconnect or a switch away: every row's next value is a baseline; the timer stops.</summary>
    partial void ResetAutoflight()
    {
        foreach (var resting in _resting.Values) resting.Reset();
        _ownSetEchoUntil.Clear();
        _lastBlanked = _lastMachFlag = _lastMachTenths = _lastSpeed = _lastMachHundredths = null;
        _speedRowsDirty = _speedRowsJudged = false;
        _judgedBlank = _judgedMach = null;
        _fedSpeed = _fedMach = null;
        _restTimer?.Stop();
        _restTimer?.Dispose();
        _restTimer = null;
    }

    /// <summary>The MCP sets' and readouts' sim access, replaced by tests (a SimConnectManager cannot read in them); null: the real one.</summary>
    internal Func<SimConnectManager, McpIo>? McpSetIo { get; init; }

    private McpIo McpIoFor(SimConnectManager sim) => McpSetIo?.Invoke(sim) ?? new McpIo
    {
        Read = (key, ct) => sim.ReadFreshAsync(key, FreshReadTimeoutMs, ct),
        Write = sim.ExecuteCalculatorCodeUnique,
        Delay = Task.Delay,
        Cached = sim.GetCachedVariableValue,
    };

    // =================================================================================
    // Value windows
    // =================================================================================

    private delegate bool McpParser(string? input, out int value, out string? error);

    /// <summary>
    /// What one value window is: its words, what is wrong with a typed text (null when it is accepted)
    /// and what an accepted text does.
    /// </summary>
    private sealed record McpEntry(string Title, string Parameter, string Range, Func<string?, string?> Error,
        Func<string, Task> Apply);

    /// <summary>A window whose typed value is one whole number (heading, altitude, vertical speed).</summary>
    private static McpEntry NumberEntry(string title, string parameter, string range, McpParser parse, Func<int, Task> set)
        => new(title, parameter, range,
            input => parse(input, out _, out string? error) ? null : error ?? "",
            // The validator has accepted this text; parsing again is the only way the callback gets the number.
            input => parse(input, out int value, out _) ? set(value) : Task.CompletedTask);

    private McpEntry EntryFor(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer) => action switch
    {
        HotkeyAction.FCUSetHeading => NumberEntry("MCP Heading", "heading", "0-360 (360 is north)", Bbs757Mcp.TryHeadingInput,
            v => SetMcpHeadingAsync(v, sim, announcer)),
        HotkeyAction.FCUSetSpeed => new("MCP Speed", "speed", "100-399 knots, or Mach 0.40-0.86",
            input => Bbs757Mach.TryParse(input, out _, out string error) ? null : error,
            input => Bbs757Mach.TryParse(input, out var entry, out _) ? SetMcpSpeedAsync(entry, sim, announcer) : Task.CompletedTask),
        HotkeyAction.FCUSetAltitude => NumberEntry("MCP Altitude", "altitude", "100-49000 feet, rounded to 100", Bbs757Mcp.TryAltitudeInput,
            v => SetMcpAltitudeAsync(v, sim, announcer)),
        HotkeyAction.FCUSetVS => NumberEntry("MCP Vertical Speed", "vertical speed", "-8000 to 6000 feet per minute, rounded to 100",
            Bbs757Mcp.TryVerticalSpeedInput, v => SetMcpVerticalSpeedAsync(v, sim, announcer)),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "not an MCP value window"),
    };

    /// <summary>
    /// Opens the modeless value window for <paramref name="action"/>, the HS787's way (the caller
    /// has left input hotkey mode first): Enter sets the value and the window stays open for the next.
    /// </summary>
    private void ShowMcpDialog(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        if (_mcpDialogs.TryGetValue(action, out var open) && !open.IsDisposed)
        {
            open.Activate();
            return;
        }

        var entry = EntryFor(action, sim, announcer);
        var dialog = new ValueInputForm(entry.Title, entry.Parameter, entry.Range, announcer,
            input => entry.Error(input) is { } error ? (false, error) : (true, ""),
            McpWindowButtons(action, sim, announcer),
            input => _ = entry.Apply(input),
            McpInputEnabled(action, sim));

        dialog.ShowCancelButton = false;
        dialog.FormClosed += (_, _) =>
        {
            if (_mcpDialogs.TryGetValue(action, out var current) && ReferenceEquals(current, dialog))
                _mcpDialogs.Remove(action);
        };
        _mcpDialogs[action] = dialog;
        dialog.Show(parentForm);
    }

    /// <summary>
    /// The mode buttons of one value window (<see cref="Bbs757McpWindowLayout"/>), each with its map
    /// label (or the window's own caption, "Speed Intervene") and its fixed access key, captioned
    /// "LNAV: On" from its cached MCP light, Speed Intervene from the speed window's blank flag ("Window
    /// open" or "Window blank"), or "HDG SEL" alone for a button with neither (the window refreshes the
    /// captions). A press is the panel's (<see cref="PressFromPanelAsync"/>): it reads the light back
    /// once ("LNAV on") or says "Speed Intervene pressed", or refuses aloud when nothing can land, by the
    /// window's caption. The window's own read-out after a press is suppressed, or the press would be
    /// said twice (CORE-7, ruling R34). A node missing from the map is left out with a warning.
    /// </summary>
    internal List<ToggleButtonDef> McpWindowButtons(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var controls = new List<Bbs757Control>();
        foreach (string node in Bbs757McpWindowLayout.NodesFor(action))
        {
            if (_byNode.TryGetValue(node, out var control)) controls.Add(WithWindowCaption(control));
            else Log.Warn("BBS757", $"MCP window: {node} is not in the control map; its button is left out.");
        }

        var io = WriteIoFor(sim);
        var captions = Bbs757McpWindowLayout.Captions(controls.Select(c => c.Node).ToList(),
            node => controls.First(c => c.Node == node).Label);
        var buttons = new List<ToggleButtonDef>();
        for (int i = 0; i < controls.Count; i++)
        {
            var control = controls[i];
            Func<string> state = Bbs757McpLights.ForNode(control.Node) is { } light
                ? () => Bbs757McpLights.State(io.Cached(light.Key))
                : control.Node == Bbs757McpWindowLayout.SpeedInterveneNode
                    ? () => SpeedInterveneState(io.Cached(Bbs757Mcp.IasBlankedVar))
                    : () => "";
            buttons.Add(new ToggleButtonDef(captions[i], state,
                () => _ = PressFromPanelAsync(control, io, announcer))   // never throws: it catches and logs
            {
                SuppressStateAnnounce = () => true,
            });
        }
        return buttons;
    }

    /// <summary>
    /// Whether a value window's box takes a value: the speed box is closed while VNAV blanks the window
    /// (cached <see cref="Bbs757Mcp.IasBlankedVar"/> 1, ruling R58) and opens again when Speed Intervene
    /// opens the window; nothing delivered yet keeps it open. Null for the other windows, which never close.
    /// The window re-checks it on its own refresh; Shift+S and the Speed Intervene caption say why it is
    /// closed, since <see cref="ValueInputForm"/>'s own words are generic.
    /// </summary>
    internal Func<bool>? McpInputEnabled(HotkeyAction action, SimConnectManager sim)
    {
        if (action != HotkeyAction.FCUSetSpeed) return null;
        var io = WriteIoFor(sim);
        return () => io.Cached(Bbs757Mcp.IasBlankedVar) is not >= 0.5;
    }

    /// <summary>Speed Intervene's caption state: the speed window open (0) or blank in VNAV (1); nothing before the first reading.</summary>
    private static string SpeedInterveneState(double? blanked) => blanked switch
    {
        null => "",
        >= 0.5 => "Window blank",
        _ => "Window open",
    };

    /// <summary>
    /// The control with the window's caption as its label, so its press and refusal say the window's
    /// name ("Speed Intervene pressed") rather than the map's; the map's own control is never changed. A
    /// node with no override is returned as it is.
    /// </summary>
    private static Bbs757Control WithWindowCaption(Bbs757Control control)
    {
        if (Bbs757McpWindowLayout.CaptionOverride(control.Node) is not { } caption) return control;
        // A copy through the map's own JSON shape, so a field the generator adds later is never dropped.
        var copy = JsonSerializer.Deserialize<Bbs757Control>(JsonSerializer.Serialize(control))!;
        copy.Label = caption;
        return copy;
    }

    /// <summary>A profile switch closes the windows: their callbacks write through this definition, which no longer may.</summary>
    private void CloseMcpDialogs()
    {
        foreach (var dialog in _mcpDialogs.Values.ToList())
        {
            try { if (!dialog.IsDisposed) dialog.Close(); }
            catch (Exception ex) { Log.Debug("BBS757", $"MCP window did not close: {ex.Message}"); }
        }
        _mcpDialogs.Clear();
    }

    // =================================================================================
    // Sets
    // =================================================================================

    internal Task SetMcpHeadingAsync(int degrees, SimConnectManager sim, ScreenReaderAnnouncer announcer)
        => RunMcpSetAsync(Bbs757Mcp.HeadingVar, degrees, ignoreSameValueInFlight: false, Bbs757Mcp.HeadingPhrase(null), sim, announcer,
            (io, ct) => Bbs757Mcp.SetHeadingAsync(io, degrees, ct));

    /// <summary>
    /// A speed set is the only slow one (steps, settle, corrections: seconds), and Enter pressed again
    /// on the same speed would cancel it and start the closed loop over; the one in flight is
    /// already going there and will speak, so the repeat is ignored. A different speed supersedes.
    /// </summary>
    internal Task SetMcpSpeedAsync(int knots, SimConnectManager sim, ScreenReaderAnnouncer announcer)
        => SetMcpSpeedAsync(new Bbs757SpeedEntry(knots, null), sim, announcer);

    /// <summary>
    /// A typed speed, knots or Mach (<see cref="Bbs757Mach.TryParse"/>). The window is first made to show
    /// the entry's units (<see cref="Bbs757Mach.EnsureUnitsAsync"/>: IAS/MACH pressed silently when it shows
    /// the other, refused aloud when that does not take), then the knots or the Mach are set in BlueBird's
    /// knob tiers. Both are one set of the speed window: a newer one supersedes; a repeat of the same entry
    /// in flight is ignored (a Mach is told apart from a knots value by an offset no knots value reaches).
    /// </summary>
    internal Task SetMcpSpeedAsync(Bbs757SpeedEntry entry, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        int? hundredths = entry.Hundredths;
        int inFlightValue = hundredths is int h ? MachInFlightOffset + h : entry.Knots ?? 0;
        string refusal = hundredths != null ? Bbs757Mach.Phrase(null) : Bbs757Mcp.SpeedPhrase(null);
        return RunMcpSetAsync(Bbs757Mcp.SpeedVar, inFlightValue, ignoreSameValueInFlight: true, refusal, sim, announcer,
            async (io, ct) =>
            {
                if (ChangeoverPress() is not { } changeover) return Bbs757Mach.ChangeoverRefusal;
                string? refused = await Bbs757Mach.EnsureUnitsAsync(io, wantMach: hundredths != null, changeover,
                    () => OnOwnWrite(Bbs757McpWindowLayout.ChangeoverNode), ct);
                if (refused != null) return refused;
                return await SpacedAsync(io, timed => hundredths is int target
                    ? Bbs757Mach.SetMachAsync(timed, target, ct)
                    : Bbs757Mcp.SetSpeedAsync(timed, entry.Knots ?? 0, ct), ct);
            });
    }

    /// <summary>Added to a Mach set's hundredths for <see cref="RunMcpSetAsync"/>'s in-flight compare: above any knots value.</summary>
    private const int MachInFlightOffset = 10_000;

    /// <summary>The IAS/MACH button's press from the map, or null (logged) when the map lacks it.</summary>
    private Bbs757Press? ChangeoverPress()
    {
        if (_byNode.TryGetValue(Bbs757McpWindowLayout.ChangeoverNode, out var control) && Bbs757Write.PressFor(control) is { } press)
            return press;
        Log.Warn("BBS757", $"{Bbs757McpWindowLayout.ChangeoverNode} is not a pressable control in the map; the speed window cannot change units.");
        return null;
    }

    /// <summary>When the last speed burst went out, on <see cref="NowMs"/>, whichever set sent it; null before the first.</summary>
    private long? _lastSpeedBurstMs;

    /// <summary>
    /// A speed or Mach set kept out of the burst before it. BlueBird counts one burst across writes less
    /// than about 0.3 s apart (<see cref="Bbs757Mcp"/>), and a set superseded just after its burst leaves
    /// that burst counting: a newer set's first write inside <see cref="Bbs757Mcp.SpeedBurstGapMs"/> of it
    /// would move 2 or 5 an event. So the rest of that gap is waited out first, before the target is read,
    /// which also lets the last burst land; and every speed write of this set is timed for the next one.
    /// </summary>
    private async Task<string> SpacedAsync(McpIo io, Func<McpIo, Task<string>> set, CancellationToken ct)
    {
        if (_lastSpeedBurstMs is long last)
        {
            long wait = Math.Clamp(Bbs757Mcp.SpeedBurstGapMs - (NowMs() - last), 0, Bbs757Mcp.SpeedBurstGapMs);
            if (wait > 0) await io.Delay((int)wait, ct);
        }
        var timed = new McpIo
        {
            Read = io.Read,
            Delay = io.Delay,
            Write = rpn =>
            {
                io.Write(rpn);
                _lastSpeedBurstMs = NowMs();
            },
        };
        return await set(timed);
    }

    internal Task SetMcpAltitudeAsync(int feet, SimConnectManager sim, ScreenReaderAnnouncer announcer)
        => RunMcpSetAsync(Bbs757Mcp.AltitudeVar, feet, ignoreSameValueInFlight: false, Bbs757Mcp.AltitudePhrase(null), sim, announcer,
            (io, ct) => Bbs757Mcp.SetAltitudeAsync(io, feet, ct));

    internal Task SetMcpVerticalSpeedAsync(int fpm, SimConnectManager sim, ScreenReaderAnnouncer announcer)
        => RunMcpSetAsync(Bbs757Mcp.VsVar, fpm, ignoreSameValueInFlight: false, Bbs757Mcp.VsPhrase(null), sim, announcer,
            (io, ct) => Bbs757Mcp.SetVerticalSpeedAsync(io, fpm, ct));

    /// <summary>
    /// One set: refuse aloud when nothing can land ("{value} unavailable", nothing written), else run
    /// the loop and speak its result unless a newer set, a flight load or a profile switch has
    /// superseded it. A repeat of the value already being set is ignored when
    /// <paramref name="ignoreSameValueInFlight"/>; a different value supersedes the one in flight.
    /// Whatever is spoken also goes to <paramref name="onSaid"/> (the altimeter window's status box).
    /// Never throws: it is fire-and-forget from a window callback.
    /// </summary>
    internal async Task RunMcpSetAsync(string key, int value, bool ignoreSameValueInFlight, string refusal,
        SimConnectManager sim, ScreenReaderAnnouncer announcer, Func<McpIo, CancellationToken, Task<string>> set,
        Action<string>? onSaid = null)
    {
        void Say(string text)
        {
            announcer.AnnounceImmediate(text);
            onSaid?.Invoke(text);
        }

        if (_lifetime.IsCancellationRequested) return;   // switched away: nothing is written
        if (_mcpSets.TryGetValue(key, out var inFlight))
        {
            if (ignoreSameValueInFlight && inFlight.Value == value && !inFlight.Cts.IsCancellationRequested)
            {
                Log.Debug("BBS757", $"{key}: {value} is already being set; the repeat is ignored.");
                return;
            }
            try { inFlight.Cts.Cancel(); } catch (ObjectDisposedException) { /* it finished meanwhile */ }
        }
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var mine = new McpSetInFlight(cts, value);
        _mcpSets[key] = mine;
        var ct = cts.Token;

        try
        {
            if (!CanWrite(sim))
            {
                Log.Debug("BBS757", $"{key}: write refused, nothing sent.");
                Say(refusal);
                return;
            }

            string said = await set(McpIoFor(sim), ct);
            ct.ThrowIfCancellationRequested();
            Say(said);
        }
        catch (OperationCanceledException) { /* superseded, reloaded, or switched away: nothing to say */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{key}: set failed: {ex.Message}");
            Say(refusal);   // whatever was sent, the pilot is not left waiting for a value
        }
        finally
        {
            if (_mcpSets.TryRemove(new KeyValuePair<string, McpSetInFlight>(key, mine))) ArmOwnSetEcho(key);
            cts.Dispose();
        }
    }

    /// <summary>Cancels every set in flight: one armed before a flight load must not write into the flight that follows.</summary>
    private void CancelMcpSets()
    {
        foreach (var inFlight in _mcpSets.Values)
        {
            try { inFlight.Cts.Cancel(); } catch (ObjectDisposedException) { /* it finished meanwhile */ }
        }
    }

    // =================================================================================
    // Readouts
    // =================================================================================

    /// <summary>
    /// Shift+H / S / A / V: reads the MCP's own variable fresh and speaks it. The simulator's
    /// autopilot values are never read; they sit at unrelated offsets from BlueBird's MCP.
    /// </summary>
    internal async Task ReadMcpAsync(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;
            var ct = _lifetime.Token;
            var io = McpIoFor(sim);

            string said = action switch
            {
                HotkeyAction.ReadHeading => await Bbs757Mcp.ReadHeadingAsync(io, ct),
                HotkeyAction.ReadSpeed => await Bbs757Mcp.ReadSpeedAsync(io, ct),
                HotkeyAction.ReadAltitude => await Bbs757Mcp.ReadAltitudeAsync(io, ct),
                HotkeyAction.ReadFCUVerticalSpeedFPA => await Bbs757Mcp.ReadVerticalSpeedAsync(io, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, "not an MCP readout"),
            };
            ct.ThrowIfCancellationRequested();
            announcer.AnnounceImmediate(said);
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{action}: read failed: {ex.Message}");
            announcer.AnnounceImmediate(UnavailablePhrase(action));   // a readout never goes silent
        }
    }

    private static string UnavailablePhrase(HotkeyAction action) => action switch
    {
        HotkeyAction.ReadHeading => Bbs757Mcp.HeadingPhrase(null),
        HotkeyAction.ReadSpeed => Bbs757Mcp.SpeedPhrase(null),
        HotkeyAction.ReadAltitude => Bbs757Mcp.AltitudePhrase(null),
        _ => Bbs757Mcp.VsPhrase(null),
    };
}
