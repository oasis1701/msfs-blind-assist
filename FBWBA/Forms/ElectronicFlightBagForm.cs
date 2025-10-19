using Microsoft.Data.Sqlite;
using FBWBA.Accessibility;
using FBWBA.Database;
using FBWBA.Database.Models;
using FBWBA.Navigation;
using FBWBA.SimConnect;
using FBWBA.Settings;

namespace FBWBA.Forms;
/// <summary>
/// Electronic Flight Bag - Main flight planning and navigation window
/// </summary>
public partial class ElectronicFlightBagForm : Form
{
    private readonly FlightPlanManager _flightPlanManager;
    private readonly SimConnectManager _simConnectManager;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly WaypointTracker _waypointTracker;
    private readonly string _simbriefUsername;
    private SimConnectManager.AircraftPosition? _lastKnownPosition;

    // Navigation tab controls
    private TabControl mainTabControl = null!;
    private DataGridView navigationGridView = null!;
    private ContextMenuStrip waypointContextMenu = null!;
    private Label statusLabel = null!;
    private Button loadSimbriefButton = null!;
    private Button refreshPositionButton = null!;

    // Other tab controls
    private TextBox departureIcaoTextBox = null!;
    private ListBox departureRunwaysListBox = null!;
    private Button loadDepartureButton = null!;

    private TextBox sidIcaoTextBox = null!;
    private ListBox sidsListBox = null!;
    private ListBox sidRunwaysListBox = null!;
    private ListBox sidTransitionsListBox = null!;
    private Button loadSIDButton = null!;

    private TextBox starIcaoTextBox = null!;
    private ListBox starsListBox = null!;
    private ListBox starRunwaysListBox = null!;
    private ListBox starTransitionsListBox = null!;
    private Button loadSTARButton = null!;

    private TextBox arrivalIcaoTextBox = null!;
    private ListBox arrivalRunwaysListBox = null!;
    private Button loadArrivalButton = null!;

    private TextBox approachIcaoTextBox = null!;
    private ListBox approachesListBox = null!;
    private ListBox transitionsListBox = null!;
    private Button loadApproachButton = null!;

    // Airport Lookup tab controls
    private TextBox airportLookupIcaoTextBox = null!;
    private ListBox airportLookupRunwaysListBox = null!;
    private Button airportLookupLoadButton = null!;
    private TextBox airportInfoTextBox = null!;
    private TextBox runwayInfoTextBox = null!;

    public ElectronicFlightBagForm(FlightPlanManager flightPlanManager, SimConnectManager simConnectManager,
                                   ScreenReaderAnnouncer announcer, WaypointTracker waypointTracker, string simbriefUsername)
    {
        _flightPlanManager = flightPlanManager;
        _simConnectManager = simConnectManager;
        _announcer = announcer;
        _waypointTracker = waypointTracker;
        _simbriefUsername = simbriefUsername;

        InitializeComponent();
        SetupEventHandlers();
        SetupAccessibility();
        SetupWaypointContextMenu();

        // Subscribe to aircraft position updates
        _simConnectManager.AircraftPositionReceived += OnAircraftPositionReceived;

        // Only auto-load SimBrief if the flight plan is empty (first time opening)
        // This preserves user modifications (SID, STAR, approaches) across window open/close
        if (!string.IsNullOrEmpty(_simbriefUsername) && _flightPlanManager.CurrentFlightPlan.IsEmpty())
        {
            LoadSimBriefFlightPlan();
        }
        else if (!_flightPlanManager.CurrentFlightPlan.IsEmpty())
        {
            // Flight plan already loaded - just refresh the display
            RefreshNavigationGrid();
        }
    }

    private void OnAircraftPositionReceived(object? sender, SimConnectManager.AircraftPosition position)
    {
        _lastKnownPosition = position;
    }

    private void InitializeComponent()
    {
        Text = "Electronic Flight Bag";
        Size = new Size(1200, 700);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(800, 500);

        // Main tab control
        mainTabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Flight Bag Tabs",
            AccessibleDescription = "Navigate between flight planning tabs"
        };

        // Create tabs
        CreateNavigationTab();
        CreateSIDTab();
        CreateSTARTab();
        CreateApproachTab();
        CreateDepartureTab();
        CreateArrivalTab();
        CreateAirportLookupTab();

