using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// "Set up MSFS Blind Assist" (UPD-9): at launch, offers the navigation database and the
/// location data while either is missing. It opens on its read-only description, which the
/// screen reader reads, and lists only what is missing; the location line is the download job's
/// own status, as the GeoNames tab shows it, and follows the job once it runs. Download starts the
/// job and the dialog stays open with focus on that button, whose label follows the job
/// (<see cref="StartupDataCheck.DownloadButton"/>). Build returns Yes, and MainForm opens the Nav
/// Database window. Escape is Not now, which reads Close once the download started.
/// </summary>
internal sealed class StartupDataDialog : Form
{
    private readonly StartupDataNeeds _needs;
    private readonly LocationDataDownloader _downloader;
    private TextBox _descriptionTextBox = null!;
    private Button? _downloadButton;
    private Button _closeButton = null!;

    internal StartupDataDialog(StartupDataNeeds needs, LocationDataDownloader downloader)
    {
        _needs = needs;
        _downloader = downloader;
        InitializeComponent();
        _downloader.StatusChanged += OnDownloadStatusChanged;
        Shown += (_, _) => _descriptionTextBox.Focus();
    }

    private void InitializeComponent()
    {
        Text = "Set up MSFS Blind Assist";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new System.Drawing.Size(560, 300);

        // A read-only TextBox, never a Label: focused first, it is what the screen reader reads.
        _descriptionTextBox = new TextBox
        {
            Location = new System.Drawing.Point(12, 12),
            Size = new System.Drawing.Size(536, 224),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "What is missing",
            Text = Description(),
            TabIndex = 0,
        };
        Controls.Add(_descriptionTextBox);

        var buttonRow = new FlowLayoutPanel
        {
            Location = new System.Drawing.Point(12, 248),
            Size = new System.Drawing.Size(536, 44),
            FlowDirection = FlowDirection.LeftToRight,
            TabIndex = 1,
        };
        // Buttons carry no AccessibleName: the screen reader reads their Text.
        if (_needs.LocationData)
        {
            _downloadButton = new Button { AutoSize = true };
            _downloadButton.Click += OnDownloadClicked;
            buttonRow.Controls.Add(_downloadButton);
        }
        if (_needs.NavigationDatabase)
            buttonRow.Controls.Add(new Button { Text = "&Build navigation database", AutoSize = true, DialogResult = DialogResult.Yes });
        _closeButton = new Button { Text = "&Not now", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttonRow.Controls.Add(_closeButton);
        Controls.Add(buttonRow);
        ApplyDownloadButton();

        // Escape is Not now. No AcceptButton: Enter must never start a 400 MB download (the
        // update dialog's rule, UpdateAvailableForm).
        CancelButton = _closeButton;
    }

    private string Description()
    {
        var status = _downloader.DisplayStatus();
        return StartupDataCheck.Describe(_needs, status.Text, status.IsRunning);
    }

    /// <summary>Focus stays on the pressed button, never dimmed: no focus change is spoken over the
    /// job's confirmation. While the label says the job runs, or that the data is ready, a press
    /// starts nothing; after a failure it retries.</summary>
    private void OnDownloadClicked(object? sender, EventArgs e)
    {
        if (!StartupDataCheck.DownloadButton(_downloader.Status.Phase, _downloader.HasData).PressStarts) return;
        _closeButton.Text = "&Close";
        _downloader.Start();
        ApplyDownloadButton();
    }

    /// <summary>The Download button's label for the job's phase, set only when it changes.</summary>
    private void ApplyDownloadButton()
    {
        if (_downloadButton is null) return;
        string text = StartupDataCheck.DownloadButton(_downloader.Status.Phase, _downloader.HasData).Text;
        if (_downloadButton.Text != text) _downloadButton.Text = text;
    }

    /// <summary>Raised on the download worker's thread; never <c>Invoke</c>, and a closing dialog
    /// may have no handle left (A380C-6).</summary>
    private void OnDownloadStatusChanged(object? sender, DownloadStatus status)
    {
        try
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(ApplyDownloadStatus);
        }
        catch (InvalidOperationException)
        {
            // Handle destroyed between the check and the post: nothing to update.
        }
    }

    /// <summary>The job's LATEST status (never the posted one), rewritten in place so the screen
    /// reader keeps its line in the box, and the Download button to match.</summary>
    private void ApplyDownloadStatus()
    {
        if (IsDisposed) return;
        DisplayText.SetPreserveCaret(_descriptionTextBox, Description());
        ApplyDownloadButton();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _downloader.StatusChanged -= OnDownloadStatusChanged;
        base.Dispose(disposing);
    }
}
