# PMDG 777 EFB Accessibility Bridge — Design Spec

## Problem

The PMDG 777's Electronic Flight Bag (EFB) is a touchscreen tablet with no SDK variables for reading or manipulating its state. It is completely inaccessible to blind sim pilots. Key functions locked behind the EFB include:

- **SimBrief uplink** — fetching the OFP and sending route/weather data to the FMC
- **Navigraph authentication** — required for SimBrief uplink and chart access
- **EFB preferences** — configuring the SimBrief alias, weather source, unit settings

## Solution

Inject a JavaScript accessibility bridge into the EFB's HTML page. The bridge hooks into the EFB's internal EventBus and exposes its functionality over HTTP to a C# accessible form.

## Scope

- SimBrief: fetch OFP, review flight details, send route + weather to FMC
- Navigraph: device-flow authentication (display code, open browser, track status)
- Preferences: SimBrief alias, weather source, unit settings, save/load
- Auto-patcher to install/restore the injection with user consent

Out of scope (future work): Ground Operations, Weight & Balance, Performance Tool, Charts, OFP viewer, First Officer's EFB.

## Architecture

```
EFB JS (EventBus/DOM)
    │
    ▼
Bridge JS (pmdg-efb-accessibility-bridge.js)
    │
    ▼ HTTP (localhost:19777)
    │
EFBBridgeServer (C# HttpListener)
    │
    ▼ C# events
    │
PMDG777EFBForm (accessible Windows Form)
    │
    ▼
Screen Reader (NVDA/JAWS)
```

Data flows bidirectionally:
- **JS → C#**: Bridge POSTs state updates to `/state` as JSON
- **C# → JS**: Bridge polls `/commands` for pending actions (500ms interval)

---

## Component 1: Injected JavaScript Bridge

**File:** `pmdg-efb-accessibility-bridge.js`, placed alongside `PMDGTablet.js` in the PMDG tablet assets folder.

**Loading:** Added to `PMDGTabletCA.html` via:
```html
<script type="text/html" import-script="/Pages/VCockpit/Instruments/PMDGTablet/pmdg-777-200ER/pmdg-efb-accessibility-bridge.js"></script>
```

### Startup Sequence

1. Poll for `MessageService.messaging_bus` at 500ms intervals until available (the EFB sets this static property during initialization)
2. Store the bus reference, subscribe to EventBus topics
3. Attempt connection to `http://localhost:19777/ping`
   - On success: begin pushing state and polling for commands
   - On failure: log to console, retry every 5 seconds
4. Subscribe to Navigraph auth state changes

### State Updates (POST `/state`)

The bridge pushes JSON payloads to the C# server when state changes:

| Type | Trigger | Payload |
|------|---------|---------|
| `connected` | Bridge starts successfully | `{}` |
| `heartbeat` | Every 5 seconds | `{}` |
| `page_changed` | EventBus `current_app` or `current_page` changes | `{"app": "efb", "page": "dashboard"}` |
| `simbrief_loaded` | EventBus `simbrief_data` fires | `{"callsign": "...", "origin_icao": "...", "dest_icao": "...", "alt_icao": "...", "cruise_alt": "...", "cost_index": "...", "zfw": "...", "fuel_total": "...", "avg_wind": "..."}` |
| `simbrief_fetch_result` | EventBus `simbrief_fetch_result` fires | `{"success": true/false, "message": "..."}` |
| `fmc_upload_started` | `sendSimbriefToPlane` is called | `{}` |
| `navigraph_auth_state` | `Navigraph.auth.onAuthStateChanged` | `{"authenticated": true/false, "username": "..."}` |
| `navigraph_code` | `signInWithDeviceFlow` callback receives params | `{"code": "ABCD-1234", "url": "https://navigraph.com/code"}` |
| `preferences` | Response to `get_preferences` command | `{"simbrief_id": "...", "weather_source": "SIM", "weight_unit": "lb", ...}` |
| `error` | Any error condition | `{"message": "..."}` |

### Commands (polled via GET `/commands`)

The bridge polls every 500ms. Response is a JSON array of command objects:

