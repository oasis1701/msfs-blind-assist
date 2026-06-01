// GsxService — owns its own SimConnect client connection to MSFS for the
// GSX Ground Services Pro accessibility integration. Ported from the
// AccessGSX project (https://github.com/jfayre/access-gsx) with permission
// of the author (both projects are GPL v3).
//
// This service is independent from MSFSBA's main SimConnectManager: MSFS
// supports multiple SimConnect clients per process, and isolating GSX traffic
// makes the integration self-contained. All speech is routed through
// MSFSBA's existing ScreenReaderAnnouncer; no Tolk is loaded here.
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Globalization;
using Microsoft.FlightSimulator.SimConnect;
using Microsoft.Win32;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Services;

/// <summary>
/// SimConnect client + state model for GSX Ground Services Pro.
/// Owns its own SimConnect connection (HWND-based, message-pump driven) so
/// it doesn't interfere with the main aircraft-data SimConnect connection.
/// Mirrors the GSX in-sim menu, reads the GSX tooltip file when GSX pushes
/// updates, and exposes events the UI form (AccessGSXForm) and MainForm
/// background hook subscribe to.
/// </summary>
public sealed class GsxService : IDisposable
{
    // Distinct WM_USER message id — the main SimConnect uses 0x0402, this
    // one uses 0x0403 so both clients' ReceiveMessage calls are dispatched
    // correctly from MainForm.WndProc.
    public const int WM_USER_GSX_SIMCONNECT = 0x0403;

    // GSX install layout: HKCU\Software\Fsdreamteam\root -> filesystem root
    // for FSDT packages. From there the GSX package lives under
    // MSFS\fsdreamteam-gsx-pro\html_ui\InGamePanels\FSDT_GSX_Panel\.
    private const string FsdtRegistrySubKey = @"Software\Fsdreamteam\";
    private const string FsdtRegistryValueName = "root";
    private const string GsxPackageFolder = @"MSFS\fsdreamteam-gsx-pro";
    private const string GsxPackageFolderHtml = @"html_ui\InGamePanels\FSDT_GSX_Panel";
    private const string GsxMenuFileName = "menu";
    private const string GsxTooltipFileName = "tooltip";
    private const string GsxSettingsFileName = "settings.html";
    private const string GsxStatusFileName = "status.html";
    private const string CouatlConfigFolderName = "Virtuali";
    private const string CouatlConfigFileName = "CouatlAddons.ini";
    private const string GsxConfigSectionName = "gsx";
    private const string CommonConfigSectionName = "common";
    private static readonly TimeSpan TimerOnlyStatusAnnouncementInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan GroundConnectionTimerAnnouncementInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FuelingProgressAnnouncementInterval = TimeSpan.FromSeconds(30);
    private const int BoardingPassengerAnnouncementInterval = 10;
    private const string CurrencyTokenPattern =
        @"(?:USD|EUR|GBP|JPY|CNY|RMB|CAD|AUD|NZD|CHF|SEK|NOK|DKK|PLN|CZK|HUF|RON|BGN|TRY|ILS|AED|SAR|QAR|INR|KRW|SGD|HKD|TWD|THB|MYR|IDR|PHP|VND|BRL|MXN|ARS|CLP|COP|ZAR|[$€£¥₩₹₽₺₪₫₴])";

    private const int ChoiceSettings = 12;
    private const int DynamicSettingDefinitionStart = 10000;
    private const string StringSlotBase64Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    private const int StringSlotChunks = 16;
    private const int StringSlotCharsPerChunk = 4;

    // SimConnect identifiers.
    private enum DataRequestId
    {
        RequestRemote,
        RequestCouatlStarted,
    }

    private enum DataDefineId
    {
        CouatlStarted,
        MenuOpen,
        MenuChoice,
        RemoteControl,
    }

    private enum GroupId
    {
        MainGroup,
    }

    private enum EventId
    {
        ExternalSystemSet,
        ExternalSystemToggle,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DoubleValue
    {
        public double Value;
    }

    public sealed record MenuOption(string Key, string Text, int Choice);
    public sealed record GsxSettingChoice(double Value, string Label);
    public sealed record GsxSettingItem(
        string Key,
        string Label,
        string Category,
        string Type,
        string Value,
        string Tip,
        double? Min,
        double? Max,
        double? Step,
        string Unit,
        IReadOnlyList<GsxSettingChoice> Choices,
        string InfoValue,
        string ButtonText);

    // ─────────────────────────────────────────────────────────────────────
    // Public surface — used by AccessGSXForm and MainForm.
    // ─────────────────────────────────────────────────────────────────────

    public bool IsConnected => _simConnect != null;
    public bool CouatlStarted => _couatlStarted;
    public string StatusText => _statusText;
    public string MenuTitle => _menuTitle;
    public IReadOnlyList<MenuOption> MenuOptions => _menuOptions;
    public string LastTooltip => _lastTooltip;
    public string LastSettingsText => _lastSettingsText;
    public IReadOnlyList<GsxSettingItem> SettingsItems => _settingsItems;

    /// <summary>
    /// When true, the service speaks tooltip updates itself (via the injected
    /// announcer) when GSX publishes a tooltip — used while AccessGSXForm is
    /// hidden so the user still hears boarding / fuel / pushback callouts.
    /// When false (form open) the form drives its own speech via TooltipChanged.
    /// </summary>
    public bool AnnounceWhenFormHidden { get; set; }

    public event EventHandler? StateChanged;
    public event EventHandler? MenuChanged;
    public event EventHandler? MenuHidden;
    public event EventHandler? MenuTimedOut;
    public event EventHandler? TooltipChanged;
    public event EventHandler? SettingsChanged;

    // ─────────────────────────────────────────────────────────────────────
    // Internal state.
    // ─────────────────────────────────────────────────────────────────────

    private readonly IntPtr _windowHandle;
    private readonly ScreenReaderAnnouncer _announcer;
    private Microsoft.FlightSimulator.SimConnect.SimConnect? _simConnect;

    private bool _menuOpen;
    private bool _couatlStarted;
    private bool _disposed;
    private string _menuTitle = "GSX Menu";
    private string _lastTooltip = string.Empty;
    private string _lastStatusStableText = string.Empty;
    private string _lastCompletedStatusServiceText = string.Empty;
    private string _lastSettingsText = string.Empty;
    private string _statusText = "Status: Disconnected";
    private string? _menuFilePath;
    private string? _tooltipFilePath;
    private string? _settingsFilePath;
    private string? _statusFilePath;
    private System.Windows.Forms.Timer? _settingsFallbackTimer;
    private System.Windows.Forms.Timer? _tooltipPollTimer;
    private DateTime _lastTimerOnlyStatusAnnouncementUtc = DateTime.MinValue;
    private readonly List<MenuOption> _menuOptions = new();
    private readonly List<GsxSettingItem> _settingsItems = new();
    private readonly Dictionary<string, DataDefineId> _dynamicSettingDefinitions = new(StringComparer.OrdinalIgnoreCase);
    private int _nextDynamicSettingDefinition = DynamicSettingDefinitionStart;
    private readonly Dictionary<string, (int Percent, DateTime SeenAt)> _lastBaggageProgressByOperation = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _recentLiveServiceAnnouncements = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastServiceOperatorByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastFuelingProgressAnnouncementByService = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lastBoardingPassengerAnnouncementByService = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _announcedInvoiceKeys = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan LiveServiceRepeatWindow = TimeSpan.FromMinutes(10);
    private static readonly Regex PercentRegex = new(
        @"\b(\d{1,3})\s*%",
        RegexOptions.Compiled);

