using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Panels: the structure and rows come from <see cref="A300PanelLayout"/> (the only source of panel
/// order); each panel's status display lists its lights and then its readouts.
/// </summary>
public partial class IniA300Definition
{
    private Dictionary<string, List<string>>? _panelStructure;
    private Dictionary<string, List<string>>? _panelControls;
    private Dictionary<string, List<string>>? _panelDisplays;

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        BuildPanelsOnce();
        return _panelStructure!;
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        BuildPanelsOnce();
        return _panelControls!;
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        BuildPanelsOnce();
        return _panelDisplays!;
    }

    private void BuildPanelsOnce()
    {
        if (_panelStructure != null)
            return;

        var structure = new Dictionary<string, List<string>>();
        foreach (var (section, panels) in _placement.Structure)
        {
            structure[section] = new List<string>(panels);
            if (section == A300DisplayPanels.AfterSection)
                structure[A300DisplayPanels.Section] = new List<string>(A300DisplayPanels.Panels);
        }

        // Every panel gets a controls entry, even an empty one: a panel with only a status
        // display is otherwise never built (the HS787 lesson).
        var controls = new Dictionary<string, List<string>>();
        foreach (var (panel, rows) in _placement.RowsByPanel)
            controls[panel] = rows.Select(r => r.Key).ToList();

        var displays = new Dictionary<string, List<string>>();
        foreach (var lamp in A300Announcements.Lamps)
            Add(displays, lamp.Panel, lamp.Key);
        foreach (var lamp in A300PanelLamps.Resolved)
            Add(displays, lamp.Lamp.Panel, lamp.Lamp.Key);
        foreach (var (panel, lines) in A300DisplayPanels.SystemLines)
            foreach (var key in lines)
                Add(displays, panel, key);
        // A readout whose variable the panel's ECAM page already reads is not repeated (total fuel: the page's
        // follows the tablet's weight unit; the pounds readout stays registered for the fuel hotkey).
        foreach (var readout in A300Readouts.All)
            if (!A300DisplayPanels.IsDisplayPanel(readout.Panel) && !ShownBySystemLines(readout))
                Add(displays, readout.Panel, readout.Key);
        Add(displays, A300Trp.Panel, A300Trp.ModeKey);   // the TRP line ([A300-21])
        foreach (var panel in A300DisplayPanels.Panels)
        {
            controls[panel] = new List<string>();
            displays[panel] = new List<string>(A300DisplayPanels.Lines[panel]);
        }

        _panelStructure = structure;
        _panelControls = controls;
        _panelDisplays = displays;

        static void Add(Dictionary<string, List<string>> map, string panel, string key)
        {
            if (!map.TryGetValue(panel, out var list))
                map[panel] = list = new List<string>();
            if (!list.Contains(key))   // a readout a system panel's lines already list
                list.Add(key);
        }
    }

    private bool ShownBySystemLines(A300Readout readout) =>
        A300DisplayPanels.SystemLines.TryGetValue(readout.Panel, out var lines)
        && lines.Any(key => _readouts.TryGetValue(key, out var line) && line.Key != readout.Key
                            && string.Equals(line.Var, readout.Var, StringComparison.OrdinalIgnoreCase));

    /// <summary>A light-labeled button's label state, from its lamp in the cache ("Heading select: On",
    /// "Autobrake low: Armed", "TRP climb: On"; <see cref="TryButtonLabel"/>), and a fault light's status box, as
    /// the cockpit shows it: "On" only while its bus's light power is on too ([A300-23]).</summary>
    public override bool TryDescribeControlState(string varKey, out string stateText)
    {
        if (LampStatus(varKey) is string lampState)
        {
            stateText = lampState;
            return true;
        }
        if (_sim is { } sim && TryButtonLabel(varKey, out _, out var describe)
            && describe(key => ReadInput(sim, key)) is string label)
        {
            stateText = label;
            return true;
        }
        return base.TryDescribeControlState(varKey, out stateText);
    }

    /// <summary>
    /// The one table of light-labeled buttons, an entry per kind (an FCU mode button, an autobrake button, a TRP button,
    /// a button a cockpit light labels, a clock button, a cargo door angle button): the keys its label reads, and the
    /// label composed from them. The keys are the button's <c>StateVariables</c>, so the open panel repaints the label on
    /// every key listed, and <paramref name="describe"/> is the label itself (null while an input it needs is unread).
    /// Keys and reads cannot drift apart by construction only for a button a cockpit light labels: both are the board
    /// lamp's own inputs (<see cref="A300LampBoard"/>), whatever its rule reads, and the ECAM page buttons read the ECAM
    /// status as well as their light and its bus's power. For the other kinds each entry states both, and
    /// IniA300DisplayPanelTests pins them together: every key a label asks the cache for is one the button repaints on,
    /// and every key it repaints on is registered. A key a label reads that another key streams is read from that key
    /// (<see cref="ReadInput"/>) and listed as that key (<see cref="RepointStateVariables"/>).
    /// </summary>
    private static bool TryButtonLabel(string rowKey, out IReadOnlyList<string> keys,
        out Func<Func<string, double?>, string?> describe)
    {
        if (A300FcuState.ByButton.TryGetValue(rowKey, out var light))
        {
            keys = new[] { light.Key };
            describe = read => read(light.Key) is double value ? A300FcuState.Describe(light, value) : null;
            return true;
        }
        if (A300Autobrake.ByButton.TryGetValue(rowKey, out var autobrake))
        {
            keys = new[] { A300Autobrake.LevelKey, autobrake.DecelKey, A300LampBoard.AcPowerKey };
            describe = read => A300Autobrake.Describe(autobrake, read(A300Autobrake.LevelKey), read(autobrake.DecelKey),
                read(A300LampBoard.AcPowerKey));
            return true;
        }
        if (A300Trp.ModeByButton.TryGetValue(rowKey, out var trpMode))
        {
            keys = new[] { A300Trp.ModeKey };
            describe = read => A300Trp.ButtonState(trpMode, read(A300Trp.ModeKey));
            return true;
        }
        if (A300PanelLamps.ByButton.TryGetValue(rowKey, out var lampKey) && A300LampBoard.ById.TryGetValue(lampKey, out var lamp))
        {
            keys = lamp.Inputs;
            describe = read => LampWords(lamp, read);
            return true;
        }
        if (A300Clock.ButtonStates.TryGetValue(rowKey, out var clock))
        {
            string stateKey = A300Clock.StateKey(rowKey);
            keys = new[] { stateKey };
            describe = read => read(stateKey) is double state && clock.Words.TryGetValue(Math.Round(state), out var word) ? word : null;
            return true;
        }
        if (A300CargoDoor.AngleButtons.TryGetValue(rowKey, out var angle))
        {
            keys = new[] { angle.Key };
            describe = read => read(angle.Key) is double picked ? A300CargoDoor.AngleLabel(picked) : null;
            return true;
        }
        keys = Array.Empty<string>();
        describe = _ => null;
        return false;
    }

    /// <summary>"On" or "Off" for a status box light as the cockpit shows it ([A300-23], [A300-25]), or null
    /// when it is not a light or an input is still unread.</summary>
    private string? LampStatus(string varKey) =>
        A300LampBoard.StatusLampKeys.Contains(varKey) && _sim is { } sim
            ? LampWords(A300LampBoard.ById[varKey], key => Cached(sim, key))
            : null;

    /// <summary>"On" or "Off" for a light as the cockpit shows it, from <paramref name="read"/>; null while an
    /// input its rule needs is unread.</summary>
    private static string? LampWords(A300BoardLamp lamp, Func<string, double?> read) =>
        A300LampBoard.IsLit(lamp, read) is bool lit ? lit ? "On" : "Off" : null;

    /// <summary>A readout's value as the status display shows it ("1013 hectopascals, 29.92 inches").
    /// The speed window reads as Mach while SPD/MACH is in Mach.</summary>
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        // The status list composes its lines here (MainForm.UpdateDisplayText), so a light reads as the
        // cockpit shows it, not its first variable's raw value.
        if (LampStatus(varKey) is string lamp)
        {
            displayText = lamp;
            return true;
        }
        if (TryGetDisplayText(varKey, value) is string text)
        {
            displayText = text;
            return true;
        }
        if (varKey == A300Readouts.SpeedKey)
        {
            displayText = A300FcuState.SpeedWindow(value, IsMach());
            return true;
        }
        if (_readouts.TryGetValue(varKey, out var readout))
        {
            displayText = readout.Format(value);
            return true;
        }
        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    /// <summary>Whether the tablet's weight unit is kilograms, from the cache. Unknown reads
    /// kilograms, the unit the tank weights are stored in.</summary>
    private bool IsMetric() =>
        _sim is not { } sim || Cached(sim, A300EcamPages.WeightUnitKey) is not double v || v >= 0.5;

    /// <summary>What the commanded-mode hold keeps the speed mode under (<see cref="A300CommandedState"/>); in the
    /// key space of <see cref="CommandKey"/>, where a row's state variable would be the same string.</summary>
    private const string SpeedModeCommand = "L:INI_Airspeed_is_mach";

    /// <summary>
    /// The SPD/MACH mode, 1 for Mach: the mode MSFSBA just commanded while that is fresh (a typed speed's flip, or
    /// the SPD/MACH button), else the cache; null while unread. The mode comes from the 1 Hz batch, which still
    /// shows the old mode for up to a second after a switch, and a switch FLIPS, so a decision made from the old mode
    /// flips it back.
    /// </summary>
    private double? SpeedMode() =>
        _commanded.Resolve(SpeedModeCommand, _sim is { } sim ? Cached(sim, A300FcuState.SpeedMachLightKey) : null, Clock());

    /// <summary>Whether the FCU speed window is in Mach (<see cref="SpeedMode"/>; false when unknown).</summary>
    private bool IsMach() => SpeedMode() is double v && v >= 0.5;

    /// <summary>The Displays section's lines that need more than their own value: the FMA columns,
    /// the attitude in words, and the speeds the tape shows only at some flap settings.</summary>
    private string? TryGetDisplayText(string key, double value)
    {
        switch (key)
        {
            case A300FmaSources.ThrustModeKey:
            case A300FmaSources.PitchModeKey:
            case A300FmaSources.RollModeKey:
            case A300FmaSources.ArmedKey:
                return FmaLine(key);
            case "PLANE_PITCH_DEGREES":
                return A300DisplayText.Pitch(value);
            case "PLANE_BANK_DEGREES":
                return A300DisplayText.Bank(value);
            case "INDICATED_ALTITUDE":
                return A300DisplayText.Feet(value);
            case "GROUND_VELOCITY":
                return A300DisplayText.Knots(value);
            case A300TakeoffCallouts.V1Key:
            case A300TakeoffCallouts.VrKey:
                return A300TakeoffCallouts.Speed(value);
            case A300TakeoffCallouts.V2Key:
                return A300TakeoffCallouts.V2(_sim is { } vSim && Cached(vSim, A300TakeoffCallouts.VrKey) is double vr ? vr : 0, value);
            case A300Trp.ModeKey:
                return _sim is { } trpSim
                    ? A300Trp.Line(value, Cached(trpSim, A300Trp.AutoModeKey), Cached(trpSim, A300Readouts.FlexTemperatureKey),
                        Cached(trpSim, A300Trp.N1LimitKey), Cached(trpSim, A300Trp.PwEnginesKey))
                    : A300Trp.Line(value, null, null, null, null);
        }
        if (A300Clock.DigitKeysByLine.TryGetValue(key, out var digitKeys))
        {
            var digits = digitKeys.Select(k => _sim is { } clockSim ? Cached(clockSim, k) : null).ToArray();
            return digits.All(d => d.HasValue) ? A300Clock.Time(digits[0]!.Value, digits[1]!.Value, digits[2]!.Value, digits[3]!.Value) : null;
        }
        if (A300Radios.ByLine.TryGetValue(key, out var line))
        {
            if (_sim is not { } radioSim || Cached(radioSim, line.Radio.Window1Key) is not double w1 || Cached(radioSim, line.Radio.Window2Key) is not double w2)
                return null;
            return line.Active ? line.Radio.Active(w1, w2, value) : line.Radio.Standby(w1, w2, value);
        }
        if (A300IrsAlignment.All.FirstOrDefault(l => l.LineKey == key) is { } irs)
            return _sim is { } irsSim && ReadInput(irsSim, irs.AligningKey) is double aligning && ReadInput(irsSim, irs.AlignedKey) is double aligned
                ? A300IrsAlignment.Status(value, aligning, aligned)
                : null;
        if (key == A300EwdMemos.LineKey)
            return _memoLine ?? "unavailable";
        if (key == A300CargoDoor.StatusKey)
        {
            var flags = A300CargoDoor.Flags.Select(f => _sim is { } doorSim ? Cached(doorSim, f.Key) : null).ToArray();
            return flags.All(f => f.HasValue)
                ? A300CargoDoor.Status(value, flags[0]!.Value, flags[1]!.Value, flags[2]!.Value, flags[3]!.Value)
                : null;
        }
        if (A300Radios.BearingLines.TryGetValue(key, out var bearing))
            return _sim is { } adfSim && Cached(adfSim, bearing.SignalKey) is double signal ? A300Radios.Bearing(signal, value) : null;
        if (key == A300IlsDeviation.LocalizerKey)
            return _sim is { } locSim
                ? A300IlsDeviation.Localizer(ReadInput(locSim, A300IlsDeviation.NavSelectorKey), ReadInput(locSim, A300IlsDeviation.LocalizerValidKey), value)
                : null;
        if (key == A300IlsDeviation.GlideslopeKey)
            return _sim is { } gsSim
                ? A300IlsDeviation.Glideslope(ReadInput(gsSim, A300IlsDeviation.NavSelectorKey), ReadInput(gsSim, A300IlsDeviation.GlideslopeReceivedKey), value)
                : null;
        if (key == A300Trim.RudderKey)
        {
            var display = A300Trim.RudderDigitKeys.Select(k => _sim is { } trimSim ? Cached(trimSim, k) : null).ToArray();
            return display.All(d => d.HasValue) ? A300Trim.Rudder(display[0]!.Value, display[1]!.Value, display[2]!.Value, value) : null;
        }
        if (A300EcamPages.KilogramKeys.Contains(key))
            return A300EcamPages.Weight(value, IsMetric());
        if (A300EcamPages.PoundsPerHourKeys.Contains(key))
            return A300EcamPages.FlowPerHour(value, IsMetric());
        if (A300Readouts.FlapSpeeds.TryGetValue(key, out var speed))
            return A300DisplayText.FlapSpeed(speed, value, _sim is { } sim ? Cached(sim, A300Levers.FlapsKey) : null);
        return null;
    }

    /// <summary>One FMA line, read fresh from the cache. A blank column reads "blank"; the whole FMA
    /// reads "not shown" while the PFD draws no guidance columns. The armed line carries the
    /// autopilot column beneath it.</summary>
    private string FmaLine(string key)
    {
        var fma = CurrentFma(_sim);
        string Column(string? words) => !fma.IsShown ? "not shown" : words ?? "blank";
        return key switch
        {
            A300FmaSources.ThrustModeKey => Column(fma.Thrust),
            A300FmaSources.PitchModeKey => Column(fma.Common ?? fma.Pitch),
            A300FmaSources.RollModeKey => Column(fma.Common ?? fma.Roll),
            _ => $"{(!fma.IsShown ? "not shown" : fma.Armed.Count == 0 ? "none" : string.Join(", ", fma.Armed))}\nAutopilot: {fma.Autopilot ?? "off"}",
        };
    }
}
