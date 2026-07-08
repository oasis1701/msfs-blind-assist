using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>GeoNames section of the unified Settings dialog. Extracted from the retired
/// standalone GeoNames API key dialog — same controls, same AccessibleNames/TabIndex-free
/// layout, but Save/Cancel are gone (the dialog owns OK/Cancel) and the old dialog's per-field
/// validation + radius-limit warning now live in <see cref="Validate"/>.</summary>
public class GeoNamesPanel : UserControl, ISettingsPanel
{
    private TextBox _apiUsernameTextBox = null!;
    private TextBox _nearbyCitiesTextBox = null!;
    private TextBox _regionalCitiesTextBox = null!;
    private TextBox _majorCitiesTextBox = null!;
    private TextBox _populationThresholdTextBox = null!;
    private TextBox _landmarksTextBox = null!;
    private TextBox _maxNearbyPlacesTextBox = null!;
    private TextBox _maxMajorCitiesTextBox = null!;
    private TextBox _maxAirportsTextBox = null!;
    private TextBox _maxTerrainFeaturesTextBox = null!;
    private TextBox _maxWaterBodiesTextBox = null!;
    private TextBox _maxTouristLandmarksTextBox = null!;
    private RadioButton _milesRadioButton = null!;
    private RadioButton _kilometersRadioButton = null!;
    private Button _resetDefaultsButton = null!;

    public string TabTitle => "GeoNames";

    public GeoNamesPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        int yPos = 20;
        int labelHeight = 23;
        int textBoxHeight = 23;
        int spacing = 30;

        // API Username section
        var apiLabel = new Label
        {
            Text = "GeoNames API Username (required for location services):",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(450, labelHeight),
            AccessibleName = "GeoNames API Username label"
        };
        Controls.Add(apiLabel);

        yPos += labelHeight + 5;
        _apiUsernameTextBox = new TextBox
        {
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(300, textBoxHeight),
            AccessibleName = "GeoNames API Username",
            AccessibleDescription = "Enter your GeoNames API username"
        };
        Controls.Add(_apiUsernameTextBox);

        var registerLabel = new Label
        {
            Text = "Register for free at geonames.org",
            Location = new System.Drawing.Point(330, yPos),
            Size = new System.Drawing.Size(150, labelHeight),
            ForeColor = System.Drawing.Color.Gray,
            AccessibleName = "Registration info"
        };
        Controls.Add(registerLabel);

        yPos += spacing + 10;

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

        // Nearby cities
        var nearbyCitiesLabel = new Label
        {
            Text = "Nearby Cities Range:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(150, labelHeight)
        };
        Controls.Add(nearbyCitiesLabel);

        _nearbyCitiesTextBox = new TextBox
        {
            Location = new System.Drawing.Point(180, yPos),
            Size = new System.Drawing.Size(60, textBoxHeight),
            AccessibleName = "Nearby cities range",
            AccessibleDescription = "Distance in miles or kilometers for nearby cities"
        };
        Controls.Add(_nearbyCitiesTextBox);

        yPos += spacing;

        // Cardinal cities
        var regionalCitiesLabel = new Label
        {
            Text = "Cardinal Cities Range:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(150, labelHeight)
        };
        Controls.Add(regionalCitiesLabel);

        _regionalCitiesTextBox = new TextBox
        {
            Location = new System.Drawing.Point(180, yPos),
            Size = new System.Drawing.Size(60, textBoxHeight),
            AccessibleName = "Cardinal cities range",
            AccessibleDescription = "Distance in miles or kilometers to search for cities in each cardinal direction (N/E/S/W)"
        };
        Controls.Add(_regionalCitiesTextBox);

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
            AccessibleDescription = "Distance in miles or kilometers for major cities"
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

        // Landmarks
        var landmarksLabel = new Label
        {
            Text = "Landmarks Range:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(150, labelHeight)
        };
        Controls.Add(landmarksLabel);

        _landmarksTextBox = new TextBox
        {
            Location = new System.Drawing.Point(180, yPos),
            Size = new System.Drawing.Size(60, textBoxHeight),
            AccessibleName = "Landmarks range",
            AccessibleDescription = "Distance in miles or kilometers for landmarks and points of interest"
        };
        Controls.Add(_landmarksTextBox);

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

