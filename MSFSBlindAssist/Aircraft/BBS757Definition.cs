using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// BlueBird Simulations 757-200 (package <c>bluebirdsim-aircraft-757-200</c>, release 0.1.0).
///
/// ═══ THE CONTROL MAP ═══
///
/// The panels are not hand-written. <c>tools/b757-gen/generate_b757_map.py</c> reads the
/// aircraft's own cockpit XML and emits <c>bbs757_control_map.json</c> (embedded, loaded by
/// <see cref="Bbs757ControlMap"/>): every regular control's node, panel, spoken label, named
/// positions and the vendor's click code. This class turns each row into one variable keyed by its
/// node and each panel into a panel, in map order. Regenerate the JSON after a BlueBird update
/// rather than patching C#.
///
/// ═══ HOW IT WRITES ═══
///
/// Every control write replays the control's OWN click code through the MobiFlight calculator
/// (<see cref="SimConnectManager.ExecuteCalculatorCodeUnique"/>, the unique prefix of [SIM-17]):
/// the cockpit click updates every var the cockpit updates (the battery click sets both its state
/// var and its <c>_ON</c> legend), so replaying it keeps parity. Before any write the definition
/// asks <see cref="SimConnectManager.CalcWriteCanLand"/> and, when it is false, says
/// "{label} unavailable" and writes nothing (the MD-11's [MD11-16] pattern). It registers
/// <c>MSFSBA_BRIDGE_PROBE</c> so the calc path is verified at all. The writes themselves are in
/// the Interaction partial.
///
/// ═══ BBS NEST ═══
///
/// BlueBird's systems do not run without its companion app; see <see cref="Bbs757Nest"/>. The
/// definition says so once per connection and once per flight load when the process is missing.
///
/// ═══ THE DATA-DEFINITION BUDGET ═══
///
/// Every control is an OnRequest L:var read on demand (one individual data definition each,
/// about 420), the status rows ride the continuous batch or their own on-request read, and the
/// connection's ceiling is about 1000 (docs/architecture.md). The flight-control rows, the 14 MCP
/// lights and the EXEC light ride the batch; the engine rows, the flap angle, the squawk, the
/// altimeters, the decision height, the gear lever, the gross weight and the A/T's armed flag are 19
/// individual definitions (about 500 variables in all). Bbs757DefinitionTests keeps the total under 900.
///
/// ═══ VERIFY IN SIM ═══
///   • A panel pick moves the control and the combo then shows where it really is.
///   • A button press is heard ("… pressed") and the aircraft reacts (MCP mode lights).
///   • With BBS Nest closed, the warning speaks once after connecting; after Nest starts and the
///     flight is reloaded, nothing is said.
///   • Ctrl+H/S/A/V set the MCP heading, speed (knots), altitude and vertical speed, and what is
///     spoken afterwards is the MCP's own value; their mode buttons press as the panel does and say
///     the light once; Shift+H/S/A/V read the same values (Bbs757Mcp).
///   • Flaps, gear, speedbrake, parking brake, the MCP lights and the EXEC light speak their changes,
///     and the trim speaks BlueBird's units while it moves; L, Shift+G, B, F, Shift+F, W and
///     Shift+W read them; Ctrl+B sets the altimeters and the radio minimums,
///     Ctrl+P is the autopilot window, the transponder panel takes a squawk right after its mode knob; the
///     Pedestal's Engines panel shows the stock engine values beside its switches (all in
///     BBS757Definition.Readouts.cs, .Altimeter.cs, .Autopilot.cs).
///   • On the RR the Engines panel lists only the RR fuel control switches; on the PW only the PW ones, and the
///     Overhead has no "Electronic engine control (RR)" panel (plan 12 task 8, from the aircraft title).
/// </summary>
public partial class BBS757Definition : BaseAircraftDefinition
{
    public override string AircraftName => "BlueBird 757-200";
    public override string AircraftCode => "BBS_757";

    /// <summary>The section and panel the read-only status rows are shown under.</summary>
    public const string StatusSection = "Status";

