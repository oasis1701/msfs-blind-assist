using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// TFDi Design MD-11 accessibility definition.
///
/// ═══ HOW THIS AIRCRAFT DIFFERS FROM EVERY OTHER ONE MSFSBA SUPPORTS ═══
///
/// 1. IT IS EVENT-DRIVEN, NOT VARIABLE-DRIVEN. TFDi's Integration Guide states it plainly:
///    "variables and systems are driven by an event, not by reading the state of an L:VAR…
///    Writing directly to any of the variables will bypass our integrity checks." There is no
///    per-control L:var to set. EVERY control is actuated by writing its event id to the single
///    L:var <c>CEVENT</c>. See <see cref="Md11EventBus"/> for the three constraints that fall out
///    of that (anti-coalescing, pacing, press+release) — each one is load-bearing.
///
///    The sanctioned exception is <c>MD11_EXTCTL_*</c>, which TFDi documents as "designed for
///    external control": those ARE direct writes by design.
///
/// 2. EVERY CONTROL IS RELATIVE. Knobs expose WHEEL_UP/WHEEL_DOWN; rotary switches expose
///    left-click/right-click. Nothing accepts "go to position 3". MSFSBA's panels are combo
///    boxes, so <see cref="Md11SelectorWalker"/> closes the loop — and, because the direction of
///    a wheel event is documented nowhere, calibrates its sign against the aircraft instead of
///    guessing.
///
/// 3. ⚠ THE DISPLAYS CANNOT BE READ. This is the big one, and it is a property of the aircraft,
///    not a gap in this code. Every DU, all three MCDUs, the ISFD, the AFS and FUEL panels are
///    declared in PANEL.CFG as
///        WasmInstrument.html?wasm_module=md11host.wasm&amp;wasm_gauge=DU1
///    i.e. they are rendered from inside md11host.wasm. There is NO HTML DOM behind them — so
///    the Coherent-debugger scrape that reads the PMDG/FBW/HS787 CDUs has nothing to read here.
///    The MCDU key buttons are still exposed (they cost nothing), but WITHOUT THE SCREEN they
///    are close to useless, and no amount of work on this file changes that. Reading the MD-11's
///    CDU needs a different channel from TFDi (their "Data Export", or the CDA the wasm creates)
///    — do not sink time into DOM scraping, there is no DOM.
///
///    The EFB is the exception: PANEL.CFG declares it as real HTML
///    (<c>aircraft_efb/TFDi_MD11_efb/efb.html</c>), so it IS scrapeable the same way the PMDG
///    EFB is. Not wired up yet.
///
///    What partially compensates: TFDi export the numbers a pilot would otherwise read off the
///    glass — V-speeds, minimums, altimeters, FCP windows, AP/ATS/APU state, fuel — as plain
///    L:vars. Those are registered here and drive the hotkey read-outs.
///
/// ═══ THE CONTROL MAP ═══
///
/// The 1361 controls are not hand-written. <c>tools/md11-gen/generate_md11_map.py</c> reads the
/// aircraft's own ModelBehaviorDefs + wasm and emits <c>md11_control_map.json</c> (embedded);
/// this class turns that into variables and panels. Labels, detent names and switch positions
/// are TFDi's own tooltip wording, so the screen reader says what the real cockpit says.
/// Regenerate the JSON after a TFDi update rather than patching C#.
///
/// ═══ THE DATA-DEFINITION BUDGET ═══
///
/// SimConnect caps a client at ~1000 data definitions and 1361 controls would obliterate that,
/// so the split below is deliberate, not incidental:
///   • momentary buttons (497) → UpdateFrequency.Never  → write-only, registered as 0 defs
///   • annunciators (488)      → Continuous+IsAnnounced → batch-covered, 0 individual defs
///   • everything else         → OnRequest              → the latching buttons, guards, switches,
///                                                        knobs, levers and handles, plus the base
///                                                        and export vars: 378 defs (cap is 900)
/// Watch <c>registration.log</c>'s approxTotalDefs after any change here.
///
/// ═══ VERIFY IN SIM (nothing below has been flown) ═══
///   • CEVENT actuation end-to-end: does a panel button actually move the switch?
///   • Wheel/click polarity: the walker self-calibrates, but confirm it converges and that the
///     log line "step polarity calibrated to …" appears at most once per control.
///   • Flap handle: all six detents, and that the 28 gate behaves (35/50 → 28 on a go-around).
///   • Dial-A-Flap: 10–25° selection lands on the chosen whole degree (one direct write of the wheel's own var).
///   • Annunciator chattiness: 488 announcing lamps may be a torrent on startup. If so, the
///     answer is Ctrl+M (monitor manager) and/or trimming IsAnnounced to a safety subset here.
/// </summary>
public partial class TFDiMD11Definition : BaseAircraftDefinition, IDisposable
{
    public override string AircraftName => "TFDi Design MD-11";
    public override string AircraftCode => "TFDI_MD11";

    private readonly Md11ControlMap _map;
    private readonly Md11FlapSystem _flaps;
    private readonly Dictionary<string, Md11Control> _byNodeId;

