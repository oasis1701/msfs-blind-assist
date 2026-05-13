using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Landing Exit Planner form. Lets the pilot pick a runway exit taxiway before
/// touchdown; the LandingExitPlanner then auto-activates taxi guidance on touchdown.
///
/// The form reuses the existing ILS destination selection (from SimConnectManager)
/// when available — no duplicate UI for picking the destination airport/runway.
/// If no ILS destination is set, the pilot can type an ICAO and pick a runway here.
///
/// Screen reader optimized: tab order follows ATC-like flow (airport → runway → exit).
/// </summary>
public class LandingExitForm : Form
{
    private readonly IAirportDataProvider _dataProvider;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly LandingExitPlanner _planner;
    private readonly SimConnectManager? _simConnectManager;

    private readonly string? _presetIcao;
    private readonly Runway? _presetRunway;

    /// <summary>
    /// Wraps a Runway so each combo item's ToString returns just the runway
    /// ID for the screen reader to read on focus. Avoids configuring
    /// DisplayMember on the combo (which only supports a single static
    /// property name and conflicts with custom item types).
    /// </summary>
    private sealed class RunwayChoice
    {
        public Runway Runway { get; }
        public RunwayChoice(Runway r) { Runway = r; }
        public override string ToString() => Runway.RunwayID;
    }

    private Label lblAirport = null!;
    private TextBox txtAirport = null!;
    private Label lblRunway = null!;
    private ComboBox cmbRunway = null!;
    private Label lblExit = null!;
    private ComboBox cmbExit = null!;
    private Button btnPlan = null!;
    private Button btnClear = null!;
    private Label lblStatus = null!;

    private string _currentIcao = "";
    private TaxiGraph? _graph;
    private List<Runway> _runways = new();
    private List<LandingExit> _exits = new();

    public LandingExitForm(
        IAirportDataProvider dataProvider,
        ScreenReaderAnnouncer announcer,
        LandingExitPlanner planner,
        string? presetIcao,
        Runway? presetRunway,
        SimConnectManager? simConnectManager = null)
    {
        _dataProvider = dataProvider;
        _announcer = announcer;
        _planner = planner;
        _simConnectManager = simConnectManager;
        _presetIcao = presetIcao;
        _presetRunway = presetRunway;
        InitializeFormControls();
    }

