# FBW A32NX Flypad (EFB) Accessibility — Design Spec

**Date:** 2026-05-30  
**Status:** Approved

---

## Overview

Implement an accessible version of the FlyByWire A320neo flypad (Electronic Flight Bag) for blind pilots using NVDA or JAWS. The form opens with Shift+T in input mode (same hotkey as PMDG/HS787 EFBs) and mirrors the flypad's tab structure exactly: Dashboard, Dispatch, Ground, Performance, Navigation, ATC, Failures, Checklists, Presets, Settings.

**Key constraint:** Zero community folder modifications. The bridge JavaScript is injected at runtime into the running FBW EFB instrument via the Coherent GT DevTools Protocol (port 19999), which is available when MSFS developer mode is enabled.

---

## Architecture

Four new components, all fitting the existing codebase patterns:

### 1. `CoherentGTInjector` (`SimConnect/CoherentGTInjector.cs`)

A service that injects JavaScript into a running Coherent GT instrument page at runtime.

**Responsibilities:**
- Poll `http://127.0.0.1:19999/pagelist.json` every 3 seconds to discover instrument pages
- Identify the FBW EFB page by title ending with `"- EFB"` (e.g. `"VCockpit68 - EFB"`)
- Connect via raw TCP WebSocket to `ws://127.0.0.1:19999/devtools/page/{id}`
- Send a CDP `Runtime.evaluate` command containing the full bridge JS
- Detect page reload (page ID changes or connection drops) and re-inject automatically
- Expose `IsConnected` property; fire `Connected` / `Disconnected` events

**Raw WebSocket requirement:** .NET's `ClientWebSocket` rejects Coherent GT's non-standard `Connection: Upgrade,Keep-Alive` header. The injector uses a raw `TcpClient` + `NetworkStream` to perform the WebSocket handshake manually, bypassing .NET's validator. Only outbound `Runtime.evaluate` messages are needed — no persistent subscription required after injection.

**Lifecycle:** Instantiated in `MainForm`, started when the FBW aircraft loads (`FlyByWireA320Definition`), stopped when the aircraft changes or the app closes.

---

### 2. `a32nx-efb-accessibility-bridge.js` (`MSFSBlindAssist/Resources/`)

The existing draft bridge promoted from `bin/Debug/Resources` to the source `Resources/` directory and added to the `.csproj` as a build resource (same pattern as `pmdg-efb-accessibility-bridge.js` and `hs787-mfd-bridge.js`).

