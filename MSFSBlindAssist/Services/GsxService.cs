// GsxService — facade over the GSX Ground Services Pro accessibility
// integration. Two independent transports feed it:
//
//   1. The Couatl Remote API (WebSocket JSON, ws://127.0.0.1:8744/) via
//      GsxRemoteConnection/GsxRemoteState in MSFSBlindAssist.Services.Gsx.Remote.
//      This is the PRIMARY transport: menu, tooltip ("message"), services,
//      settings, billing and receipts all arrive here as structured, typed
//      data instead of scraped GSX HTML files. IsConnected/RemoteApiAvailable
//      report THIS transport's reachability.
//   2. A small, independent SimConnect client (HWND-based, WM_USER 0x0403)
//      retained ONLY for the read-only FSDT_GSX_SetGate_* confirmation
//      L-vars that GsxGateSelector (a separate, later gate-selection feature)
//      depends on. Nothing else in this file touches SimConnect any more —
//      menu, tooltip, status and settings all moved to the Remote API.
//
// All speech is routed through MSFSBA's existing ScreenReaderAnnouncer; no
// Tolk is loaded here.
//
// Ported from the AccessGSX project (https://github.com/jfayre/access-gsx)
// with permission of the author (both projects are GPL v3); the Remote API
// transport is GSX's own first-party protocol, not part of that port.
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.FlightSimulator.SimConnect;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services.Gsx.Remote;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Facade over the GSX Ground Services Pro accessibility integration. Mirrors
/// GSX's menu/services/settings/billing state from the Couatl Remote API,
/// retains a small independent SimConnect client for the SetGate_* read-only
/// confirmation L-vars, and exposes events the UI form (AccessGSXForm) and
/// MainForm's background hook subscribe to.
/// </summary>
public sealed class GsxService : IDisposable
{
    // Distinct WM_USER message id — the main SimConnect uses 0x0402, this
    // one uses 0x0403 so both clients' ReceiveMessage calls are dispatched
    // correctly from MainForm.WndProc.
    public const int WM_USER_GSX_SIMCONNECT = 0x0403;

    private const string CouatlConfigFolderName = "Virtuali";
    private const string CouatlConfigFileName = "CouatlAddons.ini";

    // SimConnect identifiers — SetGate_* confirmation reads only. Every other
    // definition/request the OLD file registered (menu open/choice, remote
    // control, Couatl-started) moved to the Remote API and was removed.
    private enum DataRequestId
    {
        RequestSetGateName,
        RequestSetGateNumber,
        RequestSetGateSuffix,
    }

    private enum DataDefineId
    {
        SetGateName,
        SetGateNumber,
        SetGateSuffix,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DoubleValue
    {
        public double Value;
    }

    public sealed record MenuOption(string Key, string Text, int Choice);

    // ─────────────────────────────────────────────────────────────────────
    // Public surface — used by AccessGSXForm, GsxSettingsForm and MainForm.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>True when the Couatl Remote API socket is connected. Same value as
    /// <see cref="RemoteApiAvailable"/> — kept as a separate, longer-standing name
    /// since AccessGSXForm/MainForm already gate on it.</summary>
    public bool IsConnected => _remote.IsConnected;

    /// <summary>True when the Couatl Remote API socket is connected.</summary>
    public bool RemoteApiAvailable => _remote.IsConnected;

    /// <summary>Human-readable reason the Remote API is currently unreachable; empty when connected.</summary>
    public string UnavailableReason { get; private set; } = string.Empty;

    public bool CouatlStarted => _state.GsxRunning;
    public string StatusText => _statusText;
    public string MenuTitle => _menuTitle;
    public IReadOnlyList<MenuOption> MenuOptions => _menuOptions;
    public string LastTooltip => _lastTooltip;

    /// <summary>The delta-trimmed (or full, on first appearance) text most recently
    /// published through <see cref="AnnouncementReady"/> — what the screen reader
    /// should say right now.</summary>
    public string LastAnnouncementText { get; private set; } = string.Empty;

    // ── Active-service selection ────────────────────────────────────────
    // AccessGSXForm's Active Services combo (shown only when 2+ services
    // are simultaneously State == "performing") reads these three to decide
    // what to list and which row to highlight; docs/gsx.md documents the
    // combo as choosing "which active row drives the tooltip". The pure
    // derivation lives in GsxActiveServiceResolver (bottom of this file) so
    // it can be pinned by GsxActiveServiceResolverTests without needing a
    // GsxService instance.
    //
    // IMPORTANT — this selection governs LastTooltip ONLY, never the
    // announcement stream: GsxServiceAnnouncer.Update (in ApplyServices,
    // below) reads Services directly and announces every service's own
    // transitions regardless of what's selected here. The OLD tooltip-file
    // transport had exactly one message to show at a time, so its selector
    // necessarily gated both; the Remote API gives every service's state at
    // once, and a pilot who selected Boarding should still hear that
    // refuelling finished.
    public IReadOnlyList<string> ActiveServiceNames => _activeServiceNames;

