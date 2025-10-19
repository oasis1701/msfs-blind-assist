using FBWBA.Accessibility;
using FBWBA.Controls;
using FBWBA.SimConnect;

namespace FBWBA.Forms;
public partial class FuelPayloadDisplayForm : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private AccessibleTabControl mainTabControl = null!;

    // Tab controls
    private TextBox fuelTextBox = null!;
    private TextBox passengersTextBox = null!;
    private TextBox payloadTextBox = null!;
    private TextBox weightBalanceTextBox = null!;

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
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(700, 500);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        // Main tab control
        mainTabControl = new AccessibleTabControl
        {
            Dock = DockStyle.Fill,
            AccessibleName = ""
        };

        // Create tabs
        CreateFuelTab();
        CreatePassengersTab();
        CreatePayloadTab();
        CreateWeightBalanceTab();

        Controls.Add(mainTabControl);

        KeyPreview = true;
    }

    private void SetupAccessibility()
    {
        // Focus and bring window to front when opened
        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false; // Flash to bring to front
            fuelTextBox.Focus();

            // Announce the window and current tab to screen reader
            string currentTabName = mainTabControl.SelectedTab?.Text ?? "Fuel";
            _announcer?.AnnounceImmediate($"Fuel and Payload Window, {currentTabName} tab");
        };
    }

    private void CreateFuelTab()
    {
        var fuelTab = new TabPage("Fuel")
        {
            AccessibleName = "Fuel",
            AccessibleDescription = "Fuel quantities and capacity information"
        };

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

        // Data TextBox
        fuelTextBox = new TextBox
        {
            Location = new Point(20, 20),
            Size = new Size(840, 520),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Fuel Data",
            AccessibleDescription = "Fuel tank quantities, total fuel, and fuel capacity",
            Font = new Font("Consolas", 10, FontStyle.Regular),
            Text = "Loading fuel data...",
            TabIndex = 0
        };

        // Button panel at bottom
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(5)
        };

        var closeButton = new Button
        {
            Text = "&Close",
            Width = 75,
            Height = 30,
            AccessibleName = "Close",
            AccessibleDescription = "Close fuel and payload window",
            TabIndex = 2
        };
        closeButton.Click += (s, e) => Close();

        var refreshButton = new Button
        {
            Text = "&Refresh (F5)",
            Width = 100,
            Height = 30,
            AccessibleName = "Refresh",
            AccessibleDescription = "Refresh fuel and payload data from simulator",
            TabIndex = 1
        };
        refreshButton.Click += (s, e) =>
        {
            RefreshData();
            _announcer?.Announce("Fuel and payload data refreshed");
        };

        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(refreshButton);

        panel.Controls.Add(fuelTextBox);
        panel.Controls.Add(buttonPanel);

        fuelTab.Controls.Add(panel);
        mainTabControl.TabPages.Add(fuelTab);
    }

    private void CreatePassengersTab()
    {
        var passengersTab = new TabPage("Passengers")
        {
            AccessibleName = "Passengers",
            AccessibleDescription = "Passenger counts and weights"
        };

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

        // Data TextBox
        passengersTextBox = new TextBox
        {
            Location = new Point(20, 20),
            Size = new Size(840, 520),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Passengers Data",
            AccessibleDescription = "Passenger counts by rows, total passengers, and weights",
            Font = new Font("Consolas", 10, FontStyle.Regular),
            Text = "Loading passengers data...",
            TabIndex = 0
        };

        // Button panel at bottom
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(5)
        };

        var closeButton = new Button
        {
            Text = "&Close",
            Width = 75,
            Height = 30,
            AccessibleName = "Close",
            AccessibleDescription = "Close fuel and payload window",
            TabIndex = 2
        };
        closeButton.Click += (s, e) => Close();

        var refreshButton = new Button
        {
            Text = "&Refresh (F5)",
            Width = 100,
            Height = 30,
            AccessibleName = "Refresh",
            AccessibleDescription = "Refresh fuel and payload data from simulator",
            TabIndex = 1
        };
        refreshButton.Click += (s, e) =>
        {
            RefreshData();
            _announcer?.Announce("Fuel and payload data refreshed");
        };

        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(refreshButton);

        panel.Controls.Add(passengersTextBox);
        panel.Controls.Add(buttonPanel);

        passengersTab.Controls.Add(panel);
        mainTabControl.TabPages.Add(passengersTab);
    }

    private void CreatePayloadTab()
    {
        var payloadTab = new TabPage("Payload")
        {
            AccessibleName = "Payload",
            AccessibleDescription = "Cargo compartment weights"
        };

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

        // Data TextBox
        payloadTextBox = new TextBox
        {
            Location = new Point(20, 20),
            Size = new Size(840, 520),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Payload Data",
            AccessibleDescription = "Cargo compartment weights and total cargo",
            Font = new Font("Consolas", 10, FontStyle.Regular),
            Text = "Loading payload data...",
            TabIndex = 0
        };

        // Button panel at bottom
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(5)
        };

        var closeButton = new Button
        {
            Text = "&Close",
            Width = 75,
            Height = 30,
            AccessibleName = "Close",
            AccessibleDescription = "Close fuel and payload window",
            TabIndex = 2
        };
        closeButton.Click += (s, e) => Close();

        var refreshButton = new Button
        {
            Text = "&Refresh (F5)",
            Width = 100,
            Height = 30,
            AccessibleName = "Refresh",
            AccessibleDescription = "Refresh fuel and payload data from simulator",
            TabIndex = 1
        };
        refreshButton.Click += (s, e) =>
        {
            RefreshData();
            _announcer?.Announce("Fuel and payload data refreshed");
        };

        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(refreshButton);

        panel.Controls.Add(payloadTextBox);
        panel.Controls.Add(buttonPanel);

        payloadTab.Controls.Add(panel);
        mainTabControl.TabPages.Add(payloadTab);
    }

    private void CreateWeightBalanceTab()
    {
        var weightBalanceTab = new TabPage("Weight and Balance")
        {
            AccessibleName = "Weight and Balance",
            AccessibleDescription = "Aircraft weights, center of gravity, and limits"
        };

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

        // Data TextBox
        weightBalanceTextBox = new TextBox
        {
            Location = new Point(20, 20),
            Size = new Size(840, 520),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Weight and Balance Data",
            AccessibleDescription = "Aircraft weights, center of gravity, weight limits, and margins",
            Font = new Font("Consolas", 10, FontStyle.Regular),
            Text = "Loading weight and balance data...",
            TabIndex = 0
        };

        // Button panel at bottom
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(5)
        };

        var closeButton = new Button
        {
            Text = "&Close",
            Width = 75,
            Height = 30,
            AccessibleName = "Close",
            AccessibleDescription = "Close fuel and payload window",
            TabIndex = 2
        };
        closeButton.Click += (s, e) => Close();

        var refreshButton = new Button
        {
            Text = "&Refresh (F5)",
            Width = 100,
            Height = 30,
            AccessibleName = "Refresh",
            AccessibleDescription = "Refresh fuel and payload data from simulator",
            TabIndex = 1
        };
        refreshButton.Click += (s, e) =>
        {
            RefreshData();
            _announcer?.Announce("Fuel and payload data refreshed");
        };

        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(refreshButton);

        panel.Controls.Add(weightBalanceTextBox);
        panel.Controls.Add(buttonPanel);

        weightBalanceTab.Controls.Add(panel);
        mainTabControl.TabPages.Add(weightBalanceTab);
    }

    private async void RefreshData()
    {
        try
        {
            // Set loading text
            fuelTextBox.Text = "Loading fuel data...";
            passengersTextBox.Text = "Loading passengers data...";
            payloadTextBox.Text = "Loading payload data...";
            weightBalanceTextBox.Text = "Loading weight and balance data...";

            // Request all fuel and payload variables
            _simConnectManager?.RequestFuelAndPayloadData();

            // Wait for SimConnect data to arrive (same pattern as NavigationDisplayForm)
            await System.Threading.Tasks.Task.Delay(500);

            System.Diagnostics.Debug.WriteLine("[FuelPayloadDisplayForm] Data received, updating display");

            // Now format and display the data
            UpdateDisplay();
        }
        catch (Exception ex)
        {
            string errorMsg = $"Error loading data: {ex.Message}";
            fuelTextBox.Text = errorMsg;
            passengersTextBox.Text = errorMsg;
            payloadTextBox.Text = errorMsg;
            weightBalanceTextBox.Text = errorMsg;
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

    private string FormatFuelData()
    {
        var output = new System.Text.StringBuilder();

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

        output.AppendLine("FUEL QUANTITIES");
        output.AppendLine($"Left Outer Tank: {leftOuterKg:F0} kg ({leftOuterGal:F0} gal)");
        output.AppendLine($"Left Inner Tank: {leftInnerKg:F0} kg ({leftInnerGal:F0} gal)");
        output.AppendLine($"Center Tank: {centerKg:F0} kg ({centerGal:F0} gal)");
        output.AppendLine($"Right Inner Tank: {rightInnerKg:F0} kg ({rightInnerGal:F0} gal)");
        output.AppendLine($"Right Outer Tank: {rightOuterKg:F0} kg ({rightOuterGal:F0} gal)");
        output.AppendLine($"TOTAL FUEL: {totalFuelKg:F0} kg");
        output.AppendLine($"Fuel Density: {galToKg:F3} kg/gal");
        output.AppendLine("");
        output.AppendLine("FUEL CAPACITY");
        double fuelRemaining = MAX_FUEL - totalFuelKg;
        output.AppendLine($"Max Fuel Capacity: {MAX_FUEL:F0} kg");
        output.AppendLine($"Current Fuel: {totalFuelKg:F0} kg");
        output.AppendLine($"Remaining Capacity: {fuelRemaining:F0} kg");
        output.AppendLine("Press F5 to refresh, Press ESC to close");

        return output.ToString().TrimEnd();
    }

    private string FormatPassengersData()
    {
        var output = new System.Text.StringBuilder();

        double paxWeight = _data.ContainsKey("PAX_WEIGHT") ? _data["PAX_WEIGHT"] : 84;
        double bagWeight = _data.ContainsKey("BAG_WEIGHT") ? _data["BAG_WEIGHT"] : 20;

        int paxA = _data.ContainsKey("PAX_A") ? CountPassengerBits(_data["PAX_A"]) : 0;
        int paxB = _data.ContainsKey("PAX_B") ? CountPassengerBits(_data["PAX_B"]) : 0;
        int paxC = _data.ContainsKey("PAX_C") ? CountPassengerBits(_data["PAX_C"]) : 0;
        int paxD = _data.ContainsKey("PAX_D") ? CountPassengerBits(_data["PAX_D"]) : 0;
        int totalPax = paxA + paxB + paxC + paxD;
        double totalPaxWeight = totalPax * paxWeight;
        double totalBagWeight = totalPax * bagWeight;

        // Get FMS passenger count
        int fmsPax = _data.ContainsKey("FMS_PAX") ? (int)_data["FMS_PAX"] : 0;

        output.AppendLine("PASSENGER DISTRIBUTION");
        output.AppendLine($"Rows 1-6: {paxA} passengers");
        output.AppendLine($"Rows 7-13: {paxB} passengers");
        output.AppendLine($"Rows 14-21: {paxC} passengers");
        output.AppendLine($"Rows 22-29: {paxD} passengers");
        output.AppendLine($"TOTAL PASSENGERS: {totalPax}");
        output.AppendLine("");
        output.AppendLine("FMS PASSENGER COUNT");
        if (fmsPax > 0)
        {
            output.AppendLine($"FMS Passengers: {fmsPax}");
        }
        else
        {
            output.AppendLine("FMS Passengers: Not Entered");
        }
        output.AppendLine("");
        output.AppendLine("PASSENGER WEIGHTS");
        output.AppendLine($"Weight per Passenger: {paxWeight:F0} kg");
        output.AppendLine($"Weight per Bag: {bagWeight:F0} kg");
        output.AppendLine($"Total Pax Weight: {totalPaxWeight:F0} kg");
        output.AppendLine($"Total Bag Weight: {totalBagWeight:F0} kg");
        output.AppendLine("");
        output.AppendLine("PASSENGER CAPACITY");
        int emptySeats = MAX_PAX - totalPax;
        output.AppendLine($"Max Passengers: {MAX_PAX}");
        output.AppendLine($"Current Passengers: {totalPax}");
        output.AppendLine($"Empty Seats: {emptySeats}");
        output.AppendLine("Press F5 to refresh, Press ESC to close");

        return output.ToString().TrimEnd();
    }

    private string FormatPayloadData()
    {
        var output = new System.Text.StringBuilder();

        double fwdBaggage = _data.ContainsKey("CARGO_FWD") ? _data["CARGO_FWD"] : 0;
        double aftContainer = _data.ContainsKey("CARGO_AFT_CONT") ? _data["CARGO_AFT_CONT"] : 0;
        double aftBaggage = _data.ContainsKey("CARGO_AFT_BAG") ? _data["CARGO_AFT_BAG"] : 0;
        double aftBulk = _data.ContainsKey("CARGO_AFT_BULK") ? _data["CARGO_AFT_BULK"] : 0;
        double totalCargo = fwdBaggage + aftContainer + aftBaggage + aftBulk;

        output.AppendLine("CARGO COMPARTMENTS");
        output.AppendLine($"Forward Baggage: {fwdBaggage:F0} kg");
        output.AppendLine($"Aft Container: {aftContainer:F0} kg");
        output.AppendLine($"Aft Baggage: {aftBaggage:F0} kg");
        output.AppendLine($"Aft Bulk: {aftBulk:F0} kg");
        output.AppendLine($"TOTAL CARGO: {totalCargo:F0} kg");
        output.AppendLine("Press F5 to refresh, Press ESC to close");

        return output.ToString().TrimEnd();
    }

    private string FormatWeightBalanceData()
    {
        var output = new System.Text.StringBuilder();

        double emptyWeight = _data.ContainsKey("EMPTY_WEIGHT") ? _data["EMPTY_WEIGHT"] : 0;
        double zfw = _data.ContainsKey("ZFW") ? _data["ZFW"] : 0;
        double gw = _data.ContainsKey("GW") ? _data["GW"] : 0;
        double zfwCgMac = _data.ContainsKey("ZFW_CG_MAC") ? _data["ZFW_CG_MAC"] : 0;
        double gwCgMac = _data.ContainsKey("GW_CG_MAC") ? _data["GW_CG_MAC"] : 0;

        // Get FMS values
        double fmsZfw = _data.ContainsKey("FMS_ZFW") ? _data["FMS_ZFW"] : 0;
        double fmsGw = _data.ContainsKey("FMS_GW") ? _data["FMS_GW"] : 0;
        double fmsCg = _data.ContainsKey("FMS_CG") ? _data["FMS_CG"] : 0;

        output.AppendLine("ACTUAL WEIGHTS");
        output.AppendLine($"Empty Weight: {emptyWeight:F0} kg");
        output.AppendLine($"Zero Fuel Weight: {zfw:F0} kg");
        output.AppendLine($"Gross Weight: {gw:F0} kg");
        output.AppendLine($"CG at ZFW: {zfwCgMac:F2}% MAC");
        output.AppendLine($"CG at GW: {gwCgMac:F2}% MAC");
        output.AppendLine("");
        output.AppendLine("FMS ENTERED WEIGHTS");
        if (fmsZfw > 0)
        {
            output.AppendLine($"Zero Fuel Weight: {fmsZfw:F0} kg");
        }
        else
        {
            output.AppendLine("Zero Fuel Weight: Not Entered");
        }

        if (fmsGw > 0)
        {
            output.AppendLine($"Gross Weight: {fmsGw:F0} kg");
        }
        else
        {
            output.AppendLine("Gross Weight: Not Entered");
        }

        if (fmsCg > 0)
        {
            output.AppendLine($"CG: {fmsCg:F2}% MAC");
        }
        else
        {
            output.AppendLine("CG: Not Entered");
        }
        output.AppendLine("");
        output.AppendLine("WEIGHT LIMITS");
        // ZFW Margin
        double zfwMargin = MZFW - zfw;
        string zfwIndicator = zfwMargin >= 0 ? "OK" : "OVERWEIGHT";
        output.AppendLine($"Max Zero Fuel Weight (MZFW): {MZFW:F0} kg");
        output.AppendLine($"Current ZFW: {zfw:F0} kg");
        output.AppendLine($"ZFW Margin: {zfwMargin:F0} kg ({zfwIndicator})");
        // MTOW Margin
        double mtowMargin = MTOW - gw;
        string mtowIndicator = mtowMargin >= 0 ? "OK" : "OVERWEIGHT";
        output.AppendLine($"Max Takeoff Weight (MTOW): {MTOW:F0} kg");
        output.AppendLine($"Current GW: {gw:F0} kg");
        output.AppendLine($"MTOW Margin: {mtowMargin:F0} kg ({mtowIndicator})");
        // MLW Check
        output.AppendLine($"Max Landing Weight (MLW): {MLW:F0} kg");
        output.AppendLine($"Current GW: {gw:F0} kg");
        if (gw > MLW)
        {
            double fuelToBurn = gw - MLW;
            output.AppendLine($"Fuel to burn before landing: {fuelToBurn:F0} kg (WARNING)");
        }
        else
        {
            double mlwMargin = MLW - gw;
            output.AppendLine($"MLW Margin: {mlwMargin:F0} kg (OK)");
        }
        output.AppendLine("Press F5 to refresh, Press ESC to close");

        return output.ToString().TrimEnd();
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

            // Store the value silently in background
            // Display is only updated when user manually presses Refresh (F5 or button)
            // This prevents constant screen reader interruptions from changing fuel values
            _data[e.VarName] = e.Value;
        }
    }

    private void UpdateDisplay()
    {
        // Update all tab displays with formatted data
        fuelTextBox.Text = FormatFuelData();
        passengersTextBox.Text = FormatPassengersData();
        payloadTextBox.Text = FormatPayloadData();
        weightBalanceTextBox.Text = FormatWeightBalanceData();
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
