# HorizonSim 787-9 FMC Bridge — FS2024 Debugging Log

The HS787 FMC bridge works in **MSFS 2020** but does not connect in **MSFS 2024**. This document records what has been tried, what was ruled out, and what to investigate next. Update it before opening a PR that touches the bridge.

## Confirmed Context

- FS2024 uses **Coherent GT 2.x** — confirmed by `class` syntax in `MFD789.GE.js`. This is a newer Chromium base than FS2020's Coherent GT 1.x and has stricter security policies.
- FS2020 bridge: **working** — HTTP fetch from VCockpit JS to `127.0.0.1:19778` succeeds.
- FS2024 bridge: **not working** — C# `IsBridgeConnected` never becomes true; server receives no HTTP traffic from the JS bridge.
- The **PMDG EFB bridge** (port 19777) **works in FS2024** — but it runs in a **popup/tablet context** (`PMDGTabletCA.html`), not a VCockpit instrument context. This is the key difference. The FS2024 VCockpit context appears more sandboxed than the popup context.
- FS2020 and FS2024 installed mod packages are byte-for-byte identical — the difference is the runtime environment, not the files.

## Fix Attempts

### Attempt 1 — Private Network Access headers (BridgeVersion 9 → 10)
**Hypothesis:** Chrome 94+ Private Network Access (PNA) policy blocks `fetch()` to localhost unless the server returns `Access-Control-Allow-Private-Network: true` on both OPTIONS preflight and normal responses.

**Applied:** Added PNA header to `EFBBridgeServer.HandleRequest` covering all response paths.

**Result:** Failed. Bridge still not connected in FS2024.

---

### Attempt 2 — Switch from `import-script` to `<script src>` (BridgeVersion 10 → 11)
**Hypothesis:** `import-script` is an MSFS framework attribute specific to Coherent GT 1.x and is silently ignored by FS2024's Coherent GT 2.x, so the bridge JS never loads.

**Applied:** Replaced `<script type="text/html" import-script="...">` with `<script src="hs787-mfd-bridge.js">` (relative path, processed by Coherent GT browser directly).

**Result:** Failed. Bridge still not connected in FS2024.

---

### Attempt 3 — IPv4 force + both script tags (BridgeVersion 11 → 12)
**Two hypotheses addressed:**
1. `localhost` resolves to IPv6 `::1` in FS2024 Coherent GT 2.x, but the C# `HttpListener` binds IPv4 only → connection refused at network level.
2. Neither `import-script` alone nor `<script src>` alone is sufficient — use both with a double-load guard so whichever fires first wins.

**Applied:**
- `SERVER_URL` changed from `http://localhost:19778` to `http://127.0.0.1:19778` in both bridge JS files.
- Both `import-script` and `<script src>` tags injected into patched HTML.
- Double-load guard (`window._mfd_bridge_loaded`) added so duplicate execution is a no-op.

**Result:** Failed. Bridge still not connected in FS2024.

---

## What This Rules Out

- PNA headers alone are not the fix.
- The `import-script` vs `<script src>` distinction is not the fix.
- IPv6/IPv4 resolution of `localhost` is not the fix.
- The mod package install mechanism is not the problem — FS2020 uses the same package and works.

## Current State — Diagnostic (BridgeVersion 13)

**Problem:** After 3 failed HTTP-based fixes we still don't know whether the bridge JS script executes at all in FS2024's VCockpit context, or whether it executes but the `fetch()` call is blocked.

**Diagnostic added:** `hs787-mfd-bridge.js` now writes `L:MSFSBA_787_STAGE` via `SimVar.SetSimVarValue` at three points. The C# app reads this L-var via SimConnect continuous monitoring (`HS787_BridgeStage` in `HorizonSim787Definition`) and displays the value in the FMC form status label when not connected.

| Stage | Meaning |
|-------|---------|
| 0 | L-var never written — script did not execute in this VCockpit context, OR `SimVar` API is unavailable here |
| 1 | Script executed — `SimVar.SetSimVarValue` works in this context |
| 2 | Fetch failed — `tryConnect` threw an exception (CSP, PNA, or network policy blocking `127.0.0.1`) |
| 3 | Connected — HTTP fetch succeeded (should match `IsBridgeConnected = true` in C#) |

The stage never downgrades from 3, so a momentary reconnect during observation won't erase the result.

## What to Try Next (based on diagnostic result)

### If Stage 0 — script is not executing
The injection mechanism itself is broken in FS2024 VCockpit. Neither `import-script` nor `<script src>` fires. Possible directions:
- Investigate whether FS2024 VCockpit HTML files support any script injection mechanism at all (check Coherent GT 2.x docs / other community mods that inject into VCockpit instruments in FS2024).
- Consider whether a WASM module could trigger JS execution rather than HTML injection.

### If Stage 1 or 2 — script executes but fetch is blocked
HTTP to localhost is blocked in FS2024 VCockpit context (CSP or network sandbox). The HTTP architecture needs to be replaced. Options:
- **WebSocket** — same security surface as `fetch`, likely blocked by the same policy. Worth a quick test first since the implementation change is small.
- **SimVar-based IPC** — JS writes L-vars via `SimVar.SetSimVarValue`; C# reads them via SimConnect. No HTTP, no network policy. Proven to work (the diagnostic itself uses this channel). High implementation cost: need to encode screen data into L-vars and design a command channel back to JS.
- **Named pipe or shared memory via WASM** — more complex, requires a MobiFlight-style WASM module on the JS side.

### If Stage 3 — connected (unexpected)
The bridge should be showing as connected in C# too. Check for a race or timing issue between the SimVar write and the C# heartbeat check.
