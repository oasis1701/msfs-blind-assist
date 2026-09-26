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

    /// <summary>
    /// The last flap read-out text RECORDED — not necessarily spoken. Md11FlapSystem.ReadoutDecision
    /// records the first COMPLETE text silently, as the baseline, and only later changes are both
    /// spoken and recorded; the dedup compares against whatever was recorded last either way.
    /// </summary>
    private string _lastFlapSpoken = string.Empty;

    // Speedbrake: the lever's travel (detents) and its pull (armed), see Md11SpeedbrakeSystem.
    private double _spdbrkRng = double.NaN;
    private double _spdbrkHandle = double.NaN;
    private string _lastSpoilerSpoken = string.Empty;

    // Annunciator anti-flap. Some MD-11 lamps blink — the pneumatic avionics fan flow light
    // pulses as bleed flow changes when the throttles are advanced — and narrating every on/off is
    // noise a blind pilot does not need. After a few rapid transitions we go quiet until the lamp
    // settles; the first change or two still speak, so a genuine one-shot caption is never delayed
    // or dropped. A lamp a pilot wants fully silent is still one Ctrl+M away.
    private readonly Dictionary<string, double> _lampLastVal = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<long>> _lampChangeTicks = new(StringComparer.Ordinal);
    private const long LampFlapWindowMs = 5000;
    private const int LampFlapThreshold = 3;   // 3+ transitions inside the window ⇒ blinking

    /// <summary>Lamps are 0/1 booleans arriving as doubles; anything closer than this is the same value.</summary>
    private const double LampEpsilon = 0.0001;

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
    {
        if (varKey == Md11Squawk.SetKey)
        {
            SetSquawk(value, simConnect, announcer);
            return true;   // always ours: the generic path would send the stock XPNDR_SET event
        }
        if (Md11Radios.IsStandbySetKey(varKey))
        {
            SetComStandby(varKey, value, simConnect, announcer);
            return true;
        }
        if (Md11Radios.IsSwapKey(varKey))
        {
            SwapCom(varKey, simConnect, announcer);
            return true;
        }
        if (Md11Minimums.TryGetSide(varKey, out var minimumsSide))
        {
            SetMinimums(minimumsSide, value, simConnect, announcer);
            return true;
        }
        if (varKey == Md11SpeedbrakeSystem.ArmKey)
        {
            SetGroundSpoilers(value, simConnect, announcer);
            return true;
        }
        return SetControl(varKey, value, simConnect, announcer);
    }

    /// <summary>
    /// The Ground spoilers combo. The lever's single click toggles the pull, and the aircraft
    /// ignores it unless the lever is retracted, so a selection it would ignore is refused with
    /// the reason and nothing is sent. Success announces itself through the pull var (or is the
    /// pilot's own combo pick, which the screen reader already spoke); only a failure is spoken here.
    /// </summary>
    private void SetGroundSpoilers(double target, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        Attach(simConnect);
        _uiContext ??= SynchronizationContext.Current;
        double handle = simConnect.GetCachedVariableValue(Md11SpeedbrakeSystem.ArmKey)
                        ?? (double.IsNaN(_spdbrkHandle) ? 0 : _spdbrkHandle);
        double travel = simConnect.GetCachedVariableValue(Md11SpeedbrakeSystem.LeverKey)
                        ?? (double.IsNaN(_spdbrkRng) ? 0 : _spdbrkRng);
        var why = Md11SpeedbrakeSystem.RefuseArm(target, handle, travel);
        if (why != null) { announcer.Announce(why); return; }
        if ((int)Math.Round(target) == (int)Math.Round(handle)) return;
        if (_bus == null || !_byNodeId.TryGetValue(Md11SpeedbrakeSystem.LeverKey, out var lever)) return;
        var click = lever.Event("LEFT_BUTTON_DOWN");
        if (click is not > 0) return;
        if (!CanDeliver)
        {
            // Before the click, so no read-back is scheduled for it: VerifyGroundSpoilersAsync
            // speaks only a DELIVERED mismatch, so an outage left the arm selection silent.
            announcer.Announce(Md11Fcp.Unavailable(Md11SpeedbrakeSystem.ArmName));
            return;
        }
        int backlogMs = _bus.BacklogMs;   // sampled before the click joins the queue
        _bus.Fire(click.Value);
        _ = VerifyGroundSpoilersAsync(target, backlogMs, simConnect, announcer);
    }

    /// <summary>The pull's settle after the lever's click is WRITTEN, before the read-back is requested — the aircraft's own apply time, kept from the fixed-sleep protocol.</summary>
    private const int ArmSettleMs = 700;

    /// <summary>
    /// The click was QUEUED on the paced bus, so the settle is measured from when it will be
    /// written (<paramref name="backlogMs"/>, the walker's stamp), and the pull is then read on
    /// its next 1 Hz delivery — never a fixed sleep and the cache, which for a batch-covered var
    /// still holds the pre-click pull whenever the next delivery has not landed, and judged a
    /// click the aircraft had taken as "did not arm". Only a DELIVERED mismatch is spoken
    /// (<see cref="Md11SpeedbrakeSystem.ArmReadBack"/>): nothing delivered inside the ceiling
    /// means nothing is spoken and the Ground spoilers row shows the state. The pick itself is
    /// echo-suppressed by MainForm for three seconds, so a false failure here used to be the only
    /// thing the pilot heard.
    /// </summary>
    private Task VerifyGroundSpoilersAsync(double target, int backlogMs, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        return DeferToUiThreadAsync(Md11EventBus.ReadBackDelayMs(ArmSettleMs, backlogMs), async () =>
        {
            var read = await sim.ReadFreshAsync(Md11SpeedbrakeSystem.ArmKey, BatchReadBackTimeoutMs).ConfigureAwait(false);
            Log.Debug("MD11", $"Ground spoiler read-back: {Md11SpeedbrakeSystem.ArmKey}={read?.ToString("0.##") ?? "null"} " +
                $"after {sw.ElapsedMilliseconds} ms (backlog {backlogMs} ms), target {target:0}.");
            var failure = Md11SpeedbrakeSystem.ArmReadBack(target, read);
            if (failure == null) return null;
            return () => announcer.Announce(failure);
        }, "Ground spoiler read-back failed", "Ground spoiler read-back (UI-thread tail) failed");
    }

    /// <summary>
    /// Speaks the ONE refusal for a control that cannot be reached, logs why, and answers
    /// SetControl's "handled". Judged by each branch where that branch decides — per press for the
    /// one-shot kinds, after the debounce for the walked ones — never once per combo row arrowed
    /// over.
    /// </summary>
    private bool RefuseUndeliverable(Md11Control control, double value, ScreenReaderAnnouncer announcer)
    {
        Log.Debug("MD11", $"{control.NodeId}: refused set to {value} — the transport cannot send.");
        announcer.Announce(Md11Fcp.Unavailable(control.DisplayLabel));
        return true;
    }

    /// <summary>
    /// A hardware lever sweeps through the values between detents; only a detent is named, and
    /// only once. Baseline-first: connecting must not narrate the lever's resting position.
    /// </summary>
    private void AnnounceSpoilerTravel(ScreenReaderAnnouncer announcer)
    {
        var detent = Md11SpeedbrakeSystem.DescribeTravel(_spdbrkRng);
        if (detent == null) return;
        // Renders the sentence before comparing it, so a lever in TRAVEL (Continuous + SIM_FRAME +
        // CHANGED) allocates two short-lived strings per frame to discover the detent has not
        // changed. Left alone deliberately: a detent memo beside _lastSpoilerSpoken would have to
        // be reset in step with it at BOTH reset sites, and the two drifting apart would suppress
        // a real announcement — a correctness risk for ~120 gen-0 allocations per second during
        // the few seconds a flight spends moving the lever.
        var text = $"Spoilers {detent.ToLowerInvariant()}";
        if (string.Equals(text, _lastSpoilerSpoken, StringComparison.Ordinal)) return;
        bool first = _lastSpoilerSpoken.Length == 0;
        _lastSpoilerSpoken = text;
        if (!first) announcer.Announce(text);
    }

    /// <summary>The pull var: armed / disarmed / extended, baseline-first, spoken on a real change.</summary>
    private void AnnounceGroundSpoilers(double handle, ScreenReaderAnnouncer announcer)
    {
        bool first = double.IsNaN(_spdbrkHandle);
        bool changed = !first && (int)Math.Round(handle) != (int)Math.Round(_spdbrkHandle);
        _spdbrkHandle = handle;
        if (first || !changed) return;
        var text = Md11SpeedbrakeSystem.DescribeArm(handle);
        if (text != null) announcer.Announce(text);
    }

    /// <summary>
    /// How long a stock tuning event gets before the read-back is REQUESTED. The read then
    /// completes on the frequency's next 1 Hz delivery (ReadFreshAsync), so this is only the
    /// aircraft's own apply time — TFDi's radio panel drives the stock var, and how quickly is
    /// unmeasured (the probe said "held", not how soon) — kept rather than cut.
    /// </summary>
    private const int ComTuneSettleMs = 1500;

    /// <summary>
    /// A typed standby frequency: validated (a refusal is spoken, nothing is sent), then the
    /// stock standby-set event. The COM announcer speaks the frequency the radio then reports;
    /// this only speaks up when nothing changed, because a set the aircraft ignored would
    /// otherwise be silent and look exactly like one that worked.
    /// </summary>
    private void SetComStandby(string varKey, double typedMhz, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        Attach(simConnect);
        _uiContext ??= SynchronizationContext.Current;
        if (!Md11Radios.TryParseMhz(typedMhz, out var hz, out var error))
        {
            announcer.Announce(error);
            return;
        }
        int idx = Md11Radios.RadioIndex(varKey);
        string standbyKey = $"COM_STANDBY_FREQUENCY:{idx}";
        double targetKhz = hz / 1000.0;
        // The STOCK-event predicate, not the calc-path one: tuning goes out as a SimConnect event
        // and needs no MobiFlight module. VerifyComAsync speaks only a DELIVERED mismatch, so
        // without this an outage swallowed the typed frequency and said nothing.
        if (!simConnect.CanSendEvent)
        {
            announcer.Announce(Md11Fcp.Unavailable($"COM {idx} standby"));
            return;
        }
        simConnect.SendEvent(Md11Radios.StandbySetEvent(idx), hz);
        _ = VerifyComAsync(standbyKey, targetKhz, $"COM {idx} standby did not change", simConnect, announcer);
    }

    /// <summary>The Transfer button: the stock swap event, then a check that the active slot moved.</summary>
    private void SwapCom(string varKey, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        Attach(simConnect);
        _uiContext ??= SynchronizationContext.Current;
        int idx = Md11Radios.RadioIndex(varKey);
        string activeKey = $"COM_ACTIVE_FREQUENCY:{idx}";
        double? expected = _com.Last($"COM_STANDBY_FREQUENCY:{idx}");   // the standby we saw last is what should become active
        if (!simConnect.CanSendEvent)
        {
            announcer.Announce(Md11Fcp.Unavailable($"COM {idx} transfer"));
            return;
        }
        simConnect.SendEvent(Md11Radios.SwapEvent(idx));
        if (expected is double e && Md11Radios.InAirband(e))
            _ = VerifyComAsync(activeKey, e, $"COM {idx} transfer did not take", simConnect, announcer);
    }

    /// <summary>
    /// After the settle, read the frequency on its next delivery and speak only a DELIVERED
    /// mismatch ("… did not change, still 124.850"); a match was already announced by the COM
    /// announcer when the variable moved, and nothing delivered is no verdict (logged, not spoken).
    /// </summary>
    private Task VerifyComAsync(string key, double targetKhz, string failure, SimConnectManager sim, ScreenReaderAnnouncer announcer)
        => DeferToUiThreadAsync(ComTuneSettleMs, async () =>
        {
            var read = await sim.ReadFreshAsync(key, BatchReadBackTimeoutMs).ConfigureAwait(false);
            var sentence = Md11Radios.TuneReadBack(targetKhz, read, failure);
            if (sentence == null)
            {
                if (read == null) Log.Debug("MD11", $"COM tuning read-back: nothing delivered for {key} within {BatchReadBackTimeoutMs} ms — no verdict.");
                return null;
            }
            return () => announcer.Announce(sentence);
        }, "COM tuning read-back failed", "COM tuning read-back (UI-thread tail) failed");

    /// <summary>
    /// The inbox is consumed within the next FCC cycle — the EXTCTL apply allowance every inbox
    /// read-back shares (<see cref="Md11Fcp.VerifyAfterMs"/>, where the reasoning lives). The
    /// export is then read on its next 1 Hz delivery; the second sleep that used to out-wait the
    /// batch is gone with the fixed-sleep protocol.
    /// </summary>
    private const int MinimumsSettleMs = Md11Fcp.VerifyAfterMs;

    /// <summary>
    /// The typed minimums: validated (a refusal is spoken and nothing is sent), written to the
    /// side's inbox, then read back and spoken — including, when the side's mode switch is on
    /// Radio, the fact that the display cannot show the baro value just set (Md11Minimums).
    /// </summary>
    private void SetMinimums(Md11MinimumsSide side, double typed, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        Attach(simConnect);
        _uiContext ??= SynchronizationContext.Current;
        if (!Md11Minimums.TryParse(typed, out var feet, out var error))
        {
            announcer.Announce(error);
            return;
        }
        if (_bus == null) return;
        if (!CanDeliver)
        {
            // Refuse BEFORE the write, so no read-back is scheduled for a value that never left.
            // This path was not silent — it was WORSE: VerifyMinimumsAsync always speaks, and with
            // nothing delivered Md11Minimums.Confirmation says "<side> baro minimums set to N feet,
            // the display did not report back", which claims the set happened. Nothing had been
            // sent at all.
            announcer.Announce(Md11Fcp.Unavailable(side.Name));
            return;
        }
        _bus.WriteExternal(side.WriteVar, feet);
        _ = VerifyMinimumsAsync(side, feet, simConnect, announcer);
    }

    private Task VerifyMinimumsAsync(Md11MinimumsSide side, int feet, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        return DeferToUiThreadAsync(MinimumsSettleMs, async () =>
        {
            // Both keys are batch-covered (the read-back and the mode switch's silent mirror), so
            // each fresh read completes on its next 1 Hz delivery — one period at most, and the
            // ceiling only bounds a delivery that never comes. Requested together so neither waits
            // behind the other; each completes on its own batch's next delivery (the 528 batch vars
            // ride two batches sorted by name, so the two need not share one). A read that is not
            // delivered stays null, and Confirmation says so.
            var readTask = sim.ReadFreshAsync(side.ReadKey, BatchReadBackTimeoutMs);
            var modeTask = sim.ReadFreshAsync(side.ModeKey, BatchReadBackTimeoutMs);
            var read = await readTask.ConfigureAwait(false);
            var mode = await modeTask.ConfigureAwait(false);
            Log.Debug("MD11", $"Minimums read-back: {side.ReadKey}={read?.ToString("0.##") ?? "null"} mode={mode?.ToString("0.##") ?? "null"} " +
                $"after {sw.ElapsedMilliseconds} ms (typed {feet}).");
            bool? modeIsBaro = mode is double m ? m > 0.5 : null;
            var sentence = Md11Minimums.Confirmation(side, feet, read, modeIsBaro);
            return () => announcer.Announce(sentence);
        }, "Minimums read-back failed", "Minimums read-back (UI-thread tail) failed");
    }

    /// <summary>How long the aircraft gets to accept the fourth digit before the read-back.</summary>
    private const int SquawkCommitMs = 1000;

    /// <summary>
    /// The typed squawk: validated (a refusal is spoken and nothing is sent), then the panel's
    /// own digit keys are pressed in order through the paced CEVENT bus, then the stock
    /// TRANSPONDER CODE:1 is force-read and the result is spoken — the code when it took, and
    /// what the transponder actually reads when it did not, because an entry that silently
    /// failed would look exactly like one that worked.
    /// </summary>
    private void SetSquawk(double typed, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        Attach(simConnect);
        _uiContext ??= SynchronizationContext.Current;
        if (!Md11Squawk.TryParse(typed, out var code, out var error))
        {
            announcer.Announce(error);
            return;
        }
        if (_bus == null) return;
        if (!CanDeliver)
        {
            // The digits ride the CEVENT bus (the panel's own keypad), so this is the calc-path
            // predicate. Refuse before BeginEntry: nothing is pressed and no read-back is armed.
            // Like the minimums, this was not silent but WRONG — the entry's own confirmation says
            // "Squawk 1200 entered, the transponder did not report back", which tells the pilot the
            // code went in when not one digit was written.
            announcer.Announce(Md11Fcp.Unavailable(Md11Squawk.Name));
            return;
        }
        _squawk.BeginEntry();   // UI thread: deliveries during the entry are tracked, not spoken
        var previous = _squawkEntries;
        _squawkEntries = RunSquawkEntryAfterAsync(previous, code, simConnect, announcer);
    }

    /// <summary>
    /// Typed entries run one after another. Each presses four keypad digits over the paced bus, so
    /// two overlapping entries would interleave their digits into a code nobody typed; instead the
    /// second waits for the first to finish (read-back included) and then enters its own, so the
    /// transponder ends on the code typed last and each entry gets its own confirmation — a pilot
    /// who corrects a typo with a quick second Set hears "Squawk 1200." then "Squawk 1207.". Only
    /// ever touched on the UI thread (the panel's Set button).
    /// </summary>
    private Task _squawkEntries = Task.CompletedTask;

    private async Task RunSquawkEntryAfterAsync(Task previous, string code, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        try { await previous.ConfigureAwait(false); }
        catch (Exception ex) { Log.Debug("MD11", $"Previous squawk entry faulted: {ex.Message}"); }
        await SetSquawkAsync(code, sim, announcer).ConfigureAwait(false);
    }

    /// <summary>
    /// How long the entry's read-back waits for the code to be DELIVERED. The code rides the 1 Hz
    /// continuous batch, so a forced read is honoured on the next delivery — up to a whole period
    /// plus jitter — rather than answered at once; a fixed sleep was either too short (a
    /// successful entry read back as "did not take", then contradicted by the change
    /// announcement) or needlessly long. ReadFreshAsync completes on the delivery itself; this
    /// is only the ceiling, two periods with margin.
    /// </summary>
    private const int SquawkReadBackTimeoutMs = BatchReadBackTimeoutMs;

    /// <summary>
    /// The ceiling on a fresh read of a BATCH-COVERED key — the ground spoiler pull, the COM
    /// frequencies, the minimums and altimeter exports, the squawk: the read completes on the
    /// next 1 Hz delivery, so this is one period plus one of margin, and only bounds a delivery
    /// that never comes (a disconnect). An individual-def key answers on the next dispatch and
    /// reads under the walker's <see cref="Md11SelectorWalker.FreshReadTimeoutMs"/> (1200) —
    /// the direct-set "held" check — completing early; the guard decision and the press
    /// feedback's latch, which sit on an actuation's path, use the shorter
    /// <see cref="GuardReadTimeoutMs"/>.
    /// </summary>
    private const int BatchReadBackTimeoutMs = 2500;

    private async Task SetSquawkAsync(string code, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        try
        {
            foreach (var digit in code)
            {
                if (!_byNodeId.TryGetValue(Md11Squawk.DigitButton(digit), out var key) || _bus == null) return;
                await _bus.PressAndSettleAsync(key, settleMs: 150).ConfigureAwait(false);
            }
            // Awaited INSIDE this try: the confirmation is posted before the finally posts
            // EndEntry, and a read that throws is logged with this method's own words.
            await DeferToUiThreadAsync(SquawkCommitMs, async () =>
            {
                var read = await sim.ReadFreshAsync(Md11Squawk.CodeKey, SquawkReadBackTimeoutMs).ConfigureAwait(false);
                var readBack = read is double v ? Md11Squawk.Decode(v) : null;
                return () => announcer.Announce(Md11Squawk.Confirmation(code, readBack));
            }, "Squawk entry failed", "Squawk entry (UI-thread tail) failed").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug("MD11", $"Squawk entry failed: {ex.Message}");
        }
        finally
        {
            // Posted after the confirmation (same context, in order) and on EVERY exit — a
            // missing digit key or an exception must not leave the change announcement muted
            // for the rest of the session.
            OnUiThread(() => _squawk.EndEntry());
        }
    }

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

        // The three altimeter STD toggles (Md11StdToggles): two rows of this app's own and the
        // standby display's STD button, which has no events of its own. Each presses its altimeter
        // knob's PUSH as a CEVENT pair. Ahead of the map lookup — the two synthetic keys are not map
        // controls — and never through the knob's own row, which stays a read-only export row.
        if (Md11StdToggles.TryGet(varKey, out var std))
        {
            PressStdToggle(std, simConnect, announcer);
            return true;
        }

        if (!_byNodeId.TryGetValue(varKey, out var control))
            return false;   // not ours (a base var) — let the generic path have it

        if (_bus == null) return true;

        // Captured once: every caller (MainForm's panel handlers, the FCP dialog's combos in
        // Md11AutopilotWindow) invokes SetControl on the UI thread. PressFeedbackAsync needs this
        // to hop back to the UI thread after its ConfigureAwait(false) delays — see OnUiThread in
        // the State partial.
        _uiContext ??= SynchronizationContext.Current;

        // A control whose state var is one of TFDi's read-only EXPORTS (the FCP mode knobs, the
        // EFIS minimums caps) has no walkable axis here: its wheel moves the heading/speed/V-S/
        // minimums VALUE, never the mode a combo would name, so the walk could only stall and
        // TryDirectSetAsync would then raw-write the export — WriteExternal("MD11_CAP_MINIMUMS", 0)
        // zeroed the captain's minimums silently. Those rows render read-only (BuildControlVariable),
        // so no panel path reaches this, and Md11AutopilotWindow's two SetControl callers (the bank
        // limiter MD11_CGS_HDG_BASE_KB and the Dial-A-Flap) are not export-backed: the guard is
        // dormant by construction and exists so a future caller cannot write an export by accident.
        // The mode is switched by its own button (MD11_CGS_*_BT, Md11Minimums.ModeSwitch).
        if (Md11ExportBacked.IsReadOnly(control, _exportVars))
        {
            Log.Debug("MD11", $"{control.NodeId}: refused set to {value} — its state var {control.StateVar} is a read-only export.");
            return true;
        }

        // A COMPOSITE (the engine fire handles and the Elevator Feel knob, Md11CompositeState) has no
        // axis a combo could walk: its row reads the outer var while its wheel turns the inner one (a
        // handle's pull and its bottle-discharge rotation, the knob's MANUAL latch and the knob), so a
        // walk could never land — on a handle it could only risk a discharge click, and on the knob
        // the direct-write fallback wrote the latch. The row renders read-only (BuildControlVariable;
        // MainForm builds it as the status box, Utils.PanelRowRules), so no panel path reaches this —
        // but a caller that does is told why, on the UI thread every SetControl caller runs on.
        // Operating them is a follow-up: the handles need a live fire to verify, and the map carries
        // no event for the knob's latch.
        if (control.Composite != null)
        {
            Log.Debug("MD11", $"{control.NodeId}: refused set to {value} — a composite control has no walkable axis.");
            announcer.Announce(Md11CompositeState.RefusalSentence(control.DisplayLabel));
            return true;
        }

        // The undeliverable refusal is NOT judged here. A detented combo commits a SET per row the
        // pilot arrows over, so judged at this point it spoke "<label> unavailable" once for every
        // row passed — the defect that moved the speedbrake's refusal behind the debounce. Each
        // branch below judges it where that branch decides: the one-shot kinds per press, and the
        // walked kinds once their debounce has settled, on the selection that settled
        // (DebouncedWalk; the Dial-A-Flap does the same behind its own). It stays LAST among the
        // refusals either way, so a control-specific reason (read-only export, composite) still
        // wins — those are true whatever the connection, and more use to the pilot.
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
            {
                // One-shot: judged per press, which is exactly once per pilot action.
                if (!CanDeliver) return RefuseUndeliverable(control, value, announcer);
                bool guarded = !string.IsNullOrEmpty(control.GuardId);
                long pressedAt = Environment.TickCount64;
                _gate.NotePress(control.NodeId, pressedAt);
                // The feedback's clock starts when the press is QUEUED and adds the bus backlog it
                // then waits behind (Pending × MinGapMs — the stamp every walker click and
                // PressAndHoldAsync carry). A guarded press is queued only after its guard chain
                // has run, so GuardedPressAsync hands that moment over itself; the hold-to-test
                // path keeps an estimate (GuardedPressExtraMs) because its feedback must speak at
                // the settle, not after the 3 s hold.
                Task<int> queued;
                if (Md11TestButtons.IsHoldToTest(control.NodeId))
                {
                    queued = Task.FromResult(_bus.BacklogMs + (guarded ? GuardedPressExtraMs : 0));
                    _ = HoldTestButtonAsync(control, simConnect, guarded);   // held, so its lights get seen
                }
                else if (guarded)
                {
                    queued = GuardedPressAsync(control, simConnect);
                }
                else
                {
                    queued = Task.FromResult(_bus.BacklogMs);
                    _bus.Press(control);
                }
                _ = PressFeedbackAsync(control, simConnect, announcer, queued, pressedAt);
                return true;
            }

            // A guard cover is itself a click-toggle (empty value map, one LEFT_BUTTON_DOWN event):
            // pressing it lifts or lowers the cover. Exposed as an operable control so the pilot has
            // a manual open/close — the fallback for when the auto-open above cannot read the state.
            case Md11Kinds.Guard:
                if (!CanDeliver) return RefuseUndeliverable(control, value, announcer);
                _bus.Press(control);
                _ = GuardRefreshAsync(control, simConnect);
                return true;

            // The thumbwheel is continuous, not detented: ONE write of its own backing var, never
            // a CEVENT walk; silent when it lands, a shortfall is spoken (see SetDialAndAnnounce).
            case Md11Kinds.Knob when control.NodeId == Md11FlapSystem.DialKey:
                _ = SetDialAndAnnounce(value, simConnect, announcer);
                return true;

            // The speedbrake lever's wheel is dead while the pull is up — the aircraft's own
            // template gates it — so a selection it would ignore is refused with the reason
            // rather than walked into "did not move" (Md11SpeedbrakeSystem.RefuseTravel).
            // DebouncedWalk judges it after the debounce: only the selection that settles, against
            // the pull as it reads then. Judged here, on every commit, arrowing through the combo
            // spoke the refusal once for each row it passed — at pull 2 even on the way to the
            // Retracted the sentence itself advises.
            case Md11Kinds.Lever when control.NodeId == Md11SpeedbrakeSystem.LeverKey:
                _ = DebouncedWalk(control, value, varKey, simConnect, announcer,
                    refuse: target => Md11SpeedbrakeSystem.RefuseTravel(target,
                        simConnect.GetCachedVariableValue(Md11SpeedbrakeSystem.ArmKey)
                        ?? (double.IsNaN(_spdbrkHandle) ? 0 : _spdbrkHandle)));
                return true;

            // Everything detented: closed-loop walk to the target position, debounced so that
            // arrowing through a multi-position combo runs ONE walk (to the final selection),
            // not one concurrent walk per intermediate value — see DebouncedWalk. The guarded
            // members here are the three engine fire handles; DebouncedWalk lifts their cover first.
            // ONE spelling of "walkable", shared with BuildControlVariable's registration and with
            // Md11ExportBacked.IsReadOnly. C# cannot build case labels from a list, but it can
            // guard one — and the six kinds written out here three times could be edited in one
            // place and still compile: dropping Md11Kinds.Handle from this group alone made the
            // three engine fire handles silently unwalkable while the tripwire test, which only
            // reflects over Md11Kinds membership, went on passing.
            case string positional when Md11ExportBacked.IsPositional(positional):
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
    /// Sets the Dial-A-Flap thumbwheel, then speaks ONLY a shortfall.
    ///
    /// The set is one write of the wheel's own backing var (<see cref="Md11FlapSystem.SetDialRawAsync"/>
    /// — never a CEVENT walk), so a landed set is SILENT: the screen reader already read the combo's
    /// pick, and re-announcing the landed angle double-speaks every set — the rule
    /// <see cref="DebouncedWalk"/> follows. A wheel that settles on any OTHER whole degree
    /// says so ("Dial-A-Flap 17 degrees, could not reach 20", <see cref="Md11FlapSystem.DialSetShortfall"/>),
    /// because the pilot has no gauge to check, and says it through <see cref="OnUiThread"/>: the
    /// awaits below resume on the thread pool, where ScreenReaderAnnouncer is unreliable.
    /// </summary>
    private int _dialSetGen;
    private CancellationTokenSource? _dialSetCts;

    /// <summary>
    /// How long the Dial-A-Flap read-back waits for the written value to reach the wheel's own
    /// L:var, and how often it looks. The var streams SIM_FRAME + CHANGED and the write changes it,
    /// so the write's own delivery lands in the cache — polling the cache IS awaiting delivery, with
    /// no request of our own. The path is calc → WASM → L:var → SIM_FRAME, two to three sim frames,
    /// so a landed set answers on the first poll or two; the ceiling only bounds a set that never
    /// arrives, and the exact compare needs it so a slow frame cannot invent a miss.
    /// </summary>
    private const int DialSettleTimeoutMs = 900;
    private const int DialPollMs = 50;

    private async Task SetDialAndAnnounce(double targetRaw, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        // Arrowing through the combo fires a SET for every intermediate entry — so a move from 10°
        // to 25° queues fifteen sets that all fight over one wheel (the "no movement / inhibited"
        // chaos in the logs, from when this path was a CEVENT walk). Collapse to the LAST
        // selection: cancel any set already running, then debounce briefly so rapid arrowing
        // settles before we drive the wheel at all.
        var gen = ++_dialSetGen;
        int want = (int)Math.Round(_flaps.DegreesFor(targetRaw));
        // Cancel and DISPOSE the source this one supersedes. Replacing the field without disposing
        // leaked one CancellationTokenSource per set — fifteen for a single arrow-through from 10°
        // to 25°, since every intermediate entry fires one. Only the CURRENT source is left in the
        // field; CancelWalks cancelling it after it has finished is harmless, and
        // Md11WalkCancellation.CancelAll already tolerates a source someone else disposed (which is
        // exactly what DebouncedWalk's own finally produces).
        var superseded = _dialSetCts;
        superseded?.Cancel();
        superseded?.Dispose();
        var cts = new CancellationTokenSource();
        _dialSetCts = cts;
        try { await Task.Delay(350, cts.Token).ConfigureAwait(false); }
        catch (TaskCanceledException) { return; }     // a newer selection superseded this one
        if (gen != _dialSetGen) return;

        // Log the handle position for diagnostics, but don't gate on it — the real cause of a
        // stuck wheel was the read-back firing before the animated value settled, now fixed in the
        // walker's settle-read.
        var handle = sim.GetCachedVariableValue(Md11FlapSystem.LeverKey);
        Log.Info("MD11", $"Dial set: flap handle FLAP_RNG={handle?.ToString("0.#") ?? "null"}, target={want}°.");

        var bus = _bus;                                // read once: Dispose may null it
        if (bus == null) return;
        if (!CanDeliver)
        {
            // Refuse before the write: SetDialRawAsync always reports success (it is one
            // WriteExternal), so the only signal left was the read-back below — and during an
            // outage that delivers nothing and returns SILENTLY, so the wheel never moved and the
            // pilot heard nothing at all while the combo showed their pick.
            OnUiThread(() => announcer.Announce(Md11Fcp.Unavailable(Md11FlapSystem.DialName)));
            return;
        }
        try
        {
            await _flaps.SetDialRawAsync(targetRaw, sim, bus).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("MD11", $"Dial-A-Flap set threw: {ex.Message}");
        }

        if (gen != _dialSetGen) return;                // superseded during the write — let the newer one speak

        // WAIT FOR THE WRITTEN VALUE TO ARRIVE, don't sleep a fixed time and read whatever is there.
        // This var streams on its own SIM_FRAME + CHANGED subscription and the write changes it, so
        // the subscription delivers the landed value into the cache: the cache IS this var's delivery
        // channel, and "await delivery" means polling it until the wheel reaches the pick. Read the
        // cache itself, never ReadFreshAsync: with an empty cache that issues a forced read of its
        // own, whose PRE-write answer would use up MainForm's echo suppression (the landed angle then
        // spoken over the pick) and whose 1.2 s wait per pass would stretch this loop far past its
        // ceiling. With a fixed 200 ms settle the compare below could read the PRE-write value on a
        // slow frame (calc → WASM → L:var → SIM_FRAME is two to three frames) and invent a
        // "could not reach" for a set that landed a moment later — a false failure the exact compare
        // must never produce. A set that lands exits on the first poll; only a genuine miss waits out
        // the deadline before speaking.
        int wantedRaw = 0;
        double? raw = null;
        for (var waited = 0; waited <= DialSettleTimeoutMs; waited += DialPollMs)
        {
            await Task.Delay(DialPollMs).ConfigureAwait(false);
            // Re-checked on every pass, not only before the wait: a newer selection can supersede
            // this one — or Dispose can bump the generation — and past this point the value read is
            // `sim`'s cache, which after an aircraft switch belongs to the NEXT aircraft.
            if (gen != _dialSetGen) return;
            raw = sim.GetCachedVariableValue(Md11FlapSystem.DialKey);
            if (raw == null) continue;
            wantedRaw = _flaps.NearestSelectableDegrees(raw.Value);
            if (wantedRaw == want) break;              // landed — nothing to say
        }
        if (raw == null || gen != _dialSetGen) return;

        // A landed set is silent — the screen reader already read the pick. A wheel that settled on
        // any OTHER whole degree says so, in the display row's own rounding, on the UI thread: this
        // tail runs on the thread pool after the awaits above.
        var shortfall = Md11FlapSystem.DialSetShortfall(want, wantedRaw);
        if (shortfall != null) OnUiThread(() => announcer.Announce(shortfall));
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
    ///
    /// <paramref name="refuse"/>, when given, is asked once the debounce has settled, for the
    /// selection that settled: a reason is spoken, nothing is sent, and the walk ends as one that
    /// did not move. Only the speedbrake lever passes one (SetControl).
    /// </summary>
    private async Task DebouncedWalk(Md11Control control, double target, string varKey,
        SimConnectManager sim, ScreenReaderAnnouncer announcer, Func<double, string?>? refuse = null)
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

        bool cancelled;
        try
        {
            // Asked HERE, once the debounce has settled, so a combo arrowed through is judged once,
            // on the selection it settles on, against the aircraft as it is now — not once per row
            // passed. A refusal is said once (posted to the UI thread: this continues on a pool
            // thread), nothing is sent, and the finally ends it like a walk that did not move. It
            // answers the pilot's own pick, like SafeWalk's "did not move", so no Ctrl+M row mutes
            // it. It comes after EnsureGuardOpenAsync only nominally: the one caller, the speedbrake
            // lever, has no guard, so that returned at once.
            // Judged HERE for the same reason, and before the control-specific refusal: one pick,
            // one refusal. In SetControl this fired per row arrowed over.
            if (!CanDeliver)
            {
                Log.Debug("MD11", $"{control.NodeId}: refused set to {target} — the transport cannot send.");
                OnUiThread(() => announcer.Announce(Md11Fcp.Unavailable(control.DisplayLabel)));
                return;
            }

            if (refuse?.Invoke(target) is string why)
            {
                OnUiThread(() => announcer.Announce(why));
                return;
            }

            await SafeWalk(
                () => Md11SelectorWalker.WalkAsync(control, target, varKey, sim, _bus, cts.Token),
                control, target, sim, announcer, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            cancelled = cts.IsCancellationRequested;
            bool mine = _walkCts.TryGetValue(node, out var current) && ReferenceEquals(current, cts);
            if (mine) _walkCts.TryRemove(node, out _);
            cts.Dispose();
            // Gate down (see ProcessSimVarUpdate): one force-read so the panel combo re-syncs to
            // where the control actually is. WinForms ignores a same-index set, so a landed walk
            // stays silent; a failed one snaps the combo to the real position once, beside the
            // "did not move" message. A superseded walk leaves the re-sync to the walk that owns it.
            // Not for the flap handle or the speedbrake lever (a refused pick leaves through here
            // too): they stream SIM_FRAME + CHANGED, so this goes out on their SEED id
            // (FreshReadPolicy.RouteRequest) and its forced delivery re-enters ProcessSimVarUpdate,
            // whose deduped read-outs stay silent — and ProcessSimVarUpdate consumes every delivery
            // of them, so MainForm never re-syncs their combos after the panel is built (the
            // delivery only corrects MainForm's stored value). Each combo keeps the pilot's pick.
            if (mine && !cancelled) sim.RequestVariable(varKey, forceUpdate: true);
        }
    }

    /// <summary>
    /// Cancels everything in flight — the detented walks in <see cref="_walkCts"/> and the
    /// Dial-A-Flap set (one direct write, never a walk) — so none finishes against the NEXT
    /// aircraft's cleared registrations and speaks "did not move", or a shortfall, for a control
    /// that was never asked to move there. Called from
    /// <c>Dispose</c> only. A cancelled walk leaves through SafeWalk's ct checks (silent),
    /// DebouncedWalk's finally removes its own entry and skips the combo re-sync, and the
    /// generation bump makes a Dial-A-Flap set already past its cancellable await return before
    /// its announce. The <see cref="_walkCts"/> snapshot can carry a source its walk disposed a
    /// moment ago — the helper tolerates that.
    /// </summary>
    private void CancelWalks()
    {
        _dialSetGen++;
        var sources = _walkCts.Values.Cast<CancellationTokenSource?>().Append(_dialSetCts);
        var n = Md11WalkCancellation.CancelAll(sources);
        if (n > 0) Log.Info("MD11", $"Definition disposed with {n} walk(s) in flight — cancelled.");
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
                // don't land — the switch just sits there. Fall back to writing the control's OWN
                // state L:var directly (it moved the fuel switches and IRS selectors in the early
                // logs, before ToggleCoreAsync existed). Gated by Md11DirectSet.Refuse: only the var
                // this control's row reads back, never the speedbrake/flap/gear levers, whose state
                // vars are not their command encoding. Walk-first keeps any control that the click
                // path DOES drive working as before.
                ok = await TryDirectSetAsync(control, target, sim).ConfigureAwait(false);
            }
            if (ct.IsCancellationRequested) return;
            if (!ok)
            {
                Log.Debug("MD11", $"{control.NodeId}: failed to reach {target} (walk and direct write).");
                OnUiThread(() => announcer.Announce(Md11WalkFailure.Sentence(control.DisplayLabel)));
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection — not a failure; the newer walk speaks for the control.
        }
        catch (Exception ex)
        {
            Log.Error("MD11", $"{control.NodeId}: set threw: {ex.Message}");
            // A walk that THREW failed as surely as one that returned false, and this method's own
            // contract applies: a silently-failed selection looks identical to a successful one.
            // Silent only when the definition is gone (Dispose nulled the bus) or a newer selection
            // owns the outcome.
            if (Md11WalkFailure.AfterThrow(control.DisplayLabel, disposed: _bus == null,
                    cancelled: ct.IsCancellationRequested) is { } sentence)
                OnUiThread(() => announcer.Announce(sentence));
        }
    }

    /// <summary>
    /// Sets a detented control by writing its state L:var directly through the calc path, then
    /// reading it back to confirm the write held. Returns true only if the value actually landed on
    /// the target detent — a wasm-owned output that reverts the write returns false, so the caller
    /// still speaks the honest "did not move".
    ///
    /// Gated by <see cref="Md11DirectSet.Refuse"/>: the write goes ONLY to the var this control's
    /// own definition reads back (the read below is what confirms it), and never to a control in
    /// <see cref="Md11DirectSet.FallbackNeverWrites"/> — the speedbrake lever's row reads the
    /// travel var while its map state var is the ground-spoiler pull (a detent written there
    /// blanked the Ground spoilers row), and the flap and gear levers' state vars are travel or
    /// animation values, not command encodings. A refused control returns false and the caller
    /// speaks the same "did not move" as always.
    /// </summary>
    private async Task<bool> TryDirectSetAsync(Md11Control control, double target, SimConnectManager sim)
    {
        // Read the field ONCE: this runs on a pool thread while Dispose nulls it on the UI thread, so
        // a second load between the check and the write could see null.
        var bus = _bus;
        if (bus == null) return false;
        GetVariables().TryGetValue(control.NodeId, out var def);
        var refusal = Md11DirectSet.Refuse(control, def?.Name);
        if (refusal != null)
        {
            Log.Info("MD11", $"{control.NodeId}: direct write of {target} refused — {refusal}.");
            return false;
        }

        bus.WriteExternal(control.StateVar, target);
        await Task.Delay(600).ConfigureAwait(false);         // ANIM_LAG is 100–1000 ms: the value travels before it rests
        // Read on DELIVERY: every walked control's var has its own data definition, so this
        // completes on the PERIOD.ONCE response (or the cache for a SIM_FRAME-streamed var), never
        // on a fixed sleep over whatever the cache held. Nothing delivered is not "held".
        var actual = await sim.ReadFreshAsync(control.NodeId, Md11SelectorWalker.FreshReadTimeoutMs).ConfigureAwait(false);
        if (actual == null)
        {
            Log.Debug("MD11", $"{control.NodeId}: direct set to {target} — nothing delivered to read back.");
            return false;
        }

        var ordered = Md11SelectorWalker.OrderedValues(control);
        bool held = ordered.Count > 0
            ? Md11SelectorWalker.PositionIndex(control, ordered, actual.Value)
              == Md11SelectorWalker.PositionIndex(control, ordered, target)
            : Math.Abs(actual.Value - target) <= 0.5;
        Log.Info("MD11", $"{control.NodeId}: direct set to {target} → read {actual.Value:0.##}, held={held}.");
        return held;
    }

    /// <summary>Settle after lifting a guard before actuating the control under it, measured from the guard click's WRITE.</summary>
    internal const int GuardOpenSettleMs = 250;

    /// <summary>
    /// The ceiling on the guard's two fresh reads, and on the press feedback's latch read. A guard
    /// var has its own data definition, so a PERIOD.ONCE answers on the next dispatch — a frame or
    /// two; ten frames is generous. Short on purpose, for two reasons: the decision read sits on
    /// the fire-handle walk's and the guarded press's critical path, and a lost ONCE must not
    /// stall them by the batch-sized ceiling; and the whole guarded-press chain has to speak its
    /// feedback inside <see cref="Md11AnnouncementGate.EchoWindowMs"/> (2500 ms) or the press's own
    /// lamp echo is spoken as well — pinned by Md11AnnouncementGateTests.
    ///
    /// The trade is real and is accepted: past this ceiling the state is unreadable and
    /// <see cref="Md11Guard.Decide"/> leaves the cover alone (exactly as an undelivered value did),
    /// so the control is actuated UNGATED and a covered fire handle may then report "did not move"
    /// — retry the pick. A read that times out is ABANDONED, so its late answer cannot satisfy the
    /// next guard read on the same key (<c>FreshReadWaiters</c> releases the request on every exit
    /// and only that request's answer completes it — PR #189 X1).
    /// </summary>
    internal const int GuardReadTimeoutMs = 300;

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

            // Read the guard's current state on DELIVERY (a PERIOD.ONCE answers on the next
            // dispatch). Nothing delivered inside GuardReadTimeoutMs → null → leave alone; the old
            // sleep-and-cache read could act on a stale cached position here.
            var state = await sim.ReadFreshAsync(guard.NodeId, GuardReadTimeoutMs).ConfigureAwait(false);

            var decision = Md11Guard.Decide(state);
            if (decision != Md11Guard.Action.Open)
            {
                if (decision == Md11Guard.Action.LeaveAlone)
                    Log.Debug("MD11", $"Guard {guard.NodeId} state unreadable — actuating {control.NodeId} ungated.");
                return;
            }

            // Closed → lift it, then settle so the control underneath is actuable. The settle is
            // measured from when the click will be WRITTEN (the bus backlog ahead of it) — the
            // press or walk that follows is queued behind it in the same FIFO, so this is the only
            // thing that keeps the two writes GuardOpenSettleMs apart (a guard click written right
            // beside its button's press was ignored, live).
            int backlogMs = _bus.BacklogMs;
            _bus.Fire(openEvent.Value);
            await Task.Delay(Md11EventBus.ReadBackDelayMs(GuardOpenSettleMs, backlogMs)).ConfigureAwait(false);

            // Confirm for the log only, OFF the actuation's path: the actuation runs regardless of
            // what the re-read says, so it must not wait on it (a lost PERIOD.ONCE would stall the
            // fire-handle walk or the press by the ceiling for a value nobody acts on).
            _ = LogGuardAfterAsync(guard, control, state.Value, sim);
        }
        catch (Exception ex)
        {
            // Guard handling must never be the thing that fails a control — swallow and proceed.
            Log.Debug("MD11", $"EnsureGuardOpen for {control.NodeId} threw (ignored): {ex.Message}");
        }
    }

    /// <summary>
    /// The guard's log-only confirmation, run beside the actuation rather than ahead of it: the
    /// request is issued before this returns (so it still reads the guard "before actuating"),
    /// and the line is written whenever the delivery lands. Never touches the actuation.
    /// </summary>
    private async Task LogGuardAfterAsync(Md11Control guard, Md11Control control, double before, SimConnectManager sim)
    {
        try
        {
            var after = await sim.ReadFreshAsync(guard.NodeId, GuardReadTimeoutMs).ConfigureAwait(false);
            Log.Info("MD11", $"Guard {guard.NodeId}: {before.ToString("0.##")} → {after?.ToString("0.##") ?? "null"} " +
                $"before actuating {control.NodeId}.");
        }
        catch (Exception ex)
        {
            Log.Debug("MD11", $"Guard {guard.NodeId} re-read threw (ignored): {ex.Message}");
        }
    }

    /// <summary>
    /// Lifts the guard (best-effort, state-aware), then queues the button's press/release.
    /// Completes with the bus backlog the press was queued behind — the press feedback's clock
    /// starts there, after the guard chain has actually run, not on an estimate of it.
    /// </summary>
    private async Task<int> GuardedPressAsync(Md11Control control, SimConnectManager sim)
    {
        await EnsureGuardOpenAsync(control, sim).ConfigureAwait(false);
        int backlogMs = _bus?.BacklogMs ?? 0;
        _bus?.Press(control);
        return backlogMs;
    }

    /// <summary>
    /// A hold-to-test button (<see cref="Md11TestButtons"/>): cover lifted first when guarded,
    /// then DOWN, held, UP. The lights the test brings on speak for themselves through the lamp
    /// path.
    ///
    /// Eight of the eleven are stateless, so their press feedback is silent and the lights are
    /// the whole read-out. The other three carry a lamp state block of their own — cargo fire and
    /// smoke, hydraulic, cargo door — so those DO speak a feedback sentence ("…: Test" or
    /// "…: On"), composed from whatever their lamp reads at the settle and corrected by the lamp
    /// if it lands later.
    /// </summary>
    private async Task HoldTestButtonAsync(Md11Control control, SimConnectManager sim, bool guarded)
    {
        try
        {
            if (guarded) await EnsureGuardOpenAsync(control, sim).ConfigureAwait(false);
            if (_bus != null) await _bus.PressAndHoldAsync(control, Md11TestButtons.HoldMs).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug("MD11", $"Hold-to-test for {control.NodeId} threw: {ex.Message}");
        }
    }

    // =================================================================================
    // State → speech
    // =================================================================================

    /// <summary>Take-off roll "V1" / "Rotate" / "V2" — see <see cref="Md11TakeoffCallouts"/>. Its arm is dropped on every context reset (a flight load included) and on the reconnect.</summary>
    private readonly TakeoffVSpeedCallouts _takeoffCallouts = new();

    /// <summary>The roll callouts' machine, for the tests that pin what a context reset does to it (as <see cref="SeedPassPending"/> is for the seed gate).</summary>
    internal TakeoffVSpeedCallouts TakeoffCallouts => _takeoffCallouts;

    /// <inheritdoc />
    public override string? TakeoffCalloutFeedKey => Md11TakeoffCallouts.IasKey;
    /// <inheritdoc />
    /// <remarks>The N1 70 percent cue rides the same feed, and it only ever arms and fires on the
    /// ground, which this already covers.</remarks>
    public override bool TakeoffCalloutFeedNeeded => _takeoffCallouts.NeedsSamples(_calloutOnGround);

    /// <summary>"N1 70 percent" once per take-off roll — see <see cref="Md11N1Cue"/>. Fed IAS per frame and N1 per delivery below; its arm is dropped on a context reset, never on the reconnect.</summary>
    private readonly Md11N1Cue _n1Cue = new();

    /// <summary>"V1 145, VR 150, V2 158 … knots" as the FMS sets the take-off speeds — see <see cref="Md11VSpeedAnnouncer"/> (wiped on a context reset, never on the reconnect: see its summary).</summary>
    private readonly Md11VSpeedAnnouncer _vSpeeds = new();

    /// <summary>
    /// Waits out the settle, then speaks the pending take-off speeds on the UI thread as one
    /// sentence — if still the latest (each change arms its own check; an early one finds nothing
    /// due and re-arms once, the last one speaks), if no reconnect or aircraft switch intervened,
    /// and with every speed the pilot muted in Ctrl+M left out. The same shape as
    /// AnnounceAltimeterWhenSettledAsync: the UI-thread tail carries its own catch.
    /// </summary>
    private Task AnnounceVSpeedsWhenSettledAsync(ScreenReaderAnnouncer announcer)
        => DeferToUiThreadAsync(Md11VSpeedAnnouncer.SettleMs + 50, () => Task.FromResult<Action?>(() =>
        {
            var muted = Settings.SettingsManager.Current.Md11DisabledMonitorVariablesSet;
            var sentence = _vSpeeds.Due(Environment.TickCount64, muted.Contains);
            if (sentence == null)
            {
                if (_vSpeeds.HasPending) _ = AnnounceVSpeedsWhenSettledAsync(announcer);   // checked early: one more round
                return;
            }
            announcer.Announce(sentence);
        }), "Take-off speed announcement threw", "Take-off speed announcement (UI-thread tail) threw", guardGeneration: true);

    /// <summary>
    /// The last SIM_ON_GROUND sample. Starts true: a ramp start is the norm, and an airborne start
    /// is harmless either way, because the machine arms only on a sample below 40 kt, which the
    /// air never delivers (the iFly's choice, and MainForm's for its own _lastOnGround).
    /// </summary>
    private bool _calloutOnGround = true;

    /// <summary>
    /// The flap handle and thumbwheel are two vars describing ONE fact ("what flap setting am I
    /// taking off on?"), so they are composed here rather than announced separately. Returning
    /// true consumes the update; MainForm's global echo wrap keeps a combo set from
    /// double-speaking.
    /// </summary>
    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        // Take-off roll V-speed callouts, fed per SIM_FRAME (hot path — first branch). The
        // contract is TakeoffVSpeedCallouts': arms on the ground below 40 kt with V1 and VR set,
        // upward crossings only, once per roll, a rejected take-off re-arms. AnnounceImmediate,
        // deliberately: "V1" and "Rotate" are action cues whose value IS the timing, and a queued
        // announce would wait out an in-progress "100 knots". It interrupts, so the calls one
        // sample crosses go out as ONE utterance (TakeoffVSpeedCallouts.Compose): spoken one by
        // one, "V1" was cut off by "Rotate" on every take-off with V1 = VR. That bypasses
        // MainForm's Suppressed wrap AND the Ctrl+M mute, so both are re-applied here — per speed,
        // keyed on the listed V1 / Rotate speed / V2 rows (Md11TakeoffCallouts.IsMuted), the way
        // the iFly does it.
        if (varName == Md11TakeoffCallouts.IasKey)
        {
            _n1Cue.OnIas(value, _calloutOnGround);      // arms/disarms the N1 cue; never speaks from here
            var callouts = _takeoffCallouts.ProcessSample(value, _calloutOnGround);
            if (callouts.Count > 0 && !announcer.Suppressed)
            {
                var muted = Settings.SettingsManager.Current.Md11DisabledMonitorVariablesSet;
                string? calloutSentence = TakeoffVSpeedCallouts.Compose(callouts, callout => Md11TakeoffCallouts.Keys.IsMuted(callout, muted));
                if (calloutSentence != null) announcer.AnnounceImmediate(calloutSentence);   // "V1, Rotate": one utterance, never two
            }
            return true;
        }

        // While a context-reset seed pass is pending, every delivery of a var that pass would
        // seed is evidence for or against the cache having settled (Md11SeedGate). One bool
        // while nothing is pending, which is nearly always; ahead of every consuming branch.
        if (_seedGate.Armed && IsSeededFromCache(varName)) _seedGate.NoteValue(varName, value, IsAircraftOwned(varName));

        // Air/ground for the roll callouts, peeked from the base SIM_ON_GROUND var and never
        // consumed — it falls through to base.ProcessSimVarUpdate, which speaks "On ground" /
        // "Airborne". Up here, ahead of every consuming branch, so none can swallow it later.
        if (varName == "SIM_ON_GROUND") _calloutOnGround = value >= 0.5;

        // The FMS V-speed exports arm the roll callouts. Explicit and ahead of the silent read-out
        // branch that consumes them, so the feed does not hinge on those exports' registration
        // shape — an export given ValueDescriptions one day would leave the silent set, and the
        // callouts would go quietly dead with every test still green.
        if (Md11TakeoffCallouts.Keys.IsVSpeedKey(varName)) Md11TakeoffCallouts.Keys.Feed(_takeoffCallouts, varName, value);

        // The FMS setting the take-off speeds is news, spoken the way the PMDGs speak it and as
        // ONE sentence in V1 / VR / V2 order once the batch's burst has settled
        // (Md11VSpeedAnnouncer): baseline-first, a cleared speed silent. Consumed here, so the
        // silent read-out branch below never sees these five. The sentence is spoken from a
        // timer, so AnnounceVSpeedsWhenSettledAsync checks the Ctrl+M mute itself, per speed.
        if (Md11VSpeeds.IsKey(varName))
        {
            _uiContext ??= SynchronizationContext.Current;
            if (_vSpeeds.OnUpdate(varName, value, Environment.TickCount64)) _ = AnnounceVSpeedsWhenSettledAsync(announcer);
            return true;
        }

        switch (varName)
        {
            case Md11FlapSystem.LeverKey:
                _flapRng = value;
                AnnounceFlaps(announcer);
                return true;

            case Md11SpeedbrakeSystem.LeverKey:
                _spdbrkRng = value;
                AnnounceSpoilerTravel(announcer);
                return true;

            case Md11SpeedbrakeSystem.ArmKey:
                AnnounceGroundSpoilers(value, announcer);
                return true;

            case Md11FlapSystem.DialKey:
                _dialRaw = value;
                // Moving the thumbwheel only changes the commanded angle when the handle is
                // actually IN the Dial-A-Flap detent. Elsewhere it is a pre-selection — silent.
                // A set from a combo is ONE write, so it arrives as one delivery; the pilot's own
                // pick is echo-suppressed by MainForm's wrap.
                if (!double.IsNaN(_flapRng) && _flaps.DetentFor(_flapRng)?.Dial == true)
                    AnnounceFlaps(announcer);
                return true;
        }

        // A control whose walk is in flight owns its state var until the walk lands. Every forced
        // read the walker makes would otherwise reach MainForm's control refresh, which re-sets the
        // focused combo to the intermediate position and NVDA reads it out — "Takeoff", "Off",
        // "Minimum" after the pilot picked "Minimum". DebouncedWalk force-reads once more with the
        // gate down when it finishes, so the combo re-syncs exactly once, silently on success.
        // Sits below the flap and speedbrake cases on purpose: those own their own read-outs.
        if (_walkCts.ContainsKey(varName)) return true;

        // COM radios: baseline-first and airband-gated ("COM 1 active 135.500"). Consumed here so
        // the generic path never narrates the raw kHz; Ctrl+M mutes through MainForm's wrap.
        if (Md11Radios.IsComKey(varName))
        {
            var com = _com.OnUpdate(varName, value);
            if (com != null) announcer.Announce(com);
            return true;
        }

        // The squawk speaks on change, whichever way it was set (Md11SquawkAnnouncer): a hardware
        // transponder, the sim's own keys, an ATC assignment. A typed entry's deliveries are
        // tracked but not spoken — its own confirmation is the sentence. Consumed here, or the
        // generic path would speak the raw BCO16 word; Ctrl+M mutes through MainForm's wrap.
        if (varName == Md11Squawk.CodeKey)
        {
            var squawk = _squawk.OnUpdate(value);
            if (squawk != null) announcer.Announce(squawk);
            return true;
        }

        // The captain's altimeter speaks once it settles (a knob wind speaks the final setting,
        // not every hundredth). Consumed here — the var has no ValueDescriptions, so it would
        // otherwise be a silent read-out, and the generic path would speak a raw number if it
        // were not. The speaking happens from a timer, so AnnounceAltimeterWhenSettledAsync
        // checks the Ctrl+M mute itself.
        if (varName == Md11Fcp.ReadCaptainBaro)
        {
            _uiContext ??= SynchronizationContext.Current;
            _altimeter.OnUpdate(value, Environment.TickCount64);
            // Only a value that actually armed a settle needs a check. An unchanged redelivery
            // (the panel's per-second force-read) is ignored by OnUpdate, so spawning a check for
            // it would queue a Task per second for the life of an open panel with nothing to say.
            if (_altimeter.HasPending) _ = AnnounceAltimeterWhenSettledAsync(announcer);
            return true;
        }

        // Silent numeric read-outs: cached for the hotkeys, never narrated on change. Consuming
        // them here suppresses the generic auto-announce (an N1/fuel stream spoken every second).
        // The single exception is the take-off cue: engine N1 first reaching 70% (ATS takeover) on
        // the take-off ROLL — Md11N1Cue arms only on the ground, slow and idle, and fires only on
        // the ground, so reverse on the rollout, a go-around and an approach swing stay silent.
        // Spoken from here, inside ProcessSimVarUpdate, so MainForm's Suppressed wrap mutes it
        // through the three N1 rows in Ctrl+M.
        if (_silentReadouts.Contains(varName))
        {
            if (_n1Cue.OnN1(varName, value, _calloutOnGround))
            {
                announcer.Announce(Md11N1Cue.Sentence);
                Log.Debug("MD11", $"Take-off cue spoken: {Md11N1Cue.Sentence} — {varName} at {value.ToString("F1", CultureInfo.InvariantCulture)} percent on the ground.");
            }
            return true;
        }

        // The DC-power gate: consumed silently; the hook reads it from the cache.
        if (varName == DcPowerKey) return true;

        // Every lamp is handled here (owner state or standalone lit/dark) and consumed, so the
        // generic announce path never speaks a raw "X light: on" again.
        if (_byNodeId.TryGetValue(varName, out var ctrl) && ctrl.Kind == Md11Kinds.Annunciator)
        {
            HandleLampUpdate(ctrl, varName, value, announcer);
            return true;
        }

        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    /// <summary>Settle-then-speak for the captain's altimeter — see <see cref="Md11AltimeterAnnouncer"/>.</summary>
    private readonly Md11AltimeterAnnouncer _altimeter = new();

    /// <summary>
    /// Waits out the settle, then speaks the pending sentence on the UI thread — if the value is
    /// still the latest (each update arms its own check; an early one finds nothing due and the
    /// last one speaks), if no reconnect/aircraft switch intervened, and if the pilot has not
    /// muted the altimeter in Ctrl+M. That last check is here, not in MainForm's Suppressed
    /// wrap, because this runs from a timer outside ProcessSimVarUpdate.
    ///
    /// A check that finds a value still pending fired marginally early — Task.Delay may return a
    /// hair under its interval, and TickCount64 is a ~15 ms-granularity clock — so it re-arms
    /// itself once rather than drop the sentence. The tail runs on the UI thread through
    /// OnUiThread, i.e. outside this method's try, so it carries its own catch: the same shape as
    /// DeferDarkTransitionAsync (TFDiMD11Definition.State.cs). An announcement must never be the
    /// thing that takes the message pump down.
    /// </summary>
    private Task AnnounceAltimeterWhenSettledAsync(ScreenReaderAnnouncer announcer)
        => DeferToUiThreadAsync(Md11AltimeterAnnouncer.SettleMs + 50, () => Task.FromResult<Action?>(() =>
        {
            var sentence = _altimeter.Due(Environment.TickCount64);
            if (sentence == null)
            {
                // Checked early: the value is still armed and nothing else will look at
                // it (each update arms one check). One more round, then it is spoken.
                if (_altimeter.HasPending) _ = AnnounceAltimeterWhenSettledAsync(announcer);
                return;
            }
            if (Settings.SettingsManager.Current.Md11DisabledMonitorVariablesSet.Contains(Md11Fcp.ReadCaptainBaro)) return;
            announcer.Announce(sentence);
        }), "Altimeter announcement threw", "Altimeter announcement (UI-thread tail) threw", guardGeneration: true);

    /// <summary>
    /// True when this annunciator update should be suppressed as blink chatter — 3+ transitions
    /// within <see cref="LampFlapWindowMs"/>. The first sighting (baseline) and the first couple of
    /// changes pass through, so a genuine one-shot caption is prompt; only sustained blinking is
    /// dropped. Runs on the UI thread (the WinForms timer that dispatches every SimVar update), so
    /// the dictionaries need no lock.
    /// </summary>
    private bool SuppressAnnunciatorFlap(string varName, double value)
    {
        if (!_lampLastVal.TryGetValue(varName, out var last))
        {
            _lampLastVal[varName] = value;
            return false;                                   // first sight — baseline-first, let it through
        }
        if (Math.Abs(last - value) < LampEpsilon) return false;  // unchanged — downstream already dedups
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

    /// <summary>
    /// Speaks the handle position, always with the Dial-A-Flap angle when that is the detent.
    /// Deduplicated on the spoken string (the lever var can re-deliver the same value) and
    /// BASELINE-FIRST like the spoiler read-out: the first complete text after connecting — or after
    /// switching to the MD-11 in flight — is recorded silently, so nobody is told "Flap 35" about a
    /// handle nobody moved (<see cref="Md11FlapSystem.ReadoutDecision"/>). The context reset leaves
    /// the flap pair alone on purpose: it dedups on this last text.
    /// </summary>
    private void AnnounceFlaps(ScreenReaderAnnouncer announcer)
    {
        if (double.IsNaN(_flapRng)) return;

        var dial = SampledDialRaw;
        var text = _flaps.DescribePosition(_flapRng, dial);
        // Complete once every var the words need has been read: the Dial-A-Flap detent needs the
        // wheel too, whose first sample may land after the lever's.
        bool complete = dial != null || _flaps.DetentFor(_flapRng)?.Dial != true;

        var (speak, record) = Md11FlapSystem.ReadoutDecision(_lastFlapSpoken, text, complete);
        if (record) _lastFlapSpoken = text;
        if (speak) announcer.Announce(text);
    }

    /// <summary>The thumbwheel's last sample, or null before the first — never a stand-in 0, which is a real 10°.</summary>
    private double? SampledDialRaw => double.IsNaN(_dialRaw) ? null : _dialRaw;

    /// <summary>
    /// Renders the flap combos' live value. Without this the lever would display the bare
    /// FLAP_RNG number, and the Dial-A-Flap detent — a RANGE, never equal to its representative
    /// value — would match no ValueDescriptions entry at all and show nothing.
    /// </summary>
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        // Status Display rows (the read-only list, Ctrl+3). A lamp reads its composed state — the
        // delivered value for its own var, the cache for the DC gate — and an exported number reads
        // with its unit; Md11StatusRow decides the words. A null means "nothing special" and the
        // generic path (ValueDescriptions, then the bare number) takes over.
        if (_byNodeId.TryGetValue(varKey, out var lamp) && lamp.Kind == Md11Kinds.Annunciator)
        {
            var text = Md11StatusRow.Lamp(lamp.State, lamp.StateVar, value, ReadStateVar, IsDcPowered());
            displayText = text ?? "";
            return text != null;
        }
        var readout = Md11StatusRow.Readout(varKey, value, key => _sim?.GetCachedVariableValue(key));
        if (readout != null) { displayText = readout; return true; }

        switch (varKey)
        {
            case Md11FlapSystem.LeverKey:
                displayText = _flaps.DescribePosition(value, SampledDialRaw);
                return true;

            case Md11FlapSystem.DialKey:
                displayText = $"{_flaps.DegreesFor(value).ToString("0", CultureInfo.InvariantCulture)} degrees";
                return true;

            case Md11SpeedbrakeSystem.LeverKey:
                displayText = Md11SpeedbrakeSystem.DisplayTravel(value);   // the detent, or "between detents"
                return true;
        }

        if (Md11Radios.IsComKey(varKey))
        {
            displayText = Md11Radios.Display(value);   // "135.500", or "--" while the radio reads nothing
            return true;
        }

        if (varKey == Md11Squawk.CodeKey)
        {
            displayText = Md11Squawk.Decode(value);    // BCO16 word -> "5473"
            return true;
        }

        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    // =================================================================================
    // Hotkey read-outs
    // =================================================================================

    /// <summary>
    /// The gear key's answer, read FRESH: <c>MD11_MIP_GEAR_SW</c> is OnRequest, so the cache is
    /// filled only while the Landing Gear panel is open — the read-out said "unavailable" until the
    /// panel had been opened once, and went stale the moment the gear moved with the sim's own key.
    /// The read completes on the var's own delivery (a frame or two, at most
    /// <see cref="Md11SelectorWalker.FreshReadTimeoutMs"/>); the words are
    /// <see cref="Md11GearLever.Describe"/>'s, "unavailable" only when nothing was delivered. The
    /// read resumes off the UI thread, so the answer goes through <see cref="DeferToUiThreadAsync"/>
    /// — with a zero delay, so the read is still issued at once, on the hotkey's thread — and is not
    /// spoken at all once the definition has been disposed (an aircraft switch mid-read: the tail's
    /// own check, not the generation guard).
    /// </summary>
    private Task ReadGearAsync(SimConnectManager sim, ScreenReaderAnnouncer announcer)
        => DeferToUiThreadAsync(0, async () =>
        {
            var travel = await sim.ReadFreshAsync(Md11GearLever.Key, Md11SelectorWalker.FreshReadTimeoutMs).ConfigureAwait(false);
            if (travel == null)
                Log.Debug("MD11", $"Gear read-out: nothing delivered for {Md11GearLever.Key} within {Md11SelectorWalker.FreshReadTimeoutMs} ms.");
            var sentence = Md11GearLever.Describe(travel);
            return () =>
            {
                if (_sim == null) return;   // disposed meanwhile: never answer for the aircraft that left
                announcer.AnnounceImmediate(sentence);
            };
        }, "Gear read-out threw", "Gear read-out (UI-thread tail) threw");

    /// <summary>
    /// The DUs have no text behind them, so these are read from a capture of the sim after the
    /// camera has been moved to the instrument view that frames the display — and the camera is
    /// put back afterwards. The restore was removed on 2026-09-09 and reinstated on 2026-09-18,
    /// when the reasoning behind that removal was disproven on another airframe; it is verified by
    /// read-back and says so when it fails. See InstrumentViewPlan and ReadDisplay.
    /// </summary>
    protected override IReadOnlyList<AiDisplayRead> DisplayReads => Md11DisplayReads.All;

    /// <summary>
    /// On this aircraft the read-outs are not a convenience — the DUs are WASM-rendered, so the
    /// exported L:vars are the only way a blind pilot gets the numbers a sighted one reads off the
    /// glass, and the five AI display reads are the only way to read the glass itself.
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

            // The characteristic-speed keys follow the NUMBER, not the Airbus letter: the MD-11
            // has two retraction speeds, so they sit on Shift+1 (VSR, slats) and Shift+2 (VFR,
            // flaps). They shipped on Shift+2/Shift+3 to match the Airbus S and F keys, and the
            // owner ruled that out (2026-09-06): a pilot who only flies the MD-11 has no reason to
            // know the Airbus letters, and read-outs start at 1. ReadSpeedGD is the Shift+1 action;
            // the MD-11 exports no clean-configuration speed, so nothing is displaced. Shift+3 and
            // above are deliberately unbound here. These speeds have no other source on this
            // aircraft — there is no speed tape to read them off.
            case HotkeyAction.ReadSpeedGD:
                AnnounceSpeed(simConnect, announcer, "MD11_VSR", "Slat retraction speed");
                return true;

            case HotkeyAction.ReadSpeedS:
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

            // Stock SimVar — nothing MD-11-specific, so both go through the same shared
            // request/announce path every other aircraft uses. W in output mode is the
            // waypoint-info key by registration, and every other definition repurposes it for
            // gross weight in POUNDS (Shift+W is kilograms); this one had no case for it, so W
            // said nothing on the MD-11 (reported 2026-09-07).
            case HotkeyAction.ReadWaypointInfo:
                simConnect.RequestSingleValue(
                    (int)SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT,
                    "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT");
                return true;

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
            //
            // Read FRESH, never from the cache (ReadGearAsync): the answer is spoken when the read
            // delivers, and the hotkey itself returns at once.
            case HotkeyAction.ReadGear:
                _ = ReadGearAsync(simConnect, announcer);
                return true;

            // B — the captain's altimeter in BOTH units, hPa first, exactly as the PMDG 737/777 and
            // Fenix say it ("Altimeter: 1013, 29.92"), or "Altimeter standard" (Md11Fcp.IsStandard).
            // The var carries only the unit the PFD shows; DescribeAltimeter converts the other.
            case HotkeyAction.ReadAltimeter:
            {
                announcer.AnnounceImmediate(Md11AltimeterAnnouncer.HotkeySentence(
                    simConnect.GetCachedVariableValue(Md11Fcp.ReadCaptainBaro)));
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
            // (the map's knob_pp kind carries its own PUSH_/PULL_ event pairs). Each goes through
            // PressKnobAction, so a press that cannot be delivered SPEAKS — these six spoke
            // nothing at all, which on an aircraft whose FCP window a blind pilot cannot read made
            // a dropped push identical to an accepted one.
            case HotkeyAction.FCUHeadingPush:
                PressKnobAction(Md11Fcp.HeadingKnob, "PUSH_DOWN", "PUSH_UP",
                    Md11Fcp.PushAction(Md11Fcp.HeadingKnobName), announcer);
                return true;

            case HotkeyAction.FCUHeadingPull:
                PressKnobAction(Md11Fcp.HeadingKnob, "PULL_DOWN", "PULL_UP",
                    Md11Fcp.PullAction(Md11Fcp.HeadingKnobName), announcer);
                return true;

            case HotkeyAction.FCUSpeedPush:
                PressKnobAction(Md11Fcp.SpeedKnob, "PUSH_DOWN", "PUSH_UP",
                    Md11Fcp.PushAction(Md11Fcp.SpeedKnobName), announcer);
                return true;

            case HotkeyAction.FCUSpeedPull:
                PressKnobAction(Md11Fcp.SpeedKnob, "PULL_DOWN", "PULL_UP",
                    Md11Fcp.PullAction(Md11Fcp.SpeedKnobName), announcer);
                return true;

            case HotkeyAction.FCUAltitudePush:
                PressKnobAction(Md11Fcp.AltitudeKnob, "PUSH_DOWN", "PUSH_UP",
                    Md11Fcp.PushAction(Md11Fcp.AltitudeKnobName), announcer);
                return true;

            case HotkeyAction.FCUAltitudePull:
                PressKnobAction(Md11Fcp.AltitudeKnob, "PULL_DOWN", "PULL_UP",
                    Md11Fcp.PullAction(Md11Fcp.AltitudeKnobName), announcer);
                return true;

            // Ctrl+P — the Flight Control Panel (this aircraft's MCP). Tracked so an aircraft
            // switch disposes it along with everything else the definition owns.
            case HotkeyAction.FCUSetAutopilot:
                hotkeyManager.ExitInputHotkeyMode();
                // The window's two combos write through SetControl, past the panel path that marks
                // a pick in MainForm's echo window, so it marks them itself — the PMDG Ctrl+P
                // window's arrangement (BaseAircraftDefinition.ShowPMDGAutopilotWindow).
                ShowTrackedWindow(
                    () => new Forms.MD11.Md11AutopilotWindow(this, simConnect, announcer,
                        (key, v) => (parentForm as MainForm)?.SuppressUiEcho(key, v)),
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
        announcer.Announce(_flaps.DescribePosition(_flapRng, SampledDialRaw));
    }

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
