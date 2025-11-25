using System.Runtime.InteropServices;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Form for selecting an airport for taxiway guidance
/// </summary>
public partial class TaxiAirportSelectForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private TextBox _searchTextBox = null!;
    private ListBox _airportListBox = null!;
    private Button _selectButton = null!;
    private Button _cancelButton = null!;
    private Label _statusLabel = null!;

    private readonly TaxiwayDatabaseProvider _database;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly IntPtr _previousWindow;

    private System.Windows.Forms.Timer? _searchTimer;

    public string? SelectedIcao { get; private set; }
    public string? SelectedName { get; private set; }

    public TaxiAirportSelectForm(string databasePath, ScreenReaderAnnouncer announcer)
    {
        _previousWindow = GetForegroundWindow();
        _database = new TaxiwayDatabaseProvider(databasePath);
        _announcer = announcer;

        InitializeComponent();
        SetupAccessibility();
    }

    private void InitializeComponent()
    {
        Text = "Select Airport for Taxiway Guidance";
        Size = new Size(500, 400);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        // Search Label and TextBox
        var searchLabel = new Label
        {
            Text = "Search by ICAO or Airport Name:",
            Location = new Point(20, 20),
            Size = new Size(200, 20),
            AccessibleName = "Search"
        };

        _searchTextBox = new TextBox
        {
            Location = new Point(20, 45),
            Size = new Size(440, 25),
            AccessibleName = "Search for airport",
            AccessibleDescription = "Enter ICAO code or airport name to search"
        };
        _searchTextBox.TextChanged += SearchTextBox_TextChanged;
        _searchTextBox.KeyDown += SearchTextBox_KeyDown;

        // Airport List Label and ListBox
        var listLabel = new Label
        {
            Text = "Matching Airports:",
            Location = new Point(20, 80),
            Size = new Size(200, 20),
            AccessibleName = "Matching Airports"
        };

        _airportListBox = new ListBox
        {
            Location = new Point(20, 105),
            Size = new Size(440, 180),
            AccessibleName = "Airport List",
            AccessibleDescription = "Select an airport from the list"
        };
        _airportListBox.SelectedIndexChanged += AirportListBox_SelectedIndexChanged;
        _airportListBox.KeyDown += AirportListBox_KeyDown;
        _airportListBox.DoubleClick += AirportListBox_DoubleClick;

        // Status Label
        _statusLabel = new Label
        {
            Location = new Point(20, 295),
            Size = new Size(440, 40),
            AccessibleName = "Status",
            Text = "Type to search for an airport by ICAO code or name"
        };

        // Buttons
        _selectButton = new Button
        {
            Text = "Select",
            Location = new Point(305, 340),
            Size = new Size(75, 30),
            Enabled = false,
            AccessibleName = "Select Airport",
            AccessibleDescription = "Select the highlighted airport for taxiway guidance"
        };
        _selectButton.Click += SelectButton_Click;

        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(385, 340),
            Size = new Size(75, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            AccessibleDescription = "Cancel airport selection"
        };

        Controls.AddRange(new Control[]
        {
            searchLabel, _searchTextBox, listLabel, _airportListBox,
            _statusLabel, _selectButton, _cancelButton
        });

        AcceptButton = _selectButton;
        CancelButton = _cancelButton;

        // Setup search timer for debouncing
        _searchTimer = new System.Windows.Forms.Timer
        {
            Interval = 300
        };
        _searchTimer.Tick += SearchTimer_Tick;
    }

    private void SetupAccessibility()
    {
        _searchTextBox.TabIndex = 0;
        _airportListBox.TabIndex = 1;
        _selectButton.TabIndex = 2;
        _cancelButton.TabIndex = 3;

        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false;
            _searchTextBox.Focus();
        };
    }

    private void SearchTextBox_TextChanged(object? sender, EventArgs e)
    {
        // Debounce search
        _searchTimer?.Stop();
        _searchTimer?.Start();
    }

    private void SearchTimer_Tick(object? sender, EventArgs e)
    {
        _searchTimer?.Stop();
        PerformSearch();
    }

    private void PerformSearch()
    {
        string searchText = _searchTextBox.Text.Trim();

        if (searchText.Length < 2)
        {
            _airportListBox.Items.Clear();
            _statusLabel.Text = "Enter at least 2 characters to search";
            _selectButton.Enabled = false;
            return;
        }

        try
        {
            var airports = _database.SearchAirports(searchText, 50);

            _airportListBox.Items.Clear();

            if (airports.Count == 0)
            {
                _statusLabel.Text = $"No airports found matching '{searchText}'";
                _selectButton.Enabled = false;
                return;
            }

            foreach (var (icao, name, city, country) in airports)
            {
                string display = $"{icao} - {name}";
                if (!string.IsNullOrEmpty(city))
                    display += $", {city}";
                if (!string.IsNullOrEmpty(country))
                    display += $" ({country})";

                _airportListBox.Items.Add(new AirportItem(icao, name, display));
            }

            _statusLabel.Text = $"Found {airports.Count} airport{(airports.Count != 1 ? "s" : "")}";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error searching: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[TaxiAirportSelectForm] Search error: {ex}");
        }
    }

    private void SearchTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && _airportListBox.Items.Count > 0)
        {
            _airportListBox.Focus();
            _airportListBox.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Down && _airportListBox.Items.Count > 0)
        {
            _airportListBox.Focus();
            _airportListBox.SelectedIndex = 0;
            e.Handled = true;
        }
    }

    private void AirportListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_airportListBox.SelectedItem is AirportItem item)
        {
            SelectedIcao = item.Icao;
            SelectedName = item.Name;
            _selectButton.Enabled = true;

            // Check if airport has taxiway data
            int? airportId = _database.GetAirportId(item.Icao);
            if (airportId.HasValue)
            {
                int taxiCount = _database.GetTaxiPathCount(airportId.Value);
                _statusLabel.Text = $"Selected: {item.Icao} - {item.Name}\n{taxiCount} taxi segments available";
            }
            else
            {
                _statusLabel.Text = $"Selected: {item.Icao} - {item.Name}\nNo taxiway data available";
            }
        }
        else
        {
            SelectedIcao = null;
            SelectedName = null;
            _selectButton.Enabled = false;
        }
    }

    private void AirportListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && _airportListBox.SelectedItem != null)
        {
            SelectButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void AirportListBox_DoubleClick(object? sender, EventArgs e)
    {
        if (_airportListBox.SelectedItem != null)
        {
            SelectButton_Click(sender, e);
        }
    }

    private void SelectButton_Click(object? sender, EventArgs e)
    {
        if (SelectedIcao != null)
        {
            DialogResult = DialogResult.OK;
            Close();

            if (_previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(_previousWindow);
            }
        }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
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

        _searchTimer?.Stop();
        _searchTimer?.Dispose();

        if (DialogResult == DialogResult.Cancel && _previousWindow != IntPtr.Zero)
        {
            SetForegroundWindow(_previousWindow);
        }
    }

    /// <summary>
    /// Helper class for airport list items
    /// </summary>
    private class AirportItem
    {
        public string Icao { get; }
        public string Name { get; }
        public string Display { get; }

        public AirportItem(string icao, string name, string display)
        {
            Icao = icao;
            Name = name;
            Display = display;
        }

        public override string ToString() => Display;
    }
}