    private void InitializeFormControls()
    {
        this.Text = "Landing Exit Planner";
        this.Size = new System.Drawing.Size(460, 340);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.KeyPreview = true;
        this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) this.Close(); };

        int y = 15, labelX = 15, controlX = 15, controlWidth = 410;

        lblAirport = new Label
        {
            Text = "&Airport ICAO:",
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            AccessibleName = "Airport ICAO Label"
        };
        y += 20;
        txtAirport = new TextBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            CharacterCasing = CharacterCasing.Upper,
            AccessibleName = "Airport ICAO",
            AccessibleDescription = "ICAO of the destination airport. Pre-filled from your ILS destination if set."
        };
        txtAirport.Leave += (s, e) => LoadAirport(txtAirport.Text.Trim());
        y += 30;

        lblRunway = new Label
        {
            Text = "&Runway:",
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            AccessibleName = "Runway Label"
        };
        y += 20;
        cmbRunway = new ComboBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Runway",
            AccessibleDescription = "Landing runway. Pre-filled from your ILS destination if set."
        };
        cmbRunway.SelectedIndexChanged += (s, e) => RepopulateExits();
        y += 30;

        lblExit = new Label
        {
            Text = "&Exit taxiway:",
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            AccessibleName = "Exit taxiway Label"
        };
        y += 20;
        cmbExit = new ComboBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Exit taxiway",
            AccessibleDescription = "Exit taxiway from the runway, sorted by distance from landing threshold. High-speed exits are marked."
        };
        y += 35;

        btnPlan = new Button
        {
            Text = "&Plan Exit",
            Location = new System.Drawing.Point(controlX, y),
            Width = 180,
            Height = 30,
            AccessibleName = "Plan Exit",
            AccessibleDescription = "Save the selected exit. Guidance will auto-activate on touchdown."
        };
        btnPlan.Click += OnPlanClicked;

        btnClear = new Button
        {
            Text = "&Clear Plan",
            Location = new System.Drawing.Point(controlX + 190, y),
            Width = 180,
            Height = 30,
            AccessibleName = "Clear Plan",
            AccessibleDescription = "Clear any active landing exit plan."
        };
        btnClear.Click += (s, e) => { _planner.Clear(); lblStatus.Text = "Plan cleared."; };
        y += 40;

        lblStatus = new Label
        {
            Text = "",
            Location = new System.Drawing.Point(labelX, y),
            Width = controlWidth,
            Height = 60,
            AccessibleName = "Status"
        };

        this.Controls.Add(lblAirport);
        this.Controls.Add(txtAirport);
        this.Controls.Add(lblRunway);
        this.Controls.Add(cmbRunway);
        this.Controls.Add(lblExit);
        this.Controls.Add(cmbExit);
        this.Controls.Add(btnPlan);
        this.Controls.Add(btnClear);
        this.Controls.Add(lblStatus);

        int t = 0;
        txtAirport.TabIndex = t++;
        cmbRunway.TabIndex = t++;
        cmbExit.TabIndex = t++;
        btnPlan.TabIndex = t++;
        btnClear.TabIndex = t++;

        this.Load += async (s, e) =>
        {
            // async void event handler: wrap body in try/catch so no exception
            // escapes to the UI message pump. If the preset load fails, show the
            // reason in the status label and leave the form usable (user can type
            // another ICAO).
            try
            {
                this.BringToFront();
                this.Activate();

                if (!string.IsNullOrEmpty(_presetIcao))
                {
                    txtAirport.Text = _presetIcao.ToUpperInvariant();
                    await LoadAirportAsync(_presetIcao);

                    // Re-check disposed — form may have been closed during the await.
                    if (IsDisposed || Disposing) return;

                    // Preselect the preset runway if one was provided. Items
                    // are now RunwayChoice wrappers, not raw Runway objects —
                    // unwrap to compare RunwayID.
                    if (_presetRunway != null)
                    {
                        for (int i = 0; i < cmbRunway.Items.Count; i++)
                        {
                            if (cmbRunway.Items[i] is RunwayChoice rc && rc.Runway.RunwayID == _presetRunway.RunwayID)
                            {
                                cmbRunway.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                }
                if (!IsDisposed && !Disposing)
                    txtAirport.Focus();
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                {
                    lblStatus.Text = $"Error: {ex.Message}";
                    btnPlan.Enabled = true;
                }
            }
        };
    }

    // async void event-handler entry points: must not let exceptions escape to the
    // UI message pump (would crash the app). Every async-void path goes through
    // this wrapper so a bad DB query or a cancelled await can't kill the process.
    private async void LoadAirport(string icao)
    {
        try
        {
            await LoadAirportAsync(icao);
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
            {
                lblStatus.Text = $"Error loading {icao}: {ex.Message}";
                btnPlan.Enabled = true;
            }
        }
    }

    private async System.Threading.Tasks.Task LoadAirportAsync(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return;
        if (IsDisposed || Disposing) return;
        if (icao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase) && _graph != null) return;

        _currentIcao = icao.ToUpperInvariant();
        cmbRunway.Items.Clear();
        cmbExit.Items.Clear();
        _exits.Clear();

        if (!_dataProvider.AirportExists(icao))
        {
            lblStatus.Text = $"Airport {icao} not found in database.";
            return;
        }

        var paths = _dataProvider.GetTaxiPaths(icao);
        if (paths.Count == 0)
        {
            lblStatus.Text = $"No taxi data for {icao}.";
            return;
        }

        lblStatus.Text = $"{icao}: building taxi graph…";
        btnPlan.Enabled = false;
        Navigation.TaxiGraph? builtGraph = null;
        try
        {
            var parking = _dataProvider.GetParkingSpots(icao);
            var starts = _dataProvider.GetRunwayStarts(icao);
            builtGraph = await TaxiGraph.BuildAsync(paths, parking, starts);
        }
        finally
        {
            // Do NOT touch UI controls if the form was closed during the await —
            // BuildAsync can take 200–500 ms at large airports and the user may
            // have hit Escape / X in the meantime. Accessing disposed controls
            // throws ObjectDisposedException on the UI thread.
            if (!IsDisposed && !Disposing)
                btnPlan.Enabled = true;
        }

        // Re-check disposed state after the await before touching any controls.
        if (IsDisposed || Disposing) return;

        _graph = builtGraph;

        // Filter against landing-eligible flags. Defaults are permissive so
        // DBs that don't populate IsClosed/IsLanding still show every runway.
        // Closed runways and landing-prohibited directions are dropped — pilot
        // can't pick an exit on a runway they can't legally land on.
        _runways = _dataProvider.GetRunways(icao)
            .Where(r => !r.IsClosed && r.IsLanding)
            .ToList();

        // Wrap each Runway so the combo's ToString returns just the runway ID.
        foreach (var rwy in _runways)
            cmbRunway.Items.Add(new RunwayChoice(rwy));

        lblStatus.Text = $"{icao}: {_runways.Count} runway directions loaded.";

        if (cmbRunway.Items.Count > 0)
            cmbRunway.SelectedIndex = 0;
    }

    private void RepopulateExits()
    {
        cmbExit.Items.Clear();
        _exits.Clear();

        if (_graph == null) return;
        if (cmbRunway.SelectedItem is not RunwayChoice choice) return;
        Runway rwy = choice.Runway;

        _exits = _graph.GetLandingExits(rwy);

        if (_exits.Count == 0)
        {
            // Blind users cannot see lblStatus — announce aloud so they know
            // the empty exit list is a data reality (runway lacks hold-short /
            // ILS hold-short nodes in the user's DB) and NOT a UI bug. Common
            // at newer / renumbered runways when the user's navdatareader DB
            // is older than the sim's scenery.
            string msg = $"No usable exits found on runway {rwy.RunwayID} in the database. Try another runway or update your navdata.";
            lblStatus.Text = msg;
            _announcer.Announce(msg);
            return;
        }

        foreach (var exit in _exits)
            cmbExit.Items.Add(exit);

        cmbExit.SelectedIndex = 0;
        lblStatus.Text = $"{_exits.Count} exit(s) on runway {rwy.RunwayID}.";

        _announcer.Announce(
            $"{_exits.Count} exit option{(_exits.Count == 1 ? "" : "s")} for runway {rwy.RunwayID}.");
    }

    private void OnPlanClicked(object? sender, EventArgs e)
    {
        if (_graph == null)
        {
            _announcer.Announce("No airport loaded. Enter an ICAO first.");
            return;
        }
        if (cmbRunway.SelectedItem is not RunwayChoice choice)
        {
            _announcer.Announce("Select a runway.");
            return;
        }
        Runway rwy = choice.Runway;
        if (cmbExit.SelectedItem is not LandingExit exit)
        {
            _announcer.Announce("Select an exit.");
            return;
        }

        // Pass the actual current air/ground state. _simConnectManager may be
        // null in tests; default to airborne in that case (matches the typical
        // flow — pilot plans an exit during descent). LastKnownOnGround stays
        // null until the first SIM_ON_GROUND sample arrives, in which case
        // also default to airborne (better to arm and let the GS≥40 kt floor
        // reject false touchdowns than to fail to arm at all).
        bool currentlyAirborne = _simConnectManager?.LastKnownOnGround != true;
        _planner.SetExit(_dataProvider, _currentIcao, rwy, exit, _graph, currentlyAirborne);
        lblStatus.Text = $"Plan set: {exit}";
        this.Close();
    }
}
