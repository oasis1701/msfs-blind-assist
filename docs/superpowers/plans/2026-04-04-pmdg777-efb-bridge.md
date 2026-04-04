# PMDG 777 EFB Accessibility Bridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the PMDG 777 EFB's SimBrief uplink, Navigraph auth, and preferences accessible to blind users by injecting a JS bridge into the EFB HTML and exposing its functionality through an accessible Windows Form.

**Architecture:** A JavaScript bridge script injected into `PMDGTabletCA.html` hooks into the EFB's internal EventBus and communicates with a C# `HttpListener` on `localhost:19777`. A Windows Form provides screen-reader-accessible controls for SimBrief, Navigraph, and preferences. An auto-patcher handles installation with user consent and backup.

**Tech Stack:** C# / .NET 9 / Windows Forms, JavaScript (MSFS Coherent GT), HttpListener, System.Text.Json

**Spec:** `docs/superpowers/specs/2026-04-04-pmdg777-efb-bridge-design.md`

---

## File Structure

**New files:**
- `MSFSBlindAssist/SimConnect/EFBBridgeServer.cs` — HTTP server for JS↔C# communication
- `MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.cs` — Accessible form (hand-coded, no designer)
- `MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.Designer.cs` — Form layout and controls
- `MSFSBlindAssist/Patching/EFBPatcher.cs` — Auto-patcher for EFB HTML injection
- `MSFSBlindAssist/Resources/pmdg-efb-accessibility-bridge.js` — Injected JS bridge

**Modified files:**
- `MSFSBlindAssist/Hotkeys/HotkeyManager.cs` — Add hotkey constant, registration, and handler
- `MSFSBlindAssist/MainForm.cs` — Add form lifecycle, server lifecycle, patcher integration
- `MSFSBlindAssist/HotkeyGuides/PMDG_777_Hotkeys.txt` — Document new hotkey

---

## Task 0: Create Feature Branch

- [ ] **Step 1: Create and switch to feature branch**

```bash
cd C:/Users/robin/Downloads/msfs-blind-assist
git checkout -b feature/pmdg-777-efb-bridge
```

---

## Task 1: EFB Bridge Server (EFBBridgeServer.cs)

The HTTP server is the foundation — everything else depends on it.

**Files:**
- Create: `MSFSBlindAssist/SimConnect/EFBBridgeServer.cs`

- [ ] **Step 1: Create EFBBridgeServer.cs with the full implementation**

Create `MSFSBlindAssist/SimConnect/EFBBridgeServer.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect
{
    public class EFBStateUpdateEventArgs : EventArgs
    {
        public string Type { get; set; } = "";
        public Dictionary<string, string> Data { get; set; } = new();
    }

    public class EFBCommand
    {
        public string Command { get; set; } = "";
        public Dictionary<string, string>? Payload { get; set; }
    }

    public class EFBBridgeServer : IDisposable
    {
        private const int Port = 19777;
        private const string Prefix = "http://localhost:19777/";
        private const int HeartbeatTimeoutSeconds = 15;
        private const int CommandExpirySeconds = 30;

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentQueue<(EFBCommand Command, DateTime EnqueuedAt)> _commandQueue = new();
        private readonly SynchronizationContext? _syncContext;
        private DateTime _lastHeartbeat = DateTime.MinValue;
        private bool _disposed;

        public event EventHandler<EFBStateUpdateEventArgs>? StateUpdated;

        public bool IsRunning => _listener?.IsListening == true;
        public bool IsBridgeConnected => (DateTime.UtcNow - _lastHeartbeat).TotalSeconds < HeartbeatTimeoutSeconds;

        public EFBBridgeServer()
        {
            _syncContext = SynchronizationContext.Current;
        }

        public void Start()
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add(Prefix);

            try
            {
                _listener.Start();
                Task.Run(() => ListenLoop(_cts.Token));
            }
            catch (HttpListenerException ex)
            {
                System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Failed to start on {Prefix}: {ex.Message}");
                _listener = null;
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            if (_listener?.IsListening == true)
            {
                _listener.Stop();
            }
            _listener?.Close();
            _listener = null;
            _lastHeartbeat = DateTime.MinValue;
        }

        public void EnqueueCommand(string command, Dictionary<string, string>? payload = null)
        {
            _commandQueue.Enqueue((new EFBCommand { Command = command, Payload = payload }, DateTime.UtcNow));
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener?.IsListening == true)
            {
                try
                {
                    var context = await _listener.GetContextAsync().WaitAsync(ct);
                    _ = Task.Run(() => HandleRequest(context), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Listen error: {ex.Message}");
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // CORS headers for all responses
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            try
            {
                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 204;
                    response.Close();
                    return;
                }

                string path = request.Url?.AbsolutePath ?? "";

                switch (path)
                {
                    case "/ping":
                        await HandlePing(response);
                        break;
                    case "/state" when request.HttpMethod == "POST":
                        await HandleStateUpdate(request, response);
                        break;
                    case "/commands" when request.HttpMethod == "GET":
                        await HandleGetCommands(response);
                        break;
                    default:
                        response.StatusCode = 404;
                        await WriteJson(response, new { error = "Not found" });
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Request error: {ex.Message}");
                try
                {
                    response.StatusCode = 500;
                    response.Close();
                }
                catch { }
            }
        }

        private async Task HandlePing(HttpListenerResponse response)
        {
            _lastHeartbeat = DateTime.UtcNow;
            await WriteJson(response, new { status = "ok" });
        }

        private async Task HandleStateUpdate(HttpListenerRequest request, HttpListenerResponse response)
        {
            _lastHeartbeat = DateTime.UtcNow;

            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            string body = await reader.ReadToEndAsync();

            try
            {
                var json = JsonDocument.Parse(body);
                var root = json.RootElement;

                string type = root.GetProperty("type").GetString() ?? "";
                var data = new Dictionary<string, string>();

                if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in dataElement.EnumerateObject())
                    {
                        data[prop.Name] = prop.Value.ToString();
                    }
                }

                var args = new EFBStateUpdateEventArgs { Type = type, Data = data };

                if (_syncContext != null)
                {
                    _syncContext.Post(_ => StateUpdated?.Invoke(this, args), null);
                }
                else
                {
                    StateUpdated?.Invoke(this, args);
                }
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: JSON parse error: {ex.Message}");
            }

            await WriteJson(response, new { received = true });
        }

        private async Task HandleGetCommands(HttpListenerResponse response)
        {
            _lastHeartbeat = DateTime.UtcNow;

            var commands = new List<object>();
            var now = DateTime.UtcNow;

            while (_commandQueue.TryDequeue(out var item))
            {
                // Skip expired commands
                if ((now - item.EnqueuedAt).TotalSeconds > CommandExpirySeconds)
                    continue;

                if (item.Command.Payload != null)
                {
                    commands.Add(new { command = item.Command.Command, payload = item.Command.Payload });
                }
                else
                {
                    commands.Add(new { command = item.Command.Command });
                }
            }

            await WriteJson(response, commands);
        }

        private static async Task WriteJson(HttpListenerResponse response, object data)
        {
            string json = JsonSerializer.Serialize(data);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer);
            response.Close();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts?.Dispose();
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/SimConnect/EFBBridgeServer.cs
git commit -m "feat(pmdg777): add EFB bridge HTTP server for JS-C# communication"
```