    /// <summary>The map's read-only exports (<c>export_vars</c>), for <see cref="Md11ExportBacked"/>.</summary>
    private readonly HashSet<string> _exportVars;

    /// <summary>Stock DC bus voltage: half of the "annunciators have power" gate. Silent; read from the cache by the hook.</summary>
    public const string DcPowerKey = "MD11_DC_BUS_VOLTAGE";

    /// <summary>
    /// The other half of that gate: DC bus 1's own OFF annunciator, already registered as an
    /// ordinary lamp. Lit means the DC busses that feed the systems annunciators are dead, which
    /// is the normal battery-only state at 24 V — see <see cref="Md11ControlState.IsPowered"/>.
    /// </summary>
    public const string Dc1BusOffKey = "MD11_OVHD_ELEC_DC1_BUS_OFF_LT";

    /// <summary>L:var name → variable KEY (a lamp's node id is its key; a few lamps light from a foreign VIS_VAR).</summary>
    private readonly Dictionary<string, string> _keyByStateVar = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lamp KEY → the buttons whose state folds that lamp in (spec §3.7: a lamp change speaks its owner's state).</summary>
    private readonly Dictionary<string, List<Md11Control>> _lampOwners = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The silent numeric read-outs (every <c>Export()</c> var): continuously batched so the cache
    /// and hotkey read-outs stay fresh, but NEVER narrated on change — a raw "Engine 1 N1: 70.3"
    /// stream every second is unusable. ProcessSimVarUpdate consumes these so the generic announce
    /// gate (MainForm Step 6) never speaks them; the one exception is the N1-reaching-70% take-off
    /// cue. Populated in <see cref="BuildVariables"/> by their signature.
    /// </summary>
    private readonly HashSet<string> _silentReadouts = new(StringComparer.Ordinal);

    private Md11EventBus? _bus;
    private SimConnectManager? _sim;

    /// <summary>
    /// Serializes the walker's polarity persistence: two controls can learn their polarity at the
    /// same moment on two pool threads, and an unguarded read-compute-swap lets the second swap
    /// discard the first's entry — learned for the session, lost on disk.
    /// </summary>
    private static readonly object PolarityPersistLock = new();

    public TFDiMD11Definition()
    {
        _map = Md11ControlMap.Load();
        _exportVars = new HashSet<string>(_map.ExportVars, StringComparer.OrdinalIgnoreCase);
        _byNodeId = new Dictionary<string, Md11Control>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _map.Controls) _byNodeId[c.NodeId] = c;
        // TWO PASSES, and the order is load-bearing. A control whose state var IS its own node id
        // owns that name outright — its own definition is the one registered under it. Only then
        // may a FOREIGN control claim a name it merely reads. First-wins alone let map order decide:
        // MD11_OVHD_PNEU_ECON_BT's tooltip reads the air-system selector's var, so it too carries
        // state_var = MD11_OVHD_PNEU_SYSTEM_SEL_BT and, sorting first, claimed it — pointing the
        // selector's own latch at a write-only (UpdateFrequency.Never) def that is never registered,
        // so "Air System Mode" could never say Auto or Manual.
        foreach (var c in _map.Controls)
            if (!string.IsNullOrEmpty(c.StateVar) && string.Equals(c.StateVar, c.NodeId, StringComparison.OrdinalIgnoreCase))
                _keyByStateVar[c.StateVar] = c.NodeId;
        foreach (var c in _map.Controls)
            if (!string.IsNullOrEmpty(c.StateVar) && !_keyByStateVar.ContainsKey(c.StateVar))
                _keyByStateVar[c.StateVar] = c.NodeId;
        foreach (var c in _map.Controls)
        {
            if (c.Kind != Md11Kinds.Button || c.State == null) continue;
            foreach (var lamp in c.State.Lamps)
            {
                var key = KeyFor(lamp.Var);
                if (!_lampOwners.TryGetValue(key, out var owners)) _lampOwners[key] = owners = new List<Md11Control>();
                owners.Add(c);
            }
        }
        _flaps = new Md11FlapSystem(_map);
    }

    private string KeyFor(string stateVar) => _keyByStateVar.TryGetValue(stateVar, out var k) ? k : stateVar;

    /// <summary>The keys MainForm must watch to keep this control's label current.</summary>
    private IReadOnlyList<string>? StateDependencies(Md11Control c)
    {
        if (c.State == null) return null;
        var deps = new List<string>();
        foreach (var lamp in c.State.Lamps) deps.Add(KeyFor(lamp.Var));
        if (c.State.Latch != null) deps.Add(KeyFor(c.State.Latch.Var));
        // Both halves of the power gate: a change in either can turn every composed state on the
        // visible panel into "unpowered" or back, and MainForm's reverse index only relabels a row
        // whose dependencies it was told about.
        deps.Add(DcPowerKey);
        deps.Add(Dc1BusOffKey);
        return deps;
    }

