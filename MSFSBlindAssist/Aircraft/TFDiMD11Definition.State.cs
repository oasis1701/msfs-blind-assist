using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Composed control state (spec §3.3–3.7): the hook MainForm labels buttons and status rows
/// from, the DC-power gate, lamp-change announcements and press feedback.
/// </summary>
public partial class TFDiMD11Definition
{
    /// <summary>Reads a state L:var (by NAME, as the state block spells it) from the shared cache.</summary>
    private double? ReadStateVar(string stateVar) => _sim?.GetCachedVariableValue(KeyFor(stateVar));

    /// <summary>
    /// True while the annunciators have power: a live main bus AND DC bus 1 not annunciated off.
    /// The second half is what makes battery-only read as unpowered — see
    /// <see cref="Md11ControlState.IsPowered"/> for the measurements behind it.
    /// </summary>
    public bool IsDcPowered() => Md11ControlState.IsPowered(
        _sim?.GetCachedVariableValue(DcPowerKey),
        _sim?.GetCachedVariableValue(Dc1BusOffKey));

    /// <summary>
    /// MainForm's label seam. No opinion before <see cref="Attach"/> (no cache to read) and for
    /// controls with neither a state block nor a composite (momentary buttons, knobs, switches,
    /// read-outs). A composite (the engine fire handles, the Elevator Feel knob) composes from its two
    /// positions alone (<see cref="Md11CompositeState.Describe"/>), and its inner var is read by its
    /// own KEY, never through <see cref="KeyFor"/>: that var's name is the control's own key, which
    /// reads the OUTER var.
    /// </summary>
    public override bool TryDescribeControlState(string varKey, out string stateText)
    {
        stateText = "";
        if (_sim == null) return false;
        if (!_byNodeId.TryGetValue(varKey, out var c)) return false;

        string? text;
        if (c.Composite != null)
            text = Md11CompositeState.Describe(c.Composite, _sim.GetCachedVariableValue(c.NodeId),
                _sim.GetCachedVariableValue(Md11CompositeState.InnerKeyFor(c.NodeId)));
        else if (c.State != null)
            text = Md11ControlState.Compose(c.State, ReadStateVar, IsDcPowered());
        else
            return false;
        if (text == null) return false;
        stateText = text;
        return true;
    }

    private readonly Md11AnnouncementGate _gate = new();
    private readonly Md11ComAnnouncer _com = new();   // COM 1-3 active/standby, baseline-first
    private readonly Md11SquawkAnnouncer _squawk = new();   // the squawk on change, baseline-first

    /// <summary>
    /// The UI thread's context, captured by <see cref="SetControl"/> the first time it runs (every
    /// caller is a WinForms event handler).
    ///
    /// EVERYTHING that touches the gate or the announcer runs on that ONE thread — the WinForms
    /// timer that dispatches every SimVar update, so <see cref="HandleLampUpdate"/> is on it too;
    /// the gate's plain dictionaries need no lock precisely because of that. What is NOT on it is
    /// the tail of any method that has awaited: <see cref="PressFeedbackAsync"/> and
    /// <see cref="DeferDarkTransitionAsync"/> resume on a thread-pool thread after their
    /// ConfigureAwait(false) delays, so both hop back via <see cref="OnUiThread"/> rather than
    /// touch the gate or the announcer directly.
    /// </summary>
    private SynchronizationContext? _uiContext;

    /// <summary>
    /// Lamps ride the 1 Hz batch; a press's effect is visible within this of the press being
    /// WRITTEN — the feedback's clock starts when the press is queued and adds the bus backlog
    /// ahead of it (<see cref="Md11EventBus.ReadBackDelayMs"/>). A guarded press hands over that
    /// moment itself (GuardedPressAsync), after its guard chain has actually run.
    /// </summary>
    internal const int PressSettleMs = 1200;

    /// <summary>
    /// What the feedback must leave itself after the wait: the latch's fresh read
    /// (<see cref="GuardReadTimeoutMs"/>) plus a margin, so the sentence lands inside the echo
    /// window rather than exactly on its edge.
    /// </summary>
    internal const int FeedbackReserveMs = GuardReadTimeoutMs + 100;

