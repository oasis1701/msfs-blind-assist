using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG777.Apps
{
    /// <summary>
    /// EFB Dashboard sub-tab — SimBrief import and flight plan summary.
    /// Owns fetch/send-to-FMC flow with timeouts.
    /// </summary>
    public class DashboardPanel : EfbAppPanelBase
    {
        private Button fetchButton = null!;
        private Button sendToFmcButton = null!;
        private TextBox statusText = null!;
        private TextBox callsignValue = null!;
        private TextBox originValue = null!;
        private TextBox destValue = null!;
        private TextBox altValue = null!;
        private TextBox cruiseAltValue = null!;
        private TextBox costIndexValue = null!;
        private TextBox zfwValue = null!;
        private TextBox fuelValue = null!;
        private TextBox windValue = null!;
        private TextBox aircraftRegValue = null!;
        private TextBox aircraftTypeValue = null!;
        private TextBox coRouteValue = null!;
        private TextBox routeDistValue = null!;
        private TextBox plannedDepartureValue = null!;
        private TextBox estimatedDepartureValue = null!;
        private TextBox plannedArrivalValue = null!;
        private TextBox estimatedArrivalValue = null!;
        private TextBox estTimeEnrouteValue = null!;

        private System.Windows.Forms.Timer? _fetchTimeoutTimer;
        private System.Windows.Forms.Timer? _sendToFmcTimeoutTimer;
        private bool _simbriefLoaded;

        public override Control? InitialFocusControl => fetchButton;

        public bool IsSimBriefLoaded => _simbriefLoaded;

        public override void OnActivated()
        {
            // Ask the bridge to re-post the currently-loaded SimBrief payload, if any.
            // Populates the dashboard without requiring the user to press Fetch again.
            if (BridgeServer.IsBridgeConnected)
            {
                ArmLoadAnnouncement();
                BridgeServer.EnqueueCommand("replay_simbrief");
            }
        }

        protected override void BuildUi()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10);
            AccessibleName = "Dashboard";

            int y = 10;
            const int labelX = 10;
            const int valueX = 160;
            const int valueWidth = 300;
            const int rowHeight = 28;

            statusText = new TextBox
            {
                Text = "Ready",
                Location = new System.Drawing.Point(labelX, y),
                Size = new System.Drawing.Size(valueWidth + valueX - labelX, 22),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = System.Drawing.SystemColors.Control,
                Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold),
                AccessibleName = "Status",
                TabIndex = 0
            };
            y += rowHeight + 5;

            fetchButton = new Button
            {
                Text = "Fetch SimBrief",
                Location = new System.Drawing.Point(labelX, y),
                Size = new System.Drawing.Size(140, 30),
                AccessibleName = "Fetch SimBrief",
                TabIndex = 1
            };
            sendToFmcButton = new Button
            {
                Text = "Send to FMC",
                Location = new System.Drawing.Point(valueX, y),
                Size = new System.Drawing.Size(140, 30),
                Enabled = false,
                AccessibleName = "Send to FMC",
                TabIndex = 2
            };
            fetchButton.Click += OnFetchClick;
            sendToFmcButton.Click += OnSendToFmcClick;
            y += 40;

            int tabIdx = 3;
            void Row(string labelText, string accName, out TextBox field)
            {
                var lbl = new Label { Text = labelText, Location = new System.Drawing.Point(labelX, y), AutoSize = true };
                field = CreateReadOnlyField(accName);
                field.Location = new System.Drawing.Point(valueX, y);
                field.Size = new System.Drawing.Size(valueWidth, 22);
                field.TabIndex = tabIdx++;
                Controls.Add(lbl);
                Controls.Add(field);
                y += rowHeight;
            }

            Row("Callsign:", "Callsign", out callsignValue);
            Row("Aircraft Reg:", "Aircraft Registration", out aircraftRegValue);
            Row("Aircraft Type:", "Aircraft Type", out aircraftTypeValue);
            Row("Origin:", "Origin", out originValue);
            Row("Destination:", "Destination", out destValue);
            Row("Alternate:", "Alternate", out altValue);
            Row("CO RTE:", "Company Route", out coRouteValue);
            Row("Route Dist:", "Route Distance", out routeDistValue);
            Row("Cruise Altitude:", "Cruise Altitude", out cruiseAltValue);
            Row("Cost Index:", "Cost Index", out costIndexValue);
            Row("ZFW:", "Zero Fuel Weight", out zfwValue);
            Row("Total Fuel:", "Total Fuel", out fuelValue);
            Row("Average Wind:", "Average Wind", out windValue);
            Row("Planned Departure:", "Planned Departure", out plannedDepartureValue);
            Row("Estimated Departure:", "Estimated Departure", out estimatedDepartureValue);
            Row("Planned Arrival:", "Planned Arrival", out plannedArrivalValue);
            Row("Estimated Arrival:", "Estimated Arrival", out estimatedArrivalValue);
            Row("Est. Time Enroute:", "Estimated Time Enroute", out estTimeEnrouteValue);

            Controls.Add(statusText);
            Controls.Add(fetchButton);
            Controls.Add(sendToFmcButton);
        }

        public void SetConnected(bool connected)
        {
            if (IsDisposed) return;
            if (!connected)
            {
                // Drop the stale flag so reconnect doesn't re-enable Send-to-FMC
                // against a flight plan that no longer lives in the tablet.
                _simbriefLoaded = false;
            }
            fetchButton.Enabled = connected;
            sendToFmcButton.Enabled = connected && _simbriefLoaded;
        }

        private void OnFetchClick(object? sender, EventArgs e)
        {
            if (BridgeServer.HasPendingCommand("fetch_simbrief")) return;
            fetchButton.Enabled = false;
            statusText.Text = "Fetching...";
            BridgeServer.EnqueueCommand("fetch_simbrief");
            StartFetchTimeout();
        }

        private void OnSendToFmcClick(object? sender, EventArgs e)
        {
            if (BridgeServer.HasPendingCommand("send_to_fmc")) return;
            sendToFmcButton.Enabled = false;
            BridgeServer.EnqueueCommand("send_to_fmc");
            StartSendToFmcTimeout();
        }

        protected override void HandleStateUpdate(EFBStateUpdateEventArgs e)
        {
            switch (e.Type)
            {
                case "simbrief_loaded":
                    AnnounceLoadedIfPending();
                    StopFetchTimeout();
                    _simbriefLoaded = true;
                    UpdateFlightDetails(e.Data);
                    statusText.Text = "Loaded";
                    fetchButton.Enabled = BridgeServer.IsBridgeConnected;
                    sendToFmcButton.Enabled = BridgeServer.IsBridgeConnected;
                    string origin = e.Data.GetValueOrDefault("origin_icao", "");
                    string dest = e.Data.GetValueOrDefault("dest_icao", "");
                    Announcer.Announce($"SimBrief flight plan loaded: {origin} to {dest}");
                    break;

                case "simbrief_fetch_result":
                    StopSendToFmcTimeout();
                    bool success = bool.TryParse(e.Data.GetValueOrDefault("success", "false"), out var s) && s;
                    string message = e.Data.GetValueOrDefault("message", "");
                    sendToFmcButton.Enabled = BridgeServer.IsBridgeConnected && _simbriefLoaded;
                    if (success)
                        Announcer.Announce($"FMC file transfer complete: {message}");
                    else if (!string.IsNullOrEmpty(message))
                        Announcer.Announce($"FMC transfer result: {message}");
                    break;

                case "fmc_upload_started":
                    Announcer.Announce("Flight plan sent to FMC");
                    break;

                case "error":
                    // Only react if we appear to be in a fetch cycle.
                    if (_fetchTimeoutTimer != null || _sendToFmcTimeoutTimer != null)
                    {
                        StopFetchTimeout();
                        StopSendToFmcTimeout();
                        string errorMsg = e.Data.GetValueOrDefault("message", "Unknown error");
                        statusText.Text = $"Error: {errorMsg}";
                        fetchButton.Enabled = BridgeServer.IsBridgeConnected;
                        sendToFmcButton.Enabled = BridgeServer.IsBridgeConnected && _simbriefLoaded;
                        Announcer.Announce($"EFB error: {errorMsg}");
                    }
                    break;
            }
        }

        private void UpdateFlightDetails(Dictionary<string, string> data)
        {
            callsignValue.Text = data.GetValueOrDefault("callsign", "\u2014");
            originValue.Text = data.GetValueOrDefault("origin_icao", "\u2014");
            destValue.Text = data.GetValueOrDefault("dest_icao", "\u2014");
            altValue.Text = data.GetValueOrDefault("alt_icao", "\u2014");
            cruiseAltValue.Text = data.GetValueOrDefault("cruise_alt", "\u2014");
            costIndexValue.Text = data.GetValueOrDefault("cost_index", "\u2014");
            string ofpUnit = data.GetValueOrDefault("ofp_weight_unit", "");
            zfwValue.Text = FormatWeight(data.GetValueOrDefault("zfw", ""), ofpUnit);
            fuelValue.Text = FormatWeight(data.GetValueOrDefault("fuel_total", ""), ofpUnit);
            windValue.Text = data.GetValueOrDefault("avg_wind", "\u2014");
            aircraftRegValue.Text = data.GetValueOrDefault("aircraft_reg", "\u2014");
            aircraftTypeValue.Text = data.GetValueOrDefault("aircraft_type", "\u2014");
            coRouteValue.Text = data.GetValueOrDefault("co_route", "\u2014");
            routeDistValue.Text = data.GetValueOrDefault("route_dist", "\u2014");
            plannedDepartureValue.Text = NonEmptyOrDash(data.GetValueOrDefault("planned_departure", ""));
            estimatedDepartureValue.Text = NonEmptyOrDash(data.GetValueOrDefault("estimated_departure", ""));
            plannedArrivalValue.Text = NonEmptyOrDash(data.GetValueOrDefault("planned_arrival", ""));
            estimatedArrivalValue.Text = NonEmptyOrDash(data.GetValueOrDefault("estimated_arrival", ""));
            estTimeEnrouteValue.Text = NonEmptyOrDash(data.GetValueOrDefault("est_time_enroute", ""));
        }

        private static string NonEmptyOrDash(string raw) =>
            string.IsNullOrWhiteSpace(raw) ? "\u2014" : raw;

        private static string FormatWeight(string raw, string ofpUnit)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "\u2014";
            if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                return raw;

            string target = (PreferencesCache.WeightUnit ?? "kg").ToLowerInvariant();
            string source = (ofpUnit ?? "").ToLowerInvariant();
            bool sourceIsLbs = source == "lbs" || source == "lb";
            bool sourceIsKgs = source == "kgs" || source == "kg";
            bool targetIsLbs = target == "lbs" || target == "lb";

            if (sourceIsLbs && !targetIsLbs) value *= 0.45359237;
            else if (sourceIsKgs && targetIsLbs) value *= 2.20462262;

            string unitLabel = targetIsLbs ? "lb" : "kg";
            return $"{Math.Round(value):0} {unitLabel}";
        }

        private void StartFetchTimeout()
        {
            StopFetchTimeout();
            _fetchTimeoutTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            _fetchTimeoutTimer.Tick += (_, _) =>
            {
                StopFetchTimeout();
                if (IsDisposed) return;
                statusText.Text = "Fetch timed out \u2014 try again";
                fetchButton.Enabled = BridgeServer.IsBridgeConnected;
                Announcer.Announce("SimBrief fetch timed out");
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

        private void StartSendToFmcTimeout()
        {
            StopSendToFmcTimeout();
            _sendToFmcTimeoutTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            _sendToFmcTimeoutTimer.Tick += (_, _) =>
            {
                StopSendToFmcTimeout();
                if (IsDisposed) return;
                sendToFmcButton.Enabled = BridgeServer.IsBridgeConnected && _simbriefLoaded;
                Announcer.Announce("FMC transfer timed out");
            };
            _sendToFmcTimeoutTimer.Start();
        }

        private void StopSendToFmcTimeout()
        {
            if (_sendToFmcTimeoutTimer != null)
            {
                _sendToFmcTimeoutTimer.Stop();
                _sendToFmcTimeoutTimer.Dispose();
                _sendToFmcTimeoutTimer = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopFetchTimeout();
                StopSendToFmcTimeout();
            }
            base.Dispose(disposing);
        }
    }
}
