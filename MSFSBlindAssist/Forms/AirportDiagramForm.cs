using System.Text;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Accessible AIRPORT DIAGRAM (Ctrl+Shift+D, input mode) — lets a blind pilot understand the
/// airport layout the way a sighted pilot reads the ND/chart, and PLAN a taxi route so they can
/// anticipate an ATC clearance ("from this stand to runway 22 you'd taxi A, B, hold short 04, K").
///
/// GLOBAL (any aircraft) — it reuses the existing taxi-guidance engine (the same TaxiGraph the
/// taxi guidance / landing-exit planner build from the user's navdata DB): TaxiGraph.BuildAsync
/// for the layout + TaxiRouter for the route, WITHOUT activating live guidance/tones. So the
/// route it shows is the "expected ATC routing", static and read-only.
///
/// Layout (native accessible — combos/lists + read-only readouts, no map):
///   • ICAO box (auto-filled from the nearest airport) → builds the graph.
///   • Overview readout: runways (length + heading), taxiway count, stand count.
///   • Browse: a category combo (Runways / Taxiways / Stands) → a list → a detail readout
///     (runway = length/heading/width/surface; taxiway = what it connects to; stand = name).
///   • Plan route: From (my position / a stand) + To (a runway / a stand) → a described,
///     turn-grouped route with hold-shorts + total distance.
///   • Where am I: the current taxiway / stand / runway from the live position.
/// </summary>
public sealed class AirportDiagramForm : Form
{
    private readonly IAirportDataProvider _provider;
    private readonly ScreenReaderAnnouncer _announcer;

    private double _lat, _lon, _heading;
    private string _nearestIcao = "";

    private TaxiGraph? _graph;
    private string _currentIcao = "";
    private List<Runway> _runways = new();
    private readonly Dictionary<string, Runway> _runwayByLabel = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _standNodeByName = new(StringComparer.OrdinalIgnoreCase);

    private TextBox _icao = null!;
    private Label _status = null!;
    private TextBox _overview = null!;
    private ComboBox _category = null!;
    private ListBox _items = null!;
    private TextBox _detail = null!;
    private ComboBox _from = null!;
    private ComboBox _to = null!;
    private Button _planBtn = null!;
    private TextBox _route = null!;
    private Button _whereBtn = null!;

    public AirportDiagramForm(IAirportDataProvider provider, ScreenReaderAnnouncer announcer)
    {
        _provider = provider;
        _announcer = announcer;
        BuildUi();
    }

    public void SetAircraftPosition(double lat, double lon, double heading, string nearestIcao)
    {
        _lat = lat; _lon = lon; _heading = heading; _nearestIcao = nearestIcao ?? "";
    }

