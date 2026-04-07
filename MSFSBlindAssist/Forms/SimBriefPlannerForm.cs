using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Controls;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// SimBrief Flight Planner - accessible two-tab form.
/// Create tab: prefill and open SimBrief dispatch website.
/// View tab: fetch and browse the latest OFP with sub-tabs for all data sections.
/// </summary>
public class SimBriefPlannerForm : Form
{
    // ── Dependencies ─────────────────────────────────────────────────────────
    private readonly string _simbriefUsername;
    private readonly ScreenReaderAnnouncer? _announcer;
    private readonly SimBriefService _service = new SimBriefService();
    private SimBriefOFP? _ofp;
    private bool _fetchInProgress = false;

    // ── Outer layout ─────────────────────────────────────────────────────────
    private TabControl _outerTabs = null!;

    // ── Create tab controls ───────────────────────────────────────────────────
    private ComboBox  _createAcType      = null!;
    private TextBox   _createOrigin      = null!;
    private TextBox   _createDest        = null!;
    private TextBox   _createAltn        = null!;
    private TextBox   _createCruiseLevel = null!;
    private TextBox   _createAirline     = null!;
    private TextBox   _createFlightNum   = null!;
    private TextBox   _createCi          = null!;
    private TextBox   _createRoute       = null!;
    private TextBox   _createPax         = null!;
    private TextBox   _createFreight     = null!;
    private ComboBox  _createWeightUnits = null!;
    private Button    _planButton        = null!;
    private Label     _createStatus      = null!;

    // ── View tab controls ─────────────────────────────────────────────────────
    private Button    _fetchButton    = null!;
    private Label     _viewStatus     = null!;
    private TabControl _viewTabs      = null!;
    private TreeView  _overviewTree   = null!;
    private ListBox   _overviewDetail = null!;
    private SplitContainer _overviewSplit = null!;
    private TreeView _navLogGrid = null!;
    private ListBox   _fuelText       = null!;
    private ListBox   _weightsText    = null!;
    // Performance tab – three separate panes + expand buttons
    private ListBox   _perfTakeoffText      = null!;
    private ListBox   _perfEnRouteText      = null!;
    private ListBox   _perfLandingText      = null!;
    private Button    _perfTakeoffExpandBtn = null!;
    private Button    _perfLandingExpandBtn = null!;
    private bool      _perfTakeoffExpanded;
    private bool      _perfLandingExpanded;
    // Weather tab – one pane per station
    private ListBox   _weatherDepText   = null!;
    private ListBox   _weatherDestText  = null!;
    private ListBox   _weatherAltnText  = null!;

    // ─────────────────────────────────────────────────────────────────────────

    public SimBriefPlannerForm(string simbriefUsername, ScreenReaderAnnouncer? announcer = null)
    {
        _simbriefUsername = simbriefUsername;
        _announcer = announcer;
        InitializeComponent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Form initialisation
    // ─────────────────────────────────────────────────────────────────────────

    private void InitializeComponent()
    {
        Text = "SimBrief Flight Planner";
        Size = new Size(900, 680);
        MinimumSize = new Size(700, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;

        _outerTabs = new AccessibleTabControl
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Planner tabs"
        };

        _outerTabs.TabPages.Add(BuildCreateTab());
        _outerTabs.TabPages.Add(BuildViewTab());

        Controls.Add(_outerTabs);

        // Load aircraft types asynchronously once the form handle is ready
        Load += async (_, _) => await LoadAircraftTypesAsync();

        // Set SplitterDistance after the form has been fully laid out
        Shown += OnFirstShown;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Create tab
    // ─────────────────────────────────────────────────────────────────────────

    private TabPage BuildCreateTab()
    {
        var tab = new TabPage("Create")
        {
            AccessibleName = "Create flight plan",
            AccessibleDescription = "Enter flight details and open SimBrief dispatch website with fields pre-filled"
        };

        // Scrollable outer panel
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(12),
            TabStop = false
        };

        // Two-column table: Label | Control
        var table = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0),
            Margin = new Padding(0),
            TabStop = false
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Instructions
        var instructions = new Label
        {
            Text = "Fill in the fields below, then press Plan on SimBrief Website. " +
                   "Your browser will open with the details pre-filled. " +
                   "You must be logged in to SimBrief for the page to work. " +
                   "All fields except Origin and Destination are optional.",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 0, 10)
        };
        table.Controls.Add(instructions, 0, 0);
        table.SetColumnSpan(instructions, 2);