---

## Task 2: Auto-Patcher (EFBPatcher.cs)

**Files:**
- Create: `MSFSBlindAssist/Patching/EFBPatcher.cs`
- Read (reference): `MSFSBlindAssist/Database/NavdataReaderBuilder.cs:487-566` — MSFS path detection pattern

- [ ] **Step 1: Create EFBPatcher.cs with full implementation**

Create `MSFSBlindAssist/Patching/EFBPatcher.cs`:

```csharp
using System.IO;
using System.Text;

namespace MSFSBlindAssist.Patching
{
    public enum PatchResult
    {
        Success,
        AlreadyPatched,
        FileNotFound,
        BackupFailed,
        PatchFailed,
        Restored
    }

    public static class EFBPatcher
    {
        private const string HtmlFileName = "PMDGTabletCA.html";
        private const string BackupFileName = "PMDGTabletCA.html.bak";
        private const string BridgeJsFileName = "pmdg-efb-accessibility-bridge.js";
        private const string ScriptTag = "<script type=\"text/html\" import-script=\"/Pages/VCockpit/Instruments/PMDGTablet/pmdg-777-200ER/pmdg-efb-accessibility-bridge.js\"></script>";
        private const string HtmlRelativePath = @"html_ui\Pages\VCockpit\Instruments\PMDGTablet\pmdg-777-200ER";

        // Known PMDG 777 package folder names (all variants share the same EFB)
        private static readonly string[] PackageFolderNames = new[]
        {
            "pmdg-aircraft-77er",
            "pmdg-aircraft-77w",
            "pmdg-aircraft-77l",
            "pmdg-aircraft-77f"
        };

        /// <summary>
        /// Checks if the EFB HTML file already contains the bridge script tag.
        /// </summary>
        public static bool IsPatched(string pmdgPackagePath)
        {
            string htmlPath = Path.Combine(pmdgPackagePath, HtmlRelativePath, HtmlFileName);
            if (!File.Exists(htmlPath)) return false;

            string content = File.ReadAllText(htmlPath);
            return content.Contains(BridgeJsFileName);
        }

        /// <summary>
        /// Checks if a backup file exists (indicates a previous patch was applied).
        /// </summary>
        public static bool HasBackup(string pmdgPackagePath)
        {
            string backupPath = Path.Combine(pmdgPackagePath, HtmlRelativePath, BackupFileName);
            return File.Exists(backupPath);
        }

        /// <summary>
        /// Searches for the PMDG 777 package in the MSFS Community folder.
        /// Returns the first found package path, or null if not found.
        /// </summary>
        public static string? FindPMDGPackagePath()
        {
            // Try known MSFS install locations
            var candidatePaths = new List<string>();

            // Check UserCfg.opt for InstalledPackagesPath
            string[] configPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft Flight Simulator", "UserCfg.opt"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft Flight Simulator 2024", "UserCfg.opt"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
            };

            foreach (string configPath in configPaths)
            {
                string? basePath = TryParseInstalledPackagesPath(configPath);
                if (basePath != null)
                {
                    string communityPath = Path.Combine(basePath, "Community");
                    if (Directory.Exists(communityPath))
                    {
                        candidatePaths.Add(communityPath);
                    }
                }
            }

            // Also check common manual install paths
            string[] commonPaths = new[]
            {
                @"D:\MSFS\Community",
                @"C:\MSFS\Community",
                @"E:\MSFS\Community",
            };

            foreach (string path in commonPaths)
            {
                if (Directory.Exists(path) && !candidatePaths.Contains(path))
                {
                    candidatePaths.Add(path);
                }
            }

            // Search for any PMDG 777 variant package folder
            foreach (string communityPath in candidatePaths)
            {
                foreach (string folderName in PackageFolderNames)
                {
                    string packagePath = Path.Combine(communityPath, folderName);
                    string htmlPath = Path.Combine(packagePath, HtmlRelativePath, HtmlFileName);
                    if (File.Exists(htmlPath))
                    {
                        return packagePath;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Applies the bridge patch: creates backup, copies JS file, appends script tag.
        /// </summary>
        public static PatchResult ApplyPatch(string pmdgPackagePath, string bridgeJsSourcePath)
        {
            string htmlDir = Path.Combine(pmdgPackagePath, HtmlRelativePath);
            string htmlPath = Path.Combine(htmlDir, HtmlFileName);
            string backupPath = Path.Combine(htmlDir, BackupFileName);
            string bridgeJsDest = Path.Combine(htmlDir, BridgeJsFileName);

            if (!File.Exists(htmlPath))
                return PatchResult.FileNotFound;

            string content = File.ReadAllText(htmlPath);
            if (content.Contains(BridgeJsFileName))
                return PatchResult.AlreadyPatched;

            try
            {
                // Create backup only if one doesn't already exist (preserve original)
                if (!File.Exists(backupPath))
                {
                    File.Copy(htmlPath, backupPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EFBPatcher: Backup failed: {ex.Message}");
                return PatchResult.BackupFailed;
            }

            try
            {
                // Copy bridge JS file into PMDG assets folder
                File.Copy(bridgeJsSourcePath, bridgeJsDest, overwrite: true);

                // Append script tag to HTML
                content = content.TrimEnd() + "\n" + ScriptTag + "\n";
                File.WriteAllText(htmlPath, content);

                return PatchResult.Success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EFBPatcher: Patch failed: {ex.Message}");
                return PatchResult.PatchFailed;
            }
        }

        /// <summary>
        /// Removes the patch: restores backup and deletes bridge JS.
        /// </summary>
        public static PatchResult RemovePatch(string pmdgPackagePath)
        {
            string htmlDir = Path.Combine(pmdgPackagePath, HtmlRelativePath);
            string htmlPath = Path.Combine(htmlDir, HtmlFileName);
            string backupPath = Path.Combine(htmlDir, BackupFileName);
            string bridgeJsPath = Path.Combine(htmlDir, BridgeJsFileName);

            if (!File.Exists(backupPath))
                return PatchResult.FileNotFound;

            try
            {
                File.Copy(backupPath, htmlPath, overwrite: true);

                if (File.Exists(bridgeJsPath))
                {
                    File.Delete(bridgeJsPath);
                }

                return PatchResult.Restored;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EFBPatcher: Remove failed: {ex.Message}");
                return PatchResult.PatchFailed;
            }
        }

        private static string? TryParseInstalledPackagesPath(string configPath)
        {
            if (!File.Exists(configPath)) return null;

            try
            {
                foreach (string line in File.ReadLines(configPath))
                {
                    if (line.TrimStart().StartsWith("InstalledPackagesPath"))
                    {
                        int quoteStart = line.IndexOf('"');
                        int quoteEnd = line.LastIndexOf('"');
                        if (quoteStart >= 0 && quoteEnd > quoteStart)
                        {
                            return line.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                        }
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Patching/EFBPatcher.cs
git commit -m "feat(pmdg777): add EFB auto-patcher with backup and restoration"
```

