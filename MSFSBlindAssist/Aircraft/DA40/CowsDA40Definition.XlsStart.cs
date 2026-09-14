using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Instrument Panel → Engine Start (DA40-XLS).
///
/// The NG's panel of the same name is the start in one place: the two things pressed and
/// everything watched while pressing them, in the AFM's order. The XLS has no engine master,
/// no glow plug and no separate start key, so its version is the same shape with the
/// Lycoming's parts in it — and its KEY is deliberately not here. On the XLS the ignition
/// key is also the L/R/BOTH selector for the run-up, it lives on Magnetos, and one control on
/// two panels is the heading-bug mistake (set in one place, stale in the other).
///
/// What IS here is what no other panel has:
///
///  • <b>Start readiness</b> — the first thing stopping a start, named. The aeroplane can be
///    structurally unstartable with nothing in the cockpit saying so (the un-generated
///    variations; docs/da40-xls-variables.md), and a blind pilot has no gauge to stare at.
///    <see cref="DA40StartReadiness"/> orders the blockers; the variations trap alone is
///    ANNOUNCED, once, because it is the one the aeroplane never announces.
///  • <b>Auto-start</b> — COWS's own script (Ctrl+E in the cockpit; <c>K:ENGINE_AUTO_START</c>),
///    which primes, cranks and clears a flood itself, narrated step by step from its own
///    counter (<see cref="DA40AutoStart"/>) so the pilot hears "Priming", "Cranking", "Mixture
///    in" rather than silence and then either an engine or not.
///  • <b>The crank</b> — the crank tach (<c>ENG_COMP_RPM</c>, the tach to read below 400), the
///    starter, combustion (announced: "Engine: Running" is the start succeeding), and the
///    battery amps that read −77 while it turns.
///
/// ⚠️ UNPOWERED, THE MODEL'S FUEL LOGIC DOES NOT TICK. Measured with the master off after a
/// reload: the servo charge held 21.6 g against a cap that computes to 0.51 and
/// <c>ENG_FUEL_PRESS</c> read 19.75; master on, both snapped to 0 within a tick. So the
/// readiness row checks the master BEFORE anything fuel-derived, and the Fuel panel's
/// pressure row says "master off" instead of rendering a frozen number as 286 psi.
/// </summary>
public partial class CowsDA40Definition
{
    private static Dictionary<string, SimVarDefinition> BuildXlsStartVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Control ----------

        v["DA40_XLS_AUTO_START"] = new SimVarDefinition
        {
            Name = "DA40_XLS_AUTO_START",
            DisplayName = "Auto-start",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        // The counterpart the aircraft binds beside it (ENGINE_AUTO_SHUTDOWN): mixture to
        // cut-off and pump off at once, then throttle closed, key OFF and the flag cleared
        // once the engine has stopped - the shutdown a blind pilot otherwise does across
        // three panels.
        v["DA40_XLS_AUTO_SHUTDOWN"] = new SimVarDefinition
        {
            Name = "DA40_XLS_AUTO_SHUTDOWN",
            DisplayName = "Auto-shutdown",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        // ---------- Readiness ----------

        // The row hangs on the variation the trap zeroes, so the row itself is the mute for
        // the one call-out this panel makes on its own. Silent as a number (captured list).
        v["DA40_XLS_START_READY"] = new SimVarDefinition
        {
            Name = "FUEL_SPREAD_PRESSURE",
            DisplayName = "Start Readiness",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true
        };

        // The readiness inputs the batch does not already carry. ⚠️ Each is the ONLY batched
        // key on its SimVar name: the Fuel panel's rows on the same names are OnRequest
        // twins, which the batch tolerates; a second batched key would not be.
        v["DA40_XLS_START_FEED"] = Capture("FUEL_FEED_QUANTITY", "Feed Quantity");
        v["DA40_XLS_START_PRESS"] = Capture("ENG_FUEL_PRESS", "Fuel Pressure Input");
        v["DA40_XLS_START_BOIL"] = Capture("FUEL_TEMP_BOIL_FAC", "Vapour Factor Input");
        v["DA40_XLS_START_PUMP"] = new SimVarDefinition
        {
            Name = "GENERAL ENG FUEL PUMP ON:1",
            DisplayName = "Electric Pump Input",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromMonitorManager = true
        };

        // ---------- Auto-start ----------

        v["DA40_XLS_AUTO_STEP"] = new SimVarDefinition
        {
            Name = "AUTOSTART_STEP",
            DisplayName = "Auto-start Progress",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true
            // In the Monitor Manager: the mute for the script's narration.
        };
        v["DA40_XLS_AUTO_ACTIVE"] = Capture("INPUT_START", "Auto-start Input");
        v["DA40_XLS_AUTO_TIMER"] = Capture("AUTOSTART_STARTER_TIMER", "Auto-start Crank Timer");

        // ---------- The crank ----------

        // The start succeeding is a described state a pilot is waiting on, so it announces:
        // "Engine: Running". The generic announcer does that from the descriptions.
        v["DA40_XLS_COMBUSTION"] = new SimVarDefinition
        {
            Name = "GENERAL ENG COMBUSTION:1",
            DisplayName = "Engine",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Stopped",
                [1] = "Running"
            }
        };

        // The crank tach: ~190 turning over, the model's own "below 400 read this one".
        v["DA40_XLS_CRANK_RPM"] = new SimVarDefinition
        {
            Name = "ENG_COMP_RPM",
            DisplayName = "Crank Tach",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Units = "rpm",
            Format = "F0"
        };

        return v;
    }

