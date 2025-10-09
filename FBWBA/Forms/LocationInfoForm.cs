using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using FBWBA.Models;
using FBWBA.Services;
using FBWBA.Accessibility;

namespace FBWBA.Forms
{
    public partial class LocationInfoForm : Form
    {
        // Windows API declarations for focus management
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private TableLayoutPanel mainLayout;
        private TextBox nearbyPlacesTextBox;
        private TextBox majorCitiesTextBox;
        private TextBox cardinalDirectionsTextBox;
        private TextBox airportsTextBox;
        private TextBox terrainTextBox;
        private TextBox waterBodiesTextBox;
        private TextBox touristLandmarksTextBox;
        private double latitude;
        private double longitude;
        private GeoNamesService geoNamesService;
        private ScreenReaderAnnouncer announcer;
        private readonly IntPtr previousWindow;

        public LocationInfoForm(double lat, double lon, ScreenReaderAnnouncer screenReaderAnnouncer = null)
        {
            // Capture the current foreground window (likely the simulator)
            previousWindow = GetForegroundWindow();

            latitude = lat;
            longitude = lon;
            announcer = screenReaderAnnouncer;
            geoNamesService = new GeoNamesService();
            InitializeComponent();
            LoadLocationInfoAsync();
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
            nearbyPlacesTextBox = CreateCategoryTextBox("Nearby Places");
            majorCitiesTextBox = CreateCategoryTextBox("Major Cities");
            cardinalDirectionsTextBox = CreateCategoryTextBox("Cardinal Directions");
            airportsTextBox = CreateCategoryTextBox("Airports");
            terrainTextBox = CreateCategoryTextBox("Terrain");
            waterBodiesTextBox = CreateCategoryTextBox("Water Bodies");
            touristLandmarksTextBox = CreateCategoryTextBox("Tourist Landmarks");

            // Add controls to layout
            mainLayout.Controls.Add(nearbyPlacesTextBox, 0, 0);
            mainLayout.Controls.Add(majorCitiesTextBox, 0, 1);
            mainLayout.Controls.Add(cardinalDirectionsTextBox, 0, 2);
            mainLayout.Controls.Add(airportsTextBox, 0, 3);
            mainLayout.Controls.Add(terrainTextBox, 0, 4);
            mainLayout.Controls.Add(waterBodiesTextBox, 0, 5);
            mainLayout.Controls.Add(touristLandmarksTextBox, 0, 6);

            this.Controls.Add(mainLayout);

            // Handle key events for quick close
            this.KeyPreview = true;
            this.KeyDown += LocationInfoForm_KeyDown;

            // Focus and bring window to front when opened
            this.Load += (sender, e) =>
            {
                BringToFront();
                Activate();
                TopMost = true;
                TopMost = false; // Flash to bring to front
                nearbyPlacesTextBox.Focus();
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

        private void LocationInfoForm_KeyDown(object sender, KeyEventArgs e)
        {
            // Allow Escape or Alt+F4 to close the form
            if (e.KeyCode == Keys.Escape)
            {
                this.Close();
            }
        }


        private async void LoadLocationInfoAsync()
        {
            try
            {
                var locationData = await geoNamesService.GetLocationInfoAsync(latitude, longitude);

                // Populate each category textbox
                PopulateCategoryTextBoxes(locationData);

                // Announce summary to screen reader
                AnnounceSummary(locationData);
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

                // Show error in the first textbox
                nearbyPlacesTextBox.Text = errorMessage;
                // Clear other textboxes
                majorCitiesTextBox.Text = "";
                cardinalDirectionsTextBox.Text = "";
                airportsTextBox.Text = "";
                terrainTextBox.Text = "";
                waterBodiesTextBox.Text = "";
                touristLandmarksTextBox.Text = "";
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
            geoNamesService = null;
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
}