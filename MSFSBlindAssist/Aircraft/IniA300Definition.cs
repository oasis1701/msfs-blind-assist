using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// iniBuilds A300-600 (Community package <c>inibuilds-aircraft-a300</c>, MSFS 2024; the passenger
/// and freighter, GE and PW variants share one cockpit). Full notes: docs/a300.md.
///
/// HOW IT WORKS
/// <list type="bullet">
/// <item>Every clickable control comes from the generated map (<c>Resources/a300_control_map.json</c>,
/// built by <c>tools/a300-gen</c> from the aircraft's own compiled cockpit behaviour and tooltips);
/// <see cref="A300PanelLayout"/> places them on panels that follow the cockpit, with spoken names.
/// Regenerate the map after an iniBuilds update; never hand-edit it.</item>
/// <item>A write fires the control's OWN B: event, <c>value (&gt;B:AIRLINER_…_Set)</c>, the event its
/// mouse code fires, through the calculator path, unique-ified so a repeat is never coalesced
/// (<see cref="A300WritePlan"/>). For a two-position switch that event FLIPS the switch whatever
/// value it is given (iniBuilds' own On and Off bindings flip too), so a write is planned from the
/// position the switch is in now.</item>
/// <item>The flap handle, ground spoiler arm and speed brake are hand-written with the stock events
/// the A300's systems take (<see cref="A300Levers"/>).</item>
/// <item>Switch positions and the two master lights ride the continuous batches. Positions are
/// consumed silently, because the screen reader already speaks the pilot's own picks; the master
/// lights and a lever moved by something else speak (<see cref="A300Announcements"/>).</item>
/// <item>Knobs and readouts are on-request reads listed with their panel.</item>
/// </list>
/// The MCDU windows (IniA300Definition.Mcdu), the FCU, autopilot and readout hotkeys (.Autoflight),
/// and the FMA and display boxes (.Displays, .Panels) are in their own partial files; the tablet is
/// the shared Coherent EFB reader.
/// </summary>
public partial class IniA300Definition : BaseAircraftDefinition, IDisposable
{
    public const string Code = "INI_A300";

    public override string AircraftName => "iniBuilds A300-600";
    public override string AircraftCode => Code;
    public override string? ChecklistFileName => "iniBuilds_A300_Checklist.txt";

    private readonly A300ControlMap _map;
    private readonly A300Placement _placement;

    /// <summary>Variable key → the panel row it drives (every operable row).</summary>
    private readonly Dictionary<string, A300PlacedRow> _rows = new(StringComparer.Ordinal);

    /// <summary>Readout variable key → its value.</summary>
    private readonly Dictionary<string, A300Readout> _readouts = new(StringComparer.Ordinal);

    /// <summary>
    /// The keys <see cref="OwnSubscription"/> registered without a Ctrl+M row: the subscriptions only a label, a box line
    /// or a refusal reads, which ProcessSimVarUpdate consumes without a word. The set also holds the two light power
    /// flags, the panel lamps' shared inputs and the TRP's four keys; the light board and TRP branches consume those first,
    /// so the check against this set stays after them. An input that reads another key's cache instead
    /// (<see cref="_inputAliases"/>) is never registered, so it is not in the set.
    /// </summary>
    private readonly HashSet<string> _silentKeys = new(StringComparer.Ordinal);

    /// <summary><see cref="_silentKeys"/> once the variables are built. A delivery can reach the definition before
    /// anything asked for them (several tests deliver first), so asking for them here fills the set.</summary>
    internal IReadOnlySet<string> SilentKeys
    {
        get
        {
            _ = GetVariables();
            return _silentKeys;
        }
    }

    /// <summary>
    /// A composed line's input key → the key that already streams its variable (<see cref="InputSubscription"/>). The
    /// input is registered nowhere, and every reader of it asks <see cref="ReadInput"/>, which answers from that key's
    /// cache: the same variable, the same once-a-second sample. Only an input read from the cache is ever here (the E/WD
    /// memos' inputs, the IRS flags, the EFIS nav selector, the MCDU annunciators); a light board input, a panel row and
    /// a key that carries a Ctrl+M row keep their own subscription, because their own deliveries drive something.
    /// </summary>
    private readonly Dictionary<string, string> _inputAliases = new(StringComparer.Ordinal);

