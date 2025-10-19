using FBWBA.Database;
using FBWBA.Database.Models;
using FBWBA.Accessibility;

namespace FBWBA.Forms;
public partial class DestinationRunwayForm : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private TextBox icaoTextBox = null!;
    private ListBox runwayListBox = null!;
    private Button selectButton = null!;
    private Button cancelButton = null!;
    private Label statusLabel = null!;

    private readonly IAirportDataProvider _database;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly IntPtr previousWindow;

    public Runway? SelectedRunway { get; private set; }
    public Airport? SelectedAirport { get; private set; }

    public DestinationRunwayForm(IAirportDataProvider database, ScreenReaderAnnouncer announcer)
    {
        // Capture the current foreground window (likely the simulator)
        previousWindow = GetForegroundWindow();

        _database = database;
        _announcer = announcer;
        InitializeComponent();
        SetupAccessibility();
    }

    private void InitializeComponent()
    {
        Text = "Select Destination Runway";
        Size = new Size(400, 350);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        // ICAO Label and TextBox
        var icaoLabel = new Label
        {
            Text = "Airport ICAO Code:",
            Location = new Point(20, 20),
            Size = new Size(120, 20),
            AccessibleName = "Airport ICAO Code"
        };

        icaoTextBox = new TextBox
        {
            Location = new Point(20, 45),
            Size = new Size(100, 25),
            CharacterCasing = CharacterCasing.Upper,
            MaxLength = 4,
            AccessibleName = "Airport ICAO Code",
            AccessibleDescription = "Enter the 4-letter ICAO code for the destination airport"
        };
        icaoTextBox.TextChanged += IcaoTextBox_TextChanged;
        icaoTextBox.KeyDown += IcaoTextBox_KeyDown;

        // Runway Label and ListBox
        var runwayLabel = new Label
        {
            Text = "Available Runways:",
            Location = new Point(20, 80),
            Size = new Size(120, 20),
            AccessibleName = "Available Runways"
        };

        runwayListBox = new ListBox
        {
            Location = new Point(20, 105),
            Size = new Size(350, 150),
            AccessibleName = "Runway List",
            AccessibleDescription = "Select a runway from the list and press Enter to set as destination"
        };
        runwayListBox.SelectedIndexChanged += RunwayListBox_SelectedIndexChanged;
        runwayListBox.KeyDown += RunwayListBox_KeyDown;

        // Status Label
        statusLabel = new Label
        {
            Location = new Point(20, 265),
            Size = new Size(350, 20),
            AccessibleName = "Status",
            Text = "Enter an airport ICAO code to see available runways"
        };

        // Buttons
        selectButton = new Button
        {
            Text = "Select",
            Location = new Point(215, 290),
            Size = new Size(75, 30),
            Enabled = false,
            AccessibleName = "Select Destination Runway",
            AccessibleDescription = "Set the selected runway as destination"
        };
        selectButton.Click += SelectButton_Click;

        cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(295, 290),
            Size = new Size(75, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            AccessibleDescription = "Cancel destination runway selection"
        };

        // Add controls to form
        Controls.AddRange(new Control[]
        {
            icaoLabel, icaoTextBox, runwayLabel, runwayListBox,
            statusLabel, selectButton, cancelButton
        });

        AcceptButton = selectButton;
        CancelButton = cancelButton;
    }

    private void SetupAccessibility()
    {
        // Set tab order for logical navigation
        icaoTextBox.TabIndex = 0;
        runwayListBox.TabIndex = 1;
        selectButton.TabIndex = 2;
        cancelButton.TabIndex = 3;

        // Focus and bring window to front when opened
        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false; // Flash to bring to front
            icaoTextBox.Focus();
        };
    }

    private void IcaoTextBox_TextChanged(object? sender, EventArgs e)
    {
        string icao = icaoTextBox.Text.Trim();

        if (icao.Length == 4)
        {
            LoadRunways(icao);
        }
        else
        {
            ClearRunways();
        }
    }

    private void IcaoTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && runwayListBox.Items.Count > 0)
        {
            runwayListBox.Focus();
            if (runwayListBox.Items.Count > 0)
            {
                runwayListBox.SelectedIndex = 0;
            }
            e.Handled = true;
        }
    }

    private void LoadRunways(string icao)
    {
        try
        {
            var airport = _database.GetAirport(icao);
            if (airport == null)
            {
                statusLabel.Text = $"Airport {icao} not found in database";
                ClearRunways();
                return;
            }

            var runways = _database.GetRunways(icao);
            if (runways.Count == 0)
            {
                statusLabel.Text = $"No runways found for {icao}";
                ClearRunways();
                return;
            }

            SelectedAirport = airport;
            runwayListBox.Items.Clear();
            runwayListBox.Items.AddRange(runways.ToArray());

            statusLabel.Text = $"Found {runways.Count} runways for {airport.Name}";

            selectButton.Enabled = false;
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Error loading runways: {ex.Message}";
            ClearRunways();
        }
    }

    private void ClearRunways()
    {
        runwayListBox.Items.Clear();
        SelectedRunway = null;
        SelectedAirport = null;
        selectButton.Enabled = false;

        if (string.IsNullOrEmpty(icaoTextBox.Text))
        {
            statusLabel.Text = "Enter an airport ICAO code to see available runways";
        }
    }

    private void RunwayListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (runwayListBox.SelectedItem is Runway selectedRunway)
        {
            SelectedRunway = selectedRunway;
            selectButton.Enabled = true;

            string description = $"Selected {selectedRunway}";
            statusLabel.Text = description;
        }
        else
        {
            SelectedRunway = null;
            selectButton.Enabled = false;
        }
    }

    private void RunwayListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && runwayListBox.SelectedItem != null)
        {
            SelectButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void SelectButton_Click(object? sender, EventArgs e)
    {
        if (SelectedRunway != null && SelectedAirport != null)
        {
            DialogResult = DialogResult.OK;
            Close();

            // Restore focus to the previous window (likely the simulator)
            if (previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(previousWindow);
            }
        }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Handle Escape key
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        // Restore focus to the previous window (likely the simulator) if canceled
        if (DialogResult == DialogResult.Cancel && previousWindow != IntPtr.Zero)
        {
            SetForegroundWindow(previousWindow);
        }
    }
}