---

## Task 3: JavaScript Bridge (pmdg-efb-accessibility-bridge.js)

**Files:**
- Create: `MSFSBlindAssist/Resources/pmdg-efb-accessibility-bridge.js`

- [ ] **Step 1: Create the bridge JS file**

Create `MSFSBlindAssist/Resources/pmdg-efb-accessibility-bridge.js`:

```javascript
// PMDG EFB Accessibility Bridge
// Injected into PMDGTabletCA.html to expose EFB functionality
// to the MSFS Blind Assist application via HTTP on localhost:19777.
(function () {
    'use strict';

    const SERVER_URL = 'http://localhost:19777';
    const COMMAND_POLL_INTERVAL = 500;
    const HEARTBEAT_INTERVAL = 5000;
    const BUS_WAIT_INTERVAL = 500;
    const RECONNECT_INTERVAL = 5000;

    let bus = null;
    let publisher = null;
    let serverConnected = false;
    let commandPollTimer = null;
    let heartbeatTimer = null;

    // --- HTTP Communication ---

    async function postState(type, data) {
        if (!serverConnected) return;
        try {
            await fetch(`${SERVER_URL}/state`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ type, data: data || {} })
            });
        } catch (e) {
            // Server unreachable — will be detected by heartbeat
        }
    }

    async function pollCommands() {
        if (!serverConnected) return;
        try {
            const response = await fetch(`${SERVER_URL}/commands`);
            const commands = await response.json();
            for (const cmd of commands) {
                handleCommand(cmd.command, cmd.payload);
            }
        } catch (e) {
            // Server unreachable — will be detected by heartbeat
        }
    }

    async function tryConnect() {
        try {
            const response = await fetch(`${SERVER_URL}/ping`, {
                signal: AbortSignal.timeout(2000)
            });
            if (response.ok) {
                if (!serverConnected) {
                    serverConnected = true;
                    console.log('[EFB Bridge] Connected to accessibility server');
                    startPolling();
                    postState('connected');
                }
                return true;
            }
        } catch (e) {
            // Not available yet
        }

        if (serverConnected) {
            serverConnected = false;
            console.log('[EFB Bridge] Lost connection to accessibility server');
            stopPolling();
        }
        return false;
    }

    function startPolling() {
        stopPolling();
        commandPollTimer = setInterval(pollCommands, COMMAND_POLL_INTERVAL);
        heartbeatTimer = setInterval(async () => {
            const ok = await tryConnect();
            if (ok) {
                postState('heartbeat');
            }
        }, HEARTBEAT_INTERVAL);
    }

    function stopPolling() {
        if (commandPollTimer) {
            clearInterval(commandPollTimer);
            commandPollTimer = null;
        }
        if (heartbeatTimer) {
            clearInterval(heartbeatTimer);
            heartbeatTimer = null;
        }
    }

    // --- Command Handlers ---

    function handleCommand(command, payload) {
        console.log('[EFB Bridge] Command:', command, payload);
        switch (command) {
            case 'fetch_simbrief':
                cmdFetchSimbrief();
                break;
            case 'send_to_fmc':
                cmdSendToFMC();
                break;
            case 'start_navigraph_auth':
                cmdStartNavigraphAuth();
                break;
            case 'sign_out_navigraph':
                cmdSignOutNavigraph();
                break;
            case 'get_preferences':
                cmdGetPreferences();
                break;
            case 'set_preference':
                if (payload) cmdSetPreference(payload.key, payload.value);
                break;
            case 'save_preferences':
                cmdSavePreferences();
                break;
            default:
                console.warn('[EFB Bridge] Unknown command:', command);
        }
    }

    function cmdFetchSimbrief() {
        if (!publisher) return;
        try {
            // Navigate to EFB dashboard first
            publisher.pub('current_app', 'efb');
            publisher.pub('current_page', 'dashboard');
            // Trigger the SimBrief fetch
            if (typeof Dashboard !== 'undefined' && Dashboard.prototype && Dashboard.prototype.getSimbrief) {
                Dashboard.prototype.getSimbrief.call(null, publisher);
            } else {
                // Fallback: click the fetch button if it exists
                const fetchBtn = document.getElementById('efb_dashboard_requestsimbrief');
                if (fetchBtn) fetchBtn.click();
            }
        } catch (e) {
            postState('error', { message: 'Failed to fetch SimBrief: ' + e.message });
        }
    }

    function cmdSendToFMC() {
        try {
            if (typeof Dashboard !== 'undefined' && Dashboard.simbrief) {
                // Navigate to EFB dashboard
                if (publisher) {
                    publisher.pub('current_app', 'efb');
                    publisher.pub('current_page', 'dashboard');
                }
                // Use the Dashboard's sendSimbriefToPlane method
                // This is an instance method on the Dashboard component, but it accesses
                // Dashboard.simbrief (static), so we replicate the core logic:
                const simbrief_data = Dashboard.simbrief;
                const simbrief_processed_data = {
                    message_tag: "simbrief_data",
                    data: {
                        "dept_icao": simbrief_data.origin.icao_code ? simbrief_data.origin.icao_code.toString() : "",
                        "dest_icao": simbrief_data.destination.icao_code ? simbrief_data.destination.icao_code.toString() : "",
                        "altn_icao": simbrief_data.alternate.icao_code ? simbrief_data.alternate.icao_code.toString() : "",
                        "flight_id": (simbrief_data.general.icao_airline + simbrief_data.general.flight_number) ? (simbrief_data.general.icao_airline + simbrief_data.general.flight_number).toString() : "",
                        "trans_alt": Number(simbrief_data.origin.trans_alt),
                        "trans_lvl": Number(simbrief_data.destination.trans_level),
                        "cost_index": Number(simbrief_data.general.costindex),
                        "crz_alt": Number(simbrief_data.general.initial_altitude),
                        "units_kg": simbrief_data.params.units == "kgs" ? 1 : 0,
                        "zero_fuelweight": Number(simbrief_data.weights.est_zfw),
                        "total_fuelload": Number(simbrief_data.fuel.plan_ramp),
                        "fuel_reserves": Number(simbrief_data.fuel.alternate_burn) + Number(simbrief_data.fuel.reserve),
                        "ave_crzwndspd": Number(simbrief_data.general.avg_wind_spd),
                        "ave_crzwndhdg": Number(simbrief_data.general.avg_wind_dir),
                        "ave_crzisadev": Number(simbrief_data.general.avg_temp_dev)
                    }
                };
                MessageService.postPlaneMessage(simbrief_processed_data);

                // Also send route and weather files
                const route_file = simbrief_data.fms_downloads.pmr.link;
                const wx_file = simbrief_data.fms_downloads.pmw.link;
                const route_params = {
                    message_tag: "route_file",
                    data: { route_fn: route_file ? route_file.toString() : "" }
                };
                const wx_params = {
                    message_tag: "wx_file",
                    data: { wx_fn: wx_file ? wx_file.toString() : "" }
                };
                MessageService.postPlaneMessage(route_params);
                setTimeout(() => {
                    MessageService.postPlaneMessage(wx_params);
                }, 500);

                postState('fmc_upload_started');
            } else {
                postState('error', { message: 'No SimBrief data loaded' });
            }
        } catch (e) {
            postState('error', { message: 'Failed to send to FMC: ' + e.message });
        }
    }

    function cmdStartNavigraphAuth() {
        if (!publisher) return;
        try {
            publisher.pub('current_app', 'efb');
            publisher.pub('current_page', 'authenticate');
            // The Authenticate page's hide() method automatically triggers signInWithDeviceFlow
            // We monitor the navigraph_code DOM element for the code to appear
            startNavigraphCodeObserver();
        } catch (e) {
            postState('error', { message: 'Failed to start Navigraph auth: ' + e.message });
        }
    }

    function cmdSignOutNavigraph() {
        try {
            if (typeof Navigraph !== 'undefined' && Navigraph.auth) {
                Navigraph.auth.signOut();
                postState('navigraph_auth_state', { authenticated: 'false', username: '' });
            }
        } catch (e) {
            postState('error', { message: 'Failed to sign out: ' + e.message });
        }
    }

    function cmdGetPreferences() {
        try {
            const prefs = {};
            const keys = [
                'simbrief_id', 'weather_source', 'weight_unit', 'distance_unit',
                'altitude_unit', 'temperature_unit', 'length_unit', 'speed_unit',
                'airspeed_unit', 'pressure_unit', 'on_screen_keyboard', 'theme_setting'
            ];
            for (const key of keys) {
                if (typeof Settings !== 'undefined' && Settings[key] !== undefined) {
                    prefs[key] = String(Settings[key]);
                }
            }
            postState('preferences', prefs);
        } catch (e) {
            postState('error', { message: 'Failed to read preferences: ' + e.message });
        }
    }

    function cmdSetPreference(key, value) {
        try {
            const elementMap = {
                'simbrief_id': 'efb_preferences_simbrief_id',
                'weather_source': 'efb_preferences_weather_source',
                'weight_unit': 'efb_preferences_weight_unit',
                'distance_unit': 'efb_preferences_distance_unit',
                'altitude_unit': 'efb_preferences_altitude_unit',
                'temperature_unit': 'efb_preferences_temperature_unit',
            };

            const elementId = elementMap[key];
            if (!elementId) return;

            const element = document.getElementById(elementId);
            if (!element) return;

            if (element.tagName === 'INPUT' && element.type === 'text') {
                element.value = value;
            } else if (element.classList.contains('custom-select')) {
                // Set the data-selected attribute for PMDG custom dropdowns
                element.setAttribute('data-selected', value);
                const selectedOption = element.querySelector('.selected-option');
                if (selectedOption) selectedOption.textContent = value;
            } else if (element.type === 'checkbox') {
                element.checked = value === 'true' || value === '1';
            }
        } catch (e) {
            postState('error', { message: 'Failed to set preference: ' + e.message });
        }
    }

    function cmdSavePreferences() {
        try {
            // Navigate to preferences page first
            if (publisher) {
                publisher.pub('current_app', 'efb');
                publisher.pub('current_page', 'preferences');
            }
            // Small delay to ensure page is loaded, then click save
            setTimeout(() => {
                const saveBtn = document.getElementById('efb_preferences_save_tablet_prefs');
                if (saveBtn) saveBtn.click();
            }, 300);
        } catch (e) {
            postState('error', { message: 'Failed to save preferences: ' + e.message });
        }
    }

    // --- Navigraph Auth Code Observer ---

    let navigraphObserver = null;

    function startNavigraphCodeObserver() {
        stopNavigraphCodeObserver();

        // Poll for the code element since MutationObserver may not catch
        // the initial render inside Coherent GT
        const checkCode = setInterval(() => {
            const codeEl = document.getElementById('navigraph_code');
            if (codeEl) {
                const code = codeEl.textContent.trim();
                if (code && code !== '\u00A0' && code !== '&nbsp') {
                    clearInterval(checkCode);
                    postState('navigraph_code', {
                        code: code,
                        url: 'https://navigraph.com/code'
                    });
                }
            }
        }, 500);

        // Stop checking after 60 seconds
        setTimeout(() => clearInterval(checkCode), 60000);
    }

    function stopNavigraphCodeObserver() {
        if (navigraphObserver) {
            navigraphObserver.disconnect();
            navigraphObserver = null;
        }
    }

    // --- EventBus Subscriptions ---

    function subscribeToBusEvents() {
        const subscriber = bus.getSubscriber();

        // Track page changes
        subscriber.on('current_app').whenChanged().handle((app) => {
            postState('page_changed', { app: app, page: '' });
        });
        subscriber.on('current_page').whenChanged().handle((page) => {
            postState('page_changed', { app: '', page: page });
        });

        // Track SimBrief data
        subscriber.on('simbrief_data').whenChanged().handle((data) => {
            try {
                postState('simbrief_loaded', {
                    callsign: data.atc.callsign || '',
                    origin_icao: data.origin.icao_code || '',
                    dest_icao: data.destination.icao_code || '',
                    alt_icao: data.alternate.icao_code || '',
                    cruise_alt: String(data.general.initial_altitude || ''),
                    cost_index: String(data.general.costindex || ''),
                    zfw: String(data.weights.est_zfw || ''),
                    fuel_total: String(data.fuel.plan_ramp || ''),
                    avg_wind: data.general.avg_wind_dir + '/' + data.general.avg_wind_spd
                });
            } catch (e) {
                console.error('[EFB Bridge] Error processing simbrief_data:', e);
            }
        });

        // Track SimBrief fetch results from WASM
        subscriber.on('simbrief_fetch_result').handle((result) => {
            try {
                const parsed = typeof result === 'string' ? JSON.parse(result) : result;
                const success = parsed.data && parsed.data.status === 'ok';
                postState('simbrief_fetch_result', {
                    success: String(success),
                    message: success ? 'Files transferred to FMC' : 'Transfer failed'
                });
            } catch (e) {
                console.error('[EFB Bridge] Error processing simbrief_fetch_result:', e);
            }
        });

        // Track Navigraph auth state
        if (typeof Navigraph !== 'undefined' && Navigraph.auth) {
            Navigraph.auth.onAuthStateChanged((user) => {
                if (user) {
                    postState('navigraph_auth_state', {
                        authenticated: 'true',
                        username: user.preferred_username || user.name || 'Unknown'
                    });
                } else {
                    postState('navigraph_auth_state', {
                        authenticated: 'false',
                        username: ''
                    });
                }
            }, true);
        }
    }

    // --- Initialization ---

    function initBridge(eventBus) {
        bus = eventBus;
        publisher = bus.getPublisher();
        console.log('[EFB Bridge] EventBus acquired, subscribing to events');
        subscribeToBusEvents();
        startConnectionLoop();
    }

    function startConnectionLoop() {
        tryConnect();
        // Keep trying to connect/reconnect
        setInterval(async () => {
            if (!serverConnected) {
                await tryConnect();
            }
        }, RECONNECT_INTERVAL);
    }

    // Wait for the EFB's MessageService to initialize and expose the bus
    function waitForBus() {
        const check = setInterval(() => {
            if (typeof MessageService !== 'undefined' && MessageService.messaging_bus) {
                clearInterval(check);
                console.log('[EFB Bridge] MessageService.messaging_bus found');
                initBridge(MessageService.messaging_bus);
            }
        }, BUS_WAIT_INTERVAL);
    }

    // Entry point
    console.log('[EFB Bridge] Accessibility bridge script loaded, waiting for EventBus...');
    waitForBus();
})();
```

