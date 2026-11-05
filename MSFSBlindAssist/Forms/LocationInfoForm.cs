using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Controls;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services.Geography;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// The Location Info window (Output > Shift+L), answered from the offline location data only (LOC-1).
/// From the top: "Where you are" (focused on opening, so the screen reader reads it; the window speaks
/// nothing itself, LOC-8), the places as one table nearest first, and the filters behind a show/hide
/// button. Everything is measured from the map position: the aircraft while "Map position follows the
/// aircraft" is ticked, else a place picked with "Set as map position". The filters, range, search and
/// map position are remembered between openings and sessions. Nothing refreshes by itself, so the list
/// never moves under the pilot; F5 or Refresh reads the aircraft's position again. Design:
/// docs/design/2026-11-05-location-info-redesign.md.
/// </summary>
public partial class LocationInfoForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>The most places a whole-world name search keeps: the nearest to the map position.</summary>
    private const int WorldSearchMax = 2000;

    private readonly OfflineLocationService _service;
    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly WaypointTracker _tracker;
    private readonly IntPtr _previousWindow;
    private readonly UserSettings _settings = SettingsManager.Current;
    private readonly bool _kilometres;

    private LocationReport _aircraftReport;
    private double _aircraftLat, _aircraftLon;
    private IReadOnlyList<Models.NearbyPlace> _majorCities = Array.Empty<Models.NearbyPlace>();

    // The loaded places, and which of them pass the filters (indices, nearest first).
    private IReadOnlyList<PlaceResult> _places = Array.Empty<PlaceResult>();
    private List<int> _shown = new();
    private double _loadedLat, _loadedLon, _loadedRadiusKm = -1;
    private bool _loadedWorld;
    private string _loadedWorldQuery = "";
    private int _worldTotal;
    private bool _loading;
    private CancellationTokenSource? _loadCts;

    private AirportMatcher? _airports;
    private readonly Dictionary<int, AirportSearchEntry?> _airportMatches = new();

    private readonly List<(string Header, int Width, Func<PlaceResult, string> Text)> _columns = new();
    private bool _updatingControls;

    private TextBox _whereBox = null!;
    private ListView _results = null!;
    private TextBox _statusBox = null!;
    private Button _filtersButton = null!;
    private TableLayoutPanel _filtersPanel = null!;
    private TextBox _searchBox = null!;
    private ComboBox _scopeCombo = null!;
    private NumericUpDown _rangeBox = null!;
    private ComboBox _directionCombo = null!;
    private NativeAccessibleTreeView _typeTree = null!;
    private CheckBox _followCheck = null!;
    private Button _resetButton = null!;
    private Button _refreshButton = null!;
    private readonly System.Windows.Forms.Timer _searchDebounce = new() { Interval = 400 };
    private readonly System.Windows.Forms.Timer _rangeDebounce = new() { Interval = 400 };

    internal LocationInfoForm(OfflineLocationService service, SimConnectManager sim, ScreenReaderAnnouncer announcer,
        WaypointTracker tracker, LocationReport aircraftReport, double aircraftLat, double aircraftLon)
    {
        _previousWindow = GetForegroundWindow();
        _service = service;
        _sim = sim;
        _announcer = announcer;
        _tracker = tracker;
        _aircraftReport = aircraftReport;
        _aircraftLat = aircraftLat;
        _aircraftLon = aircraftLon;
        _kilometres = _settings.DistanceUnits == "kilometers";
        _majorCities = aircraftReport.Details?.MajorCities ?? new List<Models.NearbyPlace>();
        InitializeComponent();
        LoadFilterControls();
        BuildColumns();
        UpdateWhereBox();
    }

    // ---- the map position ----

    private bool Following => _settings.LocationInfoFollowAircraft;
    private double MapLat => Following ? _aircraftLat : _settings.LocationInfoMapLatitude;
    private double MapLon => Following ? _aircraftLon : _settings.LocationInfoMapLongitude;
    private string UnitWord => LocationPhrases.UnitWord(_kilometres);
    private double RangeKm => _kilometres ? (double)_rangeBox.Value : (double)_rangeBox.Value * LocationQuerySettings.KmPerMile;
    private bool WholeWorld => _scopeCombo.SelectedIndex == 1;

    // ---- layout ----

    private void InitializeComponent()
    {
        SuspendLayout();
        Text = "Location Information";
        Size = new Size(1000, 760);
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = true;
        TopMost = true;
        KeyPreview = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(6) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140)); // where you are
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // results
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // status
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // show/hide filters
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // filters
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // buttons

        _whereBox = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            AccessibleName = "Where you are", TabIndex = 0,
        };

        _results = new ListView
        {
            View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false,
            VirtualMode = true, Dock = DockStyle.Fill, AccessibleName = "Places", TabIndex = 1,
        };
        _results.RetrieveVirtualItem += Results_RetrieveVirtualItem;
        _results.ContextMenuStrip = BuildResultsMenu();

        _statusBox = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, AccessibleName = "Search status", TabIndex = 2 };

        _filtersButton = new Button { AutoSize = true, TabIndex = 3 };
        _filtersButton.Click += (_, _) => SetFiltersShown(!_filtersPanel.Visible);

        _filtersPanel = BuildFiltersPanel();
        _filtersPanel.TabIndex = 4;

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, TabIndex = 5 };
        _resetButton = new Button { Text = "Reset filters", AutoSize = true, TabIndex = 0 };
        _resetButton.Click += (_, _) => ResetFilters();
        _refreshButton = new Button { Text = "Refresh (F5)", AutoSize = true, AccessibleName = "Refresh", TabIndex = 1 };
        _refreshButton.Click += (_, _) => RefreshFromAircraft();
        buttons.Controls.AddRange(new Control[] { _resetButton, _refreshButton });

        layout.Controls.Add(_whereBox, 0, 0);
        layout.Controls.Add(_results, 0, 1);
        layout.Controls.Add(_statusBox, 0, 2);
        layout.Controls.Add(_filtersButton, 0, 3);
        layout.Controls.Add(_filtersPanel, 0, 4);
        layout.Controls.Add(buttons, 0, 5);
        Controls.Add(layout);

        KeyDown += Form_KeyDown;
        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); OnSearchSettled(); };
        _rangeDebounce.Tick += (_, _) => { _rangeDebounce.Stop(); Reload(); };
        Shown += (_, _) =>
        {
            Activate();
            TopMost = true;
            TopMost = false;
            _whereBox.Focus();
            _whereBox.Select(0, 0);
            Reload();
        };
        ResumeLayout(false);
    }

    private TableLayoutPanel BuildFiltersPanel()
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int tab = 0;
        void Row(string label, Control control)
        {
            control.TabIndex = tab++;
            if (label.Length > 0) control.AccessibleName ??= label.TrimEnd(':');
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, panel.RowCount);
            panel.Controls.Add(control, 1, panel.RowCount);
            panel.RowCount++;
        }

        _searchBox = new TextBox { Width = 300 };
        _searchBox.TextChanged += (_, _) => { if (_updatingControls) return; _searchDebounce.Stop(); _searchDebounce.Start(); };
        _searchBox.KeyDown += SearchBox_KeyDown;
        Row("Search by name:", _searchBox);

        _scopeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        _scopeCombo.Items.AddRange(new object[] { "Around the map position", "Whole world, by name" });
        _scopeCombo.SelectedIndexChanged += (_, _) => { if (!_updatingControls) Reload(); };
        Row("Search where:", _scopeCombo);

        _rangeBox = new NumericUpDown { Minimum = 1, Maximum = LocationQuerySettings.MaxRange(_kilometres), Width = 100 };
        _rangeBox.ValueChanged += (_, _) => { if (_updatingControls) return; _rangeDebounce.Stop(); _rangeDebounce.Start(); };
        Row($"Range in {LocationPhrases.UnitWord(_kilometres)}:", _rangeBox);

        _directionCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        _directionCombo.Items.Add("All directions");
        foreach (var d in PlaceFilter.Directions) _directionCombo.Items.Add(d);
        _directionCombo.SelectedIndexChanged += (_, _) => { if (!_updatingControls) ApplyFilters(); };
        Row("Direction from the map position:", _directionCombo);

        _typeTree = new NativeAccessibleTreeView { CheckBoxes = true, Height = 220, Width = 400 };
        _typeTree.AfterCheck += TypeTree_AfterCheck;
        _typeTree.ContextMenuStrip = BuildTypeMenu();
        Row("Kinds of place:", _typeTree);

        _followCheck = new CheckBox { Text = "Map position follows the aircraft", AutoSize = true };
        _followCheck.CheckedChanged += FollowCheck_CheckedChanged;
        Row("", _followCheck);
        return panel;
    }

    private void SetFiltersShown(bool shown)
    {
        _filtersPanel.Visible = shown;
        _filtersButton.Text = shown ? "Hide filters and search" : "Show filters and search";
        _settings.LocationInfoFiltersShown = shown;
    }

    // ---- columns ----

    /// <summary>The table's columns. "From aircraft" is left out while the map position is the aircraft,
    /// since it would repeat "From map position".</summary>
    private void BuildColumns()
    {
        _columns.Clear();
        _columns.Add(("Name", 220, p => p.Name));
        _columns.Add(("Type", 150, p => p.TypeName));
        _columns.Add(("Region", 200, p => LocationPhrases.Region(p.State, p.Country)));
        _columns.Add(("From map position", 150, p => FromPoint(MapLat, MapLon, p)));
        if (!Following) _columns.Add(("From aircraft", 150, p => FromPoint(_aircraftLat, _aircraftLon, p)));
        _columns.Add(("Details", 180, Details));
        _columns.Add(("Coordinates", 160, p => LocationPhrases.Coordinates(p.Lat, p.Lon)));

        _results.BeginUpdate();
        _results.Columns.Clear();
        foreach (var (header, width, _) in _columns) _results.Columns.Add(header, width);
        _results.EndUpdate();
    }

    private string FromPoint(double lat, double lon, PlaceResult p) => LocationPhrases.DistanceAndDirection(
        LocationGeometry.GreatCircleNm(lat, lon, p.Lat, p.Lon), LocationGeometry.BearingTrue(lat, lon, p.Lat, p.Lon), _kilometres);

    private string Details(PlaceResult p)
    {
        string? ident = null;
        if (p.Category == LocationCategory.Airport)
        {
            if (!_airportMatches.TryGetValue(p.Id, out var match))
                _airportMatches[p.Id] = match = _airports?.Match(p.Name, p.AlternateNames, p.Lat, p.Lon);
            if (_airports is null) return "";
            ident = match?.Ident;
        }
        return LocationPhrases.PlaceDetails(p.Category, p.Population, ident);
    }

    private void Results_RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _shown.Count)
        {
            e.Item = new ListViewItem(new string[_columns.Count]);
            return;
        }
        var place = _places[_shown[e.ItemIndex]];
        e.Item = new ListViewItem(_columns.Select(c => c.Text(place)).ToArray());
    }

    private PlaceResult? SelectedPlace =>
        _results.SelectedIndices.Count > 0 && _results.SelectedIndices[0] < _shown.Count
            ? _places[_shown[_results.SelectedIndices[0]]]
            : null;

    // ---- filters ----

    private void LoadFilterControls()
    {
        _updatingControls = true;
        try
        {
            _searchBox.Text = _settings.LocationInfoSearch;
            _scopeCombo.SelectedIndex = _settings.LocationInfoWholeWorld ? 1 : 0;
            _rangeBox.Value = Math.Clamp(_settings.LocationInfoRange, (int)_rangeBox.Minimum, (int)_rangeBox.Maximum);
            int direction = PlaceFilter.Directions.ToList().IndexOf(_settings.LocationInfoDirection);
            _directionCombo.SelectedIndex = direction + 1;
            _followCheck.Checked = Following;
            BuildTypeTree();
        }
        finally { _updatingControls = false; }
        SetFiltersShown(_settings.LocationInfoFiltersShown);
    }

    private void SaveFilterSettings()
    {
        _settings.LocationInfoSearch = _searchBox.Text;
        _settings.LocationInfoWholeWorld = WholeWorld;
        _settings.LocationInfoRange = (int)_rangeBox.Value;
        _settings.LocationInfoDirection = _directionCombo.SelectedIndex > 0 ? PlaceFilter.Directions[_directionCombo.SelectedIndex - 1] : "";
        _settings.LocationInfoHiddenTypes = HiddenTypes().ToList();
    }

    private void ResetFilters()
    {
        var defaults = new UserSettings();
        _settings.LocationInfoSearch = defaults.LocationInfoSearch;
        _settings.LocationInfoWholeWorld = defaults.LocationInfoWholeWorld;
        _settings.LocationInfoRange = defaults.LocationInfoRange;
        _settings.LocationInfoDirection = defaults.LocationInfoDirection;
        _settings.LocationInfoHiddenTypes = defaults.LocationInfoHiddenTypes;
        LoadFilterControls();
        Reload();
    }

    /// <summary>Typing settled: a whole-world search runs again; around a point the loaded places are filtered.</summary>
    private void OnSearchSettled()
    {
        if (WholeWorld) Reload();
        else ApplyFilters();
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        _searchDebounce.Stop();
        OnSearchSettled();
        // The count is spoken only here, never on each filter change (CORE-7).
        if (!_loading) _announcer.AnnounceImmediate(_statusBox.Text);
        else _speakCountWhenLoaded = true;
    }

    private bool _speakCountWhenLoaded;

    // ---- the kinds-of-place tree ----

    /// <summary>All, then each shown category, then the kinds of place in it, from the database's own list
    /// (<see cref="OfflineLocationService.GetPlaceTypes"/>). Unticked kinds come from the settings.</summary>
    private void BuildTypeTree()
    {
        var types = _service.GetPlaceTypes() ?? Array.Empty<PlaceType>();
        var hidden = _settings.LocationInfoHiddenTypes.ToHashSet(StringComparer.Ordinal);
        _typeTree.BeginUpdate();
        _typeTree.Nodes.Clear();
        var root = new TreeNode("All places");
        foreach (var group in types.GroupBy(t => t.Category).OrderBy(g => g.Key))
        {
            var categoryNode = new TreeNode(LocationFeatureCatalog.Label(group.Key)) { Tag = group.Key };
            foreach (var type in group.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
                categoryNode.Nodes.Add(new TreeNode(type.Name) { Tag = type, Checked = !hidden.Contains(type.Key) });
            categoryNode.Checked = categoryNode.Nodes.Cast<TreeNode>().All(n => n.Checked);
            root.Nodes.Add(categoryNode);
        }
        root.Checked = root.Nodes.Cast<TreeNode>().All(n => n.Checked);
        _typeTree.Nodes.Add(root);
        root.Expand();
        _typeTree.EndUpdate();
    }

    private static IEnumerable<TreeNode> Descendants(TreeNode node) =>
        node.Nodes.Cast<TreeNode>().SelectMany(n => Descendants(n).Prepend(n));

    private IEnumerable<string> HiddenTypes() =>
        _typeTree.Nodes.Cast<TreeNode>().SelectMany(n => Descendants(n).Prepend(n))
            .Where(n => n.Tag is PlaceType && !n.Checked).Select(n => ((PlaceType)n.Tag!).Key);

    /// <summary>Ticking or unticking an item does the same to everything under it; an item above shows
    /// ticked only while everything under it is ticked. Only the kinds of place (the bottom level) decide
    /// what the list shows.</summary>
    private void TypeTree_AfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (e.Action == TreeViewAction.Unknown || e.Node is null) return; // our own writes below
        foreach (var child in Descendants(e.Node)) child.Checked = e.Node.Checked;
        for (var parent = e.Node.Parent; parent is not null; parent = parent.Parent)
            parent.Checked = parent.Nodes.Cast<TreeNode>().All(n => n.Checked);
        ApplyFilters();
    }

    private ContextMenuStrip BuildTypeMenu()
    {
        var menu = new ContextMenuStrip();
        var use = new ToolStripMenuItem("Use for Output C");
        use.Click += (_, _) => UseForOutputC(_typeTree.SelectedNode);
        var reset = new ToolStripMenuItem("Output C back to the nearest town or village");
        reset.Click += (_, _) =>
        {
            _settings.OutputCPlaceTypes = new List<string>();
            SettingsManager.Save();
            _announcer.AnnounceImmediate("Output C names the nearest town or village again.");
        };
        menu.Items.AddRange(new ToolStripItem[] { use, reset });
        return menu;
    }

    /// <summary>Output C names the nearest place of every kind under this item (All: of any kind).</summary>
    private void UseForOutputC(TreeNode? node)
    {
        if (node is null) return;
        var keys = Descendants(node).Prepend(node).Where(n => n.Tag is PlaceType).Select(n => ((PlaceType)n.Tag!).Key).ToList();
        if (keys.Count == 0) return;
        _settings.OutputCPlaceTypes = keys;
        SettingsManager.Save();
        _announcer.AnnounceImmediate($"Output C names the nearest place of the kind {node.Text}.");
    }

    // ---- the results' context menu ----

    private ContextMenuStrip BuildResultsMenu()
    {
        var menu = new ContextMenuStrip();
        var track = new ToolStripMenuItem("Track in slot");
        for (int slot = 1; slot <= _tracker.SlotCount; slot++)
        {
            int s = slot;
            var item = new ToolStripMenuItem($"Slot {s}");
            item.Click += (_, _) => TrackSelected(s);
            track.DropDownItems.Add(item);
        }
        var setMap = new ToolStripMenuItem("Set as map position");
        setMap.Click += (_, _) => SetMapPosition(SelectedPlace);
        var copy = new ToolStripMenuItem("Copy coordinates");
        copy.Click += (_, _) => CopyCoordinates(SelectedPlace);
        menu.Items.AddRange(new ToolStripItem[] { track, setMap, copy });
        menu.Opening += (_, e) => e.Cancel = SelectedPlace is null;
        return menu;
    }

    private void TrackSelected(int slot)
    {
        if (SelectedPlace is not { } p) return;
        _tracker.TrackWaypoint(slot, new WaypointFix
        {
            Ident = p.Name, Name = p.Name, Latitude = p.Lat, Longitude = p.Lon, Section = FlightPlanSection.Enroute,
        });
        _announcer.AnnounceImmediate($"{p.Name} tracked in slot {slot}");
    }

    private void CopyCoordinates(PlaceResult? p)
    {
        if (p is null) return;
        try
        {
            Clipboard.SetText(LocationPhrases.Coordinates(p.Lat, p.Lon));
            _announcer.AnnounceImmediate("Coordinates copied");
        }
        catch (ExternalException ex)
        {
            _announcer.AnnounceImmediate("Could not copy: the clipboard is in use.");
            Log.Warn("LocationInfo", $"Clipboard copy failed: {ex.Message}");
        }
    }

    private void SetMapPosition(PlaceResult? p)
    {
        if (p is null) return;
        _settings.LocationInfoFollowAircraft = false;
        _settings.LocationInfoMapName = p.Name;
        _settings.LocationInfoMapLatitude = p.Lat;
        _settings.LocationInfoMapLongitude = p.Lon;
        _updatingControls = true;
        _followCheck.Checked = false;
        _updatingControls = false;
        _announcer.AnnounceImmediate($"Map position set to {p.Name}.");
        OnMapPositionChanged();
    }

    private void FollowCheck_CheckedChanged(object? sender, EventArgs e)
    {
        if (_updatingControls) return;
        if (_followCheck.Checked) _settings.LocationInfoFollowAircraft = true;
        else
        {
            // Unticked by hand: the map position stays where the aircraft is now.
            _settings.LocationInfoFollowAircraft = false;
            _settings.LocationInfoMapName = "Where the aircraft was";
            _settings.LocationInfoMapLatitude = _aircraftLat;
            _settings.LocationInfoMapLongitude = _aircraftLon;
        }
        OnMapPositionChanged();
    }

    private void OnMapPositionChanged()
    {
        BuildColumns();
        UpdateWhereBox();
        Reload();
    }

    // ---- loading ----

    /// <summary>Reads the places again when the map position, the range (beyond what is loaded), the scope or a
    /// whole-world search changed; otherwise only filters. Off the UI thread; a newer load cancels an older one.</summary>
    private async void Reload()
    {
        SaveFilterSettings();
        // Whatever runs is stale now: cancelled before anything else, so it can never overwrite what follows.
        _loadCts?.Cancel();
        _loading = false;
        bool world = WholeWorld;
        string folded = SearchText.Fold(_searchBox.Text);
        double lat = MapLat, lon = MapLon, radiusKm = RangeKm;
        bool samePoint = Math.Abs(lat - _loadedLat) < 1e-9 && Math.Abs(lon - _loadedLon) < 1e-9;
        if (!world && !_loadedWorld && samePoint && radiusKm <= _loadedRadiusKm)
        {
            ApplyFilters();
            return;
        }
        if (world && _loadedWorld && samePoint && folded == _loadedWorldQuery)
        {
            ApplyFilters();
            return;
        }

        var cts = _loadCts = new CancellationTokenSource();
        _loading = true;
        _statusBox.Text = world && folded.Length == 0 ? WorldPrompt : "Searching...";
        try
        {
            if (world && folded.Length == 0)
            {
                _places = Array.Empty<PlaceResult>();
                _loadedWorld = true;
                _loadedWorldQuery = "";
                _worldTotal = 0;
            }
            else if (world)
            {
                var outcome = await Task.Run(() => _service.SearchWorld(folded, lat, lon, WorldSearchMax, cts.Token));
                if (cts.IsCancellationRequested || IsDisposed) return;
                _places = outcome?.Places ?? Array.Empty<PlaceResult>();
                _worldTotal = outcome?.TotalMatches ?? 0;
                _loadedWorld = true;
                _loadedWorldQuery = folded;
            }
            else
            {
                var places = await Task.Run(() => _service.SearchAround(lat, lon, radiusKm, cts.Token));
                if (cts.IsCancellationRequested || IsDisposed) return;
                _places = places ?? Array.Empty<PlaceResult>();
                _loadedWorld = false;
                _loadedRadiusKm = radiusKm;
            }
            _loadedLat = lat;
            _loadedLon = lon;
            _airportMatches.Clear();
            await EnsureAirportsAsync();
            if (cts.IsCancellationRequested || IsDisposed) return;
            await LoadMajorCitiesAsync(lat, lon);
            if (cts.IsCancellationRequested || IsDisposed) return;
            _loading = false;
            ApplyFilters();
            if (_speakCountWhenLoaded)
            {
                _speakCountWhenLoaded = false;
                _announcer.AnnounceImmediate(_statusBox.Text);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _loading = false;
            _statusBox.Text = "Could not read the location data: " + ex.Message;
            Log.Warn("LocationInfo", $"Search failed: {ex.Message}");
        }
    }

    private const string WorldPrompt = "Type a name in Search by name to search the whole world.";

    /// <summary>The simulator's airports, read once per window, for the Details column's ident.</summary>
    private async Task EnsureAirportsAsync()
    {
        if (_airports is not null || !_places.Any(p => p.Category == LocationCategory.Airport)) return;
        string path = DatabasePathResolver.ResolveExistingDatabasePath(_settings.SimulatorVersion ?? "FS2020");
        try
        {
            var airports = await Task.Run(() => AirportNameSearch.ReadAirports(path));
            _airports = new AirportMatcher(airports);
        }
        catch (Exception ex)
        {
            Log.Warn("LocationInfo", $"Could not read the navigation database's airports: {ex.Message}");
        }
    }

    /// <summary>The major cities around the map position, for the first box.</summary>
    private async Task LoadMajorCitiesAsync(double lat, double lon)
    {
        if (Following)
        {
            _majorCities = _aircraftReport.Details?.MajorCities ?? new List<Models.NearbyPlace>();
        }
        else
        {
            var query = LocationQuerySettings.From(_settings);
            var report = await Task.Run(() => _service.GetLocationInfo(lat, lon, query));
            _majorCities = report?.Details?.MajorCities ?? new List<Models.NearbyPlace>();
        }
        UpdateWhereBox();
    }

    /// <summary>Filters the loaded places in memory and shows them, keeping the selected place selected.</summary>
    private void ApplyFilters()
    {
        if (_loading) return;
        SaveFilterSettings();
        var selectedId = SelectedPlace?.Id;
        string? direction = _directionCombo.SelectedIndex > 0 ? PlaceFilter.Directions[_directionCombo.SelectedIndex - 1] : null;
        double? maxNm = WholeWorld ? null : RangeKm / LocationPhrases.KmPerNm;
        _shown = PlaceFilter.Apply(_places, MapLat, MapLon, HiddenTypes().ToHashSet(StringComparer.Ordinal),
            SearchText.Fold(_searchBox.Text), direction, maxNm);

        _results.BeginUpdate();
        _results.SelectedIndices.Clear();
        _results.VirtualListSize = _shown.Count;
        // Always a selected row: Tab into a list with none gives the screen reader nothing to read.
        // The place selected before stays selected while it is still listed; otherwise the nearest.
        if (_shown.Count > 0)
        {
            int index = selectedId is { } id ? _shown.FindIndex(i => _places[i].Id == id) : -1;
            if (index < 0) index = 0;
            _results.SelectedIndices.Add(index);
            _results.FocusedItem = _results.Items[index];
            _results.EnsureVisible(index);
        }
        _results.Invalidate();
        _results.EndUpdate();
        _statusBox.Text = StatusText();
    }

    private string StatusText()
    {
        // Invariant, as everything the location data shows (LOC-3).
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string places = _shown.Count == 1 ? "1 place" : string.Create(inv, $"{_shown.Count:N0} places");
        if (!WholeWorld)
            return string.Create(inv, $"{places} within {(int)_rangeBox.Value} {UnitWord} of the map position");
        if (_loadedWorldQuery.Length == 0) return WorldPrompt;
        return _worldTotal > _places.Count
            ? string.Create(inv, $"{places} shown, the nearest of {_worldTotal:N0} named like that")
            : $"{places} named like that";
    }

    // ---- where you are, and refresh ----

    private void UpdateWhereBox()
    {
        string? mapName = Following ? null : _settings.LocationInfoMapName;
        double nm = LocationGeometry.GreatCircleNm(_aircraftLat, _aircraftLon, MapLat, MapLon);
        double bearing = LocationGeometry.BearingTrue(_aircraftLat, _aircraftLon, MapLat, MapLon);
        // Rewritten only when it changed: new text puts the caret on line 1 and the reader loses its place.
        string text = LocationPhrases.LocationInfoText(_aircraftReport.Where, mapName, nm, bearing, _majorCities, _kilometres);
        if (_whereBox.Text != text) _whereBox.Text = text;
    }

    /// <summary>F5 or Refresh: the aircraft's position read again; the list follows it when the map position does.</summary>
    private void RefreshFromAircraft()
    {
        if (!_sim.IsConnected)
        {
            _announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }
        _sim.RequestAircraftPositionAsync(async position =>
        {
            try
            {
                var query = LocationQuerySettings.From(_settings);
                var report = await Task.Run(() => _service.GetLocationInfo(position.Latitude, position.Longitude, query));
                if (IsDisposed || report is null) return;
                _aircraftReport = report;
                _aircraftLat = position.Latitude;
                _aircraftLon = position.Longitude;
                if (Following) _majorCities = report.Details?.MajorCities ?? new List<Models.NearbyPlace>();
                UpdateWhereBox();
                Reload();
                _results.Invalidate(); // the From aircraft column
                _announcer.AnnounceImmediate("Refreshed");
            }
            catch (Exception ex)
            {
                Log.Warn("LocationInfo", $"Refresh failed: {ex.Message}");
                if (!IsDisposed) _announcer.AnnounceImmediate("Could not refresh: " + ex.Message);
            }
        });
    }

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            Close();
        }
        else if (e.KeyCode == Keys.F5)
        {
            e.SuppressKeyPress = true;
            RefreshFromAircraft();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _loadCts?.Cancel();
        _searchDebounce.Dispose();
        _rangeDebounce.Dispose();
        SaveFilterSettings();
        SettingsManager.Save();
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }
}
