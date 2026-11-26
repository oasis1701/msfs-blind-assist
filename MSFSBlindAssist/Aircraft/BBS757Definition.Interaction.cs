using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Panel writes, the BBS Nest warning and the hotkeys.
///
/// Speech follows CORE-7: a toggle or switch pick says nothing of its own (the screen reader read
/// the combo, and the re-read after the write moves the combo to where the control really is); a
/// button press, which has no readable state, is confirmed once with "{label} pressed"; a write
/// that cannot be made says "{label} unavailable" and sends nothing; a guarded pick that does not
/// land says "{label} did not move" once (BBS757Definition.Overhead.cs).
///
/// Every write runs on the UI thread: MainForm's panel handlers call in there, and the awaits below
/// keep its context (no ConfigureAwait(false)), so the calculator calls, the re-read and the speech
/// all go out from the UI thread. Nothing here blocks it: the waits are awaited, never slept.
/// </summary>
public partial class BBS757Definition
{
    /// <summary>Spoken once per connection and flight load when BBS Nest is not running.</summary>
    public const string NestMissingWarning =
        "BBS Nest is not running. The 757's systems will not respond until it runs and the flight is reloaded.";

    /// <summary>
    /// How long a positional pick waits before it acts. Arrowing through a closed combo commits each
    /// row it passes; only the row it settles on is written, and only it is refused aloud (the
    /// MD-11's debounce, for the same reason: concurrent writes to one control step it twice).
    /// </summary>
    private const int PickSettleMs = 300;

    /// <summary>The ceiling on the fresh read a positional write starts from (an individual def answers within a frame or two).</summary>
    private const int FreshReadTimeoutMs = 1500;

    /// <summary>
    /// How long after a write the control is re-read, so its combo shows the aircraft's own state:
    /// the cockpit model takes a click at once, but a write it refused shows only once read back.
    /// </summary>
    private const int ReadBackDelayMs = 1500;

    /// <summary>BlueBird's PARKING BRAKE MODE: true REALISTIC, false EASY, null unknown (tests: a fixed answer).</summary>
    internal Func<bool?> ParkingBrakeRealistic { get; init; } = Bbs757ParkingBrake.ReadRealisticMode;

    /// <summary>
    /// Canceled (and disposed) when the pilot switches to another profile: no pending pick or press
    /// may write after that. Every user checks <c>IsCancellationRequested</c> before it touches the
    /// token, which is safe on a disposed source.
    /// </summary>
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>
    /// The batch-covered variable whose batch delivery runs the BBS Nest check while it is pending
    /// (<see cref="DeferredFlushWatchVariable"/>). A base variable, so it is registered on every
    /// profile and rides the 1 Hz continuous batch, which is delivered every second whether or not a
    /// value changed: a quiet cold-and-dark cockpit still gets its check within about a second.
    /// </summary>
    internal const string NestCheckWatchKey = "SIM_ON_GROUND";

    /// <summary>The pick in flight per node; a newer pick of the same control cancels it.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _pendingPicks =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Counts the flight loads and dropped connections that dropped every pick in flight
    /// (<see cref="CancelPendingPicks"/>). A guarded sequence that has sent its first call keeps going
    /// when a newer pick of its control cancels it, so its own token cannot say it was dropped: it
    /// captures this count at the start and sends nothing more when it has changed
    /// (<see cref="SetGuardedAsync"/>).
    /// </summary>
    private int _pickEpoch;

    /// <summary>Whether BBS Nest is running, asked once per connection; replaced by tests.</summary>
    internal Func<bool> NestRunning { get; set; } = () => Bbs757Nest.IsRunning();

    // Reset by OnSimContextReset and fresh on construction, so the check runs once per connection
    // and per flight load. Null: not asked yet.
    private bool? _nestRunning;
    private bool _nestSettled;

    // =================================================================================
    // Writes
    // =================================================================================

