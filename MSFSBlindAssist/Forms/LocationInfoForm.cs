using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms;
public partial class LocationInfoForm : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private TableLayoutPanel mainLayout = null!;
    private TextBox nearbyPlacesTextBox = null!;
    private TextBox majorCitiesTextBox = null!;
    private TextBox cardinalDirectionsTextBox = null!;
    private TextBox airportsTextBox = null!;
    private TextBox terrainTextBox = null!;
    private TextBox waterBodiesTextBox = null!;
    private TextBox touristLandmarksTextBox = null!;
    private double latitude;
    private double longitude;
    private GeoNamesService geoNamesService = null!;
    private ScreenReaderAnnouncer? announcer;
    private readonly IntPtr previousWindow;

    public LocationInfoForm(double lat, double lon, ScreenReaderAnnouncer? screenReaderAnnouncer = null)
    {
        // Capture the current foreground window (likely the simulator)
        previousWindow = GetForegroundWindow();

        latitude = lat;
        longitude = lon;
        announcer = screenReaderAnnouncer;
        geoNamesService = new GeoNamesService();
        InitializeComponent();
        // LoadLocationInfoAsync() moved to Shown event to ensure handle is created
    }

    private void InitializeComponent()
    {
        this.SuspendLayout();

        // Form properties
        this.Text = "Location Information";
        this.Size = new System.Drawing.Size(800, 600);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MinimumSize = new System.Drawing.Size(700, 500);
        this.MaximizeBox = true;
        this.MinimizeBox = true;
        this.ShowInTaskbar = true;
        this.TopMost = true;

        // Create main layout
        mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            AutoSize = true
        };

        // Set row styles for equal distribution
        for (int i = 0; i < 7; i++)
        {
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 14.3f));
        }
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // Create category text boxes
        majorCitiesTextBox = CreateCategoryTextBox("Major Cities");
        majorCitiesTextBox.TabIndex = 0;

        touristLandmarksTextBox = CreateCategoryTextBox("Tourist Landmarks");
        touristLandmarksTextBox.TabIndex = 1;

        terrainTextBox = CreateCategoryTextBox("Terrain");
        terrainTextBox.TabIndex = 2;

        nearbyPlacesTextBox = CreateCategoryTextBox("Nearby Places");
        nearbyPlacesTextBox.TabIndex = 3;

        cardinalDirectionsTextBox = CreateCategoryTextBox("Cardinal Directions");
        cardinalDirectionsTextBox.TabIndex = 4;

        airportsTextBox = CreateCategoryTextBox("Airports");
        airportsTextBox.TabIndex = 5;

        waterBodiesTextBox = CreateCategoryTextBox("Water Bodies");
        waterBodiesTextBox.TabIndex = 6;

        // Add controls to layout
        mainLayout.Controls.Add(majorCitiesTextBox, 0, 0);
        mainLayout.Controls.Add(touristLandmarksTextBox, 0, 1);
        mainLayout.Controls.Add(terrainTextBox, 0, 2);
        mainLayout.Controls.Add(nearbyPlacesTextBox, 0, 3);
        mainLayout.Controls.Add(cardinalDirectionsTextBox, 0, 4);
        mainLayout.Controls.Add(airportsTextBox, 0, 5);
        mainLayout.Controls.Add(waterBodiesTextBox, 0, 6);

        this.Controls.Add(mainLayout);

        // Handle key events for quick close
        this.KeyPreview = true;
        this.KeyDown += LocationInfoForm_KeyDown;

        // Focus and bring window to front when opened
        this.Shown += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false; // Flash to bring to front
            majorCitiesTextBox.Focus();

            // Start loading location data after handle is created
            LoadLocationInfoAsync();
        };

        this.ResumeLayout(false);
    }

    private TextBox CreateCategoryTextBox(string categoryName)
    {
        return new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular),
            BackColor = System.Drawing.SystemColors.Window,
            ForeColor = System.Drawing.SystemColors.WindowText,
            TabStop = true,
            AccessibleName = categoryName,
            AccessibleDescription = $"{categoryName} information",
            Text = "Loading..."
        };
    }

    private void LocationInfoForm_KeyDown(object? sender, KeyEventArgs e)
    {
        // Allow Escape or Alt+F4 to close the form
        if (e.KeyCode == Keys.Escape)
        {
            this.Close();
        }
    }

    /// <summary>
    /// Safely invokes an action on the UI thread, checking for handle creation and thread requirements
    /// </summary>
    private void SafeInvoke(Action action)
    {
        if (!IsHandleCreated || IsDisposed)
        {
            // Form not ready or already disposed, skip the action
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                Invoke(action);
            }
            catch (ObjectDisposedException)
            {
                // Form was disposed while trying to invoke, ignore
            }
            catch (InvalidOperationException)
            {
                // Handle was destroyed, ignore
            }
        }
        else
        {
            action();
        }
    }

    private async void LoadLocationInfoAsync()
    {
        try
        {
            var locationData = await geoNamesService.GetLocationInfoAsync(latitude, longitude);

            // Populate each category textbox - using SafeInvoke for thread safety
            SafeInvoke(() =>
            {
                PopulateCategoryTextBoxes(locationData);

                // Announce summary to screen reader
                AnnounceSummary(locationData);

                // Ensure focus is set after data is loaded
                majorCitiesTextBox.Focus();
            });
        }
        catch (Exception ex)
        {
            var errorMessage = "Error loading location information:\r\n\r\n";

            if (ex.Message.Contains("username is not configured"))
            {
                errorMessage += "GeoNames API username is not configured.\r\n";
                errorMessage += "Please go to File → Define GeoNames API Key to configure your API settings.\r\n\r\n";
                errorMessage += "You can register for a free GeoNames account at geonames.org";
            }
            else
            {
                errorMessage += $"Technical details: {ex.Message}\r\n\r\n";
                errorMessage += "This could be due to:\r\n";
                errorMessage += "• No internet connection\r\n";
                errorMessage += "• Invalid API username\r\n";
                errorMessage += "• GeoNames service temporarily unavailable\r\n";
                errorMessage += "• API rate limit exceeded";
            }

            // Show error in textboxes - using SafeInvoke for thread safety
            SafeInvoke(() =>
            {
                // Show error in the first textbox
                majorCitiesTextBox.Text = errorMessage;
                // Clear other textboxes
                touristLandmarksTextBox.Text = "";
                terrainTextBox.Text = "";
                nearbyPlacesTextBox.Text = "";
                cardinalDirectionsTextBox.Text = "";
                airportsTextBox.Text = "";
                waterBodiesTextBox.Text = "";

                // Ensure focus is set even on error
                majorCitiesTextBox.Focus();
            });
        }
    }

    private void PopulateCategoryTextBoxes(LocationData data)
    {
        // Nearby Places
        if (data.NearbyPlaces != null && data.NearbyPlaces.Count > 0)
        {
            nearbyPlacesTextBox.Text = string.Join("\r\n", data.NearbyPlaces);
        }
        else
        {
            nearbyPlacesTextBox.Text = "No nearby places found.";
        }

        // Major Cities
        if (data.MajorCities != null && data.MajorCities.Count > 0)
        {
            majorCitiesTextBox.Text = string.Join("\r\n", data.MajorCities);
        }
        else
        {
            majorCitiesTextBox.Text = "No major cities found.";
        }

        // Cardinal Directions
        if (data.Directions != null && !string.IsNullOrEmpty(data.Directions.ToString().Trim()))
        {
            cardinalDirectionsTextBox.Text = data.Directions.ToString();
        }
        else
        {
            cardinalDirectionsTextBox.Text = "No cardinal direction information available.";
        }

        // Categorized Landmarks
        PopulateLandmarkTextBox(airportsTextBox, data.CategorizedLandmarks, "Airports");
        PopulateLandmarkTextBox(terrainTextBox, data.CategorizedLandmarks, "Terrain");
        PopulateLandmarkTextBox(waterBodiesTextBox, data.CategorizedLandmarks, "Water Bodies");
        PopulateLandmarkTextBox(touristLandmarksTextBox, data.CategorizedLandmarks, "Tourist Landmarks");
    }

    private void PopulateLandmarkTextBox(TextBox textBox, Dictionary<string, List<Landmark>> categorizedLandmarks, string category)
    {
        if (categorizedLandmarks != null && categorizedLandmarks.ContainsKey(category) && categorizedLandmarks[category].Count > 0)
        {
            textBox.Text = string.Join("\r\n", categorizedLandmarks[category]);
        }
        else
        {
            textBox.Text = $"No {category.ToLower()} found.";
        }
    }


    private void AnnounceSummary(LocationData data)
    {
        try
        {
            var summary = new StringBuilder();

            // Find the closest place for summary
            if (data.NearbyPlaces != null && data.NearbyPlaces.Count > 0)
            {
                var closest = data.NearbyPlaces[0];
                summary.Append($"Near {closest.Name}");

                if (!string.IsNullOrEmpty(closest.State))
                {
                    summary.Append($", {closest.State}");
                }

                if (closest.Distance > 0)
                {
                    summary.Append($", {closest.Distance:F0} miles {closest.Direction}");
                }

                // Add major landmark if available
                if (data.NearbyPlaces.Count > 1)
                {
                    var secondPlace = data.NearbyPlaces[1];
                    if (secondPlace.Type == "airport" || secondPlace.Name.Contains("Airport"))
                    {
                        summary.Append($". {secondPlace.Name} {secondPlace.Distance:F0} miles {secondPlace.Direction}");
                    }
                }
            }
            else if (data.Regional != null && !string.IsNullOrEmpty(data.Regional.State))
            {
                summary.Append($"Currently in {data.Regional.State}");
                if (!string.IsNullOrEmpty(data.Regional.Country))
                {
                    summary.Append($", {data.Regional.Country}");
                }
            }

            if (summary.Length > 0 && announcer != null)
            {
                // Use the application's screen reader announcer
                announcer.Announce(summary.ToString());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocationInfoForm] Error announcing summary: {ex.Message}");
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        // Restore focus to the previous window (likely the simulator)
        if (previousWindow != IntPtr.Zero)
        {
            SetForegroundWindow(previousWindow);
        }

        // Clean up resources if needed
        geoNamesService = null!;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Handle Alt+F4 explicitly
        if (keyData == (Keys.Alt | Keys.F4))
        {
            this.Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}