    /// <summary>
    /// The FCP takes direct values, so all four windows get a type-in box.
    ///
    /// This used to say the opposite — "the MD-11 has no FCU value-set events … the direct-entry
    /// dialogs do not apply" — and walked the knobs a click at a time. That was wrong. TFDi export
    /// <c>MD11_EXTCTL_FCP_{SPD,HDG,ALT,VR}</c> for exactly this, and a live probe confirmed a write
    /// lands in the FCP window (see <see cref="Md11Fcp"/>). The earlier verdict came from reading
    /// the aircraft as event-driven-therefore-relative and never testing the one family documented
    /// as "designed for external control" — the Troubleshooting Playbook's standing warning about
    /// concluding "X doesn't work" without probing the right path.
    /// </summary>
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    public override Dictionary<string, string> GetButtonStateMapping() => new();

    /// <summary>
    /// Heavy three-engine widebody. Numbers mirror the 747/777 class (similar approach attitude
    /// and inertia). The glidepath/flare biases are ESTIMATES — calibrate against a coupled ILS
    /// autoland before trusting the flare cue, exactly as the 747 and A330 profiles still need.
    /// </summary>
    public override VisualGuidanceProfile GetVisualGuidanceProfile() => new()
    {
        TypicalApproachAoaDeg = 5.0,
        ReferenceVrefKnots = 155.0,     // MD-11 Vref is high for its size
        MaxPitchRateDegPerSec = 2.0,
        MaxBankRateDegPerSec = 3.0,
        GlideslopeAltitudeBiasFt = 75.0,   // estimate — calibrate vs a coupled ILS autoland
        FlareAltitudeBiasFt = 35.0,   // estimate
        FlareTriggerWheelHeightFt = 35.0,
        FlareTargetPitchDeg = 5.0,
        TonePitchRangeDeg = 10.0
    };

    public override double TaxiTurnLeadSeconds => 0.6;   // long-wheelbase trijet

    /// <summary>Captures the SimConnect handle and spins up the CEVENT pump.</summary>
    public void Attach(SimConnectManager sim)
    {
        _sim = sim;
        _bus ??= new Md11EventBus(sim);
        // Every caller reaches here on the UI thread (MainForm's aircraft switch, a panel write, a
        // hotkey), and this is the EARLIEST of them — it runs before the first lamp can arrive.
        // A deferred dark transition needs the context to exist by then: OnUiThread falls back to
        // running inline, which on that path would be a thread-pool thread mutating the gate's
        // unlocked dictionaries beside HandleLampUpdate. SetControl's capture used to be the only
        // one, so until the pilot pressed something there was nothing to marshal to.
        _uiContext ??= SynchronizationContext.Current;

        // Wired here, not in the constructor: Attach is production-only (MainForm's aircraft switch
        // and every control write), while the test suite constructs this definition freely and must
        // never reach the real settings file through a process-wide hook.
        // The walker's learned step polarity survives the session: settings carry the node ids that
        // stepped the wrong way once. Static hooks, re-assigned by every definition instance — they
        // capture nothing, so an aircraft switch leaks nothing. Swap the list, never mutate it:
        // SettingsManager.Save serializes the live settings object on another thread.
        Md11SelectorWalker.LoadPolarity = id =>
            Md11PolarityStore.Load(Settings.SettingsManager.Current.Md11InvertedStepControls, id);
        Md11SelectorWalker.SavePolarity = (id, conventional) =>
        {
            lock (PolarityPersistLock)
            {
                var settings = Settings.SettingsManager.Current;
                var updated = Md11PolarityStore.With(settings.Md11InvertedStepControls, id, conventional);
                if (ReferenceEquals(updated, settings.Md11InvertedStepControls)) return;
                settings.Md11InvertedStepControls = updated;
                Settings.SettingsManager.Save();
            }
        };
    }

    /// <summary>
    /// Presses a mapped control by node id, returning false if it could not be pressed.
    ///
    /// This is the whole surface windows like the MCDU need: they press keys by name and have no
    /// business holding the bus (which owns CEVENT's anti-coalescing sequence and its pacing — a
    /// second writer would defeat both) or the control map. False means "the press did not
    /// happen", which callers must SPEAK rather than swallow: on an aircraft whose screens a blind
    /// pilot cannot see, a silently dropped keystroke is indistinguishable from a successful one.
    /// </summary>
    public bool PressControl(string nodeId)
    {
        var bus = _bus;                                                   // read once: Dispose may null it
        if (bus == null || !CanDeliver) return false;                     // Attach hasn't run, or nothing would land
        var control = PressableControl(nodeId);
        if (control == null) return false;

        bus.Press(control);
        return true;
    }

    /// <summary>
    /// True when a press would actually REACH the aircraft: a bus is attached AND the transport
    /// under it can send right now (<see cref="SimConnectManager.CalcWriteCanLand"/>).
    ///
    /// The second half is not belt-and-braces. <see cref="_bus"/> is created in
    /// <see cref="Attach"/> and nulled only in <see cref="Dispose"/> — a SimConnect drop leaves it
    /// attached — while the bus writes through <c>ExecuteCalculatorCode</c>, which during an outage
    /// returns having done nothing. The pump takes the id off its queue either way, so a keystroke
    /// pressed then is CONSUMED and lost: without this the MCDU window announced nothing, cleared
    /// the pilot's typed line, and left them to retype text they could not see had never gone in.
    ///
    /// It asks <see cref="SimConnectManager.CalcWriteCanLand"/>, not the bare
    /// <c>CanExecuteCalculatorCode</c>: the latter is TRUE in a session with no MobiFlight WASM
    /// module (the object exists whether or not the module is installed), which is precisely the
    /// configuration where every CEVENT write is discarded. The bridge probe is the only
    /// end-to-end evidence, and this aircraft registers <c>MSFSBA_BRIDGE_PROBE</c> — so a concluded
    /// unverified probe here really does mean the writes are going nowhere, while the probe's
    /// pending window stays permissive so nothing is refused that would have worked.
    /// </summary>
    private bool CanDeliver => _bus != null && _sim?.CalcWriteCanLand == true;

