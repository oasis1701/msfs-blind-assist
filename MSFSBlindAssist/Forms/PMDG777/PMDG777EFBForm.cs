using System.Diagnostics;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG777
{
    public partial class PMDG777EFBForm : Form
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly EFBBridgeServer _bridgeServer;
        private readonly ScreenReaderAnnouncer _announcer;
        private IntPtr _previousWindow = IntPtr.Zero;
        private bool _simbriefLoaded = false;
        private bool _wasConnected = false;
        private System.Windows.Forms.Timer? _connectionCheckTimer;
        private System.Windows.Forms.Timer? _fetchTimeoutTimer;
        private System.Windows.Forms.Timer? _authTimeoutTimer;

        public PMDG777EFBForm(EFBBridgeServer bridgeServer, ScreenReaderAnnouncer announcer)
        {
            _bridgeServer = bridgeServer;
            _announcer = announcer;

            InitializeComponent();
            SetupEventHandlers();

            // Poll connection status every 3 seconds
            _connectionCheckTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _connectionCheckTimer.Tick += OnConnectionCheck;
            _connectionCheckTimer.Start();

            // Run initial check immediately
            OnConnectionCheck(this, EventArgs.Empty);
        }

        public void ShowForm()
        {
            _previousWindow = GetForegroundWindow();
            Show();
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false;

            fetchSimbriefButton?.Focus();
        }

        private void SetupEventHandlers()
        {
            _bridgeServer.StateUpdated += OnStateUpdated;
            _bridgeServer.Error += OnBridgeServerError;

            fetchSimbriefButton!.Click += (_, _) =>
            {
                if (_bridgeServer.HasPendingCommand("fetch_simbrief")) return;
                fetchSimbriefButton.Enabled = false;
                simbriefStatusText!.Text = "Fetching...";
                _bridgeServer.EnqueueCommand("fetch_simbrief");
                StartFetchTimeout();
            };

            sendToFmcButton!.Click += (_, _) =>
            {
                if (_bridgeServer.HasPendingCommand("send_to_fmc")) return;
                sendToFmcButton.Enabled = false;
                _bridgeServer.EnqueueCommand("send_to_fmc");
            };

            navigraphSignInButton!.Click += (_, _) =>
            {
                if (_bridgeServer.HasPendingCommand("start_navigraph_auth")) return;
                navigraphSignInButton.Enabled = false;
                navigraphStatusText!.Text = "Awaiting code...";
                authCodeTextBox!.Text = "";
                _bridgeServer.EnqueueCommand("start_navigraph_auth");
                StartAuthTimeout();
            };

            navigraphSignOutButton!.Click += (_, _) =>
            {
                if (_bridgeServer.HasPendingCommand("sign_out_navigraph")) return;
                navigraphSignOutButton.Enabled = false;
                _bridgeServer.EnqueueCommand("sign_out_navigraph");
            };

            savePreferencesButton!.Click += OnSavePreferences;

            tabControl!.SelectedIndexChanged += (_, _) =>
            {
                if (tabControl.SelectedTab == preferencesTab)
                {
                    _bridgeServer.EnqueueCommand("get_preferences");
                }
            };
        }

        private void OnBridgeServerError(string message)
        {
            if (IsDisposed || !IsHandleCreated) return;
            _announcer.Announce(message);
        }

        private void OnConnectionCheck(object? sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;

            bool connected = _bridgeServer.IsBridgeConnected;

            connectionStatusText!.Text = connected
                ? "Connected"
                : "Not connected \u2014 EFB tablet must be open in simulator";

            // Announce transitions only
            if (connected && !_wasConnected)
            {
                _announcer.Announce("EFB bridge connected");
                UpdateButtonStates(true);
            }
            else if (!connected && _wasConnected)
            {
                _announcer.Announce("EFB bridge disconnected");
                UpdateButtonStates(false);
            }

            _wasConnected = connected;
        }

        private void UpdateButtonStates(bool connected)
        {
            fetchSimbriefButton!.Enabled = connected;
            sendToFmcButton!.Enabled = connected && _simbriefLoaded;
            navigraphSignInButton!.Enabled = connected;
            navigraphSignOutButton!.Enabled = connected;
            savePreferencesButton!.Enabled = connected;
        }

        private void StartFetchTimeout()
        {
            StopFetchTimeout();
            _fetchTimeoutTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            _fetchTimeoutTimer.Tick += (_, _) =>
            {
                StopFetchTimeout();
                simbriefStatusText!.Text = "Fetch timed out \u2014 try again";
                fetchSimbriefButton!.Enabled = _bridgeServer.IsBridgeConnected;
                _announcer.Announce("SimBrief fetch timed out");
            };
            _fetchTimeoutTimer.Start();
        }

        private void StopFetchTimeout()
        {
            if (_fetchTimeoutTimer != null)
            {
                _fetchTimeoutTimer.Stop();
                _fetchTimeoutTimer.Dispose();
                _fetchTimeoutTimer = null;
            }
        }

        private void StartAuthTimeout()
        {
            StopAuthTimeout();
            _authTimeoutTimer = new System.Windows.Forms.Timer { Interval = 60000 };
            _authTimeoutTimer.Tick += (_, _) =>
            {
                StopAuthTimeout();
                navigraphStatusText!.Text = "Sign-in timed out";
                navigraphSignInButton!.Enabled = _bridgeServer.IsBridgeConnected;
                _announcer.Announce("Navigraph sign-in timed out");
            };
            _authTimeoutTimer.Start();
        }

        private void StopAuthTimeout()
        {
            if (_authTimeoutTimer != null)
            {
                _authTimeoutTimer.Stop();
                _authTimeoutTimer.Dispose();
                _authTimeoutTimer = null;
            }
        }

        private void OnStateUpdated(object? sender, EFBStateUpdateEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;

            switch (e.Type)
            {
                case "connected":
                    // Connection announcement handled by OnConnectionCheck
                    break;

                case "simbrief_loaded":
                    StopFetchTimeout();
                    _simbriefLoaded = true;
                    UpdateFlightDetails(e.Data);
                    simbriefStatusText!.Text = "Loaded";
                    fetchSimbriefButton!.Enabled = _bridgeServer.IsBridgeConnected;
                    sendToFmcButton!.Enabled = _bridgeServer.IsBridgeConnected;
                    string origin = e.Data.GetValueOrDefault("origin_icao", "");
                    string dest = e.Data.GetValueOrDefault("dest_icao", "");
                    _announcer.Announce($"SimBrief flight plan loaded: {origin} to {dest}");
                    break;

                case "simbrief_fetch_result":
                    bool success = bool.TryParse(e.Data.GetValueOrDefault("success", "false"), out var s) && s;
                    string message = e.Data.GetValueOrDefault("message", "");
                    sendToFmcButton!.Enabled = _bridgeServer.IsBridgeConnected && _simbriefLoaded;
                    if (success)
                    {
                        _announcer.Announce($"FMC file transfer complete: {message}");
                    }
                    else if (!string.IsNullOrEmpty(message))
                    {
                        _announcer.Announce($"FMC transfer result: {message}");
                    }
                    break;

                case "fmc_upload_started":
                    _announcer.Announce("Flight plan sent to FMC");
                    break;

                case "navigraph_code":
                    StopAuthTimeout();
                    string code = e.Data.GetValueOrDefault("code", "");
                    string url = e.Data.GetValueOrDefault("url", "https://navigraph.com/code");
                    authCodeTextBox!.Text = code;
                    _announcer.Announce($"Navigraph sign-in code: {code}. Opening browser.");
                    try
                    {
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch { }
                    break;

                case "navigraph_auth_state":
                    StopAuthTimeout();
                    bool authenticated = e.Data.GetValueOrDefault("authenticated", "false") == "true";
                    string username = e.Data.GetValueOrDefault("username", "");
                    if (authenticated)
                    {
                        navigraphStatusText!.Text = $"Authenticated as: {username}";
                        navigraphSignInButton!.Enabled = false;
                        navigraphSignOutButton!.Enabled = _bridgeServer.IsBridgeConnected;
                        _announcer.Announce($"Signed in to Navigraph as {username}");
                    }
                    else
                    {
                        navigraphStatusText!.Text = "Not authenticated";
                        navigraphSignInButton!.Enabled = _bridgeServer.IsBridgeConnected;
                        navigraphSignOutButton!.Enabled = false;
                        authCodeTextBox!.Text = "";
                        if (!string.IsNullOrEmpty(username))
                        {
                            _announcer.Announce("Signed out of Navigraph");
                        }
                    }
                    break;

                case "preferences":
                    PopulatePreferences(e.Data);
                    break;

                case "error":
                    StopFetchTimeout();
                    string errorMsg = e.Data.GetValueOrDefault("message", "Unknown error");
                    simbriefStatusText!.Text = $"Error: {errorMsg}";
                    fetchSimbriefButton!.Enabled = _bridgeServer.IsBridgeConnected;
                    _announcer.Announce($"EFB error: {errorMsg}");
                    break;
            }
        }

        private void UpdateFlightDetails(Dictionary<string, string> data)
        {
            callsignValue!.Text = data.GetValueOrDefault("callsign", "\u2014");
            originValue!.Text = data.GetValueOrDefault("origin_icao", "\u2014");
            destValue!.Text = data.GetValueOrDefault("dest_icao", "\u2014");
            altValue!.Text = data.GetValueOrDefault("alt_icao", "\u2014");
            cruiseAltValue!.Text = data.GetValueOrDefault("cruise_alt", "\u2014");
            costIndexValue!.Text = data.GetValueOrDefault("cost_index", "\u2014");
            zfwValue!.Text = data.GetValueOrDefault("zfw", "\u2014");
            fuelValue!.Text = data.GetValueOrDefault("fuel_total", "\u2014");
            windValue!.Text = data.GetValueOrDefault("avg_wind", "\u2014");
        }

        private void PopulatePreferences(Dictionary<string, string> data)
        {
            if (data.TryGetValue("simbrief_id", out string? simbriefId))
                simbriefAliasTextBox!.Text = simbriefId;

            SetComboValue(weatherSourceCombo!, data.GetValueOrDefault("weather_source", ""));
            SetComboValue(weightUnitCombo!, data.GetValueOrDefault("weight_unit", ""));
            SetComboValue(distanceUnitCombo!, data.GetValueOrDefault("distance_unit", ""));
            SetComboValue(altitudeUnitCombo!, data.GetValueOrDefault("altitude_unit", ""));
            SetComboValue(temperatureUnitCombo!, data.GetValueOrDefault("temperature_unit", ""));
        }

        private static void SetComboValue(ComboBox combo, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(combo.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void OnSavePreferences(object? sender, EventArgs e)
        {
            if (!_bridgeServer.IsBridgeConnected)
            {
                _announcer.Announce("EFB bridge not connected. Preferences cannot be saved while the EFB tablet is not active in the simulator.");
                return;
            }

            if (_bridgeServer.HasPendingCommand("save_preferences")) return;

            savePreferencesButton!.Enabled = false;

            _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                { { "key", "simbrief_id" }, { "value", simbriefAliasTextBox!.Text ?? "" } });

            EnqueueComboPreference(weatherSourceCombo!, "weather_source");
            EnqueueComboPreference(weightUnitCombo!, "weight_unit");
            EnqueueComboPreference(distanceUnitCombo!, "distance_unit");
            EnqueueComboPreference(altitudeUnitCombo!, "altitude_unit");
            EnqueueComboPreference(temperatureUnitCombo!, "temperature_unit");

            _bridgeServer.EnqueueCommand("save_preferences");
            _announcer.Announce("Preferences saved");

            // Re-enable after a short delay (preferences are fire-and-forget)
            var reenableTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            reenableTimer.Tick += (_, _) =>
            {
                reenableTimer.Stop();
                reenableTimer.Dispose();
                if (!IsDisposed && _bridgeServer.IsBridgeConnected)
                    savePreferencesButton!.Enabled = true;
            };
            reenableTimer.Start();
        }

        private void EnqueueComboPreference(ComboBox combo, string key)
        {
            string? value = combo.SelectedItem?.ToString();
            if (value != null)
            {
                _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                    { { "key", key }, { "value", value } });
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _connectionCheckTimer?.Stop();
            _connectionCheckTimer?.Dispose();
            StopFetchTimeout();
            StopAuthTimeout();
            _bridgeServer.StateUpdated -= OnStateUpdated;
            _bridgeServer.Error -= OnBridgeServerError;
            if (_previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(_previousWindow);
            }
            base.OnFormClosing(e);
        }
    }
}
