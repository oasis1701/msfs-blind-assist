using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Variables the output-mode readouts need that do not belong to any panel built so far.
///
/// A readout hotkey answers from the SimConnect cache, and TWO things about that cache
/// decide what belongs here.
///
/// It is keyed by the MSFSBA VARIABLE KEY, never by the SimVar name — `lastVariableValues`
/// is written as `lastVariableValues[varKey]`. Looking up "FUEL TANK LEFT MAIN QUANTITY"
/// or "KOHLSMAN SETTING HG:1" therefore returns null however correct the name is, and a
/// `?? 0` fallback then answers zero. That is exactly what the B, F and W keys did: they
/// reported "0 hectopascals" and "0.0 gallons" on a full aeroplane.
///
/// And CONTINUOUS ALONE IS NOT ENOUGH. Batch membership — which is what actually gets a
/// variable polled and cached — is `Continuous && IsAnnounced && !ExcludeFromBatch`
/// (SimConnectManager.Setup.cs). A Continuous variable with IsAnnounced false falls to the
/// individual-data-def branch instead, which is only read on request, so it never reaches
/// the cache at all. That is why the B, F and W keys still answered "not available yet"
/// after being pointed at the right KEYS: the keys were right and the variables were never
/// being polled.
///
/// So everything here is IsAnnounced, and silenced instead in ProcessSimVarUpdate, which
/// returns true for these keys so the generic announcer never speaks them. Announcing a
/// tank quantity or a subscale on every change would bury everything else. Registering them here makes B and L work now
/// rather than waiting for the G1000 and Flaps panels; when those arrive they reuse these
/// same keys rather than defining second copies.
/// </summary>
public partial class CowsDA40Definition
{
    private static Dictionary<string, SimVarDefinition> BuildSharedReadoutVariables() => new()
    {
        // The G1000's own subscale. The standby one lives on the Standby panel; the B key
        // reads both, because this aeroplane has two and either can be wrong on its own.
        ["DA40_G1000_BARO"] = new SimVarDefinition
        {
            Name = "KOHLSMAN SETTING HG:1",
            DisplayName = "Altimeter Setting",
            Type = SimVarType.SimVar,
            Units = "inHg",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            // ⚠️ SIM_FRAME, NOT THE 1 Hz BATCH - a subscale the pilot is TURNING moves
            // faster than the batch samples it. Same reasoning as G_FORCE's touchdown spike.
            //
            // ⚠️ "CHANGED MEANS A STILL ALTIMETER COSTS NOTHING" IS THE SAME SENTENCE THAT
            // TURNED OUT TO BE A BUG ON THE RADIOS, and this one is DELIBERATELY LEFT ALONE.
            // HighFrequency asks for SIM_FRAME with the CHANGED flag, so a value that never
            // moves is never delivered and never reaches the cache - which is exactly why
            // the Radios panel read "COM 1 Active: --" for four static frequencies. Both
            // altimeters were tested in the cockpit and BOTH WORK, so the evidence says this
            // path is fed; pattern-matching it into a "fix" would change something known
            // good on a theory, which is how the last two wrong fixes happened. If a baro
            // readout is ever seen blank on a panel, this comment is where to start.
            ExcludeFromBatch = true,
            HighFrequency = true
            // NOT excluded from the Monitor Manager any more: it announces now (debounced,
            // once the knob settles), so its Ctrl+M row mutes something real. The rule is
            // that a checkbox which silences nothing should not exist - the converse is
            // that one which does, must.
        },

        // Tank quantities, for the F readout and for the Fuel System scan. BOTH variants
        // register these — the F key must answer on the XLS too, and the NG-only fuel
        // panel reuses these same keys rather than defining second copies.
        //
        // CONTINUOUS, not OnRequest: a readout hotkey answers from the cache, and an
        // OnRequest variable is only polled while its panel is open. They are numbers, so
        // they stay silent and out of the Monitor Manager.
        ["DA40_FUEL_MAIN_ACTUAL"] = new SimVarDefinition
        {
            Name = "FUEL TANK LEFT MAIN QUANTITY",
            DisplayName = "Main Tank Measured",
            Type = SimVarType.SimVar,
            Units = "gallons",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F1"
        },

        ["DA40_FUEL_AUX_ACTUAL"] = new SimVarDefinition
        {
            Name = "FUEL TANK RIGHT MAIN QUANTITY",
            DisplayName = "Auxiliary Tank Measured",
            Type = SimVarType.SimVar,
            Units = "gallons",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F1"
        },

        // Indicated airspeed. The flap panel's overspeed warning needs it from the cache,
        // and airspeed is otherwise only a FIXED data definition in SimConnectManager -
        // not a keyed variable, so there is nothing in lastVariableValues to look up.
        ["DA40_AIRSPEED"] = new SimVarDefinition
        {
            Name = "AIRSPEED INDICATED",
            DisplayName = "Airspeed",
            Type = SimVarType.SimVar,
            Units = "knots",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromMonitorManager = true
        },

        // THE TRANSPONDER, announced. It is tuned on the G1000 and nowhere else, so
        // without this the only way to know a squawk changed is to have the PFD window
        // open on the right page. A code is not a continuously-varying quantity - it
        // changes when someone sets it, and ATC assigning one is exactly the background
        // change worth hearing - so it announces, like the standby subscale and unlike the
        // power lever.
        ["DA40_XPDR_CODE"] = new SimVarDefinition
        {
            Name = "TRANSPONDER CODE:1",
            DisplayName = "Squawk",
            Type = SimVarType.SimVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            Format = "F0"
        },

        ["DA40_XPDR_MODE"] = new SimVarDefinition
        {
            Name = "TRANSPONDER STATE:1",
            DisplayName = "Transponder",
            Type = SimVarType.SimVar,
            Units = "enum",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "Standby",
                [2] = "Test",
                [3] = "On",
                [4] = "Altitude reporting"
            }
        },

