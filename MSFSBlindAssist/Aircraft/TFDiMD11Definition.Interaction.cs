using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Panel writes, state announcements, and hotkey read-outs.
/// </summary>
public partial class TFDiMD11Definition
{
    // Last-known values feeding the composed read-outs. NaN = never sampled, so the first
    // sample is silent — the baseline-first rule every monitor in this app follows, otherwise
    // connecting mid-flight narrates the entire cockpit at you.
    private double _flapRng = double.NaN;
    private double _dialRaw = double.NaN;
    private string _lastFlapSpoken = string.Empty;

    // Annunciator anti-flap. Some MD-11 lamps blink — the pneumatic avionics fan flow light
    // pulses as bleed flow changes when the throttles are advanced — and narrating every on/off is
    // noise a blind pilot does not need. After a few rapid transitions we go quiet until the lamp
    // settles; the first change or two still speak, so a genuine one-shot caption is never delayed
    // or dropped. A lamp a pilot wants fully silent is still one Ctrl+M away.
    private readonly Dictionary<string, double> _lampLastVal = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<long>> _lampChangeTicks = new(StringComparer.Ordinal);
    private const long LampFlapWindowMs = 5000;
    private const int LampFlapThreshold = 3;   // 3+ transitions inside the window ⇒ blinking

    // =================================================================================
    // Writes
    // =================================================================================

    /// <summary>
    /// Every panel write on this aircraft lands here. There is no generic fallback worth having:
    /// the base class would try SetLVar, and writing a control L:var directly is exactly what
    /// TFDi's Integration Guide says bypasses their integrity checks. So this always returns true
    /// — handled — even on failure paths, so nothing leaks through to a direct write.
    /// </summary>
    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        => SetControl(varKey, value, simConnect, announcer);

    /// <summary>
    /// The actual write, callable without a <see cref="SimVarDefinition"/>.
    ///
    /// The panel path arrives through <see cref="HandleUIVariableSet"/>, but the aircraft's own
    /// windows (the FCP dialog's bank limiter and Dial-A-Flap combos) have a node id and a value
    /// and no var def to hand over — and they must not reimplement the walk, because the walk is
    /// where the closed-loop verification and the "did not move" announcement live.
    /// </summary>
    public bool SetControl(string varKey, double value,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        Attach(simConnect);

        if (!_byNodeId.TryGetValue(varKey, out var control))
            return false;   // not ours (a base var) — let the generic path have it

        if (_bus == null) return true;

        switch (control.Kind)
        {
            // Momentary: press AND release. A press-only pulse leaves the button held for the
            // session — the Fenix stuck-button bug, which on that aircraft re-fired the takeoff
            // config test after touchdown.
            //
            // Guarded buttons (cargo fire agents, fuel dump, battery, generator drives, oxygen
            // masks, ditching…) need the cover lifted first. Done off-thread, state-aware and
            // best-effort: if the guard can't be read it is left alone and the press proceeds
            // exactly as before — never worse than today.
            case Md11Kinds.Button:
                if (!string.IsNullOrEmpty(control.GuardId))
                    _ = GuardedPressAsync(control, simConnect, announcer);
                else
                    _bus.Press(control);
                return true;

            // A guard cover is itself a click-toggle (empty value map, one LEFT_BUTTON_DOWN event):
            // pressing it lifts or lowers the cover. Exposed as an operable control so the pilot has
            // a manual open/close — the fallback for when the auto-open above cannot read the state.
            case Md11Kinds.Guard:
                _bus.Press(control);
                return true;

            // The thumbwheel is continuous, not detented: walk it by measured step size, then
            // ALWAYS speak the actual achieved angle (see SetDialAndAnnounce).
            case Md11Kinds.Knob when control.NodeId == Md11FlapSystem.DialKey:
                _ = SetDialAndAnnounce(value, simConnect, announcer);
                return true;

            // Everything detented: closed-loop walk to the target position, debounced so that
            // arrowing through a multi-position combo runs ONE walk (to the final selection),
            // not one concurrent walk per intermediate value — see DebouncedWalk. The guarded
            // members here are the three engine fire handles; DebouncedWalk lifts their cover first.
            case Md11Kinds.Switch:
            case Md11Kinds.Knob:
            case Md11Kinds.KnobPush:
            case Md11Kinds.KnobPushPull:
            case Md11Kinds.Lever:
            case Md11Kinds.Handle:
                _ = DebouncedWalk(control, value, varKey, simConnect, announcer);
                return true;

            // Annunciators are read-only; a write here is a bug upstream, not a no-op to honour.
            case Md11Kinds.Annunciator:
                return true;

            default:
                return true;
        }
    }