    /// <summary>
    /// Every map control is written here, and always answered "handled", refusals included, so the
    /// generic path never sends its own L:var write. Any other key goes to the base.
    /// </summary>
    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // The gap plans' own fields and controls (BBS757Definition.Hooks.cs).
        bool handled = false;
        HandleRadioSet(varKey, value, simConnect, announcer, ref handled);                   // plan 04
        if (!handled) HandleOverheadSet(varKey, value, simConnect, announcer, ref handled);   // plan 03, then 12
        if (!handled) HandleThrustSet(varKey, value, simConnect, announcer, ref handled);     // plan 15
        if (handled)
        {
            Sim = simConnect;
            return true;
        }

        // The squawk box is set from its text (HandleUITypedText), which MainForm offers first and which
        // always claims it. Claimed here too, sending nothing, so the generic path never fires its
        // registration's XPNDR_SET with a decimal value.
        if (varKey == Bbs757Squawk.SetKey) return true;
        if (!_byNode.TryGetValue(varKey, out var control))
            return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
        Sim = simConnect;
        var io = WriteIoFor(simConnect);

        // A guarded control (the emergency lights, a stab trim cutout) moves only with its guard open
        // (BBS757Definition.Overhead.cs). A guard the map does not list is a map error: nothing is
        // sent, and the combo goes back to where the aircraft is.
        if (!string.IsNullOrWhiteSpace(control.Guard))
        {
            if (_byNode.TryGetValue(control.Guard, out var guard))
            {
                _ = SetGuardedAsync(control, guard, value, io, announcer);   // never throws: it catches and logs
            }
            else
            {
                Log.Warn("BBS757", $"{control.Node}: its guard {control.Guard} is not in the control map; nothing sent.");
                SayUnavailable(control, announcer);
                io.RequestRead(control.Node);
            }
            return true;
        }

        if (Bbs757Write.IsMomentary(control))
        {
            _ = PressFromPanelAsync(control, io, announcer);   // never throws: it catches and logs
            return true;
        }