| Command | Payload | Action |
|---------|---------|--------|
| `fetch_simbrief` | — | Gets the publisher from `MessageService.messaging_bus`, calls `Dashboard.getSimbrief(publisher)` |
| `send_to_fmc` | — | Calls `Dashboard.sendSimbriefToPlane(Dashboard.simbrief)`. Only valid after SimBrief data is loaded. |
| `start_navigraph_auth` | — | Navigates to the auth page: `publisher.pub('current_app', 'efb')` then `publisher.pub('current_page', 'authenticate')` |
| `sign_out_navigraph` | — | Calls `Navigraph.auth.signOut()` |
| `get_preferences` | — | Reads all preference values via `DataStoreWrapper.get()` and pushes a `preferences` state update |
| `set_preference` | `{"key": "simbrief_id", "value": "myalias"}` | Sets the DOM input value for the specified preference |
| `save_preferences` | — | Triggers click on `efb_preferences_save_tablet_prefs` button |

### EventBus Access

`MessageService.messaging_bus` is a static property set during `MessageService` construction, which happens during EFB initialization. The bridge polls for it:

```javascript
const waitForBus = setInterval(() => {
    if (typeof MessageService !== 'undefined' && MessageService.messaging_bus) {
        clearInterval(waitForBus);
        initBridge(MessageService.messaging_bus);
    }
}, 500);
```

Once available, the bridge subscribes via `bus.getSubscriber().on(topic).handle(callback)`.

### Navigraph Auth Interception

The Navigraph device flow is triggered when the user navigates to the Authenticate page. The EFB calls `Navigraph.auth.signInWithDeviceFlow(callback)` where the callback receives `{verification_uri_complete, user_code}`. The bridge intercepts this by:

1. Sending the `start_navigraph_auth` command navigates to the auth page
2. The EFB's own `Authenticate.hide()` method fires `signInWithDeviceFlow`
3. The bridge monitors the `navigraph_code` DOM element (`document.getElementById("navigraph_code")`) via a MutationObserver for when the code text appears
4. Pushes the code to C# as a `navigraph_code` state update
5. Also subscribes to `Navigraph.auth.onAuthStateChanged` to detect when auth completes

---

## Component 2: HTTP Server (EFBBridgeServer)

**File:** `MSFSBlindAssist/SimConnect/EFBBridgeServer.cs`

A lightweight HTTP server for bridge communication.

### Endpoints

| Method | Path | Request | Response |
|--------|------|---------|----------|
| GET | `/ping` | — | `{"status": "ok"}` |
| POST | `/state` | JSON state update | `{"received": true}` |
| GET | `/commands` | — | JSON array of pending commands |

All responses include `Access-Control-Allow-Origin: *` for CORS.
A preflight OPTIONS handler returns appropriate CORS headers for the POST endpoint.

### Class Design

```csharp
public class EFBBridgeServer : IDisposable
{
    // Configuration
    private const int Port = 19777;
    private const string Prefix = "http://localhost:19777/";

    // State
    private HttpListener _listener;
    private CancellationTokenSource _cts;
    private readonly ConcurrentQueue<EFBCommand> _commandQueue;

    // Lifecycle
    public void Start();       // Starts listener on background thread
    public void Stop();        // Stops listener, cancels token
    public void Dispose();

    // Commands (called by the form)
    public void EnqueueCommand(string command, Dictionary<string, string>? payload = null);

    // Events (consumed by the form)
    public event EventHandler<EFBStateUpdateEventArgs>? StateUpdated;

    // Properties
    public bool IsRunning { get; }
    public bool IsBridgeConnected { get; }  // True if heartbeat received within last 15s
}

public class EFBStateUpdateEventArgs : EventArgs
{
    public string Type { get; set; }
    public Dictionary<string, string> Data { get; set; }
}

public class EFBCommand
{
    public string Command { get; set; }
    public Dictionary<string, string>? Payload { get; set; }
}
```

### Threading