    /// <summary>
    /// True when <see cref="PressControl"/> would press <paramref name="nodeId"/> right now: the
    /// press can be delivered (<see cref="CanDeliver"/>), the node is mapped, and it has a
    /// LEFT_BUTTON_DOWN. The same conditions, not a copy of them — both go through
    /// <see cref="CanDeliver"/> and <see cref="PressableControl"/>.
    ///
    /// For a caller that presses SEVERAL keys which must all land or none: the MCDU window checks
    /// a whole typed entry before its first key, because a key that fails part-way leaves the
    /// scratchpad holding text the pilot did not type.
    /// </summary>
    public bool CanPress(string nodeId) => CanDeliver && PressableControl(nodeId) != null;

    /// <summary>The mapped control a left-click press reaches, or null (unmapped, or no LEFT_BUTTON_DOWN).</summary>
    private Md11Control? PressableControl(string nodeId) =>
        _byNodeId.TryGetValue(nodeId, out var control) && control.Events.ContainsKey("LEFT_BUTTON_DOWN")
            ? control
            : null;

    /// <summary>
    /// Fires a NAMED event pair on a control — for actions that are not a plain left-click.
    ///
    /// The FCP's speed/heading/altitude knobs are push-pull (the map's knob_pp kind): push and
    /// pull are distinct physical actions with their own PUSH_/PULL_ event pairs, alongside the
    /// WHEEL_ pair that turns them. <see cref="PressControl"/> can only reach the left-click pair,
    /// so a push or a pull has to name its events.
    ///
    /// False means the press did NOT happen — no bus, an unmapped node, no such events, or the
    /// transport cannot send (<see cref="CanDeliver"/>) — and every caller must SPEAK it, in the
    /// one sentence <see cref="Md11Fcp.Unavailable"/> owns. On an aircraft whose FCP window a blind
    /// pilot cannot read, a dropped press is indistinguishable from an accepted one.
    /// </summary>
    public bool PressControlEvents(string nodeId, string downEvent, string upEvent)
    {
        var bus = _bus;                                                   // read once: Dispose may null it
        if (bus == null || !CanDeliver) return false;                     // Attach hasn't run, or nothing would land
        if (!_byNodeId.TryGetValue(nodeId, out var control)) return false;

        var down = control.Event(downEvent);
        var up = control.Event(upEvent);
        if (down == null && up == null) return false;

        bus.FirePressRelease(down, up);
        return true;
    }

    /// <summary>
    /// Fires a SINGLE named event on a control — for wheel steps, which are one event each
    /// (WHEEL_UP / WHEEL_DOWN), not a DOWN/UP pair like a button or a push/pull knob.
    ///
    /// The V/S / FPA wheel is the case in point: on the MD-11 there is no "engage V/S" button —
    /// rotating that wheel is what engages the pitch mode — so the pilot needs to fire its wheel
    /// events directly. Returns false if the control or event is unknown, or if the transport
    /// cannot send (<see cref="CanDeliver"/>), which callers must SPEAK through
    /// <see cref="Md11Fcp.Unavailable"/>: on an aircraft with no readable FCP window a dropped step
    /// looks identical to a taken one — and this wheel is what ENGAGES the pitch mode.
    /// </summary>
    public bool FireControlEvent(string nodeId, string eventName)
    {
        var bus = _bus;                                                   // read once: Dispose may null it
        if (bus == null || !CanDeliver) return false;                     // Attach hasn't run, or nothing would land
        if (!_byNodeId.TryGetValue(nodeId, out var control)) return false;

        var id = control.Event(eventName);
        if (id == null) return false;

        bus.Fire(id.Value);
        return true;
    }

    /// <summary>
    /// The selectable Dial-A-Flap take-off angles as raw-value → spoken-label pairs, for a combo.
    /// Keyed by RAW thumbwheel units so a selection maps straight back onto what the aircraft
    /// reports; see <see cref="Md11FlapSystem.DialValueDescriptions"/>.
    /// </summary>
    public Dictionary<double, string> DialAFlapChoices() => _flaps.DialValueDescriptions();

    /// <summary>
    /// The <see cref="DialAFlapChoices"/> key nearest the wheel's current raw value — what a combo
    /// built from those choices must select to show the current setting. The var is continuous, so
    /// an exact-key match misses on every real value; see <see cref="Md11FlapSystem.NearestDialChoice"/>.
    /// </summary>
    public double NearestDialAFlapChoice(double raw) => _flaps.NearestDialChoice(raw);

