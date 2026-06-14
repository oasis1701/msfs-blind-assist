using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Taxi guidance form. Allows blind users to select a destination and taxiway route,
/// then activates real-time steering guidance.
///
/// Design:
/// - Airport ICAO input with auto-fill from nearest airport
/// - Destination type selection (Runway / Gate-Parking)
/// - Destination combo (runways or gates sorted by distance)
/// - First taxiway combo: all taxiways sorted closest to farthest, with "(None - calculate shortest path)" at top
/// - "Add Taxiway" button to dynamically add connected taxiway combos
/// - Each added taxiway shows only connected taxiways from the previous selection
/// - Screen reader optimized tab order
/// </summary>
public class TaxiAssistForm : Form
{
    private readonly IAirportDataProvider _dataProvider;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly TaxiGuidanceManager _guidanceManager;
    private readonly Services.GateDataSource? _gateSource;
    // When non-null, OnCalculateClicked fires GSX gate auto-select for gate destinations
    // (if the setting is on and GSX is available). The selector is constructed in MainForm
    // and is only non-null when GSX is installed. NOTE: the selector itself is always
    // non-null once assigned — null-checking it tells you whether the feature is available
    // at all, but the REAL runtime availability gate is _gsxGateSelector.CouatlStarted.
    // A non-null selector with CouatlStarted == false means GSX is installed but not
    // running this session; in that case auto-select silently falls through to manual routing.
    private readonly Services.Gsx.GsxGateSelector? _gsxGateSelector;
    // Optional. When non-null, OnCalculateClicked refreshes aircraft position
    // from `LastKnownPosition` (or via RequestAircraftPositionAsync) right
    // before computing the route, so the route starts from where the aircraft
    // ACTUALLY is — not from a stale snapshot taken when the form opened.
    // Critical for the open-form-then-push-back workflow: without this, the
    // route is computed from the gate, the post-pushback aircraft is already
    // off that route, and off-route detection recalcs immediately.
    private readonly MSFSBlindAssist.SimConnect.SimConnectManager? _simConnectManager;
    private readonly TcasService? _tcasService;
    // Refreshed at the top of LoadAirportData from _simConnectManager?.AircraftWingSpan
    // so a mid-session aircraft swap (multi-aircraft architecture) is honored on the
    // next form open. Constructor parameter is preserved as a fallback for callers
    // that don't pass a SimConnectManager.
    private double _aircraftWingspan;

    // Form controls
    private Label lblAirport = null!;
    private TextBox txtAirport = null!;
    private Label lblDestType = null!;
    private ComboBox cmbDestType = null!;
    private Label lblDestination = null!;
    private CheckBox chkFitFilter = null!;
    private Label lblGateSearch = null!;
    private TextBox txtGateSearch = null!;
    private ComboBox cmbDestination = null!;
    private Label lblFirstTaxiway = null!;
    private ComboBox cmbFirstTaxiway = null!;
    private CheckBox chkFirstHoldShort = null!;
    private ComboBox cmbFirstHoldShortRunway = null!;
    private Button btnAddTaxiway = null!;
    // Progressive Taxi terminator controls. These two combos are form-level (not
    // per-row): RefreshTerminatorRow() repositions and shows them on whichever
    // taxiway row is CURRENTLY last, and only when the destination type is
    // Progressive Taxi (index 2). The chosen terminator therefore "travels with
    // the last row" as the spec requires. The runway TARGET reuses that last
    // row's existing "Hold short of runway" combo; cmbTerminatorTaxiway is the
    // separate target picker for the hold-short-of-taxiway case.
    private Label lblTerminatorType = null!;
    private ComboBox cmbTerminatorType = null!;
    private Label lblTerminatorTaxiway = null!;
    private ComboBox cmbTerminatorTaxiway = null!;
    // Display strings for the terminator type combo, index-aligned with the
    // ProgressiveTerminatorType resolution switch in OnCalculateClicked.
    private static readonly string[] TerminatorTypeItems =
    {
        "Hold short of runway",
        "Hold short of taxiway",
        "After crossing runway",
        "End of last taxiway"
    };
    private Button btnCalculate = null!;
    private Button btnStop = null!;
    private Label lblStatus = null!;
    private Label lblRouteSummary = null!;
    private TextBox txtRouteSummary = null!;

    // Constant entry shown in every "Hold short of runway" combo when no
    // explicit runway hold-short has been picked. Match exactly when reading
    // user selections back out so we can distinguish "no selection" from a
    // genuine runway pick.
    private const string NO_RUNWAY_HOLDSHORT = "(none)";

    // Sentinel "&Of runway:" mnemonic letter for the per-row dropdown. Picked
    // because A, T, E, F, H, D, C, S are already burned by other form controls
    // (see the mnemonic plan at the top of InitializeFormControls). O is free
    // and reads cleanly: "Hold short OF runway".
    private const string HOLD_SHORT_RUNWAY_LABEL = "Hold short &of runway:";

    // Dynamic taxiway controls. Tuple now carries the runway-hold-short combo
    // alongside the existing combo / hold-short checkbox / remove button so
    // OnCalculateClicked can iterate them all in one pass.
    private Panel pnlTaxiways = null!;
    private List<(Label label, ComboBox combo, CheckBox holdShort, ComboBox holdShortRunway, Button removeBtn)> _additionalTaxiways = new();
    private const int MAX_ADDITIONAL_TAXIWAYS = 20;

    // Vertical pixel height of one dynamic taxiway row inside pnlTaxiways.
    // Two-line layout: line 1 holds the taxiway combo + Hold-short checkbox +
    // Remove button; line 2 holds the "Hold short of runway" combo.
    private const int DYNAMIC_ROW_HEIGHT_PX = 80;

    // Vertical pixel height of the Progressive Taxi terminator block (type combo
    // on line 1, taxiway-target combo on line 2) shown below the last taxiway
    // row inside pnlTaxiways when in Progressive Taxi mode.
    private const int TERMINATOR_BLOCK_HEIGHT_PX = 55;

    // Cached runway designators for the current airport (e.g. ["09L","09R","27L","27R"]).
    // Populated on airport load; consumed when constructing every Hold-short-of-runway
    // combo (first row + each dynamically added row).
    private List<string> _airportRunwayIds = new();

    // State
    private TaxiGraph? _graph;
    private string _currentIcao = "";
    private double _aircraftLat, _aircraftLon, _aircraftHeading;

    // Destination nodes for routing
    private Dictionary<string, int> _destinationNodeMap = new();
    private Dictionary<string, double> _destinationHeadingMap = new();
    private Dictionary<string, double> _destinationHeadingTrueMap = new();
    private Dictionary<string, (double lat, double lon)> _destinationThresholdMap = new();
    // Progressive Taxi: maps "Runway X" display name → Runway, used by the "After crossing runway"
    // and "Hold short of runway" terminators to resolve the far-side / near-side node at Calculate time.
    private Dictionary<string, Runway> _crossRunwayMap = new();
    // Gate mode: maps the display label (same key as _destinationNodeMap) → ParkingSpot.
    // Populated in the gate branch of PopulateDestinations so OnCalculateClicked can pass the
    // actual ParkingSpot to GsxGateSelector without re-querying the data provider.
    private Dictionary<string, ParkingSpot> _destinationSpotMap = new();

    // Gate-branch cache (Fix: per-keystroke gate-list rebuild). PopulateDestinations
    // runs on every txtGateSearch keystroke, every chkFitFilter toggle, and on each
    // dest-type change. The expensive work in the GATE branch — GateDataSource.GetGates
    // (directory enumeration / uncached navdata DB query at .py-only airports like EDDF),
    // plus a _graph.FindNearestNode + distance check per spot — depends ONLY on the
    // airport (ICAO + graph), not on the search text or fit filter. We resolve it ONCE
    // per airport into _cachedGateSpots (spot + its routing node id), and each
    // PopulateDestinations pass merely applies the search + wingspan filters and rebuilds
    // the combo/map entries in memory. Mirrors how GateTeleportForm loads once + filters.
    //
    // The wingspan fit-filter is deliberately NOT baked into the cache: _aircraftWingspan
    // can change between passes (mid-session aircraft swap), so it must re-apply per pass
    // against the full cached list.
    private List<(ParkingSpot spot, int nodeId)>? _cachedGateSpots;
    private string _cachedGateSpotsIcao = "";

    // Docking guidance manager: receives the selected gate so proximity audio
    // and lateral tone can guide the pilot to the stop position. Set in
    // OnCalculateClicked for gate destinations; cleared on runway destinations.
    private readonly Services.DockingGuidanceManager? _dockingManager;

    // Resolves the GSX .py per-aircraft stop offset for a navdata/.py gate so the
    // docking stop moves to where GSX's VDGS would stop THIS airframe. Lazy + cached.
    private readonly Services.Gsx.GsxStopOffsetResolver _stopOffsetResolver = new();

    public TaxiAssistForm(
        IAirportDataProvider dataProvider,
        ScreenReaderAnnouncer announcer,
        TaxiGuidanceManager guidanceManager,
        MSFSBlindAssist.SimConnect.SimConnectManager? simConnectManager = null,
        TcasService? tcasService = null,
        double aircraftWingspan = 0,
        Services.GateDataSource? gateSource = null,
        Services.Gsx.GsxGateSelector? gsxGateSelector = null,
        Services.DockingGuidanceManager? dockingManager = null)
    {
        _dataProvider = dataProvider;
        _announcer = announcer;
        _guidanceManager = guidanceManager;
        _simConnectManager = simConnectManager;
        _tcasService = tcasService;
        _aircraftWingspan = aircraftWingspan;
        _gateSource = gateSource;
        _gsxGateSelector = gsxGateSelector;
        _dockingManager = dockingManager;
        InitializeFormControls();
    }

    /// <summary>
    /// Sets the aircraft position for initial taxiway sorting and graph building.
    /// Call before Show().
    /// </summary>
    public void SetAircraftPosition(double lat, double lon, double heading, string nearestIcao)
    {
        _aircraftLat = lat;
        _aircraftLon = lon;
        _aircraftHeading = heading;

        if (!string.IsNullOrEmpty(nearestIcao))
        {
            txtAirport.Text = nearestIcao.ToUpperInvariant();
            LoadAirportData(nearestIcao);
        }
    }

    /// <summary>
    /// Refreshes aircraft position while the form is open. MainForm calls this on every
    /// position update so that when the user presses Calculate — especially during a
    /// mid-taxi route amendment — the route starts from the CURRENT position, not from
    /// wherever the aircraft was when the form opened.
    /// </summary>
    public void UpdateAircraftPosition(double lat, double lon, double heading)
    {
        _aircraftLat = lat;
        _aircraftLon = lon;
        _aircraftHeading = heading;
    }

