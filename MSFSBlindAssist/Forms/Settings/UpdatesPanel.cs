using System.Windows.Forms;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>
/// Updates section of the unified Settings dialog: which releases the update check offers,
/// and whether it runs automatically at startup.
/// </summary>
public class UpdatesPanel : UserControl, ISettingsPanel
{
    private RadioButton _releaseRadio = null!;
    private RadioButton _previewRadio = null!;
    private CheckBox _autoCheckBox = null!;
    private TextBox _versionTextBox = null!;

    /// <summary>
    /// The channel the panel was opened with. The preview confirmation fires only on a
    /// genuine Release to Preview switch — comparing against the live radio state alone
    /// would raise the dialog on every OK press for anyone already on preview.
    /// </summary>
    private UpdateChannel _loadedChannel = UpdateChannel.Release;

    public string TabTitle => "Updates";

    public UpdatesPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        const int labelHeight = 23;
        var yPos = 20;

        var channelLabel = new Label
        {
            Text = "Which builds should MSFS Blind Assist offer?",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(560, labelHeight),
            Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold),
            AccessibleName = "Update channel section"
        };
        Controls.Add(channelLabel);
        yPos += labelHeight + 8;

        _releaseRadio = new RadioButton
        {
            Text = "&Release builds (recommended)",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(560, labelHeight),
            AccessibleName = "Release builds",
            AccessibleDescription = "Only offer full releases. This is the default."
        };
        Controls.Add(_releaseRadio);
        yPos += labelHeight + 4;

        _previewRadio = new RadioButton
        {
            Text = "&Preview builds",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(560, labelHeight),
            AccessibleName = "Preview builds",
            AccessibleDescription =
                "Also offer the preview build, which is rebuilt every time a change is finished. " +
                "Preview builds have had less flying time than a release."
        };
        Controls.Add(_previewRadio);
        yPos += labelHeight + 16;

        _autoCheckBox = new CheckBox
        {
            Text = "&Check for updates automatically when the app starts",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(560, labelHeight),
            AccessibleName = "Check for updates automatically at startup",
            AccessibleDescription =
                "When on, MSFS Blind Assist checks once each time it starts and tells you if " +
                "something newer is available. It stays silent if there is nothing to report."
        };
        Controls.Add(_autoCheckBox);
        yPos += labelHeight + 20;

        var versionLabel = new Label
        {
            Text = "Version you are running:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(560, labelHeight),
            AccessibleName = "Running version label"
        };
        Controls.Add(versionLabel);
        yPos += labelHeight + 4;

        // A read-only TextBox, never a Label: a Label is not in the tab order, so with a
        // screen reader it has to be hunted for with the review cursor.
        _versionTextBox = new TextBox
        {
            Text = AppVersion.DisplayString,
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(400, labelHeight),
            ReadOnly = true,
            AccessibleName = "Version you are running",
            AccessibleDescription =
                "The version and build of MSFS Blind Assist currently running. Include this when reporting a problem."
        };
        Controls.Add(_versionTextBox);
    }

    public void LoadFrom(UserSettings settings)
    {
        _loadedChannel = settings.UpdateChannel;

        _previewRadio.Checked = settings.UpdateChannel == UpdateChannel.Preview;
        _releaseRadio.Checked = settings.UpdateChannel != UpdateChannel.Preview;
        _autoCheckBox.Checked = settings.CheckForUpdatesOnStartup;
        _versionTextBox.Text = AppVersion.DisplayString;
    }

    public bool Validate(out string error, out Control? focus)
    {
        error = "";
        focus = null;

        // Only a genuine switch TO preview is confirmed. This is the one place the route
        // back is stated, which is why it is a dialog rather than static text beside the
        // radio buttons — there is no in-app rollback.
        if (_previewRadio.Checked && _loadedChannel == UpdateChannel.Release)
        {
            var answer = MessageBox.Show(
                this,
                "Preview builds contain the newest changes as soon as they are finished, instead of " +
                "waiting for the next release. Every change is reviewed and tested before it gets " +
                "here, but preview builds have had far less flying time than a release, so you are " +
                "more likely to run into a bug or a stability problem.\n\n" +
                "To go back later, set this to Release builds and use Check for Updates. You will be " +
                "offered the current release even though its version number is lower than the preview " +
                "you are running.\n\n" +
                "Switch to preview builds?",
                "Preview builds",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes)
            {
                // Declining is a valid final answer, not a value that needs correcting, so
                // revert the radio and let the save proceed. Blocking the whole Settings
                // dialog over a changed mind would strand every other edited setting.
                _releaseRadio.Checked = true;
                _previewRadio.Checked = false;
            }
        }

        // Resync so a second Validate() in the same dialog session cannot re-prompt for a
        // switch the pilot already confirmed (or already declined). Without this the
        // panel is only safe because it happens to be registered LAST in SettingsForm —
        // a property of another file, which the next panel added after it would silently
        // break.
        _loadedChannel = _previewRadio.Checked ? UpdateChannel.Preview : UpdateChannel.Release;

        return true;
    }

    public void ApplyTo(UserSettings settings)
    {
        settings.UpdateChannel = _previewRadio.Checked ? UpdateChannel.Preview : UpdateChannel.Release;
        settings.CheckForUpdatesOnStartup = _autoCheckBox.Checked;
    }

    public void OnLeaving()
    {
    }
}
