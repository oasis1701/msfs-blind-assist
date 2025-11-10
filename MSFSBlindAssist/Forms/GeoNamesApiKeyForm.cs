using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;
public partial class GeoNamesApiKeyForm : Form
{
    private TextBox apiUsernameTextBox = null!;
    private TextBox nearbyCitiesTextBox = null!;
    private TextBox regionalCitiesTextBox = null!;
    private TextBox majorCitiesTextBox = null!;
    private TextBox landmarksTextBox = null!;
    private TextBox populationThresholdTextBox = null!;
    private TextBox maxNearbyPlacesTextBox = null!;
    private TextBox maxMajorCitiesTextBox = null!;
    private TextBox maxAirportsTextBox = null!;
    private TextBox maxTerrainFeaturesTextBox = null!;
    private TextBox maxWaterBodiesTextBox = null!;
    private TextBox maxTouristLandmarksTextBox = null!;
    private RadioButton milesRadioButton = null!;
    private RadioButton kilometersRadioButton = null!;
    private ComboBox nearestCityAnnouncementComboBox = null!;
    private Button saveButton = null!;
    private Button cancelButton = null!;
    private Button resetDefaultsButton = null!;

    public GeoNamesApiKeyForm()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void InitializeComponent()
    {
        this.SuspendLayout();

        // Form properties
        this.Text = "GeoNames Settings";
        this.Size = new System.Drawing.Size(600, 650);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ShowInTaskbar = false;

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
        this.Controls.Add(apiLabel);

        yPos += labelHeight + 5;
        apiUsernameTextBox = new TextBox
        {
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(300, textBoxHeight),
            AccessibleName = "GeoNames API Username",
            AccessibleDescription = "Enter your GeoNames API username"
        };
        this.Controls.Add(apiUsernameTextBox);

        var registerLabel = new Label
        {
            Text = "Register for free at geonames.org",
            Location = new System.Drawing.Point(330, yPos),
            Size = new System.Drawing.Size(150, labelHeight),
            ForeColor = System.Drawing.Color.Gray,
            AccessibleName = "Registration info"
        };
        this.Controls.Add(registerLabel);

        yPos += spacing + 10;

        // Distance ranges section
        var distanceLabel = new Label
        {
            Text = "Distance Ranges (adjust based on your preferences):",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(400, labelHeight),
            AccessibleName = "Distance ranges section"
        };
        this.Controls.Add(distanceLabel);

        yPos += labelHeight + 10;

        // Nearby cities
        var nearbyCitiesLabel = new Label
        {
            Text = "Nearby Cities Range:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(150, labelHeight)
        };
        this.Controls.Add(nearbyCitiesLabel);

        nearbyCitiesTextBox = new TextBox
        {
            Location = new System.Drawing.Point(180, yPos),
            Size = new System.Drawing.Size(60, textBoxHeight),
            AccessibleName = "Nearby cities range",
            AccessibleDescription = "Distance in miles or kilometers for nearby cities"
        };
        this.Controls.Add(nearbyCitiesTextBox);

        yPos += spacing;

        // Cardinal cities
        var regionalCitiesLabel = new Label
        {
            Text = "Cardinal Cities Range:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(150, labelHeight)
        };
        this.Controls.Add(regionalCitiesLabel);

        regionalCitiesTextBox = new TextBox
        {
            Location = new System.Drawing.Point(180, yPos),
            Size = new System.Drawing.Size(60, textBoxHeight),
            AccessibleName = "Cardinal cities range",
            AccessibleDescription = "Distance in miles or kilometers to search for cities in each cardinal direction (N/E/S/W)"
        };
        this.Controls.Add(regionalCitiesTextBox);

        yPos += spacing;

        // Major cities
        var majorCitiesLabel = new Label
        {
            Text = "Major Cities Range:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(150, labelHeight)
        };
        this.Controls.Add(majorCitiesLabel);

        majorCitiesTextBox = new TextBox
        {
            Location = new System.Drawing.Point(180, yPos),
            Size = new System.Drawing.Size(60, textBoxHeight),
            AccessibleName = "Major cities range",
            AccessibleDescription = "Distance in miles or kilometers for major cities"
        };
        this.Controls.Add(majorCitiesTextBox);

        yPos += spacing;

        // Major Cities Population Threshold
        var populationThresholdLabel = new Label
        {
            Text = "Minimum Population for Major Cities:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(200, labelHeight)
        };
        this.Controls.Add(populationThresholdLabel);

        populationThresholdTextBox = new TextBox
        {
            Location = new System.Drawing.Point(230, yPos),
            Size = new System.Drawing.Size(80, textBoxHeight),
            AccessibleName = "Major cities population threshold",
            AccessibleDescription = "Minimum population for a city to be considered a major city"
        };
        this.Controls.Add(populationThresholdTextBox);

        var populationHelpLabel = new Label
        {
            Text = "(e.g., 10000, 25000, 50000)",
            Location = new System.Drawing.Point(320, yPos),
            Size = new System.Drawing.Size(150, labelHeight),
            ForeColor = System.Drawing.Color.Gray,
            AccessibleName = "Population threshold help"
        };
        this.Controls.Add(populationHelpLabel);

        yPos += spacing;

        // Landmarks
        var landmarksLabel = new Label
        {
            Text = "Landmarks Range:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(150, labelHeight)
        };
        this.Controls.Add(landmarksLabel);

        landmarksTextBox = new TextBox
        {
            Location = new System.Drawing.Point(180, yPos),
            Size = new System.Drawing.Size(60, textBoxHeight),
            AccessibleName = "Landmarks range",
            AccessibleDescription = "Distance in miles or kilometers for landmarks and points of interest"
        };
        this.Controls.Add(landmarksTextBox);

        yPos += spacing + 10;

        // Units selection
        var unitsLabel = new Label
        {
            Text = "Distance Units:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(100, labelHeight)
        };
        this.Controls.Add(unitsLabel);

        milesRadioButton = new RadioButton
        {
            Text = "Miles",
            Location = new System.Drawing.Point(130, yPos),
            Size = new System.Drawing.Size(70, labelHeight),
            AccessibleName = "Miles unit"
        };
        this.Controls.Add(milesRadioButton);

        kilometersRadioButton = new RadioButton
        {
            Text = "Kilometers",
            Location = new System.Drawing.Point(210, yPos),
            Size = new System.Drawing.Size(100, labelHeight),
            AccessibleName = "Kilometers unit"
        };
        this.Controls.Add(kilometersRadioButton);

        yPos += spacing;

        // Nearest City Announcement section
        var nearestCityAnnouncementLabel = new Label
        {
            Text = "Announce nearest city automatically:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(250, labelHeight),
            AccessibleName = "Announce nearest city automatically label"
        };
        this.Controls.Add(nearestCityAnnouncementLabel);

        nearestCityAnnouncementComboBox = new ComboBox
        {
            Location = new System.Drawing.Point(280, yPos),
            Size = new System.Drawing.Size(150, textBoxHeight),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Announce nearest city automatically",
            AccessibleDescription = "Choose how often to automatically announce the nearest city"
        };
        nearestCityAnnouncementComboBox.Items.AddRange(new object[]
        {
            "Off",
            "Every 1 minute",
            "Every 2 minutes",
            "Every 5 minutes",
            "Every 10 minutes",
            "Every 15 minutes",
            "Every 20 minutes"
        });
        this.Controls.Add(nearestCityAnnouncementComboBox);

        yPos += spacing + 20;

        // Maximum Results section
        var maxResultsLabel = new Label
        {
            Text = "Maximum Results Per Category:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(450, labelHeight),
            Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold),
            AccessibleName = "Maximum results section"
        };
        this.Controls.Add(maxResultsLabel);

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
        this.Controls.Add(maxNearbyLabel);