    public GsxService(IntPtr windowHandle, ScreenReaderAnnouncer announcer)
    {
        _windowHandle = windowHandle;
        _announcer = announcer ?? throw new ArgumentNullException(nameof(announcer));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Start (or no-op if already connected). Safe to call repeatedly — e.g.
    /// from a SimConnect ConnectionStatusChanged callback on reconnect.
    /// </summary>
    public void Start()
    {
        if (_disposed) return;
        if (_simConnect != null) return;

        try
        {
            _simConnect = new Microsoft.FlightSimulator.SimConnect.SimConnect(
                "MSFSBA_GSX", _windowHandle, WM_USER_GSX_SIMCONNECT, null, 0);
            HookSimConnectEvents();
            _statusText = "Status: Connected to Microsoft Flight Simulator";
            RaiseStateChanged();
            System.Diagnostics.Debug.WriteLine("[GsxService] SimConnect client created.");
        }
        catch (COMException ex)
        {
            _simConnect = null;
            _statusText = "Status: Can't open SimConnect. Is MSFS running?";
            RaiseStateChanged();
            System.Diagnostics.Debug.WriteLine($"[GsxService] SimConnect unavailable: {ex.Message}");
        }
        catch (Exception ex)
        {
            _simConnect = null;
            _statusText = "Status: SimConnect initialization failed.";
            RaiseStateChanged();
            System.Diagnostics.Debug.WriteLine($"[GsxService] SimConnect failed to initialize: {ex.Message}");
        }
    }

    /// <summary>
    /// Stop the SimConnect client and release the remote-control flag so the
    /// GSX toolbar panel is no longer suppressed in-sim.
    /// </summary>
    public void Stop()
    {
        if (_simConnect == null) return;
        _tooltipPollTimer?.Stop();

        try
        {
            // Best-effort release of remote-control so GSX restores its
            // in-sim toolbar behaviour.
            SendVariable(DataDefineId.RemoteControl, 0);
        }
        catch
        {
            // ignore — we're tearing down
        }

        try
        {
            _simConnect.Dispose();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _simConnect = null;
            _statusText = "Status: Disconnected";
            _menuOpen = false;
            _menuOptions.Clear();
            RaiseStateChanged();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settingsFallbackTimer?.Stop();
        _settingsFallbackTimer?.Dispose();
        _settingsFallbackTimer = null;
        _tooltipPollTimer?.Stop();
        _tooltipPollTimer?.Dispose();
        _tooltipPollTimer = null;
        Stop();
    }

    // ─────────────────────────────────────────────────────────────────────
    // WndProc routing — MainForm.WndProc forwards every message; we filter
    // here for our distinct WM_USER id. Swallowing COM / null exceptions
    // mirrors SimConnectManager.ProcessWindowMessage to stay robust during
    // simulator teardown.
    // ─────────────────────────────────────────────────────────────────────
    public void ProcessWindowMessage(ref Message m)
    {
        if (m.Msg == WM_USER_GSX_SIMCONNECT && _simConnect != null)
        {
            try
            {
                _simConnect.ReceiveMessage();
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GsxService] ReceiveMessage COM exception (expected during disconnect): {ex.Message}");
            }
            catch (NullReferenceException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GsxService] ReceiveMessage null reference (expected during disconnect): {ex.Message}");
            }
            catch (AccessViolationException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GsxService] ReceiveMessage access violation ignored: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GsxService] Unexpected exception in ProcessWindowMessage: {ex}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public commands.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Ask GSX to (re)open its menu. Writes L:FSDT_GSX_MENU_OPEN = 1.</summary>
    public void OpenMenu() => SendVariable(DataDefineId.MenuOpen, 1);

    public void HideMenu() => HideMenuInternal();

    public void RefreshTooltip() => ReloadAndPublishTooltip();

    /// <summary>
    /// Ask GSX to open its settings page and publish an accessible mirror.
    /// GSX 4.x no longer opens the old desktop dialog for choice 12; it writes
    /// settings.html for the toolbar panel instead.
    /// </summary>
    public void OpenSettings()
    {
        if (string.IsNullOrWhiteSpace(_settingsFilePath))
            ResolveFsdtPaths();

        // Keep GSX informed so its own current settings transport still runs,
        // but open our accessible mirror immediately. GSX 4.0.2 does not
        // reliably send the settings-open event back to remote-control clients.
        SendVariable(DataDefineId.MenuChoice, ChoiceSettings);
        ReloadAndPublishSettings();
        ScheduleSettingsFallback();
    }

    /// <summary>Submit a menu choice (0..9 for the numbered options, 10..14 for A..E).</summary>
    public void Choose(int choice)
    {
        // Hide locally BEFORE sending the choice — same ordering as the
        // upstream AccessGSX CloseWithChoice(). This fires MenuHidden so the
        // form's textbox switches to the "GSX Menu hidden. Press F5 to open
        // it." prompt the instant the user picks. If GSX subsequently brings
        // up a follow-on menu (submenu, main menu reappears, etc.), the
        // EXTERNAL_SYSTEM_TOGGLE value-1 path will repopulate via
        // ReloadMenuFromFile. Without this local hide, the form keeps showing
        // stale options until GSX explicitly closes — which it sometimes
        // doesn't, leaving the user staring at the option they just picked
        // and pressing F5 twice to recover.
        // GSX Pro 4.x changed the Settings entry (choice 12) from a desktop
        // dialog into an in-panel HTML settings page. The Python side writes
        // settings.html and fires EXTERNAL_SYSTEM_TOGGLE=12. If we clear the
        // local menu first, users hear the hidden prompt and then nothing
        // until that new event is handled, which makes C look broken.
        if (choice == ChoiceSettings)
        {
            OpenSettings();
            return;
        }

        HideMenuInternal();
        SendVariable(DataDefineId.MenuChoice, choice);
    }

    // ─────────────────────────────────────────────────────────────────────
    // SimConnect callbacks.
    // ─────────────────────────────────────────────────────────────────────

    private void HookSimConnectEvents()
    {
        if (_simConnect == null) return;
        _simConnect.OnRecvOpen += OnSimConnectOpen;
        _simConnect.OnRecvQuit += OnSimConnectQuit;
        _simConnect.OnRecvException += OnSimConnectException;
        _simConnect.OnRecvEvent += OnSimConnectEvent;
        _simConnect.OnRecvSimobjectData += OnSimConnectSimObjectData;
    }

    private void OnSimConnectOpen(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_OPEN data)
    {
        System.Diagnostics.Debug.WriteLine("[GsxService] SimConnect channel opened.");
        try
        {
            DefineSimVars();
            MapEvents();
            RequestSimVars();
            CloseToolbarPanel();
            ResolveFsdtPaths();
            SendVariable(DataDefineId.RemoteControl, 1);
            StartTooltipPolling();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GsxService] OnSimConnectOpen failed: {ex.Message}");
        }
    }

    private void OnSimConnectQuit(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV data)
    {
        System.Diagnostics.Debug.WriteLine("[GsxService] Simulator has closed the connection.");
        _statusText = "Status: Simulator disconnected";
        _menuOpen = false;
        _menuOptions.Clear();
        _tooltipPollTimer?.Stop();
        RaiseStateChanged();
        try { _simConnect?.Dispose(); } catch { }
        _simConnect = null;
    }