        _ = SetPositionAsync(control, value, io, announcer);
        return true;
    }

    /// <summary>
    /// A button pressed from a panel or from an MCP value window's button (BBS757Definition.Mcp.cs):
    /// one press per pilot action, so the refusal is judged here, once ("{label} unavailable", nothing
    /// sent); otherwise <see cref="PressAsync"/> with speech, which reads the light back once ("LNAV
    /// on") or says "{label} pressed". Never throws.
    /// </summary>
    private Task PressFromPanelAsync(Bbs757Control control, Bbs757SimIo io, ScreenReaderAnnouncer announcer)
    {
        var press = Bbs757Write.PressFor(control);
        if (press == null || !io.CanWrite())
        {
            SayUnavailable(control, announcer);
            return Task.CompletedTask;
        }
        return PressAsync(control, press, io, announcer, speak: true);
    }

    /// <summary>The panel writes' sim access, replaced by tests (a SimConnectManager cannot read in them); null: the real one.</summary>
    internal Func<SimConnectManager, Bbs757SimIo>? WriteIo { get; init; }

    private Bbs757SimIo WriteIoFor(SimConnectManager sim) => WriteIo?.Invoke(sim) ?? new Bbs757SimIo
    {
        CanWrite = () => CanWrite(sim),
        ReadFresh = (key, ct) => sim.ReadFreshAsync(key, FreshReadTimeoutMs, ct),
        Cached = sim.GetCachedVariableValue,
        Write = sim.ExecuteCalculatorCodeUnique,
        RequestRead = key => sim.RequestVariable(key, forceUpdate: true),
        Delay = Task.Delay,
    };

    private static bool SayUnavailable(Bbs757Control control, ScreenReaderAnnouncer announcer)
    {
        Log.Debug("BBS757", $"{control.Node}: write refused, nothing sent.");
        announcer.Announce($"{control.Label} unavailable");
        return true;
    }

    /// <summary>
    /// A button: press, hold, release, then confirm it once (CORE-7). A button that moves a state row
    /// (an MCP button's light, a speedbrake spot's lever: <see cref="LinkedStatusKey"/>) reads that row
    /// back after <see cref="ReadBackDelayMs"/> and says it ("CMD R on", "Speedbrake armed"); its
    /// change in between is this press's echo and is not announced. Any other button with no readable
    /// state says "{label} pressed". With <paramref name="speak"/> false (the autopilot window, whose
    /// button caption shows the new state) a light that changed says nothing, but one that reads back
    /// as it was before the press says so once ("CMD L did not engage", "LNAV did not disengage"): the
    /// caption did not change, so nothing else would. The echo is settled either way.
    /// The release is never canceled: a press is always let go, even across a profile switch.
    /// </summary>
    private async Task PressAsync(Bbs757Control control, Bbs757Press press, Bbs757SimIo io,
        ScreenReaderAnnouncer announcer, bool speak, string? label = null)
    {
        try
        {
            label ??= control.Label;   // the autopilot window passes its own caption ("A/P Disconnect")
            if (_lifetime.IsCancellationRequested) return;   // switched away: nothing is pressed
            // Taken once, before any await: a switch away disposes _lifetime, whose Token then throws.
            var ct = _lifetime.Token;
            string? linked = LinkedStatusKey(control.Node);
            // The state the window's caption shows now, to tell a press the aircraft refused.
            double? before = linked != null && !speak && Bbs757McpLights.ForKey(linked) != null ? io.Cached(linked) : null;
            if (linked != null) ArmOwnWriteEcho(linked);
            OnOwnWrite(control.Node);   // plan 13
            io.Write(press.Down);
            if (press.Up != null)
            {
                await io.Delay(press.HoldMs, CancellationToken.None);
                io.Write(press.Up);
            }
            RelockIfUnlock(control, io);   // a fire handle unlocked from the app locks again (BBS757Definition.Overhead.cs)
            if (_lifetime.IsCancellationRequested) return;
            if (linked == null && speak && !Bbs757Write.HasReadableState(control)) announcer.Announce($"{label} pressed");

            await io.Delay(ReadBackDelayMs, ct);
            io.RequestRead(control.Node);
            if (linked == null) return;

            double? state = await io.ReadFresh(linked, ct);
            ct.ThrowIfCancellationRequested();
            string phrase = LinkedPhrase(linked, state);
            if (state != null) SettleOwnWriteEcho(linked, phrase);
            if (speak) announcer.Announce(state != null ? phrase : $"{label} pressed");
            else if (Bbs757McpLights.RefusedPhrase(label, before, state) is { } refused) announcer.Announce(refused);
        }
        catch (OperationCanceledException) { /* switched away: nothing left to say or read */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.Node}: press failed: {ex.Message}");
        }
    }

    /// <summary>
    /// A toggle, guard, select or switch: settle, refuse aloud if nothing can land, read the control
    /// fresh, send what <see cref="Bbs757Write.ToPosition"/> plans, then re-read it so the combo shows
    /// where it really is. A newer pick of the same control supersedes this one at any await.
    ///
    /// While the pick is in flight, <see cref="ProcessSimVarUpdate"/> consumes every delivery of the
    /// control: the fresh read answers with the position from BEFORE the write, and MainForm's
    /// control refresh would otherwise set the focused combo back to it, which the screen reader
    /// reads out over the pilot's pick (the MD-11's lesson, [MD11-8]). The one re-read that brings
    /// the combo current goes out after the pick has left the pending set, so it reaches the combo:
    /// silent when the control landed on the pick (WinForms ignores a same-index set), the real
    /// position once when it did not or nothing could be written.
    ///
    /// True when it wrote and waited out the read-back delay; false when it refused, found the control
    /// already there, or was superseded (the autopilot window's A/T Arm reads its flag only after a write).
    /// </summary>
    private async Task<bool> SetPositionAsync(Bbs757Control control, double target, Bbs757SimIo io,
        ScreenReaderAnnouncer announcer)
    {
        var cts = BeginPick(control.Node);
        if (cts == null) return false;   // switched away: nothing is written
        var ct = cts.Token;
        bool resync = false, wrote = false;

        try
        {
            if (!await SettleAndCheckWriteAsync(control, io, announcer, ct))
            {
                resync = true;
                return false;
            }

            double? current = await io.ReadFresh(control.Node, ct);
            ct.ThrowIfCancellationRequested();

            var plan = Bbs757Write.ToPosition(control, current, target);
            switch (plan.Outcome)
            {
                case Bbs757WriteOutcome.AtPosition:
                    return false;
                case Bbs757WriteOutcome.Refused:
                    Log.Debug("BBS757", $"{control.Node}: read {current?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nothing"}, " +
                        $"target {target.ToString(System.Globalization.CultureInfo.InvariantCulture)}: no safe write.");
                    SayUnavailable(control, announcer);
                    resync = true;
                    return false;
            }

            // The parking brake's state row would announce this pick a second time ("Parking brake
            // set" after the screen reader read "Set"): its change is this write's echo, until the row
            // is read back below.
            string? linked = LinkedStatusKey(control.Node);
            if (linked != null) ArmOwnWriteEcho(linked);
            OnOwnWrite(control.Node);   // plan 13
            foreach (var call in plan.Calls) io.Write(call);

            await io.Delay(ReadBackDelayMs, ct);
            resync = true;
            wrote = true;
            // REALISTIC parking-brake mode: the lever alone does nothing; only then, retry with full braking.
            if (control.Node == Bbs757ParkingBrake.Node
                && Bbs757ParkingBrake.ShouldRetry(await io.ReadFresh(control.Node, ct), target > 0.5, ParkingBrakeRealistic()))
                await Bbs757ParkingBrake.RetryWithFullBrakingAsync(io, target > 0.5, ReadBackDelayMs, ct);
            if (linked != null)
            {
                // Read the row and close the echo window, as a press does: a change after this (the
                // brake snapping back to Set) is the aircraft's, and is spoken.
                double? state = await io.ReadFresh(linked, ct);
                if (state != null) SettleOwnWriteEcho(linked, LinkedPhrase(linked, state));
            }
        }
        catch (OperationCanceledException) { /* superseded by a newer pick, a reload, or switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.Node}: write failed: {ex.Message}");
            resync = true;   // whatever was or was not sent, the combo should show the aircraft's position
        }
        finally
        {
            if (EndPick(control.Node, cts) && resync)
            {
                io.RequestRead(control.Node);
                // Closing a guard writes CloseSets to the control it covers (BlueBird: the emergency lights
                // go to ARMED), and map controls are read only when their panel is built: read the covered
                // control again too, or its combo keeps the position from before the guard closed.
                foreach (var covered in _byNode.Values)
                {
                    if (string.Equals(covered.Guard, control.Node, StringComparison.Ordinal)) io.RequestRead(covered.Node);
                }
            }
        }
        return wrote;
    }

    /// <summary>
    /// Enters a pick of <paramref name="node"/> in the pending set (the set <see cref="IsPickPending"/>,
    /// <see cref="CancelPendingPicks"/> and <see cref="ProcessSimVarUpdate"/>'s delivery consumption
    /// read): a source linked to the profile's lifetime, after canceling any earlier pick of the node.
    /// The earlier pick ends at its next await that watches its token: every await of a positional pick
    /// does; a guarded sequence's do too, except its step gaps once its first call has gone, so it
    /// finishes its remaining calls and only its read-back stops (the newer pick speaks, see
    /// <see cref="SetGuardedAsync"/>). Null once the pilot has switched away from the profile: nothing
    /// may be written then. The pick ends with <see cref="EndPick"/>.
    /// </summary>
    private CancellationTokenSource? BeginPick(string node)
    {
        if (_lifetime.IsCancellationRequested) return null;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        if (_pendingPicks.TryGetValue(node, out var prior))
        {
            try { prior.Cancel(); } catch (ObjectDisposedException) { /* it finished meanwhile */ }
        }
        _pendingPicks[node] = cts;
        return cts;
    }

    /// <summary>
    /// Takes a pick out of the pending set and disposes its source. True only when the entry was still
    /// its own: a newer pick of the control has replaced it otherwise, and then the re-read is that
    /// pick's to make; a flight load has cleared it, and then there is nothing to re-read into.
    /// </summary>
    private bool EndPick(string node, CancellationTokenSource pick)
    {
        bool mine = _pendingPicks.TryRemove(new KeyValuePair<string, CancellationTokenSource>(node, pick));
        pick.Dispose();
        return mine;
    }

    /// <summary>
    /// The first two steps of every positional pick: wait out <see cref="PickSettleMs"/> (arrowing
    /// through a combo commits each row it passes; only the one it settles on is written), then ask
    /// whether a write can land at all. False when it cannot: "{label} unavailable" has been said and
    /// nothing may be sent, and the caller re-reads the control so its combo shows the aircraft's position.
    /// </summary>
    private async Task<bool> SettleAndCheckWriteAsync(Bbs757Control control, Bbs757SimIo io,
        ScreenReaderAnnouncer announcer, CancellationToken ct)
    {
        await io.Delay(PickSettleMs, ct);
        if (io.CanWrite()) return true;
        SayUnavailable(control, announcer);
        return false;
    }

    /// <summary>True while a pick of this control is in flight (its deliveries are consumed).</summary>
    internal bool IsPickPending(string varKey) => _pendingPicks.ContainsKey(varKey);

    /// <summary>
    /// Stops every pending pick and press: a profile switch must not let one write into the next
    /// aircraft (a guarded sequence in its step gap included: it sends none of its remaining calls).
    /// The outgoing definition is never used again, so its lifetime source goes too.
    /// Safe to call again (a switch that threw part-way, then app exit): a canceled and disposed
    /// source stays canceled, and Dispose is idempotent. The display readers stop, and their module
    /// session goes, and then the tablet client, even when a step before them throws, so no reader
    /// speaks over the next aircraft and the client never keeps the view for a profile that is gone.
    /// </summary>
    public override void OnSwitchedAway()
    {
        try
        {
            base.OnSwitchedAway();
            CloseMcpDialogs();
            CloseAutoflightWindows();
            CloseRadioWindows();   // plan 04
            ResetAutoflight();     // plan 05: its resting-value timer stops with the aircraft
            CancelLoadWatch();
            try { _lifetime.Cancel(); } catch (ObjectDisposedException) { /* switched away before */ }
            _lifetime.Dispose();
        }
        finally
        {
            StopDisplays();          // plan 23: never throws; before the tablet client goes
            DisposeTabletClient();   // last: after every window on it has closed
        }
    }

    /// <summary>
    /// Cancels and forgets every pick in flight: one armed before a flight load or a dropped
    /// connection must not write into the flight that follows. Each canceled pick ends silently and
    /// makes no re-read (its entry is gone). A guarded sequence that has already sent its first call
    /// does not watch its own token in its step gaps (a newer pick of its control must not cut it
    /// short); its read-back does, so this ends it silently, and this also bumps
    /// <see cref="_pickEpoch"/>, which stops its remaining calls: it sends none once it sees the count
    /// has changed.
    /// </summary>
    private void CancelPendingPicks()
    {
        Interlocked.Increment(ref _pickEpoch);
        foreach (var cts in _pendingPicks.Values)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { /* it finished meanwhile */ }
        }
        _pendingPicks.Clear();
    }

    // =================================================================================
    // Announcements
    // =================================================================================

    /// <summary>
    /// The BBS Nest check also rides any delivery that comes before its batch hook (see
    /// <see cref="DeferredFlushWatchVariable"/>). A control with a pick in flight is consumed
    /// silently (see <see cref="SetPositionAsync"/>). Everything else is the base's (the trim
    /// call-out; the 757 has no glideslope one, see <see cref="UsesStockNavReceivers"/>) or the
    /// generic announcer's (the announced status rows).
    /// </summary>
    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        WarnOnceIfNestMissing(announcer);
        NoteDelivery(varName, value);   // the flight load's gate counts every delivery (BBS757Definition.Readouts.cs)
        if (IsPickPending(varName)) return true;
        FollowEhsiControl(varName, value);   // plan 26: the EHSI window's EFIS control shows it; consumes nothing
        // A display reader's input: read from the cache, never spoken (BBS757Definition.Displays.cs).
        if (IsDisplayInputCopy(varName)) return true;   // plan 23
        // The gap plans' rows (BBS757Definition.Hooks.cs); the first to claim a delivery ends it.
        bool handled = false;
        ProcessRadioUpdate(varName, value, announcer, ref handled);                   // plan 04
        if (!handled) ProcessAutoflightUpdate(varName, value, announcer, ref handled);   // plan 05
        if (!handled) ProcessEfbUpdate(varName, value, announcer, ref handled);          // plan 07, then 11
        if (handled) return true;
        // Flaps, gear, speedbrake, parking brake, the MCP lights and the EXEC light speak their own
        // changes (BBS757Definition.Readouts.cs); MainForm's mute wrap gates them ([VAR-8]).
        if (ProcessReadoutUpdate(varName, value, announcer)) return true;
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    /// <summary>
    /// While the BBS Nest check of this connection or flight load is pending, MainForm calls
    /// <see cref="OnDeferredFlushBatchDelivered"/> at the end of every delivery of the batch carrying
    /// <see cref="NestCheckWatchKey"/>. The continuous batches arrive every second whether or not a
    /// value changed, so the check runs about a second after connecting, switching to this profile
    /// or loading a flight, even in a cold and dark cockpit where no variable moves (a value-change
    /// delivery alone could wait for minutes). Null once the check has settled, so the hook costs
    /// nothing for the rest of the session.
    /// </summary>
    public override string? DeferredFlushWatchVariable => _nestSettled ? null : NestCheckWatchKey;

    /// <summary>
    /// The batch carrying <see cref="NestCheckWatchKey"/> has been dispatched: run the BBS Nest
    /// check. This runs outside MainForm's Ctrl+M wrap, which is right: the warning is no row's, so
    /// no mute applies to it.
    /// </summary>
    public override void OnDeferredFlushBatchDelivered(ScreenReaderAnnouncer announcer)
        => WarnOnceIfNestMissing(announcer);

    /// <summary>
    /// Asks once whether BBS Nest runs and, when it does not, says so once. The warning is no
    /// Ctrl+M row's, so a delivery MainForm has wrapped in <c>announcer.Suppressed</c> (a muted row,
    /// a UI-set echo) does not use it up: it is said on the next delivery or batch instead.
    /// </summary>
    private void WarnOnceIfNestMissing(ScreenReaderAnnouncer announcer)
    {
        if (_nestSettled) return;
        _nestRunning ??= NestRunning();
        if (_nestRunning != true)
        {
            if (announcer.Suppressed) return;
            Log.Warn("BBS757", $"{Bbs757Nest.ProcessName} is not running; the 757's systems will not respond.");
            announcer.Announce(NestMissingWarning);
        }

        _nestSettled = true;
        OnConnectionBatch(announcer);   // plan 13: once per connection and per flight load
    }

    /// <summary>
    /// A flight load or a dropped connection: BBS Nest is asked about again on the next batch, every
    /// pick still in flight is dropped so none writes into the flight that follows (a guarded
    /// sequence already past its first call included: it sends none of its remaining calls), the
    /// load watch ends (its load belonged to the flight before), and the display readers search
    /// memory again, the new flight's first reading a silent baseline.
    /// </summary>
    public override void OnSimContextReset()
    {
        base.OnSimContextReset();
        _nestRunning = null;
        _nestSettled = false;
        CancelPendingPicks();
        CancelMcpSets();
        CancelLoadWatch();
        QuietReadoutBaselines();   // the new flight's flaps, gear and lights are no change; a drop clears them (OnVariableCacheCleared)
        ResetRadios();          // plan 04
        ResetAutoflight();      // plan 05
        ResetThrust();          // plan 15
        ResetAnnouncements();   // plan 13
        ResetDisplays();        // plan 23
    }

    // =================================================================================
    // Hotkeys
    // =================================================================================

    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        Sim = simConnect;

        // A display reader's key, first (BBS757Definition.Displays.cs): plan 23's registry.
        if (HandleDisplayKey(action, simConnect, announcer, parentForm, hotkeyManager)) return true;

        // The gap plans' hotkeys (BBS757Definition.Hooks.cs).
        bool handled = false;
        HandleRadioHotkey(action, simConnect, announcer, parentForm, hotkeyManager, ref handled);                 // plan 04
        if (!handled) HandleReadoutHotkey(action, simConnect, announcer, parentForm, hotkeyManager, ref handled);   // plan 06, then 14
        if (handled) return true;

        switch (action)
        {
            // Ctrl+M: mute individual auto-announced variables.
            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                (parentForm as MainForm)?.ShowBBS757MonitorManagerDialog();
                return true;

            // Ctrl+H / S / A / V: the MCP value windows (BBS757Definition.Mcp.cs), the HS787's way.
            case HotkeyAction.FCUSetHeading:
            case HotkeyAction.FCUSetSpeed:
            case HotkeyAction.FCUSetAltitude:
            case HotkeyAction.FCUSetVS:
                hotkeyManager.ExitInputHotkeyMode();
                ShowMcpDialog(action, simConnect, announcer, parentForm);
                return true;

            // Shift+H / S / A / V: the MCP's own values, read fresh.
            case HotkeyAction.ReadHeading:
            case HotkeyAction.ReadSpeed:
            case HotkeyAction.ReadAltitude:
            case HotkeyAction.ReadFCUVerticalSpeedFPA:
                _ = ReadMcpAsync(action, simConnect, announcer);   // never throws: it catches and logs
                return true;

            // L flaps, Shift+G gear, B altimeters and minimums, F / Shift+F fuel, W / Shift+W gross
            // weight: the other Boeings' keys (BBS757Definition.Readouts.cs).
            case HotkeyAction.ReadFlaps:
            case HotkeyAction.ReadGear:
            case HotkeyAction.ReadAltimeter:
            case HotkeyAction.ReadFuelQuantity:
            case HotkeyAction.ReadFuelInfo:
            case HotkeyAction.ReadWaypointInfo:
            case HotkeyAction.ReadGrossWeightKg:
                _ = ReadoutAsync(action, simConnect, announcer);   // never throws: it catches and logs
                return true;

            // Ctrl+B: the altimeter and minimums window (BBS757Definition.Altimeter.cs).
            case HotkeyAction.FCUSetBaro:
                hotkeyManager.ExitInputHotkeyMode();
                ShowAltimeterWindow(simConnect, announcer, parentForm);
                return true;

            // Ctrl+P: the 757 autopilot window (BBS757Definition.Autopilot.cs).
            case HotkeyAction.FCUSetAutopilot:
                hotkeyManager.ExitInputHotkeyMode();
                ShowAutopilotWindow(simConnect, announcer);
                return true;
        }

        // AI display reads, the time read-outs and the trim toggle.
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }
}
