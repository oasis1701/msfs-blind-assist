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

    // The augmentation decorator, when the app wired one up (null when the feature is
    // off or the provider isn't decorated — tests pass a bare provider). Held typed so
    // the load path can AWAIT its fetch and so a late fetch can refresh the exit list.
    private readonly Services.TaxiAugment.AugmentingAirportDataProvider? _augProvider;
    private Action<string>? _augUpdatedHandler;
    // Re-entrancy latch for the late-augmentation refresh: the fetch can raise
    // AirportDataUpdated more than once (one per source) and each refresh awaits a
    // graph build, so two pushes could otherwise interleave two builds onto _graph.
    private bool _refreshingAfterAugment;

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
        _augProvider = dataProvider as Services.TaxiAugment.AugmentingAirportDataProvider;
        InitializeFormControls();

        // A fetch slower than the bounded wait in LoadAirportAsync still lands eventually.
        // Without this the exit list would stay on the navdata-only reading for the whole
        // life of the form (LoadAirportAsync early-returns on the same ICAO once _graph is
        // built), which at an airport whose navdata names nothing — LSZH names 0 of 1407
        // taxi segments — is the difference between 0-2 exits per runway and 3-9.
        if (_augProvider != null)
        {
            _augUpdatedHandler = OnAirportDataUpdated;
            _augProvider.AirportDataUpdated += _augUpdatedHandler;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _augProvider != null && _augUpdatedHandler != null)
        {
            _augProvider.AirportDataUpdated -= _augUpdatedHandler;
            _augUpdatedHandler = null;
        }
        base.Dispose(disposing);
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
        // Drop the old graph up front: every exit below (airport absent, no taxi data,
        // a superseded load) must leave NO graph standing rather than the previous
        // airport's under the new ICAO.
        _graph = null;
        cmbRunway.Items.Clear();
        cmbExit.Items.Clear();
        _exits.Clear();

        if (!_dataProvider.AirportExists(icao))
        {
            lblStatus.Text = $"Airport {icao} not found in database.";
            return;
        }

        // Fetch online taxiway-name augmentation BEFORE building the graph, exactly as
        // TaxiAssistForm does. GetLandingExits only ever returns a node whose edges carry a
        // taxiway NAME, so an airport whose navdata names nothing yields almost no exits until
        // the merge has landed (LSZH: 0 of 1407 segments named → 0-2 exits per runway raw,
        // 3-9 once merged). MainForm's prefetch is fire-and-forget and only runs for an ILS
        // destination that hasn't been prefetched this session, so a typed ICAO reached here
        // with nothing fetched at all — and the graph never rebuilds for the same airport
        // (early return above), so the pilot saw the navdata-only list for the whole session
        // of the form.
        if (_augProvider != null && _augProvider.Enabled)
        {
            lblStatus.Text = $"{icao}: fetching taxiway names…";
            // BOUND the wait, same reasoning as TaxiAssistForm: a cache hit returns instantly,
            // and a slow Overpass mirror must never hold the planner shut. If the fetch hasn't
            // landed we build from navdata now; the shared in-flight task keeps running and
            // OnAirportDataUpdated refreshes the list when it does.
            const int prefetchWaitMs = 8000;
            try
            {
                var prefetch = _augProvider.PrefetchAsync(icao);
                await System.Threading.Tasks.Task.WhenAny(
                    prefetch, System.Threading.Tasks.Task.Delay(prefetchWaitMs));
            }
            catch { /* offline / fetch failed — fall back to navdata names */ }

            if (IsDisposed || Disposing) return;
            // The pilot can retype during a wait this long (a cache hit for the second
            // airport finishes first), so anything below would populate THIS airport's
            // data under the newer ICAO — same re-check RefreshAfterAugmentation makes.
            if (!icao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase)) return;
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

        // Re-check disposed state after the await before touching any controls, and that
        // this load is still the current one — a superseded load must not append its
        // runways to the newer airport's combo or hand it a mismatched graph.
        if (IsDisposed || Disposing) return;
        if (!icao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase)) return;

        _graph = builtGraph;

        // Only exclude closed runways. IsLanding=false just means no published
        // instrument approach in the navdata — ATC can still assign the runway
        // for landing (as at EIDW 10R), so let the pilot pick any open direction.
        _runways = _dataProvider.GetRunways(icao)
            .Where(r => !r.IsClosed)
            .ToList();

        // Wrap each Runway so the combo's ToString returns just the runway ID.
        foreach (var rwy in _runways)
            cmbRunway.Items.Add(new RunwayChoice(rwy));

        lblStatus.Text = $"{icao}: {_runways.Count} runway directions loaded.";

        if (cmbRunway.Items.Count > 0)
            cmbRunway.SelectedIndex = 0;
    }

    /// <param name="announce">
    /// False when the caller composes its own announcement (the late-augmentation refresh
    /// says WHY the list changed; a bare "9 exit options" arriving unprompted would not).
    /// </param>
    private void RepopulateExits(bool announce = true)
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
            if (announce) _announcer.Announce(msg);
            return;
        }

        // Flag exits the rollout handoff would not be able to get clear of the runway.
        // Same resolution the handoff runs (LandingExitDestination), so the pre-flight
        // warning and the in-flight behaviour cannot disagree. At a few airports the
        // navdata maps no taxiway past the junction at all — better to learn that while
        // choosing than at 60 knots on the rollout.
        int unusable = 0;
        foreach (var exit in _exits)
        {
            Navigation.LandingExitDestination.Resolve(
                _graph, exit, _exits, rwy, rwy.Heading,
                out _, out double endLateralM, out _);
            exit.VacatesRunway = Navigation.RunwayVacateResolver.IsOffPavement(endLateralM, rwy);
            if (!exit.VacatesRunway) unusable++;
        }

        foreach (var exit in _exits)
            cmbExit.Items.Add(exit);

        // Select a usable exit by default rather than blindly the first one — the
        // flagged ones stay in the list (at some airports they are ALL that exists, so
        // hiding them would leave nothing to pick), they just aren't the default.
        int firstUsable = _exits.FindIndex(e => e.VacatesRunway);
        cmbExit.SelectedIndex = firstUsable >= 0 ? firstUsable : 0;

        string warnSuffix = unusable > 0
            ? $" {unusable} of them ha{(unusable == 1 ? "s" : "ve")} no taxiway mapped clear of the runway."
            : "";
        lblStatus.Text = $"{_exits.Count} exit(s) on runway {rwy.RunwayID}.{warnSuffix}";

        if (announce)
            _announcer.Announce(
                $"{_exits.Count} exit option{(_exits.Count == 1 ? "" : "s")} for runway {rwy.RunwayID}.{warnSuffix}");
    }

    /// <summary>
    /// Raised by the augmentation decorator on a background thread when an online fetch for
    /// an airport completes. Marshals to the UI thread; ignores every airport but the one on
    /// screen.
    /// </summary>
    private void OnAirportDataUpdated(string icao)
    {
        if (string.IsNullOrEmpty(icao)) return;
        if (!icao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase)) return;
        SafeBeginInvoke(() => RefreshAfterAugmentation(icao));
    }

    // ObjectDisposedException derives from InvalidOperationException, so one catch covers both.
    // The bare IsHandleCreated check alone races a concurrent handle-destroy (the pilot closing
    // the planner while the fetch lands); without the guard the throw would be unobserved on the
    // marshalling thread.
    private void SafeBeginInvoke(Action action)
    {
        try { if (IsHandleCreated) BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Rebuilds the graph and the exit list after a late augmentation fetch. Keeps the pilot's
    /// current exit selection by NAME when that taxiway survives, and speaks only when the
    /// option count actually changed — a silent list that grew is the failure this fixes, but
    /// an unprompted recital of an unchanged list is noise.
    /// </summary>
    private async void RefreshAfterAugmentation(string icao)
    {
        try
        {
            if (IsDisposed || Disposing) return;
            if (!icao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase)) return;
            if (_graph == null) return;               // nothing built yet; the load path will do it
            if (_refreshingAfterAugment) return;
            _refreshingAfterAugment = true;
            try
            {
                var paths = _dataProvider.GetTaxiPaths(icao);
                if (paths.Count == 0) return;
                var parking = _dataProvider.GetParkingSpots(icao);
                var starts = _dataProvider.GetRunwayStarts(icao);
                var rebuilt = await TaxiGraph.BuildAsync(paths, parking, starts);

                // The form can be closed, or the pilot can have typed another ICAO, during the
                // build — re-check both before touching _graph or any control.
                if (IsDisposed || Disposing) return;
                if (!icao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase)) return;

                _graph = rebuilt;

                int before = _exits.Count;
                string? selectedName = (cmbExit.SelectedItem as LandingExit)?.TaxiwayName;

                RepopulateExits(announce: false);

                // Restore the pilot's pick when that taxiway is still offered. Matching by name
                // (not index) because the merge can insert exits ahead of it in the list.
                if (!string.IsNullOrEmpty(selectedName))
                {
                    int idx = _exits.FindIndex(e =>
                        string.Equals(e.TaxiwayName, selectedName, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) cmbExit.SelectedIndex = idx;
                }

                if (_exits.Count != before && cmbRunway.SelectedItem is RunwayChoice choice)
                    _announcer.Announce(
                        $"Taxiway names updated for {icao}. Now {_exits.Count} exit" +
                        $"{(_exits.Count == 1 ? "" : "s")} for runway {choice.Runway.RunwayID}.");
            }
            finally
            {
                _refreshingAfterAugment = false;
            }
        }
        catch (Exception ex)
        {
            // async void: nothing may escape to the UI message pump.
            if (!IsDisposed && !Disposing)
                lblStatus.Text = $"Could not refresh exits for {icao}: {ex.Message}";
        }
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
