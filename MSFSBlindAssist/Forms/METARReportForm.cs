using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms;

public partial class METARReportForm : Form
    {
        // Windows API declarations for focus management
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private TextBox icaoTextBox = null!;
        private TextBox metarTextBox = null!;
        private TextBox asMetarTextBox = null!;
        private Button closeButton = null!;
        private Label icaoLabel = null!;
        private Label metarLabel = null!;
        private Label asMetarLabel = null!;
        private Label statusLabel = null!;

        private readonly ScreenReaderAnnouncer _announcer;
        private readonly IntPtr previousWindow;
        // Per-form AS client. Each form does its own parallel-probe detection
        // on first fetch (~1.2 s worst case when AS isn't running). We don't
        // share a singleton because the existing WeatherRadarForm also keeps
        // its own instance, and they don't conflict — the underlying HttpClient
        // is static-shared.
        private readonly ActiveSkyClient _activeSky = new();

        public METARReportForm(ScreenReaderAnnouncer announcer)
        {
            // Capture the current foreground window (likely the simulator)
            previousWindow = GetForegroundWindow();

            _announcer = announcer;
            InitializeComponent();
            SetupAccessibility();
        }

        public void ShowForm()
        {
            // Show as modeless window (Load event handler will bring to front and set focus)
            Show();
        }

        private void InitializeComponent()
        {
            Text = "METAR Report";
            // Default (compact) size — used when ActiveSky is NOT detected.
            // Form silently grows to 580px tall on Load if AS detection
            // succeeds, revealing the AS METAR section. User-invisible
            // fallback: when AS isn't running we look identical to the
            // pre-AS form.
            Size = new Size(500, 400);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;

            // ICAO Label
            icaoLabel = new Label
            {
                Text = "Airport ICAO Code:",
                Location = new Point(20, 20),
                Size = new Size(120, 20),
                AccessibleName = "Airport ICAO Code Label"
            };

            // ICAO TextBox
            icaoTextBox = new TextBox
            {
                Location = new Point(20, 45),
                Size = new Size(100, 25),
                CharacterCasing = CharacterCasing.Upper,
                MaxLength = 4,
                AccessibleName = "Airport ICAO Code",
                AccessibleDescription = "Enter 4-letter ICAO code and press Enter to retrieve METAR"
            };
            icaoTextBox.KeyDown += IcaoTextBox_KeyDown;

            // Status Label
            statusLabel = new Label
            {
                Location = new Point(130, 45),
                Size = new Size(300, 25),
                Text = "",
                AccessibleName = "Status"
            };

            // VATSIM METAR Label
            metarLabel = new Label
            {
                Text = "VATSIM METAR:",
                Location = new Point(20, 85),
                Size = new Size(200, 20),
                AccessibleName = "VATSIM METAR Label"
            };

            // VATSIM METAR TextBox (read-only, multiline)
            metarTextBox = new TextBox
            {
                Location = new Point(20, 110),
                Size = new Size(440, 160),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = "VATSIM METAR",
                AccessibleDescription = "Raw METAR data from VATSIM",
                Font = new Font("Consolas", 9, FontStyle.Regular),
                Text = "Enter an ICAO code above and press Enter to fetch METAR data."
            };

            // ActiveSky METAR Label (shown only when AS is detected; label
            // text updates on fetch to indicate status — we leave it visible
            // either way so the user can tab to the box and read the status
            // message there too).
            // ActiveSky METAR section — invisible by default. Revealed in
            // SetupAccessibility's async Load handler IF ActiveSky is detected.
            // When AS isn't running, the user sees the original VATSIM-only
            // form unchanged.
            asMetarLabel = new Label
            {
                Text = "ActiveSky METAR:",
                Location = new Point(20, 285),
                Size = new Size(440, 20),
                AccessibleName = "ActiveSky METAR Label",
                Visible = false
            };

            asMetarTextBox = new TextBox
            {
                Location = new Point(20, 310),
                Size = new Size(440, 160),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = "ActiveSky METAR",
                AccessibleDescription = "Raw METAR data from HiFi ActiveSky",
                Font = new Font("Consolas", 9, FontStyle.Regular),
                Text = "Enter an ICAO code above and press Enter to fetch ActiveSky METAR.",
                Visible = false
            };

            // Close Button — positioned for compact form (AS hidden). When
            // AS detection succeeds in Load, we shift it down to y=490 so it
            // sits below the AS section.
            closeButton = new Button
            {
                Text = "&Close",
                Location = new Point(385, 310),
                Size = new Size(75, 30),
                DialogResult = DialogResult.OK,
                AccessibleName = "Close",
                AccessibleDescription = "Close METAR report window"
            };
            closeButton.Click += CloseButton_Click;

            // Add controls to form
            Controls.AddRange(new Control[]
            {
                icaoLabel, icaoTextBox, statusLabel,
                metarLabel, metarTextBox,
                asMetarLabel, asMetarTextBox,
                closeButton
            });

            CancelButton = closeButton;
        }

        private void SetupAccessibility()
        {
            // Set tab order for logical navigation. AS textbox sits between
            // VATSIM METAR and Close — when AS is hidden, the OS skips it
            // automatically because TabStop honours Visible.
            icaoTextBox.TabIndex = 0;
            metarTextBox.TabIndex = 1;
            asMetarTextBox.TabIndex = 2;
            closeButton.TabIndex = 3;

            // Focus + bring to front, then async-detect ActiveSky. If AS is
            // running, reveal the AS section and grow the form. If not, the
            // form stays compact and the user sees the original VATSIM-only
            // layout — silent fallback by design.
            Load += async (sender, e) =>
            {
                BringToFront();
                Activate();
                TopMost = true;
                TopMost = false; // Flash to bring to front
                icaoTextBox.Focus();

                bool asAvailable = await _activeSky.IsRunningAsync();
                if (asAvailable && IsHandleCreated && !IsDisposed)
                {
                    asMetarLabel.Visible = true;
                    asMetarTextBox.Visible = true;
                    closeButton.Location = new Point(385, 490);
                    Size = new Size(500, 580);
                    // Re-center after grow — StartPosition.CenterScreen ran
                    // against the original 400px form, so without this the
                    // bottom of the grown form can fall below the screen on
                    // smaller displays (1366×768 etc.).
                    CenterToScreen();
                }
            };
        }

        private async void IcaoTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                await FetchMETAR();
            }
        }

        private async Task FetchMETAR()
        {
            string icao = icaoTextBox.Text.Trim();

            if (string.IsNullOrEmpty(icao))
            {
                statusLabel.Text = "Please enter an ICAO code";
                metarTextBox.Text = "Enter an ICAO code above and press Enter to fetch METAR data.";
                return;
            }

            if (icao.Length != 4)
            {
                statusLabel.Text = "ICAO code must be 4 characters";
                metarTextBox.Text = "ICAO code must be exactly 4 characters (e.g., KJFK, EGLL, KATL).";
                return;
            }

            try
            {
                statusLabel.Text = "Fetching METAR...";
                metarTextBox.Text = "Loading METAR data from VATSIM...";
                if (asMetarTextBox.Visible)
                    asMetarTextBox.Text = "Loading METAR data from ActiveSky...";

                // Disable input during fetch
                icaoTextBox.Enabled = false;
                closeButton.Enabled = false;

                // Fetch VATSIM and AS METARs in parallel when AS is active.
                // When AS isn't detected, we don't even kick off the AS task
                // — keeps things silent on systems without ActiveSky.
                Task<string> vatsimTask = VATSIMService.GetMETARAsync(icao);
                Task<string?> asTask = asMetarTextBox.Visible
                    ? _activeSky.GetMetarAsync(icao)
                    : Task.FromResult<string?>(null);

                string metar = await vatsimTask;

                if (IsDisposed) return;

                if (string.IsNullOrEmpty(metar) || metar.Trim().Length == 0)
                {
                    statusLabel.Text = "No METAR data available";
                    metarTextBox.Text = $"No METAR data found for {icao}. The airport may not have current weather reporting or the ICAO code may be invalid.";
                }
                else
                {
                    statusLabel.Text = $"METAR for {icao}";
                    metarTextBox.Text = metar.Trim();

                    // Focus the METAR text box so screen reader reads the content
                    metarTextBox.Focus();
                    metarTextBox.SelectAll();
                }

                // ActiveSky METAR — only when the AS section is visible.
                if (asMetarTextBox.Visible)
                {
                    string? asMetar = await asTask;

                    if (IsDisposed) return;

                    if (string.IsNullOrWhiteSpace(asMetar))
                    {
                        asMetarTextBox.Text =
                            $"No ActiveSky METAR found for {icao}. The airport may not be in AS's station list, or AS may have stopped responding.";
                    }
                    else
                    {
                        asMetarTextBox.Text = asMetar.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Error fetching METAR";
                metarTextBox.Text = $"Error retrieving METAR data for {icao}: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[METARReportForm] Error fetching METAR: {ex.Message}");
            }
            finally
            {
                // Re-enable controls only if the form is still alive — the
                // user may have closed it during the fetch.
                if (!IsDisposed)
                {
                    icaoTextBox.Enabled = true;
                    closeButton.Enabled = true;
                }
            }
        }

        private void CloseButton_Click(object? sender, EventArgs e)
        {
            Close();

            // Restore focus to the previous window (likely the simulator)
            if (previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(previousWindow);
            }
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            // Handle Escape key
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

            // Restore focus to the previous window (likely the simulator) if escape was used
            if (previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(previousWindow);
            }
        }
}