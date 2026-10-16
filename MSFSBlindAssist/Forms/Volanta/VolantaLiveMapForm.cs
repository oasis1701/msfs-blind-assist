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
/// Extras, Volanta: Volanta's live map as text. Three tabs: Traffic (Volanta users and network pilots around the
/// pilot, filtered by distance, network, phase, altitude and a search), ATC (online controllers by distance) and
/// Connection (whether Volanta has live map access, and the button that gives it). In each list tab the list
/// comes first, then the selected item's details, then the status and the filters.
///
/// Screen-reader strategy: everything updates on its own every <see cref="RefreshIntervalMs"/>, in place, the
/// way the Sim Performance window does: only changed rows are rewritten, so the reader's cursor never jumps, and
/// the row the pilot is on re-reads when it changes. While the pilot is in a list its order holds (new rows join
/// at the end), so arrowing down always reaches the next row. F5 re-sorts at once and speaks the status.
/// Background changes (connected, lost, paused) are spoken once each, queued.
/// </summary>
public sealed class VolantaLiveMapForm : Form
{
    private static readonly (string Text, double? Nm)[] DistanceChoices =
    {
        ("10 nm", 10), ("25 nm", 25), ("50 nm", 50), ("100 nm", 100), ("200 nm", 200), ("500 nm", 500), ("Any distance", null),
    };
    private static readonly (string Text, double? Ft)[] AltitudeChoices =
    {
        ("Any altitude", null), ("Within 1,000 feet", 1000), ("Within 2,000 feet", 2000),
        ("Within 5,000 feet", 5000), ("Within 10,000 feet", 10000),
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

    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly OfflineLocationService _locations;
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
    private ComboBox _distance = null!, _phase = null!, _altitude = null!;
    private CheckBox _srcVolanta = null!, _srcVatsim = null!, _srcIvao = null!, _srcPilotEdge = null!, _srcApoc = null!;
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
    // Connection
    private DisplayListBox _connectionStatus = null!;
    private Button _startButton = null!;

    // The latest computed row behind each key, for the details list (the row shown may be held).
    private Dictionary<string, VolantaTrafficEntry> _trafficByKey = new(StringComparer.Ordinal);
    private Dictionary<string, VolantaFriendEntry> _friendsByKey = new(StringComparer.Ordinal);
    private Dictionary<string, (VolantaController Station, double? Nm)> _atcByKey = new(StringComparer.Ordinal);

    private VolantaLinkStatus _spokenStatus = VolantaLinkStatus.Stopped;
    private DateTimeOffset? _connectedAt;
    private DateTimeOffset? _spokenFriendsReceived;
    private bool? _spokenPaused;
    private bool _trafficFilled, _starting, _reconciling;

    /// <summary>A list row: its key (flight or station id) and its text. Value equality, so an unchanged row is
    /// never rewritten.</summary>
    private sealed record Row(string Key, string Text)
    {
        public override string ToString() => Text;
    }

    internal VolantaLiveMapForm(SimConnectManager sim, ScreenReaderAnnouncer announcer, OfflineLocationService locations)
    {
        _sim = sim;
        _announcer = announcer;
        _locations = locations;
        BuildUI();
        CurrentPlaceSettings();   // names the GeoNames tab's values in the major-city checkboxes

        _link.Changed += OnLinkChanged;
        _timer.Tick += (_, _) => OnTimer();
        Shown += (_, _) =>
        {
            _sim.RequestAircraftPosition();
            _link.Start();
            _timer.Start();
            RefreshConnectionStatus();
        };
        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
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

        // No restart button of its own: the Connection tab's one button restarts Volanta, which reloads the list.
        var layout = NewColumn(4);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_friendsList, 0, 0);
        layout.Controls.Add(_friendsDetails, 0, 1);
        layout.Controls.Add(_friendsStatus, 0, 2);
        layout.Controls.Add(_friendsCities, 0, 3);
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
        _distance = Choice("Distance", DistanceChoices.Select(c => c.Text), defaultIndex: 3);
        _phase = Choice("Show", PhaseChoices.Select(c => c.Text), defaultIndex: 0);
        _altitude = Choice("Altitude", AltitudeChoices.Select(c => c.Text), defaultIndex: 0);
        _srcVolanta = Toggle("Volanta users");
        _srcVatsim = Toggle("VATSIM");
        _srcIvao = Toggle("IVAO");
        _srcPilotEdge = Toggle("PilotEdge");
        _srcApoc = Toggle("APOC");
        _search = new TextBox { Width = 200, AccessibleName = "Search callsign, user, aircraft or airport" };
        var refresh = new Button { Text = "&Re-sort now (F5)", AutoSize = true, AccessibleName = "Re-sort now" };
        refresh.Click += (_, _) => RefreshNow();

        foreach (var c in new[] { _distance, _phase, _altitude })
            c.SelectedIndexChanged += (_, _) => RefreshTraffic(hold: false);
        foreach (var c in new[] { _srcVolanta, _srcVatsim, _srcIvao, _srcPilotEdge, _srcApoc })
            c.CheckedChanged += (_, _) => RefreshTraffic(hold: false);
        _search.TextChanged += (_, _) => RefreshTraffic(hold: false);

        filters.Controls.AddRange(new Control[]
        {
            Caption("Distance:"), _distance, Caption("Show:"), _phase, Caption("Altitude:"), _altitude,
            _srcVolanta, _srcVatsim, _srcIvao, _srcPilotEdge, _srcApoc, Caption("Search:"), _search, refresh,
        });

        _trafficCities = CityToggle(() => RefreshTraffic(hold: false));

        // The list first, then its details, then the status, the filters and the location choice: the order the
        // pilot tabs through.
        var layout = NewColumn(5);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_trafficList, 0, 0);
        layout.Controls.Add(_trafficDetails, 0, 1);
        layout.Controls.Add(_trafficStatus, 0, 2);
        layout.Controls.Add(filters, 0, 3);
        layout.Controls.Add(_trafficCities, 0, 4);
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
        _atcDistance = Choice("ATC distance", DistanceChoices.Select(c => c.Text), defaultIndex: 4);
        _atcVatsim = Toggle("VATSIM");
        _atcIvao = Toggle("IVAO");
        _atcDistance.SelectedIndexChanged += (_, _) => RefreshAtc(hold: false);
        _atcVatsim.CheckedChanged += (_, _) => RefreshAtc(hold: false);
        _atcIvao.CheckedChanged += (_, _) => RefreshAtc(hold: false);
        filters.Controls.AddRange(new Control[] { Caption("Distance:"), _atcDistance, _atcVatsim, _atcIvao });

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
            AccessibleDescription = "Volanta's live map access and what has been received",
        };

        _connectionStatus = NewDetailsList("Connection status", "Volanta's live map access and what has been received, updated live.");
        _connectionStatus.SetText("Checking Volanta.");
        _startButton = new Button
        {
            Text = "&Start or restart Volanta with live map access",
            AutoSize = true,
            AccessibleName = "Start or restart Volanta with live map access",
        };
        _startButton.Click += async (_, _) => await StartVolantaAsync();

        var help = new TextBox
        {
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            AccessibleName = "About the Volanta live map",
            Text = string.Join(Environment.NewLine, new[]
            {
                "This window reads the live map of Volanta, the flight tracker: the Volanta users and online network pilots around you, and online ATC.",
                "",
                "What it needs: Volanta installed, you logged in, and Volanta started with the button above, which gives this app access to Volanta's map. Volanta started any other way (with Windows, with the sim, after an update) has no access until it is restarted with the button.",
                "",
                "Keep Volanta open. It can stay behind the sim, but while it is minimized Volanta stops updating its map, so the traffic here stops too.",
                "",
                "Everything here updates every 5 seconds, and the line you are on re-reads when it changes. While you are in a list, its order holds, so the down arrow always reaches the next aircraft; new ones join at the end. F5 re-sorts the lists at once.",
                "",
                "Friends: Volanta sends your friends list once, when it starts, so this app reads it when it starts Volanta, and keeps a copy of your friends' names for next time. Who is flying, and where, is always live. If your list looks out of date, restart Volanta with the button above.",
                "",
                "Each aircraft's Location names the nearest town, from the location data of Settings, GeoNames tab. Tick \"Report location to nearest major city\" on the Traffic or Friends tab to hear how far and which way it is from the nearest major city instead. A major city follows your own Minimum Population for Major Cities and Major Cities Range on the GeoNames tab.",
                "",
                "Volanta's positions can be up to two minutes old, so this window estimates where each aircraft is now. Use it for awareness, not to avoid traffic.",
                "",
                "While Volanta runs with live map access, programs on this PC can control Volanta. The access ends when Volanta is next started normally.",
                "",
                "Keys: F5 re-sorts the lists and reads the status, Ctrl+Tab moves between tabs, Escape closes the window.",
            }),
        };

        var layout = NewColumn(3);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(_connectionStatus, 0, 0);
        layout.Controls.Add(_startButton, 0, 1);
        layout.Controls.Add(help, 0, 2);
        page.Controls.Add(layout);
        return page;
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

    private static CheckBox Toggle(string text)
        => new() { Text = text, Checked = true, AutoSize = true, Margin = new Padding(6, 6, 0, 3) };

    /// <summary>"Report location to nearest major city", one per list tab, each on its own, unticked at every
    /// opening (the owner's choice, 2026-10-15). Its text names the pilot's own GeoNames settings it follows
    /// (<see cref="CityToggleText"/>), set by <see cref="CurrentPlaceSettings"/>.</summary>
    private static CheckBox CityToggle(Action changed)
    {
        var box = new CheckBox
        {
            AutoSize = true,
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
        if (_friendsList.Items.Count == 0 && snap.Friends is { Count: > 0 }) RefreshFriends(hold: true);
        if (_atcList.Items.Count == 0 && snap.Controllers != null) RefreshAtc(hold: true);
    }

    private void AnnounceTransitions(VolantaLinkSnapshot snap)
    {
        if (snap.Status != _spokenStatus)
        {
            _connectedAt = snap.Status == VolantaLinkStatus.Connected ? DateTimeOffset.UtcNow : null;
            if (snap.Status == VolantaLinkStatus.Connected)
                _announcer.AnnounceWithQueue("Connected to Volanta's live map.");
            else if (_spokenStatus == VolantaLinkStatus.Connected && snap.Status != VolantaLinkStatus.Stopped && !_starting)
                // Not while this window restarts Volanta itself: the pilot asked for that, and hears "Connected".
                _announcer.AnnounceWithQueue("Lost the connection to Volanta's live map.");
            _spokenStatus = snap.Status;
        }
        // Volanta's own friends download comes only when Volanta starts, which here is always the pilot's press.
        if (snap.FriendsReceived != null && snap.FriendsReceived != _spokenFriendsReceived && snap.Friends is { } friends)
        {
            if (_spokenFriendsReceived != null || _starting || _connectedAt is DateTimeOffset c && (DateTimeOffset.UtcNow - c).TotalSeconds < 30)
                _announcer.AnnounceWithQueue(friends.Count == 1 ? "Loaded your friends list: 1 friend." : $"Loaded your friends list: {friends.Count.ToString(Inv)} friends.");
            _spokenFriendsReceived = snap.FriendsReceived;
        }
        if (snap.Status == VolantaLinkStatus.Connected && snap.VolantaPaused != _spokenPaused && snap.VolantaPaused != null)
        {
            if (snap.VolantaPaused == true)
                _announcer.AnnounceWithQueue("Volanta is minimized, so its traffic has stopped updating.");
            else if (_spokenPaused == true)
                _announcer.AnnounceWithQueue("Volanta traffic is updating again.");
            _spokenPaused = snap.VolantaPaused;
        }
    }

    private void OnTimer()
    {
        // Keep the sim position fresh, as the TCAS window does: each update reads the cached one.
        _sim.RequestAircraftPosition();
        CurrentPlaceSettings();   // the checkbox text follows the GeoNames tab even while no traffic shows
        RefreshTraffic(hold: true);
        RefreshFriends(hold: true);
        RefreshAtc(hold: true);
        RefreshConnectionStatus();
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

    private void RefreshFriends(bool hold)
    {
        if (IsDisposed) return;
        var snap = _link.Snapshot;
        var now = DateTimeOffset.UtcNow;
        if (snap.Friends is not { } friends)
        {
            _friendsByKey = new(StringComparer.Ordinal);
            Reconcile(_friendsList, Array.Empty<Row>(), hold: false);
            ShowFriendDetails();
            DisplayText.SetPreserveCaret(_friendsStatus, "Your friends list has not been read yet. Volanta sends it "
                + "only when it starts: restart Volanta from the Connection tab to load it.");
            return;
        }

        var volantaFlights = snap.VolantaFlights?.Items ?? Array.Empty<VolantaFlight>();
        var own = OwnPosition(snap, now, out _);
        IReadOnlyList<VolantaTrafficEntry> placed = own is VolantaOwnPosition me
            ? VolantaTrafficCalculator.Compute(volantaFlights, me, now, snap.OwnFlight?.FlightId, snap.OwnVolantaPosition, snap.OwnUserId)
            : Array.Empty<VolantaTrafficEntry>();
        CurrentPlaceSettings();
        bool cities = _friendsCities.Checked;
        var entries = VolantaFriends.Arrange(friends, volantaFlights, placed)
            .Select(e => e.Placed is { } p ? e with { Placed = WithPlaces(new[] { p }, cities)[0] } : e)
            .ToList();
        LookUpPlaces(entries.Where(e => e.Placed != null).Select(e => e.Placed!).ToList(), cities);
        _friendsByKey = entries.GroupBy(e => e.Friend.FriendId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        Reconcile(_friendsList, entries.Select(e => new Row(e.Friend.FriendId, VolantaTrafficFormatter.FriendLine(e))).ToList(),
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
        if (snap.FriendsReceived != null)
            status += " The list is from Volanta and stays current while it runs.";
        else if (snap.FriendsSavedAt is DateTimeOffset saved)
            status += $" The list is your saved copy from {saved.ToLocalTime().ToString("d MMMM", Inv)}. Who is flying is live; "
                + "other statuses show once Volanta reports a change. To load the latest list, restart Volanta from the Connection tab.";
        if (snap.VolantaFlights == null) status += " Waiting for Volanta's flights to see who is flying.";
        DisplayText.SetPreserveCaret(_friendsStatus, status);
    }

    private void ShowFriendDetails()
    {
        if (_friendsList.SelectedItem is Row row && _friendsByKey.TryGetValue(row.Key, out var e))
            SetDetails(_friendsDetails, VolantaTrafficFormatter.FriendDetails(e));
        else
            SetDetails(_friendsDetails, _friendsList.Items.Count == 0 ? "No friends listed." : "No friend selected.");
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

    private VolantaTrafficFilter CurrentFilter()
    {
        var sources = new HashSet<VolantaSource> { VolantaSource.Other };
        if (_srcVolanta.Checked) sources.Add(VolantaSource.Volanta);
        if (_srcVatsim.Checked) sources.Add(VolantaSource.Vatsim);
        if (_srcIvao.Checked) sources.Add(VolantaSource.Ivao);
        if (_srcPilotEdge.Checked) sources.Add(VolantaSource.PilotEdge);
        if (_srcApoc.Checked) sources.Add(VolantaSource.Apoc);
        return new VolantaTrafficFilter
        {
            RadiusNm = DistanceChoices[Math.Max(0, _distance.SelectedIndex)].Nm,
            Phase = PhaseChoices[Math.Max(0, _phase.SelectedIndex)].Phase,
            AltitudeWithinFt = AltitudeChoices[Math.Max(0, _altitude.SelectedIndex)].Ft,
            Sources = sources,
            Search = _search.Text,
        };
    }

    /// <param name="hold">Keep the list's order and the line under the cursor while the pilot is in the list;
    /// false re-sorts it whatever has focus (F5, a filter change).</param>
    private void RefreshTraffic(bool hold)
    {
        if (IsDisposed) return;
        var snap = _link.Snapshot;
        var now = DateTimeOffset.UtcNow;
        var flights = (snap.VolantaFlights?.Items ?? Array.Empty<VolantaFlight>())
            .Concat(snap.NetworkFlights?.Items ?? Array.Empty<VolantaFlight>())
            .ToList();
        if (flights.Count == 0)
        {
            DisplayText.SetPreserveCaret(_trafficStatus, NoDataText(snap));
            return;
        }

        // The first fill waits briefly for the pilot's own flight to be known, so it is never listed once.
        bool ownKnown = snap.OwnUserId != null || snap.OwnFlight != null
            || (_connectedAt is DateTimeOffset c && (now - c).TotalSeconds >= OwnFlightGraceSeconds);
        if (!_trafficFilled && !ownKnown)
        {
            DisplayText.SetPreserveCaret(_trafficStatus, "Connected to Volanta. Finding your own flight.");
            return;
        }

        var own = OwnPosition(snap, now, out string source);
        if (own is not VolantaOwnPosition me)
        {
            _trafficByKey = new(StringComparer.Ordinal);
            Reconcile(_trafficList, Array.Empty<Row>(), hold: false);
            ShowTrafficDetails();
            DisplayText.SetPreserveCaret(_trafficStatus, $"{flights.Count.ToString("N0", Inv)} aircraft online, "
                + "but your position is unknown: connect the simulator, or wait for Volanta to report your position.");
            _trafficFilled = true;
            return;
        }

        var entries = VolantaTrafficCalculator.Compute(flights, me, now, snap.OwnFlight?.FlightId, snap.OwnVolantaPosition,
            snap.OwnUserId);
        CurrentPlaceSettings();
        var flightIds = flights.Select(f => f.Id).ToList();
        _townPlaces.KeepOnly(flightIds);
        _cityPlaces.KeepOnly(flightIds);
        bool cities = _trafficCities.Checked;
        var shown = WithPlaces(CurrentFilter().Apply(entries), cities);
        LookUpPlaces(shown, cities);
        _trafficByKey = shown.GroupBy(e => e.Flight.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        Reconcile(_trafficList, shown.Select(e => new Row(e.Flight.Id, VolantaTrafficFormatter.ListLine(e))).ToList(),
            hold && _trafficList.Focused);
        ShowTrafficDetails();
        _trafficFilled = true;

        int volantaUsers = snap.VolantaFlights?.Items.Count ?? 0;
        int networkPilots = snap.NetworkFlights?.Items.Count ?? 0;
        string status = shown.Count == 0 ? "No aircraft match the filters." : $"{shown.Count.ToString("N0", Inv)} aircraft shown.";
        status += $" Online: {volantaUsers.ToString("N0", Inv)} Volanta users, {networkPilots.ToString("N0", Inv)} network pilots."
            + $" Your position from {source}. {DataAge("Traffic", snap.VolantaFlightsReceived, now)}";
        if (!_locations.IsAvailable) status += " Locations need the location data: Settings, GeoNames tab.";
        string warning = PausedWarning(snap, now);
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
                if (found > 0) SafeBeginInvoke(() => { RefreshTraffic(hold: true); RefreshFriends(hold: true); });
            }
        });
    }

    private void ShowTrafficDetails()
    {
        if (_trafficList.SelectedItem is Row row && _trafficByKey.TryGetValue(row.Key, out var e))
            SetDetails(_trafficDetails, VolantaTrafficFormatter.Details(e));
        else
            SetDetails(_trafficDetails, _trafficList.Items.Count == 0 ? "No aircraft listed." : "No aircraft selected.");
    }

    // ── ATC ───────────────────────────────────────────────────────────────────

    private void RefreshAtc(bool hold)
    {
        if (IsDisposed) return;
        var snap = _link.Snapshot;
        var now = DateTimeOffset.UtcNow;
        if (snap.Controllers is not { } feed)
        {
            DisplayText.SetPreserveCaret(_atcStatus, NoDataText(snap));
            return;
        }
        var own = OwnPosition(snap, now, out _);
        double? radius = DistanceChoices[Math.Max(0, _atcDistance.SelectedIndex)].Nm;
        var rows = new List<(VolantaController Station, double? Nm)>();
        foreach (var c in feed.Items)
        {
            if (c.Network == VolantaSource.Vatsim && !_atcVatsim.Checked) continue;
            if (c.Network == VolantaSource.Ivao && !_atcIvao.Checked) continue;
            double? nm = own is VolantaOwnPosition me && c.Latitude is double lat && c.Longitude is double lon
                ? NavigationCalculator.CalculateDistance(me.Latitude, me.Longitude, lat, lon)
                : null;
            if (radius is double r && own != null && (nm is null || nm > r)) continue;
            rows.Add((c, nm));
        }
        var ordered = rows.OrderBy(r => r.Nm ?? double.MaxValue)
            .ThenBy(r => r.Station.Callsign, StringComparer.OrdinalIgnoreCase).ToList();
        _atcByKey = ordered.GroupBy(r => r.Station.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        Reconcile(_atcList, ordered.Select(r => new Row(r.Station.Id, VolantaTrafficFormatter.ControllerLine(r.Station, r.Nm))).ToList(),
            hold && _atcList.Focused);
        ShowAtcDetails();

        string status = ordered.Count == 0 ? "No ATC online within the filters." : $"{ordered.Count.ToString("N0", Inv)} ATC stations shown.";
        status += $" Online: {feed.Items.Count.ToString("N0", Inv)} stations.";
        if (own == null) status += " Your position is unknown, so stations are in name order and not filtered by distance.";
        status += " " + DataAge("ATC", snap.ControllersReceived, now);
        DisplayText.SetPreserveCaret(_atcStatus, status);
    }

    private void ShowAtcDetails()
    {
        if (_atcList.SelectedItem is Row row && _atcByKey.TryGetValue(row.Key, out var r))
            SetDetails(_atcDetails, VolantaTrafficFormatter.ControllerDetails(r.Station, r.Nm));
        else
            SetDetails(_atcDetails, _atcList.Items.Count == 0 ? "No stations listed." : "No station selected.");
    }

    // ── List reconcile ────────────────────────────────────────────────────────

    /// <summary>
    /// Brings a list to <paramref name="sorted"/> in place: only rows whose key or text changed are rewritten,
    /// and the selection follows its row by key, so the reader's cursor never jumps (the Sim Performance
    /// window's rule, by key instead of by text, since a traffic line's text changes every update). The row the
    /// pilot is on is rewritten like any other, so it re-reads when it changes, as in Sim Performance (the
    /// owner's choice, 2026-10-15). With <paramref name="hold"/> (the pilot is in the list) the order holds
    /// (<see cref="VolantaListOrder"/>), so arrowing down always reaches the next row.
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

        var arranged = VolantaListOrder.Arrange(shown.Select(r => r.Key).ToList(), sorted, r => r.Key, hold);
        if (shown.Count == arranged.Count && shown.SequenceEqual(arranged)) return;

        int top = list.TopIndex;
        list.BeginUpdate();
        try
        {
            while (list.Items.Count > arranged.Count) list.Items.RemoveAt(list.Items.Count - 1);
            for (int i = 0; i < arranged.Count; i++)
            {
                if (i >= list.Items.Count) list.Items.Add(arranged[i]);
                else if (!Equals(list.Items[i], arranged[i])) list.Items[i] = arranged[i];
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

    /// <summary>Why the traffic may be standing still: Volanta minimized, or no new file for a while (Volanta
    /// off its map, or in its performance mode).</summary>
    private static string PausedWarning(VolantaLinkSnapshot snap, DateTimeOffset now)
    {
        if (snap.Status != VolantaLinkStatus.Connected) return "Not connected to Volanta, so this traffic is not updating.";
        if (snap.VolantaPaused == true)
            return "Volanta is minimized, so its traffic has stopped updating: restore Volanta's window. It can stay behind the sim.";
        if (snap.VolantaFlightsReceived is DateTimeOffset t && (now - t).TotalSeconds > StaleSeconds)
            return "Volanta has not sent new traffic for a while: keep Volanta open on its map, not minimized.";
        return "";
    }

    private static string NoDataText(VolantaLinkSnapshot snap) => snap.Status switch
    {
        VolantaLinkStatus.Connected => "Connected to Volanta. Waiting for its first update; it comes every few seconds while Volanta's map is open.",
        VolantaLinkStatus.WaitingForPage => "Volanta is starting. Waiting for its map.",
        _ => "No access to Volanta's live map. Open the Connection tab to start Volanta with live map access.",
    };

    private void RefreshConnectionStatus()
    {
        if (IsDisposed) return;
        var snap = _link.Snapshot;
        var now = DateTimeOffset.UtcNow;
        var lines = new List<string>();
        switch (snap.Status)
        {
            case VolantaLinkStatus.Connected:
                lines.Add("Connected to Volanta's live map.");
                string warning = PausedWarning(snap, now);
                if (warning.Length > 0) lines.Add(warning);
                lines.Add("Volanta users: " + FeedLine(snap.VolantaFlights?.Items.Count, snap.VolantaFlightsReceived, now));
                lines.Add("Network pilots: " + FeedLine(snap.NetworkFlights?.Items.Count, snap.NetworkFlightsReceived, now));
                lines.Add("ATC stations: " + FeedLine(snap.Controllers?.Items.Count, snap.ControllersReceived, now));
                break;
            case VolantaLinkStatus.WaitingForPage:
                lines.Add("Volanta is starting, or is between pages. Waiting for its map.");
                break;
            default:
                lines.Add(VolantaLauncher.IsRunning()
                    ? "Volanta is running without live map access. Press the button below to restart it with access."
                    : VolantaLauncher.FindExePath() == null
                        ? "Volanta is not installed for this Windows user. Install Volanta and log in, then press the button below."
                        : "Volanta is not running. Press the button below to start it with live map access.");
                break;
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

    // ── Start / restart ───────────────────────────────────────────────────────

    /// <summary>The Connection tab's one button: start Volanta with access, or restart it, even when it already
    /// has access, since that is also how the friends list is reloaded (Volanta sends it only when it starts).</summary>
    private async Task StartVolantaAsync()
    {
        if (_starting) return;
        _starting = true;
        try
        {
            switch (await VolantaLauncher.DetectAsync())
            {
                case VolantaAccessState.NotInstalled:
                    _announcer.Announce("Volanta is not installed for this Windows user.");
                    return;
                case VolantaAccessState.RunningWithAccess:
                    if (!Confirm("Volanta already has live map access. Restart it anyway? That also reloads your "
                            + "friends list. Volanta is force-closed and started again, and keeps tracking your flight.")) return;
                    if (!await ForceCloseVolantaAsync()) return;
                    break;
                case VolantaAccessState.RunningWithoutAccess:
                    if (!Confirm("Volanta is running without live map access. Restart it with access now? Volanta is "
                            + "force-closed and started again, and keeps tracking your flight.")) return;
                    if (!await ForceCloseVolantaAsync()) return;
                    break;
            }
            // Step two: start it with access, the link watching closely for its friends download.
            _link.ExpectLaunch();
            string? error = VolantaLauncher.StartWithAccess();
            _announcer.Announce(error ?? "Starting Volanta with live map access. It connects in a few seconds.");
        }
        catch (Exception ex)
        {
            Log.Warn("Volanta", $"Start or restart failed: {ex.Message}");
            _announcer.Announce($"Could not start Volanta: {ex.Message}");
        }
        finally
        {
            _starting = false;
            RefreshConnectionStatus();
        }
    }

    /// <summary>Step one of a restart. Asking Volanta to quit was not reliable (its close button only hides it in
    /// the notification area when its "show in tray" setting is on), so it is force-closed straight away. Windows
    /// refusing to end it leaves the pilot the Quit item of its notification-area menu.</summary>
    private async Task<bool> ForceCloseVolantaAsync()
    {
        _announcer.Announce("Closing Volanta.");
        if (await VolantaLauncher.ForceCloseAsync()) return true;
        _announcer.Announce("Volanta could not be closed. Quit it from the notification area: press Windows+B, find "
            + "Volanta, open its menu with the Applications key, and choose Quit. Then press this button again.");
        return false;
    }

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

    private bool Confirm(string question)
        => MessageBox.Show(this, question, "Restart Volanta", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    /// <summary>Marshal to the UI thread; BeginInvoke can still throw while the handle is being destroyed
    /// (A380C-6), so the check alone is not enough.</summary>
    private void SafeBeginInvoke(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { /* the window is closing */ }
    }
}