    /// <summary>The key whose cache holds <paramref name="key"/>'s variable: the key itself, or the one it reads instead
    /// (<see cref="_inputAliases"/>). Asking builds the variables first, as <see cref="SilentKeys"/> does.</summary>
    internal string StreamingKey(string key)
    {
        _ = GetVariables();
        return _inputAliases.GetValueOrDefault(key, key);
    }

    /// <summary>A composed line's input from the cache, through the key that streams it.</summary>
    private double? ReadInput(SimConnectManager sim, string key) => Cached(sim, StreamingKey(key));

    private SimConnectManager? _sim;
    private bool _disposed;

    /// <summary>The clock the commanded-position hold reads; tests replace it.</summary>
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    /// <summary>Whether a write can reach the aircraft (<see cref="SimConnectManager.CalcWriteCanLand"/>); tests replace it.</summary>
    internal Func<SimConnectManager, bool> CanLand { get; set; } = sim => sim.CalcWriteCanLand;

    /// <summary>Asks for a row's value again so the next delivery puts its combo back. Only when
    /// connected: there is nothing to ask otherwise, and the request path needs the SimConnect
    /// library loaded. Tests replace it.</summary>
    internal Action<string, SimConnectManager> ReRead { get; set; } = (key, sim) =>
    {
        if (sim.IsConnected)
            sim.RequestVariable(key, forceUpdate: true);
    };

    /// <summary>Reads a key from the SimConnect cache; tests replace it.</summary>
    internal Func<SimConnectManager, string, double?> Cached { get; set; } = (sim, key) => sim.GetCachedVariableValue(key);

    /// <summary>Sends one calculator string; tests replace it.</summary>
    internal Action<SimConnectManager, string> Send { get; set; } = (sim, rpn) => sim.ExecuteCalculatorCodeUnique(rpn);

    /// <summary>The waits inside a plan (a hold, a spring); tests replace it.</summary>
    internal Func<int, Task> Delay { get; set; } = Task.Delay;

    public IniA300Definition()
    {
        _map = A300ControlMap.Load();
        _placement = A300PanelLayout.Place(_map);
        foreach (var rows in _placement.RowsByPanel.Values)
            foreach (var row in rows)
                _rows[row.Key] = row;
        foreach (var readout in A300Readouts.All.Concat(A300EcamPages.Readouts))
            _readouts[readout.Key] = readout;
    }

    /// <summary>The SimConnect manager the definition writes through; MainForm calls this when the
    /// A300 is selected and at start-up, and every write path refreshes it.</summary>
    public void Attach(SimConnectManager sim) => _sim = sim;

    // The FCU windows take typed values (A300TypedValues), as the MD-11's and the FBW Airbuses' do.
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    public override Dictionary<string, string> GetButtonStateMapping() => new();

    // =================================================================================
    // Variables
    // =================================================================================

    /// <summary>
    /// One variable per panel row, lamp and readout. The UpdateFrequency choice here IS the
    /// SimConnect budget strategy: positions and lamps ride the continuous batches (no individual
    /// definitions); knobs, readouts and duplicate positions are on-request definitions; buttons are
    /// write-only (never registered). A position variable two rows share rides the batch once — two
    /// keys with one name in a batch shift every later slot — and the second row reads it on request.
    /// </summary>
    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = GetBaseVariables();

        // The glideslope call-out listens to the A300's ILS receiver, NAV 3 ([A300-10]), not the base's NAV 1.
        // A new definition copied from the base's entry; the base's own entry is not edited.
        var baseGlideslope = vars["MON_GlideSlopeAlive"];
        vars["MON_GlideSlopeAlive"] = new SimVarDefinition
        {
            Name = "NAV HAS GLIDE SLOPE:3",
            DisplayName = baseGlideslope.DisplayName,
            Type = baseGlideslope.Type,
            Units = baseGlideslope.Units,
            UpdateFrequency = baseGlideslope.UpdateFrequency,
            IsAnnounced = baseGlideslope.IsAnnounced,
        };