        maxNearbyPlacesTextBox = new TextBox
        {
            Location = new System.Drawing.Point(leftColumn + labelWidth + 5, yPos),
            Size = new System.Drawing.Size(textBoxWidth, textBoxHeight),
            AccessibleName = "Maximum nearby places"
        };
        this.Controls.Add(maxNearbyPlacesTextBox);

        var maxMajorCitiesLabel = new Label
        {
            Text = "Major Cities:",
            Location = new System.Drawing.Point(rightColumn, yPos),
            Size = new System.Drawing.Size(labelWidth, labelHeight)
        };
        this.Controls.Add(maxMajorCitiesLabel);

        maxMajorCitiesTextBox = new TextBox
        {
            Location = new System.Drawing.Point(rightColumn + labelWidth + 5, yPos),
            Size = new System.Drawing.Size(textBoxWidth, textBoxHeight),
            AccessibleName = "Maximum major cities"
        };
        this.Controls.Add(maxMajorCitiesTextBox);

        yPos += spacing;

        var maxAirportsLabel = new Label
        {
            Text = "Airports:",
            Location = new System.Drawing.Point(leftColumn, yPos),
            Size = new System.Drawing.Size(labelWidth, labelHeight)
        };
        this.Controls.Add(maxAirportsLabel);

        maxAirportsTextBox = new TextBox
        {
            Location = new System.Drawing.Point(leftColumn + labelWidth + 5, yPos),
            Size = new System.Drawing.Size(textBoxWidth, textBoxHeight),
            AccessibleName = "Maximum airports"
        };
        this.Controls.Add(maxAirportsTextBox);