    /// <summary>The calc-path probe target the FBW defs and the MD-11 register; see <see cref="BuildVariables"/>.</summary>
    public const string BridgeProbeKey = "MSFSBA_BRIDGE_PROBE";

    /// <summary>The generated control map this profile is built from.</summary>
    internal Bbs757ControlMap Map { get; }

    /// <summary>
    /// The SimConnect manager, captured by the first panel build, panel write or hotkey that hands one in
    /// (<see cref="IsPanelControlVisible"/>, <see cref="HandleUIVariableSet"/>, <see cref="HandleHotkeyAction"/>);
    /// ProcessSimVarUpdate is not handed one. Null until then.
    /// </summary>
    internal SimConnectManager? Sim { get; private set; }

    /// <summary>Map controls by node, the variable key.</summary>
    private readonly Dictionary<string, Bbs757Control> _byNode = new(StringComparer.Ordinal);

    public BBS757Definition() : this(Bbs757ControlMap.Load()) { }

    internal BBS757Definition(Bbs757ControlMap map)
    {
        Map = map;
        foreach (var c in map.Controls)
            if (!_byNode.TryAdd(c.Node, c))
                Log.Warn("BBS757", $"Control map lists node {c.Node} twice; the second row is ignored.");
    }

    // The MCP takes exact values (docs/b757.md, "The MCP value windows"): Ctrl+H/S/A/V open the entry windows
    // (BBS757Definition.Mcp.cs), Shift+H/S/A/V read the MCP's own variables.
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    public override Dictionary<string, string> GetButtonStateMapping() => new();

    /// <summary>
    /// The 757 plays its own pilot-monitoring 80 knots, V1 and rotate (docs/b757.md, "Take-off callouts"), so
    /// Takeoff Assist's "80 knots" is not spoken on it; its "100 knots", which BlueBird does not call, is.
    /// </summary>
    public override bool PlaysOwnTakeoffSpeedCallouts => true;

    /// <summary>
    /// BlueBird's VOR and ILS receivers are its own: tuning them changed no stock NAV variable
    /// (measured, [B757-5]), so the shared glideslope call-out, which reads NAV 1,
    /// is off on the 757 (ruling R56).
    /// </summary>
    public override bool UsesStockNavReceivers => false;

    /// <summary>
    /// The AI vision reads on the measured instrument views: Alt+I the standby instruments, always; Alt+P / Alt+N /
    /// Alt+E / Alt+S the ADI, the EHSI and the upper and lower EICAS only as the fallback of their memory readers'
    /// windows, while a reader cannot run (docs/b757.md, "AI display reads"). The readers' keys go to the display
    /// registry first; <c>BaseAircraftDefinition.HandleHotkeyAction</c> dispatches the table.
    /// </summary>
    protected override IReadOnlyList<AiDisplayRead> DisplayReads => Bbs757DisplayReads.All;

    // =================================================================================
    // Variables
    // =================================================================================

    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = GetBaseVariables();

        foreach (var c in _byNode.Values)
        {
            if (vars.ContainsKey(c.Node)) continue;   // never shadow a base var
            vars[c.Node] = ControlVariable(c);
        }

        foreach (var row in Map.Status)
        {
            if (vars.ContainsKey(row.Key)) continue;
            vars[row.Key] = StatusVariable(row);
        }

        // End-to-end MobiFlight probe target, as the MD-11 registers it. MainForm calc-writes a
        // nonce here and reads it back over the data-def path; a match is the only signal that the
        // WASM module runs our RPN, and every 757 control write is a calculator write. Registering
        // it is what opts the 757 into the probe, its verdict (CalcWriteCanLand) and the spoken
        // warning when the round trip never succeeds. OnRequest, in no panel, never announced.
        vars[BridgeProbeKey] = new SimVarDefinition
        {
            Name = BridgeProbeKey, DisplayName = "Bridge Probe",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
        };

        // The MCP values the entry windows and readouts read fresh; see Bbs757Mcp.
        AddMcpVariables(vars);

