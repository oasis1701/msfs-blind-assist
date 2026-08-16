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
/// The 1404 controls are not hand-written. <c>tools/md11-gen/generate_md11_map.py</c> reads the
/// aircraft's own ModelBehaviorDefs + wasm and emits <c>md11_control_map.json</c> (embedded);
/// this class turns that into variables and panels. Labels, detent names and switch positions
/// are TFDi's own tooltip wording, so the screen reader says what the real cockpit says.
/// Regenerate the JSON after a TFDi update rather than patching C#.
///
/// ═══ THE DATA-DEFINITION BUDGET ═══
///
/// SimConnect caps a client at ~1000 data definitions and 1404 controls would obliterate that,
/// so the split below is deliberate, not incidental:
///   • buttons (666)       → UpdateFrequency.Never  → write-only, registered as 0 defs
///   • annunciators (532)  → Continuous+IsAnnounced → batch-covered, 0 individual defs
///   • everything else     → OnRequest              → ~206 individual defs (cap is 900)
/// Watch <c>registration.log</c>'s approxTotalDefs after any change here.
///
/// ═══ VERIFY IN SIM (nothing below has been flown) ═══
///   • CEVENT actuation end-to-end: does a panel button actually move the switch?
///   • Wheel/click polarity: the walker self-calibrates, but confirm it converges and that the
///     log line "step polarity calibrated to …" appears at most once per control.
///   • Flap handle: all six detents, and that the 28 gate behaves (35/50 → 28 on a go-around).
///   • Dial-A-Flap: 10–25° selection lands on whole degrees; check the analog walk converges.
///   • Annunciator chattiness: 532 announcing lamps may be a torrent on startup. If so, the
///     answer is Ctrl+M (monitor manager) and/or trimming IsAnnounced to a safety subset here.
/// </summary>
public partial class TFDiMD11Definition : BaseAircraftDefinition, IDisposable
{
    public override string AircraftName => "TFDi Design MD-11";
    public override string AircraftCode => "TFDI_MD11";

    private readonly Md11ControlMap _map;
    private readonly Md11FlapSystem _flaps;
    private readonly Dictionary<string, Md11Control> _byNodeId;

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

    public TFDiMD11Definition()
    {
        _map = Md11ControlMap.Load();
        _byNodeId = new Dictionary<string, Md11Control>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _map.Controls) _byNodeId[c.NodeId] = c;
        _flaps = new Md11FlapSystem(_map);
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
        if (_bus == null) return false;                                   // Attach hasn't run — no sim yet
        if (!_byNodeId.TryGetValue(nodeId, out var control)) return false;
        if (!control.Events.ContainsKey("LEFT_BUTTON_DOWN")) return false;

