# Route description persistence — design

**Date:** 2026-09-28
**Status:** Approved

## Problem

The flight bag's (Shift+E) Describe Route result lives only in the window's `routeDescriptionTextBox`.
Closing the flight bag disposes the form (`MainForm.ShowElectronicFlightBagDialog` then builds a new one),
so the description — which costs an AI call plus up to ~25 s of taxi-route computation — is lost. A database
switch or rebuild also closes the flight bag (and recreates `FlightPlanManager`); an aircraft switch does not.

## Requirement

- The generated description is retained while the app runs, across flight-bag close/reopen, database
  switches and rebuilds.
- It is erased ONLY when Load SimBrief actually loads a new plan (a failed load keeps it), including the constructor's auto-load, which is the same
  action. It is kept in memory only: closing the app forgets it.
- A description still generating when the flight bag closes finishes in the background, is kept, and
  "Route description ready" is announced (a background state change — allowed by the announcement rules).

## Design

### `Services/RouteDescriptionSession` (pure, no WinForms)

Created once by MainForm, passed to every `ElectronicFlightBagForm`.

- `string Text` — the kept description (`""` when none).
- `bool IsGenerating` — a description is being prepared, by any flight-bag instance.
- `int Generation` — bumped by `Clear()`.
- `int BeginGenerating()` — sets `IsGenerating`, raises `Changed`, returns the current `Generation`.
- `void EndGenerating()` — clears `IsGenerating`, raises `Changed`.
- `bool TryStore(int generationAtStart, string text)` — stores `text` and raises `Changed` only when
  `generationAtStart == Generation`; returns whether it stored.
- `void Clear()` — erases `Text`, bumps `Generation`, raises `Changed`.
- `event EventHandler? Changed`.

### `ElectronicFlightBagForm` wiring

- Constructor takes the session. On open, if `Text` is non-empty, the Route Description box shows it
  (visible); Describe Route is enabled only when `!IsGenerating` and the plan has `ExtractedFlightData`.
- Subscribes to `Changed` to refresh the box and the button; unsubscribes on `FormClosed` so the
  app-lifetime session never keeps a closed form alive.
- `LoadSimBriefFlightPlan` calls `session.Clear()` right after `LoadFromSimBrief` returns (a failed load throws first and keeps the description), where it used to blank the box.
- `DescribeRouteAsync`:
  - The per-form `_describingRoute` guard becomes `session.IsGenerating` (one at a time across windows).
  - Captures `BeginGenerating()`'s generation; `EndGenerating()` in `finally`.
  - Window closing mid-run no longer cancels: the run continues. After each await, a discard happens only
    when Load SimBrief was pressed (generation changed) or the old reference check on the plan fails, with the
    existing "The flight plan changed…" announcement.
  - On success: `TryStore`; announce "Route description ready"; if the same form is still open, focus the box
    and update the status line (every UI touch is guarded by `!IsDisposed`).
  - Error announcements unchanged; status updates guarded by `!IsDisposed`.

### MainForm

Owns one `RouteDescriptionSession` field for the app's lifetime and passes it in `ShowElectronicFlightBagDialog`.
Nothing on the database switch/rebuild paths touches it.

## Testing

- xUnit `RouteDescriptionSessionTests`: starts empty; `TryStore` keeps text; `Clear` erases it and bumps
  generation; a store with a stale generation is rejected and leaves text unchanged; `BeginGenerating`/
  `EndGenerating` toggle the flag; `Changed` fires on store, clear, begin and end, and not on a rejected store.
- Manual (no sim needed; SimBrief username + AI key): generate, close/reopen → kept; close mid-generation →
  "ready" announced, shown on reopen; Load SimBrief → erased.
