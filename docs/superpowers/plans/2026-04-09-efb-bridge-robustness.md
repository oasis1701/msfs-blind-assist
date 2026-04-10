# EFB Bridge Robustness Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the PMDG 777 EFB bridge reliable by adding state retry, connection feedback, timeouts, deduplication, reconnection guards, server auto-restart, queue caps, and double-patch protection.

**Architecture:** Targeted fixes within the existing HTTP polling architecture (JS bridge <-> C# HttpListener <-> WinForms). Each improvement is independent and additive — no structural changes to communication model.

**Tech Stack:** JavaScript (Coherent GT compatible — no modern APIs), C# .NET 9, Windows Forms, System.Windows.Forms.Timer

**Spec:** `docs/superpowers/specs/2026-04-09-efb-bridge-robustness-design.md`

---

### Task 1: Command Queue Cap and HasPendingCommand (EFBBridgeServer)

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/EFBBridgeServer.cs`

- [ ] **Step 1: Add MaxQueueSize constant and cap logic in EnqueueCommand**

Add constant and modify `EnqueueCommand()`:

```csharp
private const int MaxQueueSize = 50;
```

Replace the existing `EnqueueCommand` method (line 79-82):

```csharp
public void EnqueueCommand(string command, Dictionary<string, string>? payload = null)
{
    while (_commandQueue.Count >= MaxQueueSize)
    {
        if (_commandQueue.TryDequeue(out var dropped))
        {
            System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Command queue full, dropped: {dropped.Command.Command}");
        }
    }
    _commandQueue.Enqueue((new EFBCommand { Command = command, Payload = payload }, DateTime.UtcNow));
}
```

- [ ] **Step 2: Add HasPendingCommand method**

Add after `EnqueueCommand`:

```csharp
public bool HasPendingCommand(string commandName)
{
    return _commandQueue.Any(item => item.Command.Command == commandName);
}
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/SimConnect/EFBBridgeServer.cs
git commit -m "feat(efb): add command queue cap and HasPendingCommand to EFBBridgeServer"
```

---

### Task 2: Server Auto-Restart and Start/Stop Lock (EFBBridgeServer)

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/EFBBridgeServer.cs`

- [ ] **Step 1: Add lock object, restart count, and Error event**

Add fields after the existing `_disposed` field (line 33):

```csharp
private readonly object _startStopLock = new();
private int _restartCount;
private const int MaxRestartAttempts = 5;
private const int RestartDelayMs = 2000;

public event Action<string>? Error;
```

- [ ] **Step 2: Add locking to Start() and Stop()**

Replace the `Start()` method (lines 45-65):

```csharp
public void Start()
{
    lock (_startStopLock)
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add(Prefix);

        try
        {
            _listener.Start();
            _restartCount = 0;
            Task.Run(() => ListenLoop(_cts.Token));
        }
        catch (HttpListenerException ex)
        {
            System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Failed to start on {Prefix}: {ex.Message}");
            Error?.Invoke($"EFB server failed to start: {ex.Message}");
            _cts.Dispose();
            _cts = null;
            _listener = null;
        }
    }
}
```

Replace the `Stop()` method (lines 67-77):

```csharp
public void Stop()
{
    lock (_startStopLock)
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
}
```

- [ ] **Step 3: Add auto-restart logic to ListenLoop**

Replace the `ListenLoop` method (lines 84-106):

```csharp
private async Task ListenLoop(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        try
        {
            while (!ct.IsCancellationRequested && _listener?.IsListening == true)
            {
                var context = await _listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleRequest(context), ct);
                _restartCount = 0; // Reset on successful accept
            }
            break; // Normal exit (listener stopped)
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (HttpListenerException)
        {
            break; // Intentional shutdown
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Listen error: {ex.Message}");
            if (_restartCount >= MaxRestartAttempts)
            {
                System.Diagnostics.Debug.WriteLine("EFBBridgeServer: Max restart attempts reached, stopping.");
                Error?.Invoke("EFB server stopped after repeated failures");
                break;
            }
            _restartCount++;
            System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Restarting listener (attempt {_restartCount}/{MaxRestartAttempts})...");
            try
            {
                await Task.Delay(RestartDelayMs, ct);
                lock (_startStopLock)
                {
                    if (ct.IsCancellationRequested) break;
                    _listener?.Close();
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(Prefix);
                    _listener.Start();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception restartEx)
            {
                System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Restart failed: {restartEx.Message}");
                Error?.Invoke("EFB server restart failed");
                break;
            }
        }
    }
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/SimConnect/EFBBridgeServer.cs
git commit -m "feat(efb): add server auto-restart, start/stop lock, and Error event"
```

---

### Task 3: State Retry Queue and Robust Reconnection (JS Bridge)

**Files:**
- Modify: `MSFSBlindAssist/Resources/pmdg-efb-accessibility-bridge.js`

- [ ] **Step 1: Add state retry queue fields and critical type list**

Add new fields to the `_efb` object (after line 22, before the closing `};`):

```javascript
pendingStates: [],
MAX_PENDING_STATES: 20,
MAX_STATE_RETRIES: 3,
CRITICAL_STATE_TYPES: ['simbrief_loaded', 'navigraph_code', 'navigraph_auth_state', 'preferences', 'simbrief_fetch_result', 'fmc_upload_started', 'connected', 'error'],
connecting: false,
navigraphStateSent: false
```

- [ ] **Step 2: Replace postState with retry-aware version**

Replace the `postState` function (lines 27-38):

```javascript
_efb.postState = async function(type, data) {
    if (!_efb.serverConnected) {
        _efb.queueState(type, data);
        return;
    }
    try {
        var response = await fetch(_efb.SERVER_URL + '/state', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type: type, data: data || {} })
        });
        if (!response.ok) {
            _efb.queueState(type, data);
        }
    } catch (e) {
        _efb.queueState(type, data);
    }
};

_efb.queueState = function(type, data) {
    if (_efb.CRITICAL_STATE_TYPES.indexOf(type) === -1) return; // Drop non-critical
    if (_efb.pendingStates.length >= _efb.MAX_PENDING_STATES) {
        _efb.pendingStates.shift(); // Drop oldest
        console.warn('[EFB Bridge] Pending state queue full, dropped oldest entry');
    }
    _efb.pendingStates.push({ type: type, data: data || {}, retryCount: 0 });
};

_efb.flushPendingStates = async function() {
    var toRetry = _efb.pendingStates.slice();
    _efb.pendingStates = [];
    for (var i = 0; i < toRetry.length; i++) {
        var entry = toRetry[i];
        try {
            var response = await fetch(_efb.SERVER_URL + '/state', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ type: entry.type, data: entry.data })
            });
            if (!response.ok) throw new Error('not ok');
        } catch (e) {
            entry.retryCount++;
            if (entry.retryCount < _efb.MAX_STATE_RETRIES) {
                _efb.pendingStates.push(entry);
            } else {
                console.warn('[EFB Bridge] Dropping state after max retries:', entry.type);
            }
        }
    }
};
```

- [ ] **Step 3: Add response.ok check to pollCommands**

Replace the `pollCommands` function (lines 40-51):

```javascript
_efb.pollCommands = async function() {
    if (!_efb.serverConnected) return;
    try {
        var response = await fetch(_efb.SERVER_URL + '/commands');
        if (!response.ok) return;
        var commands = await response.json();
        for (var i = 0; i < commands.length; i++) {
            _efb.handleCommand(commands[i].command, commands[i].payload);
        }
    } catch (e) {
        // Server unreachable — will be detected by heartbeat
    }
};
```

- [ ] **Step 4: Add connecting guard and reconnection flush to tryConnect**

Replace the `tryConnect` function (lines 68-92):

```javascript
_efb.tryConnect = async function() {
    if (_efb.connecting) return _efb.serverConnected;
    _efb.connecting = true;
    try {
        var response = await _efb.fetchWithTimeout(_efb.SERVER_URL + '/ping', {}, 2000);
        if (response.ok) {
            if (!_efb.serverConnected) {
                _efb.serverConnected = true;
                _efb.navigraphStateSent = false;
                console.log('[EFB Bridge] Connected to accessibility server');
                _efb.startPolling();
                _efb.postState('connected');
                // Flush any states queued while disconnected
                await _efb.flushPendingStates();
                // Send current state now that connection is established
                _efb.sendCurrentNavigraphState();
            }
            return true;
        }
    } catch (e) {
        // Not available yet
    }

    if (_efb.serverConnected) {
        _efb.serverConnected = false;
        _efb.navigraphStateSent = false;
        console.log('[EFB Bridge] Lost connection to accessibility server');
        _efb.stopPolling();
    }
    return false;
};

// Wrap tryConnect exit to always clear connecting flag
var _origTryConnect = _efb.tryConnect;
_efb.tryConnect = async function() {
    try {
        return await _origTryConnect();
    } finally {
        _efb.connecting = false;
    }
};
```

- [ ] **Step 5: Simplify sendCurrentNavigraphState with sequential retry**

Replace the `sendCurrentNavigraphState` function (lines 226-255):

```javascript
_efb.sendCurrentNavigraphState = function() {
    if (_efb.navigraphStateSent) return;
    var delays = [0, 3000, 10000];
    var attemptIndex = 0;

    function attempt() {
        if (_efb.navigraphStateSent || !_efb.serverConnected) return;
        if (typeof Navigraph !== 'undefined' && Navigraph.auth && Navigraph.auth.getUser) {
            Navigraph.auth.getUser(true).then(function(user) {
                if (_efb.navigraphStateSent) return;
                if (user) {
                    _efb.navigraphStateSent = true;
                    _efb.postState('navigraph_auth_state', {
                        authenticated: 'true',
                        username: user.preferred_username || user.name || 'Unknown'
                    });
                } else if (attemptIndex >= delays.length - 1) {
                    _efb.navigraphStateSent = true;
                    _efb.postState('navigraph_auth_state', {
                        authenticated: 'false',
                        username: ''
                    });
                } else {
                    attemptIndex++;
                    setTimeout(attempt, delays[attemptIndex]);
                }
            }).catch(function(e) {
                console.error('[EFB Bridge] Error getting Navigraph user:', e);
                if (attemptIndex < delays.length - 1) {
                    attemptIndex++;
                    setTimeout(attempt, delays[attemptIndex]);
                }
            });
        } else if (attemptIndex < delays.length - 1) {
            attemptIndex++;
            setTimeout(attempt, delays[attemptIndex]);
        }
    }

    attempt();
};
```

- [ ] **Step 6: Build to verify JS is valid (check it's copied to output)**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds and JS file is copied to output

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Resources/pmdg-efb-accessibility-bridge.js
git commit -m "feat(efb): add state retry queue, response.ok checks, and robust reconnection to JS bridge"
```

---

### Task 4: Connection Status, Timeouts, and Button Management (Form)

**Files:**
- Modify: `MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.Designer.cs`
- Modify: `MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.cs`

- [ ] **Step 1: Add connectionStatusText to Designer**

In `PMDG777EFBForm.Designer.cs`, add a new field declaration after `savePreferencesButton` (line 52):

```csharp
private TextBox? connectionStatusText;
```

In `InitializeComponent()`, replace line 199 (`this.Controls.Add(tabControl);`) with the following. Windows Forms docks in reverse add-order, so `tabControl` (Fill) is added first, then `connectionStatusText` (Top) is added second so it appears above the tabs:

```csharp
this.Controls.Add(tabControl);

connectionStatusText = new TextBox
{
    Text = "Not connected",
    Dock = DockStyle.Top,
    ReadOnly = true,
    BorderStyle = BorderStyle.None,
    BackColor = System.Drawing.SystemColors.Control,
    Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold),
    AccessibleName = "Connection Status",
    TabIndex = 0,
    Height = 25
};
this.Controls.Add(connectionStatusText);
```

- [ ] **Step 2: Add timer fields and connection/timeout logic to Form.cs**

In `PMDG777EFBForm.cs`, add new fields after `_simbriefLoaded` (line 19):

```csharp
private bool _wasConnected = false;
private System.Windows.Forms.Timer? _connectionCheckTimer;
private System.Windows.Forms.Timer? _fetchTimeoutTimer;
private System.Windows.Forms.Timer? _authTimeoutTimer;
```

- [ ] **Step 3: Add connection check timer setup in constructor**

In the constructor, after `SetupEventHandlers();` (line 27), add:

```csharp
// Poll connection status every 3 seconds
_connectionCheckTimer = new System.Windows.Forms.Timer { Interval = 3000 };
_connectionCheckTimer.Tick += OnConnectionCheck;
_connectionCheckTimer.Start();

// Run initial check immediately
OnConnectionCheck(this, EventArgs.Empty);
```

- [ ] **Step 4: Add the OnConnectionCheck method**

Add after `SetupEventHandlers()` method:

```csharp
private void OnConnectionCheck(object? sender, EventArgs e)
{
    if (IsDisposed || !IsHandleCreated) return;

    bool connected = _bridgeServer.IsBridgeConnected;

    connectionStatusText!.Text = connected
        ? "Connected"
        : "Not connected \u2014 EFB tablet must be open in simulator";

    // Announce transitions only
    if (connected && !_wasConnected)
    {
        _announcer.Announce("EFB bridge connected");
        UpdateButtonStates(true);
    }
    else if (!connected && _wasConnected)
    {
        _announcer.Announce("EFB bridge disconnected");
        UpdateButtonStates(false);
    }

    _wasConnected = connected;
}

private void UpdateButtonStates(bool connected)
{
    fetchSimbriefButton!.Enabled = connected;
    sendToFmcButton!.Enabled = connected && _simbriefLoaded;
    navigraphSignInButton!.Enabled = connected;
    navigraphSignOutButton!.Enabled = connected;
    savePreferencesButton!.Enabled = connected;
}
```

- [ ] **Step 5: Add timeout helper methods**

Add after `UpdateButtonStates`:

```csharp
private void StartFetchTimeout()
{
    StopFetchTimeout();
    _fetchTimeoutTimer = new System.Windows.Forms.Timer { Interval = 30000 };
    _fetchTimeoutTimer.Tick += (_, _) =>
    {
        StopFetchTimeout();
        simbriefStatusText!.Text = "Fetch timed out \u2014 try again";
        fetchSimbriefButton!.Enabled = _bridgeServer.IsBridgeConnected;
        _announcer.Announce("SimBrief fetch timed out");
    };
    _fetchTimeoutTimer.Start();
}

private void StopFetchTimeout()
{
    if (_fetchTimeoutTimer != null)
    {
        _fetchTimeoutTimer.Stop();
        _fetchTimeoutTimer.Dispose();
        _fetchTimeoutTimer = null;
    }
}

private void StartAuthTimeout()
{
    StopAuthTimeout();
    _authTimeoutTimer = new System.Windows.Forms.Timer { Interval = 60000 };
    _authTimeoutTimer.Tick += (_, _) =>
    {
        StopAuthTimeout();
        navigraphStatusText!.Text = "Sign-in timed out";
        navigraphSignInButton!.Enabled = _bridgeServer.IsBridgeConnected;
        _announcer.Announce("Navigraph sign-in timed out");
    };
    _authTimeoutTimer.Start();
}

private void StopAuthTimeout()
{
    if (_authTimeoutTimer != null)
    {
        _authTimeoutTimer.Stop();
        _authTimeoutTimer.Dispose();
        _authTimeoutTimer = null;
    }
}
```

- [ ] **Step 6: Update button click handlers to disable buttons and start timeouts**

Replace the `SetupEventHandlers` method body (lines 42-78) with:

```csharp
private void SetupEventHandlers()
{
    _bridgeServer.StateUpdated += OnStateUpdated;

    fetchSimbriefButton!.Click += (_, _) =>
    {
        if (_bridgeServer.HasPendingCommand("fetch_simbrief")) return;
        fetchSimbriefButton.Enabled = false;
        simbriefStatusText!.Text = "Fetching...";
        _bridgeServer.EnqueueCommand("fetch_simbrief");
        StartFetchTimeout();
    };

    sendToFmcButton!.Click += (_, _) =>
    {
        if (_bridgeServer.HasPendingCommand("send_to_fmc")) return;
        sendToFmcButton.Enabled = false;
        _bridgeServer.EnqueueCommand("send_to_fmc");
    };

    navigraphSignInButton!.Click += (_, _) =>
    {
        if (_bridgeServer.HasPendingCommand("start_navigraph_auth")) return;
        navigraphSignInButton.Enabled = false;
        navigraphStatusText!.Text = "Awaiting code...";
        authCodeTextBox!.Text = "";
        _bridgeServer.EnqueueCommand("start_navigraph_auth");
        StartAuthTimeout();
    };

    navigraphSignOutButton!.Click += (_, _) =>
    {
        if (_bridgeServer.HasPendingCommand("sign_out_navigraph")) return;
        navigraphSignOutButton.Enabled = false;
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
```

- [ ] **Step 7: Update OnStateUpdated to cancel timeouts and re-enable buttons**

Replace the `OnStateUpdated` method with:

```csharp
private void OnStateUpdated(object? sender, EFBStateUpdateEventArgs e)
{
    if (IsDisposed || !IsHandleCreated) return;

    switch (e.Type)
    {
        case "connected":
            // Connection announcement handled by OnConnectionCheck
            break;

        case "simbrief_loaded":
            StopFetchTimeout();
            _simbriefLoaded = true;
            UpdateFlightDetails(e.Data);
            simbriefStatusText!.Text = "Loaded";
            fetchSimbriefButton!.Enabled = _bridgeServer.IsBridgeConnected;
            sendToFmcButton!.Enabled = _bridgeServer.IsBridgeConnected;
            string origin = e.Data.GetValueOrDefault("origin_icao", "");
            string dest = e.Data.GetValueOrDefault("dest_icao", "");
            _announcer.Announce($"SimBrief flight plan loaded: {origin} to {dest}");
            break;

        case "simbrief_fetch_result":
            bool success = bool.TryParse(e.Data.GetValueOrDefault("success", "false"), out var s) && s;
            string message = e.Data.GetValueOrDefault("message", "");
            sendToFmcButton!.Enabled = _bridgeServer.IsBridgeConnected && _simbriefLoaded;
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
            StopAuthTimeout();
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
            StopAuthTimeout();
            bool authenticated = e.Data.GetValueOrDefault("authenticated", "false") == "true";
            string username = e.Data.GetValueOrDefault("username", "");
            if (authenticated)
            {
                navigraphStatusText!.Text = $"Authenticated as: {username}";
                navigraphSignInButton!.Enabled = false;
                navigraphSignOutButton!.Enabled = _bridgeServer.IsBridgeConnected;
                _announcer.Announce($"Signed in to Navigraph as {username}");
            }
            else
            {
                navigraphStatusText!.Text = "Not authenticated";
                navigraphSignInButton!.Enabled = _bridgeServer.IsBridgeConnected;
                navigraphSignOutButton!.Enabled = false;
                authCodeTextBox!.Text = "";
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
            StopFetchTimeout();
            string errorMsg = e.Data.GetValueOrDefault("message", "Unknown error");
            simbriefStatusText!.Text = $"Error: {errorMsg}";
            fetchSimbriefButton!.Enabled = _bridgeServer.IsBridgeConnected;
            _announcer.Announce($"EFB error: {errorMsg}");
            break;
    }
}
```

- [ ] **Step 8: Update OnSavePreferences to check connection and dedup**

Replace the `OnSavePreferences` method with:

```csharp
private void OnSavePreferences(object? sender, EventArgs e)
{
    if (!_bridgeServer.IsBridgeConnected)
    {
        _announcer.Announce("EFB bridge not connected. Preferences cannot be saved while the EFB tablet is not active in the simulator.");
        return;
    }

    if (_bridgeServer.HasPendingCommand("save_preferences")) return;

    savePreferencesButton!.Enabled = false;

    _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
        { { "key", "simbrief_id" }, { "value", simbriefAliasTextBox!.Text ?? "" } });

    EnqueueComboPreference(weatherSourceCombo!, "weather_source");
    EnqueueComboPreference(weightUnitCombo!, "weight_unit");
    EnqueueComboPreference(distanceUnitCombo!, "distance_unit");
    EnqueueComboPreference(altitudeUnitCombo!, "altitude_unit");
    EnqueueComboPreference(temperatureUnitCombo!, "temperature_unit");

    _bridgeServer.EnqueueCommand("save_preferences");
    _announcer.Announce("Preferences saved");

    // Re-enable after a short delay (preferences are fire-and-forget)
    var reenableTimer = new System.Windows.Forms.Timer { Interval = 2000 };
    reenableTimer.Tick += (_, _) =>
    {
        reenableTimer.Stop();
        reenableTimer.Dispose();
        if (!IsDisposed && _bridgeServer.IsBridgeConnected)
            savePreferencesButton!.Enabled = true;
    };
    reenableTimer.Start();
}
```

- [ ] **Step 9: Update OnFormClosing to clean up timers**

Replace the `OnFormClosing` method with:

```csharp
protected override void OnFormClosing(FormClosingEventArgs e)
{
    _connectionCheckTimer?.Stop();
    _connectionCheckTimer?.Dispose();
    StopFetchTimeout();
    StopAuthTimeout();
    _bridgeServer.StateUpdated -= OnStateUpdated;
    if (_previousWindow != IntPtr.Zero)
    {
        SetForegroundWindow(_previousWindow);
    }
    base.OnFormClosing(e);
}
```

- [ ] **Step 10: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds

- [ ] **Step 11: Commit**

```bash
git add MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.cs MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.Designer.cs
git commit -m "feat(efb): add connection status, operation timeouts, and button deduplication to EFB form"
```

---

### Task 5: Double-Patch Guard (Mod Package Manager)

**Files:**
- Modify: `MSFSBlindAssist/Patching/EFBModPackageManager.cs`

- [ ] **Step 1: Add double-patch guard to Install method**

In the `Install` method, replace line 294:

```csharp
string modifiedHtml = originalHtml.TrimEnd() + GetBridgeScriptTag(variantSubfolder);
```

With:

```csharp
string modifiedHtml = originalHtml.Contains(BridgeJsFileName)
    ? originalHtml  // Already patched — don't double-patch
    : originalHtml.TrimEnd() + GetBridgeScriptTag(variantSubfolder);
```

- [ ] **Step 2: Add double-patch guard to UpdateModPackage method**

In the `UpdateModPackage` method, replace line 363:

```csharp
string modifiedHtml = originalHtml.TrimEnd() + GetBridgeScriptTag(variantSubfolder);
```

With:

```csharp
string modifiedHtml = originalHtml.Contains(BridgeJsFileName)
    ? originalHtml  // Already patched — don't double-patch
    : originalHtml.TrimEnd() + GetBridgeScriptTag(variantSubfolder);
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Patching/EFBModPackageManager.cs
git commit -m "fix(efb): guard against double-patching EFB HTML in mod package manager"
```

---

### Task 6: Final Build Verification

**Files:** None (verification only)

- [ ] **Step 1: Clean build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with 0 errors

- [ ] **Step 2: Release build**

Run: `dotnet build MSFSBlindAssist.sln -c Release`
Expected: Build succeeds with 0 errors

- [ ] **Step 3: Verify JS bridge file in output**

Check that the modified bridge JS is present in the build output:
```bash
ls MSFSBlindAssist/bin/x64/Debug/net9.0-windows/Resources/pmdg-efb-accessibility-bridge.js
```
Expected: File exists

- [ ] **Step 4: Commit spec and plan**

```bash
git add docs/superpowers/specs/2026-04-09-efb-bridge-robustness-design.md docs/superpowers/plans/2026-04-09-efb-bridge-robustness.md
git commit -m "docs: add EFB bridge robustness improvement spec and plan"
```