        // Gross weight, for the W readout.
        ["DA40_GROSS_WEIGHT"] = new SimVarDefinition
        {
            Name = "TOTAL WEIGHT",
            DisplayName = "Gross Weight",
            Type = SimVarType.SimVar,
            Units = "pounds",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromMonitorManager = true
        }

        // DA40_FLAPS_POSITION used to live here for the same reason. It has since been
        // PROMOTED into the Flaps panel, where it is the selector control itself — the
        // key is unchanged, so the L readout and the flap limit speeds still find it.
        // It was never duplicated, and must not be: two keys sharing one SimVar Name are
        // fine in general but NOT when both are Continuous and batched, because the
        // continuous batch sorts by name and a duplicate shifts every later variable's
        // struct slot.
    };

    /// <summary>
    /// Readout plumbing that must be POLLED but must never be SPOKEN.
    ///
    /// These have to be IsAnnounced to earn a place in the continuous batch - that is the
    /// only thing that gets them cached - so the silence has to come from somewhere else,
    /// and ProcessSimVarUpdate is where. Every one is a NUMBER that changes constantly.
    /// </summary>
    /// <summary>
    /// EVERYTHING that is polled and never spoken, from both lists.
    ///
    /// It is the union rather than one list because the two have different reasons - these
    /// are readout plumbing, the others are hotkey plumbing - but only one truthful answer
    /// to "will this speak", and the tests that ask it must get that one.
    /// </summary>
    public static IReadOnlyCollection<string> SilentCachedReadoutKeys =>
        SilentCachedReadouts.Concat(HotkeyCachedReadouts).Concat(PrimingCapturedKeys).Concat(XlsStartCapturedKeys).Concat(XlsMixtureCapturedKeys).ToList();

    private static readonly HashSet<string> SilentCachedReadouts = new()
    {
        // The control surfaces. Announced only so they reach the batch cache; a control
        // check sweeps the stick stop to stop, and speaking every position on the way
        // would bury the one reading the pilot is listening for.
        "DA40_CTL_ELEVATOR",
        "DA40_CTL_AILERON",
        "DA40_CTL_RUDDER",

        // The three bus voltages the master's read-back speaks. Cached so that announcement
        // can read them; never spoken on their own, because a voltage moves continuously in
        // flight and would talk over everything.
        "DA40_ELEC_BUS_MAIN_VOLT",
        "DA40_ELEC_BUS_ESS_VOLT",
        "DA40_ELEC_BUS_BATT_VOLT",

        // The elevator STICK. Never a row and never spoken on its own - it exists so the
        // surface can be checked against it, which is the only way to tell a jam from an
        // axis sitting off centre. See DescribeElevatorStickAgreement.
        "DA40_CTL_YOKE_Y",

        // DA40_G1000_BARO is deliberately NOT here any more. It was, and that is why a
        // subscale changed on external hardware said nothing at all. It is handled by the
        // settle-timer announcer instead, which speaks the value the knob came to rest on.
        // The outside air temperature the performance tables are entered with. Batched so a
        // row on another panel can read it; silent because it is a number.
        "DA40_PERF_OAT",

        "DA40_FUEL_MAIN_ACTUAL",
        "DA40_FUEL_AUX_ACTUAL",

        // The NG's emergency transfer rate. Batched so its row can be composed from the
        // valve and the tank beside it; silent because it is a rate, and because it is
        // non-zero whenever the propeller turns, valve or no valve.
        "DA40_FUEL_XFER_EMERG",

        "DA40_GROSS_WEIGHT",
        "DA40_AIRSPEED",
        "DA40_TRIM_SET",
        "DA40_POWER_LEVER_SET",

        // The flight director bars. They carry IsAnnounced only to reach the batch cache
        // - the whole AP value family does - but the commanded attitude moves continuously
        // in flight, and a pilot flying the bars reads them from the scan, not from a
        // running commentary of every tenth of a degree.
        "DA40_AP_FD_PITCH",
        "DA40_AP_FD_BANK",

        // Carries the waypoint-passing call's Ctrl+M row and nothing else. The call itself is
        // spoken from the GPS waypoint callback, which checks this key's mute for itself.
        "DA40_WAYPOINT_PASSING",

        // The G1000's VNAV output, polled for the Shift+D readout and never spoken on its own.
        "DA40_VNAV_TOD_DIST",
        "DA40_VNAV_TOD_LEG",

        // The XLS's three levers. Cached so the readout hotkeys can say where they are;
        // never spoken on their own - under hardware each would talk several times a second.
        "DA40_XLS_THROTTLE_SET",
        "DA40_XLS_PROP_SET",
        "DA40_XLS_MIXTURE_SET"
    };

    /// <summary>
    /// Every hotkey-cached readout is silent for the same reason the list above is: they
    /// carry IsAnnounced only to reach the batch, and they are engine numbers that move
    /// several times a second. Kept as a separate list so the two reasons stay separate,
    /// and unioned here so neither can be forgotten.
    /// </summary>
    private static bool IsSilentCachedReadout(string varName)
        => SilentCachedReadouts.Contains(varName)
           || HotkeyCachedReadouts.Contains(varName)
           // The XLS priming inputs: polled so the state can be classified from them,
           // never spoken as numbers - the state is spoken instead, on its crossing.
           || PrimingCapturedKeys.Contains(varName)
           // The XLS start inputs: the readiness row is derived from them and the
           // script's narration is spoken from its counter - the numbers stay silent.
           || XlsStartCapturedKeys.Contains(varName)
           // The XLS mixture inputs: eight temperatures, the assist pair, and the states'
           // inputs - spoken as states on their crossings, never as numbers.
           || XlsMixtureCapturedKeys.Contains(varName);

    /// <summary>
    /// Returning true means "handled" - the generic announcer never runs for that key.
    /// Nothing is announced here; that IS the handling.
    /// </summary>
    public override bool ProcessSimVarUpdate(string varName, double value,
        Accessibility.ScreenReaderAnnouncer announcer)
    {
        // Captured BEFORE the silence gate: the stick is silent by design, and a gate that
        // returns first would leave the elevator comparison with nothing to compare against.
        NoteFlightControlValue(varName, value);
        NoteBusVoltage(varName, value);
        // The XLS mag check: the tachometer is silent by design and this must see it, so
        // the drop can be read from the RPM the key left BOTH at. Never announces itself.
        NoteMagnetoChange(varName, value, announcer);
        // The XLS priming state: its inputs are silent numbers, so this must see them here.
        NotePrimingChange(varName, value, announcer);
        // The XLS start readiness and auto-start narration: reads the master, selector,
        // combustion and the captured fuel inputs as they pass; speaks on its own terms.
        NoteXlsStartChange(varName, value, announcer);
        // The XLS mixture states: lean-assist peaks, the red box, fouling, shock cooling
        // and cylinder damage, each spoken on its crossing from the captured inputs.
        //
        // ⚠️ THIS WAS CALLED TWICE. Unlike the duplicated line in the display-override
        // chain - harmless, because the first call returns - this one is a VOID call with
        // side effects, so every crossing ran its logic twice: a lean-assist peak, a red-box
        // entry or a fouling warning could be announced twice, and any state the method
        // latches was advanced two steps per update.
        NoteXlsMixtureChange(varName, value, announcer);
        // The XLS's mixture REGIME. Returns true so the generic announcer never reads the
        // ramp; it speaks the settled edge itself. Above the silence gate because it is a
        // state, not one of the captured numbers.
        if (NoteAutomixtureChange(varName, value, announcer)) return true;
        // What the POH's performance tables are entered with - altitude, temperature and
        // the power being made. Every one is already batched and already passes here.
        NotePerfTableValue(varName, value);
        // The NG fuel valve, so the emergency-transfer row can say whether anything is
        // moving. A control is on no display list, so its own override never runs.
        NoteFuelValveChange(varName, value);

        if (IsSilentCachedReadout(varName)) return true;

        // ⚠️ Returns false by design - the switch keeps its own immediate announcement and
        // this only ARMS the bus read-back that follows it. See NotePowerSwitchChange.
        NotePowerSwitchChange(varName, value, announcer);

        // A door is OPEN or CLOSED, never "65.4" - the percentage sweeps as the canopy
        // swings and used to announce every step of it.
        if (NoteDoorChange(varName, value, announcer)) return true;

        // Engine health, which falls rather than switching. Only a material fall speaks.
        if (NoteEngineHealth(varName, value, announcer)) return true;

        // The three graded failures - percentages, so the numeric-silence rule was keeping
        // a coolant leak and a turbo failure quiet. Onset and worsening only.
        if (NoteGradedFailure(varName, value, announcer)) return true;

        // A LAMP never speaks on its own; a SWITCH always does. NoteLampChange arms the
        // settle for both and returns true only for the lamp, so the switch carries on to
        // the generic announcer exactly as it always has.
        if (NoteLampChange(varName, announcer)) return true;

        // Both barometric subscales: recorded and announced once the knob settles, rather
        // than on every 0.01 inHg step. Returns true either way - the generic announcer
        // must not also read them.
        if (NoteBaroChange(varName, value, announcer)) return true;

        // Every COM and NAV frequency, active and standby, announced once tuning settles
        // rather than on every 25 kHz step - and spoken from what the RADIO reported,
        // never from a prediction made before the event was sent.
        if (NoteRadioChange(varName, value, announcer)) return true;

        // Remember every breaker position so the per-panel "how many are out" row can be
        // computed. A display override gets no SimConnect, so it cannot read them itself.
        // Returning FALSE here on purpose: a breaker moving on its own is exactly the kind
        // of background change that must still be announced.
        if (varName.StartsWith("DA40_CB_")) _breakerState[varName] = value;

        return base.ProcessSimVarUpdate(varName, value, announcer);
    }
}
