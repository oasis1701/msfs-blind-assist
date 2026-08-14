// GsxService — facade over the GSX Ground Services Pro accessibility
// integration. All of it — menu, tooltip ("message"), services, settings,
// billing, receipts, and (since Spec 2) gate selection and the live gate
// list — is fed by ONE transport: the Couatl Remote API (WebSocket JSON,
// ws://127.0.0.1:8744/) via GsxRemoteConnection/GsxRemoteState in
// MSFSBlindAssist.Services.Gsx.Remote. IsConnected/RemoteApiAvailable report
// this transport's reachability.
//
// This file no longer touches SimConnect at all. It used to retain a small,
// independent SimConnect client (HWND-based, WM_USER 0x0403) for nothing but
// the read-only FSDT_GSX_SetGate_* confirmation L-vars the OLD menu-walking
// GsxGateSelector polled after picking a stand; gate.select's own synchronous
// result payload replaced that polling entirely (Spec 2), and with the old
// selector deleted, so is the SimConnect client, WM_USER_GSX_SIMCONNECT, and
// the PumpSimConnectMessage pump MainForm.WndProc used to route to it.
//
// All speech is routed through MSFSBA's existing ScreenReaderAnnouncer; no
// Tolk is loaded here.
//
// Ported from the AccessGSX project (https://github.com/jfayre/access-gsx)
// with permission of the author (both projects are GPL v3); the Remote API
// transport is GSX's own first-party protocol, not part of that port.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services.Gsx.Remote;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Facade over the GSX Ground Services Pro accessibility integration. Mirrors
/// GSX's menu/services/settings/billing/gate state from the Couatl Remote
/// API and exposes events the UI form (AccessGSXForm) and MainForm's
/// background hook subscribe to.
/// </summary>
public sealed class GsxService : IDisposable
{
    private const string CouatlConfigFolderName = "Virtuali";
    private const string CouatlConfigFileName = "CouatlAddons.ini";

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

    // The three reasons GSX data can be unavailable. Each names its OWN cause:
    // "not connected to the simulator" is accurate ONLY after Stop(), which
    // MainForm calls from the SimConnect disconnect edge — it used to be the
    // message for every case, including the one that matters most (a GSX build
    // with no Remote API at all), where it sent the pilot looking at the wrong
    // thing entirely.
    //
    // The version IS now named, and it is 4.0.8 — that number is known from
    // Virtuali's own "Couatl Remote API v2 — Developer Guide & SDK Reference"
    // and release notes (embedded in GSX_manual_MSFS.pdf; see docs/gsx.md).
    // The Remote API itself arrived in 4.0.1, but 4.0.8 is what MSFSBA requires
    // overall — it is where gate.select and the handler.set write surface
    // landed — so telling a pilot "4.0.1" would send them to a build where the
    // Access GSX window works but gate selection silently does nothing. This
    // message is the ONE place a version number is stated; the code itself
    // feature-detects on hello.capabilities and never compares versions (the
    // vendor guide's own instruction).
    internal const string ReasonNoRemoteApi =
        "GSX's Remote API is not reachable. This needs GSX 4.0.8 or newer.";
    internal const string ReasonConnectionLost =
        "Lost the connection to GSX's Remote API. Reconnecting.";
    internal const string ReasonSimDisconnected =
        "Not connected to the simulator, so GSX is not being monitored.";

    /// <summary>
    /// Human-readable reason GSX data is currently unavailable; empty when the
    /// Remote API is connected. NEVER empty otherwise — callers announce this
    /// string directly, and a queued announcement of "" is silently dropped, so
    /// the one message the pilot needs would simply never be spoken. That was
    /// the live failure: <c>_unavailableReason</c> stayed "" because
    /// <c>GsxRemoteConnection.SetConnected</c> early-returns on an unchanged
    /// value, so a connect that NEVER succeeds never raises
    /// <c>ConnectedChanged(false)</c> and nothing ever assigned a reason.
    /// </summary>
    public string UnavailableReason =>
        RemoteApiAvailable
            ? string.Empty
            : string.IsNullOrWhiteSpace(_unavailableReason) ? ReasonNoRemoteApi : _unavailableReason;

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

