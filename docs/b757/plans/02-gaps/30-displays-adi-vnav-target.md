# BlueBird 757 Displays from Memory: the ADI's VNAV Target Speed (plan 30, added 2026-11-16)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own. Tasks marked [controller] or [owner] are never dispatched to an implementer.

**Goal:** In VNAV without speed intervention, the ADI window's "Left: target speed" row shows the target BlueBird draws (the FMC's, through the active route's path object), where plan 27 shows none; in an active hold it says the target is not read.

**Architecture:** An eighth optional part of plan 27's ADI layout, `VnavTarget`, read by `Bbs757AdiReader` only while VNAV is the target's source and decoded in the speed tape draw's own order (25862). No new reader, window, hotkey or spoken line: the row already exists for the MCP's speed.

**Tech Stack:** C# 13 / .NET 10, xUnit, the private toolkit, the b757-memory probe scripts (read-only).

**Spec:** "What BlueBird does" below, traced 2026-11-16 from this build's WAT (function numbers are WAT indices) and measured read-only at the ENGM gate; Task 1 copies it into `docs/b757/evidence/displays-adi.md`. Plan 27's Global Constraints and rulings stand. The owner's go-ahead, 2026-11-16: "Yes, trace it", after the LOWW to ENGM flight showed no target speed row for 1 h 47 min of VNAV (plan 27, "Flight results").

## What BlueBird does

The speed tape (25862) takes its target from the AFDS pitch mode `i32 afds+11924`. Plan 27 reads the first two cases:

1. **Not 6 or 7 (VNAV PTH, VNAV SPD):** the MCP's `f64 mcp+872`.
2. **6 or 7 with `u8 afds+15584` set (speed intervention):** the MCP's too.
3. **6 or 7 otherwise (new here):**
   1. **The route R** (f39247 on the fms, `pfd+628`): RTE 1 if its `u8 R+1235` (active) is set, else RTE 2 if set, else none. The routes are an unordered map at `fms+35216`: the first node at `+8`, a node's next at `+0`, its key `i32 +8`, its route object inline at `+16` (f39248, f39228). Live: key 2 at 0x612cc0 (inactive, id 4), key 1 at 0x47a590 (active, id 14).
   2. **The path object P** (f37385): the path registry's entry for `i32 R+1700`. The registry is the static map at 1197208: the first node at `+8`, next `+0`, key `+8`, value `+12` (the EHSI layout's `PathRegistry` fields). P is 0 with no R or no entry.
   3. **A holding speed** (f24303(P)), only while the active leg is a hold. Its checks:
      - `P+3248` (the route) ≠ 0, `i32 P+4272` = 0, `P+216` = `P+3248`, and `i32 P+224` ≥ 0.
      - `i32 P+220` equals the route's flown leg count (f24025: the vector at route+680, 1,632-byte legs).
      - `i32 P+256` is not 0 and not 5.
      - The active leg is `legs[0]` of route+668 (f24032 with index = the flown count), and its terminator `i32 leg+24` is 22, 23 or 24 (f24042).

      Further checks and f24309's holding speed follow. That speed is f24722 (16·√clamp(weight/1000, 132, 265) + 0.2·(clamp(altitude/1000, 1.5, 35) − 1.5)), capped by the hold's speed constraint. **Not ported** (R1).

      With P = 0, BlueBird runs the same checks on low memory: they fail at `i32 [4272]` = −1073741810 (measured), so there is no hold.
   4. **Else, pitch mode 7 with P ≠ 0 and the path's phase `i32 P+3916` = 2:** `f64 afds+11408` (f30633).
   5. **Else, P ≠ 0:** `f64 P+3944` (f25885). **Else:** `f64 afds+11408`.