        var batchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in vars.Values)
            if (ContinuousBatchLayout.RidesBatch(def))
                batchNames.Add(ContinuousBatchLayout.FullName(def));

        foreach (var lamp in A300Announcements.Lamps)
        {
            var def = new SimVarDefinition
            {
                Name = lamp.Var,
                DisplayName = lamp.Name,
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,                  // spoken from ProcessSimVarUpdate
                // The fault lights stream on their own once-a-second subscriptions, never the batch:
                // 58 more names there pushed the FMA's pitch trim sources into a second batch, and
                // the FMA must read one complete sample. The two master lights ride the batch.
                ExcludeFromBatch = lamp.SpeaksOff,
                HighFrequency = lamp.Flashes,        // every change, so the off hold sees a flash (A300LampSpeech)
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" },
                RenderAsReadOnlyStatus = true,
            };
            // A light's status box shows what the cockpit shows, its state AND its bus's light power
            // (TryDescribeControlState, [A300-23]), so a power change repaints it too.
            if (A300LampBoard.PowerKeyFor(lamp.Power) is string powerKey)
                def.StateVariables = new[] { lamp.Key, powerKey };
            if (ContinuousBatchLayout.RidesBatch(def))
                batchNames.Add(ContinuousBatchLayout.FullName(def));
            vars[lamp.Key] = def;
        }

        // The lights read through their own generated rule ([A300-25]): each streams the rule's first variable
        // on its own subscription under its own key, which carries its Ctrl+M row and its status box line; the
        // rule's other variables stream once each under a shared key, consumed silently. Never the batch ([A300-9]).
        foreach (var lamp in A300PanelLamps.Resolved)
        {
            var primary = lamp.Primary;
            var keys = lamp.Rule.Inputs.Select(lamp.KeyFor).ToList();
            if (A300LampBoard.PowerKeyFor(lamp.Power) is string power)
                keys.Add(power);
            vars[lamp.Lamp.Key] = new SimVarDefinition
            {
                Name = primary.Name,
                DisplayName = lamp.Lamp.Name,
                Type = primary.IsStock ? SimVarType.SimVar : SimVarType.LVar,
                Units = primary.IsStock ? primary.Units : "number",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,                  // spoken from ProcessSimVarUpdate (A300LampBoard)
                ExcludeFromBatch = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" },
                RenderAsReadOnlyStatus = true,
                StateVariables = keys.Distinct().ToArray(),
            };
        }
        foreach (var input in A300PanelLamps.SharedInputs)
        {
            var shared = OwnSubscription(vars, A300PanelLamps.InputKey(input), input.Name, input.Name);
            shared.Type = input.IsStock ? SimVarType.SimVar : SimVarType.LVar;
            shared.Units = input.IsStock ? input.Units : "number";
        }

        // The FCU buttons' lamps: batch-covered, consumed silently, shown only on the buttons' labels.
        foreach (var light in A300FcuState.ByButton.Values)
        {
            var def = new SimVarDefinition
            {
                Name = light.Var,
                DisplayName = light.Key,
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromMonitorManager = true,
            };
            batchNames.Add(ContinuousBatchLayout.FullName(def));
            vars[light.Key] = def;
        }

        foreach (var row in _rows.Values)
        {
            if (vars.ContainsKey(row.Key))
                continue;   // never shadow a base variable
            var def = row.Action switch
            {
                A300RowAction.Custom => BuildLeverVariable(row),
                A300RowAction.Typed => new SimVarDefinition
                {
                    Name = "MSFSBA_" + row.Key,
                    DisplayName = row.Name,
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.Never,
                    // An empty or mistyped box arrives as NaN, which every typed value refuses with
                    // its range; 0 would be a real heading.
                    UnparseableTextAsNaN = true,
                },
                _ => BuildRowVariable(row),
            };
            // A light-labeled button repaints on every input its label reads, from the same table as the label.
            if (TryButtonLabel(row.Key, out var labelKeys, out _))
                def.StateVariables = labelKeys.ToArray();
            if (A300Announcements.AnnouncedKeys.Contains(row.Key))
                def.ExcludeFromMonitorManager = false;   // it speaks, so Ctrl+M can mute it
            // A row whose variable another row already carries on the batch (an IDC copy, the crew oxygen
            // supply button wired to the courier supply's variable) streams on its own subscription ([VAR-7]):
            // read only at panel open, it went stale after the other row's write and planned from it.
            if (ContinuousBatchLayout.RidesBatch(def) && !batchNames.Add(ContinuousBatchLayout.FullName(def)))
                def.ExcludeFromBatch = true;
            vars[row.Key] = def;
        }

        RegisterFmaSources(vars);
        RegisterTakeoffCallouts(vars);

        // Ctrl+B's STD and QNH (A300Baro, [A300-16]): each side's STD flag streams on its own
        // once-a-second subscription, never the batch (a new batch name can split the FMA's sources,
        // [A300-9]), and is consumed silently; the setting a side saves when pulled is read on demand.
        foreach (var side in A300Baro.Sides)
        {
            vars[side.ModeKey] = new SimVarDefinition
            {
                Name = side.ModeVar,
                DisplayName = side.Name + " altimeter mode",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromBatch = true,
                ExcludeFromMonitorManager = true,
            };
            vars[side.SavedKey] = new SimVarDefinition
            {
                Name = side.SavedVar,
                DisplayName = side.Name + " altimeter saved setting",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
            };
        }

        // The two buses' light power ([A300-23]): every light is lit only while its bus's is on. Their own
        // subscriptions, never the batch ([A300-9]); consumed silently, no Ctrl+M row of their own.
        OwnSubscription(vars, A300LampBoard.AcPowerKey, A300LampBoard.AcPowerVar, "AC light power");
        OwnSubscription(vars, A300LampBoard.DcPowerKey, A300LampBoard.DcPowerVar, "DC light power");

        // The autobrake buttons' lamps ([A300-20]): the armed level and each button's DECEL light, on the
        // buttons' labels and spoken as they change (A300LampBoard), so each carries a Ctrl+M row: the
        // level's mutes the three armed lights it lights. Their own subscriptions, never the batch ([A300-9]).
        OwnSubscription(vars, A300Autobrake.LevelKey, A300Autobrake.LevelVar, "Autobrake armed lights", ctrlMRow: true);
        foreach (var button in A300Autobrake.ByButton.Values)
            OwnSubscription(vars, button.DecelKey, button.DecelVar, button.Name + " decel light", ctrlMRow: true);

        // The thrust rating panel ([A300-21]): the mode lights the buttons and is the Thrust Rating Panel's
        // "TRP" line, which also reads AUTO's limit, the flex temperature (that panel's own readout)
        // and, on GE engines, the N1 limit. Their own subscriptions, never the batch ([A300-9]).
        var trp = OwnSubscription(vars, A300Trp.ModeKey, A300Trp.ModeVar, "TRP");
        trp.RenderAsReadOnlyStatus = true;
        trp.StateVariables = new[]   // any part changing repaints the line; its own key too (consumed)
        {
            A300Trp.ModeKey, A300Trp.AutoModeKey, A300Readouts.FlexTemperatureKey, A300Trp.N1LimitKey, A300Trp.PwEnginesKey,
        };
        OwnSubscription(vars, A300Trp.AutoModeKey, A300Trp.AutoModeVar, "TRP auto limit");
        OwnSubscription(vars, A300Trp.N1LimitKey, A300Trp.N1LimitVar, "TRP N1 limit");
        OwnSubscription(vars, A300Trp.PwEnginesKey, A300Trp.PwEnginesVar, "PW engines");

        // The clocks (A300Clock): the GMT digits a time line is composed from, and the chrono and elapsed time
        // buttons' states that label them. Their own subscriptions, never the batch ([A300-9]); consumed silently.
        foreach (var (key, var) in A300Clock.Digits)
            OwnSubscription(vars, key, var);
        foreach (var (button, state) in A300Clock.ButtonStates)
            OwnSubscription(vars, A300Clock.StateKey(button), state.Var);
        // The rudder trim display's direction and digits its line is composed from (A300Trim), the same way.
        foreach (var (key, var) in A300Trim.RudderDigits)
            OwnSubscription(vars, key, var);
        // The VHF and ADF windows and transfer switches their lines and read-backs are composed from (A300Radios),
        // and each ADF's signal, which gates its bearing line.
        foreach (var radio in A300Radios.All)
        {
            OwnSubscription(vars, radio.Window1Key, radio.Window1Var);
            OwnSubscription(vars, radio.Window2Key, radio.Window2Var);
            OwnSubscription(vars, radio.TransferKey, radio.TransferVar);
        }
        // Each IRS's aligning and aligned flags, its alignment line is composed from (A300IrsAlignment). The aligning flag
        // is the IRS align light's variable, which the light's own key streams: the line reads that key's cache.
        foreach (var line in A300IrsAlignment.All)
        {
            InputSubscription(vars, line.AligningKey, line.AligningVar);
            InputSubscription(vars, line.AlignedKey, line.AlignedVar);
        }
        // The MCDUs' annunciators (A300McduLights): the MCDU window's status box and its call-out of a light coming on.
        // The captain's MSG lamp is also a fault light, whose key streams it.
        foreach (var light in A300McduLights.All)
            InputSubscription(vars, light.Key, light.Var);
        // The ECAM Memos box's line, then the E/WD memos' inputs (A300EwdMemos). The line needs a variable of its own to
        // carry it, so it takes the first input's, and that input reads the line's cache. Memos are never spoken (design
        // decision, 2026-10-11), so the line has no Ctrl+M row ([VAR-9]).
        var memoLine = OwnSubscription(vars, A300EwdMemos.LineKey, A300EwdMemos.Inputs[0].Var, "ECAM memos");
        memoLine.RenderAsReadOnlyStatus = true;
        foreach (var input in A300EwdMemos.Inputs)
        {
            var def = InputSubscription(vars, input.Key, input.Var, input.IsStock, input.Units);
            if (def != null)
                def.ChangeTolerance = input.ChangeTolerance;
        }
        // The cargo door's flags its line is composed from, and which angle button is picked (A300CargoDoor).
        foreach (var (key, var) in A300CargoDoor.Flags.Concat(A300CargoDoor.AngleButtons.Values))
            OwnSubscription(vars, key, var);
        foreach (var (signalKey, signalVar) in A300Radios.BearingLines.Values)
        {
            var signal = OwnSubscription(vars, signalKey, signalVar);
            signal.Type = SimVarType.SimVar;
            signal.Units = "number";
            signal.ChangeTolerance = A300Radios.SignalChangeTolerance;   // compared with zero only
        }
        // Whether the captain's PFD draws its ILS deviation pointers (A300IlsDeviation): the EFIS nav selector (the selector
        // row's variable, so that row's cache), the localizer's validity and the ILS receiver's glideslope, the same way.
        InputSubscription(vars, A300IlsDeviation.NavSelectorKey, A300IlsDeviation.NavSelectorVar);
        OwnSubscription(vars, A300IlsDeviation.LocalizerValidKey, A300IlsDeviation.LocalizerValidVar);
        var glideslopeReceived = OwnSubscription(vars, A300IlsDeviation.GlideslopeReceivedKey, A300IlsDeviation.GlideslopeReceivedVar);
        glideslopeReceived.Type = SimVarType.SimVar;
        glideslopeReceived.Units = "Bool";

        foreach (var readout in _readouts.Values)
        {
            // The altitude window rides the batch: it speaks a change MSFSBA did not make
            // (A300FcuWindows), and its Ctrl+M row mutes that. Every other readout is read on request.
            bool announced = readout.Key == A300Readouts.AltitudeKey;
            vars[readout.Key] = new SimVarDefinition
            {
                Name = readout.Var,
                DisplayName = readout.Name,
                Type = readout.IsStock ? SimVarType.SimVar : SimVarType.LVar,
                Units = readout.Units,
                UpdateFrequency = announced ? UpdateFrequency.Continuous : UpdateFrequency.OnRequest,
                IsAnnounced = announced,
                RenderAsReadOnlyStatus = true,
            };
        }

        // End-to-end MobiFlight probe target: every A300 write is a calculator-path string, and with
        // no WASM module installed each one is dropped with nothing to say so. Registering this var
        // opts the A300 into MainForm's probe and its spoken warning (CalcPathProbeOptInTests).
        vars["MSFSBA_BRIDGE_PROBE"] = new SimVarDefinition
        {
            Name = "MSFSBA_BRIDGE_PROBE",
            DisplayName = "Bridge Probe",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
        };

        // A label or status line that reads an aliased key repaints when the key that streams it is delivered.
        RepointStateVariables(vars.Values, _inputAliases);

        // A later `vars[key] =` over a silent subscription would leave its key consumed in silence by a definition that
        // may have a Ctrl+M row of its own.
        foreach (var key in _silentKeys)
            if (!vars.TryGetValue(key, out var registered) || !registered.ExcludeFromMonitorManager)
                Log.Warn("A300", $"{key} is in the silent set but is no longer registered as a silent subscription.");

        int batched = vars.Values.Count(ContinuousBatchLayout.RidesBatch);
        int onRequest = vars.Values.Count(d => d.UpdateFrequency == UpdateFrequency.OnRequest);
        Log.Info("A300", $"Built {vars.Count} variables: {batched} batch-covered, {onRequest} on request, {_rows.Count} panel rows, " +
                         $"{_inputAliases.Count} composed-line inputs read from the key that streams them.");
        return vars;
    }

    /// <summary>
    /// Registers the subscription a composed line's input reads under <paramref name="key"/>, as
    /// <see cref="OwnSubscription"/> does, unless a key registered before it already streams the same variable (a
    /// continuous, announced definition with the same name, type and, for a stock variable, unit). Then it registers
    /// nothing, records the key in <see cref="_inputAliases"/> and returns null, and every reader asks
    /// <see cref="ReadInput"/>. Only an input that is read from the cache comes through here. The key it reads instead
    /// may be a light board input, a panel row or a key with a Ctrl+M row (every delivery fills the same cache); those
    /// keys themselves are never passed in, because their own deliveries drive something.
    /// </summary>
    private SimVarDefinition? InputSubscription(Dictionary<string, SimVarDefinition> vars, string key, string varName,
        bool isStock = false, string units = "number")
    {
        var type = isStock ? SimVarType.SimVar : SimVarType.LVar;
        foreach (var (streamingKey, streaming) in vars)
        {
            if (streaming.UpdateFrequency == UpdateFrequency.Continuous && streaming.IsAnnounced && streaming.Type == type
                && string.Equals(streaming.Name, varName, StringComparison.Ordinal)
                && (!isStock || string.Equals(streaming.Units, units, StringComparison.OrdinalIgnoreCase)))
            {
                _inputAliases[key] = streamingKey;
                return null;
            }
        }
        var own = OwnSubscription(vars, key, varName);
        if (isStock)
        {
            own.Type = SimVarType.SimVar;
            own.Units = units;
        }
        return own;
    }

    /// <summary>Makes every <see cref="SimVarDefinition.StateVariables"/> list that names an aliased key name the key that
    /// streams it instead, once each.</summary>
    internal static void RepointStateVariables(IEnumerable<SimVarDefinition> definitions, IReadOnlyDictionary<string, string> aliases)
    {
        foreach (var def in definitions)
            if (def.StateVariables is { } listed && listed.Any(aliases.ContainsKey))
                def.StateVariables = listed.Select(key => aliases.GetValueOrDefault(key, key)).Distinct().ToArray();
    }

    /// <summary>
    /// Registers a state variable a label, a box line or a refusal reads under <paramref name="key"/>: its own
    /// once-a-second subscription, never the batch (a new batch name can split the FMA's sources, [A300-9]), consumed
    /// silently and with no Ctrl+M row, so the key joins <see cref="_silentKeys"/>. The autobrake lights, which the
    /// light board speaks, pass <paramref name="ctrlMRow"/>: the row mutes that speech and the board consumes the key,
    /// so it is not silent. The display name is the key unless <paramref name="name"/> says otherwise.
    /// </summary>
    private SimVarDefinition OwnSubscription(Dictionary<string, SimVarDefinition> vars, string key, string varName,
        string? name = null, bool ctrlMRow = false)
    {
        var def = new SimVarDefinition
        {
            Name = varName,
            DisplayName = name ?? key,
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromBatch = true,
            ExcludeFromMonitorManager = !ctrlMRow,
        };
        vars[key] = def;
        if (!ctrlMRow)
            _silentKeys.Add(key);
        return def;
    }

    /// <summary>A map control's row.</summary>
    private static SimVarDefinition BuildRowVariable(A300PlacedRow row)
    {
        var control = row.Control!;
        switch (row.Action)
        {
            case A300RowAction.Set when control.Kind == A300Kinds.Knob:
                return new SimVarDefinition
                {
                    Name = Bare(control.StateVar!),
                    DisplayName = row.Name,
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.OnRequest,   // read when its panel opens
                    RenderAsSlider = true,
                    SliderMin = 0,
                    SliderMax = 100 * (control.Scale ?? 1),
                    RefreshControlWhenDefHandled = _ => true,
                };

            case A300RowAction.Set:
            {
                bool stockVar = control.StateVar!.StartsWith("A:", StringComparison.Ordinal);
                return new SimVarDefinition
                {
                    Name = Bare(control.StateVar),
                    DisplayName = row.Name,
                    Type = stockVar ? SimVarType.SimVar : SimVarType.LVar,
                    Units = stockVar ? control.StateUnit ?? "Bool" : "number",
                    UpdateFrequency = UpdateFrequency.Continuous,
                    IsAnnounced = true,                    // batch-covered; consumed silently in ProcessSimVarUpdate
                    ExcludeFromMonitorManager = true,      // a checkbox here would mute nothing
                    ValueDescriptions = new Dictionary<double, string>(row.Positions),
                    // The panel combo follows a change made elsewhere (the cockpit, a hardware
                    // switch): the write fires only on a user commit, so a refresh sends nothing.
                    RefreshControlWhenDefHandled = _ => true,
                };
            }

            default:   // Press, Increase, Decrease: write-only buttons, never registered
                return new SimVarDefinition
                {
                    Name = "MSFSBA_" + row.Key.Replace('#', '_'),
                    DisplayName = row.Name,
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.Never,
                    RenderAsButton = true,
                };
        }
    }

    /// <summary>A hand-written lever (<see cref="A300Levers"/>).</summary>
    private static SimVarDefinition BuildLeverVariable(A300PlacedRow row) => row.Key switch
    {
        A300Levers.FlapsKey => Lever(row.Name, "FLAPS HANDLE INDEX", SimVarType.SimVar, "number", A300Levers.FlapPositions),
        A300Levers.SpoilersArmKey => Lever(row.Name, "INI_SPOILERS_ARMED", SimVarType.LVar, "number", A300Levers.ArmPositions),
        _ => new SimVarDefinition
        {
            Name = "INI_SPOILERS_HANDLE_POSITION",
            DisplayName = row.Name,
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromMonitorManager = true,
            RenderAsSlider = true,
            SliderMin = 0,
            SliderMax = 1,
            RefreshControlWhenDefHandled = _ => true,
        },
    };

    /// <summary>A lever read from the batch and shown as a combo.</summary>
    private static SimVarDefinition Lever(string name, string var, SimVarType type, string units,
        IReadOnlyDictionary<double, string> positions) => new()
    {
        Name = var,
        DisplayName = name,
        Type = type,
        Units = units,
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        ExcludeFromMonitorManager = true,
        ValueDescriptions = new Dictionary<double, string>(positions),
        RefreshControlWhenDefHandled = _ => true,
    };

    /// <summary>"L:NAME" → "NAME", "A:NAME:2" → "NAME:2" (SimVarDefinition names carry no prefix).</summary>
    private static string Bare(string stateVar) => stateVar.Length > 2 && stateVar[1] == ':' ? stateVar.Substring(2) : stateVar;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _seedGate.Disarm();
        _lampSpeech.Clear();
        ReleaseOwedSteps();   // a held button or a spring switch is let go before the definition goes away
        ReleaseMcduKeys();
        _sim = null;
        DisposeTrackedWindows();
        GC.SuppressFinalize(this);
    }
}
