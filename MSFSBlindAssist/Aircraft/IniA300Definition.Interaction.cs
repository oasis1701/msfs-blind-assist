using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Writes: every panel row is claimed here, so MainForm's generic paths (SetLVar's data-definition
/// write, "pressed" announcements) never touch an A300 control. Nothing the pilot does on a panel
/// is spoken back; only errors are.
/// </summary>
public partial class IniA300Definition
{
    private readonly A300CommandedState _commanded = new();

    /// <summary>Steps still owed after a wait that has not finished (a hold button's release, a
    /// spring switch's return), keyed by run, so <see cref="Dispose"/> can let go first.</summary>
    private readonly Dictionary<int, IReadOnlyList<A300Step>> _owed = new();
    private int _nextRunId;

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (!_rows.TryGetValue(varKey, out var row))
            return false;   // a base variable: MainForm's generic path
        _sim = simConnect;

        if (row.Action == A300RowAction.Custom)
        {
            HandleLeverSet(row, value, simConnect, announcer);
            return true;
        }
        if (row.Action == A300RowAction.Typed)
        {
            SetTyped(row.Key, value, simConnect, announcer, row.Name);
            return true;
        }

        var control = row.Control!;
        var plan = row.Action switch
        {
            A300RowAction.Set => A300WritePlan.ForSet(control, value, CurrentValue(row.Key, simConnect)),
            A300RowAction.Press => A300WritePlan.ForPress(control),
            A300RowAction.Increase => A300WritePlan.ForStep(control, increase: true),
            A300RowAction.Decrease => A300WritePlan.ForStep(control, increase: false),
            _ => A300Plan.Refused(A300WritePlan.NotSettableRefusal),
        };
        // A spring switch returns to its rest, so the position to remember is the rest.
        double? commanded = row.Action != A300RowAction.Set ? null
            : control.Kind == A300Kinds.Spring ? control.StateForPosition(control.Rest ?? 1)
            : value;
        bool sent = Execute(row, plan, simConnect, announcer, commanded);
        if (sent && commanded is double target && control.Kind is A300Kinds.Toggle or A300Kinds.Command or A300Kinds.Selector)
            _ = CheckMovedAsync(row, target, simConnect, announcer, ++_pickSeq);
        if (sent && row.Action == A300RowAction.Press && A300PanelLamps.ByButton.TryGetValue(row.Key, out var ownLamp))
            _pressedLampUntil[ownLamp] = Clock() + PressedLampMs;
        // An autobrake button's label says its own armed segment ([A300-20]), so that light is left to it; its DECEL
        // light and another level's armed light going out are not the press's own, and still speak.
        if (sent && row.Action == A300RowAction.Press && A300Autobrake.ByButton.TryGetValue(row.Key, out var autobrake))
            _pressedLampUntil[autobrake.ArmedLampId] = Clock() + PressedLampMs;
        if (sent && control.Kind == A300Kinds.Spring && row.Action == A300RowAction.Set)
            _ = ReReadAfterSpringAsync(row, value, simConnect, announcer, ++_pickSeq);