    private void InitializeFormControls()
    {
        this.Text = "Taxi Guidance";
        this.Size = new System.Drawing.Size(420, 480);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.KeyPreview = true;
        this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) this.Close(); };

        int y = 15;
        int labelX = 15;
        int controlX = 15;
        int controlWidth = 370;

        // Mnemonic plan (must be unique across the form so Alt+letter jumps to one
        // unambiguous control; duplicates cause Windows to cycle, which is jarring
        // for blind users):
        //   Alt+A  Airport (ICAO)
        //   Alt+T  Destination Type combo
        //   Alt+E  Destination combo (D&estination)
        //   Alt+F  First taxiway combo
        //   Alt+H  First Hold-short checkbox  (dynamic Hold-shorts share Alt+H — cycle)
        //   Alt+O  Hold short &of runway combo (first row + dynamic — all cycle on Alt+O)
        //   Alt+D  Add (D)taxiway button
        //   Alt+N  Progressive-taxi termi&nator type combo (last row only, index 2)
        //   Alt+W  Progressive-taxi terminator taxi&way target combo (last row only)
        //   Alt+C  Calculate Route
        //   Alt+S  Stop Guidance
        //   Alt+R  Remove (dynamic) — shared across all Remove buttons (cycle)
        //   Alt+2..9  Dynamic Taxiway label (Taxiway &2 .. Taxiway &9)
        //
        // Tab order (top→bottom of form, no jumps to dynamic panel at the end):
        //   1 txtAirport, 2 cmbDestType, 3 cmbDestination,
        //   4 cmbFirstTaxiway, 5 chkFirstHoldShort, 6 btnAddTaxiway,
        //   7 pnlTaxiways (dynamic taxiway groups visit here in insertion order),
        //   8 btnCalculate, 9 btnStop.

        // Airport ICAO
        lblAirport = new Label
        {
            Text = "&Airport (ICAO):",
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            AccessibleName = "Airport ICAO Label"
        };
        y += 20;
        txtAirport = new TextBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            CharacterCasing = CharacterCasing.Upper,
            AccessibleName = "Airport ICAO",
            AccessibleDescription = "Enter the four-letter ICAO code for the airport"
        };
        txtAirport.Leave += (s, e) => LoadAirportData(txtAirport.Text.Trim());
        y += 30;

        // Destination type
        lblDestType = new Label
        {
            Text = "Destination &type:",
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            AccessibleName = "Destination type Label"
        };
        y += 20;
        cmbDestType = new ComboBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Destination type",
            AccessibleDescription = "Select whether to taxi to a runway, a gate/parking position, a progressive taxi (route to a hold short or across a runway), or a deice area"
        };
        cmbDestType.Items.AddRange(new object[] { "Runway", "Gate / Parking", "Progressive Taxi", "Deice Area" });
        cmbDestType.SelectedIndex = 0;
        cmbDestType.SelectedIndexChanged += OnDestTypeChanged;
        y += 30;

        // Destination
        lblDestination = new Label
        {
            Text = "D&estination:",
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            AccessibleName = "Destination Label"
        };
        chkFitFilter = new CheckBox
        {
            Text = "Show &fitting only",
            Location = new System.Drawing.Point(200, y),
            AutoSize = true,
            Visible = false,
            Checked = _aircraftWingspan > 0,
            Enabled = _aircraftWingspan > 0,
            AccessibleName = "Show only fitting parking spots",
            AccessibleDescription = "When checked, only shows parking spots large enough for your aircraft"
        };
        chkFitFilter.CheckedChanged += (s, e) => { if (cmbDestType.SelectedIndex == 1) PopulateDestinations(); };
        y += 20;

        // Gate search box (type-to-filter on name+number+suffix). Hidden
        // until Gate/Parking destination type is selected.
        lblGateSearch = new Label
        {
            Text = "&Gate search:",
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            Visible = false,
            AccessibleName = "Gate search label"
        };
        y += 20;
        txtGateSearch = new TextBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            Visible = false,
            AccessibleName = "Gate search",
            AccessibleDescription = "Type a gate letter or number to filter the destination list"
        };
        txtGateSearch.TextChanged += (s, e) =>
        {
            if (cmbDestType.SelectedIndex == 1) PopulateDestinations();
        };
        txtGateSearch.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (cmbDestination.Items.Count > 0)
                {
                    cmbDestination.SelectedIndex = 0;
                    cmbDestination.Focus();
                }
                else
                {
                    _announcer.AnnounceImmediate("No matching gates.");
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        y += 30;

        cmbDestination = new ComboBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Destination",
            AccessibleDescription = "Select the destination runway or gate"
        };
        y += 30;

        // First taxiway
        lblFirstTaxiway = new Label
        {
            Text = "&First taxiway:",
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            AccessibleName = "First taxiway Label"
        };
        y += 20;
        cmbFirstTaxiway = new ComboBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = 280,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "First taxiway",
            AccessibleDescription = "Select the first taxiway to follow, sorted by distance. Select None to calculate the shortest path automatically."
        };
        cmbFirstTaxiway.SelectedIndexChanged += OnFirstTaxiwayChanged;

        chkFirstHoldShort = new CheckBox
        {
            Text = "&Hold short",
            Location = new System.Drawing.Point(controlX + 290, y + 2),
            Width = 90,
            AccessibleName = "Hold short after first taxiway",
            AccessibleDescription = "When checked, guidance will stop at the end of this taxiway and wait for you to continue"
        };
        y += 30;

        // Hold-short-of-runway picker for the first taxiway slot. Lets the
        // user EXPLICITLY annotate an ATC-instructed runway hold-short that
        // falls between the first taxiway and the next taxiway in the
        // sequence. Auto-detection still runs over the whole route, so even
        // when nothing is picked here, every runway crossing on the path
        // gets an automatic hold-short. The explicit picker is a belt-and-
        // -suspenders cue — useful when the pilot wants confirmation that
        // the system flagged the SPECIFIC runway ATC named, and as the
        // mechanism for the rare case where auto-detect didn't fire.
        Label lblFirstHoldShortRunway = new Label
        {
            Text = HOLD_SHORT_RUNWAY_LABEL,
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            AccessibleName = "Hold short of runway after first taxiway label"
        };
        y += 20;
        cmbFirstHoldShortRunway = new ComboBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Hold short of runway after first taxiway",
            AccessibleDescription = "Optional: pick a runway to hold short of after this taxiway. Use when ATC explicitly assigns a hold-short clearance for a runway your route crosses. Leave at \"(none)\" to rely on automatic runway-crossing detection."
        };
        cmbFirstHoldShortRunway.Items.Add(NO_RUNWAY_HOLDSHORT);
        cmbFirstHoldShortRunway.SelectedIndex = 0;
        this.Controls.Add(lblFirstHoldShortRunway);
        y += 30;

        // Add Taxiway button
        btnAddTaxiway = new Button
        {
            Text = "A&dd Taxiway",
            Location = new System.Drawing.Point(controlX, y),
            Width = 140,
            Height = 28,
            AccessibleName = "Add Taxiway",
            AccessibleDescription = "Add another taxiway to the route sequence. Only available after selecting a taxiway.",
            Enabled = false
        };
        btnAddTaxiway.Click += OnAddTaxiwayClicked;
        y += 35;

        // Progressive Taxi terminator controls. Created once, hidden by default;
        // RefreshTerminatorRow() repositions them onto the current last taxiway
        // row and shows them only in Progressive Taxi mode. They live inside
        // pnlTaxiways so they sit visually with the last row and stay in the
        // dynamic-panel tab slot. The taxiway-target combo is only shown for
        // the "Hold short of taxiway" terminator type.
        lblTerminatorType = new Label
        {
            Text = "Termi&nator:",
            AutoSize = true,
            Visible = false,
            AccessibleName = "Progressive taxi terminator type label"
        };
        cmbTerminatorType = new ComboBox
        {
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false,
            AccessibleName = "Progressive taxi terminator",
            AccessibleDescription = "Choose how this progressive taxi leg ends: hold short of a runway, hold short of a taxiway, after crossing a runway, or at the end of the last taxiway. Pick the target runway in the Hold short of runway combo, or the target taxiway in the terminator taxiway combo."
        };
        cmbTerminatorType.Items.AddRange(TerminatorTypeItems);
        cmbTerminatorType.SelectedIndex = 0;
        cmbTerminatorType.SelectedIndexChanged += (s, ev) => RefreshTerminatorRow();
        lblTerminatorTaxiway = new Label
        {
            Text = "Hold short of taxi&way:",
            AutoSize = true,
            Visible = false,
            AccessibleName = "Progressive taxi terminator taxiway label"
        };
        cmbTerminatorTaxiway = new ComboBox
        {
            Width = 190,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false,
            AccessibleName = "Progressive taxi terminator taxiway",
            AccessibleDescription = "Pick the taxiway to hold short of where it meets the last taxiway in your route."
        };

        // Dynamic taxiway panel (for additional taxiway combos)
        pnlTaxiways = new Panel
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            Height = 0, // starts empty, grows as taxiways are added
            AutoSize = false
        };
        // y will be adjusted dynamically

        // Calculate button
        btnCalculate = new Button
        {
            Text = "&Calculate Route",
            Location = new System.Drawing.Point(controlX, y),
            Width = 180,
            Height = 30,
            AccessibleName = "Calculate Route",
            AccessibleDescription = "Calculate the taxi route and start guidance"
        };
        btnCalculate.Click += OnCalculateClicked;

        btnStop = new Button
        {
            Text = "&Stop Guidance",
            Location = new System.Drawing.Point(controlX + 190, y),
            Width = 180,
            Height = 30,
            AccessibleName = "Stop Guidance",
            AccessibleDescription = "Stop the active taxi guidance"
        };
        btnStop.Click += OnStopClicked;
        y += 40;

        // Status label
        lblStatus = new Label
        {
            Text = "",
            Location = new System.Drawing.Point(labelX, y),
            Width = controlWidth,
            Height = 20,
            AccessibleName = "Status"
        };
        y += 25;

        // Route summary read-only TextBox. Shows the same text the announcer
        // speaks when Calculate succeeds (e.g. "Taxi to runway 28L via A,
        // B, K. Total distance 1.2 miles."). Useful for two reasons:
        //   1. Screen readers often interrupt the spoken summary with their
        //      own UI announcement (especially after the form closes), so
        //      the text-form is the only reliable record.
        //   2. The shortest-path calculate path is the same way — the user
        //      can verify what the router actually produced when they
        //      didn't pick taxiways.
        // Multi-line + ReadOnly = TabStop on but not editable; the user can
        // arrow-read with the screen reader. Populated by OnCalculateClicked
        // from TaxiGuidanceManager.LastRouteSummary.
        lblRouteSummary = new Label
        {
            Text = "Last route &summary:",
            Location = new System.Drawing.Point(labelX, y),
            Width = controlWidth,
            Height = 20,
            AccessibleName = "Last route summary label"
        };
        y += 22;
        txtRouteSummary = new TextBox
        {
            Location = new System.Drawing.Point(labelX, y),
            Width = controlWidth,
            Height = 70,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            TabStop = true,
            AccessibleName = "Last route summary",
            AccessibleDescription = "Read-only display of the most recent route summary, including the shortest-path result. Use arrow keys to navigate the text with your screen reader."
        };

        // Add controls (tab order follows add order). cmbFirstHoldShortRunway
        // is added between chkFirstHoldShort and btnAddTaxiway so it sits in
        // the linear flow at the right spot.
        this.Controls.Add(lblAirport);
        this.Controls.Add(txtAirport);
        this.Controls.Add(lblDestType);
        this.Controls.Add(cmbDestType);
        this.Controls.Add(lblDestination);
        this.Controls.Add(chkFitFilter);
        this.Controls.Add(lblGateSearch);
        this.Controls.Add(txtGateSearch);
        this.Controls.Add(cmbDestination);
        this.Controls.Add(lblFirstTaxiway);
        this.Controls.Add(cmbFirstTaxiway);
        this.Controls.Add(chkFirstHoldShort);
        this.Controls.Add(cmbFirstHoldShortRunway);
        this.Controls.Add(btnAddTaxiway);
        // Terminator controls live inside the dynamic taxiway panel so they sit
        // with the last row and share the panel's tab slot. Added after the
        // first AddTaxiway row's controls would be, but since the panel starts
        // empty they go in now and RefreshTerminatorRow positions/shows them.
        pnlTaxiways.Controls.Add(lblTerminatorType);
        pnlTaxiways.Controls.Add(cmbTerminatorType);
        pnlTaxiways.Controls.Add(lblTerminatorTaxiway);
        pnlTaxiways.Controls.Add(cmbTerminatorTaxiway);
        // The terminator block belongs to the LAST taxiway row, so it should tab
        // AFTER every dynamic row inside the panel. Dynamic rows get sequential
        // TabIndexes starting low (= panel control count at add time); a high base
        // keeps the terminator combos last in the panel's tab stream.
        cmbTerminatorType.TabIndex = 9000;
        cmbTerminatorTaxiway.TabIndex = 9001;
        this.Controls.Add(pnlTaxiways);
        this.Controls.Add(btnCalculate);
        this.Controls.Add(btnStop);
        this.Controls.Add(lblStatus);
        this.Controls.Add(lblRouteSummary);
        this.Controls.Add(txtRouteSummary);

        // Tab order: Airport → Type → Destination → First taxiway → First
        // hold-short → First hold-short-of-runway → Add Taxiway → DYNAMIC
        // TAXIWAYS → Calculate → Stop. The dynamic-taxiway panel needs an
        // explicit TabIndex BETWEEN Add and Calculate; without that, its inner
        // controls land at the END of the tab order (after Stop), which is
        // what made adding taxiways feel "illogical" — Tab from Add jumped
        // past the new combos straight to Calculate, then later wrapped back
        // through them. Setting pnlTaxiways.TabStop=true and an explicit
        // TabIndex puts the panel's children where they belong in the linear
        // flow. Each dynamic group gets sequential tab indices inside the
        // panel as it is created.
        int tabIdx = 0;
        txtAirport.TabIndex = tabIdx++;
        cmbDestType.TabIndex = tabIdx++;
        txtGateSearch.TabIndex = tabIdx++;
        cmbDestination.TabIndex = tabIdx++;
        chkFitFilter.TabIndex = tabIdx++;
        cmbFirstTaxiway.TabIndex = tabIdx++;
        chkFirstHoldShort.TabIndex = tabIdx++;
        cmbFirstHoldShortRunway.TabIndex = tabIdx++;
        btnAddTaxiway.TabIndex = tabIdx++;
        pnlTaxiways.TabStop = true;
        pnlTaxiways.TabIndex = tabIdx++;
        btnCalculate.TabIndex = tabIdx++;
        btnStop.TabIndex = tabIdx++;
        txtRouteSummary.TabIndex = tabIdx++;

        // Load handler for focus
        this.Load += (s, e) =>
        {
            this.BringToFront();
            this.Activate();
            txtAirport.Focus();
        };
    }

    private async void LoadAirportData(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return;

        // Refresh wingspan from the live SimConnectManager. This form persists
        // across opens (hide-on-close), so the constructor-time wingspan can be
        // stale after a mid-session aircraft swap or after SimConnect connected.
        // _simConnectManager is null only when the form was constructed without
        // one (test/standalone) — fall back to the constructor value in that case.
        if (_simConnectManager != null && _simConnectManager.AircraftWingSpan > 0)
            _aircraftWingspan = _simConnectManager.AircraftWingSpan;

        // Re-enable the "fitting only" checkbox if wingspan data has become
        // available since the form was constructed. The Visible state is
        // refreshed by OnDestTypeChanged when the user selects a parking
        // destination type; the Enabled state needs its own refresh here.
        chkFitFilter.Enabled = _aircraftWingspan > 0;

        if (icao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase) && _graph != null) return;

        _currentIcao = icao.ToUpperInvariant();
        // Invalidate the gate-branch resolution cache — the new airport has a
        // different graph + parking layout. The cache is also re-validated by
        // ICAO inside the GATE branch of PopulateDestinations (defence in depth),
        // but clearing here frees the old airport's spots immediately.
        _cachedGateSpots = null;
        _cachedGateSpotsIcao = "";
        _destinationNodeMap.Clear();
        _destinationHeadingMap.Clear();

        // Clear first taxiway and all additional taxiways. Also flush the
        // cached airport runway list and reset the first row's "Hold short
        // of runway" combo to "(none)" so we don't show stale runway names
        // from the previous airport if the new one fails to load below.
        cmbFirstTaxiway.Items.Clear();
        ClearAllAdditionalTaxiways();
        _airportRunwayIds = new List<string>();
        RebuildHoldShortRunwayCombo(cmbFirstHoldShortRunway);

        cmbDestination.Items.Clear();

        // Check airport exists
        if (!_dataProvider.AirportExists(icao))
        {
            lblStatus.Text = $"Airport {icao} not found in database.";
            return;
        }

        // Build graph (off the UI thread to avoid stalls at large airports)
        var paths = _dataProvider.GetTaxiPaths(icao);
        if (paths.Count == 0)
        {
            lblStatus.Text = $"No taxi data for {icao}.";
            return;
        }

        var parking = _dataProvider.GetParkingSpots(icao);
        var starts = _dataProvider.GetRunwayStarts(icao);

        lblStatus.Text = $"{icao}: building taxi graph…";
        btnCalculate.Enabled = false;
        try
        {
            _graph = await TaxiGraph.BuildAsync(paths, parking, starts);
        }
        finally
        {
            btnCalculate.Enabled = true;
        }

        lblStatus.Text = $"{icao}: {_graph.Nodes.Count} nodes, {paths.Count} paths.";

        // Populate destinations
        PopulateDestinations();

        // Cache the airport's runway designators so every Hold-short-of-runway
        // combo (first row + each dynamic row) can be populated identically.
        // Same source as the destination dropdown, but unfiltered by IsTakeoff —
        // the user might want to hold short of a runway they can't take off
        // from (perfectly fine; it's still a runway you must hold short of).
        // Closed runways ARE excluded — no point holding short of pavement
        // that's marked closed.
        _airportRunwayIds = _dataProvider.GetRunways(icao)
            .Where(r => !r.IsClosed)
            .Select(r => r.RunwayID)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        RebuildHoldShortRunwayCombo(cmbFirstHoldShortRunway);

        // Populate first taxiway combobox sorted by distance, closest first
        PopulateFirstTaxiway();
    }

    /// <summary>
    /// Resets the given combo to "(none)" + every runway designator at the
    /// current airport. Called after airport load (for the first row) and
    /// whenever a new dynamic taxiway row is added (for that row's combo).
    /// Preserves the current selection by name when possible, so a user that
    /// switches airports and back doesn't lose their pick.
    /// </summary>
    private void RebuildHoldShortRunwayCombo(ComboBox combo)
    {
        string? previous = combo.SelectedItem?.ToString();
        combo.Items.Clear();
        combo.Items.Add(NO_RUNWAY_HOLDSHORT);
        foreach (string r in _airportRunwayIds)
            combo.Items.Add(r);

        int idx = 0;
        if (!string.IsNullOrEmpty(previous))
        {
            int found = combo.Items.IndexOf(previous);
            if (found >= 0) idx = found;
        }
        combo.SelectedIndex = idx;
    }

    private void PopulateDestinations()
    {
        cmbDestination.Items.Clear();
        _destinationNodeMap.Clear();
        _destinationHeadingMap.Clear();
        _destinationHeadingTrueMap.Clear();
        _destinationThresholdMap.Clear();
        _crossRunwayMap.Clear();
        _destinationSpotMap.Clear();

        if (_graph == null) return;

        bool isRunway = cmbDestType.SelectedIndex == 0;
        bool isDeice = cmbDestType.SelectedIndex == 3;

        if (isRunway)
        {
            // Build a runway-name → StartPosition lookup so we can anchor the
            // route destination and the lineup target at the actual painted
            // lineup point, not the physical pavement edge.
            //
            // Runway.StartLat/StartLon comes from runway_end.lonx/laty in the
            // navdatareader DB — i.e., the physical pavement edge of the
            // runway end. For runways with a displaced threshold (e.g., KLAS
            // 26R has a 1407 ft displacement), the painted lineup point sits
            // hundreds of meters from that edge. Using the physical edge
            // would cause FindNearestNode to resolve to an adjacent-taxiway
            // node instead of a runway-threshold node, and _destinationThresholdMap
            // would feed a wrong _lineupTargetLat/Lon into LiningUp's cross-track
            // math.
            //
            // The `start` table is navdatareader's curated "where MSFS spawns an
            // aircraft if you select runway X" value, which correctly accounts
            // for displaced thresholds. It is ALSO the source TaxiGraph builds
            // RunwayCenterlines from (see TaxiGraph.Build, around line 170),
            // and TakeoffAssist's cross-track math reads those centerlines.
            // Anchoring the route destination and lineup target here on the
            // same source means taxi-lineup centerline math and TakeoffAssist
            // centerline math reference the same physical position; otherwise
            // the two systems disagree on where the runway "begins" by hundreds
            // of meters at displaced-threshold airports.
            //
            // Fall back to Runway.StartLat/StartLon only when the start table
            // has no entry for a given runway name. That preserves the current
            // behavior for runways the start table doesn't cover (rare; covers
            // DBs/scenery where start-table data is incomplete).
            var startsByRunway = _dataProvider.GetRunwayStarts(_currentIcao)
                .Where(s => !string.IsNullOrEmpty(s.RunwayName))
                .GroupBy(s => s.RunwayName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var runways = _dataProvider.GetRunways(_currentIcao);

            foreach (var rwy in runways)
            {
                // Filter against runway operational flags from the DB. Defaults
                // are permissive (closed=false, can-takeoff=true) so DBs that
                // don't populate these columns still see every runway. Users
                // with Navigraph or third-party scenery — which DOES populate
                // these — won't see closed runways in the destination dropdown.
                if (rwy.IsClosed) continue;

                // Prefer the start-table lineup point (handles displaced
                // thresholds correctly). Fall back to the physical pavement
                // edge when no start row exists for this runway name.
                double lineupLat;
                double lineupLon;
                if (startsByRunway.TryGetValue(rwy.RunwayID, out var start))
                {
                    lineupLat = start.Latitude;
                    lineupLon = start.Longitude;
                }
                else
                {
                    lineupLat = rwy.StartLat;
                    lineupLon = rwy.StartLon;
                }

                var nearNode = _graph.FindNearestNode(lineupLat, lineupLon);
                if (nearNode != null)
                {
                    string name = $"Runway {rwy.RunwayID}";

                    if (!_destinationNodeMap.ContainsKey(name))
                    {
                        _destinationNodeMap[name] = nearNode.NodeId;
                        _destinationHeadingMap[name] = rwy.HeadingMag;
                        _destinationHeadingTrueMap[name] = rwy.Heading;
                        _destinationThresholdMap[name] = (lineupLat, lineupLon);
                        cmbDestination.Items.Add(name);
                    }
                }
            }
        }
        else if (isDeice)
        {
            // Deice area path: populate from GateDataSource.GetDeiceAreas().
            // Uses the same node-resolution and _destinationSpotMap machinery as the
            // gate path so OnCalculateClicked can resolve the spot and hand it to
            // DockingGuidanceManager.SetDestinationGate (which handles the
            // IsDeiceArea flag internally — emits "Deicing guidance" and uses
            // datum alignment). MAX_PARKING_TO_GRAPH_M matches the gate path so
            // spots without a nearby graph node are silently dropped (no way to
            // taxi there).
            const double MAX_DEICE_TO_GRAPH_M = 100.0;

            var deiceAreas = _gateSource?.GetDeiceAreas(_currentIcao) ?? new List<ParkingSpot>();

            foreach (var spot in deiceAreas.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                string label = spot.ToString();
                if (_destinationNodeMap.ContainsKey(label)) continue;

                // Prefer the GSX stop position (the docking target) for routing;
                // fall back to the spot's base lat/lon when stop position is absent.
                // Test HasValue, not "!= 0": null is the only correct "absent"
                // signal. A stop coordinate or heading that legitimately normalizes
                // to exactly 0.0 — a due-north (0°) stop heading, or the rare 0.0°
                // lon/lat — is a real value, and a `GetValueOrDefault() != 0` test
                // would discard it and silently substitute the parking-position
                // value. This mirrors DockingGuidanceManager's `StopHeading ?? Heading`
                // null-coalescing convention.
                double targetLat = spot.StopLatitude.HasValue
                    ? spot.StopLatitude.Value : spot.Latitude;
                double targetLon = spot.StopLongitude.HasValue
                    ? spot.StopLongitude.Value : spot.Longitude;

                var nearNode = _graph.FindNearestNode(targetLat, targetLon);
                if (nearNode == null) continue;

                double dist = TaxiGraph.CalculateDistanceMeters(
                    nearNode.Latitude, nearNode.Longitude, targetLat, targetLon);
                if (dist > MAX_DEICE_TO_GRAPH_M) continue;

                _destinationNodeMap[label] = nearNode.NodeId;
                double stopHeading = spot.StopHeading.HasValue
                    ? spot.StopHeading.Value : spot.Heading;
                _destinationHeadingMap[label] = stopHeading;
                _destinationHeadingTrueMap[label] = stopHeading;
                _destinationThresholdMap[label] = (targetLat, targetLon);
                _destinationSpotMap[label] = spot;
                cmbDestination.Items.Add(label);
            }
        }
        else
        {
            // PARITY WITH THE GATE-TELEPORT DIALOG. Earlier the parking listing
            // was driven off graph nodes that happened to be tagged with a
            // ParkingName during graph build — which silently dropped any
            // parking spot whose lat/lon didn't have a nearby graph node
            // (common in third-party scenery whose taxi-path data lags the
            // parking layout). Result: a pilot given "Parking 21" by ATC
            // would see "Parking 21" in the gate teleport dialog but NOT in
            // the taxi guidance form — confusing and route-blocking.
            //
            // The fix: drive the dropdown directly from
            // `_dataProvider.GetParkingSpots(icao)` — the same data source
            // gate teleport uses — and use `ParkingSpot.ToString()` for
            // identical display labels (e.g. "P 21 - Ramp GA Large (Jetway)").
            // Each parking spot's actual lat/lon is the convergence target,
            // matching what TeleportToParkingSpot places you at, so taxi
            // guidance and gate teleport end up at the same physical position.
            // Routing endpoint = nearest graph node to the parking spot
            // (within 100 m); if the graph has no reachable node within that
            // radius, the spot is dropped — there's no way to taxi there.
            const double MAX_PARKING_TO_GRAPH_M = 100.0;

            // ── Load-once resolution (cached per airport) ──────────────────────
            // Resolve the heavy per-airport work — GetGates + per-spot nearest-node
            // lookup + distance gate — ONCE per ICAO into _cachedGateSpots. This is
            // what made every keystroke expensive: it re-enumerated GSX profile
            // directories / re-ran the uncached navdata DB query and walked the graph
            // per spot, synchronously on the UI thread (a screen-reader-responsiveness
            // hazard). The search text and fit filter do NOT affect node resolution,
            // so caching it is behaviour-preserving.
            if (_cachedGateSpots == null
                || !_cachedGateSpotsIcao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase))
            {
                var sourceSpots = (_gateSource?.GetGates(_currentIcao)) ?? _dataProvider.GetParkingSpots(_currentIcao);
                var resolved = new List<(ParkingSpot spot, int nodeId)>(sourceSpots.Count);
                foreach (var spot in sourceSpots)
                {
                    var nearNode = _graph.FindNearestNode(spot.Latitude, spot.Longitude);
                    if (nearNode == null) continue;

                    double dist = TaxiGraph.CalculateDistanceMeters(
                        nearNode.Latitude, nearNode.Longitude, spot.Latitude, spot.Longitude);
                    if (dist > MAX_PARKING_TO_GRAPH_M) continue;

                    resolved.Add((spot, nearNode.NodeId));
                }
                _cachedGateSpots = resolved;
                _cachedGateSpotsIcao = _currentIcao;
            }

            // ── Per-pass filter + ordering (cheap, in-memory) ─────────────────
            // Category display order matching GateTeleportForm: gates first
            // (small → extra), then ramp types, then dock/other.
            var categoryOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Gate Small"] = 1, ["Gate Medium"] = 2, ["Gate Large"] = 3,
                ["Gate Heavy"] = 4, ["Gate Extra"] = 5,
                ["Ramp GA"] = 6, ["Ramp Cargo"] = 7, ["Ramp Military"] = 8,
                ["Dock"] = 9, ["Other"] = 10
            };

            IEnumerable<(ParkingSpot spot, int nodeId)> filtered = _cachedGateSpots;

            // Gate search filter: type-to-filter on name+number+suffix. Run against
            // the cached resolved list per keystroke (GateSearchFilter operates on
            // ParkingSpot, so project, filter, then re-pair with the node id).
            if (!string.IsNullOrEmpty(txtGateSearch.Text))
            {
                var matched = new HashSet<ParkingSpot>(
                    Services.GateSearchFilter.Filter(_cachedGateSpots.Select(r => r.spot).ToList(), txtGateSearch.Text));
                filtered = filtered.Where(r => matched.Contains(r.spot));
            }

            // Wingspan filter: spot must be large enough for the aircraft. Applied
            // PER PASS against the cached (unfiltered) list — never baked into the
            // cache — because _aircraftWingspan can change between passes (mid-session
            // aircraft swap) and chkFitFilter toggles re-run PopulateDestinations.
            // Source-aware (see ParkingSpot.FitsAircraft): GSX spots use the
            // authoritative max wing span (metres); navdata spots use the physical
            // parking radius (feet). The old "Radius >= wingspan/2" mixed units for
            // GSX spots (metres vs a feet threshold) and filtered nearly everything out.
            if (chkFitFilter.Checked && _aircraftWingspan > 0)
                filtered = filtered.Where(r => r.spot.FitsAircraft(_aircraftWingspan));

            // Same ordering as before: category, then number, then name.
            var parkingSpots = filtered
                .OrderBy(r => categoryOrder.TryGetValue(r.spot.GetFilterCategory(), out int o) ? o : 99)
                .ThenBy(r => r.spot.Number > 0 ? r.spot.Number : int.MaxValue)
                .ThenBy(r => r.spot.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var (spot, nodeId) in parkingSpots)
            {
                // ParkingSpot.ToString() format matches the gate-teleport dialog.
                string label = spot.ToString();
                if (_destinationNodeMap.ContainsKey(label)) continue;

                _destinationNodeMap[label] = nodeId;
                _destinationHeadingMap[label] = spot.Heading;
                _destinationHeadingTrueMap[label] = spot.Heading; // parking heading is true heading
                _destinationThresholdMap[label] = (spot.Latitude, spot.Longitude);
                _destinationSpotMap[label] = spot;
                cmbDestination.Items.Add(label);
            }
        }

        if (cmbDestType.SelectedIndex == 2)
        {
            // Progressive Taxi: the gate/runway destination picker is hidden; the
            // leg ends at a terminator on the last taxiway row. We still build
            // _crossRunwayMap (name → Runway) here because the "After crossing
            // runway" and "Hold short of runway" terminators resolve the far-side
            // / near-side node from it at Calculate time (using the aircraft's
            // actual position to determine which side of the runway it's on).
            // GetRunways returns BOTH ends as separate entries (e.g. "10R" and
            // "28L"); listing both lets the pilot match whichever designator ATC
            // named without converting to the reciprocal. The runway TARGET is
            // picked in the last row's "Hold short of runway" combo (populated
            // from _airportRunwayIds); the taxiway TARGET is cmbTerminatorTaxiway.
            foreach (var rwy in _dataProvider.GetRunways(_currentIcao).Where(r => !r.IsClosed))
            {
                string label = $"Runway {rwy.RunwayID}";
                if (!_crossRunwayMap.ContainsKey(label))
                    _crossRunwayMap[label] = rwy;
            }

            // Taxiway target list for the "Hold short of taxiway" terminator.
            cmbTerminatorTaxiway.Items.Clear();
            foreach (var name in _graph.GetAllTaxiwayNames())
                cmbTerminatorTaxiway.Items.Add(name);
            if (cmbTerminatorTaxiway.Items.Count > 0)
                cmbTerminatorTaxiway.SelectedIndex = 0;
        }

        if (cmbDestination.Items.Count > 0)
            cmbDestination.SelectedIndex = 0;
    }

    private void PopulateFirstTaxiway()
    {
        if (_graph == null) return;

        cmbFirstTaxiway.Items.Clear();

        // Add "(None - calculate shortest path)" as first option
        cmbFirstTaxiway.Items.Add("(None - calculate shortest path)");

        // Get taxiways sorted by distance, closest first
        var sorted = _graph.GetTaxiwayNamesSortedByDistance(_aircraftLat, _aircraftLon, _aircraftHeading);

        foreach (var name in sorted)
            cmbFirstTaxiway.Items.Add(name);

        // Select the closest taxiway in the aircraft's direction
        string? closest = _graph.GetClosestTaxiwayInDirection(_aircraftLat, _aircraftLon, _aircraftHeading);
        if (closest != null)
        {
            int idx = cmbFirstTaxiway.Items.IndexOf(closest);
            if (idx >= 0)
                cmbFirstTaxiway.SelectedIndex = idx;
            else
                cmbFirstTaxiway.SelectedIndex = 0;
        }
        else
        {
            cmbFirstTaxiway.SelectedIndex = 0;
        }
    }

    private void OnDestTypeChanged(object? sender, EventArgs e)
    {
        bool isGate = cmbDestType.SelectedIndex == 1;
        bool isProgressive = cmbDestType.SelectedIndex == 2;
        chkFitFilter.Visible = isGate && _aircraftWingspan > 0;
        lblGateSearch.Visible = isGate;
        txtGateSearch.Visible = isGate;
        if (!isGate)
            txtGateSearch.Text = string.Empty;

        // Progressive Taxi has no final destination — hide the gate/runway
        // destination picker and route to a terminator on the last taxiway row
        // instead. Other destination types restore the picker (mirrors the gate
        // visibility toggling above).
        lblDestination.Visible = !isProgressive;
        cmbDestination.Visible = !isProgressive;

        PopulateDestinations();
        RefreshTerminatorRow();

        // Announce "no deicing areas" immediately after populating so the user
        // knows before pressing Calculate that the airport has nothing to route to.
        if (cmbDestType.SelectedIndex == 3 && cmbDestination.Items.Count == 0)
            _announcer.AnnounceImmediate("No deicing areas at this airport.");
    }

    private void OnFirstTaxiwayChanged(object? sender, EventArgs e)
    {
        // Clear all additional taxiways when first taxiway changes
        ClearAllAdditionalTaxiways();

        string? selected = cmbFirstTaxiway.SelectedItem?.ToString();
        bool isTaxiwaySelected = !string.IsNullOrEmpty(selected) && !selected.StartsWith("(None");

        // Enable "Add Taxiway" only when an actual taxiway is selected
        btnAddTaxiway.Enabled = isTaxiwaySelected;
    }

    private void OnAddTaxiwayClicked(object? sender, EventArgs e)
    {
        if (_graph == null) return;
        if (_additionalTaxiways.Count >= MAX_ADDITIONAL_TAXIWAYS) return;

        // Determine which taxiway was selected last (first taxiway or last additional)
        string? previousTaxiway;
        if (_additionalTaxiways.Count == 0)
        {
            previousTaxiway = cmbFirstTaxiway.SelectedItem?.ToString();
        }
        else
        {
            previousTaxiway = _additionalTaxiways[^1].combo.SelectedItem?.ToString();
        }

        if (string.IsNullOrEmpty(previousTaxiway) || previousTaxiway.StartsWith("(None"))
            return;

        // Get heuristically-connected taxiways (within 2 named-taxiway crossings).
        // The dropdown then lists ALL airport taxiways: connected ones first
        // (sorted by aircraft distance, the most-likely-relevant ordering for
        // ATC-issued clearances), then any remaining airport taxiways
        // alphabetically. Showing the full list as a fallback covers cases
        // where the connectivity heuristic misses an unusual graph layout, or
        // where ATC issues a clearance that skips ahead in the network. The
        // router's `FindRunwayBridge` and constrained-path logic still
        // resolve the actual route — this is purely UX so the user can match
        // what ATC said even when the heuristic doesn't surface it.
        //
        // Duplicate taxiways are allowed: ATC clearances like "via C, hold
        // short 04L, C" at KBOS need to re-use a taxiway across a runway
        // crossing. The router handles consecutive duplicates as a benign
        // no-op step (FindBestIntersection resolves to the current node and
        // the currentNode == targetNode short-circuit at TaxiRouter.cs skips
        // the redundant step); the per-row user hold-short on the first
        // occurrence still tags the correct segment via
        // ApplyUserRunwayHoldShorts.
        //
        // The immediately-previous taxiway is hidden ONLY when the previous
        // slot has no hold-short configured. Without a hold-short, picking
        // the same taxiway twice in a row is a no-op click error. With a
        // hold-short (either the "Hold short" checkbox OR a runway selected
        // in the per-row "Hold short of runway" combo), the same-taxiway
        // duplicate is a legitimate clearance pattern: taxi to the
        // hold-short line, hold until ATC clears the crossing, resume on
        // the same taxiway on the far side. Without this conditional
        // relaxation, KBOS clearances like "K, B, N, hold short 15R, N,
        // hold short 22R, N" cannot be entered literally — the second and
        // third N never appear in the dropdown.
        bool prevHasHoldShort;
        if (_additionalTaxiways.Count == 0)
        {
            string? firstRwy = cmbFirstHoldShortRunway.SelectedItem?.ToString();
            prevHasHoldShort =
                chkFirstHoldShort.Checked ||
                (!string.IsNullOrEmpty(firstRwy) && firstRwy != NO_RUNWAY_HOLDSHORT);
        }
        else
        {
            var (_, _, hsChk, hsRwy, _) = _additionalTaxiways[^1];
            string? rwy = hsRwy.SelectedItem?.ToString();
            prevHasHoldShort =
                hsChk.Checked ||
                (!string.IsNullOrEmpty(rwy) && rwy != NO_RUNWAY_HOLDSHORT);
        }

        // Single predicate used in both filter sites below so a future
        // edit can't drift one site out of sync with the other.
        bool ShouldKeep(string n) =>
            prevHasHoldShort ||
            !n.Equals(previousTaxiway, StringComparison.OrdinalIgnoreCase);

        var connected = _graph.GetConnectedTaxiwayNames(previousTaxiway);

        var connectedAvailable = connected
            .Where(ShouldKeep)
            .ToList();

        var connectedSet = new HashSet<string>(connectedAvailable, StringComparer.OrdinalIgnoreCase);
        var otherAirportTaxiways = _graph.GetAllTaxiwayNames()
            .Where(n => !connectedSet.Contains(n))
            .Where(ShouldKeep)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Sort connected taxiways by distance from aircraft (most relevant first)
        connectedAvailable = SortTaxiwaysByDistance(connectedAvailable);

        var available = new List<string>();
        available.AddRange(connectedAvailable);
        available.AddRange(otherAirportTaxiways);

        if (available.Count == 0)
        {
            _announcer.Announce("No additional taxiways available at this airport.");
            return;
        }

        // Create new combo and label. Row layout (DYNAMIC_ROW_HEIGHT_PX = 80
        // px tall): label / taxiway-combo + hold-short checkbox + remove button
        // on line 1, then "Hold short of runway" combo on line 2 below. The
        // 80-px height is wide enough for two readable lines without crowding.
        int index = _additionalTaxiways.Count;
        int panelY = index * DYNAMIC_ROW_HEIGHT_PX;

        // Mnemonics: the combo label gets `Taxiway &N:` for N in 2..9, giving
        // Alt+2 .. Alt+9 to jump straight to that taxiway slot. Past 9, no
        // unique single-digit mnemonic exists, so we omit the ampersand. The
        // checkbox uses `&Hold short` and the button uses `&Remove` — both
        // shared across all dynamic instances, which is fine: Windows cycles
        // through duplicates with repeated Alt-key, and the user can also Tab.
        int taxiwayNumber = index + 2;
        string labelText = taxiwayNumber <= 9
            ? $"Taxiway &{taxiwayNumber}:"
            : $"Taxiway {taxiwayNumber}:";

        var label = new Label
        {
            Text = labelText,
            Location = new System.Drawing.Point(0, panelY),
            AutoSize = true,
            AccessibleName = $"Taxiway {taxiwayNumber} Label"
        };

        var combo = new ComboBox
        {
            Location = new System.Drawing.Point(0, panelY + 18),
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = $"Taxiway {taxiwayNumber}",
            AccessibleDescription = $"Select the next taxiway in the route sequence. Connected taxiways appear first; all other airport taxiways follow."
        };

        combo.Items.Add("(None - end here)");
        foreach (var name in available)
            combo.Items.Add(name);
        combo.SelectedIndex = 0;

        int capturedIndex = index;
        combo.SelectedIndexChanged += (s, ev) => OnAdditionalTaxiwayChanged(capturedIndex);

        var holdShortChk = new CheckBox
        {
            Text = "&Hold short",
            Location = new System.Drawing.Point(210, panelY + 20),
            Width = 85,
            AccessibleName = $"Hold short after taxiway {taxiwayNumber}",
            AccessibleDescription = $"When checked, guidance will stop at the end of taxiway {taxiwayNumber} and wait for you to continue"
        };

        var removeBtn = new Button
        {
            Text = "&Remove",
            Location = new System.Drawing.Point(300, panelY + 17),
            Width = 70,
            Height = 24,
            AccessibleName = $"Remove taxiway {taxiwayNumber}",
            AccessibleDescription = $"Remove taxiway {taxiwayNumber} and all subsequent taxiways from the route"
        };
        int removeIndex = index;
        removeBtn.Click += (s, ev) => RemoveTaxiwaysFrom(removeIndex);

        // Line 2 of the row: "Hold short of runway" combo. Lets the user
        // EXPLICITLY annotate an ATC-instructed runway hold-short between
        // this taxiway and the next. Auto-detection still runs over the
        // whole route (so leaving this at "(none)" loses nothing); the
        // explicit picker confirms the SPECIFIC runway the controller named.
        // Same Alt+O mnemonic as the first-row combo — Windows will cycle
        // Alt+O across all instances, identical to the Hold-short checkbox
        // (Alt+H) and Remove button (Alt+R) cycle behaviour.
        var lblRunwayHs = new Label
        {
            Text = HOLD_SHORT_RUNWAY_LABEL,
            Location = new System.Drawing.Point(0, panelY + 45),
            AutoSize = true,
            AccessibleName = $"Hold short of runway after taxiway {taxiwayNumber} label"
        };
        var holdShortRunwayCmb = new ComboBox
        {
            Location = new System.Drawing.Point(180, panelY + 43),
            Width = 190,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = $"Hold short of runway after taxiway {taxiwayNumber}",
            AccessibleDescription = $"Optional: pick a runway to hold short of after taxiway {taxiwayNumber}. Use when ATC explicitly assigns a hold-short clearance for a runway your route crosses. Leave at \"(none)\" to rely on automatic runway-crossing detection."
        };
        RebuildHoldShortRunwayCombo(holdShortRunwayCmb);

        // Tab order WITHIN the panel: each new group is added at the end so
        // pressing Tab inside the panel walks through Combo → Hold-short →
        // Remove → Hold-short-of-runway for slot 2, then slot 3, etc. The
        // panel's overall slot in the FORM tab order is fixed at the position
        // set in InitializeFormControls (between Add Taxiway and Calculate),
        // so the user never has to tab backwards from Calculate to reach a
        // newly-added taxiway.
        int innerTab = pnlTaxiways.Controls.Count; // labels & controls share one stream
        combo.TabIndex = innerTab + 1;
        holdShortChk.TabIndex = innerTab + 2;
        removeBtn.TabIndex = innerTab + 3;
        holdShortRunwayCmb.TabIndex = innerTab + 4;

        pnlTaxiways.Controls.Add(label);
        pnlTaxiways.Controls.Add(combo);
        pnlTaxiways.Controls.Add(holdShortChk);
        pnlTaxiways.Controls.Add(removeBtn);
        pnlTaxiways.Controls.Add(lblRunwayHs);
        pnlTaxiways.Controls.Add(holdShortRunwayCmb);

        _additionalTaxiways.Add((label, combo, holdShortChk, holdShortRunwayCmb, removeBtn));

        // Update panel height and reposition controls below. RefreshTerminatorRow
        // relocates the Progressive Taxi terminator block onto this new last row
        // (and calls UpdateLayout to resize).
        RefreshTerminatorRow();

        // Focus the new combo
        combo.Focus();

        // Update Add Taxiway button state
        UpdateAddTaxiwayButtonState();
    }

    private void OnAdditionalTaxiwayChanged(int index)
    {
        // Remove all taxiways after this one
        RemoveTaxiwaysAfter(index);

        // Update Add Taxiway button state
        UpdateAddTaxiwayButtonState();
    }

    private void RemoveTaxiwaysFrom(int fromIndex)
    {
        // Remove this taxiway and all after it. The 5-tuple gained the
        // hold-short-of-runway combo; we also need to find and remove the
        // small "Hold short of runway:" label that accompanies it (it's not
        // tracked in the tuple to keep the destructuring tidier; we look it
        // up by Y-coordinate in the panel below). Both the combo and that
        // label live on line 2 of the row.
        while (_additionalTaxiways.Count > fromIndex)
        {
            var (label, combo, holdShortChk, holdShortRunwayCmb, removeBtn) = _additionalTaxiways[^1];
            int rowIdx = _additionalTaxiways.Count - 1;
            int line2Y = rowIdx * DYNAMIC_ROW_HEIGHT_PX + 45;

            // The companion "Hold short of runway:" label sits at Y == line2Y
            // and is the only Label in the panel at that Y other than the
            // taxiway-row label (which is at Y == row's panelY, not panelY+45).
            Label? companionLabel = null;
            foreach (Control c in pnlTaxiways.Controls)
            {
                if (c is Label l && l.Location.Y == line2Y)
                {
                    companionLabel = l;
                    break;
                }
            }

            pnlTaxiways.Controls.Remove(label);
            pnlTaxiways.Controls.Remove(combo);
            pnlTaxiways.Controls.Remove(holdShortChk);
            pnlTaxiways.Controls.Remove(removeBtn);
            pnlTaxiways.Controls.Remove(holdShortRunwayCmb);
            if (companionLabel != null) pnlTaxiways.Controls.Remove(companionLabel);

            label.Dispose();
            combo.Dispose();
            holdShortChk.Dispose();
            removeBtn.Dispose();
            holdShortRunwayCmb.Dispose();
            companionLabel?.Dispose();
            _additionalTaxiways.RemoveAt(_additionalTaxiways.Count - 1);
        }

        // RefreshTerminatorRow relocates the terminator block onto the new last
        // row (and calls UpdateLayout to resize).
        RefreshTerminatorRow();
        UpdateAddTaxiwayButtonState();
    }

    private void RemoveTaxiwaysAfter(int afterIndex)
    {
        RemoveTaxiwaysFrom(afterIndex + 1);
    }

    private void ClearAllAdditionalTaxiways()
    {
        RemoveTaxiwaysFrom(0);
    }

    private void UpdateAddTaxiwayButtonState()
    {
        if (_additionalTaxiways.Count >= MAX_ADDITIONAL_TAXIWAYS)
        {
            btnAddTaxiway.Enabled = false;
            return;
        }

        // Check if the last selected taxiway is a real taxiway (not "(None...)")
        string? lastSelected;
        if (_additionalTaxiways.Count == 0)
        {
            lastSelected = cmbFirstTaxiway.SelectedItem?.ToString();
        }
        else
        {
            lastSelected = _additionalTaxiways[^1].combo.SelectedItem?.ToString();
        }

        btnAddTaxiway.Enabled = !string.IsNullOrEmpty(lastSelected) && !lastSelected.StartsWith("(None");
    }

    private List<string> GetSelectedTaxiwayNames()
    {
        var names = new List<string>();

        string? first = cmbFirstTaxiway.SelectedItem?.ToString();
        if (!string.IsNullOrEmpty(first) && !first.StartsWith("(None"))
            names.Add(first);

        foreach (var (_, combo, _, _, _) in _additionalTaxiways)
        {
            string? sel = combo.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(sel) && !sel.StartsWith("(None"))
                names.Add(sel);
        }

        return names;
    }

    /// <summary>
    /// Gets the indices (in the taxiway sequence) where the user has requested hold-short.
    /// Index 0 = first taxiway, index 1 = second taxiway (first additional), etc.
    /// </summary>
    private List<int> GetUserHoldShortIndices()
    {
        var indices = new List<int>();

        // Check first taxiway hold-short
        string? first = cmbFirstTaxiway.SelectedItem?.ToString();
        if (!string.IsNullOrEmpty(first) && !first.StartsWith("(None") && chkFirstHoldShort.Checked)
            indices.Add(0);

        // Check additional taxiway hold-shorts
        int seqIndex = 1;
        foreach (var (_, combo, holdShortChk, _, _) in _additionalTaxiways)
        {
            string? sel = combo.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(sel) && !sel.StartsWith("(None"))
            {
                if (holdShortChk.Checked)
                    indices.Add(seqIndex);
                seqIndex++;
            }
        }

        return indices;
    }

    /// <summary>
    /// Reads each taxiway row's "Hold short of runway" combo and returns a
    /// dictionary mapping the taxiway-sequence index (0 = first taxiway, 1 =
    /// taxiway 2, …) to the runway designator the user wants to hold short
    /// of AFTER that taxiway. Only includes rows where a real runway was
    /// selected (skipping "(none)"). Sequence indices match what the router
    /// uses for ApplyUserHoldShorts so the same lookup logic can find the
    /// last segment of the matching taxiway and tag the next route segment
    /// as the hold-short for the requested runway.
    /// </summary>
    private Dictionary<int, string> GetUserRunwayHoldShorts()
    {
        var result = new Dictionary<int, string>();

        // First taxiway slot — only meaningful if a real taxiway is selected.
        string? firstTaxi = cmbFirstTaxiway.SelectedItem?.ToString();
        string? firstRwy = cmbFirstHoldShortRunway.SelectedItem?.ToString();
        if (!string.IsNullOrEmpty(firstTaxi) && !firstTaxi.StartsWith("(None") &&
            !string.IsNullOrEmpty(firstRwy) && firstRwy != NO_RUNWAY_HOLDSHORT)
        {
            result[0] = firstRwy;
        }

        // Dynamic taxiway rows.
        int seqIndex = 1;
        foreach (var (_, combo, _, holdShortRunwayCmb, _) in _additionalTaxiways)
        {
            string? sel = combo.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(sel) && !sel.StartsWith("(None"))
            {
                string? rwy = holdShortRunwayCmb.SelectedItem?.ToString();
                if (!string.IsNullOrEmpty(rwy) && rwy != NO_RUNWAY_HOLDSHORT)
                    result[seqIndex] = rwy;
                seqIndex++;
            }
        }

        return result;
    }

    private List<string> SortTaxiwaysByDistance(List<string> taxiwayNames)
    {
        if (_graph == null) return taxiwayNames;

        return taxiwayNames
            .Select(name =>
            {
                double minDist = double.MaxValue;
                foreach (var node in _graph.Nodes.Values)
                {
                    if (node.TaxiwayNames.Contains(name))
                    {
                        double d = TaxiGraph.CalculateDistanceMeters(
                            _aircraftLat, _aircraftLon, node.Latitude, node.Longitude);
                        if (d < minDist) minDist = d;
                    }
                }
                return (name, minDist);
            })
            .OrderBy(x => x.minDist)
            .Select(x => x.name)
            .ToList();
    }

    /// <summary>
    /// Progressive Taxi (dest type index 2): position the terminator type/target
    /// combos on the CURRENT last taxiway row and show them; otherwise hide them.
    /// The chosen terminator therefore travels with "the last row" — add/remove
    /// row handlers and OnDestTypeChanged all call this so the control relocates.
    /// Always finishes by calling UpdateLayout so the panel/form grows to fit.
    /// </summary>
    private void RefreshTerminatorRow()
    {
        bool progressive = cmbDestType.SelectedIndex == 2;
        if (!progressive)
        {
            lblTerminatorType.Visible = false;
            cmbTerminatorType.Visible = false;
            lblTerminatorTaxiway.Visible = false;
            cmbTerminatorTaxiway.Visible = false;
            UpdateLayout();
            return;
        }

        // The terminator block sits just below the current last taxiway row,
        // inside pnlTaxiways. When no additional rows exist, the "last row" is
        // the first-taxiway slot (outside the panel) and the block sits at the
        // top of the panel (panelY 0).
        int blockY = _additionalTaxiways.Count * DYNAMIC_ROW_HEIGHT_PX;

        lblTerminatorType.Location = new System.Drawing.Point(0, blockY + 2);
        cmbTerminatorType.Location = new System.Drawing.Point(140, blockY);
        lblTerminatorType.Visible = true;
        cmbTerminatorType.Visible = true;

        // Taxiway target only applies to "Hold short of taxiway" (index 1).
        bool needTaxiwayTarget = cmbTerminatorType.SelectedIndex == 1;
        lblTerminatorTaxiway.Location = new System.Drawing.Point(0, blockY + 30);
        cmbTerminatorTaxiway.Location = new System.Drawing.Point(180, blockY + 28);
        lblTerminatorTaxiway.Visible = needTaxiwayTarget;
        cmbTerminatorTaxiway.Visible = needTaxiwayTarget;

        UpdateLayout();
    }

    private void UpdateLayout()
    {
        // Resize panel to fit all additional taxiways. Each row is
        // DYNAMIC_ROW_HEIGHT_PX tall (two-line: combo + hold-short + remove on
        // top, runway-hold-short combo on bottom).
        int panelHeight = _additionalTaxiways.Count * DYNAMIC_ROW_HEIGHT_PX;
        // Reserve space for the Progressive Taxi terminator block when it is
        // shown below the last row (cmbTerminatorType.Visible is set by
        // RefreshTerminatorRow before this runs).
        if (cmbTerminatorType.Visible)
            panelHeight += TERMINATOR_BLOCK_HEIGHT_PX;
        pnlTaxiways.Height = panelHeight;

        // Reposition buttons and status below the panel
        int y = pnlTaxiways.Location.Y + panelHeight;
        if (panelHeight > 0) y += 5;

        btnCalculate.Location = new System.Drawing.Point(15, y);
        btnStop.Location = new System.Drawing.Point(15 + 190, y);
        y += 40;

        lblStatus.Location = new System.Drawing.Point(15, y);
        y += 40;

        // Resize form to fit
        int formHeight = y + 50;
        if (formHeight < 480) formHeight = 480;
        this.ClientSize = new System.Drawing.Size(this.ClientSize.Width, formHeight);
    }

    private void OnCalculateClicked(object? sender, EventArgs e)
    {
        if (_graph == null)
        {
            _announcer.Announce("No airport loaded. Enter an ICAO code first.");
            return;
        }

        // Refresh aircraft position from the latest SimConnect sample (if
        // available) before route construction. Without this, the route starts
        // from wherever the aircraft was when the form was OPENED — typically
        // pre-pushback, several meters from where the post-pushback aircraft
        // actually is. The off-route detector then fires within seconds of the
        // pilot starting to taxi because they were "off route" from the very
        // first frame. LastKnownPosition is updated by every position-bearing
        // SimConnect sample (visual guidance, hand-fly, etc.) so it's almost
        // always within a frame of the truth even when no taxi-specific
        // position monitor is active yet.
        if (_simConnectManager?.LastKnownPosition is { } pos)
        {
            _aircraftLat = pos.Latitude;
            _aircraftLon = pos.Longitude;
            _aircraftHeading = pos.HeadingMagnetic;
        }

        // Progressive Taxi: resolve the last-row terminator to a destination
        // node + descriptor and route to it. No final gate/runway, no lineup,
        // no Takeoff-Assist — guidance ends in the manager's ProgressiveHold
        // state and announces the hold/end (see TaxiGuidanceManager.HandleArrival).
        if (cmbDestType.SelectedIndex == 2)
        {
            var progSeq = GetSelectedTaxiwayNames();
            string? lastTaxiway = progSeq.Count > 0 ? progSeq[^1] : null;
            if (string.IsNullOrEmpty(lastTaxiway))
            {
                _announcer.Announce("Select at least one taxiway for progressive taxi.");
                return;
            }

            // Component + start node for the graph-distance terminator helpers,
            // mirroring FindFarSideRunwayNode's aircraft-component restriction so
            // the resolved node is actually reachable from the aircraft.
            var startNode = _graph.FindNearestNode(_aircraftLat, _aircraftLon);
            if (startNode == null)
            {
                _announcer.Announce("Could not find your position on the taxi network.");
                return;
            }
            int destComponentId = startNode.ComponentId;

            // The runway TARGET is the last row's "Hold short of runway" combo;
            // the taxiway TARGET is cmbTerminatorTaxiway.
            string runwayTarget = LastRowHoldShortRunway();   // bare designator, "" if none
            string taxiwayTarget = cmbTerminatorTaxiway.SelectedItem?.ToString() ?? "";

            int terminatorTypeIndex = cmbTerminatorType.SelectedIndex;
            int destNode = -1;
            ProgressiveTerminator term;
            var progRwyHoldShorts = GetUserRunwayHoldShorts();

            // For the runway-type terminators (Hold short of runway / After
            // crossing runway), the LAST row's "Hold short of runway" combo IS
            // the terminator target — not an intermediate hold-short. Drop it
            // from the per-row hold-short map so the terminator owns it (avoids a
            // redundant tag and a spurious "not on route" mismatch warning). For
            // the taxiway/end terminators the last-row runway combo is a genuine
            // intermediate hold-short and is kept.
            if (terminatorTypeIndex == 0 || terminatorTypeIndex == 2)
                progRwyHoldShorts.Remove(progSeq.Count - 1);

            switch (terminatorTypeIndex)
            {
                case 0: // Hold short of runway
                {
                    if (string.IsNullOrEmpty(runwayTarget))
                    {
                        _announcer.Announce("Pick the runway to hold short of in the Hold short of runway combo on the last taxiway row.");
                        return;
                    }
                    // Route to the near-side hold-short node so guidance ENDS at
                    // the hold line (where ProgressiveHold fires) — NOT past the
                    // runway. (ApplyUserRunwayHoldShorts only TAGS an intermediate
                    // hold-short; it does not truncate the route, so routing to the
                    // far-side node would carry the leg across the runway before the
                    // terminal announcement. The near-side node is correct.)
                    var hsNode = ResolveHoldShortRunwayNode(runwayTarget);
                    if (hsNode != null) destNode = hsNode.NodeId;
                    term = new ProgressiveTerminator(ProgressiveTerminatorType.HoldShortRunway, runwayTarget);
                    break;
                }
                case 1: // Hold short of taxiway
                {
                    if (string.IsNullOrEmpty(taxiwayTarget))
                    {
                        _announcer.Announce("Pick the taxiway to hold short of.");
                        return;
                    }
                    destNode = _graph.FindTaxiwayIntersectionNode(lastTaxiway, taxiwayTarget, destComponentId);
                    term = new ProgressiveTerminator(ProgressiveTerminatorType.HoldShortTaxiway, taxiwayTarget);
                    break;
                }
                case 2: // After crossing runway
                {
                    if (string.IsNullOrEmpty(runwayTarget))
                    {
                        _announcer.Announce("Pick the runway to cross in the Hold short of runway combo on the last taxiway row.");
                        return;
                    }
                    if (_crossRunwayMap.TryGetValue($"Runway {runwayTarget}", out var crossRwy))
                    {
                        var farNode = FindFarSideRunwayNode(crossRwy);
                        if (farNode != null) destNode = farNode.NodeId;
                    }
                    term = new ProgressiveTerminator(ProgressiveTerminatorType.AfterCrossingRunway, runwayTarget);
                    break;
                }
                default: // 3: End of last taxiway
                {
                    destNode = _graph.FindTaxiwayEndNode(startNode.NodeId, lastTaxiway);
                    term = new ProgressiveTerminator(ProgressiveTerminatorType.EndOfTaxiway, "");
                    break;
                }
            }

            if (destNode < 0)
            {
                string what = terminatorTypeIndex == 1 ? $"taxiway {taxiwayTarget}"
                    : terminatorTypeIndex == 3 ? $"the end of taxiway {lastTaxiway}"
                    : $"runway {runwayTarget}";
                string msg = $"Could not find {what} from {lastTaxiway}. Check your entry.";
                _announcer.Announce(msg);
                lblStatus.Text = msg;
                return;
            }

            var progHoldShorts = GetUserHoldShortIndices();
            var progSettings = SettingsManager.Current;
            string progDestName = term.Type switch
            {
                ProgressiveTerminatorType.HoldShortRunway => $"hold short of runway {runwayTarget}",
                ProgressiveTerminatorType.HoldShortTaxiway => $"hold short of taxiway {taxiwayTarget}",
                ProgressiveTerminatorType.AfterCrossingRunway => $"across runway {runwayTarget}",
                _ => $"end of taxiway {lastTaxiway}",
            };

            string? progError = _guidanceManager.LoadRoute(
                _dataProvider, _currentIcao,
                _aircraftLat, _aircraftLon, _aircraftHeading,
                destNode, progDestName,
                progSeq.Count > 0 ? progSeq : null,
                progHoldShorts,
                destinationHeading: null,
                destinationThresholdLat: null, destinationThresholdLon: null,
                destinationHeadingTrue: null,
                isRunwayDestination: false,
                prebuiltGraph: _graph,
                userRunwayHoldShorts: progRwyHoldShorts.Count > 0 ? progRwyHoldShorts : null,
                progressiveTerminator: term);

            if (progError != null)
            {
                _announcer.Announce(progError);
                lblStatus.Text = progError;
                txtRouteSummary.Text = progError;
                return;
            }

            // A progressive leg is never a gate/runway lineup — clear any prior
            // docking target so a stale gate doesn't engage near the terminator.
            _dockingManager?.SetDestinationGate(null);

            txtRouteSummary.Text = _guidanceManager.LastRouteSummary;
            lblStatus.Text = "Route loaded. Guidance active.";
            _guidanceManager.StartGuidance(progSettings);
            return;
        }

        // Get destination
        string? destName = cmbDestination.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(destName) || !_destinationNodeMap.TryGetValue(destName, out int destNodeId))
        {
            _announcer.Announce("Please select a destination.");
            return;
        }

        // Collect selected taxiways and hold-short points
        var taxiwaySequence = GetSelectedTaxiwayNames();
        var userHoldShorts = GetUserHoldShortIndices();
        var userRunwayHoldShorts = GetUserRunwayHoldShorts();

        // Load route through guidance manager
        var settings = SettingsManager.Current;
        double? destHeading = _destinationHeadingMap.TryGetValue(destName, out double h) ? h : null;
        double? destHeadingTrue = _destinationHeadingTrueMap.TryGetValue(destName, out double ht) ? ht : null;
        double? thresholdLat = null, thresholdLon = null;
        if (_destinationThresholdMap.TryGetValue(destName, out var threshold))
        {
            thresholdLat = threshold.lat;
            thresholdLon = threshold.lon;
        }
        bool isRunwayDest = cmbDestType.SelectedIndex == 0;
        string? error = _guidanceManager.LoadRoute(
            _dataProvider, _currentIcao,
            _aircraftLat, _aircraftLon, _aircraftHeading,
            destNodeId, destName,
            taxiwaySequence.Count > 0 ? taxiwaySequence : null,
            userHoldShorts,
            destHeading,
            thresholdLat, thresholdLon, destHeadingTrue,
            isRunwayDest,
            prebuiltGraph: _graph,
            userRunwayHoldShorts: userRunwayHoldShorts.Count > 0 ? userRunwayHoldShorts : null);

        if (error != null)
        {
            _announcer.Announce(error);
            lblStatus.Text = error;
            txtRouteSummary.Text = error;
            return;
        }

        // Capture the spoken summary into the read-only box so the user
        // can re-read it later — screen readers often interrupt the
        // spoken version, and the shortest-path branch produces a
        // particularly long string. This is the only place that surfaces
        // what the router actually decided when no taxiways were picked.
        txtRouteSummary.Text = _guidanceManager.LastRouteSummary;
        lblStatus.Text = "Route loaded. Guidance active.";

        // Docking guidance: set the target gate unconditionally when heading to
        // a gate (independent of GSX setting / availability), or clear it when
        // heading to a runway so a prior gate target doesn't persist.
        if (isRunwayDest)
        {
            _dockingManager?.SetDestinationGate(null);
        }
        else
        {
            _destinationSpotMap.TryGetValue(destName, out var destSpot);
            _dockingManager?.SetDestinationGate(destSpot);
            ApplyGsxStopOffset(destSpot);
        }

        CheckGateOccupancy(isRunwayDest, thresholdLat, thresholdLon);

        _guidanceManager.StartGuidance(settings);

        // GSX gate auto-select: fire-and-forget when heading to a gate and
        // the feature is enabled. Conditions:
        //   - destination is a gate (not runway, not progressive taxi, not deice area)
        //   - setting is on
        //   - a selector was provided (i.e. GsxService exists in this session)
        //   - GSX CouatlStarted is confirmed via the selector being non-null
        //     (the selector is only built by MainForm when _gsxService != null)
        //     PLUS the CouatlStarted live check here, so we don't drive the
        //     menu when GSX hasn't started yet this session.
        // NOTE: deice areas (index 3) are explicitly excluded — SelectGateAsync
        // drives the GSX parking-gate menu, which has no deice-pad entries.
        // DockingGuidanceManager handles deice guidance via SetDestinationGate
        // (spot.IsDeiceArea is true) without any GSX menu interaction.
        if (!isRunwayDest
            && cmbDestType.SelectedIndex != 3
            && SettingsManager.Current.GsxAutoSelectGateOnRoute
            && _gsxGateSelector != null
            && _gsxGateSelector.CouatlStarted
            && _destinationSpotMap.TryGetValue(destName, out var gsxSpot))
        {
            // The selector itself announces its outcome and never throws.
            // Do NOT await — route loading must not block on GSX menu navigation.
            _ = _gsxGateSelector.SelectGateAsync(gsxSpot);
        }

        // Form stays open so the user can read the summary box while
        // guidance is active. They close it manually with Escape / window-X
        // or by switching focus elsewhere; Stop Guidance button is also
        // available without re-opening.
    }

    private void CheckGateOccupancy(bool isRunwayDest, double? gateLat, double? gateLon)
    {
        if (isRunwayDest || _tcasService == null || gateLat == null || gateLon == null) return;

        // ~55 m — tight enough to distinguish adjacent gates but large enough
        // to catch an aircraft that has stopped just short of the spot centre.
        const double GATE_OCCUPIED_NM = 0.030;

        // Force a fresh SimConnect traffic request before reading the snapshot.
        // TcasService's own 3-second poll timer may have just ticked; without
        // PollNow() the snapshot could be up to ~3 s stale when the user clicks
        // Calculate, and an aircraft that just spawned at the gate would be missed.
        // The request is asynchronous so a brand-new occupant within the last
        // few hundred ms can still slip through, but the staleness window
        // shrinks from "up to 3 s" to "one SimConnect roundtrip ≈ 33 ms".
        _tcasService.PollNow();

        var occupying = _tcasService.GetTraffic(onGround: true)
            .FirstOrDefault(t => NavigationCalculator.CalculateDistance(
                gateLat.Value, gateLon.Value, t.Latitude, t.Longitude) <= GATE_OCCUPIED_NM);

        if (occupying == null) return;

        string who = string.IsNullOrWhiteSpace(occupying.Callsign)
            ? "an aircraft"
            : occupying.Callsign;
        // AnnounceImmediate — the very next line in OnCalculateClicked is
        // StartGuidance, which announces the (long) route summary. A queued
        // gate warning would be drowned out behind it.
        _announcer.AnnounceImmediate($"Warning: {who} is at the destination gate.");
    }

    /// <summary>
    /// Reads the CURRENT last taxiway row's "Hold short of runway" combo and
    /// returns the bare runway designator selected there (e.g. "09L"), or "" when
    /// none is picked. The last row is the last additional row, or the first
    /// taxiway slot when there are no additional rows. Used by the Progressive
    /// Taxi terminator to resolve the runway target (the runway-case terminators
    /// reuse this per-row combo rather than a separate picker).
    /// </summary>
    private string LastRowHoldShortRunway()
    {
        ComboBox combo = _additionalTaxiways.Count > 0
            ? _additionalTaxiways[^1].holdShortRunway
            : cmbFirstHoldShortRunway;
        string? sel = combo.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(sel) || sel == NO_RUNWAY_HOLDSHORT) return "";
        return sel;
    }

    /// <summary>
    /// Resolves the hold-short node for Progressive Taxi's "hold short of runway"
    /// terminator: the graph node nearest the chosen runway's centerline on the
    /// aircraft's OWN side (the side it is currently on), restricted to the
    /// aircraft's connected component. Routing the leg to THIS node ends guidance
    /// at the hold line (where the manager's ProgressiveHold fires) rather than
    /// past the runway. Returns null if no near-side node is found (caller treats
    /// that as the "could not find" mismatch).
    ///
    /// Geometry mirrors <see cref="FindFarSideRunwayNode"/> but selects the
    /// aircraft's side and minimises lateral distance to the centerline so the
    /// chosen node sits just before the runway.
    /// </summary>
    private TaxiNode? ResolveHoldShortRunwayNode(string runwayDesignator)
    {
        if (_graph == null) return null;
        if (!_crossRunwayMap.TryGetValue($"Runway {runwayDesignator}", out var runway))
            return null;

        double hdgRad = runway.Heading * Math.PI / 180.0;
        double rwEast = Math.Sin(hdgRad);
        double rwNorth = Math.Cos(hdgRad);

        const double DEG_TO_M_LAT = 111320.0;
        double degToMLon = DEG_TO_M_LAT * Math.Cos(_aircraftLat * Math.PI / 180.0);

        double SignedCT(double lat, double lon)
        {
            double pDy = (lat - runway.StartLat) * DEG_TO_M_LAT;
            double pDx = (lon - runway.StartLon) * degToMLon;
            return rwEast * pDy - rwNorth * pDx;
        }

        double halfWidthM = runway.Width > 0 ? runway.Width / 2.0 : 30.0;
        double minLateralM = Math.Max(halfWidthM, 15.0);

        double acSignedCT = SignedCT(_aircraftLat, _aircraftLon);

        // Near side = the aircraft's own side. If the aircraft is ON the runway,
        // use its heading to pick the side it is coming FROM (opposite the exit
        // side the far-side finder would pick).
        int nearSign;
        if (Math.Abs(acSignedCT) >= minLateralM)
        {
            nearSign = Math.Sign(acSignedCT);
        }
        else
        {
            double perpComp = Math.Sin((runway.HeadingMag - _aircraftHeading) * Math.PI / 180.0);
            nearSign = perpComp >= 0 ? -1 : 1;
        }

        double runwayLengthM = runway.Length > 0
            ? runway.Length * 0.3048
            : TaxiGraph.CalculateDistanceMeters(
                runway.StartLat, runway.StartLon, runway.EndLat, runway.EndLon);

        const double MAX_LATERAL_M = 600.0;
        const double MAX_ALONG_PAST_END_M = 500.0;

        int? aircraftComponentId = _graph.FindNearestNode(_aircraftLat, _aircraftLon)?.ComponentId;

        TaxiNode? bestNode = null;
        double bestLateral = double.MaxValue;

        foreach (var node in _graph.Nodes.Values)
        {
            if (aircraftComponentId.HasValue && node.ComponentId != aircraftComponentId.Value) continue;

            double nodeSignedCT = SignedCT(node.Latitude, node.Longitude);
            if (Math.Sign(nodeSignedCT) != nearSign) continue;
            double lateralAbs = Math.Abs(nodeSignedCT);
            if (lateralAbs < minLateralM) continue;
            if (lateralAbs > MAX_LATERAL_M) continue;

            double nPDx = (node.Longitude - runway.StartLon) * degToMLon;
            double nPDy = (node.Latitude - runway.StartLat) * DEG_TO_M_LAT;
            double along = rwEast * nPDx + rwNorth * nPDy;
            if (along < -MAX_ALONG_PAST_END_M) continue;
            if (along > runwayLengthM + MAX_ALONG_PAST_END_M) continue;

            // Closest to the centerline on the near side = the hold-short point.
            if (lateralAbs < bestLateral)
            {
                bestLateral = lateralAbs;
                bestNode = node;
            }
        }

        return bestNode;
    }

    /// <summary>
    /// Finds the nearest graph node on the opposite side of <paramref name="runway"/>
    /// from the aircraft's current position. Used by the Progressive Taxi "After
    /// crossing runway" terminator to produce a routing target that forces A*
    /// across the runway; the InsertRunwayCrossingHoldShorts pass then auto-tags
    /// the hold-short point (which LoadRoute strips for the cleared crossing).
    ///
    /// If the aircraft is ON the runway (within half-width of the centerline), the
    /// aircraft's heading is used to determine the intended exit side.
    /// </summary>
    private TaxiNode? FindFarSideRunwayNode(Runway runway)
    {
        if (_graph == null) return null;

        double hdgRad = runway.Heading * Math.PI / 180.0;
        // Runway unit vector in east-north space: (sin h, cos h)
        double rwEast = Math.Sin(hdgRad);
        double rwNorth = Math.Cos(hdgRad);

        // Flat-earth scale factors at the aircraft latitude
        const double DEG_TO_M_LAT = 111320.0;
        double degToMLon = DEG_TO_M_LAT * Math.Cos(_aircraftLat * Math.PI / 180.0);

        // Signed cross-track of a point P from the runway centerline:
        //   positive = LEFT side looking down the runway heading
        //   negative = RIGHT side
        // Formula: rwEast * (P.lat - T.lat)_in_m  −  rwNorth * (P.lon - T.lon)_in_m
        double AircraftSignedCT()
        {
            double pDy = (_aircraftLat - runway.StartLat) * DEG_TO_M_LAT;
            double pDx = (_aircraftLon - runway.StartLon) * degToMLon;
            return rwEast * pDy - rwNorth * pDx;
        }

        double NodeSignedCT(TaxiNode n)
        {
            double pDy = (n.Latitude - runway.StartLat) * DEG_TO_M_LAT;
            double pDx = (n.Longitude - runway.StartLon) * degToMLon;
            return rwEast * pDy - rwNorth * pDx;
        }

        double halfWidthM = runway.Width > 0 ? runway.Width / 2.0 : 30.0;
        double minLateralM = Math.Max(halfWidthM, 15.0);

        double acSignedCT = AircraftSignedCT();

        // Determine which side to target
        int targetSign;
        if (Math.Abs(acSignedCT) >= minLateralM)
        {
            // Aircraft is off the runway: far side has opposite sign
            targetSign = -Math.Sign(acSignedCT);
        }
        else
        {
            // Aircraft is on the runway: use heading to determine intended exit side.
            // Perpendicular component of aircraft heading relative to runway heading:
            // sin(runwayHdg - aircraftHdg) > 0 → aircraft heading toward left side.
            // Use HeadingMag (not the TRUE Heading used for the geographic
            // cross-track above) so both operands are in the magnetic frame that
            // _aircraftHeading (PLANE HEADING DEGREES MAGNETIC) lives in.
            double perpComp = Math.Sin((runway.HeadingMag - _aircraftHeading) * Math.PI / 180.0);
            targetSign = perpComp >= 0 ? 1 : -1;
        }

        // Search geometry bounds
        const double MAX_LATERAL_M = 600.0;      // max lateral distance from runway centerline
        const double MAX_ALONG_PAST_END_M = 500.0; // buffer past each runway end

        // Along-track extent of the runway (threshold → far end)
        double runwayLengthM = runway.Length > 0
            ? runway.Length * 0.3048  // stored in feet
            : TaxiGraph.CalculateDistanceMeters(
                runway.StartLat, runway.StartLon, runway.EndLat, runway.EndLon);

        // Restrict candidates to the aircraft's own connected component so the
        // chosen far node is actually reachable. Without this, the nearest
        // far-side node can land in an isolated navdata island (e.g. GCLP S5)
        // and LoadRoute then fails with the generic "Could not calculate a
        // route." When the far side is a genuinely separate component this
        // leaves bestNode null, so the caller surfaces the specific
        // "far side of runway X" message instead — a better diagnostic.
        int? aircraftComponentId = _graph.FindNearestNode(_aircraftLat, _aircraftLon)?.ComponentId;

        TaxiNode? bestNode = null;
        double bestDist = double.MaxValue;

        foreach (var node in _graph.Nodes.Values)
        {
            if (aircraftComponentId.HasValue && node.ComponentId != aircraftComponentId.Value) continue;

            double nodeSignedCT = NodeSignedCT(node);

            if (Math.Sign(nodeSignedCT) != targetSign) continue;
            if (Math.Abs(nodeSignedCT) < minLateralM) continue;
            if (Math.Abs(nodeSignedCT) > MAX_LATERAL_M) continue;

            // Along-track: must be within the runway's length + buffer
            double nPDx = (node.Longitude - runway.StartLon) * degToMLon;
            double nPDy = (node.Latitude - runway.StartLat) * DEG_TO_M_LAT;
            double along = rwEast * nPDx + rwNorth * nPDy;
            if (along < -MAX_ALONG_PAST_END_M) continue;
            if (along > runwayLengthM + MAX_ALONG_PAST_END_M) continue;

            double dist = TaxiGraph.CalculateDistanceMeters(
                _aircraftLat, _aircraftLon, node.Latitude, node.Longitude);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestNode = node;
            }
        }

        return bestNode;
    }

    /// <summary>
    /// Computes the GSX <c>.py</c> per-aircraft stop offset for <paramref name="spot"/> and
    /// feeds it to docking, so the stop moves to where GSX's VDGS would stop this airframe.
    /// Applies to ALL non-deice gates — <c>.ini</c> gates INCLUDED: the <c>.py</c>
    /// <c>customOffset</c> is GSX's per-aircraft adjustment layered ON TOP of the static
    /// <c>.ini</c>/navdata base (EDDF A66: 777 = 5.3 m, A380 = 6.3 m, base = 1.65 m), so a
    /// <c>.ini</c> stop position is NOT aircraft-exact on its own. Deice pads stay
    /// datum-aligned (no offset). Resolves the aircraft id from SimConnect ICAO + wingspan.
    /// Any miss (no profile / unknown aircraft / parse fail) yields
    /// <see cref="Services.Gsx.GsxOffset.Zero"/> — the safe base position. Never throws.
    /// </summary>
    private void ApplyGsxStopOffset(Database.Models.ParkingSpot? spot)
    {
        if (_dockingManager == null) return;

        // Default to base position; only a successful resolution moves the stop.
        var offset = Services.Gsx.GsxOffset.Zero;
        // Diagnostic breadcrumbs for the stop-offset chain (why stopOffL was 0 at runtime).
        string dIcaoType = "", dAcId = "";
        int dNumber = spot?.Number ?? -1;
        string dSuffix = spot?.Suffix ?? "<null>";
        bool dStopLatSet = spot?.StopLatitude != null;
        try
        {
            // Apply for BOTH navdata/.py gates (StopLatitude == null, base = parking centre)
            // AND .ini gates (StopLatitude set, base = the .ini gate position). The .py
            // customOffset is GSX's PER-AIRCRAFT stop adjustment, which GSX adds on top of the
            // static gate base regardless of source — that's why the same gate yields different
            // offsets per airframe (EDDF A66: 777=5.3 m, A380=6.3 m, base=1.65 m). The earlier
            // `StopLatitude == null` guard wrongly assumed the .ini base was already aircraft-
            // exact, so at every .ini airport (EDDF, etc.) the 777 parked ~5 m short of GSX's
            // real VDGS stop. Deice pads stay datum-aligned (no per-aircraft offset).
            if (spot != null
                && !spot.IsDeiceArea
                && _simConnectManager != null)
            {
                // Snapshot the aircraft identity once, adjacently. This runs UI-thread on the
                // Calculate click (not per-frame); each field is an atomic read on x64. The only
                // possible inconsistency is reading the ICAO and wingspan from across the ~1-frame
                // window of an aircraft swap — and that would only mis-pick the wingspan-derived
                // ARC group, which is the last-resort fallback (after ICAO and idMajor) and stays
                // within the safe |offset| band, so no lock is warranted.
                string icaoType = _simConnectManager.CurrentAircraftIcaoType;
                dIcaoType = icaoType ?? "<null>";
                double wingspanM = _simConnectManager.AircraftWingSpan > 0
                    ? _simConnectManager.AircraftWingSpan * 0.3048 // feet -> metres
                    : 0.0;
                if (!string.IsNullOrWhiteSpace(icaoType))
                {
                    // TryResolve always yields a usable id even when it returns false (idMajor
                    // not derived) — the raw ICAO can still hit an ICAO-keyed table, so we
                    // evaluate with whatever id it produced regardless of the bool.
                    Services.Gsx.GsxAircraftIdMap.TryResolve(icaoType, wingspanM, out var acId);
                    dAcId = $"{acId.Icao}/maj{acId.IdMajor}/min{acId.IdMinor}";
                    offset = _stopOffsetResolver.Resolve(_currentIcao, spot.Number, spot.Suffix, acId);
                }
            }
        }
        catch (Exception ex) { offset = Services.Gsx.GsxOffset.Zero; dAcId += $" EX:{ex.GetType().Name}"; }

        // One-line diagnostic so a live dock reveals exactly why the offset resolved the way it
        // did (airport icao, gate number/suffix as parsed, whether a .ini stop was present, the
        // resolved aircraft id, and the final offset). Never throws.
        try
        {
            // AppLogs.PathFor ensures the logs folder exists on a fresh install
            // (AppendAllText throws DirectoryNotFoundException rather than creating it).
            string p = MSFSBlindAssist.Utils.AppLogs.PathFor("docking-aircraft.log");
            System.IO.File.AppendAllText(p, string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:HH:mm:ss}  STOPOFFSET  icao='{1}' gate#={2} suffix='{3}' stopLatSet={4} ac='{5}' acId={6} -> long={7:F2} lat={8:F2}{9}",
                DateTime.Now, _currentIcao, dNumber, dSuffix, dStopLatSet, dIcaoType, dAcId,
                offset.LongitudinalMetres, offset.LateralMetres, Environment.NewLine));
        }
        catch { }

        _dockingManager.SetStopOffset(offset);

        // Cue 2: use the gate's GSX gatedistancethreshold as the engage range when present.
        // Null for navdata-only and .py-only gates (no threshold) → keeps the 50 m default.
        _dockingManager.SetEngageRangeMetres(spot?.GateDistanceThreshold);
    }

    private void OnStopClicked(object? sender, EventArgs e)
    {
        _guidanceManager.StopGuidance();
        _dockingManager?.SetDestinationGate(null);
        _announcer.Announce("Taxi guidance stopped.");
        lblStatus.Text = "Guidance stopped.";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
}
