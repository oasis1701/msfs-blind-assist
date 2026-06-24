# Gate-data correctness: clean identity, safe enrichment, correct GSX/VDGS/search

- **Date:** 2026-06-23
- **Branch:** `feat/taxi-data-augmentation`
- **Status:** Design approved (pending written-spec review)
- **Approach:** 1 — surgical changes under an *identity-vs-enrichment* principle (keep existing structure; low regression risk in the tuned taxi/docking code).

## 1. Problem

The taxi-data augmentation on this branch enriches gate names from online sources (OpenStreetMap `parking_position` + the X-Plane Gateway `apt.dat`). For airports whose navdata gate names are empty (CYUL stores most gates as `name="G"` → decoded to `""`), the augmentation's *empty-name fill* adopts the **nearest online stand name within 30 m** as the gate's `Name`. This is geometrically unsafe at dense terminals and corrupts gate identity, which then poisons every downstream consumer (label, GSX selection, taxi routing, VDGS docking, search).

### Evidence (captured during the 2026-06-23 investigation)

- Real-world / FlightAware reality: YUL terminal gates are **numeric** (1–89). FlightAware shows `[concourse letter][gate number]` (e.g. `A51`, `C83`); the **number always equals the navdata gate number** (verified across 10 live flights: A2/A5/A50/A52/A63/A64/C73/C83 etc.). OSM `aeroway=gate` refs are all plain numbers and align 1:1 with navdata (0 mismatches across 44 gates).
- The corruption comes from **apt.dat** (Gateway scenery `sceneryId=101802`): its author-placed ramps are sparse and offset, so the nearest-within-30 m fill grabs a neighbor. Reproduced against real data:
  - navdata gate **15** → apt.dat `"Gate 11B"` (19 m) → label `Gate 11B 15 …` (wrong stand).
  - navdata gate **86** → apt.dat `"Gate 87"` (26 m).
- Symptoms heard by the user in the Shift+G list and the taxi-guidance gate list:
  - `Gate 9 9 - Gate Medium (Jetway) (also Gate 9)` (redundant doubling)
  - `Gate 11B 15 - Gate Medium (Jetway) (also Gate 11B)` (wrong identity)
- Root locations:
  - Fill/alias logic: `MSFSBlindAssist/Services/TaxiAugment/AugmentingAirportDataProvider.cs` `GetParkingSpots` (~L94–159).
  - Label rendering: `MSFSBlindAssist/Database/Models/ParkingSpot.cs` `Describe()`/`ToString()` (L153/L179); `(also …)` is built only at L187.
  - Generic-gate decode: `MSFSBlindAssist/Database/LittleNavMapProvider.cs` `MapParkingName` (`"G"` → `""`, L982).
  - Gate list source: `MSFSBlindAssist/Services/GateDataSource.cs` (GSX-authoritative merge, else navdata).
  - Shift+G = Gate Teleport (`HotkeyManager` `HOTKEY_GATE_TELEPORT`, Shift+G → `GateTeleportForm`); taxi-guidance gate list = `TaxiAssistForm`.

## 2. Requirements (from the user)

