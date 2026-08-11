using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms;

public class UpdateAvailableForm : Form
    {
        private Label titleLabel = null!;
        private Label currentVersionLabel = null!;
        private Label latestVersionLabel = null!;
        private WebView2 releaseNotesView = null!;
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
            // A downgrade is offered when a pilot on a preview build switches back to the
            // release channel: the current release is genuinely older than what they run,
            // so calling it an "update" would be wrong.
            this.Text = updateInfo.IsDowngrade ? "Return to Release Build" : "Update Available";
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
                Text = updateInfo.IsDowngrade
                    ? "Switch back to the current release build"
                    : "A new version of MSFS Blind Assist is available!",
                Location = new Point(20, yPos),
                Size = new Size(540, 30),
                Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
                AccessibleName = updateInfo.IsDowngrade ? "Return to release build" : "Update available"
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

            // Release notes: a WebView2 rendering the Markdown body properly, so a screen
            // reader gets a real document — heading navigation, list semantics, working
            // links — instead of raw Markdown symbols on one run-on line. Same idiom as
            // the EFB forms (FbwEfbForm): render in WebView2, fall back to a native
            // control if the runtime is missing or init fails.
            releaseNotesView = new WebView2
            {
                Location = new Point(20, yPos),
                Size = new Size(540, 180),
                AccessibleName = "Release notes",
                AccessibleDescription = "Release notes for the offered version"
            };
            // Keys pressed inside the WebView2 document are consumed by the browser
            // process and never reach the form's ProcessDialogKey — the wrapper only
            // re-raises accelerator keys (Esc, Alt+letter) as this control's KeyDown.
            // Without this handler, Escape and the button mnemonics are dead exactly
            // where a screen-reader user spends the whole dialog, since the notes view
            // is the first selectable control and therefore the default focus.
            releaseNotesView.KeyDown += OnNotesKeyDown;
            this.Controls.Add(releaseNotesView);

            // The fallback, hidden unless WebView2 init fails. Same bounds.
            releaseNotesTextBox = new TextBox
            {
                Location = new Point(20, yPos),
                Size = new Size(540, 180),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Visible = false,
                AccessibleName = "Release notes",
                AccessibleDescription = "Release notes for the offered version"
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
            // The update button is wider than the default because "&Install Release" is longer
            // than "&Update Now", so its left edge is moved to keep the gap before cancel.
            updateButton = new Button
            {
                Text = updateInfo.IsDowngrade ? "&Install Release" : "&Update Now",
                Location = new Point(340, yPos),
                Size = new Size(120, 30),
                AccessibleName = updateInfo.IsDowngrade ? "Install release build" : "Update now",
                AccessibleDescription = updateInfo.IsDowngrade
                    ? "Download and install the current release build, replacing the preview build you are running"
                    : "Download and install the update"
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
            currentVersionLabel.Text = $"Current version: {updateInfo.CurrentVersion}";

            // "Latest" is wrong for a downgrade — the offered build is older on purpose,
            // and a pilot must not be left thinking the version number went backwards by
            // accident.
            latestVersionLabel.Text = updateInfo.IsDowngrade
                ? $"Release version: {updateInfo.LatestVersion} ({updateInfo.TagName}) — older than the preview build you are running"
                : $"Latest version: {updateInfo.LatestVersion} ({updateInfo.TagName})";

            // The notes themselves are loaded by InitNotesViewAsync (or its fallback),
            // which OnLoad kicks off — EnsureCoreWebView2Async cannot run in the
            // constructor.
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Fire-and-forget is safe: the method catches everything and degrades to the
            // plain-text fallback.
            _ = InitNotesViewAsync();
        }

        private async Task InitNotesViewAsync()
        {
            try
            {
                await releaseNotesView.EnsureCoreWebView2Async();
                var s = releaseNotesView.CoreWebView2.Settings;
                s.AreDefaultContextMenusEnabled = false;
                s.AreDevToolsEnabled = false;
                s.IsZoomControlEnabled = false;
                s.AreBrowserAcceleratorKeysEnabled = false;
                // The page carries no JS of its own (pinned by ReleaseNotesHtmlTests), and
                // the body is third-party text — belt and braces on top of DisableHtml.
                s.IsScriptEnabled = false;

                releaseNotesView.CoreWebView2.NavigationStarting += OnNotesNavigationStarting;
                releaseNotesView.CoreWebView2.NewWindowRequested += OnNotesNewWindowRequested;
                // The other half of the FbwEfbForm idiom: a browser-process death after a
                // successful init raises no exception into the form, only this event —
                // without it a crash strands the pilot on a focusable, silent, empty pane
                // while the working fallback sits hidden.
                releaseNotesView.CoreWebView2.ProcessFailed += OnNotesProcessFailed;
                releaseNotesView.CoreWebView2.NavigateToString(ReleaseNotesHtml.Build(updateInfo.ReleaseNotes));
            }
            catch (Exception ex)
            {
                // Missing WebView2 runtime, or the dialog closed mid-init. Either way the
                // pilot still gets the notes, just unrendered.
                Log.Debug("Updates", $"WebView2 init failed for release notes; using plain text: {ex.Message}");
                ShowPlainTextNotes();
            }
        }

        /// <summary>
        /// The dialog itself never shows any document but the one NavigateToString
        /// provided. Both allowed prefixes are load-bearing: NavigateToString's own load
        /// arrives at this event as a data: navigation (Source reads about:blank only
        /// afterwards — probe-verified on WebView2 1.0.4022.49), so cancelling data: here
        /// blanks the notes. A data: link CLICKED inside the notes never gets this far:
        /// Chromium blocks top-frame data: navigations outright. Everything else is
        /// cancelled, and http/https links open in the default browser — GitHub's
        /// generated notes are full of PR links.
        /// </summary>
        private void OnNotesNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            e.Cancel = true;
            OpenExternally(e.Uri);
        }

        private void OnNotesNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            OpenExternally(e.Uri);
        }

        /// <summary>Opens http/https links in the default browser; drops anything else.</summary>
        private static void OpenExternally(string uri)
        {
            if (!uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Debug("Updates", $"Could not open release-notes link: {ex.Message}");
            }
        }

        private void OnNotesProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            Log.Debug("Updates", $"WebView2 process failed for release notes ({e.ProcessFailedKind}); using plain text.");
            ShowPlainTextNotes();
        }

        /// <summary>
        /// Restores the dialog keys the browser process swallows. Escape and mnemonics go
        /// through the buttons' own click paths, so disabled-while-downloading semantics
        /// are preserved (PerformClick no-ops on a disabled button, like CancelButton).
        /// </summary>
        private void OnNotesKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                cancelButton.PerformClick();
            }
            else if (e.Alt && e.KeyCode is >= Keys.A and <= Keys.Z)
            {
                // Form.ProcessMnemonic finds the matching &-labelled button itself, so
                // this stays correct when the update button's label (and mnemonic)
                // differs between update and downgrade.
                if (ProcessMnemonic((char)e.KeyCode)) e.Handled = true;
            }
        }

        private void ShowPlainTextNotes()
        {
            if (IsDisposed) return;

            // Order matters: show the fallback BEFORE hiding the view. Hiding the focused
            // WebView2 first makes WinForms move focus to the next visible selectable
            // control — at that instant the Update button, silently parking a blind
            // pilot's focus on the destructive action (probe-verified). With the TextBox
            // already visible, focus lands on it instead.
            releaseNotesTextBox.Visible = true;
            releaseNotesView.Visible = false;

            // Both note producers emit LF-only text (preview.yml's bash block,
            // ChangelogRenderer), and a TextBox only breaks lines on CRLF — normalize, or
            // the whole body renders as one run-on line.
            var text = updateInfo.ReleaseNotes ?? "No release notes available.";
            releaseNotesTextBox.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
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