    private void OnSimConnectException(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
    {
        System.Diagnostics.Debug.WriteLine($"[GsxService] SimConnect exception: {data.dwException}");
    }

    private void OnSimConnectEvent(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EVENT data)
    {
        switch ((EventId)data.uEventID)
        {
            case EventId.ExternalSystemToggle:
                HandleToggleEvent(data.dwData);
                break;
            case EventId.ExternalSystemSet:
                ReloadAndPublishTooltip();
                break;
        }
    }

    private void HandleToggleEvent(uint value)
    {
        // Values defined by the GSX integration protocol (see AccessGSX):
        //  1 — menu requested. If currently open, hide; else reload.
        //  2 — hide menu.
        //  3 — menu timed out (no user choice).
        //  4 — in-sim toolbar panel was closed (informational).
        // 12 — GSX 4.x settings page written to settings.html.
        switch (value)
        {
            case 1:
                if (_menuOpen)
                    HideMenuInternal();
                else
                    ReloadMenuFromFile();
                break;
            case 2:
                HideMenuInternal();
                break;
            case 3:
                // Tell GSX we've abandoned the menu, then surface the timeout
                // to subscribers (form announces "GSX menu timeout").
                SendVariable(DataDefineId.MenuChoice, -1);
                HideMenuInternal();
                MenuTimedOut?.Invoke(this, EventArgs.Empty);
                break;
            case 4:
                System.Diagnostics.Debug.WriteLine("[GsxService] GSX toolbar panel closed.");
                break;
            case 12:
                ReloadAndPublishSettings();
                break;
        }
    }

    private void OnSimConnectSimObjectData(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        switch ((DataRequestId)data.dwRequestID)
        {
            case DataRequestId.RequestCouatlStarted:
            {
                var value = (DoubleValue)data.dwData[0];
                bool wasStarted = _couatlStarted;
                _couatlStarted = value.Value != 0;
                System.Diagnostics.Debug.WriteLine($"[GsxService] COUATL_STARTED = {value.Value}");
                if (wasStarted && !_couatlStarted)
                    ClearLastTooltip();
                UpdateStatusText();
                RaiseStateChanged();
                break;
            }
            case DataRequestId.RequestRemote:
            {
                var value = (DoubleValue)data.dwData[0];
                System.Diagnostics.Debug.WriteLine($"[GsxService] REMOTECONTROL = {value.Value}");
                // GSX sometimes clears the remote-control flag on its own.
                // Re-assert it so menu/tooltip events keep flowing.
                if (Math.Abs(value.Value) < double.Epsilon)
                    SendVariable(DataDefineId.RemoteControl, 1);
                break;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // SimConnect setup (data definitions, event maps, data requests).
    // ─────────────────────────────────────────────────────────────────────

    private void DefineSimVars()
    {
        if (_simConnect == null) return;

        _simConnect.AddToDataDefinition(DataDefineId.CouatlStarted, "L:FSDT_GSX_COUATL_STARTED", "number",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
        _simConnect.AddToDataDefinition(DataDefineId.MenuOpen, "L:FSDT_GSX_MENU_OPEN", "number",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
        _simConnect.AddToDataDefinition(DataDefineId.MenuChoice, "L:FSDT_GSX_MENU_CHOICE", "number",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
        _simConnect.AddToDataDefinition(DataDefineId.RemoteControl, "L:FSDT_GSX_SET_REMOTECONTROL", "number",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);

        _simConnect.RegisterDataDefineStruct<DoubleValue>(DataDefineId.CouatlStarted);
        _simConnect.RegisterDataDefineStruct<DoubleValue>(DataDefineId.MenuOpen);
        _simConnect.RegisterDataDefineStruct<DoubleValue>(DataDefineId.MenuChoice);
        _simConnect.RegisterDataDefineStruct<DoubleValue>(DataDefineId.RemoteControl);
    }

    private void MapEvents()
    {
        if (_simConnect == null) return;

        _simConnect.MapClientEventToSimEvent(EventId.ExternalSystemSet, "EXTERNAL_SYSTEM_SET");
        _simConnect.MapClientEventToSimEvent(EventId.ExternalSystemToggle, "EXTERNAL_SYSTEM_TOGGLE");
        _simConnect.AddClientEventToNotificationGroup(GroupId.MainGroup, EventId.ExternalSystemSet, false);
        _simConnect.AddClientEventToNotificationGroup(GroupId.MainGroup, EventId.ExternalSystemToggle, false);
        _simConnect.SetNotificationGroupPriority(GroupId.MainGroup,
            Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_GROUP_PRIORITY_HIGHEST);
    }

    private void RequestSimVars()
    {
        if (_simConnect == null) return;

        _simConnect.RequestDataOnSimObject(DataRequestId.RequestRemote, DataDefineId.RemoteControl,
            Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_PERIOD.VISUAL_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
        _simConnect.RequestDataOnSimObject(DataRequestId.RequestCouatlStarted, DataDefineId.CouatlStarted,
            Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_PERIOD.VISUAL_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
    }

    private void CloseToolbarPanel()
    {
        // Toggle value 4 tells GSX to close its in-sim toolbar panel so we
        // control the menu externally. Mapping was already done in MapEvents.
        if (_simConnect == null) return;
        _simConnect.TransmitClientEvent(0, EventId.ExternalSystemToggle, 4, GroupId.MainGroup,
            SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
    }

    private void SendVariable(DataDefineId definition, double value)
    {
        if (_simConnect == null) return;
        try
        {
            _simConnect.SetDataOnSimObject(definition,
                Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_DATA_SET_FLAG.DEFAULT, new DoubleValue { Value = value });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GsxService] Failed to set {definition}: {ex.Message}");
        }
    }

    private void ClearLastTooltip()
    {
        if (string.IsNullOrWhiteSpace(_lastTooltip))
            return;

        _lastTooltip = string.Empty;
        _lastStatusStableText = string.Empty;
        TooltipChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSettingNumber(string key, double value)
    {
        if (_simConnect == null || string.IsNullOrWhiteSpace(key)) return;

        string lvarName = "L:FSDT_GSX_SET_" + key.ToUpperInvariant();
        SendDynamicNumber(lvarName, value);
    }

    public void PulseSettingAction(string key)
    {
        SetSettingNumber(key, 1);
    }

    public void SetSettingText(string key, string value)
    {
        if (_simConnect == null || string.IsNullOrWhiteSpace(key)) return;

        string slotName = "SET_S_" + key.ToUpperInvariant();
        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty)).TrimEnd('=');
        int totalLen = Math.Min(base64.Length, StringSlotChunks * StringSlotCharsPerChunk);

        for (int i = 0; i < StringSlotChunks; i++)
        {
            double chunkValue = 0;
            for (int j = 0; j < StringSlotCharsPerChunk; j++)
            {
                int pos = i * StringSlotCharsPerChunk + j;
                if (pos >= totalLen) break;

                int idx = StringSlotBase64Chars.IndexOf(base64[pos]);
                if (idx < 0) continue;
                chunkValue += idx * Math.Pow(64, j);
            }

            SendDynamicNumber($"L:FSDT_GSX_{slotName}_B{i}", chunkValue);
        }

        SendDynamicNumber($"L:FSDT_GSX_{slotName}_LEN", totalLen);
    }

    public void PersistSettingValue(GsxSettingItem item, string value)
    {
        if (string.IsNullOrWhiteSpace(item.Key))
            return;

        if (string.Equals(item.Type, "action", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Type, "info", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            SaveGsxSettingToIni(item, value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GsxService] Failed to persist setting {item.Key}: {ex.Message}");
        }
    }

    private void SendDynamicNumber(string lvarName, double value)
    {
        if (_simConnect == null) return;

        try
        {
            if (!_dynamicSettingDefinitions.TryGetValue(lvarName, out DataDefineId definition))
            {
                definition = (DataDefineId)_nextDynamicSettingDefinition++;
                _simConnect.AddToDataDefinition(definition, lvarName, "number",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
                _simConnect.RegisterDataDefineStruct<DoubleValue>(definition);
                _dynamicSettingDefinitions[lvarName] = definition;
            }

            _simConnect.SetDataOnSimObject(definition,
                Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_DATA_SET_FLAG.DEFAULT, new DoubleValue { Value = value });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GsxService] Failed to set {lvarName}: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // FSDT path resolution + menu / tooltip file reads.
    // ─────────────────────────────────────────────────────────────────────

    private void ResolveFsdtPaths()
    {
        string? fsdtRoot = ReadRegistryValue(FsdtRegistrySubKey, FsdtRegistryValueName);
        if (string.IsNullOrWhiteSpace(fsdtRoot))
        {
            System.Diagnostics.Debug.WriteLine("[GsxService] FSDT root not found in registry. GSX may not be installed.");
            return;
        }

        _menuFilePath = Path.Combine(fsdtRoot, GsxPackageFolder, GsxPackageFolderHtml, GsxMenuFileName);
        _tooltipFilePath = Path.Combine(fsdtRoot, GsxPackageFolder, GsxPackageFolderHtml, GsxTooltipFileName);
        _settingsFilePath = Path.Combine(fsdtRoot, GsxPackageFolder, GsxPackageFolderHtml, GsxSettingsFileName);
        _statusFilePath = Path.Combine(fsdtRoot, GsxPackageFolder, GsxPackageFolderHtml, GsxStatusFileName);
        System.Diagnostics.Debug.WriteLine($"[GsxService] FSDT root: {fsdtRoot}");
    }

    private static string? ReadRegistryValue(string subKey, string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            return key?.GetValue(valueName)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private void ReloadMenuFromFile()
    {
        if (string.IsNullOrWhiteSpace(_menuFilePath))
        {
            System.Diagnostics.Debug.WriteLine("[GsxService] Menu file path not set — GSX paths unresolved.");
            return;
        }

        List<string> lines;
        try
        {
            lines = new List<string>(File.ReadAllLines(_menuFilePath, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GsxService] Failed to read menu file: {ex.Message}");
            return;
        }

        if (lines.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[GsxService] Menu file is empty.");
            return;
        }

        _menuOptions.Clear();
        _menuTitle = lines[0];

        // GSX numbered options 0..9. Line index 1 is displayed as "1", line
        // index 10 wraps around as "0" — matches the in-sim numpad layout.
        for (int i = 1; i < lines.Count; i++)
        {
            int displayNumber = i == 10 ? 0 : i;
            int choice = displayNumber == 0 ? 9 : displayNumber - 1;
            _menuOptions.Add(new MenuOption(displayNumber.ToString(), lines[i], choice));
        }

        // Always-available A..E suffix (choice indices 10..14) — these don't
        // come from the menu file; they're constants exposed by GSX's IPC.
        _menuOptions.Add(new MenuOption("A", "Customize Airport positions...", 10));
        _menuOptions.Add(new MenuOption("B", "Customize Airplane...", 11));
        _menuOptions.Add(new MenuOption("C", "GSX Settings...", 12));
        _menuOptions.Add(new MenuOption("D", "Restart GSX", 13));
        _menuOptions.Add(new MenuOption("E", "Reload Simbrief", 14));

        _menuOpen = true;
        MenuChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HideMenuInternal()
    {
        if (!_menuOpen && _menuOptions.Count == 0) return;
        _menuOpen = false;
        _menuOptions.Clear();
        MenuHidden?.Invoke(this, EventArgs.Empty);
    }

    private void ReloadAndPublishTooltip()
    {
        if (!_couatlStarted)
            return;

        if (TryReadStatusText(out string statusText, out string stableStatusText, out bool statusWasReadable))
        {
            PublishLiveServiceText(statusText, stableStatusText);
            return;
        }

        if (statusWasReadable)
            return;

        if (string.IsNullOrWhiteSpace(_tooltipFilePath))
        {
            System.Diagnostics.Debug.WriteLine("[GsxService] Tooltip file path not set.");
            return;
        }

        string tooltip;
        try
        {
            var lines = File.ReadAllLines(_tooltipFilePath, Encoding.UTF8);
            tooltip = string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GsxService] Failed to read tooltip file: {ex.Message}");
            return;
        }

        tooltip = RemoveUnsupportedLegacyTooltipLines(tooltip);
        if (string.IsNullOrWhiteSpace(tooltip))
        {
            ClearLastTooltip();
            return;
        }

        PublishLiveServiceText(tooltip, tooltip);
    }

    private bool TryReadStatusText(out string statusText, out string stableText, out bool statusWasReadable)
    {
        statusText = string.Empty;
        stableText = string.Empty;
        statusWasReadable = false;

        if (string.IsNullOrWhiteSpace(_statusFilePath))
        {
            System.Diagnostics.Debug.WriteLine("[GsxService] Status file path not set.");
            return false;
        }

        string html;
        try
        {
            html = File.ReadAllText(_statusFilePath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GsxService] Failed to read status file: {ex.Message}");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(html))
            statusWasReadable = true;

        statusText = RenderStatusHtmlAsText(html);
        if (string.IsNullOrWhiteSpace(statusText))
            return false;

        stableText = NormalizeStatusStableText(statusText);
        return !string.IsNullOrWhiteSpace(stableText);
    }

    private void PublishLiveServiceText(
        string text,
        string stableText,
        bool allowTimerOnlyAnnouncements = true)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (IsPlaceholderLiveServiceText(text))
            return;

        if (IsRepeatedBaggageProgress(text))
        {
            _lastTooltip = text;
            _lastStatusStableText = stableText;
            return;
        }

        if (IsRepeatedLiveServiceAnnouncement(stableText))
        {
            _lastTooltip = text;
            _lastStatusStableText = stableText;
            return;
        }

        if (IsThrottledServiceProgress(text))
        {
            _lastTooltip = text;
            _lastStatusStableText = stableText;
            return;
        }

        bool exactDuplicate = string.Equals(text, _lastTooltip, StringComparison.Ordinal);
        bool stableChanged = !string.Equals(stableText, _lastStatusStableText, StringComparison.Ordinal);
        TimeSpan timerOnlyInterval = GetTimerOnlyAnnouncementInterval(text);
        bool timerOnlyChangeAllowed = allowTimerOnlyAnnouncements
            && !exactDuplicate
            && !stableChanged
            && DateTime.UtcNow - _lastTimerOnlyStatusAnnouncementUtc >= timerOnlyInterval;

        if (exactDuplicate || (!stableChanged && !timerOnlyChangeAllowed))
            return;

        _lastTooltip = text;
        _lastStatusStableText = stableText;
        if (timerOnlyChangeAllowed || IsTimerStatusText(text))
            _lastTimerOnlyStatusAnnouncementUtc = DateTime.UtcNow;

        TooltipChanged?.Invoke(this, EventArgs.Empty);

        if (AnnounceWhenFormHidden && !string.IsNullOrWhiteSpace(_lastTooltip))
        {
            try { _announcer.Announce(_lastTooltip); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GsxService] Background announce failed: {ex.Message}");
            }
        }
    }

    private static bool IsPlaceholderLiveServiceText(string text)
    {
        string normalized = NormalizeWhitespace(text);
        return string.Equals(normalized, "tooltip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "gsx tooltip", StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoveUnsupportedLegacyTooltipLines(string tooltip)
    {
        if (string.IsNullOrWhiteSpace(tooltip))
            return string.Empty;

        var keptLines = new List<string>();
        foreach (string line in tooltip.ReplaceLineEndings("\n").Split('\n'))
        {
            string normalized = NormalizeWhitespace(line);
            if (normalized.Length == 0 || IsUnsupportedLegacyGsxTooltipLine(normalized))
                continue;

            keptLines.Add(normalized);
        }

        return string.Join(Environment.NewLine, keptLines);
    }

    private static bool IsUnsupportedLegacyGsxTooltipLine(string line) =>
        IsLegacyGsxPricingTooltipLine(line)
        || IsLegacyGsxCompletionTooltipLine(line)
        || IsLegacyGsxInvoiceTooltipLine(line);

    private static bool IsLegacyGsxPricingTooltipLine(string line)
    {
        string normalized = line.ToLowerInvariant();
        if (!normalized.StartsWith("[gsx]", StringComparison.Ordinal))
            return false;

        bool hasPrice = normalized.Contains("$")
            || normalized.Contains("eur")
            || normalized.Contains("gbp")
            || normalized.Contains("usd")
            || normalized.Contains("€")
            || normalized.Contains("£")
            || normalized.Contains("Ã¢â€šÂ¬")
            || normalized.Contains("Â£");
        if (!hasPrice)
            return false;

        bool looksLikeRateCard = normalized.Contains("/hr")
            || normalized.Contains("per hour")
            || normalized.Contains("while connected")
            || normalized.Contains("connection +")
            || normalized.Contains("connection fee")
            || normalized.Contains("price list")
            || normalized.Contains("rate:");

        return looksLikeRateCard && !IsCompletedStatusService(line);
    }

    private static bool IsLegacyGsxCompletionTooltipLine(string line)
    {
        string normalized = line.ToLowerInvariant();
        if (!normalized.StartsWith("[gsx]", StringComparison.Ordinal))
            return false;

        return normalized.Contains(" complete")
            || normalized.Contains(" completed")
            || normalized.Contains(" has completed")
            || normalized.Contains(" unloading complete")
            || normalized.Contains(" loading complete");
    }

    private static bool IsLegacyGsxInvoiceTooltipLine(string line)
    {
        string normalized = line.ToLowerInvariant();
        if (!normalized.StartsWith("[gsx]", StringComparison.Ordinal))
            return false;

        return normalized.Contains("invoice")
            && normalized.Contains("available")
            && (normalized.Contains("see gsx menu")
                || normalized.Contains("gsx menu")
                || normalized.Contains("invoice from"));
    }

    private bool IsRepeatedLiveServiceAnnouncement(string stableText)
    {
        string key = NormalizeWhitespace(stableText);
        if (key.Length == 0)
            return false;

        string lowerKey = key.ToLowerInvariant();
        if (!lowerKey.Contains("detailed receipt")
            && !lowerKey.Contains("completed")
            && !lowerKey.Contains(" invoice "))
        {
            return false;
        }

        DateTime now = DateTime.UtcNow;
        if (_recentLiveServiceAnnouncements.TryGetValue(key, out DateTime seenAt)
            && now - seenAt < LiveServiceRepeatWindow)
        {
            return true;
        }

        _recentLiveServiceAnnouncements[key] = now;

        foreach (string staleKey in _recentLiveServiceAnnouncements
                     .Where(pair => now - pair.Value >= LiveServiceRepeatWindow)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _recentLiveServiceAnnouncements.Remove(staleKey);
        }

        return false;
    }

    private bool IsThrottledServiceProgress(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (IsCompletedStatusService(text) || IsInvoiceAnnouncement(text))
            return false;

        if (ShouldThrottleFuelingProgress(text))
            return true;

        if (ShouldThrottleBoardingProgress(text))
            return true;

        return false;
    }

    private bool ShouldThrottleFuelingProgress(string text)
    {
        string normalized = text.ToLowerInvariant();
        if (!normalized.Contains("fuel") && !normalized.Contains("refuel"))
            return false;

        if (!normalized.Contains("being performed")
            && !normalized.Contains("in progress")
            && !normalized.Contains("current charges")
            && !normalized.Contains("loaded")
            && !normalized.Contains("uplift"))
        {
            return false;
        }

        string serviceKey = BuildProgressThrottleKey(text, "fueling");

        DateTime now = DateTime.UtcNow;
        if (_lastFuelingProgressAnnouncementByService.TryGetValue(serviceKey, out DateTime seenAt)
            && now - seenAt < FuelingProgressAnnouncementInterval)
        {
            return true;
        }

        _lastFuelingProgressAnnouncementByService[serviceKey] = now;
        return false;
    }

    private bool ShouldThrottleBoardingProgress(string text)
    {
        string normalized = text.ToLowerInvariant();
        if (!normalized.Contains("boarding") && !normalized.Contains("passenger"))
            return false;

        if (!TryParsePassengerCount(text, out int passengers))
            return false;

        string serviceKey = BuildProgressThrottleKey(text, "boarding");
        int passengerMilestone = passengers == 0
            ? 0
            : passengers >= 100
                ? 100
            : passengers / BoardingPassengerAnnouncementInterval;

        if (passengers is not 0 and < 100 && passengerMilestone == 0)
            return true;

        if (_lastBoardingPassengerAnnouncementByService.TryGetValue(serviceKey, out int lastMilestone)
            && passengerMilestone <= lastMilestone)
        {
            return true;
        }

        _lastBoardingPassengerAnnouncementByService[serviceKey] = passengerMilestone;
        return false;
    }

    private static string BuildProgressThrottleKey(string text, string fallbackService)
    {
        string serviceName = ExtractServiceName(text);
        if (string.IsNullOrWhiteSpace(serviceName) || serviceName.Length > 80)
            serviceName = fallbackService;

        string serviceOperator = ExtractServiceOperator(text);
        return string.IsNullOrWhiteSpace(serviceOperator)
            ? serviceName
            : $"{serviceName}|{serviceOperator}";
    }

    private static bool TryParsePassengerCount(string text, out int passengers)
    {
        passengers = 0;

        var match = Regex.Match(
            text,
            @"\b(?<count>\d{1,3})\s*(?:passengers|pax)\b",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;

        passengers = Math.Clamp(int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture), 0, 999);
        return true;
    }

    private static bool IsInvoiceAnnouncement(string text) =>
        NormalizeWhitespace(text).Contains(" invoice ", StringComparison.OrdinalIgnoreCase);

    private static TimeSpan GetTimerOnlyAnnouncementInterval(string text) =>
        IsGroundConnectionService(text)
            ? GroundConnectionTimerAnnouncementInterval
            : TimerOnlyStatusAnnouncementInterval;

    private static bool IsTimerStatusText(string text) =>
        text.Contains("timer:", StringComparison.OrdinalIgnoreCase);

    private bool IsRepeatedBaggageProgress(string tooltip)
    {
        if (!TryParseBaggageProgress(tooltip, out string operation, out int percent))
            return false;

        DateTime now = DateTime.UtcNow;
        if (_lastBaggageProgressByOperation.TryGetValue(operation, out var lastProgress)
            && percent == lastProgress.Percent)
        {
            return true;
        }

        _lastBaggageProgressByOperation[operation] = (percent, now);
        return false;
    }

    private static bool TryParseBaggageProgress(string tooltip, out string operation, out int percent)
    {
        operation = string.Empty;
        percent = 0;

        if (string.IsNullOrWhiteSpace(tooltip))
            return false;

        foreach (string line in tooltip.ReplaceLineEndings("\n").Split('\n'))
        {
            string normalizedLine = line.ToLowerInvariant();
            if (!normalizedLine.Contains("baggage"))
                continue;

            var match = PercentRegex.Match(normalizedLine);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out percent))
                continue;

            if (normalizedLine.Contains("unloading") || normalizedLine.Contains("unloaded"))
                operation = "unloading";
            else if (normalizedLine.Contains("loading") || normalizedLine.Contains("loaded"))
                operation = "loading";
            else
                continue;

            percent = Math.Clamp(percent, 0, 100);
            return true;
        }

        return false;
    }

    private void ReloadAndPublishSettings()
    {
        _settingsFallbackTimer?.Stop();

        if (string.IsNullOrWhiteSpace(_settingsFilePath))
        {
            ResolveFsdtPaths();
            if (string.IsNullOrWhiteSpace(_settingsFilePath))
            {
                System.Diagnostics.Debug.WriteLine("[GsxService] Settings file path not set.");
                PublishSettingsText("GSX Settings requested, but the GSX installation path could not be found.");
                return;
            }
        }

        string html;
        try
        {
            html = File.ReadAllText(_settingsFilePath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GsxService] Failed to read settings file: {ex.Message}");
            PublishSettingsText("GSX Settings requested, but GSX has not written its settings page yet. Try opening the GSX menu with F5, then press C again.");
            return;
        }

        var settingsItems = ParseSettingsHtml(html);
        ApplySavedSettingValues(settingsItems);

        _settingsItems.Clear();
        _settingsItems.AddRange(settingsItems);

        string settingsText = RenderSettingsHtmlAsText(_settingsItems);
        if (string.IsNullOrWhiteSpace(settingsText))
            settingsText = "GSX Settings opened, but no settings could be read.";

        PublishSettingsText(settingsText);
    }

    private void PublishSettingsText(string settingsText)
    {
        _lastSettingsText = settingsText;
        _menuOpen = false;
        _menuOptions.Clear();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleSettingsFallback()
    {
        if (_settingsFallbackTimer == null)
        {
            _settingsFallbackTimer = new System.Windows.Forms.Timer
            {
                Interval = 1000
            };
            _settingsFallbackTimer.Tick += (_, _) =>
            {
                _settingsFallbackTimer?.Stop();
                ReloadAndPublishSettings();
            };
        }

        _settingsFallbackTimer.Stop();
        _settingsFallbackTimer.Start();
    }

    private void StartTooltipPolling()
    {
        if (_tooltipPollTimer == null)
        {
            _tooltipPollTimer = new System.Windows.Forms.Timer
            {
                Interval = 1000
            };
            _tooltipPollTimer.Tick += (_, _) => ReloadAndPublishTooltip();
        }

        _tooltipPollTimer.Stop();
        _tooltipPollTimer.Start();
    }

    private static List<GsxSettingItem> ParseSettingsHtml(string html)
    {
        var items = new List<GsxSettingItem>();
        if (string.IsNullOrWhiteSpace(html))
            return items;

        foreach (Match match in Regex.Matches(
                     html,
                     @"<div\s+class=""(?<class>[^""]*\bgsx-set-field\b[^""]*)""(?<attrs>[^>]*)>(?<body>.*?)</div>\s*</div>",
                     RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string attrs = match.Groups["attrs"].Value;
            string body = match.Groups["body"].Value;
            string type = GetHtmlAttribute(attrs, "data-type");
            string key = GetHtmlAttribute(attrs, "data-key");
            string label = ExtractElementText(body, "gsx-set-label");
            string category = FindSettingsCategory(html, match.Index);
            string value = GetHtmlAttribute(attrs, "data-value");
            string choices = GetHtmlAttribute(attrs, "data-choices");
            string tip = GetHtmlAttribute(attrs, "data-tip");
            string infoValue = ExtractElementText(body, "gsx-set-info-value");
            string buttonText = GetHtmlAttribute(attrs, "data-button");

            if (string.IsNullOrWhiteSpace(label))
                continue;

            items.Add(new GsxSettingItem(
                key,
                label,
                category,
                type,
                value,
                tip,
                TryParseNullableDouble(GetHtmlAttribute(attrs, "data-min")),
                TryParseNullableDouble(GetHtmlAttribute(attrs, "data-max")),
                TryParseNullableDouble(GetHtmlAttribute(attrs, "data-step")),
                GetHtmlAttribute(attrs, "data-unit"),
                ParseChoices(choices),
                infoValue,
                buttonText));
        }

        return items;
    }

    private static string FindSettingsCategory(string html, int fieldIndex)
    {
        var tabTitles = ParseTabTitles(html);
        SettingsSection? best = null;

        foreach (Match match in Regex.Matches(
                     html,
                     @"<section\s+id=""(?<id>[^""]+)""\s+class=""[^""]*\bgsx-set-tab\b[^""]*""[^>]*>",
                     RegexOptions.IgnoreCase))
        {
            if (match.Index > fieldIndex)
                break;

            string id = match.Groups["id"].Value;
            int end = FindSectionEnd(html, match.Index);
            if (end < fieldIndex)
                continue;

            string title = tabTitles.TryGetValue(id, out string? mappedTitle)
                ? mappedTitle
                : HumanizeSettingsSectionId(id);
            best = new SettingsSection(title, match.Index, end);
        }

        return best?.Title ?? "General";
    }

    private static Dictionary<string, string> ParseTabTitles(string html)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(
                     html,
                     @"<tabmenu-item\s+title=""(?<title>[^""]+)""\s+target=""#(?<target>[^""]+)""",
                     RegexOptions.IgnoreCase))
        {
            string title = DecodeHtml(match.Groups["title"].Value);
            string target = match.Groups["target"].Value;
            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(target))
                result[target] = title;
        }

        return result;
    }

    private static int FindSectionEnd(string html, int sectionStart)
    {
        int depth = 0;
        foreach (Match match in Regex.Matches(
                     html[sectionStart..],
                     @"<(?<close>/)?section\b[^>]*>",
                     RegexOptions.IgnoreCase))
        {
            if (match.Groups["close"].Success)
            {
                depth--;
                if (depth == 0)
                    return sectionStart + match.Index + match.Length;
            }
            else
            {
                depth++;
            }
        }

        return html.Length;
    }

    private static string HumanizeSettingsSectionId(string id)
    {
        const string prefix = "gsx-set-tab-";
        string text = id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? id[prefix.Length..]
            : id;
        text = text.Replace('-', ' ').Replace('_', ' ');
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text);
    }

    private sealed record SettingsSection(string Title, int Start, int End);

    private static void ApplySavedSettingValues(List<GsxSettingItem> items)
    {
        if (items.Count == 0)
            return;

        var savedValues = LoadSavedGsxSettings();
        if (savedValues.Count == 0)
            return;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (string.IsNullOrWhiteSpace(item.Key))
                continue;

            if (TryGetSavedSettingValue(item, savedValues, out string? savedValue))
                items[i] = item with { Value = savedValue };
        }
    }

    private static bool TryGetSavedSettingValue(
        GsxSettingItem item,
        Dictionary<string, string> savedValues,
        out string value)
    {
        value = string.Empty;

        var iniTarget = GetIniTarget(item);
        if (savedValues.TryGetValue(iniTarget.Key, out string? savedValue)
            || savedValues.TryGetValue(item.Key, out savedValue))
        {
            value = FormatSettingValueForUi(item, savedValue);
            return true;
        }

        return false;
    }

    private static string FormatSettingValueForUi(GsxSettingItem item, string iniValue)
    {
        if (item.Key.StartsWith("audioDevice_", StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Type, "choice", StringComparison.OrdinalIgnoreCase))
        {
            string normalizedSaved = NormalizeAudioDeviceName(iniValue);
            foreach (var choice in item.Choices)
            {
                if (string.Equals(NormalizeAudioDeviceName(choice.Label), normalizedSaved, StringComparison.OrdinalIgnoreCase))
                    return FormatInvariantNumber(choice.Value);
            }

            return item.Value;
        }

        if (IsPercentStoredAsUnitInterval(item))
        {
            double percent = ParseDouble(iniValue) * 100;
            return FormatInvariantNumber(percent);
        }

        if (string.Equals(item.Key, "ui_volume", StringComparison.OrdinalIgnoreCase))
        {
            double percent = ParseDouble(iniValue) * 10;
            return FormatInvariantNumber(percent);
        }

        return iniValue;
    }

    private static Dictionary<string, string> LoadSavedGsxSettings()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            return result;

        string configPath = Path.Combine(appData, CouatlConfigFolderName, CouatlConfigFileName);
        if (!File.Exists(configPath))
            return result;

        bool inSettingsSection = false;
        try
        {
            foreach (string rawLine in File.ReadLines(configPath, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                    continue;

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    string section = line[1..^1].Trim();
                    inSettingsSection =
                        string.Equals(section, GsxConfigSectionName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(section, CommonConfigSectionName, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inSettingsSection)
                    continue;

                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                string key = line[..equals].Trim();
                string value = line[(equals + 1)..].Trim();
                if (key.Length > 0)
                    result[key] = value;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GsxService] Failed to read saved GSX settings: {ex.Message}");
        }

        return result;
    }

    private static void SaveGsxSettingToIni(GsxSettingItem item, string value)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            return;

        string configPath = Path.Combine(appData, CouatlConfigFolderName, CouatlConfigFileName);
        string configFolder = Path.GetDirectoryName(configPath) ?? appData;
        Directory.CreateDirectory(configFolder);

        var target = GetIniTarget(item);
        string iniValue = FormatSettingValueForIni(item, value);
        var lines = File.Exists(configPath)
            ? File.ReadAllLines(configPath, Encoding.UTF8).ToList()
            : new List<string>();

        int sectionStart = FindIniSectionStart(lines, target.Section);
        if (sectionStart < 0)
        {
            if (lines.Count > 0 && lines[^1].Length > 0)
                lines.Add(string.Empty);

            lines.Add($"[{target.Section}]");
            lines.Add($"{target.Key} = {iniValue}");
            File.WriteAllLines(configPath, lines, Encoding.UTF8);
            return;
        }

        int sectionEnd = FindIniSectionEnd(lines, sectionStart);
        for (int i = sectionStart + 1; i < sectionEnd; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            int equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            string key = line[..equals].Trim();
            if (!string.Equals(key, target.Key, StringComparison.OrdinalIgnoreCase))
                continue;

            string prefix = lines[i][..(lines[i].IndexOf('=') + 1)];
            string spacing = prefix.EndsWith(" ", StringComparison.Ordinal) ? string.Empty : " ";
            lines[i] = $"{prefix}{spacing}{iniValue}";
            File.WriteAllLines(configPath, lines, Encoding.UTF8);
            return;
        }

        lines.Insert(sectionEnd, $"{target.Key} = {iniValue}");
        File.WriteAllLines(configPath, lines, Encoding.UTF8);
    }

    private static int FindIniSectionStart(IReadOnlyList<string> lines, string sectionName)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].Trim();
            if (!line.StartsWith('[') || !line.EndsWith(']'))
                continue;

            string current = line[1..^1].Trim();
            if (string.Equals(current, sectionName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static int FindIniSectionEnd(IReadOnlyList<string> lines, int sectionStart)
    {
        for (int i = sectionStart + 1; i < lines.Count; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
                return i;
        }

        return lines.Count;
    }

    private static IniTarget GetIniTarget(GsxSettingItem item)
    {
        string key = item.Key;
        if (key.StartsWith("audioVolume", StringComparison.OrdinalIgnoreCase))
        {
            return new IniTarget(
                CommonConfigSectionName,
                key.Equals("audioVolume", StringComparison.OrdinalIgnoreCase)
                    ? "audiovolume"
                    : "audiovolume_" + key["audioVolume_".Length..].ToLowerInvariant());
        }

        if (key.StartsWith("audioDevice_", StringComparison.OrdinalIgnoreCase))
        {
            return new IniTarget(
                CommonConfigSectionName,
                "audiodevice_" + key["audioDevice_".Length..].ToLowerInvariant());
        }

        return new IniTarget(GsxConfigSectionName, key);
    }

    private static string FormatSettingValueForIni(GsxSettingItem item, string value)
    {
        if (IsPercentStoredAsUnitInterval(item))
        {
            double scaled = ParseDouble(value) / 100;
            return scaled.ToString("0.00", CultureInfo.InvariantCulture);
        }

        if (string.Equals(item.Key, "ui_volume", StringComparison.OrdinalIgnoreCase))
        {
            double scaled = ParseDouble(value) / 10;
            return FormatInvariantNumber(scaled);
        }

        if (item.Key.StartsWith("audioDevice_", StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Type, "choice", StringComparison.OrdinalIgnoreCase))
        {
            double current = ParseDouble(value);
            string label = item.Choices.FirstOrDefault(choice => Math.Abs(choice.Value - current) < 0.000001)?.Label ?? value;
            return FormatAudioDeviceIniValue(label);
        }

        if (string.Equals(item.Type, "range", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Type, "choice", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Type, "toggle", StringComparison.OrdinalIgnoreCase))
        {
            return FormatInvariantNumber(ParseDouble(value));
        }

        return value ?? string.Empty;
    }

    private static string FormatAudioDeviceIniValue(string label)
    {
        string text = NormalizeAudioDeviceName(label);
        if (text.Contains("no audio", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return text;
    }

    private static string NormalizeAudioDeviceName(string value)
    {
        string text = NormalizeWhitespace(value)
            .Replace("★", string.Empty)
            .Replace("â˜…", string.Empty)
            .Trim();

        text = Regex.Replace(text, @"^Default\s+[—-]\s+", string.Empty, RegexOptions.IgnoreCase);
        return text;
    }

    private static bool IsPercentStoredAsUnitInterval(GsxSettingItem item) =>
        item.Key.StartsWith("audioVolume", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(item.Key, "ui_volume", StringComparison.OrdinalIgnoreCase);

    private static string FormatInvariantNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record IniTarget(string Section, string Key);

    private static string RenderSettingsHtmlAsText(IReadOnlyList<GsxSettingItem> items)
    {
        if (items.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("GSX Settings");
        sb.AppendLine();

        foreach (var item in items)
        {
            sb.Append(item.Label);

            string displayValue = FormatSettingsValue(item);
            if (!string.IsNullOrWhiteSpace(displayValue))
                sb.Append(": ").Append(displayValue);

            if (!string.IsNullOrWhiteSpace(item.Key))
                sb.Append(" (").Append(item.Key).Append(')');

            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(item.Tip))
                sb.Append("  ").AppendLine(NormalizeWhitespace(item.Tip));
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatSettingsValue(GsxSettingItem item)
    {
        if (string.Equals(item.Type, "toggle", StringComparison.OrdinalIgnoreCase))
            return ParseDouble(item.Value) != 0 ? "On" : "Off";

        if (string.Equals(item.Type, "choice", StringComparison.OrdinalIgnoreCase))
        {
            double current = ParseDouble(item.Value);
            var choice = item.Choices.FirstOrDefault(c => Math.Abs(c.Value - current) < 0.000001);
            return choice?.Label ?? item.Value;
        }

        if (string.Equals(item.Type, "info", StringComparison.OrdinalIgnoreCase))
            return item.InfoValue;

        if (string.Equals(item.Type, "action", StringComparison.OrdinalIgnoreCase))
            return "Action";

        return DecodeHtml(item.Value);
    }

    private static double ParseDouble(string value) =>
        TryParseBooleanLike(value, out double booleanValue)
            ? booleanValue
            :
        double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 0;

    private static bool TryParseBooleanLike(string value, out double parsed)
    {
        parsed = 0;
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
        {
            parsed = 1;
            return true;
        }

        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static double? TryParseNullableDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;

    private static IReadOnlyList<GsxSettingChoice> ParseChoices(string choices)
    {
        var result = new List<GsxSettingChoice>();
        foreach (string part in choices.Split("||", StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pieces = part.Split('|');
            if (pieces.Length < 2) continue;
            if (!double.TryParse(pieces[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                continue;
            result.Add(new GsxSettingChoice(value, DecodeHtml(pieces[1])));
        }
        return result;
    }

    private static string GetHtmlAttribute(string text, string name)
    {
        var match = Regex.Match(text, $@"\b{Regex.Escape(name)}=""(?<value>[^""]*)""",
            RegexOptions.IgnoreCase);
        return match.Success ? DecodeHtml(match.Groups["value"].Value) : string.Empty;
    }

    private static string ExtractElementText(string html, string className)
    {
        var match = Regex.Match(
            html,
            $@"<[^>]*class=""[^""]*\b{Regex.Escape(className)}\b[^""]*""[^>]*>(?<value>.*?)</[^>]+>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!match.Success)
            return string.Empty;

        string withoutTags = Regex.Replace(match.Groups["value"].Value, "<.*?>", " ",
            RegexOptions.Singleline);
        return NormalizeWhitespace(DecodeHtml(withoutTags));
    }

    private static string DecodeHtml(string value) =>
        System.Net.WebUtility.HtmlDecode(value ?? string.Empty);

    private static string NormalizeWhitespace(string value) =>
        Regex.Replace(value.ReplaceLineEndings(" "), @"\s+", " ").Trim();

    // ─────────────────────────────────────────────────────────────────────
    // Status text + event raise helpers.
    // ─────────────────────────────────────────────────────────────────────

    private string RenderStatusHtmlAsText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var receiptInvoices = ExtractReceiptInvoices(html);
        bool hasReceiptData = HasStatusReceiptData(html) || receiptInvoices.Count > 0;
        var receiptChargeRows = ExtractReceiptChargeRows(html);
        string withoutReceiptData = Regex.Replace(
            html,
            @"<span\s+class=""[^""]*\bgsx-receipt-data\b[^""]*""[^>]*>.*?</span>",
            " ",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        var activeRows = new List<StatusServiceRow>();
        var completedRows = new List<StatusServiceRow>();
        var chargeRows = ExtractStatusChargeRows(withoutReceiptData)
            .Concat(receiptChargeRows)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (Match match in Regex.Matches(
                     withoutReceiptData,
                     @"<div\s+class=""(?<class>[^""]*\bstatus-service\b[^""]*)""[^>]*>(?<body>.*?)</div>\s*</div>",
                     RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string cssClass = match.Groups["class"].Value;
            string line = RenderStatusFragmentAsText(match.Groups["body"].Value);
            if (line.Length == 0)
                continue;

            string serviceName = ExtractServiceName(line);
            string serviceOperator = ExtractServiceOperator(line);
            if (!string.IsNullOrWhiteSpace(serviceName) && !string.IsNullOrWhiteSpace(serviceOperator))
                _lastServiceOperatorByName[serviceName] = serviceOperator;

            var row = new StatusServiceRow(
                line,
                IsCompletedStatusService(line),
                IsStartedStatusService(cssClass, line));

            if (row.IsCompleted)
                completedRows.Add(row);
            else
                activeRows.Add(row);
        }

        StatusServiceRow? latestCompletedRow = completedRows.TakeLast(1).FirstOrDefault();
        if (receiptInvoices.Count > 0)
        {
            string receiptAnnouncement = FormatReceiptInvoiceAnnouncement(receiptInvoices);
            if (!string.IsNullOrWhiteSpace(receiptAnnouncement))
                return receiptAnnouncement;

            return string.Empty;
        }

        bool hasInvoiceRows = chargeRows.Any(IsInvoiceChargeRow);
        if (hasReceiptData && hasInvoiceRows)
        {
            string invoiceAnnouncement = FormatInvoiceAnnouncement(activeRows, completedRows, chargeRows);
            if (!string.IsNullOrWhiteSpace(invoiceAnnouncement))
                return invoiceAnnouncement;

            return string.Empty;
        }

        bool shouldSpeakCompletedRow = latestCompletedRow is not null
            && !string.IsNullOrWhiteSpace(_lastCompletedStatusServiceText)
            && !string.Equals(latestCompletedRow.Text, _lastCompletedStatusServiceText, StringComparison.Ordinal);

        if (latestCompletedRow is not null)
            _lastCompletedStatusServiceText = latestCompletedRow.Text;

        if (!shouldSpeakCompletedRow)
        {
            string groundConnectionTimerText = FormatGroundConnectionTimerAnnouncement(activeRows, completedRows, chargeRows);
            if (!string.IsNullOrWhiteSpace(groundConnectionTimerText))
                return groundConnectionTimerText;
        }

        StatusServiceRow? rowToSpeak = shouldSpeakCompletedRow
            ? latestCompletedRow
            : activeRows.Count > 0
                ? activeRows[0]
                : latestCompletedRow;
        if (rowToSpeak is null)
            return string.Empty;

        var rowsToSpeak = new List<string> { rowToSpeak.Text };
        if (rowToSpeak.IsCompleted)
        {
            string total = FormatCompletedServiceTotal(rowToSpeak.Text, chargeRows);
            rowsToSpeak.Clear();
            rowsToSpeak.Add(FormatCompletedServiceAnnouncement(rowToSpeak.Text, total));

            if (!hasInvoiceRows && ShouldMentionReceipt(rowToSpeak.Text, chargeRows, hasReceiptData))
                rowsToSpeak.Add("A detailed receipt is available in the relevant GSX receipts folder.");
        }
        else if (rowToSpeak.HasStarted && chargeRows.Count > 0)
        {
            var currentChargeRows = FindMatchingChargeRows(rowToSpeak.Text, chargeRows);
            if (currentChargeRows.Count > 0)
            {
                rowsToSpeak.Add("Current charges:");
                rowsToSpeak.AddRange(currentChargeRows);
            }
        }

        return string.Join(Environment.NewLine, DeduplicateStatusRows(rowsToSpeak));
    }

    private sealed record StatusServiceRow(string Text, bool IsCompleted, bool HasStarted);
    private sealed record ReceiptInvoice(string ServiceName, string OperatorName, string Total, string Key);

    private string FormatGroundConnectionTimerAnnouncement(
        IReadOnlyList<StatusServiceRow> activeRows,
        IReadOnlyList<StatusServiceRow> completedRows,
        IReadOnlyList<string> chargeRows)
    {
        if (DateTime.UtcNow - _lastTimerOnlyStatusAnnouncementUtc < GroundConnectionTimerAnnouncementInterval)
            return string.Empty;

        var rowsToSpeak = new List<string>();
        foreach (var row in activeRows
                     .Concat(completedRows)
                     .Where(row => IsGroundConnectionService(row.Text)))
        {
            var timerRows = FindMatchingChargeRows(row.Text, chargeRows)
                .Where(IsTimerStatusLine)
                .ToList();
            if (timerRows.Count == 0)
                continue;

            rowsToSpeak.Add(row.Text);
            rowsToSpeak.AddRange(timerRows);
        }

        return rowsToSpeak.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, DeduplicateStatusRows(rowsToSpeak));
    }

    private static List<string> ExtractStatusChargeRows(string html)
    {
        var rows = new List<string>();
        foreach (Match match in Regex.Matches(
                     html,
                     @"<div\s+class=""(?<class>[^""]*\bstatus-line\b[^""]*)""[^>]*>(?<body>.*?)</div>",
                     RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string cssClass = match.Groups["class"].Value;
            if (Regex.IsMatch(cssClass, @"\bstatus-service\b", RegexOptions.IgnoreCase))
                continue;

            string line = RenderStatusFragmentAsText(match.Groups["body"].Value);
            if (line.Length > 0 && IsChargeStatusLine(line))
                rows.Add(line);
        }

        return rows;
    }

    private static bool HasStatusReceiptData(string html) =>
        Regex.IsMatch(
            html,
            @"<span\s+class=""[^""]*\bgsx-receipt-data\b[^""]*""[^>]*>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static string RenderStatusFragmentAsText(string html)
    {
        string text = Regex.Replace(html, @"<\s*br\s*/?\s*>", "\n",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</\s*(?:div|p|li|tr|h[1-6]|section|span)\s*>", "\n",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<.*?>", " ", RegexOptions.Singleline);
        text = DecodeHtml(text)
            .Replace("\u2022", ",")
            .Replace("\u20ac", "EUR ")
            .Replace("â‚¬", "EUR ")
            .Replace("Â£", "GBP ")
            .Replace("Â²", " squared")
            .Replace("\u00a0", " ");

        var parts = new List<string>();
        foreach (string rawLine in text.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = NormalizeWhitespace(rawLine);
            if (line.Length > 0)
                parts.Add(line);
        }

        return NormalizeWhitespace(string.Join(", ", parts));
    }

    private static bool IsCompletedStatusService(string text)
    {
        string normalized = text.ToLowerInvariant();
        return normalized.Contains("has been completed")
            || normalized.Contains("service completed")
            || normalized.Contains("operation completed");
    }

    private static bool IsStartedStatusService(string cssClass, string text)
    {
        if (IsCompletedStatusService(text))
            return false;

        string normalizedClass = cssClass.ToLowerInvariant();
        string normalizedText = text.ToLowerInvariant();
        return normalizedClass.Contains("gsx-state-performed")
            || normalizedText.Contains("is being performed")
            || normalizedText.Contains("connected")
            || normalizedText.Contains("started")
            || normalizedText.Contains("in progress");
    }

    private static bool IsChargeStatusLine(string text)
    {
        string normalized = text.ToLowerInvariant();
        return normalized.Contains("timer:")
            || normalized.Contains("invoice:")
            || normalized.Contains("eur ")
            || normalized.Contains("â‚¬")
            || normalized.Contains("$")
            || normalized.Contains("£")
            || ContainsMoneyAmount(text);
    }

    private static bool ContainsMoneyAmount(string text) =>
        Regex.IsMatch(
            text,
            $@"{CurrencyTokenPattern}\s*\d|\d[\d,.]*\s*{CurrencyTokenPattern}",
            RegexOptions.IgnoreCase);

    private static bool IsTimerStatusLine(string text) =>
        text.Contains("timer:", StringComparison.OrdinalIgnoreCase);

    private static string FormatCompletedServiceTotal(string serviceText, IReadOnlyList<string> chargeRows)
    {
        if (chargeRows.Count == 0)
            return string.Empty;

        string? matchingCharge = FindMatchingChargeRow(serviceText, chargeRows);
        if (string.IsNullOrWhiteSpace(matchingCharge) && IsInvoiceGeneratingService(serviceText))
            matchingCharge = chargeRows.LastOrDefault();

        if (string.IsNullOrWhiteSpace(matchingCharge))
            return string.Empty;

        string total = ExtractMoneySummary(matchingCharge);
        if (string.IsNullOrWhiteSpace(total))
            total = matchingCharge;

        return "Total: " + total;
    }

    private string FormatCompletedServiceAnnouncement(string serviceText, string totalText)
    {
        string serviceName = ExtractServiceName(serviceText);
        string serviceOperator = ExtractServiceOperator(serviceText);
        if (IsGroundConnectionService(serviceText))
            return $"{serviceName} disconnected.";

        if (string.IsNullOrWhiteSpace(serviceOperator)
            && !string.IsNullOrWhiteSpace(serviceName)
            && _lastServiceOperatorByName.TryGetValue(serviceName, out string? cachedOperator))
        {
            serviceOperator = cachedOperator;
        }

        string totalPhrase = string.IsNullOrWhiteSpace(totalText)
            ? string.Empty
            : NormalizeWhitespace(totalText).Replace("Total:", "total", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(serviceOperator))
        {
            return string.IsNullOrWhiteSpace(totalPhrase)
                ? $"{serviceName} available from {serviceOperator}."
                : $"{serviceName} available from {serviceOperator}, {totalPhrase}.";
        }

        return string.IsNullOrWhiteSpace(totalPhrase)
            ? $"{serviceName} completed."
            : $"{serviceName} completed, {totalPhrase}.";
    }

    private string FormatInvoiceAnnouncement(
        IReadOnlyList<StatusServiceRow> activeRows,
        IReadOnlyList<StatusServiceRow> completedRows,
        IReadOnlyList<string> chargeRows)
    {
        if (!chargeRows.Any(IsInvoiceChargeRow))
            return string.Empty;

        var serviceRows = completedRows
            .Where(row => IsInvoiceGeneratingService(row.Text))
            .Reverse()
            .Concat(activeRows
                .Where(row => IsInvoiceGeneratingService(row.Text))
                .Reverse())
            .ToList();

        StatusServiceRow? serviceRow = null;
        string invoiceRow = string.Empty;
        foreach (var candidate in serviceRows)
        {
            string? matchingInvoice = FindMatchingChargeRows(candidate.Text, chargeRows)
                .LastOrDefault(IsInvoiceChargeRow);
            if (string.IsNullOrWhiteSpace(matchingInvoice))
                continue;

            serviceRow = candidate;
            invoiceRow = matchingInvoice;
            break;
        }

        if (serviceRow is null || string.IsNullOrWhiteSpace(invoiceRow))
        {
            invoiceRow = chargeRows.LastOrDefault(IsInvoiceChargeRow)
                ?? chargeRows.LastOrDefault(row => !string.IsNullOrWhiteSpace(ExtractMoneySummary(row)))
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(invoiceRow))
                return string.Empty;

            serviceRow = activeRows
                .Concat(completedRows)
                .LastOrDefault(row => IsInvoiceGeneratingService(row.Text));
        }

        string serviceName = serviceRow is null
            ? InferInvoiceServiceName(invoiceRow)
            : ExtractServiceName(serviceRow.Text);
        string serviceOperator = serviceRow is null
            ? ExtractServiceOperator(invoiceRow)
            : ExtractServiceOperator(serviceRow.Text);
        if (string.IsNullOrWhiteSpace(serviceOperator)
            && !string.IsNullOrWhiteSpace(serviceName)
            && _lastServiceOperatorByName.TryGetValue(serviceName, out string? cachedOperator))
        {
            serviceOperator = cachedOperator;
        }

        string total = ExtractMoneySummary(invoiceRow);
        if (string.IsNullOrWhiteSpace(total))
            total = invoiceRow;

        string invoiceKey = NormalizeWhitespace($"{serviceName}|{serviceOperator}|{total}|{invoiceRow}");
        if (!_announcedInvoiceKeys.Add(invoiceKey))
            return string.Empty;

        string operatorPhrase = string.IsNullOrWhiteSpace(serviceOperator)
            ? string.Empty
            : $" from {serviceOperator}";

        return $"{serviceName} invoice available{operatorPhrase}. Total {NormalizeWhitespace(total)}. More information can be found by viewing the invoice.";
    }

    private string FormatReceiptInvoiceAnnouncement(IReadOnlyList<ReceiptInvoice> receiptInvoices)
    {
        foreach (var receipt in receiptInvoices.Reverse())
        {
            string invoiceKey = NormalizeWhitespace(receipt.Key);
            if (string.IsNullOrWhiteSpace(invoiceKey) || !_announcedInvoiceKeys.Add(invoiceKey))
                continue;

            string operatorPhrase = string.IsNullOrWhiteSpace(receipt.OperatorName)
                ? string.Empty
                : $" from {receipt.OperatorName}";
            string totalPhrase = string.IsNullOrWhiteSpace(receipt.Total)
                ? "the generated total"
                : NormalizeWhitespace(receipt.Total);

            return $"{receipt.ServiceName} invoice available{operatorPhrase}. Total {totalPhrase}. More information can be found by viewing the invoice.";
        }

        return string.Empty;
    }

    private static List<ReceiptInvoice> ExtractReceiptInvoices(string html)
    {
        var invoices = new List<ReceiptInvoice>();
        foreach (Match match in Regex.Matches(
                     html,
                     @"<span\s+class=""[^""]*\bgsx-receipt-data\b[^""]*""(?<attrs>[^>]*)>",
                     RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string attrs = match.Groups["attrs"].Value;
            string operatorName = GetHtmlAttribute(attrs, "data-operator");
            string path = GetHtmlAttribute(attrs, "data-path");
            if (string.IsNullOrWhiteSpace(path))
                continue;

            string serviceName = InferReceiptServiceName(path);
            string total = string.Empty;
            try
            {
                if (File.Exists(path))
                {
                    string receiptHtml = File.ReadAllText(path, Encoding.UTF8);
                    serviceName = InferReceiptServiceName(path, receiptHtml);
                    if (string.IsNullOrWhiteSpace(operatorName))
                        operatorName = ExtractReceiptOperator(receiptHtml);
                    total = ExtractReceiptTotal(receiptHtml);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GsxService] Failed to read GSX receipt {path}: {ex.Message}");
            }

            invoices.Add(new ReceiptInvoice(
                NormalizeWhitespace(serviceName),
                NormalizeWhitespace(operatorName),
                NormalizeWhitespace(total),
                path));
        }

        return invoices;
    }

    private static string InferReceiptServiceName(string path, string receiptHtml = "")
    {
        string title = ExtractHtmlTitle(receiptHtml);
        var titleMatch = Regex.Match(title, @"^\s*(?<service>[A-Z ]+?)\s+RECEIPT\b",
            RegexOptions.IgnoreCase);
        if (titleMatch.Success)
            return HumanizeReceiptService(titleMatch.Groups["service"].Value);

        string folderName = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
        return HumanizeReceiptService(folderName);
    }

    private static string HumanizeReceiptService(string value)
    {
        string normalized = NormalizeWhitespace(value);
        if (normalized.Length == 0)
            return "Service";

        if (normalized.Equals("fuel", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("refuel", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("refueling", StringComparison.OrdinalIgnoreCase))
        {
            return "Fueling";
        }

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private static string ExtractHtmlTitle(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var match = Regex.Match(html, @"<title[^>]*>(?<title>.*?)</title>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return match.Success ? RenderStatusFragmentAsText(match.Groups["title"].Value) : string.Empty;
    }

    private static string ExtractReceiptOperator(string html)
    {
        string title = ExtractHtmlTitle(html);
        var match = Regex.Match(title, @"\bRECEIPT\s*-\s*(?<operator>.+)$",
            RegexOptions.IgnoreCase);
        if (match.Success)
            return NormalizeWhitespace(match.Groups["operator"].Value);

        match = Regex.Match(
            html,
            @"<div\s+class=""name""[^>]*>(?<operator>.*?)</div>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return match.Success ? RenderStatusFragmentAsText(match.Groups["operator"].Value) : string.Empty;
    }

    private static string ExtractReceiptTotal(string html)
    {
        var match = Regex.Match(
            html,
            @"<tr\s+class=""[^""]*\btotal\b[^""]*""[^>]*>.*?<td\s+class=""[^""]*\bamount\b[^""]*""[^>]*>(?<amount>.*?)</td>.*?</tr>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!match.Success)
            return string.Empty;

        string totalText = RenderStatusFragmentAsText(match.Groups["amount"].Value);
        string money = ExtractMoneySummary(totalText);
        return string.IsNullOrWhiteSpace(money) ? totalText : money;
    }

    private static List<string> ExtractReceiptChargeRows(string html)
    {
        var rows = new List<string>();
        foreach (Match match in Regex.Matches(
                     html,
                     @"<span\s+class=""[^""]*\bgsx-receipt-data\b[^""]*""[^>]*>(?<body>.*?)</span>",
                     RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string line = RenderStatusFragmentAsText(match.Groups["body"].Value);
            if (line.Length > 0 && IsChargeStatusLine(line))
                rows.Add(line);
        }

        return rows;
    }

    private static bool ShouldMentionReceipt(string serviceText, IReadOnlyList<string> chargeRows, bool hasReceiptData)
    {
        if (!hasReceiptData)
            return false;

        if (!IsInvoiceGeneratingService(serviceText))
            return false;

        string? matchingCharge = FindMatchingChargeRow(serviceText, chargeRows);
        return string.IsNullOrWhiteSpace(matchingCharge)
            || IsInvoiceChargeRow(matchingCharge);
    }

    private static bool IsInvoiceGeneratingService(string serviceText)
    {
        string normalized = serviceText.ToLowerInvariant();
        if (IsGroundConnectionService(serviceText))
        {
            return false;
        }

        return normalized.Contains("catering")
            || normalized.Contains("fuel")
            || normalized.Contains("refuel")
            || normalized.Contains("baggage")
            || normalized.Contains("cargo")
            || normalized.Contains("boarding")
            || normalized.Contains("deboarding")
            || normalized.Contains("handling");
    }

    private static bool IsGroundConnectionService(string serviceText)
    {
        string normalized = serviceText.ToLowerInvariant();
        return normalized.Contains("jetway")
            || normalized.Contains("operatejetways")
            || normalized.Contains("gpu")
            || normalized.Contains("ground power");
    }

    private static bool IsInvoiceChargeRow(string chargeRow)
    {
        string normalized = chargeRow.ToLowerInvariant();
        return normalized.Contains("invoice:")
            || normalized.Contains("catering")
            || normalized.Contains("fuel")
            || normalized.Contains("refuel")
            || normalized.Contains("ground handling")
            || normalized.Contains("handling")
            || normalized.Contains("baggage")
            || normalized.Contains("cargo")
            || normalized.Contains("boarding")
            || normalized.Contains("deboarding")
            || normalized.Contains("de-icing")
            || normalized.Contains("deicing");
    }

    private static string InferInvoiceServiceName(string invoiceRow)
    {
        string normalized = invoiceRow.ToLowerInvariant();
        if (normalized.Contains("fuel") || normalized.Contains("refuel"))
            return "Fueling";
        if (normalized.Contains("catering"))
            return "Catering";
        if (normalized.Contains("de-ic") || normalized.Contains("deic"))
            return "De-icing";
        if (normalized.Contains("baggage") || normalized.Contains("cargo")
            || normalized.Contains("handling") || normalized.Contains("boarding")
            || normalized.Contains("deboarding"))
            return "Handling";

        return "Service";
    }

    private static string ExtractServiceName(string serviceText)
    {
        var match = Regex.Match(
            serviceText,
            @"^\s*(?<service>.+?)\s+service\b",
            RegexOptions.IgnoreCase);

        string serviceName = match.Success ? match.Groups["service"].Value : serviceText;
        serviceName = Regex.Replace(serviceName, @"\bOperateJetways\b", "Jetways", RegexOptions.IgnoreCase);
        return NormalizeWhitespace(serviceName);
    }

    private static string ExtractServiceOperator(string serviceText)
    {
        var match = Regex.Match(
            serviceText,
            @"\b(?:will be provided by|provided by|is being performed by|performed by|from|by)\s+(?<operator>[^,]+)",
            RegexOptions.IgnoreCase);

        return match.Success ? NormalizeWhitespace(match.Groups["operator"].Value) : string.Empty;
    }

    private static string? FindMatchingChargeRow(string serviceText, IReadOnlyList<string> chargeRows)
    {
        return FindMatchingChargeRows(serviceText, chargeRows).FirstOrDefault();
    }

    private static List<string> FindMatchingChargeRows(string serviceText, IReadOnlyList<string> chargeRows)
    {
        var rows = new List<string>();
        foreach (string keyword in GetServiceChargeKeywords(serviceText))
        {
            foreach (string row in chargeRows)
            {
                if (row.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    && !rows.Any(existing => string.Equals(existing, row, StringComparison.Ordinal)))
                {
                    rows.Add(row);
                }
            }
        }

        return rows;
    }

    private static IEnumerable<string> GetServiceChargeKeywords(string serviceText)
    {
        string normalized = serviceText.ToLowerInvariant();

        if (normalized.Contains("gpu") || normalized.Contains("ground power"))
        {
            yield return "ground power";
            yield return "gpu";
        }

        if (normalized.Contains("jetway"))
            yield return "jetway";

        if (normalized.Contains("catering"))
            yield return "catering";

        if (normalized.Contains("de-ic") || normalized.Contains("deic"))
        {
            yield return "de-icing";
            yield return "deicing";
        }

        if (normalized.Contains("fuel") || normalized.Contains("refuel"))
        {
            yield return "fuel";
            yield return "refuel";
        }

        if (normalized.Contains("baggage") || normalized.Contains("cargo")
            || normalized.Contains("boarding") || normalized.Contains("deboarding")
            || normalized.Contains("handling"))
        {
            yield return "ground handling";
            yield return "baggage";
            yield return "cargo";
        }

        if (normalized.Contains("pushback"))
            yield return "pushback";
    }

    private static string ExtractMoneySummary(string text)
    {
        var matches = Regex.Matches(
            text,
            $@"{CurrencyTokenPattern}\s*\d[\d,.]*(?:\s*\(~?\s*{CurrencyTokenPattern}?\s*\d[\d,.]*\))?|\d[\d,.]*\s*{CurrencyTokenPattern}",
            RegexOptions.IgnoreCase);

        if (matches.Count == 0)
            return string.Empty;

        return NormalizeWhitespace(matches[0].Value);
    }

    private static IEnumerable<string> DeduplicateStatusRows(IEnumerable<string> rows)
    {
        string previous = string.Empty;
        foreach (string row in rows)
        {
            if (string.Equals(row, previous, StringComparison.Ordinal))
                continue;

            yield return row;
            previous = row;
        }
    }

    private static string NormalizeStatusStableText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string stable = text;
        stable = Regex.Replace(stable, @"\b\d{1,2}:\d{2}(?::\d{2})?\b", "<time>");
        stable = Regex.Replace(stable, @"\b\d+(?:[.,]\d+)?\s*(?:sec|secs|seconds|min|mins|minutes)\b", "<duration>",
            RegexOptions.IgnoreCase);
        stable = Regex.Replace(stable, $@"{CurrencyTokenPattern}\s*\d+(?:[.,]\d+)?|\d+(?:[.,]\d+)?\s*{CurrencyTokenPattern}", "<price>",
            RegexOptions.IgnoreCase);
        stable = Regex.Replace(stable, @"\(~?\s*<price>\)", "(<price>)",
            RegexOptions.IgnoreCase);
        return NormalizeWhitespace(stable);
    }

    private void UpdateStatusText()
    {
        var sb = new StringBuilder();
        sb.Append("Status: ");
        sb.Append(_simConnect != null ? "Connected" : "Disconnected");
        if (_simConnect != null)
            sb.Append(_couatlStarted ? " | Couatl started" : " | Couatl not started");
        _statusText = sb.ToString();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