        // Status label at bottom
        statusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Ready",
            AccessibleName = "Status",
            Padding = new Padding(5)
        };

        Controls.Add(mainTabControl);
        Controls.Add(statusLabel);
    }

    private void CreateNavigationTab()
    {
        var navTab = new TabPage("Navigation")
        {
            AccessibleName = "Navigation",
            AccessibleDescription = "Flight plan waypoints with distance and bearing"
        };

        var panel = new Panel { Dock = DockStyle.Fill };

        // Top button panel
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(5)
        };

        loadSimbriefButton = new Button
        {
            Text = "Load SimBrief",
            Width = 120,
            Height = 30,
            AccessibleName = "Load SimBrief Flight Plan",
            AccessibleDescription = "Load flight plan from SimBrief"
        };

        refreshPositionButton = new Button
        {
            Text = "Refresh Position (F5)",
            Width = 150,
            Height = 30,
            AccessibleName = "Refresh Aircraft Position",
            AccessibleDescription = "Update distances and bearings from current aircraft position"
        };

        buttonPanel.Controls.Add(loadSimbriefButton);
        buttonPanel.Controls.Add(refreshPositionButton);

        // Navigation grid
        navigationGridView = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            EditMode = DataGridViewEditMode.EditProgrammatically,  // Prevents NVDA COM accessibility crash
            AccessibleName = "Waypoint Navigation List",
            AccessibleDescription = "Flight plan waypoints with distance, bearing, and navigation data. Press F5 to refresh position."
        };

        // Define columns with NotSortable to prevent "not sortable" announcements
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Section", HeaderText = "Sect", Width = 40, FillWeight = 3, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Waypoint", HeaderText = "Waypoint", Width = 80, FillWeight = 8, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Region", HeaderText = "Region", Width = 60, FillWeight = 5, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Distance", HeaderText = "Dist (NM)", Width = 70, FillWeight = 6, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Bearing", HeaderText = "Brg (°)", Width = 70, FillWeight = 6, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Airway", HeaderText = "Airway", Width = 70, FillWeight = 6, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", Width = 60, FillWeight = 5, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "FixType", HeaderText = "Fix Type", Width = 60, FillWeight = 5, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "LegDist", HeaderText = "Leg Dist", Width = 60, FillWeight = 5, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Course", HeaderText = "Course", Width = 60, FillWeight = 5, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Notes", HeaderText = "Notes", Width = 100, FillWeight = 8, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Altitude", HeaderText = "Altitude", Width = 100, FillWeight = 8, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Speed", HeaderText = "Speed", Width = 70, FillWeight = 6, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "SpeedType", HeaderText = "Spd Type", Width = 70, FillWeight = 6, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "ArincType", HeaderText = "ARINC", Width = 60, FillWeight = 5, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Missed", HeaderText = "Missed", Width = 60, FillWeight = 5, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "TrueCourse", HeaderText = "T/M", Width = 40, FillWeight = 3, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "VertAngle", HeaderText = "Vert Angle", Width = 70, FillWeight = 6, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "Time", Width = 50, FillWeight = 4, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "TurnDir", HeaderText = "Turn", Width = 50, FillWeight = 4, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "RNP", HeaderText = "RNP", Width = 50, FillWeight = 4, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Theta", HeaderText = "Theta", Width = 50, FillWeight = 4, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Rho", HeaderText = "Rho", Width = 50, FillWeight = 4, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Flyover", HeaderText = "Flyover", Width = 60, FillWeight = 5, SortMode = DataGridViewColumnSortMode.NotSortable });
        navigationGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "AltFix", HeaderText = "Alt Fix", Width = 60, FillWeight = 5, SortMode = DataGridViewColumnSortMode.NotSortable });

        panel.Controls.Add(navigationGridView);
        panel.Controls.Add(buttonPanel);

        navTab.Controls.Add(panel);
        mainTabControl.TabPages.Add(navTab);
    }

    private void CreateDepartureTab()
    {
        var depTab = new TabPage("Departure")
        {
            AccessibleName = "Departure",
            AccessibleDescription = "Set departure airport and runway"
        };

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

        var icaoLabel = new Label { Text = "Departure Airport ICAO:", Location = new Point(20, 20), Width = 200 };
        departureIcaoTextBox = new TextBox
        {
            Location = new Point(20, 45),
            Width = 100,
            CharacterCasing = CharacterCasing.Upper,
            MaxLength = 4,
            AccessibleName = "Departure ICAO",
            AccessibleDescription = "Enter departure airport ICAO code"
        };

        var runwayLabel = new Label { Text = "Departure Runway:", Location = new Point(20, 80), Width = 200 };
        departureRunwaysListBox = new ListBox
        {
            Location = new Point(20, 105),
            Size = new Size(200, 200),
            AccessibleName = "Departure Runways",
            AccessibleDescription = "Select departure runway"
        };

        loadDepartureButton = new Button
        {
            Text = "Load Departure",
            Location = new Point(20, 320),
            Size = new Size(120, 30),
            AccessibleName = "Load Departure",
            AccessibleDescription = "Load selected departure airport and runway into flight plan"
        };

        panel.Controls.AddRange(new Control[] { icaoLabel, departureIcaoTextBox, runwayLabel, departureRunwaysListBox, loadDepartureButton });
        depTab.Controls.Add(panel);
        mainTabControl.TabPages.Add(depTab);
    }

    private void CreateSIDTab()
    {
        var sidTab = new TabPage("SID")
        {
            AccessibleName = "Standard Instrument Departure",
            AccessibleDescription = "Load SID procedures"
        };

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

        var icaoLabel = new Label { Text = "Airport ICAO:", Location = new Point(20, 20), Width = 200 };
        sidIcaoTextBox = new TextBox
        {
            Location = new Point(20, 45),
            Width = 100,
            CharacterCasing = CharacterCasing.Upper,
            MaxLength = 4,
            AccessibleName = "SID Airport ICAO",
            AccessibleDescription = "Enter airport ICAO code for SID"
        };

        // First: Runways (swapped position with SID procedures)
        var runwayLabel = new Label { Text = "Runways:", Location = new Point(20, 80), Width = 200 };
        sidRunwaysListBox = new ListBox
        {
            Location = new Point(20, 105),
            Size = new Size(150, 150),
            AccessibleName = "SID Runways",
            AccessibleDescription = "Select departure runway"
        };

        // Second: SID Procedures (swapped position with runways)
        var sidLabel = new Label { Text = "SID Procedures:", Location = new Point(190, 80), Width = 200 };
        sidsListBox = new ListBox
        {
            Location = new Point(190, 105),
            Size = new Size(200, 150),
            AccessibleName = "SID Procedures",
            AccessibleDescription = "Select SID procedure for runway"
        };

        // Third: Transitions (unchanged position)
        var transitionLabel = new Label { Text = "Transitions:", Location = new Point(410, 80), Width = 200 };
        sidTransitionsListBox = new ListBox
        {
            Location = new Point(410, 105),
            Size = new Size(200, 150),
            AccessibleName = "SID Transitions",
            AccessibleDescription = "Select SID transition"
        };

        loadSIDButton = new Button
        {
            Text = "Load SID",
            Location = new Point(20, 270),
            Size = new Size(120, 30),
            AccessibleName = "Load SID",
            AccessibleDescription = "Load selected SID procedure into flight plan"
        };

        panel.Controls.AddRange(new Control[] { icaoLabel, sidIcaoTextBox, runwayLabel, sidRunwaysListBox,
                                                sidLabel, sidsListBox,
                                                transitionLabel, sidTransitionsListBox, loadSIDButton });
        sidTab.Controls.Add(panel);
        mainTabControl.TabPages.Add(sidTab);
    }

    private void CreateSTARTab()
    {
        var starTab = new TabPage("STAR")
        {
            AccessibleName = "Standard Terminal Arrival Route",
            AccessibleDescription = "Load STAR procedures"
        };

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

        var icaoLabel = new Label { Text = "Airport ICAO:", Location = new Point(20, 20), Width = 200 };
        starIcaoTextBox = new TextBox
        {
            Location = new Point(20, 45),
            Width = 100,
            CharacterCasing = CharacterCasing.Upper,
            MaxLength = 4,
            AccessibleName = "STAR Airport ICAO",
            AccessibleDescription = "Enter airport ICAO code for STAR"
        };

        // First: Runways (swapped position with STAR procedures)
        var runwayLabel = new Label { Text = "Runways:", Location = new Point(20, 80), Width = 200 };
        starRunwaysListBox = new ListBox
        {
            Location = new Point(20, 105),
            Size = new Size(150, 150),
            AccessibleName = "STAR Runways",
            AccessibleDescription = "Select arrival runway"
        };

        // Second: STAR Procedures (swapped position with runways)
        var starLabel = new Label { Text = "STAR Procedures:", Location = new Point(190, 80), Width = 200 };
        starsListBox = new ListBox
        {
            Location = new Point(190, 105),
            Size = new Size(200, 150),
            AccessibleName = "STAR Procedures",
            AccessibleDescription = "Select STAR procedure for runway"
        };

        // Third: Transitions (unchanged position)
        var transitionLabel = new Label { Text = "Transitions:", Location = new Point(410, 80), Width = 200 };
        starTransitionsListBox = new ListBox
        {
            Location = new Point(410, 105),
            Size = new Size(200, 150),
            AccessibleName = "STAR Transitions",
            AccessibleDescription = "Select STAR transition"
        };

        loadSTARButton = new Button
        {
            Text = "Load STAR",
            Location = new Point(20, 270),
            Size = new Size(120, 30),
            AccessibleName = "Load STAR",
            AccessibleDescription = "Load selected STAR procedure into flight plan"
        };

        panel.Controls.AddRange(new Control[] { icaoLabel, starIcaoTextBox, runwayLabel, starRunwaysListBox,
                                                starLabel, starsListBox,
                                                transitionLabel, starTransitionsListBox, loadSTARButton });
        starTab.Controls.Add(panel);
        mainTabControl.TabPages.Add(starTab);
    }

    private void CreateArrivalTab()
    {
        var arrTab = new TabPage("Arrival")
        {
            AccessibleName = "Arrival",
            AccessibleDescription = "Set arrival airport and runway"
        };

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

        var icaoLabel = new Label { Text = "Arrival Airport ICAO:", Location = new Point(20, 20), Width = 200 };
        arrivalIcaoTextBox = new TextBox
        {
            Location = new Point(20, 45),
            Width = 100,
            CharacterCasing = CharacterCasing.Upper,
            MaxLength = 4,
            AccessibleName = "Arrival ICAO",
            AccessibleDescription = "Enter arrival airport ICAO code"
        };

        var runwayLabel = new Label { Text = "Arrival Runway:", Location = new Point(20, 80), Width = 200 };
        arrivalRunwaysListBox = new ListBox
        {
            Location = new Point(20, 105),
            Size = new Size(200, 200),
            AccessibleName = "Arrival Runways",
            AccessibleDescription = "Select arrival runway"
        };

        loadArrivalButton = new Button
        {
            Text = "Load Arrival",
            Location = new Point(20, 320),
            Size = new Size(120, 30),
            AccessibleName = "Load Arrival",
            AccessibleDescription = "Load selected arrival airport and runway into flight plan"
        };

        panel.Controls.AddRange(new Control[] { icaoLabel, arrivalIcaoTextBox, runwayLabel, arrivalRunwaysListBox, loadArrivalButton });
        arrTab.Controls.Add(panel);
        mainTabControl.TabPages.Add(arrTab);
    }

    private void CreateApproachTab()
    {
        var appTab = new TabPage("Approach")
        {
            AccessibleName = "Approach",
            AccessibleDescription = "Load approach procedures"
        };

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

        var icaoLabel = new Label { Text = "Airport ICAO:", Location = new Point(20, 20), Width = 200 };
        approachIcaoTextBox = new TextBox
        {
            Location = new Point(20, 45),
            Width = 100,
            CharacterCasing = CharacterCasing.Upper,
            MaxLength = 4,
            AccessibleName = "Approach Airport ICAO",
            AccessibleDescription = "Enter airport ICAO code for approach"
        };

        var approachLabel = new Label { Text = "Approaches:", Location = new Point(20, 80), Width = 200 };
        approachesListBox = new ListBox
        {
            Location = new Point(20, 105),
            Size = new Size(300, 150),
            AccessibleName = "Approaches",
            AccessibleDescription = "Select approach procedure"
        };

        var transitionLabel = new Label { Text = "Transitions:", Location = new Point(340, 80), Width = 200 };
        transitionsListBox = new ListBox
        {
            Location = new Point(340, 105),
            Size = new Size(300, 150),
            AccessibleName = "Transitions",
            AccessibleDescription = "Select approach transition"
        };

        loadApproachButton = new Button
        {
            Text = "Load Approach",
            Location = new Point(20, 270),
            Size = new Size(120, 30),
            AccessibleName = "Load Approach",
            AccessibleDescription = "Load selected approach procedure into flight plan"
        };

        panel.Controls.AddRange(new Control[] { icaoLabel, approachIcaoTextBox, approachLabel, approachesListBox,
                                                transitionLabel, transitionsListBox, loadApproachButton });
        appTab.Controls.Add(panel);
        mainTabControl.TabPages.Add(appTab);
    }

    private void CreateAirportLookupTab()
    {
        var lookupTab = new TabPage("Airport Lookup")
        {
            AccessibleName = "Airport Lookup",
            AccessibleDescription = "Lookup detailed airport and runway information"
        };

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

        // ICAO textbox
        var icaoLabel = new Label { Text = "Airport ICAO:", Location = new Point(20, 20), Width = 200 };
        airportLookupIcaoTextBox = new TextBox
        {
            Location = new Point(20, 45),
            Width = 100,
            CharacterCasing = CharacterCasing.Upper,
            MaxLength = 4,
            AccessibleName = "Airport Lookup ICAO",
            AccessibleDescription = "Enter airport ICAO code for lookup"
        };

        // Runway listbox
        var runwayLabel = new Label { Text = "Runways:", Location = new Point(20, 80), Width = 200 };
        airportLookupRunwaysListBox = new ListBox
        {
            Location = new Point(20, 105),
            Size = new Size(200, 100),
            AccessibleName = "Airport Lookup Runways",
            AccessibleDescription = "Select runway for detailed information"
        };

        // Load button
        airportLookupLoadButton = new Button
        {
            Text = "Load",
            Location = new Point(20, 215),
            Size = new Size(100, 30),
            AccessibleName = "Load Airport and Runway Info",
            AccessibleDescription = "Load detailed airport and runway information"
        };

        // Airport info textbox (left side)
        var airportInfoLabel = new Label { Text = "Airport Information:", Location = new Point(20, 255), Width = 250 };
        airportInfoTextBox = new TextBox
        {
            Location = new Point(20, 280),
            Size = new Size(540, 340),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
            AccessibleName = "Airport Information",
            AccessibleDescription = "Detailed airport information"
        };

        // Runway info textbox (right side)
        var runwayInfoLabel = new Label { Text = "Runway Information:", Location = new Point(580, 255), Width = 250 };
        runwayInfoTextBox = new TextBox
        {
            Location = new Point(580, 280),
            Size = new Size(540, 340),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
            AccessibleName = "Runway Information",
            AccessibleDescription = "Detailed runway information"
        };

        panel.Controls.AddRange(new Control[] {
            icaoLabel, airportLookupIcaoTextBox,
            runwayLabel, airportLookupRunwaysListBox,
            airportLookupLoadButton,
            airportInfoLabel, airportInfoTextBox,
            runwayInfoLabel, runwayInfoTextBox
        });
        lookupTab.Controls.Add(panel);
        mainTabControl.TabPages.Add(lookupTab);
    }

    private void SetupEventHandlers()
    {
        // Navigation tab
        loadSimbriefButton.Click += (s, e) => LoadSimBriefFlightPlan();
        refreshPositionButton.Click += (s, e) => RefreshAircraftPosition();

        // Departure tab
        departureIcaoTextBox.TextChanged += DepartureIcaoTextBox_TextChanged;
        loadDepartureButton.Click += LoadDepartureButton_Click;

        // SID tab
        sidIcaoTextBox.TextChanged += SIDIcaoTextBox_TextChanged;
        sidsListBox.SelectedIndexChanged += SIDsListBox_SelectedIndexChanged;
        sidRunwaysListBox.SelectedIndexChanged += SIDRunwaysListBox_SelectedIndexChanged;
        loadSIDButton.Click += LoadSIDButton_Click;

        // STAR tab
        starIcaoTextBox.TextChanged += STARIcaoTextBox_TextChanged;
        starsListBox.SelectedIndexChanged += STARsListBox_SelectedIndexChanged;
        starRunwaysListBox.SelectedIndexChanged += STARRunwaysListBox_SelectedIndexChanged;
        loadSTARButton.Click += LoadSTARButton_Click;

        // Arrival tab
        arrivalIcaoTextBox.TextChanged += ArrivalIcaoTextBox_TextChanged;
        loadArrivalButton.Click += LoadArrivalButton_Click;

        // Approach tab
        approachIcaoTextBox.TextChanged += ApproachIcaoTextBox_TextChanged;
        approachesListBox.SelectedIndexChanged += ApproachesListBox_SelectedIndexChanged;
        loadApproachButton.Click += LoadApproachButton_Click;

        // Airport Lookup tab
        airportLookupIcaoTextBox.TextChanged += AirportLookupIcaoTextBox_TextChanged;
        airportLookupLoadButton.Click += AirportLookupLoadButton_Click;

        // Flight plan manager events
        _flightPlanManager.StatusChanged += (s, msg) => UpdateStatus(msg);
        _flightPlanManager.FlightPlanUpdated += (s, fp) => RefreshNavigationGrid();
    }

    private void SetupAccessibility()
    {
        // Allow F5 key for refresh
        KeyPreview = true;
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                RefreshAircraftPosition();
                e.Handled = true;
            }
        };

        // DataGridView row selection announcement
        // DISABLED: Custom announcements cause performance lag during keyboard navigation
        // Screen readers (NVDA/JAWS) already read grid cells using native DataGridView accessibility
        // navigationGridView.SelectionChanged += navigationGridView_SelectionChanged;
    }

    private void SetupWaypointContextMenu()
    {
        // Create context menu
        waypointContextMenu = new ContextMenuStrip
        {
            AccessibleName = "Waypoint Tracking Menu",
            AccessibleDescription = "Track waypoint in slots 1 through 5"
        };

        // Add menu items for each tracking slot
        for (int i = 1; i <= 5; i++)
        {
            var menuItem = new ToolStripMenuItem($"Track Slot {i}")
            {
                AccessibleName = $"Track in Slot {i}",
                Tag = i // Store slot number in Tag
            };
            menuItem.Click += TrackSlotMenuItem_Click;
            waypointContextMenu.Items.Add(menuItem);
        }

        // Attach context menu to navigation grid
        navigationGridView.ContextMenuStrip = waypointContextMenu;

        // Handle keyboard events for application key (VK_APPS)
        navigationGridView.KeyDown += NavigationGridView_KeyDown;

        // Handle mouse events for right-click
        navigationGridView.MouseDown += NavigationGridView_MouseDown;
    }

    private void NavigationGridView_KeyDown(object? sender, KeyEventArgs e)
    {
        // Handle application key (context menu key) - typically right of spacebar
        if (e.KeyCode == Keys.Apps)
        {
            ShowContextMenuForSelectedRow();
            e.Handled = true;
        }
    }

    private void NavigationGridView_MouseDown(object? sender, MouseEventArgs e)
    {
        // Handle right-click
        if (e.Button == MouseButtons.Right)
        {
            // Get row index at mouse position
            var hitTest = navigationGridView.HitTest(e.X, e.Y);
            if (hitTest.RowIndex >= 0)
            {
                // Select the row that was right-clicked
                navigationGridView.ClearSelection();
                navigationGridView.Rows[hitTest.RowIndex].Selected = true;
                navigationGridView.CurrentCell = navigationGridView.Rows[hitTest.RowIndex].Cells[0];
            }
        }
    }

    private void ShowContextMenuForSelectedRow()
    {
        if (navigationGridView.CurrentRow != null && navigationGridView.CurrentRow.Index >= 0)
        {
            // Get the cell position to show the menu
            var rect = navigationGridView.GetCellDisplayRectangle(0, navigationGridView.CurrentRow.Index, true);
            waypointContextMenu.Show(navigationGridView, rect.Left, rect.Top + rect.Height);
        }
    }

    private void TrackSlotMenuItem_Click(object? sender, EventArgs e)
    {
        var menuItem = sender as ToolStripMenuItem;
        if (menuItem?.Tag == null) return;

        int slotNumber = (int)menuItem.Tag;

        if (navigationGridView.CurrentRow == null || navigationGridView.CurrentRow.Index < 0)
        {
            _announcer.Announce("No waypoint selected");
            return;
        }

        // Get the selected waypoint from the flight plan
        var waypoints = _flightPlanManager.CurrentFlightPlan.GetAllWaypoints();
        int selectedIndex = navigationGridView.CurrentRow.Index;

        if (selectedIndex >= waypoints.Count)
        {
            _announcer.Announce("Invalid waypoint selection");
            return;
        }

        var waypoint = waypoints[selectedIndex];

        // Track the waypoint
        _waypointTracker.TrackWaypoint(slotNumber, waypoint);

        // Announce to screen reader
        _announcer.Announce($"Waypoint {waypoint.Ident} tracked in slot {slotNumber}");
    }

    private void LoadSimBriefFlightPlan()
    {
        try
        {
            if (string.IsNullOrEmpty(_simbriefUsername))
            {
                MessageBox.Show("SimBrief username not configured. Please configure in settings.",
                              "SimBrief Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            UpdateStatus("Loading flight plan from SimBrief...");
            _flightPlanManager.LoadFromSimBrief(_simbriefUsername);
            _announcer.Announce("SimBrief flight plan loaded");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading SimBrief flight plan: {ex.Message}",
                          "SimBrief Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _announcer.Announce("Error loading SimBrief flight plan");
        }
    }

    private void RefreshAircraftPosition()
    {
        if (_simConnectManager?.IsConnected != true)
        {
            _announcer.Announce("Not connected to simulator");
            return;
        }

        // Request current aircraft position with async callback to avoid race condition
        _simConnectManager.RequestAircraftPositionAsync(position =>
        {
            // Marshal to UI thread if needed
            if (InvokeRequired)
            {
                Invoke(new Action(() =>
                {
                    _flightPlanManager.UpdateAircraftPosition(
                        position.Latitude,
                        position.Longitude,
                        position.MagneticVariation);

                    _announcer.Announce("Position updated");
                    UpdateStatus("Aircraft position updated");
                }));
            }
            else
            {
                _flightPlanManager.UpdateAircraftPosition(
                    position.Latitude,
                    position.Longitude,
                    position.MagneticVariation);

                _announcer.Announce("Position updated");
                UpdateStatus("Aircraft position updated");
            }
        });
    }

    private void RefreshNavigationGrid()
    {
        // Safety check: Ensure columns are defined before attempting to add rows
        if (navigationGridView.Columns.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("WARNING: NavigationGridView has no columns. Skipping refresh.");
            return;
        }

        // Save current cell position (row and column) before clearing
        int previousRowIndex = -1;
        int previousColumnIndex = 0;
        if (navigationGridView.CurrentCell != null)
        {
            previousRowIndex = navigationGridView.CurrentCell.RowIndex;
            previousColumnIndex = navigationGridView.CurrentCell.ColumnIndex;
        }

        navigationGridView.Rows.Clear();

        var waypoints = _flightPlanManager.CurrentFlightPlan.GetAllWaypoints();

        foreach (var waypoint in waypoints)
        {
            string sectionLabel = GetSectionLabel(waypoint.Section);

            // Distance and Bearing (calculated, not from database)
            string distanceText = waypoint.DistanceFromAircraft.HasValue ?
                $"{waypoint.DistanceFromAircraft.Value:F1}" : "-";
            string bearingText = waypoint.BearingFromAircraft.HasValue ?
                $"{waypoint.BearingFromAircraft.Value:000}" : "-";

            // Raw database values
            string courseText = waypoint.Course.HasValue ? $"{waypoint.Course.Value:F0}" : "-";
            string trueCourseText = waypoint.IsTrueCourse ? "T" : "M";
            string legDistText = waypoint.Distance.HasValue ? $"{waypoint.Distance.Value:F1}" : "-";
            string altitudeText = waypoint.AltitudeRestriction ?? "-";
            string speedText = waypoint.SpeedLimit.HasValue ? $"{waypoint.SpeedLimit}" : "-";
            string speedTypeText = waypoint.SpeedLimitType ?? "-";
            string vertAngleText = waypoint.VerticalAngle.HasValue ? $"{waypoint.VerticalAngle.Value:F0}" : "-";
            string rnpText = waypoint.RNP.HasValue ? $"{waypoint.RNP.Value:F2}" : "-";
            string timeText = waypoint.Time.HasValue ? $"{waypoint.Time.Value:F1}" : "-";
            string thetaText = waypoint.Theta.HasValue ? $"{waypoint.Theta.Value:F1}" : "-";
            string rhoText = waypoint.Rho.HasValue ? $"{waypoint.Rho.Value:F1}" : "-";
            string flyoverText = waypoint.IsFlyover ? "Y" : "-";
            string turnDirText = waypoint.TurnDirection ?? "-";
            string missedText = waypoint.IsMissedApproach ? "MISS" : "-";
            string fixTypeText = waypoint.FixType ?? "-";
            string altFixText = waypoint.RecommendedFixIdent ?? "-";

            navigationGridView.Rows.Add(
                sectionLabel,
                waypoint.Ident ?? "-",
                waypoint.Region ?? "-",
                distanceText,
                bearingText,
                waypoint.InboundAirway ?? "-",
                waypoint.Type ?? "-",
                fixTypeText,
                legDistText,
                courseText,
                waypoint.Notes ?? "-",
                altitudeText,
                speedText,
                speedTypeText,
                waypoint.ArincDescCode ?? "-",
                missedText,
                trueCourseText,
                vertAngleText,
                timeText,
                turnDirText,
                rnpText,
                thetaText,
                rhoText,
                flyoverText,
                altFixText
            );
        }

        // Restore current cell position (this maintains keyboard focus)
        if (previousRowIndex >= 0 && previousRowIndex < navigationGridView.Rows.Count &&
            previousColumnIndex >= 0 && previousColumnIndex < navigationGridView.Columns.Count)
        {
            // Set CurrentCell - this is the key to maintaining keyboard focus position
            navigationGridView.CurrentCell = navigationGridView.Rows[previousRowIndex].Cells[previousColumnIndex];

            // Ensure the row is visible in the viewport
            navigationGridView.FirstDisplayedScrollingRowIndex = previousRowIndex;

            // Restore keyboard focus to the grid after all UI updates complete
            if (IsHandleCreated)
            {
                BeginInvoke(new Action(() => navigationGridView.Focus()));
            }
            else
            {
                navigationGridView.Focus();
            }
        }
        else if (navigationGridView.Rows.Count > 0)
        {
            // If no previous position or invalid index, set first cell as current
            navigationGridView.CurrentCell = navigationGridView.Rows[0].Cells[0];
            if (IsHandleCreated)
            {
                BeginInvoke(new Action(() => navigationGridView.Focus()));
            }
            else
            {
                navigationGridView.Focus();
            }
        }

        UpdateStatus($"Flight plan: {waypoints.Count} waypoints");
    }

    private string GetSectionLabel(FlightPlanSection section)
    {
        switch (section)
        {
            case FlightPlanSection.DepartureAirport: return "A";
            case FlightPlanSection.SID: return "B";
            case FlightPlanSection.Enroute: return "C";
            case FlightPlanSection.STAR: return "D";
            case FlightPlanSection.Approach: return "E";
            case FlightPlanSection.ArrivalAirport: return "F";
            default: return "-";
        }
    }

    // Departure tab handlers
    private void DepartureIcaoTextBox_TextChanged(object? sender, EventArgs e)
    {
        string icao = departureIcaoTextBox.Text.Trim();
        if (icao.Length == 4)
        {
            LoadRunwaysForDeparture(icao);
        }
        else
        {
            departureRunwaysListBox.Items.Clear();
        }
    }

    private void LoadRunwaysForDeparture(string icao)
    {
        try
        {
            var runways = _flightPlanManager.GetRunways(icao);
            departureRunwaysListBox.Items.Clear();

            foreach (var runway in runways)
            {
                departureRunwaysListBox.Items.Add(runway);
            }

            if (runways.Count > 0)
            {
                departureRunwaysListBox.SelectedIndex = 0;
            }
        }
        catch
        {
            departureRunwaysListBox.Items.Clear();
        }
    }

    private void LoadDepartureButton_Click(object? sender, EventArgs e)
    {
        try
        {
            string icao = departureIcaoTextBox.Text.Trim();
            var selectedRunway = departureRunwaysListBox.SelectedItem as Runway;

            if (string.IsNullOrEmpty(icao) || selectedRunway == null)
            {
                MessageBox.Show("Please select an airport and runway", "Invalid Selection",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _flightPlanManager.LoadDeparture(icao, selectedRunway.RunwayID);
            _announcer.Announce($"Loaded departure {icao} runway {selectedRunway.RunwayID}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading departure: {ex.Message}", "Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // SID tab handlers
    private void SIDIcaoTextBox_TextChanged(object? sender, EventArgs e)
    {
        string icao = sidIcaoTextBox.Text.Trim();
        if (icao.Length == 4)
        {
            LoadSIDRunways(icao);
        }
        else
        {
            sidRunwaysListBox.Items.Clear();
            sidsListBox.Items.Clear();
            sidTransitionsListBox.Items.Clear();
        }
    }

    private void LoadSIDRunways(string icao)
    {
        try
        {
            var runways = _flightPlanManager.GetRunwaysForSIDs(icao);
            sidRunwaysListBox.Items.Clear();
            sidsListBox.Items.Clear();
            sidTransitionsListBox.Items.Clear();

            foreach (var runway in runways)
            {
                sidRunwaysListBox.Items.Add(runway);
            }

            if (runways.Count > 0)
            {
                sidRunwaysListBox.SelectedIndex = 0;
            }
        }
        catch
        {
            sidRunwaysListBox.Items.Clear();
            sidsListBox.Items.Clear();
            sidTransitionsListBox.Items.Clear();
        }
    }

    private void SIDRunwaysListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var selectedRunway = sidRunwaysListBox.SelectedItem as string;
        if (!string.IsNullOrEmpty(selectedRunway))
        {
            LoadSIDsForRunway(sidIcaoTextBox.Text.Trim(), selectedRunway);
        }
    }

    private void LoadSIDsForRunway(string icao, string runwayName)
    {
        try
        {
            var sids = _flightPlanManager.GetSIDsForRunway(icao, runwayName);
            sidsListBox.Items.Clear();
            sidTransitionsListBox.Items.Clear();

            foreach (var sid in sids)
            {
                sidsListBox.Items.Add(new SIDItem { Name = sid.sidName ?? "", FixIdent = sid.fixIdent ?? "", ApproachId = sid.approachId });
            }

            if (sids.Count > 0)
            {
                sidsListBox.SelectedIndex = 0;
            }
        }
        catch
        {
            sidsListBox.Items.Clear();
            sidTransitionsListBox.Items.Clear();
        }
    }

    private void SIDsListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var selected = sidsListBox.SelectedItem as SIDItem;
        if (selected != null)
        {
            LoadSIDTransitions(selected.ApproachId);
        }
    }

    private void LoadSIDTransitions(int approachId)
    {
        try
        {
            var transitions = _flightPlanManager.GetTransitions(approachId);
            sidTransitionsListBox.Items.Clear();

            foreach (var transition in transitions)
            {
                sidTransitionsListBox.Items.Add(new TransitionItem { Name = transition.name, Id = transition.id });
            }
        }
        catch
        {
            sidTransitionsListBox.Items.Clear();
        }
    }

    private void LoadSIDButton_Click(object? sender, EventArgs e)
    {
        try
        {
            var selectedRunway = sidRunwaysListBox.SelectedItem as string;
            var selectedSID = sidsListBox.SelectedItem as SIDItem;
            var selectedTransition = sidTransitionsListBox.SelectedItem as TransitionItem;

            if (string.IsNullOrEmpty(selectedRunway) || selectedSID == null)
            {
                MessageBox.Show("Please select a runway and SID", "Invalid Selection",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string sidName = $"{selectedSID.Name} RWY {selectedRunway}";
            _flightPlanManager.LoadSID(selectedSID.ApproachId, selectedTransition?.Id, sidName);
            _announcer.Announce($"Loaded {sidName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading SID: {ex.Message}", "Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // STAR tab handlers
    private void STARIcaoTextBox_TextChanged(object? sender, EventArgs e)
    {
        string icao = starIcaoTextBox.Text.Trim();
        if (icao.Length == 4)
        {
            LoadSTARRunways(icao);
        }
        else
        {
            starRunwaysListBox.Items.Clear();
            starsListBox.Items.Clear();
            starTransitionsListBox.Items.Clear();
        }
    }

    private void LoadSTARRunways(string icao)
    {
        try
        {
            var runways = _flightPlanManager.GetRunwaysForSTARs(icao);
            starRunwaysListBox.Items.Clear();
            starsListBox.Items.Clear();
            starTransitionsListBox.Items.Clear();

            foreach (var runway in runways)
            {
                starRunwaysListBox.Items.Add(runway);
            }

            if (runways.Count > 0)
            {
                starRunwaysListBox.SelectedIndex = 0;
            }
        }
        catch
        {
            starRunwaysListBox.Items.Clear();
            starsListBox.Items.Clear();
            starTransitionsListBox.Items.Clear();
        }
    }

    private void STARRunwaysListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var selectedRunway = starRunwaysListBox.SelectedItem as string;
        if (!string.IsNullOrEmpty(selectedRunway))
        {
            LoadSTARsForRunway(starIcaoTextBox.Text.Trim(), selectedRunway);
        }
    }

    private void LoadSTARsForRunway(string icao, string runwayName)
    {
        try
        {
            var stars = _flightPlanManager.GetSTARsForRunway(icao, runwayName);
            starsListBox.Items.Clear();
            starTransitionsListBox.Items.Clear();

            foreach (var star in stars)
            {
                starsListBox.Items.Add(new STARItem { Name = star.starName ?? "", FixIdent = star.fixIdent ?? "", ApproachId = star.approachId });
            }

            if (stars.Count > 0)
            {
                starsListBox.SelectedIndex = 0;
            }
        }
        catch
        {
            starsListBox.Items.Clear();
            starTransitionsListBox.Items.Clear();
        }
    }

    private void STARsListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var selected = starsListBox.SelectedItem as STARItem;
        if (selected != null)
        {
            LoadSTARTransitions(selected.ApproachId);
        }
    }

    private void LoadSTARTransitions(int approachId)
    {
        try
        {
            var transitions = _flightPlanManager.GetTransitions(approachId);
            starTransitionsListBox.Items.Clear();

            foreach (var transition in transitions)
            {
                starTransitionsListBox.Items.Add(new TransitionItem { Name = transition.name, Id = transition.id });
            }
        }
        catch
        {
            starTransitionsListBox.Items.Clear();
        }
    }

    private void LoadSTARButton_Click(object? sender, EventArgs e)
    {
        try
        {
            var selectedRunway = starRunwaysListBox.SelectedItem as string;
            var selectedSTAR = starsListBox.SelectedItem as STARItem;
            var selectedTransition = starTransitionsListBox.SelectedItem as TransitionItem;

            if (string.IsNullOrEmpty(selectedRunway) || selectedSTAR == null)
            {
                MessageBox.Show("Please select a runway and STAR", "Invalid Selection",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string starName = $"{selectedSTAR.Name} RWY {selectedRunway}";
            _flightPlanManager.LoadSTAR(selectedSTAR.ApproachId, selectedTransition?.Id, starName);
            _announcer.Announce($"Loaded {starName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading STAR: {ex.Message}", "Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Arrival tab handlers
    private void ArrivalIcaoTextBox_TextChanged(object? sender, EventArgs e)
    {
        string icao = arrivalIcaoTextBox.Text.Trim();
        if (icao.Length == 4)
        {
            LoadRunwaysForArrival(icao);
        }
        else
        {
            arrivalRunwaysListBox.Items.Clear();
        }
    }

    private void LoadRunwaysForArrival(string icao)
    {
        try
        {
            var runways = _flightPlanManager.GetRunways(icao);
            arrivalRunwaysListBox.Items.Clear();

            foreach (var runway in runways)
            {
                arrivalRunwaysListBox.Items.Add(runway);
            }

            if (runways.Count > 0)
            {
                arrivalRunwaysListBox.SelectedIndex = 0;
            }
        }
        catch
        {
            arrivalRunwaysListBox.Items.Clear();
        }
    }

    private void LoadArrivalButton_Click(object? sender, EventArgs e)
    {
        try
        {
            string icao = arrivalIcaoTextBox.Text.Trim();
            var selectedRunway = arrivalRunwaysListBox.SelectedItem as Runway;

            if (string.IsNullOrEmpty(icao) || selectedRunway == null)
            {
                MessageBox.Show("Please select an airport and runway", "Invalid Selection",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _flightPlanManager.LoadArrival(icao, selectedRunway.RunwayID);
            _announcer.Announce($"Loaded arrival {icao} runway {selectedRunway.RunwayID}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading arrival: {ex.Message}", "Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Approach tab handlers
    private void ApproachIcaoTextBox_TextChanged(object? sender, EventArgs e)
    {
        string icao = approachIcaoTextBox.Text.Trim();
        if (icao.Length == 4)
        {
            LoadApproaches(icao);
        }
        else
        {
            approachesListBox.Items.Clear();
            transitionsListBox.Items.Clear();
        }
    }

    private void LoadApproaches(string icao)
    {
        try
        {
            var approaches = _flightPlanManager.GetApproaches(icao);
            approachesListBox.Items.Clear();
            transitionsListBox.Items.Clear();

            foreach (var approach in approaches)
            {
                approachesListBox.Items.Add(new ApproachItem { Name = approach.name, Id = approach.id });
            }

            if (approaches.Count > 0)
            {
                approachesListBox.SelectedIndex = 0;
            }
        }
        catch
        {
            approachesListBox.Items.Clear();
            transitionsListBox.Items.Clear();
        }
    }

    private void ApproachesListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var selected = approachesListBox.SelectedItem as ApproachItem;
        if (selected != null)
        {
            LoadTransitions(selected.Id);
        }
    }

    private void LoadTransitions(int approachId)
    {
        try
        {
            var transitions = _flightPlanManager.GetTransitions(approachId);
            transitionsListBox.Items.Clear();

            foreach (var transition in transitions)
            {
                transitionsListBox.Items.Add(new TransitionItem { Name = transition.name, Id = transition.id });
            }
        }
        catch
        {
            transitionsListBox.Items.Clear();
        }
    }

    private void LoadApproachButton_Click(object? sender, EventArgs e)
    {
        try
        {
            var selectedApproach = approachesListBox.SelectedItem as ApproachItem;
            var selectedTransition = transitionsListBox.SelectedItem as TransitionItem;

            if (selectedApproach == null)
            {
                MessageBox.Show("Please select an approach", "Invalid Selection",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _flightPlanManager.LoadApproach(selectedApproach.Id, selectedTransition?.Id, selectedApproach.Name);
            _announcer.Announce($"Loaded approach {selectedApproach.Name}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading approach: {ex.Message}", "Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateStatus(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(UpdateStatus), message);
            return;
        }

        statusLabel.Text = message;
    }

    private SimConnectManager.AircraftPosition? GetLastKnownPosition()
    {
        return _lastKnownPosition;
    }

    // Airport Lookup tab handlers
    private void AirportLookupIcaoTextBox_TextChanged(object? sender, EventArgs e)
    {
        string icao = airportLookupIcaoTextBox.Text.Trim();
        if (icao.Length == 4)
        {
            LoadRunwaysForAirportLookup(icao);
        }
        else
        {
            airportLookupRunwaysListBox.Items.Clear();
            airportInfoTextBox.Clear();
            runwayInfoTextBox.Clear();
        }
    }

    private void LoadRunwaysForAirportLookup(string icao)
    {
        try
        {
            var runways = _flightPlanManager.GetRunways(icao);
            airportLookupRunwaysListBox.Items.Clear();

            foreach (var runway in runways)
            {
                airportLookupRunwaysListBox.Items.Add(runway);
            }

            if (runways.Count > 0)
            {
                airportLookupRunwaysListBox.SelectedIndex = 0;
            }
        }
        catch
        {
            airportLookupRunwaysListBox.Items.Clear();
        }
    }

    private void AirportLookupLoadButton_Click(object? sender, EventArgs e)
    {
        try
        {
            string icao = airportLookupIcaoTextBox.Text.Trim();
            var selectedRunway = airportLookupRunwaysListBox.SelectedItem as Runway;

            if (string.IsNullOrEmpty(icao))
            {
                MessageBox.Show("Please enter an airport ICAO code", "Invalid Selection",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Load airport information
            airportInfoTextBox.Text = GetAirportDetailedInfo(icao);

            // Load runway information if a runway is selected
            if (selectedRunway != null)
            {
                runwayInfoTextBox.Text = GetRunwayDetailedInfo(icao, selectedRunway.RunwayID);
            }
            else
            {
                runwayInfoTextBox.Text = "No runway selected";
            }

            UpdateStatus($"Loaded information for {icao}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading airport information: {ex.Message}", "Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string GetAirportDetailedInfo(string icao)
    {
        var settings = SettingsManager.Current;
        string simulatorVersion = settings.SimulatorVersion ?? "FS2020";
        string dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FBWBA", "databases", $"{simulatorVersion.ToLower()}.sqlite");

        if (!File.Exists(dbPath))
        {
            return $"Database not found: {dbPath}";
        }

        var sb = new StringBuilder();
        string connectionString = $"Data Source={dbPath};Mode=ReadOnly;";

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();

            var sql = @"SELECT * FROM airport WHERE UPPER(icao) = UPPER(@ICAO) OR UPPER(ident) = UPPER(@ICAO) LIMIT 1";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@ICAO", icao);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        sb.AppendLine("═══════════════════════════════════════════════════");
                        sb.AppendLine($"  AIRPORT INFORMATION - {reader["ident"]?.ToString() ?? icao}");
                        sb.AppendLine("═══════════════════════════════════════════════════");
                        sb.AppendLine();

                        // Basic Information
                        sb.AppendLine("BASIC INFORMATION:");
                        sb.AppendLine($"  ICAO:         {reader["icao"]?.ToString() ?? "N/A"}");
                        sb.AppendLine($"  Ident:        {reader["ident"]?.ToString() ?? "N/A"}");
                        sb.AppendLine($"  Name:         {reader["name"]?.ToString() ?? "N/A"}");
                        sb.AppendLine($"  IATA:         {reader["iata"]?.ToString() ?? "N/A"}");
                        sb.AppendLine($"  FAA:          {reader["faa"]?.ToString() ?? "N/A"}");
                        sb.AppendLine($"  Local:        {reader["local"]?.ToString() ?? "N/A"}");
                        sb.AppendLine();

                        // Location
                        sb.AppendLine("LOCATION:");
                        sb.AppendLine($"  City:         {reader["city"]?.ToString() ?? "N/A"}");
                        sb.AppendLine($"  State:        {reader["state"]?.ToString() ?? "N/A"}");
                        sb.AppendLine($"  Country:      {reader["country"]?.ToString() ?? "N/A"}");
                        sb.AppendLine($"  Region:       {reader["region"]?.ToString() ?? "N/A"}");
                        sb.AppendLine($"  Latitude:     {reader["laty"]}");
                        sb.AppendLine($"  Longitude:    {reader["lonx"]}");
                        sb.AppendLine($"  Altitude:     {reader["altitude"]} ft");
                        sb.AppendLine($"  Mag Var:      {reader["mag_var"]}°");
                        sb.AppendLine();

                        // Frequencies
                        sb.AppendLine("FREQUENCIES:");
                        var towerFreq = reader["tower_frequency"];
                        sb.AppendLine($"  Tower:        {(towerFreq != DBNull.Value && Convert.ToInt32(towerFreq) > 0 ? $"{Convert.ToDouble(towerFreq) / 1000000.0:F3} MHz" : "N/A")}");
                        var atisFreq = reader["atis_frequency"];
                        sb.AppendLine($"  ATIS:         {(atisFreq != DBNull.Value && Convert.ToInt32(atisFreq) > 0 ? $"{Convert.ToDouble(atisFreq) / 1000000.0:F3} MHz" : "N/A")}");
                        var awosFreq = reader["awos_frequency"];
                        sb.AppendLine($"  AWOS:         {(awosFreq != DBNull.Value && Convert.ToInt32(awosFreq) > 0 ? $"{Convert.ToDouble(awosFreq) / 1000000.0:F3} MHz" : "N/A")}");
                        var asosFreq = reader["asos_frequency"];
                        sb.AppendLine($"  ASOS:         {(asosFreq != DBNull.Value && Convert.ToInt32(asosFreq) > 0 ? $"{Convert.ToDouble(asosFreq) / 1000000.0:F3} MHz" : "N/A")}");
                        var unicomFreq = reader["unicom_frequency"];
                        sb.AppendLine($"  UNICOM:       {(unicomFreq != DBNull.Value && Convert.ToInt32(unicomFreq) > 0 ? $"{Convert.ToDouble(unicomFreq) / 1000000.0:F3} MHz" : "N/A")}");
                        sb.AppendLine();

                        // Status
                        sb.AppendLine("STATUS:");
                        sb.AppendLine($"  Closed:       {(Convert.ToInt32(reader["is_closed"]) == 1 ? "Yes" : "No")}");
                        sb.AppendLine($"  Military:     {(Convert.ToInt32(reader["is_military"]) == 1 ? "Yes" : "No")}");
                        sb.AppendLine($"  Addon:        {(Convert.ToInt32(reader["is_addon"]) == 1 ? "Yes" : "No")}");
                        sb.AppendLine($"  3D:           {(Convert.ToInt32(reader["is_3d"]) == 1 ? "Yes" : "No")}");
                        sb.AppendLine($"  Rating:       {reader["rating"]}/5");
                        sb.AppendLine();

                        // Fuel
                        sb.AppendLine("FUEL:");
                        sb.AppendLine($"  Avgas:        {(Convert.ToInt32(reader["has_avgas"]) == 1 ? "Available" : "Not Available")}");
                        sb.AppendLine($"  Jet Fuel:     {(Convert.ToInt32(reader["has_jetfuel"]) == 1 ? "Available" : "Not Available")}");
                        sb.AppendLine();

                        // Facilities Count
                        sb.AppendLine("FACILITIES:");
                        sb.AppendLine($"  COM Frequencies:        {reader["num_com"]}");
                        sb.AppendLine($"  Parking Gates:          {reader["num_parking_gate"]}");
                        sb.AppendLine($"  Parking GA Ramps:       {reader["num_parking_ga_ramp"]}");
                        sb.AppendLine($"  Parking Cargo:          {reader["num_parking_cargo"]}");
                        sb.AppendLine($"  Parking Mil Cargo:      {reader["num_parking_mil_cargo"]}");
                        sb.AppendLine($"  Parking Mil Combat:     {reader["num_parking_mil_combat"]}");
                        sb.AppendLine($"  Approaches:             {reader["num_approach"]}");
                        sb.AppendLine($"  Runways (Hard):         {reader["num_runway_hard"]}");
                        sb.AppendLine($"  Runways (Soft):         {reader["num_runway_soft"]}");
                        sb.AppendLine($"  Runways (Water):        {reader["num_runway_water"]}");
                        sb.AppendLine($"  Lighted Runways:        {reader["num_runway_light"]}");
                        sb.AppendLine($"  ILS Equipped Ends:      {reader["num_runway_end_ils"] ?? "N/A"}");
                        sb.AppendLine($"  Closed Runway Ends:     {reader["num_runway_end_closed"]}");
                        sb.AppendLine($"  VASI Equipped Ends:     {reader["num_runway_end_vasi"]}");
                        sb.AppendLine($"  ALS Equipped Ends:      {reader["num_runway_end_als"]}");
                        sb.AppendLine($"  Helipads:               {reader["num_helipad"]}");
                        sb.AppendLine($"  Jetways:                {reader["num_jetway"]}");
                        sb.AppendLine($"  Taxi Paths:             {reader["num_taxi_path"]}");
                        sb.AppendLine($"  Aprons:                 {reader["num_apron"]}");
                        sb.AppendLine($"  Start Positions:        {reader["num_starts"]}");
                        sb.AppendLine();

                        // Runway Info
                        sb.AppendLine("RUNWAY SUMMARY:");
                        sb.AppendLine($"  Total Runways:          {reader["num_runways"]}");
                        sb.AppendLine($"  Longest Runway Length:  {reader["longest_runway_length"]} ft");
                        sb.AppendLine($"  Longest Runway Width:   {reader["longest_runway_width"]} ft");
                        sb.AppendLine($"  Longest Runway Heading: {reader["longest_runway_heading"]}°");
                        sb.AppendLine($"  Longest Runway Surface: {reader["longest_runway_surface"]?.ToString() ?? "N/A"}");
                        sb.AppendLine();

                        // Parking Info
                        sb.AppendLine("PARKING:");
                        sb.AppendLine($"  Largest Ramp:           {reader["largest_parking_ramp"]?.ToString() ?? "N/A"}");
                        sb.AppendLine($"  Largest Gate:           {reader["largest_parking_gate"]?.ToString() ?? "N/A"}");
                        sb.AppendLine();

                        // Tower
                        var towerAlt = reader["tower_altitude"];
                        var towerLonx = reader["tower_lonx"];
                        var towerLaty = reader["tower_laty"];
                        if (towerAlt != DBNull.Value || towerLonx != DBNull.Value || towerLaty != DBNull.Value)
                        {
                            sb.AppendLine("TOWER:");
                            sb.AppendLine($"  Altitude:     {(towerAlt != DBNull.Value ? $"{towerAlt} ft" : "N/A")}");
                            sb.AppendLine($"  Longitude:    {(towerLonx != DBNull.Value ? towerLonx.ToString() : "N/A")}");
                            sb.AppendLine($"  Latitude:     {(towerLaty != DBNull.Value ? towerLaty.ToString() : "N/A")}");
                            sb.AppendLine($"  Has Tower Object: {(Convert.ToInt32(reader["has_tower_object"]) == 1 ? "Yes" : "No")}");
                            sb.AppendLine();
                        }

                        // Transitions
                        var transAlt = reader["transition_altitude"];
                        var transLevel = reader["transition_level"];
                        if (transAlt != DBNull.Value || transLevel != DBNull.Value)
                        {
                            sb.AppendLine("TRANSITIONS:");
                            sb.AppendLine($"  Transition Altitude:    {(transAlt != DBNull.Value ? $"{transAlt} ft" : "N/A")}");
                            sb.AppendLine($"  Transition Level:       {(transLevel != DBNull.Value ? $"FL{transLevel}" : "N/A")}");
                            sb.AppendLine();
                        }

                        // Scenery
                        sb.AppendLine("SCENERY:");
                        sb.AppendLine($"  Local Path:   {reader["scenery_local_path"]?.ToString() ?? "N/A"}");
                        sb.AppendLine($"  BGL File:     {reader["bgl_filename"]?.ToString() ?? "N/A"}");
                    }
                    else
                    {
                        sb.AppendLine($"Airport {icao} not found in database.");
                    }
                }
            }
        }

        return sb.ToString();
    }

    private string GetRunwayDetailedInfo(string icao, string runwayId)
    {
        var settings = SettingsManager.Current;
        string simulatorVersion = settings.SimulatorVersion ?? "FS2020";
        string dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FBWBA", "databases", $"{simulatorVersion.ToLower()}.sqlite");

        if (!File.Exists(dbPath))
        {
            return $"Database not found: {dbPath}";
        }

        var sb = new StringBuilder();
        string connectionString = $"Data Source={dbPath};Mode=ReadOnly;";

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();

            // Get airport_id first
            int airportId = -1;
            var airportSql = "SELECT airport_id FROM airport WHERE UPPER(icao) = UPPER(@ICAO) OR UPPER(ident) = UPPER(@ICAO) LIMIT 1";
            using (var cmd = new SqliteCommand(airportSql, connection))
            {
                cmd.Parameters.AddWithValue("@ICAO", icao);
                var result = cmd.ExecuteScalar();
                if (result != null) airportId = Convert.ToInt32(result);
            }

            if (airportId == -1)
            {
                return $"Airport {icao} not found.";
            }

            // Query runway with runway_end join
            var sql = @"
                SELECT r.*, re.*, a.mag_var
                FROM runway r
                JOIN runway_end re ON re.runway_end_id = r.primary_end_id OR re.runway_end_id = r.secondary_end_id
                JOIN airport a ON r.airport_id = a.airport_id
                WHERE r.airport_id = @AirportId AND UPPER(re.name) = UPPER(@RunwayId)
                LIMIT 1";

            using (var command = new SqliteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@AirportId", airportId);
                command.Parameters.AddWithValue("@RunwayId", runwayId);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        sb.AppendLine("═══════════════════════════════════════════════════");
                        sb.AppendLine($"  RUNWAY INFORMATION - {runwayId}");
                        sb.AppendLine("═══════════════════════════════════════════════════");
                        sb.AppendLine();

                        sb.AppendLine($"Runway ID:        {reader["name"]?.ToString() ?? runwayId}");
                        sb.AppendLine();

                        // ILS INFORMATION (placed at top as requested)
                        var ilsIdent = reader["ils_ident"]?.ToString();
                        if (!string.IsNullOrEmpty(ilsIdent))
                        {
                            sb.AppendLine("ILS INFORMATION:");
                            sb.AppendLine($"  ILS Ident:    {ilsIdent}");

                            // Query ILS table for detailed information
                            var ilsSql = "SELECT * FROM ils WHERE ident = @IlsIdent LIMIT 1";
                            using (var ilsCmd = new SqliteCommand(ilsSql, connection))
                            {
                                ilsCmd.Parameters.AddWithValue("@IlsIdent", ilsIdent);
                                using (var ilsReader = ilsCmd.ExecuteReader())
                                {
                                    if (ilsReader.Read())
                                    {
                                        var freq = ilsReader["frequency"];
                                        sb.AppendLine($"  ILS Frequency:        {(freq != DBNull.Value ? $"{Convert.ToDouble(freq) / 1000.0:F2} MHz" : "N/A")}");
                                        sb.AppendLine($"  ILS Name:             {ilsReader["name"]?.ToString() ?? "N/A"}");
                                        sb.AppendLine($"  ILS Region:           {ilsReader["region"]?.ToString() ?? "N/A"}");
                                        sb.AppendLine($"  ILS Type:             {ilsReader["type"]?.ToString() ?? "N/A"}");
                                        sb.AppendLine($"  Loc Heading:          {ilsReader["loc_heading"]}°");
                                        sb.AppendLine($"  Loc Width:            {ilsReader["loc_width"]}°");
                                        sb.AppendLine($"  Range:                {ilsReader["range"]} NM");
                                        sb.AppendLine($"  Has Backcourse:       {(Convert.ToInt32(ilsReader["has_backcourse"] ?? 0) == 1 ? "Yes" : "No")}");
                                        sb.AppendLine($"  Performance Indicator:{ilsReader["perf_indicator"]?.ToString() ?? "N/A"}");
                                        sb.AppendLine($"  Provider:             {ilsReader["provider"]?.ToString() ?? "N/A"}");
                                        sb.AppendLine($"  Mag Var:              {ilsReader["mag_var"]}°");

                                        // DME Information
                                        var dmeRange = ilsReader["dme_range"];
                                        if (dmeRange != DBNull.Value && Convert.ToInt32(dmeRange) > 0)
                                        {
                                            sb.AppendLine($"  DME Range:            {dmeRange} NM");
                                            sb.AppendLine($"  DME Altitude:         {ilsReader["dme_altitude"]} ft");
                                            sb.AppendLine($"  DME Longitude:        {ilsReader["dme_lonx"]}");
                                            sb.AppendLine($"  DME Latitude:         {ilsReader["dme_laty"]}");
                                        }

                                        // Glideslope Information
                                        var gsRange = ilsReader["gs_range"];
                                        if (gsRange != DBNull.Value && Convert.ToInt32(gsRange) > 0)
                                        {
                                            sb.AppendLine($"  GS Range:             {gsRange} NM");
                                            sb.AppendLine($"  GS Pitch:             {ilsReader["gs_pitch"]}°");
                                            sb.AppendLine($"  GS Altitude:          {ilsReader["gs_altitude"]} ft");
                                            sb.AppendLine($"  GS Longitude:         {ilsReader["gs_lonx"]}");
                                            sb.AppendLine($"  GS Latitude:          {ilsReader["gs_laty"]}");
                                        }

                                        // Localizer coordinates
                                        sb.AppendLine($"  Localizer Altitude:   {ilsReader["altitude"]} ft");
                                        sb.AppendLine($"  Localizer Longitude:  {ilsReader["lonx"]}");
                                        sb.AppendLine($"  Localizer Latitude:   {ilsReader["laty"]}");
                                    }
                                }
                            }
                            sb.AppendLine();
                        }
                        else
                        {
                            sb.AppendLine("ILS INFORMATION:");
                            sb.AppendLine("  No ILS available");
                            sb.AppendLine();
                        }

                        // DIMENSIONS
                        sb.AppendLine("DIMENSIONS:");
                        sb.AppendLine($"  Length:               {reader["length"]} ft");
                        sb.AppendLine($"  Width:                {reader["width"]} ft");
                        sb.AppendLine();

                        // SURFACE
                        sb.AppendLine("SURFACE:");
                        sb.AppendLine($"  Surface:              {reader["surface"]?.ToString() ?? "N/A"}");
                        var smoothness = reader["smoothness"];
                        sb.AppendLine($"  Smoothness:           {(smoothness != DBNull.Value ? smoothness.ToString() : "N/A")}");
                        sb.AppendLine($"  Shoulder:             {reader["shoulder"]?.ToString() ?? "N/A"}");
                        sb.AppendLine();

                        // HEADINGS
                        var magVar = Convert.ToDouble(reader["mag_var"] ?? 0.0);
                        var heading = Convert.ToDouble(reader["heading"] ?? 0.0);
                        sb.AppendLine("HEADINGS:");
                        sb.AppendLine($"  True Heading:         {heading:F1}°");
                        sb.AppendLine($"  Magnetic Heading:     {(heading - magVar):F1}°");
                        sb.AppendLine();

                        // COORDINATES
                        sb.AppendLine("COORDINATES:");
                        sb.AppendLine($"  Longitude:            {reader["lonx"]}");
                        sb.AppendLine($"  Latitude:             {reader["laty"]}");
                        sb.AppendLine();

                        // THRESHOLD DATA
                        sb.AppendLine("THRESHOLD DATA:");
                        sb.AppendLine($"  Offset Threshold:     {reader["offset_threshold"]} ft");
                        sb.AppendLine($"  Blast Pad:            {reader["blast_pad"]} ft");
                        sb.AppendLine($"  Overrun:              {reader["overrun"]} ft");
                        sb.AppendLine();

                        // PATTERN ALTITUDE
                        sb.AppendLine("PATTERN:");
                        sb.AppendLine($"  Pattern Altitude:     {reader["pattern_altitude"]} ft");
                        sb.AppendLine($"  Altitude (MSL):       {reader["altitude"]} ft");
                        sb.AppendLine();

                        // LIGHTING
                        sb.AppendLine("LIGHTING:");
                        sb.AppendLine($"  Edge Light:           {reader["edge_light"]?.ToString() ?? "N/A"}");
                        sb.AppendLine($"  Center Light:         {reader["center_light"]?.ToString() ?? "N/A"}");
                        sb.AppendLine($"  Has Center Red:       {(Convert.ToInt32(reader["has_center_red"] ?? 0) == 1 ? "Yes" : "No")}");
                        sb.AppendLine($"  Approach Light System:{reader["app_light_system_type"]?.ToString() ?? "N/A"}");
                        sb.AppendLine($"  Has End Lights:       {(Convert.ToInt32(reader["has_end_lights"] ?? 0) == 1 ? "Yes" : "No")}");
                        sb.AppendLine($"  Has REILS:            {(Convert.ToInt32(reader["has_reils"] ?? 0) == 1 ? "Yes" : "No")}");
                        sb.AppendLine($"  Has Touchdown Lights: {(Convert.ToInt32(reader["has_touchdown_lights"] ?? 0) == 1 ? "Yes" : "No")}");
                        var strobes = reader["num_strobes"];
                        sb.AppendLine($"  Number of Strobes:    {(strobes != DBNull.Value ? strobes.ToString() : "N/A")}");
                        sb.AppendLine();

                        // MARKINGS
                        sb.AppendLine("MARKINGS:");
                        sb.AppendLine($"  Marking Flags:        {reader["marking_flags"]}");
                        sb.AppendLine($"  Has Closed Markings:  {(Convert.ToInt32(reader["has_closed_markings"] ?? 0) == 1 ? "Yes" : "No")}");
                        sb.AppendLine($"  Has STOL Markings:    {(Convert.ToInt32(reader["has_stol_markings"] ?? 0) == 1 ? "Yes" : "No")}");
                        sb.AppendLine();

                        // OPERATIONS
                        sb.AppendLine("OPERATIONS:");
                        sb.AppendLine($"  Is Takeoff:           {(Convert.ToInt32(reader["is_takeoff"] ?? 0) == 1 ? "Yes" : "No")}");
                        sb.AppendLine($"  Is Landing:           {(Convert.ToInt32(reader["is_landing"] ?? 0) == 1 ? "Yes" : "No")}");
                        sb.AppendLine($"  Is Pattern:           {reader["is_pattern"]?.ToString() ?? "N/A"}");
                        sb.AppendLine($"  End Type:             {reader["end_type"]?.ToString() ?? "N/A"}");
                        sb.AppendLine();

                        // VASI
                        sb.AppendLine("VASI:");
                        var leftVasiType = reader["left_vasi_type"]?.ToString();
                        var rightVasiType = reader["right_vasi_type"]?.ToString();
                        if (!string.IsNullOrEmpty(leftVasiType))
                        {
                            sb.AppendLine($"  Left VASI Type:       {leftVasiType}");
                            var leftPitch = reader["left_vasi_pitch"];
                            sb.AppendLine($"  Left VASI Pitch:      {(leftPitch != DBNull.Value ? $"{leftPitch}°" : "N/A")}");
                        }
                        else
                        {
                            sb.AppendLine("  Left VASI Type:       None");
                        }

                        if (!string.IsNullOrEmpty(rightVasiType))
                        {
                            sb.AppendLine($"  Right VASI Type:      {rightVasiType}");
                            var rightPitch = reader["right_vasi_pitch"];
                            sb.AppendLine($"  Right VASI Pitch:     {(rightPitch != DBNull.Value ? $"{rightPitch}°" : "N/A")}");
                        }
                        else
                        {
                            sb.AppendLine("  Right VASI Type:      None");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"Runway {runwayId} not found at airport {icao}.");
                    }
                }
            }
        }

        return sb.ToString();
    }

    // Helper classes for ListBox items
    private class SIDItem
    {
        public string Name { get; set; } = "";
        public string FixIdent { get; set; } = "";
        public int ApproachId { get; set; }
        public override string ToString() => Name;
    }

    private class STARItem
    {
        public string Name { get; set; } = "";
        public string FixIdent { get; set; } = "";
        public int ApproachId { get; set; }
        public override string ToString() => Name;
    }

    private class ApproachItem
    {
        public string Name { get; set; } = "";
        public int Id { get; set; }
        public override string ToString() => Name;
    }

    private class TransitionItem
    {
        public string Name { get; set; } = "";
        public int Id { get; set; }
        public override string ToString() => Name;
    }
}
