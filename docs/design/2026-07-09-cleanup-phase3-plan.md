# Codebase Cleanup Phase 3 Implementation Plan (PR-8, PR-9 — the RISKY bucket)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the RISKY-bucket findings from `docs/design/2026-07-07-codebase-cleanup-design.md` as two PRs whose merges are gated on Robin's in-sim/NVDA verification — the PR bodies carry the test checklists as first-class deliverables.

**Architecture:** Two independent branches off `main`: `fix/js-agents` (PR-8, the Coherent agent fixes — jsdom tests FIRST for every behavior change the harness can reach) and `fix/behavioral-cleanup` (PR-9, behavioral C# — every announcement-text change explicitly documented for Robin's veto). Same execution machinery as Phases 1-2.

**Tech Stack:** .NET 10 / C# 13, ES5-era JS (Coherent GT / Chromium 49 — no optional chaining, no Array.flat, MutationObserver IS available), jsdom test harnesses under `tools/`, xUnit.

## Global Constraints

- `main` protected; branch + PR. Robin granted session-wide push/PR permission 2026-07-08 — push and open PRs without asking; MERGES remain Robin's after in-sim testing.
- Build `dotnet build MSFSBlindAssist.sln -c Debug` (0 warnings); suite 580/580 must stay green. jsdom suites (`tools/flypad-settings-test`, `tools/flypad-shell-test`, `tools/pmdg-efb-test`, `tools/perf-builder-test`) must stay green after every JS task; find each suite's run command by reading its folder.
- Chromium 49 constraints in all agent JS: no optional chaining, no arrow-function reliance beyond what the file already uses, match each file's existing idiom.
- CLAUDE.md invariants bind everywhere; docs/pmdg-efb.md + docs/flypad.md + docs/a380x.md rules are DELIBERATE — violating one is a Critical review finding.
- Every task that changes spoken output or page readout must list the exact before/after in its report — these aggregate into the PR body for Robin's veto.
- The ECAM latent-writer task (9.9) is investigate-then-fix-if-clean: a finding, not a mandate.
- NOT in scope (stay deferred): ND-2 GetLandingExits consolidation, structural dedup, Wave-2 tests, AC-8 (Fenix GEN1 aliasing) and AC-17 (HS787 KOHLSMAN units) — the last two are live-sim investigations listed in PR-9's checklist as notes for Robin, not code changes.

---

# PR-8: `fix/js-agents` (branch off main)

### Task 8.1: JS-1 — A380 flightInfo T/D ident order (test first)

**Files:**
- Modify: `MSFSBlindAssist/Resources/coherent-a380-agent.js` (`flightInfo()` ~:1993: `(pw[p].ident || pw[p].mcduIdent)` → check `mcduIdent` FIRST, mirroring `coherent-a32nx-flightinfo.js:50` and its comment: a cruise StepDescent pseudo-waypoint has `ident '(T/D)'` but `mcduIdent '(S/D)'`)
- Test: extend the jsdom harness that loads the a380 agent (`tools/perf-builder-test/` — read its structure; if flightInfo isn't reachable there, create `tools/a380-flightinfo-test/` mirroring the harness pattern)

- [ ] **Step 1 (test first):** Write a jsdom/node table test feeding `flightInfo` a stub `pw` array containing a step-descent pseudo-waypoint (`ident:'(T/D)', mcduIdent:'(S/D)'`) ahead of the real T/D (`ident:'(T/D)', mcduIdent:'(T/D)'`) — assert current behavior FAILS to pick the real one (red), matching the A32NX comment's described bug.
- [ ] **Step 2:** Apply the one-line order swap. Test green. All jsdom suites green.
- [ ] **Step 3:** Commit: `fix(a380-agent): check mcduIdent before ident in flightInfo — step-descent no longer masks T/D`

### Task 8.2: JS-2 — flyPad setValue disabled guard (test first)

**Files:**
- Modify: `MSFSBlindAssist/Resources/coherent-flypad-agent.js` (`setValue` ~:2376-2418: the toggle/checkbox branch and the final `clickNode` fallback lack the `disabledFor` check that `clickElement` (~:2353) has; the agent's own rule at :674-677 says disabled controls must be reported, never actuated)
- Test: `tools/flypad-settings-test/` (setValue paths are covered there — read the existing setValue tests first)

- [ ] **Step 1 (test first):** jsdom test: a disabled-styled toggle (whatever `disabledFor` keys on — read it: pointer-events-none/aria-disabled/class) + `setValue` → assert it currently ACTUATES (red-documenting), then after the fix returns `"disabled"` without firing the click.
- [ ] **Step 2:** Add `if (A.disabledFor(node)) return "disabled";` at setValue's top (position it so ALL branches are covered — read the function's flow; if the input-committing branch must stay reachable for editable fields inside disabled-looking containers, guard only the actuating branches and say so).
- [ ] **Step 3:** All jsdom suites green. Commit: `fix(flypad-agent): setValue honors disabledFor — no actuation of disabled controls`

### Task 8.3: JS-9 — isVisible exception defaults

**Files:**
- Modify: `MSFSBlindAssist/Resources/coherent-a380-agent.js` (`isVisible` ~:74 returns TRUE on exception) and `MSFSBlindAssist/Resources/coherent-ewd-agent.js` (~:43, same)

- [ ] **Step 1:** Determine per agent what the page contains: the A380 MFD scrape targets HTML (`mfd-*` classes) — fail-open there is accidental inheritance; the EWD scrape — read what node types it walks (`.StsArea`, EWD lines — SVG?). Decision rule: HTML-only page → align to fail-CLOSED (return false) like flypad/pmdg agents; SVG-containing page → KEEP fail-open but add the display-agent's explanatory comment (SVG nodes can throw on style reads).
- [ ] **Step 2:** Apply per the rule; add a one-line comment at every site stating the chosen default and why. All jsdom suites green.
- [ ] **Step 3:** Commit: `fix(agents): deliberate isVisible failure defaults — fail-closed on HTML pages, documented fail-open where SVG`

### Task 8.4: JS-3 — flyPad scrape memoization

**Files:**
- Modify: `MSFSBlindAssist/Resources/coherent-flypad-agent.js` (the two-pass scrape ~:1888-2103: per-element `isVisible` (style+rect) runs repeatedly; `containsInteractive` (~:2215) re-classifies whole subtrees; pass 2 re-runs `classify` per element; `selectInputContext` walks 8 ancestors per text leaf)

- [ ] **Step 1:** Add PER-SCRAPE memoization only — a scrape-generation counter + expando stamps (`node.__msfsbaGen`, `node.__msfsbaKind`, `node.__msfsbaVis`) so `classify`/`isVisible` compute once per node per scrape. Do NOT cache across scrapes (the page mutates between polls). Gate `selectInputContext` on the page containing a SelectInput root (one cheap querySelector before the leaf loop).
- [ ] **Step 2:** The output must be IDENTICAL: same lines, same order, same labels. The flypad jsdom suites are the equivalence net — run `tools/flypad-settings-test` + `tools/flypad-shell-test` and they must pass UNCHANGED (no golden updates allowed; a golden change = you altered behavior → fix the code, not the test).
- [ ] **Step 3:** Commit: `perf(flypad-agent): per-scrape node memoization — classify/isVisible once per node`

### Task 8.5: JS-4 — PMDG EFB dirty gate

**Files:**
- Modify: `MSFSBlindAssist/Resources/coherent-pmdg-efb-agent.js` (`collect()`/`scrape()` ~:385-395: two full-tree traversals + layout reads per 600 ms poll with no change detection)
- Modify: `MSFSBlindAssist/SimConnect/CoherentPmdgEfbClient.cs` (the poll consumer — must treat a new `{unchanged:true}` token as "keep previous result", not as an empty page)

- [ ] **Step 1:** In-page dirty gate: install ONE MutationObserver (Chromium 49 supports it) on `document.body` with `{subtree:true, childList:true, characterData:true, attributes:true}` setting a `dirty` flag; `scrape()` returns `{ok:true, unchanged:true}` when the flag is clear AND at least one full scrape has run; clears the flag before scraping. The observer must be installed defensively (re-install if the agent is re-injected; guard double-install).
- [ ] **Step 2:** C# side: read the client's poll loop; on `unchanged:true`, skip the parse/diff work and keep the last document (the existing hash-dedup downstream continues to work — this just short-circuits earlier). The FIRST poll after (re)injection must always be a full scrape.
- [ ] **Step 3:** `tools/pmdg-efb-test` suite green unchanged (if the harness calls scrape() twice on a static DOM, the second call may now return unchanged — adjust the HARNESS calls if needed, never the agent's collect logic; document what you did).
- [ ] **Step 4:** Build 0 warnings + 580 green (C# change). Commit: `perf(pmdg-efb-agent): MutationObserver dirty gate — skip full-tree scrape on unchanged page`

### Task 8.6: JS-5 + JS-10 remainder — A380 combo leaf index + small items

**Files:**
- Modify: `MSFSBlindAssist/Resources/coherent-a380-agent.js` (`comboSelectedValue` ~:1079-1110: two full-subtree scans with rect reads per dropdown per poll → build ONE `(text, rect)` leaf index per `enumerateLines` pass, shared across all combo calls in that pass; `buildPerfLines`+`buildLabeledFields` each re-query `.mfd-label-value-container` → share one query result)
- Modify: `MSFSBlindAssist/Resources/coherent-pmdg-efb-agent.js` (`A.activeUnit` ~:231-242 re-implements the `settingsObj()` window→bare-global fallback inline → call `A.settingsObj()`; the double `var w` declaration in `collect()` ~:711/:780 → rename the second)

- [ ] **Step 1:** Apply all four. The leaf index must produce identical `comboSelectedValue` outputs (the geometry pairing was live-verified per the file's comments — the index is the same data, gathered once). `tools/perf-builder-test` green unchanged.
- [ ] **Step 2:** Explicitly SKIPPED from JS-10 (flag in PR body, do not implement): `buildFuelLines` O(n²) (Fuel-page-scoped, 600 ms, low value vs risk) and the nbsp normalization difference (page-driven, informational).
- [ ] **Step 3:** All jsdom suites green. Commit: `perf(a380+pmdg agents): shared leaf index for combos, shared container query, settingsObj reuse, var shadow fix`

### Task 8.7: PR-8 wrap (controller)

- [ ] Whole-PR cross-task review (8.4 and 8.2 touch the same flypad file; 8.1/8.3/8.6 the same a380 file). All jsdom suites + build + 580. Push, open PR with this in-sim/NVDA checklist in the body:
  1. A380 F-PLN with a STEP ALT entered: D/Shift+D reads the REAL T/D, not the step descent.
  2. flyPad (A320 + A380): Ground/Fuel/Payload/Settings pages read correctly under NVDA; a disabled control reports "disabled" and does NOT actuate; page updates (fuel loading progress) still announce.
  3. PMDG 737/777 EFB (Shift+T): pages read correctly; a value-only change (e.g. Ground Ops progress) is still picked up within a poll or two (dirty-gate must not eat value mutations).
  4. A380 MFD ARRIVAL page: RWY/APPR/STAR/TRANS/VIA dropdown selections read correctly (leaf-index change).
  5. A380 EWD/ECAM readout unchanged.

---

# PR-9: `fix/behavioral-cleanup` (branch off main)

### Task 9.1: AC-2 — PMDG 777 IsReady gate

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs` (the four switch-dispatch regions the audit flagged (~:5837-5853, 5990-6006, 6039-6046, 6084-6098 pre-Phase-2 numbering — locate by the `(int)dm.GetFieldValue(...) == target` guard pattern with no IsReady check)

- [ ] **Step 1:** Read `PMDG737Definition`'s equivalents (~:4224-4228 etc.): the 737 checks `dm == null || !dm.IsReady` and announces "Switch not ready — waiting for aircraft data" (read the EXACT string). Mirror that gate + the exact same announcement text at each 777 site. Before the first CDA snapshot, `GetFieldValue` returns the 0.0 sentinel — the gate prevents dispatching against fictitious positions.
- [ ] **Step 2:** Build + 580 green. Report the exact announce string added. Commit: `fix(pmdg777): IsReady gate on switch dispatch — no writes against the pre-snapshot 0.0 sentinel`

### Task 9.2: AC-4 + AC-5 — A32NX announcement-rule fixes

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs` (AC-4: the `AUTOBRAKE_MODE` combo handler announces its own selection (`announcer.Announce($"{varDef.DisplayName} set to {varDef.ValueDescriptions[value]}")`) — a combo echo the CLAUDE.md rules forbid, and `ValueDescriptions[value]` throws on unmapped values; AC-5: the ECP LED state-change handler uses `AnnounceImmediate` where the convention says state changes queue via `Announce`)

- [ ] **Step 1 (AC-4):** Delete the combo's self-announce (the screen reader already announces the selection; the `_uiSetEcho` wrap covers the SimVar echo). Where the surrounding code still needs the value lookup, use `TryGetValue` with a fallback. Verify the separately-keyed `A32NX_AUTOBRAKES_ARMED_MODE` monitor still announces BACKGROUND changes (that's the desired behavior — read its def).
- [ ] **Step 2 (AC-5):** `AnnounceImmediate` → `Announce` at the ECP LED site (~:7509 pre-Phase-2 numbering).
- [ ] **Step 3:** Build + 580 green. Report before/after announcement behavior precisely. Commit: `fix(a32nx): autobrake combo stops self-announcing; ECP LED changes queue`

### Task 9.3: AC-6 + AC-7 — HS787 state-aware combos + A380 RMP uniquifier

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/HorizonSim787Definition.UiAndHotkeys.cs` (AC-6, ~:31-35/:65-69: `HS787_APMaster`/`HS787_ATStatus` fire toggle events UNCONDITIONALLY from state-target combos — re-selecting the current value INVERTS the system; every sibling handler is state-aware via `!= if{}` calc guards — read the ParkBrake handler ~:221-227 as the reference pattern)
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA380Definition.Rmp.cs` (AC-7, ~:62/:70: the press/release H-event pair lacks the mandated `{seq} 0 *` uniquifier that `SendRmpKey` (~:28) has — MobiFlight coalesces consecutive identical calc strings)

- [ ] **Step 1 (AC-6):** Wrap each toggle fire in the sibling pattern: compare target vs current state (read how the current state is available — L:var read or cached value) and fire only on mismatch. The comparison source must be the same one the siblings use.
- [ ] **Step 2 (AC-7):** Apply the same `{seq} 0 *` prefix mechanism `SendRmpKey` uses to the press/release calls (read `SendRmpKey` and reuse its sequence counter — do not create a second counter).
- [ ] **Step 3:** Build + 580 green. Commit: `fix(hs787+a380): state-aware AP/AT combos; RMP press/release calc strings uniquified`

### Task 9.4: FM-5 — TaxiAssistForm validation announce normalization (Robin's decision)

**Files:**
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs` (the ~10 queued `Announce` validation-error sites on the Calculate path (~:1350, 1863, 1894, 1910, 1936, 1954, 1965, 2050, 2056, 2091 pre-Phase-2 numbering) → `AnnounceImmediate`, matching the 3 sites that already use it)

- [ ] **Step 1:** Locate every validation-error announce on the Calculate/user-action path (grep `Announce(` in the file; classify each: user-action validation feedback → immediate; genuine background state → stays queued). List the classification per site in the report.
- [ ] **Step 2:** Build + 580 green. Commit: `fix(taxi-form): validation errors announce immediately (owner decision 2026-07-07)`

### Task 9.5: SV-4 (VG half) — VisualGuidanceManager UtcNow

**Files:**
- Modify: `MSFSBlindAssist/Services/VisualGuidanceManager.cs` (every `DateTime.Now` pair — including the vertical-PD `deltaTime` derivation (~:1165-1175 pre-Phase-2) and the callout/grace stamps (~:253-257, 808, 1293-1384))

- [ ] **Step 1:** Same pair discipline as Phase 2's Task 5.3: enumerate every `DateTime.Now`, map each field's write+read pairs, swap complete pairs only, flag any cross-file pair. The PD `deltaTime` swap is THE risky one (guidance-math input) — it must land, that's the point of this task; note it prominently for the in-sim checklist (fly one full approach).
- [ ] **Step 2:** Build + 580 green. Commit: `fix(visual-guidance): monotonic UtcNow timing — PD deltaTime immune to clock changes`

### Task 9.6: AC-15 (Thread.Sleep) — A320 non-blocking set paths

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs` (the `Thread.Sleep(100)` in the COM set path (~:7692 pre-Phase-2) and `Thread.Sleep(50)` in `SetFCUAltitudeValue` (~:8345) — both on the UI thread; the file already owns the non-blocking idiom (`DeferReadback` ~:8288 — read it))

- [ ] **Step 1:** Read each sleep's purpose (write-ordering delay before a readback/second write?). Replace with the `DeferReadback` idiom preserving the SAME delay duration and the SAME subsequent action. If a sleep guards WRITE-then-WRITE ordering (not readback), use the deferred-action equivalent with the same ms — behavior timeline preserved, UI thread unblocked.
- [ ] **Step 2:** Build + 580 green. Commit: `fix(a32nx): non-blocking deferred actions replace UI-thread sleeps in COM/FCU set paths`

### Task 9.7: SC-12 — hotkey readout defs registered once

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.DataRequests.cs` (the 13 near-identical readout methods (`RequestAltitudeAGL/MSL/AirspeedIndicated/True/GroundSpeed/VerticalSpeed/Mach/Bank/Pitch/OutsideTemp/Squawk/HeadingMag/HeadingTrue` + `RequestSingleValue`) — each clears + re-registers a static data def with a 50 ms `DoEvents` pump per press), `SimConnectManager.Setup.cs` (`SetupDataDefinitions` — the once-per-connection registration home)

- [ ] **Step 1:** Read the current mechanics: each method's def ID, simvar name, unit, datum type. Build a static table `(defId, simVar, unit)`; register ALL of them ONCE in `SetupDataDefinitions` (AFTER the fixed/critical defs, per the architecture rule; ~13 defs — check the approxTotalDefs budget note in registration.log guidance, they were transiently registered before so net budget impact ≈ 0 permanent +13 worst case).
- [ ] **Step 2:** Each request method collapses to: `RequestDataOnSimObject(reqId, defId, ..., ONCE)` — no clear, no re-add, no DoEvents pump. Table-driven single helper + thin wrappers (keep public signatures).
- [ ] **Step 3:** The clear-first pattern was a crash-avoidance measure (FSDeveloper note in the code — read it): the crash risk existed because defs were RE-ADDED with different content on a shared ID; registering once with fixed content removes the re-add entirely, which is safer, not riskier — make this argument concretely in the report after reading the original note. Aircraft-switch path: verify these defs survive `ReregisterAllVariables`/reconnect or are re-registered there.
- [ ] **Step 4:** Build + 580 green. Commit: `perf(simconnect): hotkey readout defs registered once — no per-press clear/DoEvents/re-register cycle`

### Task 9.8: AC-11 wording — PMDG harmonization + TryParseSpeedInput port + LAND-speech fix

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`, `MSFSBlindAssist/Aircraft/PMDG737Definition.cs` (readout wording drift: 777 "V1 150 knots" vs 737 "V1 150"; 777 "Heading 250" vs 737 "MCP heading 250"; fuel readout ordering differs; port the 737's `TryParseSpeedInput` (M-prefix Mach support, ~:5904-5921 pre-Phase-2) to the 777's speed-input path (~:7034-7065))
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs` (LAND-capability speech drift: three copies say "LAND3 dual" vs "LAND 3 dual" — after Phase-2 consolidation check how many remain; unify to "LAND 3 dual" (spaced — matches Airbus phraseology))

- [ ] **Step 1 (harmonization direction — DOCUMENT EVERY CHOICE):** For each drifted readout pick ONE form and apply to both aircraft: V-speeds → WITH units ("V1 150 knots") on first mention (clarity for a blind pilot); heading → "MCP heading 250" (states the source, matches the 737); fuel ordering → the 737's order (total first — verify by reading both). These are DEFAULTS FOR ROBIN'S VETO — the PR body lists each before/after pair; he tests by ear.
- [ ] **Step 2 (TryParseSpeedInput port):** Move/copy the 737's parser so the 777 speed dialog accepts M-prefix Mach entries identically; if Phase-2 left both defs' dialogs sharing base helpers, put the parser in Base.
- [ ] **Step 3:** Build + 580 green. Report: the full before/after wording table. Commit: `fix(pmdg): harmonized readout wording (owner to verify by ear); 777 speed dialog accepts Mach entries; LAND 3 speech unified`

### Task 9.9: ECAM latent-writer investigation (fix-if-clean)

**Files:**
- Investigate: `MSFSBlindAssist/SimConnect/SimConnectManager.cs` (fields `ecamMasterWarning`/`ecamMasterCaution`/`ecamStallWarning` — declared, read by `VarCache.cs` into `ECAMDataEventArgs`, written by NOTHING since before Phase 1), the A32NX ECAM window consumer (find via `ECAMDataEventArgs` usages), and the A32NX master-warning vars that DO flow through the batch (`A32NX_MASTER_WARNING`/`A32NX_MASTER_CAUTION` — check GetVariables)

- [ ] **Step 1:** Establish what the consumer DOES with the flags (does the ECAM window display a master-warning indicator that has been silently dead?). If the same information already reaches the user via the announced monitor vars, the flags may be vestigial → propose deletion instead of wiring.
- [ ] **Step 2:** If wiring is clean (set the fields where the corresponding batch vars are processed — e.g. inside the batch path keyed on the exact var names), implement it; if ambiguous, write the findings to the report and make NO code change.
- [ ] **Step 3:** Build + 580 green. Commit (if changed): `fix(a32nx): wire ECAM master-warning flags from live monitor vars (dead since the 400-499 branch went unreachable)` — otherwise document-only, no commit.

### Task 9.10: PR-9 wrap (controller)

- [ ] Whole-PR cross-task review (9.2/9.6/9.8 share FlyByWireA320Definition.cs; 9.1/9.8 share PMDG777Definition.cs). Build + 580. Push, open PR with the full checklist:
  1. PMDG 777: flip several overhead switches BEFORE the CDA snapshot arrives (immediately after loading) — "not ready" announcement, no misfires; then normally after.
  2. A32NX: change autobrake via the combo — NVDA announces the combo selection ONCE (no app echo, no triple-announce); a background autobrake change (autobrake disarm on landing) still announces.
  3. HS787: re-select the CURRENT AP master / AT value in the combo — nothing toggles; select the other value — toggles.
  4. A380 RMP: double-press the same key twice quickly — both presses register.
  5. TaxiAssistForm: trigger validation errors — announced immediately.
  6. Visual guidance: one full approach — tones/callouts behave identically (PD deltaTime now UtcNow).
  7. A32NX: COM frequency set + FCU altitude set — values stick, readback announcements unchanged, UI stays responsive.
  8. Hotkey readouts (altitude/speed/heading/etc.): spam them rapidly, then switch aircraft and use them again — correct values, no crash (the once-registered defs).
  9. PMDG wording: listen to the harmonized readouts (V-speeds/heading/fuel) — veto any wording per the PR body's table.
  10. 777 speed dialog: enter "M.82" style values — accepted like the 737.
  11. Investigation notes for a live session (no code in this PR): Fenix "Emergency Gen 1 Line" combo aliases GEN1's L:var (AC-8); HS787 KOHLSMAN_SET sends inHg×16 where the stock event expects millibars×16 (AC-17) — check baro readback correctness.

## Self-review record

- Spec coverage: PR-8 = JS-1,2,3,4,5,9,10(scoped) ✓ (buildFuelLines + nbsp skipped, documented in 8.6); PR-9 = AC-2,4,5,6,7,11(wording+port),15(sleep), FM-5, SV-4(VG), SC-12, ECAM investigation ✓. AC-8/AC-17 correctly checklist-notes only.
- No placeholders; every step names files/symbols/patterns; decisions with veto-points explicitly marked.
- Type consistency: no cross-task signatures introduced beyond 8.5's `{ok:true, unchanged:true}` token consumed in the same task.
