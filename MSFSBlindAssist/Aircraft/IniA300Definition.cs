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

    /// <summary>Lamp variable key → the light it announces.</summary>
    private readonly Dictionary<string, A300Lamp> _lamps = new(StringComparer.Ordinal);

    /// <summary>Readout variable key → its value.</summary>
    private readonly Dictionary<string, A300Readout> _readouts = new(StringComparer.Ordinal);

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
        foreach (var lamp in A300Announcements.Lamps)
            _lamps[lamp.Key] = lamp;
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
            var shared = OwnSubscription(input.Name, input.Name);
            shared.Type = input.IsStock ? SimVarType.SimVar : SimVarType.LVar;
            shared.Units = input.IsStock ? input.Units : "number";
            vars[A300PanelLamps.InputKey(input)] = shared;
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
            if (A300FcuState.ByButton.TryGetValue(row.Key, out var buttonLight))
                def.StateVariables = new[] { buttonLight.Key };
            else if (A300Autobrake.ByButton.TryGetValue(row.Key, out var autobrake))
                def.StateVariables = new[] { A300Autobrake.LevelKey, autobrake.DecelKey, A300LampBoard.AcPowerKey };
            else if (A300Trp.ModeByButton.ContainsKey(row.Key))
                def.StateVariables = new[] { A300Trp.ModeKey };
            else if (A300PanelLamps.ByButton.TryGetValue(row.Key, out var lampKey))
                def.StateVariables = new[] { lampKey, A300LampBoard.AcPowerKey, A300LampBoard.DcPowerKey };
            else if (A300Clock.ButtonStates.ContainsKey(row.Key))
                def.StateVariables = new[] { A300Clock.StateKey(row.Key) };
            else if (A300CargoDoor.AngleButtons.TryGetValue(row.Key, out var angle))
                def.StateVariables = new[] { angle.Key };
            if (A300Announcements.AnnouncedKeys.Contains(row.Key))
                def.ExcludeFromMonitorManager = false;   // it speaks, so Ctrl+M can mute it
            // A row whose variable another row already carries on the batch (an IDC copy, the crew oxygen
            // supply button wired to the courier supply's variable) streams on its own subscription ([VAR-7]):
            // read only at panel open, it went stale after the other row's write and planned from it.
            if (ContinuousBatchLayout.RidesBatch(def) && !batchNames.Add(ContinuousBatchLayout.FullName(def)))
                def.ExcludeFromBatch = true;
            vars[row.Key] = def;
        }

        RegisterFmaSources(vars, batchNames);
        RegisterTakeoffCallouts(vars, batchNames);

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
        vars[A300LampBoard.AcPowerKey] = OwnSubscription(A300LampBoard.AcPowerVar, "AC light power");
        vars[A300LampBoard.DcPowerKey] = OwnSubscription(A300LampBoard.DcPowerVar, "DC light power");

        // The autobrake buttons' lamps ([A300-20]): the armed level and each button's DECEL light, on the
        // buttons' labels and spoken as they change (A300LampBoard), so each carries a Ctrl+M row: the
        // level's mutes the three armed lights it lights. Their own subscriptions, never the batch ([A300-9]).
        vars[A300Autobrake.LevelKey] = OwnSubscription(A300Autobrake.LevelVar, "Autobrake armed lights");
        vars[A300Autobrake.LevelKey].ExcludeFromMonitorManager = false;
        foreach (var button in A300Autobrake.ByButton.Values)
        {
            vars[button.DecelKey] = OwnSubscription(button.DecelVar, button.Name + " decel light");
            vars[button.DecelKey].ExcludeFromMonitorManager = false;
        }

        // The thrust rating panel ([A300-21]): the mode lights the buttons and is the Thrust Rating Panel's
        // "TRP" line, which also reads AUTO's limit, the flex temperature (that panel's own readout)
        // and, on GE engines, the N1 limit. Their own subscriptions, never the batch ([A300-9]).
        var trp = OwnSubscription(A300Trp.ModeVar, "TRP");
        trp.RenderAsReadOnlyStatus = true;
        trp.StateVariables = new[]   // any part changing repaints the line; its own key too (consumed)
        {
            A300Trp.ModeKey, A300Trp.AutoModeKey, A300Readouts.FlexTemperatureKey, A300Trp.N1LimitKey, A300Trp.PwEnginesKey,
        };
        vars[A300Trp.ModeKey] = trp;
        vars[A300Trp.AutoModeKey] = OwnSubscription(A300Trp.AutoModeVar, "TRP auto limit");
        vars[A300Trp.N1LimitKey] = OwnSubscription(A300Trp.N1LimitVar, "TRP N1 limit");
        vars[A300Trp.PwEnginesKey] = OwnSubscription(A300Trp.PwEnginesVar, "PW engines");

        // The clocks (A300Clock): the GMT digits a time line is composed from, and the chrono and elapsed time
        // buttons' states that label them. Their own subscriptions, never the batch ([A300-9]); consumed silently.
        foreach (var (key, var) in A300Clock.Digits)
            vars[key] = OwnSubscription(var, key);
        foreach (var (button, state) in A300Clock.ButtonStates)
            vars[A300Clock.StateKey(button)] = OwnSubscription(state.Var, A300Clock.StateKey(button));
        // The rudder trim display's direction and digits its line is composed from (A300Trim), the same way.
        foreach (var (key, var) in A300Trim.RudderDigits)
            vars[key] = OwnSubscription(var, key);
        // The VHF and ADF windows and transfer switches their lines and read-backs are composed from (A300Radios),
        // and each ADF's signal, which gates its bearing line.
        foreach (var radio in A300Radios.All)
        {
            vars[radio.Window1Key] = OwnSubscription(radio.Window1Var, radio.Window1Key);
            vars[radio.Window2Key] = OwnSubscription(radio.Window2Var, radio.Window2Key);
            vars[radio.TransferKey] = OwnSubscription(radio.TransferVar, radio.TransferKey);
        }
        // Each IRS's aligning and aligned flags, its alignment line is composed from (A300IrsAlignment).
        foreach (var line in A300IrsAlignment.All)
        {
            vars[line.AligningKey] = OwnSubscription(line.AligningVar, line.AligningKey);
            vars[line.AlignedKey] = OwnSubscription(line.AlignedVar, line.AlignedKey);
        }
        // The MCDUs' annunciators (A300McduLights): the MCDU window's status box and its "MSG" call-out.
        foreach (var light in A300McduLights.All)
            vars[light.Key] = OwnSubscription(light.Var, light.Key);
        // The E/WD memos' inputs (A300EwdMemos), and the ECAM Memos box's line. Memos are never spoken (design
        // decision, 2026-10-11), so the line has no Ctrl+M row ([VAR-9]).
        foreach (var input in A300EwdMemos.Inputs)
        {
            var def = OwnSubscription(input.Var, input.Key);
            if (input.IsStock)
            {
                def.Type = SimVarType.SimVar;
                def.Units = input.Units;
            }
            vars[input.Key] = def;
        }
        var memoLine = OwnSubscription(A300EwdMemos.Inputs[0].Var, "ECAM memos");
        memoLine.RenderAsReadOnlyStatus = true;
        vars[A300EwdMemos.LineKey] = memoLine;
        // The cargo door's flags its line is composed from, and which angle button is picked (A300CargoDoor).
        foreach (var (key, var) in A300CargoDoor.Flags.Concat(A300CargoDoor.AngleButtons.Values))
            vars[key] = OwnSubscription(var, key);
        foreach (var (signalKey, signalVar) in A300Radios.BearingLines.Values)
        {
            var signal = OwnSubscription(signalVar, signalKey);
            signal.Type = SimVarType.SimVar;
            signal.Units = "number";
            vars[signalKey] = signal;
        }
        // Whether the captain's PFD draws its ILS deviation pointers (A300IlsDeviation): the EFIS nav selector,
        // the localizer's validity and the ILS receiver's glideslope, the same way.
        vars[A300IlsDeviation.NavSelectorKey] = OwnSubscription(A300IlsDeviation.NavSelectorVar, A300IlsDeviation.NavSelectorKey);
        vars[A300IlsDeviation.LocalizerValidKey] = OwnSubscription(A300IlsDeviation.LocalizerValidVar, A300IlsDeviation.LocalizerValidKey);
        var glideslopeReceived = OwnSubscription(A300IlsDeviation.GlideslopeReceivedVar, A300IlsDeviation.GlideslopeReceivedKey);
        glideslopeReceived.Type = SimVarType.SimVar;
        glideslopeReceived.Units = "Bool";
        vars[A300IlsDeviation.GlideslopeReceivedKey] = glideslopeReceived;

        foreach (var readout in _readouts.Values)
        {
            // The altitude window rides the batch: it speaks a change MSFSBA did not make
            // (A300FcuWindows), and its Ctrl+M row mutes that. Every other readout is read on request.
            bool announced = readout.Key == A300Readouts.AltitudeKey;
            var def = new SimVarDefinition
            {
                Name = readout.Var,
                DisplayName = readout.Name,
                Type = readout.IsStock ? SimVarType.SimVar : SimVarType.LVar,
                Units = readout.Units,
                UpdateFrequency = announced ? UpdateFrequency.Continuous : UpdateFrequency.OnRequest,
                IsAnnounced = announced,
                RenderAsReadOnlyStatus = true,
            };
            if (announced)
                batchNames.Add(ContinuousBatchLayout.FullName(def));
            vars[readout.Key] = def;
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

        int batched = vars.Values.Count(ContinuousBatchLayout.RidesBatch);
        int onRequest = vars.Values.Count(d => d.UpdateFrequency == UpdateFrequency.OnRequest);
        Log.Info("A300", $"Built {vars.Count} variables: {batched} batch-covered, {onRequest} on request, {_rows.Count} panel rows.");
        return vars;
    }

    /// <summary>A state variable a label or a refusal reads: its own once-a-second subscription, never the
    /// batch (a new batch name can split the FMA's sources, [A300-9]), consumed silently, no Ctrl+M row.</summary>
    private static SimVarDefinition OwnSubscription(string var, string name) => new()
    {
        Name = var,
        DisplayName = name,
        Type = SimVarType.LVar,
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        ExcludeFromBatch = true,
        ExcludeFromMonitorManager = true,
    };

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
