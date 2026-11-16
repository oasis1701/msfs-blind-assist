using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;
using R = MSFSBlindAssist.Aircraft.BBS757.Bbs757Readouts;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The 757's flight-control readouts, its MCP lights, its EXEC light, its engine rows and its announced
/// lights and states (<see cref="Bbs757Annunciators"/>, plan 06; EXT PWR reads back on or off): what is
/// registered, where it shows, what is spoken on change, the trim in BlueBird's units, and the
/// output-mode readouts (L flaps, Shift+G gear, B the altimeters and the radio minimums, F and Shift+F
/// fuel, W and Shift+W gross weight).
///
/// Speech follows CORE-7. A change the pilot did not make (flaps, gear, speedbrake, parking brake,
/// an MCP light, the EXEC light) is spoken once, through <see cref="Bbs757ChangeTracker"/>: the
/// cockpit as found is a silent baseline, an unchanged value is silent, and a change the app's own
/// press or pick made is its echo (the press reads it back itself, the screen reader read the pick).
/// A readout is spoken at once (AnnounceImmediate), never refused silently.
///
/// The data-definition budget: the announced rows ride the 1 Hz batch (no individual definition), the
/// flap angle and the ten engine rows are read while their display is shown, and the
/// altimeters, the decision height, the gear lever and the gross weight are read on request. The
/// whole profile stays under 900 variables (Bbs757DefinitionTests).
/// </summary>
public partial class BBS757Definition
{
    /// <summary>How long a change of a state row after the app's own write to its control is that write's echo.</summary>
    internal const int OwnWriteEchoMs = 5000;

    /// <summary>The state call-outs' change tracker (baseline, echo, change).</summary>
    private readonly Bbs757ChangeTracker _changes = new();

    /// <summary>The gear legs as last delivered, for the composed gear call-out and status row.</summary>
    private readonly Dictionary<string, double> _gearLegs = new(StringComparer.Ordinal);

    /// <summary>The announced rows' last delivered values, for a row whose phrase reads another's (an IRS's switch).</summary>
    private readonly Dictionary<string, double> _annunciatorValues = new(StringComparer.Ordinal);

    /// <summary>The clock the echo windows and the speed sets' spacing (BBS757Definition.Mcp.cs) run on; replaced by tests.</summary>
    internal Func<long> NowMs { get; init; } = () => Environment.TickCount64;

    /// <summary>
    /// Whether the pilot muted a Ctrl+M row; replaced by tests. Only the gear call-out asks it: the
    /// three legs speak the one "Gear" row's call-out, so they are left out of MainForm's mute wrap
    /// (<see cref="IsMuteWrapExempt"/>, [VAR-8]) and check the row themselves.
    /// </summary>
    internal Func<string, bool> IsMonitorMuted { get; init; } =
        key => Settings.SettingsManager.Current.BBS757DisabledMonitorVariablesSet.Contains(key);

    // =================================================================================
    // Variables
    // =================================================================================