    private static readonly List<string> XlsStartControls = new()
    {
        "DA40_XLS_AUTO_START",
        "DA40_XLS_AUTO_SHUTDOWN"
    };

    // Readiness first - it is the answer - then the script if it is running, then what a
    // pilot watches while the starter turns, in the AFM's order: is it turning, is it
    // running, is the oil up, and the fuel side it was primed from.
    private static readonly List<string> XlsStartDisplay = new()
    {
        "DA40_XLS_START_READY",
        "DA40_XLS_AUTO_STEP",
        "DA40_XLS_CRANK_RPM",
        "DA40_MAG_STARTER",
        "DA40_ELEC_BATT_AMPS",
        "DA40_XLS_COMBUSTION",
        "DA40_XLS_RPM",
        "DA40_XLS_OIL_PRESSURE",
        "DA40_XLS_FUEL_PRESSURE",
        "DA40_XLS_FUEL_FLOW",
        "DA40_PRIME_CYL_1",
        "DA40_XLS_FUEL_VAPOUR"
    };

    /// <summary>Every silently captured start input, for the silent-readout set.</summary>
    public static readonly string[] XlsStartCapturedKeys =
    {
        "DA40_XLS_START_READY", "DA40_XLS_START_FEED", "DA40_XLS_START_PRESS",
        "DA40_XLS_START_BOIL", "DA40_XLS_START_PUMP",
        "DA40_XLS_AUTO_STEP", "DA40_XLS_AUTO_ACTIVE", "DA40_XLS_AUTO_TIMER"
    };

    private bool HandleXlsStartSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        if (varKey == "DA40_XLS_AUTO_SHUTDOWN")
        {
            // The binding's own write (Inputs.xml 82); the Inputs code does the rest and
            // clears the flag itself. "Engine: Stopped" is the announced result.
            simConnect.ExecuteCalculatorCodeUnique("-1 (>L:INPUT_START, percent)");
            return true;
        }
        if (varKey != "DA40_XLS_AUTO_START") return false;

