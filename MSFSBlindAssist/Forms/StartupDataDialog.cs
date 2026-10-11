using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// "Set up MSFS Blind Assist" (UPD-9): at launch, offers the navigation database and the
/// location data while either is missing. It opens on its read-only description, which the
/// screen reader reads, and lists only what is missing. Download starts the job in the
/// background; when the navigation database is missing too, the dialog stays open on that
/// button. Build closes it, and MainForm opens Database Settings. Escape is Not now.
/// </summary>
internal sealed class StartupDataDialog : Form
{
    private readonly StartupDataNeeds _needs;
    private readonly Action _startLocationDownload;
    private TextBox _descriptionTextBox = null!;
    private Button? _downloadButton;
    private Button? _buildButton;

    /// <summary>The pilot pressed Build navigation database: MainForm opens Database Settings.</summary>
    internal bool BuildNavigationDatabaseChosen { get; private set; }

    internal StartupDataDialog(StartupDataNeeds needs, Action startLocationDownload)
    {
        _needs = needs;
        _startLocationDownload = startLocationDownload;
        InitializeComponent();
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
            Text = StartupDataCheck.Describe(_needs),
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
            _downloadButton = new Button
            {
                Text = _needs.LocationDataUpdate ? "&Update location data" : "&Download location data",
                AutoSize = true,
            };
            _downloadButton.Click += OnDownloadClicked;
            buttonRow.Controls.Add(_downloadButton);
        }
        if (_needs.NavigationDatabase)
        {
            _buildButton = new Button { Text = "&Build navigation database", AutoSize = true };
            _buildButton.Click += (_, _) =>
            {
                BuildNavigationDatabaseChosen = true;
                DialogResult = DialogResult.OK;
            };
            buttonRow.Controls.Add(_buildButton);
        }
        var notNowButton = new Button { Text = "&Not now", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttonRow.Controls.Add(notNowButton);
        Controls.Add(buttonRow);

        // Escape is Not now. No AcceptButton: Enter must never start a 400 MB download (the
        // update dialog's rule, UpdateAvailableForm).
        CancelButton = notNowButton;
    }

    private void OnDownloadClicked(object? sender, EventArgs e)
    {
        _startLocationDownload();
        if (_buildButton is null)
        {
            DialogResult = DialogResult.OK;
            return;
        }
        // The navigation database is still to do: stay open on its button. Focus moves BEFORE
        // the pressed button goes, or WinForms would move it to the next tab stop by itself.
        _buildButton.Focus();
        _downloadButton!.Visible = false;
        DisplayText.SetPreserveCaret(_descriptionTextBox, StartupDataCheck.Describe(_needs, locationDownloading: true));
    }
}