- The `HttpListener` runs on a background thread via `Task.Run`
- State updates received on the listener thread are marshalled to the UI thread via `SynchronizationContext.Post` before raising `StateUpdated`
- The command queue uses `ConcurrentQueue<EFBCommand>` for thread-safe enqueue (form thread) and dequeue (listener thread)

### Lifecycle

- **Start**: Called when the PMDG 777 aircraft profile is loaded, after the patcher has run
- **Stop**: Called when switching aircraft or closing the app
- Server gracefully handles the JS bridge not being connected (commands queue up and expire after 30 seconds)

---

## Component 3: Accessible Form (PMDG777EFBForm)

**File:** `MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.cs`

### Opening

- New `HotkeyAction.ShowPMDG777EFB` in the hotkey system
- Singleton lifecycle managed by `MainForm`, same pattern as `PMDG777CDUForm`
- Opened via hotkey; `MainForm.ShowPMDG777EFBDialog()` creates/shows the form

### Constructor

```csharp
public PMDG777EFBForm(EFBBridgeServer bridgeServer, ScreenReaderAnnouncer announcer)
```

Subscribes to `bridgeServer.StateUpdated` to receive all state changes.

### Tab 1: SimBrief

Controls:
- **Fetch SimBrief** button — enqueues `fetch_simbrief` command
- **Status** label — shows "Ready", "Fetching...", "Loaded", "Error: ..."
- **Flight details** group — read-only fields populated when `simbrief_loaded` arrives:
  - Callsign, Origin ICAO, Destination ICAO, Alternate ICAO
  - Cruise Altitude, Cost Index, ZFW, Total Fuel, Average Wind
- **Send to FMC** button — enqueues `send_to_fmc` command, enabled only after SimBrief data loads

### Tab 2: Navigraph

Controls:
- **Status** label — "Not authenticated", "Authenticated as: {username}", "Awaiting code..."
- **Sign In** button — enqueues `start_navigraph_auth` command
- **Auth Code** read-only text field — populated when `navigraph_code` arrives
- **Sign Out** button — enqueues `sign_out_navigraph` command

When the auth code arrives, the form:
1. Populates the code field
2. Announces "Navigraph sign-in code: {code}. Opening browser."
3. Opens `https://navigraph.com/code` in the default browser via `Process.Start`

When auth succeeds (via `navigraph_auth_state` with `authenticated: true`):
1. Updates status label
2. Announces "Signed in to Navigraph as {username}"

### Tab 3: Preferences

Controls:
- **SimBrief Alias** — text box
- **Weather Source** — combo box (SIM / REAL-WORLD)
- **Weight Unit** — combo box (lb / kg)
- **Distance Unit** — combo box (nm / km)
- **Altitude Unit** — combo box (ft / m)
- **Temperature Unit** — combo box (C / F)
- **Save Preferences** button

On tab activation: enqueues `get_preferences` command. When `preferences` state arrives, populates all fields.

On save: enqueues `set_preference` for each changed field, then `save_preferences` to trigger the EFB's save logic (which validates Hoppie ID etc.).

### Announcements

All announcements use `Announce()` (queued) since they are background state changes:

| Event | Announcement |
|-------|-------------|
| SimBrief loaded | "SimBrief flight plan loaded: {origin} to {destination}" |
| SimBrief error | "SimBrief error: {message}" |
| Send to FMC | "Flight plan sent to FMC" |
| Navigraph code | "Navigraph sign-in code: {code}. Opening browser." |
| Navigraph signed in | "Signed in to Navigraph as {username}" |
| Navigraph signed out | "Signed out of Navigraph" |
| Preferences saved | (Not announced — the EFB's save button triggers an alert internally; we don't add a redundant announcement) |
| Bridge connected | "EFB bridge connected" |
| Bridge disconnected | "EFB bridge disconnected" |

---

## Component 4: Auto-Patcher (EFBPatcher)

**File:** `MSFSBlindAssist/Patching/EFBPatcher.cs`

### Class Design