        // The A/T's armed flag the Ctrl+P window shows; see BBS757Definition.Autopilot.cs.
        AddAutopilotVariables(vars);

        // The tanks and payload stations the EFB window's SimBrief load reads back; see Bbs757Loadsheet.
        // F and Shift+F read the same four.
        AddLoadsheetVariables(vars);

        // Flaps, gear, speedbrake, parking brake, the MCP lights, the engines, the altimeters, the
        // radio minimums, the gross weight and the squawk; see BBS757Definition.Readouts.cs.
        AddReadoutVariables(vars);

        // The gap plans' variables (BBS757Definition.Hooks.cs); each call compiles away until its plan lands.
        AddOverheadVariables(vars);       // plan 03, then 12
        AddRadioVariables(vars);          // plan 04
        AddEfbVariables(vars);            // plan 07, then 11
        AddThrustVariables(vars);         // plan 15
        AddFirstOfficerVariables(vars);   // plan 10

        // The display readers' simulator inputs, batched; last, so it sees every other registration (BBS757Definition.Displays.cs).
        AddDisplayVariables(vars);        // plan 23

        Log.Info("BBS757", $"Built {vars.Count} variables from {Map.Controls.Count} controls and {Map.Status.Count} status rows.");
        return vars;
    }

    /// <summary>
    /// One map control → one OnRequest L:var keyed by its node. A button or held button renders as
    /// a button (its press is a momentary click, never a position). A button with no state var is
    /// keyed on a read of its node's own name: harmless (an unknown L:var reads 0), and it gives the
    /// press's re-read something to ask for.
    /// </summary>
    private static SimVarDefinition ControlVariable(Bbs757Control c)
    {
        bool momentary = Bbs757Write.IsMomentary(c);
        return new SimVarDefinition
        {
            Name = string.IsNullOrWhiteSpace(c.StateVar) ? c.Node : c.StateVar,
            DisplayName = c.Label,
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            ValueDescriptions = DescriptionsFor(c.Positions),
            RenderAsButton = momentary,
            // A momentary push has no meaningful resting value (CORE-10's opt-in).
            SuppressRestingButtonState = momentary,
        };
    }

    /// <summary>
    /// One status row → a read-only row on the Status display. An announced row rides the continuous
    /// batch and keeps its Ctrl+M row; a silent one (bus sources, APU speed, cabin altitude: bare
    /// numbers) is read while the display is shown and has no Ctrl+M row, which would mute nothing.
    /// </summary>
    private static SimVarDefinition StatusVariable(Bbs757StatusRow row) => new()
    {
        Name = row.Key,
        DisplayName = row.Label,
        Type = SimVarType.LVar,
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = row.Announce,
        ValueDescriptions = DescriptionsFor(row.Positions),
        RenderAsReadOnlyStatus = true,
        ExcludeFromMonitorManager = !row.Announce,
    };

    /// <summary>
    /// Registers the stock tank, fuel-density and payload-station variables the SimBrief load reads
    /// back (<see cref="SimConnect.BBS757.Bbs757Loadsheet.ReadBackVariables"/>), each under its own
    /// key: <c>ReadFreshAsync</c> delivers only a registered key. OnRequest, never announced, no
    /// Ctrl+M row ([VAR-9]) and on no panel; eight individual definitions.
    /// </summary>
    private static void AddLoadsheetVariables(Dictionary<string, SimVarDefinition> vars)
    {
        foreach (var v in SimConnect.BBS757.Bbs757Loadsheet.ReadBackVariables)
        {
            if (vars.ContainsKey(v.Key)) continue;   // never shadow a map control or a base var
            vars[v.Key] = new SimVarDefinition
            {
                Name = v.SimVar, DisplayName = v.Label, Units = v.Units,
                Type = SimVarType.SimVar, UpdateFrequency = UpdateFrequency.OnRequest,
                ExcludeFromMonitorManager = true,
            };
        }
    }

    private static Dictionary<double, string> DescriptionsFor(IEnumerable<Bbs757Position> positions)
    {
        var d = new Dictionary<double, string>();
        foreach (var p in positions) d.TryAdd(p.Value, p.Label);
        return d;
    }

    // =================================================================================
    // Panels
    // =================================================================================

    private Dictionary<string, List<string>>? _panelStructure;
    private Dictionary<string, List<string>>? _panelControls;
    private Dictionary<string, List<string>>? _panelDisplays;

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        BuildPanelsOnce();
        FollowEngineType();   // plan 12 task 8: a panel the engine type empties is not listed
        return _panelStructure!;
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        BuildPanelsOnce();
        return _panelControls!;
    }

    /// <summary>The status rows, as the Status display of their panel ("Status" today).</summary>
    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        BuildPanelsOnce();
        return _panelDisplays!;
    }

    /// <summary>
    /// Sections and panels from the map, in map order, then the status rows. The map's order is the
    /// order the pilot tabs through them: each panel is one panel of the cockpit (plan 12;
    /// docs/b757.md, "Sections, panels and their order"), a section's panels follow the first action on each of them
    /// in BlueBird's Simplified Procedures (plan 12 task 9, the same section), with
    /// the Forward panel pinned to open with Brakes and the Pedestal's start (Control stand, Engines,
    /// TCAS and transponder) pinned by plan 01's test, and a panel's controls come every-day first (the
    /// generator's panel_order and control_order; whatever they leave out follows alphabetically, or in
    /// cockpit file order). A status row whose panel is a
    /// control panel joins that panel's Status display; any other panel is a display-only panel under
    /// the Status section, with an empty controls entry so the panel builds at all ([HS-5]). Panels are
    /// keyed by name, so no two sections may share one.
    /// </summary>
    private void BuildPanelsOnce()
    {
        if (_panelStructure != null) return;

        var structure = new Dictionary<string, List<string>>();
        var controls = new Dictionary<string, List<string>>();
        var sectionOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in _byNode.Values)
        {
            if (!controls.TryGetValue(c.Panel, out var keys))
            {
                controls[c.Panel] = keys = new List<string>();
                sectionOf[c.Panel] = c.Section;
                if (!structure.TryGetValue(c.Section, out var panels)) structure[c.Section] = panels = new List<string>();
                panels.Add(c.Panel);
            }
            else if (sectionOf[c.Panel] != c.Section)
            {
                Log.Warn("BBS757", $"Panel {c.Panel} appears in {sectionOf[c.Panel]} and {c.Section}; {c.Node} is listed under {sectionOf[c.Panel]}.");
            }
            keys.Add(c.Node);
        }

        var displays = new Dictionary<string, List<string>>();
        foreach (var row in Map.Status)
        {
            string panel = string.IsNullOrWhiteSpace(row.Panel) ? StatusSection : row.Panel;
            if (!displays.TryGetValue(panel, out var rows)) displays[panel] = rows = new List<string>();
            rows.Add(row.Key);
            if (controls.ContainsKey(panel)) continue;
            controls[panel] = new List<string>();
            if (!structure.TryGetValue(StatusSection, out var statusPanels)) structure[StatusSection] = statusPanels = new List<string>();
            statusPanels.Add(panel);
        }

        // The app's own rows and fields, after the map's (BBS757Definition.Readouts.cs).
        AddReadoutPanels(structure, controls, displays);

        // The gap plans' panels and fields (BBS757Definition.Hooks.cs).
        AddOverheadPanels(structure, controls, displays);   // plan 03, then 12
        AddRadioPanels(structure, controls, displays);      // plan 04
        AddEfbPanels(structure, controls, displays);        // plan 07, then 11
        AddThrustPanels(structure, controls, displays);     // plan 15

        // Plan 12 task 8: every section's panels, kept; the lists handed out lose a panel the engine type empties
        // (FollowEngineType). The control lists stay whole: IsPanelControlVisible decides a panel's controls.
        _allPanelStructure = structure.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        _panelControls = controls;
        _panelDisplays = displays;
        _panelStructure = structure;
    }

    // =================================================================================
    // The engine type (plan 12 task 8; the owner approved this change to the core file)
    // =================================================================================

    // The section lists as BuildPanelsOnce built them, every panel in them.
    private Dictionary<string, List<string>>? _allPanelStructure;

    /// <summary>The engine type the section lists follow now: Unknown, every panel, until a title names one.</summary>
    internal Bbs757EngineType ShownEngineType { get; private set; }

    /// <summary>The loaded aircraft's title; replaced by tests. Null: the real one, <see cref="Sim"/>'s TITLE.</summary>
    internal Func<string?>? AircraftTitle { get; init; }

    private Bbs757EngineType LoadedEngineType() =>
        Bbs757Variant.FromTitle(AircraftTitle != null ? AircraftTitle() : Sim?.CurrentAircraftTitle);

    /// <summary>
    /// THE mechanism that decides a panel's controls (the Fenix's and the PMDG 777's way): MainForm asks this of every
    /// control of a panel as it builds it, handing in its SimConnectManager. A map control of the other engine type's
    /// cockpit is left out (on an RR the PW fuel control switches, on a PW the RR ones and the RR's engine control
    /// switches and guards); anything else, and everything while the type is unknown, is shown. It reads the live title
    /// on every build, so a flight or aircraft load is followed by the next panel built, with no section change; the
    /// panels' control lists stay whole. Right after a connection the definition holds no SimConnectManager until a
    /// panel build hands it one here; it keeps it in <see cref="Sim"/> for <see cref="FollowEngineType"/>. It never
    /// rewrites a list: MainForm is walking one while it asks.
    /// </summary>
    public override bool IsPanelControlVisible(string varKey, SimConnectManager? simConnect)
    {
        if (simConnect != null) Sim = simConnect;
        return !_byNode.TryGetValue(varKey, out var control) || Bbs757Variant.IsFitted(control.Variant, LoadedEngineType());
    }

    /// <summary>
    /// Hands the definition the SimConnectManager at startup and on a switch to the 757 (MainForm, as the MD-11's
    /// Attach), so <see cref="FollowEngineType"/> can read the loaded aircraft's title for the first section list
    /// built after connecting. Without it <see cref="Sim"/> stays null until a panel is built, and a PW's first
    /// section list still names the RR-only panel.
    /// </summary>
    public void Attach(SimConnectManager sim) => Sim = sim;

    /// <summary>
    /// The section lists only: when the loaded engine type has changed, leaves out of them, in place, a panel the type
    /// empties (<see cref="Bbs757Variant.RewriteSections"/>: on a PW the RR's Electronic engine control panel), and an
    /// unknown type brings every panel back. A panel with an app field or a status display stays. MainForm calls
    /// GetPanelStructure, and so this, each time the pilot opens a section, before it lists the section's panels, so a
    /// section listed while the title named another type is right from the next section opened. Which controls a panel
    /// shows is <see cref="IsPanelControlVisible"/>'s alone. Variables, writes and status rows do not change.
    /// </summary>
    private void FollowEngineType()
    {
        var engines = LoadedEngineType();
        if (engines == ShownEngineType || _allPanelStructure == null) return;
        Bbs757Variant.RewriteSections(_allPanelStructure, _panelControls!, _panelDisplays!, _panelStructure!,
            Bbs757Variant.HiddenNodes(_byNode.Values, engines));
        ShownEngineType = engines;
        Log.Info("BBS757", $"Engine type {engines}: the sections list " +
            (engines == Bbs757EngineType.Unknown ? "every panel." : "its panels only."));
    }

    // =================================================================================
    // Ctrl+M
    // =================================================================================

    /// <summary>
    /// The 757 Monitor Manager's rows: every announced variable's, as on every aircraft
    /// (<see cref="MonitorRowBuilder"/>), then the rows of speech that is no variable's, which the
    /// gap plans add through <see cref="AddCduMonitorRows"/> and <see cref="AddCockpitMonitorRows"/>, then the display
    /// readers' rows, last (<see cref="Displays"/>: the FMA's first).
    /// </summary>
    public IReadOnlyList<MonitorRow> MonitorRows()
    {
        var rows = MonitorRowBuilder.Build(GetVariables());
        AddCduMonitorRows(rows);       // plan 09
        AddCockpitMonitorRows(rows);   // plan 13
        rows.AddRange(Displays.Rows);  // plan 23
        return rows;
    }

    // =================================================================================
    // The tablet view
    // =================================================================================

    private readonly object _tabletLock = new();
    private CoherentBbs757TabletClient? _tabletClient;
    // Set by a switch away, under _tabletLock: from then on no client is made from the factory.
    private bool _tabletRetired;

    /// <summary>Makes the tablet client; replaced by tests (the default starts a real one).</summary>
    internal Func<CoherentBbs757TabletClient>? TabletClientFactory { get; init; }

    /// <summary>
    /// Closes MainForm's windows on the tablet client (the CDU and EFB windows); set by MainForm when
    /// it hands one of them the client. <see cref="OnSwitchedAway"/> runs it before it disposes the
    /// client: the switch calls OnSwitchedAway before it disposes the 757's windows, which would
    /// otherwise run on a disposed client meanwhile.
    /// </summary>
    internal Action? CloseTabletWindows { get; set; }

    /// <summary>
    /// The one client on the 757's "BBSTablet" Coherent view ([DBG-9]: one socket per view), created and
    /// started on first use and disposed when the pilot switches away (<see cref="OnSwitchedAway"/>), so
    /// background features can read the CDUs from the moment the 757 loads. The CDU window and the EFB
    /// window's Navigraph read use it through MainForm. Call it first from the UI thread: the client
    /// raises its events on the context it was created on. After a switch away it returns the disposed
    /// client, which reads and presses nothing, rather than opening a new socket for a dead profile;
    /// when nothing had made one by then, a disposed client that never started.
    /// </summary>
    internal CoherentBbs757TabletClient EnsureTabletClient()
    {
        lock (_tabletLock)
        {
            if (_tabletClient != null) return _tabletClient;
            if (_tabletRetired)
            {
                // A late call after the switch: a client made now would never be disposed, and two
                // persistent clients on one view knock each other off its one inspector socket ([DBG-9]).
                var retired = new CoherentBbs757TabletClient(() => "");
                retired.Dispose();
                return _tabletClient = retired;
            }
            return _tabletClient = (TabletClientFactory ?? StartedTabletClient)();
        }
    }

    private static CoherentBbs757TabletClient StartedTabletClient()
    {
        var client = new CoherentBbs757TabletClient();
        client.Start();
        return client;
    }

    private Bbs757CduPages? _cduPages;

    /// <summary>
    /// The page service the gap plans' background features read and type on the first officer's
    /// CDU with (<see cref="Bbs757CduPages.FirstOfficer"/>), over <see cref="EnsureTabletClient"/>'s
    /// client: one per definition, made on first use (from the UI thread, as that client is). After a
    /// switch away it is over the disposed client, so every call answers null (false).
    /// </summary>
    internal Bbs757CduPages CduPages
    {
        get
        {
            lock (_tabletLock) return _cduPages ??= Bbs757CduPages.For(EnsureTabletClient());
        }
    }

    /// <summary>
    /// The end of a switch away: MainForm's windows on the client close first
    /// (<see cref="CloseTabletWindows"/>), then the client goes, socket and loop with it, and the
    /// definition makes no other (<see cref="EnsureTabletClient"/>). Never throws: a window that fails
    /// to close still lets the client be disposed.
    /// </summary>
    private void DisposeTabletClient()
    {
        try { CloseTabletWindows?.Invoke(); }
        catch (Exception ex) { Log.Debug("BBS757", $"Tablet windows did not close: {ex.Message}"); }
        CloseTabletWindows = null;
        CoherentBbs757TabletClient? client;
        lock (_tabletLock)
        {
            _tabletRetired = true;
            client = _tabletClient;
        }
        client?.Dispose();
    }
}