- [ ] **Step 2: Set the JS file as "Copy to Output Directory" in the project**

Add to `MSFSBlindAssist.csproj` inside an `<ItemGroup>`:

```xml
<None Update="Resources\pmdg-efb-accessibility-bridge.js">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded, JS file copied to output directory

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Resources/pmdg-efb-accessibility-bridge.js MSFSBlindAssist/MSFSBlindAssist.csproj
git commit -m "feat(pmdg777): add JavaScript EFB accessibility bridge"
```

---

## Task 4: Accessible Form (PMDG777EFBForm)

**Files:**
- Create: `MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.cs`
- Create: `MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.Designer.cs`
- Read (reference): `MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs` — form pattern
- Read (reference): `MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.Designer.cs` — designer pattern
- Read (reference): `MSFSBlindAssist/Controls/AccessibleTabControl.cs` — tab control

- [ ] **Step 1: Create PMDG777EFBForm.Designer.cs**

Create `MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.Designer.cs`:

```csharp
namespace MSFSBlindAssist.Forms.PMDG777
{
    partial class PMDG777EFBForm
    {
        private System.ComponentModel.IContainer? components = null;

        // Tab control
        private Controls.AccessibleTabControl? tabControl;
        private TabPage? simbriefTab;
        private TabPage? navigraphTab;
        private TabPage? preferencesTab;

        // SimBrief tab controls
        private Button? fetchSimbriefButton;
        private Label? simbriefStatusLabel;
        private Label? callsignLabel;
        private Label? callsignValue;
        private Label? originLabel;
        private Label? originValue;
        private Label? destLabel;
        private Label? destValue;
        private Label? altLabel;
        private Label? altValue;
        private Label? cruiseAltLabel;
        private Label? cruiseAltValue;
        private Label? costIndexLabel;
        private Label? costIndexValue;
        private Label? zfwLabel;
        private Label? zfwValue;
        private Label? fuelLabel;
        private Label? fuelValue;
        private Label? windLabel;
        private Label? windValue;
        private Button? sendToFmcButton;

        // Navigraph tab controls
        private Label? navigraphStatusLabel;
        private Button? navigraphSignInButton;
        private Label? authCodeLabel;
        private TextBox? authCodeTextBox;
        private Button? navigraphSignOutButton;

        // Preferences tab controls
        private Label? simbriefAliasLabel;
        private TextBox? simbriefAliasTextBox;
        private Label? weatherSourceLabel;
        private ComboBox? weatherSourceCombo;
        private Label? weightUnitLabel;
        private ComboBox? weightUnitCombo;
        private Label? distanceUnitLabel;
        private ComboBox? distanceUnitCombo;
        private Label? altitudeUnitLabel;
        private ComboBox? altitudeUnitCombo;
        private Label? temperatureUnitLabel;
        private ComboBox? temperatureUnitCombo;
        private Button? savePreferencesButton;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.Text = "PMDG 777 EFB";
            this.Size = new System.Drawing.Size(500, 520);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.KeyPreview = true;
            this.AccessibleName = "PMDG 777 EFB";

            // Tab control
            tabControl = new Controls.AccessibleTabControl();
            tabControl.Dock = DockStyle.Fill;

            // === SimBrief Tab ===
            simbriefTab = new TabPage("SimBrief");
            simbriefTab.Padding = new Padding(10);

            int y = 10;
            const int labelX = 10;
            const int valueX = 160;
            const int rowHeight = 28;

            simbriefStatusLabel = new Label { Text = "Ready", Location = new System.Drawing.Point(labelX, y), AutoSize = true, Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold), AccessibleName = "Status" };
            y += rowHeight + 5;

            fetchSimbriefButton = new Button { Text = "Fetch SimBrief", Location = new System.Drawing.Point(labelX, y), Size = new System.Drawing.Size(140, 30), AccessibleName = "Fetch SimBrief" };
            sendToFmcButton = new Button { Text = "Send to FMC", Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(140, 30), Enabled = false, AccessibleName = "Send to FMC" };
            y += 40;

            callsignLabel = new Label { Text = "Callsign:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            callsignValue = new Label { Text = "—", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Callsign" };
            y += rowHeight;

            originLabel = new Label { Text = "Origin:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            originValue = new Label { Text = "—", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Origin" };
            y += rowHeight;

            destLabel = new Label { Text = "Destination:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            destValue = new Label { Text = "—", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Destination" };
            y += rowHeight;

            altLabel = new Label { Text = "Alternate:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            altValue = new Label { Text = "—", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Alternate" };
            y += rowHeight;

            cruiseAltLabel = new Label { Text = "Cruise Altitude:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            cruiseAltValue = new Label { Text = "—", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Cruise Altitude" };
            y += rowHeight;

            costIndexLabel = new Label { Text = "Cost Index:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            costIndexValue = new Label { Text = "—", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Cost Index" };
            y += rowHeight;

            zfwLabel = new Label { Text = "ZFW:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            zfwValue = new Label { Text = "—", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Zero Fuel Weight" };
            y += rowHeight;

            fuelLabel = new Label { Text = "Total Fuel:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            fuelValue = new Label { Text = "—", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Total Fuel" };
            y += rowHeight;

            windLabel = new Label { Text = "Average Wind:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            windValue = new Label { Text = "—", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Average Wind" };

            simbriefTab.Controls.AddRange(new Control[] {
                simbriefStatusLabel, fetchSimbriefButton, sendToFmcButton,
                callsignLabel, callsignValue, originLabel, originValue,
                destLabel, destValue, altLabel, altValue,
                cruiseAltLabel, cruiseAltValue, costIndexLabel, costIndexValue,
                zfwLabel, zfwValue, fuelLabel, fuelValue,
                windLabel, windValue
            });

            // === Navigraph Tab ===
            navigraphTab = new TabPage("Navigraph");
            navigraphTab.Padding = new Padding(10);
            y = 10;

            navigraphStatusLabel = new Label { Text = "Not authenticated", Location = new System.Drawing.Point(labelX, y), AutoSize = true, Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold), AccessibleName = "Navigraph Status" };
            y += rowHeight + 5;

            navigraphSignInButton = new Button { Text = "Sign In", Location = new System.Drawing.Point(labelX, y), Size = new System.Drawing.Size(140, 30), AccessibleName = "Sign In to Navigraph" };
            navigraphSignOutButton = new Button { Text = "Sign Out", Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(140, 30), Enabled = false, AccessibleName = "Sign Out of Navigraph" };
            y += 40;

            authCodeLabel = new Label { Text = "Auth Code:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            authCodeTextBox = new TextBox { Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(200, 25), ReadOnly = true, AccessibleName = "Navigraph Auth Code" };

            navigraphTab.Controls.AddRange(new Control[] {
                navigraphStatusLabel, navigraphSignInButton, navigraphSignOutButton,
                authCodeLabel, authCodeTextBox
            });

            // === Preferences Tab ===
            preferencesTab = new TabPage("Preferences");
            preferencesTab.Padding = new Padding(10);
            y = 10;

            simbriefAliasLabel = new Label { Text = "SimBrief Alias:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            simbriefAliasTextBox = new TextBox { Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(200, 25), AccessibleName = "SimBrief Alias" };
            y += rowHeight + 5;

            weatherSourceLabel = new Label { Text = "Weather Source:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            weatherSourceCombo = new ComboBox { Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Weather Source" };
            weatherSourceCombo.Items.AddRange(new object[] { "SIM", "REAL-WORLD" });
            y += rowHeight + 5;

            weightUnitLabel = new Label { Text = "Weight Unit:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            weightUnitCombo = new ComboBox { Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Weight Unit" };
            weightUnitCombo.Items.AddRange(new object[] { "lb", "kg" });
            y += rowHeight + 5;

            distanceUnitLabel = new Label { Text = "Distance Unit:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            distanceUnitCombo = new ComboBox { Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Distance Unit" };
            distanceUnitCombo.Items.AddRange(new object[] { "nm", "km" });
            y += rowHeight + 5;

            altitudeUnitLabel = new Label { Text = "Altitude Unit:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            altitudeUnitCombo = new ComboBox { Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Altitude Unit" };
            altitudeUnitCombo.Items.AddRange(new object[] { "ft", "m" });
            y += rowHeight + 5;

            temperatureUnitLabel = new Label { Text = "Temperature Unit:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            temperatureUnitCombo = new ComboBox { Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Temperature Unit" };
            temperatureUnitCombo.Items.AddRange(new object[] { "C", "F" });
            y += rowHeight + 10;

            savePreferencesButton = new Button { Text = "Save Preferences", Location = new System.Drawing.Point(labelX, y), Size = new System.Drawing.Size(140, 30), AccessibleName = "Save Preferences" };

            preferencesTab.Controls.AddRange(new Control[] {
                simbriefAliasLabel, simbriefAliasTextBox,
                weatherSourceLabel, weatherSourceCombo,
                weightUnitLabel, weightUnitCombo,
                distanceUnitLabel, distanceUnitCombo,
                altitudeUnitLabel, altitudeUnitCombo,
                temperatureUnitLabel, temperatureUnitCombo,
                savePreferencesButton
            });

            // Assemble tabs
            tabControl.TabPages.Add(simbriefTab);
            tabControl.TabPages.Add(navigraphTab);
            tabControl.TabPages.Add(preferencesTab);
            this.Controls.Add(tabControl);

            // Tab order
            fetchSimbriefButton.TabIndex = 0;
            sendToFmcButton.TabIndex = 1;
            navigraphSignInButton.TabIndex = 0;
            navigraphSignOutButton.TabIndex = 1;
            authCodeTextBox.TabIndex = 2;
            simbriefAliasTextBox.TabIndex = 0;
            weatherSourceCombo.TabIndex = 1;
            weightUnitCombo.TabIndex = 2;
            distanceUnitCombo.TabIndex = 3;
            altitudeUnitCombo.TabIndex = 4;
            temperatureUnitCombo.TabIndex = 5;
            savePreferencesButton.TabIndex = 6;
        }
    }
}
```