        // An FCU knob step is read back once it lands ("Heading 271"): a numeric confirmation, as a
        // typed value's is. The altitude window's own call-out is told it is an echo.
        if (sent && row.Action is A300RowAction.Increase or A300RowAction.Decrease
            && A300FcuWindows.ReadoutByKnob.TryGetValue(control.Key, out var readoutKey))
        {
            if (readoutKey == A300Readouts.AltitudeKey)
                _altitudeWindow.SuppressEcho(Clock());
            _ = ReadBackAsync(simConnect, announcer, readoutKey, v => A300FcuWindows.Phrase(readoutKey, v, IsMach()));
        }
        // A flex temperature knob step, the same way ("Flex temperature 46 degrees", [A300-21]).
        if (sent && row.Action is A300RowAction.Increase or A300RowAction.Decrease && control.Key == A300Trp.FlexKnobKey)
            _ = ReadBackAsync(simConnect, announcer, A300Readouts.FlexTemperatureKey, A300Trp.FlexPhrase);
        // Any other knob that reads back ("Landing elevation 50 feet").
        if (sent && row.Action is A300RowAction.Increase or A300RowAction.Decrease
            && A300Readouts.KnobReadBacks.TryGetValue(control.Key, out var readBack))
            _ = ReadBackAsync(simConnect, announcer, readBack.Readout, readBack.Phrase);
        // A VHF or ADF knob's window, named by its role now ("VHF 1 standby 124.805"), and a transfer's new frequency
        // in use: the transfer switch picks a window, it does not swap them (A300Radios).
        if (sent && row.Action is A300RowAction.Increase or A300RowAction.Decrease
            && A300Radios.ByKnob.TryGetValue(control.Key, out var knob))
        {
            string windowKey = knob.Window == 1 ? knob.Radio.Window1Key : knob.Radio.Window2Key;
            _ = ReadBackWhenSettledAsync(simConnect, announcer, knob.Radio.Name,
                v => knob.Radio.KnobPhrase(knob.Window, v[0], v[1]), windowKey, knob.Radio.TransferKey);
        }
        if (sent && row.Action == A300RowAction.Press && A300Radios.ByTransfer.TryGetValue(control.Key, out var radio))
            _ = ReadBackWhenSettledAsync(simConnect, announcer, radio.Name,
                v => radio.TransferPhrase(v[0], v[1], v[2]), radio.Window1Key, radio.Window2Key, radio.TransferKey);
        return true;
    }

    /// <summary>How long a pressed button's own light is left to its label: the screen reader reads the
    /// focused button's new label ("Pressurization system 2: On"), so speaking the light too would repeat it
    /// ([CORE-7]). Long enough for the 1 Hz light delivery and the gather window.</summary>
    public const long PressedLampMs = 3000;

    private readonly Dictionary<string, long> _pressedLampUntil = new(StringComparer.Ordinal);

    /// <summary>Whether this light is a just-pressed button's own, which its label already says.</summary>
    private bool IsPressedButtonsLamp(string lampKey) =>
        _pressedLampUntil.TryGetValue(lampKey, out var until) && Clock() <= until;

    /// <summary>Each row's latest pick, its target, and whether a delivery has reached it since.</summary>
    private readonly Dictionary<string, (int Pick, double Target, bool Reached)> _pendingPicks = new(StringComparer.Ordinal);
    private int _pickSeq;

    /// <summary>A delivery that reaches a pending pick's target: the switch moved, whatever it does next.</summary>
    private void NotePickReached(string key, double value)
    {
        if (_pendingPicks.TryGetValue(key, out var p) && !p.Reached && Math.Abs(value - p.Target) < A300WritePlan.SameValueTolerance)
            _pendingPicks[key] = p with { Reached = true };
    }

    /// <summary>
    /// Reads a picked switch back once it has had time to move. When the aircraft ignored the write (the
    /// crossbleed button in auto mode), the combo would keep the pick while the switch stayed put, and the
    /// next pick would be planned from it: so the row is read again, the stayed position is remembered, and
    /// it is said once, as the MD-11 and the iFly 737 say it ("Crossbleed did not move"), what the screen
    /// reader cannot say ([CORE-7]); the combo going back shows where it is. A switch that
    /// reached the pick and then dropped by itself (a SAS lever) moved: its drop is the aircraft's, spoken as such.
    /// A reading that is none of the switch's positions (a valve part way across) is neither: nothing is said
    /// or remembered.
    /// </summary>
    private async Task CheckMovedAsync(A300PlacedRow row, double target, SimConnectManager sim,
        ScreenReaderAnnouncer announcer, int pick)
    {
        _pendingPicks[row.Key] = (pick, target, false);
        bool Superseded() => !_pendingPicks.TryGetValue(row.Key, out var p) || p.Pick != pick || p.Reached;
        try
        {
            await TypedDelay(ToggleReadBackMs);
            if (_disposed || Superseded())
                return;
            // _disposed again after the read: an aircraft switch disposes this definition before the pending reads
            // are failed, and a delivery can land in between; that value belongs to the old aircraft.
            if (await ReadFresh(sim, row.Key, ReadoutTimeoutMs) is not double now
                || _disposed
                || Superseded()
                || row.Control!.PositionForState(now) is null
                || Math.Abs(now - target) < A300WritePlan.SameValueTolerance)
                return;
            _commanded.Record(CommandKey(row.Key), now, Clock());
            ReRead(row.Key, sim);
            announcer.Announce($"{row.Name} did not move");
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"Read-back of {row.Key} failed: {ex.Message}");
        }
        finally
        {
            if (_pendingPicks.TryGetValue(row.Key, out var p) && p.Pick == pick)
                _pendingPicks.Remove(row.Key);
        }
    }

    /// <summary>A spring switch's hold can fall between two 1 Hz deliveries, which then see no change, so its
    /// combo kept the side picked after the switch had returned (the rudder trim, 2026-10-10): read it again
    /// once the hold is over, so it goes back to its rest. The cargo door then says where it stopped, unless a
    /// later pick of its switch took over.</summary>
    private async Task ReReadAfterSpringAsync(A300PlacedRow row, double picked, SimConnectManager sim,
        ScreenReaderAnnouncer announcer, int pick)
    {
        var control = row.Control!;
        _latestSpringPick[row.Key] = pick;
        try
        {
            bool atRest = control.PositionForState(picked) is double position
                && Math.Abs(position - (control.Rest ?? 1)) < A300WritePlan.SameValueTolerance;
            await Delay((atRest ? 0 : A300WritePlan.SpringHoldMsFor(control)) + SpringSettleMs);
            if (_disposed || _latestSpringPick.GetValueOrDefault(row.Key) != pick)
                return;
            ReRead(row.Key, sim);
            if (row.Key == A300CargoDoor.SwitchKey)
                await ReadBackWhenSettledAsync(sim, announcer, "Cargo door",
                    v => $"Cargo door {A300CargoDoor.Status(v[0], v[1], v[2], v[3], v[4])}",
                    A300CargoDoor.Flags.Select(f => f.Key).Prepend(A300CargoDoor.StatusKey).ToArray());
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"Re-read of {row.Key} failed: {ex.Message}");
        }
    }

    /// <summary>Each spring row's latest pick: an earlier pick's re-read and read-back are dropped.</summary>
    private readonly Dictionary<string, int> _latestSpringPick = new(StringComparer.Ordinal);

    /// <summary>Speaks a read-back composed from several values once the change has had time to land. The values
    /// are read together: one after another, a radio's read-back came 3 s after the press (2026-10-10).</summary>
    private async Task ReadBackWhenSettledAsync(SimConnectManager sim, ScreenReaderAnnouncer announcer, string what,
        Func<double[], string> compose, params string[] keys)
    {
        try
        {
            await TypedDelay(ToggleReadBackMs);
            if (_disposed)
                return;
            if (await ReadAllFreshAsync(sim, keys) is not { } values)
                return;
            announcer.AnnounceImmediate(values.All(v => v.HasValue)
                ? compose(values.Select(v => v!.Value).ToArray())
                : $"{what} unavailable");
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"{what} read-back failed: {ex.Message}");
        }
    }

    /// <summary>After a spring's hold, the time its return takes to land.</summary>
    public const int SpringSettleMs = 500;

    /// <summary>The value to plan from: what MSFSBA just commanded while it is fresh, else the cache.</summary>
    private double? CurrentValue(string key, SimConnectManager sim) =>
        _commanded.Resolve(CommandKey(key), Cached(sim, key), Clock());

    /// <summary>
    /// What a row's commanded position is kept under: its state variable, so the rows that flip one variable
    /// through their own Set events (the courier and crew oxygen supply, the dome lights, terrain mode, the
    /// autoland tests) share one hold. A hold kept per row left the second row planning from its own stale
    /// cache, and its Set, which FLIPS, put the first row's pick back. A row with no state variable is kept
    /// under its own key.
    /// </summary>
    private string CommandKey(string rowKey) =>
        _rows.TryGetValue(rowKey, out var row) && row.Control?.StateVar is { Length: > 0 } stateVar ? stateVar : rowKey;

    /// <summary>Runs a plan, or refuses it aloud; true when its steps were sent.</summary>
    private bool Execute(A300PlacedRow row, A300Plan plan, SimConnectManager sim, ScreenReaderAnnouncer announcer,
        double? commandedValue)
    {
        if (plan.Refusal != null)
        {
            announcer.Announce(A300WritePlan.SpokenRefusal(row.Name, plan.Refusal));
            ReRead(row.Key, sim);
            return false;
        }
        if (plan.IsEmpty)
            return false;   // already there; the screen reader has said so
        if (!CanLand(sim))
        {
            announcer.Announce($"{row.Name} unavailable");
            ReRead(row.Key, sim);
            return false;
        }
        if (commandedValue is double target)
            _commanded.Record(CommandKey(row.Key), target, Clock());
        // The SPD/MACH button is a press, but it FLIPS the mode: a typed speed planned from the cache right after
        // would flip it back and write the number in the wrong unit. A mode that was never read is not guessed.
        if (row.Key == A300FcuState.SpeedMachButtonKey && SpeedMode() is double mode)
            _commanded.Record(SpeedModeCommand, mode >= 0.5 ? 0 : 1, Clock());
        _ = RunAsync(row.Key, plan.Steps, sim);
        return true;
    }

    /// <summary>Each row's latest run: an earlier run of the same row stops at its next wait.</summary>
    private readonly Dictionary<string, int> _latestRunByRow = new(StringComparer.Ordinal);

    /// <summary>
    /// Runs a plan's steps on the UI thread (each await resumes there, so SimConnect is never used
    /// from a pool thread). The steps after a wait are recorded as owed until they have run. A later
    /// pick of the same row takes over: the earlier run's steps after its wait are dropped, so a spring's
    /// old release cannot cut a new hold short (the cargo door, picked Open again within its 35 s hold).
    /// </summary>
    private async Task RunAsync(string rowKey, IReadOnlyList<A300Step> steps, SimConnectManager sim)
    {
        int run = ++_nextRunId;
        _latestRunByRow[rowKey] = run;
        try
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (_disposed)
                    return;
                switch (steps[i])
                {
                    case A300CalcStep calc:
                        Send(sim, calc.Rpn);
                        break;
                    case A300DelayStep delay:
                        _owed[run] = steps.Skip(i + 1).ToList();
                        await Delay(delay.Milliseconds);
                        _owed.Remove(run);
                        if (_latestRunByRow.GetValueOrDefault(rowKey) != run)
                            return;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"A control write failed: {ex.Message}");
        }
        finally
        {
            _owed.Remove(run);
        }
    }

    /// <summary>Sends every owed step at once (the aircraft is switched away or the app is closing).</summary>
    private void ReleaseOwedSteps()
    {
        var sim = _sim;
        if (sim == null)
            return;
        foreach (var steps in _owed.Values.ToList())
            foreach (var step in steps)
                if (step is A300CalcStep calc)
                    Send(sim, calc.Rpn);
        _owed.Clear();
    }

    /// <summary>The three hand-written levers: refused aloud when the write cannot land, and the
    /// combo or slider put back.</summary>
    private void HandleLeverSet(A300PlacedRow row, double value, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        string rpn = row.Key switch
        {
            A300Levers.FlapsKey => A300Levers.FlapsRpn((int)Math.Round(value)),
            A300Levers.SpoilersArmKey => A300Levers.SpoilersArmRpn(value),
            _ => A300Levers.SpeedBrakeRpn(value),
        };
        if (!CanLand(sim))
        {
            announcer.Announce($"{row.Name} unavailable");
            ReRead(row.Key, sim);
            return;
        }
        Send(sim, rpn);
    }

    // =================================================================================
    // Hotkeys
    // =================================================================================

    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        _sim = simConnect;
        if (action == HotkeyAction.MonitorManager)
        {
            hotkeyManager.ExitOutputHotkeyMode();
            (parentForm as MainForm)?.ShowA300MonitorManagerDialog();
            return true;
        }
        if (TryHandleAutoflightHotkey(action, simConnect, announcer, parentForm, hotkeyManager))
            return true;
        if (TryHandleDisplayHotkey(action, simConnect, announcer))
            return true;
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }
}