    private void BuildUi()
    {
        Text = "Airport Diagram";
        Size = new Size(700, 640);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        KeyPreview = true;

        var icaoLabel = new Label { Text = "&Airport ICAO:", Location = new Point(12, 14), Size = new Size(90, 22) };
        _icao = new TextBox { Location = new Point(106, 11), Size = new Size(90, 24), AccessibleName = "Airport ICAO", CharacterCasing = CharacterCasing.Upper };
        _icao.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; _ = LoadAirportAsync(_icao.Text.Trim()); } };
        _status = new Label { Location = new Point(206, 14), Size = new Size(470, 22), Text = "Enter an ICAO and press Enter.", AccessibleName = "Status" };

        var ovLabel = new Label { Text = "&Overview:", Location = new Point(12, 42), Size = new Size(200, 18) };
        _overview = new TextBox
        {
            Location = new Point(12, 62), Size = new Size(664, 120),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10), AccessibleName = "Airport overview", Text = ""
        };

        var catLabel = new Label { Text = "&Browse:", Location = new Point(12, 190), Size = new Size(60, 22) };
        _category = new ComboBox { Location = new Point(76, 187), Size = new Size(140, 24), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Browse category" };
        _category.Items.AddRange(new object[] { "Runways", "Taxiways", "Stands" });
        _category.SelectedIndex = 0;
        _category.SelectedIndexChanged += (_, _) => PopulateItems();

        var itemsLabel = new Label { Text = "&Items:", Location = new Point(230, 190), Size = new Size(50, 22) };
        _items = new ListBox { Location = new Point(284, 187), Size = new Size(160, 120), AccessibleName = "Items" };
        _items.SelectedIndexChanged += (_, _) => ShowItemDetail();

        var detailLabel = new Label { Text = "&Detail:", Location = new Point(456, 190), Size = new Size(60, 18) };
        _detail = new TextBox
        {
            Location = new Point(456, 209), Size = new Size(220, 98),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10), AccessibleName = "Item detail", Text = ""
        };

        var fromLabel = new Label { Text = "Route &from:", Location = new Point(12, 322), Size = new Size(80, 22) };
        _from = new ComboBox { Location = new Point(96, 319), Size = new Size(180, 24), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Route from" };
        var toLabel = new Label { Text = "&to:", Location = new Point(286, 322), Size = new Size(30, 22) };
        _to = new ComboBox { Location = new Point(320, 319), Size = new Size(180, 24), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Route to" };
        _planBtn = new Button { Text = "&Plan route", Location = new Point(510, 318), Size = new Size(120, 26), AccessibleName = "Plan route" };
        _planBtn.Click += (_, _) => PlanRoute();

        var routeLabel = new Label { Text = "&Route:", Location = new Point(12, 350), Size = new Size(200, 18) };
        _route = new TextBox
        {
            Location = new Point(12, 370), Size = new Size(664, 150),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10), AccessibleName = "Planned route", Text = ""
        };

        _whereBtn = new Button { Text = "&Where am I", Location = new Point(12, 530), Size = new Size(120, 30), AccessibleName = "Where am I" };
        _whereBtn.Click += (_, _) => WhereAmI();
        var close = new Button { Text = "Close", Location = new Point(140, 530), Size = new Size(100, 30), AccessibleName = "Close" };
        close.Click += (_, _) => Hide();

        Controls.AddRange(new Control[]
        {
            icaoLabel, _icao, _status, ovLabel, _overview,
            catLabel, _category, itemsLabel, _items, detailLabel, _detail,
            fromLabel, _from, toLabel, _to, _planBtn, routeLabel, _route,
            _whereBtn, close
        });

        int t = 1;
        _icao.TabIndex = t++; _overview.TabIndex = t++;
        _category.TabIndex = t++; _items.TabIndex = t++; _detail.TabIndex = t++;
        _from.TabIndex = t++; _to.TabIndex = t++; _planBtn.TabIndex = t++; _route.TabIndex = t++;
        _whereBtn.TabIndex = t++; close.TabIndex = t;

        SetBusy(false);
    }

    public void ShowForm()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
        // Auto-load the nearest airport on open if we have one and haven't already.
        if (!string.IsNullOrEmpty(_nearestIcao) && !_nearestIcao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase))
        {
            _icao.Text = _nearestIcao;
            _ = LoadAirportAsync(_nearestIcao);
        }
        ActiveControl = _icao;
        _icao.Focus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Hide(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---- airport load + graph build ----------------------------------------

    private async System.Threading.Tasks.Task LoadAirportAsync(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return;
        icao = icao.ToUpperInvariant();
        if (icao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase) && _graph != null) return;

        if (!_provider.AirportExists(icao)) { _status.Text = $"Airport {icao} not found in the database."; return; }

        var paths = _provider.GetTaxiPaths(icao);
        if (paths.Count == 0) { _status.Text = $"No taxiway data for {icao} in this database."; return; }

        _status.Text = $"{icao}: building airport diagram…";
        SetBusy(true);
        TaxiGraph? built = null;
        try
        {
            var parking = _provider.GetParkingSpots(icao);
            var starts = _provider.GetRunwayStarts(icao);
            built = await TaxiGraph.BuildAsync(paths, parking, starts);
        }
        catch (Exception ex) { if (!IsDisposed) _status.Text = $"Could not build {icao}: {ex.Message}"; }
        finally { if (!IsDisposed && !Disposing) SetBusy(false); }

        if (IsDisposed || Disposing || built == null) return;

        _graph = built;
        _currentIcao = icao;
        _runways = _provider.GetRunways(icao).Where(r => !r.IsClosed).ToList();

        BuildStandMap();
        RenderOverview();
        PopulateItems();
        PopulateRouteCombos();
        _status.Text = $"{icao}: {_runways.Count} runways, {GetTaxiways().Count} taxiways, {_standNodeByName.Count} stands.";
        _announcer?.Announce($"{icao} loaded.");
    }

    private void SetBusy(bool busy)
    {
        _planBtn.Enabled = !busy && _graph != null;
        _whereBtn.Enabled = !busy && _graph != null;
    }

    private void BuildStandMap()
    {
        _standNodeByName.Clear();
        if (_graph == null) return;
        foreach (var n in _graph.Nodes.Values)
        {
            if (n.Type == TaxiNodeType.Parking && !string.IsNullOrWhiteSpace(n.ParkingName)
                && !n.ParkingName.StartsWith("Runway", StringComparison.OrdinalIgnoreCase)
                && !_standNodeByName.ContainsKey(n.ParkingName))
                _standNodeByName[n.ParkingName] = n.NodeId;
        }
    }

    private List<string> GetTaxiways() => _graph?.GetAllTaxiwayNames() ?? new();

    // ---- overview + browse -------------------------------------------------

    private void RenderOverview()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Airport {_currentIcao}.");
        sb.AppendLine($"{_runways.Count} runways:");
        foreach (var r in _runways)
            sb.AppendLine($"  {r.RunwayID}: {Len(r.Length)}, heading {r.HeadingMag:000} magnetic.");
        sb.AppendLine($"{GetTaxiways().Count} taxiways, {_standNodeByName.Count} stands.");
        DisplayText.SetPreserveCaret(_overview, sb.ToString().TrimEnd());
    }

    private void PopulateItems()
    {
        if (_graph == null) return;
        _items.BeginUpdate();
        _items.Items.Clear();
        string cat = _category.SelectedItem as string ?? "Runways";
        if (cat == "Runways") foreach (var r in _runways) _items.Items.Add(r.RunwayID);
        else if (cat == "Taxiways") foreach (var t in GetTaxiways()) _items.Items.Add(t);
        else foreach (var s in _standNodeByName.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) _items.Items.Add(s);
        _items.EndUpdate();
        if (_items.Items.Count > 0) _items.SelectedIndex = 0; else DisplayText.SetPreserveCaret(_detail, "");
    }

    private void ShowItemDetail()
    {
        if (_graph == null || _items.SelectedItem is not string sel) return;
        string cat = _category.SelectedItem as string ?? "Runways";
        var sb = new StringBuilder();
        if (cat == "Runways")
        {
            var r = _runways.FirstOrDefault(x => x.RunwayID == sel);
            if (r != null)
            {
                sb.AppendLine($"Runway {r.RunwayID}.");
                sb.AppendLine($"Length: {Len(r.Length)}.");
                sb.AppendLine($"Width: {Len(r.Width)}.");
                sb.AppendLine($"Heading: {r.HeadingMag:000} magnetic, {r.Heading:000} true.");
                sb.AppendLine($"Surface: {r.GetSurfaceType()}.");
            }
        }
        else if (cat == "Taxiways")
        {
            sb.AppendLine($"Taxiway {sel}.");
            var conn = _graph.GetReachableTaxiwayNames(sel, 1);
            conn.Remove(sel);
            sb.AppendLine(conn.Count > 0
                ? "Connects to: " + string.Join(", ", conn) + "."
                : "No directly connected taxiways found in the data.");
        }
        else
        {
            sb.AppendLine($"Stand {sel}.");
            if (_standNodeByName.TryGetValue(sel, out var nid) && _graph.Nodes.TryGetValue(nid, out var node))
            {
                var twys = node.TaxiwayNames.Where(x => x.Length > 0).ToList();
                sb.AppendLine(twys.Count > 0 ? "On/near taxiway: " + string.Join(", ", twys) + "." : "Apron stand.");
            }
        }
        DisplayText.SetPreserveCaret(_detail, sb.ToString().TrimEnd());
    }

    // ---- route planning ----------------------------------------------------

    private void PopulateRouteCombos()
    {
        _from.BeginUpdate(); _from.Items.Clear();
        _from.Items.Add("My position");
        foreach (var s in _standNodeByName.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) _from.Items.Add("Stand: " + s);
        _from.EndUpdate(); if (_from.Items.Count > 0) _from.SelectedIndex = 0;

        _to.BeginUpdate(); _to.Items.Clear();
        foreach (var r in _runways) _to.Items.Add("Runway: " + r.RunwayID);
        foreach (var s in _standNodeByName.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) _to.Items.Add("Stand: " + s);
        _to.EndUpdate(); if (_to.Items.Count > 0) _to.SelectedIndex = 0;
    }

    private void PlanRoute()
    {
        if (_graph == null) return;
        int startId = ResolveFrom(out string fromName);
        int endId = ResolveTo(out string toName);
        if (startId <= 0) { _announcer?.Announce("Could not resolve the start point."); return; }
        if (endId <= 0) { _announcer?.Announce("Could not resolve the destination."); return; }
        if (startId == endId) { _announcer?.Announce("Start and destination are the same."); return; }

        var router = new TaxiRouter(_graph);
        var route = router.FindShortestPath(startId, endId);
        if (route == null || route.Segments.Count == 0)
        {
            string msg = "No taxi route found between those points in the data.";
            DisplayText.SetPreserveCaret(_route, msg);
            _announcer?.Announce(msg);
            return;
        }
        string text = DescribeRoute(route, fromName, toName);
        DisplayText.SetPreserveCaret(_route, text);
        _announcer?.Announce(RouteSummary(route, fromName, toName));
    }

    private int ResolveFrom(out string name)
    {
        name = _from.SelectedItem as string ?? "";
        if (_graph == null) return -1;
        if (name == "My position")
        {
            var n = _graph.FindNearestNode(_lat, _lon);
            name = "your position";
            return n?.NodeId ?? -1;
        }
        if (name.StartsWith("Stand: ", StringComparison.Ordinal))
        {
            string s = name.Substring(7);
            name = "stand " + s;
            return _standNodeByName.TryGetValue(s, out var id) ? id : -1;
        }
        return -1;
    }

    private int ResolveTo(out string name)
    {
        name = _to.SelectedItem as string ?? "";
        if (_graph == null) return -1;
        if (name.StartsWith("Runway: ", StringComparison.Ordinal))
        {
            string rid = name.Substring(8);
            name = "runway " + rid;
            var r = _runways.FirstOrDefault(x => x.RunwayID == rid);
            if (r == null) return -1;
            var n = _graph.FindNearestNode(r.StartLat, r.StartLon);
            return n?.NodeId ?? -1;
        }
        if (name.StartsWith("Stand: ", StringComparison.Ordinal))
        {
            string s = name.Substring(7);
            name = "stand " + s;
            return _standNodeByName.TryGetValue(s, out var id) ? id : -1;
        }
        return -1;
    }

    // Group consecutive segments by taxiway, surface hold-shorts, total distance.
    private static string DescribeRoute(TaxiRoute route, string from, string to)
    {
        var legs = new List<string>();
        string last = " ";
        foreach (var s in route.Segments)
        {
            string twy = string.IsNullOrEmpty(s.TaxiwayName) ? "ramp" : ("taxiway " + s.TaxiwayName);
            if (twy != last) { legs.Add(twy); last = twy; }
            if (s.IsHoldShortPoint && !string.IsNullOrEmpty(s.HoldShortRunway))
            {
                legs.Add("hold short of " + s.HoldShortRunway);
                last = " ";   // re-name the taxiway after a runway crossing
            }
        }
        var sb = new StringBuilder();
        sb.AppendLine($"From {from} to {to}:");
        sb.AppendLine(legs.Count > 0 ? string.Join(", then ", legs) + "." : "(direct).");
        double m = route.TotalDistanceMeters;
        sb.AppendLine($"Total distance about {Math.Round(m)} metres ({Math.Round(m * 3.280839895)} feet).");
        if (!string.IsNullOrEmpty(route.ConstrainedFallbackReason))
            sb.AppendLine("Note: shortest path used.");
        sb.AppendLine();
        sb.AppendLine("Suggested routing — ATC may clear you differently.");
        return sb.ToString().TrimEnd();
    }

    private static string RouteSummary(TaxiRoute route, string from, string to)
    {
        var twys = new List<string>();
        string last = "";
        foreach (var s in route.Segments)
        {
            string t = string.IsNullOrEmpty(s.TaxiwayName) ? "" : s.TaxiwayName;
            if (t.Length > 0 && t != last) { twys.Add(t); last = t; }
        }
        return $"Route to {to} via {(twys.Count > 0 ? string.Join(", ", twys) : "ramp")}. {Math.Round(route.TotalDistanceMeters)} metres.";
    }

    private void WhereAmI()
    {
        if (_graph == null) { _announcer?.AnnounceImmediate("No airport loaded."); return; }
        string where = _graph.DescribeLocation(_lat, _lon);
        _announcer?.AnnounceImmediate(string.IsNullOrEmpty(where) ? "Position not on the airport diagram." : where);
    }

    // Runway Length/Width come from the navdata in FEET — convert to metres for display.
    private static string Len(double feet)
        => feet <= 0 ? "unknown" : $"{Math.Round(feet / 3.280839895)} metres ({Math.Round(feet)} feet)";
}