        var maxTerrainLabel = new Label
        {
            Text = "Terrain Features:",
            Location = new System.Drawing.Point(rightColumn, yPos),
            Size = new System.Drawing.Size(labelWidth, labelHeight)
        };
        this.Controls.Add(maxTerrainLabel);

        maxTerrainFeaturesTextBox = new TextBox
        {
            Location = new System.Drawing.Point(rightColumn + labelWidth + 5, yPos),
            Size = new System.Drawing.Size(textBoxWidth, textBoxHeight),
            AccessibleName = "Maximum terrain features"
        };
        this.Controls.Add(maxTerrainFeaturesTextBox);

        yPos += spacing;

        var maxWaterLabel = new Label
        {
            Text = "Water Bodies:",
            Location = new System.Drawing.Point(leftColumn, yPos),
            Size = new System.Drawing.Size(labelWidth, labelHeight)
        };
        this.Controls.Add(maxWaterLabel);

        maxWaterBodiesTextBox = new TextBox
        {
            Location = new System.Drawing.Point(leftColumn + labelWidth + 5, yPos),
            Size = new System.Drawing.Size(textBoxWidth, textBoxHeight),
            AccessibleName = "Maximum water bodies"
        };
        this.Controls.Add(maxWaterBodiesTextBox);

        var maxTouristLabel = new Label
        {
            Text = "Tourist Landmarks:",
            Location = new System.Drawing.Point(rightColumn, yPos),
            Size = new System.Drawing.Size(labelWidth, labelHeight)
        };
        this.Controls.Add(maxTouristLabel);

        maxTouristLandmarksTextBox = new TextBox
        {
            Location = new System.Drawing.Point(rightColumn + labelWidth + 5, yPos),
            Size = new System.Drawing.Size(textBoxWidth, textBoxHeight),
            AccessibleName = "Maximum tourist landmarks"
        };
        this.Controls.Add(maxTouristLandmarksTextBox);

        yPos += spacing + 20;

        // Buttons
        resetDefaultsButton = new Button
        {
            Text = "Reset to Defaults",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(120, 30),
            AccessibleName = "Reset to defaults",
            AccessibleDescription = "Reset all distance ranges to default values"
        };
        resetDefaultsButton.Click += ResetDefaultsButton_Click;
        this.Controls.Add(resetDefaultsButton);