    /// <summary>
    /// Sets one FCP window, optionally switching its unit first.
    ///
    /// Unit BEFORE value, deliberately: the unit decides how the FCC reads the number, so writing
    /// 0.82 while the window is still in IAS would hand the autopilot a 0.82-knot target for the
    /// instant before the unit lands. Both are one-shot inboxes that self-clear to -1, so there is
    /// nothing to reset afterwards.
    ///
    /// False means NOTHING was written — no bus, or the transport cannot send
    /// (<see cref="CanDeliver"/>) — and the caller must SPEAK it and skip whatever it would have
    /// done next. The check is here rather than at each caller so the unit can never go out
    /// without its value: these are one-shot inboxes, and a unit written alone would make the FCC
    /// read the NEXT value in the wrong unit.
    /// </summary>
    public bool SetFcpValue(string valueVar, double value, SimConnectManager sim,
        string? unitVar = null, double? unit = null)
    {
        Attach(sim);
        if (_bus == null || !CanDeliver) return false;

        if (unitVar != null && unit != null) _bus.WriteExternal(unitVar, unit.Value);
        _bus.WriteExternal(valueVar, value);
        return true;
    }

    /// <summary>
    /// Sets the vertical-speed window AND engages the V/S / FPA pitch mode.
    ///
    /// Writing <see cref="Md11Fcp.WriteVerticalSpeed"/> on its own only puts a number in the
    /// window — on the MD-11 the pitch mode is engaged by ROTATING the V/S / FPA wheel, and the
    /// aircraft has no separate engage button (its only V/S controls are that wheel and the VS/FPA
    /// display toggle). So a value alone can sit in a window the FCC is not flying, which is exactly
    /// the "V/S doesn't engage" the reporter hit with the plain value-set.
    ///
    /// This nudges the wheel once to engage the mode, then writes the exact value AFTER the nudge
    /// has landed. Ordering is load-bearing: the wheel step rides the paced CEVENT queue while the
    /// EXTCTL write is immediate, so writing the value first would let the later wheel click drag it
    /// back off target. The single-detent transient is corrected the instant the value lands. The
    /// wait is measured from the click's expected WRITE (<see cref="Md11EventBus.ReadBackDelayMs"/>:
    /// the 250 ms settle plus the backlog queued ahead of the click), not from when it was queued —
    /// a fixed 250 ms covered only four queued ids.
    ///
    /// UNVERIFIED IN SIM — whether one wheel step reliably engages, and whether the EXTCTL value
    /// then holds, is exactly what the in-sim test must confirm; the manual wheel controls (FCP
    /// window + V/S dialog) are the reliable fallback if the auto-engage falls short.
    /// </summary>
    public void SetVerticalSpeedEngaged(double value, double unit, SimConnectManager sim,
        Accessibility.ScreenReaderAnnouncer announcer)
    {
        Attach(sim);
        if (_bus == null) return;

        _bus.WriteExternal(Md11Fcp.WriteVerticalSpeedUnit, unit);
        var backlogMs = _bus.BacklogMs;                             // sampled BEFORE the wheel click joins the queue
        if (!FireControlEvent(Md11Fcp.VerticalSpeedKnob, "WHEEL_UP"))   // engage V/S / FPA pitch mode
        {
            // The wheel is the ONLY way to engage the pitch mode here, so a nudge that cannot be
            // delivered would leave the typed value sitting in a window the FCC is not flying —
            // and the value write below rides the same dead transport anyway. Say so and send
            // nothing further: this path spoke NOTHING at all before, so a pilot who typed a
            // vertical speed during an outage got silence and an unflown mode. (The unit write
            // above has already gone out; during an outage it did nothing either, and it is an
            // idle-sentinel inbox, so it leaves no state behind.)
            announcer.Announce(Md11Fcp.Unavailable(Md11Fcp.VerticalSpeedName));
            return;
        }
        _ = SetAfterEngage(value, backlogMs);

        async Task SetAfterEngage(double v, int backlog)
        {
            // Let the paced wheel event land before correcting the rate to the typed value — its
            // settle runs from the click's expected WRITE, so behind a burst (an MCDU scratchpad
            // send) the late click can no longer land after the value and drag it off target.
            await Task.Delay(Md11EventBus.ReadBackDelayMs(250, backlog)).ConfigureAwait(false);
            _bus?.WriteExternal(Md11Fcp.WriteVerticalSpeed, v);
        }
    }

    // =================================================================================
    // Variables
    // =================================================================================

    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = GetBaseVariables();

        foreach (var c in _map.Controls)
        {
            if (vars.ContainsKey(c.NodeId)) continue;   // never shadow a base var
            var def = BuildControlVariable(c);
            if (def != null) vars[c.NodeId] = def;
            // A composite's INNER var (an engine fire handle's own rotation, the Elevator Feel knob's
            // own position) gets its own OnRequest read under a key of its own, because the control's
            // key reads the OUTER var (the pull, the MANUAL latch). Never announced and never a row:
            // only TryDescribeControlState reads it.
            if (def != null && c.Composite != null)
                vars[Md11CompositeState.InnerKeyFor(c.NodeId)] = new SimVarDefinition
                {
                    Name = c.Composite.InnerVar,
                    DisplayName = $"{c.DisplayLabel} (inner position)",
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.OnRequest,
                };
        }

