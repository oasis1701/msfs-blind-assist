using System.Globalization;
using System.Threading;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Controls;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Geography;
using MSFSBlindAssist.Services.Volanta;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.Volanta;

/// <summary>
/// Extras, Volanta: Volanta's live map as text, read from Volanta's public web map in a browser the pilot never sees
/// (<see cref="VolantaWebMap"/>). Four tabs: Traffic (Volanta users and network pilots around the pilot, filtered by
/// distance, network, phase and a search), Friends, ATC (online controllers by distance) and Connection (the web
/// map's state, what it downloads, and the button that logs in to Volanta or out). In each list tab the list comes
/// first, then the selected item's details, the major-city checkbox where there is one, then the status and the
/// filters.
///
/// Screen-reader strategy: the tab in view updates on its own every <see cref="RefreshIntervalMs"/> (another
/// tab is brought up to date when it is switched to), in place, the way the Sim Performance window does: only
/// changed rows are rewritten, so the reader's cursor never jumps, and
/// the row the pilot is on re-reads when it changes. While the pilot is in a list its order holds (new rows join
/// at the end), so arrowing down always reaches the next row. F5 re-sorts at once and speaks the status.
/// Background changes (connected, lost) are spoken once each, queued. While Volanta's data cannot be pulled
/// (loading, nothing new, the web map could not start), each list says why in a line at its top instead
/// (<see cref="NotUpdatingRow"/>). The web map downloads only while this window is in the foreground (VOL-12).
/// </summary>
public sealed class VolantaLiveMapForm : Form
{
    /// <summary>The Traffic tab's distances. The row before the last is a warning, not a distance (the owner's
    /// choice, 2026-10-15): landing on it keeps the distance chosen before, so the pilot hears what the last row
    /// does before choosing it.</summary>
    private static readonly (string Text, double? Nm, bool Warning)[] TrafficDistanceChoices =
    {
        ("10 nm", 10, false), ("25 nm", 25, false), ("50 nm", 50, false), ("100 nm", 100, false), ("200 nm", 200, false),
        ("500 nm", 500, false), ("1,000 nm", 1000, false),
        ("Warning: the item below this will display all pilots across Volanta!", null, true),
        ("All pilots across Volanta", null, false),
    };
    private const int DefaultTrafficDistance = 3;   // 100 nm
    private static readonly (string Text, double? Nm)[] AtcDistanceChoices =
    {
        ("10 nm", 10), ("25 nm", 25), ("50 nm", 50), ("100 nm", 100), ("200 nm", 200), ("500 nm", 500), ("Any distance", null),
    };
    private static readonly (string Text, VolantaPhaseFilter Phase)[] PhaseChoices =
    {
        ("All aircraft", VolantaPhaseFilter.All), ("Airborne only", VolantaPhaseFilter.Airborne),
        ("On the ground only", VolantaPhaseFilter.OnGround),
    };

    /// <summary>How often the window updates itself. Volanta sends its users every 6 seconds.</summary>
    private const int RefreshIntervalMs = 5000;
    /// <summary>Older than this, Volanta's traffic is called stale: Volanta normally sends it every 6 seconds.</summary>
    private const double StaleSeconds = 30;
    /// <summary>How long the first traffic fill waits for the pilot's own flight to be identified, so it is not
    /// listed as traffic once: the user id comes from a check that may answer after the first file.</summary>
    private const double OwnFlightGraceSeconds = 10;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>At most this many place lookups in one read of the location data, and one burst of lookups runs
    /// at most <see cref="PlaceBurstMs"/> before the lists show what it found.</summary>
    private const int PlaceBatchSize = 40;
    private const int PlaceBurstMs = 2000;