    /// <summary>
    /// The feedback's wait: the lamps' settle measured from the press's WRITE
    /// (<see cref="Md11EventBus.ReadBackDelayMs"/>), capped at whatever the echo window has left
    /// (<see cref="Md11AnnouncementGate.RemainingFeedbackBudgetMs"/>).
    ///
    /// The cap is the finding this method exists for: the chain absorbs the bus backlog TWICE —
    /// once in EnsureGuardOpenAsync's cover settle, again here — and the term is Pending × MinGapMs,
    /// bounded only by the queue (256 ids ⇒ 15 s). A guarded press queued behind a real burst (an
    /// MCDU scratchpad send, ~1.4 s) would otherwise speak after the window had closed, and the
    /// press's own lamp echo would be spoken beside it — the doubled announcement the window exists
    /// to prevent, and one the pre-branch fixed 1700 ms could not produce. Only the WAIT is cut, and
    /// cutting it is the safe direction: the feedback may then read the pre-press state, and a lamp
    /// landing later with a different text is the correction (which is why Feedback closes the
    /// window). The guard's own settle is NOT clamped — that one paces two writes at the aircraft,
    /// and shortening it is how a covered control fails to actuate at all.
    /// </summary>
    internal static int FeedbackDelayMs(int backlogMs, long elapsedSincePressMs)
        => Math.Min(Md11EventBus.ReadBackDelayMs(PressSettleMs, backlogMs),
                    Md11AnnouncementGate.RemainingFeedbackBudgetMs(elapsedSincePressMs, FeedbackReserveMs));