        foreach (var kvp in BuildExportVariables())
            if (!vars.ContainsKey(kvp.Key)) vars[kvp.Key] = kvp.Value;

        vars[DcPowerKey] = new SimVarDefinition
        {
            Name = "ELECTRICAL MAIN BUS VOLTAGE",
            DisplayName = "DC bus voltage",
            Type = SimVarType.SimVar,
            Units = "Volts",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,                 // batch-covered; ProcessSimVarUpdate consumes it silently
            ExcludeFromMonitorManager = true,
            RenderAsReadOnlyStatus = true,
            // Half a volt, not the shared 0.001: the power gate needs only the 20 V line (battery or
            // ground power reads 24 V, cold and dark 0 V — steps, never a drift across it), and this
            // key is in every state-bearing row's StateVariables, so with the shared tolerance each
            // ripple of the bus re-composed every stateful row of the open panel (~32 on Electrical)
            // once a second. The DC1 BUS OFF lamp, the gate's other half, still relabels on its own.
            ChangeTolerance = 0.5,
        };

        // End-to-end MobiFlight probe target — the same var the FBW defs register. MainForm
        // calc-writes a nonce here and reads it back over the data-def path; a match is the ONLY
        // signal that the WASM module is actually executing our RPN. Every control write on this
        // aircraft is a calculator-path write (Md11EventBus: CEVENT and the direct-write family),
        // and with no module installed each one lands in a dead client-data area with nothing
        // to say so: MobiFlightWasmModule.IsConnected is true without a module (Initialize is
        // purely local setup) and the bus writes quiet. Registering this var is what opts the
        // MD-11 into the probe and its verdict — CalcPathVerdict.LogLine in debug.log, and the
        // spoken PilotWarning when the round-trip never succeeds. OnRequest (one individual def),
        // in no panel, never announced. NOT in SeededScalarKeys: the nonce is ours, not the
        // aircraft's, and the seed gate's IsSeededFromCache whitelist is what keeps it out —
        // IsAircraftOwned would say yes, since it is an L:var in this dictionary.
        vars["MSFSBA_BRIDGE_PROBE"] = new SimVarDefinition
        {
            Name = "MSFSBA_BRIDGE_PROBE", DisplayName = "Bridge Probe",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest
        };

        // Silent read-outs: every Export() var is Continuous+IsAnnounced+LVar with NO
        // ValueDescriptions (a bare number, meaningless spoken). That signature is also the
        // generic auto-announce condition, so without consuming them they narrate on every batch
        // tick. Collect them so ProcessSimVarUpdate can silence them. Annunciators / decoded
        // Announced() vars carry ValueDescriptions and are excluded; so are the flap lever/dial,
        // which own their own wording.
        _silentReadouts.Clear();
        foreach (var (key, d) in vars)
        {
            if (d.Type == SimVarType.LVar && d.IsAnnounced
                && d.UpdateFrequency == UpdateFrequency.Continuous
                && (d.ValueDescriptions == null || d.ValueDescriptions.Count == 0)
                && key != Md11FlapSystem.LeverKey && key != Md11FlapSystem.DialKey)
            {
                _silentReadouts.Add(key);
            }
        }