    /// <summary>In the list the reader is in, rows are rewritten in place only this far on each side of their row
    /// (VOL-9): a row rewritten in place costs about 0.35 ms, so all 3,000 rows of "All pilots across Volanta"
    /// blocked the window for a second at every update. A row farther away is rewritten once the reader comes
    /// within this many rows of it.</summary>
    private const int InPlaceWindowRows = 150;
    /// <summary>A list the reader is in that must be rewritten whole (F5 re-sorts it) is rebuilt in one go above
    /// this many rows, its selection put back by key (VOL-9): rewritten in place, 3,000 rows took a second.</summary>
    private const int RebuildAboveRows = 2 * InPlaceWindowRows;
    /// <summary>The main lists' fixed scrollable width in pixels (VOL-9): left to measure its own, a list
    /// re-measured its text for every row rewritten, and 3,000 rows took 7.8 seconds.</summary>
    private const int ListScrollWidthPx = 2500;

    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly OfflineLocationService _locations;
    /// <summary>The callsign vPilot says the pilot connected to VATSIM with, or null.</summary>
    private readonly Func<string?> _vatsimCallsign;
    private readonly VolantaLink _link = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = RefreshIntervalMs };
    // Each flight's place, one cache per kind (nearest town, nearest major city). Replaced, never cleared, when the
    // GeoNames tab's settings change, so a burst still running under the old settings fills only the old cache.
    private VolantaPlaceCache<string> _townPlaces = new();
    private VolantaPlaceCache<TrafficCityReport> _cityPlaces = new();
    private LocationQuerySettings? _placeSettings;

    private AccessibleTabControl _tabs = null!;
    // Traffic
    private ListBox _trafficList = null!;
    private DisplayListBox _trafficDetails = null!;
    private TextBox _trafficStatus = null!;
    private ComboBox _distance = null!, _phase = null!;
    private int _trafficDistance = DefaultTrafficDistance;   // the last distance chosen, never the warning row
    private CheckBox _srcVolanta = null!, _srcVatsim = null!, _srcIvao = null!, _srcPilotEdge = null!, _srcApoc = null!,
        _srcOther = null!;
    private TextBox _search = null!;
    private CheckBox _trafficCities = null!;
    // Friends
    private ListBox _friendsList = null!;
    private DisplayListBox _friendsDetails = null!;
    private TextBox _friendsStatus = null!;
    private CheckBox _friendsCities = null!;
    // ATC
    private ListBox _atcList = null!;
    private DisplayListBox _atcDetails = null!;
    private TextBox _atcStatus = null!;
    private ComboBox _atcDistance = null!;
    private CheckBox _atcVatsim = null!, _atcIvao = null!;
    private TextBox _atcSearch = null!;
    // Connection
    private DisplayListBox _connectionStatus = null!;
    private Button _loginButton = null!;

    // The latest computed row behind each key, for the details list (the row shown may be held).
    private Dictionary<string, VolantaTrafficEntry> _trafficByKey = new(StringComparer.Ordinal);
    private Dictionary<string, VolantaFriendEntry> _friendsByKey = new(StringComparer.Ordinal);
    private Dictionary<string, (VolantaController Station, double? Nm)> _atcByKey = new(StringComparer.Ordinal);

    private VolantaLinkStatus _spokenStatus = VolantaLinkStatus.Stopped;
    private bool _trafficFilled, _atcFilled, _reconciling;

    /// <summary>The page is up, but nothing has come for <see cref="StaleSeconds"/> since it came up or was last live
    /// again: Volanta's site may be down, or asking a human check the hidden page cannot show.</summary>
    private const string SilentText =
        "Volanta's web map has sent nothing for 30 seconds. Volanta's site may be busy; if this lasts, press Log in to "
        + "Volanta on the Connection tab to see whether Volanta asks you anything.";
    /// <summary>The web map could not start (no WebView2 runtime, or its browser profile could not be used).</summary>
    private const string NoAccessText =
        "Volanta's web map could not start. The Connection tab says why; the Microsoft Edge WebView2 runtime may need installing.";

    /// <summary>A list row: its key (flight or station id) and its text. Value equality, so an unchanged row is
    /// never rewritten.</summary>
    private sealed record Row(string Key, string Text)
    {
        public override string ToString() => Text;
    }

    /// <summary>Volanta's public web map in a browser the pilot never sees; created when the window is shown,
    /// disposed with it.</summary>
    private VolantaWebMap? _webMap;
    /// <summary>How long the window may be in the background before the web map pauses: an Alt+Tab to the sim
    /// and straight back costs no catch-up download.</summary>
    private static readonly TimeSpan WebMapPauseAfter = TimeSpan.FromSeconds(5);
    private readonly System.Windows.Forms.Timer _webMapPauseTimer = new() { Interval = (int)WebMapPauseAfter.TotalMilliseconds };

    internal VolantaLiveMapForm(SimConnectManager sim, ScreenReaderAnnouncer announcer, OfflineLocationService locations,
        Func<string?> vatsimCallsign)
    {
        _sim = sim;
        _announcer = announcer;
        _locations = locations;
        _vatsimCallsign = vatsimCallsign;
        BuildUI();
        CurrentPlaceSettings();   // names the GeoNames tab's values in the major-city checkboxes

        _link.Changed += OnLinkChanged;
        _timer.Tick += (_, _) => OnTimer();
        Shown += (_, _) =>
        {
            _sim.RequestAircraftPosition();
            _link.Start();
            _webMap = new VolantaWebMap(_link);
            _webMap.StateChanged += (_, _) =>
            {
                UpdateLoginButton();
                RefreshConnectionStatus();
            };
            _webMap.LoginCompleted += (_, _) =>
            {
                // The login window closed itself: bring this window back and say so once, since the window
                // closing is not something the pilot pressed.
                ShowForm();
                _announcer.AnnounceWithQueue("Logged in to Volanta.");
            };
            // Shown comes after ShowForm's activation: a window already in the background by now (focus went back to
            // the sim, or it was never let forward) gets no Deactivate, so its pause starts here (VOL-12).
            if (ActiveForm != this) _webMapPauseTimer.Start();
            _ = _webMap.StartAsync();
            _timer.Start();
            RefreshConnectionStatus();
        };
        // The web map downloads only while this window is in the foreground (VOL-12): live at once when it comes
        // forward, paused a few seconds after it goes back.
        Activated += (_, _) =>
        {
            _webMapPauseTimer.Stop();
            if (_webMap is not { Active: false } paused) return;
            paused.SetActive(true);
            // Nothing updated while paused (OnTimer): bring the tab in view up to date now, counted from the resume,
            // so the pilot never meets a "not updating" line left from the pause.
            RefreshVisibleTab(hold: true);
        };
        // Also before Shown creates the web map: the timer's tick pauses it once it exists.
        Deactivate += (_, _) => _webMapPauseTimer.Start();
        _webMapPauseTimer.Tick += (_, _) =>
        {
            _webMapPauseTimer.Stop();
            if (ActiveForm != this) _webMap?.SetActive(false);
        };
        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
            _webMapPauseTimer.Stop();
            _webMapPauseTimer.Dispose();
            _webMap?.Dispose();
            _link.Changed -= OnLinkChanged;
            _link.Dispose();
        };
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildUI()
    {
        Text = "Volanta Live Map";
        Width = 900;
        Height = 650;
        MinimumSize = new Size(600, 450);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        KeyDown += OnFormKeyDown;

        // As in the EFB: NVDA names only the selected tab, never "tab control".
        _tabs = new AccessibleTabControl { Dock = DockStyle.Fill, AccessibleName = "" };
        _tabs.TabPages.Add(BuildTrafficTab());
        _tabs.TabPages.Add(BuildFriendsTab());
        _tabs.TabPages.Add(BuildAtcTab());
        _tabs.TabPages.Add(BuildConnectionTab());
        // Only the tab in view updates on the timer, so the one switched to is brought up to date at once.
        _tabs.SelectedIndexChanged += (_, _) => RefreshVisibleTab(hold: true);
        Controls.Add(_tabs);
    }

    private TabPage BuildFriendsTab()
    {
        var page = new TabPage("Friends")
        {
            AccessibleName = "Friends",
            AccessibleDescription = "Your Volanta friends: who is flying and where, who is online",
        };

        _friendsList = new ListBox
        {
            Dock = DockStyle.Fill,
            HorizontalScrollbar = true,
            HorizontalExtent = ListScrollWidthPx,
            IntegralHeight = false,
            AccessibleName = "Friends",
            AccessibleDescription = "Friends flying first, nearest first, then online, then offline. Updated every 5 seconds. F5 re-sorts.",
        };
        _friendsList.SelectedIndexChanged += (_, _) => { if (!_reconciling) ShowFriendDetails(); };
        _friendsList.Enter += (_, _) => SelectFirstIfNone(_friendsList);

        _friendsDetails = NewDetailsList("Selected friend details",
            "The selected friend's status and, when they fly, everything known about their flight, one item per line.");

        _friendsStatus = ReadOnlyBox("Friends status");
        _friendsStatus.Text = "Waiting for Volanta.";
        _friendsCities = CityToggle(() => RefreshFriends(hold: false));

        // No login button of its own: the Connection tab's one button logs in, and the list comes with the login.
        // The location choice right after the details, as on the Traffic tab.
        var layout = NewColumn(4);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_friendsList, 0, 0);
        layout.Controls.Add(_friendsDetails, 0, 1);
        layout.Controls.Add(_friendsCities, 0, 2);
        layout.Controls.Add(_friendsStatus, 0, 3);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildTrafficTab()
    {
        var page = new TabPage("Traffic")
        {
            AccessibleName = "Traffic",
            AccessibleDescription = "Volanta users and network pilots around you",
        };

        _trafficList = new ListBox
        {
            Dock = DockStyle.Fill,
            HorizontalScrollbar = true,
            HorizontalExtent = ListScrollWidthPx,
            IntegralHeight = false,
            AccessibleName = "Traffic",
            AccessibleDescription = "Aircraft around you, nearest first, updated every 5 seconds. While you are in "
                + "the list its order holds, and the line you are on re-reads when it changes. F5 re-sorts.",
        };
        _trafficList.SelectedIndexChanged += (_, _) => { if (!_reconciling) ShowTrafficDetails(); };
        _trafficList.Enter += (_, _) => SelectFirstIfNone(_trafficList);

        _trafficDetails = NewDetailsList("Selected aircraft details",
            "Everything known about the selected aircraft, one item per line, updated live.");

        _trafficStatus = ReadOnlyBox("Traffic status");
        _trafficStatus.Text = "Waiting for Volanta.";

        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        _distance = Choice("Distance", TrafficDistanceChoices.Select(c => c.Text), defaultIndex: DefaultTrafficDistance);
        _distance.DropDownWidth = 460;   // the warning row is long
        _phase = Choice("Show", PhaseChoices.Select(c => c.Text), defaultIndex: 0);
        _srcVolanta = Toggle("Volanta users");
        _srcVatsim = Toggle("VATSIM");
        _srcIvao = Toggle("IVAO");
        _srcPilotEdge = Toggle("PilotEdge");
        _srcApoc = Toggle("APOC");
        // Any network Volanta's file names that the boxes above do not, so every pilot listed has a box that hides them.
        _srcOther = Toggle("Other networks");
        _search = new TextBox
        {
            Width = 200,
            AccessibleName = "Search callsign, user, aircraft or airport",
            AccessibleDescription = "Searches every distance, whatever the Distance box says.",
        };
        var refresh = new Button { Text = "&Re-sort now (F5)", AutoSize = true, AccessibleName = "Re-sort now" };
        refresh.Click += (_, _) => RefreshNow();

        _distance.SelectedIndexChanged += (_, _) =>
        {
            int i = _distance.SelectedIndex;
            if (i < 0 || TrafficDistanceChoices[i].Warning) return;   // the warning row keeps the distance chosen before
            _trafficDistance = i;
            RefreshTraffic(hold: false);
        };
        _phase.SelectedIndexChanged += (_, _) => RefreshTraffic(hold: false);
        foreach (var c in new[] { _srcVolanta, _srcVatsim, _srcIvao, _srcPilotEdge, _srcApoc, _srcOther })
            c.CheckedChanged += (_, _) => RefreshTraffic(hold: false);
        _search.TextChanged += (_, _) => RefreshTraffic(hold: false);

        filters.Controls.AddRange(new Control[]
        {
            Caption("Distance:"), _distance, Caption("Show:"), _phase,
            _srcVolanta, _srcVatsim, _srcIvao, _srcPilotEdge, _srcApoc, _srcOther, Caption("Search:"), _search, refresh,
        });

        _trafficCities = CityToggle(() => RefreshTraffic(hold: false));

        // The list first, then its details, the location choice, the status and the filters: the order the pilot
        // tabs through (the owner's choice, 2026-10-16: the location choice right after the details).
        var layout = NewColumn(5);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_trafficList, 0, 0);
        layout.Controls.Add(_trafficDetails, 0, 1);
        layout.Controls.Add(_trafficCities, 0, 2);
        layout.Controls.Add(_trafficStatus, 0, 3);
        layout.Controls.Add(filters, 0, 4);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildAtcTab()
    {
        var page = new TabPage("ATC")
        {
            AccessibleName = "ATC",
            AccessibleDescription = "Online VATSIM and IVAO controllers",
        };

        _atcList = new ListBox
        {
            Dock = DockStyle.Fill,
            HorizontalScrollbar = true,
            HorizontalExtent = ListScrollWidthPx,
            IntegralHeight = false,
            AccessibleName = "Online ATC",
            AccessibleDescription = "Online controllers, nearest first, updated every 5 seconds. F5 re-sorts.",
        };
        _atcList.SelectedIndexChanged += (_, _) => { if (!_reconciling) ShowAtcDetails(); };
        _atcList.Enter += (_, _) => SelectFirstIfNone(_atcList);
        _atcDetails = NewDetailsList("Selected station details",
            "The selected station's frequency, controller and ATIS, one item per line.");

        _atcStatus = ReadOnlyBox("ATC status");
        _atcStatus.Text = "Waiting for Volanta.";

        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        _atcDistance = Choice("ATC distance", AtcDistanceChoices.Select(c => c.Text), defaultIndex: 4);
        _atcVatsim = Toggle("VATSIM");
        _atcIvao = Toggle("IVAO");
        // Look up an airport's ATC wherever it is (the owner's request, 2026-10-16): "KATL", "EGLL".
        _atcSearch = new TextBox
        {
            Width = 200,
            AccessibleName = "Search airport, callsign, controller or frequency",
            AccessibleDescription = "Searches every distance, whatever the Distance box says. Type an airport code, such as KATL or EGLL, to hear its ATC.",
        };
        _atcDistance.SelectedIndexChanged += (_, _) => RefreshAtc(hold: false);
        _atcVatsim.CheckedChanged += (_, _) => RefreshAtc(hold: false);
        _atcIvao.CheckedChanged += (_, _) => RefreshAtc(hold: false);
        _atcSearch.TextChanged += (_, _) => RefreshAtc(hold: false);
        filters.Controls.AddRange(new Control[]
        {
            Caption("Distance:"), _atcDistance, _atcVatsim, _atcIvao, Caption("Search:"), _atcSearch,
        });

        var layout = NewColumn(4);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_atcList, 0, 0);
        layout.Controls.Add(_atcDetails, 0, 1);
        layout.Controls.Add(_atcStatus, 0, 2);
        layout.Controls.Add(filters, 0, 3);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildConnectionTab()
    {
        var page = new TabPage("Connection")
        {
            AccessibleName = "Connection",
            AccessibleDescription = "Volanta's web map: whether it is up, your Volanta login, and what it downloads",
        };

        _connectionStatus = NewDetailsList("Connection status", "Volanta's web map, your login and what has been received, updated live.");
        _connectionStatus.SetText("Opening Volanta's web map.");
        _loginButton = new Button
        {
            Text = LogInText,
            AutoSize = true,
            AccessibleName = LogInText.Replace("&", ""),
        };
        _loginButton.Click += async (_, _) =>
        {
            if (_webMap?.LoggedIn == true)
            {
                // The button's new name is not spoken by itself (.NET 10 sends no name-change notice, VOL-10).
                _announcer.Announce(await _webMap.LogOutAsync()
                    ? "Logged out of Volanta."
                    : "Could not log out of Volanta. Try again.");
            }
            else if (_webMap?.ShowLogin() != true)
                _announcer.Announce("Volanta's login page cannot open: the web map is not running. The status above says why.");
        };

        var help = new TextBox
        {
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            AccessibleName = "About the Volanta live map",
            Text = string.Join(Environment.NewLine, new[]
            {
                "This window reads the live map of Volanta, the flight tracker: the Volanta users and online network pilots around you, your Volanta friends, and online ATC. It reads Volanta's public web map (fly.volanta.app) in a browser you do not see, so the Volanta app does not need to be running; it can stay in the notification area logging your flights.",
                "",
                "Without logging in you get the traffic and ATC, but your own flight may be listed as traffic and there is no friends list. To log in, press Log in to Volanta above: Volanta's login page opens in a window of its own. Log in there; the window closes by itself once you are logged in (Escape closes it sooner), and the map reloads with your login, which is kept for next time. The same button then logs you out.",
                "",
                "The web map downloads only while this window is in the foreground. Five seconds after you switch away it pauses, and when you come back it catches up within a few seconds. While it runs it downloads about 415 megabytes an hour; the Connection tab shows what it downloaded in the last minute. The page's ad and analytics services are blocked.",
                "",
                "Everything here updates every 5 seconds, and the line you are on re-reads when it changes. While you are in a list, its order holds, so the down arrow always reaches the next aircraft; new ones join at the end. F5 re-sorts the lists at once.",
                "",
                "Friends: who is flying, and where, is always live; the others' statuses update as Volanta reports them.",
                "",
                "Each aircraft's Location names the nearest town, from the location data of Settings, GeoNames tab. Tick \"Report location to nearest major city\" on the Traffic or Friends tab to hear how far and which way it is from the nearest major city instead. A major city follows your own Minimum Population for Major Cities and Major Cities Range on the GeoNames tab.",
                "",
                "Volanta's positions can be up to two minutes old, so this window estimates where each aircraft is now. Use it for awareness, not to avoid traffic.",
                "",
                "Keys: F5 re-sorts the lists and reads the status, Ctrl+Tab moves between tabs, Escape closes the window.",
            }),
        };

        var layout = NewColumn(3);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(_connectionStatus, 0, 0);
        layout.Controls.Add(_loginButton, 0, 1);
        layout.Controls.Add(help, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private const string LogInText = "&Log in to Volanta";
    private const string LogOutText = "&Log out of Volanta";

    /// <summary>The Connection tab's one button logs in, or once logged in, logs out (the owner's request,
    /// 2026-10-17). Text and accessible name change together.</summary>
    private void UpdateLoginButton()
    {
        if (IsDisposed) return;
        string text = _webMap?.LoggedIn == true ? LogOutText : LogInText;
        if (_loginButton.Text == text) return;
        _loginButton.Text = text;
        _loginButton.AccessibleName = text.Replace("&", "");
    }

    private static TableLayoutPanel NewColumn(int rows)
        => new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows, Padding = new Padding(6) };

    private static Label Caption(string text)
        => new() { Text = text, AutoSize = true, Margin = new Padding(3, 8, 0, 3) };

    private static ComboBox Choice(string accessibleName, IEnumerable<string> items, int defaultIndex)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, AccessibleName = accessibleName };
        box.Items.AddRange(items.Cast<object>().ToArray());
        box.SelectedIndex = defaultIndex;
        return box;
    }

    /// <summary>A checkbox drawn by Windows (FlatStyle.System), so Windows itself tells the screen reader when it is
    /// ticked (VOL-10): .NET 10's WinForms sends that notice for its own drawn checkboxes only behind a switch, and
    /// NVDA said nothing on Space.</summary>
    private static CheckBox Toggle(string text)
        => new() { Text = text, Checked = true, AutoSize = true, FlatStyle = FlatStyle.System, Margin = new Padding(6, 6, 0, 3) };

    /// <summary>"Report location to nearest major city", one per list tab, each on its own, unticked at every
    /// opening (the owner's choice, 2026-10-15). Its text names the pilot's own GeoNames settings it follows
    /// (<see cref="CityToggleText"/>), set by <see cref="CurrentPlaceSettings"/>.</summary>
    private static CheckBox CityToggle(Action changed)
    {
        var box = new CheckBox
        {
            AutoSize = true,
            FlatStyle = FlatStyle.System,   // so ticking it is spoken (VOL-10), as Toggle's
            Margin = new Padding(6, 6, 0, 3),
            AccessibleDescription = "When ticked, each location says how far and which way the aircraft is from the "
                + "nearest major city, instead of naming the nearest town. Set the population and range in Settings, "
                + "GeoNames tab: Minimum Population for Major Cities, and Major Cities Range.",
        };
        box.CheckedChanged += (_, _) => changed();
        return box;
    }

    private static string CityToggleText(LocationQuerySettings s)
        => $"Report location to nearest major city (your GeoNames settings: population {s.MajorPopulation.ToString("N0", Inv)} "
            + $"or more, within {s.MajorRange.ToString(Inv)} {LocationPhrases.UnitWord(s.Kilometres)})";

    /// <summary>One status line: a read-only text box, never a label, since a label is not in the tab order
    /// (VAT-13). Updated in place, so a reader inside it keeps their position.</summary>
    private static TextBox ReadOnlyBox(string accessibleName) => new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill,
        AccessibleName = accessibleName,
        TabStop = true,
    };

    /// <summary>A details or status list, one item per row, reconciled in place like Sim Performance's.</summary>
    private static DisplayListBox NewDetailsList(string accessibleName, string description) => new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 9f),
        AccessibleName = accessibleName,
        AccessibleDescription = description,
    };

    /// <summary>Entering a list with nothing selected selects its first row, so the reader hears an aircraft
    /// rather than an empty list.</summary>
    private static void SelectFirstIfNone(ListBox list)
    {
        if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
    }

    // ── Keys ──────────────────────────────────────────────────────────────────

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5)
        {
            RefreshNow();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape && ActiveControl is not ComboBox { DroppedDown: true })
        {
            Close();
            e.Handled = true;
        }
    }

    // ── Link events and the timer ─────────────────────────────────────────────

    private void OnLinkChanged(object? sender, EventArgs e) => SafeBeginInvoke(ApplyLinkChange);

    private void ApplyLinkChange()
    {
        var snap = _link.Snapshot;
        AnnounceTransitions(snap);
        // The first data shows at once; after that the timer keeps everything current.
        if (!_trafficFilled) RefreshTraffic(hold: true);
        if (!ListsItems(_friendsList) && snap.Friends is { Count: > 0 }) RefreshFriends(hold: true);
        // Once, like the traffic: an ATC list the filters leave empty must not be redone at every hub frame.
        if (!_atcFilled && snap.Controllers != null) RefreshAtc(hold: true);
    }

    private void AnnounceTransitions(VolantaLinkSnapshot snap)
    {
        // The friends list comes with every logged-in page load, so it is not announced: "Logged in to Volanta" says
        // it is coming.
        if (snap.Status == _spokenStatus) return;
        if (snap.Status == VolantaLinkStatus.Connected)
            _announcer.AnnounceWithQueue("Connected to Volanta's live map.");
        else if (_spokenStatus == VolantaLinkStatus.Connected && snap.Status != VolantaLinkStatus.Stopped)
            _announcer.AnnounceWithQueue("Lost the connection to Volanta's live map.");
        _spokenStatus = snap.Status;
    }

    private void OnTimer()
    {
        // Paused (VOL-12), Volanta's data stands still and nobody reads the window: no updates and no lookups until
        // it comes back, when Activated brings the tab in view up to date.
        if (_webMap is { Active: false }) return;
        // Keep the sim position fresh, as the TCAS window does: each update reads the cached one.
        _sim.RequestAircraftPosition();
        CurrentPlaceSettings();   // the checkbox text follows the GeoNames tab even while no traffic shows
        RefreshVisibleTab(hold: true);
    }

    /// <summary>Updates the tab in view only: nobody reads the others, and with all pilots listed the Traffic
    /// list alone is 3,000 rows. A tab is brought up to date the moment it is switched to, and F5 updates every
    /// tab. <paramref name="lookUp"/> false shows the places found so far without starting another lookup.</summary>
    private void RefreshVisibleTab(bool hold, bool lookUp = true)
    {
        switch (_tabs.SelectedIndex)
        {
            case 0: RefreshTraffic(hold, lookUp); break;
            case 1: RefreshFriends(hold, lookUp); break;
            case 2: RefreshAtc(hold); break;
            default: RefreshConnectionStatus(); break;
        }
    }

    /// <summary>F5 and the Re-sort button: re-sort the lists now, even while the pilot is in one, and read the
    /// status of the tab in view.</summary>
    private void RefreshNow()
    {
        RefreshTraffic(hold: false);
        RefreshFriends(hold: false);
        RefreshAtc(hold: false);
        RefreshConnectionStatus();
        _announcer.Announce(_tabs.SelectedIndex switch
        {
            0 => _trafficStatus.Text,
            1 => _friendsStatus.Text,
            2 => _atcStatus.Text,
            _ => string.Join(" ", _connectionStatus.Items.Cast<object>()),
        });
    }

    // ── Friends ───────────────────────────────────────────────────────────────

    private void RefreshFriends(bool hold, bool lookUp = true)
    {
        if (IsDisposed) return;
        var snap = _link.Snapshot;
        var now = DateTimeOffset.UtcNow;
        var why = NotUpdatingRow(snap, now);
        if (snap.Friends is not { } friends)
        {
            _friendsByKey = new(StringComparer.Ordinal);
            ReconcileList(_friendsList, why, Array.Empty<Row>(), hold: false);
            ShowFriendDetails();
            DisplayText.SetPreserveCaret(_friendsStatus, _webMap?.LoggedIn == true
                ? "Logged in to Volanta: your friends list comes with the web map's next download."
                : "Your friends list comes when you are logged in to Volanta: press Log in to Volanta on the Connection tab.");
            return;
        }

        // Only the friends' own flights are placed, not every Volanta user's: the list needs no other.
        var friendIds = new HashSet<string>(friends.Select(f => f.FriendId), StringComparer.OrdinalIgnoreCase);
        var friendFlights = (snap.VolantaFlights?.Items ?? Array.Empty<VolantaFlight>())
            .Where(f => f.UserId.Length > 0 && friendIds.Contains(f.UserId))
            .ToList();
        var own = OwnPosition(snap, now, out _);
        IReadOnlyList<VolantaTrafficEntry> placed = own is VolantaOwnPosition me
            ? VolantaTrafficCalculator.Compute(friendFlights, me, now, snap.OwnFlight?.FlightId, snap.OwnVolantaPosition, snap.OwnUserId)
            : Array.Empty<VolantaTrafficEntry>();
        CurrentPlaceSettings();
        bool cities = _friendsCities.Checked;
        var entries = VolantaFriends.Arrange(friends, friendFlights, placed)
            .Select(e => e.Placed is { } p ? e with { Placed = WithPlaces(new[] { p }, cities)[0] } : e)
            .ToList();
        if (lookUp) LookUpPlaces(entries.Where(e => e.Placed != null).Select(e => e.Placed!).ToList(), cities);
        _friendsByKey = entries.GroupBy(e => e.Friend.FriendId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        ReconcileList(_friendsList, why, entries.Select(e => new Row(e.Friend.FriendId, VolantaTrafficFormatter.FriendLine(e))),
            hold && _friendsList.Focused);
        ShowFriendDetails();

        string status;
        if (friends.Count == 0)
            status = "You have no Volanta friends yet.";
        else
        {
            int flying = entries.Count(e => e.IsFlying);
            int online = entries.Count(e => !e.IsFlying && e.Friend.Status is { IsOffline: false });
            int offline = entries.Count(e => !e.IsFlying && e.Friend.Status is { IsOffline: true });
            int unknown = entries.Count(e => !e.IsFlying && e.Friend.Status == null);
            status = (friends.Count == 1 ? "1 friend: " : $"{friends.Count.ToString(Inv)} friends: ")
                + $"{flying.ToString(Inv)} flying, {online.ToString(Inv)} online, {offline.ToString(Inv)} offline"
                + (unknown > 0 ? $", {unknown.ToString(Inv)} not known yet." : ".");
        }
        status += " The list is from Volanta and stays current while you are logged in.";
        if (snap.VolantaFlights == null) status += " Waiting for Volanta's flights to see who is flying.";
        DisplayText.SetPreserveCaret(_friendsStatus, status);
    }

    private void ShowFriendDetails()
    {
        if (_friendsList.SelectedItem is Row row && _friendsByKey.TryGetValue(row.Key, out var e))
            SetDetails(_friendsDetails, VolantaTrafficFormatter.FriendDetails(e));
        else if (_friendsList.SelectedItem is Row status && VolantaListOrder.IsStatusKey(status.Key))
            SetDetails(_friendsDetails, status.Text);
        else
            SetDetails(_friendsDetails, _friendsByKey.Count == 0 ? "No friends listed." : "No friend selected.");
    }

    // ── Traffic ───────────────────────────────────────────────────────────────

    /// <summary>The pilot's position now: the sim's when connected, else Volanta's own last report moved on.</summary>
    private VolantaOwnPosition? OwnPosition(VolantaLinkSnapshot snap, DateTimeOffset now, out string source)
    {
        if (_sim.IsConnected && _sim.LastKnownPosition is SimConnectManager.AircraftPosition p
            && (p.Latitude != 0 || p.Longitude != 0))
        {
            source = "the simulator";
            return new VolantaOwnPosition(p.Latitude, p.Longitude, p.Altitude,
                RelativeDirection.Normalize360(p.HeadingMagnetic + p.MagneticVariation), p.GroundSpeedKnots,
                p.SimOnGround > 0.5, now);
        }
        if (snap.OwnVolantaPosition is VolantaOwnPosition v)
        {
            source = "Volanta's last report";
            return VolantaTrafficCalculator.Extrapolate(v, now);
        }
        source = "";
        return null;
    }

    /// <summary>The callsigns a network may list the pilot under: the one vPilot connected with, and the sim's ATC
    /// ID. Volanta leaves the pilot out of its network file only while it tracks their flight (VOL-4).</summary>
    private IReadOnlyCollection<string> OwnCallsigns()
    {
        var callsigns = new List<string>(2);
        if (_vatsimCallsign() is { Length: > 0 } vatsim) callsigns.Add(vatsim);
        if (_sim.IsConnected && _sim.CurrentAircraftAtcId is { Length: > 0 } atcId) callsigns.Add(atcId);
        return callsigns;
    }

    private VolantaTrafficFilter CurrentFilter()
    {
        var sources = new HashSet<VolantaSource>();
        if (_srcVolanta.Checked) sources.Add(VolantaSource.Volanta);
        if (_srcVatsim.Checked) sources.Add(VolantaSource.Vatsim);
        if (_srcIvao.Checked) sources.Add(VolantaSource.Ivao);
        if (_srcPilotEdge.Checked) sources.Add(VolantaSource.PilotEdge);
        if (_srcApoc.Checked) sources.Add(VolantaSource.Apoc);
        if (_srcOther.Checked) sources.Add(VolantaSource.Other);
        return new VolantaTrafficFilter
        {
            RadiusNm = TrafficDistanceChoices[_trafficDistance].Nm,
            Phase = PhaseChoices[Math.Max(0, _phase.SelectedIndex)].Phase,
            Sources = sources,
            Search = _search.Text,
        };
    }

    /// <param name="hold">Keep the list's order and the line under the cursor while the pilot is in the list;
    /// false re-sorts it whatever has focus (F5, a filter change).</param>
    /// <param name="lookUp">False shows the places found so far without starting another lookup: the update a
    /// finished lookup asks for, which would otherwise start the next one at once, back to back (VOL-8).</param>
    private void RefreshTraffic(bool hold, bool lookUp = true)
    {
        if (IsDisposed) return;
        var snap = _link.Snapshot;
        var now = DateTimeOffset.UtcNow;
        var why = NotUpdatingRow(snap, now);
        var flights = (snap.VolantaFlights?.Items ?? Array.Empty<VolantaFlight>())
            .Concat(snap.NetworkFlights?.Items ?? Array.Empty<VolantaFlight>())
            .ToList();
        if (flights.Count == 0)
        {
            ShowNoTraffic(why);
            DisplayText.SetPreserveCaret(_trafficStatus, NoDataText(snap, now));
            return;
        }

        // The first fill waits briefly for the pilot's own flight to be known, so it is never listed once.
        bool ownKnown = snap.OwnUserId != null || snap.OwnFlight != null
            || (snap.ConnectedAt is DateTimeOffset c && (now - c).TotalSeconds >= OwnFlightGraceSeconds);
        if (!_trafficFilled && !ownKnown)
        {
            ShowNoTraffic(why);
            DisplayText.SetPreserveCaret(_trafficStatus, "Connected to Volanta. Finding your own flight.");
            return;
        }

        var own = OwnPosition(snap, now, out string source);
        if (own is not VolantaOwnPosition me)
        {
            ShowNoTraffic(why);
            DisplayText.SetPreserveCaret(_trafficStatus, $"{flights.Count.ToString("N0", Inv)} aircraft online, "
                + "but your position is unknown: connect the simulator, or wait for Volanta to report your position.");
            _trafficFilled = true;
            return;
        }

        var entries = VolantaTrafficCalculator.Compute(flights, me, now, snap.OwnFlight?.FlightId, snap.OwnVolantaPosition,
            snap.OwnUserId, OwnCallsigns());
        CurrentPlaceSettings();
        var flightIds = flights.Select(f => f.Id).ToList();
        _townPlaces.KeepOnly(flightIds);
        _cityPlaces.KeepOnly(flightIds);
        bool cities = _trafficCities.Checked;
        var shown = WithPlaces(CurrentFilter().Apply(entries), cities);
        if (lookUp) LookUpPlaces(shown, cities);
        _trafficByKey = shown.GroupBy(e => e.Flight.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        ReconcileList(_trafficList, why, shown.Select(e => new Row(e.Flight.Id, VolantaTrafficFormatter.ListLine(e))),
            hold && _trafficList.Focused);
        ShowTrafficDetails();
        _trafficFilled = true;

        int volantaUsers = snap.VolantaFlights?.Items.Count ?? 0;
        int networkPilots = snap.NetworkFlights?.Items.Count ?? 0;
        string status = shown.Count == 0 ? "No aircraft match the filters." : $"{shown.Count.ToString("N0", Inv)} aircraft shown.";
        if (_search.Text.Trim().Length > 0) status += " The search covers every distance.";
        status += $" Online: {volantaUsers.ToString("N0", Inv)} Volanta users, {networkPilots.ToString("N0", Inv)} network pilots."
            + $" Your position from {source}. {DataAge("Traffic", snap.VolantaFlightsReceived, now)}";
        if (!_locations.IsAvailable) status += " Locations need the location data: Settings, GeoNames tab.";
        string warning = StatusWarning(snap, now);
        DisplayText.SetPreserveCaret(_trafficStatus, warning.Length > 0 ? warning + " " + status : status);
    }

    // ── Places ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The GeoNames tab's settings the lookups follow, read at every update so a change there applies here
    /// without reopening the window. When they change, both caches start over (what they hold was found under
    /// the old population, range or units) and the major-city checkboxes name the new values.
    /// </summary>
    private LocationQuerySettings CurrentPlaceSettings()
    {
        var settings = LocationQuerySettings.From(MSFSBlindAssist.Settings.SettingsManager.Current);
        if (settings == _placeSettings) return settings;
        if (_placeSettings != null)
        {
            _townPlaces = new();
            _cityPlaces = new();
        }
        _placeSettings = settings;
        string text = CityToggleText(settings);
        foreach (var box in new[] { _trafficCities, _friendsCities })
            if (box.Text != text) box.Text = text;
        return settings;
    }

    /// <summary>The entries with the place each one was last looked up at, where known: the nearest town's
    /// words, or with <paramref name="cities"/> the distance and direction from the nearest major city, worked
    /// out from where the aircraft is now.</summary>
    private List<VolantaTrafficEntry> WithPlaces(IEnumerable<VolantaTrafficEntry> entries, bool cities)
    {
        bool kilometres = _placeSettings?.Kilometres ?? false;
        return entries.Select(e =>
        {
            string? place = cities
                ? _cityPlaces.PlaceOf(e.Flight.Id)?.TextAt(e.Latitude, e.Longitude, kilometres)
                : _townPlaces.PlaceOf(e.Flight.Id);
            return place is null ? e : e with { Location = place };
        }).ToList();
    }

    /// <summary>Looks up the places of the given entries (nearest first): the nearest town, or with
    /// <paramref name="cities"/> the nearest major city, each kind into its own cache.</summary>
    private void LookUpPlaces(IReadOnlyList<VolantaTrafficEntry> wanted, bool cities)
    {
        if (wanted.Count == 0 || !_locations.IsAvailable || _placeSettings is not { } settings) return;
        if (cities)
            LookUpPlaces(_cityPlaces, wanted, positions => _locations.GetMajorCities(positions, settings));
        else
            LookUpPlaces(_townPlaces, wanted, positions => _locations.GetPlaces(positions, settings)?
                .Select(r => LocationPhrases.TrafficPlace(r.Where, r.Nearest)).ToList());
    }

    /// <summary>
    /// Looks up, in the background, the places of the given entries (nearest first) that have none yet or
    /// have moved since, from the offline location data Output C reads; one burst at a time per cache, in batches
    /// of <see cref="PlaceBatchSize"/>, for at most <see cref="PlaceBurstMs"/>, then the lists update in place to
    /// show what it found, and the next update starts the next burst. Never on the UI thread: each lookup reads
    /// the location database.
    /// </summary>
    private void LookUpPlaces<TPlace>(VolantaPlaceCache<TPlace> cache, IReadOnlyList<VolantaTrafficEntry> wanted,
        Func<IReadOnlyList<(double Lat, double Lon)>, IReadOnlyList<TPlace?>?> find) where TPlace : class
    {
        if (cache.Due(wanted, 1).Count == 0 || !cache.TryBeginLookup()) return;
        var candidates = wanted.ToList();
        _ = Task.Run(() =>
        {
            int found = 0;
            try
            {
                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < PlaceBurstMs && !IsDisposed)
                {
                    var batch = cache.Due(candidates, PlaceBatchSize);
                    if (batch.Count == 0) break;
                    var places = find(batch.Select(e => (e.Latitude, e.Longitude)).ToList());
                    if (places == null || places.Count != batch.Count) break;
                    for (int i = 0; i < batch.Count; i++)
                        cache.Store(batch[i].Flight.Id, batch[i].Latitude, batch[i].Longitude, places[i]);
                    found += batch.Count;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Volanta", $"Traffic place lookup failed: {ex.Message}");
            }
            finally
            {
                cache.EndLookup();
                // Show what was found now, but leave the next burst to the next update: started from here, bursts
                // ran back to back, and with all pilots listed the lists were rebuilt every two seconds.
                if (found > 0) SafeBeginInvoke(() => RefreshVisibleTab(hold: true, lookUp: false));
            }
        });
    }

    /// <summary>The Traffic list with no traffic to list: only the line saying why, while there is one.</summary>
    private void ShowNoTraffic(Row? why)
    {
        _trafficByKey = new(StringComparer.Ordinal);
        ReconcileList(_trafficList, why, Array.Empty<Row>(), hold: false);
        ShowTrafficDetails();
    }

    private void ShowTrafficDetails()
    {
        if (_trafficList.SelectedItem is Row row && _trafficByKey.TryGetValue(row.Key, out var e))
            SetDetails(_trafficDetails, VolantaTrafficFormatter.Details(e));
        else if (_trafficList.SelectedItem is Row status && VolantaListOrder.IsStatusKey(status.Key))
            SetDetails(_trafficDetails, status.Text);
        else
            SetDetails(_trafficDetails, _trafficByKey.Count == 0 ? "No aircraft listed." : "No aircraft selected.");
    }

    // ── ATC ───────────────────────────────────────────────────────────────────

    private void RefreshAtc(bool hold)
    {
        if (IsDisposed) return;
        var snap = _link.Snapshot;
        var now = DateTimeOffset.UtcNow;
        var why = NotUpdatingRow(snap, now);
        if (snap.Controllers is not { } feed)
        {
            _atcByKey = new(StringComparer.Ordinal);
            ReconcileList(_atcList, why, Array.Empty<Row>(), hold: false);
            ShowAtcDetails();
            DisplayText.SetPreserveCaret(_atcStatus, NoDataText(snap, now));
            return;
        }
        _atcFilled = true;
        var own = OwnPosition(snap, now, out _);
        // A search covers every distance, as on the Traffic tab.
        bool searching = _atcSearch.Text.Trim().Length > 0;
        double? radius = searching ? null : AtcDistanceChoices[Math.Max(0, _atcDistance.SelectedIndex)].Nm;
        var rows = new List<(VolantaController Station, double? Nm)>();
        foreach (var c in feed.Items)
        {
            if (c.Network == VolantaSource.Vatsim && !_atcVatsim.Checked) continue;
            if (c.Network == VolantaSource.Ivao && !_atcIvao.Checked) continue;
            if (!VolantaAtcSearch.Matches(c, _atcSearch.Text)) continue;
            double? nm = own is VolantaOwnPosition me && c.Latitude is double lat && c.Longitude is double lon
                ? NavigationCalculator.CalculateDistance(me.Latitude, me.Longitude, lat, lon)
                : null;
            if (radius is double r && own != null && (nm is null || nm > r)) continue;
            rows.Add((c, nm));
        }
        var ordered = rows.OrderBy(r => r.Nm ?? double.MaxValue)
            .ThenBy(r => r.Station.Callsign, StringComparer.OrdinalIgnoreCase).ToList();
        _atcByKey = ordered.GroupBy(r => r.Station.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        ReconcileList(_atcList, why, ordered.Select(r => new Row(r.Station.Id, VolantaTrafficFormatter.ControllerLine(r.Station, r.Nm))),
            hold && _atcList.Focused);
        ShowAtcDetails();

        string status = ordered.Count == 0
            ? (searching ? "No ATC online matches the search." : "No ATC online within the filters.")
            : $"{ordered.Count.ToString("N0", Inv)} ATC stations shown.";
        if (searching) status += " The search covers every distance.";
        status += $" Online: {feed.Items.Count.ToString("N0", Inv)} stations.";
        if (own == null) status += " Your position is unknown, so stations are in name order and not filtered by distance.";
        status += " " + DataAge("ATC", snap.ControllersReceived, now);
        DisplayText.SetPreserveCaret(_atcStatus, status);
    }

    private void ShowAtcDetails()
    {
        if (_atcList.SelectedItem is Row row && _atcByKey.TryGetValue(row.Key, out var r))
            SetDetails(_atcDetails, VolantaTrafficFormatter.ControllerDetails(r.Station, r.Nm));
        else if (_atcList.SelectedItem is Row status && VolantaListOrder.IsStatusKey(status.Key))
            SetDetails(_atcDetails, status.Text);
        else
            SetDetails(_atcDetails, _atcByKey.Count == 0 ? "No stations listed." : "No station selected.");
    }

    // ── List reconcile ────────────────────────────────────────────────────────

    /// <summary>
    /// The line at the top of each list while Volanta's data cannot be pulled, saying why, or null while it
    /// updates (VOL-2; the owner's choice, 2026-10-16, over a spoken notice that left an empty list behind). Rows
    /// already received stay below the line. Keyed by its text, so a new reason replaces it at the top.
    /// </summary>
    private static Row? NotUpdatingRow(VolantaLinkSnapshot snap, DateTimeOffset now)
    {
        string? text = NotUpdatingText(snap, now);
        return text is null ? null : new Row(VolantaListOrder.StatusKeyPrefix + text, text);
    }

    /// <summary>Why Volanta's data is standing still (VOL-2). One reason, worked out once, for every place that
    /// says it: the line at the top of each list (<see cref="NotUpdatingText"/>), the status boxes
    /// (<see cref="StatusWarning"/>) and an empty list's status (<see cref="NoDataText"/>).</summary>
    internal enum Standstill
    {
        /// <summary>Connected, and Volanta's files keep coming.</summary>
        None,
        /// <summary>The link is not started yet, or is stopping with the window.</summary>
        NotStarted,
        /// <summary>The page has been up for <see cref="StaleSeconds"/> and nothing has come since it came up or was
        /// last live again.</summary>
        Silent,
        /// <summary>No new Volanta flights file for <see cref="StaleSeconds"/> since the last one or since the page
        /// came up or was last live again, whichever is later.</summary>
        Stale,
        /// <summary>The web map's page is loading, or is being loaded again after it failed.</summary>
        Starting,
        /// <summary>The web map could not start.</summary>
        NoAccess,
    }

    internal static Standstill StandstillOf(VolantaLinkSnapshot snap, DateTimeOffset now) => snap.Status switch
    {
        VolantaLinkStatus.Connected when SilentSinceConnect(snap, now) => Standstill.Silent,
        VolantaLinkStatus.Connected when IsStale(snap, now) => Standstill.Stale,
        VolantaLinkStatus.Connected => Standstill.None,
        VolantaLinkStatus.WaitingForPage => Standstill.Starting,
        VolantaLinkStatus.NoAccess => Standstill.NoAccess,
        _ => Standstill.NotStarted,
    };

    /// <summary>The line at the top of each list saying why it stands still, or null while it updates.</summary>
    internal static string? NotUpdatingText(VolantaLinkSnapshot snap, DateTimeOffset now) => StandstillOf(snap, now) switch
    {
        Standstill.Silent
            => "Volanta's web map has sent nothing since it opened, so this list is not updating. Volanta's site may be busy; the Connection tab says more.",
        Standstill.Stale
            => "Volanta's web map has not sent anything new for a while, so this list is not updating.",
        Standstill.Starting => "Volanta's web map is loading, so this list is not updating yet.",
        Standstill.NoAccess => "This list is not updating. " + NoAccessText,
        _ => null,
    };

    /// <summary>The status boxes' and the Connection tab's warning, for the same reason as the list's line;
    /// empty while Volanta's data updates.</summary>
    internal static string StatusWarning(VolantaLinkSnapshot snap, DateTimeOffset now) => StandstillOf(snap, now) switch
    {
        Standstill.Silent => SilentText,
        Standstill.Stale => "Volanta's web map has not sent new traffic for a while.",
        Standstill.Starting => "Volanta's web map is loading, so this traffic is not updating yet.",
        Standstill.NoAccess => NoAccessText,
        _ => "",
    };

    /// <summary>A status box's text while its list has nothing to show.</summary>
    internal static string NoDataText(VolantaLinkSnapshot snap, DateTimeOffset now) => StandstillOf(snap, now) switch
    {
        Standstill.Silent => SilentText,
        Standstill.None or Standstill.Stale => "Connected to Volanta's web map. Waiting for its first update; it comes within a few seconds.",
        Standstill.Starting => "Opening Volanta's web map.",
        Standstill.NoAccess => NoAccessText,
        _ => "Opening Volanta's web map.",
    };

    /// <summary>Connected for more than <see cref="StaleSeconds"/> and no file of any kind since the page came up or
    /// was last live again (<see cref="VolantaLinkSnapshot.ConnectedAt"/>). Files from before do not count: the
    /// snapshot keeps them through a pause or a reload.</summary>
    private static bool SilentSinceConnect(VolantaLinkSnapshot snap, DateTimeOffset now)
        => snap.ConnectedAt is DateTimeOffset c && (now - c).TotalSeconds > StaleSeconds
            && !ReceivedSince(snap.VolantaFlightsReceived, c) && !ReceivedSince(snap.NetworkFlightsReceived, c)
            && !ReceivedSince(snap.ControllersReceived, c);

    private static bool ReceivedSince(DateTimeOffset? received, DateTimeOffset since) => received is DateTimeOffset r && r >= since;

    /// <summary>No new Volanta flights file for <see cref="StaleSeconds"/>, counted from <see cref="VolantaLinkSnapshot.ConnectedAt"/>
    /// when that is later: just after a pause the last file is old, but nothing is overdue yet.</summary>
    private static bool IsStale(VolantaLinkSnapshot snap, DateTimeOffset now)
    {
        if (snap.VolantaFlightsReceived is not DateTimeOffset t) return false;
        var since = snap.ConnectedAt is DateTimeOffset c && c > t ? c : t;
        return (now - since).TotalSeconds > StaleSeconds;
    }

    /// <summary>Brings a list to its rows with <paramref name="why"/>, when there is one, first. A pilot already in
    /// the list with nothing selected has the line selected, so it is read the moment it appears.</summary>
    private void ReconcileList(ListBox list, Row? why, IEnumerable<Row> rows, bool hold)
    {
        Reconcile(list, why is null ? rows.ToList() : rows.Prepend(why).ToList(), hold);
        if (why != null && list.Focused) SelectFirstIfNone(list);
    }

    /// <summary>Whether a list shows any item, its status line not counted.</summary>
    private static bool ListsItems(ListBox list)
        => list.Items.Count > (list.Items.Count > 0 && list.Items[0] is Row r && VolantaListOrder.IsStatusKey(r.Key) ? 1 : 0);

    /// <summary>
    /// Brings a list to <paramref name="sorted"/>, the selection following its row by key, so the reader's cursor
    /// never jumps (the Sim Performance window's rule, by key instead of by text, since a traffic line's text
    /// changes every update). In the list the reader is in, only rows whose key or text changed are rewritten,
    /// in place, and the row they are on re-reads when it changes, as in Sim Performance (the owner's choice,
    /// 2026-10-15); with <paramref name="hold"/> its order holds (<see cref="VolantaListOrder"/>), so arrowing
    /// down always reaches the next row, and only the rows near theirs are rewritten (VOL-9). A list nobody is in
    /// is rebuilt in one go, which is ten times faster.
    /// </summary>
    private void Reconcile(ListBox list, IReadOnlyList<Row> sorted, bool hold)
    {
        // A row rewritten under the selection briefly selects whatever row lands there. The lists' selection
        // handlers ignore that (_reconciling) and their details are shown once, after, for the final selection:
        // showing another aircraft's details for that moment pulled the reader's place in them up (2026-10-15).
        _reconciling = true;
        try { ReconcileRows(list, sorted, hold); }
        finally { _reconciling = false; }
    }

    private static void ReconcileRows(ListBox list, IReadOnlyList<Row> sorted, bool hold)
    {
        // A plain text item (a list's first placeholder) counts as a row with no key, so it is replaced.
        var shown = list.Items.Cast<object>().Select(o => o as Row ?? new Row("\0" + o, o.ToString() ?? "")).ToList();
        string? selectedKey = (list.SelectedItem as Row)?.Key;
        int selectedIndex = list.SelectedIndex;

        var shownKeys = shown.Select(r => r.Key).ToList();
        var arranged = VolantaListOrder.Arrange(shownKeys, sorted, r => r.Key, hold);
        if (shown.Count == arranged.Count && shown.SequenceEqual(arranged)) return;

        int top = list.TopIndex;
        list.BeginUpdate();
        try
        {
            // Never every row in place on the UI thread (VOL-9): at about 0.35 ms a row, 3,000 rows blocked the
            // window for a second at every update.
            var plan = hold && list.Focused
                ? VolantaListOrder.PlanHeldUpdate(shownKeys, arranged.Select(r => r.Key).ToList(), selectedKey, selectedIndex, InPlaceWindowRows)
                : null;
            if (!list.Focused || (plan == null && Math.Max(shown.Count, arranged.Count) > RebuildAboveRows))
            {
                // Nobody is reading it, or the reader's big list is re-sorted (F5) and would be rewritten whole:
                // rebuilt in one go, 125 ms for 3,000 rows, and the selection put back by key below.
                list.Items.Clear();
                list.Items.AddRange(arranged.Cast<object>().ToArray());
            }
            else if (plan != null)
            {
                // The reader is in it and its order holds: rows gone drop out, a new status line goes in at the
                // top, other new rows join at the end, and only the rows near the reader's are rewritten.
                foreach (int i in plan.Remove) list.Items.RemoveAt(i);
                for (int i = 0; i < plan.InsertTop; i++) list.Items.Insert(i, arranged[i]);
                for (int i = list.Items.Count; i < arranged.Count; i++) list.Items.Add(arranged[i]);
                for (int i = plan.RewriteFrom; i <= plan.RewriteTo; i++)
                    if (!Equals(list.Items[i], arranged[i])) list.Items[i] = arranged[i];
            }
            else
            {
                // The reader is in a short list that re-sorts (F5), or in a details list: every row in place.
                while (list.Items.Count > arranged.Count) list.Items.RemoveAt(list.Items.Count - 1);
                for (int i = 0; i < arranged.Count; i++)
                {
                    if (i >= list.Items.Count) list.Items.Add(arranged[i]);
                    else if (!Equals(list.Items[i], arranged[i])) list.Items[i] = arranged[i];
                }
            }
        }
        finally
        {
            list.EndUpdate();
            int newIndex = -1;
            if (selectedKey != null)
                for (int i = 0; i < arranged.Count && newIndex < 0; i++)
                    if (arranged[i].Key == selectedKey) newIndex = i;
            if (newIndex < 0 && selectedIndex >= 0 && arranged.Count > 0) newIndex = Math.Min(selectedIndex, arranged.Count - 1);
            if (newIndex >= 0 && newIndex != list.SelectedIndex) list.SelectedIndex = newIndex;
            if (top >= 0 && top < list.Items.Count) list.TopIndex = top;
        }
    }

    /// <summary>Shows a details text in a details list, one line per row, keyed by its field name
    /// (<see cref="VolantaListOrder.DetailKeys"/>): the reader stays on the same field ("Ground speed") when a
    /// line above it comes or goes, or when the list switches to another aircraft that has the field too.</summary>
    private void SetDetails(ListBox list, string text)
    {
        var lines = text.Split(Environment.NewLine);
        var keys = VolantaListOrder.DetailKeys(lines);
        Reconcile(list, lines.Select((line, i) => new Row(keys[i], line)).ToList(), hold: false);
    }

    // ── Status text ───────────────────────────────────────────────────────────

    private static string DataAge(string what, DateTimeOffset? received, DateTimeOffset now)
        => received is DateTimeOffset t
            ? $"{what} updated {VolantaTrafficFormatter.Age((now - t).TotalSeconds)}."
            : $"No {what.ToLowerInvariant()} from Volanta yet.";

    private void RefreshConnectionStatus()
    {
        if (IsDisposed) return;
        var snap = _link.Snapshot;
        var now = DateTimeOffset.UtcNow;
        var lines = new List<string>
        {
            // The web map's own state, the login, what it downloads, then what has been received.
            _webMap?.Problem ?? (snap.Status == VolantaLinkStatus.Connected
                ? "Connected to Volanta's web map." : "Opening Volanta's web map."),
            _webMap?.LoggedIn switch
            {
                true => "Logged in to Volanta.",
                false => "Not logged in to Volanta: press the button below to log in, for your own flight and your friends.",
                _ => "Login not checked yet.",
            },
        };
        if (_webMap != null) lines.Add("Downloads: " + _webMap.Stats.Summary(now));
        if (snap.Status == VolantaLinkStatus.Connected)
        {
            string warning = StatusWarning(snap, now);
            if (warning.Length > 0) lines.Add(warning);
            lines.Add("Volanta users: " + FeedLine(snap.VolantaFlights?.Items.Count, snap.VolantaFlightsReceived, now));
            lines.Add("Network pilots: " + FeedLine(snap.NetworkFlights?.Items.Count, snap.NetworkFlightsReceived, now));
            lines.Add("ATC stations: " + FeedLine(snap.Controllers?.Items.Count, snap.ControllersReceived, now));
        }
        if (snap.OwnFlight is { } f)
            lines.Add($"Your Volanta flight: {(f.Callsign.Length > 0 ? f.Callsign : "no callsign")}"
                + (VolantaTrafficFormatter.RouteText(f.Origin, f.Destination) is { Length: > 0 } route ? $", {route}" : "") + ".");
        OwnPosition(snap, now, out string source);
        lines.Add(source.Length > 0 ? $"Your position comes from {source}." : "Your position is unknown: connect the simulator, or wait for Volanta to report it.");
        _connectionStatus.SetLines(lines);
    }

    private static string FeedLine(int? count, DateTimeOffset? received, DateTimeOffset now)
        => count is int n && received is DateTimeOffset t
            ? $"{n.ToString("N0", Inv)}, updated {VolantaTrafficFormatter.Age((now - t).TotalSeconds)}."
            : "nothing received yet.";

    // ── Showing the window ────────────────────────────────────────────────────

    /// <summary>Shows the window as its own top-level window, never owned by the main window, so the pilot can
    /// Alt+Tab between the two (the TCAS window's pattern); a brief TopMost brings it forward without pinning it.</summary>
    public void ShowForm()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        TopMost = true;
        TopMost = false;
        Activate();
        BringToFront();
    }

    /// <summary>Marshal to the UI thread; BeginInvoke can still throw while the handle is being destroyed
    /// (A380C-6), so the check alone is not enough.</summary>
    private void SafeBeginInvoke(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { /* the window is closing */ }
    }
}