        cancelButton = new Button
        {
            Text = "Cancel",
            Location = new System.Drawing.Point(300, yPos),
            Size = new System.Drawing.Size(80, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel"
        };
        this.Controls.Add(cancelButton);

        saveButton = new Button
        {
            Text = "Save",
            Location = new System.Drawing.Point(390, yPos),
            Size = new System.Drawing.Size(80, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "Save settings"
        };
        saveButton.Click += SaveButton_Click;
        this.Controls.Add(saveButton);

        this.AcceptButton = saveButton;
        this.CancelButton = cancelButton;

        this.ResumeLayout(false);
    }

    private void LoadCurrentSettings()
    {
        apiUsernameTextBox.Text = SettingsManager.Current.GeoNamesApiUsername;
        nearbyCitiesTextBox.Text = SettingsManager.Current.NearbyCitiesRange.ToString();
        regionalCitiesTextBox.Text = SettingsManager.Current.RegionalCitiesRange.ToString();
        majorCitiesTextBox.Text = SettingsManager.Current.MajorCitiesRange.ToString();
        populationThresholdTextBox.Text = SettingsManager.Current.MajorCityPopulationThreshold.ToString();
        landmarksTextBox.Text = SettingsManager.Current.LandmarksRange.ToString();

        // Load maximum results settings
        maxNearbyPlacesTextBox.Text = SettingsManager.Current.MaxNearbyPlacesToShow.ToString();
        maxMajorCitiesTextBox.Text = SettingsManager.Current.MaxMajorCitiesToShow.ToString();
        maxAirportsTextBox.Text = SettingsManager.Current.MaxAirportsToShow.ToString();
        maxTerrainFeaturesTextBox.Text = SettingsManager.Current.MaxTerrainFeaturesToShow.ToString();
        maxWaterBodiesTextBox.Text = SettingsManager.Current.MaxWaterBodiesToShow.ToString();
        maxTouristLandmarksTextBox.Text = SettingsManager.Current.MaxTouristLandmarksToShow.ToString();

        if (SettingsManager.Current.DistanceUnits == "kilometers")
        {
            kilometersRadioButton.Checked = true;
        }
        else
        {
            milesRadioButton.Checked = true;
        }

        // Load nearest city announcement interval
        int interval = SettingsManager.Current.NearestCityAnnouncementInterval;
        if (interval == 0)
        {
            nearestCityAnnouncementComboBox.SelectedIndex = 0; // Off
        }
        else if (interval == 60)
        {
            nearestCityAnnouncementComboBox.SelectedIndex = 1; // Every 1 minute
        }
        else if (interval == 120)
        {
            nearestCityAnnouncementComboBox.SelectedIndex = 2; // Every 2 minutes
        }
        else if (interval == 300)
        {
            nearestCityAnnouncementComboBox.SelectedIndex = 3; // Every 5 minutes
        }
        else if (interval == 600)
        {
            nearestCityAnnouncementComboBox.SelectedIndex = 4; // Every 10 minutes
        }
        else if (interval == 900)
        {
            nearestCityAnnouncementComboBox.SelectedIndex = 5; // Every 15 minutes
        }
        else if (interval == 1200)
        {
            nearestCityAnnouncementComboBox.SelectedIndex = 6; // Every 20 minutes
        }
        else
        {
            nearestCityAnnouncementComboBox.SelectedIndex = 0; // Default to Off if unrecognized
        }
    }

    private void ResetDefaultsButton_Click(object? sender, EventArgs e)
    {
        nearbyCitiesTextBox.Text = "25";
        regionalCitiesTextBox.Text = "50";
        majorCitiesTextBox.Text = "185";
        populationThresholdTextBox.Text = "50000";
        landmarksTextBox.Text = "30";

        // Reset maximum results to defaults
        maxNearbyPlacesTextBox.Text = "10";
        maxMajorCitiesTextBox.Text = "10";
        maxAirportsTextBox.Text = "8";
        maxTerrainFeaturesTextBox.Text = "8";
        maxWaterBodiesTextBox.Text = "8";
        maxTouristLandmarksTextBox.Text = "8";

        milesRadioButton.Checked = true;
        nearestCityAnnouncementComboBox.SelectedIndex = 0; // Off
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        try
        {
            // Validate and save API username
            SettingsManager.Current.GeoNamesApiUsername = apiUsernameTextBox.Text.Trim();

            // Validate and save distance ranges
            if (int.TryParse(nearbyCitiesTextBox.Text, out int nearbyCities) && nearbyCities > 0)
            {
                SettingsManager.Current.NearbyCitiesRange = nearbyCities;
            }
            else
            {
                MessageBox.Show("Please enter a valid number for Nearby Cities Range.", "Invalid Input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                nearbyCitiesTextBox.Focus();
                return;
            }

            if (int.TryParse(regionalCitiesTextBox.Text, out int regionalCities) && regionalCities > 0)
            {
                SettingsManager.Current.RegionalCitiesRange = regionalCities;
            }
            else
            {
                MessageBox.Show("Please enter a valid number for Regional Cities Range.", "Invalid Input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                regionalCitiesTextBox.Focus();
                return;
            }

            if (int.TryParse(majorCitiesTextBox.Text, out int majorCities) && majorCities > 0)
            {
                // Check if radius exceeds GeoNames API limit (300 km / ~186 miles)
                var units = kilometersRadioButton.Checked ? "km" : "miles";
                var radiusKm = units == "km" ? majorCities : (int)Math.Round(majorCities * 1.60934);

                if (radiusKm > 300)
                {
                    var maxInUnits = units == "km" ? "300 km" : "186 miles";
                    var result = MessageBox.Show($"Warning: Major Cities range of {majorCities} {units} exceeds the GeoNames API free service limit of {maxInUnits}.\n\nThe application will automatically cap the radius at {maxInUnits} for API calls.\n\nDo you want to continue?",
                        "Radius Limit Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (result == DialogResult.No)
                    {
                        majorCitiesTextBox.Focus();
                        return;
                    }
                }

                SettingsManager.Current.MajorCitiesRange = majorCities;
            }
            else
            {
                MessageBox.Show("Please enter a valid number for Major Cities Range.", "Invalid Input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                majorCitiesTextBox.Focus();
                return;
            }

            // Validate and save population threshold
            if (int.TryParse(populationThresholdTextBox.Text, out int populationThreshold) && populationThreshold >= 500)
            {
                SettingsManager.Current.MajorCityPopulationThreshold = populationThreshold;

                // Determine the appropriate API threshold based on user's population threshold
                // Use MORE restrictive API filters for HIGHER population requirements
                // This prevents flooding results with small cities when searching for large ones
                string apiThreshold;
                if (populationThreshold >= 50000)
                {
                    // For very high thresholds, use cities15000 to pre-filter small cities
                    apiThreshold = "cities15000";
                }
                else if (populationThreshold >= 15000)
                {
                    // For medium-high thresholds, use cities15000
                    apiThreshold = "cities15000";
                }
                else if (populationThreshold >= 5000)
                {
                    // For medium thresholds, use cities5000
                    apiThreshold = "cities5000";
                }
                else
                {
                    // For low thresholds, use cities1000
                    apiThreshold = "cities1000";
                }
                SettingsManager.Current.MajorCityAPIThreshold = apiThreshold;
            }
            else
            {
                MessageBox.Show("Please enter a valid number (minimum 500) for Major Cities Population Threshold.", "Invalid Input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                populationThresholdTextBox.Focus();
                return;
            }

            if (int.TryParse(landmarksTextBox.Text, out int landmarks) && landmarks > 0)
            {
                SettingsManager.Current.LandmarksRange = landmarks;
            }
            else
            {
                MessageBox.Show("Please enter a valid number for Landmarks Range.", "Invalid Input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                landmarksTextBox.Focus();
                return;
            }

            // Validate and save maximum results settings
            if (int.TryParse(maxNearbyPlacesTextBox.Text, out int maxNearbyPlaces) && maxNearbyPlaces > 0 && maxNearbyPlaces <= 50)
            {
                SettingsManager.Current.MaxNearbyPlacesToShow = maxNearbyPlaces;
            }
            else
            {
                MessageBox.Show("Please enter a valid number (1-50) for Maximum Nearby Places.", "Invalid Input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                maxNearbyPlacesTextBox.Focus();
                return;
            }

            if (int.TryParse(maxMajorCitiesTextBox.Text, out int maxMajorCities) && maxMajorCities > 0 && maxMajorCities <= 50)
            {
                SettingsManager.Current.MaxMajorCitiesToShow = maxMajorCities;
            }
            else
            {
                MessageBox.Show("Please enter a valid number (1-50) for Maximum Major Cities.", "Invalid Input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                maxMajorCitiesTextBox.Focus();
                return;
            }

            if (int.TryParse(maxAirportsTextBox.Text, out int maxAirports) && maxAirports > 0 && maxAirports <= 50)
            {
                SettingsManager.Current.MaxAirportsToShow = maxAirports;
            }
            else
            {
                MessageBox.Show("Please enter a valid number (1-50) for Maximum Airports.", "Invalid Input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                maxAirportsTextBox.Focus();
                return;
            }

            if (int.TryParse(maxTerrainFeaturesTextBox.Text, out int maxTerrain) && maxTerrain > 0 && maxTerrain <= 50)
            {
                SettingsManager.Current.MaxTerrainFeaturesToShow = maxTerrain;
            }
            else
            {
                MessageBox.Show("Please enter a valid number (1-50) for Maximum Terrain Features.", "Invalid Input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                maxTerrainFeaturesTextBox.Focus();
                return;
            }

            if (int.TryParse(maxWaterBodiesTextBox.Text, out int maxWater) && maxWater > 0 && maxWater <= 50)
            {
                SettingsManager.Current.MaxWaterBodiesToShow = maxWater;
            }
            else
            {
                MessageBox.Show("Please enter a valid number (1-50) for Maximum Water Bodies.", "Invalid Input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                maxWaterBodiesTextBox.Focus();
                return;
            }

            if (int.TryParse(maxTouristLandmarksTextBox.Text, out int maxTourist) && maxTourist > 0 && maxTourist <= 50)
            {
                SettingsManager.Current.MaxTouristLandmarksToShow = maxTourist;
            }
            else
            {
                MessageBox.Show("Please enter a valid number (1-50) for Maximum Tourist Landmarks.", "Invalid Input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                maxTouristLandmarksTextBox.Focus();
                return;
            }

            // Save nearest city announcement interval
            int selectedInterval = nearestCityAnnouncementComboBox.SelectedIndex switch
            {
                0 => 0,      // Off
                1 => 60,     // Every 1 minute
                2 => 120,    // Every 2 minutes
                3 => 300,    // Every 5 minutes
                4 => 600,    // Every 10 minutes
                5 => 900,    // Every 15 minutes
                6 => 1200,   // Every 20 minutes
                _ => 0       // Default to Off
            };
            SettingsManager.Current.NearestCityAnnouncementInterval = selectedInterval;

            // Save units
            SettingsManager.Current.DistanceUnits = kilometersRadioButton.Checked ? "kilometers" : "miles";

            // Save settings
            SettingsManager.Save();

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving settings: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}