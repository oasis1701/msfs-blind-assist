using FBWBA.Accessibility;
using FBWBA.Services;

namespace FBWBA.Forms;

public partial class METARReportForm : Form
    {
        // Windows API declarations for focus management
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private TextBox icaoTextBox = null!;
        private TextBox metarTextBox = null!;
        private Button closeButton = null!;
        private Label icaoLabel = null!;
        private Label metarLabel = null!;
        private Label statusLabel = null!;

        private readonly ScreenReaderAnnouncer _announcer;
        private readonly IntPtr previousWindow;

        public METARReportForm(ScreenReaderAnnouncer announcer)
        {
            // Capture the current foreground window (likely the simulator)
            previousWindow = GetForegroundWindow();

            _announcer = announcer;
            InitializeComponent();
            SetupAccessibility();
        }

        private void InitializeComponent()
        {
            Text = "METAR Report";
            Size = new Size(500, 400);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

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

            // METAR Label
            metarLabel = new Label
            {
                Text = "METAR Report:",
                Location = new Point(20, 85),
                Size = new Size(120, 20),
                AccessibleName = "METAR Report Label"
            };

            // METAR TextBox (read-only, multiline)
            metarTextBox = new TextBox
            {
                Location = new Point(20, 110),
                Size = new Size(440, 180),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = "METAR Report",
                AccessibleDescription = "Raw METAR data from VATSIM",
                Font = new Font("Consolas", 9, FontStyle.Regular),
                Text = "Enter an ICAO code above and press Enter to fetch METAR data."
            };

            // Close Button
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
                metarLabel, metarTextBox, closeButton
            });

            CancelButton = closeButton;
        }

        private void SetupAccessibility()
        {
            // Set tab order for logical navigation
            icaoTextBox.TabIndex = 0;
            metarTextBox.TabIndex = 1;
            closeButton.TabIndex = 2;

            // Focus and bring window to front when opened
            Load += (sender, e) =>
            {
                BringToFront();
                Activate();
                TopMost = true;
                TopMost = false; // Flash to bring to front
                icaoTextBox.Focus();
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

                // Disable input during fetch
                icaoTextBox.Enabled = false;
                closeButton.Enabled = false;

                string metar = await VATSIMService.GetMETARAsync(icao);

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
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Error fetching METAR";
                metarTextBox.Text = $"Error retrieving METAR data for {icao}: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[METARReportForm] Error fetching METAR: {ex.Message}");
            }
            finally
            {
                // Re-enable controls
                icaoTextBox.Enabled = true;
                closeButton.Enabled = true;
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