        // Helper: add a labelled row
        int row = 1;
        Control AddRow(string labelText, Control control)
        {
            var lbl = new Label
            {
                Text = labelText,
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, 6, 8, 0)
            };
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, 3, 0, 3);
            table.Controls.Add(lbl, 0, row);
            table.Controls.Add(control, 1, row);
            row++;
            return control;
        }

        _createOrigin = (TextBox)AddRow("Origin ICAO:",
            new TextBox { AccessibleName = "Origin ICAO", AccessibleDescription = "4-letter ICAO code for departure airport e.g. EGLL", MaxLength = 4 });

        _createDest = (TextBox)AddRow("Destination ICAO:",
            new TextBox { AccessibleName = "Destination ICAO", AccessibleDescription = "4-letter ICAO code for destination airport e.g. KJFK", MaxLength = 4 });

        _createAcType = new ComboBox
        {
            AccessibleName = "Aircraft Type",
            AccessibleDescription = "Type or select an ICAO aircraft type code e.g. B77W. List loads from SimBrief.",
            DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems
        };
        AddRow("Aircraft Type:", _createAcType);

        _createAltn = (TextBox)AddRow("Alternate ICAO:",
            new TextBox { AccessibleName = "Alternate ICAO", AccessibleDescription = "ICAO code of alternate airport", MaxLength = 4 });

        _createCruiseLevel = (TextBox)AddRow("Cruise Level (ft):",
            new TextBox { AccessibleName = "Cruise Level", AccessibleDescription = "Cruise altitude in feet e.g. 36000 or FL360", MaxLength = 6 });

        _createAirline = (TextBox)AddRow("Airline ICAO Code:",
            new TextBox { AccessibleName = "Airline ICAO Code", AccessibleDescription = "3-letter airline ICAO code e.g. BAW", MaxLength = 3 });

        _createFlightNum = (TextBox)AddRow("Flight Number:",
            new TextBox { AccessibleName = "Flight Number", AccessibleDescription = "Flight number without airline prefix e.g. 178", MaxLength = 10 });

        _createCi = (TextBox)AddRow("Cost Index:",
            new TextBox { AccessibleName = "Cost Index", AccessibleDescription = "Cost index 0 to 999", Text = "30", MaxLength = 4 });

        _createRoute = (TextBox)AddRow("Route (optional):",
            new TextBox { AccessibleName = "Route", AccessibleDescription = "Route string e.g. MATCH DCT WOBUN", MaxLength = 500 });

        _createPax = (TextBox)AddRow("Number of Passengers:",
            new TextBox { AccessibleName = "Number of Passengers", AccessibleDescription = "Number of passengers on board", MaxLength = 4 });

        _createFreight = (TextBox)AddRow("Freight Weight:",
            new TextBox { AccessibleName = "Freight Weight", AccessibleDescription = "Freight or cargo weight in selected units", MaxLength = 8 });

        _createWeightUnits = new ComboBox
        {
            AccessibleName = "Weight Units",
            AccessibleDescription = "Select weight units: lbs or kgs",
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _createWeightUnits.Items.AddRange(new object[] { "lbs", "kgs" });
        _createWeightUnits.SelectedIndex = 0;
        AddRow("Weight Units:", _createWeightUnits);

        // Plan button – full width
        _planButton = new Button
        {
            Text = "Plan on SimBrief Website",
            Height = 32,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 0, 4),
            AccessibleName = "Plan on SimBrief Website",
            AccessibleDescription = "Open SimBrief dispatch page in your browser with the above fields pre-filled"
        };
        _planButton.Click += PlanButton_Click;
        table.Controls.Add(_planButton, 0, row);
        table.SetColumnSpan(_planButton, 2);
        row++;

        // Status label
        _createStatus = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            AccessibleName = "Status",
            ForeColor = System.Drawing.SystemColors.Highlight,
            Margin = new Padding(0, 4, 0, 0)
        };
        table.Controls.Add(_createStatus, 0, row);
        table.SetColumnSpan(_createStatus, 2);

        scroll.Controls.Add(table);
        tab.Controls.Add(scroll);
        return tab;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // View tab
    // ─────────────────────────────────────────────────────────────────────────

    private TabPage BuildViewTab()
    {
        var tab = new TabPage("View")
        {
            AccessibleName = "View flight plan",
            AccessibleDescription = "Fetch and read your latest SimBrief flight plan"
        };

        // Top bar: fetch button + status
        var topBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(6, 6, 6, 0),
            AutoSize = false,
            TabStop = false
        };

        string userHint = string.IsNullOrEmpty(_simbriefUsername)
            ? "(no username set – configure via File > Define SimBrief Username)"
            : $"Username: {_simbriefUsername}";

        _fetchButton = new Button
        {
            Text = "Fetch Latest Plan",
            Width = 150,
            Height = 30,
            AccessibleName = "Fetch Latest Plan",
            AccessibleDescription = "Download your latest SimBrief flight plan"
        };
        _fetchButton.Click += FetchButton_Click;

        _viewStatus = new Label
        {
            Text = userHint,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(10, 7, 0, 0),
            AccessibleName = "Status",
            TabStop = false
        };

        topBar.Controls.Add(_fetchButton);
        topBar.Controls.Add(_viewStatus);

        // Inner tab control for the plan sections
        _viewTabs = new AccessibleTabControl
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Flight plan sections"
        };

        _viewTabs.TabPages.Add(BuildOverviewSubTab());
        _viewTabs.TabPages.Add(BuildNavLogSubTab());
        _viewTabs.TabPages.Add(BuildTextSubTab("Fuel",    "Fuel",    "Expanded fuel breakdown", ref _fuelText!));
        _viewTabs.TabPages.Add(BuildTextSubTab("Weights", "Weights", "Weights and payload breakdown", ref _weightsText!));
        _viewTabs.TabPages.Add(BuildPerformanceTab());
        _viewTabs.TabPages.Add(BuildWeatherTab());

        tab.Controls.Add(_viewTabs);
        tab.Controls.Add(topBar);   // Added last = drawn first (DockStyle.Top)
        return tab;
    }

    private TabPage BuildOverviewSubTab()
    {
        var tab = new TabPage("Overview")
        {
            AccessibleName = "Overview",
            AccessibleDescription = "Key flight plan sections. Use arrow keys to navigate sections; details appear on the right."
        };

        _overviewSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            TabStop = false   // SplitContainer itself shouldn't receive Tab focus
        };
        var split = _overviewSplit;

        _overviewTree = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false,
            AccessibleName = "Flight plan sections",
            AccessibleDescription = "Arrow through sections; details appear on the right"
        };
        _overviewTree.AfterSelect += OverviewTree_AfterSelect;

        _overviewDetail = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new System.Drawing.Font("Consolas", 9.5f),
            HorizontalScrollbar = true,
            AccessibleName = "Section details",
            AccessibleDescription = "Detail for the selected section"
        };

        // Prevent Tab from landing on the SplitterPanel containers themselves;
        // NVDA would otherwise announce "pane" when tabbing between tree and detail box.
        split.Panel1.TabStop = false;
        split.Panel2.TabStop = false;

        split.Panel1.Controls.Add(_overviewTree);
        split.Panel2.Controls.Add(_overviewDetail);
        tab.Controls.Add(split);
        return tab;
    }

    private TabPage BuildNavLogSubTab()
    {
        var tab = new TabPage("Nav Log")
        {
            AccessibleName = "Navigation log",
            AccessibleDescription = "Expandable waypoint list. Up/Down to move between waypoints. Right arrow to expand a waypoint for more detail. Left arrow to collapse."
        };

        // Hint label
        var hint = new Label
        {
            Text = "Up/Down: waypoints   Right: expand   Left: collapse",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 22,
            Padding = new Padding(4, 3, 0, 0),
            TabStop = false
        };

        _navLogGrid = new TreeView
        {
            Dock          = DockStyle.Fill,
            ShowLines     = true,
            ShowPlusMinus = true,
            ShowRootLines = true,
            FullRowSelect = true,
            HideSelection = false,
            AccessibleName = "Navigation log",
            TabStop        = true
        };

        tab.Controls.Add(_navLogGrid);
        tab.Controls.Add(hint);
        return tab;
    }

    private TabPage BuildTextSubTab(string tabName, string accessibleName, string description, ref ListBox textField)
    {
        var tab = new TabPage(tabName)
        {
            AccessibleName = accessibleName,
            AccessibleDescription = description
        };

        textField = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new System.Drawing.Font("Consolas", 9.5f),
            HorizontalScrollbar = true,
            AccessibleName = accessibleName,
            AccessibleDescription = description
        };

        tab.Controls.Add(textField);
        return tab;
    }

    private TabPage BuildPerformanceTab()
    {
        var tab = new TabPage("Performance")
        {
            AccessibleName = "Performance",
            AccessibleDescription = "Takeoff, En Route and Landing performance. " +
                "Use the Additional Info buttons to show runway distances and weights."
        };

        var panel   = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6), TabStop = false };
        var font     = new System.Drawing.Font("Consolas", 9.5f);
        var boldFont = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold);
        const int lh  = 20;
        const int gap = 10;
        const int bh  = 30;   // button height

        Label MakeLbl(string text, int y) => new Label
        {
            Text = text, Location = new System.Drawing.Point(6, y),
            AutoSize = false, Width = 500, Height = lh, Font = boldFont, TabStop = false
        };

        ListBox MakeBox(string accName, string desc, int y, int h, ref ListBox field)
        {
            field = new ListBox
            {
                Location = new System.Drawing.Point(6, y),
                Size = new System.Drawing.Size(600, h),
                Font = font,
                HorizontalScrollbar = true,
                AccessibleName = accName,
                AccessibleDescription = desc,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                TabStop = true
            };
            return field;
        }

        Button MakeBtn(string text, string accName, string desc, int y, EventHandler handler)
        {
            var btn = new Button
            {
                Text = text, Location = new System.Drawing.Point(6, y),
                Width = 280, Height = bh,
                AccessibleName = accName, AccessibleDescription = desc,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            btn.Click += handler;
            return btn;
        }

        int y0 = 6;

        // ── Takeoff ────────────────────────────────────────────────────────
        var lbl1 = MakeLbl("Takeoff Performance", y0);
        var tb1  = MakeBox("Takeoff Performance",
            "V1, rotation speed, V2, flaps, thrust, runway surface and conditions",
            y0 + lh + 2, 160, ref _perfTakeoffText!);
        y0 += lh + 2 + 160 + 4;
        _perfTakeoffExpandBtn = MakeBtn(
            "Show Additional Takeoff Info",
            "Show Additional Takeoff Info",
            "Toggle runway distances and weight data for the planned takeoff runway",
            y0, TakeoffExpandBtn_Click);
        _perfTakeoffExpandBtn.Enabled = false;
        y0 += bh + gap;

        // ── En Route ───────────────────────────────────────────────────────
        var lbl2 = MakeLbl("En Route Performance", y0);
        var tb2  = MakeBox("En Route Performance",
            "Climb, cruise and descent profiles and speeds",
            y0 + lh + 2, 130, ref _perfEnRouteText!);
        y0 += lh + 2 + 130 + gap;

        // ── Landing ────────────────────────────────────────────────────────
        var lbl3 = MakeLbl("Landing Performance", y0);
        var tb3  = MakeBox("Landing Performance",
            "Reference speed, flaps, brakes and runway surface",
            y0 + lh + 2, 160, ref _perfLandingText!);
        y0 += lh + 2 + 160 + 4;
        _perfLandingExpandBtn = MakeBtn(
            "Show Additional Landing Info",
            "Show Additional Landing Info",
            "Toggle runway length and landing distance data for the planned arrival runway",
            y0, LandingExpandBtn_Click);
        _perfLandingExpandBtn.Enabled = false;

        panel.Controls.AddRange(new Control[]
        {
            lbl1, tb1, _perfTakeoffExpandBtn,
            lbl2, tb2,
            lbl3, tb3, _perfLandingExpandBtn
        });
        tab.Controls.Add(panel);
        return tab;
    }

    private void TakeoffExpandBtn_Click(object? sender, EventArgs e)
    {
        _perfTakeoffExpanded = !_perfTakeoffExpanded;
        _perfTakeoffExpandBtn.Text = _perfTakeoffExpanded
            ? "Hide Additional Takeoff Info"
            : "Show Additional Takeoff Info";
        SetListText(_perfTakeoffText, BuildTakeoffText());
        if (_perfTakeoffExpanded)
            FocusListBoxAtSection(_perfTakeoffText, "Additional Info");
        else if (_perfTakeoffText.Items.Count > 0)
            _perfTakeoffText.SelectedIndex = 0;
    }

    private void LandingExpandBtn_Click(object? sender, EventArgs e)
    {
        _perfLandingExpanded = !_perfLandingExpanded;
        _perfLandingExpandBtn.Text = _perfLandingExpanded
            ? "Hide Additional Landing Info"
            : "Show Additional Landing Info";
        SetListText(_perfLandingText, BuildLandingText());
        if (_perfLandingExpanded)
            FocusListBoxAtSection(_perfLandingText, "Additional Info");
        else if (_perfLandingText.Items.Count > 0)
            _perfLandingText.SelectedIndex = 0;
    }

    private TabPage BuildWeatherTab()
    {
        var tab = new TabPage("Weather")
        {
            AccessibleName = "Weather",
            AccessibleDescription = "METARs and TAFs for departure, destination and alternate, each in a separate read-only section"
        };

        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6), TabStop = false };

        var font = new System.Drawing.Font("Consolas", 9.5f);
        const int labelHeight = 20;
        const int boxHeight   = 160;
        const int gap         = 10;

        Label MakeLabel(string text, int y) => new Label
        {
            Text = text, Location = new System.Drawing.Point(6, y),
            AutoSize = false, Width = 400, Height = labelHeight,
            Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold),
            TabStop = false
        };

        ListBox MakeBox(string accessibleName, string description, int y, ref ListBox field) =>
            field = new ListBox
            {
                Location = new System.Drawing.Point(6, y),
                Size = new System.Drawing.Size(600, boxHeight),
                Font = font,
                HorizontalScrollbar = true,
                AccessibleName = accessibleName,
                AccessibleDescription = description,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                TabStop = true
            };

        int y0 = 6;
        var lbl1 = MakeLabel("Departure Weather", y0);
        var tb1  = MakeBox("Departure weather", "METAR and TAF for departure airport", y0 + labelHeight + 2, ref _weatherDepText!);

        y0 += labelHeight + 2 + boxHeight + gap;
        var lbl2 = MakeLabel("Destination Weather", y0);
        var tb2  = MakeBox("Destination weather", "METAR and TAF for destination airport", y0 + labelHeight + 2, ref _weatherDestText!);

        y0 += labelHeight + 2 + boxHeight + gap;
        var lbl3 = MakeLabel("Alternate Weather", y0);
        var tb3  = MakeBox("Alternate weather", "METAR and TAF for alternate airport, or note if no alternate", y0 + labelHeight + 2, ref _weatherAltnText!);

        panel.Controls.AddRange(new Control[] { lbl1, tb1, lbl2, tb2, lbl3, tb3 });
        tab.Controls.Add(panel);
        return tab;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Create tab – event handlers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task LoadAircraftTypesAsync()
    {
        _createAcType.Items.Clear();
        _createAcType.Text = "Loading aircraft types...";
        _createAcType.Enabled = false;

        try
        {
            var types = await _service.FetchAircraftTypesAsync();
            _createAcType.Items.Clear();
            foreach (var (id, name) in types)
                _createAcType.Items.Add($"{id} - {name}");

            _createAcType.Text = "";
        }
        catch
        {
            _createAcType.Items.Clear();
            _createAcType.Text = "";
            // Silently fail – user can still type a type code manually
        }
        finally
        {
            _createAcType.Enabled = true;
        }
    }

    private void PlanButton_Click(object? sender, EventArgs e)
    {
        string origin = _createOrigin.Text.Trim().ToUpperInvariant();
        string dest   = _createDest.Text.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(origin) || string.IsNullOrEmpty(dest))
        {
            _createStatus.Text = "Origin and Destination are required.";
            _createStatus.AccessibleDescription = "Error: Origin and Destination are required.";
            return;
        }

        var url = new StringBuilder("https://dispatch.simbrief.com/options/custom?");
        url.Append($"orig={Uri.EscapeDataString(origin)}");
        url.Append($"&dest={Uri.EscapeDataString(dest)}");

        string acType = ExtractAircraftTypeId(_createAcType.Text);
        if (!string.IsNullOrEmpty(acType))
            url.Append($"&type={Uri.EscapeDataString(acType)}");

        Append(url, "altn",    _createAltn.Text.Trim().ToUpperInvariant());
        Append(url, "fl",      _createCruiseLevel.Text.Trim());
        Append(url, "airline", _createAirline.Text.Trim().ToUpperInvariant());
        Append(url, "fltnum",  _createFlightNum.Text.Trim());
        Append(url, "civalue", _createCi.Text.Trim());
        Append(url, "route",   _createRoute.Text.Trim());
        Append(url, "pax",     _createPax.Text.Trim());
        Append(url, "cargo",   _createFreight.Text.Trim());

        string finalUrl = url.ToString();

        try
        {
            Process.Start(new ProcessStartInfo(finalUrl) { UseShellExecute = true });
            _createStatus.Text = "Opened SimBrief in your browser. You may need to log in first.";
        }
        catch (Exception ex)
        {
            _createStatus.Text = $"Could not open browser: {ex.Message}";
        }
    }

    private static void Append(StringBuilder url, string param, string value)
    {
        if (!string.IsNullOrEmpty(value))
            url.Append($"&{param}={Uri.EscapeDataString(value)}");
    }

    private static string ExtractAircraftTypeId(string text)
    {
        text = text.Trim();
        int dash = text.IndexOf(" - ", StringComparison.Ordinal);
        return dash >= 0 ? text[..dash].Trim() : text;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // View tab – event handlers
    // ─────────────────────────────────────────────────────────────────────────

    private async void FetchButton_Click(object? sender, EventArgs e) => await FetchPlanAsync();

    private async Task FetchPlanAsync()
    {
        if (_fetchInProgress) return;

        if (string.IsNullOrWhiteSpace(_simbriefUsername))
        {
            _viewStatus.Text = "No SimBrief username set. Go to File > Define SimBrief Username.";
            return;
        }

        _fetchInProgress = true;

        // Reset immediately so stale data from a prior fetch can never bleed through.
        _ofp = null;
        _navLogGrid.Nodes.Clear();
        _fetchButton.Enabled = false;
        _viewStatus.Text = "Fetching flight plan...";

        try
        {
            _ofp = await _service.FetchFullOFPAsync(_simbriefUsername);
            PopulateViewTabs();
            bool hasEfob = _ofp.NavLog.Any(f => !string.IsNullOrEmpty(f.Efob));
            string efobNote = hasEfob ? "" : $" — EFOB missing (fix fields: {_ofp.NavLogFieldNames})";
            _viewStatus.Text = $"Plan loaded: {_ofp.OriginIcao} → {_ofp.DestIcao}{efobNote}";
        }
        catch (Exception ex)
        {
            _viewStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _fetchButton.Enabled = true;
            _fetchInProgress = false;
        }
    }

    /// <summary>
    /// Called by the host EFB form whenever the planner window is opened, so the
    /// plan is always fresh when the user navigates to the View tab.
    /// Guarded by _fetchInProgress so rapid open/close doesn't queue parallel fetches.
    /// </summary>
    public void BeginAutoFetch()
    {
        if (!string.IsNullOrWhiteSpace(_simbriefUsername))
            _ = FetchPlanAsync();   // _fetchInProgress guard inside prevents double-fetch
    }

    private void OverviewTree_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (_ofp == null || e.Node == null) return;

        SetListText(_overviewDetail, e.Node.Name switch
        {
            "FlightInfo"   => BuildFlightInfoText(),
            "Departure"    => BuildDepartureText(),
            "Destination"  => BuildDestinationText(),
            "Cruise"       => BuildCruiseText(),
            "FuelSummary"  => BuildFuelSummaryText(),
            "WeightSummary"=> BuildWeightSummaryText(),
            _              => ""
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Populate all view tabs with OFP data
    // ─────────────────────────────────────────────────────────────────────────

    private void PopulateViewTabs()
    {
        if (_ofp == null) return;

        PopulateOverviewTree();
        PopulateNavLog();
        SetListText(_fuelText,    BuildFuelText());
        SetListText(_weightsText, BuildWeightsText());

        // Reset expand state on fresh load
        _perfTakeoffExpanded = false;
        _perfLandingExpanded = false;
        bool hasTlr = !string.IsNullOrEmpty(_ofp.TlrText);
        _perfTakeoffExpandBtn.Enabled = hasTlr;
        _perfLandingExpandBtn.Enabled = hasTlr;
        _perfTakeoffExpandBtn.Text = "Show Additional Takeoff Info";
        _perfLandingExpandBtn.Text = "Show Additional Landing Info";

        SetListText(_perfTakeoffText, BuildTakeoffText());
        SetListText(_perfEnRouteText, BuildEnRouteText());
        SetListText(_perfLandingText, BuildLandingText());

        SetListText(_weatherDepText,  BuildWeatherStationText("DEPARTURE",  _ofp.OriginIcao, _ofp.OriginMetar, _ofp.OriginTaf));
        SetListText(_weatherDestText, BuildWeatherStationText("DESTINATION", _ofp.DestIcao,   _ofp.DestMetar,   _ofp.DestTaf));
        SetListText(_weatherAltnText, string.IsNullOrEmpty(_ofp.AltnIcao)
            ? "No alternate in this flight plan."
            : BuildWeatherStationText("ALTERNATE", _ofp.AltnIcao, _ofp.AltnMetar, _ofp.AltnTaf));
    }

    private void PopulateOverviewTree()
    {
        _overviewTree.Nodes.Clear();
        _overviewDetail.Items.Clear();

        AddNode("Flight Info",     "FlightInfo");
        AddNode("Departure",       "Departure");
        AddNode("Destination",     "Destination");
        AddNode("Cruise",          "Cruise");
        AddNode("Fuel Summary",    "FuelSummary");
        AddNode("Weight Summary",  "WeightSummary");

        if (_overviewTree.Nodes.Count > 0)
        {
            _overviewTree.SelectedNode = _overviewTree.Nodes[0];
            SetListText(_overviewDetail, BuildFlightInfoText());
        }

        void AddNode(string text, string name)
        {
            var node = new TreeNode(text) { Name = name };
            _overviewTree.Nodes.Add(node);
        }
    }

    private void PopulateNavLog()
    {
        // BeginUpdate wraps the Clear too so the tree never renders a partial state.
        _navLogGrid.BeginUpdate();
        _navLogGrid.Nodes.Clear();

        if (_ofp == null)
        {
            _navLogGrid.EndUpdate();
            return;
        }

        var fixes = _ofp.NavLog;

        // Determine which SID/STAR fixes are SID (before first enroute) vs STAR (after last enroute).
        int firstEnroute = fixes.FindIndex(f => !f.IsSidStar);
        int lastEnroute  = fixes.FindLastIndex(f => !f.IsSidStar);

        // Trans alt (dep) and trans level (dest) in feet for altitude display rules.
        int transAlt   = int.TryParse(_ofp.OriginTransAlt,  out int _ta) ? _ta : 0;
        int transLevel = int.TryParse(_ofp.DestTransLevel,   out int _tl) ? _tl : 0;
        for (int i = 0; i < fixes.Count; i++)
        {
            var fix = fixes[i];
            string u = _ofp.Units;

            // ── Parent node summary ──────────────────────────────────────────
            var parts = new List<string> { fix.Ident };

            // SID / STAR label
            if (fix.IsSidStar)
            {
                string procLabel = (firstEnroute < 0 || i < firstEnroute) ? "SID" : "STAR";
                parts.Add(procLabel);
            }

            if (!string.IsNullOrEmpty(fix.ViaAirway))
                parts.Add($"via {fix.ViaAirway}");

            if (!string.IsNullOrEmpty(fix.DistLeg) && fix.DistLeg != "0")
                parts.Add($"{fix.DistLeg} nm");

            if (!string.IsNullOrEmpty(fix.DistCum) && fix.DistCum != "0")
                parts.Add($"{fix.DistCum} nm total");

            if (!string.IsNullOrEmpty(fix.Course) && fix.Course != "0" && fix.Course != "000")
                parts.Add($"course {fix.Course}°");

            // Altitude display:
            // - Airport fixes (origin/dest) at endpoints: suppress — elevation isn't a cruising alt
            // - SID fixes below trans alt: show feet
            // - STAR fixes below trans level: show feet
            // - Everything else: FL format
            bool isEndpointApt = fix.Type.Equals("apt", StringComparison.OrdinalIgnoreCase)
                                 && (i == 0 || i == fixes.Count - 1);
            if (!isEndpointApt && !string.IsNullOrEmpty(fix.AltitudeFt)
                && int.TryParse(fix.AltitudeFt, out int altRaw) && altRaw > 0)
            {
                int altFt = altRaw >= 1000 ? altRaw : altRaw * 100;
                bool isSid  = fix.IsSidStar && (firstEnroute < 0 || i < firstEnroute);
                bool isStar = fix.IsSidStar && firstEnroute >= 0 && i > lastEnroute;

                string altDisplay;
                if (isSid && transAlt > 0 && altFt < transAlt)
                    altDisplay = $"{altFt:N0} ft";
                else if (isStar && transLevel > 0 && altFt < transLevel)
                    altDisplay = $"{altFt:N0} ft";
                else
                    altDisplay = altRaw >= 1000 ? $"FL{altRaw / 100:D3}" : $"FL{altRaw:D3}";
                parts.Add(altDisplay);
            }

            // Mach: "0.82" → ".82", IAS: append "kt"
            string machStr = fix.Mach.TrimStart('0');  // "0.82" → ".82"; "" → ""
            string iasStr  = string.IsNullOrEmpty(fix.Ias) ? "" : $"{fix.Ias}kt";
            if (!string.IsNullOrEmpty(machStr) && !string.IsNullOrEmpty(iasStr))
                parts.Add($"{machStr} / {iasStr}");
            else if (!string.IsNullOrEmpty(machStr))
                parts.Add(machStr);
            else if (!string.IsNullOrEmpty(iasStr))
                parts.Add(iasStr);

            var parent = new TreeNode(string.Join(", ", parts));

            // ── Child nodes (detail) ─────────────────────────────────────────
            // Full name + type
            string nameLabel = fix.VorName;
            string typeLabel = string.IsNullOrEmpty(fix.Type) ? "" : fix.Type.ToUpperInvariant();
            if (!string.IsNullOrEmpty(nameLabel) && !string.IsNullOrEmpty(typeLabel))
                parent.Nodes.Add($"{nameLabel}, {typeLabel}");
            else if (!string.IsNullOrEmpty(nameLabel))
                parent.Nodes.Add(nameLabel);
            else if (!string.IsNullOrEmpty(typeLabel))
                parent.Nodes.Add(typeLabel);

            if (!string.IsNullOrEmpty(fix.IcaoFir))
                parent.Nodes.Add($"FIR: {fix.IcaoFir}");

            if (!string.IsNullOrEmpty(fix.Frequency))
                parent.Nodes.Add($"Frequency: {fix.Frequency}");

            // Leg time (seconds → "Xh Ym" or "X min")
            if (!string.IsNullOrEmpty(fix.TimeLeg) && fix.TimeLeg != "0" &&
                int.TryParse(fix.TimeLeg, out int legSecs) && legSecs > 0)
            {
                int legMins = legSecs / 60;
                string legTimeStr = legMins >= 60
                    ? $"{legMins / 60}h {legMins % 60:D2}m"
                    : $"{legMins} min";
                parent.Nodes.Add($"Leg time: {legTimeStr}");
            }

            // Elapsed (seconds → "H:MM")
            string elapsed = FormatElapsed(fix.TimeTotal);
            if (!string.IsNullOrEmpty(elapsed))
                parent.Nodes.Add($"Elapsed: {elapsed}");

            // Wind + wind component (wind_comp: negative = headwind, positive = tailwind)
            if (!string.IsNullOrEmpty(fix.WindDir) && !string.IsNullOrEmpty(fix.WindSpd))
            {
                string windStr = $"Wind: {fix.WindDir}° / {fix.WindSpd}kt";
                if (!string.IsNullOrEmpty(fix.WindComp) &&
                    double.TryParse(fix.WindComp,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double wcd) && wcd != 0)
                {
                    int wcKt = (int)Math.Round(Math.Abs(wcd));
                    string compLabel = wcd < 0 ? $"headwind {wcKt}kt" : $"tailwind {wcKt}kt";
                    windStr += $", {compLabel}";
                }
                parent.Nodes.Add(windStr);
            }

            if (!string.IsNullOrEmpty(fix.Oat))
                parent.Nodes.Add($"OAT: {fix.Oat}°C");

            if (!string.IsNullOrEmpty(fix.IsaDev))
                parent.Nodes.Add($"ISA dev: {fix.IsaDev}°C");

            string mora = FormatNavAlt(fix.Mora);
            if (mora != "-")
                parent.Nodes.Add($"MORA: {mora}");

            // Fuel: EFOB (estimated fuel on board) — prefer efob field, fall back to fuel_plan_onboard
            string fobRaw = !string.IsNullOrEmpty(fix.Efob) ? fix.Efob : fix.FuelPlanOnboard;
            if (!string.IsNullOrEmpty(fobRaw) && fobRaw != "0" &&
                int.TryParse(fobRaw, out int fobVal) && fobVal > 0)
                parent.Nodes.Add($"EFOB: {fobVal:N0} {u}");

            if (!string.IsNullOrEmpty(fix.FuelLeg) && fix.FuelLeg != "0" &&
                int.TryParse(fix.FuelLeg, out int fuelLeg) && fuelLeg > 0)
                parent.Nodes.Add($"Leg fuel: {fuelLeg:N0} {u}");

            if (!string.IsNullOrEmpty(fix.FuelTotalUsed) && fix.FuelTotalUsed != "0" &&
                int.TryParse(fix.FuelTotalUsed, out int fuelUsed) && fuelUsed > 0)
                parent.Nodes.Add($"Total fuel used: {fuelUsed:N0} {u}");

            _navLogGrid.Nodes.Add(parent);
        }
        _navLogGrid.EndUpdate();

        // Focus first node
        if (_navLogGrid.Nodes.Count > 0)
            _navLogGrid.SelectedNode = _navLogGrid.Nodes[0];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Text builders for each section
    // ─────────────────────────────────────────────────────────────────────────

    private string BuildFlightInfoText()
    {
        if (_ofp == null) return "";
        return Section("FLIGHT INFO",
            ("Airline",     _ofp.AirlineIcao),
            ("Flight",      _ofp.FlightNumber),
            ("Callsign",    _ofp.Callsign),
            ("Aircraft",    _ofp.AircraftName),
            ("Type",        _ofp.AircraftIcao),
            ("Reg",         _ofp.AircraftReg),
            ("Route",       _ofp.Route),
            ("Distance",    Nm(_ofp.RouteDistance)),
            ("Air Time",    FormatAirTime(_ofp.AirTime)),
            ("Passengers",  _ofp.Passengers),
            ("Cost Index",  _ofp.CostIndex),
            ("Avg Wind",    FormatAvgWind(_ofp.AvgWindComp)),
            ("Avg ISA Dev", _ofp.AvgIsaDev.Length > 0 ? _ofp.AvgIsaDev + "°C" : ""),
            ("Units",       _ofp.Units));
    }

    private string BuildDepartureText()
    {
        if (_ofp == null) return "";
        string wind = (_ofp.OriginWindDir.Length > 0 && _ofp.OriginWindSpd.Length > 0)
            ? $"{_ofp.OriginWindDir}° at {_ofp.OriginWindSpd}kt" : "";

        return Section("DEPARTURE",
            ("Airport",        _ofp.OriginIcao),
            ("Name",           _ofp.OriginName),
            ("Elevation",      _ofp.OriginElevation.Length > 0 ? _ofp.OriginElevation + "ft" : ""),
            ("Runway",         _ofp.OriginRunway),
            ("SID",            _ofp.OriginSid),
            ("SID Transition", _ofp.OriginSidTrans),
            ("Trans Alt",      FormatTransFt(_ofp.OriginTransAlt)),
            ("Trans Level",    FormatTransLevel(_ofp.OriginTransLevel)),
            ("Surface Wind",   wind));
    }

    private string BuildDestinationText()
    {
        if (_ofp == null) return "";
        return Section("DESTINATION",
            ("Airport",        _ofp.DestIcao),
            ("Name",           _ofp.DestName),
            ("Elevation",      _ofp.DestElevation.Length > 0 ? _ofp.DestElevation + "ft" : ""),
            ("Runway",         _ofp.DestRunway),
            ("STAR",           _ofp.DestStar),
            ("STAR Transition",_ofp.DestStarTrans),
            ("Approach",       _ofp.DestApproach),
            ("Appr Transition",_ofp.DestApproachTrans),
            ("ILS Frequency",  _ofp.DestIlsFreq.Length > 0 ? _ofp.DestIlsFreq + " MHz" : ""),
            ("Trans Alt",      FormatTransFt(_ofp.DestTransAlt)),
            ("Trans Level",    FormatTransLevel(_ofp.DestTransLevel)),
            ("Alternate",      _ofp.AltnIcao.Length > 0 ? $"{_ofp.AltnIcao} ({_ofp.AltnName})" : ""));
    }

    private string BuildCruiseText()
    {
        if (_ofp == null) return "";
        string stepClimbs = string.IsNullOrEmpty(_ofp.StepClimbString) ? "" : _ofp.StepClimbString;
        return Section("CRUISE",
            ("Initial Altitude", FormatNavAlt(_ofp.InitialAltitude)),
            ("Cruise Mach",      _ofp.CruiseMach.Length > 0 ? "M" + _ofp.CruiseMach : ""),
            ("Cruise TAS",       _ofp.CruiseTas.Length > 0 ? _ofp.CruiseTas + "kt" : ""),
            ("Cost Index",       _ofp.CostIndex),
            ("Avg Wind",         FormatAvgWind(_ofp.AvgWindComp)),
            ("Avg ISA Dev",      _ofp.AvgIsaDev.Length > 0 ? _ofp.AvgIsaDev + "°C" : ""),
            ("Step Climbs",      stepClimbs));
    }

    private string BuildFuelSummaryText()
    {
        if (_ofp == null) return "";
        string u = _ofp.Units;
        return Section("FUEL SUMMARY",
            ("Block (Ramp)",  Wt(_ofp.FuelBlockRamp, u)),
            ("Trip Fuel",     Wt(_ofp.FuelTrip, u)),
            ("Reserves",      Wt(_ofp.FuelReserve, u)),
            ("Alternate",     Wt(_ofp.FuelAlternate, u)),
            ("Min Takeoff",   Wt(_ofp.FuelMinTakeoff, u)));
    }

    private string BuildWeightSummaryText()
    {
        if (_ofp == null) return "";
        string u = _ofp.Units;
        return Section("WEIGHT SUMMARY",
            ("Zero Fuel Weight",     Wt(_ofp.WeightZfw, u)),
            ("Max Zero Fuel Wt",     Wt(_ofp.WeightMaxZfw, u)),
            ("Takeoff Weight",       Wt(_ofp.WeightTow, u)),
            ("Max Takeoff Weight",   Wt(_ofp.WeightMaxTow, u)),
            ("Landing Weight",       Wt(_ofp.WeightLw, u)),
            ("Max Landing Weight",   Wt(_ofp.WeightMaxLw, u)),
            ("Payload",              Wt(_ofp.WeightPayload, u)),
            ("Passengers",           _ofp.Passengers.Length > 0 ? _ofp.Passengers + " pax" : ""));
    }

    private string BuildFuelText()
    {
        if (_ofp == null) return "";
        string u = _ofp.Units;
        return Section("FUEL",
            ("Block / Ramp",    Wt(_ofp.FuelBlockRamp, u)),
            ("Trip Fuel",       Wt(_ofp.FuelTrip, u)),
            ("Contingency",     Wt(_ofp.FuelContingency, u)),
            ("Alternate",       Wt(_ofp.FuelAlternate, u)),
            ("Final Reserve",   Wt(_ofp.FuelReserve, u)),
            ("Extra",           Wt(_ofp.FuelExtra, u)),
            ("Minimum Takeoff", Wt(_ofp.FuelMinTakeoff, u)),
            ("Taxi",            Wt(_ofp.FuelTaxi, u)),
            ("Planned Landing", Wt(_ofp.FuelPlannedLanding, u)));
    }

    private string BuildWeightsText()
    {
        if (_ofp == null) return "";
        string u = _ofp.Units;
        return Section("WEIGHTS",
            ("OEW",              Wt(_ofp.WeightOew, u)),
            ("Payload",          Wt(_ofp.WeightPayload, u)),
            ("Pax Weight",       Wt(_ofp.WeightPaxWeight, u)),
            ("Cargo",            Wt(_ofp.WeightCargo, u)),
            ("Est ZFW",          Wt(_ofp.WeightZfw, u)),
            ("Max ZFW",          Wt(_ofp.WeightMaxZfw, u)),
            ("Est TOW",          Wt(_ofp.WeightTow, u)),
            ("Max TOW",          Wt(_ofp.WeightMaxTow, u)),
            ("Est Landing Wt",   Wt(_ofp.WeightLw, u)),
            ("Max Landing Wt",   Wt(_ofp.WeightMaxLw, u)));
    }

    private string BuildTakeoffText()
    {
        if (_ofp == null) return "";

        if (!string.IsNullOrEmpty(_ofp.TlrText))
        {
            string tlr = FormatTlrTakeoff(_perfTakeoffExpanded);
            if (!string.IsNullOrEmpty(tlr)) return tlr;
        }

        // Fall back to structured XML performance fields
        bool hasData = !string.IsNullOrEmpty(_ofp.TakeoffV1) || !string.IsNullOrEmpty(_ofp.TakeoffVr)
                    || !string.IsNullOrEmpty(_ofp.TakeoffFlaps);
        if (!hasData) return
            "Takeoff performance data not available.\n\n" +
            "V-speed and TLR data is only included in certain SimBrief OFP types (e.g. LIDO, RYR). " +
            "In the Create tab, select one of those OFP types in SimBrief dispatch, then re-fetch.";

        return Section("TAKEOFF PERFORMANCE",
            ("V1",              Kt(_ofp.TakeoffV1)),
            ("VR",              Kt(_ofp.TakeoffVr)),
            ("V2",              Kt(_ofp.TakeoffV2)),
            ("Flaps",           _ofp.TakeoffFlaps),
            ("Trim",            _ofp.TakeoffTrim),
            ("Headwind",        _ofp.TakeoffHw.Length > 0 ? _ofp.TakeoffHw + "kt" : ""),
            ("Crosswind",       _ofp.TakeoffXw.Length > 0 ? _ofp.TakeoffXw + "kt" : ""),
            ("Limiting Factor", _ofp.PerfLimitFactor));
    }

    private string BuildEnRouteText()
    {
        if (_ofp == null) return "";
        bool hasData = !string.IsNullOrEmpty(_ofp.ClimbProfile) || !string.IsNullOrEmpty(_ofp.CruiseMach);
        if (!hasData) return "En route performance data not available for this plan.";
        return Section("EN ROUTE PERFORMANCE",
            ("Climb Profile",   _ofp.ClimbProfile),
            ("Climb IAS",       _ofp.ClimbIas.Length  > 0 ? _ofp.ClimbIas  + "kt" : ""),
            ("Climb Mach",      _ofp.ClimbMach.Length > 0 ? "M" + _ofp.ClimbMach : ""),
            ("Cruise Profile",  _ofp.CruiseProfile),
            ("Cruise Mach",     _ofp.CruiseMach.Length > 0 ? "M" + _ofp.CruiseMach : ""),
            ("Cruise TAS",      _ofp.CruiseTas.Length  > 0 ? _ofp.CruiseTas  + "kt" : ""),
            ("Step Climbs",     _ofp.StepClimbString),
            ("Descent Profile", _ofp.DescentProfile),
            ("Descent IAS",     _ofp.DescentIas.Length  > 0 ? _ofp.DescentIas  + "kt" : ""),
            ("Descent Mach",    _ofp.DescentMach.Length > 0 ? "M" + _ofp.DescentMach : ""));
    }

    private string BuildLandingText()
    {
        if (_ofp == null) return "";

        if (!string.IsNullOrEmpty(_ofp.TlrText))
        {
            string tlr = FormatTlrLanding(_perfLandingExpanded);
            if (!string.IsNullOrEmpty(tlr)) return tlr;
        }

        bool hasData = !string.IsNullOrEmpty(_ofp.LandingVapp) || !string.IsNullOrEmpty(_ofp.LandingDistDry);
        if (!hasData) return
            "Landing performance data not available.\n\n" +
            "V-ref and TLR data is only included in certain SimBrief OFP types (e.g. LIDO, RYR). " +
            "In the Create tab, select one of those OFP types in SimBrief dispatch, then re-fetch.";

        return Section("LANDING PERFORMANCE",
            ("VAPP",          Kt(_ofp.LandingVapp)),
            ("Flaps",         _ofp.LandingFlaps),
            ("Dist (Dry)",    MetresToFt(_ofp.LandingDistDry)),
            ("Dist (Wet)",    MetresToFt(_ofp.LandingDistWet)),
            ("Brake Setting", _ofp.LandingBrakeSetting),
            ("Headwind",      _ofp.LandingHw.Length > 0 ? _ofp.LandingHw + "kt" : ""),
            ("Crosswind",     _ofp.LandingXw.Length > 0 ? _ofp.LandingXw + "kt" : ""));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TLR formatter & parser
    // ─────────────────────────────────────────────────────────────────────────

    private string FormatTlrTakeoff(bool expanded)
    {
        if (_ofp == null || string.IsNullOrEmpty(_ofp.TlrText)) return "";
        string tlr = _ofp.TlrText;
        string units = _ofp.Units;

        // ── Section boundaries ─────────────────────────────────────────────
        int secStart = tlr.IndexOf("TAKEOFF DATA", StringComparison.OrdinalIgnoreCase);
        if (secStart < 0) return "";
        int secEnd   = tlr.IndexOf("LANDING DATA", StringComparison.OrdinalIgnoreCase);
        string section = secEnd > secStart ? tlr[secStart..secEnd] : tlr[secStart..];
        string[] lines = section.Split('\n');

        // ── Main planned data row ──────────────────────────────────────────
        int hdrIdx = TlrFindLine(lines, "APT", "V1", "VR", "V2");
        if (hdrIdx < 0) return "";
        int dataIdx = TlrNextDataLine(lines, hdrIdx, _ofp.OriginIcao);
        if (dataIdx < 0) return "";

        var vals = TlrMapCols(lines[hdrIdx], lines[dataIdx]);

        // ── RMKS ──────────────────────────────────────────────────────────
        var rmks = TlrCollectRmks(lines, dataIdx);
        var (thrust, flex, surface, notes) = TlrParseRmks(rmks);

        // ── Format main view ──────────────────────────────────────────────
        string apt = TlrV(vals, "APT");
        string rwy = TlrV(vals, "PRWY");
        // Repair speeds: if column parser gave < 100, scan the data line for a 3-digit number
        string v1 = TlrRepairSpeed(TlrV(vals, "V1"), lines[hdrIdx], lines[dataIdx], "V1");
        string vr = TlrRepairSpeed(TlrV(vals, "VR"), lines[hdrIdx], lines[dataIdx], "VR");
        string v2 = TlrRepairSpeed(TlrV(vals, "V2"), lines[hdrIdx], lines[dataIdx], "V2");
        var sb = new StringBuilder();
        sb.AppendLine($"PLANNED TAKEOFF  {apt} Runway {rwy}");
        sb.AppendLine(new string('─', 38));
        TlrRow(sb, "V1",              v1,  "kt");
        TlrRow(sb, "Rotation speed",  vr,  "kt");
        TlrRow(sb, "V2",              v2,  "kt");
        TlrRow(sb, "Flaps",           TlrV(vals, "FLP"));
        if (!string.IsNullOrEmpty(thrust))
            TlrRow(sb, "Thrust setting",
                string.IsNullOrEmpty(flex) ? thrust : $"{thrust} / {flex}°C");
        TlrRow(sb, "Runway surface",  string.IsNullOrEmpty(surface) ? "" : surface);
        TlrRow(sb, "Wind",            TlrFmtWind(TlrV(vals, "PWIND")));
        TlrRow(sb, "OAT",             TlrV(vals, "POAT"),  "°C");
        TlrRow(sb, "QNH",             TlrV(vals, "PQNH"),  " hPa");
        TlrRow(sb, "Limiting factor", TlrFmtLimit(TlrV(vals, "LIMIT")));
        if (notes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Notes:");
            foreach (string n in notes) sb.AppendLine("  " + n);
        }

        // ── Additional info ────────────────────────────────────────────────
        if (expanded)
        {
            sb.AppendLine();
            sb.AppendLine("─── Additional Info ──────────────────");
            var (rwyLen, maxTowRwy) = TlrAcarsRunway(lines, rwy, isTakeoff: true);
            if (!string.IsNullOrEmpty(rwyLen))
                TlrRow(sb, "Runway length (TORA)", TlrFmtFt(rwyLen));
            // ASDA: SimBrief TLR doesn't separate TORA/TODA/ASDA; show runway length
            string ptow  = TlrV(vals, "PTOW");
            string pmrtw = TlrV(vals, "PMRTW");
            if (!string.IsNullOrEmpty(maxTowRwy))
                TlrRow(sb, "Max TOW (this runway)",  TlrFmtWt(maxTowRwy, units));
            if (!string.IsNullOrEmpty(ptow))
                TlrRow(sb, "Planned TOW",             TlrFmtWt(ptow, units));
            if (!string.IsNullOrEmpty(pmrtw))
                TlrRow(sb, "Max regulated TOW",       TlrFmtWt(pmrtw, units));
            TlrRow(sb, "Limiting factor",             TlrFmtLimit(TlrV(vals, "LIMIT")));
        }

        return sb.ToString().TrimEnd();
    }

    private string FormatTlrLanding(bool expanded)
    {
        if (_ofp == null || string.IsNullOrEmpty(_ofp.TlrText)) return "";
        string tlr  = _ofp.TlrText;
        string units = _ofp.Units;

        int secStart = tlr.IndexOf("LANDING DATA", StringComparison.OrdinalIgnoreCase);
        if (secStart < 0) return "";
        string section = tlr[secStart..];
        string[] lines = section.Split('\n');

        int hdrIdx = TlrFindLine(lines, "APT", "PRWY", "FLP");
        if (hdrIdx < 0) return "";
        int dataIdx = TlrNextDataLine(lines, hdrIdx, _ofp.DestIcao);
        if (dataIdx < 0) return "";

        var vals = TlrMapCols(lines[hdrIdx], lines[dataIdx]);
        var rmks = TlrCollectRmks(lines, dataIdx);
        var (_, __, surface, notes) = TlrParseRmks(rmks);

        // Landing distance table lives in the whole TLR text
        var dist = TlrParseLdgDistTable(tlr);

        string apt = TlrV(vals, "APT");
        string rwy = TlrV(vals, "PRWY");
        var sb = new StringBuilder();
        sb.AppendLine($"PLANNED LANDING  {apt} Runway {rwy}");
        sb.AppendLine(new string('─', 38));

        // VREF from dist table (marked row); fallback to XML model
        string vref = !string.IsNullOrEmpty(dist.Vref) ? dist.Vref
                    : !string.IsNullOrEmpty(_ofp.LandingVapp) ? _ofp.LandingVapp
                    : "";
        TlrRow(sb, "Reference speed (VREF)", vref, "kt");

        string flaps = !string.IsNullOrEmpty(dist.Flaps) ? dist.Flaps : TlrV(vals, "FLP");
        TlrRow(sb, "Flaps",            flaps);
        if (!string.IsNullOrEmpty(dist.BrakeSetting))
            TlrRow(sb, "Brakes",       dist.BrakeSetting);
        TlrRow(sb, "Runway surface",   string.IsNullOrEmpty(surface) ? "" : surface);
        TlrRow(sb, "Wind",             TlrFmtWind(TlrV(vals, "PWIND")));
        TlrRow(sb, "OAT",              TlrV(vals, "POAT"), "°C");
        TlrRow(sb, "QNH",              TlrV(vals, "PQNH"), " hPa");
        TlrRow(sb, "Planned landing wt", TlrFmtWt(TlrV(vals, "PLDW"), units));
        TlrRow(sb, "Limiting factor",  TlrFmtLimit(TlrV(vals, "LIMIT")));
        if (notes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Notes:");
            foreach (string n in notes) sb.AppendLine("  " + n);
        }

        if (expanded)
        {
            sb.AppendLine();
            sb.AppendLine("─── Additional Info ──────────────────");
            var (rwyLen, maxLw) = TlrAcarsRunway(lines, rwy, isTakeoff: false);
            if (!string.IsNullOrEmpty(rwyLen))
                TlrRow(sb, "Landing dist available", TlrFmtFt(rwyLen));
            if (!string.IsNullOrEmpty(dist.FactDry))
                TlrRow(sb, "Required (factored dry)", TlrFmtFt(dist.FactDry));
            if (!string.IsNullOrEmpty(dist.FactWet))
                TlrRow(sb, "Required (factored wet)", TlrFmtFt(dist.FactWet));
            if (!string.IsNullOrEmpty(dist.ActDry))
                TlrRow(sb, "Actual dry distance",    TlrFmtFt(dist.ActDry));
            if (!string.IsNullOrEmpty(dist.ActWet))
                TlrRow(sb, "Actual wet distance",    TlrFmtFt(dist.ActWet));
            if (!string.IsNullOrEmpty(maxLw))
                TlrRow(sb, "Max regulated LW",       TlrFmtWt(maxLw, units));
        }

        return sb.ToString().TrimEnd();
    }

    // ─── TLR parsing helpers ──────────────────────────────────────────────────

    // Index of first line containing ALL required tokens (case-insensitive)
    private static int TlrFindLine(string[] lines, params string[] tokens)
    {
        for (int i = 0; i < lines.Length; i++)
            if (tokens.All(t => lines[i].IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                return i;
        return -1;
    }

    // First data line after hdrIdx that begins with airportIcao (or any non-separator line if icao is blank)
    private static int TlrNextDataLine(string[] lines, int hdrIdx, string icao)
    {
        for (int i = hdrIdx + 1; i < Math.Min(hdrIdx + 6, lines.Length); i++)
        {
            string t = lines[i].TrimStart();
            if (string.IsNullOrWhiteSpace(t) || t.StartsWith("---") || t.StartsWith("===")) continue;
            string tok = t.Split(new[] { ' ', '\t' }, 2)[0];
            if (string.IsNullOrEmpty(icao) || tok.Equals(icao, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    // Collect non-blank, non-separator lines after dataIdx until first separator
    private static List<string> TlrCollectRmks(string[] lines, int dataIdx)
    {
        var result = new List<string>();
        for (int i = dataIdx + 1; i < lines.Length; i++)
        {
            string t = lines[i].TrimStart();
            if (t.StartsWith("---") || t.StartsWith("===")) break;
            if (string.IsNullOrWhiteSpace(t)) { if (result.Count > 0) break; continue; }
            // Strip "RMKS" keyword if present
            string cleaned = System.Text.RegularExpressions.Regex
                .Replace(t, @"^RMKS\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Trim();
            if (!string.IsNullOrEmpty(cleaned)) result.Add(cleaned);
        }
        return result;
    }

    // Extract thrust setting, flex temperature, runway surface and other notes from RMKS list
    private static (string thrust, string flex, string surface, List<string> other)
        TlrParseRmks(List<string> rmks)
    {
        string thrust = "", flex = "", surface = "";
        var other = new List<string>();

        foreach (string r in rmks)
        {
            string up = r.ToUpperInvariant();

            // Surface condition
            if (up.Contains("WET RUNWAY") || up == "WET") { surface = "Wet"; continue; }
            if (up.Contains("DRY RUNWAY") || up == "DRY") { surface = "Dry"; continue; }
            if (up.Contains("CONTAMINATED"))               { surface = "Contaminated"; continue; }
            if (up.Contains("SNOW") || up.Contains("ICE")) { surface = "Snow/ice"; continue; }

            // Thrust/flex: e.g. "D-TO - SEL TEMP 49" or "TO" or "FLEX 49"
            bool looksLikeThrust = up.StartsWith("D-TO") || up.StartsWith("D-T")
                || up.StartsWith("TOGA") || up.StartsWith("MCT") || up.StartsWith("FLEX")
                || up.StartsWith("TO ");
            if (string.IsNullOrEmpty(thrust) && looksLikeThrust)
            {
                var parts = r.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                thrust = parts[0].Trim();
                foreach (var p in parts.Skip(1))
                {
                    var sm = System.Text.RegularExpressions.Regex.Match(p,
                        @"SEL\s*TEMP\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (sm.Success) { flex = sm.Groups[1].Value; continue; }
                    var fm = System.Text.RegularExpressions.Regex.Match(p,
                        @"FLEX\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (fm.Success) { flex = fm.Groups[1].Value; continue; }
                    if (!string.IsNullOrWhiteSpace(p)) other.Add(p.Trim());
                }
                continue;
            }

            other.Add(r);
        }
        return (thrust, flex, surface, other);
    }

    // Find planned runway row in the ACARS RUNWAYS table within the given section lines
    private static (string length, string maxWeight) TlrAcarsRunway(string[] lines, string runway, bool isTakeoff)
    {
        string wtCol = isTakeoff ? "PMTOW" : "PMRLW";
        // Find ACARS RUNWAYS marker
        int ai = -1;
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].IndexOf("ACARS RUNWAYS", StringComparison.OrdinalIgnoreCase) >= 0)
            { ai = i; break; }
        if (ai < 0) return ("", "");

        // Find header row (has RWY and LENGTH) within next 3 lines
        int hi = -1;
        for (int i = ai + 1; i < Math.Min(ai + 4, lines.Length); i++)
            if (lines[i].IndexOf("RWY", StringComparison.OrdinalIgnoreCase) >= 0 &&
                lines[i].IndexOf("LENGTH", StringComparison.OrdinalIgnoreCase) >= 0)
            { hi = i; break; }
        if (hi < 0) return ("", "");

        string hdr = lines[hi];
        for (int i = hi + 1; i < Math.Min(hi + 20, lines.Length); i++)
        {
            string t = lines[i].TrimStart();
            if (t.StartsWith("---") || t.StartsWith("===")) break;
            if (string.IsNullOrWhiteSpace(t)) continue;
            string tok = t.Split(new[] { ' ', '\t' }, 2)[0];
            if (tok.Equals(runway, StringComparison.OrdinalIgnoreCase))
            {
                var v = TlrMapCols(hdr, lines[i]);
                return (TlrV(v, "LENGTH"), TlrV(v, wtCol));
            }
        }
        return ("", "");
    }

    private readonly record struct LdgDist(
        string Flaps, string BrakeSetting,
        string Vref, string ActDry, string ActWet, string FactDry, string FactWet);

    // Parse the LANDING DISTANCE table to get VREF and distances for the planned landing weight (marked with /)
    private static LdgDist TlrParseLdgDistTable(string tlrText)
    {
        int idx = tlrText.IndexOf("LANDING DISTANCE", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return default;

        // Extract header line (first line of this section)
        int nl = tlrText.IndexOf('\n', idx);
        string hdrLine = nl > idx ? tlrText[idx..nl] : tlrText[idx..];

        // Flap setting
        string flaps = "";
        var fm = System.Text.RegularExpressions.Regex.Match(hdrLine, @"FLAPS?\s+(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (fm.Success) flaps = fm.Groups[1].Value;

        // Brake setting — check most-specific patterns first
        string brakes = "";
        // A/B 3 (e.g. "A/B 3 MAX MANUAL")
        var abm = System.Text.RegularExpressions.Regex.Match(hdrLine,
            @"A/B\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (abm.Success)
            brakes = $"A/B {abm.Groups[1].Value}";
        else
        {
            var bm = System.Text.RegularExpressions.Regex.Match(hdrLine,
                @"AUTO[\s-]?BRAKE[S]?\s*(\w+)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (bm.Success)
                brakes = bm.Groups[1].Success ? $"Autobrake {bm.Groups[1].Value}" : "Autobrake";
            else if (hdrLine.IndexOf("MAX MANUAL", StringComparison.OrdinalIgnoreCase) >= 0)
                brakes = "Max manual";
        }

        string[] lines = tlrText[idx..].Split('\n');

        // Find column header row: has LDW and VREF
        int colHdr = TlrFindLine(lines, "LDW", "VREF");
        if (colHdr < 0) return new LdgDist(flaps, brakes, "", "", "", "", "");

        string colLine = lines[colHdr];

        // Find the row marked with "/" (planned landing weight)
        for (int i = colHdr + 1; i < Math.Min(colHdr + 30, lines.Length); i++)
        {
            string raw = lines[i];
            string t   = raw.TrimStart();
            if (t.StartsWith("---") || t.StartsWith("===")) break;
            if (!t.StartsWith("/")) continue;

            // Replace "/" with " " to allow fixed-width column matching
            string dataLine = raw.Replace('/', ' ');
            var ordered = TlrMapColsOrdered(colLine, dataLine);

            // Column order: LDW, VREF, DRY(act), WET(act), DRY(fact), WET(fact)
            string G(int pos) => ordered.Count > pos ? ordered[pos].Value : "";
            return new LdgDist(flaps, brakes, G(1), G(2), G(3), G(4), G(5));
        }
        return new LdgDist(flaps, brakes, "", "", "", "", "");
    }

    // Map header token positions → data values (unique keys; fixed-width with token fallback)
    private static Dictionary<string, string> TlrMapCols(string hdr, string data)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ms = System.Text.RegularExpressions.Regex.Matches(hdr, @"\S+");
        for (int i = 0; i < ms.Count; i++)
        {
            int s = ms[i].Index;
            int e = i + 1 < ms.Count ? ms[i + 1].Index : data.Length + 1;
            if (s >= data.Length) continue;
            string val = TlrSliceToken(data, s, e);
            if (!string.IsNullOrEmpty(val)) d[ms[i].Value] = val;
        }
        if (d.Count < ms.Count / 2)  // Fallback: token-by-token
        {
            d.Clear();
            string[] toks = data.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < Math.Min(ms.Count, toks.Length); i++)
                d[ms[i].Value] = toks[i];
        }
        return d;
    }

    // Like TlrMapCols but preserves order and allows duplicate column names (for landing dist table)
    private static List<(string Name, string Value)> TlrMapColsOrdered(string hdr, string data)
    {
        var result = new List<(string, string)>();
        var ms = System.Text.RegularExpressions.Regex.Matches(hdr, @"\S+");
        for (int i = 0; i < ms.Count; i++)
        {
            int s = ms[i].Index;
            int e = i + 1 < ms.Count ? ms[i + 1].Index : data.Length + 1;
            if (s >= data.Length) continue;
            result.Add((ms[i].Value, TlrSliceToken(data, s, e)));
        }
        if (result.Count < ms.Count / 2)
        {
            result.Clear();
            string[] toks = data.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < Math.Min(ms.Count, toks.Length); i++)
                result.Add((ms[i].Value, toks[i]));
        }
        return result;
    }

    /// <summary>
    /// Extracts a single token from a fixed-width data line given the column's [s, e) range.
    /// Walks back from s to capture right-justified leading digits, then takes only the first
    /// whitespace-separated token from the slice (avoids bleeding into the next column).
    /// </summary>
    private static string TlrSliceToken(string data, int s, int e)
    {
        // Walk back from s over any digits that belong to a right-justified number
        int actualS = s;
        while (actualS > 0 && (actualS - 1) < data.Length && char.IsDigit(data[actualS - 1]))
            actualS--;

        string slice = data[actualS..Math.Min(e, data.Length)];
        // Return only the first whitespace-delimited token (prevents bleeding into next col)
        return slice.TrimStart()
                    .Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? "";
    }

    // ─── TLR formatting micro-helpers ─────────────────────────────────────────

    private static string TlrV(Dictionary<string, string> d, string key) =>
        d.TryGetValue(key, out string? v) ? v : "";

    /// <summary>
    /// SimBrief TLR abbreviates V-speeds as 2 digits (e.g. "47" meaning "147").
    /// If the parsed speed is &lt;100, prepend "1" since all jet takeoff/landing speeds are 1xx kt.
    /// </summary>
    private static string TlrRepairSpeed(string value, string hdr, string data, string colName)
    {
        if (!int.TryParse(value, out int v) || v >= 100) return value;
        return v > 0 ? "1" + value : value;  // "47" → "147", "53" → "153"
    }

    private static void TlrRow(StringBuilder sb, string label, string value, string suffix = "")
    {
        if (string.IsNullOrEmpty(value)) return;
        sb.AppendLine($"{(label + ":").PadRight(26)} {value}{suffix}");
    }

    private static string TlrFmtWind(string wind)
    {
        if (string.IsNullOrEmpty(wind)) return "";
        var m = System.Text.RegularExpressions.Regex.Match(wind, @"^(\d{3})[MT](\d+)$");
        if (m.Success) return $"{m.Groups[1].Value}°/{m.Groups[2].Value}kt";
        // "CALM" passes through unchanged
        return wind;
    }

    private static string TlrFmtLimit(string code) =>
        code.ToUpperInvariant() switch
        {
            "FLD"  => $"Field length (FLD)",
            "CLB"  => $"Climb (CLB)",
            "OBS"  => $"Obstacle clearance (OBS)",
            "OBW"  => $"Obstacle clearance (OBW)",
            "AFM"  => $"Aircraft flight manual (AFM)",
            "VMCG" => $"Min control speed ground (VMCG)",
            "TYRE" => $"Tyre speed (TYRE)",
            "BRK"  => $"Brake energy (BRK)",
            ""     => "",
            _      => code
        };

    // TLR weight formatting: some OFP layouts (e.g. LIDO) store weights in units of 100
    // (e.g. "1870" = 187,000 lbs), while others (e.g. RYR) store the full value (e.g. "187000").
    // Heuristic: values < 10,000 are in units of 100 and need × 100; values ≥ 10,000 are full.
    private static string TlrFmtWt(string rawWt, string units)
    {
        if (string.IsNullOrEmpty(rawWt)) return "";
        if (!int.TryParse(rawWt, out int v) || v <= 0) return $"{rawWt} {units}";
        long displayed = v < 10_000 ? (long)v * 100 : v;
        return $"{displayed:N0} {units}";
    }

    private static string TlrFmtFt(string rawFt)
    {
        if (string.IsNullOrEmpty(rawFt)) return "";
        if (!int.TryParse(rawFt, out int v)) return $"{rawFt} ft";
        // SimBrief ACARS RUNWAYS table uses metres for European OFP formats.
        // Commercial runways: 1500–4800 m, or 5000–15750 ft.
        // A value < 5000 is almost certainly metres — convert to feet.
        if (v > 0 && v < 5000)
            return $"{(int)Math.Round(v * 3.28084):N0} ft";
        return $"{v:N0} ft";
    }

    private static string BuildWeatherStationText(string heading, string icao, string metar, string taf)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{heading}: {icao}");
        sb.AppendLine(new string('─', 30));
        if (!string.IsNullOrEmpty(metar))
        {
            sb.AppendLine("METAR:");
            sb.AppendLine(metar);
        }
        if (!string.IsNullOrEmpty(taf))
        {
            if (!string.IsNullOrEmpty(metar)) sb.AppendLine();
            sb.AppendLine("TAF:");
            sb.AppendLine(taf);
        }
        if (string.IsNullOrEmpty(metar) && string.IsNullOrEmpty(taf))
            sb.AppendLine("No weather data available.");
        return sb.ToString().TrimEnd();
    }

    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Populates a ListBox with lines split from a multi-line string.
    /// Each \n-delimited line becomes one item.
    /// </summary>
    private static void SetListText(ListBox lb, string text)
    {
        lb.BeginUpdate();
        lb.Items.Clear();
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            // Skip blank lines and pure-separator lines (─, -, =, etc.) that NVDA reads as silence
            if (trimmed.Any(char.IsLetterOrDigit))
                lb.Items.Add(trimmed);
        }
        lb.EndUpdate();
    }

    /// <summary>
    /// Scrolls a ListBox to the first item whose text contains sectionText
    /// (skipping one line past the header), so NVDA starts reading from there.
    /// </summary>
    private static void FocusListBoxAtSection(ListBox lb, string sectionText)
    {
        int idx = -1;
        for (int i = 0; i < lb.Items.Count; i++)
        {
            string item = lb.Items[i]?.ToString() ?? "";
            if (item.IndexOf(sectionText, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Start one line after the section header
                idx = Math.Min(i + 1, lb.Items.Count - 1);
                break;
            }
        }
        if (idx >= 0)
        {
            lb.SelectedIndex = idx;
            lb.TopIndex      = idx;
        }
        lb.Focus();
    }

    // Formatting helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Builds a formatted section with label: value rows.</summary>
    private static string Section(string title, params (string Label, string Value)[] rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine(new string('─', Math.Max(title.Length, 20)));
        foreach (var (label, value) in rows)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sb.AppendLine($"{(label + ":").PadRight(22)} {value}");
        }
        return sb.ToString();
    }

    private static string Wt(string raw, string units)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        // SimBrief returns fuel/weight in lbs; format with thousands separator
        if (int.TryParse(raw, out int v))
            return $"{v:N0} {units}";
        return $"{raw} {units}";
    }

    private static string Kt(string raw) =>
        string.IsNullOrEmpty(raw) ? "" : $"{raw}kt";

    // Formats an altitude string for the nav log.
    // altitude_feet values (>=1000, e.g. 7000 or 34000) are divided by 100 → FL070/FL340.
    // flight_level fallback values (<1000, e.g. 280 or 70) are used directly → FL280/FL070.
    private static string FormatNavAlt(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "-";
        if (!int.TryParse(raw, out int v) || v == 0) return "-";
        if (v >= 1000) return $"FL{v / 100:D3}";  // altitude_feet: 7000→FL070, 34000→FL340
        return $"FL{v:D3}";                          // flight_level:   280→FL280,   70→FL070
    }

    // Trans Alt: raw feet value (e.g. "05000") → "5,000 ft"
    private static string FormatTransFt(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        return int.TryParse(raw, out int v) && v > 0 ? $"{v:N0} ft" : "";
    }

    // Trans Level: raw feet value (e.g. "06000") → "FL060"  (divide by 100)
    private static string FormatTransLevel(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        return int.TryParse(raw, out int v) && v > 0 ? $"FL{v / 100:D3}" : "";
    }

    // Avg wind component: SimBrief convention — negative = headwind, positive = tailwind.
    // Uses double parsing (invariant culture) so decimal values like "-20.5" are handled.
    private static string FormatAvgWind(string comp)
    {
        if (string.IsNullOrEmpty(comp)) return "";
        if (!double.TryParse(comp,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double d))
            return "";   // unparseable — omit rather than show raw garbage
        int kt = (int)Math.Round(Math.Abs(d));
        if (kt == 0) return "calm";
        return d < 0 ? $"{kt}kt headwind" : $"{kt}kt tailwind";
    }

    // Landing distances from SimBrief XML are in metres — convert to feet
    private static string MetresToFt(string metres)
    {
        if (string.IsNullOrEmpty(metres)) return "";
        return int.TryParse(metres, out int m) && m > 0
            ? $"{(int)Math.Round(m * 3.28084):N0} ft"
            : "";
    }

    private static string Nm(string raw) =>
        string.IsNullOrEmpty(raw) ? "" : $"{raw} NM";

    private static string FormatAirTime(string minutes)
    {
        if (string.IsNullOrEmpty(minutes)) return "";
        if (int.TryParse(minutes, out int m))
            return $"{m / 60}h {m % 60:D2}m";
        return minutes;
    }

    private static string FormatMinutes(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        if (int.TryParse(raw, out int m))
            return $"{m / 60:D2}:{m % 60:D2}";
        return raw;
    }

    /// <summary>Formats elapsed seconds as "H:MM" (e.g. 5040 → "1:24"). Returns "" if invalid.</summary>
    private static string FormatElapsed(string raw)
    {
        if (!int.TryParse(raw, out int secs) || secs <= 0) return "";
        int h = secs / 3600;
        int m = (secs % 3600) / 60;
        return h > 0 ? $"{h}:{m:D2}" : $"{m} min";
    }

    // ─────────────────────────────────────────────────────────────────────────
    private void OnFirstShown(object? sender, EventArgs e)
    {
        Shown -= OnFirstShown;
        // Apply min sizes and preferred splitter position now that the control has a real width.
        try
        {
            _overviewSplit.Panel1MinSize = 140;
            _overviewSplit.Panel2MinSize = 200;
            int max = _overviewSplit.Width - 200 - _overviewSplit.SplitterWidth;
            if (max > 140)
                _overviewSplit.SplitterDistance = Math.Clamp(200, 140, max);
        }
        catch { /* leave at default if anything goes wrong */ }
    }

    // Public show helper (keeps same instance across open/close like EFB form)
    // ─────────────────────────────────────────────────────────────────────────

    public void ShowPlanner(int tabIndex = 0)
    {
        _outerTabs.SelectedIndex = Math.Clamp(tabIndex, 0, _outerTabs.TabPages.Count - 1);
        Show();
        BringToFront();
        Activate();

        // Fallback: if somehow arrived here without a fetch running (e.g. direct call),
        // kick one off. BeginAutoFetch() from OpenSimbriefPlanner is the primary trigger.
        if (tabIndex == 1 && !_fetchInProgress && _ofp == null)
            _ = FetchPlanAsync();
    }
}
