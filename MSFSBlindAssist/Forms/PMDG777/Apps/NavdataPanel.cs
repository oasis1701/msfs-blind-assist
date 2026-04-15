using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG777.Apps
{
    /// <summary>
    /// Navigation Data sub-tab — AIRAC cycle status read directly from the
    /// tablet's Navigation Data page via read_values. The update button is
    /// a passthrough to the tablet's own navdataupdate_update button; progress
    /// is polled from the tablet's progress bar while an install runs.
    ///
    /// Navigraph sign-in/out now lives on the Preferences tab to match PMDG's
    /// own layout.
    /// </summary>
    public class NavdataPanel : EfbAppPanelBase
    {
        private const string ValuesTag = "navdata";
        private const string IdCurrentCycle = "navdataupdate_current_cycle";
        private const string IdValidityPeriod = "navdataupdate_validity_period";
        private const string IdAvailableCycle = "navdataupdate_available_cycle";
        private const string IdUpdateButton = "navdataupdate_update";
        private const string IdProgressText = "navdataupdate_progress";
        private const string IdProgressBar = "navdataupdate_progress_bar";

        private TextBox currentCycleValue = null!;
        private TextBox validityPeriodValue = null!;
        private TextBox availableCycleValue = null!;
        private Button updateButton = null!;
        private TextBox progressText = null!;

        private System.Windows.Forms.Timer? _progressPollTimer;
        private int _lastAnnouncedProgressMilestone = -1;
        private bool _installInProgress;

        public override Control? InitialFocusControl => updateButton;

        protected override void BuildUi()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10);
            AccessibleName = "Navigation Data";

            int y = 10;
            const int labelX = 10;
            const int valueX = 180;
            const int valueWidth = 300;
            const int rowHeight = 28;
            int tabIdx = 0;

            Controls.Add(new Label
            {
                Text = "AIRAC Cycle",
                Location = new System.Drawing.Point(labelX, y),
                Size = new System.Drawing.Size(valueWidth + valueX - labelX, 20),
                Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold),
                AccessibleName = "AIRAC Cycle section"
            });
            y += rowHeight;

            void CycleRow(string labelText, string accName, out TextBox field)
            {
                Controls.Add(new Label { Text = labelText, Location = new System.Drawing.Point(labelX, y), AutoSize = true });
                field = CreateReadOnlyField(accName);
                field.Location = new System.Drawing.Point(valueX, y);
                field.Size = new System.Drawing.Size(valueWidth, 22);
                field.TabIndex = tabIdx++;
                Controls.Add(field);
                y += rowHeight;
            }

            CycleRow("Current Cycle:", "Currently installed AIRAC cycle", out currentCycleValue);
            CycleRow("Validity Period:", "AIRAC validity period", out validityPeriodValue);
            CycleRow("Latest Available:", "Latest available AIRAC cycle", out availableCycleValue);
            y += 5;

            updateButton = new Button
            {
                Text = "Reinstall / Update NavData",
                Location = new System.Drawing.Point(labelX, y),
                Size = new System.Drawing.Size(220, 30),
                Enabled = false,
                AccessibleName = "Reinstall or update navigation data",
                TabIndex = tabIdx++
            };
            updateButton.Click += OnUpdateClick;
            Controls.Add(updateButton);
            y += 40;

            progressText = CreateReadOnlyField("Update Progress");
            progressText.Text = "";
            progressText.Location = new System.Drawing.Point(labelX, y);
            progressText.Size = new System.Drawing.Size(valueWidth + valueX - labelX, 22);
            progressText.TabIndex = tabIdx++;
            Controls.Add(progressText);
        }

        public override void OnActivated()
        {
            if (!BridgeServer.IsBridgeConnected) return;
            RequestCycleRead();
        }

        public void SetConnected(bool connected)
        {
            if (IsDisposed) return;
            updateButton.Enabled = connected && updateButton.Enabled;
        }

        private void RequestCycleRead()
        {
            BridgeServer.EnqueueCommand("read_values", new Dictionary<string, string>
            {
                ["tag"] = ValuesTag,
                ["ids"] = string.Join(",", new[]
                {
                    IdCurrentCycle, IdValidityPeriod, IdAvailableCycle,
                    IdUpdateButton, IdProgressText, IdProgressBar
                })
            });
        }

        private void OnUpdateClick(object? sender, EventArgs e)
        {
            if (!BridgeServer.IsBridgeConnected) return;
            updateButton.Enabled = false;
            progressText.Text = "Starting...";
            _lastAnnouncedProgressMilestone = -1;
            _installInProgress = true;
            BridgeServer.EnqueueCommand("click_by_id", new Dictionary<string, string> { ["id"] = IdUpdateButton });
            StartProgressPoll();
        }

        protected override void HandleStateUpdate(EFBStateUpdateEventArgs e)
        {
            if (e.Type == "values" && e.Data.GetValueOrDefault("_tag", "") == ValuesTag)
                HandleCycleValues(e.Data);
        }

        private void HandleCycleValues(Dictionary<string, string> data)
        {
            string current = data.GetValueOrDefault(IdCurrentCycle, "");
            string validity = data.GetValueOrDefault(IdValidityPeriod, "");
            string available = data.GetValueOrDefault(IdAvailableCycle, "");
            string buttonLabel = data.GetValueOrDefault(IdUpdateButton, "");
            string tabletProgress = data.GetValueOrDefault(IdProgressText, "");
            string progressPct = data.GetValueOrDefault(IdProgressBar, "");

            currentCycleValue.Text = string.IsNullOrWhiteSpace(current) ? "\u2014" : current;
            validityPeriodValue.Text = string.IsNullOrWhiteSpace(validity) ? "\u2014" : validity;
            availableCycleValue.Text = string.IsNullOrWhiteSpace(available) ? "\u2014" : available;

            if (!string.IsNullOrWhiteSpace(buttonLabel))
                updateButton.Text = buttonLabel;
            updateButton.Enabled = BridgeServer.IsBridgeConnected
                                    && !string.IsNullOrWhiteSpace(buttonLabel);

            int percent = 0;
            int.TryParse(progressPct, out percent);
            if (!string.IsNullOrWhiteSpace(tabletProgress))
                progressText.Text = percent > 0 ? $"{tabletProgress} ({percent}%)" : tabletProgress;
            else if (percent > 0)
                progressText.Text = $"{percent}%";
            else if (!_installInProgress)
                progressText.Text = "";

            if (_installInProgress && percent > 0)
            {
                int milestone = (percent / 25) * 25;
                if (milestone > _lastAnnouncedProgressMilestone)
                {
                    _lastAnnouncedProgressMilestone = milestone;
                    if (milestone > 0) Announcer.Announce($"{milestone} percent");
                }
            }

            if (_installInProgress && percent <= 0
                && !string.IsNullOrWhiteSpace(current) && current == available)
            {
                _installInProgress = false;
                StopProgressPoll();
                progressText.Text = "Update complete";
                Announcer.Announce("Navigation data update complete");
            }
        }

        private void StartProgressPoll()
        {
            StopProgressPoll();
            _progressPollTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _progressPollTimer.Tick += (_, _) =>
            {
                if (IsDisposed) { StopProgressPoll(); return; }
                RequestCycleRead();
            };
            _progressPollTimer.Start();
        }

        private void StopProgressPoll()
        {
            if (_progressPollTimer != null)
            {
                _progressPollTimer.Stop();
                _progressPollTimer.Dispose();
                _progressPollTimer = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) StopProgressPoll();
            base.Dispose(disposing);
        }
    }
}