        // What the aircraft's own Ctrl+E binding writes (Inputs.xml 79). Measured: the
        // K-event route started the engine once and then not again - INPUT_START decayed
        // to noise with the counter at 0 - while this write walked the script every time.
        // Byte-identical every press, so unique. Nothing spoken here: the button speaks
        // itself, and the script narrates from its first step within the second.
        simConnect.ExecuteCalculatorCodeUnique("1 (>L:INPUT_START, percent)");
        return true;
    }

    // ==================================================================================
    // The captured state, and the two things spoken from it
    // ==================================================================================

    private bool? _startMasterOn;
    private double _startSpread = double.NaN;
    private int _startSelector;
    private double _startFeed;
    private bool _startPumpOn;
    private double _startPressure;
    private double _startBoil = 1;
    private bool _startCombustion;
    private bool _startTrapSpoken;

    private bool _autoActive;
    private double _autoStep;
    private double _autoTimer;
    private int _autoSpokenStep = -1;
    private double _autoHighestStep;

    private DA40StartInputs StartInputs() => new(
        EngineRunning: _startCombustion || PrimeEngineRunning,
        MasterOn: _startMasterOn ?? true,
        SpreadPressure: double.IsNaN(_startSpread) ? 1 : _startSpread,
        SelectorPosition: _startSelector,
        FeedQuantityGal: _startFeed,
        PumpOn: _startPumpOn,
        FuelPressureBar: _startPressure,
        BoilFactor: _startBoil,
        Prime: _primeEvap <= 0 ? null : DA40Priming.Classify(_primeCyl, _primeAtom, _primeEvap));

    /// <summary>
    /// Captures the readiness inputs from the shared keys that carry them, and speaks the two
    /// things this panel speaks on its own. Returns false always: the shared keys keep their
    /// own announcements, and the captured numbers are silent.
    /// </summary>
    private bool NoteXlsStartChange(string varKey, double value, ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "DA40_ELEC_MASTER_BATTERY": _startMasterOn = value > 0.5; return false;
            case "DA40_XLS_FUEL_SELECTOR": _startSelector = (int)Math.Round(value); return false;
            case "DA40_XLS_COMBUSTION": _startCombustion = value > 0.5; return false;
            case "DA40_XLS_START_FEED": _startFeed = value; return false;
            case "DA40_XLS_START_PRESS": _startPressure = value; return false;
            case "DA40_XLS_START_BOIL": _startBoil = value; return false;
            case "DA40_XLS_START_PUMP": _startPumpOn = value > 0.5; return false;
            case "DA40_XLS_AUTO_TIMER": _autoTimer = value; return false;

            case "DA40_XLS_START_READY":
                _startSpread = value;
                NoteVariationsTrap(announcer);
                return false;

            case "DA40_XLS_AUTO_STEP":
                _autoStep = value;
                if (_autoActive) _autoHighestStep = Math.Max(_autoHighestStep, value);
                NoteAutoStartProgress(announcer);
                return false;

            case "DA40_XLS_AUTO_ACTIVE":
                // Written "1 (>L:INPUT_START, percent)": positive is running, -1 is the
                // shutdown script, 0 is idle. Any positive reading counts.
                bool active = value > 0.001;
                if (active == _autoActive) return false;
                _autoActive = active;
                if (active)
                {
                    _autoHighestStep = _autoStep;
                    _autoSpokenStep = -1;
                    NoteAutoStartProgress(announcer);
                }
                else
                {
                    if (!Muted("DA40_XLS_AUTO_STEP"))
                        announcer.Announce(DA40AutoStart.Outcome(_autoHighestStep, _autoStep, _startCombustion));
                    _autoSpokenStep = -1;
                }
                return false;
        }

        return false;
    }

    /// <summary>
    /// The one fault the aeroplane never names. Spoken on the first zero reading rather than
    /// baseline-first: after a reload the trap is already in place, and "only on a change"
    /// would keep it silent for exactly the pilot who needs it. Once per zero; re-armed when
    /// the reset regenerates the spreads.
    /// </summary>
    private void NoteVariationsTrap(ScreenReaderAnnouncer announcer)
    {
        if (_startSpread > 0) { _startTrapSpoken = false; return; }
        if (_startTrapSpoken || Muted("DA40_XLS_START_READY")) return;
        _startTrapSpoken = true;
        announcer.Announce("Engine variations not generated, the engine cannot start. Clear Engine Damage on the Reset panel");
    }

    /// <summary>Narrates the script: each whole step once, queued behind whatever is speaking.</summary>
    private void NoteAutoStartProgress(ScreenReaderAnnouncer announcer)
    {
        if (!_autoActive) return;
        int step = (int)Math.Floor(_autoStep);
        if (step == _autoSpokenStep) return;
        _autoSpokenStep = step;
        if (Muted("DA40_XLS_AUTO_STEP")) return;
        announcer.Announce("Auto-start: " + DA40AutoStart.Describe(true, _autoStep, _autoTimer));
    }

    private static bool Muted(string key)
        => Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet.Contains(key);

    private bool TryGetXlsStartDisplayOverride(string varKey, double value, out string displayText)
    {
        switch (varKey)
        {
            case "DA40_XLS_START_READY":
                displayText = DA40StartReadiness.Describe(StartInputs());
                return true;

            case "DA40_XLS_AUTO_STEP":
                displayText = DA40AutoStart.Describe(_autoActive, value, _autoTimer);
                return true;

            case "DA40_XLS_CRANK_RPM":
                displayText = $"{value:F0} rpm";
                return true;
        }

        displayText = string.Empty;
        return false;
    }

    /// <summary>Forgets the captured state. Called when the aircraft is switched away.</summary>
    private void ResetXlsStartState()
    {
        _startMasterOn = null;
        _startSpread = double.NaN;
        _startSelector = 0;
        _startFeed = 0;
        _startPumpOn = false;
        _startPressure = 0;
        _startBoil = 1;
        _startCombustion = false;
        _startTrapSpoken = false;
        _autoActive = false;
        _autoStep = 0;
        _autoTimer = 0;
        _autoSpokenStep = -1;
        _autoHighestStep = 0;
    }
}
