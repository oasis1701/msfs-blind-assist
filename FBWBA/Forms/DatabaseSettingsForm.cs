using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FBWBA.Accessibility;
using FBWBA.Database;
using FBWBA.Settings;

namespace FBWBA.Forms
{
    public partial class DatabaseSettingsForm : Form
    {
        private RadioButton fs2020Radio;
        private RadioButton fs2024Radio;
        private Label statusLabel;
        private Button buildDatabaseButton;
        private Button refreshButton;
        private Button okButton;
        private Button cancelButton;
        private Label titleLabel;
        private Label infoLabel;

        private readonly ScreenReaderAnnouncer announcer;

        public DatabaseSettingsForm(ScreenReaderAnnouncer announcer)
        {
            this.announcer = announcer;
            InitializeComponent();
            LoadCurrentSettings();
            SetupAccessibility();
            UpdateDatabaseStatus();
        }

        private void InitializeComponent()
        {
            Text = "Database Configuration";
            Size = new Size(600, 400);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            int yPos = 20;

            // Title Label
            titleLabel = new Label
            {
                Text = "Select Your Flight Simulator",
                Location = new Point(20, yPos),
                Size = new Size(550, 25),
                AccessibleName = "Database Configuration Title",
                Font = new Font(Font.FontFamily, 12, FontStyle.Bold)
            };
            yPos += 40;

            // FS2020 Radio Button
            fs2020Radio = new RadioButton
            {
                Text = "Flight Simulator 2020",
                Location = new Point(40, yPos),
                Size = new Size(500, 30),
                AccessibleName = "Flight Simulator 2020",
                AccessibleDescription = "Use navdatareader database for FS2020",
                Font = new Font(Font.FontFamily, 10)
            };
            fs2020Radio.CheckedChanged += SimulatorVersionRadio_CheckedChanged;
            yPos += 40;

            // FS2024 Radio Button
            fs2024Radio = new RadioButton
            {
                Text = "Flight Simulator 2024",
                Location = new Point(40, yPos),
                Size = new Size(500, 30),
                AccessibleName = "Flight Simulator 2024",
                AccessibleDescription = "Use navdatareader database for FS2024",
                Font = new Font(Font.FontFamily, 10)
            };
            fs2024Radio.CheckedChanged += SimulatorVersionRadio_CheckedChanged;
            yPos += 50;

            // Info Label
            infoLabel = new Label
            {
                Text = "The application uses navdatareader to build airport databases.\nDatabases are stored in: " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FBWBA", "databases"),
                Location = new Point(40, yPos),
                Size = new Size(520, 50),
                AccessibleName = "Database information",
                ForeColor = Color.Gray,
                Font = new Font(Font.FontFamily, 8)
            };
            yPos += 60;

            // Status Label
            statusLabel = new Label
            {
                Text = "Status: Checking...",
                Location = new Point(40, yPos),
                Size = new Size(520, 30),
                AccessibleName = "Database status",
                Font = new Font(Font.FontFamily, 10),
                ForeColor = Color.Gray
            };
            yPos += 45;

            // Build Database Button
            buildDatabaseButton = new Button
            {
                Text = "Build Database",
                Location = new Point(40, yPos),
                Size = new Size(150, 35),
                AccessibleName = "Build database",
                AccessibleDescription = "Build airport database using navdatareader",
                Font = new Font(Font.FontFamily, 9)
            };
            buildDatabaseButton.Click += BuildDatabaseButton_Click;

            // Refresh Button
            refreshButton = new Button
            {
                Text = "Verify Database",
                Location = new Point(200, yPos),
                Size = new Size(130, 35),
                AccessibleName = "Verify current selected database",
                AccessibleDescription = "Verify the current selected and active database",
                Font = new Font(Font.FontFamily, 9)
            };
            refreshButton.Click += RefreshButton_Click;
            yPos += 55;

            // OK and Cancel Buttons
            okButton = new Button
            {
                Text = "OK",
                Location = new Point(400, yPos),
                Size = new Size(75, 30),
                DialogResult = DialogResult.OK,
                AccessibleName = "Save settings",
                AccessibleDescription = "Save database configuration"
            };
            okButton.Click += OkButton_Click;

            cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(480, yPos),
                Size = new Size(75, 30),
                DialogResult = DialogResult.Cancel,
                AccessibleName = "Cancel",
                AccessibleDescription = "Cancel without saving"
            };