1. **Clean up gate-list verbosity** in both lists (Shift+G `GateTeleportForm` and the taxi-guidance gate list `TaxiAssistForm`). The user wants the **same full format, with the garbage removed** — not less information. Target: `Gate 51 - Gate Heavy (Jetway) [SafeDock]`, with correct identity, no corruption, no duplicate `(also …)`, and generic gates reading `Gate 51` not `Spot 51`.
2. **GSX selects the correct gate** when a gate is chosen as a **taxi destination** (taxi-to-gate flow only; gate-teleport stays teleport-only).
3. **Fill missing gate data across the board** from **GSX** + **online (OSM/apt.dat)** (not the other sim's DB; online stays in play).
4. **Taxi to the selected gate and use VDGS without errors.**
5. **Search returns appropriate results.**

### Source priority (user refinement) and the anti-grass rule

- Priority intent: **GSX → online → sqlite**, BUT **a gate that exists only in the online database (not in GSX or sqlite) must never be selectable**, to prevent taxiing to grass.
- Reconciled **per field** (the grass risk lives in *position*, never in *names*):

| Field | Priority | Notes |
|---|---|---|
| Selectable gate set | GSX ∪ sqlite | Online **never** adds a selectable gate. *(anti-grass #1)* |
| Taxi / dock position | GSX (if GSX stand) → sqlite | Online positions **never** route or dock. *(anti-grass #2 — the real safeguard)* |
| Display name / identity | GSX name → `Gate {number}` (sqlite) | Online is never the primary name |
| Aliases (searchable) | online (identity-matched) | The one thing online contributes |
| VDGS / stop-point | GSX only | |
| Other missing fields (jetway, etc.) | GSX → sqlite | |

"Online before sqlite" is therefore reinterpreted as: **online is alias-only** — it can add a searchable alternate label for the *same* stand but can never replace a gate's identity or position. This is what keeps the clean label AND guarantees no grass.

## 3. Design

### 3.1 `StandIdentity` — one shared parser (new)

A pure, probe-tested helper that parses any stand label/ref into a canonical key `(leadingLetter, number, trailingSuffix)`. Used by the matcher, search, and (verification of) GSX keying so they can never disagree.

- Strip a leading word token (`GATE`/`STAND`/`PARKING`), then parse: optional leading letter(s) → `leadingLetter`; digits → `number`; optional trailing letter(s) → `trailingSuffix`.
- Examples: `Gate 11B`→`(—,11,B)`; `A51`→`(A,51,—)`; `N1`/`N 1`→`(N,1,—)`; `51`→`(—,51,—)`; `53A`→`(—,53,A)`; `F211`→`(F,211,—)`; `P 209`→`(P,209,—)`; `""`→ none.
- Returns "no identity" when there is no number.

### 3.2 Matching rule — replaces `AugmentingAirportDataProvider.GetParkingSpots` fill/alias

The new rule is **identity-matched (not nearest-by-distance), alias-only, and idempotent.**

For each authoritative gate (from `_base`, i.e. GSX-merged or sqlite), derive its identity from **stable fields** — `StandIdentity.Parse(spot.Name)` for the letter (navdata-decoded `N/E/W/S/P/…` or empty) combined with `spot.Number` + `spot.Suffix`. **Never** derive identity from a previously-filled display string.

Then, over online stands (OSM `parking_position` + apt.dat ramps):

| Gate identity | Online stand | Match? | Result |
|---|---|---|---|
| `(—,15)` | `Gate 11B` `(—,11,B)` | ✗ number 15≠11 | nothing — corruption gone |
| `(—,51)` | `Gate 51` / `51` | ✓ same, no extra info | nothing — kills `Gate 51 51` / `(also 51)` |
| `(—,51)` | `A51` `(A,51)` | ✓ number, +concourse letter | alias `A51` (online) |
| `(—,53)` | `53A`,`53B` | ✓ number, +suffix | aliases `53A`,`53B` (online) |
| `(N,3)` | `S3` `(S,3)` | ✗ letters differ | nothing — N/S de-ice pads never cross |

Matching predicate (`number` must always be equal):
- both sides have a leading letter → letters must be **equal**;
- gate has no leading letter, online has one (concourse prefix like `A51`) → **allowed** (the prefix is the new info);
- gate has a leading letter, online has none (e.g. `N 3` vs bare `3`) → **not matched** (safety);
- trailing suffix differing (gate `53` vs online `53A`) → matched as a **sub-stand alias**.

Add as an **alias** only when the online label adds info the identity does not already state (an extra leading letter and/or trailing suffix). A pure restatement of the same identity (`Gate 51`/`51` for gate 51) contributes nothing.

Invariants enforced by this method:
- **Never** writes `spot.Name`, `spot.Latitude`, or `spot.Longitude`.
- **Never** promotes an online-only stand to a selectable gate (it only annotates the authoritative gates returned by `_base`).
- **Idempotent**: aliases are recomputed deterministically from `(identity, online stands)` and deduped; running twice yields identical results. This structurally removes the `(also Gate 9)` doubling regardless of object reuse.
- Distance is a **sanity tiebreaker only**: if multiple same-identity online stands exist, prefer the nearer; if the single identity-match sits absurdly far from the navdata position (> ~150 m), skip it as a data error.

### 3.3 Display / label (`ParkingSpot`)

- **Identity rendering** is type-aware in the display layer: when `Name` is empty and `Type` is a gate type, render `Gate {number}{suffix}`. `MapParkingName("G")` stays `""` (do **not** change it — keeping `Name` as the pure navdata letter/empty is what keeps `StandIdentity` stable). De-ice/letter stands keep `N 3`, `E 1`, etc.
- **Alias rendering**: because online is now the *only* source of aliases, every `ParkingSpot.Aliases` entry is online-sourced, so `ToString()` tags them uniformly — `… , also A51 (online)` (or `also A51, 53A (online)`) — with **no per-alias flag needed**. `Describe()` (no aliases) vs `ToString()` (with aliases) split is preserved.
- Default: the online alias appears in the scroll-by line (user-confirmed).

### 3.4 GSX selection & VDGS (taxi-to-gate flow)

- The identity handed to `GsxGateSelector` is the clean GSX/sqlite identity (number + real letter), **never an online alias**. The selector keeps its existing live-menu DFS + bare-number fallback; it works reliably once the key is clean.
- The online alias is **display/search only** — never the GSX match key or the dock target.
- VDGS/docking reads the GSX stop position + per-aircraft offset for the *correctly-selected* stand (existing `GsxStopOffsetResolver` / occupancy-clamp machinery unchanged). Correct selection → correct stop → no docking error.
- **Transparency add:** the taxi flow announces the GSX-selection result ("GSX gate 51 selected" / "couldn't find gate 51 in the GSX menu") rather than only writing `gsx-gate-select.log`.

### 3.5 Search

- `GateSearchFilter` matches the clean identity (number + letter) **and all aliases** (including `(online)` ones) — so `A51`→gate 51, `53A`→the sub-stand. (Confirm the filter's current corpus during planning and extend it to aliases.)
- The identity-match rule removes wrong hits: `11B` no longer returns gate 15.

### 3.6 "Would this gate lead to grass?" signaling (`TaxiAssistForm`)

- By construction an aliased gate is as safe as a plain one (position is always GSX/sqlite). The residual risk is a verified gate the **taxi graph cannot reach** (no node within `MAX_PARKING_TO_GRAPH_M = 100 m`), which is silently dropped today.
- Change: keep such a stand discoverable but **mark it `(no taxi route)`**, and if chosen as a taxi destination, **announce a clear warning** instead of building a route across non-pavement. Covers GSX/sqlite gates too, not just online-aliased ones.

### 3.7 "Fill missing data across the board" — scope

Realized via: GSX-authoritative merge (existing `GsxNavdataMerger` fills positions/VDGS/names GSX knows), online number-matched aliases (new rule), and clean identity rendering. We deliberately **do not fabricate positions or new gates from online** (anti-grass). No new cross-fill engine.

## 4. Components / files touched

- **New** `StandIdentity` helper (pure; probe-tested).
- `Services/TaxiAugment/AugmentingAirportDataProvider.cs` — new matching rule; delete the name-overwrite fill branch.
- `Database/Models/ParkingSpot.cs` — type-aware `Gate {n}` identity; `(online)`-tagged alias rendering.
- `Services/GateSearchFilter.cs` — include aliases in the corpus.
- `Forms/TaxiAssistForm.cs` — `(no taxi route)` mark + warn-on-select; announce GSX-selection result.
- `Forms/GateTeleportForm.cs` — inherits clean labels via `ToString()` (no logic change expected).
- `Services/Gsx/GsxGateSelector.cs` / docking — verify they key off the identity, not the alias (expected no change).

## 5. Non-goals

- No change to GSX selection in the gate-teleport (Shift+G) flow — it stays teleport-only.
- No cross-fill between `fs2020.sqlite` and `fs2024.sqlite`.
- No fabricating gate positions or new selectable gates from online data.
- No restructuring of the tuned taxi-router / docking internals (Approach 2 was rejected for regression risk).
- No airport-specific hardcoding (CYUL is the test case; the fix is general).

## 6. Verification

- **Probe tests** (repo has no xUnit; reuse `tools/TaxiAugmentProbe` + `AppContext.BaseDirectory` fixtures) using the **real CYUL data captured this session** (navdata gates + OSM `parking_position` + apt.dat ramps):
  - gate 15 gets **no** `Gate 11B` alias (number mismatch);
  - gate 51 gets **no** redundant alias (same number, no extra info);
  - a synthetic `A51` online stand aliases gate 51, tagged `(online)`;
  - `N 3` vs `S3` never cross-alias;
  - **idempotency**: run the rule twice → identical aliases;
  - `StandIdentity` parser cases (`Gate 11B`, `A51`, `N1`, `51`, `53A`, `F211`, `P 209`, empty).
- **Existing probes stay green**: `TaxiAugmentProbe`, `TaxiGuidanceProbe`, `ProgressiveTaxiProbe`.
- **In-sim test plan** (owner runs; written into the PR): at CYUL with GSX — clean labels in both lists; selecting a gate as taxi destination drives GSX to the correct stand (announced); correct taxi route; VDGS docks clean; search `51`/`A51` → gate 51; an unreachable stand shows `(no taxi route)` and warns. Plus a non-GSX airport to confirm the sqlite-only path + clean labels.

## 7. Open questions

None outstanding. (Online alias shown in the scroll-by line by default — confirmed.)
