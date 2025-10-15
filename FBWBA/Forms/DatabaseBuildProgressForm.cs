using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FBWBA.Accessibility;
using FBWBA.Database;

namespace FBWBA.Forms
{
    /// <summary>
    /// Progress dialog for building airport databases using navdatareader.
    /// Displays real-time progress with accessible status updates.
    /// </summary>
    public partial class DatabaseBuildProgressForm : Form
    {
        private ProgressBar progressBar;
        private Label statusLabel;
        private Label detailsLabel;
        private Button cancelButton;
        private Button closeButton;

        private readonly ScreenReaderAnnouncer announcer;
        private readonly NavdataReaderBuilder builder;
        private readonly string simulatorVersion;
        private readonly string outputPath;
        private CancellationTokenSource cancellationTokenSource;

        private bool buildCompleted;
        private bool buildSucceeded;
        private string lastAnnouncedStatus;

        public bool BuildSucceeded => buildSucceeded;

        public DatabaseBuildProgressForm(string simulatorVersion, ScreenReaderAnnouncer announcer)
        {
            this.simulatorVersion = simulatorVersion;
            this.announcer = announcer;
            this.builder = new NavdataReaderBuilder();
            this.outputPath = NavdataReaderBuilder.GetDefaultDatabasePath(simulatorVersion);
            this.cancellationTokenSource = new CancellationTokenSource();

            InitializeComponent();
            SetupAccessibility();
            SetupBuilder();
        }

        private void InitializeComponent()
        {
            Text = $"Building {simulatorVersion} Database";
            Size = new Size(500, 250);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ControlBox = false; // Prevent closing during build

            int yPos = 20;

            // Status Label
            statusLabel = new Label
            {
                Text = "Initializing...",
                Location = new Point(20, yPos),
                Size = new Size(450, 30),
                AccessibleName = "Build status",
                Font = new Font(Font, FontStyle.Bold)
            };
            yPos += 40;

            // Progress Bar
            progressBar = new ProgressBar
            {
                Location = new Point(20, yPos),
                Size = new Size(450, 30),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100,
                AccessibleName = "Build progress"
            };
            yPos += 40;

            // Details Label - show simulator-specific requirements
            string detailsText = "This may take 2-5 minutes depending on your system and scenery complexity.\n\n";
            if (simulatorVersion == "FS2024")
            {
                detailsText += "Requirement: FS2024 must be running (uses SimConnect API)";
            }
            else
            {
                detailsText += "Requirement: FS2020 should be closed (reads scenery files)";
            }

            detailsLabel = new Label
            {
                Text = detailsText,
                Location = new Point(20, yPos),
                Size = new Size(450, 60),
                AccessibleName = "Build details"
            };
            yPos += 70;

            // Cancel Button
            cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(300, yPos),
                Size = new Size(80, 30),
                AccessibleName = "Cancel build",
                AccessibleDescription = "Cancel the database build process"
            };
            cancelButton.Click += CancelButton_Click;

            // Close Button (initially hidden)
            closeButton = new Button
            {
                Text = "Close",
                Location = new Point(390, yPos),
                Size = new Size(80, 30),
                AccessibleName = "Close dialog",
                AccessibleDescription = "Close this dialog",
                Visible = false
            };
            closeButton.Click += CloseButton_Click;

            // Add controls
            Controls.AddRange(new Control[]
            {
                statusLabel, progressBar, detailsLabel, cancelButton, closeButton
            });
        }

        private void SetupAccessibility()
        {
            // Set tab order
            statusLabel.TabIndex = 0;
            progressBar.TabIndex = 1;
            detailsLabel.TabIndex = 2;
            cancelButton.TabIndex = 3;
            closeButton.TabIndex = 4;

            // Start build when form loads
            Load += async (sender, e) =>
            {
                BringToFront();
                Activate();
                TopMost = true;
                TopMost = false;

                // Give screen reader time to announce dialog
                await Task.Delay(500);

                announcer?.AnnounceImmediate($"Building {simulatorVersion} database. This may take several minutes.");

                // Start the build process
                await StartBuildAsync();
            };

            // Handle form closing
            FormClosing += DatabaseBuildProgressForm_FormClosing;
        }