- [ ] **Step 2: Create PMDG777EFBForm.cs**

Create `MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG777
{
    public partial class PMDG777EFBForm : Form
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly EFBBridgeServer _bridgeServer;
        private readonly ScreenReaderAnnouncer _announcer;
        private IntPtr _previousWindow = IntPtr.Zero;
        private bool _simbriefLoaded = false;

        public PMDG777EFBForm(EFBBridgeServer bridgeServer, ScreenReaderAnnouncer announcer)
        {
            _bridgeServer = bridgeServer;
            _announcer = announcer;

            InitializeComponent();
            SetupEventHandlers();
        }

        public void ShowForm()
        {
            _previousWindow = GetForegroundWindow();
            Show();
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false;

            fetchSimbriefButton?.Focus();
        }

        private void SetupEventHandlers()
        {
            _bridgeServer.StateUpdated += OnStateUpdated;

            fetchSimbriefButton!.Click += (_, _) =>
            {
                simbriefStatusLabel!.Text = "Fetching...";
                _bridgeServer.EnqueueCommand("fetch_simbrief");
            };

            sendToFmcButton!.Click += (_, _) =>
            {
                _bridgeServer.EnqueueCommand("send_to_fmc");
            };

            navigraphSignInButton!.Click += (_, _) =>
            {
                navigraphStatusLabel!.Text = "Awaiting code...";
                authCodeTextBox!.Text = "";
                _bridgeServer.EnqueueCommand("start_navigraph_auth");
            };

            navigraphSignOutButton!.Click += (_, _) =>
            {
                _bridgeServer.EnqueueCommand("sign_out_navigraph");
            };

            savePreferencesButton!.Click += OnSavePreferences;

            tabControl!.SelectedIndexChanged += (_, _) =>
            {
                if (tabControl.SelectedTab == preferencesTab)
                {
                    _bridgeServer.EnqueueCommand("get_preferences");
                }
            };
        }

        private void OnStateUpdated(object? sender, EFBStateUpdateEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;

            switch (e.Type)
            {
                case "connected":
                    _announcer.Announce("EFB bridge connected");
                    break;

                case "simbrief_loaded":
                    _simbriefLoaded = true;
                    UpdateFlightDetails(e.Data);
                    simbriefStatusLabel!.Text = "Loaded";
                    sendToFmcButton!.Enabled = true;
                    string origin = e.Data.GetValueOrDefault("origin_icao", "");
                    string dest = e.Data.GetValueOrDefault("dest_icao", "");
                    _announcer.Announce($"SimBrief flight plan loaded: {origin} to {dest}");
                    break;

                case "simbrief_fetch_result":
                    bool success = e.Data.GetValueOrDefault("success", "false") == "true" ||
                                   e.Data.GetValueOrDefault("success", "false") == "True";
                    string message = e.Data.GetValueOrDefault("message", "");
                    if (success)
                    {
                        _announcer.Announce($"FMC file transfer complete: {message}");
                    }
                    else if (!string.IsNullOrEmpty(message))
                    {
                        _announcer.Announce($"FMC transfer result: {message}");
                    }
                    break;

                case "fmc_upload_started":
                    _announcer.Announce("Flight plan sent to FMC");
                    break;

                case "navigraph_code":
                    string code = e.Data.GetValueOrDefault("code", "");
                    string url = e.Data.GetValueOrDefault("url", "https://navigraph.com/code");
                    authCodeTextBox!.Text = code;
                    _announcer.Announce($"Navigraph sign-in code: {code}. Opening browser.");
                    try
                    {
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch { }
                    break;

                case "navigraph_auth_state":
                    bool authenticated = e.Data.GetValueOrDefault("authenticated", "false") == "true";
                    string username = e.Data.GetValueOrDefault("username", "");
                    if (authenticated)
                    {
                        navigraphStatusLabel!.Text = $"Authenticated as: {username}";
                        navigraphSignInButton!.Enabled = false;
                        navigraphSignOutButton!.Enabled = true;
                        _announcer.Announce($"Signed in to Navigraph as {username}");
                    }
                    else
                    {
                        navigraphStatusLabel!.Text = "Not authenticated";
                        navigraphSignInButton!.Enabled = true;
                        navigraphSignOutButton!.Enabled = false;
                        authCodeTextBox!.Text = "";
                        // Only announce if this was a sign-out (not initial state)
                        if (!string.IsNullOrEmpty(username))
                        {
                            _announcer.Announce("Signed out of Navigraph");
                        }
                    }
                    break;

                case "preferences":
                    PopulatePreferences(e.Data);
                    break;

                case "error":
                    string errorMsg = e.Data.GetValueOrDefault("message", "Unknown error");
                    simbriefStatusLabel!.Text = $"Error: {errorMsg}";
                    _announcer.Announce($"EFB error: {errorMsg}");
                    break;
            }
        }

        private void UpdateFlightDetails(Dictionary<string, string> data)
        {
            callsignValue!.Text = data.GetValueOrDefault("callsign", "—");
            originValue!.Text = data.GetValueOrDefault("origin_icao", "—");
            destValue!.Text = data.GetValueOrDefault("dest_icao", "—");
            altValue!.Text = data.GetValueOrDefault("alt_icao", "—");
            cruiseAltValue!.Text = data.GetValueOrDefault("cruise_alt", "—");
            costIndexValue!.Text = data.GetValueOrDefault("cost_index", "—");
            zfwValue!.Text = data.GetValueOrDefault("zfw", "—");
            fuelValue!.Text = data.GetValueOrDefault("fuel_total", "—");
            windValue!.Text = data.GetValueOrDefault("avg_wind", "—");
        }

        private void PopulatePreferences(Dictionary<string, string> data)
        {
            if (data.TryGetValue("simbrief_id", out string? simbriefId))
                simbriefAliasTextBox!.Text = simbriefId;

            SetComboValue(weatherSourceCombo!, data.GetValueOrDefault("weather_source", ""));
            SetComboValue(weightUnitCombo!, data.GetValueOrDefault("weight_unit", ""));
            SetComboValue(distanceUnitCombo!, data.GetValueOrDefault("distance_unit", ""));
            SetComboValue(altitudeUnitCombo!, data.GetValueOrDefault("altitude_unit", ""));
            SetComboValue(temperatureUnitCombo!, data.GetValueOrDefault("temperature_unit", ""));
        }

        private static void SetComboValue(ComboBox combo, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(combo.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void OnSavePreferences(object? sender, EventArgs e)
        {
            // Send each preference value
            if (!string.IsNullOrEmpty(simbriefAliasTextBox!.Text))
            {
                _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                {
                    { "key", "simbrief_id" }, { "value", simbriefAliasTextBox.Text }
                });
            }

            if (weatherSourceCombo!.SelectedItem != null)
            {
                _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                {
                    { "key", "weather_source" }, { "value", weatherSourceCombo.SelectedItem.ToString()! }
                });
            }

            if (weightUnitCombo!.SelectedItem != null)
            {
                _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                {
                    { "key", "weight_unit" }, { "value", weightUnitCombo.SelectedItem.ToString()! }
                });
            }

            if (distanceUnitCombo!.SelectedItem != null)
            {
                _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                {
                    { "key", "distance_unit" }, { "value", distanceUnitCombo.SelectedItem.ToString()! }
                });
            }

            if (altitudeUnitCombo!.SelectedItem != null)
            {
                _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                {
                    { "key", "altitude_unit" }, { "value", altitudeUnitCombo.SelectedItem.ToString()! }
                });
            }

            if (temperatureUnitCombo!.SelectedItem != null)
            {
                _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                {
                    { "key", "temperature_unit" }, { "value", temperatureUnitCombo.SelectedItem.ToString()! }
                });
            }

            // Trigger the EFB's save
            _bridgeServer.EnqueueCommand("save_preferences");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _bridgeServer.StateUpdated -= OnStateUpdated;

            if (_previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(_previousWindow);
            }

            base.OnFormClosing(e);
        }
    }
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.cs MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.Designer.cs
git commit -m "feat(pmdg777): add accessible EFB form with SimBrief, Navigraph, and preferences tabs"
```

