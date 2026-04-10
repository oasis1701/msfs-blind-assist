# EFB Bridge Robustness Improvements

**Date:** 2026-04-09
**Status:** Approved
**Scope:** Targeted fixes within existing HTTP polling architecture

## Problem

The EFB bridge (JS in MSFS Coherent GT <-> C# HttpListener <-> WinForms UI) is functionally complete but flaky. Root causes: silent state loss on HTTP failures, no connection feedback to user, no operation timeouts, race conditions in reconnection, unbounded queue growth, and risk of double-patching the mod package HTML.

## Changes

### 1. State Retry Queue (JS Bridge)

Add `_efb.pendingStates` array. When `postState()` fails (fetch throws or `!response.ok`), push `{type, data, retryCount}` onto the queue if the type is critical:

- **Critical types** (queued on failure): `simbrief_loaded`, `navigraph_code`, `navigraph_auth_state`, `preferences`, `simbrief_fetch_result`, `fmc_upload_started`, `connected`, `error`
- **Non-critical types** (dropped on failure): `heartbeat`, `page_changed`

Constraints:
- Max 20 pending entries; drop oldest when exceeded
- Max 3 retries per entry; drop with `console.warn` after exhausted
- Flush queue in order on reconnection (in `tryConnect` after transition to connected)

Also add `response.ok` check in `pollCommands()` before calling `response.json()`.

### 2. Connection Status on Form

Add a `connectionStatusText` read-only TextBox at top of the form, above the tab control (always visible regardless of active tab). Use a `System.Windows.Forms.Timer` (`_connectionCheckTimer`, 3-second interval) to poll `_bridgeServer.IsBridgeConnected`:

- Text: "Connected" or "Not connected - EFB tablet must be open in simulator"
- Announce via screen reader only on **transitions** (connected -> disconnected or vice versa), tracked via `_wasConnected` bool
- Disable action buttons when disconnected: `fetchSimbriefButton`, `sendToFmcButton`, `navigraphSignInButton`, `navigraphSignOutButton`, `savePreferencesButton`
- Re-enable buttons on reconnection (respecting per-button state, e.g. `sendToFmcButton` only if `_simbriefLoaded`)

Timer starts in constructor, stops in `OnFormClosing`.

### 3. Form-Side Timeouts

Two timeout timers:

**SimBrief fetch timeout** (`_fetchTimeoutTimer`, 30 seconds):
- Started when "Fetch SimBrief" clicked
- Cancelled when `simbrief_loaded` or `error` state arrives
- On fire: reset `simbriefStatusText` to "Fetch timed out - try again", re-enable `fetchSimbriefButton`, announce

**Navigraph auth timeout** (`_authTimeoutTimer`, 60 seconds):
- Started when "Sign In" clicked
- Cancelled when `navigraph_code` or `navigraph_auth_state` arrives
- On fire: reset `navigraphStatusText` to "Sign-in timed out", re-enable `navigraphSignInButton`, announce

Both are `System.Windows.Forms.Timer` (single-shot via `timer.Tick += handler; timer.Start()` pattern, stopped in handler).

### 4. Double-Patch Guard (Mod Package Manager)

In both `Install()` and `UpdateModPackage()`, before appending the bridge script tag:

```csharp
if (!originalHtml.Contains(BridgeJsFileName))
{
    modifiedHtml = originalHtml.TrimEnd() + GetBridgeScriptTag(variantSubfolder);
}
else
{
    modifiedHtml = originalHtml; // Already patched
}
```

This prevents duplicate `<script>` tags if the original HTML source was already patched (e.g., reading from a previous override rather than the pristine PMDG file).

### 5. Command Deduplication

**Form side (primary):** Disable the clicked button immediately on click. Re-enable on:
- Relevant state response arriving (e.g., `simbrief_loaded` re-enables `fetchSimbriefButton`)
- Timeout firing (improvement #3)
- Connection status change re-evaluation (improvement #2)

**Server side (safety net):** Add `HasPendingCommand(string commandName)` to `EFBBridgeServer`:
```csharp
public bool HasPendingCommand(string commandName)
{
    return _commandQueue.Any(item => item.Command.Command == commandName);
}
```
Form checks this before enqueueing. The queue is always small (<10 items), so linear scan is fine.

### 6. Robust Reconnection (JS Bridge)

**Connecting guard:** Add `_efb.connecting = false`. In `tryConnect()`, return immediately if `connecting` is true. Set `connecting = true` at entry, `false` at exit (in both success and failure paths).

**Reconnection state sync:** On transition from disconnected to connected (in `tryConnect`), flush pending states (improvement #1), then send current Navigraph state.

**Simplified Navigraph state:** Replace the 5 staggered `setTimeout` calls in `sendCurrentNavigraphState()` with a sequential retry:
- 3 attempts at delays 0, 3000, 10000ms
- Track success via `_efb.navigraphStateSent` flag (reset on disconnect)
- Post "not authenticated" only after final attempt fails
- Stop early if any attempt succeeds

### 7. Server Auto-Restart

**ListenLoop retry:** If the loop exits due to an unexpected exception (not `OperationCanceledException` or `HttpListenerException`), wait 2 seconds and retry, up to 5 times. Track via `_restartCount` that resets on each successful request.

```
ListenLoop:
  restartCount = 0
  while not cancelled:
    try:
      while not cancelled:
        context = await GetContextAsync()
        HandleRequest(context)
        restartCount = 0  // reset on success
    catch OperationCanceled: break
    catch HttpListenerException: break  // intentional shutdown
    catch Exception:
      if restartCount >= 5: break
      restartCount++
      log warning
      await Task.Delay(2000)
      recreate + start listener
```

**Start/Stop lock:** Add `private readonly object _startStopLock = new()` to prevent concurrent `Start()`/`Stop()` calls from racing on `_listener` and `_cts`.

**Error event:** Add `public event Action<string>? Error` that fires on listener failures so the form can announce server issues if needed.

### 8. Command Queue Cap

Add `private const int MaxQueueSize = 50` to `EFBBridgeServer`. In `EnqueueCommand()`:

```csharp
while (_commandQueue.Count >= MaxQueueSize)
{
    _commandQueue.TryDequeue(out _); // drop oldest
}
_commandQueue.Enqueue((...));
```

Log when items are dropped via `Debug.WriteLine`.

## Files Modified

| File | Changes |
|---|---|
| `MSFSBlindAssist/Resources/pmdg-efb-accessibility-bridge.js` | State retry queue, response.ok check, connecting guard, simplified Navigraph state retry |
| `MSFSBlindAssist/SimConnect/EFBBridgeServer.cs` | Queue cap, HasPendingCommand, auto-restart with retry, start/stop lock, Error event |
| `MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.cs` | Connection status polling, operation timeouts, button disable/enable |
| `MSFSBlindAssist/Forms/PMDG777/PMDG777EFBForm.Designer.cs` | connectionStatusText TextBox, layout adjustment |
| `MSFSBlindAssist/Patching/EFBModPackageManager.cs` | Double-patch guard |

## Testing

- Build succeeds with no errors
- Manual verification: open form without sim running, confirm "Not connected" status and disabled buttons
- Manual verification: connect to sim with EFB open, confirm "Connected" status and enabled buttons
- Manual verification: click Fetch SimBrief, confirm button disables, confirm timeout fires after 30s if no response
- Manual verification: install mod package twice, confirm no duplicate script tags in HTML