    /// <summary>
    /// Runs a walk off the UI thread and reports only genuine failure.
    ///
    /// Success is deliberately SILENT: the screen reader already announced the combo selection,
    /// and re-announcing the landed value would double-speak every set — the global _uiSetEcho
    /// rule. But a walk that does NOT reach its target must say so, because on this aircraft the
    /// pilot has no gauge to check: a silently-failed selection would look identical to a
    /// successful one.
    /// </summary>
    /// <summary>
    /// Sets the Dial-A-Flap thumbwheel, then ALWAYS speaks the angle the wheel actually reached.
    ///
    /// The thumbwheel is analog with no direct-set, so a walk rarely lands EXACTLY on the target
    /// and can only get partway. The generic SafeWalk's "did not move" (spoken on any non-exact
    /// landing) is both wrong — it DID move — and confusing: the screen reader has already read the
    /// combo's target, so the pilot hears one number from the combo and a contradicting "did not
    /// move", with the ReadFlaps read-out showing a third. So here we ignore success/failure and
    /// simply announce the REAL resulting angle — one truth, matching ReadFlaps.
    /// </summary>
    private int _dialSetGen;
    private CancellationTokenSource? _dialWalkCts;
    private volatile bool _dialWalkActive;

    private async Task SetDialAndAnnounce(double targetRaw, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        // Arrowing through the combo fires a SET for every intermediate entry — so a move from 10°
        // to 25° queues fifteen walks that all fight over one wheel (the "no movement / inhibited"
        // chaos in the logs). Collapse to the LAST selection: cancel any walk already running, then
        // debounce briefly so rapid arrowing settles before we drive the wheel at all.
        var gen = ++_dialSetGen;
        int want = (int)Math.Round(_flaps.DegreesFor(targetRaw));
        _dialWalkCts?.Cancel();
        var cts = new CancellationTokenSource();
        _dialWalkCts = cts;
        try { await Task.Delay(350, cts.Token).ConfigureAwait(false); }
        catch (TaskCanceledException) { return; }     // a newer selection superseded this one
        if (gen != _dialSetGen) return;

        // Log the handle position for diagnostics, but don't gate on it — the real cause of a
        // stuck wheel was the read-back firing before the animated value settled, now fixed in the
        // walker's settle-read.
        var handle = sim.GetCachedVariableValue(Md11FlapSystem.LeverKey);
        Log.Info("MD11", $"Dial set: flap handle FLAP_RNG={handle?.ToString("0.#") ?? "null"}, target={want}°.");

        _dialWalkActive = true;
        try
        {
            await _flaps.SetDialRawAsync(targetRaw, sim, _bus, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            Log.Error("MD11", $"Dial-A-Flap set threw: {ex.Message}");
        }
        finally
        {
            _dialWalkActive = false;
        }

        if (gen != _dialSetGen) return;                // superseded during the walk — let the newer one speak
        await Task.Delay(200).ConfigureAwait(false);   // let the final value settle and stream in
        sim.RequestVariable(Md11FlapSystem.DialKey, forceUpdate: true);
        await Task.Delay(120).ConfigureAwait(false);
        var raw = sim.GetCachedVariableValue(Md11FlapSystem.DialKey);
        if (raw == null) return;

        _dialRaw = raw.Value;
        int deg = (int)Math.Round(_flaps.DegreesFor(raw.Value));
        // If it reached the target (within a degree) just confirm it; otherwise say both, so the
        // pilot knows the wheel stopped short rather than silently trusting the combo.
        announcer.Announce(Math.Abs(deg - want) <= 1
            ? $"Dial-A-Flap {deg} degrees"
            : $"Dial-A-Flap {deg} degrees, could not reach {want}");
    }

    // Per-control debounce for the detented walk. Arrowing through a combo fires a SET for every
    // intermediate entry; without this each one spawned its own WalkAsync, several ran at once on
    // the SAME control, and each force-requested the state var every ~150 ms until the SimConnect
    // send queue flooded (error 0xC00000B0) — reads then returned null, the walk read "state var
    // unreadable", and every one reported "did not move" (the autobrake symptom in the logs). The
    // Dial-A-Flap path already collapses to the last selection this way (SetDialAndAnnounce); this
    // brings switches, knobs, guards, levers and handles to parity.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _walkGen = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _walkCts = new();

    /// <summary>
    /// Runs ONE walk per control at a time, to the LAST selection. Cancels any walk already running
    /// on the same control, debounces briefly so rapid arrowing settles, then walks — passing the
    /// cancellation token down so a walk superseded mid-flight stops force-requesting immediately
    /// instead of adding to the SimConnect flood.
    /// </summary>
    private async Task DebouncedWalk(Md11Control control, double target, string varKey,
        SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (_bus == null) return;
        var node = control.NodeId;

        var gen = _walkGen.AddOrUpdate(node, 1, (_, g) => g + 1);
        if (_walkCts.TryGetValue(node, out var prior)) { try { prior.Cancel(); } catch { /* already disposed */ } }
        var cts = new CancellationTokenSource();
        _walkCts[node] = cts;

        try { await Task.Delay(300, cts.Token).ConfigureAwait(false); }
        catch (TaskCanceledException) { return; }        // a newer selection superseded this one
        if (_walkGen.TryGetValue(node, out var cur) && cur != gen) return;

        // Lift the guard first if this control has one (the three engine fire handles). State-aware
        // and best-effort — a no-op when there is no guard, when it is already open, or when its
        // state cannot be read; it never toggles a guard it cannot see.
        await EnsureGuardOpenAsync(control, sim).ConfigureAwait(false);
        if (cts.IsCancellationRequested) return;

        try
        {
            await SafeWalk(
                () => Md11SelectorWalker.WalkAsync(control, target, varKey, sim, _bus, cts.Token),
                control, target, sim, announcer, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            if (_walkCts.TryGetValue(node, out var mine) && ReferenceEquals(mine, cts))
                _walkCts.TryRemove(node, out _);
            cts.Dispose();
        }
    }

    private async Task SafeWalk(Func<Task<bool>> walk, Md11Control control, double target,
        SimConnectManager sim, ScreenReaderAnnouncer announcer, CancellationToken ct = default)
    {
        try
        {
            var ok = await walk().ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;   // superseded — a newer selection owns the outcome
            if (!ok)
            {
                // The CEVENT click channel is a rate-limited shared slot, and single clicks often
                // don't land — the switch just sits there. Fall back to writing the control's state
                // L:var directly (the same animation-input var the Dial-A-Flap wheel uses, which
                // proved settable this way where clicks failed). Walk-first keeps any control that
                // the click path DOES drive working as before.
                ok = await TryDirectSetAsync(control, target, sim).ConfigureAwait(false);
            }
            if (ct.IsCancellationRequested) return;
            if (!ok)
            {
                Log.Debug("MD11", $"{control.NodeId}: failed to reach {target} (walk and direct write).");
                announcer.Announce($"{control.DisplayLabel} did not move. It may be guarded, unpowered, or inhibited.");
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection — not a failure; the newer walk speaks for the control.
        }
        catch (Exception ex)
        {
            Log.Error("MD11", $"{control.NodeId}: set threw: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets a detented control by writing its state L:var directly through the calc path, then
    /// reading it back to confirm the write held. Returns true only if the value actually landed on
    /// the target detent — a wasm-owned output that reverts the write returns false, so the caller
    /// still speaks the honest "did not move".
    /// </summary>
    private async Task<bool> TryDirectSetAsync(Md11Control control, double target, SimConnectManager sim)
    {
        if (_bus == null || string.IsNullOrEmpty(control.StateVar)) return false;

        _bus.WriteExternal(control.StateVar, target);
        await Task.Delay(600).ConfigureAwait(false);         // ANIM_LAG is 100–1000 ms
        sim.RequestVariable(control.NodeId, forceUpdate: true);
        await Task.Delay(250).ConfigureAwait(false);

        var actual = sim.GetCachedVariableValue(control.NodeId);
        if (actual == null) return false;

        var ordered = Md11SelectorWalker.OrderedValues(control);
        bool held = ordered.Count > 0
            ? Md11SelectorWalker.PositionIndex(control, ordered, actual.Value)
              == Md11SelectorWalker.PositionIndex(control, ordered, target)
            : Math.Abs(actual.Value - target) <= 0.5;
        Log.Info("MD11", $"{control.NodeId}: direct set to {target} → read {actual.Value:0.##}, held={held}.");
        return held;
    }

    /// <summary>Settle after lifting a guard before actuating the control under it.</summary>
    private const int GuardOpenSettleMs = 250;

    /// <summary>
    /// Lifts a control's guard cover first, if it has one and it is currently closed.
    ///
    /// State-aware and BEST-EFFORT, by design — this must never leave a guarded control worse off
    /// than it is today (a bare press/walk with no guard handling at all):
    ///   • no guard, no guard control, or no open event  → nothing to do, proceed;
    ///   • guard state unreadable                         → LEAVE IT ALONE (never toggle blind —
    ///                                                       that could close an already-open cover);
    ///   • already open                                   → don't toggle (so a repeat press can't
    ///                                                       re-close it);
    ///   • closed                                         → fire the guard's click once, settle.
    /// Every failure path just returns; the actuation then runs exactly as it would have. The guard
    /// is ALSO an operable control (rendered as a button), so if the auto-open ever gets it wrong
    /// the pilot has a manual open/close.
    /// </summary>
    private async Task EnsureGuardOpenAsync(Md11Control control, SimConnectManager sim)
    {
        try
        {
            if (_bus == null || string.IsNullOrEmpty(control.GuardId)) return;
            if (!_byNodeId.TryGetValue(control.GuardId!, out var guard)) return;

            var openEvent = guard.Event("LEFT_BUTTON_DOWN");
            if (openEvent == null) return;   // no way to move it — proceed ungated

            // Read the guard's current state (settle briefly). Unreadable → leave alone.
            sim.RequestVariable(guard.NodeId, forceUpdate: true);
            await Task.Delay(180).ConfigureAwait(false);
            var state = sim.GetCachedVariableValue(guard.NodeId);

            var decision = Md11Guard.Decide(state);
            if (decision != Md11Guard.Action.Open)
            {
                if (decision == Md11Guard.Action.LeaveAlone)
                    Log.Debug("MD11", $"Guard {guard.NodeId} state unreadable — actuating {control.NodeId} ungated.");
                return;
            }

            // Closed → lift it, then settle so the control underneath is actuable.
            _bus.Fire(openEvent.Value);
            await Task.Delay(GuardOpenSettleMs).ConfigureAwait(false);

            // Confirm for the log only; the actuation runs regardless of what the re-read says.
            sim.RequestVariable(guard.NodeId, forceUpdate: true);
            await Task.Delay(120).ConfigureAwait(false);
            var after = sim.GetCachedVariableValue(guard.NodeId);
            Log.Info("MD11", $"Guard {guard.NodeId}: {state.Value.ToString("0.##")} → {after?.ToString("0.##") ?? "null"} " +
                $"before actuating {control.NodeId}.");
        }
        catch (Exception ex)
        {
            // Guard handling must never be the thing that fails a control — swallow and proceed.
            Log.Debug("MD11", $"EnsureGuardOpen for {control.NodeId} threw (ignored): {ex.Message}");
        }
    }

    /// <summary>Lifts the guard (best-effort, state-aware), then fires the button's press/release.</summary>
    private async Task GuardedPressAsync(Md11Control control, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        await EnsureGuardOpenAsync(control, sim).ConfigureAwait(false);
        _bus?.Press(control);
    }

    // =================================================================================
    // State → speech
    // =================================================================================

    /// <summary>
    /// The flap handle and thumbwheel are two vars describing ONE fact ("what flap setting am I
    /// taking off on?"), so they are composed here rather than announced separately. Returning
    /// true consumes the update; MainForm's global echo wrap keeps a combo set from
    /// double-speaking.
    /// </summary>
    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        switch (varName)
        {
            case Md11FlapSystem.LeverKey:
                _flapRng = value;
                AnnounceFlaps(announcer);
                return true;

            case Md11FlapSystem.DialKey:
                _dialRaw = value;
                // Moving the thumbwheel only changes the commanded angle when the handle is
                // actually IN the Dial-A-Flap detent. Elsewhere it is a pre-selection — silent.
                // While OUR walk is driving the wheel, SetDialAndAnnounce owns the single final
                // call-out — don't also narrate every degree the walk sweeps through.
                if (!_dialWalkActive && !double.IsNaN(_flapRng) && _flaps.DetentFor(_flapRng)?.Dial == true)
                    AnnounceFlaps(announcer);
                return true;
        }

        // Silent numeric read-outs: cached for the hotkeys, never narrated on change. Consuming
        // them here suppresses the generic auto-announce (an N1/fuel stream spoken every second).
        // The single exception is the take-off cue: engine N1 first reaching 70% (ATS takeover).
        if (_silentReadouts.Contains(varName))
        {
            HandleN1Callout(varName, value, announcer);
            return true;
        }

        // Annunciator anti-flap: swallow a lamp that is blinking. Returning true consumes the
        // update so neither the generic announce nor the monitor baseline runs; when the lamp
        // settles the next change falls under the threshold and speaks the settled state normally.
        if (_byNodeId.TryGetValue(varName, out var ctrl) && ctrl.Kind == Md11Kinds.Annunciator
            && SuppressAnnunciatorFlap(varName, value))
        {
            return true;
        }

        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    /// <summary>
    /// True when this annunciator update should be suppressed as blink chatter — 3+ transitions
    /// within <see cref="LampFlapWindowMs"/>. The first sighting (baseline) and the first couple of
    /// changes pass through, so a genuine one-shot caption is prompt; only sustained blinking is
    /// dropped. Runs on the UI thread (the event-batch consumer), so the dictionaries need no lock.
    /// </summary>
    private bool SuppressAnnunciatorFlap(string varName, double value)
    {
        if (!_lampLastVal.TryGetValue(varName, out var last))
        {
            _lampLastVal[varName] = value;
            return false;                                   // first sight — baseline-first, let it through
        }
        if (Math.Abs(last - value) < 0.0001) return false;  // unchanged — downstream already dedups
        _lampLastVal[varName] = value;

        long now = Environment.TickCount64;
        if (!_lampChangeTicks.TryGetValue(varName, out var q))
        {
            q = new Queue<long>();
            _lampChangeTicks[varName] = q;
        }
        q.Enqueue(now);
        while (q.Count > 0 && now - q.Peek() > LampFlapWindowMs) q.Dequeue();

        return q.Count >= LampFlapThreshold;
    }

    // Engine N1 take-off cue. The read-outs otherwise stream silently; the ONE moment worth
    // speaking is N1 reaching 70% — where the MD-11's autothrottle takes over on the take-off
    // roll. One combined call-out on the first engine to cross 70%, latched until N1 falls well
    // back (hysteresis) so the next take-off re-arms without jitter re-firing it.
    private readonly double[] _n1 = { double.NaN, double.NaN, double.NaN };
    private bool _n1SeventyAnnounced;

    private void HandleN1Callout(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        int idx = varName switch
        {
            "MD11_ENG1_N1" => 0,
            "MD11_ENG2_N1" => 1,
            "MD11_ENG3_N1" => 2,
            _ => -1,
        };
        if (idx < 0) return;

        _n1[idx] = value;
        double max = double.NegativeInfinity;
        foreach (var n in _n1)
            if (!double.IsNaN(n) && n > max) max = n;
        if (double.IsNegativeInfinity(max)) return;

        if (!_n1SeventyAnnounced && max >= 70.0)
        {
            _n1SeventyAnnounced = true;
            announcer.Announce("N1 70 percent");
        }
        else if (_n1SeventyAnnounced && max < 60.0)
        {
            _n1SeventyAnnounced = false;   // re-arm for the next take-off
        }
    }

    /// <summary>
    /// Speaks the handle position, always with the Dial-A-Flap angle when that is the detent.
    /// Deduplicated on the spoken string: the lever var can re-deliver the same value, and a
    /// thumbwheel walk steps through many raw values that round to the same degree.
    /// </summary>
    private void AnnounceFlaps(ScreenReaderAnnouncer announcer)
    {
        if (double.IsNaN(_flapRng)) return;

        var dial = double.IsNaN(_dialRaw) ? 0 : _dialRaw;
        var text = _flaps.DescribePosition(_flapRng, dial);

        if (string.Equals(text, _lastFlapSpoken, StringComparison.Ordinal)) return;
        _lastFlapSpoken = text;
        announcer.Announce(text);
    }

    /// <summary>
    /// Renders the flap combos' live value. Without this the lever would display the bare
    /// FLAP_RNG number, and the Dial-A-Flap detent — a RANGE, never equal to its representative
    /// value — would match no ValueDescriptions entry at all and show nothing.
    /// </summary>
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        switch (varKey)
        {
            case Md11FlapSystem.LeverKey:
                displayText = _flaps.DescribePosition(value, double.IsNaN(_dialRaw) ? 0 : _dialRaw);
                return true;

            case Md11FlapSystem.DialKey:
                displayText = $"{_flaps.DegreesFor(value).ToString("0", CultureInfo.InvariantCulture)} degrees";
                return true;
        }

        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    // =================================================================================
    // Hotkey read-outs
    // =================================================================================

    /// <summary>
    /// On this aircraft the read-outs are not a convenience — the DUs are WASM-rendered and
    /// unreadable, so these exported L:vars are the only way a blind pilot gets the numbers a
    /// sighted one reads off the glass.
    /// </summary>
    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, System.Windows.Forms.Form parentForm, HotkeyManager hotkeyManager)
    {
        Attach(simConnect);

        switch (action)
        {
            case HotkeyAction.ReadFlaps:
                AnnounceFlapsOnDemand(simConnect, announcer);
                return true;

            // S and F speed are Airbus names, but the CONCEPTS are exact: the MD-11's VSR is
            // the slat retraction speed and VFR is the flap retraction speed. Claiming these two
            // keys means the retraction speeds are one keypress away on the aircraft where they
            // cannot be read off a speed tape.
            case HotkeyAction.ReadSpeedS:
                AnnounceSpeed(simConnect, announcer, "MD11_VSR", "Slat retraction speed");
                return true;

            case HotkeyAction.ReadSpeedF:
                AnnounceSpeed(simConnect, announcer, "MD11_VFR", "Flap retraction speed");
                return true;

            // Ctrl+M — mute individual auto-announced variables. Not optional on this aircraft:
            // 532 annunciator lamps announce by default, because with no readable displays those
            // lamps ARE the instrument panel.
            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                (parentForm as MainForm)?.ShowMd11MonitorManagerDialog();
                return true;

            // Fuel twice, matching the PMDG convention: ReadFuelQuantity in pounds (the unit the
            // aircraft itself reports and the CDU shows), ReadFuelInfo in kilograms for pilots who
            // plan in metric.
            case HotkeyAction.ReadFuelQuantity:
                AnnounceFuel(simConnect, announcer, kilograms: false);
                return true;

            case HotkeyAction.ReadFuelInfo:
                AnnounceFuel(simConnect, announcer, kilograms: true);
                return true;

            // Stock SimVar — nothing MD-11-specific, so it goes through the same shared
            // request/announce path every other aircraft uses.
            case HotkeyAction.ReadGrossWeightKg:
                simConnect.RequestSingleValue(
                    (int)SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT_KG,
                    "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT_KG");
                return true;

            // The lever, not the wheels: "gear down" means the pilot's selection. The actual strut
            // positions (MD11_EXT_*_GEAR) are a different question — a disagreement between them is
            // what the annunciators are for.
            //
            // NOT a boolean. MD11_MIP_GEAR_SW is the lever's 0-25 TRAVEL, and the aircraft's own
            // tooltip tests `>= 20` for Down (CenterInstrument.xml). A `> 0.5` test happens to give
            // the right answer at either end and the WRONG one mid-travel — it would call a lever
            // at 10 "down" while the aircraft calls it up. The control map's {0:Up, 1:Down} is the
            // generator mis-reading the %{if}: the COMPARISON yields the boolean, not the var.
            case HotkeyAction.ReadGear:
            {
                var g = simConnect.GetCachedVariableValue("MD11_MIP_GEAR_SW");
                announcer.AnnounceImmediate(g == null
                    ? "Gear position unavailable"
                    : g.Value >= GearLeverDownThreshold ? "Gear down" : "Gear up");
                return true;
            }

            case HotkeyAction.ReadAltimeter:
            {
                var b = simConnect.GetCachedVariableValue("MD11_CAP_ALTIMETER");
                announcer.AnnounceImmediate(b == null
                    ? "Altimeter unavailable"
                    : $"Altimeter {FormatAltimeter(b.Value)}");
                return true;
            }

            // Output mode Shift+H / S / A / V — the SELECTED value in each FCP window, with the
            // mode that says what the number means. Without these the base class answers with the
            // aircraft's ACTUAL heading/speed from a stock SimVar, which is a different question
            // and quietly the wrong answer.
            case HotkeyAction.ReadHeading:
                announcer.AnnounceImmediate(DescribeHeading(simConnect));
                return true;

            case HotkeyAction.ReadSpeed:
                announcer.AnnounceImmediate(DescribeSpeed(simConnect));
                return true;

            case HotkeyAction.ReadAltitude:
                announcer.AnnounceImmediate(DescribeAltitude(simConnect));
                return true;

            case HotkeyAction.ReadFCUVerticalSpeedFPA:
                announcer.AnnounceImmediate(DescribeVertical(simConnect));
                return true;

            // The four FCP windows take a typed value via MD11_EXTCTL_FCP_* — see Md11Fcp for the
            // live-probe evidence that a write lands in the window.
            case HotkeyAction.FCUSetHeading:
                ShowHeadingDialog(simConnect, announcer, parentForm);
                return true;

            case HotkeyAction.FCUSetSpeed:
                ShowSpeedDialog(simConnect, announcer, parentForm);
                return true;

            case HotkeyAction.FCUSetAltitude:
                ShowAltitudeDialog(simConnect, announcer, parentForm);
                return true;

            case HotkeyAction.FCUSetVS:
                ShowVSDialog(simConnect, announcer, parentForm);
                return true;

            // Ctrl+B — set the captain's altimeter, or STD. MD11_EXTCTL_CAP_BARO, proven live.
            case HotkeyAction.FCUSetBaro:
                ShowBaroDialog(simConnect, announcer, parentForm);
                return true;

            // Push and pull are real, distinct actions on the FCP's speed/heading/altitude knobs
            // (the map's knob_pp kind carries its own PUSH_/PULL_ event pairs).
            case HotkeyAction.FCUHeadingPush:
                PressControlEvents("MD11_CGS_HDG_KB", "PUSH_DOWN", "PUSH_UP");
                return true;

            case HotkeyAction.FCUHeadingPull:
                PressControlEvents("MD11_CGS_HDG_KB", "PULL_DOWN", "PULL_UP");
                return true;

            case HotkeyAction.FCUSpeedPush:
                PressControlEvents("MD11_CGS_SPD_KB", "PUSH_DOWN", "PUSH_UP");
                return true;

            case HotkeyAction.FCUSpeedPull:
                PressControlEvents("MD11_CGS_SPD_KB", "PULL_DOWN", "PULL_UP");
                return true;

            case HotkeyAction.FCUAltitudePush:
                PressControlEvents("MD11_CGS_ALT_KB", "PUSH_DOWN", "PUSH_UP");
                return true;

            case HotkeyAction.FCUAltitudePull:
                PressControlEvents("MD11_CGS_ALT_KB", "PULL_DOWN", "PULL_UP");
                return true;

            // Ctrl+P — the Flight Control Panel (this aircraft's MCP). Tracked so an aircraft
            // switch disposes it along with everything else the definition owns.
            case HotkeyAction.FCUSetAutopilot:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(
                    () => new Forms.MD11.Md11AutopilotWindow(this, simConnect, announcer),
                    w => w.ShowForm());
                return true;
        }

        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    /// <summary>Reads the flap handle on demand, refreshing both vars first.</summary>
    private void AnnounceFlapsOnDemand(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var rng = sim.GetCachedVariableValue(Md11FlapSystem.LeverKey);
        var dial = sim.GetCachedVariableValue(Md11FlapSystem.DialKey);

        if (rng == null)
        {
            announcer.Announce("Flap position unavailable.");
            return;
        }

        _flapRng = rng.Value;
        if (dial != null) _dialRaw = dial.Value;

        // Bypasses the dedupe: an explicit hotkey press must always speak, even if the answer
        // is the same as last time.
        announcer.Announce(_flaps.DescribePosition(_flapRng, double.IsNaN(_dialRaw) ? 0 : _dialRaw));
    }

    /// <summary>
    /// The gear lever's travel (0-25) at or above which the aircraft calls it DOWN. TFDi's own
    /// threshold, from the Gear Lever tooltip in CenterInstrument.xml — not a guess, and not the
    /// 0/1 the control map's value_map claims.
    /// </summary>
    private const double GearLeverDownThreshold = 20;

    /// <summary>
    /// One altimeter setting, in whichever unit it is currently in.
    ///
    /// The var carries BOTH units and TFDi disambiguate by magnitude: their tooltip renders it as
    /// an integer when `> 500` (hectopascals — 1013) and to two decimals otherwise (inches of
    /// mercury — 29.92). Formatting it one way always is wrong half the time: a fixed "0.00" reads
    /// hPa as "1013.00", and a fixed integer reads inHg as "30".
    /// </summary>
    private static string FormatAltimeter(double v) => v > 500
        ? $"{v.ToString("0", CultureInfo.InvariantCulture)} hectopascals"
        : $"{v.ToString("0.00", CultureInfo.InvariantCulture)} inches";

    /// <summary>
    /// Fuel, by tank plus a total.
    ///
    /// Read from TFDi's own exported tank quantities rather than the stock FUEL SimVars: the MD-11
    /// has a tail trim tank and an auxiliary tank that the stock left/right/center model has no
    /// slot for, so the stock read would omit real fuel. With no readable SD page these five vars
    /// are the pilot's only fuel picture.
    /// </summary>
    private static void AnnounceFuel(SimConnectManager sim, ScreenReaderAnnouncer announcer, bool kilograms)
    {
        var tanks = new (string Key, string Name)[]
        {
            ("MD11_OVHD_TANK_1_VAL", "Tank 1"),
            ("MD11_OVHD_TANK_2_VAL", "Tank 2"),
            ("MD11_OVHD_TANK_3_VAL", "Tank 3"),
            ("MD11_OVHD_TANK_AUX_VAL", "Auxiliary"),
            ("MD11_OVHD_TANK_TAIL_VAL", "Tail"),
        };

        var parts = new List<string>(tanks.Length + 1);
        double total = 0;
        var anyRead = false;

        foreach (var (key, name) in tanks)
        {
            var v = sim.GetCachedVariableValue(key);
            if (v == null) continue;
            anyRead = true;
            total += v.Value;
            // An empty aux/tail tank is normal on this aircraft, not a fault — say zero rather than
            // omitting it, so the pilot can tell "empty" from "not reported".
            parts.Add($"{name} {Convert(v.Value).ToString("0", CultureInfo.InvariantCulture)}");
        }

        if (!anyRead)
        {
            announcer.AnnounceImmediate("Fuel quantity unavailable");
            return;
        }

        parts.Add($"Total {Convert(total).ToString("0", CultureInfo.InvariantCulture)} {(kilograms ? "kilograms" : "pounds")}");
        announcer.AnnounceImmediate(string.Join(", ", parts));

        double Convert(double lb) => kilograms ? lb * 0.45359237 : lb;
    }

    /// <summary>
    /// Speaks one exported speed. An unset speed reads 0 from the FMS before it is entered, and
    /// "slat retraction speed zero" is worse than useless to act on — so an unset speed is named
    /// as unset rather than read as a number.
    /// </summary>
    private static void AnnounceSpeed(SimConnectManager sim, ScreenReaderAnnouncer announcer,
        string varKey, string label)
    {
        var v = sim.GetCachedVariableValue(varKey);
        announcer.Announce(v is null or <= 0
            ? $"{label} not set"
            : $"{label} {v.Value.ToString("0", CultureInfo.InvariantCulture)} knots");
    }
}
