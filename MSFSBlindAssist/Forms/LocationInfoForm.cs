using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// The Location Info window (Output > Shift+L). It opens already filled from the offline location
/// data (the lookup takes milliseconds), with the "Where you are" box first and focused, so the
/// screen reader reads it on arrival; nothing else is spoken, since a separate summary would only
/// repeat that box (CORE-7). Escape or Alt+F4 closes it and returns focus to the simulator.
/// </summary>
public partial class LocationInfoForm : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private TableLayoutPanel mainLayout = null!;
    private TextBox whereYouAreTextBox = null!;
    private TextBox nearbyPlacesTextBox = null!;
    private TextBox majorCitiesTextBox = null!;
    private TextBox cardinalDirectionsTextBox = null!;
    private TextBox airportsTextBox = null!;
    private TextBox terrainTextBox = null!;
    private TextBox waterBodiesTextBox = null!;
    private TextBox touristLandmarksTextBox = null!;
    private readonly IntPtr previousWindow;

    internal LocationInfoForm(LocationReport report, bool kilometres)
    {
        // Capture the current foreground window (likely the simulator)
        previousWindow = GetForegroundWindow();
        InitializeComponent();
        Populate(report, kilometres);
    }

    private void InitializeComponent()
    {
        this.SuspendLayout();

        // Form properties
        this.Text = "Location Information";
        this.Size = new System.Drawing.Size(800, 640);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MinimumSize = new System.Drawing.Size(700, 540);
        this.MaximizeBox = true;
        this.MinimizeBox = true;
        this.ShowInTaskbar = true;
        this.TopMost = true;

        const int rows = 8;
        mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rows,
            AutoSize = true
        };
        for (int i = 0; i < rows; i++)
        {
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
        }
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // "Where you are" first: the box the window opens on.
        whereYouAreTextBox = CreateCategoryTextBox("Where you are");
        majorCitiesTextBox = CreateCategoryTextBox("Major Cities");
        touristLandmarksTextBox = CreateCategoryTextBox("Tourist Landmarks");
        terrainTextBox = CreateCategoryTextBox("Terrain");
        nearbyPlacesTextBox = CreateCategoryTextBox("Nearby Places");
        cardinalDirectionsTextBox = CreateCategoryTextBox("Cardinal Directions");
        airportsTextBox = CreateCategoryTextBox("Airports");
        waterBodiesTextBox = CreateCategoryTextBox("Water Bodies");

        var boxes = new[]
        {
            whereYouAreTextBox, majorCitiesTextBox, touristLandmarksTextBox, terrainTextBox,
            nearbyPlacesTextBox, cardinalDirectionsTextBox, airportsTextBox, waterBodiesTextBox,
        };
        for (int i = 0; i < boxes.Length; i++)
        {
            boxes[i].TabIndex = i;
            mainLayout.Controls.Add(boxes[i], 0, i);
        }

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
            whereYouAreTextBox.Focus();
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

    private void Populate(LocationReport report, bool kilometres)
    {
        whereYouAreTextBox.Text = LocationPhrases.WhereYouAreText(report.Where, kilometres);

        var data = report.Details ?? new LocationData();
        nearbyPlacesTextBox.Text = data.NearbyPlaces.Count > 0
            ? string.Join("\r\n", data.NearbyPlaces)
            : "No nearby places found.";
        majorCitiesTextBox.Text = data.MajorCities.Count > 0
            ? string.Join("\r\n", data.MajorCities)
            : "No major cities found.";

        string directions = data.Directions.ToString().Trim();
        cardinalDirectionsTextBox.Text = directions.Length > 0
            ? directions
            : "No cardinal direction information available.";

        PopulateLandmarkTextBox(airportsTextBox, data.CategorizedLandmarks, "Airports");
        PopulateLandmarkTextBox(terrainTextBox, data.CategorizedLandmarks, "Terrain");
        PopulateLandmarkTextBox(waterBodiesTextBox, data.CategorizedLandmarks, "Water Bodies");
        PopulateLandmarkTextBox(touristLandmarksTextBox, data.CategorizedLandmarks, "Tourist Landmarks");
    }

    private static void PopulateLandmarkTextBox(TextBox textBox, Dictionary<string, List<Landmark>> categorizedLandmarks, string category)
    {
        textBox.Text = categorizedLandmarks.TryGetValue(category, out var list) && list.Count > 0
            ? string.Join("\r\n", list)
            : $"No {category.ToLowerInvariant()} found.";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        // Restore focus to the previous window (likely the simulator)
        if (previousWindow != IntPtr.Zero)
        {
            SetForegroundWindow(previousWindow);
        }
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