---

## Task 5: Hotkey Integration

**Files:**
- Modify: `MSFSBlindAssist/Hotkeys/HotkeyManager.cs`
- Modify: `MSFSBlindAssist/HotkeyGuides/PMDG_777_Hotkeys.txt`

- [ ] **Step 1: Add hotkey constant, enum value, registration, and handler**

In `MSFSBlindAssist/Hotkeys/HotkeyManager.cs`:

Add the constant after line 132 (`HOTKEY_NEAREST_CITY = 9093`):

```csharp
        // PMDG 777 EFB hotkey ID
        private const int HOTKEY_PMDG_777_EFB = 9094;
```

Add the enum value after `ShowFenixMCDU` (after line 1060):

```csharp
        ShowPMDG777EFB,
```

Add the registration in `ActivateInputHotkeyMode()` after line 728 (`HOTKEY_FENIX_MCDU` registration):

```csharp
            RegisterHotKey(windowHandle, HOTKEY_PMDG_777_EFB, MOD_SHIFT, 0x54);  // Shift+T (PMDG 777 EFB Tablet)
```

Add the handler case in the input mode section of `ProcessWindowMessage`, after the `HOTKEY_FENIX_MCDU` case (after line 460):

```csharp
                        case HOTKEY_PMDG_777_EFB:
                            TriggerHotkey(HotkeyAction.ShowPMDG777EFB);
                            break;
```