            // Add all controls to form
            Controls.AddRange(new Control[]
            {
                titleLabel, fs2020Radio, fs2024Radio, infoLabel,
                statusLabel, buildDatabaseButton, refreshButton,
                okButton, cancelButton
            });

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void SetupAccessibility()
        {
            // Set tab order
            int tabIndex = 0;
            titleLabel.TabIndex = tabIndex++;
            fs2020Radio.TabIndex = tabIndex++;
            fs2024Radio.TabIndex = tabIndex++;
            statusLabel.TabIndex = tabIndex++;
            buildDatabaseButton.TabIndex = tabIndex++;
            refreshButton.TabIndex = tabIndex++;
            okButton.TabIndex = tabIndex++;
            cancelButton.TabIndex = tabIndex++;

            // Focus on load
            Load += (sender, e) =>
            {
                BringToFront();
                Activate();
                TopMost = true;
                TopMost = false;
                fs2020Radio.Focus();
            };
        }

        private void LoadCurrentSettings()
        {
            var settings = SettingsManager.Current;

            // Set simulator version radio button
            string simVersion = settings.SimulatorVersion ?? "FS2020";
            if (simVersion.Equals("FS2024", StringComparison.OrdinalIgnoreCase))
                fs2024Radio.Checked = true;
            else
                fs2020Radio.Checked = true;
        }

        private void UpdateDatabaseStatus()
        {
            var settings = SettingsManager.Current;
            string simulatorVersion = settings.SimulatorVersion ?? "FS2020";
            string navdataPath = NavdataReaderBuilder.GetDefaultDatabasePath(simulatorVersion);

            if (!File.Exists(navdataPath))
            {
                statusLabel.Text = $"{simulatorVersion} Status: Database not built";
                statusLabel.ForeColor = Color.DarkOrange;
                buildDatabaseButton.Enabled = true;
                return;
            }

            try
            {
                var provider = new LittleNavMapProvider(navdataPath, simulatorVersion);
                int airportCount = provider.GetAirportCount();
                statusLabel.Text = $"{simulatorVersion} Status: Ready - {airportCount:N0} airports available";
                statusLabel.ForeColor = Color.DarkGreen;
                buildDatabaseButton.Enabled = true;
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"{simulatorVersion} Status: Error - {ex.Message}";
                statusLabel.ForeColor = Color.Red;
                buildDatabaseButton.Enabled = true;
            }
        }

        private void SimulatorVersionRadio_CheckedChanged(object sender, EventArgs e)
        {
            // Update status when simulator version changes
            if (((RadioButton)sender).Checked)
            {
                UpdateDatabaseStatus();
            }
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            UpdateDatabaseStatus();
            // Announce the status directly without extra prefix
            announcer?.AnnounceImmediate(statusLabel.Text);
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            // Save settings
            var settings = SettingsManager.Current;

            if (fs2024Radio.Checked)
                settings.SimulatorVersion = "FS2024";
            else
                settings.SimulatorVersion = "FS2020";

            SettingsManager.Save();

            DialogResult = DialogResult.OK;
            Close();
        }

        private void BuildDatabaseButton_Click(object sender, EventArgs e)
        {
            string simulatorVersion = fs2024Radio.Checked ? "FS2024" : "FS2020";
            BuildDatabaseForSimulator(simulatorVersion);
        }

        private void BuildDatabaseForSimulator(string simulatorVersion)
        {
            // Build simulator-specific warning message
            string requirementMessage;
            if (simulatorVersion == "FS2024")
            {
                requirementMessage =
                    "IMPORTANT: Flight Simulator 2024 MUST be running and loaded to the main menu.\n" +
                    "Navdatareader uses SimConnect to retrieve scenery data from the running simulator.\n\n";
            }
            else // FS2020
            {
                requirementMessage =
                    "IMPORTANT: Flight Simulator 2020 should be CLOSED.\n" +
                    "Navdatareader reads scenery files directly from disk.\n\n";
            }

            // Confirm with user
            var result = MessageBox.Show(
                requirementMessage +
                $"This will build the {simulatorVersion} airport database using navdatareader.\n\n" +
                "This process may take 2-5 minutes depending on your system.\n\n" +
                "Do you want to continue?",
                $"Build {simulatorVersion} Database",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            // Show progress dialog
            using (var progressForm = new DatabaseBuildProgressForm(simulatorVersion, announcer))
            {
                var dialogResult = progressForm.ShowDialog(this);

                if (dialogResult == DialogResult.OK)
                {
                    // Build succeeded
                    announcer?.AnnounceImmediate($"{simulatorVersion} database built successfully");
                    MessageBox.Show(
                        $"{simulatorVersion} database has been built successfully!",
                        "Build Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    // Build failed or was cancelled
                    MessageBox.Show(
                        $"Database build was cancelled or failed.\n\nCheck the error message for details.",
                        "Build Not Completed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                // Refresh status
                UpdateDatabaseStatus();
            }
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }
    }
}