```csharp
public static class EFBPatcher
{
    // Detection
    public static bool IsPatched(string pmdgPackagePath);
    public static string? FindPMDGPackagePath();  // Scans known MSFS Community paths

    // Patching
    public static PatchResult ApplyPatch(string pmdgPackagePath);
    public static PatchResult RemovePatch(string pmdgPackagePath);

    // Constants
    private const string HtmlFileName = "PMDGTabletCA.html";
    private const string BackupFileName = "PMDGTabletCA.html.bak";
    private const string BridgeJsFileName = "pmdg-efb-accessibility-bridge.js";
    private const string ScriptTag = "<script type=\"text/html\" import-script=\"/Pages/VCockpit/Instruments/PMDGTablet/pmdg-777-200ER/pmdg-efb-accessibility-bridge.js\"></script>";
    private const string HtmlRelativePath = "html_ui/Pages/VCockpit/Instruments/PMDGTablet/pmdg-777-200ER";
}

public enum PatchResult
{
    Success,
    AlreadyPatched,
    FileNotFound,
    BackupFailed,
    PatchFailed,
    Restored
}
```

### Patching Flow

1. **Find package path**: Search for `pmdg-aircraft-77er` in known MSFS Community folder locations. Store the found path for future use.
2. **Check if patched**: Read `PMDGTabletCA.html`, search for the bridge script tag.
3. **Create backup**: Copy `PMDGTabletCA.html` → `PMDGTabletCA.html.bak`. Only if `.bak` doesn't already exist (preserves original across re-patches).
4. **Copy bridge JS**: Extract `pmdg-efb-accessibility-bridge.js` from embedded resources (or app directory) into the PMDG tablet assets folder.
5. **Patch HTML**: Append the `<script>` tag to the end of `PMDGTabletCA.html`.
6. Return `PatchResult.Success`.

### Restoration Flow

1. Check that `.bak` exists
2. Copy `.bak` → `PMDGTabletCA.html` (overwrite)
3. Delete the bridge JS file from the PMDG folder
4. Return `PatchResult.Restored`

### Integration with Aircraft Loading

When the PMDG 777 profile is loaded in `MainForm`:

```
if (!EFBPatcher.IsPatched(packagePath))
{
    // Announce and show Yes/No dialog
    "PMDG EFB accessibility bridge is not installed. Would you like to install it?"
    if (userSaysYes)
    {
        var result = EFBPatcher.ApplyPatch(packagePath);
        // Announce result
        "EFB accessibility bridge installed. Restart the flight or reload the aircraft for changes to take effect."
    }
}
```

### PMDG Update Detection

On each 777 load: if the HTML file does NOT contain the script tag but a `.bak` file exists → PMDG updated and overwrote the patch. Offer to re-patch. The `.bak` is left as-is since it's the clean original.

---

## File Inventory

New files:
- `MSFSBlindAssist/SimConnect/EFBBridgeServer.cs` — HTTP server
- `MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.cs` — Accessible form
- `MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.Designer.cs` — Form designer
- `MSFSBlindAssist/Patching/EFBPatcher.cs` — Auto-patcher
- `MSFSBlindAssist/Resources/pmdg-efb-accessibility-bridge.js` — Bridge JS (embedded resource)

Modified files:
- `MSFSBlindAssist/Hotkeys/HotkeyManager.cs` — Add `HOTKEY_PMDG_EFB` constant and binding
- `MSFSBlindAssist/Hotkeys/HotkeyAction.cs` — Add `ShowPMDG777EFB` enum value
- `MSFSBlindAssist/MainForm.cs` — Add form lifecycle management, patcher integration on 777 load
- `MSFSBlindAssist/HotkeyGuides/PMDG_777_Hotkeys.txt` — Document new hotkey

## Testing Strategy

- **JS Bridge**: Manual testing in-sim — verify EventBus subscriptions fire, HTTP communication works, commands trigger correct EFB actions
- **HTTP Server**: Unit-testable in isolation — verify endpoint routing, CORS headers, command queue behavior, thread marshalling
- **Form**: Manual testing with screen reader — verify all announcements, tab navigation, button states
- **Patcher**: Unit-testable — verify detection, backup creation, script tag insertion, restoration
- **Integration**: End-to-end test with MSFS running — full SimBrief fetch → FMC uplink flow, Navigraph auth flow
