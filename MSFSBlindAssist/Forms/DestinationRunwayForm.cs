using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;

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
    private TextBox statusTextBox = null!;

    private readonly IAirportDataProvider _database;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly IntPtr previousWindow;
    private readonly string? _presetIcao;
    private readonly string? _presetRunwayId;
    private int _loadGeneration;
    private bool _enterWhileLoading;

    private const string TypedIcaoDescription = "Enter the 4-letter ICAO code for the destination airport";
    private const string PrefilledIcaoDescription =
        "Filled in from your destination or flight plan. Type to replace it, or press Enter for the runways.";

    public Runway? SelectedRunway { get; private set; }
    public Airport? SelectedAirport { get; private set; }
    /// <summary>
    /// True when the pilot checked "Manual landing assist" — arms the flare tones as well as the
    /// rollout centerline tone (LandingFlareAssistManager) for this destination. Unchecked by default,
    /// the choice for ILS-flown approaches: the destination then arms the rollout centerline tone alone
    /// while the Hand Fly setting RolloutCenterlineToneEnabled is on (MainForm.RefreshRolloutCueArming).
    /// </summary>
    public bool ManualLandingAssist => manualLandingCheckBox.Checked;

    /// <param name="presetIcao">Airport the app already knows (LandingExitPlannerPreset.Resolve),
    /// so the ICAO box opens filled in. Null opens it empty.</param>
    /// <param name="presetRunwayId">Runway to highlight when the list is entered, or null.</param>
    public DestinationRunwayForm(IAirportDataProvider database, ScreenReaderAnnouncer announcer,
        string? presetIcao = null, string? presetRunwayId = null)
    {
        _announcer = announcer;
        _presetIcao = presetIcao;
        _presetRunwayId = presetRunwayId;
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
            AccessibleDescription = TypedIcaoDescription
        };
        icaoTextBox.TextChanged += IcaoTextBox_TextChanged;
        // A single-line TextBox doesn't claim Enter as an input key, so without this the
        // Enter press goes to the form's AcceptButton and KeyDown never fires (PR #318 review).
        icaoTextBox.PreviewKeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) e.IsInputKey = true; };
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
        runwayListBox.Enter += RunwayListBox_Enter;

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

        // Status box: a read-only TextBox, not a Label, so it is in the tab order and a
        // screen-reader user can reach the lookup result ("Found N runways…", "Airport X
        // not found…") without hunting with the review cursor (VAT-13).
        statusTextBox = new TextBox
        {
            Location = new Point(20, 290),
            Size = new Size(350, 20),
            ReadOnly = true,
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
            manualLandingCheckBox, statusTextBox, selectButton, cancelButton
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
        statusTextBox.TabIndex = 3;
        selectButton.TabIndex = 4;
        cancelButton.TabIndex = 5;

        // Focus and bring window to front when opened
        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false; // Flash to bring to front

            // Fill in the destination airport the app already knows (the destination set
            // earlier, or the flight plan arrival). Setting the text loads the runway list.
            // The text is selected so typing a different ICAO replaces it, and the screen
            // reader speaks the filled-in value when the box takes focus. Only a 4-character
            // code: the box loads runways only at exactly 4 characters, so a shorter ident
            // (an FAA code like 0S9) would sit in the box unloadable.
            if (_presetIcao is { Length: 4 })
            {
                icaoTextBox.Text = _presetIcao;
                icaoTextBox.SelectAll(); // TextChanged has already set the prefilled description
            }
            icaoTextBox.Focus();
        };
    }

    private void IcaoTextBox_TextChanged(object? sender, EventArgs e)
    {
        string icao = icaoTextBox.Text.Trim();
        icaoTextBox.AccessibleDescription =
            _presetIcao != null && string.Equals(icao, _presetIcao, StringComparison.OrdinalIgnoreCase)
                ? PrefilledIcaoDescription : TypedIcaoDescription;

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
        if (e.KeyCode != Keys.Enter) return;
        // Always swallow it: PreviewKeyDown claimed Enter, so an unhandled one reaches the
        // native edit control, which beeps.
        e.Handled = true;
        e.SuppressKeyPress = true;

        if (runwayListBox.SelectedItem != null)
            SelectButton_Click(sender, e); // a runway is already chosen: Enter confirms, as AcceptButton did
        else if (runwayListBox.Items.Count > 0)
            runwayListBox.Focus(); // RunwayListBox_Enter highlights the planned runway
        else
        {
            _announcer.Announce(statusTextBox.Text); // nothing to move to: say why
            // Still loading: move to the list once it arrives, so the pilot needn't guess
            // when to press Enter again.
            _enterWhileLoading = true;
        }
    }

    /// <summary>
    /// Speaks <paramref name="message"/> after the screen reader has read the focus change
    /// that is happening now (the window opening, or the list taking focus). Announce queues
    /// without interrupting, but NVDA and JAWS drop queued speech on a focus change, and they
    /// handle that change in their own process, after our handler has already returned.
    /// </summary>
    private void AnnounceAfterFocus(string message)
    {
        // ponytail: fixed 500 ms, a judgement; tune here if a reader still talks over it.
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (s, e) =>
        {
            timer.Dispose();
            if (!IsDisposed && !Disposing) _announcer.Announce(message);
        };
        timer.Start();
    }

    /// <summary>
    /// The list taking focus (Tab, or Enter from the ICAO box) with nothing selected highlights
    /// the planned runway — the first runway when none is planned or the box holds a typed
    /// airport. Through LandingExitPlannerPreset.SelectRunway, the Landing Exit Planner's
    /// selector, so both windows match designators the same way and give the same notice when
    /// the planned runway is not in this airport's list (closed, or renamed for magnetic
    /// drift — how #234 started). Not done in LoadRunways: selecting there enables the Select
    /// button, and Enter in the ICAO box would then confirm a runway straight away.
    /// </summary>
    private void RunwayListBox_Enter(object? sender, EventArgs e)
    {
        if (runwayListBox.SelectedIndex != -1 || runwayListBox.Items.Count == 0) return;

        bool presetAirport = !string.IsNullOrEmpty(_presetRunwayId)
            && string.Equals(icaoTextBox.Text.Trim(), _presetIcao, StringComparison.OrdinalIgnoreCase);
        var ids = runwayListBox.Items.OfType<Runway>().Select(r => r.RunwayID).ToList();
        var selection = LandingExitPlannerPreset.SelectRunway(ids, presetAirport ? _presetRunwayId : null);

        if (selection.Index >= 0 && selection.Index < runwayListBox.Items.Count)
            runwayListBox.SelectedIndex = selection.Index;
        if (selection.Notice != null)
        {
            statusTextBox.Text = selection.Notice;
            AnnounceAfterFocus(selection.Notice); // behind the list's own focus readout
        }
    }

    /// <summary>
    /// Loads the airport's runways off the UI thread: GetRunways is a multi-JOIN query with
    /// correlated ILS subqueries, and the prefill runs it on every open (LandingExitForm moved
    /// the same query off-thread, PR #238 §8a). A failed lookup is spoken, not only written to
    /// the status box: with the prefill the pilot never typed anything, and a wrong typed code
    /// is a validation error (CORE-7).
    /// </summary>
    private async void LoadRunways(string icao)
    {
        ClearRunways(); // never leave the previous airport's runways selectable while loading
        int generation = _loadGeneration;
        statusTextBox.Text = $"Loading runways for {icao}…";

        string? failure = null;
        try
        {
            var (airport, runways) = await Task.Run(() =>
            {
                var a = _database.GetAirport(icao);
                return (a, a == null ? new List<Runway>() : _database.GetRunways(icao));
            });
            if (IsDisposed || Disposing || generation != _loadGeneration) return;

            if (airport == null)
                failure = $"Airport {icao} not found in database";
            else if (runways.Count == 0)
                failure = $"No runways found for {icao}";
            else
            {
                SelectedAirport = airport;
                runwayListBox.Items.AddRange(runways.ToArray());
                statusTextBox.Text = $"Found {runways.Count} runways for {airport.Name}";
                if (_enterWhileLoading)
                {
                    _enterWhileLoading = false;
                    runwayListBox.Focus();
                }
                return;
            }
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing || generation != _loadGeneration) return;
            failure = $"Error loading runways: {ex.Message}";
        }

        _enterWhileLoading = false;
        statusTextBox.Text = failure;
        // At open, the result can arrive before the reader has read the window's focus
        // change, which would drop it.
        AnnounceAfterFocus(failure);
    }

    private void ClearRunways()
    {
        _loadGeneration++; // a load still in flight is for text the box no longer holds
        _enterWhileLoading = false;
        runwayListBox.Items.Clear();
        SelectedRunway = null;
        SelectedAirport = null;
        selectButton.Enabled = false;
        // Every time, not only when the box is empty: otherwise a discarded load leaves
        // "Loading runways…" or an old "Selected Runway…", which Enter would then speak.
        statusTextBox.Text = "Enter an airport ICAO code to see available runways";
    }

    private void RunwayListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (runwayListBox.SelectedItem is Runway selectedRunway)
        {
            SelectedRunway = selectedRunway;
            selectButton.Enabled = true;

            string description = $"Selected {selectedRunway}";
            statusTextBox.Text = description;
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