        _bus.Press(control);
        return true;
    }

    /// <summary>
    /// Fires a NAMED event pair on a control — for actions that are not a plain left-click.
    ///
    /// The FCP's speed/heading/altitude knobs are push-pull (the map's knob_pp kind): push and
    /// pull are distinct physical actions with their own PUSH_/PULL_ event pairs, alongside the
    /// WHEEL_ pair that turns them. <see cref="PressControl"/> can only reach the left-click pair,
    /// so a push or a pull has to name its events.
    /// </summary>
    public bool PressControlEvents(string nodeId, string downEvent, string upEvent)
    {
        if (_bus == null) return false;
        if (!_byNodeId.TryGetValue(nodeId, out var control)) return false;

        var down = control.Event(downEvent);
        var up = control.Event(upEvent);
        if (down == null && up == null) return false;

        _bus.FirePressRelease(down, up);
        return true;
    }

    /// <summary>
    /// Fires a SINGLE named event on a control — for wheel steps, which are one event each
    /// (WHEEL_UP / WHEEL_DOWN), not a DOWN/UP pair like a button or a push/pull knob.
    ///
    /// The V/S / FPA wheel is the case in point: on the MD-11 there is no "engage V/S" button —
    /// rotating that wheel is what engages the pitch mode — so the pilot needs to fire its wheel
    /// events directly. Returns false if the control or event is unknown, which callers must SPEAK:
    /// on an aircraft with no readable FCP window a dropped step looks identical to a taken one.
    /// </summary>
    public bool FireControlEvent(string nodeId, string eventName)
    {
        if (_bus == null) return false;
        if (!_byNodeId.TryGetValue(nodeId, out var control)) return false;

        var id = control.Event(eventName);
        if (id == null) return false;

        _bus.Fire(id.Value);
        return true;
    }

    /// <summary>
    /// The selectable Dial-A-Flap take-off angles as raw-value → spoken-label pairs, for a combo.
    /// Keyed by RAW thumbwheel units so a selection maps straight back onto what the aircraft
    /// reports; see <see cref="Md11FlapSystem.DialValueDescriptions"/>.
    /// </summary>
    public Dictionary<double, string> DialAFlapChoices() => _flaps.DialValueDescriptions();

    /// <summary>
    /// Sets one FCP window, optionally switching its unit first.
    ///
    /// Unit BEFORE value, deliberately: the unit decides how the FCC reads the number, so writing
    /// 0.82 while the window is still in IAS would hand the autopilot a 0.82-knot target for the
    /// instant before the unit lands. Both are one-shot inboxes that self-clear to -1, so there is
    /// nothing to reset afterwards.
    /// </summary>
    public bool SetFcpValue(string valueVar, double value, SimConnectManager sim,
        string? unitVar = null, double? unit = null)
    {
        Attach(sim);
        if (_bus == null) return false;

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
    /// back off target. The single-detent transient is corrected the instant the value lands.
    ///
    /// UNVERIFIED IN SIM — whether one wheel step reliably engages, and whether the EXTCTL value
    /// then holds, is exactly what the in-sim test must confirm; the manual wheel controls (FCP
    /// window + V/S dialog) are the reliable fallback if the auto-engage falls short.
    /// </summary>
    public void SetVerticalSpeedEngaged(double value, double unit, SimConnectManager sim)
    {
        Attach(sim);
        if (_bus == null) return;

        _bus.WriteExternal(Md11Fcp.WriteVerticalSpeedUnit, unit);
        FireControlEvent(Md11Fcp.VerticalSpeedKnob, "WHEEL_UP");   // engage V/S / FPA pitch mode
        _ = SetAfterEngage(value);

        async Task SetAfterEngage(double v)
        {
            // Let the paced wheel event land before correcting the rate to the typed value.
            await Task.Delay(250).ConfigureAwait(false);
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
        }

        foreach (var kvp in BuildExportVariables())
            if (!vars.ContainsKey(kvp.Key)) vars[kvp.Key] = kvp.Value;

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
            // Momentary push-buttons. Write-only: there is no resting state worth reading, and
            // Never keeps them off the data-definition budget entirely (666 of them).
            case Md11Kinds.Button:
                return new SimVarDefinition
                {
                    Name = c.StateVar,
                    DisplayName = label,
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.Never,
                    RenderAsButton = true,
                    SuppressRestingButtonState = true,
                };

            // Indicator lamps. A blind pilot cannot see a lamp illuminate, so this is the single
            // most valuable class of variable on the aircraft — and Continuous+IsAnnounced makes
            // them batch-covered, i.e. free. Stripped from panel controls (see BuildPanelControls):
            // a pilot navigates a panel to OPERATE things, not to scan "X Fault: Normal" rows.
            case Md11Kinds.Annunciator:
                return new SimVarDefinition
                {
                    Name = c.StateVar,
                    DisplayName = label,
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.Continuous,
                    IsAnnounced = true,
                    ValueDescriptions = values.Count > 0 ? values : LampStates,
                    RenderAsReadOnlyStatus = true,
                };

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
                };

            // Everything with an operable, readable position: switches, knobs, levers,
            // fire handles. OnRequest — read on demand when a panel opens, ~206 defs total.
            case Md11Kinds.Switch:
            case Md11Kinds.Knob:
            case Md11Kinds.KnobPush:
            case Md11Kinds.KnobPushPull:
            case Md11Kinds.Lever:
            case Md11Kinds.Handle:
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
                bool flapStream = c.NodeId == Md11FlapSystem.LeverKey || c.NodeId == Md11FlapSystem.DialKey;
                // The Dial-A-Flap thumbwheel's declared state_var is the INDICATOR needle
                // (MD11_DIALAFLAP_IND_RNG), which the cockpit XML animates with ANIM_LAG=1000 — it
                // trails the real value by ~1 s, so the closed-loop walk read it a step behind, saw
                // "no movement", and bailed. Read the knob's OWN live L:var (the NodeId,
                // MD11_DIALAFLAP_WHEEL_RNG — the OVERRIDE_ANIM_CODE source) instead: it updates the
                // instant the CEVENT lands. Same 0–100 → 10–25° scale, so DegreesFor is unchanged.
                string readVar = c.NodeId == Md11FlapSystem.DialKey ? c.NodeId : c.StateVar;
                return new SimVarDefinition
                {
                    Name = readVar,
                    DisplayName = label,
                    Type = SimVarType.LVar,
                    UpdateFrequency = flapStream ? UpdateFrequency.Continuous : UpdateFrequency.OnRequest,
                    IsAnnounced = flapStream,               // required to join continuous monitoring
                    ExcludeFromBatch = flapStream,          // per-var subscription, not the 1 Hz batch
                    HighFrequency = flapStream,             // SIM_FRAME + CHANGED: fresh reads for the walk
                    ExcludeFromMonitorManager = flapStream, // ProcessSimVarUpdate owns the wording
                    ValueDescriptions = values,
                    // No ValueDescriptions means a bare number with no meaning to speak — render
                    // it read-only rather than offering an empty combo the user cannot use.
                    RenderAsReadOnlyStatus = values.Count == 0,
                };
            }

            default:
                return null;
        }
    }

    /// <summary>Fallback lamp wording when TFDi's tooltip carries no case map (most annunciators).</summary>
    private static readonly Dictionary<double, string> LampStates = new() { [0] = "off", [1] = "on" };

    /// <summary>
    /// TFDi's value→label map, parsed to numbers. The flap lever's descriptions come from the
    /// curated detents instead, because the tooltip's %{case} block is missing the Dial-A-Flap
    /// position entirely (it is an RPN range test) — see <see cref="Md11FlapSystem"/>.
    /// </summary>
    private Dictionary<double, string> ValueDescriptionsFor(Md11Control c)
    {
        if (c.NodeId == Md11FlapSystem.LeverKey) return _flaps.LeverValueDescriptions();
        if (c.NodeId == Md11FlapSystem.DialKey) return _flaps.DialValueDescriptions();

        var d = new Dictionary<double, string>();
        foreach (var kvp in c.ValueMap)
            if (double.TryParse(kvp.Key, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                d[v] = kvp.Value;
        return d;
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new();

    public void Dispose()
    {
        _bus?.Dispose();
        _bus = null;
        DisposeTrackedWindows();
        GC.SuppressFinalize(this);
    }
}
