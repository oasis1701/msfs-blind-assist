using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FBWBAUpdater
{
    public class UpdaterForm : Form
    {
        private TextBox logTextBox;
        private ProgressBar progressBar;
        private Button restartButton;
        private Button closeButton;

        private string zipPath;
        private string appDirectory;
        private string appExecutablePath;
        private bool updateSuccessful = false;

        public UpdaterForm(string zipPath, string appDirectory, string appExecutablePath)
        {
            this.zipPath = zipPath;
            this.appDirectory = appDirectory;
            this.appExecutablePath = appExecutablePath;

            InitializeComponent();

            // Start the update process automatically when form loads
            this.Load += UpdaterForm_Load;
        }

        private void InitializeComponent()
        {
            this.Text = "FBWBA Updater";
            this.Size = new Size(700, 500);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            int yPos = 20;

            // Title label
            Label titleLabel = new Label
            {
                Text = "Updating FBWBA",
                Location = new Point(20, yPos),
                Size = new Size(640, 30),
                Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
                AccessibleName = "Updater title"
            };
            this.Controls.Add(titleLabel);
            yPos += 40;

            // Progress bar
            progressBar = new ProgressBar
            {
                Location = new Point(20, yPos),
                Size = new Size(640, 25),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                AccessibleName = "Update progress",
                AccessibleDescription = "Progress indicator for the update process"
            };
            this.Controls.Add(progressBar);
            yPos += 35;

            // Log text box
            logTextBox = new TextBox
            {
                Location = new Point(20, yPos),
                Size = new Size(640, 300),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9),
                AccessibleName = "Update log",
                AccessibleDescription = "Detailed log of the update process. Use arrow keys or screen reader review commands to read."
            };
            this.Controls.Add(logTextBox);
            yPos += 310;

            // Restart button
            restartButton = new Button
            {
                Text = "&Restart FBWBA",
                Location = new Point(390, yPos),
                Size = new Size(130, 35),
                Enabled = false,
                AccessibleName = "Restart FBWBA",
                AccessibleDescription = "Restart FBWBA after successful update"
            };
            restartButton.Click += RestartButton_Click;
            this.Controls.Add(restartButton);

            // Close button
            closeButton = new Button
            {
                Text = "&Close",
                Location = new Point(530, yPos),
                Size = new Size(130, 35),
                AccessibleName = "Close",
                AccessibleDescription = "Close the updater window"
            };
            closeButton.Click += CloseButton_Click;
            this.Controls.Add(closeButton);

            this.CancelButton = closeButton;
        }

        private async void UpdaterForm_Load(object sender, EventArgs e)
        {
            // Give the form a moment to render before starting
            await Task.Delay(100);

            // Run the update process
            await RunUpdateAsync();
        }

        private async Task RunUpdateAsync()
        {
            try
            {
                LogMessage("FBWBA Updater");
                LogMessage("=============");
                LogMessage("");
                LogMessage($"ZIP Path: {zipPath}");
                LogMessage($"App Directory: {appDirectory}");
                LogMessage($"App Executable: {appExecutablePath}");
                LogMessage("");

                // Step 1: Verify ZIP file exists
                if (!File.Exists(zipPath))
                {
                    throw new FileNotFoundException("Update ZIP file not found", zipPath);
                }

                LogMessage("Update file verified.");

                // Step 2: Wait for main application to close
                LogMessage("Waiting for FBWBA to close...");
                await Task.Run(() => WaitForProcessToExit("FBWBA"));
                await Task.Delay(1000); // Additional delay to ensure file handles are released

                // Step 3: Create backup directory
                string backupDir = Path.Combine(Path.GetTempPath(), "FBWBA_Backup_" + DateTime.Now.Ticks);
                Directory.CreateDirectory(backupDir);
                LogMessage($"Backup directory created: {backupDir}");

                // Step 4: Backup current files (for rollback if needed)
                LogMessage("Creating backup...");
                BackupFiles(appDirectory, backupDir);

                // Step 5: Extract update files
                LogMessage("Extracting update...");
                await Task.Run(() => ExtractUpdate(zipPath, appDirectory));

                // Step 6: Clean up ZIP file
                LogMessage("Cleaning up temporary files...");
                File.Delete(zipPath);

                LogMessage("");
                LogMessage("Update completed successfully!");
                LogMessage("You can now restart FBWBA or close this window.");

                updateSuccessful = true;

                // Enable restart button and stop progress bar
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 100;
                restartButton.Enabled = true;
                restartButton.Focus(); // Focus on restart button for easy access
            }
            catch (Exception ex)
            {
                LogMessage("");
                LogMessage("ERROR: Update failed!");
                LogMessage($"Details: {ex.Message}");
                LogMessage("");
                LogMessage("The update could not be completed. Please try again or contact support.");

                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 0;
                closeButton.Focus();
            }
        }

        private void LogMessage(string message)
        {
            if (logTextBox.InvokeRequired)
            {
                logTextBox.Invoke(new Action(() => LogMessage(message)));
                return;
            }

            logTextBox.AppendText(message + Environment.NewLine);
        }

        private void WaitForProcessToExit(string processName)
        {
            System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName(processName);

            foreach (System.Diagnostics.Process process in processes)
            {
                try
                {
                    process.WaitForExit(10000); // Wait up to 10 seconds
                }
                catch
                {
                    // Process may have already exited
                }
            }
        }

        private void BackupFiles(string sourceDir, string backupDir)
        {
            try
            {
                // Backup only the executable and critical DLLs (not the entire directory)
                string[] filesToBackup = new[]
                {
                    "FBWBA.exe",
                    "FBWBA.exe.config",
                    "FBWBAUpdater.exe"
                };

                foreach (string fileName in filesToBackup)
                {
                    string sourcePath = Path.Combine(sourceDir, fileName);
                    if (File.Exists(sourcePath))
                    {
                        string destPath = Path.Combine(backupDir, fileName);
                        File.Copy(sourcePath, destPath, true);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Warning: Backup failed: {ex.Message}");
                // Continue anyway - backup is optional
            }
        }

        private void ExtractUpdate(string zipPath, string destinationDir)
        {
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    try
                    {
                        // Skip directory entries
                        if (string.IsNullOrEmpty(entry.Name))
                            continue;

                        // Build the full path
                        string destinationPath = Path.Combine(destinationDir, entry.FullName);

                        // Create directory if needed
                        string directoryPath = Path.GetDirectoryName(destinationPath);
                        if (!Directory.Exists(directoryPath))
                        {
                            Directory.CreateDirectory(directoryPath);
                        }

                        // Skip if it's the updater itself (don't overwrite while running)
                        if (entry.Name.Equals("FBWBAUpdater.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            LogMessage($"Skipping: {entry.FullName} (updater executable)");
                            continue;
                        }

                        // Extract file (overwrite existing)
                        LogMessage($"Extracting: {entry.FullName}");
                        entry.ExtractToFile(destinationPath, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Warning: Failed to extract {entry.FullName}: {ex.Message}");
                        // Continue with other files
                    }
                }
            }
        }

        private void RestartButton_Click(object sender, EventArgs e)
        {
            if (updateSuccessful)
            {
                try
                {
                    System.Diagnostics.Process.Start(appExecutablePath);
                    Application.Exit();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to restart FBWBA: {ex.Message}\n\nPlease start FBWBA manually.",
                        "Restart Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}