    /// <summary>The sensible default when the pilot hasn't chosen: the first active service, or null when none is active.</summary>
    public string? DefaultActiveServiceName => _activeServiceNames.Count > 0 ? _activeServiceNames[0] : null;

    public string? SelectedActiveService
    {
        get => _selectedActiveService;
        set
        {
            string? normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (string.Equals(_selectedActiveService, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            _selectedActiveService = normalized;
            // Silent by design: this setter is reached only from a direct
            // combo-selection UI interaction (AccessGSXForm), which the
            // screen reader already announces on its own. RecomputeTooltip
            // only raises TooltipChanged (a textbox resync, no speech of its
            // own) — never AnnouncementReady.
            RecomputeTooltip();
        }
    }

    // ── SetGate_* read-only L-vars (GSX confirmation of selected gate) ──
    // Default -1 until GSX sets a gate. Updated via VISUAL_FRAME polling on
    // the retained SetGate_*-only SimConnect client.

    /// <summary>Latest value of <c>L:FSDT_GSX_SetGate_Name</c> (integer enum; -1 until set).</summary>
    public int SetGateName { get; private set; } = -1;

    /// <summary>Latest value of <c>L:FSDT_GSX_SetGate_Number</c> (-1 until set).</summary>
    public int SetGateNumber { get; private set; } = -1;

    /// <summary>Latest value of <c>L:FSDT_GSX_SetGate_Suffix</c> (-1 until set).</summary>
    public int SetGateSuffix { get; private set; } = -1;

    /// <summary>
    /// True while GSX reports its menu is showing (the Remote API's own
    /// "menuShown" state key). The auto-gate selector uses this to avoid
    /// driving the menu while the user is already navigating it manually.
    /// </summary>
    public bool IsMenuActive =>
        _state.TryGet("menuShown", out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>
    /// When true, the service speaks tooltip/service updates itself (via the
    /// injected announcer) when GSX publishes one — used while AccessGSXForm
    /// is hidden so the user still hears boarding/fuel/pushback callouts.
    /// When false (form open) the form drives its own speech via
    /// AnnouncementReady/TooltipChanged.
    /// </summary>
    public bool AnnounceWhenFormHidden { get; set; }

    // ── Remote API structured state ─────────────────────────────────────
    public GsxMenuModel Menu { get; private set; } = GsxMenuModel.Empty;
    public IReadOnlyList<GsxServiceState> Services { get; private set; } = Array.Empty<GsxServiceState>();
    public GsxSettingsSchema Settings { get; private set; } = GsxSettingsSchema.Empty;
    public GsxBilling Billing { get; private set; } = GsxBilling.Empty;
    public GsxReceipt? Receipt { get; private set; }

    public event EventHandler? StateChanged;
    public event EventHandler? MenuChanged;
    public event EventHandler? MenuHidden;
    // Never raised under the Remote API transport (kept for compile
    // compatibility — GsxMenuAutomation.WaitForNextMenuAsync callers still
    // subscribe to it). GSX's Remote API has no observed "menu timed out"
    // frame/topic; WaitForNextMenuAsync's own local await-timeout (a plain
    // TimeoutException) is the only timeout signal left. See task-10-report.md.
    public event EventHandler? MenuTimedOut;
    public event EventHandler? TooltipChanged;
    // Fires once per phrase Update()/receipt handling decided is worth
    // speaking; LastAnnouncementText holds that phrase at the moment this
    // fires. AccessGSXForm speaks it when visible; this service speaks it
    // itself (queued, via the injected ScreenReaderAnnouncer) when
    // AnnounceWhenFormHidden is set.
    public event EventHandler? AnnouncementReady;
    // Fires whenever the services list changes. ActiveServiceNames itself is
    // inert (see above) — kept so AccessGSXForm's existing subscription
    // still compiles.
    public event EventHandler? ActiveServicesChanged;
    public event EventHandler? SettingsChanged;

    // ─────────────────────────────────────────────────────────────────────
    // Internal state.
    // ─────────────────────────────────────────────────────────────────────

    private readonly IntPtr _windowHandle;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly GsxRemoteConnection _remote = new();
    private readonly GsxRemoteState _state = new();
    private readonly GsxServiceAnnouncer _serviceAnnouncer = new();
    private Microsoft.FlightSimulator.SimConnect.SimConnect? _simConnect;
    private bool _remoteStarted;
    private bool _disposed;

    private string _menuTitle = "GSX Menu";
    private string _lastTooltip = string.Empty;
    private string _statusText = "Status: Disconnected";
    private readonly List<MenuOption> _menuOptions = new();
    private IReadOnlyList<string> _activeServiceNames = Array.Empty<string>();
    private string? _selectedActiveService;

    public GsxService(IntPtr windowHandle, ScreenReaderAnnouncer announcer)
    {
        _windowHandle = windowHandle;
        _announcer = announcer ?? throw new ArgumentNullException(nameof(announcer));

        // Couatl can't parse a UTF-8 BOM at the start of CouatlAddons.ini —
        // it errors with "invalid line '<BOM>[gsx]'" and drops the rest of
        // the section. Earlier MSFSBA builds wrote the file themselves with
        // .NET's Encoding.UTF8 (BOM-emitting), so a user who ran one of
        // those old builds can still have a poisoned config today. Strip
        // the BOM at startup so Couatl gets a clean read when it loads with
        // the sim (see StripUtf8BomIfPresent below for more).
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                string configPath = Path.Combine(appData, CouatlConfigFolderName, CouatlConfigFileName);
                if (File.Exists(configPath))
                    StripUtf8BomIfPresent(configPath);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Gsx", $"Couatl config sanitization failed: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Start (or no-op the already-started half of) both transports. Safe to
    /// call repeatedly — e.g. from a SimConnect ConnectionStatusChanged
    /// callback on reconnect.
    /// </summary>
    public void Start()
    {
        if (_disposed) return;

        if (_simConnect == null)
        {
            try
            {
                _simConnect = new Microsoft.FlightSimulator.SimConnect.SimConnect(
                    "MSFSBA_GSX", _windowHandle, WM_USER_GSX_SIMCONNECT, null, 0);
                HookSimConnectEvents();
                Log.Debug("Gsx", "SimConnect client created (SetGate_* reads only).");
            }
            catch (COMException ex)
            {
                _simConnect = null;
                Log.Debug("Gsx", $"SimConnect unavailable: {ex.Message}");
            }
            catch (Exception ex)
            {
                _simConnect = null;
                Log.Debug("Gsx", $"SimConnect failed to initialize: {ex.Message}");
            }
        }

        if (!_remoteStarted)
        {
            _remoteStarted = true;
            _remote.FrameReceived += OnFrame;
            _remote.ConnectedChanged += OnRemoteConnectedChanged;
            _remote.Start();
            Log.Debug("Gsx", "Remote API client starting.");
        }
    }

    /// <summary>
    /// Stop both transports. Start() will be called again on the next
    /// SimConnect reconnect.
    /// </summary>
    public void Stop()
    {
        if (_simConnect == null && !_remoteStarted) return;

        if (_remoteStarted)
        {
            // Unsubscribe BEFORE stopping — GsxRemoteConnection.Stop() itself
            // flips IsConnected to false and would otherwise re-enter
            // OnRemoteConnectedChanged(false) while we're already mid-teardown
            // here. The explicit reset below is this method's own, deliberate
            // teardown of the same state OnRemoteConnectedChanged(false)
            // would have reset.
            _remoteStarted = false;
            _remote.FrameReceived -= OnFrame;
            _remote.ConnectedChanged -= OnRemoteConnectedChanged;
            _remote.Stop();
        }

        if (_simConnect != null)
        {
            try { _simConnect.Dispose(); }
            catch { /* ignore — we're tearing down */ }
            _simConnect = null;
        }

        _serviceAnnouncer.Reset();
        Menu = GsxMenuModel.Empty;
        _menuOptions.Clear();
        Services = Array.Empty<GsxServiceState>();
        Settings = GsxSettingsSchema.Empty;
        Billing = GsxBilling.Empty;
        Receipt = null;
        LastAnnouncementText = string.Empty;
        _lastTooltip = string.Empty;
        _activeServiceNames = Array.Empty<string>();
        _selectedActiveService = null;
        UnavailableReason = string.Empty;
        _statusText = "Status: Disconnected";
        RaiseStateChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _remote.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Retained SimConnect message pump (SetGate_* reads only).
    //
    // NOTE: deliberately NOT named ProcessWindowMessage — GsxServiceFacadeTests
    // asserts that name is gone, because the OLD menu/tooltip/settings
    // SimConnect L:var protocol this method used to ALSO pump is fully
    // retired in favour of the Couatl Remote API. But the retained SimConnect
    // client (SetGate_* only, kept for GsxGateSelector) is still an
    // HWND-based client that needs *some* pump: MainForm.WndProc forwards
    // every window message here, filtering on our distinct WM_USER id.
    // Swallowing COM/null exceptions mirrors SimConnectManager.ProcessWindowMessage
    // to stay robust during simulator teardown.
    // ─────────────────────────────────────────────────────────────────────
    public void PumpSimConnectMessage(ref Message m)
    {
        if (m.Msg == WM_USER_GSX_SIMCONNECT && _simConnect != null)
        {
            // Shared re-entrancy gate with the main SimConnect connection. The managed
            // SimConnect ReceiveMessage() is not reentrant; a DoEvents() pump (during a main
            // connection's data-def wait) can dispatch THIS GSX message mid-marshalling, which
            // corrupts the buffer (0xC0000005 in coreclr.dll / ExecutionEngineException). While
            // either connection is dispatching, defer — the message stays queued for the next
            // clean pump. All dispatch is on the UI thread, so a plain flag is enough.
            if (MSFSBlindAssist.SimConnect.SimConnectManager.SimConnectDispatchInProgress) return;
            MSFSBlindAssist.SimConnect.SimConnectManager.SimConnectDispatchInProgress = true;
            try
            {
                _simConnect.ReceiveMessage();
            }
            catch (COMException ex)
            {
                Log.Debug("Gsx",
                    $"ReceiveMessage COM exception (expected during disconnect): {ex.Message}");
            }
            catch (NullReferenceException ex)
            {
                Log.Debug("Gsx",
                    $"ReceiveMessage null reference (expected during disconnect): {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.Debug("Gsx",
                    $"Unexpected exception in PumpSimConnectMessage: {ex}");
            }
            finally
            {
                MSFSBlindAssist.SimConnect.SimConnectManager.SimConnectDispatchInProgress = false;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public commands — all fire-and-forget over the Remote API.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Ask GSX to (re)open its menu.</summary>
    public void OpenMenu() => _remote.Send("menu.open");

    public void HideMenu() => _remote.Send("menu.close");

    /// <summary>
    /// No-op under the Remote API transport. The OLD implementation re-read
    /// GSX's tooltip/status HTML files from disk on demand, because the
    /// file-polling timer could be up to a second stale. LastTooltip is now
    /// kept live by the "message" patch handler the instant GSX pushes a
    /// change, so there is nothing to refresh. Kept as a public method
    /// (rather than removed) because Ctrl+G (MainForm.Announcers.cs) still
    /// calls it immediately before reading LastTooltip.
    /// </summary>
    public void RefreshTooltip() { }

    /// <summary>Ask GSX to publish its current settings schema.</summary>
    public void OpenSettings() => _remote.Send("settings.get");

    /// <summary>
    /// Returns a <see cref="Task{T}"/> that completes with the next
    /// <see cref="MenuOptions"/> snapshot when <see cref="MenuChanged"/>
    /// fires, or faults if the menu is hidden/times out before a new menu
    /// arrives, or if <paramref name="timeout"/> elapses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Thread safety: <see cref="MenuChanged"/>, <see cref="MenuHidden"/>,
    /// and <see cref="MenuTimedOut"/> now fire from <see cref="OnFrame"/>
    /// after it has reposted onto the UI thread (see EnsureUiThread) — so
    /// this still resumes on the UI thread precisely as it always did.
    /// Callers that await this on the UI thread can touch UI controls
    /// directly on resume.
    /// </para>
    /// <para>
    /// Call this BEFORE triggering the menu action (OpenMenu / Choose) so
    /// the completion source is registered before the event can fire.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<MenuOption>> WaitForNextMenuAsync(TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<MenuOption>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cts = new CancellationTokenSource(timeout);

        // NOTE: we intentionally do NOT fault on MenuHidden. A caller may
        // hide the menu deliberately (or GSX may briefly clear "menuShown"
        // between two follow-on menus) — we complete only on the next
        // MenuChanged, or the timeout, which correctly covers a terminal
        // action that opens no submenu.
        EventHandler? onMenuChanged = null;
        EventHandler? onMenuTimedOut = null;

        void Unsubscribe()
        {
            MenuChanged  -= onMenuChanged;
            MenuTimedOut -= onMenuTimedOut;
            cts.Dispose();
        }

        onMenuChanged = (_, _) =>
        {
            if (tcs.TrySetResult(_menuOptions.ToList()))
                Unsubscribe();
        };

        onMenuTimedOut = (_, _) =>
        {
            if (tcs.TrySetException(
                    new InvalidOperationException("GSX menu timed out before a new menu arrived.")))
                Unsubscribe();
        };

        // Register cancellation (timeout) callback — fires on the ThreadPool,
        // but TrySetCanceled is thread-safe.
        cts.Token.Register(() =>
        {
            if (tcs.TrySetException(new TimeoutException(
                    $"WaitForNextMenuAsync: no menu arrived within {timeout}.")))
                Unsubscribe();
        });

        MenuChanged  += onMenuChanged;
        MenuTimedOut += onMenuTimedOut;

        return tcs.Task;
    }

    /// <summary>Submit a menu choice — the raw 0-based index into <see cref="Menu"/>'s Entries.</summary>
    public void Choose(int choice) => _remote.Send("menu.pick", new { index = choice });

    /// <summary>
    /// Re-resolves <paramref name="expectedLabel"/> against the CURRENT menu
    /// before choosing it — the label may have moved (or vanished) between
    /// the moment it was read out and the moment the key was pressed. Does
    /// nothing when the label is gone, ambiguous, or the resolved entry is
    /// disabled.
    /// </summary>
    public void PickMenuEntry(int paintedIndex, string expectedLabel)
    {
        int idx = Menu.ResolveIndex(paintedIndex, expectedLabel);
        if (idx < 0 || !Menu.IsSelectable(idx))
            return;

        _remote.Send("menu.pick", new { index = idx });
    }

    public void SetSettingNumber(string key, double value) =>
        _remote.Send("settings.set", new { key, value });

    public void PulseSettingAction(string key) =>
        _remote.Send("settings.action", new { key });

    public void SetSettingText(string key, string value) =>
        _remote.Send("settings.set", new { key, value });

    // ─────────────────────────────────────────────────────────────────────
    // Remote API frame handling.
    //
    // GsxRemoteConnection invokes FrameReceived/ConnectedChanged from its
    // background WebSocket receive loop, never the UI thread. Every field
    // this service exposes (Menu, Services, _menuOptions, ...) is read from
    // the UI thread by AccessGSXForm/MainForm with no locking of its own —
    // the OLD SimConnect+WndProc design was implicitly single-threaded
    // throughout, so nothing downstream was ever written to expect a
    // background writer. EnsureUiThread reposts onto MainForm's thread
    // before touching any field, restoring that invariant instead of pushing
    // thread-safety onto every reader.
    //
    // Every path below is defensive by construction (GsxFrame.Parse and
    // every GsxXxx.Parse never throw), so a frame can never propagate an
    // exception back into GsxRemoteConnection's receive loop.
    // ─────────────────────────────────────────────────────────────────────

    private void OnFrame(GsxFrame f)
    {
        if (!EnsureUiThread(() => OnFrame(f))) return;

        bool wasCouatlRunning = _state.GsxRunning;
        _state.Apply(f);

        if (wasCouatlRunning && !_state.GsxRunning)
        {
            // Couatl (GSX's own engine) stopped or is mid-restart — the
            // service-state baseline and any half-spoken announcement no
            // longer describe a session that exists. Mirrors the OLD
            // COUATL_STARTED 1->0 handling (ClearLastTooltip /
            // ClearProgressTrackingState).
            _serviceAnnouncer.Reset();
            LastAnnouncementText = string.Empty;
        }

        switch (f.Type)
        {
            case GsxFrameType.Snapshot:
            case GsxFrameType.Hello:
                // A fresh connect/reconnect republishes everything as one
                // bulk snapshot (or a bare Hello with no payload keys at
                // all) — re-derive every cached model and fire the same
                // events an equivalent set of individual patches would have,
                // so a form already open resyncs instead of going stale.
                ApplyMenu();
                MenuChanged?.Invoke(this, EventArgs.Empty);
                // ApplyServices recomputes LastTooltip itself (see its
                // remarks) — it reads "message" from _state too, which
                // _state.Apply(f) already applied above, so a separate
                // ApplyTooltip/TooltipChanged pair here would be redundant.
                ApplyServices();
                ApplySettings();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
                ApplyBilling();
                ApplyReceipt();
                UpdateStatusText();
                RaiseStateChanged();
                break;

            case GsxFrameType.Event when f.Topic == "engine":
                // "engine" is a SYNTHETIC signal from GsxRemoteState.Apply,
                // never a real state key — do not TryGet("engine", …). The
                // Couatl-restart baseline reset above already reacted to the
                // GsxRunning transition; what's still missing is the status
                // refresh. UpdateStatusText/RaiseStateChanged are otherwise
                // only reachable via Snapshot/Hello or a Patch keyed
                // statusHtml/parking/airport — an in-place Couatl restart
                // keeps the WebSocket open, so none of those necessarily
                // follow. Without this, StatusText (screen-reader-reachable
                // in AccessGSXForm) can keep reporting "Couatl started" after
                // Couatl has actually stopped, until an unrelated frame
                // happens to refresh it.
                UpdateStatusText();
                RaiseStateChanged();
                break;

            case GsxFrameType.Patch when !string.IsNullOrEmpty(f.Key):
                DispatchPatch(f.Key!);
                break;
        }
    }

    private void DispatchPatch(string key)
    {
        switch (key)
        {
            case "menu":
                ApplyMenu();
                MenuChanged?.Invoke(this, EventArgs.Empty);
                break;

            case "menuShown":
                if (!IsMenuActive)
                {
                    _menuOptions.Clear();
                    MenuHidden?.Invoke(this, EventArgs.Empty);
                }
                break;

            case "services":
                ApplyServices();
                break;

            case "settings":
                ApplySettings();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
                break;

            case "billing":
                // Exposed for future persistent-connection (GPU/jetway
                // timer) callouts; this facade swap does not itself add any
                // new spoken behaviour for billing changes.
                ApplyBilling();
                break;

            case "receipt":
                ApplyReceipt();
                break;

            case "statusHtml":
            case "parking":
            case "airport":
                UpdateStatusText();
                RaiseStateChanged();
                break;

            case "message":
                RecomputeTooltip();
                break;
        }
    }

    private void ApplyMenu()
    {
        Menu = _state.TryGet("menu", out var v) ? GsxMenuModel.Parse(v) : GsxMenuModel.Empty;
        _menuTitle = Menu.Title;
        _menuOptions.Clear();
        for (int i = 0; i < Menu.Count; i++)
            _menuOptions.Add(new MenuOption(i.ToString(CultureInfo.InvariantCulture), Menu.Entries[i], i));
    }

    /// <summary>
    /// Reparses Services, announces every service's own transitions
    /// unconditionally (baseline-first, via GsxServiceAnnouncer — this does
    /// NOT depend on the active-service selection below), then updates the
    /// active-service set and recomputes LastTooltip from whichever service
    /// currently governs it.
    /// </summary>
    private void ApplyServices()
    {
        Services = _state.TryGet("services", out var v)
            ? GsxServiceState.ParseList(v)
            : Array.Empty<GsxServiceState>();

        foreach (string phrase in _serviceAnnouncer.Update(Services))
            Announce(phrase);

        UpdateActiveServiceNames();
        RecomputeTooltip();
    }

    /// <summary>
    /// Recomputes ActiveServiceNames from the current Services list and
    /// raises ActiveServicesChanged only when the SET of names actually
    /// differs from last time — not on every services patch, or
    /// AccessGSXForm would rebuild its combo (and disturb screen-reader
    /// focus) on every progress tick even though nothing a pilot would
    /// call "active" changed. Order doesn't gate the comparison (GSX's own
    /// array order is used either way), only membership.
    /// </summary>
    private void UpdateActiveServiceNames()
    {
        var active = GsxActiveServiceResolver.ActiveNames(Services);

        bool changed = active.Count != _activeServiceNames.Count
            || !new HashSet<string>(active, StringComparer.OrdinalIgnoreCase).SetEquals(_activeServiceNames);
        if (!changed) return;

        _activeServiceNames = active;

        // The pilot's selection stopped being active — clear it so the
        // readout falls back to the default (first active service) rather
        // than being permanently stranded on a service that finished.
        if (_selectedActiveService != null
            && !_activeServiceNames.Any(n => string.Equals(n, _selectedActiveService, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedActiveService = null;
        }

        ActiveServicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySettings()
    {
        Settings = _state.TryGet("settings", out var v) ? GsxSettingsSchema.Parse(v) : GsxSettingsSchema.Empty;
    }

    private void ApplyBilling()
    {
        Billing = _state.TryGet("billing", out var v) ? GsxBilling.Parse(v) : GsxBilling.Empty;
    }

    private void ApplyReceipt()
    {
        Receipt = _state.TryGet("receipt", out var v) ? GsxReceipt.Parse(v) : null;
        if (Receipt is not { } receipt) return;

        Announce(FormatReceiptAnnouncement(receipt));
        // Tell GSX we've displayed the invoice so it clears its in-game banner.
        _remote.Send("invoice.seen");
    }

    /// <summary>
    /// Recomputes LastTooltip and raises TooltipChanged only when the text
    /// actually changes. When a service is currently active, the pilot's
    /// SelectedActiveService (if it's still active) or else the first
    /// active service governs the text (GsxActiveServiceResolver); with
    /// nothing active, falls back to GSX's own raw "message" state — the
    /// only content GSX itself publishes for the idle/cruise case.
    /// </summary>
    private void RecomputeTooltip()
    {
        string text = GsxActiveServiceResolver.ResolveGoverning(Services, _selectedActiveService) is { } governing
            ? GsxActiveServiceResolver.ComposeTooltip(governing)
            : RawMessageText();

        if (string.Equals(text, _lastTooltip, StringComparison.Ordinal))
            return;

        _lastTooltip = text;
        TooltipChanged?.Invoke(this, EventArgs.Empty);
    }

    private string RawMessageText() =>
        _state.TryGet("message", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    private static string FormatReceiptAnnouncement(GsxReceipt receipt)
    {
        string total = receipt.Total.ToString("0.00", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(receipt.Operator)
            ? $"Invoice available. Total {total}."
            : $"Invoice available from {receipt.Operator}. Total {total}.";
    }

    /// <summary>
    /// Publishes one announcement phrase through the same dual channel every
    /// GsxService announcement uses: AnnouncementReady + LastAnnouncementText
    /// for a visible AccessGSXForm to speak, and a direct queued Announce
    /// when the form is hidden and the user has opted into background
    /// monitoring (AnnounceWhenFormHidden).
    /// </summary>
    private void Announce(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return;

        LastAnnouncementText = phrase;
        AnnouncementReady?.Invoke(this, EventArgs.Empty);

        if (!AnnounceWhenFormHidden) return;
        try { _announcer.Announce(phrase); }
        catch (Exception ex)
        {
            Log.Debug("Gsx", $"Background announce failed: {ex.Message}");
        }
    }

    private void OnRemoteConnectedChanged(bool up)
    {
        if (!EnsureUiThread(() => OnRemoteConnectedChanged(up))) return;

        if (up)
        {
            UnavailableReason = string.Empty;
            UpdateStatusText();
            RaiseStateChanged();
            return;
        }

        // Connection dropped: every cached model describes a session we can
        // no longer vouch for, and the announcer's baseline is stale the
        // moment we reconnect (a service that quietly finished while we were
        // offline must not be reported as a fresh transition on reconnect).
        _state.Clear();
        _serviceAnnouncer.Reset();
        LastAnnouncementText = string.Empty;
        Menu = GsxMenuModel.Empty;
        _menuOptions.Clear();
        Services = Array.Empty<GsxServiceState>();
        Settings = GsxSettingsSchema.Empty;
        Billing = GsxBilling.Empty;
        Receipt = null;
        _activeServiceNames = Array.Empty<string>();
        _selectedActiveService = null;
        UnavailableReason = "GSX Remote API not reachable.";
        UpdateStatusText();
        RaiseStateChanged();
    }

    /// <summary>
    /// True when already running on the UI thread and the caller should
    /// proceed inline. False when the call was reposted onto the UI thread
    /// via BeginInvoke — the caller must return immediately without touching
    /// any field, since the reposted continuation will re-run the whole
    /// handler.
    /// </summary>
    private bool EnsureUiThread(Action retry)
    {
        Control? ctl;
        try { ctl = Control.FromHandle(_windowHandle); }
        catch { ctl = null; }

        if (ctl == null || !ctl.IsHandleCreated || ctl.IsDisposed)
            return true; // nothing to marshal onto (e.g. shutting down) — proceed best-effort

        if (!ctl.InvokeRequired)
            return true;

        try { ctl.BeginInvoke(retry); }
        // ObjectDisposedException derives from InvalidOperationException — one
        // catch covers both "handle destroyed mid-marshal" (InvalidOperationException)
        // and "control disposed mid-marshal" (ObjectDisposedException).
        catch (InvalidOperationException) { }
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────
    // SimConnect callbacks — SetGate_* reads only.
    // ─────────────────────────────────────────────────────────────────────

    private void HookSimConnectEvents()
    {
        if (_simConnect == null) return;
        _simConnect.OnRecvOpen += OnSimConnectOpen;
        _simConnect.OnRecvQuit += OnSimConnectQuit;
        _simConnect.OnRecvException += OnSimConnectException;
        _simConnect.OnRecvSimobjectData += OnSimConnectSimObjectData;
    }

    private void OnSimConnectOpen(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_OPEN data)
    {
        Log.Debug("Gsx", "SimConnect channel opened.");
        try
        {
            DefineSimVars();
            RequestSimVars();
        }
        catch (Exception ex)
        {
            Log.Debug("Gsx", $"OnSimConnectOpen failed: {ex.Message}");
        }
    }

    private void OnSimConnectQuit(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV data)
    {
        Log.Debug("Gsx", "Simulator has closed the connection.");
        try { _simConnect?.Dispose(); } catch { }
        _simConnect = null;
    }

    private void OnSimConnectException(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
    {
        Log.Debug("Gsx", $"SimConnect exception: {data.dwException}");
    }

    private void OnSimConnectSimObjectData(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        switch ((DataRequestId)data.dwRequestID)
        {
            case DataRequestId.RequestSetGateName:
            {
                var value = (DoubleValue)data.dwData[0];
                SetGateName = (int)value.Value;
                Log.Debug("Gsx", $"SetGate_Name = {SetGateName}");
                break;
            }
            case DataRequestId.RequestSetGateNumber:
            {
                var value = (DoubleValue)data.dwData[0];
                SetGateNumber = (int)value.Value;
                Log.Debug("Gsx", $"SetGate_Number = {SetGateNumber}");
                break;
            }
            case DataRequestId.RequestSetGateSuffix:
            {
                var value = (DoubleValue)data.dwData[0];
                SetGateSuffix = (int)value.Value;
                Log.Debug("Gsx", $"SetGate_Suffix = {SetGateSuffix}");
                break;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // SimConnect setup — SetGate_* data definitions/requests only.
    // ─────────────────────────────────────────────────────────────────────

    private void DefineSimVars()
    {
        if (_simConnect == null) return;

        // SetGate read-only confirmation L-vars (GSX manual p.94+).
        _simConnect.AddToDataDefinition(DataDefineId.SetGateName, "L:FSDT_GSX_SetGate_Name", "number",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
        _simConnect.AddToDataDefinition(DataDefineId.SetGateNumber, "L:FSDT_GSX_SetGate_Number", "number",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
        _simConnect.AddToDataDefinition(DataDefineId.SetGateSuffix, "L:FSDT_GSX_SetGate_Suffix", "number",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);

        _simConnect.RegisterDataDefineStruct<DoubleValue>(DataDefineId.SetGateName);
        _simConnect.RegisterDataDefineStruct<DoubleValue>(DataDefineId.SetGateNumber);
        _simConnect.RegisterDataDefineStruct<DoubleValue>(DataDefineId.SetGateSuffix);
    }

    private void RequestSimVars()
    {
        if (_simConnect == null) return;

        // Poll the read-only SetGate confirmation vars on every changed frame.
        _simConnect.RequestDataOnSimObject(DataRequestId.RequestSetGateName, DataDefineId.SetGateName,
            Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_PERIOD.VISUAL_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
        _simConnect.RequestDataOnSimObject(DataRequestId.RequestSetGateNumber, DataDefineId.SetGateNumber,
            Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_PERIOD.VISUAL_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
        _simConnect.RequestDataOnSimObject(DataRequestId.RequestSetGateSuffix, DataDefineId.SetGateSuffix,
            Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_PERIOD.VISUAL_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Couatl config BOM migration.
    // ─────────────────────────────────────────────────────────────────────

    // Couatl's INI parser doesn't tolerate a UTF-8 BOM at the start of
    // CouatlAddons.ini — it treats the three BOM bytes as part of "[gsx]"
    // and reports an invalid-line error, dropping the rest of the section.
    // Earlier MSFSBA builds wrote this file themselves with .NET's
    // Encoding.UTF8 (BOM-emitting) instances, so a user who ran one of
    // those old builds can still have a poisoned file today even though
    // MSFSBA no longer writes it at all (GSX owns settings persistence via
    // the Remote API's settings.set/settings.action). Run a one-shot strip
    // in the constructor (see above) so Couatl still gets a clean read.
    private static void StripUtf8BomIfPresent(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> head = stackalloc byte[3];
            if (fs.Read(head) < 3)
                return;
            if (head[0] != 0xEF || head[1] != 0xBB || head[2] != 0xBF)
                return;

            byte[] body = new byte[fs.Length - 3];
            int total = 0;
            while (total < body.Length)
            {
                int n = fs.Read(body, total, body.Length - total);
                if (n <= 0) break;
                total += n;
            }
            fs.Close();
            File.WriteAllBytes(path, body);
            Log.Debug("Gsx", $"Stripped UTF-8 BOM from {path}");
        }
        catch (Exception ex)
        {
            Log.Debug("Gsx", $"BOM strip failed for {path}: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Status text + event raise helpers.
    // ─────────────────────────────────────────────────────────────────────

    private void UpdateStatusText()
    {
        _statusText = RemoteApiAvailable
            ? "Status: Connected to GSX | " + (CouatlStarted ? "Couatl started" : "Couatl not started")
            : "Status: " + (string.IsNullOrWhiteSpace(UnavailableReason) ? "Not connected to GSX." : UnavailableReason);
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Derives the "active" (State == "performing") service set from GSX's
/// services list, and resolves which one — if any — should govern
/// LastTooltip. Pure and stateless; kept internal + covered directly by
/// GsxActiveServiceResolverTests via InternalsVisibleTo, the same pattern
/// GsxSettingsForm.cs uses for GsxRangeBoundsResolver — GsxService itself
/// needs a window handle and cannot be constructed in a unit test.
/// </summary>
internal static class GsxActiveServiceResolver
{
    /// <summary>The speakable name for a service: its GSX-published
    /// DisplayName, falling back to the bare Id when GSX left it blank.</summary>
    public static string NameOf(GsxServiceState service) =>
        string.IsNullOrEmpty(service.DisplayName) ? service.Id : service.DisplayName;

    public static bool IsActive(GsxServiceState service) =>
        string.Equals(service.State, "performing", StringComparison.Ordinal);

    /// <summary>Names of every currently-active service, in Services' own (GSX-published) order.</summary>
    public static IReadOnlyList<string> ActiveNames(IReadOnlyList<GsxServiceState> services) =>
        services.Where(IsActive).Select(NameOf).ToList();

    /// <summary>
    /// The service that should govern LastTooltip: <paramref name="selected"/>
    /// when it names a currently-active service, else the first active
    /// service (GSX's own order), else null when nothing is active at all.
    /// </summary>
    public static GsxServiceState? ResolveGoverning(IReadOnlyList<GsxServiceState> services, string? selected)
    {
        if (selected != null)
        {
            var match = services.FirstOrDefault(s =>
                IsActive(s) && string.Equals(NameOf(s), selected, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return services.FirstOrDefault(IsActive);
    }

    /// <summary>
    /// The tooltip text for one active service: its own GSX-stated StateText
    /// (falling back to its name when GSX left StateText blank), with a
    /// parenthesized ProgressText suffix when GSX published one.
    /// </summary>
    public static string ComposeTooltip(GsxServiceState service)
    {
        string text = string.IsNullOrWhiteSpace(service.StateText) ? NameOf(service) : service.StateText;
        return string.IsNullOrWhiteSpace(service.ProgressText) ? text : $"{text} ({service.ProgressText})";
    }
}
