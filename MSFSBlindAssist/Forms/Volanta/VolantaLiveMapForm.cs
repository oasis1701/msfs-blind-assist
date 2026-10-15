using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Controls;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
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

    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly VolantaLink _link = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = RefreshIntervalMs };

    private AccessibleTabControl _tabs = null!;
    // Traffic
    private ListBox _trafficList = null!;
    private DisplayListBox _trafficDetails = null!;
    private TextBox _trafficStatus = null!;
    private ComboBox _distance = null!, _phase = null!, _altitude = null!;
    private CheckBox _srcVolanta = null!, _srcVatsim = null!, _srcIvao = null!, _srcPilotEdge = null!, _srcApoc = null!;
    private TextBox _search = null!;
    // Friends
    private ListBox _friendsList = null!;
    private DisplayListBox _friendsDetails = null!;
    private TextBox _friendsStatus = null!;
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
    private bool _trafficFilled, _starting;

    /// <summary>A list row: its key (flight or station id) and its text. Value equality, so an unchanged row is
    /// never rewritten.</summary>
    private sealed record Row(string Key, string Text)
    {
        public override string ToString() => Text;
    }

    public VolantaLiveMapForm(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _sim = sim;
        _announcer = announcer;
        BuildUI();

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
        _friendsList.SelectedIndexChanged += (_, _) => ShowFriendDetails();
        _friendsList.Enter += (_, _) => SelectFirstIfNone(_friendsList);

        _friendsDetails = NewDetailsList("Selected friend details",
            "The selected friend's status and, when they fly, everything known about their flight, one item per line.");

        _friendsStatus = ReadOnlyBox("Friends status");
        _friendsStatus.Text = "Waiting for Volanta.";

        var reload = new Button
        {
            Text = "Restart Volanta to &reload your friends",
            AutoSize = true,
            AccessibleName = "Restart Volanta to reload your friends",
        };
        reload.Click += async (_, _) => await StartVolantaAsync(reloadFriends: true);

        var layout = NewColumn(4);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_friendsList, 0, 0);
        layout.Controls.Add(_friendsDetails, 0, 1);
        layout.Controls.Add(_friendsStatus, 0, 2);
        layout.Controls.Add(reload, 0, 3);
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
        _trafficList.SelectedIndexChanged += (_, _) => ShowTrafficDetails();
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

        // The list first, then its details, then the status and the filters: the order the pilot tabs through.
        var layout = NewColumn(4);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_trafficList, 0, 0);
        layout.Controls.Add(_trafficDetails, 0, 1);
        layout.Controls.Add(_trafficStatus, 0, 2);
        layout.Controls.Add(filters, 0, 3);
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
        _atcList.SelectedIndexChanged += (_, _) => ShowAtcDetails();
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
        _startButton.Click += async (_, _) => await StartVolantaAsync(reloadFriends: false);

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
                "Friends: Volanta sends your friends list once, when it starts, so this app reads it when it starts Volanta, and keeps a copy of your friends' names for next time. Who is flying, and where, is always live. If your list looks out of date, use the Friends tab's restart button.",
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
                + "only when it starts: press Restart Volanta to reload your friends.");
            return;
        }

        var volantaFlights = snap.VolantaFlights?.Items ?? Array.Empty<VolantaFlight>();
        var own = OwnPosition(snap, now, out _);
        IReadOnlyList<VolantaTrafficEntry> placed = own is VolantaOwnPosition me
            ? VolantaTrafficCalculator.Compute(volantaFlights, me, now, snap.OwnFlight?.FlightId, snap.OwnVolantaPosition, snap.OwnUserId)
            : Array.Empty<VolantaTrafficEntry>();
        var entries = VolantaFriends.Arrange(friends, volantaFlights, placed);
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
                + "other statuses show once Volanta reports a change. To load the latest list, press Restart Volanta to reload your friends.";
        if (snap.VolantaFlights == null) status += " Waiting for Volanta's flights to see who is flying.";
        DisplayText.SetPreserveCaret(_friendsStatus, status);
    }

    private void ShowFriendDetails()
    {
        if (_friendsList.SelectedItem is Row row && _friendsByKey.TryGetValue(row.Key, out var e))
            _friendsDetails.SetText(VolantaTrafficFormatter.FriendDetails(e));
        else
            _friendsDetails.SetText(_friendsList.Items.Count == 0 ? "No friends listed." : "No friend selected.");
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
        var shown = CurrentFilter().Apply(entries);
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
        string warning = PausedWarning(snap, now);
        DisplayText.SetPreserveCaret(_trafficStatus, warning.Length > 0 ? warning + " " + status : status);
    }

    private void ShowTrafficDetails()
    {
        if (_trafficList.SelectedItem is Row row && _trafficByKey.TryGetValue(row.Key, out var e))
            _trafficDetails.SetText(VolantaTrafficFormatter.Details(e));
        else
            _trafficDetails.SetText(_trafficList.Items.Count == 0 ? "No aircraft listed." : "No aircraft selected.");
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
            _atcDetails.SetText(VolantaTrafficFormatter.ControllerDetails(r.Station, r.Nm));
        else
            _atcDetails.SetText(_atcList.Items.Count == 0 ? "No stations listed." : "No station selected.");
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
    private static void Reconcile(ListBox list, IReadOnlyList<Row> sorted, bool hold)
    {
        var shown = list.Items.Cast<object>().OfType<Row>().ToList();
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

    /// <param name="reloadFriends">The Friends tab's button: restart Volanta even when it has access, since
    /// Volanta sends the friends list only when it starts.</param>
    private async Task StartVolantaAsync(bool reloadFriends)
    {
        if (_starting) return;
        _starting = true;
        try
        {
            string forced = "";
            switch (await VolantaLauncher.DetectAsync())
            {
                case VolantaAccessState.NotInstalled:
                    _announcer.Announce("Volanta is not installed for this Windows user.");
                    return;
                case VolantaAccessState.RunningWithAccess when !reloadFriends:
                    _announcer.Announce("Volanta already has live map access.");
                    return;
                case VolantaAccessState.RunningWithAccess:
                    if (!Confirm("Restart Volanta to load your friends list? Volanta quits and starts again, and keeps "
                            + "tracking your flight. If it does not quit within a few seconds, it is force-closed.")) return;
                    _announcer.Announce("Restarting Volanta.");
                    if (!TryQuitResult(await VolantaLauncher.QuitAsync(), out forced)) return;
                    break;
                case VolantaAccessState.RunningWithoutAccess:
                    if (!Confirm("Volanta is running without live map access. Restart it with access now? Volanta keeps "
                            + "tracking your flight after the restart. If it does not quit within a few seconds, it is force-closed.")) return;
                    _announcer.Announce("Closing Volanta.");
                    if (!TryQuitResult(await VolantaLauncher.QuitAsync(), out forced)) return;
                    break;
            }
            _link.ExpectLaunch();
            string? error = VolantaLauncher.StartWithAccess();
            _announcer.Announce(error ?? forced + "Starting Volanta with live map access. It connects in a few seconds.");
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

    /// <summary>Whether the restart can go on after the quit, with what to say first when Volanta had to be
    /// force-closed. Windows refusing to end Volanta leaves the pilot the Quit item of its notification-area
    /// menu.</summary>
    private bool TryQuitResult(VolantaQuitResult result, out string forcedNote)
    {
        forcedNote = result == VolantaQuitResult.ForceClosed ? "Volanta did not quit on its own, so it was force-closed. " : "";
        if (result != VolantaQuitResult.StillRunning) return true;
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