**What the bridge does (runs inside FBW's EFB instrument context):**

- Connects to the C# HTTP server on `http://localhost:19777` (port shared with PMDG — no conflict, only one aircraft active at a time)
- Polls `GET /commands` every 500 ms for commands from C#
- Posts state updates to `POST /state` with `{type, data}` JSON
- Reads/writes FBW L-vars via `SimVar.GetSimVarValue` / `SetSimVarValue` for fuel, payload, ground state
- Fires standard MSFS K-events for ground services (TOGGLE_JETWAY, REQUEST_FUEL_KEY, etc.)
- Accesses FBW's `Navigraph.auth` global for sign-in/out and auth state
- Reads FBW's `NXDataStore` for all settings values
- Includes reconnect logic, pending-state queue for critical messages, double-load guard

**Commands handled:**
- Dashboard: `get_simbrief_state`, `fetch_simbrief`, `send_to_mcdu`
- Ground: `get_ground_state`, `toggle_ground_service`
- Fuel: `get_fuel_state`, `set_fuel_target`, `set_fuel_mode`, `start_fuel_loading`, `stop_fuel_loading`
- Payload: `get_payload_state`, `set_passenger_count`, `set_cargo_weight`, `set_boarding_rate`, `start_boarding`, `stop_boarding`
- Navigraph: `start_navigraph_auth`, `sign_out_navigraph`, `check_navigraph_auth`, `get_navdata_status`
- Settings: `get_settings`, `set_setting`, `save_settings` — covers all NXDataStore-backed settings
- Diagnostics: `ping`, `get_page_text`, `run_diagnostics`

**Note:** No DOM selectors are required for fuel/payload/ground/Navigraph — all use SimVar or FBW JS globals. DOM interaction is only used for triggering the SimBrief import button and reading page text for Dispatch.

---

### 3. `A32NXEFBForm` (`MSFSBlindAssist/Forms/A32NX/A32NXEFBForm.cs`)

A WinForms form, screen-reader optimised, matching the flypad structure. Uses the existing `EFBBridgeServer` on port 19777 (same instance used by PMDG and HS787).

**Form shell:**
- `TabControl` with ten top-level tabs (same order as flypad toolbar)
- Connection status label (always visible, announced on change)
- `ShowForm()` method matching the PMDG/HS787 pattern (saves/restores foreground window, focuses first meaningful control)
- `System.Windows.Forms.Timer` at 3 s for connection health check
- Wired to `_bridgeServer.StateUpdated` for all incoming state

---

### 4. Hotkey wiring (`MainForm.cs`)

One additional branch in the `ShowPMDGEFB` case:

```csharp
case HotkeyAction.ShowPMDGEFB:
    if (currentAircraft is IPMDGAircraft pmdg && pmdg.HasEFBSupport)
        ShowPMDGEFBDialog();
    else if (currentAircraft?.AircraftCode == "HS_787")
        ShowHS787EFBFormDialog();
    else if (currentAircraft?.AircraftCode == "A320")   // NEW
        ShowA32NXEFBDialog();
    break;
```

`MainForm` gets a new `_a32nxEFBForm` field and `ShowA32NXEFBDialog()` / `CleanupA32NXEFBForm()` methods mirroring the HS787 pattern.

---

## Tab Details

### Dashboard

**State:** `simbrief_loaded` — announces or displays: callsign, origin ICAO, destination ICAO, alternate ICAO, cruise altitude, cost index, ZFW, planned fuel (kg), route distance (nm), average wind (dir/spd), estimated enroute time, planned/estimated departure and arrival times (UTC).

**Controls:**
- Labels for each field (read-only, accessible by Tab)
- Button: *Import from SimBrief* — sends `fetch_simbrief`, triggers FBW's own EFB import so MCDU/FMS receives the flight plan
- Button: *Send to MCDU* — sends `send_to_mcdu`
- Button: *Refresh* — sends `get_simbrief_state`

**Screen reader behaviour:** Focusing any label announces the field name and value. Import/Send announce confirmation or error from bridge response.

---

### Dispatch

**State:** OFP text fetched directly from the SimBrief API by C# using the stored pilot ID (same pattern as PMDGEFBForm's SimBrief integration).

**Controls:**
- Multi-line read-only `TextBox` containing the OFP text
- Button: *Fetch OFP* — calls SimBrief API, populates TextBox

**Note:** This tab does not use the bridge — C# fetches the OFP directly.

---

### Ground — subtab: Services

**State:** `ground_state` — each service reports `"connected"` or `"available"`.

**Controls:** One toggle `Button` per service, labelled with current state:
- Jetway
- Stairs Forward
- Stairs Aft
- GPU (External Power)
- Fuel Truck
- Catering
- Baggage

**Interaction:** Pressing a button sends `toggle_ground_service` with the service ID, then re-queries `get_ground_state` after 2 s to confirm new state.

---

### Ground — subtab: Fuel

**State:** `fuel_state` — current and target kg for each tank, total, mode, loading status.

**Controls:**
- Read-only labels: current kg for Centre, Left Main, Left Aux, Right Main, Right Aux, Total
- Editable numeric inputs: target kg for each tank (updates `L:A32NX_FUEL_*_DESIRED` via `set_fuel_target`)
- Radio group: fueling mode — Real / Fast / Instant (sends `set_fuel_mode`)
- Button: *Start Fueling* / *Stop Fueling* (sends `start_fuel_loading` or `stop_fuel_loading`)
- Button: *Refresh* (sends `get_fuel_state`)
- Status label: "Idle" / "Loading"

**Unit:** All values in kg. Bridge converts gallons ↔ kg using `FUEL WEIGHT PER GALLON`.

---

### Ground — subtab: Payload

**State:** `payload_state` — pax count, boarding status, boarding rate, cargo zone weights (kg).

**Controls:**
- Numeric input: total passenger count (0–174); sends `set_passenger_count`, distributes front-to-back across PAX stations using bit-flag encoding
- Radio group: boarding rate — Real / Fast / Instant (sends `set_boarding_rate`)
- Numeric inputs for four cargo zones: Fwd Baggage Container, Aft Container, Aft Baggage, Aft Bulk Loose (kg each); send `set_cargo_weight`
- Button: *Start Boarding* / *Stop Boarding*
- Button: *Refresh*
- Status label: "Not Started" / "Boarding"

---

### Ground — subtab: Pushback

**Controls:**
- Button: *Start Pushback* (sends `toggle_ground_service` with `service_id: "pushback"`)
- Button: *Stop Pushback*
- Status label: "Attached" / "Not Attached"

Map-based steering is visual-only and not included.

---

### Performance

**Phase 1:** Stub tab — displays "Performance calculator coming soon." The tab exists to match the flypad structure; the DOM element IDs for FBW's React-based calculator need verification against a live sim before the inputs and results can be wired.

**Phase 2 (future):**
- Input fields: airport ICAO, runway, OAT (°C), QNH (hPa), wind (dir/kts), runway condition, flex temp
- Button: *Calculate Takeoff* / *Calculate Landing*
- Read-only results: V1, VR, V2 / Vapp, Vref, landing distance

---

### Navigation

Exposes ATIS retrieval (the only non-visual element on this page).

**Controls:**
- Text input: airport ICAO
- Button: *Fetch ATIS*
- Read-only multi-line TextBox: ATIS text

**Implementation:** C# fetches ATIS directly from a public ATIS source (e.g. VATSIM/IVAO API) using the entered ICAO. Does not depend on the bridge. Charts are images and are not included.

---

### ATC

**Phase 1:** Stub tab — displays "ATC / CPDLC coming soon." FBW's CPDLC message store is accessible via the bridge but requires DOM selector research against a live sim session to map message list elements and response buttons.

**Phase 2 (future):** Incoming message list, read-only message text, preset response buttons.

---

### Failures

Read-only list of active failures.

**Controls:**
- List box of active failure descriptions (empty = "No active failures")
- Button: *Refresh*

**Implementation:** Bridge reads active failure labels from the Failures page DOM via `get_page_text`.

---

### Checklists

**Controls:**
- ComboBox: select checklist by name
- List of checklist items — each item shows name and checked state
- Button: *Check Item* (marks selected item complete)
- Button: *Reset Checklist*
- Button: *Refresh*

**Implementation:** Bridge navigates to the Checklists page, reads item states via DOM, clicks check buttons via `click_by_id`.

---

### Presets

**Controls:**
- List box of available presets by name
- Button: *Apply Selected Preset*
- Button: *Refresh List*
- Status label: last applied preset name

**Implementation:** Bridge navigates to Presets page, reads preset names, clicks apply via DOM.

---

### Settings — Aircraft Options / Pin Programs

Settings backed by `NXDataStore`. Exposes the aircraft-specific options (ISIS baro, autoland warning, etc.) and pin program slots. Each option is a labelled combo box or toggle. Refresh reads current values on tab focus; Save writes all pending changes.

---

### Settings — Sim Options

**Settings exposed:**
- Default barometer unit (Auto / inHg / hPa) — combo box
- Sync MSFS flight plan (off / load only / save) — combo box
- Auto-load MSFS route — toggle
- SimBridge enabled (Auto / Off) — combo box
- SimBridge machine (Local / Remote) — combo box + address text input when Remote
- SimBridge port — numeric input
- Dynamic registration decal — toggle
- Use calculated ILS signals — toggle
- FDR enabled — toggle

---

### Settings — Realism

**Settings exposed:**
- ADIRS align time (Instant / Fast / Real) — radio group
- DMC self-test time (Instant / Fast / Real) — radio group
- Boarding time (Instant / Fast / Real) — radio group
- Autofill checklists — toggle
- Separate tiller from rudder inputs — toggle
- MCDU keyboard input — toggle (+ focus timeout sub-setting)
- Sync EFIS — toggle
- Pilot avatar — toggle
- First Officer avatar — toggle
- Pause at TOD — toggle (+ distance sub-setting)

---

### Settings — 3rd Party Options

**Settings exposed:**
- **Navigraph:** Sign-in status label (username or "Not signed in"), *Sign In* button (starts device-flow auth via `Navigraph.auth` — updates FBW's own auth), *Sign Out* button
- **AIRAC cycle:** read-only label
- SimBrief pilot ID — text input + *Validate* button
- Auto-import SimBrief data — toggle
- GSX fuel enabled — toggle
- GSX payload enabled — toggle
- GSX power enabled — toggle

---

### Settings — ATSU / AOC

**Settings exposed:**
- Hoppie ACARS network user ID — text input
- Automatically import SimBrief data — toggle
- Weather source (MSFS / IVAO / VATSIM / PilotEdge) — combo box
- ATC network (offline / VATSIM / IVAO / POSCON) — combo box

---

### Settings — Audio

**Settings exposed (all sliders mapped to numeric spin boxes 0–100):**
- Exterior master volume
- Engine interior volume
- Wind interior volume
- PTU audible in cockpit — toggle
- Passenger ambience — toggle
- Announcements — toggle
- Boarding music — toggle

---

### Settings — flyPad

**Settings exposed:**
- Language — combo box
- Onscreen keyboard layout — combo box
- Auto-show onscreen keyboard — toggle
- Auto-brightness — toggle (+ brightness spin box when off)
- Battery life enabled — toggle
- Show status bar flight progress indicator — toggle
- Show coloured raw METAR — toggle
- Time displayed (UTC / Local / Both) — combo box
- Time format (12h / 24h) — combo box

---

### Settings — About

Read-only labels: flypad version, AIRAC cycle, SimBridge connection status.

---

## Data Flow

```
C# A32NXEFBForm
    │  (reads state, sends commands)
    ▼
EFBBridgeServer (port 19777, existing)
    │  HTTP POST /state  ◄──────────────────────┐
    │  HTTP GET /commands ──────────────────────►│
    ▼                                            │
CoherentGTInjector                    bridge JS (injected)
    │                                  runs inside FBW EFB
    │  TCP WebSocket                   SimVar / NXDataStore /
    └─► ws://127.0.0.1:19999/devtools/page/{id}  Navigraph.auth
           CDP Runtime.evaluate (inject once per page load)
```

---

## Settings persistence

All NXDataStore-backed settings are read and written via the injected bridge:
```javascript
NXDataStore.get('KEY', 'default')   // read
NXDataStore.set('KEY', value)        // write — persists across sessions
```
Changes take effect immediately in FBW (same as changing them on the flypad).

---

## Bridge injection lifecycle

1. `CoherentGTInjector.Start()` called when `FlyByWireA320Definition` loads
2. Polls `/pagelist.json` every 3 s
3. On finding `title.EndsWith("- EFB")`: reads bridge JS from embedded resource, sends via raw-TCP WebSocket `Runtime.evaluate`
4. Bridge posts `connected` state → `A32NXEFBForm` updates connection status label
5. On page reload (ID changes or WS drops): re-inject after 2 s delay
6. `CoherentGTInjector.Stop()` called on aircraft change or app close

---

## Files created or modified

### New files
| File | Purpose |
|------|---------|
| `MSFSBlindAssist/SimConnect/CoherentGTInjector.cs` | CDP injection service |
| `MSFSBlindAssist/Forms/A32NX/A32NXEFBForm.cs` | Accessible WinForms form |
| `MSFSBlindAssist/Forms/A32NX/A32NXEFBForm.Designer.cs` | Designer file |
| `MSFSBlindAssist/Resources/a32nx-efb-accessibility-bridge.js` | Bridge JS (promoted from bin/Debug) |

### Modified files
| File | Change |
|------|--------|
| `MSFSBlindAssist/MainForm.cs` | Add `_a32nxEFBForm`, `ShowA32NXEFBDialog()`, `CleanupA32NXEFBForm()`, FBW branch in `ShowPMDGEFB` case, start/stop injector on aircraft load |
| `MSFSBlindAssist/MSFSBlindAssist.csproj` | Add bridge JS as `<EmbeddedResource>` |

---

## Out of scope (future phases)

- Navigraph charts (images, not accessible)
- Pushback map-based steering
- Performance calculator full implementation (DOM selectors need live-sim verification)
- ATC CPDLC full implementation (DOM selectors need live-sim verification)
- ATC free-text compose (only preset responses in phase 2)
- Multi-language support (English only)
