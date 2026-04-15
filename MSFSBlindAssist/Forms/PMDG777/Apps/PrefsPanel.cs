using System.Diagnostics;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG777.Apps
{
    /// <summary>
    /// EFB Preferences sub-tab. Mirrors PMDG's own preferences page 1:1 in
    /// field order, so anyone comparing our panel to the tablet sees the same
    /// list in the same sequence. Also owns the Navigraph sign-in UI, which
    /// PMDG puts on the preferences page too.
    /// </summary>
    public class PrefsPanel : EfbAppPanelBase
    {
        // Order matches PMDG's preferences page. Skipped: Tablet Brightness
        // (slider, not useful via screen reader) and Reset to Factory Settings
        // (too dangerous to expose without a confirmation flow).

        private ComboBox startScreenOnCombo = null!;
        private TextBox simbriefAliasTextBox = null!;
        private TextBox hoppieIdTextBox = null!;
        private TextBox sayIntentionsKeyTextBox = null!;
        private ComboBox atcNetworkCombo = null!;
        private ComboBox weatherSourceCombo = null!;
        private ComboBox onScreenKeyboardCombo = null!;
        private ComboBox themeCombo = null!;
        private ComboBox distanceUnitCombo = null!;
        private ComboBox altitudeUnitCombo = null!;
        private ComboBox lengthUnitCombo = null!;
        private ComboBox speedUnitCombo = null!;
        private ComboBox airspeedUnitCombo = null!;
        private ComboBox temperatureUnitCombo = null!;
        private ComboBox pressureUnitCombo = null!;
        private ComboBox weightUnitCombo = null!;
        private Button savePreferencesButton = null!;

        // Navigraph authentication section (lives here now; Navdata tab is
        // AIRAC-only).
        private TextBox navigraphStatusText = null!;
        private Button navigraphSignInButton = null!;
        private Button navigraphSignOutButton = null!;
        private TextBox navigraphAuthCodeTextBox = null!;

        private System.Windows.Forms.Timer? _authTimeoutTimer;
        private System.Windows.Forms.Timer? _signOutTimeoutTimer;

        public override Control? InitialFocusControl => startScreenOnCombo;

        protected override void BuildUi()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10);
            AccessibleName = "Preferences";

            int y = 10;
            const int labelX = 10;
            const int valueX = 200;
            const int rowHeight = 28;
            int tabIdx = 0;

            TextBox TextRow(string labelText, string accName)
            {
                Controls.Add(new Label { Text = labelText, Location = new System.Drawing.Point(labelX, y), AutoSize = true });
                var box = new TextBox
                {
                    Location = new System.Drawing.Point(valueX, y),
                    Size = new System.Drawing.Size(260, 25),
                    AccessibleName = accName,
                    TabIndex = tabIdx++
                };
                Controls.Add(box);
                y += rowHeight + 2;
                return box;
            }

            ComboBox ComboRow(string labelText, string accName, object[] items)
            {
                Controls.Add(new Label { Text = labelText, Location = new System.Drawing.Point(labelX, y), AutoSize = true });
                var combo = new ComboBox
                {
                    Location = new System.Drawing.Point(valueX, y),
                    Size = new System.Drawing.Size(260, 25),
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    AccessibleName = accName,
                    TabIndex = tabIdx++
                };
                combo.Items.AddRange(items);
                Controls.Add(combo);
                y += rowHeight + 2;
                return combo;
            }

            // PMDG preferences page order (Tablet Brightness skipped):
            startScreenOnCombo       = ComboRow("Start with Screen On:",   "Start with Screen On",   new object[] { "YES", "NO" });
            simbriefAliasTextBox     = TextRow("SimBrief Alias:",          "SimBrief Alias");
            hoppieIdTextBox          = TextRow("Hoppie ID:",               "Hoppie ID");
            sayIntentionsKeyTextBox  = TextRow("Say Intentions API Key:",  "Say Intentions API Key");
            atcNetworkCombo          = ComboRow("ATC Network:",            "ATC Network",            new object[] { "NONE", "HOPPIE", "SAY INTENTIONS" });
            weatherSourceCombo       = ComboRow("Weather Source:",         "Weather Source",         new object[] { "SIM", "REAL-WORLD" });
            onScreenKeyboardCombo    = ComboRow("On Screen Keyboard:",     "On Screen Keyboard",     new object[] { "ON", "OFF" });
            themeCombo               = ComboRow("Theme Preference:",       "Theme Preference",       new object[] { "MANUAL", "AUTO" });
            distanceUnitCombo        = ComboRow("Distance Unit:",          "Distance Unit",          new object[] { "nm", "km" });
            altitudeUnitCombo        = ComboRow("Altitude Unit:",          "Altitude Unit",          new object[] { "ft", "m" });
            lengthUnitCombo          = ComboRow("Length Unit:",            "Length Unit",            new object[] { "ft", "m" });
            speedUnitCombo           = ComboRow("Speed Unit:",             "Speed Unit",             new object[] { "kph", "mph" });
            airspeedUnitCombo        = ComboRow("AirSpeed Unit:",          "AirSpeed Unit",          new object[] { "kts", "kph" });
            temperatureUnitCombo     = ComboRow("Temperature Unit:",       "Temperature Unit",       new object[] { "C", "F" });
            pressureUnitCombo        = ComboRow("Pressure Unit:",          "Pressure Unit",          new object[] { "hPa", "inHg" });
            weightUnitCombo          = ComboRow("Weight Unit:",            "Weight Unit",            new object[] { "lb", "kg" });
            y += 6;

            savePreferencesButton = new Button
            {
                Text = "Save Preferences",
                Location = new System.Drawing.Point(labelX, y),
                Size = new System.Drawing.Size(160, 30),
                AccessibleName = "Save Preferences",
                TabIndex = tabIdx++
            };
            savePreferencesButton.Click += OnSavePreferences;
            Controls.Add(savePreferencesButton);
            y += 40;

            // --- Navigraph section ---
            Controls.Add(new Label
            {
                Text = "Navigraph Authentication",
                Location = new System.Drawing.Point(labelX, y),
                Size = new System.Drawing.Size(450, 20),
                Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold),
                AccessibleName = "Navigraph Authentication section"
            });
            y += rowHeight;

            navigraphStatusText = new TextBox
            {
                Text = "Not authenticated",
                Location = new System.Drawing.Point(labelX, y),
                Size = new System.Drawing.Size(450, 22),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = System.Drawing.SystemColors.Control,
                Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold),
                AccessibleName = "Navigraph Status",
                TabIndex = tabIdx++
            };
            Controls.Add(navigraphStatusText);
            y += rowHeight + 2;

            navigraphSignInButton = new Button
            {
                Text = "Sign In",
                Location = new System.Drawing.Point(labelX, y),
                Size = new System.Drawing.Size(140, 30),
                AccessibleName = "Sign In to Navigraph",
                TabIndex = tabIdx++
            };
            navigraphSignOutButton = new Button
            {
                Text = "Sign Out",
                Location = new System.Drawing.Point(labelX + 150, y),
                Size = new System.Drawing.Size(140, 30),
                Enabled = false,
                AccessibleName = "Sign Out of Navigraph",
                TabIndex = tabIdx++
            };
            navigraphSignInButton.Click += OnNavigraphSignInClick;
            navigraphSignOutButton.Click += OnNavigraphSignOutClick;
            Controls.Add(navigraphSignInButton);
            Controls.Add(navigraphSignOutButton);
            y += 40;

            Controls.Add(new Label { Text = "Auth Code:", Location = new System.Drawing.Point(labelX, y), AutoSize = true });
            navigraphAuthCodeTextBox = new TextBox
            {
                Location = new System.Drawing.Point(valueX, y),
                Size = new System.Drawing.Size(200, 25),
                ReadOnly = true,
                AccessibleName = "Navigraph Auth Code",
                TabIndex = tabIdx++
            };
            Controls.Add(navigraphAuthCodeTextBox);
        }

        public override void OnActivated()
        {
            if (!BridgeServer.IsBridgeConnected) return;
            BridgeServer.EnqueueCommand("get_preferences");
            BridgeServer.EnqueueCommand("check_navigraph_auth");
        }

        public void SetConnected(bool connected)
        {
            if (IsDisposed) return;
            savePreferencesButton.Enabled = connected;
            navigraphSignInButton.Enabled = connected;
            // signOut enabled state depends on whether we're authed; refreshed
            // when navigraph_auth_state arrives.
        }

        private const string PopupDismissMarker = "EFB_PREF_POPUP_DISMISS";

        protected override void HandleStateUpdate(EFBStateUpdateEventArgs e)
        {
            switch (e.Type)
            {
                case "preferences":
                    PopulatePreferences(e.Data);
                    PreferencesCache.Update(e.Data);
                    break;

                case "navigraph_code":
                    StopAuthTimeout();
                    string code = e.Data.GetValueOrDefault("code", "");
                    string url = e.Data.GetValueOrDefault("url", "https://navigraph.com/code");
                    navigraphAuthCodeTextBox.Text = code;
                    Announcer.Announce($"Navigraph sign-in code: {code}. Opening browser.");
                    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
                    break;

                case "navigraph_auth_state":
                    StopAuthTimeout();
                    StopSignOutTimeout();
                    bool authenticated = e.Data.GetValueOrDefault("authenticated", "false") == "true";
                    string username = e.Data.GetValueOrDefault("username", "");
                    if (authenticated)
                    {
                        navigraphStatusText.Text = $"Authenticated as: {username}";
                        navigraphSignInButton.Enabled = false;
                        navigraphSignOutButton.Enabled = BridgeServer.IsBridgeConnected;
                    }
                    else
                    {
                        navigraphStatusText.Text = "Not authenticated";
                        navigraphSignInButton.Enabled = BridgeServer.IsBridgeConnected;
                        navigraphSignOutButton.Enabled = false;
                        navigraphAuthCodeTextBox.Text = "";
                    }
                    break;

                case "eval_result":
                    string result = e.Data.GetValueOrDefault("result", "");
                    if (result.Contains(PopupDismissMarker) && result.Contains("clicked"))
                        StopPopupDismissTimer();
                    break;
            }
        }

        private void PopulatePreferences(Dictionary<string, string> data)
        {
            SetTextBox(simbriefAliasTextBox, NormalizePmdgString(data.GetValueOrDefault("simbrief_id", "")));
            SetTextBox(hoppieIdTextBox, NormalizePmdgString(data.GetValueOrDefault("hoppie_id", "")));
            SetTextBox(sayIntentionsKeyTextBox, NormalizePmdgString(data.GetValueOrDefault("sayintentions_id", "")));

            SetComboValue(startScreenOnCombo, data.GetValueOrDefault("start_screen_on", ""));
            SetComboValue(atcNetworkCombo, data.GetValueOrDefault("atc_network", ""));
            SetComboValue(weatherSourceCombo, data.GetValueOrDefault("weather_source", ""));
            SetComboValue(onScreenKeyboardCombo, data.GetValueOrDefault("on_screen_keyboard", ""));
            SetComboValue(themeCombo, data.GetValueOrDefault("theme_setting", ""));

            SetComboValue(distanceUnitCombo, data.GetValueOrDefault("distance_unit", ""));
            SetComboValue(altitudeUnitCombo, data.GetValueOrDefault("altitude_unit", ""));
            SetComboValue(lengthUnitCombo, data.GetValueOrDefault("length_unit", ""));
            SetComboValue(speedUnitCombo, data.GetValueOrDefault("speed_unit", ""));
            SetComboValue(airspeedUnitCombo, data.GetValueOrDefault("airspeed_unit", ""));
            SetComboValue(temperatureUnitCombo, data.GetValueOrDefault("temperature_unit", ""));
            SetComboValue(pressureUnitCombo, data.GetValueOrDefault("pressure_unit", ""));
            SetComboValue(weightUnitCombo, data.GetValueOrDefault("weight_unit", ""));
        }

        private static void SetTextBox(TextBox box, string value)
        {
            // No focus guard: get_preferences only fires on activation, so the
            // risk of stomping mid-edit is minimal, and skipping leaves the
            // initially-focused field permanently blank on re-activation.
            box.Text = value ?? "";
        }

        // PMDG uses the literal string "NULL" to represent an unset text field
        // (observed for hoppie_id on a fresh profile). Render those as blank so
        // the user doesn't see the word NULL in our UI.
        private static string NormalizePmdgString(string raw) =>
            string.Equals(raw, "NULL", StringComparison.Ordinal) ? "" : raw;

        private static void SetComboValue(ComboBox combo, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            // Exact (case-insensitive) match first.
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(combo.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            // Singular/plural tolerance: PMDG's stored unit strings aren't always
            // consistent (e.g. "kt" vs "kts" for airspeed depending on toggle
            // state). Strip a trailing 's' from either side and retry.
            string normalizedValue = value.TrimEnd('s', 'S');
            for (int i = 0; i < combo.Items.Count; i++)
            {
                string item = combo.Items[i]?.ToString() ?? "";
                string normalizedItem = item.TrimEnd('s', 'S');
                if (string.Equals(normalizedItem, normalizedValue, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void OnSavePreferences(object? sender, EventArgs e)
        {
            if (!BridgeServer.IsBridgeConnected)
            {
                Announcer.Announce("EFB bridge not connected. Preferences cannot be saved while the EFB tablet is not active in the simulator.");
                return;
            }

            if (BridgeServer.HasPendingCommand("save_preferences")) return;

            savePreferencesButton.Enabled = false;

            EnqueueTextPreference("simbrief_id", simbriefAliasTextBox.Text);
            EnqueueTextPreference("hoppie_id", hoppieIdTextBox.Text);
            EnqueueTextPreference("sayintentions_id", sayIntentionsKeyTextBox.Text);

            EnqueueComboPreference(startScreenOnCombo, "start_screen_on");
            EnqueueComboPreference(atcNetworkCombo, "atc_network");
            EnqueueComboPreference(weatherSourceCombo, "weather_source");
            EnqueueComboPreference(onScreenKeyboardCombo, "on_screen_keyboard");
            EnqueueComboPreference(themeCombo, "theme_setting");

            EnqueueComboPreference(distanceUnitCombo, "distance_unit");
            EnqueueComboPreference(altitudeUnitCombo, "altitude_unit");
            EnqueueComboPreference(lengthUnitCombo, "length_unit");
            EnqueueComboPreference(speedUnitCombo, "speed_unit");
            EnqueueComboPreference(airspeedUnitCombo, "airspeed_unit");
            EnqueueComboPreference(temperatureUnitCombo, "temperature_unit");
            EnqueueComboPreference(pressureUnitCombo, "pressure_unit");
            EnqueueComboPreference(weightUnitCombo, "weight_unit");

            BridgeServer.EnqueueCommand("save_preferences");

            ScheduleDismissSavePopup();

            StopReenableTimer();
            _reenableTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            _reenableTimer.Tick += (_, _) =>
            {
                StopReenableTimer();
                if (IsDisposed) return;
                if (BridgeServer.IsBridgeConnected)
                    savePreferencesButton.Enabled = true;
            };
            _reenableTimer.Start();
        }

        private void OnNavigraphSignInClick(object? sender, EventArgs e)
        {
            if (BridgeServer.HasPendingCommand("start_navigraph_auth")) return;
            navigraphSignInButton.Enabled = false;
            navigraphStatusText.Text = "Awaiting code...";
            navigraphAuthCodeTextBox.Text = "";
            BridgeServer.EnqueueCommand("start_navigraph_auth");
            StartAuthTimeout();
        }

        private void OnNavigraphSignOutClick(object? sender, EventArgs e)
        {
            if (BridgeServer.HasPendingCommand("sign_out_navigraph")) return;
            navigraphSignOutButton.Enabled = false;
            BridgeServer.EnqueueCommand("sign_out_navigraph");
            StartSignOutTimeout();
        }

        private System.Windows.Forms.Timer? _reenableTimer;
        private System.Windows.Forms.Timer? _popupDismissTimer;

        private void StopReenableTimer()
        {
            if (_reenableTimer != null) { _reenableTimer.Stop(); _reenableTimer.Dispose(); _reenableTimer = null; }
        }

        private void StopPopupDismissTimer()
        {
            if (_popupDismissTimer != null) { _popupDismissTimer.Stop(); _popupDismissTimer.Dispose(); _popupDismissTimer = null; }
        }

        private void StartAuthTimeout()
        {
            StopAuthTimeout();
            _authTimeoutTimer = new System.Windows.Forms.Timer { Interval = 60000 };
            _authTimeoutTimer.Tick += (_, _) =>
            {
                StopAuthTimeout();
                if (IsDisposed) return;
                navigraphStatusText.Text = "Sign-in timed out";
                navigraphSignInButton.Enabled = BridgeServer.IsBridgeConnected;
                Announcer.Announce("Navigraph sign-in timed out");
            };
            _authTimeoutTimer.Start();
        }

        private void StopAuthTimeout()
        {
            if (_authTimeoutTimer != null) { _authTimeoutTimer.Stop(); _authTimeoutTimer.Dispose(); _authTimeoutTimer = null; }
        }

        private void StartSignOutTimeout()
        {
            StopSignOutTimeout();
            _signOutTimeoutTimer = new System.Windows.Forms.Timer { Interval = 15000 };
            _signOutTimeoutTimer.Tick += (_, _) =>
            {
                StopSignOutTimeout();
                if (IsDisposed) return;
                navigraphSignOutButton.Enabled = BridgeServer.IsBridgeConnected;
                Announcer.Announce("Navigraph sign-out timed out");
            };
            _signOutTimeoutTimer.Start();
        }

        private void StopSignOutTimeout()
        {
            if (_signOutTimeoutTimer != null) { _signOutTimeoutTimer.Stop(); _signOutTimeoutTimer.Dispose(); _signOutTimeoutTimer = null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopReenableTimer();
                StopPopupDismissTimer();
                StopAuthTimeout();
                StopSignOutTimeout();
            }
            base.Dispose(disposing);
        }

        private void ScheduleDismissSavePopup()
        {
            StopPopupDismissTimer();
            int attempts = 0;
            _popupDismissTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _popupDismissTimer.Tick += (_, _) =>
            {
                attempts++;
                if (IsDisposed || attempts > 6)
                {
                    StopPopupDismissTimer();
                    return;
                }
                const string js =
                    "(function(){try{" +
                    "var containers=document.querySelectorAll('.popup,.modal,.dialog,[class*=\"popup\"],[class*=\"modal\"],[class*=\"dialog\"],[role=\"dialog\"]');" +
                    "for(var ci=0;ci<containers.length;ci++){" +
                    "var c=containers[ci];" +
                    "try{var ccs=window.getComputedStyle(c);if(ccs.display==='none'||ccs.visibility==='hidden')continue;}catch(e){}" +
                    "var btns=c.querySelectorAll('button');" +
                    "for(var i=0;i<btns.length;i++){" +
                    "var b=btns[i];var t=(b.textContent||'').trim().toUpperCase();" +
                    "if(t==='OK'||t==='CLOSE'||t==='DISMISS'){" +
                    "try{var cs=window.getComputedStyle(b);if(cs.display==='none'||cs.visibility==='hidden')continue;}catch(e){}" +
                    "b.click();return 'EFB_PREF_POPUP_DISMISS clicked '+t;" +
                    "}}}" +
                    "return 'EFB_PREF_POPUP_DISMISS no popup';" +
                    "}catch(e){return 'EFB_PREF_POPUP_DISMISS err '+e.message;}})()";
                BridgeServer.EnqueueCommand("eval_js", new Dictionary<string, string> { ["code"] = js });
            };
            _popupDismissTimer.Start();
        }

        private void EnqueueTextPreference(string key, string? value)
        {
            BridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                { { "key", key }, { "value", value ?? "" } });
        }

        private void EnqueueComboPreference(ComboBox combo, string key)
        {
            string? value = combo.SelectedItem?.ToString();
            if (value != null)
            {
                BridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                    { { "key", key }, { "value", value } });
            }
        }
    }
}
