using MSFSBlindAssist.Services.Geography;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>GeoNames section of the unified Settings dialog: the location data download (Output C
/// and Location Info read only that local data) and the ranges and limits the lookups use. The
/// download job belongs to MainForm, so it keeps running after Settings closes; this panel only
/// shows its status (a read-only TextBox, VAT-13) and starts or cancels it.</summary>
public class GeoNamesPanel : UserControl, ISettingsPanel
{
    private readonly LocationDataDownloader? _downloader;
    private TextBox _locationStatusTextBox = null!;
    private Button _downloadButton = null!;
    private Button _cancelDownloadButton = null!;
    private TextBox _nearbyCitiesTextBox = null!;
    private TextBox _majorCitiesTextBox = null!;
    private TextBox _populationThresholdTextBox = null!;
    private TextBox _coastRangeTextBox = null!;
    private RadioButton _milesRadioButton = null!;
    private RadioButton _kilometersRadioButton = null!;
    private Button _resetDefaultsButton = null!;

    public string TabTitle => "GeoNames";

    internal GeoNamesPanel(LocationDataDownloader? downloader = null)
    {
        _downloader = downloader;
        InitializeComponent();
        if (_downloader != null) _downloader.StatusChanged += OnDownloadStatusChanged;
    }

    private void InitializeComponent()
    {
        int yPos = 20;
        int labelHeight = 23;
        int textBoxHeight = 23;
        int spacing = 30;
        AutoScroll = true; // the download section made the tab taller than the dialog

        // Location data section: status first, so tabbing in reads what is installed or running.
        var locationLabel = new Label
        {
            Text = "Location data for Output C and Location Info (downloaded once, then used offline):",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(600, labelHeight),
        };
        Controls.Add(locationLabel);

        yPos += labelHeight + 5;
        _locationStatusTextBox = new TextBox
        {
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(600, 54),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Location data status",
            Text = "Location data status unavailable.",
        };
        Controls.Add(_locationStatusTextBox);

        yPos += 54 + 8;
        _downloadButton = new Button
        {
            Text = "Download location data",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(200, 30),
            AccessibleName = "Download location data",
            Enabled = _downloader != null,
        };
        _downloadButton.Click += (_, _) => _downloader?.Start();
        Controls.Add(_downloadButton);

        _cancelDownloadButton = new Button
        {
            Text = "Cancel download",
            Location = new System.Drawing.Point(230, yPos),
            Size = new System.Drawing.Size(150, 30),
            AccessibleName = "Cancel download",
            Enabled = false,
        };
        _cancelDownloadButton.Click += (_, _) => _downloader?.Cancel();
        Controls.Add(_cancelDownloadButton);

        yPos += 30 + 8;
        var creditTextBox = new TextBox
        {
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(600, 38),
            Multiline = true,
            ReadOnly = true,
            AccessibleName = "Data credits",
            Text = "Place data © GeoNames (geonames.org), licensed CC BY 4.0. "
                + "Shapes (seas, coastline, lakes, regions, states) from Natural Earth, public domain.",
        };
        Controls.Add(creditTextBox);

        yPos += 38 + 18;

        // Distance ranges section
        var distanceLabel = new Label
        {
            Text = "Distance Ranges (adjust based on your preferences):",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(400, labelHeight),
            AccessibleName = "Distance ranges section"
        };
        Controls.Add(distanceLabel);

        yPos += labelHeight + 10;

        // The nearest town or village (UserSettings.NearbyCitiesRange): not cities, any town or village however
        // small, for Output C, Location Info's Where you are and the Volanta traffic list (owner, 2026-11-08).
        var nearbyCitiesLabel = new Label
        {
            Text = "Nearest Town or Village Range:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(200, labelHeight)
        };
        Controls.Add(nearbyCitiesLabel);

        _nearbyCitiesTextBox = new TextBox
        {
            Location = new System.Drawing.Point(230, yPos),
            Size = new System.Drawing.Size(60, textBoxHeight),
            AccessibleName = "Nearest town or village range",
            AccessibleDescription = "How far, in miles or kilometers, Output C and the location readouts look for the nearest town or village"
        };
        Controls.Add(_nearbyCitiesTextBox);

        yPos += spacing;

        // Major cities
        var majorCitiesLabel = new Label
        {
            Text = "Major Cities Range:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(150, labelHeight)
        };
        Controls.Add(majorCitiesLabel);

        _majorCitiesTextBox = new TextBox
        {
            Location = new System.Drawing.Point(180, yPos),
            Size = new System.Drawing.Size(60, textBoxHeight),
            AccessibleName = "Major cities range",
            AccessibleDescription = "How far, in miles or kilometers, the Volanta traffic list looks for the nearest major city"
        };
        Controls.Add(_majorCitiesTextBox);

        yPos += spacing;

        // Major Cities Population Threshold
        var populationThresholdLabel = new Label
        {
            Text = "Minimum Population for Major Cities:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(200, labelHeight)
        };
        Controls.Add(populationThresholdLabel);

        _populationThresholdTextBox = new TextBox
        {
            Location = new System.Drawing.Point(230, yPos),
            Size = new System.Drawing.Size(80, textBoxHeight),
            AccessibleName = "Major cities population threshold",
            AccessibleDescription = "Minimum population for a city to be considered a major city"
        };
        Controls.Add(_populationThresholdTextBox);

        var populationHelpLabel = new Label
        {
            Text = "(e.g., 10000, 25000, 50000)",
            Location = new System.Drawing.Point(320, yPos),
            Size = new System.Drawing.Size(150, labelHeight),
            ForeColor = System.Drawing.Color.Gray,
            AccessibleName = "Population threshold help"
        };
        Controls.Add(populationHelpLabel);

        yPos += spacing;

        // Coast: over land the sea coast is mentioned only within this range; at sea and over a
        // lake the nearest land or shore is always said.
        var coastRangeLabel = new Label
        {
            Text = "Coast Range:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(150, labelHeight)
        };
        Controls.Add(coastRangeLabel);

        _coastRangeTextBox = new TextBox
        {
            Location = new System.Drawing.Point(180, yPos),
            Size = new System.Drawing.Size(60, textBoxHeight),
            AccessibleName = "Coast range",
            AccessibleDescription = "Over land, the sea coast is mentioned when it is within this many miles or kilometers. "
                + "Over water the nearest land is always said."
        };
        Controls.Add(_coastRangeTextBox);

        yPos += spacing + 10;

        // Units selection
        var unitsLabel = new Label
        {
            Text = "Distance Units:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(100, labelHeight)
        };
        Controls.Add(unitsLabel);

        _milesRadioButton = new RadioButton
        {
            Text = "Miles",
            Location = new System.Drawing.Point(130, yPos),
            Size = new System.Drawing.Size(70, labelHeight),
            AccessibleName = "Miles unit"
        };
        Controls.Add(_milesRadioButton);

        _kilometersRadioButton = new RadioButton
        {
            Text = "Kilometers",
            Location = new System.Drawing.Point(210, yPos),
            Size = new System.Drawing.Size(100, labelHeight),
            AccessibleName = "Kilometers unit"
        };
        Controls.Add(_kilometersRadioButton);

        yPos += spacing + 20;

        // Reset Defaults button (kept — resets only this panel's fields; the dialog owns Save/Cancel)
        _resetDefaultsButton = new Button
        {
            Text = "Reset to Defaults",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(120, 30),
            AccessibleName = "Reset to defaults",
            AccessibleDescription = "Reset all distance ranges to default values"
        };
        _resetDefaultsButton.Click += ResetDefaultsButton_Click;
        Controls.Add(_resetDefaultsButton);
    }

    private void ResetDefaultsButton_Click(object? sender, EventArgs e)
    {
        _nearbyCitiesTextBox.Text = "25";
        _majorCitiesTextBox.Text = "185";
        _populationThresholdTextBox.Text = "50000";
        _coastRangeTextBox.Text = "50";

        _milesRadioButton.Checked = true;
    }

    public void LoadFrom(UserSettings settings)
    {
        ApplyDownloadStatus();
        // A range saved before the ceilings existed is shown clamped, as the lookups read it, so OK
        // never meets an error.
        int ceiling = LocationQuerySettings.MaxRange(settings.DistanceUnits == "kilometers");
        _nearbyCitiesTextBox.Text = Math.Clamp(settings.NearbyCitiesRange, 1, ceiling).ToString();
        _majorCitiesTextBox.Text = Math.Clamp(settings.MajorCitiesRange, 1, ceiling).ToString();
        _populationThresholdTextBox.Text = settings.MajorCityPopulationThreshold.ToString();
        _coastRangeTextBox.Text = Math.Clamp(settings.CoastRange, 1, LocationQuerySettings.MaxCoastRange).ToString();

        if (settings.DistanceUnits == "kilometers")
        {
            _kilometersRadioButton.Checked = true;
        }
        else
        {
            _milesRadioButton.Checked = true;
        }
    }

    public bool Validate(out string error, out Control? focus)
    {
        // The ceilings the lookups clamp to (LocationQuerySettings.From): query cost grows with the area searched.
        int ceiling = LocationQuerySettings.MaxRange(_kilometersRadioButton.Checked);
        string units = _kilometersRadioButton.Checked ? "kilometers" : "miles";
        if (!(int.TryParse(_nearbyCitiesTextBox.Text, out int nearbyCities) && nearbyCities > 0 && nearbyCities <= ceiling))
        {
            error = $"Please enter a number from 1 to {ceiling} ({units}) for Nearest Town or Village Range.";
            focus = _nearbyCitiesTextBox;
            return false;
        }

        if (!(int.TryParse(_majorCitiesTextBox.Text, out int majorCities) && majorCities > 0 && majorCities <= ceiling))
        {
            error = $"Please enter a number from 1 to {ceiling} ({units}) for Major Cities Range.";
            focus = _majorCitiesTextBox;
            return false;
        }

        if (!(int.TryParse(_populationThresholdTextBox.Text, out int populationThreshold) && populationThreshold >= 500))
        {
            error = "Please enter a valid number (minimum 500) for Major Cities Population Threshold.";
            focus = _populationThresholdTextBox;
            return false;
        }

        if (!(int.TryParse(_coastRangeTextBox.Text, out int coastRange) && coastRange > 0 && coastRange <= LocationQuerySettings.MaxCoastRange))
        {
            error = $"Please enter a number from 1 to {LocationQuerySettings.MaxCoastRange} ({units}) for Coast Range.";
            focus = _coastRangeTextBox;
            return false;
        }

        error = "";
        focus = null;
        return true;
    }

    public void ApplyTo(UserSettings settings)
    {
        settings.NearbyCitiesRange = int.Parse(_nearbyCitiesTextBox.Text);
        settings.MajorCitiesRange = int.Parse(_majorCitiesTextBox.Text);
        settings.MajorCityPopulationThreshold = int.Parse(_populationThresholdTextBox.Text);
        settings.CoastRange = int.Parse(_coastRangeTextBox.Text);

        settings.DistanceUnits = _kilometersRadioButton.Checked ? "kilometers" : "miles";
    }

    public void OnLeaving()
    {
    }

    /// <summary>Status posts made before this tab was first shown were dropped: an unselected
    /// TabPage's controls have no handle yet. Show the latest whenever the tab appears, as
    /// VatsimPanel does; it only rewrites the box, never speaks.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) ApplyDownloadStatus();
    }

    /// <summary>Raised on the download worker's thread; never <c>Invoke</c> (it could deadlock
    /// against a UI thread waiting on the job), and a closing dialog may have no handle left.</summary>
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

    /// <summary>Shows the job's LATEST status (never the posted one, so a late progress post can
    /// never overwrite the final line), rewriting only what changed so the screen reader keeps
    /// its place in the box.</summary>
    private void ApplyDownloadStatus()
    {
        if (IsDisposed) return;
        if (_downloader == null)
        {
            DisplayText.SetPreserveCaret(_locationStatusTextBox, "Location data status unavailable.");
            return;
        }
        var status = _downloader.DisplayStatus();
        DisplayText.SetPreserveCaret(_locationStatusTextBox, status.Text);

        // WinForms moves focus off a control it disables, to the next tab stop: enable the button
        // that takes over first, so a Download press lands on Cancel, never on the credits box
        // behind it, and hand focus back to Download before Cancel goes when the job ends.
        if (status.IsRunning)
        {
            _cancelDownloadButton.Enabled = true;
            _downloadButton.Enabled = false;
        }
        else
        {
            _downloadButton.Enabled = true;
            if (_cancelDownloadButton.Focused) _downloadButton.Focus();
            _cancelDownloadButton.Enabled = false;
        }
        string label = _downloader.HasData ? "Update location data" : "Download location data";
        if (_downloadButton.Text != label)
        {
            _downloadButton.Text = label;
            _downloadButton.AccessibleName = label;
        }
    }

    protected override void Dispose(bool disposing)
    {
        // Unsubscribe here, never in OnLeaving: that runs on every tab switch.
        if (disposing && _downloader != null) _downloader.StatusChanged -= OnDownloadStatusChanged;
        base.Dispose(disposing);
    }
}
