using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FBWBA.Accessibility;
using FBWBA.Database;
using FBWBA.Database.Models;
using FBWBA.Navigation;
using FBWBA.SimConnect;
using FBWBA.Settings;

namespace FBWBA.Forms
{
    /// <summary>
    /// Electronic Flight Bag - Main flight planning and navigation window
    /// </summary>
    public partial class ElectronicFlightBagForm : Form
    {
        private readonly FlightPlanManager _flightPlanManager;
        private readonly SimConnectManager _simConnectManager;
        private readonly ScreenReaderAnnouncer _announcer;
        private readonly string _simbriefUsername;
        private SimConnectManager.AircraftPosition? _lastKnownPosition;

        // State persistence
        private int _savedNavigationRowIndex = -1;
        private int _savedNavigationColumnIndex = 0;

        // Navigation tab controls
        private TabControl mainTabControl;
        private DataGridView navigationGridView;
        private Label statusLabel;
        private Button loadSimbriefButton;
        private Button refreshPositionButton;

        // Other tab controls
        private TextBox departureIcaoTextBox;
        private ListBox departureRunwaysListBox;
        private Button loadDepartureButton;

        private TextBox sidIcaoTextBox;
        private ListBox sidsListBox;
        private ListBox sidRunwaysListBox;
        private ListBox sidTransitionsListBox;
        private Button loadSIDButton;

        private TextBox starIcaoTextBox;
        private ListBox starsListBox;
        private ListBox starRunwaysListBox;
        private ListBox starTransitionsListBox;
        private Button loadSTARButton;

        private TextBox arrivalIcaoTextBox;
        private ListBox arrivalRunwaysListBox;
        private Button loadArrivalButton;

        private TextBox approachIcaoTextBox;
        private ListBox approachesListBox;
        private ListBox transitionsListBox;
        private Button loadApproachButton;