        Log.Info("MD11", $"Built {vars.Count} variables from {_map.Controls.Count} controls + {_map.ExportVars.Count} exports; {_silentReadouts.Count} silent read-outs.");
        return vars;
    }

    /// <summary>
    /// One control → one SimVarDefinition. The UpdateFrequency choice here IS the data-definition
    /// budget strategy (see the class remarks) — change it and re-check registration.log.
    /// </summary>
    private SimVarDefinition? BuildControlVariable(Md11Control c)
    {
        var label = c.DisplayLabel;
        var values = ValueDescriptionsFor(c);

        switch (c.Kind)
        {
            case Md11Kinds.Option:
                return null;   // MD11_OPT_* configuration flags: not controls, not lamps

            // Momentary or latching push-buttons. A PROVEN latch (its own L:var holds the
            // position — TFDi's tooltip reads it, or the battery, measured live) is read on
            // panel open and after a press; everything else stays write-only and costs no
            // data definition. The spoken state is composed by TryDescribeControlState.
            case Md11Kinds.Button:
                return new SimVarDefinition
                {
                    Name = c.StateVar,
                    DisplayName = label,
                    Type = SimVarType.LVar,
                    UpdateFrequency = c.State?.Latch != null ? UpdateFrequency.OnRequest : UpdateFrequency.Never,
                    RenderAsButton = true,
                    SuppressRestingButtonState = true,
                    StateVariables = StateDependencies(c),
                };

            // Indicator lamps. Batch-covered (free). DisplayName is the generated spoken name
            // ("External Power AVAIL light", "AC Bus 1"); the value words come from the state
            // block so a standalone light reads "AC Bus 1: Off" / "AC Bus 1: Powered".
            case Md11Kinds.Annunciator:
            {
                var self = c.State?.Lamps.FirstOrDefault();
                string lit = self?.Lit ?? "on";
                string dark = c.State?.Dark ?? "off";
                return new SimVarDefinition
                {
                    Name = c.StateVar,
                    DisplayName = label,
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.Continuous,
                    IsAnnounced = true,
                    ValueDescriptions = new Dictionary<double, string> { [0] = dark, [1] = lit },
                    RenderAsReadOnlyStatus = true,
                    ExcludeFromMonitorManager = string.IsNullOrEmpty(lit),   // a lamp with nothing to say (APU BLANK)
                    StateVariables = new[] { c.NodeId, DcPowerKey, Dc1BusOffKey },
                };
            }

            // Guard covers. A guard is a click-toggle with no positions (empty value map), so it is
            // rendered as an operable BUTTON rather than a combo — the pilot can lift/lower it by
            // hand, the manual fallback for the state-aware auto-open (EnsureGuardOpenAsync). Kept
            // OnRequest (not Never) so that auto-open can still read the cover's state on demand.
            case Md11Kinds.Guard:
                return new SimVarDefinition
                {
                    Name = c.StateVar,
                    DisplayName = label,
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.OnRequest,
                    RenderAsButton = true,
                    StateVariables = StateDependencies(c),
                };

            // Everything with an operable, readable position: switches, knobs, levers,
            // fire handles. OnRequest — read on demand when a panel opens, ~206 defs total.
            case string positional when Md11ExportBacked.IsPositional(positional):
            {
                // The flap handle and Dial-A-Flap thumbwheel MUST stream continuously, not
                // OnRequest. A blind pilot moving the physical lever needs the new detent spoken
                // live (ProcessSimVarUpdate composes it from these two vars), and the on-demand
                // read-out (ReadFlaps) reads the cache — an OnRequest var that nothing ever
                // requests stays uncached and reports "unavailable".
                //
                // But they must NOT ride the 1 Hz continuous BATCH: the closed-loop walk that SETS
                // them fires a step and re-reads to see if it moved, and at 1 Hz that read is up to
                // a second stale, so the walk reads "no movement" and bails (or false-wins on drift)
                // — which is exactly why the handle refused to reach 50 and the wheel undershot. A
                // per-var SIM_FRAME feed (ExcludeFromBatch + HighFrequency, with the CHANGED flag)
                // makes every read fresh while a stationary control still delivers nothing.
                bool flapStream = c.NodeId == Md11FlapSystem.LeverKey || c.NodeId == Md11FlapSystem.DialKey
                    // The speedbrake lever too: a hardware lever's detent is spoken live and its
                    // walk reads fresh. It reads the TRAVEL var, not its own node var — see
                    // Md11SpeedbrakeSystem for the three-variable model the tooltip revealed.
                    || c.NodeId == Md11SpeedbrakeSystem.LeverKey;
                // The Dial-A-Flap thumbwheel's declared state_var is the INDICATOR needle
                // (MD11_DIALAFLAP_IND_RNG), which the cockpit XML animates with ANIM_LAG=1000 — it
                // trails the real value by ~1 s, so the closed-loop CEVENT walk this path used to
                // use read it a step behind, saw "no movement", and bailed. Read the knob's OWN live
                // L:var (the NodeId, MD11_DIALAFLAP_WHEEL_RNG — the OVERRIDE_ANIM_CODE source)
                // instead: it follows the wheel's real value rather than the needle's animation.
                // The wheel is no longer walked at all — SetDialRawAsync writes this same var once,
                // directly — but the read var stays the right one: it is what the set's read-back
                // polls until the written value arrives, and what the display row and the spoken
                // read-out show. Same 0–100 → 10–25° scale, so DegreesFor is unchanged.
                string readVar = c.NodeId == Md11FlapSystem.DialKey ? c.NodeId
                    : c.NodeId == Md11SpeedbrakeSystem.LeverKey ? Md11SpeedbrakeSystem.TravelVar
                    : c.StateVar;
                var def = new SimVarDefinition
                {
                    Name = readVar,
                    DisplayName = label,
                    Type = SimVarType.LVar,
                    UpdateFrequency = flapStream ? UpdateFrequency.Continuous : UpdateFrequency.OnRequest,
                    IsAnnounced = flapStream,               // required to join continuous monitoring
                    ExcludeFromBatch = flapStream,          // per-var subscription, not the 1 Hz batch
                    HighFrequency = flapStream,             // SIM_FRAME + CHANGED: fresh reads for the walk
                    // NOT ExcludeFromMonitorManager, even though ProcessSimVarUpdate owns the
                    // wording: that flag means "muted by plumbing, a checkbox here would silence
                    // nothing", and these two DO speak (the composed flap read-out). Unticking
                    // either really does silence its trigger, so both stay Ctrl+M rows.
                    ValueDescriptions = c.NodeId == Md11SpeedbrakeSystem.LeverKey ? Md11SpeedbrakeSystem.TravelValues : values,
                    // No ValueDescriptions means a bare number with no meaning to speak — render
                    // it read-only rather than offering an empty combo the user cannot use.
                    // So does a state var that is one of TFDi's read-only EXPORTS: the FCP mode
                    // knobs read MD11_AP_HDG_TRK / IAS_MACH / VS_FPA and the EFIS minimums caps
                    // read MD11_CAP/FO_MINIMUMS, but their wheel moves the heading/speed/V-S/
                    // minimums VALUE, never that var — a combo on it can only stall its walk, and
                    // the direct-write fallback then zeroed the export. The FCP rows keep the mode
                    // words as a status field ("Heading"/"Track"); the mode is switched by its own
                    // button beside the row. See Md11ExportBacked.
                    // So does a COMPOSITE (the engine fire handles and the Elevator Feel knob,
                    // Md11CompositeState): its row reads the OUTER var (a handle's pull, the knob's
                    // MANUAL latch) while its wheel turns the INNER one (the bottle-discharge rotation,
                    // the knob itself), so a walk could never land. Its words come from
                    // TryDescribeControlState, which reads the outer var under this key and the inner
                    // one under its inner key — so MainForm must read and watch exactly those two (no
                    // power term: a position is not an annunciator). StateVariables is also what makes
                    // MainForm build the status box whatever the description count (the knob has one;
                    // Utils.PanelRowRules).
                    RenderAsReadOnlyStatus = values.Count == 0 || c.Composite != null
                                             || Md11ExportBacked.IsReadOnly(c, _exportVars),
                    StateVariables = c.Composite != null
                        ? new[] { c.NodeId, Md11CompositeState.InnerKeyFor(c.NodeId) }
                        : null,
                };
                // The gear lever's var is its 0-25 TRAVEL against the map's {0 Up, 1 Down}, and
                // MainForm's combo lookup is an exact key match — parked at 25 the combo selected
                // nothing. Classify the travel onto the map's keys by TFDi's own threshold, the
                // same rule the gear hotkey read-out uses (Md11GearLever). The pick still writes
                // the key, which is what the walker's two-position toggle resolves against.
                if (c.NodeId == Md11GearLever.Key) def.ValueToDescriptionKey = Md11GearLever.DescriptionKey;
                // Both flap controls and the speedbrake lever are combos of discrete positions over
                // a var that is not discrete: the thumbwheel's raw value is continuous, the handle's
                // Dial-A-Flap detent is a BAND (FLAP_RNG 38-65 — parked at 46.91 in TFDi's
                // ReadyToFly state), and the speedbrake's travel (MD11_SPDBRK_RNG) streams every
                // frame and rests wherever the lever stopped, a little off its detent (26.4 for
                // "2/3 extended"). None of the three reliably rests on an exact key, and a value
                // that does not matched nothing, so the combo opened with NO selection, and in a
                // DropDownList the first Down-arrow selects row 0 and COMMITS it: 10 degrees on the
                // wheel, "Flap Up / Slat Retracted" on the handle — a walk that RETRACTS the flaps —
                // and "Retracted" on the Spoilers row. Each classifies by the rule its own read-out
                // already uses. MainForm applies the classifier only when it builds the panel: its
                // live re-sync never sees these three, because ProcessSimVarUpdate consumes every
                // delivery of them. So a handle or lever between detents opens with nothing
                // selected, and once built each combo keeps what it opened on, or the pilot's last
                // pick, while the control moves on.
                else if (c.NodeId == Md11FlapSystem.DialKey) def.ValueToDescriptionKey = _flaps.NearestDialChoice;
                else if (c.NodeId == Md11FlapSystem.LeverKey) def.ValueToDescriptionKey = _flaps.LeverDetentKey;
                else if (c.NodeId == Md11SpeedbrakeSystem.LeverKey) def.ValueToDescriptionKey = Md11SpeedbrakeSystem.TravelDescriptionKey;
                return def;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// TFDi's value→label map, parsed to numbers. The flap lever's descriptions come from the
    /// curated detents instead, because the tooltip's %{case} block is missing the Dial-A-Flap
    /// position entirely (it is an RPN range test) — see <see cref="Md11FlapSystem"/>.
    /// </summary>
    private Dictionary<double, string> ValueDescriptionsFor(Md11Control c)
    {
        if (c.NodeId == Md11FlapSystem.LeverKey) return _flaps.LeverValueDescriptions();
        if (c.NodeId == Md11FlapSystem.DialKey) return _flaps.DialValueDescriptions();
        // A composite's value map is empty by design; its row carries the OUTER words as the text shown
        // before there is a cache, and the rest is composed by TryDescribeControlState
        // (Md11CompositeState.OuterDescriptions).
        if (c.Composite != null) return Md11CompositeState.OuterDescriptions(c.Composite);

        var d = new Dictionary<double, string>();
        foreach (var kvp in c.ValueMap)
            if (double.TryParse(kvp.Key, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                d[v] = kvp.Value;
        return d;
    }

    public void Dispose()
    {
        _announceGeneration++;   // a deferred dark transition must not speak for a disposed definition
        _seedGate.Disarm();      // nor may a pending seed pass run for one
        CancelWalks();           // nor may a walk in flight finish — and speak — against the next aircraft
        _bus?.Dispose();         // writes what is queued and releases any held test button (Md11EventBus.Dispose)
        _bus = null;
        _sim = null;             // every reader null-checks; a late callback must not read the next aircraft's cache
        DisposeTrackedWindows();
        GC.SuppressFinalize(this);
    }
}