    /// <summary>
    /// True while GSX reports its menu is showing (the Remote API's own
    /// "menuShown" state key).
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

    /// <summary>
    /// GSX's currently-advertised <c>hello.capabilities</c> tokens — refreshed on every
    /// <c>hello</c> frame (the initial connect and every reconnect) and cleared when the
    /// Remote API connection drops, so a stale token set can never outlive the socket that
    /// published it. Feature-gates that depend on a specific verb/data surface (the
    /// <c>gate</c> token for <c>gate.select</c>, the <c>handlerData</c> token for the live
    /// parking list) must check this, never a version number — the vendor guide's own
    /// instruction. Wired to <see cref="Services.Gsx.Remote.GsxRemoteGateSelector"/> and
    /// <see cref="Services.GateDataSource"/> from MainForm.Dialogs.cs
    /// (BuildGsxGateSelector/BuildGateDataSource).
    /// </summary>
    public IReadOnlyCollection<string> Capabilities { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// GSX's current <c>handlerData.airport</c> sub-object — the live parking table for
    /// whichever airport GSX has loaded (<c>icao</c>, <c>name</c>, <c>parkings</c>; see
    /// <see cref="Services.Gsx.Remote.GsxRemoteParkingReader"/>) — or null when no
    /// <c>handlerData</c> patch has arrived yet, or it arrived in an unexpected shape.
    /// <c>handlerData</c> itself (the flat state store's "handlerData" key) is the ~1.7 MB
    /// <c>{aircraft, airport, gate, simbrief}</c> blob GSX republishes once per airport or
    /// aircraft change; only its <c>airport</c> member is read here, and only on demand —
    /// <see cref="Services.GateDataSource"/> calls this once per airport change (it keeps its
    /// own cache), never on a hot path. Never throws: a malformed shape degrades to null,
    /// exactly like the capability genuinely being absent. Thread-safe — reads through
    /// <see cref="GsxRemoteState.TryGet"/>, which locks internally, so this is safe to call
    /// from the UI thread regardless of when the WebSocket receive loop last wrote it.
    /// </summary>
    public JsonElement? GetHandlerDataAirport()
    {
        try
        {
            if (!_state.TryGet("handlerData", out var handlerData) || handlerData.ValueKind != JsonValueKind.Object)
                return null;
            if (!handlerData.TryGetProperty("airport", out var airport) || airport.ValueKind != JsonValueKind.Object)
                return null;
            return airport;
        }
        catch (Exception ex)
        {
            Log.Debug("Gsx", $"GetHandlerDataAirport failed: {ex.Message}");
            return null;
        }
    }

    public event EventHandler? StateChanged;
    public event EventHandler? MenuChanged;
    public event EventHandler? MenuHidden;
    // NOTE: there is deliberately no MenuTimedOut event. The OLD SimConnect
    // transport had a menu-timeout signal; the Remote API publishes no
    // equivalent frame or topic, so the event carried over from the migration
    // could never be raised (a live CS0067) and its whole UI path —
    // AccessGSXForm.OnMenuTimedOutUi and its prompt — was dead with it.
    // WaitForNextMenuAsync's own await-timeout (a plain TimeoutException) is
    // the only timeout signal there is. Do not re-add the event without a real
    // frame to raise it from.
    public event EventHandler? TooltipChanged;
    // Fires once per phrase Update()/receipt handling decided is worth
    // speaking; LastAnnouncementText holds that phrase at the moment this
    // fires. AccessGSXForm speaks it when visible; this service speaks it
    // itself (queued, via the injected ScreenReaderAnnouncer) when
    // AnnounceWhenFormHidden is set.
    public event EventHandler? AnnouncementReady;
    // Fires when the SET of active (State == "performing") services changes —
    // not on every services patch, so AccessGSXForm's combo doesn't rebuild
    // (and disturb screen-reader focus) on every progress tick.
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
    private bool _remoteStarted;
    private bool _disposed;

    private string _menuTitle = "GSX Menu";
    private string _lastTooltip = string.Empty;
    private string _statusText = "Status: Disconnected";
    // Seeded with the sim-disconnected reason: until MainForm calls Start() off
    // the SimConnect connect edge, that is exactly the situation (Start() itself
    // no longer touches SimConnect, but MainForm still calls it from that edge).
    private string _unavailableReason = ReasonSimDisconnected;
    private readonly List<MenuOption> _menuOptions = new();
    private IReadOnlyList<string> _activeServiceNames = Array.Empty<string>();
    private string? _selectedActiveService;
    // Digests of every invoice already spoken this GSX session — see ApplyReceipt.
    private readonly HashSet<string> _announcedReceipts = new(StringComparer.Ordinal);

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
    /// Start (or no-op if already started) the Remote API transport. Safe to
    /// call repeatedly — e.g. from a SimConnect ConnectionStatusChanged
    /// callback on reconnect (MainForm still gates this on the main
    /// SimConnect connection even though this class no longer touches
    /// SimConnect itself — see AircraftSwitch.cs).
    /// </summary>
    public void Start()
    {
        if (_disposed) return;

        if (!_remoteStarted)
        {
            _remoteStarted = true;
            // Seed the reason BEFORE the first connection attempt resolves. A
            // connect that never succeeds produces no ConnectedChanged at all
            // (SetConnected early-returns on an unchanged value, and _connected
            // starts false), so nothing downstream would ever set one — leaving
            // Alt+G / Ctrl+G / F5 to announce an empty string, i.e. nothing.
            // This message IS the whole mitigation for a GSX build that predates
            // the Remote API: there is no fallback transport to degrade to.
            _unavailableReason = ReasonNoRemoteApi;
            UpdateStatusText();
            _remote.FrameReceived += OnFrame;
            _remote.ConnectedChanged += OnRemoteConnectedChanged;
            _remote.Start();
            Log.Debug("Gsx", "Remote API client starting.");
            RaiseStateChanged();
        }
    }

    /// <summary>
    /// Stop the Remote API transport. Start() will be called again on the
    /// next SimConnect reconnect.
    /// </summary>
    public void Stop()
    {
        if (!_remoteStarted) return;

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

        _serviceAnnouncer.Reset();
        Menu = GsxMenuModel.Empty;
        _menuOptions.Clear();
        Services = Array.Empty<GsxServiceState>();
        Settings = GsxSettingsSchema.Empty;
        Billing = GsxBilling.Empty;
        Receipt = null;
        Capabilities = Array.Empty<string>();
        _announcedReceipts.Clear();
        LastAnnouncementText = string.Empty;
        _activeServiceNames = Array.Empty<string>();
        _selectedActiveService = null;
        _unavailableReason = ReasonSimDisconnected;
        // Clear the tooltip through the event-raising path, not by assignment:
        // AccessGSXForm refreshes its Tooltip box on TooltipChanged and on
        // nothing else, so a bare assignment leaves the last live tooltip on
        // screen looking current after the integration has been torn down.
        ClearTooltip();
        UpdateStatusText();
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
    /// fires, or faults with a <see cref="TimeoutException"/> when
    /// <paramref name="timeout"/> elapses first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Thread safety: <see cref="MenuChanged"/> and <see cref="MenuHidden"/>
    /// fire from <see cref="OnFrame"/> after it has reposted onto the UI
    /// thread (see EnsureUiThread) — so this still resumes on the UI thread
    /// precisely as it always did. Callers that await this on the UI thread
    /// can touch UI controls directly on resume.
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

        void Unsubscribe()
        {
            MenuChanged -= onMenuChanged;
            cts.Dispose();
        }

        onMenuChanged = (_, _) =>
        {
            if (tcs.TrySetResult(_menuOptions.ToList()))
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

        MenuChanged += onMenuChanged;

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

    /// <summary>
    /// Runs one of GSX's own system commands — the Remote API's <c>command.run</c>
    /// verb. See <see cref="GsxSystemCommands"/> for the four AccessGSXForm binds
    /// to A/B/D/E. Fire-and-forget, like every other command here: GSX either
    /// acts or it does not, and RESTART_COUATL drops the socket by design.
    /// </summary>
    public void RunCommand(string command) => _remote.Send("command.run", new { command });

    /// <summary>
    /// Sends a Remote API command and awaits its correlated result — the general-purpose
    /// escape hatch beneath the specific fire-and-forget commands above (<see cref="Choose"/>,
    /// <see cref="RunCommand"/>, …), for a caller that needs the typed result rather than a
    /// send-and-forget. This is the production wiring behind
    /// <see cref="Services.Gsx.Remote.GsxCommandSender"/> for
    /// <see cref="Services.Gsx.Remote.GsxRemoteGateSelector"/> (constructed in
    /// MainForm.Dialogs.BuildGsxGateSelector) — <c>gate.select</c> is a one-shot
    /// request/response with no menu state, so it does not fit the fire-and-forget shape
    /// every other command here uses.
    /// </summary>
    public Task<GsxResult> SendCommandAsync(string verb, object? args = null) => _remote.SendAsync(verb, args);

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

        // Only a 'hello' frame carries capabilities (GsxFrame.Parse leaves every other
        // frame type's Capabilities at its default empty list) -- refresh, never clear,
        // on anything else, so a later patch/event frame can't wipe out what the last
        // hello actually advertised.
        if (f.Type == GsxFrameType.Hello)
            Capabilities = f.Capabilities;

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
            // A new Couatl session issues new invoices; the previous session's
            // digests must not silence them. Same lifetime the pre-Remote-API
            // _announcedInvoiceKeys set had (cleared in ClearProgressTrackingState,
            // which the COUATL_STARTED 1->0 edge drove).
            _announcedReceipts.Clear();
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

    /// <summary>
    /// Reparses the invoice and announces it AT MOST ONCE.
    ///
    /// The dedup is load-bearing, not tidiness: this runs on every <c>receipt</c>
    /// patch AND on every reconnect snapshot, and GSX restarts its engine
    /// routinely — without it a pilot hears the same invoice again on every
    /// reconnect, in a queue that never interrupts and therefore only grows.
    /// Mirrors the pre-Remote-API <c>_announcedInvoiceKeys</c> set, including
    /// its lifetime: cleared on a Couatl shutdown (OnFrame) and on Stop(),
    /// never on a mere socket drop.
    ///
    /// The key is a digest of the raw receipt payload rather than the operator
    /// name, so a second, genuinely different invoice from the SAME handler in
    /// one session still announces.
    /// </summary>
    private void ApplyReceipt()
    {
        if (!_state.TryGet("receipt", out var v))
        {
            Receipt = null;
            return;
        }

        Receipt = GsxReceipt.Parse(v);
        if (Receipt is not { } receipt) return;

        if (!_announcedReceipts.Add(ReceiptKey(v))) return;

        Announce(FormatReceiptAnnouncement(receipt, Billing));
        // Tell GSX we've displayed the invoice so it clears its in-game banner.
        _remote.Send("invoice.seen");
    }

    /// <summary>
    /// Identity of one invoice: a digest of GSX's own raw payload. Hashed rather
    /// than stored verbatim because the payload embeds the rendered invoice HTML
    /// and its logo — never held, never logged (docs/gsx.md: "never log a raw
    /// frame"; receipts carry operator and financial data).
    /// </summary>
    private static string ReceiptKey(JsonElement receipt)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(receipt.GetRawText()));
        return Convert.ToHexString(digest);
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

    /// <summary>Blanks the tooltip and tells the form, when there was one to blank.</summary>
    private void ClearTooltip()
    {
        if (_lastTooltip.Length == 0) return;
        _lastTooltip = string.Empty;
        TooltipChanged?.Invoke(this, EventArgs.Empty);
    }

    private string RawMessageText() =>
        _state.TryGet("message", out var v) ? GsxActiveServiceResolver.MessageText(v) : string.Empty;

    /// <summary>
    /// The spoken invoice line. GSX's <c>/receipt</c> frame publishes NO figure
    /// at all (canPrint/html/logo/operator/printPreview/printer), so the total is
    /// sourced from <c>/billing</c>'s own pre-totalled builders — the only place
    /// GSX states money. When billing has published no builders yet, the phrase
    /// states no figure rather than inventing one: reading an authoritative-
    /// sounding "Total 0.00" over a real 1761.42 charge is worse than saying
    /// nothing about the amount.
    /// </summary>
    internal static string FormatReceiptAnnouncement(GsxReceipt receipt, GsxBilling billing)
    {
        string lead = string.IsNullOrWhiteSpace(receipt.Operator)
            ? "Invoice available."
            : $"Invoice available from {receipt.Operator}.";

        if (billing is not { HasBuilders: true }) return lead;

        string total = billing.BuildersTotal.ToString("0.00", CultureInfo.InvariantCulture);
        return $"{lead} Total {total}.";
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
            _unavailableReason = string.Empty;
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
        Capabilities = Array.Empty<string>();
        _activeServiceNames = Array.Empty<string>();
        _selectedActiveService = null;
        // Reached only after a connection that HAD succeeded (SetConnected
        // early-returns on an unchanged value), so this is a genuine drop —
        // routine during a RESTART_COUATL — not "your GSX has no Remote API".
        _unavailableReason = ReasonConnectionLost;
        // _state was cleared above, so this recomputes to empty and raises
        // TooltipChanged — without it the Tooltip box kept showing the last
        // live text as if it were current, while Stop() cleared it. The two
        // teardown paths must agree.
        RecomputeTooltip();
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
/// Everything that decides the TOOLTIP TEXT: which service (if any) governs it,
/// how that service's row reads, and what GSX's own idle "message" slot says
/// when no service is running. Pure and stateless; kept internal + covered
/// directly by
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
    /// (falling back to its name when GSX left StateText blank), suffixed with
    /// the row's detail in parentheses when GSX published any.
    ///
    /// The detail's precedence is deliberate. GSX's own <c>statusText</c> wins:
    /// on the captured deboarding it reads "bus in position / pax 181/186 /
    /// bags 100%", the true picture. <c>progressText</c> on that SAME row reads
    /// "181/181" — GSX's progress bar is current-out-of-current — and rendering
    /// it bare told the pilot deboarding had finished with five passengers still
    /// aboard. So the typed pax/bags detail is preferred over progressText too,
    /// and progressText is used only when GSX published no typed detail at all
    /// (e.g. a fuel row's "8823/13001 kg", where there is no contradicting
    /// source and the units make it self-describing).
    /// </summary>
    public static string ComposeTooltip(GsxServiceState service)
    {
        string text = string.IsNullOrWhiteSpace(service.StateText) ? NameOf(service) : service.StateText;
        string detail = ComposeDetail(service);
        return detail.Length == 0 ? text : $"{text} ({detail})";
    }

    /// <summary>
    /// GSX's own tooltip slot, the fallback whenever no service is performing —
    /// i.e. parked, before departure, in the cruise, and after every service
    /// finishes.
    ///
    /// The published shape is an OBJECT: <c>{"text":"…","visible":false}</c>.
    /// GSX's own client renders <c>m.text</c> gated on <c>m.visible</c>, and so
    /// does this. Reading the slot as a bare STRING (which it never is) made the
    /// fallback return "" always, so LastTooltip was permanently empty across
    /// that whole idle case — the Tooltip box blank and Ctrl+G answering "No GSX
    /// tooltip yet." A bare string is still accepted, harmlessly, should a
    /// future GSX simplify the slot.
    /// </summary>
    public static string MessageText(JsonElement message)
    {
        if (message.ValueKind == JsonValueKind.String)
            return message.GetString() ?? string.Empty;

        if (message.ValueKind != JsonValueKind.Object) return string.Empty;

        if (!message.TryGetProperty("visible", out var visible) || visible.ValueKind != JsonValueKind.True)
            return string.Empty;

        return message.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string ComposeDetail(GsxServiceState service)
    {
        if (!string.IsNullOrWhiteSpace(service.StatusText))
            // GSX writes statusText as separate lines; the tooltip box is one
            // field and Ctrl+G speaks it as one utterance, so join with commas.
            return string.Join(", ", service.StatusText
                .ReplaceLineEndings("\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var parts = new List<string>();
        if (service.PaxDone is { } done)
            parts.Add(service.PaxTotal is { } total ? $"pax {done}/{total}" : $"pax {done}");
        if (service.BagsPercent is { } bags)
            parts.Add($"bags {bags}%");
        if (parts.Count > 0)
            return string.Join(", ", parts);

        return string.IsNullOrWhiteSpace(service.ProgressText) ? string.Empty : service.ProgressText;
    }
}