        public ElectronicFlightBagForm(FlightPlanManager flightPlanManager, SimConnectManager simConnectManager,
                                       ScreenReaderAnnouncer announcer, string simbriefUsername)
        {
            _flightPlanManager = flightPlanManager;
            _simConnectManager = simConnectManager;
            _announcer = announcer;
            _simbriefUsername = simbriefUsername;

            InitializeComponent();
            SetupEventHandlers();
            SetupAccessibility();

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

        private void OnAircraftPositionReceived(object sender, SimConnectManager.AircraftPosition position)
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

            // Load saved state when form is shown
            Load += ElectronicFlightBagForm_Load;

            // Save state when form is closing
            FormClosing += ElectronicFlightBagForm_FormClosing;
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

            // Request current aircraft position
            _simConnectManager.RequestAircraftPosition();

            // The position will be received asynchronously, but we can use SimConnect's magnetic variation
            // For now, use a default variation of 0 if we don't have current position
            var lastPosition = GetLastKnownPosition();
            if (lastPosition.HasValue)
            {
                _flightPlanManager.UpdateAircraftPosition(
                    lastPosition.Value.Latitude,
                    lastPosition.Value.Longitude,
                    lastPosition.Value.MagneticVariation);

                _announcer.Announce("Position updated");
                UpdateStatus("Aircraft position updated");
            }
            else
            {
                _announcer.Announce("Aircraft position not available");
                UpdateStatus("Aircraft position not available");
            }
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
        private void DepartureIcaoTextBox_TextChanged(object sender, EventArgs e)
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

        private void LoadDepartureButton_Click(object sender, EventArgs e)
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
        private void SIDIcaoTextBox_TextChanged(object sender, EventArgs e)
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

        private void SIDRunwaysListBox_SelectedIndexChanged(object sender, EventArgs e)
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
                    sidsListBox.Items.Add(new SIDItem { Name = sid.sidName, FixIdent = sid.fixIdent, ApproachId = sid.approachId });
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

        private void SIDsListBox_SelectedIndexChanged(object sender, EventArgs e)
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

        private void LoadSIDButton_Click(object sender, EventArgs e)
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
        private void STARIcaoTextBox_TextChanged(object sender, EventArgs e)
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

        private void STARRunwaysListBox_SelectedIndexChanged(object sender, EventArgs e)
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
                    starsListBox.Items.Add(new STARItem { Name = star.starName, FixIdent = star.fixIdent, ApproachId = star.approachId });
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

        private void STARsListBox_SelectedIndexChanged(object sender, EventArgs e)
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

        private void LoadSTARButton_Click(object sender, EventArgs e)
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
        private void ArrivalIcaoTextBox_TextChanged(object sender, EventArgs e)
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

        private void LoadArrivalButton_Click(object sender, EventArgs e)
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
        private void ApproachIcaoTextBox_TextChanged(object sender, EventArgs e)
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

        private void ApproachesListBox_SelectedIndexChanged(object sender, EventArgs e)
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

        private void LoadApproachButton_Click(object sender, EventArgs e)
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

        private void ElectronicFlightBagForm_Load(object sender, EventArgs e)
        {
            try
            {
                var settings = SettingsManager.Current;

                // Restore tab index
                if (settings.EFBTabIndex >= 0 && settings.EFBTabIndex < mainTabControl.TabPages.Count)
                {
                    mainTabControl.SelectedIndex = settings.EFBTabIndex;
                }

                // Restore window size
                if (settings.EFBWindowWidth > 0 && settings.EFBWindowHeight > 0)
                {
                    Width = settings.EFBWindowWidth;
                    Height = settings.EFBWindowHeight;
                }

                // Restore window position (if valid)
                if (settings.EFBWindowX >= 0 && settings.EFBWindowY >= 0)
                {
                    // Verify position is within screen bounds
                    var screen = Screen.FromPoint(new Point(settings.EFBWindowX, settings.EFBWindowY));
                    if (screen.WorkingArea.Contains(settings.EFBWindowX, settings.EFBWindowY))
                    {
                        StartPosition = FormStartPosition.Manual;
                        Location = new Point(settings.EFBWindowX, settings.EFBWindowY);
                    }
                }

                // Restore navigation cell position (after flight plan loads)
                _savedNavigationRowIndex = settings.EFBNavigationRowIndex;
                _savedNavigationColumnIndex = settings.EFBNavigationColumnIndex;

                // If we have a saved position and the grid has data, restore it
                if (_savedNavigationRowIndex >= 0 && _savedNavigationRowIndex < navigationGridView.Rows.Count &&
                    _savedNavigationColumnIndex >= 0 && _savedNavigationColumnIndex < navigationGridView.Columns.Count)
                {
                    navigationGridView.CurrentCell = navigationGridView.Rows[_savedNavigationRowIndex].Cells[_savedNavigationColumnIndex];
                    navigationGridView.FirstDisplayedScrollingRowIndex = _savedNavigationRowIndex;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading EFB window state: {ex.Message}");
            }
        }

        private void ElectronicFlightBagForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                var settings = SettingsManager.Current;

                // Save current tab index
                settings.EFBTabIndex = mainTabControl.SelectedIndex;

                // Save currently active navigation cell position
                if (navigationGridView.CurrentCell != null)
                {
                    settings.EFBNavigationRowIndex = navigationGridView.CurrentCell.RowIndex;
                    settings.EFBNavigationColumnIndex = navigationGridView.CurrentCell.ColumnIndex;
                }
                else
                {
                    settings.EFBNavigationRowIndex = -1;
                    settings.EFBNavigationColumnIndex = 0;
                }

                // Save window size
                settings.EFBWindowWidth = Width;
                settings.EFBWindowHeight = Height;

                // Save window position (only if not maximized or minimized)
                if (WindowState == FormWindowState.Normal)
                {
                    settings.EFBWindowX = Location.X;
                    settings.EFBWindowY = Location.Y;
                }

                // Persist settings to disk
                SettingsManager.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving EFB window state: {ex.Message}");
            }
        }

        // Helper classes for ListBox items
        private class SIDItem
        {
            public string Name { get; set; }
            public string FixIdent { get; set; }
            public int ApproachId { get; set; }
            public override string ToString() => Name;
        }

        private class STARItem
        {
            public string Name { get; set; }
            public string FixIdent { get; set; }
            public int ApproachId { get; set; }
            public override string ToString() => Name;
        }

        private class ApproachItem
        {
            public string Name { get; set; }
            public int Id { get; set; }
            public override string ToString() => Name;
        }

        private class TransitionItem
        {
            public string Name { get; set; }
            public int Id { get; set; }
            public override string ToString() => Name;
        }
    }
}