    /// <summary>Every variable of this partial, added after the map's (never shadowing one).</summary>
    private static void AddReadoutVariables(Dictionary<string, SimVarDefinition> vars)
    {
        void Add(string key, SimVarDefinition def)
        {
            if (!vars.ContainsKey(key)) vars[key] = def;
        }

        // Announced state rows: Continuous + IsAnnounced rides the 1 Hz batch; ProcessSimVarUpdate
        // speaks them, and each keeps its Ctrl+M row.
        var detents = new Dictionary<double, string>();
        for (int i = 0; i < R.FlapDetents.Count; i++) detents[i] = R.FlapDetents[i];
        Add(R.FlapsHandleKey, StreamedRow("FLAPS HANDLE INDEX", "Flaps", SimVarType.SimVar, "number", detents));
        Add(R.SpeedbrakeKey, StreamedRow(R.SpeedbrakeLeverVar, "Speedbrake", SimVarType.LVar, "number"));
        Add(R.ParkingBrakeKey, StreamedRow(R.ParkingBrakeVar, "Parking Brake", SimVarType.LVar, "number",
            new Dictionary<double, string> { [0] = "Released", [1] = "Set" }));

        // The three gear legs: the nose leg's row is the one Ctrl+M row for the gear call-outs.
        Add(R.GearNoseKey, StreamedRow("GEAR CENTER POSITION", "Gear", SimVarType.SimVar, "percent over 100"));
        var left = StreamedRow("GEAR LEFT POSITION", "Left Main Gear", SimVarType.SimVar, "percent over 100");
        left.ExcludeFromMonitorManager = true;
        Add(R.GearLeftKey, left);
        var right = StreamedRow("GEAR RIGHT POSITION", "Right Main Gear", SimVarType.SimVar, "percent over 100");
        right.ExcludeFromMonitorManager = true;
        Add(R.GearRightKey, right);

        // The MCP button lights.
        foreach (var light in Bbs757McpLights.All)
            Add(light.Key, StreamedRow(light.LightVar, light.Label, SimVarType.LVar, "number"));

        // The announced lights and states (plan 06): status rows on their panel's display, or on no display.
        // A silent row (an IRS switch another row reads) rides the batch too, with no Ctrl+M row ([VAR-9]).
        foreach (var row in Bbs757Annunciators.Rows)
        {
            Add(row.Key, new SimVarDefinition
            {
                Name = row.LVar, DisplayName = row.Label, Type = SimVarType.LVar, Units = "number",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ExcludeFromMonitorManager = row.Silent,
                RenderAsReadOnlyStatus = row.Panel != null,
                ValueDescriptions = row.States?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<double, string>(),
            });
        }

        // Plan 14's watches: a squawk, TCAS mode or altimeter change from any source is spoken, each
        // with its Ctrl+M row. The squawk is the transponder panel's read-back row itself, now on the
        // batch: a second continuous row of the same name would shift every later slot ([VAR-7]).
        var squawk = WatchRow("TRANSPONDER CODE:1", "Squawk", SimVarType.SimVar, "BCO16");
        squawk.RenderAsReadOnlyStatus = true;
        Add(Bbs757Annunciators.SquawkWatchKey, squawk);
        Add(Bbs757Annunciators.TcasModeWatchKey, WatchRow(Bbs757Annunciators.TcasModeNode, "TCAS Mode", SimVarType.LVar, "number"));
        Add(Bbs757Annunciators.AltimeterWatchKey, WatchRow("KOHLSMAN SETTING MB:1", "Altimeter", SimVarType.SimVar, "millibars"));

        // The captain's CDU EXEC light (ruling R36): spoken on change, with a Ctrl+M row of its own,
        // and on no panel or status display.
        Add(R.ExecLightKey, new SimVarDefinition
        {
            Name = R.ExecLightVar, DisplayName = "EXEC light", Type = SimVarType.LVar, Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
        });

        // Silent rows, read while their status display is shown (no Ctrl+M row: they speak nothing).
        Add(R.FlapsAngleKey, DisplayedRow("TRAILING EDGE FLAPS LEFT ANGLE", "Flap Angle", "degrees"));
        foreach (var row in R.EngineRows) Add(row.Key, DisplayedRow(row.SimVar, row.Label, row.Units));

        // Read on request only: the readouts and the windows' read-backs.
        Add(R.GearHandleKey, RequestedRead("B757_GEAR_HANDLE_POS", "Gear Lever", "number", SimVarType.LVar));
        Add(R.TotalWeightKey, RequestedRead("TOTAL WEIGHT", "Gross Weight", "pounds"));
        for (int i = 0; i < Bbs757Altimeters.KohlsmanKeys.Count; i++)
            Add(Bbs757Altimeters.KohlsmanKeys[i], RequestedRead($"KOHLSMAN SETTING MB:{i + 1}", $"Altimeter {i + 1}", "millibars"));
        Add(Bbs757Altimeters.DecisionHeightKey, RequestedRead("DECISION HEIGHT", "Decision Height", "feet"));

        // The squawk field. A key containing "_SET" is what MainForm renders as a text box Enter
        // applies; the definition claims it (HandleUIVariableSet), so the event named here is never
        // sent: it only keeps the registration an event, which takes no data definition.
        Add(Bbs757Squawk.SetKey, new SimVarDefinition
        {
            Name = "XPNDR_SET", DisplayName = "Squawk Code",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.OnRequest,
            UnparseableTextAsNaN = true,
        });
    }

    private static SimVarDefinition StreamedRow(string name, string label, SimVarType type, string units,
        Dictionary<double, string>? descriptions = null) => new()
    {
        Name = name, DisplayName = label, Type = type, Units = units,
        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
        RenderAsReadOnlyStatus = true,
        ValueDescriptions = descriptions ?? new Dictionary<double, string>(),
    };

    private static SimVarDefinition WatchRow(string name, string label, SimVarType type, string units) => new()
    {
        Name = name, DisplayName = label, Type = type, Units = units,
        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
    };

    private static SimVarDefinition DisplayedRow(string name, string label, string units) => new()
    {
        Name = name, DisplayName = label, Type = SimVarType.SimVar, Units = units,
        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = false,
        RenderAsReadOnlyStatus = true, ExcludeFromMonitorManager = true,
    };

    private static SimVarDefinition RequestedRead(string name, string label, string units,
        SimVarType type = SimVarType.SimVar) => new()
    {
        Name = name, DisplayName = label, Type = type, Units = units,
        UpdateFrequency = UpdateFrequency.OnRequest, ExcludeFromMonitorManager = true,
    };

    // =================================================================================
    // Panels
    // =================================================================================

