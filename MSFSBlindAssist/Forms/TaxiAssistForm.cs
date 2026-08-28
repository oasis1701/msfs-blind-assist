using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.SayIntentions;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;

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
    // Shared with DockingGuidanceManager/MainForm.AircraftSwitch/SimConnectManager.Dispatch —
    // all writers of docking-aircraft.log now serialize through this one channel.
    private static readonly LogChannel _dockingAircraftLog = Log.Channel("docking-aircraft");
    private static readonly LogChannel _taxiRouterLog = Log.Channel("taxi_router");

    private readonly IAirportDataProvider _dataProvider;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly TaxiGuidanceManager _guidanceManager;
    private readonly Services.GateDataSource? _gateSource;
    // When non-null, OnCalculateClicked fires GSX gate auto-select for gate destinations
    // (if the setting is on and GSX is available). The selector is constructed in MainForm
    // and is only non-null when GSX is installed. Unlike the retired menu-walking selector,
    // there is no separate "CouatlStarted" gate to check here: GsxRemoteGateSelector
    // feature-checks the 'gate' capability itself, on every call, before ever sending
    // gate.select -- so a non-null selector against a GSX that isn't running returns
    // Unavailable, and one against a connected build older than 4.0.8 returns
    // GateSelectUnsupported; either way it falls through to manual routing with no wasted
    // send. Only the second is spoken (once) -- see SelectGsxGateAsync.
    private readonly Services.Gsx.Remote.GsxRemoteGateSelector? _gsxGateSelector;
    // Once-per-instance latch for the "gate selection needs GSX 4.0.8" message — see
    // SelectGsxGateAsync. In practice that means once per APP session, not once per time
    // the dialog is opened: MainForm caches this form (GetOrCreateTaxiAssistForm) and
    // OnFormClosing cancels a user close and Hide()s instead, so the instance — and this
    // latch with it — survives closing and reopening the dialog. That is the right amount
    // of speech either way: the fact does not change until the pilot updates GSX, which
    // needs a sim restart anyway, and this path runs on EVERY gate-destination Calculate.
    // Instance state rather than static so it cannot outlive the form it describes.
    private bool _gsxUnsupportedAnnounced;
    // Optional. When non-null, OnCalculateClicked refreshes aircraft position
    // from `LastKnownPosition` (or via RequestAircraftPositionAsync) right
    // before computing the route, so the route starts from where the aircraft
    // ACTUALLY is — not from a stale snapshot taken when the form opened.
    // Critical for the open-form-then-push-back workflow: without this, the
    // route is computed from the gate, the post-pushback aircraft is already
    // off that route, and off-route detection recalcs immediately.
    private readonly MSFSBlindAssist.SimConnect.SimConnectManager? _simConnectManager;
    private readonly TcasService? _tcasService;
    // ~55 m — tight enough to distinguish adjacent gates but large enough to catch an
    // aircraft that has stopped just short of the spot centre. Class-scoped because BOTH
    // the destination-list occupancy filter and the Calculate-time warning measure with
    // it; two copies could drift and make the list and the warning disagree.
    private const double GATE_OCCUPIED_NM = 0.030;
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
    // Gate destinations only: leaves stands with an aircraft parked on them out of the
    // list. See the occupancy filter in PopulateDestinations and SpotIsOccupied.
    private CheckBox chkHideOccupied = null!;
    private Label lblGateSearch = null!;
    private TextBox txtGateSearch = null!;
    private ComboBox cmbDestination = null!;
    // Intersection departure (runway destinations only): tick chkIntersection to
    // line up partway down the runway at a taxiway intersection instead of the
    // full-length threshold. cmbIntersection lists the valid intersections for the
    // selected runway; _intersectionMap resolves the chosen label back to the graph
    // node + centerline point + remaining runway.
    private CheckBox chkIntersection = null!;
    private ComboBox cmbIntersection = null!;
    private readonly Dictionary<string, TaxiGraph.RunwayIntersection> _intersectionMap = new();
    // Named-holding-point departure (runway destinations only): tick chkHoldingPoint
    // to enter the runway at a PAINTED holding point picked by name (OSM
    // aeroway=holding_position refs — LSZH "A2"), covering entries whose stub
    // taxiway is UNNAMED in navdata (where the intersection list can't help).
    // Resolution is TaxiGraph.ResolveHoldingPointEntries — the painted point only
    // SELECTS the entry node; hold-short placement stays authoritative from
    // navdata. Partway entries reuse the intersection lineup semantics (lineup
    // target moves to the entry's centerline projection); full-length entries
    // keep the start-table lineup point and just force WHICH stub is used.
    private CheckBox chkHoldingPoint = null!;
    private ComboBox cmbHoldingPoint = null!;
    private readonly Dictionary<string, (TaxiGraph.RunwayIntersection Entry, bool Partway)> _holdingPointMap = new();
    // Last runway whose default (untick-everything) holding point was announced, so
    // re-entering the same selection doesn't repeat the call-out. Cleared on airport
    // load. See AnnounceDefaultHoldingPoint.
    private string _lastHoldingPointAnnounceKey = "";
    // Set while the destination combo is being rebuilt or driven programmatically (the
    // list rebuild re-selects index 0, and the SayIntentions import selects the imported
    // destination). Both raise SelectedIndexChanged without the pilot choosing anything,
    // and the import speaks its own single summary that a holding-point call-out must
    // not talk over.
    private bool _suppressHoldingPointAnnounce;
    // Set while an EXTERNAL destination (the SayIntentions assigned gate) is being
    // resolved and kept set once one has been seated: the occupied-stands filter is a
    // browsing aid, but an assigned gate with AI parked on it is still the assigned
    // gate, and hiding it makes the name, alias AND position steps all miss — the
    // whole chain then falls through to the ARRIVAL RUNWAY, the failure those
    // fallbacks exist to stop. Latched (not restored on success) so a later list
    // rebuild cannot drop the stand the pilot is being taxied to; cleared when the
    // pilot toggles the checkbox themselves and on every airport load. The occupancy
    // WARNING still reaches them at Calculate (CheckGateOccupancy).
    private bool _suppressOccupiedFilter;
    // Same shape, same lifetime, for the wingspan "show fitting only" filter — and for
    // a stronger reason. The occupancy filter reads a live traffic feed; this one reads
    // SCENERY-AUTHORED size data (a navdata parking radius, or a GSX max wing span), and
    // that data is frequently wrong. So a stand the controller NAMED, at an airport the
    // aircraft is really going to, outranks a number saying it will not fit: the pilot
    // was told to go there. Left applied, the whole gate half of a SayIntentions import
    // failed at once — PopulateDestinations drops a non-fitting spot BEFORE its label
    // reaches the combo and the two maps behind it, so the name, this scenery's aliases
    // and the published coordinate all had nothing to match against, and a known arrival
    // announced "this scenery has no stand under that label" for a stand it does have.
    // Latched on a successful seat (a later rebuild — the Calculate-path and show-path
    // gate-source refreshes both repopulate — must not drop the stand being taxied to);
    // cleared when the pilot toggles the checkbox themselves and on every airport load.
    private bool _suppressFitFilter;
    // CAT III / low-visibility hold (runway destinations only). When ticked, a
    // runway-destination route holds at the CAT III / ILS hold-short (further
    // back, protects the ILS critical area — e.g. EGKK A3/C3/M3) instead of the
    // default full-length line (A1/M1). Passed to LoadRoute as preferIlsHold.
    private CheckBox chkCatIiiHold = null!;
    // Full-length backtrack departure (runway destinations only). When ticked, and
    // the airport requires it, guidance holds short of an intermediate runway
    // entrance, then backtracks to the departure threshold and lines up full length
    // (e.g. iniBuilds EGNM 32 via D1, EGGW). Passed to LoadRoute as
    // fullLengthBacktrack; the entrance node is found via FindBacktrackEntryNode.
    private CheckBox chkFullLengthBacktrack = null!;
    private Label lblFirstTaxiway = null!;
    private ComboBox cmbFirstTaxiway = null!;
    private CheckBox chkFirstHoldShort = null!;
    private Label lblFirstHoldShortRunway = null!;
    private ComboBox cmbFirstHoldShortRunway = null!;
    private Button btnAddTaxiway = null!;
    // Progressive Taxi terminator controls. These are form-level (not per-row):
    // RefreshTerminatorRow() repositions and shows them on whichever taxiway row
    // is CURRENTLY last, and only when the destination type is Progressive Taxi
    // (index 2). The chosen terminator therefore "travels with the last row" as
    // the spec requires. The terminator block is SELF-CONTAINED: it carries its
    // own target pickers — cmbTerminatorRunway for the runway terminators (Hold
    // short of runway / After crossing runway) and cmbTerminatorTaxiway for the
    // taxiway terminator (and the optional cross-at taxiway). The per-row "Hold
    // short of runway" combos are NOT reused for the terminator target; they keep
    // their single meaning of an intermediate hold-short on any row.
    private Label lblTerminatorType = null!;
    private ComboBox cmbTerminatorType = null!;
    private Label lblTerminatorRunway = null!;
    private ComboBox cmbTerminatorRunway = null!;
    private Label lblTerminatorTaxiway = null!;
    private ComboBox cmbTerminatorTaxiway = null!;
    private Label lblTerminatorHoldPoint = null!;
    private ComboBox cmbTerminatorHoldPoint = null!;
    // Published NAMED holding points (VIKAS, N2E…) for the loaded airport:
    // online-sourced designators resolved onto navdata graph nodes by
    // NamedHoldingPointResolver. Empty when augmentation is off, the airport has
    // none, or the online fetch hasn't landed yet (PopulateTerminatorHoldPointList
    // retries the resolve on demand so a late background fetch still surfaces).
    private List<NamedHoldingPoint> _namedHoldingPoints = new();
    // True once ResolveNamedHoldingPoints has actually SEEN online holding-point data
    // for the loaded airport — whether or not any of it resolved. Gates the retry in
    // PopulateTerminatorHoldPointList: while the async fetch hasn't landed the raw list
    // is empty and retrying costs nothing (the resolve early-returns before scanning),
    // but once raw data has arrived the O(points × nodes) scan must not repeat on every
    // dropdown open and every taxiway row add/remove.
    private bool _namedHoldingPointsResolved;
    // Computed height of the terminator block (1-3 visible lines depending on
    // terminator type), read by UpdateLayout. Always set by RefreshTerminatorRow
    // before UpdateLayout consumes it (and UpdateLayout only reads it while the
    // block is visible), so the initial 0 is never observed.
    private int _terminatorBlockHeightPx;
    // Display strings for the terminator type combo, index-aligned with the
    // ProgressiveTerminatorType resolution switch in OnCalculateClicked.
    private static readonly string[] TerminatorTypeItems =
    {
        "Hold short of runway",
        "Hold short of taxiway",
        "After crossing runway",
        "End of last taxiway",
        "Hold at named holding point"
    };
    private Button btnSayIntentions = null!;
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

    private Panel pnlTaxiways = null!;

    /// <summary>
    /// One dynamically-added taxiway row in the planner. Holds a direct reference
    /// to every control the row owns — including the second-line
    /// "Hold short of runway:" label — so the row can be removed and its per-row
    /// controls toggled without scanning the panel by pixel position or label text.
    /// </summary>
    private sealed class TaxiwayRow
    {
        public Label Label = null!;                 // line 1: "Taxiway N:" label
        public ComboBox Combo = null!;              // line 1: taxiway selector
        public CheckBox HoldShort = null!;          // line 1: "Hold short" checkbox
        public Label HoldShortRunwayLabel = null!;  // line 2: "Hold short of runway:" label
        public ComboBox HoldShortRunway = null!;    // line 2: runway combo
        public Button RemoveBtn = null!;            // line 1: "Remove" button

        /// <summary>Every control this row owns — used to remove/dispose the row in one pass.
        /// Named OwnedControls (not Controls) to avoid confusion with WinForms' Control.Controls.</summary>
        public IEnumerable<Control> OwnedControls =>
            new Control[] { Label, Combo, HoldShort, HoldShortRunwayLabel, HoldShortRunway, RemoveBtn };
    }

    private List<TaxiwayRow> _additionalTaxiways = new();
    private const int MAX_ADDITIONAL_TAXIWAYS = 20;

    // Vertical pixel height of one dynamic taxiway row inside pnlTaxiways.
    // Two-line layout: line 1 holds the taxiway combo + Hold-short checkbox +
    // Remove button; line 2 holds the "Hold short of runway" combo.
    private const int DYNAMIC_ROW_HEIGHT_PX = 80;

    // Cached runway designators for the current airport (e.g. ["09L","09R","27L","27R"]).
    // Populated on airport load; consumed when constructing every Hold-short-of-runway
    // combo (first row + each dynamically added row).
    private List<string> _airportRunwayIds = new();

    // State
    private TaxiGraph? _graph;
    private string _currentIcao = "";
    /// <summary>
    /// The gate-list source token (<see cref="Services.GateDataSource.GetGateListVersion"/>) that
    /// was live when <see cref="_graph"/> was built — the graph's own staleness key, alongside
    /// <see cref="_currentIcao"/>.
    ///
    /// <para>
    /// Stand names are baked into the graph's nodes at build time, so a graph built before GSX
    /// published this airport carries navdata's concourse letters permanently. The dropdown cache
    /// beside it already rebuilt on this token; the graph did not, and the same-ICAO early return
    /// in <c>LoadAirportDataCoreAsync</c> meant it never could. Pre-planning the arrival during
    /// descent therefore produced a form whose combo offered "B 25" while the graph it handed to
    /// guidance as <c>prebuiltGraph</c> called the same stand "A 25" — and
    /// <c>TaxiGuidanceManager.DescribeCurrentLocation</c> prefers that installed graph, so
    /// Where-Am-I said "A 25" on arrival at the stand the pilot had just been routed to as B 25.
    /// </para>
    /// </summary>
    private string _graphSourceToken = "";
    private double _aircraftLat, _aircraftLon, _aircraftHeading;
    // Magnetic variation (east positive) matching _aircraftHeading, which is MAGNETIC.
    // Only the route-start turn cue consumes it (passed to LoadRoute as
    // aircraftHeadingMagVar) — it must be composed in TRUE north like the steering tone,
    // or the spoken direction can contradict the tone near a U-turn. Left at 0 until a
    // position sample supplies one, which is the same no-conversion behaviour as before.
    private double _aircraftMagVar;
    // Per-ICAO memo of GetRunways for the intersection picker, which re-lists on
    // every checkbox toggle / runway change. GetRunways opens a fresh SQLite
    // connection per call, so caching the (session-stable) runway set for the
    // loaded airport avoids a UI-thread DB round-trip on each interaction.
    private List<Runway>? _cachedRunways;
    private string _cachedRunwaysIcao = "";

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
    // actual ParkingSpot to GsxRemoteGateSelector without re-querying the data provider.
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
    //
    // The cache is keyed on the ICAO AND on GateDataSource.GetGateListVersion's token — the
    // SOURCE the gate list came from, not just the airport. Without the token, a list bound
    // from the .ini/navdata fallback BEFORE GSX had published the airport (the arrival
    // pre-planned during descent, or the spawn before the first handlerData frame) was served
    // for the rest of the session: this form is hide-on-close, LoadAirportDataCoreAsync
    // early-returns for a same-ICAO reload, and nothing else ever invalidated it. Those spots
    // carry GsxIdentifier == null (only the Remote API reader sets it), so every gate
    // destination Calculate -> SelectGsxGateAsync -> BadArgs -> "GSX could not prepare this
    // stand." until a DIFFERENT airport was loaded. The token is O(1) to compute (a property
    // read, no file, no DB), which is what keeps the per-keystroke path cheap — the invariant
    // this cache exists to protect. See RefreshDestinationsIfGateSourceChanged for the two
    // extra moments it is checked outside PopulateDestinations.
    private List<(ParkingSpot spot, int nodeId)>? _cachedGateSpots;
    private string _cachedGateSpotsIcao = "";
    private string _cachedGateSpotsSourceToken = "";

    // Docking guidance manager: receives the selected gate so proximity audio
    // and lateral tone can guide the pilot to the stop position. Set in
    // OnCalculateClicked for gate destinations; cleared on runway destinations.
    private readonly Services.DockingGuidanceManager? _dockingManager;

    // Resolves the GSX .py per-aircraft stop offset for a navdata/.py gate so the
    // docking stop moves to where GSX's VDGS would stop THIS airframe. Lazy + cached.
    private readonly Services.Gsx.GsxStopOffsetResolver _stopOffsetResolver = new();

    /// <summary>Runs the SayIntentions taxi-route import — the same operation the
    /// Ctrl+Shift+Y hotkey performs. Supplied by the caller rather than reached through a
    /// MainForm reference, so this form keeps knowing nothing about MainForm (the pattern
    /// TaxiGuidancePanel already uses for its taxiway-name refresh). Null when the caller
    /// has no import to offer, and the button then stays present but disabled — a control
    /// that appears and disappears is worse to navigate than one consistently there.</summary>
    private readonly Func<Task>? _importFromSayIntentions;

    public TaxiAssistForm(
        IAirportDataProvider dataProvider,
        ScreenReaderAnnouncer announcer,
        TaxiGuidanceManager guidanceManager,
        MSFSBlindAssist.SimConnect.SimConnectManager? simConnectManager = null,
        TcasService? tcasService = null,
        double aircraftWingspan = 0,
        Services.GateDataSource? gateSource = null,
        Services.Gsx.Remote.GsxRemoteGateSelector? gsxGateSelector = null,
        Services.DockingGuidanceManager? dockingManager = null,
        Func<Task>? importFromSayIntentions = null)
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
        _importFromSayIntentions = importFromSayIntentions;
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
            _ = LoadAirportDataSafeAsync(nearestIcao);
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
        //   Alt+L  CAT III / low-visibility hold checkbox ("(&LVP)", runway dest. only)
        //   Alt+F  First taxiway combo
        //   Alt+H  First Hold-short checkbox  (dynamic Hold-shorts share Alt+H — cycle)
        //   Alt+O  Hold short &of runway combo (first row + dynamic — all cycle on Alt+O)
        //   Alt+D  Add (D)taxiway button
        //   Alt+N  Progressive-taxi termi&nator type combo (last row only, index 2)
        //   Alt+U  Progressive-taxi terminator R&unway target combo (last row only,
        //          types "Hold short of runway" / "After crossing runway")
        //   Alt+W  Progressive-taxi terminator taxi&way target combo (last row only,
        //          type "Hold short of taxiway"); the SAME combo becomes the optional
        //          "Cross at ta&xiway" picker (Alt+X) for type "After crossing runway"
        //   Alt+P  Progressive-taxi terminator named holding-&point combo (last row
        //          only, type "Hold at named holding point")
        //   Alt+Y  Fill from Sa&yIntentions (matches the Ctrl+Shift+Y hotkey)
        //   Alt+C  Calculate Route
        //   Alt+S  Stop Guidance
        //   Alt+R  Remove (dynamic) — shared across all Remove buttons (cycle)
        //   Alt+2..9  Dynamic Taxiway label (Taxiway &2 .. Taxiway &9)
        //
        // Tab order (top→bottom of form, no jumps to dynamic panel at the end):
        //   txtAirport, cmbDestType, txtGateSearch, cmbDestination,
        //   chkIntersection, cmbIntersection, chkCatIiiHold, chkFitFilter,
        //   cmbFirstTaxiway, chkFirstHoldShort, cmbFirstHoldShortRunway,
        //   btnAddTaxiway, pnlTaxiways (dynamic rows in insertion order),
        //   btnCalculate, btnStop, txtRouteSummary.

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
        txtAirport.Leave += (s, e) => _ = LoadAirportDataSafeAsync(txtAirport.Text.Trim());
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
        chkFitFilter.CheckedChanged += (s, e) =>
        {
            // The pilot asserting the filter outranks an import's latched suppression
            // (same rule as chkHideOccupied below).
            _suppressFitFilter = false;
            if (cmbDestType.SelectedIndex == 1) PopulateDestinations();
        };
        y += 20;

        // Hide stands that currently have an aircraft parked on them. Default ON — an
        // occupied stand is not a destination — but escapable, because it depends on a
        // live traffic feed: with AI traffic off, or a stand whose scenery position sits
        // far from where the AI parks, the filter is the wrong answer and the pilot needs
        // the full list back. Enabled only when there IS a traffic feed.
        chkHideOccupied = new CheckBox
        {
            // Own row (the form is 420 px wide — beside "Show fitting only" it clips).
            Location = new System.Drawing.Point(200, y),
            Text = "Hide &occupied stands",
            AutoSize = true,
            Visible = false,
            Checked = _tcasService != null,
            Enabled = _tcasService != null,
            AccessibleName = "Hide occupied stands",
            AccessibleDescription = "When checked, parking spots with an aircraft already on them are left out of the destination list"
        };
        chkHideOccupied.CheckedChanged += (s, e) =>
        {
            // The pilot asserting the filter outranks an import's latched suppression.
            _suppressOccupiedFilter = false;
            if (cmbDestType.SelectedIndex == 1) PopulateDestinations();
        };
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
        // Re-list intersections when the runway changes (only matters while the
        // intersection checkbox is on and a runway destination is selected). Guard
        // on a real selection (>= 0): repopulating the destination combo fires this
        // with a transient SelectedIndex of -1, and re-listing then would find no
        // runway and spuriously announce "no intersections" + untick the box.
        cmbDestination.SelectedIndexChanged += (s, e) =>
        {
            if (cmbDestType.SelectedIndex == 0 && chkIntersection.Checked
                && cmbDestination.SelectedIndex >= 0)
                ShowIntersectionListOrFallback(focusCombo: false);
            if (cmbDestType.SelectedIndex == 0 && chkHoldingPoint.Checked
                && cmbDestination.SelectedIndex >= 0)
                ShowHoldingPointListOrFallback(focusCombo: false);
            // Tell the pilot which painted holding point a plain full-length departure
            // from this runway uses, so they can tell it apart from the one ATC named.
            AnnounceDefaultHoldingPoint();
        };
        y += 30;

        // Intersection departure. Runway destinations only — hidden for gate /
        // progressive / deice. When ticked, cmbIntersection lists the taxiways
        // that meet the selected runway (each with runway remaining ahead), and
        // Calculate lines the aircraft up at that intersection instead of the
        // full-length threshold. A permanent slot (like the gate-search row
        // above) so the layout doesn't jump; the controls just hide/show.
        chkIntersection = new CheckBox
        {
            Text = "&Intersection departure",
            Location = new System.Drawing.Point(controlX, y),
            AutoSize = true,
            // Visible for the default (Runway) destination type. OnDestTypeChanged
            // is wired AFTER cmbDestType.SelectedIndex = 0, so it never fires during
            // construction — the checkbox must therefore be born in the state that
            // matches the default selection, or the whole feature is invisible on
            // first open until the user toggles the destination type away and back.
            Visible = cmbDestType.SelectedIndex == 0,
            AccessibleName = "Intersection departure",
            AccessibleDescription = "Line up at a runway intersection instead of full length"
        };
        chkIntersection.CheckedChanged += OnIntersectionToggled;
        y += 26;
        cmbIntersection = new ComboBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false,
            AccessibleName = "Intersection",
            AccessibleDescription = "Select the taxiway intersection to depart from, with runway remaining ahead"
        };
        y += 30;

        // Named holding-point departure. Runway destinations only (same visibility
        // rule as chkIntersection — born in the default-selection state because
        // OnDestTypeChanged is wired after the initial SelectedIndex = 0). When
        // ticked, cmbHoldingPoint lists the runway's PAINTED holding points by name
        // (from online augmentation — LSZH A1/A2), and Calculate routes to that
        // exact entry: hold short there, then the normal lineup flow (and Takeoff
        // Assist auto-activation) continues from that entry.
        chkHoldingPoint = new CheckBox
        {
            Text = "Depart from named holding &point",
            Location = new System.Drawing.Point(controlX, y),
            AutoSize = true,
            Visible = cmbDestType.SelectedIndex == 0,
            AccessibleName = "Depart from named holding point",
            AccessibleDescription = "Enter the runway at a painted holding point chosen by name, for example A2, and line up from there"
        };
        chkHoldingPoint.CheckedChanged += OnHoldingPointToggled;
        y += 26;
        cmbHoldingPoint = new ComboBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false,
            AccessibleName = "Holding point",
            AccessibleDescription = "Select the named holding point to enter the runway at, with runway remaining ahead"
        };
        y += 30;

        // CAT III / low-visibility hold. Runway destinations only (same visibility
        // rule as chkIntersection — see the note there about being born in the
        // default-selection state because OnDestTypeChanged is wired after the
        // initial SelectedIndex = 0). Default OFF = hold at the full-length line
        // (closest to the runway, a normal clearance); ticked = hold further back
        // at the CAT III / ILS hold to protect the ILS critical area in low vis.
        chkCatIiiHold = new CheckBox
        {
            Text = "CAT III / low-visibility hold (&LVP)",
            Location = new System.Drawing.Point(controlX, y),
            AutoSize = true,
            Visible = cmbDestType.SelectedIndex == 0,
            AccessibleName = "CAT three, low visibility hold",
            AccessibleDescription = "When checked, hold at the CAT three / ILS hold-short further back from the runway for low-visibility procedures, instead of the full-length hold closest to the runway"
        };
        // Ticking LVP changes WHICH painted line the route holds at (EGKK 26L: M3
        // behind M1), and the call-out has a dedicated CAT III variant keyed on this
        // state — without this the variant was unreachable in the natural flow, so a
        // pilot ticking it for exactly the low-visibility case that matters heard
        // nothing and taxied expecting the normal line. Not a "combo value change"
        // readback: the tick names the DERIVED hold, which the screen reader cannot.
        chkCatIiiHold.CheckedChanged += (s, e) => AnnounceDefaultHoldingPoint();
        y += 30;

        // Full-length backtrack departure. Runway destinations only (same
        // visibility rule as chkIntersection / chkCatIiiHold — born visible in the
        // default runway selection because OnDestTypeChanged is wired after the
        // initial SelectedIndex = 0). Default OFF. When ticked, guidance takes the
        // pilot onto the runway at an intermediate entrance, backtracks to the
        // departure threshold, turns around, and lines up full length — for
        // airports with no full-length parallel taxiway (EGNM, EGGW). Offered for
        // EVERY runway (opt-in); if the airport has no such entrance the route
        // falls back to a normal full-length departure with a spoken note.
        chkFullLengthBacktrack = new CheckBox
        {
            Text = "Full-length departure (requires &backtrack)",
            Location = new System.Drawing.Point(controlX, y),
            AutoSize = true,
            Visible = cmbDestType.SelectedIndex == 0,
            AccessibleName = "Full length departure requires backtrack",
            AccessibleDescription = "When checked, and the runway has no full-length taxiway, guidance takes you onto the runway partway down, backtracks to the departure threshold, turns you around, and lines you up full length before takeoff assist"
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
        lblFirstHoldShortRunway = new Label
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
            AccessibleDescription = "Choose how this progressive taxi leg ends: hold short of a runway, hold short of a taxiway, after crossing a runway, at the end of the last taxiway, or at a published named holding point such as VIKAS. Pick the target in the combo that appears just below."
        };
        cmbTerminatorType.Items.AddRange(TerminatorTypeItems);
        cmbTerminatorType.SelectedIndex = 0;
        cmbTerminatorType.SelectedIndexChanged += (s, ev) => RefreshTerminatorRow();
        // Runway TARGET for the two runway terminators (Hold short of runway /
        // After crossing runway). The label text + accessibility strings are set
        // per-type in RefreshTerminatorRow ("Runway to hold short of:" vs "Runway
        // to cross:"). Populated from _airportRunwayIds (same source + sentinel as
        // the per-row hold-short combos) via RebuildHoldShortRunwayCombo.
        lblTerminatorRunway = new Label
        {
            Text = "R&unway to hold short of:",
            AutoSize = true,
            Visible = false,
            AccessibleName = "Progressive taxi terminator runway label"
        };
        cmbTerminatorRunway = new ComboBox
        {
            Width = 190,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false,
            AccessibleName = "Progressive taxi terminator runway",
            AccessibleDescription = "Pick the runway this progressive leg holds short of."
        };
        cmbTerminatorRunway.Items.Add(NO_RUNWAY_HOLDSHORT);
        cmbTerminatorRunway.SelectedIndex = 0;
        // When the target runway changes, refresh the optional cross-at taxiway
        // list (it is filtered to taxiways that cross the chosen runway for the
        // After-crossing terminator).
        cmbTerminatorRunway.SelectedIndexChanged += (s, ev) =>
        {
            if (cmbTerminatorType.SelectedIndex == 2)
                PopulateTerminatorTaxiwayList();
        };
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
        // For the "After crossing runway" terminator this combo doubles as the
        // optional "Cross at taxiway" picker, whose list depends on the runway
        // chosen in cmbTerminatorRunway. Refresh it just before the dropdown opens
        // so the cross-at options reflect the current runway pick regardless of the
        // order the user filled the controls in.
        cmbTerminatorTaxiway.DropDown += (s, ev) => PopulateTerminatorTaxiwayList();

        // Named-holding-point TARGET for the "Hold at named holding point"
        // terminator. Lists the airport's published holding-point designators
        // (VIKAS, N2E, A11…) sourced from online data and resolved onto navdata
        // graph nodes — only shown when the terminator type selects it, and the
        // type itself only appears useful at airports where online data carries
        // named holds (the combo says so when empty).
        lblTerminatorHoldPoint = new Label
        {
            Text = "Named holding &point:",
            AutoSize = true,
            Visible = false,
            AccessibleName = "Progressive taxi terminator holding point label"
        };
        cmbTerminatorHoldPoint = new ComboBox
        {
            Width = 190,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false,
            AccessibleName = "Progressive taxi terminator holding point",
            AccessibleDescription = "Pick the published holding point this progressive leg taxis to and holds at, for example VIKAS or N2E. The list comes from online airport data; it is empty when this airport has no named holding points."
        };
        // Refresh just before the dropdown opens: the online fetch is async, so
        // a background fetch that landed after the airport loaded still surfaces.
        cmbTerminatorHoldPoint.DropDown += (s, ev) => PopulateTerminatorHoldPointList();

        // Dynamic taxiway panel (for additional taxiway combos)
        pnlTaxiways = new Panel
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            Height = 0, // starts empty, grows as taxiways are added
            AutoSize = false
        };
        // y will be adjusted dynamically

        // SayIntentions import. Sits immediately above Calculate because it is the step
        // BEFORE it: it fills the fields, it does not start guidance. The label says
        // "Fill from" for exactly that reason — a pilot who reads it as "go" would press
        // it expecting to be moving.
        //
        // Disabled rather than hidden when no import callback was supplied (see
        // _importFromSayIntentions).
        btnSayIntentions = new Button
        {
            Text = "Fill from Sa&yIntentions",
            Location = new System.Drawing.Point(controlX, y),
            Width = 180,
            Height = 30,
            Enabled = _importFromSayIntentions != null,
            AccessibleName = "Fill from SayIntentions",
            AccessibleDescription = _importFromSayIntentions != null
                ? "Fill this form from the latest SayIntentions taxi clearance. Same as Ctrl+Shift+Y."
                : "Fill this form from the latest SayIntentions taxi clearance. Unavailable in this window."
        };
        btnSayIntentions.Click += OnSayIntentionsClicked;
        y += 35;

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
        this.Controls.Add(chkHideOccupied);
        this.Controls.Add(lblGateSearch);
        this.Controls.Add(txtGateSearch);
        this.Controls.Add(cmbDestination);
        this.Controls.Add(chkIntersection);
        this.Controls.Add(cmbIntersection);
        this.Controls.Add(chkHoldingPoint);
        this.Controls.Add(cmbHoldingPoint);
        this.Controls.Add(chkCatIiiHold);
        this.Controls.Add(chkFullLengthBacktrack);
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
        pnlTaxiways.Controls.Add(lblTerminatorRunway);
        pnlTaxiways.Controls.Add(cmbTerminatorRunway);
        pnlTaxiways.Controls.Add(lblTerminatorTaxiway);
        pnlTaxiways.Controls.Add(cmbTerminatorTaxiway);
        pnlTaxiways.Controls.Add(lblTerminatorHoldPoint);
        pnlTaxiways.Controls.Add(cmbTerminatorHoldPoint);
        // The terminator block belongs to the LAST taxiway row, so it should tab
        // AFTER every dynamic row inside the panel. Dynamic rows get sequential
        // TabIndexes starting low (= panel control count at add time); a high base
        // keeps the terminator combos last in the panel's tab stream.
        // Tab order within the terminator block: type label → type combo → taxiway
        // label → taxiway combo. The label's mnemonic (Alt+N / Alt+W) focuses the
        // NEXT tab-stop after the label, so the label must be immediately before its
        // paired combo. With high base indices these always tab AFTER every dynamic
        // taxiway row (which get sequential low indices as rows are added).
        lblTerminatorType.TabIndex = 8998;
        cmbTerminatorType.TabIndex = 8999;
        lblTerminatorRunway.TabIndex = 9000;
        cmbTerminatorRunway.TabIndex = 9001;
        lblTerminatorTaxiway.TabIndex = 9002;
        cmbTerminatorTaxiway.TabIndex = 9003;
        lblTerminatorHoldPoint.TabIndex = 9004;
        cmbTerminatorHoldPoint.TabIndex = 9005;
        this.Controls.Add(pnlTaxiways);
        this.Controls.Add(btnSayIntentions);
        this.Controls.Add(btnCalculate);
        this.Controls.Add(btnStop);
        this.Controls.Add(lblStatus);
        this.Controls.Add(lblRouteSummary);
        this.Controls.Add(txtRouteSummary);

        // Tab order: Airport → Type → Destination → First taxiway → First
        // hold-short → First hold-short-of-runway → Add Taxiway → DYNAMIC
        // TAXIWAYS → Fill from SayIntentions → Calculate → Stop. The import sits
        // with the other actions rather than at the top: it is one of three things
        // the pilot can DO once the fields are in view, and it is the one that
        // feeds Calculate. The dynamic-taxiway panel needs an
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
        chkIntersection.TabIndex = tabIdx++;
        cmbIntersection.TabIndex = tabIdx++;
        chkHoldingPoint.TabIndex = tabIdx++;
        cmbHoldingPoint.TabIndex = tabIdx++;
        chkCatIiiHold.TabIndex = tabIdx++;
        chkFullLengthBacktrack.TabIndex = tabIdx++;
        chkFitFilter.TabIndex = tabIdx++;
        chkHideOccupied.TabIndex = tabIdx++;
        cmbFirstTaxiway.TabIndex = tabIdx++;
        chkFirstHoldShort.TabIndex = tabIdx++;
        cmbFirstHoldShortRunway.TabIndex = tabIdx++;
        btnAddTaxiway.TabIndex = tabIdx++;
        pnlTaxiways.TabStop = true;
        pnlTaxiways.TabIndex = tabIdx++;
        btnSayIntentions.TabIndex = tabIdx++;
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

        // Fit the form to its content NOW. UpdateLayout otherwise only runs on
        // a destination-type change or row add/remove, so at first open the
        // initial 480 px Size clipped everything below its client area — the
        // CAT III / LVP checkbox row pushed Calculate/Stop below the fold, and
        // the status/route-summary block was already down there before it.
        UpdateLayout();
    }

    // Non-handler async void (called from the airport textbox's Leave lambda and from
    // the nearest-airport auto-load) — wrapped end-to-end so a DB/graph-build fault
    // can't escape as an unobserved async-void exception; only the augmentation
    // prefetch and the graph build had their own local guards before this.
    /// <summary>Outcome of applying an externally-sourced (SayIntentions) route.
    /// Skipped taxiways AND hold-shorts are reported so the caller can tell the pilot
    /// which parts of the clearance did not survive — silently degrading to a
    /// shortest-path route, or dropping a hold-short ATC gave, is the failure a blind
    /// pilot cannot see.</summary>
    public sealed record ExternalRouteOutcome(
        bool DestinationApplied,
        IReadOnlyList<string> AppliedTaxiways,
        IReadOnlyList<string> SkippedTaxiways,
        IReadOnlyList<AppliedHoldShort> AppliedHoldShorts,
        IReadOnlyList<string> SkippedHoldShortRunways);

    /// <summary>A hold-short that actually landed on a row: the runway designator
    /// spelled the way the combo (and therefore the router) spells it, and the
    /// taxiway whose row carries it.</summary>
    public sealed record AppliedHoldShort(string Runway, string AfterTaxiway);

    /// <summary>A hold-short from the clearance, tied to the taxiway it FOLLOWS.
    /// TaxiwayIndex indexes the taxiway list passed to ApplyExternalRoute; -1 means
    /// the clearance named no taxiway ahead of it, so no row can carry it.</summary>
    public readonly record struct ExternalHoldShort(int TaxiwayIndex, string Runway);

    /// <summary>One destination an external clearance would accept. Callers pass the
    /// whole list in priority order so the form lists each destination type once.
    ///
    /// <paramref name="Position"/> is where the source says that destination IS, for the
    /// gate candidates that have one. It is a FALLBACK for the name and nothing else: it
    /// is consulted only after <see cref="MatchDestinationLabel"/> has failed on this
    /// same candidate, because the name is what the pilot was actually told.</summary>
    public readonly record struct ExternalDestination(
        bool IsRunway, string? Identifier, GeoPoint? Position = null);

    /// <summary>Which step seated a stand the source named something else. The pilot
    /// hears a different sentence for each, because they are different claims about the
    /// airport: an ALIAS says this scenery holds the stand under another label, a
    /// POSITION says no label matched at all and the published coordinate decided.</summary>
    public enum GateSubstitutionKind
    {
        Alias,
        Position
    }

    /// <summary>A destination seated under a label the source did not use.
    /// <paramref name="AssignedName"/> is the name SayIntentions ASKED for. The import's
    /// lead sentence names only the label that WON, so this is the one thing that can
    /// tell the pilot they are being taxied to a stand the controller did not name.</summary>
    public readonly record struct GateSubstitution(
        string AssignedName, GateSubstitutionKind Kind);

    /// <summary>One gate entry as the alias step sees it: the label the combo carries,
    /// and the online names the spot behind it also answers to.</summary>
    internal readonly record struct AliasedDestination(
        string Label, IReadOnlyList<string> Aliases);

    /// <summary>Loads the airport for an external route and returns the taxiway
    /// names its graph knows. The caller resolves its clearance against THIS list
    /// so no second TaxiGraph is ever built.</summary>
    public async Task<IReadOnlyList<string>> LoadAirportForExternalRouteAsync(
        double lat, double lon, double heading, string icao)
    {
        _aircraftLat = lat;
        _aircraftLon = lon;
        _aircraftHeading = heading;
        txtAirport.Text = icao.ToUpperInvariant();
        await LoadAirportDataAsync(icao);
        // Same-ICAO reload early-returns, so an import landing after GSX published
        // this airport (the descent pre-plan case) would otherwise resolve its
        // destination against the identifier-less fallback list and only discover
        // that at Calculate time. Refresh here, before TryResolveExternalDestination
        // reads the combo; the lost-selection result is irrelevant — the import
        // seats its own destination next.
        try { RefreshDestinationsIfGateSourceChanged(); }
        catch (Exception ex) { _taxiFormLog.Error($"Gate-list refresh before import failed: {ex}"); }
        return _graph?.GetAllTaxiwayNames() ?? new List<string>();
    }

    /// <summary>Every named taxiway SEGMENT of the airport this form currently has
    /// loaded, for a caller matching published geometry against the pavement. Exposed
    /// for the same reason the taxiway names are: the SayIntentions import must never
    /// build a second TaxiGraph, and this graph is the one the route will be calculated
    /// on. Empty before an airport is loaded.</summary>
    public IReadOnlyList<(string Name, double FromLat, double FromLon, double ToLat, double ToLon)>
        GetLoadedTaxiwayEdges() =>
        _graph == null
            ? Array.Empty<(string, double, double, double, double)>()
            : _graph.GetNamedEdges().ToList();

    /// <summary>Selects the first candidate that names a real entry in the form's
    /// destination list. The form owns its label formats — callers pass a normalized
    /// identifier ("15L", "A9"), never a constructed "Runway 15L".
    ///
    /// PROBING LEAVES NO MARK. Listing a destination type re-reads the database and
    /// re-walks the taxi graph, so each type is listed at most ONCE however many
    /// candidates ask for it; and when nothing resolves, the form is put back the way
    /// it was found — a failed import must not throw away the destination the pilot
    /// had already selected.
    ///
    /// "The way it was found" includes the ROUTE-SHAPING boxes, not just the destination.
    /// Probing a gate candidate sets the destination type, and OnDestTypeChanged unticks
    /// the intersection-departure and CAT III / LVP boxes on the way out of runway mode
    /// (the first also empties the intersection list and its map). Restoring the type
    /// re-SHOWS them, unticked. So a pilot who hand-built "Runway 27L, intersection T4,
    /// CAT III hold", pressed Ctrl+Shift+Y and heard "SayIntentions route unavailable" —
    /// i.e. "nothing happened" — silently lost both, and their next Calculate lined them
    /// up at the full-length threshold holding at the CAT I line. This is the mirror
    /// image of what ResetRouteShapingControls exists to prevent on a SUCCESSFUL
    /// import.
    ///
    /// A GATE CANDIDATE IS RESOLVED IN THREE STEPS: its name, then this scenery's online
    /// ALIASES for that name, then the coordinate SayIntentions published beside it. Each
    /// is weaker evidence than the one before — an alias is still the same stand under
    /// another label, a coordinate is a guess at which pavement an unrecognized name must
    /// have meant — so each only runs when the one above it found nothing.
    ///
    /// <paramref name="gateSubstitution"/> names the gate a candidate ASKED for when one
    /// of those two fallbacks seated it, and says which — non-null only then, so the
    /// caller can tell the pilot they are being routed to a stand under a label the
    /// controller did not use.</summary>
    public bool TryResolveExternalDestination(
        IReadOnlyList<ExternalDestination> candidates, out bool isRunway, out string label,
        out GateSubstitution? gateSubstitution, List<string>? probeTrace = null)
    {
        isRunway = false;
        label = "";
        gateSubstitution = null;

        int priorType = cmbDestType.SelectedIndex;
        string priorSearch = txtGateSearch.Text;
        string? priorDestination = cmbDestination.SelectedItem?.ToString();
        bool priorIntersection = chkIntersection.Checked;
        string? priorIntersectionLabel = cmbIntersection.SelectedItem?.ToString();
        bool priorCatIiiHold = chkCatIiiHold.Checked;

        // A gate search left over from a manual lookup filters the gate list, and a
        // filtered-out gate reads exactly like "this airport has no such gate".
        if (txtGateSearch.Text.Length > 0) txtGateSearch.Text = "";

        // Same reasoning for the occupied-stands filter (see _suppressOccupiedFilter):
        // an assigned stand with AI parked on it must still be resolvable. And for the
        // wingspan filter (see _suppressFitFilter), which is ticked by DEFAULT whenever
        // wingspan data exists and drops stands on scenery size data that is often
        // wrong: a stand ATC named must still be resolvable too. The explicit
        // rebuild is needed because SelectDestinationType only repopulates when the type
        // actually CHANGES — already in gate mode, the stale filtered list would stand.
        bool priorSuppressOccupied = _suppressOccupiedFilter;
        bool priorSuppressFit = _suppressFitFilter;
        _suppressOccupiedFilter = true;
        _suppressFitFilter = true;
        if (cmbDestType.SelectedIndex == 1) PopulateDestinations();

        List<string>? runwayLabels = null;
        List<string>? gateLabels = null;

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Identifier)) continue;

            // sayintentions.log's answer to "why did THIS import end where it did":
            // one token per probed candidate, saying which step answered or how the
            // last one missed. The KSTL/KLAX arrival captures could not distinguish
            // "the runway candidate won first" from "every gate step failed" — four
            // presses, two airports, and the log recorded only the winner.
            string probe = $"{(candidate.IsRunway ? "runway" : "gate")}'{candidate.Identifier}'";

            var offered = candidate.IsRunway
                ? (runwayLabels ??= ListDestinations(true))
                : (gateLabels ??= ListDestinations(false));

            string? match = MatchDestinationLabel(offered, candidate.IsRunway, candidate.Identifier);

            // BOTH GATE FALLBACKS RUN HERE, ON THIS CANDIDATE, AND MUST NEVER BE MOVED
            // BELOW THE LOOP. The chain ends with the ARRIVAL RUNWAY — that is the whole
            // reason they exist: a gate name the scenery does not carry fell through every
            // remaining candidate and routed a just-landed aircraft at the runway it had
            // landed on. A fallback placed after the loop would let the runway win first
            // and reproduce exactly that.
            GateSubstitution? substitution = null;
            if (match == null && !candidate.IsRunway)
            {
                // _destinationThresholdMap / _destinationSpotMap only hold GATE entries
                // while gate mode is the selected type, and a runway candidate probed
                // between here and the gate listing has repopulated both with runway
                // entries. The list itself was snapshotted; the maps behind it were not.
                // Selected ONCE for both steps below — they read the same two maps.
                SelectDestinationType(false);

                match = MatchGateByAlias(offered, candidate.Identifier);
                if (match != null)
                {
                    substitution = new GateSubstitution(
                        candidate.Identifier!, GateSubstitutionKind.Alias);
                }
                else if (candidate.Position is GeoPoint published)
                {
                    match = MatchGateByPosition(offered, published, out double nearestMetres);
                    if (match != null)
                    {
                        substitution = new GateSubstitution(
                            candidate.Identifier!, GateSubstitutionKind.Position);
                    }
                    else
                    {
                        // The one distinction the KLAX capture could not make: was the
                        // published coordinate near-but-outside a stand (scenery offset
                        // — the tolerance is the thing to re-examine) or nowhere near
                        // anything (the coordinate itself is unusable)?
                        probeTrace?.Add(double.IsNaN(nearestMetres)
                            ? $"{probe}=miss(position matched no stand)"
                            : $"{probe}=miss(nearest stand {nearestMetres:F0}m from position)");
                        continue;
                    }
                }
                else
                {
                    probeTrace?.Add($"{probe}=miss(no-position)");
                    continue;
                }
            }

            if (match == null)
            {
                probeTrace?.Add($"{probe}=miss");
                continue;
            }

            // The list for this type may have been snapshotted several candidates
            // ago, so switch back to it before selecting.
            SelectDestinationType(candidate.IsRunway);
            int index = cmbDestination.Items.IndexOf(match);
            if (index < 0)
            {
                probeTrace?.Add($"{probe}=miss(matched '{match}' but the combo no longer lists it)");
                continue;
            }
            // The step is DERIVED from substitution rather than tracked beside it, so
            // destProbe and the gateSubstitution field on the same log line can never
            // disagree about which step seated the stand.
            probeTrace?.Add($"{probe}={(substitution is not GateSubstitution seated ? "name"
                : seated.Kind == GateSubstitutionKind.Alias ? "alias" : "position")}");

            // Silently: this is the import seating its own destination, not the pilot
            // choosing one, and the import's own summary must not be talked over.
            SelectDestinationSilently(index);
            isRunway = candidate.IsRunway;
            label = match;
            gateSubstitution = substitution;
            return true;
        }

        // Nothing was seated, so nothing needs a stand kept visible that the pilot's own
        // filters would hide: put both filters back (and rebuild under them) before
        // restoring the rest. Probing leaves no mark.
        _suppressOccupiedFilter = priorSuppressOccupied;
        _suppressFitFilter = priorSuppressFit;
        if (cmbDestType.SelectedIndex == 1) PopulateDestinations();

        RestoreDestinationState(
            priorType, priorSearch, priorDestination,
            priorIntersection, priorIntersectionLabel, priorCatIiiHold);
        return false;
    }

    /// <summary>The destination label matching a normalized identifier, or null.
    /// BOTH sides are normalized, and zero-padding is a difference in spelling on
    /// either of them: the clearance zero-pads a runway ("05L") where the combo
    /// carries whatever navdata spells ("5L"), and it zero-pads a stand the same way
    /// ("B06" against EDDB's "B 6"). A gate label also carries a terminal descriptor
    /// the clearance never says.</summary>
    internal static string? MatchDestinationLabel(
        IReadOnlyList<string> offered, bool isRunway, string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;

        string wanted = isRunway
            ? SayIntentionsClearanceParser.CleanRunway(identifier) ?? ""
            : SayIntentionsClearanceParser.NormalizeParkingName(identifier);
        if (wanted.Length == 0) return null;

        foreach (string text in offered)
        {
            string candidate = isRunway
                ? SayIntentionsClearanceParser.CleanRunway(text) ?? ""
                : SayIntentionsClearanceParser.NormalizeParkingName(text);

            if (candidate.Length > 0 && candidate.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                return text;
        }

        return null;
    }

    /// <summary>The gate label whose ONLINE ALIASES answer to a published gate name, or
    /// null. Runs after the name failed on this candidate and before its coordinate is
    /// consulted.
    ///
    /// It exists because the alias is INVISIBLE to MatchDestinationLabel, not because the
    /// alias was missing. The combo carries ParkingSpot.ToString() — at KDTW,
    /// "A 24A - Gate Medium, also A24 (online)" — and NormalizeParkingName deletes
    /// everything from the first spaced dash onward, which every Describe() branch puts
    /// ahead of the alias (" - {type}"). So on the live 2026-07-31 KDTW arrival, where
    /// the controller, SayIntentions and OSM all said A24 and only the scenery said A24A,
    /// the assigned gate could not resolve by name at all and destination resolution ran
    /// its whole chain to the ARRIVAL RUNWAY — with the taxiway half of the import
    /// (A5, A, R, hold short of 4R) perfectly correct, so nothing else sounded wrong. The
    /// form's own gate search finds that stand: GateSearchFilter reads ParkingSpot.Aliases
    /// directly. The data was there all along; only this resolver could not see it.</summary>
    private string? MatchGateByAlias(IReadOnlyList<string> gateLabels, string? identifier)
    {
        var offered = new List<AliasedDestination>(gateLabels.Count);
        foreach (string gateLabel in gateLabels)
        {
            if (!_destinationSpotMap.TryGetValue(gateLabel, out var spot)) continue;
            if (spot.Aliases.Count == 0) continue;
            offered.Add(new AliasedDestination(gateLabel, spot.Aliases));
        }

        return MatchDestinationAlias(offered, identifier);
    }

    /// <summary>The offered label one of whose aliases IS the identifier, or null. Both
    /// sides go through NormalizeParkingName, exactly as the name match does, so a full
    /// label ("South Terminal Gate A24") meets a bare alias ("A24") and zero-padding is
    /// still a spelling rather than an identity.
    ///
    /// EXACT normalized comparison, never a substring test. A stand id is one or two
    /// characters, so Contains would match almost any entry the combo offers, and "A2"
    /// must never seat the stand aliased A24 — the wrong-stand failure the padding rules
    /// already exist to prevent, pointed a different way.</summary>
    internal static string? MatchDestinationAlias(
        IReadOnlyList<AliasedDestination> offered, string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;

        string wanted = SayIntentionsClearanceParser.NormalizeParkingName(identifier);
        if (wanted.Length == 0) return null;

        foreach (var entry in offered)
        {
            foreach (string alias in entry.Aliases)
            {
                string candidate = SayIntentionsClearanceParser.NormalizeParkingName(alias);
                if (candidate.Length > 0
                    && candidate.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Label;
                }
            }
        }

        return null;
    }

    /// <summary>The gate label whose own radius reaches a published gate coordinate, or
    /// null. Called only after the NAME and its aliases failed on the same candidate.
    ///
    /// The unit conversion is the point of this method existing at all: ParkingSpot.Radius
    /// is FEET on a navdata spot and METRES on a GSX-sourced one, and the matcher takes
    /// metres so nothing downstream has to remember which it was holding. Feeding raw
    /// feet in is the same mistake ParkingSpot.FitsAircraft records — a navdata stand's
    /// 71 would read as 71 m, a circle three times too big, and a point on the neighbour
    /// would match.
    ///
    /// A gate the maps do not carry is skipped rather than defaulted to (0, 0): a spot
    /// with no known centre cannot be shown to contain anything, and null island is
    /// 150 m from nothing.</summary>
    private string? MatchGateByPosition(
        IReadOnlyList<string> gateLabels, GeoPoint published, out double nearestMetres)
    {
        const double FeetToMetres = 0.3048;

        var candidates = new List<GatePositionCandidate>(gateLabels.Count);
        foreach (string gateLabel in gateLabels)
        {
            if (!_destinationThresholdMap.TryGetValue(gateLabel, out var centre)) continue;
            if (!_destinationSpotMap.TryGetValue(gateLabel, out var spot)) continue;

            double radiusMetres = spot.Source == GateSource.Gsx
                ? spot.Radius
                : spot.Radius * FeetToMetres;

            candidates.Add(new GatePositionCandidate(
                gateLabel, centre.lat, centre.lon, radiusMetres));
        }

        return SayIntentionsGatePositionMatcher.Match(
            candidates, published.Latitude, published.Longitude, out nearestMetres);
    }

    /// <summary>Every label the destination combo offers for one destination type.
    /// Switching type repopulates the list, so callers cache the result per type.</summary>
    private List<string> ListDestinations(bool isRunway)
    {
        SelectDestinationType(isRunway);
        return ComboItemTexts(cmbDestination);
    }

    /// <summary>Switches the destination type WITHOUT speaking the holding-point
    /// call-out. Every caller is an external-route path (the SayIntentions import and
    /// its probing), where the switch is not a pilot choice: OnDestTypeChanged fires
    /// AnnounceDefaultHoldingPoint for runway mode, so probing a runway candidate the
    /// pilot never picked would name its holding point out loud, rewrite the status
    /// line and burn the call-out's dedup key — leaving the real call-out silent when
    /// they later select that runway themselves, and breaking the invariant that
    /// probing leaves no mark. The import speaks its own single summary instead.</summary>
    private void SelectDestinationType(bool isRunway)
    {
        int wanted = isRunway ? 0 : 1;
        if (cmbDestType.SelectedIndex == wanted) return;
        bool prior = _suppressHoldingPointAnnounce;
        _suppressHoldingPointAnnounce = true;
        try { cmbDestType.SelectedIndex = wanted; }
        finally { _suppressHoldingPointAnnounce = prior; }
    }

    private void RestoreDestinationState(
        int priorType, string priorSearch, string? priorDestination,
        bool priorIntersection, string? priorIntersectionLabel, bool priorCatIiiHold)
    {
        // Silent throughout: a restore is the probe undoing itself, so it must not
        // speak — nor burn the holding-point dedup key, which would swallow the real
        // call-out when the pilot next selects that runway ("probing leaves no mark").
        bool priorSuppressAnnounce = _suppressHoldingPointAnnounce;
        _suppressHoldingPointAnnounce = true;
        try
        {
            // Type first: leaving gate mode blanks the gate search, which would undo the
            // search restore if it ran the other way round.
            if (priorType >= 0 && cmbDestType.SelectedIndex != priorType)
                cmbDestType.SelectedIndex = priorType;
            if (txtGateSearch.Text != priorSearch)
                txtGateSearch.Text = priorSearch;

            if (!string.IsNullOrEmpty(priorDestination))
            {
                int index = cmbDestination.Items.IndexOf(priorDestination);
                if (index >= 0) cmbDestination.SelectedIndex = index;
            }

            // Both boxes are runway-only, and the intersection list is rebuilt against
            // whichever runway is selected — so this has to run AFTER the destination is
            // back, or the departure is restored onto the wrong runway's intersections.
            // Inside the suppression too: restoring the LVP tick raises CheckedChanged,
            // which speaks the CAT III call-out.
            RestoreIntersectionState(priorIntersection, priorIntersectionLabel);
            if (chkCatIiiHold.Checked != priorCatIiiHold) chkCatIiiHold.Checked = priorCatIiiHold;
        }
        finally { _suppressHoldingPointAnnounce = priorSuppressAnnounce; }
    }

    /// <summary>Puts the intersection-departure box and its selection back after a failed
    /// destination probe.
    ///
    /// Deliberately NOT done by re-ticking the checkbox and letting its handler run:
    /// OnIntersectionToggled → ShowIntersectionListOrFallback MOVES FOCUS to the
    /// intersection combo and can announce "No runway intersections available. Full
    /// length departure." Neither belongs to a silent restore — the pilot performed no
    /// action here, and CLAUDE.md's announcement rule reserves speech for real state
    /// changes. So the list is rebuilt directly, with the handler detached.</summary>
    private void RestoreIntersectionState(bool wasChecked, string? priorLabel)
    {
        if (!wasChecked)
        {
            if (chkIntersection.Checked) chkIntersection.Checked = false;
            return;
        }

        chkIntersection.CheckedChanged -= OnIntersectionToggled;
        try { chkIntersection.Checked = true; }
        finally { chkIntersection.CheckedChanged += OnIntersectionToggled; }

        PopulateIntersections();
        int index = RestoredIntersectionIndex(ComboItemTexts(cmbIntersection), priorLabel);
        if (index >= 0)
        {
            cmbIntersection.Visible = true;
            cmbIntersection.SelectedIndex = index;
        }
        else
        {
            // Nothing to offer any more. Untick through the handler so the list and its
            // map are cleared too — a checked box over an empty list is the worse state:
            // Calculate silently reverts to a full-length departure while the box still
            // reads as ticked (the bug ShowIntersectionListOrFallback exists to prevent).
            chkIntersection.Checked = false;
        }
    }

    /// <summary>Which intersection entry a restore selects: the one that was selected
    /// before the probe, the FIRST when that exact intersection is no longer offered (the
    /// probe can leave a different runway selected), and -1 when the runway offers none
    /// at all — in which case the caller unticks rather than leaving a checked-but-empty
    /// control.</summary>
    internal static int RestoredIntersectionIndex(IReadOnlyList<string> offered, string? priorLabel)
    {
        if (offered.Count == 0) return -1;

        if (!string.IsNullOrEmpty(priorLabel))
        {
            for (int i = 0; i < offered.Count; i++)
            {
                if (string.Equals(offered[i], priorLabel, StringComparison.Ordinal)) return i;
            }
        }

        return 0;
    }

    private static List<string> ComboItemTexts(ComboBox combo)
    {
        var texts = new List<string>(combo.Items.Count);
        foreach (object? item in combo.Items)
            texts.Add(item?.ToString() ?? "");
        return texts;
    }

    /// <summary>Index of the "Hold short of runway" entry naming this runway, or -1.
    /// BOTH sides go through CleanRunway: a clearance always zero-pads ("05L") while
    /// the combo carries the raw navdata designator, which need not ("5L"). A literal
    /// match silently drops the hold-short ATC just gave.</summary>
    internal static int FindRunwayItemIndex(IReadOnlyList<string> items, string? runway)
    {
        string? wanted = SayIntentionsClearanceParser.CleanRunway(runway);
        if (string.IsNullOrEmpty(wanted)) return -1;

        for (int i = 0; i < items.Count; i++)
        {
            // The "(none)" sentinel carries no runway number and can never match.
            string? candidate = SayIntentionsClearanceParser.CleanRunway(items[i]);
            if (!string.IsNullOrEmpty(candidate)
                && candidate.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>Fills the route fields from an external clearance. Selection is by
    /// EXACT combo entry — never a substring match, which for a one-character
    /// taxiway name would hit almost any entry in the list.
    ///
    /// An imported route starts from a CLEAN SLATE (ResetRouteShapingControls): a
    /// leftover intersection departure, CAT III hold or hold-short from a route the
    /// pilot built by hand would otherwise reshape the imported one with nothing said
    /// about it.
    ///
    /// Each hold-short is seated on the row of the taxiway it FOLLOWS in the
    /// clearance, and anything that could not be seated comes back in the outcome so
    /// the caller can say so out loud.
    ///
    /// This only FILLS the fields. Starting guidance is a separate call
    /// (<see cref="StartImportedRoute"/>) because the caller's summary is built from the
    /// outcome returned here, and that summary has to reach the pilot as part of the
    /// form's own single standstill utterance rather than as queued speech the first
    /// tactical callout discards.</summary>
    public ExternalRouteOutcome ApplyExternalRoute(
        bool isRunway, string destinationLabel, IReadOnlyList<string> taxiways,
        IReadOnlyList<ExternalHoldShort> holdShorts)
    {
        ResetRouteShapingControls();

        // Silent: the import is seating its own destination, and its single standstill
        // summary is what tells the pilot where they are going — a holding-point
        // call-out queued here would either be discarded by that utterance or talk
        // over it, and would burn the call-out's dedup key either way.
        SelectDestinationType(isRunway);
        int destIndex = cmbDestination.Items.IndexOf(destinationLabel);
        bool destinationApplied = destIndex >= 0;
        if (destinationApplied) SelectDestinationSilently(destIndex);

        var applied = new List<string>();
        var skipped = new List<string>();
        var appliedHoldShorts = new List<AppliedHoldShort>();
        var skippedHoldShorts = new List<string>();

        // A hold-short the clearance hung on no taxiway at all has no row to sit on.
        // Reported first because it precedes every taxiway in the clearance.
        foreach (var holdShort in holdShorts)
        {
            if (holdShort.TaxiwayIndex < 0 || holdShort.TaxiwayIndex >= taxiways.Count)
                skippedHoldShorts.Add(holdShort.Runway);
        }

        for (int i = 0; i < taxiways.Count; i++)
        {
            ComboBox combo;
            if (applied.Count == 0)
            {
                combo = cmbFirstTaxiway;
            }
            else
            {
                OnAddTaxiwayClicked(btnAddTaxiway, EventArgs.Empty);
                if (_additionalTaxiways.Count < applied.Count) { SkipTaxiway(i); continue; }
                combo = _additionalTaxiways[applied.Count - 1].Combo;
            }

            int index = combo.Items.IndexOf(taxiways[i]);
            if (index < 0) { SkipTaxiway(i); continue; }

            combo.SelectedIndex = index;
            applied.Add(taxiways[i]);

            // Seat this taxiway's hold-short BEFORE the next row is added: the Add
            // handler only offers the same taxiway a second time when the previous
            // row already carries a hold-short (the KBOS "N, hold short 15R, N"
            // clearance), so seating afterwards would lose the repeat.
            ComboBox holdCombo = applied.Count == 1
                ? cmbFirstHoldShortRunway
                : _additionalTaxiways[applied.Count - 2].HoldShortRunway;
            var holdComboItems = ComboItemTexts(holdCombo);
            bool rowTaken = false;

            foreach (var holdShort in holdShorts)
            {
                if (holdShort.TaxiwayIndex != i) continue;

                // One hold-short per row is all the form — and the router's
                // sequence-index map — can carry.
                int hsIndex = rowTaken ? -1 : FindRunwayItemIndex(holdComboItems, holdShort.Runway);
                if (hsIndex < 0) { skippedHoldShorts.Add(holdShort.Runway); continue; }

                holdCombo.SelectedIndex = hsIndex;
                rowTaken = true;
                appliedHoldShorts.Add(new AppliedHoldShort(holdComboItems[hsIndex], taxiways[i]));
            }
        }

        if (applied.Count == 0)
        {
            int noneIndex = cmbFirstTaxiway.Items.IndexOf("(None - calculate shortest path)");
            if (noneIndex >= 0) cmbFirstTaxiway.SelectedIndex = noneIndex;
        }

        // Always refreshed — PR #86 skipped this on every early-exit path, leaving
        // the Add button's enabled state stale.
        UpdateAddTaxiwayButtonState();

        return new ExternalRouteOutcome(
            destinationApplied, applied, skipped, appliedHoldShorts, skippedHoldShorts);

        void SkipTaxiway(int taxiwayIndex)
        {
            skipped.Add(taxiways[taxiwayIndex]);
            foreach (var holdShort in holdShorts)
            {
                if (holdShort.TaxiwayIndex == taxiwayIndex)
                    skippedHoldShorts.Add(holdShort.Runway);
            }
        }
    }

    /// <summary>The summary an in-progress import wants spoken, as a function of whether
    /// guidance actually started. Non-null only for the duration of the
    /// <see cref="OnCalculateClicked"/> call inside <see cref="StartImportedRoute"/>.</summary>
    private Func<bool, string>? _importSummary;

    /// <summary>Calculates and starts guidance for a route that
    /// <see cref="ApplyExternalRoute"/> has just filled in, speaking the caller's summary
    /// as part of the SAME utterance the form already makes at standstill.
    ///
    /// It cannot be spoken by the caller. Announce() queues, and the sequence here is
    /// Calculate → LoadRoute (queued router summary) → StartGuidance (first-taxiway
    /// AnnounceImmediate, which DISCARDS whatever is queued) → the standstill
    /// AnnounceImmediate. A queued import summary therefore reached the pilot only if
    /// nothing tactical happened first, which is exactly backwards for the safety lines it
    /// carries: "could not apply D, E", "could not set hold short of runway 22L", "the
    /// ground track differs from the clearance". This codebase has learned the same thing
    /// twice already — see the comments on the length advisory in
    /// TaxiGuidanceManager.Routing.cs and on the standstill block below.
    ///
    /// <paramref name="describe"/> takes whether guidance started, so a route that failed
    /// to calculate is never announced as "Guidance started."; it gets the "review the
    /// fields" tail instead, which is also the right advice after a failed Calculate.</summary>
    public void StartImportedRoute(Func<bool, string> describe)
    {
        _importSummary = describe;
        try { OnCalculateClicked(btnCalculate, EventArgs.Empty); }
        finally { _importSummary = null; }
    }

    /// <summary>Announces why Calculate stopped, carrying any pending import summary with
    /// it. Consecutive AnnounceImmediate calls stomp each other, so an abort that spoke
    /// only its own reason would swallow the import's "could not apply …" /
    /// "could not set hold short …" lines — the part a blind pilot cannot otherwise
    /// discover. Guidance did not start, so the summary gets its "review the fields"
    /// tail; the reason leads, because it is why there is no route at all.</summary>
    private void AnnounceCalculateAbort(string reason)
    {
        string? imported = _importSummary?.Invoke(false);
        _announcer.AnnounceImmediate(
            string.IsNullOrEmpty(imported) ? reason : reason + " " + imported);
    }

    /// <summary>Records why there is no route, in the two places a pilot can go looking:
    /// the status line and the route-summary box.
    ///
    /// SPEAKS NOTHING. Every caller has already announced the reason — this is the
    /// re-readable copy, and a second utterance would talk over the first (and break
    /// the screen-reader rule about announcing what the pilot has already been told).
    ///
    /// The summary box exists because speech is heard ONCE and a screen reader
    /// routinely interrupts it, so it is the only place a blind pilot can re-read what
    /// happened. That makes a STALE route sitting in it after a failed Calculate worse
    /// than an empty box: nothing distinguishes last route's summary from one that was
    /// actually just built. Every exit that leaves the pilot with no route comes
    /// through here, so the box holds either a real route or the reason there isn't
    /// one — never a route that was not built.</summary>
    public void ShowRouteFailure(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return;
        lblStatus.Text = reason;
        txtRouteSummary.Text = reason;
    }

    /// <summary>Puts every route-shaping control an import does not itself set back to
    /// its default, so the imported clearance is the WHOLE route.
    ///
    /// OnDestTypeChanged only clears the runway-only boxes when the type CHANGES, so a
    /// runway route imported over a hand-built runway route keeps the old intersection
    /// departure and CAT III hold — a different lineup point with nothing in the
    /// announcement to reveal it.</summary>
    private void ResetRouteShapingControls()
    {
        // Silent: this is the import clearing the pilot's leftovers, not a choice they
        // made, and unticking LVP raises CheckedChanged → the CAT III call-out, which
        // would speak over the import's own single summary.
        bool priorSuppressAnnounce = _suppressHoldingPointAnnounce;
        _suppressHoldingPointAnnounce = true;
        try
        {
        // Unticking fires OnIntersectionToggled, which also empties the intersection
        // list and its map.
        if (chkIntersection.Checked) chkIntersection.Checked = false;
        if (chkCatIiiHold.Checked) chkCatIiiHold.Checked = false;
        if (chkFirstHoldShort.Checked) chkFirstHoldShort.Checked = false;
        if (cmbFirstHoldShortRunway.SelectedIndex > 0) cmbFirstHoldShortRunway.SelectedIndex = 0;

        // Repopulates the gate list on its way out, so it has to run BEFORE the
        // destination is selected.
        if (txtGateSearch.Text.Length > 0) txtGateSearch.Text = "";

        // Every dynamic row's taxiway, hold-short checkbox and hold-short runway go
        // with the row itself.
        ClearAllAdditionalTaxiways();
        }
        finally { _suppressHoldingPointAnnounce = priorSuppressAnnounce; }

        // chkFitFilter is deliberately NOT reset: it describes the aircraft's
        // wingspan rather than the route, and forcing it either way could hide the
        // very gate the clearance names.
    }

    /// <summary>Fire-and-forget wrapper. LoadAirportData was `async void`, so a
    /// throw crashed the app; a bare discard would swallow it silently instead.
    /// Neither is acceptable — log it and tell the pilot.</summary>
    private async Task LoadAirportDataSafeAsync(string icao)
    {
        try
        {
            await LoadAirportDataAsync(icao);
        }
        catch (Exception ex)
        {
            _taxiFormLog.Error($"Airport load failed for '{icao}': {ex}");
            _announcer.Announce($"Could not load airport data for {icao}.");
        }
    }

    private static readonly LogChannel _taxiFormLog = Log.Channel("taxi_guidance");

    /// <summary>The airport load currently in flight, or null. Never awaited by more than
    /// one chained caller at a time; see <see cref="LoadAirportDataAsync"/>.</summary>
    private Task? _airportLoadInFlight;

    /// <summary>Loads an airport into the form's combos, SERIALIZED against any load
    /// already running.
    ///
    /// Every caller is on the UI thread, but this method awaits — the augmentation
    /// prefetch alone can wait seconds — and it CLEARS cmbFirstTaxiway, the dynamic rows
    /// and cmbDestination before those awaits and repopulates them after. Two loads
    /// interleaving at an await point therefore leave one of them repopulating combos the
    /// other has just emptied. The SayIntentions import is what makes that reachable and
    /// dangerous: it can run 9 s end to end (comms history, position, prefetch, graph
    /// build), and an import landing in that window resolved every leg against an empty
    /// combo — announcing "No taxiways from the clearance matched this airport. Using
    /// shortest path." for a perfectly good clearance, and starting guidance on it.
    ///
    /// Chained rather than dropped: a second load is usually a DIFFERENT airport (typed
    /// ICAO, aircraft moved), and dropping it would leave the form on the wrong one.
    /// Three callers reach here — SetAircraftPosition, the airport textbox's Leave
    /// handler, and LoadAirportForExternalRouteAsync.</summary>
    private async Task LoadAirportDataAsync(string icao)
    {
        Task? previous = _airportLoadInFlight;
        var mine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _airportLoadInFlight = mine.Task;
        try
        {
            // The previous load reports its own failures; waiting on it is only about
            // ordering, so a fault there must not cancel this one.
            if (previous != null)
            {
                try { await previous.ConfigureAwait(true); } catch { /* not ours to report */ }
            }

            await LoadAirportDataCoreAsync(icao).ConfigureAwait(true);
        }
        finally
        {
            mine.SetResult();
            if (ReferenceEquals(_airportLoadInFlight, mine.Task)) _airportLoadInFlight = null;
        }
    }

    private async Task LoadAirportDataCoreAsync(string icao)
    {
      try
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

        // Same airport AND the same gate-list source the graph's stand names were built from.
        // The token half is what lets a graph built before GSX published this airport be
        // rebuilt once it does; without it this return matched forever and the graph kept
        // navdata's concourse letters for the session — see _graphSourceToken. Compared through
        // ShouldRebuildGateList so a transient GSX drop (an API→fallback DOWNGRADE) does not
        // throw away a good graph, exactly as the dropdown cache treats the same token.
        string graphToken = _gateSource?.GetGateListVersion(icao) ?? "none";
        if (icao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase) && _graph != null
            && !Services.GateDataSource.ShouldRebuildGateList(_graphSourceToken, graphToken)) return;

        // DROP THE OLD AIRPORT'S GRAPH BEFORE ANYTHING ELSE, and do not claim the new
        // ICAO until one is actually built (below, right after BuildAsync).
        //
        // Every exit between here and there is a failure — airport not in the database,
        // no taxi paths, an exception — and none of them used to touch _graph, while
        // _currentIcao was claimed up front. So a failed load left the form holding the
        // PREVIOUS airport's graph under the NEW airport's name, and the early return
        // above then matched forever, so it could never rebuild.
        //
        // The SayIntentions import turned that from cosmetic into a wrong route:
        // LoadAirportForExternalRouteAsync reports success as "the graph knows some
        // taxiway names", so after taxiing at LMML and flying to an EDDF with no taxi
        // paths, Ctrl+Shift+Y got LMML's taxiway names, snapped EDDF coordinates onto
        // LMML pavement, and — with auto-start on — began guidance on it. A null graph
        // makes the caller's "no taxi path data available" guard do its job, and every
        // _graph == null path in this form already early-returns.
        _graph = null;
        _graphSourceToken = "";
        // Invalidate the gate-branch resolution cache — the new airport has a
        // different graph + parking layout. The cache is also re-validated by
        // ICAO inside the GATE branch of PopulateDestinations (defence in depth),
        // but clearing here frees the old airport's spots immediately.
        _cachedGateSpots = null;
        _cachedGateSpotsIcao = "";
        _cachedGateSpotsSourceToken = "";
        // A previous import's occupied-stand and wingspan suppressions belonged to that
        // airport's assigned gate; the pilot's own filter settings own the new airport's
        // list.
        _suppressOccupiedFilter = false;
        _suppressFitFilter = false;
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
        RebuildHoldShortRunwayCombo(cmbTerminatorRunway);
        _namedHoldingPoints = new List<NamedHoldingPoint>();
        _namedHoldingPointsResolved = false;
        cmbTerminatorHoldPoint.Items.Clear();

        cmbDestination.Items.Clear();

        // Check airport exists
        if (!_dataProvider.AirportExists(icao))
        {
            lblStatus.Text = $"Airport {icao} not found in database.";
            return;
        }

        // Fetch online taxiway-name augmentation BEFORE building the graph, so the taxiway list
        // includes the augmented names on first open. Without this, a cache-miss here returns
        // navdata-only and the graph (which never rebuilds for the same airport — see the early
        // return above) would never pick up the names even after the background fetch lands. A cache
        // hit (departure/destination already prefetched) returns instantly; only a never-fetched
        // airport actually waits, and the status line shows why.
        if (_dataProvider is MSFSBlindAssist.Services.TaxiAugment.AugmentingAirportDataProvider augProvider
            && augProvider.Enabled)
        {
            lblStatus.Text = $"{icao}: fetching taxiway names…";
            // BOUND the wait. A cache hit (dep/dest already prefetched by the flight triggers)
            // completes instantly. A never-fetched airport — typically only an ad-hoc typed ICAO —
            // would otherwise block the form open on a network round-trip up to the HttpClient's
            // 60 s timeout if an Overpass mirror is slow. Wait at most a few seconds; if the fetch
            // hasn't landed, build from navdata NOW. The fetch keeps running in the background
            // (shared in-flight task) and populates the cache, so augmented names appear the next
            // time this airport's taxi form is opened.
            const int prefetchWaitMs = 8000;
            try
            {
                var prefetch = augProvider.PrefetchAsync(icao);
                await Task.WhenAny(prefetch, Task.Delay(prefetchWaitMs));
            }
            catch { /* offline / fetch failed — fall back to navdata names */ }
        }

        // Build graph (off the UI thread to avoid stalls at large airports)
        var paths = _dataProvider.GetTaxiPaths(icao);
        if (paths.Count == 0)
        {
            lblStatus.Text = $"No taxi data for {icao}.";
            return;
        }

        // Navdata's own spot SET — unchanged in count, coordinates and order — carrying the SAME
        // stand names this form's destination dropdown shows, so a stand cannot be "Gate B 25" in
        // the combo and "Gate A 25" when Where-Am-I reads the graph this build produces. The set
        // must stay navdata's: TaxiGraph.Build's parking pass also marks node TYPES, which the
        // hold-short and named-holding-point resolvers read. See Services/ParkingSpotSource.
        var parking = Services.ParkingSpotSource.GetNamedSpots(_dataProvider, _gateSource, icao);
        var starts = _dataProvider.GetRunwayStarts(icao);
        // The runway table is what lets Build run SnapStartToRunwayCenterline, which
        // repairs start rows that navdata stores off to the SIDE of their own runway
        // (EGKK: three of four rows 99–122 m out). Without it this graph pairs its
        // centerlines from the unrepaired rows while the guidance manager's own build
        // is repaired — two graphs disagreeing about where a runway is, and THIS is the
        // one the planner hands to LoadRoute as prebuiltGraph, so it is the one every
        // hold-short association and crossing test on a planned route measures against.
        var graphRunways = _dataProvider.GetRunways(icao);

        lblStatus.Text = $"{icao}: building taxi graph…";
        btnCalculate.Enabled = false;
        try
        {
            _graph = await TaxiGraph.BuildAsync(paths, parking, starts, graphRunways);
        }
        finally
        {
            btnCalculate.Enabled = true;
        }

        // Claimed only now, on a graph that exists. Everything below reads _currentIcao
        // (PopulateDestinations, ResolveNamedHoldingPoints, the gate-spot cache), so it
        // has to be set before them and cannot simply move to the end of the method.
        _currentIcao = icao.ToUpperInvariant();
        // Stamped with the graph, for the same reason and at the same moment: these names are
        // now frozen into the nodes, and this records which source they came from.
        _graphSourceToken = graphToken;

        lblStatus.Text = $"{icao}: {_graph.Nodes.Count} nodes, {paths.Count} paths.";

        // Resolve the airport's published named holding points (VIKAS, N2E…)
        // onto the fresh graph, so the Progressive Taxi "Hold at named holding
        // point" terminator has its list ready on first open. A fetch that
        // hasn't landed yet is fine — the combo re-resolves on dropdown open.
        ResolveNamedHoldingPoints();
        // Refill the combo too — LoadAirportData cleared its Items above and nothing else
        // repopulates it on an airport change (RefreshTerminatorRow only runs on
        // terminator-type / destination-type / taxiway-row changes). Without this the
        // combo reads as EMPTY to a screen reader until the dropdown is opened, and
        // arrowing a DropDownList does not raise DropDown.
        PopulateTerminatorHoldPointList();

        // Populate destinations
        PopulateDestinations();

        // Name the default full-length holding point for the runway the list just
        // auto-selected. Without this the ONE case that never gets the call-out is the
        // pilot who accepts the pre-selected runway — the case where they are least
        // likely to have thought about which painted line the route will use.
        AnnounceDefaultHoldingPoint(statusPrefix: lblStatus.Text);

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
        RebuildHoldShortRunwayCombo(cmbTerminatorRunway);

        // Populate first taxiway combobox sorted by distance, closest first
        PopulateFirstTaxiway();
      }
      catch (Exception ex)
      {
          lblStatus.Text = $"Error loading {icao}: {ex.Message}";
          btnCalculate.Enabled = true;
      }
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

    /// <summary>Selects a destination without the holding-point call-out — the list rebuild
    /// and the external-route import both drive this without the pilot choosing anything.</summary>
    private void SelectDestinationSilently(int index)
    {
        bool prior = _suppressHoldingPointAnnounce;
        _suppressHoldingPointAnnounce = true;
        try { cmbDestination.SelectedIndex = index; }
        finally { _suppressHoldingPointAnnounce = prior; }
    }

    /// <summary>Whether the wingspan "show fitting only" filter applies to this
    /// <see cref="PopulateDestinations"/> pass.
    ///
    /// Three conditions, and the middle one is the fix this method exists to carry: an
    /// EXTERNAL clearance's gate is never wingspan-filtered. That data is authored by
    /// the scenery (<see cref="ParkingSpot.FitsAircraft"/> reads a navdata parking radius
    /// or a GSX max wing span) and is often wrong, while a stand SayIntentions named is a
    /// stand a controller told the pilot to taxi to. Filtered out, the label never reaches
    /// the combo or the maps behind it, so the name, alias and published-coordinate steps
    /// of <see cref="TryResolveExternalDestination"/> all miss together and a known
    /// arrival fails loudly for a stand the airport has.
    ///
    /// The pilot's own tick still decides for their own browsing, and a zero wingspan
    /// means there is nothing to filter against at all.</summary>
    internal static bool ShouldApplyFitFilter(
        bool fitFilterChecked, bool suppressedForExternalDestination, double aircraftWingspanFeet)
        => fitFilterChecked && !suppressedForExternalDestination && aircraftWingspanFeet > 0;

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
            // ALL rows per runway end, not the first one: a runway end can carry
            // several, and TaxiGraph.PickFullLengthStart picks the full-length
            // (furthest-back) one and REJECTS a row past 40 % of the runway. Taking
            // g.First() took whichever the DB happened to return — EGLL 09R's is 342 m
            // down the runway with a 67 m row sitting right there, and LatinVFR's LEMD
            // parks four runways' rows at the MIDPOINT — which anchors both the route
            // destination and the lineup target mid-field, and skews the holding-point
            // envelope's LineupAlong with them.
            var startsByRunway = _dataProvider.GetRunwayStarts(_currentIcao)
                .Where(s => !string.IsNullOrEmpty(s.RunwayName))
                .GroupBy(s => s.RunwayName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

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
                // edge when no start row exists for this runway name — or when
                // PickFullLengthStart rejects every row it has as a midfield
                // placeholder, which is the same "no usable row" case.
                //
                // The row is trusted for WHERE ALONG the runway the departure begins,
                // but pulled back onto the runway_end centerline first: EGKK's rows sit
                // ~110 m to the SIDE of their own runway, which would aim the lineup
                // target — the thing a blind pilot steers by — off the pavement, and
                // feed the same error into the route destination node and the
                // holding-point envelope's near end. TaxiGraph.Build already repairs the
                // rows it uses for the centerlines (SnapStartToRunwayCenterline); this
                // map is the other consumer, and the two must not disagree about where a
                // runway begins. A no-op where the data is sound.
                double lineupLat;
                double lineupLon;
                StartPosition? start = null;
                if (startsByRunway.TryGetValue(rwy.RunwayID, out var rwyStartRows))
                {
                    start = TaxiGraph.PickFullLengthStart(
                        rwyStartRows, rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon);
                }
                if (start != null)
                {
                    (lineupLat, lineupLon) = TaxiGraph.SnapStartToRunwayCenterline(
                        start.Latitude, start.Longitude,
                        rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon);
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
                // ONE entry per spot — same rule as the gate path: ToString() shows any online
                // alias as a "(also X)" hint, and a separate per-alias dropdown entry is NOT added
                // (it doubled the list at airports whose online names differ from the scenery names).
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

                double stopHeading = spot.StopHeading.HasValue
                    ? spot.StopHeading.Value : spot.Heading;
                _destinationNodeMap[label] = nearNode.NodeId;
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
            // The fix: drive the dropdown directly from the parking list —
            // resolved through `Services.ParkingSpotSource`, the one resolution
            // gate teleport, the graph builds and the SayIntentions
            // parked-at-the-right-stand check also use, so a stand cannot be
            // named two different ways in one session — and use
            // `ParkingSpot.ToString()` for identical display labels
            // (e.g. "P 21 - Ramp GA Large (Jetway)").
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
            //
            // ...ONCE per (ICAO, gate-list SOURCE). The token compare is the one thing added
            // to the per-keystroke path, and it is a property read: GateDataSource does no
            // file or DB work to answer it. It is what makes a list bound from the fallback
            // before GSX published this airport rebuild the moment GSX does — the
            // descent-pre-plan / pre-publish scenario described at the field.
            string sourceToken = CurrentGateSourceToken();
            if (_cachedGateSpots == null
                || !_cachedGateSpotsIcao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase)
                || Services.GateDataSource.ShouldRebuildGateList(_cachedGateSpotsSourceToken, sourceToken))
            {
                // The SELECTABLE list — GSX's own, because a destination has to be acted on: the
                // fit filter needs GSX's max wingspan, docking needs the stop position, auto-select
                // needs GsxIdentifier, and TerminalName is what tells two identically-named stands
                // apart. Plus this scenery's online gate aliases (GSX bypasses GetParkingSpots, but
                // GSX stands carry spot codes that don't match real gate numbers, and the alias is
                // what lets the pilot pick the ATC gate).
                var sourceSpots = Services.ParkingSpotSource.GetSelectableGates(_dataProvider, _gateSource, _currentIcao);
                var resolved = new List<(ParkingSpot spot, int nodeId)>(sourceSpots.Count);
                foreach (var spot in sourceSpots)
                {
                    int nodeId = -1; // -1 = no reachable taxi-graph node (kept, marked "(no taxi route)")
                    var nearNode = _graph.FindNearestNode(spot.Latitude, spot.Longitude);
                    if (nearNode != null)
                    {
                        double dist = TaxiGraph.CalculateDistanceMeters(
                            nearNode.Latitude, nearNode.Longitude, spot.Latitude, spot.Longitude);
                        if (dist <= MAX_PARKING_TO_GRAPH_M)
                            nodeId = nearNode.NodeId;
                    }
                    resolved.Add((spot, nodeId));
                }
                _cachedGateSpots = resolved;
                _cachedGateSpotsIcao = _currentIcao;
                _cachedGateSpotsSourceToken = sourceToken;
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
            // Inert while an external clearance's gate is being resolved or is seated
            // (see _suppressFitFilter) — that size data is scenery-authored and often
            // wrong, and the pilot was TOLD to go to that stand.
            if (ShouldApplyFitFilter(chkFitFilter.Checked, _suppressFitFilter, _aircraftWingspan))
                filtered = filtered.Where(r => r.spot.FitsAircraft(_aircraftWingspan));

            // Occupied-stand filter: a stand with an aircraft parked on it is not a
            // destination, so drop it rather than let the pilot taxi to it and hear the
            // "someone is at your gate" warning only at Calculate. Same rule as
            // CheckGateOccupancy (SpotIsOccupied) so the list and that warning can never
            // disagree. Applied PER PASS, never baked into _cachedGateSpots — traffic
            // moves. Silently inert when there is no traffic feed.
            // NOT gated on chkHideOccupied.Visible: that is EFFECTIVE visibility, false
            // while the form itself is hidden, so the same list came back filtered on a
            // shown form and unfiltered on a hidden one (the SayIntentions import builds
            // it hidden). This block already sits inside the gate branch, which is the
            // condition the Visible flag was standing in for.
            int occupiedHidden = 0;
            if (chkHideOccupied.Checked && !_suppressOccupiedFilter && _tcasService != null)
            {
                // Snapshot the traffic ONCE for the whole pass. This runs on every gate-
                // search keystroke, and GetTraffic copies its dictionary under a lock —
                // calling it per spot would be hundreds of locked copies per keystroke.
                var ground = _tcasService.GetTraffic(onGround: true);
                if (ground.Count > 0)
                {
                    var kept = new List<(ParkingSpot spot, int nodeId)>();
                    foreach (var r in filtered)
                    {
                        if (SpotIsOccupied(ground, r.spot.Latitude, r.spot.Longitude) != null) occupiedHidden++;
                        else kept.Add(r);
                    }
                    filtered = kept;
                }
            }

            // Nearest first — a taxi destination is chosen from where the aircraft is
            // standing, so the stands it might actually be going to belong at the top of
            // a list a screen reader walks one item at a time. Falls back to the old
            // category / number / name ordering when the position isn't known yet
            // (LoadAirportData before the first position fix).
            bool haveOwnPosition = _aircraftLat != 0 || _aircraftLon != 0;
            var parkingSpots = haveOwnPosition
                ? filtered
                    .OrderBy(r => TaxiGraph.CalculateDistanceMeters(
                        _aircraftLat, _aircraftLon, r.spot.Latitude, r.spot.Longitude))
                    .ThenBy(r => r.spot.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : filtered
                    .OrderBy(r => categoryOrder.TryGetValue(r.spot.GetFilterCategory(), out int o) ? o : 99)
                    .ThenBy(r => r.spot.Number > 0 ? r.spot.Number : int.MaxValue)
                    .ThenBy(r => r.spot.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            foreach (var (spot, nodeId) in parkingSpots)
            {
                // ONE entry per spot — matches the gate-teleport dialog (GateTeleportForm). ToString()
                // appends any online alias as a "(also X)" hint, and the alias-aware gate search box
                // above (GateSearchFilter, which matches ParkingSpot.Aliases) lets the pilot type the
                // ATC name to find it. We deliberately do NOT add a separate dropdown entry per alias:
                // at airports whose online names differ from the scenery names (e.g. LIMC) that doubled
                // the gate list with a near-identical "online" duplicate of every "scenery" gate.
                string label = spot.ToString();
                if (nodeId < 0) label += " (no taxi route)";
                if (_destinationNodeMap.ContainsKey(label)) continue;

                _destinationNodeMap[label] = nodeId;
                _destinationHeadingMap[label] = spot.Heading;
                _destinationHeadingTrueMap[label] = spot.Heading; // parking heading is true heading
                _destinationThresholdMap[label] = (spot.Latitude, spot.Longitude);
                _destinationSpotMap[label] = spot;
                cmbDestination.Items.Add(label);
            }

            // Say how many stands the occupancy filter removed, on the status line only
            // (never spoken — the pilot is typing in the search box or arrowing the list
            // and the screen reader is already talking). Without it a stand that is
            // simply absent from the list reads as missing scenery data.
            if (occupiedHidden > 0)
                lblStatus.Text = $"{cmbDestination.Items.Count} listed, " +
                                 $"{occupiedHidden} hidden (occupied by traffic).";
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
            // picked in the terminator block's own cmbTerminatorRunway (populated
            // from _airportRunwayIds); the taxiway TARGET is cmbTerminatorTaxiway.
            foreach (var rwy in _dataProvider.GetRunways(_currentIcao).Where(r => !r.IsClosed))
            {
                string label = $"Runway {rwy.RunwayID}";
                if (!_crossRunwayMap.ContainsKey(label))
                    _crossRunwayMap[label] = rwy;
            }

            // Taxiway target list — type-aware (hold-short-of-taxiway = all
            // taxiways; after-crossing = only taxiways crossing the chosen runway).
            PopulateTerminatorTaxiwayList();
        }

        // Silently: the rebuild re-selects index 0 with no pilot involvement, and in gate
        // mode that happens on every gate-search KEYSTROKE — a holding-point call-out per
        // keystroke is noise over the letters the pilot is typing. The type change that
        // legitimately warrants the call-out speaks it itself (OnDestTypeChanged).
        if (cmbDestination.Items.Count > 0)
            SelectDestinationSilently(0);
    }

    /// <summary>The gate-list source token for the loaded airport — see
    /// <see cref="Services.GateDataSource.GetGateListVersion"/>. "none" when this form was
    /// built without a gate source, so the token can never differ and the cache falls back to
    /// its original ICAO-only invalidation. Never throws (GetGateListVersion never does).</summary>
    private string CurrentGateSourceToken()
        => _gateSource?.GetGateListVersion(_currentIcao) ?? "none";

    /// <summary>
    /// Rebuilds the gate destination list when the gate-list SOURCE has moved since the cache
    /// was filled — the fallback → Remote API flip described at <see cref="_cachedGateSpots"/>.
    /// <see cref="PopulateDestinations"/> already checks the token on every pass, but it runs
    /// only on airport load, destination-type change, fit-filter change and gate-search
    /// keystrokes — NOT when the form is re-shown and NOT before Calculate. So a form opened,
    /// hidden and re-shown after GSX caught up, or left open through a whole flight, kept
    /// serving the identifier-less list. This closes those two gaps and is called from
    /// <see cref="OnVisibleChanged"/> and the top of <see cref="OnCalculateClicked"/>.
    /// <para>
    /// A no-op (one string compare) unless the token differs AND the destination type is
    /// Gate / Parking AND a graph is loaded — the only combination in which the cache is even
    /// consulted. When it does rebuild, the pilot's current selection is put back by label
    /// where the new list still carries it; the return value says whether it could NOT be —
    /// which the caller must act on, because <see cref="PopulateDestinations"/> lands the combo
    /// on item 0, a stand the pilot never chose. On the Calculate path that means ABORT, not
    /// "route to item 0"; on the show path it means a queued announcement. Neither is a
    /// UI-interaction echo: the list changed under the pilot because of a background GSX event.
    /// </para>
    /// </summary>
    /// <returns>True when a destination was selected before the rebuild and could not be
    /// restored afterwards (the selection is now cleared — never item 0). False when nothing
    /// was rebuilt, the selection survived, or nothing was selected to begin with.</returns>
    private bool RefreshDestinationsIfGateSourceChanged()
    {
        if (_graph == null || cmbDestType.SelectedIndex != 1) return false;

        string token = CurrentGateSourceToken();
        // Upgrade/refresh only — a transient drop downgrades the token and must NOT
        // rebuild (see GateDataSource.ShouldRebuildGateList); the check inside
        // PopulateDestinations applies the same rule, so both agree.
        if (_cachedGateSpots == null
            || !Services.GateDataSource.ShouldRebuildGateList(_cachedGateSpotsSourceToken, token))
            return false;

        string? previous = cmbDestination.SelectedItem?.ToString();
        PopulateDestinations();

        if (string.IsNullOrEmpty(previous))
        {
            // Nothing was selected before this rebuild — which, on this form, means
            // an EARLIER rebuild already lost the pilot's stand and cleared the
            // selection (PopulateDestinations itself always seats item 0). Keep it
            // cleared: a later same-tier refresh (GSX republishes handlerData on an
            // arrival-gate assignment; a reconnect snapshot) must not quietly seat
            // item 0 and let the next Calculate route to it. The pilot was already
            // told to choose again; nothing new to say.
            if (cmbDestination.Items.Count > 0)
                cmbDestination.SelectedIndex = -1;
            return false;
        }
        int idx = cmbDestination.Items.IndexOf(previous);
        if (idx >= 0)
        {
            cmbDestination.SelectedIndex = idx;
            return false;
        }
        // The pilot's stand is gone from the rebuilt list. LEAVE NOTHING SELECTED:
        // PopulateDestinations lands the combo on item 0, and a later Calculate on
        // that would route (and gate.select) to a stand the pilot never chose —
        // with -1 it aborts with "Please select a destination." instead.
        cmbDestination.SelectedIndex = -1;
        _taxiFormLog.Info($"Gate list for {_currentIcao} rebuilt from a new source ({token}); previous destination '{previous}' is no longer listed.");
        return true;
    }

    /// <summary>Spoken when the gate list was rebuilt from a new source and the pilot's chosen
    /// destination is no longer in it. Shared by the show path (queued) and the Calculate path
    /// (immediate abort) so the pilot hears the same words for the same event.</summary>
    private const string GateListUpdatedMessage = "Gate list updated from GSX. Please choose the destination again.";

    /// <summary>
    /// This form is hide-on-close (see <see cref="OnFormClosing"/>), so re-opening it does not
    /// reload the airport (<see cref="LoadAirportDataCoreAsync"/> early-returns for the same
    /// ICAO) and nothing else re-populated the gate list. Becoming visible is therefore the
    /// moment to notice that GSX has published this airport since the list was last built —
    /// the arrival planned during descent, or the spawn before the first handlerData frame.
    /// The refresh is a single string compare when nothing changed. A lost selection is
    /// announced QUEUED, never immediate: it is a background state change, not the echo of
    /// anything the pilot just did, and it must not stomp whatever the screen reader is
    /// speaking about the window that just opened.
    /// </summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible || IsDisposed) return;

        try
        {
            if (RefreshDestinationsIfGateSourceChanged())
                _announcer.Announce(GateListUpdatedMessage);
        }
        catch (Exception ex)
        {
            // Never let a refresh failure break showing the form.
            _taxiFormLog.Error($"Gate-list refresh on show failed: {ex}");
        }
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
        bool isRunway = cmbDestType.SelectedIndex == 0;
        chkFitFilter.Visible = isGate && _aircraftWingspan > 0;
        chkHideOccupied.Visible = isGate && _tcasService != null;
        lblGateSearch.Visible = isGate;
        txtGateSearch.Visible = isGate;
        if (!isGate)
            txtGateSearch.Text = string.Empty;

        // Intersection departure applies only to runway destinations. Leaving
        // runway mode unticks it and hides the list so a stale intersection can't
        // leak into a gate/progressive/deice route.
        chkIntersection.Visible = isRunway;
        if (!isRunway && chkIntersection.Checked)
            chkIntersection.Checked = false; // fires OnIntersectionToggled → hides + clears
        else if (!isRunway)
            cmbIntersection.Visible = false;

        // Named-holding-point departure is runway-only too — same stale-state rule
        // as the intersection block above.
        chkHoldingPoint.Visible = isRunway;
        if (!isRunway && chkHoldingPoint.Checked)
            chkHoldingPoint.Checked = false; // fires OnHoldingPointToggled → hides + clears
        else if (!isRunway)
            cmbHoldingPoint.Visible = false;

        // CAT III / LVP hold is runway-only too. Leaving runway mode unticks it so
        // a stale low-visibility preference can't leak into a later runway route.
        chkCatIiiHold.Visible = isRunway;
        if (!isRunway)
            chkCatIiiHold.Checked = false;

        // Full-length backtrack departure is runway-only as well — untick on leaving
        // runway mode so a stale backtrack preference can't leak into a later route.
        chkFullLengthBacktrack.Visible = isRunway;
        if (!isRunway)
            chkFullLengthBacktrack.Checked = false;

        // Progressive Taxi has no final destination — hide the gate/runway
        // destination picker and route to a terminator on the last taxiway row
        // instead. Other destination types restore the picker (mirrors the gate
        // visibility toggling above).
        lblDestination.Visible = !isProgressive;
        cmbDestination.Visible = !isProgressive;

        // Progressive Taxi mode hides the per-row "Hold short of runway" combos so
        // the terminator block is the single runway-hold-short control; other modes
        // show them. (Resets hidden combos to "(none)" so routing is unaffected.)
        SetRowRunwayHoldShortVisible(!isProgressive);

        PopulateDestinations();
        RefreshTerminatorRow();

        // Announce "no deicing areas" immediately after populating so the user
        // knows before pressing Calculate that the airport has nothing to route to.
        if (cmbDestType.SelectedIndex == 3 && cmbDestination.Items.Count == 0)
            _announcer.AnnounceImmediate("No deicing areas at this airport.");

        // Entering gate mode: kick a traffic sweep and rebuild once it lands, so the
        // occupied-stand filter works on the first list rather than only after the pilot
        // has typed something. The sweep is asynchronous (responses arrive over the next
        // few dozen ms), which is why this is a request-then-rebuild rather than a read.
        if (isGate) RefreshTrafficThenRepopulate();

        // Name the default holding point for the runway that is now selected (the list
        // was just rebuilt, which mutes the call-out inside PopulateDestinations).
        if (isRunway) AnnounceDefaultHoldingPoint();
    }

    /// <summary>
    /// Requests a fresh SimConnect traffic sweep and rebuilds the gate list when it has
    /// had time to land. TcasService promotes each arriving aircraft into its snapshot,
    /// so a short delay is enough; no-ops without a traffic service or the filter off.
    /// Fire-and-forget by design — the list is already usable meanwhile.
    /// </summary>
    private async void RefreshTrafficThenRepopulate()
    {
        if (_tcasService == null || !chkHideOccupied.Checked) return;
        try
        {
            _tcasService.PollNow();
            await Task.Delay(600);
            // The pilot may have moved on (closed the form, switched destination type)
            // while the sweep was in flight.
            if (IsDisposed || !IsHandleCreated || cmbDestType.SelectedIndex != 1) return;

            // Keep whatever the pilot has selected/typed — PopulateDestinations rebuilds
            // the list and re-selects index 0, which would otherwise silently move the
            // destination out from under them.
            string? selected = cmbDestination.SelectedItem?.ToString();
            PopulateDestinations();
            if (!string.IsNullOrEmpty(selected))
            {
                int idx = cmbDestination.Items.IndexOf(selected);
                if (idx >= 0) SelectDestinationSilently(idx);
            }
        }
        catch (Exception ex)
        {
            Utils.Logging.Log.Debug("TaxiAssist", $"Traffic refresh failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The aircraft parked on the stand at these coordinates, or null. ONE rule, shared
    /// by the destination-list filter and the Calculate-time warning
    /// (<see cref="CheckGateOccupancy"/>) so the list and the warning can never disagree.
    /// Reads TcasService's existing snapshot — never polls; callers that need freshness
    /// request the sweep themselves.
    /// </summary>
    private Models.TcasTraffic? SpotIsOccupied(double lat, double lon) =>
        _tcasService == null ? null : SpotIsOccupied(_tcasService.GetTraffic(onGround: true), lat, lon);

    /// <summary>Same rule against an already-taken traffic snapshot — for callers testing
    /// many stands in one pass (the destination-list filter).</summary>
    private static Models.TcasTraffic? SpotIsOccupied(
        IReadOnlyList<Models.TcasTraffic> groundTraffic, double lat, double lon)
    {
        foreach (var t in groundTraffic)
        {
            if (NavigationCalculator.CalculateDistance(lat, lon, t.Latitude, t.Longitude) <= GATE_OCCUPIED_NM)
                return t;
        }
        return null;
    }

    private void OnIntersectionToggled(object? sender, EventArgs e)
    {
        if (chkIntersection.Checked)
        {
            ShowIntersectionListOrFallback(focusCombo: true);
        }
        else
        {
            cmbIntersection.Visible = false;
            cmbIntersection.Items.Clear();
            _intersectionMap.Clear();
        }
    }

    /// <summary>
    /// Populates the intersection combo for the current runway and either reveals
    /// it with the first entry selected, or — if the runway has no usable
    /// intersections — announces the full-length fallback and unticks the box.
    /// Shared by the checkbox-toggle and runway-change paths so BOTH always leave a
    /// valid entry selected; without this the runway-change path repopulated the
    /// combo but never set SelectedIndex, leaving it blank so Calculate silently
    /// reverted to a full-length departure while the box stayed checked.
    /// </summary>
    private void ShowIntersectionListOrFallback(bool focusCombo)
    {
        PopulateIntersections();
        if (cmbIntersection.Items.Count > 0)
        {
            cmbIntersection.Visible = true;
            cmbIntersection.SelectedIndex = 0;
            if (focusCombo)
                cmbIntersection.Focus();
        }
        else
        {
            // Nothing to offer (sparse navdata, or the taxi graph has no
            // taxiway node on this runway). Fall back to a full-length
            // departure rather than leaving a checked-but-empty control.
            _announcer.AnnounceImmediate("No runway intersections available. Full length departure.");
            chkIntersection.Checked = false; // re-enters OnIntersectionToggled → hides the list
        }
    }

    /// <summary>
    /// Fills the intersection combo with the taxiways that meet the currently
    /// selected runway, each labelled with distance from the threshold and the
    /// runway remaining ahead in the takeoff direction.
    /// </summary>
    private void PopulateIntersections()
    {
        cmbIntersection.Items.Clear();
        _intersectionMap.Clear();

        if (_graph == null || cmbDestType.SelectedIndex != 0) return;
        string? destName = cmbDestination.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(destName)) return;

        // "Runway 22R" → "22R".
        string runwayId = destName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
            ? destName.Substring(7).Trim()
            : destName.Trim();

        // Physical runway geometry (runway_end thresholds), NOT the start-table
        // centerline — the start table sits inside the pavement at displaced
        // thresholds, which would understate the runway length / remaining. The
        // Runway model gives the departure threshold (StartLat/Lon), the far end
        // (EndLat/Lon), and the width, so distance-from-threshold and remaining
        // reflect the true runway.
        if (_cachedRunways == null || _cachedRunwaysIcao != _currentIcao)
        {
            _cachedRunways = _dataProvider.GetRunways(_currentIcao);
            _cachedRunwaysIcao = _currentIcao;
        }
        var rwy = _cachedRunways
            .FirstOrDefault(r => string.Equals(r.RunwayID, runwayId, StringComparison.OrdinalIgnoreCase));
        if (rwy == null) return;

        // Half-width from the runway width (feet → metres); default 150 ft wide.
        double halfWidthM = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;

        // The start-table lineup point (same one full-length departures line up
        // at) lets the enumeration drop the normal full-length entrance — only
        // genuine shortcuts past it are offered (displaced-threshold fix).
        double? lineupLat = null, lineupLon = null;
        if (_destinationThresholdMap.TryGetValue(destName, out var lineup))
        {
            lineupLat = lineup.lat;
            lineupLon = lineup.lon;
        }

        foreach (var ix in _graph.GetRunwayIntersections(
                     rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, halfWidthM,
                     lineupLat, lineupLon))
        {
            string label =
                $"{ix.TaxiwayName}, {DistanceFormatter.FromMetres(ix.RemainingMeters)} remaining, " +
                $"{DistanceFormatter.FromMetres(ix.AlongMetersFromThreshold)} from threshold";
            // Same-named taxiways now legitimately produce MULTIPLE entries (one
            // per meeting point), distinguished by the distances in the label.
            // This guard only fires if display rounding collapses two close
            // meeting points to identical text — then the first (closer to the
            // threshold) wins and the duplicate is dropped rather than shadowed.
            if (_intersectionMap.ContainsKey(label)) continue;
            _intersectionMap[label] = ix;
            cmbIntersection.Items.Add(label);
        }
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

    private void OnHoldingPointToggled(object? sender, EventArgs e)
    {
        if (chkHoldingPoint.Checked)
        {
            ShowHoldingPointListOrFallback(focusCombo: true);
        }
        else
        {
            cmbHoldingPoint.Visible = false;
            cmbHoldingPoint.Items.Clear();
            _holdingPointMap.Clear();
            // Back to the default entry — say which one that is. Forced past the
            // repeat-guard: the pilot has just changed which entry the route uses, so
            // the same answer as before is new information here.
            _lastHoldingPointAnnounceKey = "";
            AnnounceDefaultHoldingPoint();
        }
    }

    /// <summary>
    /// Populates the holding-point combo for the current runway and either reveals
    /// it with the first entry selected, or — when the airport's online data has no
    /// named holding points resolvable to an entry on this runway — announces the
    /// fallback and unticks the box. Same always-leave-a-valid-selection contract as
    /// <see cref="ShowIntersectionListOrFallback"/>.
    /// </summary>
    private void ShowHoldingPointListOrFallback(bool focusCombo)
    {
        PopulateHoldingPoints();
        if (cmbHoldingPoint.Items.Count > 0)
        {
            cmbHoldingPoint.Visible = true;
            cmbHoldingPoint.SelectedIndex = 0;
            if (focusCombo)
                cmbHoldingPoint.Focus();
        }
        else
        {
            _announcer.AnnounceImmediate(
                "No named holding points available for this runway. Full length departure.");
            chkHoldingPoint.Checked = false; // re-enters OnHoldingPointToggled → hides the list
        }
    }

    /// <summary>
    /// Fills the holding-point combo with the PAINTED holding points (online
    /// augmentation names — OSM holding_position refs) that resolve to an entry on
    /// the currently selected runway, each labelled with runway remaining ahead and
    /// distance from the threshold. Unlike the intersection list this INCLUDES
    /// full-length entries: choosing between two full-length stubs by their painted
    /// names (LSZH A2 vs A1) is the point of the feature.
    /// </summary>
    private void PopulateHoldingPoints()
    {
        cmbHoldingPoint.Items.Clear();
        _holdingPointMap.Clear();

        foreach (var (entry, kind, partway) in ResolveHoldingPointsForRunway(
                     cmbDestination.SelectedItem?.ToString()))
        {
            // The kind suffix is what separates the two lines of a pair the pilot is
            // choosing between — EGKK 26L offers A1 and A3 at the SAME entrance and the
            // same distances, and only "(runway hold)" vs the set-back CAT II/III line
            // tells them apart. Same wording as NamedHoldingPoint.DisplayLabel.
            string label =
                $"{entry.TaxiwayName}{HoldingPointKindSuffix(kind)}, " +
                $"{DistanceFormatter.FromMetres(entry.RemainingMeters)} remaining, " +
                $"{DistanceFormatter.FromMetres(entry.AlongMetersFromThreshold)} from threshold";
            if (_holdingPointMap.ContainsKey(label)) continue;
            _holdingPointMap[label] = (entry, partway);
            cmbHoldingPoint.Items.Add(label);
        }
    }

    /// <summary>Spoken-friendly suffix for an OSM <c>holding_position:type</c> tag —
    /// same wording as <see cref="Navigation.NamedHoldingPoint.DisplayLabel"/>.</summary>
    private static string HoldingPointKindSuffix(string kind) =>
        kind.Trim().ToLowerInvariant() switch
        {
            "runway"       => " (runway hold)",
            "ils"          => " (ILS hold)",
            "intermediate" => " (intermediate hold)",
            _              => "",
        };

    /// <summary>
    /// Resolves this airport's PAINTED holding points onto the entries of the runway
    /// named by <paramref name="destName"/> ("Runway 26L"), carrying each point's OSM
    /// <c>holding_position:type</c> kind and whether its entry sits meaningfully past
    /// the full-length lineup point (Partway ⇒ intersection-style lineup semantics).
    ///
    /// Shared by the picker (<see cref="PopulateHoldingPoints"/>) and the default-hold
    /// call-out (<see cref="AnnounceDefaultHoldingPoint"/>) so the two can never
    /// describe the runway differently. Empty when augmentation is off/uncached, the
    /// destination is not a runway, or nothing resolves onto it.
    /// </summary>
    private List<(TaxiGraph.RunwayIntersection Entry, string Kind, bool Partway)>
        ResolveHoldingPointsForRunway(string? destName)
    {
        var resolved = new List<(TaxiGraph.RunwayIntersection Entry, string Kind, bool Partway)>();

        if (_graph == null || cmbDestType.SelectedIndex != 0) return resolved;
        if (string.IsNullOrEmpty(destName)) return resolved;

        var augProvider = _dataProvider as MSFSBlindAssist.Services.TaxiAugment.AugmentingAirportDataProvider;
        if (augProvider == null) return resolved;
        // The KIND-carrying reader: the kind tag is what distinguishes the normal
        // runway hold from the set-back CAT II/III line of the same pair (EGKK M1/M3),
        // which is exactly the choice the call-out below has to make.
        var namedPoints = augProvider.GetNamedHoldingPoints(_currentIcao);
        if (namedPoints.Count == 0) return resolved;

        var kindByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _, _, kind) in namedPoints)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            string key = name.Trim();
            // First non-empty tag wins: a name can be painted twice (two lines of the
            // same designator) and only one of them may carry the type tag.
            if (!kindByName.TryGetValue(key, out var existing) || string.IsNullOrEmpty(existing))
                kindByName[key] = kind?.Trim() ?? "";
        }

        var holdingPoints = namedPoints
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => (Name: p.Name.Trim(), Lat: p.Lat, Lon: p.Lon))
            .ToList();
        if (holdingPoints.Count == 0) return resolved;

        // "Runway 28" → "28".
        string runwayId = destName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
            ? destName.Substring(7).Trim()
            : destName.Trim();

        if (_cachedRunways == null || _cachedRunwaysIcao != _currentIcao)
        {
            _cachedRunways = _dataProvider.GetRunways(_currentIcao);
            _cachedRunwaysIcao = _currentIcao;
        }
        var rwy = _cachedRunways
            .FirstOrDefault(r => string.Equals(r.RunwayID, runwayId, StringComparison.OrdinalIgnoreCase));
        if (rwy == null) return resolved;

        double halfWidthM = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;

        // Measure against the OUTER ENVELOPE of the pavement edges (Runway.Start/End,
        // i.e. runway_end) and the start-table departure lineup points, because navdata
        // disagrees with itself in BOTH directions and either source alone loses entries.
        // EGKK 26L departs 406 m beyond its runway_end, so the edge anchor hid the whole
        // A/M holding area; EGLL 09R and EHAM 36C put the start row 45 m and 480 m INTO
        // the runway, so the lineup anchor dropped the full-length holds behind it (that
        // cost EGLL 09R its N1/NB1 and EHAM 36C its CAT III when measured). See
        // TaxiGraph.ChooseHoldingPointExtent.
        //
        // The far lineup point is found BY DESIGNATOR (26L → 08R) rather than by
        // proximity: at EGKK the 08L lineup point is 359 m from 08R's pavement edge while
        // 08R's own is 442 m, so a nearest-point search picks the wrong runway's far end
        // and skews the whole line. Absent (e.g. the opposite end is closed, so
        // PopulateDestinations never listed it), the pavement edge stands in and the
        // envelope degrades to the edge.
        double lineupLat = rwy.StartLat, lineupLon = rwy.StartLon;
        if (_destinationThresholdMap.TryGetValue(destName, out var lineup))
        {
            lineupLat = lineup.lat;
            lineupLon = lineup.lon;
        }

        double farLineupLat = rwy.EndLat, farLineupLon = rwy.EndLon;
        string? reciprocal = TaxiGraph.ReciprocalRunwayName(rwy.RunwayID);
        if (reciprocal != null &&
            _destinationThresholdMap.TryGetValue($"Runway {reciprocal}", out var farLineup))
        {
            farLineupLat = farLineup.lat;
            farLineupLon = farLineup.lon;
        }

        var (thrLat, thrLon, farLat, farLon, lineupAlong) = TaxiGraph.ChooseHoldingPointExtent(
            rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon,
            lineupLat, lineupLon, farLineupLat, farLineupLon);

        // Entries meaningfully past the lineup point get intersection-style lineup
        // semantics (Partway); entries at/before it are the normal full-length
        // departure via the chosen stub.
        const double FULL_LENGTH_MARGIN_M = 50.0;

        foreach (var entry in _graph.ResolveHoldingPointEntries(
                     holdingPoints,
                     thrLat, thrLon, farLat, farLon, halfWidthM,
                     _aircraftLat, _aircraftLon,
                     // Behind-threshold holds (EGCC 23L VB1/T1) are admitted only when
                     // OSM tags them as GUARDING a runway — the "runway" line or its
                     // set-back "ils" CAT II/III twin. "intermediate" queue-ladder
                     // holds (EGCC V1–V6) and untagged points stay out. So does the
                     // "NO ENTRY" ref EHAM's mappers put on no-entry bars — a prose
                     // safety marking, not a holding-point designator ATC would name.
                     behindThresholdEligible: name =>
                         !string.Equals(name.Replace(" ", "").Replace("-", ""), "NOENTRY",
                             StringComparison.OrdinalIgnoreCase) &&
                         kindByName.TryGetValue(name, out var k) &&
                         (string.Equals(k, "runway", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(k, "ils", StringComparison.OrdinalIgnoreCase)),
                     runwayName: rwy.RunwayID))
        {
            bool partway = entry.AlongMetersFromThreshold > lineupAlong + FULL_LENGTH_MARGIN_M;
            kindByName.TryGetValue(entry.TaxiwayName, out var kind);
            resolved.Add((entry, kind ?? "", partway));
        }

        return resolved;
    }

    /// <summary>
    /// Speaks (and writes to the status line) WHICH painted holding point a plain
    /// runway departure will use, so the pilot knows before they calculate whether the
    /// default matches their clearance — and therefore whether they need to tick
    /// "Depart from named holding point" and choose a different one. At EGKK 26L the
    /// default is M1 on the starter extension, while a "line up via A1" clearance means
    /// the A entrance 112 m further down.
    ///
    /// Named from the SAME resolution the picker uses, so the two can never disagree.
    /// This is an INDICATION of the full-length entry, not a promise about the exact
    /// stop point: hold-short placement on the route stays navdata-authoritative
    /// (TruncateToHoldShort). Silent when the airport has no online holding-point data,
    /// when a picker/intersection selection already owns the entry, and on the repeat of
    /// an already-spoken answer (the combo re-selects itself on every list rebuild).
    /// </summary>
    /// <param name="statusPrefix">When given, the status line keeps this text and the
    /// holding point is appended to it — used by the airport-load path, whose own
    /// "N nodes, M paths" line is the only confirmation the airport loaded.</param>
    private void AnnounceDefaultHoldingPoint(string? statusPrefix = null)
    {
        if (_suppressHoldingPointAnnounce) return;
        if (cmbDestType.SelectedIndex != 0) return;
        // The pilot has named an entry themselves (holding point / intersection) — that
        // selection is the answer and the screen reader already read it out.
        if (chkHoldingPoint.Checked || chkIntersection.Checked) return;

        string? destName = cmbDestination.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(destName)) return;

        bool lvp = chkCatIiiHold.Checked;
        string key = $"{_currentIcao}|{destName}|{(lvp ? "lvp" : "full")}";
        if (key == _lastHoldingPointAnnounceKey) return;
        _lastHoldingPointAnnounceKey = key;

        // Full-length entries only: a partway entry is an intersection departure, which
        // this (untick-everything) route never takes.
        var fullLength = ResolveHoldingPointsForRunway(destName)
            .Where(r => !r.Partway)
            .ToList();
        if (fullLength.Count == 0) return;

        // Default (no LVP): the line closest to the runway — the normal clearance, and
        // the one OSM tags holding_position:type=runway (EGKK M1 of the M1/M3 pair).
        var normal = fullLength.FirstOrDefault(
            r => string.Equals(r.Kind.Trim(), "runway", StringComparison.OrdinalIgnoreCase));
        if (normal.Entry == null) normal = fullLength[0];

        string message;
        if (!lvp)
        {
            message = $"Full length holding point {normal.Entry.TaxiwayName}.";
        }
        else
        {
            // LVP: the set-back CAT II/III line of the same entrance — same entry node,
            // different painted line (EGKK M3 behind M1). Never name the normal line as
            // if it were the CAT III one; say so instead when the pair isn't published.
            var catIii = fullLength.FirstOrDefault(
                r => r.Entry.NodeId == normal.Entry.NodeId
                     && !string.Equals(r.Entry.TaxiwayName, normal.Entry.TaxiwayName,
                                       StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(r.Kind.Trim(), "runway", StringComparison.OrdinalIgnoreCase));

            message = catIii.Entry != null
                ? $"CAT three hold {catIii.Entry.TaxiwayName}."
                : $"CAT three hold not named in online data. Full length holding point {normal.Entry.TaxiwayName}.";
        }

        lblStatus.Text = statusPrefix == null
            ? $"{destName}: {message}"
            : $"{statusPrefix} {destName}: {message}";
        // Queued, never AnnounceImmediate: the screen reader is already speaking the
        // combo item the pilot just landed on, and this follows it rather than cutting
        // across it.
        _announcer.Announce(message);
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
            previousTaxiway = _additionalTaxiways[^1].Combo.SelectedItem?.ToString();
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
            var last = _additionalTaxiways[^1];
            string? rwy = last.HoldShortRunway.SelectedItem?.ToString();
            prevHasHoldShort =
                last.HoldShort.Checked ||
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
            _announcer.AnnounceImmediate("No additional taxiways available at this airport.");
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

        _additionalTaxiways.Add(new TaxiwayRow
        {
            Label = label,
            Combo = combo,
            HoldShort = holdShortChk,
            HoldShortRunwayLabel = lblRunwayHs,
            HoldShortRunway = holdShortRunwayCmb,
            RemoveBtn = removeBtn
        });

        // A row added while already in Progressive Taxi mode must start with its
        // per-row "Hold short of runway" control hidden (the terminator owns the
        // runway hold-short). Applies current-mode visibility to all rows.
        SetRowRunwayHoldShortVisible(cmbDestType.SelectedIndex != 2);

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
        // Remove this taxiway row and all after it. Each row owns its controls
        // (including the second-line "Hold short of runway:" label), so removal is
        // a single pass over row.Controls — no panel scanning by pixel position.
        while (_additionalTaxiways.Count > fromIndex)
        {
            var row = _additionalTaxiways[^1];
            foreach (var c in row.OwnedControls)
            {
                pnlTaxiways.Controls.Remove(c);
                c.Dispose();
            }
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
            lastSelected = _additionalTaxiways[^1].Combo.SelectedItem?.ToString();
        }

        btnAddTaxiway.Enabled = !string.IsNullOrEmpty(lastSelected) && !lastSelected.StartsWith("(None");
    }

    private List<string> GetSelectedTaxiwayNames()
    {
        var names = new List<string>();

        string? first = cmbFirstTaxiway.SelectedItem?.ToString();
        if (!string.IsNullOrEmpty(first) && !first.StartsWith("(None"))
            names.Add(first);

        foreach (var row in _additionalTaxiways)
        {
            string? sel = row.Combo.SelectedItem?.ToString();
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
        foreach (var row in _additionalTaxiways)
        {
            string? sel = row.Combo.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(sel) && !sel.StartsWith("(None"))
            {
                if (row.HoldShort.Checked)
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
        foreach (var row in _additionalTaxiways)
        {
            string? sel = row.Combo.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(sel) && !sel.StartsWith("(None"))
            {
                string? rwy = row.HoldShortRunway.SelectedItem?.ToString();
                if (!string.IsNullOrEmpty(rwy) && rwy != NO_RUNWAY_HOLDSHORT)
                    result[seqIndex] = rwy;
                seqIndex++;
            }
        }

        return result;
    }

    /// <summary>
    /// Show or hide the per-row "Hold short of runway" label + combo across the
    /// first taxiway slot and every dynamic row. In Progressive Taxi mode these
    /// are hidden (the terminator block is the single runway-hold-short control);
    /// in all other destination modes they are shown. On hide, each combo is reset
    /// to "(none)" so a stale selection cannot leak into the route via
    /// GetUserRunwayHoldShorts / OnAddTaxiwayClicked. The "Hold short" checkbox is
    /// intentionally NOT touched (it is a separate concept and stays visible).
    /// </summary>
    private void SetRowRunwayHoldShortVisible(bool visible)
    {
        lblFirstHoldShortRunway.Visible = visible;
        cmbFirstHoldShortRunway.Visible = visible;
        // Guard the reset so it never fires SelectedIndexChanged on an already-(none)
        // combo (harmless today — no handler is wired — but a trap for future ones).
        if (!visible && cmbFirstHoldShortRunway.SelectedIndex != 0)
            cmbFirstHoldShortRunway.SelectedIndex = 0;

        // Each dynamic row owns both its second-line label and combo, so toggle
        // them directly. (The first-row label/combo above live on this.Controls
        // and are handled by field reference.)
        foreach (var row in _additionalTaxiways)
        {
            row.HoldShortRunwayLabel.Visible = visible;
            row.HoldShortRunway.Visible = visible;
            if (!visible && row.HoldShortRunway.SelectedIndex != 0)
                row.HoldShortRunway.SelectedIndex = 0;
        }
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
            lblTerminatorRunway.Visible = false;
            cmbTerminatorRunway.Visible = false;
            lblTerminatorTaxiway.Visible = false;
            cmbTerminatorTaxiway.Visible = false;
            lblTerminatorHoldPoint.Visible = false;
            cmbTerminatorHoldPoint.Visible = false;
            _terminatorBlockHeightPx = 0;
            UpdateLayout();
            return;
        }

        // The terminator block sits just below the current last taxiway row,
        // inside pnlTaxiways. When no additional rows exist, the "last row" is
        // the first-taxiway slot (outside the panel) and the block sits at the
        // top of the panel (blockY 0). Each visible line is LINE_PX tall.
        const int LINE_PX = 28;
        int blockY = _additionalTaxiways.Count * DYNAMIC_ROW_HEIGHT_PX;
        int Line(int n) => blockY + n * LINE_PX;

        // Line 0: terminator type (always shown in progressive mode).
        lblTerminatorType.Location = new System.Drawing.Point(0, Line(0) + 2);
        cmbTerminatorType.Location = new System.Drawing.Point(140, Line(0));
        lblTerminatorType.Visible = true;
        cmbTerminatorType.Visible = true;

        int tType = cmbTerminatorType.SelectedIndex;
        bool needRunwayTarget = tType == 0 || tType == 2;          // hold short / cross
        bool needTaxiwayTarget = tType == 1 || tType == 2;          // hold short taxiway / cross-at
        bool needHoldPointTarget = tType == 4;                      // hold at named holding point

        // Pack visible target combos on consecutive lines beneath the type combo.
        int nextLine = 1;

        // Runway target (line 1 when shown). Label + accessibility match the type.
        if (needRunwayTarget)
        {
            lblTerminatorRunway.Text = tType == 2
                ? "R&unway to cross:"
                : "R&unway to hold short of:";
            lblTerminatorRunway.AccessibleName = tType == 2
                ? "Runway to cross"
                : "Runway to hold short of";
            lblTerminatorRunway.AccessibleDescription = tType == 2
                ? "Pick the runway ATC cleared you to cross. Guidance ends just past this runway."
                : "Pick the runway this progressive leg holds short of. Guidance ends at the hold line.";
            cmbTerminatorRunway.AccessibleDescription = lblTerminatorRunway.AccessibleDescription;
            lblTerminatorRunway.Location = new System.Drawing.Point(0, Line(nextLine) + 2);
            cmbTerminatorRunway.Location = new System.Drawing.Point(180, Line(nextLine));
            nextLine++;
        }
        lblTerminatorRunway.Visible = needRunwayTarget;
        cmbTerminatorRunway.Visible = needRunwayTarget;

        // Taxiway target (next line). For type 1 it is the REQUIRED hold-short
        // taxiway; for type 2 it is the OPTIONAL cross-at taxiway.
        if (needTaxiwayTarget)
        {
            lblTerminatorTaxiway.Text = tType == 2
                ? "Cross at ta&xiway (optional):"
                : "Hold short of taxi&way:";
            lblTerminatorTaxiway.AccessibleName = tType == 2
                ? "Cross at taxiway, optional"
                : "Progressive taxi terminator taxiway label";
            cmbTerminatorTaxiway.AccessibleDescription = tType == 2
                ? "Optional: pick the taxiway at which to cross the runway, when ATC names a crossing point. Lists only taxiways that cross the runway picked above. Leave at \"(none)\" to cross at the nearest point automatically."
                : "Pick the taxiway to hold short of where it meets the last taxiway in your route.";
            lblTerminatorTaxiway.Location = new System.Drawing.Point(0, Line(nextLine) + 2);
            cmbTerminatorTaxiway.Location = new System.Drawing.Point(180, Line(nextLine));
            nextLine++;
        }
        lblTerminatorTaxiway.Visible = needTaxiwayTarget;
        cmbTerminatorTaxiway.Visible = needTaxiwayTarget;
        if (needTaxiwayTarget)
            PopulateTerminatorTaxiwayList();

        // Named-holding-point target (its own line, only for type 4).
        if (needHoldPointTarget)
        {
            lblTerminatorHoldPoint.Location = new System.Drawing.Point(0, Line(nextLine) + 2);
            cmbTerminatorHoldPoint.Location = new System.Drawing.Point(180, Line(nextLine));
            nextLine++;
        }
        lblTerminatorHoldPoint.Visible = needHoldPointTarget;
        cmbTerminatorHoldPoint.Visible = needHoldPointTarget;
        if (needHoldPointTarget)
            PopulateTerminatorHoldPointList();

        // Block height = number of visible lines (type + however many targets).
        _terminatorBlockHeightPx = nextLine * LINE_PX;

        UpdateLayout();
    }

    /// <summary>
    /// Fills cmbTerminatorTaxiway for the current terminator type, preserving the
    /// user's selection by name when possible:
    ///   index 2 (After crossing runway) — "(none)" + only the taxiways that
    ///     physically cross the runway picked in cmbTerminatorRunway.
    ///   index 1 (Hold short of taxiway) — every airport taxiway.
    /// Safe to call repeatedly (RefreshTerminatorRow + the combo's DropDown event).
    /// </summary>
    private void PopulateTerminatorTaxiwayList()
    {
        if (_graph == null) return;
        string? prev = cmbTerminatorTaxiway.SelectedItem?.ToString();
        cmbTerminatorTaxiway.Items.Clear();

        if (cmbTerminatorType.SelectedIndex == 2)
        {
            // After crossing: optional cross-at picker. "(none)" = nearest crossing.
            cmbTerminatorTaxiway.Items.Add(NO_RUNWAY_HOLDSHORT);
            string rwy = TerminatorRunwayTarget();
            if (!string.IsNullOrEmpty(rwy) &&
                _crossRunwayMap.TryGetValue($"Runway {rwy}", out var crossRwy))
            {
                foreach (string tw in GetTaxiwaysCrossingRunway(crossRwy))
                    cmbTerminatorTaxiway.Items.Add(tw);
            }
        }
        else
        {
            // Hold short of taxiway: every airport taxiway.
            foreach (string name in _graph.GetAllTaxiwayNames())
                cmbTerminatorTaxiway.Items.Add(name);
        }

        int idx = 0;
        if (!string.IsNullOrEmpty(prev))
        {
            int found = cmbTerminatorTaxiway.Items.IndexOf(prev);
            if (found >= 0) idx = found;
        }
        if (cmbTerminatorTaxiway.Items.Count > 0)
            cmbTerminatorTaxiway.SelectedIndex = idx;
    }

    // Shown in cmbTerminatorHoldPoint when the airport has no resolvable named
    // holding points (no online data, augmentation off, or none published).
    // Matched exactly at Calculate time to distinguish it from a real pick.
    private const string NO_NAMED_HOLD_POINTS = "(none available at this airport)";

    /// <summary>
    /// Resolves the loaded airport's online NAMED holding points (VIKAS, N2E…)
    /// onto navdata graph nodes. Alias-style per the augmentation safety rules:
    /// the online source contributes the NAME only; the route target is always
    /// the navdata node it resolves to, and unresolvable points are dropped.
    /// One pass over the graph per point, run at most once per airport load once the
    /// online source has data (_namedHoldingPointsResolved); until then the async fetch
    /// may still be in flight, so an empty source leaves the latch clear and the combo
    /// retries on demand.
    /// </summary>
    private void ResolveNamedHoldingPoints()
    {
        _namedHoldingPoints = new List<NamedHoldingPoint>();
        if (_graph == null || string.IsNullOrEmpty(_currentIcao)) return;
        if (_dataProvider is not MSFSBlindAssist.Services.TaxiAugment.AugmentingAirportDataProvider aug
            || !aug.Enabled)
            return;
        var raw = aug.GetNamedHoldingPoints(_currentIcao);
        // Leave the latch clear on an empty source: the online fetch is async, so this
        // is "not yet", not "none" — and retrying is free, we returned before scanning.
        if (raw.Count == 0) return;
        _namedHoldingPoints = NamedHoldingPointResolver.Resolve(_graph, raw);
        _namedHoldingPointsResolved = true;

        // Field diagnostics: a "why isn't VIKAS in my list?" report is otherwise
        // unanswerable without an ad-hoc probe against the user's navdata.
        var resolvedNames = new HashSet<string>(
            _namedHoldingPoints.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var rawNames = raw.Select(p => (p.Name ?? "").Trim())
                          .Where(n => n.Length > 0)
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .ToList();
        var dropped = rawNames.Where(n => !resolvedNames.Contains(n))
                              .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                              .ToList();
        int projected = _namedHoldingPoints.Count(p => p.InsertedOnEdge);
        _taxiRouterLog.Info(
            $"{_currentIcao}: named holding points raw={raw.Count} distinct={rawNames.Count} " +
            $"resolved={_namedHoldingPoints.Count}" +
            (projected > 0 ? $" (of which edge-projected={projected})" : "") +
            (dropped.Count > 0 ? $" dropped={string.Join(", ", dropped)}" : ""));
        foreach (var hp in _namedHoldingPoints)
            _taxiRouterLog.Debug(
                $"  {hp.Name} -> node {hp.NodeId}, {hp.SnapDistanceMeters:F1} m, " +
                $"designated={hp.SnappedToDesignatedNode}, projected={hp.InsertedOnEdge}, " +
                $"kind={(hp.Kind.Length > 0 ? hp.Kind : "-")}");
    }

    /// <summary>
    /// Fills cmbTerminatorHoldPoint with the airport's named holding points
    /// (display labels like "VIKAS (intermediate hold)"), preserving the user's
    /// selection by label when possible. Re-resolves until the online source has been
    /// seen, so a background fetch that landed after form open still surfaces. Safe
    /// to call repeatedly (RefreshTerminatorRow + the combo's DropDown event).
    /// </summary>
    private void PopulateTerminatorHoldPointList()
    {
        if (!_namedHoldingPointsResolved)
            ResolveNamedHoldingPoints();

        string? prev = cmbTerminatorHoldPoint.SelectedItem?.ToString();
        cmbTerminatorHoldPoint.Items.Clear();

        if (_namedHoldingPoints.Count == 0)
        {
            cmbTerminatorHoldPoint.Items.Add(NO_NAMED_HOLD_POINTS);
            cmbTerminatorHoldPoint.SelectedIndex = 0;
            return;
        }

        foreach (var hp in _namedHoldingPoints)
            cmbTerminatorHoldPoint.Items.Add(hp.DisplayLabel);

        int idx = 0;
        if (!string.IsNullOrEmpty(prev))
        {
            int found = cmbTerminatorHoldPoint.Items.IndexOf(prev);
            if (found >= 0) idx = found;
        }
        cmbTerminatorHoldPoint.SelectedIndex = idx;
    }

    private void UpdateLayout()
    {
        // Resize panel to fit all additional taxiways. Each row is
        // DYNAMIC_ROW_HEIGHT_PX tall (two-line: combo + hold-short + remove on
        // top, runway-hold-short combo on bottom).
        int panelHeight = _additionalTaxiways.Count * DYNAMIC_ROW_HEIGHT_PX;
        // Reserve space for the Progressive Taxi terminator block when it is
        // shown below the last row (cmbTerminatorType.Visible + the per-type
        // _terminatorBlockHeightPx are set by RefreshTerminatorRow before this runs).
        if (cmbTerminatorType.Visible)
            panelHeight += _terminatorBlockHeightPx;
        pnlTaxiways.Height = panelHeight;

        // Reposition buttons and status below the panel
        int y = pnlTaxiways.Location.Y + panelHeight;
        if (panelHeight > 0) y += 5;

        btnSayIntentions.Location = new System.Drawing.Point(15, y);
        y += 35;

        btnCalculate.Location = new System.Drawing.Point(15, y);
        btnStop.Location = new System.Drawing.Point(15 + 190, y);
        y += 40;

        lblStatus.Location = new System.Drawing.Point(15, y);
        y += 25;

        // The route-summary block moves with everything above it. Before this,
        // UpdateLayout left it at its construction position, so every added
        // taxiway row pushed the buttons/status DOWN OVER the summary box, and
        // formHeight never accounted for it (its bottom ~27 px sat below the
        // computed client area).
        lblRouteSummary.Location = new System.Drawing.Point(15, y);
        y += 22;
        txtRouteSummary.Location = new System.Drawing.Point(15, y);
        y += txtRouteSummary.Height;

        // Resize form to fit
        int formHeight = y + 15;
        if (formHeight < 480) formHeight = 480;
        this.ClientSize = new System.Drawing.Size(this.ClientSize.Width, formHeight);
    }

    /// <summary>Runs the SayIntentions import — the same call the Ctrl+Shift+Y hotkey
    /// makes, which re-enters this very form (it loads the airport here and applies the
    /// route to these controls). That is safe: the import's own one-at-a-time latch is
    /// taken by the CALLER, not by anything on this path, so a click never contends with
    /// itself; and the airport load it triggers CHAINS on any load already running rather
    /// than rejecting it, so a nested call waits for pending work instead of deadlocking
    /// on it.
    ///
    /// NOTHING IS ANNOUNCED HERE. The screen reader already speaks the button activation,
    /// and the import speaks its own progress and its summary — a third utterance would
    /// only talk over them. A second press while one is running is answered by the
    /// caller's latch, out loud, which is also why the button is not disabled for the
    /// duration: disabling the control that currently has focus moves focus off it.
    ///
    /// try/catch because this is `async void`: the import handles its own failures, but an
    /// escaped throw here would take the app down rather than the operation. QUEUED, not
    /// immediate — the import's own handler reports the specific failure immediately, and
    /// an immediate announcement here would discard that queue to say something vaguer.</summary>
    private async void OnSayIntentionsClicked(object? sender, EventArgs e)
    {
        if (_importFromSayIntentions == null) return;

        try
        {
            await _importFromSayIntentions();
        }
        catch (Exception ex)
        {
            _taxiFormLog.Error($"SayIntentions import from the taxi form failed: {ex}");
            _announcer.Announce("SayIntentions taxi route failed.");
        }
    }

    private void OnCalculateClicked(object? sender, EventArgs e)
    {
        if (_graph == null)
        {
            const string noAirport = "No airport loaded. Enter an ICAO code first.";
            AnnounceCalculateAbort(noAirport);
            ShowRouteFailure(noAirport);
            return;
        }

        // A form left open through a whole flight never re-shows and never reloads the
        // airport, so this is the last moment to notice GSX has published it since the gate
        // list was built (see _cachedGateSpots). Before the destination lookup below and
        // before SelectGsxGateAsync, because both read _destinationSpotMap: acting on the
        // stale entry is exactly the "GSX could not prepare this stand" every-route failure.
        // A no-op (one string compare) unless the source moved. If it did AND the pilot's
        // destination is no longer in the rebuilt list, ABORT rather than route to whatever
        // item 0 now is -- a stand they never chose. Immediate, not queued: this is the answer
        // to the Calculate press, and AnnounceCalculateAbort carries any import summary too.
        if (RefreshDestinationsIfGateSourceChanged())
        {
            AnnounceCalculateAbort(GateListUpdatedMessage);
            ShowRouteFailure(GateListUpdatedMessage);
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
            // Captured alongside the heading it belongs to: the heading above is MAGNETIC,
            // and the route-start turn cue must be composed in TRUE north to agree with the
            // steering tone. Both LoadRoute calls below are downstream of this refresh.
            _aircraftMagVar = pos.MagneticVariation;
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
                _announcer.AnnounceImmediate("Select at least one taxiway for progressive taxi.");
                return;
            }
            // Resolve any online-source alias label (e.g. "B (HAWKER)") to the canonical
            // navdata name BEFORE the graph-distance terminator helpers run — they match
            // taxiway names exactly, and LoadRoute's alias resolution only covers the route
            // SEQUENCE, not these pre-route terminator lookups. Without this, picking an alias
            // label from the terminator dropdown fails with "Could not find taxiway B (HAWKER)".
            lastTaxiway = _graph.ResolveTaxiwayName(lastTaxiway);

            // Component + start node for the graph-distance terminator helpers,
            // mirroring FindFarSideRunwayNode's aircraft-component restriction so
            // the resolved node is actually reachable from the aircraft.
            var startNode = _graph.FindNearestNode(_aircraftLat, _aircraftLon);
            if (startNode == null)
            {
                _announcer.AnnounceImmediate("Could not find your position on the taxi network.");
                return;
            }
            int destComponentId = startNode.ComponentId;

            // The runway TARGET is the terminator block's own runway combo; the
            // taxiway TARGET is cmbTerminatorTaxiway. Per-row "Hold short of runway"
            // combos are NOT consulted here — they remain plain intermediate
            // hold-shorts (carried in progRwyHoldShorts as on every other row).
            string runwayTarget = TerminatorRunwayTarget();   // bare designator, "" if none
            // Resolve an alias label ("B (HAWKER)") to its canonical navdata name; harmless no-op
            // for real names and the "(none)" sentinel. The Hold-short-of-taxiway list is filled
            // from GetAllTaxiwayNames() (which surfaces alias labels), so the selection can be one.
            string taxiwayTarget = _graph.ResolveTaxiwayName(cmbTerminatorTaxiway.SelectedItem?.ToString() ?? "");

            int terminatorTypeIndex = cmbTerminatorType.SelectedIndex;
            int destNode = -1;
            ProgressiveTerminator term;
            var progRwyHoldShorts = GetUserRunwayHoldShorts();

            switch (terminatorTypeIndex)
            {
                case 0: // Hold short of runway
                {
                    if (string.IsNullOrEmpty(runwayTarget))
                    {
                        _announcer.AnnounceImmediate("Pick the runway to hold short of in the terminator runway combo.");
                        return;
                    }
                    // Route to the near-side hold-short node so guidance ENDS at
                    // the hold line (where ProgressiveHold fires) — NOT past the
                    // runway. (ApplyUserRunwayHoldShorts only TAGS an intermediate
                    // hold-short; it does not truncate the route, so routing to the
                    // far-side node would carry the leg across the runway before the
                    // terminal announcement. The near-side node is correct.)
                    var hsNode = ResolveHoldShortRunwayNode(runwayTarget, lastTaxiway);
                    if (hsNode != null) destNode = hsNode.NodeId;
                    term = new ProgressiveTerminator(ProgressiveTerminatorType.HoldShortRunway, runwayTarget);
                    break;
                }
                case 1: // Hold short of taxiway
                {
                    if (string.IsNullOrEmpty(taxiwayTarget))
                    {
                        _announcer.AnnounceImmediate("Pick the taxiway to hold short of.");
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
                        _announcer.AnnounceImmediate("Pick the runway to cross in the terminator runway combo.");
                        return;
                    }
                    // Optional "cross at" taxiway pins the crossing point when ATC
                    // names one ("cross runway 27 at Tango"); "(none)" or unset =
                    // nearest crossing automatically.
                    string? crossAt = (!string.IsNullOrEmpty(taxiwayTarget) && taxiwayTarget != NO_RUNWAY_HOLDSHORT)
                        ? taxiwayTarget
                        : null;
                    if (_crossRunwayMap.TryGetValue($"Runway {runwayTarget}", out var crossRwy))
                    {
                        var farNode = FindFarSideRunwayNode(crossRwy, crossAt);
                        if (farNode != null) destNode = farNode.NodeId;
                    }
                    term = new ProgressiveTerminator(ProgressiveTerminatorType.AfterCrossingRunway, runwayTarget);
                    break;
                }
                case 4: // Hold at named holding point
                {
                    string? label = cmbTerminatorHoldPoint.SelectedItem?.ToString();
                    var holdPoint = _namedHoldingPoints.FirstOrDefault(hp => hp.DisplayLabel == label);
                    if (label == null || label == NO_NAMED_HOLD_POINTS || holdPoint == null)
                    {
                        _announcer.AnnounceImmediate(
                            _namedHoldingPoints.Count == 0
                                ? "No named holding points are available at this airport."
                                : "Pick the holding point to taxi to.");
                        return;
                    }
                    // This is the only terminator whose target is resolved purely by
                    // NAME against the whole graph — every other one derives it from the
                    // aircraft node or the cleared taxiway, so it cannot land off the
                    // aircraft's component. Disconnected taxiway islands are routine
                    // (LOWW/KJFK 6 components, EHAM 4, GCLP's 13-node S5 island), and
                    // LoadRoute picks its start node in the DESTINATION's component with
                    // no distance bound — routing to an island would silently start the
                    // route where the aircraft is not.
                    if (!_graph.Nodes.TryGetValue(holdPoint.NodeId, out var holdNode)
                        || holdNode.ComponentId != destComponentId)
                    {
                        string unreachable =
                            $"Cannot taxi to {holdPoint.Name} from your position. Check your entry.";
                        _announcer.AnnounceImmediate(unreachable);
                        lblStatus.Text = unreachable;
                        return;
                    }
                    destNode = holdPoint.NodeId;
                    term = new ProgressiveTerminator(ProgressiveTerminatorType.HoldAtNamedPoint, holdPoint.Name);
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
                bool pinnedCross = terminatorTypeIndex == 2
                    && !string.IsNullOrEmpty(taxiwayTarget) && taxiwayTarget != NO_RUNWAY_HOLDSHORT;
                string what = terminatorTypeIndex == 1 ? $"taxiway {taxiwayTarget}"
                    : terminatorTypeIndex == 3 ? $"the end of taxiway {lastTaxiway}"
                    : terminatorTypeIndex == 4 ? $"holding point {term.Target}"
                    : pinnedCross ? $"taxiway {taxiwayTarget} crossing runway {runwayTarget}"
                    : $"runway {runwayTarget}";
                string msg = $"Could not find {what} from {lastTaxiway}. Check your entry.";
                _announcer.AnnounceImmediate(msg);
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
                ProgressiveTerminatorType.HoldAtNamedPoint => $"holding point {term.Target}",
                _ => $"end of taxiway {lastTaxiway}",
            };

            string? progError = _guidanceManager.LoadRoute(
                _dataProvider, _currentIcao,
                _aircraftLat, _aircraftLon, _aircraftHeading,
                destNode, progDestName,
                progSeq.Count > 0 ? progSeq : null,
                progHoldShorts,
                // _aircraftHeading is MAGNETIC — see aircraftHeadingMagVar on LoadRoute.
                aircraftHeadingMagVar: _aircraftMagVar,
                destinationHeading: null,
                destinationThresholdLat: null, destinationThresholdLon: null,
                destinationHeadingTrue: null,
                isRunwayDestination: false,
                prebuiltGraph: _graph,
                userRunwayHoldShorts: progRwyHoldShorts.Count > 0 ? progRwyHoldShorts : null,
                progressiveTerminator: term);

            if (progError != null)
            {
                _announcer.AnnounceImmediate(progError);
                ShowRouteFailure(progError);
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
            const string noDestination = "Please select a destination.";
            AnnounceCalculateAbort(noDestination);
            ShowRouteFailure(noDestination);
            return;
        }

        if (destNodeId < 0)
        {
            string unreachable =
                $"No taxi route to {destName}. This stand can't be reached by the taxi network.";
            AnnounceCalculateAbort(unreachable);
            // The spoken sentence, not the old terse status line ("Selected stand has no
            // taxi route."): the box is where the pilot goes to re-read what they heard,
            // so it must carry the same words, including WHICH stand.
            ShowRouteFailure(unreachable);
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

        // Intersection departure: retarget the route to the chosen taxiway
        // intersection. The destination node becomes the taxiway's on-runway
        // node and the lineup target becomes that point on the centerline, so
        // guidance holds short there and lines up partway down the runway
        // instead of at the full-length threshold. destHeading/destHeadingTrue
        // stay the runway's takeoff heading — the centerline is the same line,
        // just entered further along (so Takeoff Assist, seeded from this lineup
        // target, tracks it unchanged). Everything else — TruncateToHoldShort,
        // the hold-short/continue/lineup flow, auto-activate — is relative to the
        // lineup target, so pointing it at the intersection reuses it all.
        // Full-length backtrack departure: retarget the route to an INTERMEDIATE
        // runway entrance (found geometrically, so it works even where taxiways are
        // unnamed — iniBuilds EGNM), but KEEP the lineup target at the full-length
        // threshold. Guidance holds short of the entrance, then backtracks to the
        // threshold and lines up full length. Mutually exclusive with the
        // intersection shortcut (which lines up PARTWAY down and does not backtrack).
        bool backtrackActive = false;
        // Spoken as part of the ONE standstill utterance (or the abort) further down,
        // never on its own — see where it is set.
        string? backtrackFallbackNote = null;
        if (isRunwayDest && chkFullLengthBacktrack.Checked && _graph != null)
        {
            string runwayId = destName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
                ? destName.Substring(7).Trim() : destName.Trim();
            if (_cachedRunways == null || _cachedRunwaysIcao != _currentIcao)
            {
                _cachedRunways = _dataProvider.GetRunways(_currentIcao);
                _cachedRunwaysIcao = _currentIcao;
            }
            var rwy = _cachedRunways
                .FirstOrDefault(r => string.Equals(r.RunwayID, runwayId, StringComparison.OrdinalIgnoreCase));
            if (rwy != null)
            {
                double halfWidthM = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;
                var entry = _graph.FindBacktrackEntryNode(
                    rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, halfWidthM,
                    _aircraftLat, _aircraftLon);
                if (entry != null)
                {
                    destNodeId = entry.NodeId;       // route ends at the entrance
                    backtrackActive = true;          // lineup target stays full length
                }
                else
                {
                    // HELD, not spoken here. An AnnounceImmediate at this point is
                    // stomped moments later by the single standstill AnnounceImmediate
                    // (and by an abort's), so the pilot who ticked backtrack would taxi
                    // off expecting to be taken onto the runway with no audible sign the
                    // mode was dropped. It rides in that one utterance instead — the
                    // same lesson as the import summary and the reach warning below.
                    backtrackFallbackNote =
                        "No backtrack entrance found for this runway. Full length departure.";
                }
            }
        }

        TaxiGraph.RunwayIntersection? intersection = null;
        if (!backtrackActive && isRunwayDest && chkIntersection.Checked
            && cmbIntersection.SelectedItem is string interLabel
            && _intersectionMap.TryGetValue(interLabel, out var ix))
        {
            intersection = ix;
            destNodeId = ix.NodeId;
            thresholdLat = ix.Latitude;
            thresholdLon = ix.Longitude;
        }

        // Named-holding-point departure (skipped when backtracking or when an
        // intersection already retargeted the route — same precedence pattern as
        // backtrack-over-intersection). The painted point SELECTS the entry node;
        // hold-short placement on the resulting route stays navdata-authoritative
        // (TruncateToHoldShort), so guidance holds short at that entry and the
        // normal Continue → lineup → Takeoff Assist flow runs from there. A
        // Partway entry moves the lineup target to the entry's centerline
        // projection (intersection semantics); a full-length entry keeps the
        // start-table lineup point and just forces WHICH stub is used.
        TaxiGraph.RunwayIntersection? holdingPointEntry = null;
        if (!backtrackActive && intersection == null && isRunwayDest && chkHoldingPoint.Checked
            && cmbHoldingPoint.SelectedItem is string hpLabel
            && _holdingPointMap.TryGetValue(hpLabel, out var hp))
        {
            holdingPointEntry = hp.Entry;
            destNodeId = hp.Entry.NodeId;
            if (hp.Partway)
            {
                thresholdLat = hp.Entry.Latitude;
                thresholdLon = hp.Entry.Longitude;
            }
        }

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
            userRunwayHoldShorts: userRunwayHoldShorts.Count > 0 ? userRunwayHoldShorts : null,
            preferIlsHold: isRunwayDest && chkCatIiiHold.Checked,
            fullLengthBacktrack: backtrackActive,
            // Pin the route through the chosen painted hold line, not just its runway entry
            // — otherwise the approach corridor is a free A* choice and can run up a
            // NEIGHBOURING stub that merges with this one short of the runway (EGLL 27R:
            // picked A2, taxied and held at A3). See ApplyHoldingPointPin.
            holdingPointHoldNodeId: holdingPointEntry?.HoldNodeId,
            // _aircraftHeading is MAGNETIC — see aircraftHeadingMagVar on LoadRoute.
            aircraftHeadingMagVar: _aircraftMagVar);

        if (error != null)
        {
            // The dropped-backtrack note leads: it explains what the pilot asked for
            // and did not get, and the abort's own reason follows it.
            AnnounceCalculateAbort(
                backtrackFallbackNote == null ? error : backtrackFallbackNote + " " + error);
            ShowRouteFailure(error);
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

        // Post-StartGuidance standstill speech — ONE utterance. It must come
        // after StartGuidance (whose first-taxiway callout would otherwise stomp
        // it: the reach warning was showing in the box but never heard at
        // Calculate, in-sim 2026-06-13, when spoken before StartGuidance), and
        // it must be a SINGLE AnnounceImmediate: consecutive calls stomp each
        // other, so the intersection confirmation and the reach warning are
        // joined, warning last so the safety-relevant text ends the utterance.
        // (No-op for Progressive Taxi: LastRouteReachWarning is only set for
        // runway destinations, and progressive legs never set a lineup target.)
        //
        // An imported (SayIntentions) route's summary rides at the FRONT of that same
        // utterance — see StartImportedRoute. Front, because the reach warning stays the
        // last thing said, per the ordering above; the import summary leads with its own
        // warnings for the same reason.
        var standstillParts = new List<string>();
        string? imported = _importSummary?.Invoke(true);
        if (!string.IsNullOrEmpty(imported)) standstillParts.Add(imported);
        // A ticked backtrack the airport could not honour — said before the route
        // details, because it changes what the pilot should expect to happen next.
        if (backtrackFallbackNote != null) standstillParts.Add(backtrackFallbackNote);
        if (intersection != null)
        {
            string rwyLabel = destName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
                ? "runway " + destName.Substring(7).Trim()
                : destName;
            standstillParts.Add(
                $"Intersection {intersection.TaxiwayName} departure, {rwyLabel}. " +
                $"About {DistanceFormatter.FromMetres(intersection.RemainingMeters)} of runway ahead.");
        }
        // A named holding-point departure changes WHERE the route meets the runway just
        // as an intersection departure does, so it gets the same confirmation: which
        // painted line the route now runs through, and how much runway is left beyond it.
        // Without it the only difference a pilot could hear between "depart from A2" and
        // "depart from A4" was the hold-short callout minutes later, out on the taxiway.
        if (holdingPointEntry != null)
        {
            string rwyLabel = destName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
                ? "runway " + destName.Substring(7).Trim()
                : destName;
            standstillParts.Add(
                $"Holding point {holdingPointEntry.TaxiwayName}, {rwyLabel}. " +
                $"About {DistanceFormatter.FromMetres(holdingPointEntry.RemainingMeters)} of runway ahead.");
        }
        // The route-start turn cue rides INSIDE this one utterance rather than interrupting
        // it. Live KATL 2026-08-27: it fired as its own AnnounceImmediate 50 ms after this
        // block spoke, and cut the SayIntentions import summary off mid-word -- the fifth
        // time two announcements at Calculate have stomped each other here. An instruction,
        // not a warning, so it sits after the route details and before the reach warning,
        // which stays the last thing said.
        //
        // Consumed UNCONDITIONALLY so the per-frame one-shot can never repeat it, but only
        // SPOKEN when the route reaches its runway -- preserving the suppression the
        // one-shot has always applied: the reach warning is the priority, and a turn cue
        // would be moot (the pilot will reprogram) as well as extra words in front of it.
        string? turnCue = _guidanceManager.ConsumeInitialTurnCue();
        if (!string.IsNullOrEmpty(_guidanceManager.LastRouteReachWarning))
        {
            standstillParts.Add(_guidanceManager.LastRouteReachWarning);
        }
        else if (!string.IsNullOrEmpty(turnCue))
        {
            standstillParts.Add(turnCue);
        }
        if (standstillParts.Count > 0)
            _announcer.AnnounceImmediate(string.Join(" ", standstillParts));

        // GSX gate auto-select: fire-and-forget when heading to a gate and
        // the feature is enabled. Conditions:
        //   - destination is a gate (not runway, not progressive taxi, not deice area)
        //   - setting is on
        //   - a selector was provided (i.e. GsxService existed in this session when
        //     MainForm built this form) — no separate live "is GSX running" check is
        //     needed here any more: GsxRemoteGateSelector.SelectGateAsync feature-checks
        //     the 'gate' capability itself on every call, before ever sending gate.select,
        //     and returns Unavailable (silent) when GSX isn't running, or
        //     GateSelectUnsupported (spoken once per dialog) on a connected pre-4.0.8
        //     build — see SelectGsxGateAsync's own doc comment.
        // NOTE: deice areas (index 3) are explicitly excluded — gate.select prepares a
        // GSX parking stand, which has no deice-pad equivalent. DockingGuidanceManager
        // handles deice guidance via SetDestinationGate (spot.IsDeiceArea is true)
        // without any GSX Remote API interaction.
        if (!isRunwayDest
            && cmbDestType.SelectedIndex != 3
            && SettingsManager.Current.GsxAutoSelectGateOnRoute
            && _gsxGateSelector != null
            && _destinationSpotMap.TryGetValue(destName, out var gsxSpot))
        {
            // Do NOT await — route loading must not block on the GSX round trip.
            _ = SelectGsxGateAsync(gsxSpot);
        }

        // Form stays open so the user can read the summary box while
        // guidance is active. They close it manually with Escape / window-X
        // or by switching focus elsewhere; Stop Guidance button is also
        // available without re-opening.
    }

    /// <summary>
    /// Sends <c>gate.select</c> for <paramref name="spot"/> via <see cref="_gsxGateSelector"/>
    /// and speaks the outcomes a blind pilot cannot learn any other way — see
    /// <see cref="Services.Gsx.Remote.GsxGateSelectAnnouncer"/> for exactly which outcomes
    /// that is and why. Uses the QUEUED announcer (<c>Announce</c>), never
    /// <c>AnnounceImmediate</c>: this is a background GSX decision arriving some time after
    /// the Calculate click, not a direct UI interaction, and it must not interrupt whatever
    /// the pilot is already hearing (the route summary, a taxi callout, …). Never throws —
    /// <see cref="Services.Gsx.Remote.GsxRemoteGateSelector.SelectGateAsync"/> itself never
    /// throws, and <see cref="Services.Gsx.Remote.GsxGateSelectAnnouncer.Describe"/> is pure
    /// string logic over an already-parsed result.
    /// </summary>
    private async Task SelectGsxGateAsync(ParkingSpot spot)
    {
        var result = await _gsxGateSelector!.SelectGateAsync(spot).ConfigureAwait(true);

        // GSX is connected and answering, but its capability list has no 'gate' token —
        // a 4.0.1-4.0.7 build, where gate.select simply does not exist. Say so ONCE per
        // form instance (see the latch field for why that is per app session): the first
        // Calculate is an explicit pilot action that deserves an answer (silence here means
        // taxiing to a stand believing GSX has prepared it, and finding no services on
        // arrival — the same reasoning that gives Prepared/NotFound/BadArgs their own
        // phrases in GsxGateSelectAnnouncer), but this path runs on EVERY
        // gate-destination route calculation, and on an older GSX that is every flight —
        // repeating it would be noise about something already said and unchanged.
        // Deliberately NOT routed through GsxGateSelectAnnouncer.Describe: that mapper is
        // pure and stateless so it stays unit-testable, so the latch belongs here.
        // Everything else about GSX still works on those builds, so this must never reach
        // GsxService.UnavailableReason or the Access GSX status text — announcing "GSX is
        // unavailable" while the GSX window works perfectly would be wrong.
        if (result.Outcome == Services.Gsx.Remote.GsxGateSelectOutcome.GateSelectUnsupported)
        {
            // No lock needed: SelectGsxGateAsync is started from the UI thread and awaits
            // with ConfigureAwait(true), so the test and the set run in one message-loop
            // turn — two overlapping Calculates cannot both pass the check.
            if (!_gsxUnsupportedAnnounced)
            {
                _gsxUnsupportedAnnounced = true;
                _announcer.Announce(Services.Gsx.Remote.GsxGateSelectAnnouncer.GateSelectUnsupportedMessage);
            }
            return;
        }

        string? phrase = Services.Gsx.Remote.GsxGateSelectAnnouncer.Describe(result);
        if (!string.IsNullOrEmpty(phrase))
        {
            _announcer.Announce(phrase);
            // Window route: this form's own announcer speaks it directly, so unlike the
            // GsxService path there is no background/no-listener ambiguity to record.
            Services.Gsx.Remote.GsxDiagnosticLog.Spoke(
                Services.Gsx.Remote.GsxSpeechSource.GateSelect, phrase, Services.Gsx.Remote.SpeechRoute.Background);
        }
        else
        {
            // Several outcomes are silent BY DESIGN (no_airport, a repeat services_active,
            // gsx_not_running/auth_required/Unavailable/TransportFailure). gsx-gate-select.log
            // records what GSX answered, but only this says we then chose to say nothing —
            // without it, "I picked a gate and heard nothing" cannot be told apart from a
            // phrase that was produced and lost. One line per selection, not per tick.
            Services.Gsx.Remote.GsxDiagnosticLog.Hushed(
                Services.Gsx.Remote.GsxSpeechSource.GateSelect, string.Empty, "outcome-map",
                $"outcome {result.Outcome} has no phrase (silent by design)");
        }
    }

    private void CheckGateOccupancy(bool isRunwayDest, double? gateLat, double? gateLon)
    {
        if (isRunwayDest || _tcasService == null || gateLat == null || gateLon == null) return;

        // Force a fresh SimConnect traffic request before reading the snapshot.
        // TcasService's own 3-second poll timer may have just ticked; without
        // PollNow() the snapshot could be up to ~3 s stale when the user clicks
        // Calculate, and an aircraft that just spawned at the gate would be missed.
        // The request is asynchronous so a brand-new occupant within the last
        // few hundred ms can still slip through, but the staleness window
        // shrinks from "up to 3 s" to "one SimConnect roundtrip ≈ 33 ms".
        _tcasService.PollNow();

        var occupying = SpotIsOccupied(gateLat.Value, gateLon.Value);

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
    /// The runway designator chosen as the Progressive Taxi terminator target
    /// (Hold short of runway / After crossing runway), read from the terminator
    /// block's own runway combo. Returns "" when unset ("(none)").
    /// </summary>
    private string TerminatorRunwayTarget()
    {
        string? sel = cmbTerminatorRunway.SelectedItem?.ToString();
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
    /// A node that actually lies on <paramref name="lastTaxiway"/> (the last cleared
    /// taxiway, where "via …, hold short of RWY" should end) is PREFERRED over the
    /// global nearest-centerline node: without this, a complex/parallel airport can
    /// pin the hold to a node on a DIFFERENT taxiway far down the runway, forcing the
    /// constrained router to detour off the cleared sequence. The global nearest is
    /// kept as a fallback when the cleared taxiway has no qualifying node, so this is
    /// never worse than the prior unanchored scan.
    ///
    /// Geometry mirrors <see cref="FindFarSideRunwayNode"/> but selects the
    /// aircraft's side and minimises lateral distance to the centerline so the
    /// chosen node sits just before the runway.
    /// </summary>
    private TaxiNode? ResolveHoldShortRunwayNode(string runwayDesignator, string lastTaxiway)
    {
        if (_graph == null) return null;
        if (!_crossRunwayMap.TryGetValue($"Runway {runwayDesignator}", out var runway))
            return null;

        // Delegated to the pure Navigation helper (probe-tested) so the scenery's
        // DESIGNATED hold-short nodes are preferred over the geometric scan.
        // KSFO 2026-07-01: the geometric scan held the pilot at a plain Q node
        // ~157 m from the 28L centerline while the scenery's HSND hold line sits
        // at ~97 m; the helper picks the designated node closest to the runway.
        return HoldShortNodeResolver.ResolveNearSide(
            _graph, runway, _aircraftLat, _aircraftLon, _aircraftHeading, lastTaxiway);
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
    ///
    /// When <paramref name="crossAtTaxiway"/> is non-null, only far-side nodes lying
    /// on that taxiway are considered — used when ATC names the crossing point
    /// ("cross runway 27 at Tango"). Null restores the default behaviour (nearest
    /// reachable far-side node).
    /// </summary>
    private TaxiNode? FindFarSideRunwayNode(Runway runway, string? crossAtTaxiway = null)
    {
        if (_graph == null) return null;

        // Shared runway-aligned projection frame (positive cross-track = LEFT
        // of the heading) + the legacy feet-as-metres hold setback — both live
        // on the Navigation helpers so the near-side resolver and this far-side
        // finder can never diverge on the math or the deliberate setback (see
        // HoldShortNodeResolver.LegacySetbackMetres for the do-not-"fix" note).
        var frame = RunwayFrame.For(runway, _aircraftLat);
        double minLateralM = HoldShortNodeResolver.LegacySetbackMetres(runway);
        double halfWidthTrueM = HoldShortNodeResolver.TrueHalfWidthMetres(runway);

        double acSignedCT = frame.SignedCrossTrack(_aircraftLat, _aircraftLon);

        // Determine which side to target. The on-runway test uses the TRUE
        // pavement half-width, not the legacy setback: an aircraft stopped at
        // a hold line (routinely INSIDE the legacy floor — KSFO: line ~90 m,
        // floor 97 m) is off the pavement and physically on a side, so the far
        // side is simply the opposite sign regardless of heading.
        int targetSign;
        if (Math.Abs(acSignedCT) >= halfWidthTrueM)
        {
            // Aircraft is off the runway: far side has opposite sign
            targetSign = -Math.Sign(acSignedCT);
        }
        else
        {
            // Aircraft is on the runway: use heading to determine intended exit
            // side — the SHARED heuristic (see HoldShortNodeResolver.
            // HeadingExitSideSign; both operands in the magnetic frame that
            // _aircraftHeading, PLANE HEADING DEGREES MAGNETIC, lives in).
            targetSign = HoldShortNodeResolver.HeadingExitSideSign(runway, _aircraftHeading);
        }

        // Search geometry bounds — shared with HoldShortNodeResolver so the
        // near-side and far-side finders scan the same corridor.
        const double MAX_LATERAL_M = HoldShortNodeResolver.MAX_LATERAL_M;
        const double MAX_ALONG_PAST_END_M = HoldShortNodeResolver.MAX_ALONG_PAST_END_M;

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

            // Pin the crossing to an ATC-named taxiway when requested.
            if (crossAtTaxiway != null && !node.TaxiwayNames.Contains(crossAtTaxiway)) continue;

            double nodeSignedCT = frame.SignedCrossTrack(node.Latitude, node.Longitude);

            if (Math.Sign(nodeSignedCT) != targetSign) continue;
            if (Math.Abs(nodeSignedCT) < minLateralM) continue;
            if (Math.Abs(nodeSignedCT) > MAX_LATERAL_M) continue;

            // Along-track: must be within the runway's length + buffer
            double along = frame.Along(node.Latitude, node.Longitude);
            if (along < -MAX_ALONG_PAST_END_M) continue;
            if (along > frame.LengthM + MAX_ALONG_PAST_END_M) continue;

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
    /// Returns the distinct named taxiways that physically cross
    /// <paramref name="runway"/> — i.e. have a graph edge whose endpoints sit on
    /// opposite sides of the runway centerline, with the crossing falling within
    /// the runway's length. Used to populate the Progressive Taxi "After crossing
    /// runway" terminator's optional "Cross at" picker so the pilot can only choose
    /// a crossing point that actually exists. Sorted alphanumerically. Mirrors the
    /// cross-track / along-track geometry used by FindFarSideRunwayNode.
    /// </summary>
    private List<string> GetTaxiwaysCrossingRunway(Runway runway)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_graph == null) return new List<string>();

        var frame = RunwayFrame.For(runway, runway.StartLat);
        const double ALONG_BUFFER_M = 50.0;

        foreach (var edges in _graph.Adjacency.Values)
        {
            foreach (var edge in edges)
            {
                if (string.IsNullOrEmpty(edge.TaxiwayName)) continue;
                if (names.Contains(edge.TaxiwayName)) continue;
                if (!_graph.Nodes.TryGetValue(edge.FromNodeId, out var a)) continue;
                if (!_graph.Nodes.TryGetValue(edge.ToNodeId, out var b)) continue;

                double ctA = frame.SignedCrossTrack(a.Latitude, a.Longitude);
                double ctB = frame.SignedCrossTrack(b.Latitude, b.Longitude);

                // Edge spans the centerline iff its endpoints are on opposite
                // sides (sign change). Require the crossing to fall within the
                // runway's length so a parallel taxiway that merely touches the
                // centerline beyond a threshold isn't counted.
                if (Math.Sign(ctA) == Math.Sign(ctB)) continue;

                double alongMid = (frame.Along(a.Latitude, a.Longitude) + frame.Along(b.Latitude, b.Longitude)) / 2.0;
                if (alongMid < -ALONG_BUFFER_M) continue;
                if (alongMid > frame.LengthM + ALONG_BUFFER_M) continue;

                names.Add(edge.TaxiwayName);
            }
        }

        var list = names.ToList();
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
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
            _dockingAircraftLog.Info(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "STOPOFFSET  icao='{0}' gate#={1} suffix='{2}' stopLatSet={3} ac='{4}' acId={5} -> long={6:F2} lat={7:F2}",
                _currentIcao, dNumber, dSuffix, dStopLatSet, dIcaoType, dAcId,
                offset.LongitudinalMetres, offset.LateralMetres));
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
