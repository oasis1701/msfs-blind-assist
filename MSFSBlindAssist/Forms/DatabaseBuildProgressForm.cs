using System.Threading;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;

namespace MSFSBlindAssist.Forms;
/// <summary>
/// Progress dialog for building airport databases using navdatareader.
/// Displays real-time progress with accessible status updates.
/// </summary>
public partial class DatabaseBuildProgressForm : Form
{
    private ProgressBar progressBar = null!;
    private TextBox statusTextBox = null!;
    private TextBox detailsTextBox = null!;
    private Button cancelButton = null!;
    private Button closeButton = null!;

    private readonly ScreenReaderAnnouncer announcer;
    private readonly NavdataReaderBuilder builder;
    private readonly string simulatorVersion;
    private readonly string outputPath;
    private CancellationTokenSource cancellationTokenSource;

    private bool buildCompleted;
    private bool buildSucceeded;

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

        // Status TextBox (read-only, single line, accessible)
        statusTextBox = new TextBox
        {
            Text = "Initializing...",
            Location = new Point(20, yPos),
            Size = new Size(450, 25),
            AccessibleName = "Build status",
            Font = new Font(Font, FontStyle.Bold),
            ReadOnly = true,
            TabStop = true,
            BorderStyle = BorderStyle.Fixed3D,
            BackColor = SystemColors.Control
        };
        yPos += 35;

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

        // Details TextBox (read-only, multiline, accessible) - show simulator-specific requirements
        string detailsText = "This may take 2-5 minutes depending on your system and scenery complexity.\n\n";
        if (simulatorVersion == "FS2024")
        {
            detailsText += "Requirement: FS2024 must be running (uses SimConnect API)";
        }
        else
        {
            detailsText += "Requirement: FS2020 should be closed (reads scenery files)";
        }

        detailsTextBox = new TextBox
        {
            Text = detailsText,
            Location = new Point(20, yPos),
            Size = new Size(450, 70),
            AccessibleName = "Build details",
            ReadOnly = true,
            TabStop = true,
            Multiline = true,
            BorderStyle = BorderStyle.Fixed3D,
            BackColor = SystemColors.Control,
            ScrollBars = ScrollBars.Vertical
        };
        yPos += 80;

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
            statusTextBox, progressBar, detailsTextBox, cancelButton, closeButton
        });
    }

    private void SetupAccessibility()
    {
        // Set tab order
        statusTextBox.TabIndex = 0;
        progressBar.TabIndex = 1;
        detailsTextBox.TabIndex = 2;
        cancelButton.TabIndex = 3;
        closeButton.TabIndex = 4;

        // Start build when form loads
        Load += async (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false;

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
        }
    }

    private void Builder_ProgressUpdated(object? sender, BuildProgressEventArgs e)
    {
        // Update UI on main thread
        if (InvokeRequired)
        {
            Invoke(new Action(() => Builder_ProgressUpdated(sender, e)));
            return;
        }

        UpdateStatus(e.PercentComplete, e.StatusMessage, e.DetailMessage);
    }

    private void Builder_BuildCompleted(object? sender, BuildCompletedEventArgs e)
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
        }
        else
        {
            UpdateStatus(0, "Build failed", e.Message);
        }

        // Enable close button, hide cancel button
        cancelButton.Visible = false;
        closeButton.Visible = true;
        closeButton.Focus();
        ControlBox = true; // Allow closing now
    }

    private void UpdateStatus(int percentage, string? status, string? details)
    {
        // Update progress bar (percentage -1 means don't update)
        if (percentage >= 0)
        {
            progressBar.Value = Math.Min(Math.Max(percentage, 0), 100);
        }

        // Update status text (if provided)
        if (status != null)
        {
            statusTextBox.Text = status;
        }

        // Update details text (if provided)
        if (details != null)
        {
            detailsTextBox.Text = details;
            // Auto-scroll to bottom for multiline updates
            detailsTextBox.SelectionStart = detailsTextBox.Text.Length;
            detailsTextBox.ScrollToCaret();
        }
    }

    private void CancelButton_Click(object? sender, EventArgs e)
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

            cancelButton.Visible = false;
            closeButton.Visible = true;
            closeButton.Focus();
            ControlBox = true;

            DialogResult = DialogResult.Cancel;
        }
    }

    private void CloseButton_Click(object? sender, EventArgs e)
    {
        DialogResult = buildSucceeded ? DialogResult.OK : DialogResult.Cancel;
        Close();
    }

    private void DatabaseBuildProgressForm_FormClosing(object? sender, FormClosingEventArgs e)
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
