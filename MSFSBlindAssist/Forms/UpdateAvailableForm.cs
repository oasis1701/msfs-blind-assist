using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms;

public class UpdateAvailableForm : Form
    {
        private Label titleLabel = null!;
        private Label currentVersionLabel = null!;
        private Label latestVersionLabel = null!;
        private TextBox releaseNotesTextBox = null!;
        private ProgressBar downloadProgressBar = null!;
        private Label statusLabel = null!;
        private Button updateButton = null!;
        private Button cancelButton = null!;

        private UpdateCheckResult updateInfo;
        private UpdateService updateService;
        private bool isDownloading = false;

        public bool ShouldUpdate { get; private set; }
        public string DownloadedZipPath { get; private set; } = null!;

        public UpdateAvailableForm(UpdateCheckResult updateInfo, UpdateService updateService)
        {
            this.updateInfo = updateInfo;
            this.updateService = updateService;
            InitializeComponent();
            PopulateUpdateInfo();

            // Subscribe to update service events
            this.updateService.ProgressChanged += UpdateService_ProgressChanged;
            this.updateService.StatusChanged += UpdateService_StatusChanged;
        }

        private void InitializeComponent()
        {
            this.Text = "Update Available";
            this.Size = new Size(600, 500);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.AcceptButton = null; // Prevent Enter key from triggering update immediately

            int yPos = 20;

            // Title
            titleLabel = new Label
            {
                Text = "A new version of FBWBA is available!",
                Location = new Point(20, yPos),
                Size = new Size(540, 30),
                Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
                AccessibleName = "Update available title"
            };
            this.Controls.Add(titleLabel);
            yPos += 40;

            // Current version
            currentVersionLabel = new Label
            {
                Location = new Point(20, yPos),
                Size = new Size(540, 20),
                AccessibleName = "Current version"
            };
            this.Controls.Add(currentVersionLabel);
            yPos += 25;

            // Latest version
            latestVersionLabel = new Label
            {
                Location = new Point(20, yPos),
                Size = new Size(540, 20),
                AccessibleName = "Latest version"
            };
            this.Controls.Add(latestVersionLabel);
            yPos += 35;

            // Release notes label
            Label notesLabel = new Label
            {
                Text = "Release Notes:",
                Location = new Point(20, yPos),
                Size = new Size(540, 20),
                AccessibleName = "Release notes label"
            };
            this.Controls.Add(notesLabel);
            yPos += 25;

            // Release notes
            releaseNotesTextBox = new TextBox
            {
                Location = new Point(20, yPos),
                Size = new Size(540, 180),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = "Release notes",
                AccessibleDescription = "Release notes for the new version"
            };
            this.Controls.Add(releaseNotesTextBox);
            yPos += 190;

            // Progress bar
            downloadProgressBar = new ProgressBar
            {
                Location = new Point(20, yPos),
                Size = new Size(540, 25),
                Minimum = 0,
                Maximum = 100,
                Visible = false,
                AccessibleName = "Download progress"
            };
            this.Controls.Add(downloadProgressBar);
            yPos += 30;

            // Status label
            statusLabel = new Label
            {
                Location = new Point(20, yPos),
                Size = new Size(540, 20),
                Text = "",
                AccessibleName = "Update status"
            };
            this.Controls.Add(statusLabel);
            yPos += 30;

            // Buttons
            updateButton = new Button
            {
                Text = "&Update Now",
                Location = new Point(360, yPos),
                Size = new Size(100, 30),
                AccessibleName = "Update now",
                AccessibleDescription = "Download and install the update"
            };
            updateButton.Click += UpdateButton_Click;
            this.Controls.Add(updateButton);

            cancelButton = new Button
            {
                Text = "&Cancel",
                Location = new Point(470, yPos),
                Size = new Size(90, 30),
                AccessibleName = "Cancel",
                AccessibleDescription = "Cancel update and close dialog"
            };
            cancelButton.Click += CancelButton_Click;
            this.Controls.Add(cancelButton);

            this.CancelButton = cancelButton;
        }

        private void PopulateUpdateInfo()
        {
            currentVersionLabel.Text = $"Current Version: {updateInfo.CurrentVersion}";
            latestVersionLabel.Text = $"Latest Version: {updateInfo.LatestVersion} ({updateInfo.TagName})";
            releaseNotesTextBox.Text = updateInfo.ReleaseNotes ?? "No release notes available.";
        }

        private async void UpdateButton_Click(object? sender, EventArgs e)
        {
            if (isDownloading)
                return;

            try
            {
                isDownloading = true;
                updateButton.Enabled = false;
                cancelButton.Enabled = false;
                downloadProgressBar.Visible = true;
                downloadProgressBar.Value = 0;

                statusLabel.Text = "Starting download...";

                // Download the update
                if (updateInfo.DownloadUrl != null)
                {
                    DownloadedZipPath = await updateService.DownloadUpdateAsync(updateInfo.DownloadUrl);
                }
                else
                {
                    throw new InvalidOperationException("Download URL is not available");
                }

                statusLabel.Text = "Download complete. Ready to install.";
                ShouldUpdate = true;

                // Close the dialog after a brief delay
                await System.Threading.Tasks.Task.Delay(500);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to download update: {ex.Message}",
                    "Update Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );

                isDownloading = false;
                updateButton.Enabled = true;
                cancelButton.Enabled = true;
                downloadProgressBar.Visible = false;
                statusLabel.Text = "Download failed.";
            }
        }

        private void CancelButton_Click(object? sender, EventArgs e)
        {
            ShouldUpdate = false;
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void UpdateService_ProgressChanged(object? sender, UpdateProgressEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateService_ProgressChanged(sender, e)));
                return;
            }

            downloadProgressBar.Value = e.PercentComplete;
            statusLabel.Text = $"Downloading: {e.PercentComplete}% ({e.BytesDownloaded / 1024 / 1024:F1} MB / {e.TotalBytes / 1024 / 1024:F1} MB)";
        }

        private void UpdateService_StatusChanged(object? sender, string status)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateService_StatusChanged(sender, status)));
                return;
            }

            statusLabel.Text = status;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Unsubscribe from events
                if (updateService != null)
                {
                    updateService.ProgressChanged -= UpdateService_ProgressChanged;
                    updateService.StatusChanged -= UpdateService_StatusChanged;
                }
            }
            base.Dispose(disposing);
        }
}
