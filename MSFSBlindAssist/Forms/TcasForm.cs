using System.Text.RegularExpressions;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// TCAS traffic display — two GroupBox+ListBox pairs (Airborne / On Ground).
///
/// Screen-reader strategy:
///   • GroupBox captions include live counts; NVDA announces them on tab.
///   • Each aircraft is a single flat line — no tree hierarchy.
///   • The UI never auto-refreshes while the window is open (avoids NVDA focus
///     jumping). The TcasService poll timer keeps _current fresh in the background.
///     Press F5 to apply the latest snapshot to the display.
/// </summary>
public class TcasForm : Form
{
    private readonly TcasService           _tcas;
    private readonly ScreenReaderAnnouncer _announcer;

    private GroupBox _airborneGroup = null!;
    private GroupBox _groundGroup   = null!;
    private ListBox  _airborneList  = null!;
    private ListBox  _groundList    = null!;
    private Label    _status        = null!;

    private ContextMenuStrip  _ctxMenu        = null!;
    private ToolStripMenuItem _addMenuItem    = null!;
    private ToolStripMenuItem _removeMenuItem = null!;

    // Wrapper so ListBox.Items stores the TcasTraffic alongside its display text.
    private sealed record AircraftItem(TcasTraffic Traffic, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    public TcasForm(TcasService tcas, ScreenReaderAnnouncer announcer)
    {
        _tcas      = tcas;
        _announcer = announcer;
        BuildUI();

        FormClosed += (_, _) => _tcas.Stop();

        _tcas.Start();
        ApplyRefresh();
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildUI()
    {
        Text          = "TCAS Traffic";
        Width         = 620;
        Height        = 600;
        MinimumSize   = new Size(400, 400);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview    = true;

        KeyDown += OnFormKeyDown;

        _status = new Label
        {
            Dock           = DockStyle.Top,
            Text           = "Scanning for traffic…",
            AutoSize       = false,
            Height         = 22,
            AccessibleName = "Status",
            TabStop        = false,
        };

        // ── Context menu ──────────────────────────────────────────────────────
        _addMenuItem    = new ToolStripMenuItem("Add to track list");
        _removeMenuItem = new ToolStripMenuItem("Remove from track list");
        _addMenuItem.Click    += ContextMenu_AddToTrackList;
        _removeMenuItem.Click += ContextMenu_RemoveFromTrackList;

        _ctxMenu = new ContextMenuStrip();
        _ctxMenu.Items.Add(_addMenuItem);
        _ctxMenu.Items.Add(_removeMenuItem);
        _ctxMenu.Opening += ContextMenu_Opening;

        // ── Airborne section ──────────────────────────────────────────────────
        _airborneList = new ListBox
        {
            Dock                  = DockStyle.Fill,
            HorizontalScrollbar   = true,
            AccessibleDescription = "Airborne aircraft sorted by distance. Press F5 to refresh.",
            ContextMenuStrip      = _ctxMenu,
        };

        _airborneGroup = new GroupBox
        {
            Dock    = DockStyle.Fill,
            Text    = "Airborne Traffic — none",
            Padding = new Padding(4, 16, 4, 4),
        };
        _airborneGroup.Controls.Add(_airborneList);

        // ── On Ground section ─────────────────────────────────────────────────
        _groundList = new ListBox
        {
            Dock                  = DockStyle.Fill,
            HorizontalScrollbar   = true,
            AccessibleDescription = "On-ground aircraft sorted by distance. Press F5 to refresh.",
            ContextMenuStrip      = _ctxMenu,
        };

        _groundGroup = new GroupBox
        {
            Dock    = DockStyle.Fill,
            Text    = "On Ground Traffic — none",
            Padding = new Padding(4, 16, 4, 4),
        };
        _groundGroup.Controls.Add(_groundList);

        var layout = new TableLayoutPanel
        {
            Dock     = DockStyle.Fill,
            RowCount = 2,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        layout.Controls.Add(_airborneGroup, 0, 0);
        layout.Controls.Add(_groundGroup,   0, 1);

        Controls.Add(layout);
        Controls.Add(_status);
    }

    // ── Keyboard handling ─────────────────────────────────────────────────────

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5)
        {
            ApplyRefresh();
            e.Handled = true;
        }
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the current cached snapshot from TcasService and rebuilds both lists.
    /// Called on open and on F5. The service poll timer keeps the snapshot fresh
    /// every 3 s in the background regardless of whether this window is open.
    /// </summary>
    private void ApplyRefresh()
    {
        if (IsDisposed) return;

        var airborne = _tcas.GetTraffic(onGround: false);
        var ground   = _tcas.GetTraffic(onGround: true);

        RebuildList(_airborneList, airborne);
        _airborneGroup.Text = airborne.Count == 0
            ? "Airborne Traffic — none"
            : $"Airborne Traffic — {airborne.Count} aircraft";

        RebuildList(_groundList, ground);
        _groundGroup.Text = ground.Count == 0
            ? "On Ground Traffic — none"
            : $"On Ground Traffic — {ground.Count} aircraft";

        int total = airborne.Count + ground.Count;
        _status.Text = total == 0
            ? "No traffic in range."
            : $"{total} nearby: {airborne.Count} airborne, {ground.Count} on ground.";
    }

    // ── List rebuild ──────────────────────────────────────────────────────────

    private static void RebuildList(ListBox list, IReadOnlyList<TcasTraffic> traffic)
    {
        string? selectedKey = (list.SelectedItem as AircraftItem)?.Traffic
            .Let(t => TrafficKey(t));

        list.BeginUpdate();
        list.Items.Clear();
        int restoreIndex = -1;

        for (int i = 0; i < traffic.Count; i++)
        {
            var t    = traffic[i];
            var item = new AircraftItem(t, BuildItemText(t));
            list.Items.Add(item);
            if (TrafficKey(t) == selectedKey)
                restoreIndex = i;
        }

        if (restoreIndex >= 0)
            list.SelectedIndex = restoreIndex;

        list.EndUpdate();
    }

    // ── Item text builder ─────────────────────────────────────────────────────

    private static string BuildItemText(TcasTraffic t)
    {
        string id   = string.IsNullOrEmpty(t.Callsign)
            ? $"unknown {t.ObjectId}"
            : FormatCallsign(t.Callsign);
        string type = ShortenAircraftType(t.AircraftType);

        var parts = new List<string>
        {
            id,
            t.RelativePositionSummary,
            $"{(int)t.GroundSpeedKnots} knots",
        };

        if (!string.IsNullOrEmpty(type))
            parts.Add($"type {type}");

        // Show origin→destination route when available (AI traffic with schedules)
        string route = FormatRoute(t.FromAirport, t.ToAirport);
        if (!string.IsNullOrEmpty(route))
            parts.Add(route);

        parts.Add($"heading {(int)t.HeadingMagnetic}");
        parts.Add($"{(int)t.AltitudeFt:N0} feet");

        return string.Join(" — ", parts);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Formats origin→destination route string. Returns empty if neither is available.
    /// </summary>
    private static string FormatRoute(string from, string to)
    {
        bool hasFrom = !string.IsNullOrEmpty(from);
        bool hasTo   = !string.IsNullOrEmpty(to);
        if (hasFrom && hasTo) return $"{from} to {to}";
        if (hasFrom)          return $"from {from}";
        if (hasTo)            return $"to {to}";
        return "";
    }

    private static string TrafficKey(TcasTraffic t) =>
        string.IsNullOrEmpty(t.Callsign) ? t.ObjectId.ToString() : t.Callsign;

    /// <summary>
    /// Inserts a space between the alpha airline prefix and the numeric flight number
    /// so NVDA reads "UAL 123" instead of spelling "U-A-L-1-2-3".
    /// Leaves registrations (N12345, G-ABCD) and already-spaced strings unchanged.
    /// </summary>
    private static string FormatCallsign(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        raw = raw.Trim();
        if (raw.Contains(' ') || raw.Contains('-')) return raw;
        var m = Regex.Match(raw, @"^([A-Z]{2,4})(\d{1,4}[A-Z]?)$");
        if (m.Success)
            return $"{m.Groups[1].Value} {m.Groups[2].Value}";
        return raw;
    }

    /// <summary>
    /// Converts an aircraft type string to a concise ICAO identifier.
    /// </summary>
    private static string ShortenAircraftType(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        raw = raw.Trim();

        raw = Regex.Replace(raw, @"/[LJMHSUA]$", "").Trim();
        raw = raw.Replace('_', ' ').Replace('-', ' ').Trim();
        raw = Regex.Replace(raw, @"\s+", " ");

        string up = raw.ToUpperInvariant();

        // Bare ICAO code: 1-3 letters + 1-4 digits + 0-2 trailing letters (B738, B77W, A20N, MD11, CRJ9)
        if (up.Length <= 6 && !up.Contains(' ') &&
            Regex.IsMatch(up, @"^[A-Z]{1,3}\d{1,4}[A-Z]{0,2}$"))
            return up;

        // ICAO code embedded in a longer string ("A320" in "Airbus A320 Neo Leap")
        var m = Regex.Match(up, @"\b([A-Z]{1,3}\d{2,4}[A-Z]{0,2})\b");
        if (m.Success) return m.Value;

        // Pure digit model number — map to ICAO prefix
        m = Regex.Match(up, @"\b(\d{3,4})\b");
        if (m.Success)
        {
            string icao = m.Value switch
            {
                "737" or "738" or "739" => $"B{m.Value}",
                "747"                   => "B747",
                "757"                   => "B757",
                "767"                   => "B767",
                "777"                   => "B777",
                "787"                   => "B787",
                "319" or "320" or "321" => $"A{m.Value}",
                "330"                   => "A330",
                "340"                   => "A340",
                "350"                   => "A350",
                "380"                   => "A380",
                _                       => m.Value,
            };
            return icao;
        }

        string stripped = Regex.Replace(raw,
            @"^(Airbus|Boeing|Embraer|Bombardier|ATR|Cessna|Piper|McDonnell Douglas|Dassault|Pilatus|Beechcraft|Diamond|FSLTL|Asobo)\s+",
            "", RegexOptions.IgnoreCase).Trim();
        if (!string.IsNullOrEmpty(stripped) && stripped.Length <= 14) return stripped;

        return raw.Split(' ')[0];
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private void ContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var t = GetFocusedTraffic();
        if (t == null)
        {
            _addMenuItem.Enabled    = false;
            _removeMenuItem.Enabled = false;
            return;
        }
        bool tracked = _tcas.IsTracked(TrafficKey(t));
        _addMenuItem.Enabled    = !tracked;
        _removeMenuItem.Enabled = tracked;
    }

    private void ContextMenu_AddToTrackList(object? sender, EventArgs e)
    {
        var t = GetFocusedTraffic();
        if (t == null) { _announcer.AnnounceImmediate("No aircraft selected."); return; }
        string key = TrafficKey(t);
        _tcas.AddToTrackList(key);
        _announcer.AnnounceImmediate($"{FormatCallsign(key)} added to track list.");
    }

    private void ContextMenu_RemoveFromTrackList(object? sender, EventArgs e)
    {
        var t = GetFocusedTraffic();
        if (t == null) { _announcer.AnnounceImmediate("No aircraft selected."); return; }
        string key = TrafficKey(t);
        _tcas.RemoveFromTrackList(key);
        _announcer.AnnounceImmediate($"{FormatCallsign(key)} removed from track list.");
    }

    private TcasTraffic? GetFocusedTraffic()
    {
        foreach (var list in new[] { _airborneList, _groundList })
        {
            if (list.SelectedItem is AircraftItem item)
                return item.Traffic;
        }
        return null;
    }

    // ── Public helpers ────────────────────────────────────────────────────────

    public void ShowForm()
    {
        if (!Visible) Show();
        Activate();
        ApplyRefresh();           // show whatever is cached immediately
        _airborneList.Focus();
        _ = PollThenRefreshAsync(); // then get fresh data from SimConnect
    }

    private async Task PollThenRefreshAsync()
    {
        _tcas.PollNow();
        await Task.Delay(600);    // wait for SimConnect responses to arrive
        ApplyRefresh();
    }
}

// Small helper to allow fluent `.Let()` inline without a local variable
file static class ObjectExtensions
{
    public static TResult Let<T, TResult>(this T value, Func<T, TResult> fn) => fn(value);
}