    /// <summary>The panel the transponder's controls are on (the map's), which gains the squawk field.</summary>
    internal const string TransponderPanel = "TCAS and transponder";

    /// <summary>The transponder mode knob, the map's first control on <see cref="TransponderPanel"/>; the squawk field follows it.</summary>
    internal const string TransponderModeNode = "B757_PED_TCAS_MODE_KNOB";

    /// <summary>The map's MCP panel, whose status display lists the button lights.</summary>
    internal const string McpPanel = "MCP";

    /// <summary>
    /// The map's Pedestal panel with the ignition, the start selectors and the fuel control switches (task
    /// 11d), whose status display shows the engine values (R45), so an engine start (selector GND, watch N2,
    /// fuel control RUN, EGT) happens in one panel.
    /// </summary>
    internal const string EnginesPanel = "Engines";

    /// <summary>
    /// After the map's panels: the flight-control rows join the Status display (after the map's rows),
    /// the MCP's status display lists the lights, the transponder panel gains the squawk field (right
    /// after its mode knob) and its read-back row, and the Engines panel's status display shows the
    /// engine values. A display joins only a panel the map has: a display of a missing panel would be
    /// an orphan entry and its rows would silently vanish.
    /// </summary>
    private static void AddReadoutPanels(Dictionary<string, List<string>> structure,
        Dictionary<string, List<string>> controls, Dictionary<string, List<string>> displays)
    {
        List<string> Display(string panel)
        {
            if (!displays.TryGetValue(panel, out var rows)) displays[panel] = rows = new List<string>();
            return rows;
        }

        if (!controls.ContainsKey(StatusSection))
        {
            controls[StatusSection] = new List<string>();
            if (!structure.TryGetValue(StatusSection, out var statusPanels)) structure[StatusSection] = statusPanels = new List<string>();
            statusPanels.Add(StatusSection);
        }
        Display(StatusSection).AddRange(new[]
        {
            R.FlapsHandleKey, R.FlapsAngleKey, R.GearNoseKey, R.SpeedbrakeKey, R.ParkingBrakeKey,
        });

        if (controls.ContainsKey(McpPanel))
            Display(McpPanel).AddRange(Bbs757McpLights.All.Select(l => l.Key));

        if (controls.ContainsKey(EnginesPanel))
            Display(EnginesPanel).AddRange(R.EngineRows.Select(r => r.Key));

        foreach (var row in Bbs757Annunciators.Rows)
            if (row.Panel != null && controls.ContainsKey(row.Panel)) Display(row.Panel).Add(row.Key);

        if (controls.TryGetValue(TransponderPanel, out var transponder))
        {
            // The pilot reaches the squawk field second, right after the mode knob, which the map puts first on
            // this panel (curation control_order); a map without the knob leaves the field at the end.
            int mode = transponder.IndexOf(TransponderModeNode);
            transponder.Insert(mode < 0 ? transponder.Count : mode + 1, Bbs757Squawk.SetKey);
            Display(TransponderPanel).Add(Bbs757Squawk.CodeKey);
        }
    }

    // =================================================================================
    // Announcements
    // =================================================================================

    /// <summary>The gear legs speak the "Gear" row's call-out, so they check its mute themselves ([VAR-8]).</summary>
    public override bool IsMuteWrapExempt(string varName)
        => varName is R.GearNoseKey or R.GearLeftKey or R.GearRightKey || base.IsMuteWrapExempt(varName);

