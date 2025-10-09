using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FBWBA.Database;
using FBWBA.Database.Models;
using FBWBA.Accessibility;

namespace FBWBA.Forms
{
    public partial class GateTeleportForm : Form
    {
        // Windows API declarations for focus management
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private TextBox icaoTextBox;
        private ListBox gateListBox;
        private Button teleportButton;
        private Button cancelButton;
        private Label statusLabel;

        private readonly AirportDatabase _database;
        private readonly ScreenReaderAnnouncer _announcer;
        private readonly IntPtr previousWindow;

        public ParkingSpot SelectedParkingSpot { get; private set; }
        public Airport SelectedAirport { get; private set; }

        public GateTeleportForm(AirportDatabase database, ScreenReaderAnnouncer announcer)
        {
            // Capture the current foreground window (likely the simulator)
            previousWindow = GetForegroundWindow();

            _database = database;
            _announcer = announcer;
            InitializeComponent();
            SetupAccessibility();
        }

        private void InitializeComponent()
        {
            Text = "Gate & Parking Teleport";
            Size = new Size(400, 350);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            // ICAO Label and TextBox
            var icaoLabel = new Label
            {
                Text = "Airport ICAO Code:",
                Location = new Point(20, 20),
                Size = new Size(120, 20),
                AccessibleName = "Airport ICAO Code"
            };

            icaoTextBox = new TextBox
            {
                Location = new Point(20, 45),
                Size = new Size(100, 25),
                CharacterCasing = CharacterCasing.Upper,
                MaxLength = 4,
                AccessibleName = "Airport ICAO Code",
                AccessibleDescription = "Enter the 4-letter ICAO code for the airport"
            };
            icaoTextBox.TextChanged += IcaoTextBox_TextChanged;
            icaoTextBox.KeyDown += IcaoTextBox_KeyDown;

            // Gate Label and ListBox
            var gateLabel = new Label
            {
                Text = "Available Gates & Parking:",
                Location = new Point(20, 80),
                Size = new Size(180, 20),
                AccessibleName = "Available Gates and Parking"
            };

            gateListBox = new ListBox
            {
                Location = new Point(20, 105),
                Size = new Size(350, 150),
                AccessibleName = "Gate and Parking List",
                AccessibleDescription = "Select a gate or parking spot from the list and press Enter to teleport"
            };
            gateListBox.SelectedIndexChanged += GateListBox_SelectedIndexChanged;
            gateListBox.KeyDown += GateListBox_KeyDown;

            // Status Label
            statusLabel = new Label
            {
                Location = new Point(20, 265),
                Size = new Size(350, 20),
                AccessibleName = "Status",
                Text = "Enter an airport ICAO code to see available gates and parking"
            };

            // Buttons
            teleportButton = new Button
            {
                Text = "Teleport",
                Location = new Point(215, 290),
                Size = new Size(75, 30),
                Enabled = false,
                AccessibleName = "Teleport to Selected Gate or Parking",
                AccessibleDescription = "Teleport aircraft to the selected gate or parking spot"
            };
            teleportButton.Click += TeleportButton_Click;

            cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(295, 290),
                Size = new Size(75, 30),
                DialogResult = DialogResult.Cancel,
                AccessibleName = "Cancel",
                AccessibleDescription = "Cancel gate teleport"
            };

            // Add controls to form
            Controls.AddRange(new Control[]
            {
                icaoLabel, icaoTextBox, gateLabel, gateListBox,
                statusLabel, teleportButton, cancelButton
            });

            AcceptButton = teleportButton;
            CancelButton = cancelButton;
        }

        private void SetupAccessibility()
        {
            // Set tab order for logical navigation
            icaoTextBox.TabIndex = 0;
            gateListBox.TabIndex = 1;
            teleportButton.TabIndex = 2;
            cancelButton.TabIndex = 3;

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

        private void IcaoTextBox_TextChanged(object sender, EventArgs e)
        {
            string icao = icaoTextBox.Text.Trim();

            if (icao.Length == 4)
            {
                LoadGatesAndParking(icao);
            }
            else
            {
                ClearGatesAndParking();
            }
        }

        private void IcaoTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && gateListBox.Items.Count > 0)
            {
                gateListBox.Focus();
                if (gateListBox.Items.Count > 0)
                {
                    gateListBox.SelectedIndex = 0;
                }
                e.Handled = true;
            }
        }

        private void LoadGatesAndParking(string icao)
        {
            try
            {
                var airport = _database.GetAirport(icao);
                if (airport == null)
                {
                    statusLabel.Text = $"Airport {icao} not found in database";
                    ClearGatesAndParking();
                    return;
                }

                var parkingSpots = _database.GetParkingSpots(icao);
                if (parkingSpots.Count == 0)
                {
                    statusLabel.Text = $"No gates or parking found for {icao}";
                    ClearGatesAndParking();
                    return;
                }

                SelectedAirport = airport;
                gateListBox.Items.Clear();
                gateListBox.Items.AddRange(parkingSpots.ToArray());

                var gateCount = parkingSpots.Count(p => p.GetParkingType().Contains("Gate"));
                var parkingCount = parkingSpots.Count - gateCount;

                string statusText;
                if (gateCount > 0 && parkingCount > 0)
                    statusText = $"Found {gateCount} gates and {parkingCount} parking spots for {airport.Name}";
                else if (gateCount > 0)
                    statusText = $"Found {gateCount} gates for {airport.Name}";
                else
                    statusText = $"Found {parkingCount} parking spots for {airport.Name}";

                statusLabel.Text = statusText;

                teleportButton.Enabled = false;
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Error loading gates and parking: {ex.Message}";
                ClearGatesAndParking();
            }
        }

        private void ClearGatesAndParking()
        {
            gateListBox.Items.Clear();
            SelectedParkingSpot = null;
            SelectedAirport = null;
            teleportButton.Enabled = false;

            if (string.IsNullOrEmpty(icaoTextBox.Text))
            {
                statusLabel.Text = "Enter an airport ICAO code to see available gates and parking";
            }
        }

        private void GateListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (gateListBox.SelectedItem is ParkingSpot selectedSpot)
            {
                SelectedParkingSpot = selectedSpot;
                teleportButton.Enabled = true;

                string description = $"Selected {selectedSpot}";
                statusLabel.Text = description;
            }
            else
            {
                SelectedParkingSpot = null;
                teleportButton.Enabled = false;
            }
        }

        private void GateListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && gateListBox.SelectedItem != null)
            {
                TeleportButton_Click(sender, e);
                e.Handled = true;
            }
        }

        private void TeleportButton_Click(object sender, EventArgs e)
        {
            if (SelectedParkingSpot != null && SelectedAirport != null)
            {
                DialogResult = DialogResult.OK;
                Close();

                // Restore focus to the previous window (likely the simulator)
                if (previousWindow != IntPtr.Zero)
                {
                    SetForegroundWindow(previousWindow);
                }
            }
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            // Handle Escape key
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);

            // Restore focus to the previous window (likely the simulator) if canceled
            if (DialogResult == DialogResult.Cancel && previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(previousWindow);
            }
        }
    }
}