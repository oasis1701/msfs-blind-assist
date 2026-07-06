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

    private readonly SimConnectManager _simConnect;
    private readonly ActiveSkyClient _activeSky = new();
    // Cached liveness check for ActiveSky. Re-check on every Refresh, but
    // within a single Refresh call don't re-check because we may make multiple
    // queries to AS (current conditions, METAR, etc.) and we don't need to
    // ping /GetMode for each one.
    private bool? _activeSkyAvailable;
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
    private CheckBox _decodeCheckBox = null!;

    private bool _isFetching = false;

    public WeatherRadarForm(ScreenReaderAnnouncer announcer, SimConnectManager simConnect)
    {
        _previousWindow = GetForegroundWindow();
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
        Size = new Size(600, 710);
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
            Location = new Point(12, 632),
            Size = new Size(370, 20),
            Text = "",
            AccessibleName = "Status"
        };

        _refreshButton = new Button
        {
            Text = "&Refresh (F5)",
            Location = new Point(390, 626),
            Size = new Size(100, 28),
            AccessibleName = "Refresh",
            AccessibleDescription = "Fetch current weather, advisories, and winds aloft"
        };
        _refreshButton.Click += (s, e) => _ = RefreshAsync(forceRefresh: true);

        _closeButton = new Button
        {
            Text = "&Close",
            Location = new Point(500, 626),
            Size = new Size(78, 28),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close",
            AccessibleDescription = "Close weather radar window"
        };
        _closeButton.Click += CloseButton_Click;

        _decodeCheckBox = new CheckBox
        {
            Text = "&Decode advisories into plain English",
            Location = new Point(12, 598),
            Size = new Size(370, 24),
            Checked = SettingsManager.Current.DecodeWeatherAdvisories,
            AccessibleName = "Decode advisories into plain English",
            AccessibleDescription = "Expand aviation abbreviations in SIGMETs and PIREPs into plain language"
        };
        _decodeCheckBox.CheckedChanged += (_, _) =>
        {
            SettingsManager.Current.DecodeWeatherAdvisories = _decodeCheckBox.Checked;
            SettingsManager.Save();
        };

        Controls.AddRange(new Control[]
        {
            _currentWeatherLabel, _currentWeatherBox,
            _advisoriesLabel, _advisoriesBox,
            _windsAloftLabel, _windsAloftBox,
            _decodeCheckBox, _statusLabel, _refreshButton, _closeButton
        });

        CancelButton = _closeButton;
    }

    private void SetupAccessibility()
    {
        _currentWeatherBox.TabIndex = 0;
        _advisoriesBox.TabIndex = 1;
        _windsAloftBox.TabIndex = 2;
        _decodeCheckBox.TabIndex = 3;
        _refreshButton.TabIndex = 4;
        _closeButton.TabIndex = 5;

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
            // Re-check ActiveSky availability on each refresh — the user may
            // have started/stopped AS between refreshes. Cached for the rest
            // of THIS refresh so we don't ping /GetMode for every sub-query.
            _activeSkyAvailable = await _activeSky.IsRunningAsync();

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

            // Silent fallback by design — user shouldn't have to know whether
            // ActiveSky is the source or the SimConnect AMBIENT_* fallback is.
            // If they want to diagnose, the AS-only fields (cloud ceiling,
            // surface wind+gust, QNH, turbulence) appear when AS is up and
            // silently disappear when it isn't.
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

    /// <summary>
    /// Fetches ambient weather. When ActiveSky is running we prefer its API —
    /// SimConnect's `AMBIENT_*` SimVars are unreliable under AS (precipitation
    /// type stuck on snow, in-cloud flag jittery, lagging values). When AS is
    /// NOT running, fall back to SimConnect (still useful with default MSFS
    /// live weather or static weather). The "in cloud" flag specifically
    /// is supplemented from SimConnect even when AS is the primary source,
    /// because AS's HTTP API doesn't expose that bit directly — MSFS knows
    /// where its rendered clouds are regardless of who set them, so reading
    /// AMBIENT IN CLOUD remains valid.
    /// </summary>
    private async Task<string> FetchAmbientAsync()
    {
        // Always grab SimConnect ambient too — needed for in-cloud and as a
        // fallback. simConnected tracks whether we actually got data so we can
        // distinguish "in-cloud=false" from "in-cloud unknown" downstream.
        SimConnectManager.AmbientWeatherData simData = default;
        bool simConnected = _simConnect.IsConnected;
        if (simConnected)
        {
            var tcs = new TaskCompletionSource<SimConnectManager.AmbientWeatherData>();
            _simConnect.RequestWeatherInfo(d => tcs.TrySetResult(d));
            _ = Task.Delay(3000).ContinueWith(_ => tcs.TrySetResult(default));
            simData = await tcs.Task;
        }

        if (_activeSkyAvailable == true)
        {
            // Fetch the JSON conditions and the position METAR in parallel.
            // The JSON gives us structured numbers; the METAR gives us the
            // precipitation tokens (`-RA`, `+SN`, `TSRA`, etc.) which the AS
            // JSON does NOT expose and which the SimConnect bitmask gets
            // wrong under ActiveSky.
            var conditionsTask   = _activeSky.GetCurrentConditionsAsync();
            var posMetarTask     = _activeSky.GetPositionMetarAsync();
            // Also pull the closest-station METAR. #129: the precipitation line
            // must MATCH the AS decoded-weather monitor (which is station-based)
            // so the two never contradict — the user reported the radar showing
            // rain while the decoded weather said none. Both now read the nearest
            // station first, position second.
            var stationMetarTask = _activeSky.GetClosestStationMetarAsync();
            await Task.WhenAll(conditionsTask, posMetarTask, stationMetarTask);
            var asConditions = conditionsTask.Result;
            string? posMetar = posMetarTask.Result;
            string? stationMetar = stationMetarTask.Result;

            if (asConditions != null)
                return FormatAmbientFromActiveSky(asConditions, simData, simConnected, posMetar, stationMetar);
            // AS pinged OK earlier but the conditions call failed — fall
            // through to SimConnect rather than returning an error.
        }

        if (!simConnected)
            return "Not connected to simulator.";

        return WeatherService.FormatAmbientWeather(simData);
    }

    /// <summary>
    /// Format ActiveSky's ambient conditions into the screen-reader-friendly
    /// block. We include the data ActiveSky has that SimConnect doesn't
    /// surface cleanly — surface wind, gusts, QNH, cloud ceiling, turbulence
    /// intensity. In-cloud is read from SimConnect (same caveat as in
    /// FetchAmbientAsync). Precipitation is parsed from the closest METAR
    /// when available — much more accurate than the SimConnect bitmask
    /// under ActiveSky, which the user reports stuck on "extreme snow".
    /// </summary>
    private static string FormatAmbientFromActiveSky(
        ActiveSkyClient.Conditions c,
        SimConnectManager.AmbientWeatherData simAmbient,
        bool simConnected,
        string? positionMetar,
        string? closestStationMetar = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Wind (at altitude): {c.AmbientWindDirection:F0}° at {c.AmbientWindSpeed:F0} knots");
        // Turbulence: AS reports 1-100. In practice it sits at ~25 in calm
        // conditions (atmospheric baseline / numerical noise), so a raw
        // "25/100" reads as alarming when nothing is actually happening. We
        // bucket into the standard pilot categories and hide the line when
        // the value is below the "light" threshold — matching the FAA AIM
        // 7-1-23 turbulence reporting criteria, which only call out turbulence
        // from light upwards.
        string? turbCategory = CategorizeTurbulence(c.AmbientTurbulence);
        if (turbCategory != null)
            sb.AppendLine($"Turbulence: {turbCategory}");
        sb.AppendLine($"Surface wind: {c.SurfaceWindDirection:F0}° at {c.SurfaceWindSpeed:F0} knots" +
            (c.SurfaceGustSpeed > 0 ? $", gusting {c.SurfaceGustSpeed:F0}" : ""));
        // Visibility: AS reports it in statute miles. Show both SM and km — US
        // pilots think in SM, ICAO/rest-of-world thinks in km/meters (METAR
        // "9999" means ≥10 km). Cap at 10 SM / 16 km because AS clamps there
        // and the underlying weather data resolution is meaningless past it.
        double visKm = c.SurfaceVisibility * 1.609344;
        string visStr = c.SurfaceVisibility >= 10
            ? "10+ statute miles (16+ km)"
            : $"{c.SurfaceVisibility:F1} statute miles ({visKm:F1} km)";
        sb.AppendLine($"Visibility: {visStr}");
        sb.AppendLine($"Temperature (at altitude): {c.AmbientTemperature:F0}°C");
        sb.AppendLine($"Surface temperature: {c.SurfaceTemperature:F0}°C");
        if (c.CloudCeilingFtAgl > 0)
            sb.AppendLine($"Cloud ceiling: {c.CloudCeilingFtAgl:N0} ft AGL");
        else
            sb.AppendLine("Cloud ceiling: above 8,000 ft AGL or no broken/overcast layer");
        if (c.QnhMb > 0)
            sb.AppendLine($"QNH: {c.QnhMb:F0} hPa / {(c.QnhMb * 0.02953):F2} inHg");

        // In-cloud from SimConnect (the only reliable source — AS API doesn't
        // expose it). MSFS knows where its rendered clouds are regardless of
        // who set them. If SimConnect isn't connected we genuinely don't know,
        // so say so rather than printing "No" (which would be a lie when
        // sitting inside an overcast layer with the sim disconnected).
        sb.AppendLine(simConnected
            ? $"In cloud: {(simAmbient.InCloud >= 0.5 ? "Yes" : "No")}"
            : "In cloud: unknown (sim not connected)");

        // Precipitation parsed from an AS METAR (precip tokens like -RA = light
        // rain, +SN = heavy snow, TSRA = thunderstorm with rain — the METAR is
        // what the AS weather engine actually reports, unlike the SimConnect
        // AMBIENT_PRECIP_STATE bitmask, which sticks under ActiveSky).
        // Order of preference (#129 — MUST match the AS decoded-weather monitor
        // AND the precip auto-announce so the three features never contradict;
        // the user reported the radar showing rain while the decoded weather
        // said none — both were reading DIFFERENT METARs):
        //   1. Closest-station METAR (the real nearest reporting station — the
        //      same source the decoded-weather monitor labels and reads).
        //   2. Closest-station METAR from the conditions JSON (belt-and-braces).
        //   3. Position METAR (@POS — AS's interpolated point weather).
        //   4. SimConnect bitmask (wrong under AS, last resort).
        // "METAR present, no precip token" = "None", NOT a fall-through — only
        // an entirely missing METAR triggers the next source.
        string precip;
        if (!string.IsNullOrWhiteSpace(closestStationMetar))
        {
            string parsed = ParsePrecipFromMetar(closestStationMetar);
            precip = string.IsNullOrEmpty(parsed) ? "None" : parsed;
        }
        else if (!string.IsNullOrWhiteSpace(c.ClosestMetar))
        {
            string parsed = ParsePrecipFromMetar(c.ClosestMetar);
            precip = string.IsNullOrEmpty(parsed) ? "None" : parsed;
        }
        else if (!string.IsNullOrWhiteSpace(positionMetar))
        {
            string parsed = ParsePrecipFromMetar(positionMetar);
            precip = string.IsNullOrEmpty(parsed) ? "None" : parsed;
        }
        else if (simConnected)
        {
            precip = DescribeSimPrecip(simAmbient.PrecipState, simAmbient.PrecipRate);
        }
        else
        {
            precip = "Unknown";
        }
        sb.AppendLine($"Precipitation: {precip}");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Looks for METAR precipitation tokens (RA, SN, FZRA, TS, etc., with
    /// intensity prefixes - = light, none = moderate, + = heavy) and returns
    /// a plain-English summary. Returns empty string if no precip token
    /// found OR no METAR was provided. METAR format reference: WMO AMC 4444
    /// + ICAO Annex 3 weather phenomenon codes.
    /// </summary>
    private static string ParsePrecipFromMetar(string metar)
    {
        if (string.IsNullOrWhiteSpace(metar)) return "";
        // Strip any annotation lines (Active Sky appends "(Cloned by: ...)" etc.)
        string firstLine = metar.Split('\r', '\n')[0].ToUpperInvariant();

        // Tokenize on whitespace; look for tokens matching the standard
        // weather group: optional [+-VC], optional descriptor (BC/BL/DR/FZ/MI/PR/SH/TS),
        // weather phenomenon (RA/SN/GR/GS/PL/IC/UP/FG/BR/HZ/FU etc.).
        // Cheap matcher: just look for known precip phenomena with intensity.
        // This is intentionally simple — full METAR parsing is overkill here.
        string[] tokens = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (string t in tokens)
        {
            string token = t;
            string intensity = "moderate";
            if (token.StartsWith("-")) { intensity = "light"; token = token[1..]; }
            else if (token.StartsWith("+")) { intensity = "heavy"; token = token[1..]; }
            else if (token.StartsWith("VC")) { intensity = "in vicinity"; token = token[2..]; }

            // Skip descriptor (TS/SH/FZ/etc.) but remember it
            string descriptor = "";
            if (token.Length >= 2)
            {
                string head = token[..2];
                if (head is "TS" or "SH" or "FZ" or "BL" or "DR" or "MI" or "BC" or "PR")
                {
                    descriptor = head;
                    token = token[2..];
                }
            }

            string phenom = token.Length >= 2 ? token[..2] : "";
            string phenomName = phenom switch
            {
                "RA" => "rain",
                "SN" => "snow",
                "GR" => "hail",
                "GS" => "small hail",
                "PL" => "ice pellets",
                "IC" => "ice crystals",
                "UP" => "unknown precipitation",
                "DZ" => "drizzle",
                "SG" => "snow grains",
                _ => ""
            };
            if (string.IsNullOrEmpty(phenomName)) continue;

            string descriptorName = descriptor switch
            {
                "TS" => "thunderstorm with ",
                "SH" => "showers of ",
                "FZ" => "freezing ",
                _ => ""
            };
            return $"{intensity} {descriptorName}{phenomName}";
        }
        return "";  // no precipitation token found
    }

    /// <summary>
    /// Maps ActiveSky's 1-100 turbulence number to a pilot-category word
    /// (or null = "don't show the line"). The thresholds:
    ///   ≤ 25  → null (calm — AS sits here as a baseline; showing a number
    ///           reads as alarming when nothing is happening)
    ///   26-50 → "light"
    ///   51-75 → "moderate"
    ///   76-90 → "severe"
    ///   91+   → "extreme"
    /// Categories follow FAA AIM 7-1-23 phraseology so a pilot reading the
    /// box hears the same words ATC and PIREPs would use. We don't include
    /// the raw 1-100 number — it isn't a published scale anyone would
    /// recognise, and the category alone is what's actionable.
    /// </summary>
    private static string? CategorizeTurbulence(double t)
    {
        if (t <= 25) return null;
        if (t <= 50) return "light";
        if (t <= 75) return "moderate";
        if (t <= 90) return "severe";
        return "extreme";
    }

    /// <summary>
    /// Same logic as WeatherService.DescribePrecip but local — used as a
    /// fallback when the AS METAR doesn't yield a clear precip phrase.
    /// </summary>
    private static string DescribeSimPrecip(double state, double rate)
    {
        int s = (int)Math.Round(state);
        if (s == 0 || rate < 1.0) return "None";
        string intensity = rate switch { < 20 => "Light", < 50 => "Moderate", < 80 => "Heavy", _ => "Extreme" };
        string type = s switch { 1 or 2 => "rain", 4 => "snow", 8 => "freezing rain", _ => "precipitation" };
        return $"{intensity} {type} ({rate:F0}%)";
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
        sb.AppendLine($"Aircraft: {altFt:N0} ft  |  forecast winds:");
        sb.AppendLine(new string('─', 36));

        foreach (var w in winds)
        {
            string marker = Math.Abs(w.AltitudeFt - altFt) < 500 ? " (nearest)" : "";
            sb.AppendLine($"{w.AltitudeFt:N0} ft:  {w.DirectionDeg:F0}° / {w.SpeedKts:F0} kts{marker}");
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
