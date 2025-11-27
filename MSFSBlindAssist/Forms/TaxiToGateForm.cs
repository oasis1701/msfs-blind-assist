using System.Runtime.InteropServices;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Form for setting up taxi route to a gate/parking spot
/// </summary>
public partial class TaxiToGateForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // Controls
    private Label _taxiwaysLabel = null!;
    private ListBox _taxiwaysListBox = null!;
    private Button _addButton = null!;
    private Button _removeButton = null!;
    private Button _moveUpButton = null!;
    private Button _moveDownButton = null!;
    private Label _routeLabel = null!;
    private ListBox _routeListBox = null!;
    private Label _gatesLabel = null!;
    private ListBox _gatesListBox = null!;
    private Label _statusLabel = null!;
    private Button _startButton = null!;
    private Button _cancelButton = null!;

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly IntPtr _previousWindow;
    private readonly TaxiwayGraph _graph;

    // Store gate info for display
    private readonly List<(string SpotName, bool HasJetway)> _gateInfo = new();

    /// <summary>
    /// The ordered list of selected taxiway names
    /// </summary>
    public List<string> SelectedTaxiways { get; } = new();

    /// <summary>
    /// The selected destination gate/parking name
    /// </summary>
    public string? SelectedGate { get; private set; }

    public TaxiToGateForm(TaxiwayGraph graph, ScreenReaderAnnouncer announcer)
    {
        _previousWindow = GetForegroundWindow();
        _announcer = announcer;
        _graph = graph;

        InitializeComponent();
        SetupAccessibility();
        PopulateData();
    }

    private void InitializeComponent()
    {
        Text = "Taxi to Gate";
        Size = new Size(650, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;

        // Available Taxiways section
        _taxiwaysLabel = new Label
        {
            Text = "Available Taxiways:",
            Location = new Point(20, 15),
            Size = new Size(150, 20),
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold)
        };

        _taxiwaysListBox = new ListBox
        {
            Location = new Point(20, 40),
            Size = new Size(150, 180),
            Font = new Font(Font.FontFamily, 10),
            AccessibleName = "Available Taxiways",
            AccessibleDescription = "Select a taxiway and press Enter or click Add to add it to your route"
        };
        _taxiwaysListBox.DoubleClick += TaxiwaysListBox_DoubleClick;
        _taxiwaysListBox.KeyDown += TaxiwaysListBox_KeyDown;

        // Add/Remove buttons
        _addButton = new Button
        {
            Text = "Add >>",
            Location = new Point(180, 60),
            Size = new Size(80, 30),
            AccessibleName = "Add taxiway to route",
            AccessibleDescription = "Add the selected taxiway to your route"
        };
        _addButton.Click += AddButton_Click;

        _removeButton = new Button
        {
            Text = "<< Remove",
            Location = new Point(180, 100),
            Size = new Size(80, 30),
            AccessibleName = "Remove taxiway from route",
            AccessibleDescription = "Remove the selected taxiway from your route"
        };
        _removeButton.Click += RemoveButton_Click;

        _moveUpButton = new Button
        {
            Text = "Move Up",
            Location = new Point(180, 150),
            Size = new Size(80, 30),
            AccessibleName = "Move taxiway up in route",
            AccessibleDescription = "Move the selected taxiway earlier in the route"
        };
        _moveUpButton.Click += MoveUpButton_Click;

        _moveDownButton = new Button
        {
            Text = "Move Down",
            Location = new Point(180, 190),
            Size = new Size(80, 30),
            AccessibleName = "Move taxiway down in route",
            AccessibleDescription = "Move the selected taxiway later in the route"
        };
        _moveDownButton.Click += MoveDownButton_Click;

        // Route section
        _routeLabel = new Label
        {
            Text = "Route (in order):",
            Location = new Point(270, 15),
            Size = new Size(150, 20),
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold)
        };

        _routeListBox = new ListBox
        {
            Location = new Point(270, 40),
            Size = new Size(150, 180),
            Font = new Font(Font.FontFamily, 10),
            AccessibleName = "Route taxiways in order",
            AccessibleDescription = "Your selected taxiways in the order they will be followed"
        };
        _routeListBox.SelectedIndexChanged += RouteListBox_SelectedIndexChanged;
        _routeListBox.KeyDown += RouteListBox_KeyDown;

        // Gates section
        _gatesLabel = new Label
        {
            Text = "Destination Gate:",
            Location = new Point(440, 15),
            Size = new Size(180, 20),
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold)
        };

        _gatesListBox = new ListBox
        {
            Location = new Point(440, 40),
            Size = new Size(180, 180),
            Font = new Font(Font.FontFamily, 10),
            AccessibleName = "Destination gates and parking",
            AccessibleDescription = "Select the gate or parking spot you want to reach"
        };
        _gatesListBox.SelectedIndexChanged += GatesListBox_SelectedIndexChanged;

        // Status label
        _statusLabel = new Label
        {
            Text = "Select taxiways in order and a destination gate",
            Location = new Point(20, 240),
            Size = new Size(600, 40),
            Font = new Font(Font.FontFamily, 9)
        };

        // Action buttons
        _startButton = new Button
        {
            Text = "Start Guidance",
            Location = new Point(440, 420),
            Size = new Size(100, 35),
            Enabled = false,
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            AccessibleName = "Start taxi guidance",
            AccessibleDescription = "Start guidance along the configured route"
        };
        _startButton.Click += StartButton_Click;

        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(550, 420),
            Size = new Size(75, 35),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            AccessibleDescription = "Cancel and close this window"
        };

        Controls.AddRange(new Control[]
        {
            _taxiwaysLabel, _taxiwaysListBox,
            _addButton, _removeButton, _moveUpButton, _moveDownButton,
            _routeLabel, _routeListBox,
            _gatesLabel, _gatesListBox,
            _statusLabel,
            _startButton, _cancelButton
        });

        AcceptButton = _startButton;
        CancelButton = _cancelButton;
    }

    private void SetupAccessibility()
    {
        _taxiwaysListBox.TabIndex = 0;
        _addButton.TabIndex = 1;
        _routeListBox.TabIndex = 2;
        _removeButton.TabIndex = 3;
        _moveUpButton.TabIndex = 4;
        _moveDownButton.TabIndex = 5;
        _gatesListBox.TabIndex = 6;
        _startButton.TabIndex = 7;
        _cancelButton.TabIndex = 8;

        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            _taxiwaysListBox.Focus();
        };

        Shown += (sender, e) =>
        {
            var taxiwayCount = _taxiwaysListBox.Items.Count;
            var gateCount = _gatesListBox.Items.Count;
            string announcement = $"Taxi to gate. {taxiwayCount} taxiways available, {gateCount} gates and parking spots. " +
                                  "Select taxiways in order, then select destination gate.";
            _announcer.AnnounceImmediate(announcement);
        };
    }

    private void PopulateData()
    {
        // Populate taxiways
        _taxiwaysListBox.Items.Clear();
        var taxiways = _graph.GetAllTaxiwayNames();
        foreach (var taxiway in taxiways)
        {
            _taxiwaysListBox.Items.Add(taxiway);
        }

        // Populate gates
        _gatesListBox.Items.Clear();
        _gateInfo.Clear();
        var parkingNodes = _graph.GetAllParkingNodes();
        foreach (var (_, spotName, hasJetway) in parkingNodes)
        {
            _gateInfo.Add((spotName, hasJetway));
            string displayText = hasJetway ? $"{spotName} (jetway)" : spotName;
            _gatesListBox.Items.Add(displayText);
        }

        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        _addButton.Enabled = _taxiwaysListBox.SelectedItem != null;
        _removeButton.Enabled = _routeListBox.SelectedItem != null;
        _moveUpButton.Enabled = _routeListBox.SelectedIndex > 0;
        _moveDownButton.Enabled = _routeListBox.SelectedIndex >= 0 &&
                                   _routeListBox.SelectedIndex < _routeListBox.Items.Count - 1;

        // Start button enabled when we have at least one taxiway and a gate selected
        bool hasRoute = _routeListBox.Items.Count > 0;
        bool hasGate = _gatesListBox.SelectedIndex >= 0;
        _startButton.Enabled = hasRoute && hasGate;

        // Update status
        if (!hasRoute)
        {
            _statusLabel.Text = "Add taxiways to your route in the order you will follow them.";
        }
        else if (!hasGate)
        {
            _statusLabel.Text = $"Route: {string.Join(" → ", GetRouteFromListBox())}. Now select a destination gate.";
        }
        else
        {
            var route = GetRouteFromListBox();
            string gateName = GetSelectedGateName() ?? "";
            _statusLabel.Text = $"Ready: {string.Join(" → ", route)} → {gateName}";
        }
    }

    private List<string> GetRouteFromListBox()
    {
        var route = new List<string>();
        foreach (var item in _routeListBox.Items)
        {
            route.Add(item.ToString()!);
        }
        return route;
    }

    private string? GetSelectedGateName()
    {
        int index = _gatesListBox.SelectedIndex;
        if (index >= 0 && index < _gateInfo.Count)
        {
            return _gateInfo[index].SpotName;
        }
        return null;
    }

    private void TaxiwaysListBox_DoubleClick(object? sender, EventArgs e)
    {
        AddButton_Click(sender, e);
    }

    private void TaxiwaysListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && _taxiwaysListBox.SelectedItem != null)
        {
            AddButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void AddButton_Click(object? sender, EventArgs e)
    {
        if (_taxiwaysListBox.SelectedItem is string taxiway)
        {
            _routeListBox.Items.Add(taxiway);
            _routeListBox.SelectedIndex = _routeListBox.Items.Count - 1;
            UpdateButtonStates();
            _announcer.AnnounceImmediate($"Added taxiway {taxiway} to route");
        }
    }

    private void RemoveButton_Click(object? sender, EventArgs e)
    {
        if (_routeListBox.SelectedItem is string taxiway)
        {
            int index = _routeListBox.SelectedIndex;
            _routeListBox.Items.RemoveAt(index);

            if (_routeListBox.Items.Count > 0)
            {
                _routeListBox.SelectedIndex = Math.Min(index, _routeListBox.Items.Count - 1);
            }

            UpdateButtonStates();
            _announcer.AnnounceImmediate($"Removed taxiway {taxiway} from route");
        }
    }

    private void MoveUpButton_Click(object? sender, EventArgs e)
    {
        int index = _routeListBox.SelectedIndex;
        if (index > 0)
        {
            var item = _routeListBox.Items[index];
            _routeListBox.Items.RemoveAt(index);
            _routeListBox.Items.Insert(index - 1, item);
            _routeListBox.SelectedIndex = index - 1;
            UpdateButtonStates();
            _announcer.AnnounceImmediate($"Moved {item} up");
        }
    }

    private void MoveDownButton_Click(object? sender, EventArgs e)
    {
        int index = _routeListBox.SelectedIndex;
        if (index >= 0 && index < _routeListBox.Items.Count - 1)
        {
            var item = _routeListBox.Items[index];
            _routeListBox.Items.RemoveAt(index);
            _routeListBox.Items.Insert(index + 1, item);
            _routeListBox.SelectedIndex = index + 1;
            UpdateButtonStates();
            _announcer.AnnounceImmediate($"Moved {item} down");
        }
    }

    private void RouteListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        UpdateButtonStates();
    }

    private void RouteListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && _routeListBox.SelectedItem != null)
        {
            RemoveButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void GatesListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        SelectedGate = GetSelectedGateName();
        UpdateButtonStates();
    }

    private void StartButton_Click(object? sender, EventArgs e)
    {
        // Populate the selected taxiways
        SelectedTaxiways.Clear();
        SelectedTaxiways.AddRange(GetRouteFromListBox());
        SelectedGate = GetSelectedGateName();

        if (SelectedTaxiways.Count > 0 && !string.IsNullOrEmpty(SelectedGate))
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

        if (DialogResult == DialogResult.Cancel && _previousWindow != IntPtr.Zero)
        {
            SetForegroundWindow(_previousWindow);
        }
    }
}