    /// <summary>
    /// Only the hold-to-test path still ESTIMATES its guard chain — a fresh guard read (a frame or
    /// two, <see cref="GuardReadTimeoutMs"/> at worst), the guard's click and its 250 ms settle:
    /// its feedback must speak at the settle, not after the 3 s hold, so it cannot wait for the
    /// DOWN to be queued. Everywhere else the chain is awaited, not estimated.
    /// </summary>
    private const int GuardedPressExtraMs = 500;

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread when one was captured (the announcer and the
    /// gate's dictionaries are UI-thread objects, and the WinForms timer that dispatches SimVar
    /// updates puts HandleLampUpdate on that same thread), else inline as a last resort.
    /// </summary>
    private void OnUiThread(Action action)
    {
        var ctx = _uiContext;
        if (ctx != null) ctx.Post(_ => action(), null);
        else action();
    }

    /// <summary>
    /// The ONE "wait, read, then act on the UI thread" shape every deferred read-back and settled
    /// announcement on this aircraft shares — the ground-spoiler, COM, minimums, squawk, Ctrl+B and
    /// STD read-backs, the take-off speed and altimeter settles, the deferred dark transition.
    ///
    /// After <paramref name="delayMs"/>, <paramref name="readThenCompose"/> runs OFF the UI thread:
    /// it does the site's fresh reads, writes the site's timing log line, decides, and returns the
    /// tail to run on the UI thread — or null when there is nothing to say, and then nothing is
    /// posted. The tail runs through <see cref="OnUiThread"/> inside its own catch, because it runs
    /// outside this method's try and an announcement must never be the thing that takes the message
    /// pump down. <paramref name="guardGeneration"/> drops the tail when <see cref="_announceGeneration"/>
    /// has moved since the call (an aircraft switch, a reconnect, a flight load): the ground-spoiler,
    /// COM, minimums and squawk verdicts pass false, while Ctrl+B and the STD read-backs keep the
    /// guard they always had. Each site passes its own two log
    /// texts and keeps its own Ctrl+M check inside its tail.
    ///
    /// The gear key's read-out passes a zero delay, which waits for nothing: Task.Delay(0) is
    /// already complete, so <paramref name="readThenCompose"/> starts on the caller's thread and
    /// issues its read before this returns, exactly as a direct call would.
    /// </summary>
    private async Task DeferToUiThreadAsync(int delayMs, Func<Task<Action?>> readThenCompose,
        string failedLog, string tailFailedLog, bool guardGeneration = false)
    {
        int generation = _announceGeneration;
        try
        {
            await Task.Delay(delayMs).ConfigureAwait(false);
            var tail = await readThenCompose().ConfigureAwait(false);
            if (tail == null) return;
            OnUiThread(() =>
            {
                try
                {
                    if (guardGeneration && generation != _announceGeneration) return;   // aircraft switch / reconnect / flight load
                    tail();
                }
                catch (Exception ex)
                {
                    Log.Debug("MD11", $"{tailFailedLog}: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Debug("MD11", $"{failedLog}: {ex.Message}");
        }
    }

    /// <summary>How long a lamp going DARK waits before it is allowed to speak — see <see cref="DeferDarkTransitionAsync"/>.</summary>
    private const int DarkSettleMs = 1500;

    /// <summary>
    /// Bumped by <see cref="OnSimContextReset"/> (a disconnect or a flight load), by
    /// <see cref="ResetAnnouncementBaselines"/> (the Connected branch) and by
    /// <see cref="Dispose"/> (an aircraft switch disposes the outgoing definition) so a deferred
    /// dark transition, settle or read-back scheduled before any of them cannot speak the old
    /// situation afterwards.
    /// </summary>
    private int _announceGeneration;

    /// <summary>
    /// A lamp value arrived. Baseline-first (the first sight of every lamp is silent — connecting
    /// mid-flight must not narrate the cockpit), blink-guarded, then spoken as its OWNER's composed
    /// state or, for a standalone light, as its own lit/dark word. Always consumed: the generic
    /// announce path never sees an MD-11 lamp, so Ctrl+M works through MainForm's Suppressed wrap.
    ///
    /// A lamp going DARK is the one case that never speaks on the spot — see
    /// <see cref="DeferDarkTransitionAsync"/>.
    /// </summary>
    private void HandleLampUpdate(Md11Control lamp, string varName, double value, ScreenReaderAnnouncer announcer)
    {
        bool firstSight = !_lampLastVal.TryGetValue(varName, out var last);
        bool unchanged = !firstSight && Math.Abs(last - value) < LampEpsilon;
        if (SuppressAnnunciatorFlap(varName, value)) return;   // blinking: quiet until it settles
        if (firstSight || unchanged) return;

        bool lit = value > Md11ControlState.LitThreshold;
        if (!lit)
        {
            // Going dark while the aircraft is already unpowered is not news — it is the whole
            // panel losing power, and one sentence per control is exactly the narration §3.7
            // forbids. Otherwise let it settle first: the DC-bus-1 lamp the gate reads rides a
            // different SimConnect batch from these, so "still powered" can be a beat stale.
            if (!IsDcPowered()) return;
            _ = DeferDarkTransitionAsync(lamp, announcer);
            return;
        }

        long now = Environment.TickCount64;
        if (_lampOwners.TryGetValue(lamp.NodeId, out var owners))
        {
            foreach (var owner in owners)
            {
                var text = Md11ControlState.Compose(owner.State, ReadStateVar, IsDcPowered());
                if (text != null && _gate.ShouldSpeakBackground(owner.NodeId, text, now))
                    announcer.Announce($"{owner.DisplayLabel}: {text}");
            }
            return;
        }

        string state = StandaloneWord(lamp, lit: true);
        if (string.IsNullOrEmpty(state)) return;
        if (_gate.ShouldSpeakBackground(lamp.NodeId, state, now))
            announcer.Announce($"{lamp.DisplayLabel}: {state}");
    }

    /// <summary>A standalone light's own word for its current value: its lit legend, or the state block's dark meaning.</summary>
    private static string StandaloneWord(Md11Control lamp, bool lit)
        => lit ? (lamp.State?.Lamps.FirstOrDefault()?.Lit ?? "on") : (lamp.State?.Dark ?? "off");

    /// <summary>
    /// A lamp went out. Wait <see cref="DarkSettleMs"/>, then decide from what is true THEN.
    ///
    /// Two reasons, both measured. POWER: the 528 batch-covered variables (488 of them lamps) ride
    /// two SimConnect batches sorted by name, and <c>MD11_OVHD_ELEC_DC1_BUS_OFF_LT</c> (which the
    /// gate reads) sits in batch 1 while the <c>MD11_OVHD_HYD_*</c> and <c>MD11_OVHD_PNEU_*</c>
    /// lamps sit in batch 2 —
    /// so in the normal shutdown order (external power off, battery still on) a batch-2 lamp can
    /// go dark while the gate has not yet learned the DC busses died, and every OFF-legend button
    /// on them would announce its dark meaning: "Tank 1 Fuel Pumps: On", "Pack 1: On", … one per
    /// control. STATE: a control with several legends may still have one lit, and what is worth
    /// speaking is what it reads now, not which lamp moved.
    ///
    /// So the state is re-composed here, on the UI thread (the gate and the announcer are
    /// UI-thread objects), and <see cref="Md11AnnouncementGate.SpeakDarkTransition"/> decides.
    /// LIT transitions are unaffected and still speak at once — a light coming on is news.
    ///
    /// Ctrl+M is re-checked HERE, by the lamp's own key, because this tail runs from a timer
    /// outside ProcessSimVarUpdate and therefore outside MainForm's Suppressed wrap — the wrap
    /// that mutes the lit edge cannot reach a sentence spoken 1.5 s later. The gate still runs
    /// for a muted lamp so its dedup state stays what it would be unmuted; only the speech is
    /// dropped. Same reasoning as the altimeter settle announcement.
    /// </summary>
    private Task DeferDarkTransitionAsync(Md11Control lamp, ScreenReaderAnnouncer announcer)
        => DeferToUiThreadAsync(DarkSettleMs, () => Task.FromResult<Action?>(() =>
        {
            long now = Environment.TickCount64;
            bool powered = IsDcPowered();
            bool muted = Settings.SettingsManager.Current.Md11DisabledMonitorVariablesSet.Contains(lamp.NodeId);

            if (_lampOwners.TryGetValue(lamp.NodeId, out var owners))
            {
                foreach (var owner in owners)
                {
                    var text = _gate.SpeakDarkTransition(owner.NodeId,
                        Md11ControlState.Compose(owner.State, ReadStateVar, powered), powered, now);
                    if (text != null && !muted) announcer.Announce($"{owner.DisplayLabel}: {text}");
                }
                return;
            }

            // Standalone: whatever the lamp reads now, which may be lit again.
            bool litNow = _lampLastVal.TryGetValue(lamp.NodeId, out var v) && v > Md11ControlState.LitThreshold;
            var word = _gate.SpeakDarkTransition(lamp.NodeId, StandaloneWord(lamp, litNow), powered, now);
            if (word != null && !muted) announcer.Announce($"{lamp.DisplayLabel}: {word}");
        }), $"Deferred dark transition for {lamp.NodeId} threw",
            $"Deferred dark transition (UI-thread tail) for {lamp.NodeId} threw", guardGeneration: true);

    /// <summary>
    /// Press feedback (spec §3.6): after the lamps and latch settle, speak the resulting state
    /// once, queued — always, so an inert press (engines off, AUTO mode) tells the pilot the
    /// unchanged state rather than nothing. Seeds the dedup so the press's own lamp echo is quiet.
    /// <paramref name="queued"/> completes when the press has been QUEUED on the bus, with the
    /// backlog it then waits behind — the settle is measured from there, so a press behind a
    /// walker burst, or behind its own guard chain, is not read back before it has landed.
    ///
    /// The whole chain has to finish inside <see cref="Md11AnnouncementGate.EchoWindowMs"/>, or
    /// the window closes on its own and the press's lamp echo is spoken beside this feedback.
    /// That is why the latch read below is bounded by the short <see cref="GuardReadTimeoutMs"/>
    /// rather than the walker's ceiling, and why the settle wait goes through
    /// <see cref="FeedbackDelayMs"/> against <paramref name="pressedAtMs"/> — the moment the gate
    /// stamped, which is where the window is measured from. Pinned by Md11AnnouncementGateTests.
    /// </summary>
    private async Task PressFeedbackAsync(Md11Control c, SimConnectManager sim, ScreenReaderAnnouncer announcer,
        Task<int> queued, long pressedAtMs)
    {
        // The generation this press belongs to. Dispose and every context reset bump it, so a
        // feedback still in flight when the pilot switches aircraft — or the connection drops —
        // must not speak for the definition it was started on. Without this the tail resumed
        // against a disposed definition where ReadStateVar returns null for every var and
        // IsDcPowered() is false, so Compose fell to its unpowered arm and announced e.g.
        // "APU Generator (APU Panel): unpowered" over the NEW aircraft's own startup — and wrote
        // that text into the dead gate's baseline. Its sibling DeferDarkTransitionAsync has
        // guarded this since it shipped (DeferToUiThreadAsync's guardGeneration); this one, the
        // only deferred announcement on the aircraft that did not, is the press chain — up to
        // EchoWindowMs plus a guarded press's ~550 ms cover lift, so the window is seconds wide.
        int generation = _announceGeneration;
        try
        {
            int backlogMs = await queued.ConfigureAwait(false);
            await Task.Delay(FeedbackDelayMs(backlogMs, Environment.TickCount64 - pressedAtMs)).ConfigureAwait(false);
            if (generation != _announceGeneration) return;   // cheap pre-check; the UI-thread tail re-checks under the one that matters
            // A latched button's own var has its own data definition: the read completes on the
            // PERIOD.ONCE delivery, which lands in the cache Compose reads below (the old fixed
            // 300 ms sleep read whatever the cache held). Nothing delivered leaves the cache as it
            // was, and the feedback reads the last known state as before.
            if (c.State?.Latch != null)
                await sim.ReadFreshAsync(c.NodeId, GuardReadTimeoutMs).ConfigureAwait(false);

            // The delays above leave this on a thread-pool thread. Compose + feedback + announce
            // must run on the UI thread: the gate's dictionaries are also mutated by
            // HandleLampUpdate, which the WinForms SimVar-dispatch timer puts on that same thread
            // (a same-thread invariant, not a locked one), and ScreenReaderAnnouncer.Announce is
            // unreliable off the UI thread.
            OnUiThread(() =>
            {
                try
                {
                    // The generation is read and bumped on the UI thread, so this is the check that
                    // decides. The pre-check above only saves the work when the switch already happened.
                    if (generation != _announceGeneration) return;
                    var text = Md11ControlState.Compose(c.State, ReadStateVar, IsDcPowered());
                    if (text == null) return;
                    announcer.Announce($"{c.DisplayLabel}: {_gate.Feedback(c.NodeId, text)}");
                }
                catch (Exception ex)
                {
                    Log.Debug("MD11", $"Press feedback (UI-thread tail) for {c.NodeId} threw: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Debug("MD11", $"Press feedback for {c.NodeId} threw: {ex.Message}");
        }
    }

    /// <summary>A guard has no feedback sentence (the reader spoke the button); its label refreshes from a forced read.</summary>
    private async Task GuardRefreshAsync(Md11Control guard, SimConnectManager sim)
    {
        try
        {
            await Task.Delay(400).ConfigureAwait(false);
            sim.RequestVariable(guard.NodeId, forceUpdate: true);
        }
        catch (Exception ex)
        {
            Log.Debug("MD11", $"Guard refresh for {guard.NodeId} threw: {ex.Message}");
        }
    }

    /// <summary>
    /// The Connected branch. It runs AFTER the reconnect's first batch has re-fired every var into
    /// ProcessSimVarUpdate, so NOTHING baseline-first may be wiped here — every wipe that used to
    /// live here swallowed the next real change of its tracker (the first master caution, the
    /// first COM tune, the first altimeter wind, the pilot's first perf entry of a reconnected
    /// session; review, 2026-09-08). Those move to <see cref="OnSimContextReset"/>. What belongs
    /// here is what must not survive into the new session whatever the ordering: the roll's arm,
    /// the pending timers. The roll's arm is ALSO dropped by <see cref="OnSimContextReset"/>, and
    /// on a flight load only there: a load never reaches this method. The N1 take-off cue is NOT
    /// reset here: its arm is dropped by
    /// <see cref="OnSimContextReset"/>, which precedes every reconnect, and its samples are kept
    /// there too (see <see cref="Md11N1Cue.Reset"/>) — a wipe here would lose the IAS the batch
    /// has just delivered and never re-delivers to an aircraft standing still on the runway.
    /// </summary>
    public override void ResetAnnouncementBaselines()
    {
        base.ResetAnnouncementBaselines();
        _takeoffCallouts.Reset();               // drops the arm, the last sample and the fired flags; keeps the speeds, which the batch has just re-fed
        _vSpeeds.DropPending();                 // a sentence still pending here dies with its tail below; it must not ride into a later one
        _announceGeneration++;                  // drops any dark transition, settle or read-back still waiting
    }

    /// <summary>
    /// A disconnect, or a flight load on a live connection: every baseline-first tracker that
    /// would otherwise NARRATE its re-seed is wiped here, so the next delivery of each var seeds
    /// it silently — connecting or loading must not read out the cockpit, the radio stack, the
    /// altimeter or the speeds — and the change after that speaks. The take-off roll's two ARMS
    /// are dropped here too, the N1 cue's and the V-speed callouts' (the cue keeps its samples,
    /// the callouts their speeds): a load delivers the new flight's per-frame airspeed ahead of
    /// the 1 Hz SIM_ON_GROUND, so an arm kept from a parked aircraft fired at the loaded cruise
    /// ("V1, Rotate, V2" at FL350), and the Connected branch never runs for a load. The flap
    /// pair is left alone
    /// on purpose: it dedups on its last RECORDED text — which may be a baseline recorded
    /// silently rather than anything spoken — so an unchanged lever is silent and a
    /// changed one speaks once, truthfully. An aircraft switch constructs a new definition,
    /// which needs none of this.
    ///
    /// The two callers differ in what comes next. A DISCONNECT clears the cache, so the
    /// reconnect re-fires every var and every tracker re-seeds on delivery. A FLIGHT LOAD clears
    /// nothing and the batch fires only on a CHANGED value, so a var the load left as it was
    /// (every lamp of a cold-and-dark load after an app connected at the menu, a squawk, a COM
    /// frequency) is never delivered again — and a tracker wiped for it would eat its first real
    /// change. So <see cref="SeedFromCache"/> seeds every still-empty tracker from the cache once
    /// the batch deliveries show the cache current and settled (<see cref="Md11SeedGate"/>), by
    /// which time it holds exactly the unchanged values; on the disconnect path every var
    /// re-fires first and the pass finds nothing left to seed.
    /// </summary>
    public override void OnSimContextReset()
    {
        _lampLastVal.Clear();
        _lampChangeTicks.Clear();
        _gate.Reset();
        _com.Reset();
        _squawk.Reset();
        _altimeter.Reset();
        _vSpeeds.Reset();
        _n1Cue.Reset();                         // drops the arm only — a gate arm must not fire on the cruise N1 a load delivers; the samples are re-fired or still true
        _takeoffCallouts.Reset();               // same: drops the arm, the last sample and the fired flags, keeps the speeds — a parked arm called V1/Rotate/V2 on the cruise IAS a load delivers ahead of SIM_ON_GROUND
        _spdbrkHandle = double.NaN;
        _lastSpoilerSpoken = string.Empty;
        // The flap read-out is two vars and a baseline, and all three must go. Keeping _dialRaw was
        // enough on its own to speak the PREVIOUS flight's take-off flap angle: MD11_FLAP_LATCH is
        // SIM_FRAME + CHANGED, so on a load it delivers first, AnnounceFlaps found a non-NaN dial
        // from the flight before, judged the text COMPLETE and spoke that angle — the wheel's own
        // delivery then corrected it a frame or two later, so a blind pilot heard two different
        // take-off flap settings back to back with nothing to say which was current. Both keys are
        // in SeededScalarKeys, so a load that leaves either unchanged still gets its baseline back
        // from the cache instead of going dead for the session.
        _flapRng = double.NaN;
        _dialRaw = double.NaN;
        _lastFlapSpoken = string.Empty;
        _announceGeneration++;                  // nothing scheduled before the drop may speak after it
        _seedGate.Arm(KnownSeedValues());       // SeedFromCache runs when the deliveries say the cache is current and settled
    }

    /// <summary>
    /// What the cache holds for every seedable var right now, for the gate to arm with, so a
    /// redelivery of the same value is not a change. A flight load leaves the cache full (the
    /// pre-load values); a disconnect has cleared it on the way down, so this yields nothing
    /// and every re-fire is a change, as it should be.
    /// </summary>
    private IEnumerable<KeyValuePair<string, double>> KnownSeedValues()
    {
        var sim = _sim;
        if (sim == null) yield break;
        foreach (var c in _byNodeId.Values)
            if (c.Kind == Md11Kinds.Annunciator && sim.GetCachedVariableValue(c.NodeId) is double lamp)
                yield return new KeyValuePair<string, double>(c.NodeId, lamp);
        foreach (var key in SeededScalarKeys)
            if (sim.GetCachedVariableValue(key) is double value)
                yield return new KeyValuePair<string, double>(key, value);
    }

    /// <summary>
    /// WHEN the still-empty trackers are seeded after a context reset: on the batch deliveries'
    /// evidence — every active batch delivered since the reset, one of the aircraft's own values
    /// changed since it (stillness alone is ambiguous: a loaded MD-11 publishes seconds after
    /// AircraftLoaded, and the stock radios are the sim core's), then every batch delivered
    /// <see cref="Md11SeedGate.QuietCycles"/> times with nothing seedable moving, with a ceiling
    /// for a cockpit that never settles or never changes. A 3 s wall clock stood here first and was unsound: on a load slower than that it
    /// froze the PRE-load cache as the baselines, and every lamp that then came up spoke (review,
    /// 2026-09-08). Fed by <see cref="OnContinuousBatchDelivered"/> and, per seedable delivery,
    /// from ProcessSimVarUpdate; disarmed by <see cref="Dispose"/>.
    /// </summary>
    private readonly Md11SeedGate _seedGate = new();

    /// <summary>A context-reset seed pass is still pending (tests).</summary>
    internal bool SeedPassPending => _seedGate.Armed;

    /// <inheritdoc />
    public override void OnContinuousBatchDelivered(int batchNum)
    {
        if (!_seedGate.Armed) return;
        var sim = _sim;
        if (sim == null) return;
        var trigger = _seedGate.OnBatchDelivered(batchNum, sim.ActiveContinuousBatches, Environment.TickCount64);
        if (trigger == Md11SeedTrigger.None) return;
        try
        {
            SeedFromCache(trigger);
        }
        catch (Exception ex)
        {
            Log.Debug("MD11", $"Context reset: the seed pass threw ({trigger}): {ex.Message}");
        }
    }

    /// <summary>
    /// The scalar vars <see cref="SeedFromCache"/> seeds, besides every lamp — the ONE list the
    /// pass reads and <see cref="IsSeededFromCache"/> consults, so the set seeded and the set
    /// counted as evidence cannot drift apart: a var seeded but not counted could be seeded
    /// mid-change, a var counted but never seeded would hold the pass for nothing. A key added
    /// here needs its tracker in <see cref="SeedScalar"/>. Pinned by Md11SeedGateTests.
    /// </summary>
    internal static readonly string[] SeededScalarKeys = BuildSeededScalarKeys();

    private static string[] BuildSeededScalarKeys()
    {
        var keys = new List<string>
        {
            Md11Squawk.CodeKey, Md11Fcp.ReadCaptainBaro, Md11SpeedbrakeSystem.ArmKey, Md11SpeedbrakeSystem.LeverKey,
            Md11FlapSystem.LeverKey, Md11FlapSystem.DialKey,
        };
        keys.AddRange(Md11VSpeeds.Keys);
        keys.AddRange(Md11Radios.Keys);
        return keys.ToArray();
    }

    /// <summary>A var the seed pass reads: a listed scalar, or any lamp.</summary>
    internal bool IsSeededFromCache(string varName) =>
        Array.IndexOf(SeededScalarKeys, varName) >= 0
        || (_byNodeId.TryGetValue(varName, out var control) && control.Kind == Md11Kinds.Annunciator);

    /// <summary>
    /// A var the aircraft's own module writes (an L:var), as opposed to one the sim core writes
    /// from the flight file before that module has published — the stock COM frequencies and
    /// the transponder code. Only the former is evidence that the aircraft has published.
    /// </summary>
    internal bool IsAircraftOwned(string varName) =>
        GetVariables().TryGetValue(varName, out var def) && def.Type == SimVarType.LVar;

    /// <summary>
    /// Seeds every tracker that still has no baseline from the cache, silently. A tracker that a
    /// delivery already re-seeded is skipped; a var the cache does not hold is skipped. Not
    /// gated on <see cref="_announceGeneration"/>: the Connected branch bumps that on the very
    /// flight load this pass serves.
    /// </summary>
    private void SeedFromCache(Md11SeedTrigger trigger)
    {
        var sim = _sim;
        if (sim == null) return;
        int seeded = 0;

        foreach (var c in _byNodeId.Values)
        {
            if (c.Kind != Md11Kinds.Annunciator || _lampLastVal.ContainsKey(c.NodeId)) continue;
            if (sim.GetCachedVariableValue(c.NodeId) is double lamp) { _lampLastVal[c.NodeId] = lamp; seeded++; }
        }
        foreach (var key in SeededScalarKeys)
        {
            if (sim.GetCachedVariableValue(key) is not double value) continue;
            if (SeedScalar(key, value)) seeded++;
        }

        // The flap read-out's baseline needs BOTH flap vars, so it is recorded here rather than in
        // either key's case above — whichever of them the loop seeded, the text is composed once
        // from what is known. Same silence rule ReadoutDecision applies to a first delivery: record
        // only a COMPLETE text (a Dial-A-Flap detent is incomplete until the thumbwheel is known),
        // never speak, so the next genuine change is the first thing the pilot hears.
        if (_lastFlapSpoken.Length == 0 && !double.IsNaN(_flapRng))
        {
            var dial = SampledDialRaw;
            if (dial != null || _flaps.DetentFor(_flapRng)?.Dial != true)
            {
                _lastFlapSpoken = _flaps.DescribePosition(_flapRng, dial);
                seeded++;
            }
        }

        Log.Debug("MD11", $"Context reset: {seeded} baselines seeded from the cache after {_seedGate.Deliveries} batch deliveries "
            + $"({trigger}: {_seedGate.QuietDeliveries} quiet; the aircraft's own change seen: {_seedGate.SawChange}).");
    }

    /// <summary>The flap read-out holds no sample and no spoken baseline (tests).</summary>
    internal bool FlapReadoutIsEmpty =>
        double.IsNaN(_flapRng) && double.IsNaN(_dialRaw) && _lastFlapSpoken.Length == 0;

    /// <summary>Seeds one listed scalar into its tracker when that tracker is still empty; true when it did.</summary>
    internal bool SeedScalar(string key, double value)
    {
        switch (key)
        {
            case Md11Squawk.CodeKey:
                return _squawk.SeedIfEmpty(value);
            case Md11Fcp.ReadCaptainBaro:
                return _altimeter.SeedIfEmpty(value);
            case Md11SpeedbrakeSystem.ArmKey:
                if (!double.IsNaN(_spdbrkHandle)) return false;
                _spdbrkHandle = value;
                return true;
            case Md11SpeedbrakeSystem.LeverKey:
                if (_lastSpoilerSpoken.Length != 0 || Md11SpeedbrakeSystem.DescribeTravel(value) is not string detent) return false;
                _spdbrkRng = value;
                _lastSpoilerSpoken = $"Spoilers {detent.ToLowerInvariant()}";
                return true;
            // The two flap vars restore their own samples; the read-out's baseline needs BOTH and is
            // recorded after the loop (SeedFromCache), so it is never composed from half of them.
            case Md11FlapSystem.LeverKey:
                if (!double.IsNaN(_flapRng)) return false;
                _flapRng = value;
                return true;
            case Md11FlapSystem.DialKey:
                if (!double.IsNaN(_dialRaw)) return false;
                _dialRaw = value;
                return true;
            default:
                if (Md11VSpeeds.IsKey(key)) return _vSpeeds.SeedIfEmpty(key, value);
                if (Array.IndexOf(Md11Radios.Keys, key) >= 0) return _com.SeedIfEmpty(key, value);
                Log.Debug("MD11", $"Context reset: {key} is listed in SeededScalarKeys but has no tracker to seed.");
                return false;
        }
    }
}