    /// <summary>
    /// The state rows' call-outs: true when <paramref name="varName"/> is one of them (spoken or not),
    /// false for anything else, which goes on to the generic path (the silent rows' display values).
    /// </summary>
    private bool ProcessReadoutUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        switch (varName)
        {
            case R.FlapsHandleKey:
                SpeakChange(varName, R.FlapDetentName(value) == null ? null : R.FlapsPhrase(value), announcer);
                return true;
            case R.SpeedbrakeKey:
                SpeakChange(varName, R.SpeedbrakeWords(value) == null ? null : R.SpeedbrakePhrase(value), announcer);
                return true;
            case R.ParkingBrakeKey:
                SpeakChange(varName, double.IsFinite(value) ? R.ParkingBrakePhrase(value) : null, announcer);
                return true;
            case R.ExecLightKey:
                SpeakChange(varName, double.IsFinite(value) ? R.ExecLightPhrase(value) : null, announcer);
                return true;
            case R.GearNoseKey:
            case R.GearLeftKey:
            case R.GearRightKey:
                _gearLegs[varName] = value;
                if (GearState() is { } gear && _changes.Observe(R.GearNoseKey, R.GearPhrase(gear), NowMs()) is { } said
                    && !IsMonitorMuted(R.GearNoseKey))
                    announcer.Announce(said);
                return true;
            case Bbs757Annunciators.SquawkWatchKey:
                SpeakChange(varName, Bbs757Annunciators.SquawkPhrase(value), announcer);
                return true;
            case Bbs757Annunciators.TcasModeWatchKey:
                SpeakChange(varName, Bbs757Annunciators.TcasModePhrase(value), announcer);
                return true;
            case Bbs757Annunciators.AltimeterWatchKey:
                FeedAltimeter(Bbs757Annunciators.AltimeterPhrase(value), announcer);
                return true;
        }
        if (Bbs757McpLights.ForKey(varName) is { } light)
        {
            SpeakChange(varName, double.IsFinite(value) ? Bbs757McpLights.Phrase(light.Label, value) : null, announcer);
            return true;
        }
        if (Bbs757Annunciators.ForKey(varName) is { } row)
        {
            _annunciatorValues[varName] = value;
            double? related = row.RelatedKey != null && _annunciatorValues.TryGetValue(row.RelatedKey, out double r) ? r : null;
            if (row.Phrase(value, related) is { } phrase
                && _changes.Observe(varName, phrase, NowMs()) is { } said && Bbs757Annunciators.IsSpoken(said))
                announcer.Announce(said);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The shared trim call-out (<c>MON_ElevatorTrim</c>: the stock <c>ELEVATOR TRIM POSITION</c> in
    /// degrees, on the 1 Hz batch) speaks BlueBird's indicator units
    /// (<see cref="Bbs757Readouts.StabTrimUnits"/>) in the 737s' words, "Trim 5.3"
    /// (<see cref="StabTrimUnitsCallout.Phrase"/>, their one rule), keyed on the tenth it speaks: the
    /// stabilizer is heard about once a second while it moves (ruling R35). The base class's
    /// first-reading baseline, hysteresis, Shift+T toggle and Ctrl+M row apply unchanged; no readout
    /// key and no status row.
    /// </summary>
    protected override (double Key, string Phrase) DescribeElevatorTrim(double degrees)
    {
        double units = Math.Round(R.StabTrimUnits(degrees), 1);
        return (units, StabTrimUnitsCallout.Phrase(units));
    }

    /// <summary>One row's phrase this delivery: spoken when the tracker calls it a change. A null phrase (an unreadable value) is neither spoken nor a baseline.</summary>
    private void SpeakChange(string row, string? phrase, ScreenReaderAnnouncer announcer)
    {
        if (phrase == null) return;
        if (_changes.Observe(row, phrase, NowMs()) is { } said) announcer.Announce(said);
    }

    private Bbs757GearState? GearState() => R.GearLegs(
        _gearLegs.TryGetValue(R.GearNoseKey, out double n) ? n : null,
        _gearLegs.TryGetValue(R.GearLeftKey, out double l) ? l : null,
        _gearLegs.TryGetValue(R.GearRightKey, out double r) ? r : null);

    /// <summary>The state row a control's press or pick moves, or null: an MCP button's light, a speedbrake spot's lever, the parking brake.</summary>
    internal static string? LinkedStatusKey(string node)
    {
        if (Bbs757McpLights.ForNode(node) is { } light) return light.Key;
        return node switch
        {
            "BBS757_SPOILERDOWN_CLICK" or "BBS757_SPOILERARMED_CLICK" or "BBS757_SPOILERUP_CLICK" => R.SpeedbrakeKey,
            "BBS757_PEDESTAL_Park_Brake_Lever" => R.ParkingBrakeKey,
            Bbs757Annunciators.ExtPwrButtonNode => Bbs757SharedKeys.ExtPwrOn,
            Bbs757Annunciators.TcasModeNode => Bbs757Annunciators.TcasModeWatchKey,   // the screen reader read the pick
            _ => null,
        };
    }

    /// <summary>A linked row's phrase for a read value: "CMD R on", "Speedbrake armed", "Parking brake set", "EXT PWR on", "TCAS TA/RA".</summary>
    internal static string LinkedPhrase(string statusKey, double? value)
    {
        if (Bbs757McpLights.ForKey(statusKey) is { } light) return Bbs757McpLights.Phrase(light.Label, value);
        return statusKey switch
        {
            R.SpeedbrakeKey => R.SpeedbrakePhrase(value),
            Bbs757SharedKeys.ExtPwrOn => Bbs757Annunciators.ExtPwrPhrase(value),
            Bbs757Annunciators.TcasModeWatchKey
                => value is double v ? Bbs757Annunciators.TcasModePhrase(v) ?? "TCAS mode unavailable" : "TCAS mode unavailable",
            _ => R.ParkingBrakePhrase(value),
        };
    }

    /// <summary>The app is about to write the control that moves <paramref name="statusKey"/>: its change is the write's echo for a while.</summary>
    /// <remarks>
    /// An EXT PWR press arms its AVAIL light too, for the whole window (the read-back does not end it): BlueBird
    /// may put AVAIL out as ON lights (not measured), and the press says "EXT PWR on" itself; "External power
    /// not available" beside it would say the press twice and sound like power lost.
    /// </remarks>
    private void ArmOwnWriteEcho(string statusKey)
    {
        long until = NowMs() + OwnWriteEchoMs;
        _changes.ArmEcho(statusKey, until);
        if (statusKey == Bbs757SharedKeys.ExtPwrOn) _changes.ArmEcho(Bbs757SharedKeys.ExtPwrAvail, until);
    }

    /// <summary>A read-back spoke (or showed) the row's state: that is its baseline now, and later changes speak again.</summary>
    private void SettleOwnWriteEcho(string statusKey, string phrase)
    {
        _changes.Rebaseline(statusKey, phrase);
        _changes.ArmEcho(statusKey, 0);
    }

    // ---- Plan 14: the pilot's own squawk and altimeter sets, and the altimeter once it rests ----------

    /// <summary>
    /// The squawk field is about to set <paramref name="code"/>: its read-back ("Squawk 4743") is the one
    /// confirmation, so the watch's change is that set's echo for a while (CORE-7).
    /// </summary>
    partial void OnOwnSquawkSet(string code) => ArmOwnSquawkEcho();

    /// <summary>Ctrl+B is about to set the altimeters: its read-back is the one confirmation (CORE-7).</summary>
    partial void OnOwnAltimeterSet() => ArmOwnAltimeterEcho();

    internal void ArmOwnSquawkEcho() => _changes.ArmEcho(Bbs757Annunciators.SquawkWatchKey, NowMs() + OwnWriteEchoMs);

    /// <summary>The captain's altimeter, held until it rests (a baro knob turning is heard once, where it stops).</summary>
    private readonly Bbs757RestingValue _altimeterResting = new();

    /// <summary>Until when a change of the altimeter watch is the app's own set's echo.</summary>
    private long _altimeterEchoUntil;

    /// <summary>Whether a UI-thread timer releases the altimeter; tests turn it off and call <see cref="ReleaseRestedAltimeter"/>.</summary>
    internal bool UseAltimeterTimer { get; init; } = true;

    private System.Windows.Forms.Timer? _altimeterTimer;
    private ScreenReaderAnnouncer? _altimeterAnnouncer;

    /// <summary>The release's tick: a value is heard within a quarter second of resting.</summary>
    private const int AltimeterTickMs = 250;

    internal void ArmOwnAltimeterEcho()
    {
        _altimeterEchoUntil = NowMs() + OwnWriteEchoMs;
        if (_altimeterResting.Pending) _altimeterResting.Rebaseline("");   // a knob turn under way is overtaken by the set
    }

    /// <summary>
    /// One delivery of the altimeter watch: while the app's own set runs, or within its echo window (armed at
    /// the set's start, and again by the MCP sets' machinery when it ends), the value counts as spoken; else
    /// it waits to rest. An unreadable value does nothing.
    /// </summary>
    private void FeedAltimeter(string? phrase, ScreenReaderAnnouncer announcer)
    {
        if (phrase == null) return;
        long now = NowMs();
        bool ownSet = now < _altimeterEchoUntil || _mcpSets.ContainsKey(AltimeterSetKey)
                      || (_ownSetEchoUntil.TryGetValue(AltimeterSetKey, out long until) && now < until);
        if (ownSet)
        {
            _altimeterResting.Rebaseline(phrase);
            return;
        }
        _altimeterResting.Changed(phrase, now);
        if (!_altimeterResting.Pending) return;
        _altimeterAnnouncer = announcer;
        if (!UseAltimeterTimer) return;
        if (_altimeterTimer == null)
        {
            _altimeterTimer = new System.Windows.Forms.Timer { Interval = AltimeterTickMs };
            _altimeterTimer.Tick += (_, _) =>
            {
                if (_altimeterAnnouncer is { } a) ReleaseRestedAltimeter(NowMs(), a);
            };
        }
        if (!_altimeterTimer.Enabled) _altimeterTimer.Start();
    }

    /// <summary>
    /// The release (the timer's tick, or a test with its clock): the altimeter once it has rested, unless the
    /// pilot muted its Ctrl+M row, when it is dropped. Speaks outside MainForm's mute wrap, so the mute is
    /// checked here ([VAR-8]). The timer stops once nothing waits.
    /// </summary>
    internal void ReleaseRestedAltimeter(long nowMs, ScreenReaderAnnouncer announcer)
    {
        if (_altimeterResting.TakeDue(nowMs) is { } phrase && !IsMonitorMuted(Bbs757Annunciators.AltimeterWatchKey))
            announcer.Announce(phrase);
        if (!_altimeterResting.Pending) _altimeterTimer?.Stop();
    }

    // Says when a flight load's values have arrived and gone quiet, from the batch deliveries (the MD-11's gate,
    // IAircraftDefinition.OnSimContextReset: never a wall clock), and every value delivered so far, which the gate
    // is armed with so a forced redelivery of an old value is no evidence.
    private readonly MD11.Md11SeedGate _loadGate = new();
    private readonly Dictionary<string, double> _deliveredValues = new(StringComparer.Ordinal);

    /// <summary>The batches now registered; replaced by tests. Null: the real ones, <see cref="Sim"/>'s.</summary>
    internal Func<IReadOnlyCollection<int>>? ActiveBatches { get; init; }

    /// <summary>A flight load's quiet is still on: a state row's change is tracked, not spoken.</summary>
    internal bool LoadQuietPending => _changes.LoadQuiet;

    /// <summary>
    /// A flight load on a live connection (<see cref="OnSimContextReset"/>): until the batch deliveries show the
    /// new flight's values have arrived and gone quiet (<see cref="OnContinuousBatchDelivered"/>), a state row's
    /// change is tracked, never spoken (the new flight's flaps, gear and lights are no change). The baselines and
    /// the gear legs STAY: the connection kept its value cache and the batch delivers a value only when it
    /// changes, so a row the new flight leaves as it was is not delivered again. With its baseline cleared, its
    /// first real change was taken for the baseline and never said (the parking brake released after loading
    /// parked). A dropped connection clears them (<see cref="ResetReadoutBaselines"/>): the reconnect delivers
    /// every row anew.
    /// </summary>
    private void QuietReadoutBaselines()
    {
        _changes.BeginLoadQuiet();
        _loadGate.Arm(_deliveredValues);
        ResetAltimeterResting();
    }

    /// <summary>Every delivery, for the flight load's gate: a change of one of BlueBird's own variables is the aircraft publishing.</summary>
    private void NoteDelivery(string varName, double value)
    {
        if (_loadGate.Armed)
            _loadGate.NoteValue(varName, value, CountsAsFcuLoadEvidence(GetVariables().GetValueOrDefault(varName)));
        _deliveredValues[varName] = value;
    }

    /// <summary>
    /// A batch has been dispatched: counted toward the end of a flight load's quiet, which ends once every batch has
    /// been delivered, one of BlueBird's own values has changed and nothing has moved for
    /// <see cref="MD11.Md11SeedGate.QuietCycles"/> deliveries of each batch, or <see cref="MD11.Md11SeedGate.CeilingMs"/>
    /// after the first full cycle when nothing of BlueBird's own ever moves.
    /// </summary>
    public override void OnContinuousBatchDelivered(int batchNum)
    {
        base.OnContinuousBatchDelivered(batchNum);
        if (!_loadGate.Armed) return;
        var batches = ActiveBatches != null ? ActiveBatches() : Sim?.ActiveContinuousBatches;
        if (batches == null) return;
        var trigger = _loadGate.OnBatchDelivered(batchNum, batches, NowMs());
        if (trigger == MD11.Md11SeedTrigger.None) return;
        _changes.EndLoadQuiet();
        Log.Debug("BBS757", $"Flight load: state call-outs speak again after {_loadGate.Deliveries} batch deliveries ({trigger}).");
    }

    /// <summary>
    /// A dropped connection: every state row's next delivery is a baseline again (SimConnect's value cache went
    /// with the connection, so the reconnect delivers every row anew). The announced rows' raw values stay: they
    /// are facts, not baselines.
    /// </summary>
    private void ResetReadoutBaselines()
    {
        _changes.Reset();
        _loadGate.Disarm();
        _deliveredValues.Clear();
        _gearLegs.Clear();
        ResetAltimeterResting();
    }

    private void ResetAltimeterResting()
    {
        _altimeterResting.Reset();
        _altimeterEchoUntil = 0;
        _altimeterTimer?.Stop();
    }

    /// <summary>
    /// The Connected branch, which runs AFTER the reconnect's first batch has delivered every row again
    /// (IAircraftDefinition.ResetAnnouncementBaselines): the state rows' baselines, just taken, stay, or the next
    /// real change would be taken for one. The drop cleared them already (<see cref="OnVariableCacheCleared"/>).
    /// Only the altimeter's pending call-out goes.
    /// </summary>
    public override void ResetAnnouncementBaselines()
    {
        base.ResetAnnouncementBaselines();
        ResetAltimeterResting();
    }

    // =================================================================================
    // Status display
    // =================================================================================

    /// <summary>
    /// The rows a value description cannot say: the gear (three legs together), the speedbrake, the
    /// flap angle, the squawk (BCO16), the MCP lights and the engine values.
    /// </summary>
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        // The gap plans' rows first (BBS757Definition.Hooks.cs); the first to set the text wins.
        string? text = null;
        FormatOverheadDisplay(varKey, value, ref text);                  // plan 03, then 12
        if (text == null) FormatRadioDisplay(varKey, value, ref text);   // plan 04
        if (text == null) FormatEfbDisplay(varKey, value, ref text);     // plan 07, then 11
        if (text == null) FormatThrustDisplay(varKey, value, ref text);  // plan 15
        if (text != null)
        {
            displayText = text;
            return true;
        }

        switch (varKey)
        {
            case R.GearNoseKey:
                displayText = R.GearDisplay(R.GearLegs(value,
                    _gearLegs.TryGetValue(R.GearLeftKey, out double l) ? l : null,
                    _gearLegs.TryGetValue(R.GearRightKey, out double r) ? r : null));
                return true;
            case R.FlapsHandleKey:
                displayText = R.FlapDetentName(value) ?? "unavailable";
                return true;
            case R.SpeedbrakeKey:
                displayText = R.SpeedbrakeDisplay(value);
                return true;
            case R.FlapsAngleKey:
                displayText = string.Create(CultureInfo.InvariantCulture, $"{value:0.0} degrees");
                return true;
            case Bbs757Squawk.CodeKey:
                displayText = Bbs757Squawk.Decode(value);
                return true;
        }
        if (Bbs757McpLights.ForKey(varKey) != null)
        {
            displayText = Bbs757McpLights.State(value);
            return true;
        }
        if (Bbs757Annunciators.ForKey(varKey) is { States: { } states })
        {
            displayText = double.IsFinite(value) && states.TryGetValue(Math.Round(value), out string? words) ? words : "unavailable";
            return true;
        }
        if (R.EngineRows.FirstOrDefault(r => r.Key == varKey) is { } engine)
        {
            displayText = R.EngineText(engine, value);
            return true;
        }
        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    // =================================================================================
    // Readouts (output mode)
    // =================================================================================

    /// <summary>The readouts' reads, replaced by tests (a SimConnectManager cannot read in them); null: <see cref="ReadoutIoFor"/>.</summary>
    internal Func<SimConnectManager, McpIo>? ReadoutIo { get; init; }

    /// <summary>
    /// The readouts' reads: a batch-covered row from the cache (at most a second old), anything else
    /// read fresh. Null when it cannot be read.
    /// </summary>
    private McpIo ReadoutIoFor(SimConnectManager sim) => new()
    {
        Read = (key, ct) =>
        {
            if (GetVariables().TryGetValue(key, out var def) && def.UpdateFrequency == UpdateFrequency.Continuous
                && def.IsAnnounced && sim.GetCachedVariableValue(key) is double cached)
                return Task.FromResult<double?>(cached);
            return sim.ReadFreshAsync(key, FreshReadTimeoutMs, ct);
        },
        Write = sim.ExecuteCalculatorCodeUnique,
        Delay = Task.Delay,
    };

    /// <summary>
    /// L, Shift+G, B, F, Shift+F, W and Shift+W: reads and speaks at once. Never throws (fire and
    /// forget from the hotkey); a readout that fails says what could not be read.
    /// </summary>
    internal async Task ReadoutAsync(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;
            var ct = _lifetime.Token;
            var io = ReadoutIo?.Invoke(sim) ?? ReadoutIoFor(sim);
            string said = action switch
            {
                HotkeyAction.ReadFlaps => await R.ReadFlapsAsync(io, ct),
                HotkeyAction.ReadGear => R.GearReadout(await io.Read(R.GearHandleKey, ct), await io.Read(R.GearNoseKey, ct),
                    await io.Read(R.GearLeftKey, ct), await io.Read(R.GearRightKey, ct)),
                HotkeyAction.ReadAltimeter => Bbs757Altimeters.Readout(
                    await io.Read(Bbs757Altimeters.Kohlsman1Key, ct), await io.Read(Bbs757Altimeters.Kohlsman2Key, ct),
                    await io.Read(Bbs757Altimeters.Kohlsman3Key, ct), await io.Read(Bbs757Altimeters.DecisionHeightKey, ct)),
                HotkeyAction.ReadFuelQuantity => await ReadFuelAsync(io, kilograms: false, ct),
                HotkeyAction.ReadFuelInfo => await ReadFuelAsync(io, kilograms: true, ct),
                HotkeyAction.ReadWaypointInfo => R.WeightPhrase(await io.Read(R.TotalWeightKey, ct), kilograms: false),
                HotkeyAction.ReadGrossWeightKg => R.WeightPhrase(await io.Read(R.TotalWeightKey, ct), kilograms: true),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, "not a 757 readout"),
            };
            ct.ThrowIfCancellationRequested();
            announcer.AnnounceImmediate(said);
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{action}: readout failed: {ex.Message}");
            announcer.AnnounceImmediate(ReadoutUnavailable(action));   // a readout never goes silent
        }
    }

