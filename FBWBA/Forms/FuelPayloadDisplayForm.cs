using FBWBA.Accessibility;
using FBWBA.SimConnect;

namespace FBWBA.Forms;
public partial class FuelPayloadDisplayForm : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private TextBox dataTextBox = null!;
    private Button refreshButton = null!;
    private Button closeButton = null!;
    private Label titleLabel = null!;

    private readonly ScreenReaderAnnouncer _announcer = null!;
    private readonly SimConnectManager _simConnectManager = null!;

    // Store fuel and payload values
    private readonly Dictionary<string, double> _data = new Dictionary<string, double>();

    // A32NX Aircraft Limits (constants)
    private const double MZFW = 64300;  // Max Zero Fuel Weight (kg)
    private const double MTOW = 79000;  // Max Takeoff Weight (kg)
    private const double MLW = 67400;   // Max Landing Weight (kg)
    private const double MAX_FUEL = 19046;  // Max Fuel Capacity (kg)
    private const int MAX_PAX = 174;    // Max Passengers

    private readonly IntPtr previousWindow;

    public FuelPayloadDisplayForm(ScreenReaderAnnouncer announcer, SimConnectManager simConnectManager)
    {
        // Capture the current foreground window (likely the simulator)
        previousWindow = GetForegroundWindow();

        _announcer = announcer;
        _simConnectManager = simConnectManager;
        InitializeComponent();
        SetupAccessibility();

        // Subscribe to SimVar updates
        if (_simConnectManager != null)
        {
            _simConnectManager.SimVarUpdated += OnSimVarUpdated;
        }

        RefreshData(); // Load initial data
    }

    private void InitializeComponent()
    {
        Text = "Fuel & Payload - FlyByWire A32NX";
        Size = new Size(900, 700);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        // Title Label
        titleLabel = new Label
        {
            Text = "Fuel & Payload Information - FlyByWire A32NX",
            Location = new Point(20, 20),
            Size = new Size(500, 20),
            Font = new Font("Microsoft Sans Serif", 10, FontStyle.Bold),
            AccessibleName = "Fuel and Payload Display Title"
        };

        // Data TextBox (read-only, multiline)
        dataTextBox = new TextBox
        {
            Location = new Point(20, 50),
            Size = new Size(840, 550),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Fuel and Payload Data",
            AccessibleDescription = "Display of fuel quantities, passenger counts, cargo weights, and aircraft weights",
            Font = new Font("Consolas", 10, FontStyle.Regular),
            Text = "Loading fuel and payload data..."
        };

        // Refresh Button
        refreshButton = new Button
        {
            Text = "&Refresh (F5)",
            Location = new Point(700, 620),
            Size = new Size(75, 30),
            AccessibleName = "Refresh",
            AccessibleDescription = "Refresh fuel and payload data from simulator"
        };
        refreshButton.Click += RefreshButton_Click;

        // Close Button
        closeButton = new Button
        {
            Text = "&Close",
            Location = new Point(785, 620),
            Size = new Size(75, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close",
            AccessibleDescription = "Close fuel and payload window"
        };
        closeButton.Click += CloseButton_Click;

        // Add controls to form
        Controls.AddRange(new Control[]
        {
            titleLabel, dataTextBox, refreshButton, closeButton
        });

        CancelButton = closeButton;
        KeyPreview = true;
    }

    private void SetupAccessibility()
    {
        // Set tab order for logical navigation
        dataTextBox.TabIndex = 0;
        refreshButton.TabIndex = 1;
        closeButton.TabIndex = 2;

        // Focus and bring window to front when opened
        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false; // Flash to bring to front
            dataTextBox.Focus();
        };
    }

    private void RefreshButton_Click(object? sender, EventArgs e)
    {
        RefreshData();
        _announcer?.Announce("Fuel and payload data refreshed");
    }

    private void CloseButton_Click(object? sender, EventArgs e)
    {
        Close();
    }

    private void RefreshData()
    {
        try
        {
            // Show loading message
            dataTextBox.Text = "Loading fuel and payload data...";

            // Clear existing data
            _data.Clear();

            // Request all fuel and payload variables
            _simConnectManager?.RequestFuelAndPayloadData();

            System.Diagnostics.Debug.WriteLine("[FuelPayloadDisplayForm] Data requested, waiting for response");
        }
        catch (Exception ex)
        {
            dataTextBox.Text = $"Error loading data: {ex.Message}";
        }
    }

    private int CountPassengerBits(double bitflagsValue)
    {
        int count = 0;
        long value = (long)bitflagsValue;
        while (value > 0)
        {
            count += (int)(value & 1);
            value >>= 1;
        }
        return count;
    }

    private string FormatData()
    {
        var output = new System.Text.StringBuilder();

        // ===== FUEL SECTION =====
        output.AppendLine("═══════════════════════════════════════════════════════════════");
        output.AppendLine("                            FUEL                               ");
        output.AppendLine("═══════════════════════════════════════════════════════════════");
        output.AppendLine();

        // Get fuel weight per gallon conversion
        double galToKg = _data.ContainsKey("FUEL_WEIGHT_PER_GALLON") ? _data["FUEL_WEIGHT_PER_GALLON"] : 3.039;

        // Individual tanks
        double leftOuterGal = _data.ContainsKey("FUEL_LEFT_AUX") ? _data["FUEL_LEFT_AUX"] : 0;
        double leftInnerGal = _data.ContainsKey("FUEL_LEFT_MAIN") ? _data["FUEL_LEFT_MAIN"] : 0;
        double centerGal = _data.ContainsKey("FUEL_CENTER") ? _data["FUEL_CENTER"] : 0;
        double rightInnerGal = _data.ContainsKey("FUEL_RIGHT_MAIN") ? _data["FUEL_RIGHT_MAIN"] : 0;
        double rightOuterGal = _data.ContainsKey("FUEL_RIGHT_AUX") ? _data["FUEL_RIGHT_AUX"] : 0;

        // Convert to kg
        double leftOuterKg = Math.Round(leftOuterGal * galToKg);
        double leftInnerKg = Math.Round(leftInnerGal * galToKg);
        double centerKg = Math.Round(centerGal * galToKg);
        double rightInnerKg = Math.Round(rightInnerGal * galToKg);
        double rightOuterKg = Math.Round(rightOuterGal * galToKg);
        double totalFuelKg = leftOuterKg + leftInnerKg + centerKg + rightInnerKg + rightOuterKg;

        output.AppendLine($"  Left Outer Tank:      {leftOuterKg,8:F0} kg  ({leftOuterGal,7:F0} gal)");
        output.AppendLine($"  Left Inner Tank:      {leftInnerKg,8:F0} kg  ({leftInnerGal,7:F0} gal)");
        output.AppendLine($"  Center Tank:          {centerKg,8:F0} kg  ({centerGal,7:F0} gal)");
        output.AppendLine($"  Right Inner Tank:     {rightInnerKg,8:F0} kg  ({rightInnerGal,7:F0} gal)");
        output.AppendLine($"  Right Outer Tank:     {rightOuterKg,8:F0} kg  ({rightOuterGal,7:F0} gal)");
        output.AppendLine("  ───────────────────────────────────────────────────────────");
        output.AppendLine($"  TOTAL FUEL:           {totalFuelKg,8:F0} kg");
        output.AppendLine();
        output.AppendLine($"  Fuel Density:         {galToKg:F3} kg/gal");
        output.AppendLine();

        // ===== PASSENGERS SECTION =====
        output.AppendLine("═══════════════════════════════════════════════════════════════");
        output.AppendLine("                          PASSENGERS                           ");
        output.AppendLine("═══════════════════════════════════════════════════════════════");
        output.AppendLine();

        double paxWeight = _data.ContainsKey("PAX_WEIGHT") ? _data["PAX_WEIGHT"] : 84;
        double bagWeight = _data.ContainsKey("BAG_WEIGHT") ? _data["BAG_WEIGHT"] : 20;

        int paxA = _data.ContainsKey("PAX_A") ? CountPassengerBits(_data["PAX_A"]) : 0;
        int paxB = _data.ContainsKey("PAX_B") ? CountPassengerBits(_data["PAX_B"]) : 0;
        int paxC = _data.ContainsKey("PAX_C") ? CountPassengerBits(_data["PAX_C"]) : 0;
        int paxD = _data.ContainsKey("PAX_D") ? CountPassengerBits(_data["PAX_D"]) : 0;
        int totalPax = paxA + paxB + paxC + paxD;
        double totalPaxWeight = totalPax * paxWeight;
        double totalBagWeight = totalPax * bagWeight;

        output.AppendLine($"  Rows 1-6:             {paxA,3} passengers");
        output.AppendLine($"  Rows 7-13:            {paxB,3} passengers");
        output.AppendLine($"  Rows 14-21:           {paxC,3} passengers");
        output.AppendLine($"  Rows 22-29:           {paxD,3} passengers");
        output.AppendLine("  ───────────────────────────────────────────────────────────");

        // Get FMS passenger count
        int fmsPax = _data.ContainsKey("FMS_PAX") ? (int)_data["FMS_PAX"] : 0;
        string fmsPaxDisplay = fmsPax > 0 ? $"{fmsPax}" : "Not Entered";

        output.AppendLine($"  TOTAL PASSENGERS:     {totalPax,3}  (FMS: {fmsPaxDisplay})");
        output.AppendLine();
        output.AppendLine($"  Weight per Passenger: {paxWeight:F0} kg");
        output.AppendLine($"  Weight per Bag:       {bagWeight:F0} kg");
        output.AppendLine($"  Total Pax Weight:     {totalPaxWeight:F0} kg");
        output.AppendLine($"  Total Bag Weight:     {totalBagWeight:F0} kg");
        output.AppendLine();

        // ===== CARGO SECTION =====
        output.AppendLine("═══════════════════════════════════════════════════════════════");
        output.AppendLine("                            CARGO                              ");
        output.AppendLine("═══════════════════════════════════════════════════════════════");
        output.AppendLine();

        double fwdBaggage = _data.ContainsKey("CARGO_FWD") ? _data["CARGO_FWD"] : 0;
        double aftContainer = _data.ContainsKey("CARGO_AFT_CONT") ? _data["CARGO_AFT_CONT"] : 0;
        double aftBaggage = _data.ContainsKey("CARGO_AFT_BAG") ? _data["CARGO_AFT_BAG"] : 0;
        double aftBulk = _data.ContainsKey("CARGO_AFT_BULK") ? _data["CARGO_AFT_BULK"] : 0;
        double totalCargo = fwdBaggage + aftContainer + aftBaggage + aftBulk;

        output.AppendLine($"  Forward Baggage:      {fwdBaggage,8:F0} kg");
        output.AppendLine($"  Aft Container:        {aftContainer,8:F0} kg");
        output.AppendLine($"  Aft Baggage:          {aftBaggage,8:F0} kg");
        output.AppendLine($"  Aft Bulk:             {aftBulk,8:F0} kg");
        output.AppendLine("  ───────────────────────────────────────────────────────────");
        output.AppendLine($"  TOTAL CARGO:          {totalCargo,8:F0} kg");
        output.AppendLine();

        // ===== WEIGHTS & BALANCE SECTION =====
        output.AppendLine("═══════════════════════════════════════════════════════════════");
        output.AppendLine("                      WEIGHTS & BALANCE                        ");
        output.AppendLine("═══════════════════════════════════════════════════════════════");
        output.AppendLine();

        double emptyWeight = _data.ContainsKey("EMPTY_WEIGHT") ? _data["EMPTY_WEIGHT"] : 0;
        double zfw = _data.ContainsKey("ZFW") ? _data["ZFW"] : 0;
        double gw = _data.ContainsKey("GW") ? _data["GW"] : 0;
        double zfwCgMac = _data.ContainsKey("ZFW_CG_MAC") ? _data["ZFW_CG_MAC"] : 0;
        double gwCgMac = _data.ContainsKey("GW_CG_MAC") ? _data["GW_CG_MAC"] : 0;

        // Get FMS values
        double fmsZfw = _data.ContainsKey("FMS_ZFW") ? _data["FMS_ZFW"] : 0;
        double fmsGw = _data.ContainsKey("FMS_GW") ? _data["FMS_GW"] : 0;
        double fmsCg = _data.ContainsKey("FMS_CG") ? _data["FMS_CG"] : 0;

        // Format FMS values for display
        string fmsZfwDisplay = fmsZfw > 0 ? $"{fmsZfw,8:F0} kg" : "  Not Entered";
        string fmsGwDisplay = fmsGw > 0 ? $"{fmsGw,8:F0} kg" : "  Not Entered";
        string fmsCgDisplay = fmsCg > 0 ? $"{fmsCg,7:F2}% MAC" : "Not Entered";

        output.AppendLine("                        ACTUAL            FMS");
        output.AppendLine("  ───────────────────────────────────────────────────────────");
        output.AppendLine($"  Empty Weight:         {emptyWeight,8:F0} kg");
        output.AppendLine($"  Zero Fuel Weight:     {zfw,8:F0} kg    {fmsZfwDisplay}");
        output.AppendLine($"  Gross Weight:         {gw,8:F0} kg    {fmsGwDisplay}");
        output.AppendLine();
        output.AppendLine($"  CG at ZFW:            {zfwCgMac,7:F2}% MAC  {fmsCgDisplay}");
        output.AppendLine($"  CG at GW:             {gwCgMac,7:F2}% MAC");
        output.AppendLine();

        // ===== LIMITS & MARGINS SECTION =====
        output.AppendLine("═══════════════════════════════════════════════════════════════");
        output.AppendLine("                      LIMITS & MARGINS                         ");
        output.AppendLine("═══════════════════════════════════════════════════════════════");
        output.AppendLine();

        // WEIGHT LIMITS
        output.AppendLine("WEIGHT LIMITS");
        output.AppendLine();

        // ZFW Margin
        double zfwMargin = MZFW - zfw;
        string zfwIndicator = zfwMargin >= 0 ? "✓" : "⚠ OVERWEIGHT";
        output.AppendLine($"  Max Zero Fuel Weight (MZFW):    {MZFW,8:F0} kg");
        output.AppendLine($"  Current ZFW:                    {zfw,8:F0} kg");
        output.AppendLine($"  ZFW Margin:                     {zfwMargin,8:F0} kg  {zfwIndicator}");
        output.AppendLine();

        // MTOW Margin
        double mtowMargin = MTOW - gw;
        string mtowIndicator = mtowMargin >= 0 ? "✓" : "⚠ OVERWEIGHT";
        output.AppendLine($"  Max Takeoff Weight (MTOW):      {MTOW,8:F0} kg");
        output.AppendLine($"  Current GW:                     {gw,8:F0} kg");
        output.AppendLine($"  MTOW Margin:                    {mtowMargin,8:F0} kg  {mtowIndicator}");
        output.AppendLine();

        // MLW Check
        output.AppendLine($"  Max Landing Weight (MLW):       {MLW,8:F0} kg");
        output.AppendLine($"  Current GW:                     {gw,8:F0} kg");
        if (gw > MLW)
        {
            double fuelToBurn = gw - MLW;
            output.AppendLine($"  Fuel to burn before landing:    {fuelToBurn,8:F0} kg  ⚠");
        }
        else
        {
            double mlwMargin = MLW - gw;
            output.AppendLine($"  MLW Margin:                     {mlwMargin,8:F0} kg  ✓");
        }
        output.AppendLine();

        // FUEL CAPACITY
        output.AppendLine("FUEL CAPACITY");
        output.AppendLine();
        double fuelRemaining = MAX_FUEL - totalFuelKg;
        output.AppendLine($"  Max Fuel Capacity:              {MAX_FUEL,8:F0} kg");
        output.AppendLine($"  Current Fuel:                   {totalFuelKg,8:F0} kg");
        output.AppendLine($"  Remaining Capacity:             {fuelRemaining,8:F0} kg");
        output.AppendLine();

        // PASSENGER CAPACITY
        output.AppendLine("PASSENGER CAPACITY");
        output.AppendLine();
        int emptySeats = MAX_PAX - totalPax;
        output.AppendLine($"  Max Passengers:                 {MAX_PAX,8}");
        output.AppendLine($"  Current Passengers:             {totalPax,8}");
        output.AppendLine($"  Empty Seats:                    {emptySeats,8}");
        output.AppendLine();

        output.AppendLine("═══════════════════════════════════════════════════════════════");
        output.AppendLine();
        output.AppendLine("Press F5 to refresh, Press ESC to close");

        return output.ToString();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Handle F5 key for refresh
        if (keyData == Keys.F5)
        {
            RefreshData();
            _announcer?.Announce("Fuel and payload data refreshed");
            return true;
        }

        // Handle Escape key
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    private void OnSimVarUpdated(object? sender, SimVarUpdateEventArgs e)
    {
        // Check if this is a fuel/payload variable we're tracking
        if (e.VarName.StartsWith("FUEL_") || e.VarName.StartsWith("PAX_") ||
            e.VarName.StartsWith("CARGO_") || e.VarName.StartsWith("FMS_") ||
            e.VarName == "EMPTY_WEIGHT" || e.VarName == "ZFW" || e.VarName == "GW" ||
            e.VarName == "ZFW_CG_MAC" || e.VarName == "GW_CG_MAC" ||
            e.VarName == "PAX_WEIGHT" || e.VarName == "BAG_WEIGHT")
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnSimVarUpdated(sender, e)));
                return;
            }

            // Store the value
            _data[e.VarName] = e.Value;

            // Update display
            UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        // Update the display with formatted data
        dataTextBox.Text = FormatData();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Unsubscribe from events
        if (_simConnectManager != null)
        {
            _simConnectManager.SimVarUpdated -= OnSimVarUpdated;
        }
        base.OnFormClosed(e);

        // Restore focus to the previous window (likely the simulator)
        if (previousWindow != IntPtr.Zero)
        {
            SetForegroundWindow(previousWindow);
        }
    }
}
