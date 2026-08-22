# Weather Radar ListBox Conversion + Live Refresh — Design

**Date:** 2026-07-12
**Branch:** `feat/activesky-improvements` (part of PR #159 — the boxes being converted were
mostly added by this PR)
**Status:** Approved by Robin (brainstorming session 2026-07-12); implementation plan to follow.

## 1. Background and motivation

The 2026-07-02 "Live-Display Consistency Pass" (spec:
`docs/superpowers/specs/2026-07-02-live-display-consistency-design.md`) converted every live
text display in the app — E/WD, OANS, RMP, HS787 display + EICAS, GSX menu, ECL, all
MCDU/CDU/DCDU forms, MainForm's status display — from multi-line read-only TextBoxes to
screen-reader-navigable ListBoxes reconciled through one shared helper:
`Forms.DisplayList.UpdateInPlace` (rewrites only changed rows, grows/shrinks the tail in place,
never `Items.Clear()`, restores selection by ROW CONTENT nearest the old index, no-ops on
identical content). The shared control is `Forms.DisplayListBox` (`SetLines`/`SetText`,
optional `SuppressTypeAhead` for input-bearing displays).

The Weather Radar window (Shift+R) predates its own newest boxes' conventions: its five
multi-line readouts are still plain read-only TextBoxes written by whole-`.Text` assignment,
which resets the NVDA reading position on every refresh — and consequently the form has no
auto-refresh at all (refresh is F5/button/open only). Robin asked (2026-07-12) to investigate
converting them to ListBoxes and live-refreshing where appropriate; the investigation confirmed
the fit, and this design completes the consistency pass for the radar window.

Decisions fixed during brainstorming:

- **Convert all five multi-line boxes**; the single-line ActiveSky mode box **stays a TextBox**
  (the consistency pass's own carve-out: a one-line list adds nothing).
- **Live refresh: 30 seconds, everything**, via the existing `RefreshAsync(forceRefresh: false)`
  — internet-backed sources keep serving from their caches until TTL.
- Approach A: `DisplayListBox` swap + one E/WD-style timer (raw per-form ListBoxes and the
  TextBox `SetPreserveCaret` alternative were considered and rejected — the shared control IS
  the established pattern, and `SetPreserveCaret` is deliberately down to a single legacy
  caller).

## 2. The conversion (`Forms/WeatherRadarForm.cs`)

The five fields change type from `TextBox` to `Forms.DisplayListBox`:

| Field | Content shape after newline split |
|---|---|
| `_currentWeatherBox` | one label:value fact per row (wind, turbulence, visibility, …) |
| `_stationBox` | decoded-weather rows, blank separator row, `"Raw METAR:"`, raw METAR row |
| `_profileBox` | cloud-layer rows, `"Winds and temperatures aloft:"` header, level rows |
| `_advisoriesBox` | header, `─`×58 rule row, per-advisory stanzas with blank separators |
| `_windsAloftBox` | header, `─`×36 rule row, one level per row, `Source:` footer |

Construction: each becomes `new DisplayListBox { ... }` keeping its exact current
`Location`/`Size` (y-cursor layout untouched), `AccessibleName`, `AccessibleDescription`, and
`Visible` behavior (the three AS-only boxes still hide when the switch is off), with
`Font = new Font("Consolas", 9)` set explicitly (the control defaults to 10). The
`Multiline/ReadOnly/ScrollBars` TextBox properties disappear with the type change;
`SuppressTypeAhead` stays at its default `false` (nothing in this form takes keyboard input).
The placeholder texts ("Press F5 or Refresh to fetch weather data." etc.) are delivered via
`SetText` at construction time — a one-row list until the first fetch.

Write sites: every `_box.Text = value` in `RefreshAsync` (and the initial placeholder
assignments) becomes `_box.SetText(value)`. `SetText` splits on `\r\n`/`\n` with
`StringSplitOptions.None`, so blank separator rows survive as empty items — the pass's design
explicitly declared blank and duplicate (`─` rule) rows safe under `UpdateInPlace`'s
nearest-index duplicate matching.

Deliberately unchanged:
- `_asModeBox` (single-line TextBox) — carve-out, already keyboard-reachable (2026-07-11 fix).
- Tab order (`SetupAccessibility`), `Load` focusing `_currentWeatherBox`, F5/Escape handling.
- No caller-side selection overrides after `SetText` — matching `FbwEwdWindow` (the radar has
  no positional-key semantics; the CDU-style index restores are for LSK screens).
- All text-building code (`FormatAmbientFromActiveSky`, `BuildProfileNarrative`,
  `BuildWindsAloftText`, advisories builder) — byte-identical output, only the sink changes.

## 3. Live refresh

A `System.Windows.Forms.Timer`, interval **30 000 ms**, mirroring `FbwEwdWindow`:

- Created with the form; started in the `Load` handler after the initial
  `RefreshAsync(forceRefresh: true)` completes its kickoff (start the timer right after the
  awaited call — exact ordering keeps the first tick from racing the opening fetch, though the
  `_isFetching` guard makes any overlap a no-op anyway).
- `Tick += (_, _) => _ = RefreshAsync(forceRefresh: false);`
- Stopped and disposed in BOTH `OnFormClosed` and `Dispose` (the E/WD does both; the radar
  closes fully rather than hiding, but belt-and-braces matches the precedent).

`forceRefresh: false` semantics (already plumbed through the fetches): advisories
(`WeatherService.GetNearbyAdvisoriesAsync`/`GetNearbyPirepsAsync`) and Open-Meteo winds
(`WeatherService.GetWindsAloftAsync`) serve from their existing TTL caches; the AS/SimConnect
fetches (ambient, station, profile, AS winds, mode line, position) re-run each tick, so the
radar tracks the flight. F5 and the Refresh button keep passing `forceRefresh: true`.

**Refresh-button focus hazard (folded-in fix):** `RefreshAsync` currently sets
`_refreshButton.Enabled = false` during a fetch. WinForms moves focus off a focused control
when it is disabled — with a 30 s timer this would steal focus from a user resting on the
Refresh button, exactly the class of bug this redesign removes. The `Enabled` toggling is
DELETED (both the disable and the `finally` re-enable); the `_isFetching` guard already makes
mid-fetch clicks harmless no-ops.

The status label keeps updating every tick ("Fetching weather data…" → "Last updated
HH:MM:SS") — it is a non-focusable `Label`, so it disturbs no reading position.

## 4. Error handling

Unchanged per-section degradation: a failed section's `SetText("unavailable")` reconciles to a
one-row list; recovery reconciles back without touching the user's cursor (content-based
selection restore). The 3-second SimConnect ambient timeout, the AS null-degradation paths, and
the top-level try/catch in `RefreshAsync` are untouched. No new announcements of any kind —
the timer must remain inaudible except through changed row content under the user's cursor.

## 5. Testing

No new pure logic: the reconcile contract is already pinned by `DisplayListUpdateInPlaceTests`
(only-changed-rows, selection-follows-content, duplicate-nearest, clamp, no-op). The
deliverable is form wiring — sim-facing, verified by the PR's in-sim plan:

1. Open Shift+R, arrow into the middle of each box, wait through several auto-refresh cycles —
   the cursor stays on its row; changed rows (e.g. winds during climb) update in place.
2. Fly for a few minutes without touching F5 — all boxes track the flight; "Last updated"
   advances every ~30 s.
3. Rest focus ON the Refresh button through a tick — focus stays put (Enabled-toggle removed).
4. F5 and the Refresh button still force-refresh; Escape still closes; tab order unchanged;
   AS-only boxes still vanish when the switch is off.
5. NVDA reads each box row-by-row with arrow keys (one fact per row), including the blank
   separators and rule lines in advisories/winds.

## 6. Documentation

- `docs/weather.md`: note in the Weather Radar sections that the readouts are `DisplayListBox`
  rows reconciled via `DisplayList.UpdateInPlace` and that the window auto-refreshes every 30 s
  (non-forced; caches honored), with the mode line as the deliberate single-line TextBox
  exception.
- `docs/a32nx.md`'s "single reconcile home" list (or wherever the DisplayList consumer list
  lives) gains the Weather Radar window.

## 7. Delivery

On `feat/activesky-improvements` (PR #159). Two implementation commits — (1) the ListBox
conversion, (2) the timer + Enabled-toggle removal — plus (3) docs. In-sim items appended to
the PR checklist.

## 8. Out of scope

- Converting `_asModeBox` (single-line carve-out) or the GSX-style status label.
- Any change to refresh cadences of the underlying caches (advisories/Open-Meteo TTLs).
- Announcing radar content changes (the auto-announce path is a separate, existing feature).
