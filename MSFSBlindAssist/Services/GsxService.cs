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
using System.Windows.Forms;
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

    // ─────────────────────────────────────────────────────────────────────
    // Public surface — used by AccessGSXForm and MainForm.
    // ─────────────────────────────────────────────────────────────────────

    public bool IsConnected => _simConnect != null;
    public bool CouatlStarted => _couatlStarted;
    public string StatusText => _statusText;
    public string MenuTitle => _menuTitle;
    public IReadOnlyList<MenuOption> MenuOptions => _menuOptions;
    public string LastTooltip => _lastTooltip;

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
    private string _statusText = "Status: Disconnected";
    private string? _menuFilePath;
    private string? _tooltipFilePath;
    private readonly List<MenuOption> _menuOptions = new();

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
        }
    }

    private void OnSimConnectSimObjectData(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        switch ((DataRequestId)data.dwRequestID)
        {
            case DataRequestId.RequestCouatlStarted:
            {
                var value = (DoubleValue)data.dwData[0];
                _couatlStarted = value.Value != 0;
                System.Diagnostics.Debug.WriteLine($"[GsxService] COUATL_STARTED = {value.Value}");
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
        if (string.IsNullOrWhiteSpace(_tooltipFilePath))
        {
            System.Diagnostics.Debug.WriteLine("[GsxService] Tooltip file path not set.");
            return;
        }

        try
        {
            var lines = File.ReadAllLines(_tooltipFilePath, Encoding.UTF8);
            _lastTooltip = string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GsxService] Failed to read tooltip file: {ex.Message}");
            return;
        }

        TooltipChanged?.Invoke(this, EventArgs.Empty);

        // Background-monitoring policy: when the AccessGSX form is hidden,
        // speak the tooltip ourselves so the pilot hears GSX progress
        // (boarding complete, fuel ready, pushback callouts) without having
        // to keep the form on top. The form re-enables this via
        // AnnounceWhenFormHidden when it hides. Menu announcements stay
        // off in background mode — too verbose.
        if (AnnounceWhenFormHidden && !string.IsNullOrWhiteSpace(_lastTooltip))
        {
            try { _announcer.Announce(_lastTooltip); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GsxService] Background announce failed: {ex.Message}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Status text + event raise helpers.
    // ─────────────────────────────────────────────────────────────────────

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
