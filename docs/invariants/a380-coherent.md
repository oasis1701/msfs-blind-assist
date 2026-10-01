# FlyByWire A380X Coherent clients, OANS, RMP and flyPad — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/a380-coherent.md`, which Claude Code loads when it reads matching code. Background: [a380x.md](../a380x.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's.

## A380C-1

- Never assume a Coherent UI element needs the KCCU cursor or a DOM click without checking what the real cockpit input drives underneath — the MFD uses `InputField` keypress, the ECL is driven by ECP L:var pulses, neither needs the KCCU cursor. → [a380x.md](../a380x.md)

## A380C-2

- Every Coherent client's `EnsureConnected` must re-install the agent on a still-open socket instead of reconnecting, and must Abort+Dispose any existing socket BEFORE `ConnectAsync` — skipping either orphans a healthy old socket and permanently loses the page for the process. → [a380x.md](../a380x.md)

## A380C-3

- Any public on-demand scrape method competing with a background `RunLoop` must be serialized by its own connect-lock (`_connectLock`) — the existing `_sendLock` only covers `SendAsync`, not connection setup, and doesn't close the race. → [a380x.md](../a380x.md)

## A380C-4

- Never construct a second `CoherentDisplayClient("A380X_EWD")` while `EwdMonitor` exists — the SD Upper-E/WD fallback must go through the one always-on monitor socket. → [a380x.md](../a380x.md)

## A380C-5

- The ECL must share the EWD monitor's existing Coherent socket, never open its own — a second inspector connection to the same page is rejected. → [a380x.md](../a380x.md)

## A380C-6

- Every form marshaling a background bridge-push to the UI thread must wrap `BeginInvoke` in try/catch(InvalidOperationException) (`SafeBeginInvoke`) — an `IsHandleCreated` check alone races a concurrent handle-destroy on aircraft swap/window close. → [a380x.md](../a380x.md)

## A380C-7

- Never re-add the OANS forced-render/zoom/visibility DOM scrape path — it exhausted host commit memory and froze the whole machine; OANS must stay DATA-ONLY (JS instance reads + `btvUtils` method calls, never a canvas draw/scrape). → [a380x.md](../a380x.md)

## A380C-8

- Never loop `armExit`/`armRunway` over the full OANS exit list — each accepted arm triggers a canvas redraw, and a full sweep exhausts memory; only arm the 1-2 same-named candidate features. → [a380x.md](../a380x.md)

## A380C-9

- OANS `loadAirportMap(icao)` must be called DIRECTLY, never only via the `oans_display_airport` bus event — under perf-hide mode the bus handler only sets the ICAO and skips the actual load. → [a380x.md](../a380x.md)

## A380C-10

- KCCU H-events do not reach the MFD via `Coherent.trigger`/`SimVar.SetSimVarValue` from the external debugger — they must be published on the MFD's own msfs-sdk EventBus (`bus.pub('hEvent', ...)`); required for F-PLN scrolling and any KCCU-driven navigation. → [a380x.md](../a380x.md)

## A380C-11

- `fireKey` must fire each KCCU H-event ONCE, never twice — firing it via both `Coherent.trigger` AND `SimVar.SetSimVarValue` caused double/erratic F-PLN paging. → [a380x.md](../a380x.md)

## A380C-12

- Never widen the flyPad Dashboard's column-first read order to other EFB pages without evidence a specific page is jumbled — a blind global split would break single-column pages. → [a380x.md](../a380x.md)

## A380C-13

- Never key a settings-page unit-toggle detection on a universal "checked=metric" assumption — direction differs per toggle id; use the per-id `UNIT_PAIRS` map. → [a380x.md](../a380x.md)

## A380C-14

- `buildSettingsLines` must return null (defer to the generic pass) when it finds no recognizable control in a region, rather than rendering an owned-but-blank page — this is the safety net for layouts the builder doesn't recognize. → [a380x.md](../a380x.md)

## A380C-15

- Door-tile precise names must never trust the FBW enum DIGIT alone — it can be wrong (index 9 "Main4Right" is actually Main Door 5 Right); always parse the handler comment's enum NAME, falling back to column-based Left/Right only when the enum can't be parsed. → [a380x.md](../a380x.md)

## A380C-16

- `A.DOOR_NAMES` (flyPad agent) must be kept in sync with each aircraft def's `_doorDefs` table — the flyPad label and the spoken door name must agree. → [a380x.md](../a380x.md)

## A380C-17

- The metric-weight toggle must never write `SetStoredData` directly and expect it to propagate — the ONLY reliable aircraft-side write is the real EFB "US Units" toggle; MSFSBA's Units button is a LOCAL read-out preference only, kept separate from the last-known aircraft value so the button and the live monitor don't fight. → [a380x.md](../a380x.md)

## A380C-18

- Never re-add the RMP "Radios" panel using stock COM standby-set/swap events on the A380 — the FBW A380 ignores them entirely; all tuning is RMP-only. Anything else that tunes COM with the stock events must ask `IAircraftDefinition.StockComTuningRefusal` first and speak it instead (the surroundings window's frequency list does). → [a380x.md](../a380x.md)

## A380C-19

- The RMP VHF standby readback must be computed from the TYPED digit entry, never from a single polled scrape — the FBW autocomplete settles over several frames after the last keystroke. → [a380x.md](../a380x.md)

## A380C-20

- RMP row selection must be authoritative from the manual `Ctrl+1/2/3` selection, synced from the scrape only on the FIRST poll — a per-poll sync races the LSK-select registration lag and resets a fresh selection back to row 0. → [a380x.md](../a380x.md)

## A380C-21

- RMP squawk must always be set via the stock `XPNDR_SET` event, independent of which RMP page is displayed — the page-switch+keypad+auto-validate chain was unreliable to drive externally. → [a380x.md](../a380x.md)

## A380C-22

- The RMP announce (`Apply`) must marshal to the UI thread — `_announcer.Announce` silently fails off the UI thread while the dedup key still updates, masking future announcements. → [a380x.md](../a380x.md)

## A380C-23

- Every A380 form holding a Coherent client or the def instance must be disposed in `SwitchAircraft`'s cleanup — a hide-on-close form (RMP) needs teardown in `Dispose(bool)`, since `Close()` is cancelled by the hide guard and `Form.Dispose()` skips `OnFormClosed`. → [a380x.md](../a380x.md)

## A380C-24

- Capture the OUTGOING aircraft def at the top of `SwitchAircraft` for cleanup (`StopAllMotion()`, EWD-monitor teardown) — otherwise seat/slider motor timers keep writing L:vars into the new aircraft for seconds after the swap. → [a380x.md](../a380x.md)

## A380C-25

- The A380 EWD scrape must silently baseline on first connect — only failures appearing AFTER connect should announce, matching every other MSFSBA monitor (avoids re-reading the whole screen on reconnect). → [a380x.md](../a380x.md)
