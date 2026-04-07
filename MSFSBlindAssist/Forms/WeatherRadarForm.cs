using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;

public class WeatherRadarForm : Form
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimConnectManager _simConnect;
    private readonly IntPtr _previousWindow;

    private Label _currentWeatherLabel = null!;
    private TextBox _currentWeatherBox = null!;
    private Label _advisoriesLabel = null!;
    private TextBox _advisoriesBox = null!;
    private Label _windsAloftLabel = null!;
    private TextBox _windsAloftBox = null!;
    private Label _statusLabel = null!;
    private Button _refreshButton = null!;
    private Button _closeButton = null!;

    private bool _isFetching = false;

    public WeatherRadarForm(ScreenReaderAnnouncer announcer, SimConnectManager simConnect)
    {
        _previousWindow = GetForegroundWindow();
        _announcer = announcer;
        _simConnect = simConnect;
        InitializeComponent();
        SetupAccessibility();
    }

    public void ShowForm()
    {
        Show();
    }

    private void InitializeComponent()
    {
        Text = "Weather Radar";
        Size = new Size(600, 680);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        KeyPreview = true;

        // ── Current position weather ──────────────────────────────────────
        _currentWeatherLabel = new Label
        {
            Text = "Weather at Current Position:",
            Location = new Point(12, 12),
            Size = new Size(570, 20),
            AccessibleName = "Weather at Current Position label"
        };

        _currentWeatherBox = new TextBox
        {
            Location = new Point(12, 36),
            Size = new Size(566, 100),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
            Text = "Press F5 or Refresh to fetch weather data.",
            AccessibleName = "Weather at Current Position",
            AccessibleDescription = "Ambient weather conditions at aircraft position from simulator"
        };

        // ── Advisories (SIGMETs, AIRMETs, PIREPs) ────────────────────────
        _advisoriesLabel = new Label
        {
            Text = "Nearby Advisories (SIGMETs / AIRMETs / PIREPs):",
            Location = new Point(12, 148),
            Size = new Size(570, 20),
            AccessibleName = "Nearby Advisories label"
        };

        _advisoriesBox = new TextBox
        {
            Location = new Point(12, 172),
            Size = new Size(566, 210),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
            Text = "Press F5 or Refresh to fetch advisories.",
            AccessibleName = "Nearby Advisories",
            AccessibleDescription = "Active SIGMETs, AIRMETs, and pilot reports near the aircraft"
        };

        // ── Winds Aloft ───────────────────────────────────────────────────
        _windsAloftLabel = new Label
        {
            Text = "Winds Aloft (±5000 ft):",
            Location = new Point(12, 394),
            Size = new Size(570, 20),
            AccessibleName = "Winds Aloft label"
        };

        _windsAloftBox = new TextBox
        {
            Location = new Point(12, 418),
            Size = new Size(566, 170),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
            Text = "Press F5 or Refresh to fetch winds aloft.",
            AccessibleName = "Winds Aloft",
            AccessibleDescription = "Forecast wind direction and speed at each 1000 ft from 5000 ft below to 5000 ft above aircraft altitude"
        };

        // ── Status + buttons ──────────────────────────────────────────────
        _statusLabel = new Label
        {
            Location = new Point(12, 604),
            Size = new Size(370, 20),
            Text = "",
            AccessibleName = "Status"
        };

        _refreshButton = new Button
        {
            Text = "&Refresh (F5)",
            Location = new Point(390, 598),
            Size = new Size(100, 28),
            AccessibleName = "Refresh",
            AccessibleDescription = "Fetch current weather, advisories, and winds aloft"
        };
        _refreshButton.Click += (s, e) => _ = RefreshAsync(forceRefresh: true);

        _closeButton = new Button
        {
            Text = "&Close",
            Location = new Point(500, 598),
            Size = new Size(78, 28),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close",
            AccessibleDescription = "Close weather radar window"
        };
        _closeButton.Click += CloseButton_Click;

        Controls.AddRange(new Control[]
        {
            _currentWeatherLabel, _currentWeatherBox,
            _advisoriesLabel, _advisoriesBox,
            _windsAloftLabel, _windsAloftBox,
            _statusLabel, _refreshButton, _closeButton
        });

        CancelButton = _closeButton;
    }

    private void SetupAccessibility()
    {
        _currentWeatherBox.TabIndex = 0;
        _advisoriesBox.TabIndex = 1;
        _windsAloftBox.TabIndex = 2;
        _refreshButton.TabIndex = 3;
        _closeButton.TabIndex = 4;

        Load += async (s, e) =>
        {
            BringToFront(); Activate();
            _currentWeatherBox.Focus();
            await RefreshAsync(forceRefresh: true);
        };

        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.F5) { e.Handled = true; _ = RefreshAsync(forceRefresh: true); }
        };
    }

    private async Task RefreshAsync(bool forceRefresh)
    {
        if (_isFetching) return;
        _isFetching = true;
        SetStatus("Fetching weather data...");
        _refreshButton.Enabled = false;

        try
        {
            // Get aircraft position (needed for advisories and winds aloft)
            (double lat, double lon, int altFt) = await GetPositionAsync();

            // Fetch all three in parallel
            var ambientTask    = FetchAmbientAsync();
            var advisoriesTask = FetchAdvisoriesAsync(lat, lon, forceRefresh);
            var windsTask      = FetchWindsAloftAsync(lat, lon, altFt, forceRefresh);

            await Task.WhenAll(ambientTask, advisoriesTask, windsTask);

            _currentWeatherBox.Text = ambientTask.Result;
            _advisoriesBox.Text     = advisoriesTask.Result;
            _windsAloftBox.Text     = windsTask.Result;

            SetStatus($"Last updated: {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            _isFetching = false;
            _refreshButton.Enabled = true;
        }
    }

    // ── Position helper ───────────────────────────────────────────────────────

    private async Task<(double lat, double lon, int altFt)> GetPositionAsync()
    {
        var last = _simConnect.LastKnownPosition;
        double lat = last?.Latitude ?? 0;
        double lon = last?.Longitude ?? 0;
        int altFt  = (int)(last?.Altitude ?? 0);

        if (lat == 0 && lon == 0 && _simConnect.IsConnected)
        {
            var tcs = new TaskCompletionSource<SimConnectManager.AircraftPosition>();
            _simConnect.RequestAircraftPositionAsync(p => tcs.TrySetResult(p));
            _ = Task.Delay(5000).ContinueWith(_ => tcs.TrySetResult(default));
            var pos = await tcs.Task;
            lat   = pos.Latitude;
            lon   = pos.Longitude;
            altFt = (int)pos.Altitude;
        }

        return (lat, lon, altFt);
    }

    // ── Ambient weather ───────────────────────────────────────────────────────

    private Task<string> FetchAmbientAsync()
    {
        var tcs = new TaskCompletionSource<string>();

        if (!_simConnect.IsConnected)
        { tcs.SetResult("Not connected to simulator."); return tcs.Task; }

        _simConnect.RequestWeatherInfo(data =>
        {
            string text = WeatherService.FormatAmbientWeather(data);
            if (IsHandleCreated && !IsDisposed) Invoke(() => tcs.TrySetResult(text));
            else tcs.TrySetResult(text);
        });

        _ = Task.Delay(5000).ContinueWith(_ =>
            tcs.TrySetResult("Timed out waiting for simulator weather data."));

        return tcs.Task;
    }

    // ── Advisories (SIGMETs + PIREPs) ────────────────────────────────────────

    private async Task<string> FetchAdvisoriesAsync(double lat, double lon, bool forceRefresh)
    {
        if (lat == 0 && lon == 0)
            return "Aircraft position unavailable — connect to simulator first.";

        var settings     = SettingsManager.Current;
        int displayRange = Math.Max(settings.SigmetProximityRangeNm, 300);
        bool decode      = settings.DecodeWeatherAdvisories;

        var sigmetTask = WeatherService.GetNearbyAdvisoriesAsync(lat, lon, displayRange, forceRefresh);
        List<WeatherPirep>? pireps = null;
        string? pirepError = null;
        try
        {
            pireps = await WeatherService.GetNearbyPirepsAsync(lat, lon, displayRange, forceRefresh);
        }
        catch (Exception ex)
        {
            pirepError = ex.Message;
        }

        var sigmets = await sigmetTask;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Position: {lat:F2}°, {lon:F2}° | Range: {displayRange} nm");
        sb.AppendLine(new string('─', 58));

        if (sigmets.Count > 0)
        {
            sb.AppendLine($"SIGMETs / AIRMETs ({sigmets.Count}):");
            foreach (var adv in sigmets)
            {
                string qualifier = decode && !string.IsNullOrEmpty(adv.Qualifier)
                    ? WeatherService.DecodeQualifier(adv.Qualifier) : adv.Qualifier;
                string altRange = decode
                    ? WeatherService.DecodeAltitudeRange(adv.AltLowFt, adv.AltHighFt)
                    : adv.AltitudeRange;

                sb.AppendLine($"[{adv.AdvisoryType}] {adv.HazardLabel}" +
                    (string.IsNullOrEmpty(qualifier) ? "" : $" — {qualifier}"));
                if (!string.IsNullOrEmpty(altRange))
                    sb.AppendLine($"  Altitude: {altRange}");
                sb.AppendLine($"  {adv.BearingDeg:F0}° / {adv.DistanceNm:F0} nm" +
                    (adv.ValidFrom.Length > 0 ? $"  |  {adv.ValidFrom}–{adv.ValidTo}" : ""));
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("No active SIGMETs or AIRMETs found.");
            sb.AppendLine();
        }

        if (pirepError != null)
        {
            sb.AppendLine($"Pilot Reports: fetch error — {pirepError}");
        }
        else if (pireps == null || pireps.Count == 0)
        {
            sb.AppendLine($"No pilot reports (turbulence/icing) found within {displayRange} nm.");
        }
        else
        {
            sb.AppendLine($"Pilot Reports ({pireps.Count}):");
            foreach (var p in pireps)
            {
                string hazard = decode ? WeatherService.DecodePirepHazard(p) : p.HazardSummary;
                string alt    = decode ? $"{p.AltitudeFt:N0} ft" : $"FL{p.AltitudeFt / 100:D3}";

                sb.AppendLine($"[PIREP] {hazard} — {alt}");
                sb.AppendLine($"  {p.BearingDeg:F0}° / {p.DistanceNm:F0} nm" +
                    (p.ObsTime.Length > 0 ? $"  |  {p.ObsTime}" : "") +
                    (p.AircraftType.Length > 0 ? $"  |  {p.AircraftType}" : ""));
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    // ── Winds aloft ───────────────────────────────────────────────────────────

    private async Task<string> FetchWindsAloftAsync(double lat, double lon, int altFt, bool forceRefresh)
    {
        if (lat == 0 && lon == 0)
            return "Aircraft position unavailable — connect to simulator first.";

        var winds = await WeatherService.GetWindsAloftAsync(lat, lon, altFt, forceRefresh);

        if (winds.Count == 0)
            return "Winds aloft data unavailable.";

        var sb = new System.Text.StringBuilder();
        int acFl = altFt / 100;
        sb.AppendLine($"Aircraft: FL{acFl:D3} ({altFt} ft)  |  forecast winds:");
        sb.AppendLine(new string('─', 38));

        foreach (var w in winds)
        {
            int fl = w.AltitudeFt / 100;
            string marker = Math.Abs(w.AltitudeFt - altFt) < 500 ? " ◄" : "";
            sb.AppendLine($"FL{fl:D3} ({w.AltitudeFt,6:N0} ft):  {w.DirectionDeg:F0}° / {w.SpeedKts:F0} kts{marker}");
        }

        return sb.ToString().TrimEnd();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetStatus(string text)
    {
        if (IsHandleCreated && !IsDisposed) _statusLabel.Text = text;
    }

    private void CloseButton_Click(object? sender, EventArgs e)
    {
        Close();
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }
}