Add the unregister call in `DeactivateInputHotkeyMode()` alongside the other UnregisterHotKey calls:

```csharp
            UnregisterHotKey(windowHandle, HOTKEY_PMDG_777_EFB);
```

- [ ] **Step 2: Update hotkey guide**

In `MSFSBlindAssist/HotkeyGuides/PMDG_777_Hotkeys.txt`, add after the `Shift+E` line (line 91):

```
  Shift+T    PMDG EFB Tablet (SimBrief uplink, Navigraph, preferences)
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Hotkeys/HotkeyManager.cs MSFSBlindAssist/HotkeyGuides/PMDG_777_Hotkeys.txt
git commit -m "feat(pmdg777): add Shift+T hotkey for EFB tablet form"
```

---

## Task 6: MainForm Integration

Wire up the EFB bridge server, form lifecycle, and patcher into MainForm.

**Files:**
- Modify: `MSFSBlindAssist/MainForm.cs`

- [ ] **Step 1: Add field declarations**

In `MainForm.cs`, add field declarations near the existing `pmdg777CDUForm` field (around line 33):

```csharp
        private PMDG777EFBForm? pmdg777EFBForm;
        private EFBBridgeServer? efbBridgeServer;
        private string? pmdgPackagePath;
```

Add the required using statements at the top of the file if not already present:

```csharp
using MSFSBlindAssist.Forms.PMDG777;
using MSFSBlindAssist.Patching;
using MSFSBlindAssist.SimConnect;
```

- [ ] **Step 2: Add the ShowPMDG777EFBDialog method**

Add this method near the existing `ShowPMDG777CDUDialog` method (around line 1390):

```csharp
        private void ShowPMDG777EFBDialog()
        {
            hotkeyManager.ExitInputHotkeyMode();

            if (efbBridgeServer == null || !efbBridgeServer.IsRunning)
            {
                announcer.Announce("EFB bridge server is not running. Please ensure the EFB patch is installed and restart the flight.");
                return;
            }

            if (pmdg777EFBForm == null || pmdg777EFBForm.IsDisposed)
            {
                pmdg777EFBForm = new PMDG777EFBForm(efbBridgeServer, announcer);
            }

            pmdg777EFBForm.ShowForm();
        }
```

- [ ] **Step 3: Add hotkey handler case**

In the `OnHotkeyTriggered` method, add a new case in the switch statement (near the other PMDG 777 cases):

```csharp
                case HotkeyAction.ShowPMDG777EFB:
                    if (currentAircraft?.AircraftCode == "PMDG_777")
                    {
                        ShowPMDG777EFBDialog();
                    }
                    break;
```

- [ ] **Step 4: Add patcher and server integration to PMDG 777 aircraft switching**

In the `SwitchAircraft` method, in the section where PMDG 777 is initialized (around line 2177, the `if (newAircraft.AircraftCode == "PMDG_777" && simConnectManager.IsConnected)` block), add the patcher check and server start **after** the existing PMDG 777 initialization:

```csharp
            // EFB bridge: patcher check and server start
            if (newAircraft.AircraftCode == "PMDG_777")
            {
                CheckAndOfferEFBPatch();
                StartEFBBridgeServer();
            }
            else
            {
                StopEFBBridgeServer();
            }
```

Add these helper methods:

```csharp
        private void CheckAndOfferEFBPatch()
        {
            pmdgPackagePath ??= EFBPatcher.FindPMDGPackagePath();
            if (pmdgPackagePath == null)
            {
                System.Diagnostics.Debug.WriteLine("EFB Patcher: Could not find PMDG 777 package folder");
                return;
            }

            if (EFBPatcher.IsPatched(pmdgPackagePath))
                return;

            bool hadPreviousPatch = EFBPatcher.HasBackup(pmdgPackagePath);
            string message = hadPreviousPatch
                ? "The PMDG EFB accessibility bridge needs to be re-installed (likely after a PMDG update). Would you like to install it now?"
                : "The PMDG EFB accessibility bridge is not installed. Would you like to install it? A backup of the original file will be created.";

            announcer.Announce(message);
            var result = MessageBox.Show(message, "EFB Accessibility Bridge", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                string bridgeJsSource = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "pmdg-efb-accessibility-bridge.js");
                if (!File.Exists(bridgeJsSource))
                {
                    announcer.Announce("Bridge script file not found. Cannot install patch.");
                    return;
                }

                var patchResult = EFBPatcher.ApplyPatch(pmdgPackagePath, bridgeJsSource);
                switch (patchResult)
                {
                    case PatchResult.Success:
                        announcer.Announce("EFB accessibility bridge installed. Restart the flight or reload the aircraft for changes to take effect.");
                        break;
                    case PatchResult.AlreadyPatched:
                        announcer.Announce("EFB accessibility bridge is already installed.");
                        break;
                    default:
                        announcer.Announce($"Failed to install EFB accessibility bridge: {patchResult}");
                        break;
                }
            }
        }

        private void StartEFBBridgeServer()
        {
            if (efbBridgeServer == null)
            {
                efbBridgeServer = new EFBBridgeServer();
            }

            if (!efbBridgeServer.IsRunning)
            {
                efbBridgeServer.Start();
            }
        }

        private void StopEFBBridgeServer()
        {
            if (pmdg777EFBForm != null && !pmdg777EFBForm.IsDisposed)
            {
                pmdg777EFBForm.Dispose();
                pmdg777EFBForm = null;
            }

            efbBridgeServer?.Stop();
        }
```

- [ ] **Step 5: Add cleanup on aircraft switch and app close**

In the aircraft switching cleanup section (around line 2169, near the PMDG 777 CDU form disposal), add:

```csharp
        // Dispose PMDG 777 EFB form when switching aircraft
        if (pmdg777EFBForm != null && !pmdg777EFBForm.IsDisposed)
        {
            pmdg777EFBForm.Dispose();
            pmdg777EFBForm = null;
        }
```

In the form closing handler (where other forms are cleaned up), add:

```csharp
        efbBridgeServer?.Dispose();
        efbBridgeServer = null;
```

- [ ] **Step 6: Verify it compiles**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/MainForm.cs
git commit -m "feat(pmdg777): integrate EFB bridge server, form, and patcher into MainForm"
```

---

## Task 7: Build Verification and Smoke Test

- [ ] **Step 1: Full clean build**

Run:
```bash
dotnet build MSFSBlindAssist.sln -c Debug --no-incremental
```
Expected: Build succeeded with 0 errors

- [ ] **Step 2: Verify JS file is in output**

Run:
```bash
ls MSFSBlindAssist/bin/x64/Debug/net9.0-windows/win-x64/Resources/pmdg-efb-accessibility-bridge.js
```
Expected: File exists

- [ ] **Step 3: Commit build fix if needed**

If any compilation errors were found and fixed in previous steps, commit the fixes:

```bash
git add -A
git commit -m "fix(pmdg777): resolve build issues from EFB bridge integration"
```
