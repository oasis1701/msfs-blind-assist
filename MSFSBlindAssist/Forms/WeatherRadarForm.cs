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
        Size = new Size(600, 560);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        KeyPreview = true;

        // Current position weather
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
            Size = new Size(566, 120),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
            Text = "Press F5 or click Refresh to fetch weather data.",
            AccessibleName = "Weather at Current Position",
            AccessibleDescription = "Ambient weather conditions at aircraft position from simulator"
        };

        // Nearby advisories
        _advisoriesLabel = new Label
        {
            Text = "Nearby Weather Advisories (SIGMETs / AIRMETs):",
            Location = new Point(12, 172),
            Size = new Size(570, 20),
            AccessibleName = "Nearby Weather Advisories label"
        };

        _advisoriesBox = new TextBox
        {
            Location = new Point(12, 196),
            Size = new Size(566, 260),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
            Text = "Press F5 or click Refresh to fetch nearby advisories.",
            AccessibleName = "Nearby Weather Advisories",
            AccessibleDescription = "Active SIGMETs and AIRMETs near the aircraft from aviationweather.gov"
        };

        // Status label
        _statusLabel = new Label
        {
            Location = new Point(12, 470),
            Size = new Size(380, 20),
            Text = "",
            AccessibleName = "Status"
        };

        // Refresh button
        _refreshButton = new Button
        {
            Text = "&Refresh (F5)",
            Location = new Point(400, 464),
            Size = new Size(100, 28),
            AccessibleName = "Refresh",
            AccessibleDescription = "Fetch current weather and advisories"
        };
        _refreshButton.Click += (s, e) => _ = RefreshAsync(forceRefresh: true);

        // Close button
        _closeButton = new Button
        {
            Text = "&Close",
            Location = new Point(510, 464),
            Size = new Size(68, 28),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close",
            AccessibleDescription = "Close weather radar window"
        };
        _closeButton.Click += CloseButton_Click;

        Controls.AddRange(new Control[]
        {
            _currentWeatherLabel, _currentWeatherBox,
            _advisoriesLabel, _advisoriesBox,
            _statusLabel, _refreshButton, _closeButton
        });

        CancelButton = _closeButton;
    }

    private void SetupAccessibility()
    {
        _currentWeatherBox.TabIndex = 0;
        _advisoriesBox.TabIndex = 1;
        _refreshButton.TabIndex = 2;
        _closeButton.TabIndex = 3;

        Load += async (s, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false;
            _currentWeatherBox.Focus();
            await RefreshAsync(forceRefresh: false);
        };

        KeyDown += WeatherRadarForm_KeyDown;
    }

    private void WeatherRadarForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5)
        {
            e.Handled = true;
            _ = RefreshAsync(forceRefresh: true);
        }
    }

    private async Task RefreshAsync(bool forceRefresh)
    {
        if (_isFetching) return;
        _isFetching = true;

        SetStatus("Fetching weather data...");
        _refreshButton.Enabled = false;

        try
        {
            // Fetch ambient weather from SimConnect and advisories from aviationweather.gov in parallel
            Task<string> ambientTask = FetchAmbientWeatherAsync();
            Task<string> advisoriesTask = FetchAdvisoriesAsync(forceRefresh);

            await Task.WhenAll(ambientTask, advisoriesTask);

            _currentWeatherBox.Text = ambientTask.Result;
            _advisoriesBox.Text = advisoriesTask.Result;

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

    private Task<string> FetchAmbientWeatherAsync()
    {
        var tcs = new TaskCompletionSource<string>();

        if (!_simConnect.IsConnected)
        {
            tcs.SetResult("Not connected to simulator.");
            return tcs.Task;
        }

        _simConnect.RequestWeatherInfo(data =>
        {
            string text = WeatherService.FormatAmbientWeather(data);
            if (IsHandleCreated && !IsDisposed)
                Invoke(() => tcs.TrySetResult(text));
            else
                tcs.TrySetResult(text);
        });

        // Timeout after 5 seconds
        Task.Delay(5000).ContinueWith(_ =>
            tcs.TrySetResult("Timed out waiting for simulator weather data."));

        return tcs.Task;
    }

    private async Task<string> FetchAdvisoriesAsync(bool forceRefresh)
    {
        if (!_simConnect.IsConnected)
            return "Not connected to simulator — cannot determine aircraft position.";

        var lastPos = _simConnect.LastKnownPosition;
        double lat = lastPos?.Latitude ?? 0;
        double lon = lastPos?.Longitude ?? 0;

        if (lat == 0 && lon == 0)
        {
            // Request a fresh position
            var posTcs = new TaskCompletionSource<SimConnectManager.AircraftPosition>();
            _simConnect.RequestAircraftPositionAsync(pos => posTcs.TrySetResult(pos));
            _ = Task.Delay(5000).ContinueWith(_ => posTcs.TrySetResult(default));
            var freshPos = await posTcs.Task;
            lat = freshPos.Latitude;
            lon = freshPos.Longitude;
        }

        if (lat == 0 && lon == 0)
            return "Could not determine aircraft position.";

        var settings = SettingsManager.Current;
        int rangeNm = settings.SigmetProximityRangeNm > 0 ? settings.SigmetProximityRangeNm : 300;
        // Show everything within 300 nm in the form regardless of proximity alert setting
        int displayRange = Math.Max(rangeNm, 300);

        List<WeatherAdvisory> advisories = await WeatherService.GetNearbyAdvisoriesAsync(
            lat, lon, displayRange, forceRefresh);

        if (advisories.Count == 0)
            return $"No active SIGMETs or AIRMETs found within {displayRange} nm.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Aircraft: {lat:F2}°, {lon:F2}° | {advisories.Count} advisories within {displayRange} nm");
        sb.AppendLine(new string('─', 60));

        foreach (var adv in advisories)
        {
            sb.AppendLine($"[{adv.AdvisoryType}] {adv.HazardLabel}" +
                (string.IsNullOrEmpty(adv.Qualifier) ? "" : $" — {adv.Qualifier}"));

            if (!string.IsNullOrEmpty(adv.AltitudeRange))
                sb.AppendLine($"  Altitude: {adv.AltitudeRange}");

            sb.AppendLine($"  Bearing: {adv.BearingDeg:F0}°  Distance: {adv.DistanceNm:F0} nm");

            if (!string.IsNullOrEmpty(adv.ValidFrom) || !string.IsNullOrEmpty(adv.ValidTo))
                sb.AppendLine($"  Valid: {adv.ValidFrom} – {adv.ValidTo}");

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private void SetStatus(string text)
    {
        if (IsHandleCreated && !IsDisposed)
            _statusLabel.Text = text;
    }

    private void CloseButton_Click(object? sender, EventArgs e)
    {
        Close();
        if (_previousWindow != IntPtr.Zero)
            SetForegroundWindow(_previousWindow);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        if (_previousWindow != IntPtr.Zero)
            SetForegroundWindow(_previousWindow);
    }
}