    /// <summary>
    /// Plan 06's output-mode keys: Shift+1 to Shift+5 the flap maneuver speeds, Shift+6 VREF 30 and 25, from
    /// the FMC's own VREF while the ADI reader has it (<see cref="FlapSpeedsFromFmc"/>, plan 27), else from the gross
    /// weight read fresh (<see cref="Bbs757FlapSpeeds"/>); D and Shift+D the destination and the top
    /// of climb or descent from the first officer's PROG page 1 (<see cref="Bbs757Prog"/>).
    /// </summary>
    partial void HandleReadoutHotkey(HotkeyAction action, SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        Form parentForm, HotkeyManager hotkeyManager, ref bool handled)
    {
        if (action is HotkeyAction.ReadDistanceToDest or HotkeyAction.ReadDistanceToTOD)
        {
            _ = ReadProgAsync(action == HotkeyAction.ReadDistanceToDest, announcer);
            handled = true;
            return;
        }
        int index = FlapSpeedIndex(action);
        if (index < 0) return;
        // The FMC's own VREF while the ADI reader has it (plan 27); else the measured table by the weight read fresh.
        if (FlapSpeedsFromFmc(index) is { } fromFmc)
            announcer.AnnounceImmediate(fromFmc);
        else
            _ = FlapManeuverSpeeds.AnnounceAsync(simConnect, announcer, R.TotalWeightKey, "BBS757",
                lbs => Bbs757FlapSpeeds.Describe(lbs, index));
        handled = true;
    }