        private void SetupBuilder()
        {
            builder.ProgressUpdated += Builder_ProgressUpdated;
            builder.BuildCompleted += Builder_BuildCompleted;
        }

        private async Task StartBuildAsync()
        {
            try
            {
                buildCompleted = false;
                buildSucceeded = false;

                // Start the build
                bool result = await builder.BuildDatabaseAsync(
                    simulatorVersion,
                    outputPath,
                    cancellationTokenSource.Token);

                buildSucceeded = result;
            }
            catch (Exception ex)
            {
                UpdateStatus(0, "Build failed", $"Error: {ex.Message}");
                announcer?.AnnounceImmediate($"Database build failed: {ex.Message}");
            }
        }

        private void Builder_ProgressUpdated(object sender, BuildProgressEventArgs e)
        {
            // Update UI on main thread
            if (InvokeRequired)
            {
                Invoke(new Action(() => Builder_ProgressUpdated(sender, e)));
                return;
            }

            UpdateStatus(e.PercentComplete, e.StatusMessage, null);

            // Announce significant progress milestones
            if (e.PercentComplete >= 25 && e.PercentComplete < 50 && lastAnnouncedStatus != "25")
            {
                announcer?.AnnounceImmediate("Reading scenery files");
                lastAnnouncedStatus = "25";
            }
            else if (e.PercentComplete >= 50 && e.PercentComplete < 75 && lastAnnouncedStatus != "50")
            {
                announcer?.AnnounceImmediate("Processing airport data");
                lastAnnouncedStatus = "50";
            }
            else if (e.PercentComplete >= 75 && e.PercentComplete < 95 && lastAnnouncedStatus != "75")
            {
                announcer?.AnnounceImmediate("Writing database");
                lastAnnouncedStatus = "75";
            }
        }

        private void Builder_BuildCompleted(object sender, BuildCompletedEventArgs e)
        {
            // Update UI on main thread
            if (InvokeRequired)
            {
                Invoke(new Action(() => Builder_BuildCompleted(sender, e)));
                return;
            }

            buildCompleted = true;
            buildSucceeded = e.Success;

            if (e.Success)
            {
                UpdateStatus(100, "Build completed successfully!", e.Message);
                announcer?.AnnounceImmediate($"{simulatorVersion} database built successfully. You can now close this dialog.");
            }
            else
            {
                UpdateStatus(0, "Build failed", e.Message);
                announcer?.AnnounceImmediate($"Database build failed: {e.Message}");
            }

            // Enable close button, hide cancel button
            cancelButton.Visible = false;
            closeButton.Visible = true;
            closeButton.Focus();
            ControlBox = true; // Allow closing now
        }

        private void UpdateStatus(int percentage, string status, string details)
        {
            progressBar.Value = Math.Min(Math.Max(percentage, 0), 100);
            statusLabel.Text = status;

            if (details != null)
            {
                detailsLabel.Text = details;
            }
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to cancel the database build?",
                "Cancel Build",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                cancellationTokenSource.Cancel();
                builder.CancelBuild();

                UpdateStatus(0, "Build cancelled", "The database build was cancelled by the user.");
                announcer?.AnnounceImmediate("Database build cancelled");

                cancelButton.Visible = false;
                closeButton.Visible = true;
                closeButton.Focus();
                ControlBox = true;

                DialogResult = DialogResult.Cancel;
            }
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            DialogResult = buildSucceeded ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }

        private void DatabaseBuildProgressForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Prevent closing while build is in progress
            if (!buildCompleted && !cancellationTokenSource.IsCancellationRequested)
            {
                e.Cancel = true;
                MessageBox.Show(
                    "Please wait for the build to complete or click Cancel to stop it.",
                    "Build in Progress",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // Cleanup
            if (builder != null)
            {
                builder.ProgressUpdated -= Builder_ProgressUpdated;
                builder.BuildCompleted -= Builder_BuildCompleted;
            }

            cancellationTokenSource?.Dispose();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            // Only allow Escape to close if build is complete
            if (keyData == Keys.Escape && buildCompleted)
            {
                DialogResult = buildSucceeded ? DialogResult.OK : DialogResult.Cancel;
                Close();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }
    }
}