**Units:** both values are knots.
- `P+3944` is written only by f24139 (with a kind at `P+3960`, or the pending modification's copy at `P+3952` while `P+4272` = 1). Its callers f24290 and f24105 convert a Mach target (≤ 1) to knots first (f24141).
- `afds+11408` is f30490's result after its own Mach conversion (f30551); f30559 and f30473 store it.

The draw prints the value as a whole number and places its bug against the airspeed shown, as for the MCP's speed.

**Measured at the ENGM gate, 2026-11-16** (flight complete, read-only): pitch mode 1, so not VNAV. RTE 1 is active with id 14, and P 0x28d31dc0 has phase 1, `P+3944` 100.00 and kind 1. `afds+11408` is 200.00. The registry holds ids 13 to 16, the others stale.

## Rulings (the controller's; the owner may reverse any)

- **R1:** while the hold checks above pass, the row reads "Left: target speed not read: in a hold", never a number. A guessed holding speed would be worse than none.
  - If wrong: in a hold whose later checks fail, the window says "not read" where BlueBird draws `P+3944`.
  - The port is a later plan, once a VNAV hold has been flown and measured.
- **R2:** any finite value shows as drawn, 0 included ("Left: target speed 0"). A non-finite one is `Corrupt`, by plan 27's rule (only a value a row shows).
- **R3:** the route and path walks happen only while case 3 is the source (plan 27's "only what a row can show now"). Each walk follows at most 64 nodes; a longer chain, or a node outside the module, is a lost read. A key not found is no route or no P, not a failure.
- **R4:** the layout's `GeneratorVersion` goes to 10 at landing. The generator-9 file is then removed from the layouts branch, as plan 27 did with 7 and 8.

## Global Constraints

- Plan 27's Global Constraints apply, with the trailer `Plan: 30` and the artifacts path `<worktree>/bin/b757-p30`.
- **No gate ([B757-30]):** nothing is compared with a simulator value.
- **Owned files:** `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757AdiLayout.cs` (the new part), `Bbs757LayoutDescriptions.cs`'s `DescribeAdi` line, `MSFSBlindAssist/Services/BBS757/Bbs757AdiReader.cs`, `Bbs757AdiReading.cs` and `Bbs757AdiText.cs`, their tests `tests/MSFSBlindAssist.Tests/Bbs757Adi{Layout,Reader,Text}Tests.cs`, the evidence file's new section, and plan 27's docs passages (Task 6). In the private repo: `Bbs757AdiRecipe.VnavTarget.cs` (new partial), its tests, and `GeneratorVersion.Current`.
- **Nothing spoken changes:** `Bbs757AdiText.Changes` and the five flags (B757-65) stay as they are.

## Review Focus

- **RTE 2 active, RTE 1 not:** the target comes from RTE 2's path (Task 3 test `Rte_2_is_the_source_when_only_it_is_active`).
- **A route active with no registry entry yet** (just activated, or emptied): `afds+11408`, never a stale entry's value (Task 3 tests `A_route_without_a_path_takes_the_afds_target` and `A_stale_registry_entry_is_never_used`).
- **A hold leg in the route but not active** (`P+220` ≠ the flown count, or the active leg not a hold): the path's target, not "not read" (Task 3 test `A_hold_leg_that_is_not_active_is_no_hold`).
- **A looping or broken node chain:** a lost read within 64 nodes, never a hang (Task 3 test `A_looping_route_map_is_a_lost_read`).
- **The MCP cases unchanged:** VNAV with intervention, and every other pitch mode, still read `mcp+872` and make no route reads (Task 3 test `Speed_intervention_reads_no_route`).

---

### Task 0 [controller]: Claim

- [ ] **Step 1:** `git switch -c B757-p30-vnav-target kn4iee/B757` (done in worktree `b757-p30`); add plan 30 to `00-index.md`'s display table; this file's Execution record; commit both files, `docs(b757): plan 30, the ADI's VNAV target speed`; `git push kn4iee HEAD:B757`. Private worktree at `feature/b757-displays`'s head.

### Task 1 [controller]: The evidence

**Files:** `docs/b757/evidence/displays-adi.md`

- [ ] **Step 1:** Replace the "Target speed" bullet's "**Not read** by this plan" sentence with a pointer to a new section, "### VNAV target speed (plan 30)", holding "What BlueBird does" and the gate measurement above. Commit `docs(b757): plan 30's VNAV target trace`.

### Task 2: The layout part

**Files:** modify `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757AdiLayout.cs`, `Bbs757LayoutDescriptions.cs` (`DescribeAdi`); test `tests/MSFSBlindAssist.Tests/Bbs757AdiLayoutTests.cs`

**Interfaces:**
- Produces: `public sealed record Bbs757AdiVnavTarget(...)`, compared by content (lists by sequence). The fields and this build's values:
  - **The routes:** `int FmsRoutes` 35216, `int RoutesFirst` 8, `int NodeNext` 0, `int RouteNodeKey` 8, `int RouteNodeRoute` 16, `IReadOnlyList<int> RouteNumbers` [1, 2], `int RouteActive` 1235, `int RouteId` 1700.
  - **The registry:** `uint PathRegistry` 1197208, `int PathRegistryFirst` 8, `int PathNodeKey` 8, `int PathNodeValue` 12.
  - **The path and the AFDS:** `int PathPhase` 3916, `int PathTarget` 3944, `int ClimbPhase` 2, `int SpeedMode` 7, `int AfdsTarget` 11408.
  - **The hold check:** `int PathRoute` 3248, `int PathModification` 4272, `int PathHoldRoute` 216, `int PathHoldIndex` 224, `int PathActiveIndex` 220, `int PathHoldState` 256, `IReadOnlyList<int> HoldStatesOff` [0, 5], `int RouteLegs` 668, `int RouteFlownLegs` 680, `int LegStride` 1632, `int LegTerminator` 24, `IReadOnlyList<int> HoldTerminators` [22, 23, 24].
- `Bbs757AdiLayout` gains `Bbs757AdiVnavTarget? VnavTarget, string? VnavTargetMissing` after `ReferenceSpeedsMissing` (JSON `vnavTarget`, `vnavTargetMissing`), in its `Equals` and `GetHashCode`.

- [ ] **Step 1: Write the failing tests:** `The_adi_layout_round_trips_byte_identical` gains the part with the values above (de-DE and sv-SE, as now); new `A_missing_vnav_target_part_keeps_its_reason`; new `Vnav_target_parts_differing_in_a_list_are_not_equal` (`HoldTerminators` [22, 23] against [22, 23, 24]).
- [ ] **Step 2:** Run `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Bbs757AdiLayoutTests"`. Expected: FAIL (no `VnavTarget`).
- [ ] **Step 3:** Implement the record and the layout's two fields; `DescribeAdi` adds one line, "vnav target: …" or its missing reason.
- [ ] **Step 4:** Same command. Expected: PASS. Then the whole suite: no other test changed.
- [ ] **Step 5:** Commit `feat(b757): the ADI layout's VNAV target part`.

### Task 3: Read it

**Files:** modify `Bbs757AdiReading.cs`, `Bbs757AdiReader.cs`; test `tests/MSFSBlindAssist.Tests/Bbs757AdiReaderTests.cs`

**Interfaces:**
- Consumes: Task 2's `Bbs757AdiVnavTarget`.
- Produces:
  - `public sealed record Bbs757AdiVnavRaw(bool Hold, bool HasPath, int Phase, double PathTarget, double AfdsTarget)`.
  - `Bbs757AdiSpeedRaw` gains `Bbs757AdiVnavRaw? Vnav` as its last member: null when case 3 is not the source or the part is missing.
  - `Bbs757AdiSpeedReading` gains `string? TargetSpeedNotRead` as its last member: "in a hold" (R1), or the part's missing reason while case 3 is the source.
  - `public static double? VnavTarget(Bbs757AdiVnavTarget part, Bbs757AdiVnavRaw raw, int pitchMode)` gives null in a hold, else case 3's value in order 4 then 5.

- [ ] **Step 1: Write the failing tests** on `FakeProcessMemory`, each laying out an fms route map, routes, the registry and P at this build's offsets:
  - `Vnav_target_comes_from_the_active_routes_path`: RTE 1 active, its P's `P+3944` 250.0, pitch mode 6 → `TargetSpeed` 250.0.
  - `Rte_2_is_the_source_when_only_it_is_active`: RTE 1 inactive with `P+3944` 300.0, RTE 2 active with 280.0 → 280.0.
  - `No_active_route_takes_the_afds_target`: neither active, `afds+11408` 240.0 → 240.0.
  - `A_route_without_a_path_takes_the_afds_target`: RTE 1 active, no registry entry for its id → `afds+11408`.
  - `A_stale_registry_entry_is_never_used`: entries for ids 13 and 15 at 310.0, the route's id 14 absent → `afds+11408`.
  - `Vnav_spd_in_the_climb_phase_takes_the_afds_target`: pitch mode 7, phase 2, `P+3944` 250.0, `afds+11408` 265.0 → 265.0.
  - `Vnav_pth_in_the_climb_phase_takes_the_paths_target`: pitch mode 6, phase 2 → 250.0.
  - `An_active_hold_leg_leaves_the_target_unread`: every hold check passing, `legs[0]` terminator 23 → `TargetSpeed` null, `TargetSpeedNotRead` "in a hold".
  - `A_hold_leg_that_is_not_active_is_no_hold`: the same, but `P+220` one more than the flown count → 250.0.
  - `A_looping_route_map_is_a_lost_read`: a node whose next is itself → `TryRead` false, within the bound.
  - `Speed_intervention_reads_no_route`: pitch mode 6 with intervention and the route map pointer outside the module → `TargetSpeed` the MCP's, `TryRead` true.
  - `A_non_finite_vnav_target_is_corrupt`: `P+3944` NaN → `Corrupt`.
  - `A_missing_vnav_target_part_says_so_in_vnav`: the part null with reason "not in this layout file", pitch mode 6 → `TargetSpeed` null, `TargetSpeedNotRead` that reason.
- [ ] **Step 2:** Run with `--filter "FullyQualifiedName~Bbs757AdiReaderTests"`. Expected: the new tests FAIL.
- [ ] **Step 3:** Implement. `TryReadRaw` reads `Vnav` only for case 3: the walks per R3, the hold check as listed in "What BlueBird does", with P = 0 run on low memory as BlueBird does. `Decode` sets `TargetSpeed` from `VnavTarget` and `TargetSpeedNotRead`. `CorruptCause` judges the VNAV value only when a row shows it.
- [ ] **Step 4:** Same command, then the whole suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(b757): read the ADI's VNAV target speed`.

### Task 4: The row

**Files:** modify `Bbs757AdiText.cs`; test `tests/MSFSBlindAssist.Tests/Bbs757AdiTextTests.cs`

The target row now truncates as the draw's own number does (`(int)` in 25862, the text's `Whole`), where plan 27 rounded. An FMC target converted from Mach is fractional (Mach .80 can give 287.6, which BlueBird prints as 287); the MCP's whole-number speeds read the same either way.

- [ ] **Step 1: Write the failing tests:**
  - `A_vnav_target_reads_as_the_target_speed_row`: `TargetSpeed` 287.6 → "Left: target speed 287", in the place the MCP's speed takes.
  - `A_target_not_read_says_why`: `TargetSpeed` null with `TargetSpeedNotRead` "in a hold" → "Left: target speed not read: in a hold", in the same place.
  - `Vnav_target_numbers_are_invariant_under_de_DE_and_sv_SE`: 1250.9 → "Left: target speed 1250".
  - `No_change_line_for_a_target_speed`: two readings differing only in the target → `Changes` empty.
- [ ] **Steps 2-4:** FAIL, implement (`Whole` for the target, and one `else if` after the target row), PASS with the whole suite.
- [ ] **Step 5:** Commit `feat(b757): the ADI's VNAV target row`.

### Task 5 [private repo]: The recipe

**Files:** create `toolkit/MSFSBlindAssist.WasmToolkit/Bbs757/Bbs757AdiRecipe.VnavTarget.cs`; modify `Bbs757AdiRecipe.cs` (the part's call and `VnavTargetMissing`); tests in `toolkit/MSFSBlindAssist.WasmToolkit.Tests/Bbs757AdiRecipeTests.cs`

- [ ] **Step 1:** Merge `B757-p30-vnav-target` into a private branch off `feature/b757-displays`.
- [ ] **Step 2: Write the failing tests:** `Derives_the_vnav_target_part` (every value in Task 2's list, from the installed file's WAT), and `A_vnav_target_it_cannot_derive_is_missing_not_a_failure`.
- [ ] **Step 3:** Implement. Each field is derived from the function that reads it (f39248/f39228, f14739, f37385, f74007/f74013, f24289, f25885, f30633, f24303, f24025, f24032, f24042, and 25862's mode-7 and phase-2 compares). Reuse the EHSI recipe's route and registry derivations (`Bbs757EhsiRecipe.Route.cs`, `.Helpers.cs`) where they read the same fields; never hardcode.
- [ ] **Step 4:** Run the toolkit suite. Expected: PASS.
- [ ] **Step 5:** Generate from the installed file. Every field equals Task 2's values, and the other readers come out byte-identical to the generator-9 file. Copy the file to a scratch layouts folder, not to `%APPDATA%` before Task 7.
- [ ] **Step 6:** Commit `feat(bbs757): the ADI's VNAV target part`, local only.

### Task 6: Docs and rules

**Files:** `docs/b757.md` ("The ADI (Alt+P)"), `guide/boeing-aircraft-and-the-md-11.md` ("The ADI window"), `docs/invariants/b757-adi.md` and its full text, `docs/wasm-memory.md` (the `adi` record row)

- [ ] **Step 1:** Write where the target speed now comes from in VNAV, and the hold's "not read". Remove "the VNAV target speed" from the lists of what is not read. B757-66 (BlueBird's conditions) gains the source order, in one line of at most 400 characters, with its full text; `ClaudeContextBudgetTests` green.
- [ ] **Step 2:** For the guide's new line, a pilot's words: in VNAV the target speed is the FMC's, and the line says when it cannot be read. Commit `docs(b757): the ADI's VNAV target speed in the docs and guide`.

### Task 7 [controller, simulator up]: Review, live check, land

- [ ] **Step 1:** A fresh whole-plan review on the most capable model, with this file's Review Focus; fix Critical and Important findings test-first.
- [ ] **Step 2:** Read-only at a gate with the generated layout, through the app's reader code: the target row on the ground, with VNAV not engaged (the MCP's), matches plan 27's.
- [ ] **Step 3, at the owner's word:**
  - Push `B757` and the private branch.
  - Publish the generator-10 layout, remove the generator-9 file from the layouts branch, and confirm LayoutBundle passes.
  - Deploy to `C:\MSFSBA-B757` with the layout in `%APPDATA%`.

## In-sim checks (owner, on a flight)

1. In a VNAV climb, cruise and descent, Alt+P's "target speed" matches the CDU's CLB, CRZ and DES pages' target speed (read with the CDU window), including 250 below 10,000 ft and a waypoint speed restriction.
2. The controller runs the recorder beside the app, as on 2026-11-16, and checks the tape for no gap in the target speed row in VNAV.
3. If a hold is flown in VNAV: "target speed not read: in a hold" while in it; note the CDU's holding speed for the port.

## Handoff to plan 16

- **Pilot-facing:** "Alt+P's ADI window now shows the FMC's target speed in VNAV." Changelog for plan 16 to fold into the ADI's line.
- Still open after this plan: the holding speed (R1), and the MCP speed field in Mach mode (plan 27, unseen in flight).

## Execution record

- **Status:** written 2026-11-16; waiting on the owner's review
- **Claimed by:** Robin Kipp (robin24), 2026-11-16
- **Started:** 2026-11-16, from `B757` at c4cd013e7