        yPos += spacing + 10;

        // Maximum Results section
        var maxResultsLabel = new Label
        {
            Text = "Maximum Results Per Category:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(450, labelHeight),
            Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold),
            AccessibleName = "Maximum results section"
        };
        Controls.Add(maxResultsLabel);

        yPos += labelHeight + 10;

        // Create a two-column layout for max results settings
        int leftColumn = 20;
        int rightColumn = 300;
        int labelWidth = 120;
        int textBoxWidth = 60;

        // Left column - Nearby Places, Major Cities, Airports
        var maxNearbyLabel = new Label
        {
            Text = "Nearby Places:",
            Location = new System.Drawing.Point(leftColumn, yPos),
            Size = new System.Drawing.Size(labelWidth, labelHeight)
        };
        Controls.Add(maxNearbyLabel);

        _maxNearbyPlacesTextBox = new TextBox
        {
            Location = new System.Drawing.Point(leftColumn + labelWidth + 5, yPos),
            Size = new System.Drawing.Size(textBoxWidth, textBoxHeight),
            AccessibleName = "Maximum nearby places"
        };
        Controls.Add(_maxNearbyPlacesTextBox);

        var maxMajorCitiesLabel = new Label
        {
            Text = "Major Cities:",
            Location = new System.Drawing.Point(rightColumn, yPos),
            Size = new System.Drawing.Size(labelWidth, labelHeight)
        };
        Controls.Add(maxMajorCitiesLabel);

        _maxMajorCitiesTextBox = new TextBox
        {
            Location = new System.Drawing.Point(rightColumn + labelWidth + 5, yPos),
            Size = new System.Drawing.Size(textBoxWidth, textBoxHeight),
            AccessibleName = "Maximum major cities"
        };
        Controls.Add(_maxMajorCitiesTextBox);

        yPos += spacing;

        var maxAirportsLabel = new Label
        {
            Text = "Airports:",
            Location = new System.Drawing.Point(leftColumn, yPos),
            Size = new System.Drawing.Size(labelWidth, labelHeight)
        };
        Controls.Add(maxAirportsLabel);

        _maxAirportsTextBox = new TextBox
        {
            Location = new System.Drawing.Point(leftColumn + labelWidth + 5, yPos),
            Size = new System.Drawing.Size(textBoxWidth, textBoxHeight),
            AccessibleName = "Maximum airports"
        };
        Controls.Add(_maxAirportsTextBox);

        var maxTerrainLabel = new Label
        {
            Text = "Terrain Features:",
            Location = new System.Drawing.Point(rightColumn, yPos),
            Size = new System.Drawing.Size(labelWidth, labelHeight)
        };
        Controls.Add(maxTerrainLabel);

        _maxTerrainFeaturesTextBox = new TextBox
        {
            Location = new System.Drawing.Point(rightColumn + labelWidth + 5, yPos),
            Size = new System.Drawing.Size(textBoxWidth, textBoxHeight),
            AccessibleName = "Maximum terrain features"
        };
        Controls.Add(_maxTerrainFeaturesTextBox);

        yPos += spacing;

        var maxWaterLabel = new Label
        {
            Text = "Water Bodies:",
            Location = new System.Drawing.Point(leftColumn, yPos),
            Size = new System.Drawing.Size(labelWidth, labelHeight)
        };
        Controls.Add(maxWaterLabel);

        _maxWaterBodiesTextBox = new TextBox
        {
            Location = new System.Drawing.Point(leftColumn + labelWidth + 5, yPos),
            Size = new System.Drawing.Size(textBoxWidth, textBoxHeight),
            AccessibleName = "Maximum water bodies"
        };
        Controls.Add(_maxWaterBodiesTextBox);

        var maxTouristLabel = new Label
        {
            Text = "Tourist Landmarks:",
            Location = new System.Drawing.Point(rightColumn, yPos),
            Size = new System.Drawing.Size(labelWidth, labelHeight)
        };
        Controls.Add(maxTouristLabel);

        _maxTouristLandmarksTextBox = new TextBox
        {
            Location = new System.Drawing.Point(rightColumn + labelWidth + 5, yPos),
            Size = new System.Drawing.Size(textBoxWidth, textBoxHeight),
            AccessibleName = "Maximum tourist landmarks"
        };
        Controls.Add(_maxTouristLandmarksTextBox);

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
        _regionalCitiesTextBox.Text = "50";
        _majorCitiesTextBox.Text = "185";
        _populationThresholdTextBox.Text = "50000";
        _landmarksTextBox.Text = "30";

        // Reset maximum results to defaults
        _maxNearbyPlacesTextBox.Text = "10";
        _maxMajorCitiesTextBox.Text = "10";
        _maxAirportsTextBox.Text = "8";
        _maxTerrainFeaturesTextBox.Text = "8";
        _maxWaterBodiesTextBox.Text = "8";
        _maxTouristLandmarksTextBox.Text = "8";

        _milesRadioButton.Checked = true;
    }

    public void LoadFrom(UserSettings settings)
    {
        _apiUsernameTextBox.Text = settings.GeoNamesApiUsername;
        _nearbyCitiesTextBox.Text = settings.NearbyCitiesRange.ToString();
        _regionalCitiesTextBox.Text = settings.RegionalCitiesRange.ToString();
        _majorCitiesTextBox.Text = settings.MajorCitiesRange.ToString();
        _populationThresholdTextBox.Text = settings.MajorCityPopulationThreshold.ToString();
        _landmarksTextBox.Text = settings.LandmarksRange.ToString();

        _maxNearbyPlacesTextBox.Text = settings.MaxNearbyPlacesToShow.ToString();
        _maxMajorCitiesTextBox.Text = settings.MaxMajorCitiesToShow.ToString();
        _maxAirportsTextBox.Text = settings.MaxAirportsToShow.ToString();
        _maxTerrainFeaturesTextBox.Text = settings.MaxTerrainFeaturesToShow.ToString();
        _maxWaterBodiesTextBox.Text = settings.MaxWaterBodiesToShow.ToString();
        _maxTouristLandmarksTextBox.Text = settings.MaxTouristLandmarksToShow.ToString();

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
        if (!(int.TryParse(_nearbyCitiesTextBox.Text, out int nearbyCities) && nearbyCities > 0))
        {
            error = "Please enter a valid number for Nearby Cities Range.";
            focus = _nearbyCitiesTextBox;
            return false;
        }

        if (!(int.TryParse(_regionalCitiesTextBox.Text, out int regionalCities) && regionalCities > 0))
        {
            error = "Please enter a valid number for Regional Cities Range.";
            focus = _regionalCitiesTextBox;
            return false;
        }

        if (!(int.TryParse(_majorCitiesTextBox.Text, out int majorCities) && majorCities > 0))
        {
            error = "Please enter a valid number for Major Cities Range.";
            focus = _majorCitiesTextBox;
            return false;
        }

        // Check if radius exceeds GeoNames API limit (300 km / ~186 miles)
        var units = _kilometersRadioButton.Checked ? "km" : "miles";
        var radiusKm = units == "km" ? majorCities : (int)Math.Round(majorCities * 1.60934);
        if (radiusKm > 300)
        {
            var maxInUnits = units == "km" ? "300 km" : "186 miles";
            var result = MessageBox.Show(
                $"Warning: Major Cities range of {majorCities} {units} exceeds the GeoNames API free service limit of {maxInUnits}.\n\nThe application will automatically cap the radius at {maxInUnits} for API calls.\n\nDo you want to continue?",
                "Radius Limit Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result == DialogResult.No)
            {
                error = "Major Cities Range was not saved. Adjust the value or confirm the radius limit warning to continue.";
                focus = _majorCitiesTextBox;
                return false;
            }
        }

        if (!(int.TryParse(_populationThresholdTextBox.Text, out int populationThreshold) && populationThreshold >= 500))
        {
            error = "Please enter a valid number (minimum 500) for Major Cities Population Threshold.";
            focus = _populationThresholdTextBox;
            return false;
        }

        if (!(int.TryParse(_landmarksTextBox.Text, out int landmarks) && landmarks > 0))
        {
            error = "Please enter a valid number for Landmarks Range.";
            focus = _landmarksTextBox;
            return false;
        }

        if (!(int.TryParse(_maxNearbyPlacesTextBox.Text, out int maxNearbyPlaces) && maxNearbyPlaces > 0 && maxNearbyPlaces <= 50))
        {
            error = "Please enter a valid number (1-50) for Maximum Nearby Places.";
            focus = _maxNearbyPlacesTextBox;
            return false;
        }

        if (!(int.TryParse(_maxMajorCitiesTextBox.Text, out int maxMajorCities) && maxMajorCities > 0 && maxMajorCities <= 50))
        {
            error = "Please enter a valid number (1-50) for Maximum Major Cities.";
            focus = _maxMajorCitiesTextBox;
            return false;
        }

        if (!(int.TryParse(_maxAirportsTextBox.Text, out int maxAirports) && maxAirports > 0 && maxAirports <= 50))
        {
            error = "Please enter a valid number (1-50) for Maximum Airports.";
            focus = _maxAirportsTextBox;
            return false;
        }

        if (!(int.TryParse(_maxTerrainFeaturesTextBox.Text, out int maxTerrain) && maxTerrain > 0 && maxTerrain <= 50))
        {
            error = "Please enter a valid number (1-50) for Maximum Terrain Features.";
            focus = _maxTerrainFeaturesTextBox;
            return false;
        }

        if (!(int.TryParse(_maxWaterBodiesTextBox.Text, out int maxWater) && maxWater > 0 && maxWater <= 50))
        {
            error = "Please enter a valid number (1-50) for Maximum Water Bodies.";
            focus = _maxWaterBodiesTextBox;
            return false;
        }

        if (!(int.TryParse(_maxTouristLandmarksTextBox.Text, out int maxTourist) && maxTourist > 0 && maxTourist <= 50))
        {
            error = "Please enter a valid number (1-50) for Maximum Tourist Landmarks.";
            focus = _maxTouristLandmarksTextBox;
            return false;
        }

        error = "";
        focus = null;
        return true;
    }

    public void ApplyTo(UserSettings settings)
    {
        settings.GeoNamesApiUsername = _apiUsernameTextBox.Text.Trim();

        settings.NearbyCitiesRange = int.Parse(_nearbyCitiesTextBox.Text);
        settings.RegionalCitiesRange = int.Parse(_regionalCitiesTextBox.Text);
        settings.MajorCitiesRange = int.Parse(_majorCitiesTextBox.Text);

        int populationThreshold = int.Parse(_populationThresholdTextBox.Text);
        settings.MajorCityPopulationThreshold = populationThreshold;

        // Determine the appropriate API threshold based on user's population threshold.
        // Use MORE restrictive API filters for HIGHER population requirements — this prevents
        // flooding results with small cities when searching for large ones.
        settings.MajorCityAPIThreshold = populationThreshold switch
        {
            >= 50000 => "cities15000",
            >= 15000 => "cities15000",
            >= 5000 => "cities5000",
            _ => "cities1000"
        };

        settings.LandmarksRange = int.Parse(_landmarksTextBox.Text);

        settings.MaxNearbyPlacesToShow = int.Parse(_maxNearbyPlacesTextBox.Text);
        settings.MaxMajorCitiesToShow = int.Parse(_maxMajorCitiesTextBox.Text);
        settings.MaxAirportsToShow = int.Parse(_maxAirportsTextBox.Text);
        settings.MaxTerrainFeaturesToShow = int.Parse(_maxTerrainFeaturesTextBox.Text);
        settings.MaxWaterBodiesToShow = int.Parse(_maxWaterBodiesTextBox.Text);
        settings.MaxTouristLandmarksToShow = int.Parse(_maxTouristLandmarksTextBox.Text);

        settings.DistanceUnits = _kilometersRadioButton.Checked ? "kilometers" : "miles";
    }

    public void OnLeaving()
    {
    }
}
