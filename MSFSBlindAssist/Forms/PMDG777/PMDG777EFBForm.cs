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

        public PMDG777EFBForm(EFBBridgeServer bridgeServer, ScreenReaderAnnouncer announcer)
        {
            _bridgeServer = bridgeServer;
            _announcer = announcer;

            InitializeComponent();
            SetupEventHandlers();
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

            fetchSimbriefButton!.Click += (_, _) =>
            {
                simbriefStatusText!.Text = "Fetching...";
                _bridgeServer.EnqueueCommand("fetch_simbrief");
            };

            sendToFmcButton!.Click += (_, _) =>
            {
                _bridgeServer.EnqueueCommand("send_to_fmc");
            };

            navigraphSignInButton!.Click += (_, _) =>
            {
                navigraphStatusText!.Text = "Awaiting code...";
                authCodeTextBox!.Text = "";
                _bridgeServer.EnqueueCommand("start_navigraph_auth");
            };

            navigraphSignOutButton!.Click += (_, _) =>
            {
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

        private void OnStateUpdated(object? sender, EFBStateUpdateEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;

            switch (e.Type)
            {
                case "connected":
                    _announcer.Announce("EFB bridge connected");
                    break;

                case "simbrief_loaded":
                    _simbriefLoaded = true;
                    UpdateFlightDetails(e.Data);
                    simbriefStatusText!.Text = "Loaded";
                    sendToFmcButton!.Enabled = true;
                    string origin = e.Data.GetValueOrDefault("origin_icao", "");
                    string dest = e.Data.GetValueOrDefault("dest_icao", "");
                    _announcer.Announce($"SimBrief flight plan loaded: {origin} to {dest}");
                    break;

                case "simbrief_fetch_result":
                    bool success = e.Data.GetValueOrDefault("success", "false") == "true" ||
                                   e.Data.GetValueOrDefault("success", "false") == "True";
                    string message = e.Data.GetValueOrDefault("message", "");
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
                    bool authenticated = e.Data.GetValueOrDefault("authenticated", "false") == "true";
                    string username = e.Data.GetValueOrDefault("username", "");
                    if (authenticated)
                    {
                        navigraphStatusText!.Text = $"Authenticated as: {username}";
                        navigraphSignInButton!.Enabled = false;
                        navigraphSignOutButton!.Enabled = true;
                        _announcer.Announce($"Signed in to Navigraph as {username}");
                    }
                    else
                    {
                        navigraphStatusText!.Text = "Not authenticated";
                        navigraphSignInButton!.Enabled = true;
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
                    string errorMsg = e.Data.GetValueOrDefault("message", "Unknown error");
                    simbriefStatusText!.Text = $"Error: {errorMsg}";
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
            if (!string.IsNullOrEmpty(simbriefAliasTextBox!.Text))
            {
                _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                    { { "key", "simbrief_id" }, { "value", simbriefAliasTextBox.Text } });
            }
            if (weatherSourceCombo!.SelectedItem != null)
            {
                _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                    { { "key", "weather_source" }, { "value", weatherSourceCombo.SelectedItem.ToString()! } });
            }
            if (weightUnitCombo!.SelectedItem != null)
            {
                _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                    { { "key", "weight_unit" }, { "value", weightUnitCombo.SelectedItem.ToString()! } });
            }
            if (distanceUnitCombo!.SelectedItem != null)
            {
                _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                    { { "key", "distance_unit" }, { "value", distanceUnitCombo.SelectedItem.ToString()! } });
            }
            if (altitudeUnitCombo!.SelectedItem != null)
            {
                _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                    { { "key", "altitude_unit" }, { "value", altitudeUnitCombo.SelectedItem.ToString()! } });
            }
            if (temperatureUnitCombo!.SelectedItem != null)
            {
                _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                    { { "key", "temperature_unit" }, { "value", temperatureUnitCombo.SelectedItem.ToString()! } });
            }
            _bridgeServer.EnqueueCommand("save_preferences");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _bridgeServer.StateUpdated -= OnStateUpdated;
            if (_previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(_previousWindow);
            }
            base.OnFormClosing(e);
        }
    }
}
