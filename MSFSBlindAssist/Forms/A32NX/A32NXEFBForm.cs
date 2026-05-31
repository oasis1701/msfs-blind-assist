using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.A32NX;

/// <summary>
/// Accessible flypad (EFB) for the FlyByWire A320neo.
/// Bridge JS is injected via CoherentGTInjector (CDP, port 19999).
/// State updates arrive via EFBBridgeServer.StateUpdated (HTTP, port 19777).
/// </summary>
public class A32NXEFBForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly EFBBridgeServer _bridge;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimBriefService _simBriefService;

    private IntPtr _previousWindow = IntPtr.Zero;
    private bool _disposed;
    private bool _updatingFromServer;
    private CancellationTokenSource? _serviceRefreshCts;

    // Top-level tab indices (match flypad toolbar order)
    private const int TAB_DASHBOARD   = 0;
    private const int TAB_DISPATCH    = 1;
    private const int TAB_GROUND      = 2;
    private const int TAB_PERFORMANCE = 3;
    private const int TAB_NAVIGATION  = 4;
    private const int TAB_ATC         = 5;
    private const int TAB_FAILURES    = 6;
    private const int TAB_CHECKLISTS  = 7;
    private const int TAB_PRESETS     = 8;
    private const int TAB_SETTINGS    = 9;

    // Ground sub-tab indices
    private const int GTAB_SERVICES = 0;
    private const int GTAB_FUEL     = 1;
    private const int GTAB_PAYLOAD  = 2;
    private const int GTAB_PUSHBACK = 3;

    // Settings sub-tab indices
    private const int STAB_AIRCRAFT = 0;
    private const int STAB_SIM      = 1;
    private const int STAB_REALISM  = 2;
    private const int STAB_THIRD    = 3;
    private const int STAB_ATSU     = 4;
    private const int STAB_AUDIO    = 5;
    private const int STAB_FLYPAD   = 6;
    private const int STAB_ABOUT    = 7;

    // ── Top-level layout controls ──────────────────────────────────────────────
    private Label _statusLabel = null!;
    private Button _wakeBtn = null!;
    private TabControl _tabs = null!;
    private System.Windows.Forms.Timer _healthTimer = null!;

    // ── Dashboard tab ──────────────────────────────────────────────────────────
    private Label _dbCallsign = null!, _dbOrigin = null!, _dbDest = null!, _dbAltn = null!;
    private Label _dbCruiseAlt = null!, _dbCostIndex = null!, _dbZfw = null!;
    private Label _dbFuel = null!, _dbDist = null!, _dbWind = null!;
    private Label _dbEte = null!, _dbDepPlan = null!, _dbDepEst = null!;
    private Label _dbArrPlan = null!, _dbArrEst = null!;
    private Button _dbImportBtn = null!, _dbSendBtn = null!, _dbRefreshBtn = null!;

    // ── Dispatch tab ──────────────────────────────────────────────────────────
    private TextBox _dispatchOfpBox = null!;
    private Button _dispatchFetchBtn = null!;
    private Label _dispatchStatus = null!;

    // ── Ground > Services ─────────────────────────────────────────────────────
    private Button _svcJetway = null!, _svcStairsFwd = null!, _svcStairsAft = null!;
    private Button _svcGpu = null!, _svcFuelTruck = null!, _svcCatering = null!, _svcBaggage = null!;

    // ── Ground > Fuel ─────────────────────────────────────────────────────────
    private Label _fuelCtrCur = null!, _fuelLmCur = null!, _fuelLaCur = null!;
    private Label _fuelRmCur = null!, _fuelRaCur = null!, _fuelTotalCur = null!;
    private NumericUpDown _fuelCtrTgt = null!, _fuelLmTgt = null!, _fuelLaTgt = null!;
    private NumericUpDown _fuelRmTgt = null!, _fuelRaTgt = null!;
    private RadioButton _fuelReal = null!, _fuelFast = null!, _fuelInstant = null!;
    private Button _fuelStartBtn = null!, _fuelStopBtn = null!, _fuelRefreshBtn = null!;
    private Label _fuelStatusLbl = null!;

    // ── Ground > Payload ──────────────────────────────────────────────────────
    private NumericUpDown _payPaxCount = null!;
    private RadioButton _payReal = null!, _payFast = null!, _payInstant = null!;
    private NumericUpDown _payFwdBag = null!, _payAftCont = null!, _payAftBag = null!, _payAftBulk = null!;
    private Button _payStartBtn = null!, _payStopBtn = null!, _payRefreshBtn = null!;
    private Label _payStatusLbl = null!;

    // ── Ground > Pushback ─────────────────────────────────────────────────────
    private Button _pbStartBtn = null!, _pbStopBtn = null!;
    private Label _pbStatusLbl = null!;

    // ── Navigation (ATIS) ─────────────────────────────────────────────────────
    private TextBox _atisIcaoBox = null!;
    private Button _atisFetchBtn = null!;
    private TextBox _atisResultBox = null!;

    // ── Failures ──────────────────────────────────────────────────────────────
    private ListBox _failuresList = null!;
    private Button _failuresRefreshBtn = null!;

    // ── Checklists ────────────────────────────────────────────────────────────
    private ComboBox _clSelectBox = null!;
    private ListBox _clItemsList = null!;
    private Button _clCheckBtn = null!, _clResetBtn = null!, _clRefreshBtn = null!;

    // ── Presets ───────────────────────────────────────────────────────────────
    private ListBox _presetsList = null!;
    private Button _presetsApplyBtn = null!, _presetsRefreshBtn = null!;
    private Label _presetsStatusLbl = null!;

    // ── Settings ──────────────────────────────────────────────────────────────
    // 3rd Party sub-tab
    private Label _navStatus = null!;
    private Button _navSignInBtn = null!, _navSignOutBtn = null!;
    private Label _airacLabel = null!;
    private TextBox _sbPilotIdBox = null!;
    private Button _sbValidateBtn = null!;
    private CheckBox _sbAutoImport = null!, _gsxFuel = null!, _gsxPayload = null!, _gsxPower = null!;
    // About sub-tab
    private Label _aboutVersion = null!, _aboutAirac = null!, _aboutSimbridge = null!;

    public A32NXEFBForm(EFBBridgeServer bridge, ScreenReaderAnnouncer announcer)
    {
        _bridge = bridge;
        _announcer = announcer;
        _simBriefService = new SimBriefService();

        Text = "FlyByWire A320 EFB";
        AccessibleName = "FlyByWire A320 EFB";
        Size = new Size(700, 600);
        MinimumSize = new Size(600, 500);

        BuildLayout();

        _bridge.StateUpdated += OnStateUpdated;

        _healthTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _healthTimer.Tick += (_, _) => UpdateConnectionStatus();
        _healthTimer.Start();

        Load += (_, _) =>
        {
            // Do NOT send wake_efb here — H:A32NX_EFB_POWER is a toggle, not a conditional wake.
            // Sending it when the EFB is already on turns it off. The Wake EFB button handles this manually.
            _bridge.EnqueueCommand("get_settings");
            _bridge.EnqueueCommand("check_navigraph_auth");
        };

        UpdateConnectionStatus();

        FormClosing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
            if (_previousWindow != IntPtr.Zero)
                SetForegroundWindow(_previousWindow);
        };
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        FocusFirstControlOnActiveTab();
    }

    // ── Layout construction ───────────────────────────────────────────────────

    private void BuildLayout()
    {
        SuspendLayout();

        _statusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "Connecting...",
            AccessibleName = "Connection status",
            TabIndex = 0,
            ForeColor = SystemColors.ControlText
        };

        _wakeBtn = new Button
        {
            Dock = DockStyle.Top,
            Height = 26,
            Text = "Wake EFB (power on flypad)",
            AccessibleName = "Wake EFB",
            TabIndex = 1
        };
        _wakeBtn.Click += (_, _) =>
        {
            _bridge.EnqueueCommand("wake_efb");
            _announcer?.Announce("Wake command sent.");
        };

        _tabs = new TabControl { Dock = DockStyle.Fill, TabIndex = 2 };

        _tabs.TabPages.Add(BuildDashboardTab());
        _tabs.TabPages.Add(BuildDispatchTab());
        _tabs.TabPages.Add(BuildGroundTab());
        _tabs.TabPages.Add(BuildStubTab("Performance", "Performance calculator coming soon."));
        _tabs.TabPages.Add(BuildNavigationTab());
        _tabs.TabPages.Add(BuildStubTab("ATC", "ATC / CPDLC coming soon."));
        _tabs.TabPages.Add(BuildFailuresTab());
        _tabs.TabPages.Add(BuildChecklistsTab());
        _tabs.TabPages.Add(BuildPresetsTab());
        _tabs.TabPages.Add(BuildSettingsTab());

        Controls.Add(_tabs);
        Controls.Add(_wakeBtn);
        Controls.Add(_statusLabel);

        ResumeLayout();
    }

    private static Panel MakeScrollPanel() =>
        new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };

    private static Label MakeFieldLabel(string text, Panel p, ref int y)
    {
        var lbl = new Label
        {
            Text = text + ":",
            Location = new Point(10, y),
            Size = new Size(140, 20),
            AutoSize = false
        };
        p.Controls.Add(lbl);

        var val = new Label
        {
            Text = "—",
            Location = new Point(155, y),
            Size = new Size(400, 20),
            AutoSize = false,
            AccessibleName = text
        };
        p.Controls.Add(val);
        y += 26;
        return val;
    }

    private static Button MakeButton(string text, Panel p, ref int y, int width = 180)
    {
        var btn = new Button
        {
            Text = text,
            Location = new Point(10, y),
            Size = new Size(width, 28),
            AccessibleName = text
        };
        p.Controls.Add(btn);
        y += 34;
        return btn;
    }

    private static TabPage MakeTab(string name)
    {
        var tp = new TabPage(name) { AccessibleName = name };
        return tp;
    }

    // ── Dashboard tab ─────────────────────────────────────────────────────────

    private TabPage BuildDashboardTab()
    {
        var tp = MakeTab("Dashboard");
        var p = MakeScrollPanel();
        int y = 10;

        _dbCallsign  = MakeFieldLabel("Callsign",           p, ref y);
        _dbOrigin    = MakeFieldLabel("Origin",             p, ref y);
        _dbDest      = MakeFieldLabel("Destination",        p, ref y);
        _dbAltn      = MakeFieldLabel("Alternate",          p, ref y);
        _dbCruiseAlt = MakeFieldLabel("Cruise Altitude",    p, ref y);
        _dbCostIndex = MakeFieldLabel("Cost Index",         p, ref y);
        _dbZfw       = MakeFieldLabel("ZFW (kg)",           p, ref y);
        _dbFuel      = MakeFieldLabel("Planned Fuel (kg)",  p, ref y);
        _dbDist      = MakeFieldLabel("Route Distance (nm)",p, ref y);
        _dbWind      = MakeFieldLabel("Avg Wind",           p, ref y);
        _dbEte       = MakeFieldLabel("Est Enroute Time",   p, ref y);
        _dbDepPlan   = MakeFieldLabel("Dep (planned UTC)",  p, ref y);
        _dbDepEst    = MakeFieldLabel("Dep (estimated UTC)",p, ref y);
        _dbArrPlan   = MakeFieldLabel("Arr (planned UTC)",  p, ref y);
        _dbArrEst    = MakeFieldLabel("Arr (estimated UTC)",p, ref y);

        y += 6;
        _dbImportBtn  = MakeButton("Import from SimBrief",  p, ref y);
        _dbSendBtn    = MakeButton("Send to MCDU",          p, ref y);
        _dbRefreshBtn = MakeButton("Refresh",               p, ref y);

        _dbImportBtn.Click  += OnDashboardImportClicked;
        _dbSendBtn.Click    += (_, _) => _bridge.EnqueueCommand("send_to_mcdu");
        _dbRefreshBtn.Click += OnDashboardRefreshClicked;

        tp.Controls.Add(p);
        return tp;
    }

    // ── Dispatch tab ──────────────────────────────────────────────────────────

    private TabPage BuildDispatchTab()
    {
        var tp = MakeTab("Dispatch");
        var p = MakeScrollPanel();
        int y = 10;

        _dispatchStatus = new Label
        {
            Text = "Enter SimBrief username in Settings > 3rd Party, then fetch.",
            Location = new Point(10, y),
            Size = new Size(560, 40),
            AutoSize = false
        };
        p.Controls.Add(_dispatchStatus);
        y += 46;

        _dispatchFetchBtn = MakeButton("Fetch OFP", p, ref y);
        _dispatchFetchBtn.Click += OnDispatchFetchClicked;

        _dispatchOfpBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(10, y),
            Size = new Size(560, 340),
            AccessibleName = "OFP text",
            Font = new Font("Courier New", 9f)
        };
        p.Controls.Add(_dispatchOfpBox);

        tp.Controls.Add(p);
        return tp;
    }

    // ── Ground tab (4 sub-tabs) ───────────────────────────────────────────────

    private TabPage BuildGroundTab()
    {
        var tp = MakeTab("Ground");
        var inner = new TabControl { Dock = DockStyle.Fill };

        inner.TabPages.Add(BuildGroundServicesTab());
        inner.TabPages.Add(BuildGroundFuelTab());
        inner.TabPages.Add(BuildGroundPayloadTab());
        inner.TabPages.Add(BuildGroundPushbackTab());

        tp.Controls.Add(inner);
        return tp;
    }

    private TabPage BuildGroundServicesTab()
    {
        var tp = MakeTab("Services");
        var p = MakeScrollPanel();
        int y = 10;

        _svcJetway    = MakeButton("Jetway",         p, ref y);
        _svcStairsFwd = MakeButton("Stairs Forward", p, ref y);
        _svcStairsAft = MakeButton("Stairs Aft",     p, ref y);
        _svcGpu       = MakeButton("GPU (Ext Power)", p, ref y);
        _svcFuelTruck = MakeButton("Fuel Truck",     p, ref y);
        _svcCatering  = MakeButton("Catering",       p, ref y);
        _svcBaggage   = MakeButton("Baggage",        p, ref y);

        WireServiceButton(_svcJetway,    "jetway");
        WireServiceButton(_svcStairsFwd, "stairs_fwd");
        WireServiceButton(_svcStairsAft, "stairs_aft");
        WireServiceButton(_svcGpu,       "gpu");
        WireServiceButton(_svcFuelTruck, "fuel_truck");
        WireServiceButton(_svcCatering,  "catering");
        WireServiceButton(_svcBaggage,   "baggage");

        tp.Controls.Add(p);
        return tp;
    }

    private void WireServiceButton(Button btn, string serviceId)
    {
        btn.Click += (_, _) =>
        {
            _bridge.EnqueueCommand("toggle_ground_service",
                new Dictionary<string, string> { ["service_id"] = serviceId });
            _serviceRefreshCts?.Cancel();
            _serviceRefreshCts?.Dispose();
            _serviceRefreshCts = new CancellationTokenSource();
            var token = _serviceRefreshCts.Token;
            Task.Delay(2000, token).ContinueWith(_ =>
            {
                if (token.IsCancellationRequested) return;
                try { if (IsHandleCreated) BeginInvoke(() => _bridge.EnqueueCommand("get_ground_state")); }
                catch (ObjectDisposedException) { }
            }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        };
    }

    private TabPage BuildGroundFuelTab()
    {
        var tp = MakeTab("Fuel");
        var p = MakeScrollPanel();
        int y = 10;

        _fuelCtrCur   = MakeFieldLabel("Centre Current (kg)",   p, ref y);
        _fuelLmCur    = MakeFieldLabel("Left Main Current (kg)", p, ref y);
        _fuelLaCur    = MakeFieldLabel("Left Aux Current (kg)",  p, ref y);
        _fuelRmCur    = MakeFieldLabel("Right Main Current (kg)",p, ref y);
        _fuelRaCur    = MakeFieldLabel("Right Aux Current (kg)", p, ref y);
        _fuelTotalCur = MakeFieldLabel("Total Current (kg)",     p, ref y);

        y += 6;
        _fuelCtrTgt = MakeNumericTank("Centre Target (kg)",   p, ref y, "Set centre tank fuel target");
        _fuelLmTgt  = MakeNumericTank("Left Main Target (kg)",p, ref y, "Set left main tank fuel target");
        _fuelLaTgt  = MakeNumericTank("Left Aux Target (kg)", p, ref y, "Set left aux tank fuel target");
        _fuelRmTgt  = MakeNumericTank("Right Main Target (kg)",p, ref y,"Set right main tank fuel target");
        _fuelRaTgt  = MakeNumericTank("Right Aux Target (kg)",p, ref y, "Set right aux tank fuel target");

        WireFuelTarget(_fuelCtrTgt, "centre");
        WireFuelTarget(_fuelLmTgt,  "left_main");
        WireFuelTarget(_fuelLaTgt,  "left_aux");
        WireFuelTarget(_fuelRmTgt,  "right_main");
        WireFuelTarget(_fuelRaTgt,  "right_aux");

        y += 6;
        var modeGroup = new GroupBox { Text = "Fueling Mode", Location = new Point(10, y), Size = new Size(300, 60) };
        _fuelReal    = new RadioButton { Text = "Real",    Location = new Point(5,  20), Checked = true };
        _fuelFast    = new RadioButton { Text = "Fast",    Location = new Point(80, 20) };
        _fuelInstant = new RadioButton { Text = "Instant", Location = new Point(155, 20) };
        modeGroup.Controls.AddRange(new Control[] { _fuelReal, _fuelFast, _fuelInstant });
        p.Controls.Add(modeGroup);
        y += 70;

        foreach (var rb in new[] { _fuelReal, _fuelFast, _fuelInstant })
        {
            rb.CheckedChanged += (_, _) =>
            {
                if (_updatingFromServer || !rb.Checked) return;
                string mode = rb == _fuelReal ? "real" : rb == _fuelFast ? "fast" : "instant";
                _bridge.EnqueueCommand("set_fuel_mode", new Dictionary<string, string> { ["mode"] = mode });
            };
        }

        _fuelStatusLbl = new Label { Text = "Idle", Location = new Point(10, y), Size = new Size(200, 20) };
        p.Controls.Add(_fuelStatusLbl);
        y += 26;

        _fuelStartBtn   = MakeButton("Start Fueling",  p, ref y);
        _fuelStopBtn    = MakeButton("Stop Fueling",   p, ref y);
        _fuelRefreshBtn = MakeButton("Refresh",        p, ref y);

        _fuelStartBtn.Click   += (_, _) => _bridge.EnqueueCommand("start_fuel_loading");
        _fuelStopBtn.Click    += (_, _) => _bridge.EnqueueCommand("stop_fuel_loading");
        _fuelRefreshBtn.Click += (_, _) => _bridge.EnqueueCommand("get_fuel_state");

        tp.Controls.Add(p);
        return tp;
    }

    private static NumericUpDown MakeNumericTank(string labelText, Panel p, ref int y, string accessibleName)
    {
        p.Controls.Add(new Label { Text = labelText + ":", Location = new Point(10, y), Size = new Size(175, 20) });
        var num = new NumericUpDown
        {
            Location = new Point(190, y),
            Size = new Size(100, 24),
            Minimum = 0,
            Maximum = 30000,
            Increment = 100,
            AccessibleName = accessibleName
        };
        p.Controls.Add(num);
        y += 30;
        return num;
    }

    private void WireFuelTarget(NumericUpDown num, string tank)
    {
        num.ValueChanged += (_, _) =>
        {
            if (_updatingFromServer) return;
            _bridge.EnqueueCommand("set_fuel_target",
                new Dictionary<string, string> { ["tank"] = tank, ["kg"] = num.Value.ToString("F0") });
        };
    }

    private TabPage BuildGroundPayloadTab()
    {
        var tp = MakeTab("Payload");
        var p = MakeScrollPanel();
        int y = 10;

        p.Controls.Add(new Label { Text = "Passengers (0–174):", Location = new Point(10, y), Size = new Size(175, 20) });
        _payPaxCount = new NumericUpDown
        {
            Location = new Point(190, y),
            Size = new Size(80, 24),
            Minimum = 0, Maximum = 174, Increment = 1,
            AccessibleName = "Passenger count"
        };
        _payPaxCount.ValueChanged += (_, _) =>
        {
            if (_updatingFromServer) return;
            _bridge.EnqueueCommand("set_passenger_count",
                new Dictionary<string, string> { ["value"] = _payPaxCount.Value.ToString("F0") });
        };
        p.Controls.Add(_payPaxCount);
        y += 30;

        var rateGroup = new GroupBox { Text = "Boarding Rate", Location = new Point(10, y), Size = new Size(300, 60) };
        _payInstant = new RadioButton { Text = "Instant", Location = new Point(5,  20), Checked = true };
        _payFast    = new RadioButton { Text = "Fast",    Location = new Point(80, 20) };
        _payReal    = new RadioButton { Text = "Real",    Location = new Point(155, 20) };
        rateGroup.Controls.AddRange(new Control[] { _payInstant, _payFast, _payReal });
        p.Controls.Add(rateGroup);
        y += 70;

        foreach (var rb in new[] { _payInstant, _payFast, _payReal })
        {
            rb.CheckedChanged += (_, _) =>
            {
                if (_updatingFromServer || !rb.Checked) return;
                string rate = rb == _payInstant ? "instant" : rb == _payFast ? "fast" : "real";
                _bridge.EnqueueCommand("set_boarding_rate", new Dictionary<string, string> { ["rate"] = rate });
            };
        }

        y += 6;
        _payFwdBag  = MakeNumericCargo("Fwd Baggage Container (kg)", p, ref y);
        _payAftCont = MakeNumericCargo("Aft Container (kg)",          p, ref y);
        _payAftBag  = MakeNumericCargo("Aft Baggage (kg)",            p, ref y);
        _payAftBulk = MakeNumericCargo("Aft Bulk Loose (kg)",         p, ref y);

        WireCargoTarget(_payFwdBag,  "FWD_BAGGAGE");
        WireCargoTarget(_payAftCont, "AFT_CONTAINER");
        WireCargoTarget(_payAftBag,  "AFT_BAGGAGE");
        WireCargoTarget(_payAftBulk, "AFT_BULK");

        _payStatusLbl = new Label { Text = "Not Started", Location = new Point(10, y), Size = new Size(200, 20) };
        p.Controls.Add(_payStatusLbl);
        y += 26;

        _payStartBtn   = MakeButton("Start Boarding", p, ref y);
        _payStopBtn    = MakeButton("Stop Boarding",  p, ref y);
        _payRefreshBtn = MakeButton("Refresh",        p, ref y);

        _payStartBtn.Click   += (_, _) => _bridge.EnqueueCommand("start_boarding");
        _payStopBtn.Click    += (_, _) => _bridge.EnqueueCommand("stop_boarding");
        _payRefreshBtn.Click += (_, _) => _bridge.EnqueueCommand("get_payload_state");

        tp.Controls.Add(p);
        return tp;
    }

    private static NumericUpDown MakeNumericCargo(string labelText, Panel p, ref int y)
    {
        p.Controls.Add(new Label { Text = labelText + ":", Location = new Point(10, y), Size = new Size(220, 20) });
        var num = new NumericUpDown
        {
            Location = new Point(235, y),
            Size = new Size(90, 24),
            Minimum = 0, Maximum = 5000, Increment = 10,
            AccessibleName = labelText
        };
        p.Controls.Add(num);
        y += 30;
        return num;
    }

    private void WireCargoTarget(NumericUpDown num, string zone)
    {
        num.ValueChanged += (_, _) =>
        {
            if (_updatingFromServer) return;
            _bridge.EnqueueCommand("set_cargo_weight",
                new Dictionary<string, string> { ["zone"] = zone, ["kg"] = num.Value.ToString("F0") });
        };
    }

    private TabPage BuildGroundPushbackTab()
    {
        var tp = MakeTab("Pushback");
        var p = MakeScrollPanel();
        int y = 10;

        _pbStatusLbl = new Label { Text = "Not Attached", Location = new Point(10, y), Size = new Size(200, 20) };
        p.Controls.Add(_pbStatusLbl);
        y += 26;

        _pbStartBtn = MakeButton("Start Pushback", p, ref y);
        _pbStopBtn  = MakeButton("Stop Pushback",  p, ref y);

        _pbStartBtn.Click += (_, _) =>
            _bridge.EnqueueCommand("toggle_ground_service",
                new Dictionary<string, string> { ["service_id"] = "pushback" });
        _pbStopBtn.Click += (_, _) =>
            _bridge.EnqueueCommand("toggle_ground_service",
                new Dictionary<string, string> { ["service_id"] = "pushback" });

        tp.Controls.Add(p);
        return tp;
    }

    // ── Stub tab ───────────────────────────────────────────────────────────────

    private static TabPage BuildStubTab(string name, string message)
    {
        var tp = MakeTab(name);
        tp.Controls.Add(new Label
        {
            Text = message,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            AccessibleName = message
        });
        return tp;
    }

    // ── Navigation tab (ATIS) ─────────────────────────────────────────────────

    private TabPage BuildNavigationTab()
    {
        var tp = MakeTab("Navigation");
        var p = MakeScrollPanel();
        int y = 10;

        p.Controls.Add(new Label { Text = "Airport ICAO:", Location = new Point(10, y), Size = new Size(100, 20) });
        _atisIcaoBox = new TextBox
        {
            Location = new Point(115, y),
            Size = new Size(80, 24),
            MaxLength = 4,
            AccessibleName = "Airport ICAO"
        };
        p.Controls.Add(_atisIcaoBox);
        y += 30;

        _atisFetchBtn = MakeButton("Fetch ATIS", p, ref y);
        _atisFetchBtn.Click += OnAtisFetchClicked;

        _atisResultBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(10, y),
            Size = new Size(560, 300),
            AccessibleName = "ATIS text",
            Font = new Font("Courier New", 9f)
        };
        p.Controls.Add(_atisResultBox);

        tp.Controls.Add(p);
        return tp;
    }

    // ── Failures tab ──────────────────────────────────────────────────────────

    private TabPage BuildFailuresTab()
    {
        var tp = MakeTab("Failures");
        var p = MakeScrollPanel();
        int y = 10;

        _failuresList = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(560, 300),
            AccessibleName = "Active failures"
        };
        p.Controls.Add(_failuresList);
        y += 310;

        _failuresRefreshBtn = MakeButton("Refresh", p, ref y);
        _failuresRefreshBtn.Click += (_, _) => _bridge.EnqueueCommand("get_page_text",
            new Dictionary<string, string> { ["page"] = "failures" });

        tp.Controls.Add(p);
        return tp;
    }

    // ── Checklists tab ────────────────────────────────────────────────────────

    private TabPage BuildChecklistsTab()
    {
        var tp = MakeTab("Checklists");
        var p = MakeScrollPanel();
        int y = 10;

        p.Controls.Add(new Label { Text = "Select Checklist:", Location = new Point(10, y), Size = new Size(120, 20) });
        _clSelectBox = new ComboBox
        {
            Location = new Point(135, y),
            Size = new Size(250, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Select checklist"
        };
        _clSelectBox.SelectedIndexChanged += (_, _) =>
        {
            if (_clSelectBox.SelectedItem != null)
                _bridge.EnqueueCommand("select_checklist",
                    new Dictionary<string, string> { ["name"] = _clSelectBox.SelectedItem.ToString()! });
        };
        p.Controls.Add(_clSelectBox);
        y += 30;

        _clItemsList = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(560, 220),
            AccessibleName = "Checklist items"
        };
        p.Controls.Add(_clItemsList);
        y += 230;

        _clCheckBtn   = MakeButton("Check Item",      p, ref y);
        _clResetBtn   = MakeButton("Reset Checklist", p, ref y);
        _clRefreshBtn = MakeButton("Refresh",         p, ref y);

        _clCheckBtn.Click   += (_, _) =>
        {
            if (_clItemsList.SelectedItem is string item)
                _bridge.EnqueueCommand("check_item",
                    new Dictionary<string, string> { ["item"] = item });
        };
        _clResetBtn.Click   += (_, _) => _bridge.EnqueueCommand("reset_checklist");
        _clRefreshBtn.Click += (_, _) => _bridge.EnqueueCommand("get_page_text",
            new Dictionary<string, string> { ["page"] = "checklists" });

        tp.Controls.Add(p);
        return tp;
    }

    // ── Presets tab ───────────────────────────────────────────────────────────

    private TabPage BuildPresetsTab()
    {
        var tp = MakeTab("Presets");
        var p = MakeScrollPanel();
        int y = 10;

        _presetsList = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(560, 220),
            AccessibleName = "Available presets"
        };
        p.Controls.Add(_presetsList);
        y += 230;

        _presetsApplyBtn   = MakeButton("Apply Selected Preset", p, ref y, 200);
        _presetsRefreshBtn = MakeButton("Refresh List",          p, ref y, 200);

        _presetsStatusLbl = new Label { Text = "", Location = new Point(10, y), Size = new Size(400, 20) };
        p.Controls.Add(_presetsStatusLbl);

        _presetsApplyBtn.Click += (_, _) =>
        {
            if (_presetsList.SelectedItem is string preset)
                _bridge.EnqueueCommand("apply_preset",
                    new Dictionary<string, string> { ["name"] = preset });
        };
        _presetsRefreshBtn.Click += (_, _) => _bridge.EnqueueCommand("get_page_text",
            new Dictionary<string, string> { ["page"] = "presets" });

        tp.Controls.Add(p);
        return tp;
    }

    // ── Settings tab (8 sub-tabs) ─────────────────────────────────────────────

    private TabPage BuildSettingsTab()
    {
        var tp = MakeTab("Settings");
        var inner = new TabControl { Dock = DockStyle.Fill };

        inner.TabPages.Add(BuildStubTab("Aircraft Options", "Aircraft options coming soon — read via NXDataStore."));
        inner.TabPages.Add(BuildSimOptionsTab());
        inner.TabPages.Add(BuildRealismTab());
        inner.TabPages.Add(BuildThirdPartyTab());
        inner.TabPages.Add(BuildAtsuTab());
        inner.TabPages.Add(BuildAudioTab());
        inner.TabPages.Add(BuildFlypadTab());
        inner.TabPages.Add(BuildAboutTab());

        tp.Controls.Add(inner);
        return tp;
    }

    private TabPage BuildSimOptionsTab()
    {
        var tp = MakeTab("Sim Options");
        var p = MakeScrollPanel();
        int y = 10;

        // Keys + values verified against fbw-common/.../Settings/Pages/SimOptionsPage.tsx
        AddSettingCombo(p, ref y, "Default Barometer",         "CONFIG_INIT_BARO_UNIT",
            new[] { "Auto", "inHg", "hPa" },    new[] { "AUTO", "IN HG", "HPA" });
        AddSettingCombo(p, ref y, "Sync MSFS Flight Plan",     "FP_SYNC",
            new[] { "None", "Load Only", "Save" }, new[] { "NONE", "LOAD", "SAVE" });
        AddSettingToggle(p, ref y, "Auto-load MSFS Route",     "CONFIG_AUTO_SIM_ROUTE_LOAD");
        AddSettingCombo(p, ref y, "SimBridge",                 "CONFIG_SIMBRIDGE_ENABLED",
            new[] { "Auto", "Off" },              new[] { "AUTO ON", "PERM OFF" });
        AddSettingCombo(p, ref y, "SimBridge Machine",         "CONFIG_SIMBRIDGE_REMOTE",
            new[] { "Local", "Remote" },          new[] { "local", "remote" });
        AddSettingToggle(p, ref y, "Dynamic Registration Decal", "DYNAMIC_REGISTRATION_DECAL", "1", "0");
        AddSettingToggle(p, ref y, "Calculated ILS Signals",   "RADIO_RECEIVER_USAGE_ENABLED", "1", "0");
        AddSettingToggle(p, ref y, "FDR Enabled",              "FDR_ENABLED", "1", "0");

        tp.Controls.Add(p);
        return tp;
    }

    private TabPage BuildRealismTab()
    {
        var tp = MakeTab("Realism");
        var p = MakeScrollPanel();
        int y = 10;

        // Keys + values verified against fbw-common/.../Settings/Pages/RealismPage.tsx
        AddSettingCombo(p, ref y, "ADIRS Align Time",          "CONFIG_ALIGN_TIME",
            new[] { "Instant", "Fast", "Real" },  new[] { "INSTANT", "FAST", "REAL" });
        // Self-test uses numeric seconds in NXDataStore, not label strings
        AddSettingCombo(p, ref y, "DMC Self-Test Time",        "CONFIG_SELF_TEST_TIME",
            new[] { "Instant", "Fast (5s)", "Real (12s)" }, new[] { "0", "5", "12" });
        AddSettingCombo(p, ref y, "Boarding Time",             "CONFIG_BOARDING_RATE",
            new[] { "Instant", "Fast", "Real" },  new[] { "INSTANT", "FAST", "REAL" });
        // Numeric toggles use 1/0 (usePersistentNumberProperty in FBW source)
        AddSettingToggle(p, ref y, "Autofill Checklists",      "EFB_AUTOFILL_CHECKLISTS",             "1", "0");
        AddSettingToggle(p, ref y, "Separate Tiller from Rudder", "REALISTIC_TILLER_ENABLED",         "1", "0");
        // usePersistentBooleanProperty → stores "true"/"false"
        AddSettingToggle(p, ref y, "MCDU Keyboard Input",      "MCDU_KB_INPUT");
        AddSettingToggle(p, ref y, "Sync EFIS (FO)",           "FO_SYNC_EFIS_ENABLED",                "1", "0");
        AddSettingToggle(p, ref y, "Pilot Avatar",             "CONFIG_PILOT_AVATAR_VISIBLE",         "1", "0");
        AddSettingToggle(p, ref y, "First Officer Avatar",     "CONFIG_FIRST_OFFICER_AVATAR_VISIBLE", "1", "0");
        // usePersistentBooleanProperty → stores "true"/"false"
        AddSettingToggle(p, ref y, "Pause at TOD",             "PAUSE_AT_TOD");

        tp.Controls.Add(p);
        return tp;
    }

    private TabPage BuildThirdPartyTab()
    {
        var tp = MakeTab("3rd Party");
        var p = MakeScrollPanel();
        int y = 10;

        p.Controls.Add(new Label { Text = "Navigraph:", Location = new Point(10, y), Size = new Size(100, 20) });
        _navStatus = new Label { Text = "Not signed in", Location = new Point(115, y), Size = new Size(250, 20), AccessibleName = "Navigraph status" };
        p.Controls.Add(_navStatus);
        y += 26;

        _navSignInBtn  = MakeButton("Sign In to Navigraph",    p, ref y, 200);
        _navSignOutBtn = MakeButton("Sign Out of Navigraph",   p, ref y, 200);
        _navSignInBtn.Click  += (_, _) => _bridge.EnqueueCommand("start_navigraph_auth");
        _navSignOutBtn.Click += (_, _) => _bridge.EnqueueCommand("sign_out_navigraph");

        _airacLabel = new Label { Text = "AIRAC: —", Location = new Point(10, y), Size = new Size(300, 20), AccessibleName = "AIRAC cycle" };
        p.Controls.Add(_airacLabel);
        y += 30;

        p.Controls.Add(new Label { Text = "SimBrief Pilot ID:", Location = new Point(10, y), Size = new Size(130, 20) });
        _sbPilotIdBox = new TextBox
        {
            Location = new Point(145, y), Size = new Size(160, 24),
            AccessibleName = "SimBrief pilot ID",
            Tag = "CONFIG_OVERRIDE_SIMBRIEF_USERID"  // populated by UpdateSettingControls on settings load
        };
        p.Controls.Add(_sbPilotIdBox);
        y += 30;

        _sbValidateBtn = MakeButton("Save Pilot ID", p, ref y, 130);
        _sbValidateBtn.Click += (_, _) =>
        {
            var id = _sbPilotIdBox.Text.Trim();
            _bridge.EnqueueCommand("set_setting",
                new Dictionary<string, string> { ["key"] = "CONFIG_OVERRIDE_SIMBRIEF_USERID", ["value"] = id });
            SettingsManager.Current.SimbriefUsername = id;
            SettingsManager.Save();
            _announcer?.Announce($"SimBrief pilot ID saved: {id}");
        };

        // CONFIG_AUTO_SIMBRIEF_IMPORT uses ENABLED/DISABLED (not true/false)
        _sbAutoImport = AddSettingToggle(p, ref y, "Auto-import SimBrief data", "CONFIG_AUTO_SIMBRIEF_IMPORT", "ENABLED", "DISABLED");
        // GSX toggles use numeric 1/0 (usePersistentNumberProperty in FBW source)
        _gsxFuel      = AddSettingToggle(p, ref y, "GSX Fuel enabled",   "GSX_FUEL_SYNC",    "1", "0");
        _gsxPayload   = AddSettingToggle(p, ref y, "GSX Payload enabled", "GSX_PAYLOAD_SYNC", "1", "0");
        _gsxPower     = AddSettingToggle(p, ref y, "GSX Power enabled",   "GSX_POWER_SYNC",   "1", "0");

        tp.Controls.Add(p);
        return tp;
    }

    private TabPage BuildAtsuTab()
    {
        var tp = MakeTab("ATSU / AOC");
        var p = MakeScrollPanel();
        int y = 10;

        // Keys + values verified against fbw-common/.../Settings/Pages/AtsuAocPage.tsx
        AddSettingText(p, ref y, "Hoppie ACARS User ID", "CONFIG_HOPPIE_USERID");
        // FBW has separate ATIS source and METAR source selectors
        AddSettingCombo(p, ref y, "ATIS Source",    "CONFIG_ATIS_SRC",
            new[] { "FAA (US)", "PilotEdge", "IVAO", "VATSIM" },
            new[] { "FAA", "PILOTEDGE", "IVAO", "VATSIM" });
        AddSettingCombo(p, ref y, "METAR Source",   "CONFIG_METAR_SRC",
            new[] { "MSFS", "NOAA", "PilotEdge", "VATSIM" },
            new[] { "MSFS", "NOAA", "PILOTEDGE", "VATSIM" });
        AddSettingCombo(p, ref y, "ACARS Provider", "ACARS_PROVIDER",
            new[] { "None", "Hoppie", "BATC", "SAI" },
            new[] { "NONE", "HOPPIE", "BATC", "SAI" });

        tp.Controls.Add(p);
        return tp;
    }

    private TabPage BuildAudioTab()
    {
        var tp = MakeTab("Audio");
        var p = MakeScrollPanel();
        int y = 10;

        // Keys + values verified against fbw-common/.../Settings/Pages/AudioPage.tsx
        // FBW stores volume as offset −50…+50 (0 = default); spinner range matches that
        AddVolumeSpinner(p, ref y, "Exterior Master Volume",  "SOUND_EXTERIOR_MASTER");
        AddVolumeSpinner(p, ref y, "Engine Interior Volume",  "SOUND_INTERIOR_ENGINE");
        AddVolumeSpinner(p, ref y, "Wind Interior Volume",    "SOUND_INTERIOR_WIND");
        AddSettingToggle(p, ref y, "PTU Audible in Cockpit",  "SOUND_PTU_AUDIBLE_COCKPIT",         "1", "0");
        AddSettingToggle(p, ref y, "Passenger Ambience",      "SOUND_PASSENGER_AMBIENCE_ENABLED",  "1", "0");
        AddSettingToggle(p, ref y, "Announcements",           "SOUND_ANNOUNCEMENTS_ENABLED",       "1", "0");
        AddSettingToggle(p, ref y, "Boarding Music",          "SOUND_BOARDING_MUSIC_ENABLED",      "1", "0");

        tp.Controls.Add(p);
        return tp;
    }

    private TabPage BuildFlypadTab()
    {
        var tp = MakeTab("flyPad");
        var p = MakeScrollPanel();
        int y = 10;

        // Keys + values verified against fbw-common/.../Settings/Pages/FlyPadPage.tsx
        AddSettingCombo(p, ref y, "Language", "EFB_LANGUAGE",
            new[] { "English", "Deutsch", "Français", "Español", "Português", "Polski", "Русский", "Italiano", "中文", "日本語", "한국어" },
            new[] { "en",      "de",      "fr",       "es",      "pt",        "pl",     "ru",      "it",      "zh",  "ja",    "ko"   });
        // EFB_KEYBOARD_LAYOUT_IDENT uses name strings from keyboardLayoutOptions in KeyboardWrapper.tsx
        AddSettingCombo(p, ref y, "Keyboard Layout", "EFB_KEYBOARD_LAYOUT_IDENT",
            new[] { "English", "Arabic",  "Chinese", "Czech",  "French", "German", "Greek",  "Hindi",  "Italian", "Japanese", "Korean", "Norwegian", "Polish", "Russian", "Spanish", "Swedish", "Thai" },
            new[] { "english", "arabic", "chinese", "czech",  "french", "german", "greek",  "hindi",  "italian", "japanese", "korean", "norwegian", "polish", "russian", "spanish", "swedish", "thai" });
        // Numeric toggles (usePersistentNumberProperty in FBW source)
        AddSettingToggle(p, ref y, "Auto-show Keyboard",   "EFB_AUTO_OSK",                      "1", "0");
        AddSettingToggle(p, ref y, "Auto-brightness",      "EFB_USING_AUTOBRIGHTNESS",           "1", "0");
        AddSettingToggle(p, ref y, "Battery Life Enabled", "EFB_BATTERY_LIFE_ENABLED",           "1", "0");
        AddSettingToggle(p, ref y, "Show Flight Progress", "EFB_SHOW_STATUSBAR_FLIGHTPROGRESS",  "1", "0");
        AddSettingToggle(p, ref y, "Coloured Raw METAR",   "EFB_USING_COLOREDMETAR",             "1", "0");
        // Time displayed uses lowercase strings; time format uses "12"/"24" (no 'h' suffix)
        AddSettingCombo(p, ref y, "Time Displayed",        "EFB_TIME_DISPLAYED",
            new[] { "UTC", "Local", "UTC and Local" }, new[] { "utc", "local", "both" });
        AddSettingCombo(p, ref y, "Time Format",           "EFB_TIME_FORMAT",
            new[] { "12-hour", "24-hour" },            new[] { "12", "24" });

        tp.Controls.Add(p);
        return tp;
    }

    private TabPage BuildAboutTab()
    {
        var tp = MakeTab("About");
        var p = MakeScrollPanel();
        int y = 10;

        _aboutVersion   = MakeFieldLabel("flyPad Version",           p, ref y);
        _aboutAirac     = MakeFieldLabel("AIRAC Cycle",              p, ref y);
        _aboutSimbridge = MakeFieldLabel("SimBridge Connection",     p, ref y);

        var refreshBtn = MakeButton("Refresh", p, ref y);
        refreshBtn.Click += (_, _) => _bridge.EnqueueCommand("get_settings");

        tp.Controls.Add(p);
        return tp;
    }

    // ── Settings helpers ──────────────────────────────────────────────────────

    // storedValues[i] is the NXDataStore value sent/matched for displayOptions[i].
    // When null, displayOptions are used directly as both display text and stored value.
    private void AddSettingCombo(Panel p, ref int y, string label, string nxKey,
        string[] displayOptions, string[]? storedValues = null)
    {
        var vals = storedValues ?? displayOptions;
        p.Controls.Add(new Label { Text = label + ":", Location = new Point(10, y), Size = new Size(200, 20) });
        var cb = new ComboBox
        {
            Location = new Point(215, y),
            Size = new Size(200, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = label,
            Tag = nxKey
        };
        cb.Items.AddRange(displayOptions.Cast<object>().ToArray());
        // Store value array on the combo so UpdateSettingControlsInContainer can reverse-map
        if (storedValues != null) cb.Items.Cast<object>(); // keeps reference live via closure below
        cb.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingFromServer) return;
            int i = cb.SelectedIndex;
            if (i >= 0 && i < vals.Length)
                _bridge.EnqueueCommand("set_setting",
                    new Dictionary<string, string> { ["key"] = nxKey, ["value"] = vals[i] });
        };
        // Tag: "nxKey\0val0\0val1\0..." — packed so UpdateSettingControlsInContainer can find the right index
        cb.Tag = storedValues != null
            ? nxKey + "\0" + string.Join("\0", storedValues)
            : nxKey;
        p.Controls.Add(cb);
        y += 30;
    }

    private CheckBox AddSettingToggle(Panel p, ref int y, string label, string nxKey,
        string onValue = "true", string offValue = "false")
    {
        var cb = new CheckBox
        {
            Text = label,
            Location = new Point(10, y),
            Size = new Size(450, 24),
            AccessibleName = label,
            Tag = nxKey
        };
        cb.CheckedChanged += (_, _) =>
        {
            if (_updatingFromServer) return;
            _bridge.EnqueueCommand("set_setting",
                new Dictionary<string, string> { ["key"] = nxKey, ["value"] = cb.Checked ? onValue : offValue });
        };
        p.Controls.Add(cb);
        y += 30;
        return cb;
    }

    private void AddSettingText(Panel p, ref int y, string label, string nxKey)
    {
        p.Controls.Add(new Label { Text = label + ":", Location = new Point(10, y), Size = new Size(220, 20) });
        var tb = new TextBox { Location = new Point(235, y), Size = new Size(200, 24), AccessibleName = label, Tag = nxKey };
        tb.Leave += (_, _) =>
            _bridge.EnqueueCommand("set_setting",
                new Dictionary<string, string> { ["key"] = nxKey, ["value"] = tb.Text.Trim() });
        p.Controls.Add(tb);
        y += 30;
    }

    // FBW AudioPage stores volume as an offset: −50 = quietest, 0 = default, +50 = loudest.
    // This matches the range stored in NXDataStore (not the 1–100 display FBW shows in its UI).
    private void AddVolumeSpinner(Panel p, ref int y, string label, string nxKey)
    {
        p.Controls.Add(new Label { Text = label + " (−50…+50):", Location = new Point(10, y), Size = new Size(230, 20) });
        var num = new NumericUpDown
        {
            Location = new Point(245, y),
            Size = new Size(70, 24),
            Minimum = -50, Maximum = 50, Increment = 5, Value = 0,
            AccessibleName = label,
            Tag = nxKey
        };
        num.ValueChanged += (_, _) =>
            _bridge.EnqueueCommand("set_setting",
                new Dictionary<string, string> { ["key"] = nxKey, ["value"] = num.Value.ToString("F0") });
        p.Controls.Add(num);
        y += 30;
    }

    // ── State dispatcher ──────────────────────────────────────────────────────

    private void OnStateUpdated(object? sender, EFBStateUpdateEventArgs e)
    {
        if (!IsHandleCreated) return;
        BeginInvoke(() => HandleState(e.Type, e.Data));
    }

    private void HandleState(string type, Dictionary<string, string> data)
    {
        switch (type)
        {
            case "connected":
            case "heartbeat":
                UpdateConnectionStatus();
                break;
            case "simbrief_loaded":
                ApplySimBriefState(data);
                break;
            case "simbrief_fetch_result":
                _statusLabel.Text = data.TryGetValue("success", out var ok) && ok == "true"
                    ? "Connected — SimBrief imported"
                    : "Connected — SimBrief import failed";
                break;
            case "ground_state":
                ApplyGroundServicesState(data);
                break;
            case "fuel_state":
                ApplyFuelState(data);
                break;
            case "payload_state":
                ApplyPayloadState(data);
                break;
            case "page_text":
                ApplyPageText(data);
                break;
            case "settings":
            case "settings_loaded":   // legacy alias — JS now sends "settings"
                ApplySettings(data);
                break;
            case "navigraph_status":
            case "navigraph_auth_state":   // legacy alias
                ApplyNavigraphStatus(data);
                break;
            case "navigraph_code":
            {
                var code = data.GetValueOrDefault("code", "—");
                var url  = data.GetValueOrDefault("url", "navigraph.com/activate");
                _announcer.Announce($"Navigraph auth code: {code}. Go to {url} and enter this code.");
                break;
            }
            case "navdata_status":
                if (data.TryGetValue("cycle", out var navCycle) && !string.IsNullOrEmpty(navCycle) && navCycle != "—")
                    _airacLabel.Text = $"AIRAC: {navCycle}";
                break;
        }
    }

    private void ApplySimBriefState(Dictionary<string, string> d)
    {
        string G(string k) => d.TryGetValue(k, out var v) ? v : "—";
        _dbCallsign.Text  = G("callsign");
        _dbOrigin.Text    = G("origin");
        _dbDest.Text      = G("destination");
        _dbAltn.Text      = G("alternate");
        _dbCruiseAlt.Text = G("cruise_altitude");
        _dbCostIndex.Text = G("cost_index");
        _dbZfw.Text       = G("zfw");
        _dbFuel.Text      = G("fuel_kg");
        _dbDist.Text      = G("route_distance");
        _dbWind.Text      = G("avg_wind");
        _dbEte.Text       = G("ete");
        _dbDepPlan.Text   = G("dep_planned");
        _dbDepEst.Text    = G("dep_estimated");
        _dbArrPlan.Text   = G("arr_planned");
        _dbArrEst.Text    = G("arr_estimated");
    }

    private void ApplyGroundServicesState(Dictionary<string, string> d)
    {
        void Upd(Button btn, string key)
        {
            string state = d.TryGetValue(key, out var v) ? v : "unknown";
            if (btn.Tag == null) btn.Tag = btn.Text; // cache original text on first call
            string baseText = (string)btn.Tag;
            btn.Text = $"{baseText} ({state})";
            btn.AccessibleName = $"{baseText}, {state}";
        }
        Upd(_svcJetway,    "jetway");
        Upd(_svcStairsFwd, "stairs_fwd");
        Upd(_svcStairsAft, "stairs_aft");
        Upd(_svcGpu,       "gpu");
        Upd(_svcFuelTruck, "fuel_truck");
        Upd(_svcCatering,  "catering");
        Upd(_svcBaggage,   "baggage");
        Upd(_pbStartBtn,   "pushback");
    }

    private void ApplyFuelState(Dictionary<string, string> d)
    {
        string G(string k) => d.TryGetValue(k, out var v) ? v : "—";
        _fuelCtrCur.Text   = G("centre_actual_kg");
        _fuelLmCur.Text    = G("left_main_actual_kg");
        _fuelLaCur.Text    = G("left_aux_actual_kg");
        _fuelRmCur.Text    = G("right_main_actual_kg");
        _fuelRaCur.Text    = G("right_aux_actual_kg");
        _fuelStatusLbl.Text = G("status");

        double totalActual = 0;
        foreach (string k in new[] { "centre_actual_kg", "left_main_actual_kg", "left_aux_actual_kg", "right_main_actual_kg", "right_aux_actual_kg" })
            if (d.TryGetValue(k, out var v) && double.TryParse(v, out var n)) totalActual += n;
        _fuelTotalCur.Text = totalActual > 0 ? ((int)Math.Round(totalActual)).ToString() : "—";

        _updatingFromServer = true;
        try
        {
            if (d.TryGetValue("mode", out var mode))
            {
                _fuelReal.Checked    = mode == "real";
                _fuelFast.Checked    = mode == "fast";
                _fuelInstant.Checked = mode == "instant";
            }
            void SetTgt(NumericUpDown num, string key)
            {
                if (d.TryGetValue(key, out var v) && decimal.TryParse(v, out var n))
                    num.Value = Math.Clamp(n, num.Minimum, num.Maximum);
            }
            SetTgt(_fuelCtrTgt, "centre_target_kg");
            SetTgt(_fuelLmTgt,  "left_main_target_kg");
            SetTgt(_fuelLaTgt,  "left_aux_target_kg");
            SetTgt(_fuelRmTgt,  "right_main_target_kg");
            SetTgt(_fuelRaTgt,  "right_aux_target_kg");
        }
        finally { _updatingFromServer = false; }
    }

    private void ApplyPayloadState(Dictionary<string, string> d)
    {
        _updatingFromServer = true;
        try
        {
            if (d.TryGetValue("pax_count", out var pax) && decimal.TryParse(pax, out var paxVal))
                _payPaxCount.Value = Math.Clamp(paxVal, 0, 174);
            if (d.TryGetValue("fwd_baggage", out var fb) && decimal.TryParse(fb, out var fbVal))
                _payFwdBag.Value = Math.Clamp(fbVal, 0, 5000);
            if (d.TryGetValue("aft_container", out var ac) && decimal.TryParse(ac, out var acVal))
                _payAftCont.Value = Math.Clamp(acVal, 0, 5000);
            if (d.TryGetValue("aft_baggage", out var ab) && decimal.TryParse(ab, out var abVal))
                _payAftBag.Value = Math.Clamp(abVal, 0, 5000);
            if (d.TryGetValue("aft_bulk", out var al) && decimal.TryParse(al, out var alVal))
                _payAftBulk.Value = Math.Clamp(alVal, 0, 5000);
            _payStatusLbl.Text = d.TryGetValue("status", out var bs) ? bs : "Unknown";
            if (d.TryGetValue("boarding_rate", out var rate))
            {
                _payInstant.Checked = rate == "instant";
                _payFast.Checked    = rate == "fast";
                _payReal.Checked    = rate == "real";
            }
        }
        finally
        {
            _updatingFromServer = false;
        }
    }

    private void ApplyPageText(Dictionary<string, string> d)
    {
        string page = d.TryGetValue("page", out var pg) ? pg : "";
        string text = d.TryGetValue("text", out var tx) ? tx : "";

        switch (page)
        {
            case "failures":
                _failuresList.BeginUpdate();
                _failuresList.Items.Clear();
                if (string.IsNullOrWhiteSpace(text))
                    _failuresList.Items.Add("No active failures");
                else
                    foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        _failuresList.Items.Add(line);
                _failuresList.EndUpdate();
                break;
            case "checklists":
                _clItemsList.BeginUpdate();
                _clItemsList.Items.Clear();
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    _clItemsList.Items.Add(line);
                _clItemsList.EndUpdate();
                break;
            case "presets":
                _presetsList.BeginUpdate();
                _presetsList.Items.Clear();
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    _presetsList.Items.Add(line);
                _presetsList.EndUpdate();
                break;
        }
    }

    private void ApplySettings(Dictionary<string, string> d)
    {
        _aboutVersion.Text   = d.TryGetValue("flypad_version", out var v) ? v : "—";
        _aboutAirac.Text     = d.TryGetValue("airac_cycle",    out var a) ? a : "—";
        _aboutSimbridge.Text = d.TryGetValue("simbridge",      out var s) ? s : "—";
        if (d.TryGetValue("airac_cycle", out var ac))
            _airacLabel.Text = $"AIRAC: {ac}";
        _updatingFromServer = true;
        try { UpdateSettingControls(d); }
        finally { _updatingFromServer = false; }
    }

    private void UpdateSettingControls(Dictionary<string, string> d)
    {
        foreach (TabPage sp in ((TabControl)_tabs.TabPages[TAB_SETTINGS].Controls[0]).TabPages)
        {
            UpdateSettingControlsInContainer(sp, d);
        }
    }

    private static void UpdateSettingControlsInContainer(Control container, Dictionary<string, string> d)
    {
        foreach (Control c in container.Controls)
        {
            if (c.Tag is string tagStr)
            {
                // Tag format: either "nxKey" or "nxKey\0stored0\0stored1\0..."
                var parts = tagStr.Split('\0');
                var key   = parts[0];
                if (d.TryGetValue(key, out var val))
                {
                    switch (c)
                    {
                        case ComboBox cb:
                            int idx = -1;
                            if (parts.Length > 1)
                            {
                                // Reverse-map stored value → display index
                                for (int k = 1; k < parts.Length; k++)
                                {
                                    if (parts[k] == val) { idx = k - 1; break; }
                                }
                            }
                            else
                            {
                                idx = cb.Items.IndexOf(val);
                            }
                            if (idx >= 0 && idx < cb.Items.Count) cb.SelectedIndex = idx;
                            break;
                        case CheckBox check:
                            check.Checked = val == "true" || val == "1" ||
                                            val.Equals("ENABLED", StringComparison.OrdinalIgnoreCase);
                            break;
                        case NumericUpDown num:
                            if (decimal.TryParse(val, out var dec))
                                num.Value = Math.Clamp(dec, num.Minimum, num.Maximum);
                            break;
                        case TextBox tb:
                            tb.Text = val;
                            break;
                    }
                }
            }
            if (c.Controls.Count > 0)
                UpdateSettingControlsInContainer(c, d);
        }
    }

    private void ApplyNavigraphStatus(Dictionary<string, string> d)
    {
        bool signedIn = d.TryGetValue("signed_in", out var si) && (si == "true" || si == "1");
        string username = d.TryGetValue("username", out var u) ? u : "";
        _navStatus.Text = signedIn && !string.IsNullOrEmpty(username) ? username : "Not signed in";
        if (d.TryGetValue("airac", out var airac))
            _airacLabel.Text = $"AIRAC: {airac}";
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private async void OnDashboardImportClicked(object? sender, EventArgs e)
        => await FetchAndDisplayDashboardDataAsync(triggerMcduImport: true);

    private async void OnDashboardRefreshClicked(object? sender, EventArgs e)
        => await FetchAndDisplayDashboardDataAsync(triggerMcduImport: false);

    private async Task FetchAndDisplayDashboardDataAsync(bool triggerMcduImport)
    {
        string username = SettingsManager.Current.SimbriefUsername?.Trim() ?? "";
        if (string.IsNullOrEmpty(username))
        {
            _announcer?.Announce("No SimBrief username set. Add it in Settings then try again.");
            return;
        }

        _dbImportBtn.Enabled = false;
        _dbRefreshBtn.Enabled = false;

        try
        {
            if (triggerMcduImport)
                _bridge.EnqueueCommand("fetch_simbrief");

            var ofp = await _simBriefService.FetchFullOFPAsync(username);
            if (IsDisposed || !IsHandleCreated) return;

            ApplySimBriefState(new Dictionary<string, string>
            {
                ["callsign"]       = ofp.Callsign,
                ["origin"]         = ofp.OriginIcao,
                ["destination"]    = ofp.DestIcao,
                ["alternate"]      = ofp.AltnIcao,
                ["cruise_altitude"]= ofp.InitialAltitude,
                ["cost_index"]     = ofp.CostIndex,
                ["zfw"]            = ofp.WeightZfw,
                ["fuel_kg"]        = ofp.FuelBlockRamp,
                ["route_distance"] = ofp.RouteDistance,
                ["avg_wind"]       = ofp.AvgWindComp,
                ["ete"]            = ofp.AirTime,
            });
            _announcer?.Announce(triggerMcduImport
                ? $"SimBrief imported: {ofp.OriginIcao} to {ofp.DestIcao}."
                : $"SimBrief refreshed: {ofp.OriginIcao} to {ofp.DestIcao}.");
        }
        catch (Exception ex)
        {
            if (IsDisposed || !IsHandleCreated) return;
            _announcer?.Announce($"SimBrief error: {ex.Message}");
        }
        finally
        {
            if (!IsDisposed && IsHandleCreated)
            {
                _dbImportBtn.Enabled = true;
                _dbRefreshBtn.Enabled = true;
            }
        }
    }

    private async void OnDispatchFetchClicked(object? sender, EventArgs e)
    {
        string username = SettingsManager.Current.SimbriefUsername?.Trim() ?? "";
        if (string.IsNullOrEmpty(username))
        {
            _dispatchStatus.Text = "No SimBrief username set. Add it in Settings > About (EFB) or app settings.";
            return;
        }

        _dispatchFetchBtn.Enabled = false;
        _dispatchStatus.Text = "Fetching OFP…";

        try
        {
            var ofp = await _simBriefService.FetchFullOFPAsync(username);
            if (IsDisposed || !IsHandleCreated) return;
            _dispatchOfpBox.Text = FormatOFPSummary(ofp);
            _dispatchStatus.Text = $"OFP loaded — {ofp.OriginIcao} → {ofp.DestIcao}";
        }
        catch (Exception ex)
        {
            if (IsDisposed || !IsHandleCreated) return;
            _dispatchStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            if (!IsDisposed && IsHandleCreated)
                _dispatchFetchBtn.Enabled = true;
        }
    }

    private static string FormatOFPSummary(SimBriefOFP ofp)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"FLIGHT: {ofp.Callsign}  {ofp.OriginIcao} → {ofp.DestIcao}  ALT: {ofp.AltnIcao}");
        sb.AppendLine($"CRUISE: FL{ofp.InitialAltitude}  CI: {ofp.CostIndex}  M{ofp.CruiseMach}");
        sb.AppendLine($"ZFW: {ofp.WeightZfw}  BLOCK FUEL: {ofp.FuelBlockRamp}");
        sb.AppendLine($"DIST: {ofp.RouteDistance} nm  ETE: {ofp.AirTime}");
        sb.AppendLine($"AVG WIND: {ofp.AvgWindComp}");
        sb.AppendLine();
        sb.AppendLine($"ROUTE: {ofp.Route}");
        return sb.ToString();
    }

    private async void OnAtisFetchClicked(object? sender, EventArgs e)
    {
        string icao = _atisIcaoBox.Text.Trim().ToUpperInvariant();
        if (icao.Length < 3)
        {
            _atisResultBox.Text = "Enter a valid ICAO (3–4 letters).";
            return;
        }

        _atisFetchBtn.Enabled = false;
        _atisResultBox.Text = "Fetching…";

        try
        {
            string metar = await VATSIMService.GetMETARAsync(icao);
            if (IsDisposed || !IsHandleCreated) return;
            _atisResultBox.Text = string.IsNullOrWhiteSpace(metar)
                ? $"No ATIS/METAR found for {icao}."
                : metar;
        }
        catch (Exception ex)
        {
            if (IsDisposed || !IsHandleCreated) return;
            _atisResultBox.Text = $"Error: {ex.Message}";
        }
        finally
        {
            if (!IsDisposed && IsHandleCreated)
                _atisFetchBtn.Enabled = true;
        }
    }

    // ── Connection status ─────────────────────────────────────────────────────

    private void UpdateConnectionStatus()
    {
        if (!IsHandleCreated) return;
        bool connected = _bridge.IsBridgeConnected;
        _statusLabel.Text = connected
            ? "EFB Connected — developer mode required"
            : "EFB bridge not connected — enable MSFS developer mode (Options > General > Developers)";
        _statusLabel.ForeColor = connected ? Color.DarkGreen : Color.DarkRed;
    }

    // ── Focus helper ──────────────────────────────────────────────────────────

    private void FocusFirstControlOnActiveTab()
    {
        var page = _tabs.SelectedTab;
        if (page == null) return;
        var first = GetFirstFocusable(page);
        first?.Focus();
    }

    private static Control? GetFirstFocusable(Control container)
    {
        foreach (Control c in container.Controls.OfType<Control>().OrderBy(c => c.TabIndex))
        {
            if (c is TabControl tc) return GetFirstFocusable(tc.SelectedTab ?? tc.TabPages[0]);
            if (c.CanSelect && c.Visible && c.Enabled) return c;
            var child = GetFirstFocusable(c);
            if (child != null) return child;
        }
        return null;
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            _serviceRefreshCts?.Cancel();
            _serviceRefreshCts?.Dispose();
            _serviceRefreshCts = null;
            _healthTimer.Stop();
            _healthTimer.Dispose();
            _bridge.StateUpdated -= OnStateUpdated;
        }
        base.Dispose(disposing);
    }
}
