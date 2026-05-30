using System.Windows.Forms;

namespace MSFSBlindAssist.Forms.HS787;

/// <summary>
/// Shown when the HS787 FMC bridge installer cannot find the MSFS Community folder
/// automatically. Lets the user browse to it and specify the simulator version.
/// Persisted values are stored by the caller via UserSettings.
/// </summary>
public sealed class HS787CommunityFolderForm : Form
{
    private readonly TextBox _pathTextBox;
    private readonly ComboBox _simVersionCombo;
    private readonly Button _okButton;

    /// <summary>The Community folder path the user confirmed. Valid only when DialogResult is OK.</summary>
    public string SelectedPath { get; private set; } = "";

    /// <summary>"FS2024" or "FS2020". Valid only when DialogResult is OK.</summary>
    public string SelectedSimVersion { get; private set; } = "FS2024";

    /// <param name="existingPath">Pre-populate the path box (e.g. when re-opening to correct an error).</param>
    /// <param name="existingSimVersion">"FS2024" or "FS2020" to pre-select the combo.</param>
    public HS787CommunityFolderForm(string? existingPath = null, string? existingSimVersion = null)
    {
        Text = "MSFS Community Folder";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var descLabel = new Label
        {
            Text = "MSFS Blind Assist could not find your MSFS Community folder automatically.\r\n" +
                   "Please browse to it below.",
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            Margin = new Padding(0, 0, 0, 12),
        };

        // Label TabIndex must be immediately before the first path control so its
        // mnemonic (Alt+C) moves focus to _pathTextBox.
        var pathLabel = new Label
        {
            Text = "&Community folder path:",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2),
            TabIndex = 0,
        };

        var pathPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 12),
        };

        _pathTextBox = new TextBox
        {
            ReadOnly = true,
            Width = 400,
            Text = existingPath ?? "",
            AccessibleName = "Community folder path",
            TabIndex = 1,
        };

        var browseButton = new Button
        {
            Text = "&Browse...",
            AutoSize = true,
            TabIndex = 2,
        };
        browseButton.Click += OnBrowseClick;

        pathPanel.Controls.Add(_pathTextBox);
        pathPanel.Controls.Add(browseButton);

        // Label TabIndex 3 so its mnemonic (Alt+S) moves focus to _simVersionCombo (TabIndex 4).
        var simLabel = new Label
        {
            Text = "&Simulator version:",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2),
            TabIndex = 3,
        };

        _simVersionCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 300,
            Margin = new Padding(0, 0, 0, 16),
            AccessibleName = "Simulator version",
            TabIndex = 4,
        };
        _simVersionCombo.Items.Add("Microsoft Flight Simulator 2024");
        _simVersionCombo.Items.Add("Microsoft Flight Simulator 2020");
        _simVersionCombo.SelectedIndex = existingSimVersion == "FS2020" ? 1 : 0;

        _okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Enabled = !string.IsNullOrWhiteSpace(existingPath),
            TabIndex = 5,
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            TabIndex = 6,
        };

        AcceptButton = _okButton;
        CancelButton = cancelButton;

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(_okButton);

        layout.Controls.Add(descLabel);
        layout.Controls.Add(pathLabel);
        layout.Controls.Add(pathPanel);
        layout.Controls.Add(simLabel);
        layout.Controls.Add(_simVersionCombo);
        layout.Controls.Add(buttonPanel);

        Controls.Add(layout);
    }

    private void OnBrowseClick(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select your MSFS Community folder",
            UseDescriptionForTitle = true,
        };

        if (!string.IsNullOrEmpty(_pathTextBox.Text) && Directory.Exists(_pathTextBox.Text))
            dialog.SelectedPath = _pathTextBox.Text;

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathTextBox.Text = dialog.SelectedPath;
            _okButton.Enabled = true;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            SelectedPath = _pathTextBox.Text;
            SelectedSimVersion = _simVersionCombo.SelectedIndex == 0 ? "FS2024" : "FS2020";
        }
        base.OnFormClosing(e);
    }
}
