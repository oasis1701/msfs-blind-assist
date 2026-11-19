using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The overhead panels' writes: plan 03's guarded controls (the emergency lights, the stab trim
/// cutouts), the fire handle unlocks' relock and the Pressurization panel's typed landing altitude,
/// and later plan 12's overhead panels, which join the seams at the end of this file. A guarded
/// control is any map control whose <see cref="Bbs757Control.Guard"/> names another map control
/// (BlueBird's guard cover): it moves only with the cover open, so a pick is planned from fresh
/// reads of both by <see cref="Bbs757Guarded.Plan"/> and sent here.
/// </summary>
public partial class BBS757Definition
{
    /// <summary>
    /// A pick on a guarded control: settle, refuse aloud if nothing can land, read the control and its
    /// guard fresh, send what <see cref="Bbs757Guarded.Plan"/> plans (the guard's press and the state
    /// write, <see cref="Bbs757Guarded.StepGapMs"/> apart), then read the control back. It follows
    /// <see cref="SetPositionAsync"/>: a newer pick of the same control supersedes this one at any
    /// await before its first call, and both rows are re-read at the end so their combos show where
    /// the aircraft really is.
    ///
    /// Once the first call has gone, a NEWER PICK of the same control does not stop the sequence: a
    /// half-run one would leave the guard open with no state write behind it (the gaps are awaited on
    /// the profile's lifetime, not on this pick's own token). The newer pick still cancels this one's
    /// read-back, so only the newer pick speaks. A flight load or a dropped connection
    /// (<see cref="CancelPendingPicks"/>, seen through <see cref="_pickEpoch"/>) and a profile switch
    /// do stop it: after each gap it sends nothing more, says nothing and re-reads nothing, so no
    /// write reaches the flight that follows.
    ///
    /// Speech follows CORE-7: the screen reader read the combo, so a pick that lands says nothing and
    /// one that does not says "{label} did not move" once (an unreadable read-back counts as not
    /// landed); a pick that cannot be planned or written says "{label} unavailable" and sends nothing.
    ///
    /// True when it wrote and waited out the read-back delay; false when it refused, found the control
    /// already there, or was superseded, dropped or switched away. Never throws.
    /// </summary>
    internal async Task<bool> SetGuardedAsync(Bbs757Control control, Bbs757Control guard, double target,
        Bbs757SimIo io, ScreenReaderAnnouncer announcer)
    {
        int epoch = Volatile.Read(ref _pickEpoch);   // before the pick is entered: a drop from here on is this pick's
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

            // Both at once: an unread variable then costs one read timeout, not two.
            var reads = await Task.WhenAll(io.ReadFresh(control.Node, ct), io.ReadFresh(guard.Node, ct));
            ct.ThrowIfCancellationRequested();
            double? current = reads[0], guardState = reads[1];

            var plan = Bbs757Guarded.Plan(control, guard, current, guardState, target);
            if (plan.Refused)
            {
                Log.Debug("BBS757", $"{control.Node}: read {GuardedLogValue(current)}, guard {guard.Node} read {GuardedLogValue(guardState)}, " +
                    $"target {GuardedLogValue(target)}: no safe write.");
                SayUnavailable(control, announcer);
                resync = true;
                return false;
            }
            if (plan.Calls.Count == 0) return false;   // already there, guard where the pick needs it

            OnOwnWrite(control.Node);   // plan 13
            OnOwnWrite(guard.Node);
            for (int i = 0; i < plan.Calls.Count; i++)
            {
                if (i > 0)
                {
                    // The first call has gone: finish even if a newer pick has superseded this one
                    // (its token is not awaited here), but not across a flight load or a profile
                    // switch, whichever lands before or during the gap.
                    if (SequenceDropped(epoch)) return false;
                    await io.Delay(Bbs757Guarded.StepGapMs, _lifetime.Token);
                    if (SequenceDropped(epoch)) return false;
                }
                io.Write(plan.Calls[i]);
            }

            await io.Delay(ReadBackDelayMs, ct);
            resync = true;
            wrote = true;

            double? after = await io.ReadFresh(control.Node, ct);
            ct.ThrowIfCancellationRequested();
            if (!LandedOnPick(control, after, target)) announcer.Announce($"{control.Label} did not move");
        }
        catch (OperationCanceledException) { /* superseded by a newer pick, a reload, or switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.Node}: guarded write failed: {ex.Message}");
            resync = true;   // whatever was or was not sent, the combos should show the aircraft's position
        }
        finally
        {
            if (EndPick(control.Node, cts) && resync)
            {
                io.RequestRead(control.Node);
                io.RequestRead(guard.Node);
            }
        }
        return wrote;
    }

    /// <summary>
    /// True when a guarded sequence that has sent its first call must send nothing more: the pilot
    /// switched away from the profile, or a flight load or dropped connection dropped the pick
    /// (<see cref="_pickEpoch"/> moved since <paramref name="epoch"/> was read). A newer pick of the
    /// same control is neither, so it never ends a sequence.
    /// </summary>
    private bool SequenceDropped(int epoch)
        => _lifetime.IsCancellationRequested || epoch != Volatile.Read(ref _pickEpoch);

    /// <summary>True when <paramref name="reading"/> is a number holding the position <paramref name="target"/> (within the read-back slack).</summary>
    private static bool LandedOnPick(Bbs757Control control, double? reading, double target)
    {
        if (reading is not double value || !double.IsFinite(value)) return false;
        int to = Bbs757Rpn.PositionIndex(control, target);
        return to >= 0 && Bbs757Rpn.PositionIndex(control, value) == to;
    }

    private static string GuardedLogValue(double? value)
        => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nothing";

    // =================================================================================
    // Fire handle unlocks
    // =================================================================================

    /// <summary>
    /// Called by <see cref="PressAsync"/> right after a button's press has been sent: when the button is
    /// a fire handle unlock (<see cref="Bbs757Write.RelockAfterUnlock"/>), starts the relock that
    /// BlueBird's own <c>O:</c> timer would have made in the cockpit and the calculator press cannot
    /// start. Any other control starts nothing. Never throws.
    /// </summary>
    private void RelockIfUnlock(Bbs757Control control, Bbs757SimIo io)
    {
        string? relock = Bbs757Write.RelockAfterUnlock(control, out int delayMs);
        if (relock == null) return;
        _ = RelockAfterDelayAsync(control, relock, delayMs, io);   // never throws: it catches and logs
    }

    /// <summary>
    /// Waits <paramref name="delayMs"/> on the profile's lifetime, then writes <paramref name="relock"/>.
    /// Switching to another profile ends the wait and sends nothing: the handle then stays as the
    /// cockpit would leave it. Nothing is spoken: the press already said "{label} pressed", and the
    /// relock is the cockpit's own behaviour. The awaits keep the UI context (no ConfigureAwait(false)),
    /// like every write here.
    ///
    /// It does not consult <see cref="_pickEpoch"/>, as a guarded sequence does: the write is the
    /// cockpit's resting value (0, locked), so it is safe across a flight load or a reconnect, and
    /// skipping it there would leave the handle unlocked, the defect this relock exists to prevent.
    /// </summary>
    private async Task RelockAfterDelayAsync(Bbs757Control control, string relock, int delayMs, Bbs757SimIo io)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;   // the source is disposed once switched away: its token must not be touched
            await io.Delay(delayMs, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;   // switched away as the wait ended
            OnOwnWrite(control.Node);   // plan 13
            io.Write(relock);
        }
        catch (OperationCanceledException) { /* switched away: the handle stays as the cockpit would leave it */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.Node}: relock failed: {ex.Message}");
        }
    }

    // =================================================================================
    // The Pressurization panel's landing altitude
    // =================================================================================

    /// <summary>The map's panel with the pressurization controls (Mode Select, Auto Rate), which gains the landing altitude field.</summary>
    internal const string PressurizationPanel = "Pressurization";

    /// <summary>The section a Pressurization panel the map lacks is created under.</summary>
    private const string OverheadSection = "Overhead";

    // The seams of BBS757Definition.Hooks.cs, filled in by the overhead plans one feature at a time:
    // each feature has its own helper below and the seam calls it.

    static partial void AddOverheadVariables(Dictionary<string, SimVarDefinition> vars)
    {
        AddLandingAltitudeVariables(vars);
        AddSpringControlVariables(vars);
        AddLampTestVariables(vars);
    }

    partial void AddOverheadPanels(Dictionary<string, List<string>> structure,
        Dictionary<string, List<string>> controls, Dictionary<string, List<string>> displays)
    {
        AddLandingAltitudePanels(structure, controls, displays);
        AddSpringControlPanels(structure, controls, displays);   // after the field and its row, which stay first on Pressurization
    }

    partial void HandleOverheadTypedText(string varKey, string text, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, ref bool handled)
    {
        if (varKey != Bbs757LandingAltitude.SetKey) return;
        handled = true;
        _ = SetLandingAltitudeAsync(text, simConnect, announcer);   // never throws: it catches and logs
    }

    partial void HandleOverheadSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, ref bool handled)
    {
        HandleLandingAltitudeSet(varKey, ref handled);
        if (!handled) HandleSpringSet(varKey, simConnect, announcer, ref handled);
        // A map node: HandleUIVariableSet asks this seam before its generic map path, which would press it unread.
        if (!handled) HandleLampTestSet(varKey, simConnect, announcer, ref handled);
    }

    private static void HandleLandingAltitudeSet(string varKey, ref bool handled)
    {
        if (varKey != Bbs757LandingAltitude.SetKey) return;
        // The registration's event is never sent (ruling R13): claiming the key here keeps it from the
        // generic path, which would transmit it. The field's text goes through HandleOverheadTypedText.
        handled = true;
        Log.Debug("BBS757", $"{varKey}: a panel set carries no landing altitude; nothing sent.");
    }

    /// <summary>The map's Status row "Cabin Altitude": BlueBird's cabin altitude in feet (plan 12 task 1).</summary>
    internal const string CabinAltitudeStatusKey = "B757_TEST_CABIN_ALT";

    partial void FormatOverheadDisplay(string varKey, double value, ref string? text)
    {
        if (varKey == Bbs757LandingAltitude.DisplayKey) text = Bbs757LandingAltitude.Display(value);
        // The spring controls' readout rows say the words their press's read-back says after the name.
        else if (Bbs757SpringControls.RowText(varKey, value) is { } row) text = row;
        // Whole feet, invariant, as the landing altitude's row: "1161 feet".
        else if (varKey == CabinAltitudeStatusKey) text = Bbs757LandingAltitude.Display(value);
    }

    /// <summary>
    /// The landing altitude field and its read-back row, added after the map's controls (never
    /// shadowing a key already present).
    /// </summary>
    private static void AddLandingAltitudeVariables(Dictionary<string, SimVarDefinition> vars)
    {
        void Add(string key, SimVarDefinition def)
        {
            if (!vars.ContainsKey(key)) vars[key] = def;
        }

        // The field. A key containing "_SET" is what MainForm renders as a text box Enter applies. The
        // definition claims it (HandleOverheadTypedText, HandleOverheadSet), so its event is never sent:
        // the registration only keeps it an event, which takes no data definition. SimConnectManager
        // maps every Event registration with MapClientEventToSimEvent at connect, and a dotted private
        // name changes nothing in the sim even if it were ever sent (ruling R16).
        Add(Bbs757LandingAltitude.SetKey, new SimVarDefinition
        {
            Name = "BBS757.LDG_ALT_SET", DisplayName = "Landing Altitude",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.OnRequest,
            UnparseableTextAsNaN = true,
        });

        // The read-back row: BlueBird's landing altitude in feet, read when the panel is shown and after
        // a set, shown as "1030 feet" (FormatOverheadDisplay), never announced, no Ctrl+M row.
        Add(Bbs757LandingAltitude.DisplayKey, new SimVarDefinition
        {
            Name = Bbs757LandingAltitude.ValueVar, DisplayName = "Landing Altitude",
            Type = SimVarType.LVar, Units = "feet", UpdateFrequency = UpdateFrequency.OnRequest,
            RenderAsReadOnlyStatus = true, ExcludeFromMonitorManager = true,
        });
    }

    /// <summary>
    /// The field goes first in the Pressurization panel (the pilot types it before any switch there)
    /// and its row joins the panel's status display after the map's rows. A map without a Pressurization
    /// panel gets one under the Overhead section, so the field is never lost.
    /// </summary>
    private static void AddLandingAltitudePanels(Dictionary<string, List<string>> structure,
        Dictionary<string, List<string>> controls, Dictionary<string, List<string>> displays)
    {
        if (!controls.TryGetValue(PressurizationPanel, out var panel))
        {
            controls[PressurizationPanel] = panel = new List<string>();
            if (!structure.TryGetValue(OverheadSection, out var panels)) structure[OverheadSection] = panels = new List<string>();
            panels.Add(PressurizationPanel);
        }
        panel.Insert(0, Bbs757LandingAltitude.SetKey);

        if (!displays.TryGetValue(PressurizationPanel, out var rows)) displays[PressurizationPanel] = rows = new List<string>();
        rows.Add(Bbs757LandingAltitude.DisplayKey);
    }

    /// <summary>
    /// The landing altitude field's text: refused aloud unless a landing altitude
    /// (<see cref="Bbs757LandingAltitude.TryParseText"/>), refused aloud when a calculator write cannot
    /// land; otherwise written (<see cref="Bbs757LandingAltitude.Write"/>), read back fresh after
    /// <see cref="ReadBackDelayMs"/> and the value READ spoken, whatever it is: a write that did not
    /// take (BBS Nest stopped) says the old altitude, never the one typed. Then the status row is
    /// re-read so it shows the same value. A typed number's confirmation, a validation error and a
    /// refusal always speak (CORE-7). Never throws: fire and forget from the panel. The awaits keep the
    /// UI context (no ConfigureAwait(false)), like every write here.
    /// </summary>
    internal async Task SetLandingAltitudeAsync(string typed, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;   // switched away: nothing is written or said
            var ct = _lifetime.Token;                        // taken once, after the check: safe on a disposed source
            var io = WriteIoFor(sim);

            if (!Bbs757LandingAltitude.TryParseText(typed, out int feet, out string error))
            {
                announcer.AnnounceImmediate(error);
                return;
            }
            if (!io.CanWrite())
            {
                Log.Debug("BBS757", "Landing altitude: write refused, nothing sent.");
                announcer.AnnounceImmediate(Bbs757LandingAltitude.Phrase(null));   // "Landing altitude unavailable"
                return;
            }

            OnOwnWrite(Bbs757LandingAltitude.DisplayKey);   // plan 13
            io.Write(Bbs757LandingAltitude.Write(feet));

            await io.Delay(ReadBackDelayMs, ct);
            double? back = await io.ReadFresh(Bbs757LandingAltitude.DisplayKey, ct);
            ct.ThrowIfCancellationRequested();
            announcer.AnnounceImmediate(Bbs757LandingAltitude.Phrase(back));
            io.RequestRead(Bbs757LandingAltitude.DisplayKey);
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"Landing altitude set failed: {ex.Message}");
            if (!_lifetime.IsCancellationRequested) announcer.AnnounceImmediate(Bbs757LandingAltitude.Phrase(null));
        }
    }

    // =================================================================================
    // Plan 12: the spring-loaded switches (the trims, the manual cabin altitude, the P61 tests)
    // =================================================================================

    /// <summary>
    /// The eight trim and cabin buttons and the seven reads their presses make, then the fourteen P61 test buttons
    /// and their seven switches' reads (<see cref="Bbs757SpringControls"/>), after the map's controls, never
    /// shadowing a key already present. A button is an Event registration, as the landing altitude field is: it
    /// takes no data definition, the definition claims its key (<see cref="HandleOverheadSet"/>) so the generic path
    /// never sends it, and its dotted private name changes nothing in the sim even if it were ever sent (ruling
    /// R16). A read is an OnRequest L:var with no Ctrl+M row ([VAR-9]), never announced: a fresh read delivers only
    /// a registered key. The three readouts are also read-only status rows (Rudder Trim, Aileron Trim, Outflow
    /// Valve), read when their panel's status display is shown, as the landing altitude's row is; the eleven
    /// switches are on no panel. The stab trim's readout (the trim call-out's <c>MON_ElevatorTrim</c>) and Mode
    /// Select (the map's control) are registered already.
    /// </summary>
    private static void AddSpringControlVariables(Dictionary<string, SimVarDefinition> vars)
    {
        void Add(string key, SimVarDefinition def)
        {
            if (!vars.ContainsKey(key)) vars[key] = def;
        }

        foreach (var button in Bbs757SpringControls.Buttons.Concat(Bbs757SpringControls.TestButtons))
        {
            Add(button.Key, new SimVarDefinition
            {
                Name = button.EventName, DisplayName = button.Label,
                Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.OnRequest,
                RenderAsButton = true, IsMomentary = true,
                SuppressRestingButtonState = true,   // a momentary push has no resting value to show (CORE-10's opt-in)
            });
        }

        foreach (var read in Bbs757SpringControls.Reads.Concat(Bbs757SpringControls.TestReads))
        {
            Add(read.Key, new SimVarDefinition
            {
                Name = read.Key, DisplayName = read.Label,
                Type = SimVarType.LVar, Units = "number", UpdateFrequency = UpdateFrequency.OnRequest,
                RenderAsReadOnlyStatus = read.ShownWith != null,   // its text: FormatOverheadDisplay
                ExcludeFromMonitorManager = true,
            });
        }
    }

    /// <summary>
    /// The four rudder and aileron trim buttons make up the app's own Rudder and aileron trim panel, listed right after
    /// Stabilizer trim in the Pedestal (plan 12 task 7: their switches sit on their own trim panel at the aft end of the
    /// aisle stand); the two stab trim buttons go first on the map's Stabilizer trim panel (ahead of the cutouts and
    /// their guards), the two cabin buttons last on Pressurization (the landing altitude field stays first), and the
    /// fourteen P61 test buttons on the P61 panel right after the squib test
    /// (<see cref="Bbs757SpringControls.TestButtonsAfterNode"/>, where the test switches stood in its daily-use order),
    /// or at its end when the map lacks that node. A panel the map lacks is created under its section, after the panel
    /// its control names (<see cref="Bbs757SpringControl.PanelAfter"/>) or at the section's end, so the buttons are
    /// never lost. The readout rows join their panel's status display after the rows it has (on Pressurization, after
    /// the landing altitude's): Rudder Trim and Aileron Trim on Rudder and aileron trim, Outflow Valve on
    /// Pressurization; the tests add no row.
    /// </summary>
    private static void AddSpringControlPanels(Dictionary<string, List<string>> structure,
        Dictionary<string, List<string>> controls, Dictionary<string, List<string>> displays)
    {
        List<string> PanelOf(Bbs757SpringControl control)
        {
            if (controls.TryGetValue(control.Panel, out var panel)) return panel;
            controls[control.Panel] = panel = new List<string>();
            if (!structure.TryGetValue(control.Section, out var panels)) structure[control.Section] = panels = new List<string>();
            int after = control.PanelAfter is { } previous ? panels.IndexOf(previous) : -1;
            panels.Insert(after >= 0 ? after + 1 : panels.Count, control.Panel);
            return panel;
        }

        // Each trim panel's buttons first on it, in the order Buttons lists them. A panel listed after another is made
        // last, so that the one it follows is listed already when a map lacks both.
        foreach (var trims in Bbs757SpringControls.Buttons.Where(b => !b.Control.NeedsManualMode)
                     .GroupBy(b => b.Control.Panel).OrderBy(g => g.First().Control.PanelAfter != null))
            PanelOf(trims.First().Control).InsertRange(0, trims.Select(b => b.Key));

        var cabin = Bbs757SpringControls.Buttons.Where(b => b.Control.NeedsManualMode).ToList();
        if (cabin.Count > 0) PanelOf(cabin[0].Control).AddRange(cabin.Select(b => b.Key));

        var tests = Bbs757SpringControls.TestButtons;
        if (tests.Count > 0)
        {
            var panel = PanelOf(tests[0].Control);   // all fourteen share the P61 panel
            int after = panel.IndexOf(Bbs757SpringControls.TestButtonsAfterNode);
            panel.InsertRange(after >= 0 ? after + 1 : panel.Count, tests.Select(b => b.Key));
        }

        foreach (var read in Bbs757SpringControls.Reads)
        {
            if (read.ShownWith is not { } owner) continue;
            PanelOf(owner);   // the panel is listed (its buttons made sure of it): a row never sits on a panel no one reaches
            if (!displays.TryGetValue(owner.Panel, out var rows)) displays[owner.Panel] = rows = new List<string>();
            if (!rows.Contains(read.Key)) rows.Add(read.Key);
        }
    }

    /// <summary>A press of a spring button: claimed at once so the generic path never sends it, then held on its own task (never throws).</summary>
    private void HandleSpringSet(string varKey, SimConnectManager sim, ScreenReaderAnnouncer announcer, ref bool handled)
    {
        if (Bbs757SpringControls.ForButtonKey(varKey) == null) return;
        handled = true;
        _ = PressSpringAsync(varKey, sim, announcer);   // never throws: it catches and logs
    }

    /// <summary>The spring controls holding now, by <see cref="Bbs757SpringControl.Key"/>: one hold per switch at a time.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _springHolds = new(StringComparer.Ordinal);

    /// <summary>
    /// A spring button: hold its switch to one side for the switch's <see cref="Bbs757SpringControl.HoldMs"/> (a trim
    /// or the cabin switch one second, a P61 test three), release it, make sure it rests, then say the readout once,
    /// or for a P61 test "{test} done".
    /// <list type="number">
    /// <item>A press while the same switch is holding, or still being released in the background, is ignored (its
    /// two buttons share the switch).</item>
    /// <item>Nothing can land (<c>CanWrite</c>): "{label} unavailable", nothing sent. A cabin button then reads Mode
    /// Select fresh: not MAN says "Set the pressurization mode to MAN first", unread "{label} unavailable", and
    /// nothing is sent.</item>
    /// <item>The hold is written, written again every <see cref="Bbs757SpringControl.RewriteMs"/> (the cabin switch's
    /// XML puts it back to 4 within about a second, a P61 test switch returns to neutral at once; skipped once the
    /// pilot has switched profiles) and released at <see cref="Bbs757SpringControl.HoldMs"/>
    /// (<see cref="HoldSpringAsync"/>). These waits are on <see cref="CancellationToken.None"/>, as
    /// <see cref="PressAsync"/>'s release is: nothing in the aircraft returns a trim switch to rest, so a lost
    /// release runs the trim to its stop (measured twice on 2026-10-28, [B757-15]).</item>
    /// <item>The release is verified (<see cref="ReleaseSpringAsync"/>), even after a profile switch: it is the safety.
    /// A switch still off rest is released again in the background (<see cref="KeepReleasingAsync"/>, even after a
    /// profile switch, started before anything is said so the safety never depends on speech), then says "{name}
    /// switch did not return; the trim may keep running" (the cabin switch: "Cabin altitude switch did not return;
    /// check the outflow valve"), interrupting; the press ends, and the switch may be held again, once that ends.</item>
    /// <item>Unless switched away, <see cref="Bbs757SpringControls.ReadoutDelayMs"/> later the readout is read
    /// fresh and said once (CORE-7: the screen reader said the button; the app says only the read-back). The stab
    /// trim's is the exception: the trim call-out already speaks while the stabilizer moves, so the trim is also read
    /// fresh before the hold and <see cref="Bbs757SpringControls.StabTrimSpeech"/> decides: "Stab trim did not move"
    /// when it did not, nothing when it moved and the call-out speaks it, "Trim 5.3" when it moved and the call-out
    /// is silent (<see cref="StabTrimCalloutSilent"/>). A P61 test reads nothing back: once its switch rests it says
    /// "{test} done" (<see cref="Bbs757SpringControls.DonePhrase"/>), and nothing during the hold; the lights and
    /// warnings the test raised are the cockpit watcher's to say (plan 13). One that did not return says only
    /// that.</item>
    /// </list>
    /// Nothing is said once the pilot has switched to another profile. The awaits keep the UI context (no
    /// ConfigureAwait(false)), like every write here. Never throws: fire and forget from the panel.
    /// </summary>
    internal async Task PressSpringAsync(string buttonKey, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var button = Bbs757SpringControls.ForButtonKey(buttonKey);
        if (button == null) return;
        var control = button.Control;
        if (_lifetime.IsCancellationRequested) return;   // switched away: nothing is pressed or said
        var ct = _lifetime.Token;                        // taken once, after the check: safe on a disposed source
        if (!_springHolds.TryAdd(control.Key, 0))
        {
            Log.Debug("BBS757", $"{button.Label}: {control.LVar} is already holding; press ignored.");
            return;
        }

        bool holding = true, wrote = false;
        Task stillReleasing = Task.CompletedTask;   // the background release after a lost one; never throws
        try
        {
            var io = WriteIoFor(sim);
            if (!io.CanWrite())
            {
                Log.Debug("BBS757", $"{button.Label}: write refused, nothing sent.");
                announcer.Announce(Bbs757SpringControls.UnavailablePhrase(button.Label));
                return;
            }
            if (control.NeedsManualMode)
            {
                double? mode = await io.ReadFresh(Bbs757SpringControls.ModeSelectNode, ct);
                ct.ThrowIfCancellationRequested();
                if (Bbs757SpringControls.ManualModeRefusal(mode, button.Label) is { } refusal)
                {
                    Log.Debug("BBS757", $"{button.Label}: Mode Select read {GuardedLogValue(mode)}; nothing sent.");
                    announcer.Announce(refusal);
                    return;
                }
            }
            bool stabTrim = ReferenceEquals(control, Bbs757SpringControls.AltnStabTrim);
            double? trimBefore = null;
            if (stabTrim)
            {
                trimBefore = await io.ReadFresh(Bbs757SpringControls.StabTrimReadoutKey, ct);   // its readout, to tell whether the press moved it
                ct.ThrowIfCancellationRequested();
            }

            var (hold, release) = Bbs757SpringControls.Codes(control, button.Positive);
            OnOwnWrite(control.LVar);   // plan 13
            wrote = true;
            bool rested = await HoldSpringAsync(control, hold, release, io);
            if (rested)
            {
                _springHolds.TryRemove(control.Key, out _);   // released: the switch may be held again
                holding = false;
            }
            else
            {
                Log.Warn("BBS757", $"{control.LVar} did not read its rest value after {Bbs757SpringControls.MaxReleaseWrites} releases; " +
                    $"releasing it again every {Bbs757SpringControls.ReleaseRetryIntervalMs} ms for up to {Bbs757SpringControls.ReleaseRetryForMs} ms.");
                // The safety goes on even after a profile switch; the switch's gate stays taken until it ends (finally).
                // Started before the warning is spoken, so speech that fails cannot keep it from starting (plan 12 task 12);
                // its first step is a 2 s wait, so the warning is still heard first.
                stillReleasing = KeepReleasingAsync(control, release, io, announcer);
                if (!_lifetime.IsCancellationRequested) announcer.AnnounceImmediate(Bbs757SpringControls.DidNotReturnPhrase(control));
            }

            if (_lifetime.IsCancellationRequested) return;   // switched away: released (or still releasing), nothing is said

            if (control.ReadoutKey is not { } readoutKey)
            {
                // A P61 test: nothing to read back. Done once its switch rests; one still off rest has said so.
                if (rested) announcer.Announce(Bbs757SpringControls.DonePhrase(button));
                return;
            }

            await io.Delay(Bbs757SpringControls.ReadoutDelayMs, ct);
            double? value = await io.ReadFresh(readoutKey, ct);
            ct.ThrowIfCancellationRequested();
            string? said = stabTrim
                ? Bbs757SpringControls.StabTrimSpeech(trimBefore, value, StabTrimCalloutSilent())
                : Bbs757SpringControls.ReadoutPhrase(control, value);
            if (said != null) announcer.Announce(said);
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{button.Label}: press failed: {ex.Message}");
            // Before any write: refused. After it: a trim's readout as unread; a P61 test ran and was released, so
            // nothing more is said (logged only). Nothing once switched away.
            if (!_lifetime.IsCancellationRequested)
            {
                if (!wrote) announcer.Announce(Bbs757SpringControls.UnavailablePhrase(button.Label));
                else if (control.ReadoutKey != null) announcer.Announce(Bbs757SpringControls.ReadoutPhrase(control, null));
            }
        }
        finally
        {
            await stillReleasing;   // never throws: no new hold starts on this switch while it is still being released
            if (holding) _springHolds.TryRemove(control.Key, out _);
        }
    }

    /// <summary>
    /// The release after a lost one (<see cref="ReleaseSpringAsync"/> found the switch still off rest after
    /// <see cref="Bbs757SpringControls.MaxReleaseWrites"/> writes, as a lost link would leave it): every
    /// <see cref="Bbs757SpringControls.ReleaseRetryIntervalMs"/>, for up to <see cref="Bbs757SpringControls.ReleaseRetryForMs"/>,
    /// reads the switch fresh and, while it does not read its rest value (unread counts as off rest), writes
    /// <paramref name="release"/> again. Once it rests, "{name} switch returned" is said once, unless the pilot has
    /// switched to another profile or no <paramref name="announcer"/> is given (the First Officer's P61 tests are
    /// silent, <see cref="FoSpringTestCoreAsync"/>); a switch that never rests is left after the last write with
    /// nothing more said (the press already warned). A profile switch does not stop it: it is the safety, so the
    /// waits and reads are on <see cref="CancellationToken.None"/>. The writes are the press's own (<c>Write</c>,
    /// unique calculator writes). The awaits keep the UI context. Never throws: a failed wait, read or write counts
    /// as off rest.
    /// </summary>
    private async Task KeepReleasingAsync(Bbs757SpringControl control, string release, Bbs757SimIo io, ScreenReaderAnnouncer? announcer)
    {
        try
        {
            for (int waited = 0; waited < Bbs757SpringControls.ReleaseRetryForMs; waited += Bbs757SpringControls.ReleaseRetryIntervalMs)
            {
                double? reading = null;
                try
                {
                    await io.Delay(Bbs757SpringControls.ReleaseRetryIntervalMs, CancellationToken.None);
                    reading = await io.ReadFresh(control.LVar, CancellationToken.None);
                }
                catch (Exception ex) { Log.Error("BBS757", $"{control.LVar}: background release read failed: {ex.Message}"); }

                if (Bbs757SpringControls.AtRest(control, reading))
                {
                    Log.Info("BBS757", $"{control.LVar} read its rest value {waited + Bbs757SpringControls.ReleaseRetryIntervalMs} ms into the background release.");
                    if (!_lifetime.IsCancellationRequested) announcer?.Announce(Bbs757SpringControls.ReturnedPhrase(control));
                    return;
                }

                try { io.Write(release); }
                catch (Exception ex) { Log.Error("BBS757", $"{control.LVar}: background release write failed: {ex.Message}"); }
            }
            Log.Warn("BBS757", $"{control.LVar} still did not read its rest value after {Bbs757SpringControls.ReleaseRetryForMs} ms of background releases; stopped.");
        }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.LVar}: background release failed: {ex.Message}");
        }
    }

    /// <summary>
    /// True when the trim call-out (the base class's <c>MON_ElevatorTrim</c> row) will not speak a move: the pilot
    /// muted its Ctrl+M row, or turned trim announcements off (Shift+T). Speech outside ProcessSimVarUpdate checks
    /// the mute itself ([VAR-8]), as the gear and altimeter call-outs do.
    /// </summary>
    private bool StabTrimCalloutSilent()
        => !_trimAnnouncementsEnabled || IsMonitorMuted(Bbs757SpringControls.StabTrimReadoutKey);

    /// <summary>
    /// Holds the switch: <paramref name="hold"/> now and again every <see cref="Bbs757SpringControl.RewriteMs"/> (not
    /// once switched away), then the verified release once <see cref="Bbs757SpringControl.HoldMs"/> has passed: a trim
    /// or the cabin switch at 0 and 500 ms, released at 1,000 ms; a P61 test at 0, 30, ... 2,970 ms, released at
    /// 3,000 ms. The hold ends by whichever comes first of the waits asked and the real time on <see cref="NowMs"/>:
    /// each real wait runs a little long (a 30 ms one measured about 40 ms), so a hold counted by its waits alone would
    /// run about 4 s; with real 40 ms waits a test writes about 75 times and is released at 3,000 ms. Each gap is the
    /// re-write gap or what is left of the hold, whichever is shorter, never under 1 ms. The waits are never canceled,
    /// so a switch away still waits out the hold before the release, and a hold that fails part-way is released at
    /// once. True when the switch reads its rest value afterwards. Never throws.
    /// </summary>
    private async Task<bool> HoldSpringAsync(Bbs757SpringControl control, string hold, string release, Bbs757SimIo io)
    {
        try
        {
            long start = NowMs();
            io.Write(hold);
            long asked = 0, elapsed = 0;   // the waits asked, and the real time since the first write
            while (Math.Max(asked, elapsed) < control.HoldMs)
            {
                // At least 1 ms: a table error (a gap of 0) must not spin.
                long gap = Math.Max(1, Math.Min(control.RewriteMs, control.HoldMs - Math.Max(asked, elapsed)));
                await io.Delay((int)gap, CancellationToken.None);
                asked += gap;
                elapsed = NowMs() - start;
                if (asked < control.HoldMs && elapsed < control.HoldMs && !_lifetime.IsCancellationRequested) io.Write(hold);
            }
        }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.LVar}: hold failed: {ex.Message}; releasing it now.");
        }
        return await ReleaseSpringAsync(control, release, io);
    }

    /// <summary>
    /// Writes <paramref name="release"/>, waits <see cref="Bbs757SpringControls.ReleaseReadBackMs"/> (so a read that
    /// has not yet seen the write cannot count as a lost release), reads the switch fresh and, while it does not
    /// read its rest value (<see cref="Bbs757SpringControls.AtRest"/>; unread counts as off rest), writes it again:
    /// at most <see cref="Bbs757SpringControls.MaxReleaseWrites"/> writes in all. Runs after a profile switch too
    /// (the waits and reads are on <see cref="CancellationToken.None"/>; a real read gives up after its own
    /// timeout). True when the switch rests. Never throws: a failed write, wait or read counts as a release that
    /// did not land.
    /// </summary>
    private static async Task<bool> ReleaseSpringAsync(Bbs757SpringControl control, string release, Bbs757SimIo io)
    {
        for (int written = 0; written < Bbs757SpringControls.MaxReleaseWrites; written++)
        {
            try { io.Write(release); }
            catch (Exception ex) { Log.Error("BBS757", $"{control.LVar}: release write failed: {ex.Message}"); }

            double? reading = null;
            try
            {
                await io.Delay(Bbs757SpringControls.ReleaseReadBackMs, CancellationToken.None);
                reading = await io.ReadFresh(control.LVar, CancellationToken.None);
            }
            catch (Exception ex) { Log.Error("BBS757", $"{control.LVar}: release read-back failed: {ex.Message}"); }
            if (Bbs757SpringControls.AtRest(control, reading)) return true;
        }
        return false;
    }

    // =================================================================================
    // Plan 12: the lamp test (the map's Warning Test), read back after its press
    // =================================================================================

    /// <summary>
    /// The lamp test's latch (<see cref="Bbs757LampTest.LitVar"/>), read fresh after a Warning Test press: an OnRequest
    /// L:var with no Ctrl+M row ([VAR-9]), never announced, on no panel, never shadowing a key already present.
    /// </summary>
    private static void AddLampTestVariables(Dictionary<string, SimVarDefinition> vars)
    {
        if (vars.ContainsKey(Bbs757LampTest.LitVar)) return;
        vars[Bbs757LampTest.LitVar] = new SimVarDefinition
        {
            Name = Bbs757LampTest.LitVar, DisplayName = "Lamp Test",
            Type = SimVarType.LVar, Units = "number", UpdateFrequency = UpdateFrequency.OnRequest,
            ExcludeFromMonitorManager = true,
        };
    }

    /// <summary>
    /// A panel press of the map's Warning Test (<see cref="Bbs757LampTest.Node"/>): claimed here, ahead of the generic
    /// map path, so its press reads the lamp test back (<see cref="PressLampTestAsync"/>) instead of saying "Warning
    /// Test pressed". A map without the node, with it as anything but a button, or with a guard on it (the generic
    /// path's <see cref="SetGuardedAsync"/>) leaves it to the generic path.
    /// </summary>
    private void HandleLampTestSet(string varKey, SimConnectManager sim, ScreenReaderAnnouncer announcer, ref bool handled)
    {
        if (varKey != Bbs757LampTest.Node) return;
        if (!_byNode.TryGetValue(varKey, out var control) || !Bbs757Write.IsMomentary(control)
            || !string.IsNullOrWhiteSpace(control.Guard)) return;
        handled = true;
        _ = PressLampTestAsync(control, WriteIoFor(sim), announcer);   // never throws: it catches and logs
    }

    /// <summary>
    /// The Warning Test pressed as <see cref="PressFromPanelAsync"/> presses any button (its own press, the button's
    /// hold, its release, <see cref="Bbs757Write.PressFor"/>; the release waits on <see cref="CancellationToken.None"/>,
    /// so a press is always let go), then, <see cref="Bbs757LampTest.ReadDelayMs"/> after the release, the latch read
    /// fresh and said once (CORE-7: the screen reader said the button; the app says only the read-back): "Lamp test
    /// on", "Lamp test off", or "Warning Test pressed" when it could not be read. Nothing can land, or the button has
    /// no press: "Warning Test unavailable", nothing sent. Nothing is pressed or said once the pilot has switched to
    /// another profile; a switch during the press still sends the release. The awaits keep the UI context, like every
    /// write here. Never throws.
    /// </summary>
    private async Task PressLampTestAsync(Bbs757Control control, Bbs757SimIo io, ScreenReaderAnnouncer announcer)
    {
        bool pressed = false;
        try
        {
            if (_lifetime.IsCancellationRequested) return;   // switched away: nothing is pressed or said
            var press = Bbs757Write.PressFor(control);
            if (press == null || !io.CanWrite())
            {
                SayUnavailable(control, announcer);
                return;
            }

            OnOwnWrite(control.Node);   // plan 13
            io.Write(press.Down);
            pressed = true;
            if (press.Up != null)
            {
                await io.Delay(press.HoldMs, CancellationToken.None);
                io.Write(press.Up);
            }
            if (_lifetime.IsCancellationRequested) return;

            var ct = _lifetime.Token;   // taken after the check: safe on a disposed source
            await io.Delay(Bbs757LampTest.ReadDelayMs, ct);
            double? lit = await io.ReadFresh(Bbs757LampTest.LitVar, ct);
            ct.ThrowIfCancellationRequested();
            announcer.Announce(Bbs757LampTest.Phrase(lit, control.Label));
        }
        catch (OperationCanceledException) { /* switched away: nothing left to say */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{control.Node}: lamp test press failed: {ex.Message}");
            // Pressed but not read back: the button's own confirmation.
            if (pressed && !_lifetime.IsCancellationRequested) announcer.Announce(Bbs757LampTest.Phrase(null, control.Label));
        }
    }
}