    /// <summary>
    /// D (<paramref name="destination"/>) or Shift+D: reads PROG page 1 on the first officer's CDU and speaks
    /// its line, or <see cref="Bbs757Prog.NoCdu"/>. Never throws (fire and forget from the hotkey).
    /// </summary>
    private async Task ReadProgAsync(bool destination, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;
            var ct = _lifetime.Token;
            var screen = await Bbs757Prog.ReadPageOneAsync(CduPages, ct);
            ct.ThrowIfCancellationRequested();
            announcer.AnnounceImmediate(screen == null ? Bbs757Prog.NoCdu
                : destination ? Bbs757Prog.DestinationPhrase(Bbs757Prog.Destination(screen))
                : Bbs757Prog.TopPhrase(Bbs757Prog.TopOf(screen)));
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"PROG readout failed: {ex.Message}");
            announcer.AnnounceImmediate(Bbs757Prog.NoCdu);
        }
    }

    /// <summary>
    /// The flap speed readout for <paramref name="scheduleIndex"/> from the FMC's own VREF 30 and VREF 25, which the ADI
    /// reader's newest good reading carries whatever the display's power (plan 27; the owner's word, 2026-11-16), or null:
    /// the reader not running, its reference speeds part missing or its read lost, or a VREF not above 0 (PERF INIT not
    /// done, a weight outside the FMC's table). On null the calculated readout stays as it was.
    /// </summary>
    internal string? FlapSpeedsFromFmc(int scheduleIndex) =>
        Adi.Newest is { } reading ? Bbs757FlapSpeeds.DescribeFromFmc(reading.Vref30, reading.Vref25, scheduleIndex) : null;

    /// <summary>The schedule index of a flap speed key (Shift+1 to Shift+6), or -1.</summary>
    internal static int FlapSpeedIndex(HotkeyAction action) => action switch
    {
        HotkeyAction.ReadSpeedGD => 0,
        HotkeyAction.ReadSpeedS => 1,
        HotkeyAction.ReadSpeedF => 2,
        HotkeyAction.ReadSpeedVLS => 3,
        HotkeyAction.ReadSpeedVS => 4,
        HotkeyAction.ReadSpeedVFE => 5,
        _ => -1,
    };

    private static async Task<string> ReadFuelAsync(McpIo io, bool kilograms, CancellationToken ct)
        => R.FuelPhrase(
            await io.Read(SimConnect.BBS757.Bbs757Loadsheet.LeftFuelKey, ct),
            await io.Read(SimConnect.BBS757.Bbs757Loadsheet.RightFuelKey, ct),
            await io.Read(SimConnect.BBS757.Bbs757Loadsheet.CenterFuelKey, ct),
            await io.Read(SimConnect.BBS757.Bbs757Loadsheet.FuelDensityKey, ct),
            kilograms);

    private static string ReadoutUnavailable(HotkeyAction action) => action switch
    {
        HotkeyAction.ReadFlaps => R.FlapsPhrase(null),
        HotkeyAction.ReadGear => R.GearPhrase(null),
        HotkeyAction.ReadAltimeter => Bbs757Altimeters.Readout(null, null, null, null),
        HotkeyAction.ReadFuelQuantity or HotkeyAction.ReadFuelInfo => "Fuel unavailable",
        _ => "Gross weight unavailable",
    };
}
