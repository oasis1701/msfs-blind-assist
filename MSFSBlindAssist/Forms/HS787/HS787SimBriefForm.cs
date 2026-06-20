using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.HS787;

/// <summary>
/// Accessible SimBrief fetch + fuel-loading form for the HorizonSim 787-9.
/// Replaces the broken DOM-scraping EFB form. Fetches an OFP from SimBrief,
/// displays key flight details, and sets fuel quantities via SimConnect.
/// </summary>
public partial class HS787SimBriefForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly SimConnectManager _simConnect;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimBriefService _simBriefService = new();

    private SimBriefOFP? _ofp;
    private IntPtr _previousWindow = IntPtr.Zero;
    private bool _disposed;
    private bool _fetching;

    public HS787SimBriefForm(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _simConnect = simConnect;
        _announcer = announcer;

        InitializeComponent();

        // Pre-fill pilot ID from shared SimBrief settings
        pilotIdBox.Text = SettingsManager.Current.SimbriefUsername ?? "";

        fetchButton.Click  += FetchButton_Click;
        loadFuelButton.Click += LoadFuelButton_Click;
        openFmcButton.Click  += OpenFmcButton_Click;

        FormClosing += (s, e) =>
        {
            e.Cancel = true;
            Hide();
            if (_previousWindow != IntPtr.Zero)
                SetForegroundWindow(_previousWindow);
        };
    }

    // ------------------------------------------------------------------
    // Show / focus
    // ------------------------------------------------------------------

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;

        if (string.IsNullOrWhiteSpace(pilotIdBox.Text))
            pilotIdBox.Focus();
        else
            fetchButton.Focus();
    }

    // ------------------------------------------------------------------
    // Fetch
    // ------------------------------------------------------------------

    private async void FetchButton_Click(object? sender, EventArgs e)
    {
        if (_fetching) return;

        string username = pilotIdBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            SetStatus("Please enter your SimBrief Pilot ID first.");
            pilotIdBox.Focus();
            return;
        }

        // Persist pilot ID to shared settings
        SettingsManager.Current.SimbriefUsername = username;
        SettingsManager.Save();

        _fetching = true;
        fetchButton.Enabled = false;
        loadFuelButton.Enabled = false;
        SetStatus("Fetching SimBrief flight plan…");
        flightInfoList.Items.Clear();
        _ofp = null;

        // 30 s UI-side timeout, matching DashboardPanel.cs:262.
        // Note: this does not cancel the underlying HTTP request — the HttpClient
        // default timeout (100 s) will eventually fail it. We unblock the UI so
        // the user can retry without waiting on the slow fetch.
        var fetchTask = _simBriefService.FetchFullOFPAsync(username);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
        var winner = await Task.WhenAny(fetchTask, timeoutTask);

        try
        {
            if (winner == timeoutTask)
            {
                SetStatus("Fetch timed out — try again.");
                _announcer.Announce("SimBrief fetch timed out.");
                return;
            }

            _ofp = await fetchTask;
            PopulateFlightInfo(_ofp);
            loadFuelButton.Enabled = true;
            SetStatus("Flight plan loaded. Use Load Fuel to set fuel, or Open FMC Init to program the FMC.");
            _announcer.Announce($"SimBrief loaded. {_ofp.OriginIcao} to {_ofp.DestIcao}.");
            flightInfoList.Focus();
        }
        catch (Exception ex)
        {
            SetStatus($"Fetch failed: {ex.Message}");
            _announcer.Announce("SimBrief fetch failed.");
        }
        finally
        {
            _fetching = false;
            fetchButton.Enabled = true;
        }
    }

    private void PopulateFlightInfo(SimBriefOFP ofp)
    {
        flightInfoList.BeginUpdate();
        flightInfoList.Items.Clear();

        void Add(string line) => flightInfoList.Items.Add(line);

        string flt = string.IsNullOrWhiteSpace(ofp.FlightNumber) ? "" : $"  {ofp.Callsign}";
        Add($"Route: {ofp.OriginIcao} → {ofp.DestIcao}{flt}");
        if (!string.IsNullOrWhiteSpace(ofp.AltnIcao))
            Add($"Alternate: {ofp.AltnIcao}");
        if (!string.IsNullOrWhiteSpace(ofp.OriginRunway))
            Add($"Departure runway: {ofp.OriginRunway}");
        if (!string.IsNullOrWhiteSpace(ofp.OriginSid))
            Add($"SID: {ofp.OriginSid}");
        if (!string.IsNullOrWhiteSpace(ofp.DestRunway))
            Add($"Arrival runway: {ofp.DestRunway}");
        if (!string.IsNullOrWhiteSpace(ofp.DestStar))
            Add($"STAR: {ofp.DestStar}");
        if (!string.IsNullOrWhiteSpace(ofp.DestApproach))
            Add($"Approach: {ofp.DestApproach}");
        if (!string.IsNullOrWhiteSpace(ofp.InitialAltitude))
            Add($"Initial altitude: {ofp.InitialAltitude}");
        if (!string.IsNullOrWhiteSpace(ofp.CostIndex))
            Add($"Cost index: {ofp.CostIndex}");
        if (!string.IsNullOrWhiteSpace(ofp.CruiseMach))
            Add($"Cruise Mach: {ofp.CruiseMach}");
        if (!string.IsNullOrWhiteSpace(ofp.AirTime))
            Add($"Est. flight time: {ofp.AirTime}");

        string units = ofp.Units.ToUpperInvariant().Contains("KG") ? "kg" : "lbs";
        if (!string.IsNullOrWhiteSpace(ofp.FuelBlockRamp))
            Add($"Fuel block / ramp: {ofp.FuelBlockRamp} {units}");
        if (!string.IsNullOrWhiteSpace(ofp.FuelTrip))
            Add($"Trip fuel: {ofp.FuelTrip} {units}");
        if (!string.IsNullOrWhiteSpace(ofp.WeightZfw))
            Add($"ZFW: {ofp.WeightZfw} {units}");
        if (!string.IsNullOrWhiteSpace(ofp.WeightTow))
            Add($"TOW: {ofp.WeightTow} {units}");
        if (!string.IsNullOrWhiteSpace(ofp.Passengers))
            Add($"Passengers: {ofp.Passengers}");
        if (!string.IsNullOrWhiteSpace(ofp.TakeoffV1))
            Add($"V1: {ofp.TakeoffV1}  VR: {ofp.TakeoffVr}  V2: {ofp.TakeoffV2}");
        if (!string.IsNullOrWhiteSpace(ofp.TakeoffFlaps))
            Add($"Takeoff flaps: {ofp.TakeoffFlaps}");

        flightInfoList.EndUpdate();
        if (flightInfoList.Items.Count > 0)
            flightInfoList.SelectedIndex = 0;
    }

    // ------------------------------------------------------------------
    // Load fuel
    // ------------------------------------------------------------------

    private void LoadFuelButton_Click(object? sender, EventArgs e)
    {
        if (_ofp == null) return;

        if (!_simConnect.IsConnected)
        {
            SetStatus("Not connected to the simulator.");
            _announcer.Announce("Not connected to simulator.");
            return;
        }

        if (!double.TryParse(_ofp.FuelBlockRamp, out double fuelAmount) || fuelAmount <= 0)
        {
            SetStatus("Fuel block/ramp value not available in this flight plan.");
            return;
        }

        // Convert to pounds
        double fuelLbs;
        bool isKg = _ofp.Units.ToUpperInvariant().Contains("KG");
        fuelLbs = isKg ? fuelAmount * 2.20462 : fuelAmount;

        // Get fuel weight per gallon from cache; fall back to JetA default (~6.7 lb/gal)
        double wtPerGal = _simConnect.GetCachedVariableValue("HS787_FuelWtPerGal") ?? 6.7;
        if (wtPerGal < 1) wtPerGal = 6.7;

        double totalGallons = fuelLbs / wtPerGal;

        // Distribute evenly across three tanks. The 787-9 wing and center tanks
        // are roughly equal capacity (~14,000 gal each), so thirds is a safe split.
        double perTank = Math.Round(totalGallons / 3.0, 1);

        _simConnect.ExecuteCalculatorCode($"{perTank:F1} (>A:FUEL TANK LEFT MAIN QUANTITY, gallons)");
        _simConnect.ExecuteCalculatorCode($"{perTank:F1} (>A:FUEL TANK RIGHT MAIN QUANTITY, gallons)");
        _simConnect.ExecuteCalculatorCode($"{perTank:F1} (>A:FUEL TANK CENTER QUANTITY, gallons)");

        int totalKg = (int)Math.Round(fuelLbs / 2.20462);
        SetStatus($"Fuel set: {totalKg} kg total ({(int)perTank} gal per tank). Verify on FUEL page.");
        _announcer.Announce($"Fuel loaded. {totalKg} kilograms total.");
    }

    // ------------------------------------------------------------------
    // Open FMC Init
    // ------------------------------------------------------------------

    private void OpenFmcButton_Click(object? sender, EventArgs e)
    {
        // The 787 FMC/CDU is now read and driven through the Coherent debugger
        // (open it with Shift+M); the retired HTTP bridge no longer exists.
        SetStatus("Open the 787 CDU with Shift+M to reach the FMC.");
    }

    // ------------------------------------------------------------------
    // Keyboard shortcuts
    // ------------------------------------------------------------------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Alt | Keys.F:
                fetchButton.PerformClick();
                return true;
            case Keys.Alt | Keys.L:
                if (loadFuelButton.Enabled) loadFuelButton.PerformClick();
                return true;
            case Keys.Alt | Keys.I:
                openFmcButton.PerformClick();
                return true;
            case Keys.Escape:
                Hide();
                if (_previousWindow != IntPtr.Zero)
                    SetForegroundWindow(_previousWindow);
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void SetStatus(string text)
    {
        if (IsHandleCreated)
            statusLabel.Text = text;
    }

    // ------------------------------------------------------------------
    // Dispose
    // ------------------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
            _disposed = true;
        base.Dispose(disposing);
    }
}
