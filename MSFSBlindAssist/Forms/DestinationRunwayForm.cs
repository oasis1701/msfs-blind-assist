using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Forms;
public partial class DestinationRunwayForm : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private TextBox icaoTextBox = null!;
    private ListBox runwayListBox = null!;
    private CheckBox manualLandingCheckBox = null!;
    private Button selectButton = null!;
    private Button cancelButton = null!;
    private Label statusLabel = null!;

    private readonly IAirportDataProvider _database;
    private readonly IntPtr previousWindow;
    private readonly DestinationPrefill _prefill;

    public Runway? SelectedRunway { get; private set; }
    public Airport? SelectedAirport { get; private set; }
    /// <summary>
    /// True when the pilot checked "Manual landing assist" — arms the flare tones as well as the
    /// rollout centerline tone (LandingFlareAssistManager) for this destination. Unchecked by default,
    /// the choice for ILS-flown approaches: the destination then arms the rollout centerline tone alone
    /// while the Hand Fly setting RolloutCenterlineToneEnabled is on (MainForm.RefreshRolloutCueArming).
    /// </summary>
    public bool ManualLandingAssist => manualLandingCheckBox.Checked;

    /// <param name="prefill">Airport (and optionally runway) the app already knows, so the
    /// ICAO box opens filled in. Null opens it empty.</param>
    public DestinationRunwayForm(IAirportDataProvider database, ScreenReaderAnnouncer announcer,
        DestinationPrefill? prefill = null)
    {
        _prefill = prefill ?? DestinationPrefill.None;
        // Capture the current foreground window (likely the simulator)
        previousWindow = GetForegroundWindow();

        _database = database;
        InitializeComponent();
        SetupAccessibility();
    }

    private void InitializeComponent()
    {
        Text = "Select Destination Runway";
        Size = new Size(400, 390);
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

        // Manual landing assist checkbox (unchecked by default). When checked, the flare tones
        // arm as well as the rollout centerline tone; unchecked (the normal choice for ILS-flown
        // approaches) the rollout centerline tone alone follows the Hand Fly setting.
        manualLandingCheckBox = new CheckBox
        {
            Text = "Manual landing assist (adds flare tones)",
            Location = new Point(20, 262),
            Size = new Size(350, 22),
            Checked = false,
            AccessibleName = "Manual landing assist",
            AccessibleDescription = "When checked, a flare guidance tone plays from 50 feet, then the centerline tone guides the rollout. Leave unchecked for ILS approaches: the rollout centerline tone still plays after touchdown while it is switched on in the Hand Fly settings, which it is by default."
        };

        // Status Label
        statusLabel = new Label
        {
            Location = new Point(20, 290),
            Size = new Size(350, 20),
            AccessibleName = "Status",
            Text = "Enter an airport ICAO code to see available runways"
        };

        // Buttons
        selectButton = new Button
        {
            Text = "Select",
            Location = new Point(215, 315),
            Size = new Size(75, 30),
            Enabled = false,
            AccessibleName = "Select Destination Runway",
            AccessibleDescription = "Set the selected runway as destination"
        };
        selectButton.Click += SelectButton_Click;

        cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(295, 315),
            Size = new Size(75, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            AccessibleDescription = "Cancel destination runway selection"
        };

        // Add controls to form
        Controls.AddRange(new Control[]
        {
            icaoLabel, icaoTextBox, runwayLabel, runwayListBox,
            manualLandingCheckBox, statusLabel, selectButton, cancelButton
        });

        AcceptButton = selectButton;
        CancelButton = cancelButton;
    }

    private void SetupAccessibility()
    {
        // Set tab order for logical navigation
        icaoTextBox.TabIndex = 0;
        runwayListBox.TabIndex = 1;
        manualLandingCheckBox.TabIndex = 2;
        selectButton.TabIndex = 3;
        cancelButton.TabIndex = 4;

        // Focus and bring window to front when opened
        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false; // Flash to bring to front

            // Fill in the destination airport the app already knows (flight plan arrival,
            // or the destination set earlier). Setting the text loads the runway list.
            // The text is selected so typing a different ICAO replaces it, and the screen
            // reader speaks the filled-in value when the box takes focus.
            if (_prefill.Icao != null && string.IsNullOrEmpty(icaoTextBox.Text))
            {
                icaoTextBox.Text = _prefill.Icao;
                icaoTextBox.SelectAll();
            }
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
                runwayListBox.SelectedIndex = PrefillRunwayIndex();
            }
            e.Handled = true;
        }
    }

    /// <summary>Index of the prefilled runway in the list, or 0 when it isn't there.</summary>
    private int PrefillRunwayIndex()
    {
        // Only when the box still holds the prefilled airport; a typed one starts at the top.
        if (!string.Equals(icaoTextBox.Text.Trim(), _prefill.Icao, StringComparison.OrdinalIgnoreCase))
            return 0;
        for (int i = 0; i < runwayListBox.Items.Count; i++)
        {
            if (runwayListBox.Items[i] is Runway r && _prefill.MatchesRunway(r.RunwayID))
                return i;
        }
        return 0;
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