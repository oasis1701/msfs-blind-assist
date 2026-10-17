# PMDG 737 speedbrake + gear OFF — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Act on what a live-sim probing session against the user's PMDG 737-800 proved today — collapse the speedbrake arm ladder to the one rung that works, and stop the After Takeoff flow claiming it moved a gear lever that cannot be moved externally.

**Architecture:** Both changes are confined to the PMDG 737 First Officer profile (`MSFSBlindAssist/FirstOfficer/PMDG737/`) plus its docs. No shared machinery is touched.

**Tech Stack:** C# 13 / .NET 10, Windows Forms, xUnit. No new dependencies.

## Evidence this plan rests on (measured live, not inferred)

Probed against the user's PMDG 737-800 in flight, writing with `tools/PMDGDispatchTester`
and reading with the SimConnect MCP's `get_pmdg_var`.

**Speedbrake ARM — rung 1 is sufficient.** `CDA + MOUSE_FLAG_LEFTSINGLE` on
`EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM` (id 76424) armed it on the first attempt:
`MAIN_annunSPEEDBRAKE_ARMED` went `false → true` and the user heard it. Transmit +
LEFTSINGLE arms it too, and the DOWN sub-event disarms it the same way. The 3-rung
escalation therefore never needs rungs 2 and 3 on a healthy aircraft.

**Gear lever OFF — not achievable externally.** `MAIN_GearLever` (0=UP, 1=OFF, 2=DOWN) is
a LIVE, trustworthy field: it read `2` the moment the user moved the lever by hand.
Eighteen distinct write shapes across four transports were all inert:

- CDA + plain param 0/1/2, and CDA + LEFTSINGLE, on both `EVT_GEAR_LEVER` (70087) and `EVT_GEAR_LEVER_OFF` (74183)
- `TransmitClientEvent` + plain param 0/1/2, and + LEFTSINGLE / RIGHTSINGLE, on both
- `EVT_GEAR_LEVER_UNLOCK` (74184) pulsed and held before the move, both transports
- An L:var write to `switch_455_73X`, which **accepted** the value (read back `1.0`) while `MAIN_GearLever` stayed `0` — a dead output mirror
- The `ROTOR_BRAKE` encoded-parameter channel (see below): `455101`, a `455101`+`455104` press/release pair, and `45501`

**The trap that fooled us both:** transmit + mouse-flag on `EVT_GEAR_LEVER` produces an
**audible click** while the lever does not move. Sound is not actuation. Do not accept a
click as evidence.

**Both reference add-ons fail the same way.** Talking Flight Monitor's PMDG support is
FSX/NGX-only (zero NG3 references in its binary). FSFO's `Gear;OFF` handler sends the
stock `GEAR_UP` first — a loud, audible gear retraction — waits 1.5 s, then fires an
inaudible click, and speaks its "Gear" callout regardless; its own NG3 vocabulary lists
`Gear;Up,Down` with no OFF, and **the user confirmed FSFO's checklist hangs on that
item**. UP and OFF are indistinguishable by ear, which is why it convinces.

**A third transport was discovered and verified** (not used by this plan, but recorded in
Task 3): FSFO drives PMDG switches through the *stock* `ROTOR_BRAKE` K-event (66587) with
`param = (pmdgEventId - 69632) * 100 + mouseCode`. Proven live: `679201` armed the
speedbrake, `679101` disarmed it, via plain `TransmitClientEvent`. It does not rescue the
gear lever.

## Global Constraints

- Build the SOLUTION, never the bare csproj: `dotnet build MSFSBlindAssist.sln -c Debug`. A bare `dotnet build` on `MSFSBlindAssist\MSFSBlindAssist.csproj` silently defaults to `Platform=AnyCPU` and writes to a different folder than the x64 run path, so it reports success while the running exe never updates.
- Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
- Suite baseline at the start of this plan: **3859 passed, 0 failed.**
- Known pre-existing warnings: TaxiGraph.cs CS8601 ×2, GsxServiceAnnouncerDiagnosticsTests xUnit2029 ×3, GsxServiceStateTests xUnit2029 ×1. Anything beyond those six is a finding.
- The exe is file-locked while MSFSBA runs (MSB3021) — the user may have it open; report rather than killing it.
- **Screen-reader rule:** never announce a direct UI interaction. These changes add no new announcements; the gear step's speech comes from the existing Captain-callout mechanism.
- Branch `feature/first-officer`, PR #160. Never commit to `main`. Do NOT push.
- Every commit message ends with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

---

## File Structure

| File | Responsibility | Task |
|------|----------------|------|
| `MSFSBlindAssist/FirstOfficer/PMDG737/SpeedbrakeArmLadder.cs` | the attempt list | 1 |
| `tests/MSFSBlindAssist.Tests/SpeedbrakeArmLadderTests.cs` | existing 6 facts; update expectations | 1 |
| `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs` | After Takeoff gear step | 2 |
| `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737ChecklistDefinitions.cs` | After Takeoff gear item | 2 |
| `MSFSBlindAssist/FirstOfficer/PMDG737/AircraftActionExecutor.cs` | `SetGearLever` becomes dead | 2 |
| `CLAUDE.md`, `docs/pmdg-737.md`, `changelog.d/160-*.md` | invariants + release note | 3 |

---

## Task 1: Collapse the speedbrake arm ladder to its one working rung

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/SpeedbrakeArmLadder.cs`
- Modify: `tests/MSFSBlindAssist.Tests/SpeedbrakeArmLadderTests.cs`

**Interfaces:** Consumes nothing. `SpeedbrakeArmLadder.Attempts` shrinks from 3 entries to 1; `AircraftActionExecutor.ArmSpeedbrakeAsync` iterates it and needs no change.

**Design note.** Keep the ladder *structure* — the loop, the read-back, `ShouldContinue`, the DO-NOT-ARM bail-out and the already-armed/already-extended guard. Only the `Attempts` list changes. The escalation existed because we could not tell which transport worked; now we can, and rungs 2 and 3 only ever cost the pilot time on an aircraft where rung 1 already failed for a real reason (a faulted system, which `DoNotArmField` catches). The read-back proof is what makes the step trustworthy and it stays.

Do **not** delete the `SpeedbrakeArmTransport` enum members `TransmitClick` and `TransmitPressRelease` — both are proven-working transports for this control (transmit+LEFTSINGLE arms it too), the enum documents the shapes, and a future control may need them.

---

- [ ] **Step 1: Read the existing tests and the ladder**

Read `tests/MSFSBlindAssist.Tests/SpeedbrakeArmLadderTests.cs` (6 facts) and `SpeedbrakeArmLadder.cs` in full before changing anything. Some facts almost certainly assert the three-rung sequence or `ShouldContinue` behaviour across indices 0..2. Those are the ones to update — they are pinning a design decision that measurement has now superseded, not a defect.

- [ ] **Step 2: Update the tests to the new expectation (red)**

Change the facts so they pin: `Attempts` contains exactly one entry, and it is `SpeedbrakeArmTransport.CdaClick`. Keep every fact that pins behaviour independent of the list length (the DO-NOT-ARM bail-out, the already-armed guard, `ShouldContinue` returning false at the final index).

Add one fact recording *why*, so a future reader does not "restore" the escalation:

```csharp
    // Live-verified against a PMDG 737-800 in flight (2026-08-25): a single
    // CDA + MOUSE_FLAG_LEFTSINGLE on EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM armed the
    // lever on the first attempt (MAIN_annunSPEEDBRAKE_ARMED false -> true, audible to
    // the pilot). The escalation existed only because we could not tell which transport
    // worked; rungs 2 and 3 now only ever spend the pilot's time on an aircraft where
    // rung 1 failed for a real reason, which DoNotArmField already catches.
    [Fact]
    public void TheLadderIsASingleProvenRung()
    {
        Assert.Equal(new[] { SpeedbrakeArmTransport.CdaClick }, SpeedbrakeArmLadder.Attempts);
    }
```

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SpeedbrakeArmLadderTests"`
Expected: the new/updated facts FAIL against the current 3-entry list. Report the actual failure output.

- [ ] **Step 3: Collapse the list (green)**

In `SpeedbrakeArmLadder.cs`, reduce `Attempts` to `{ SpeedbrakeArmTransport.CdaClick }` and rewrite its doc comment to state the measurement (date, field, observed transition) rather than "cheapest and most-likely first".

Re-run the filtered tests. Expected: all pass.

- [ ] **Step 4: Full verification**

Run `dotnet build MSFSBlindAssist.sln -c Debug` — expect 0 errors and exactly the six known warnings.
Run the FULL suite — expect 3859 passed, 0 failed (the count may shift by the one fact you added; report the real number).

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/PMDG737/SpeedbrakeArmLadder.cs tests/MSFSBlindAssist.Tests/SpeedbrakeArmLadderTests.cs
git commit -m "fix(fo): collapse the 737 speedbrake ladder to the rung that works

Live-verified on a PMDG 737-800 in flight: one CDA + LEFTSINGLE click on
EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM arms the lever first time. The
escalation existed only because we could not tell which transport worked,
so rungs 2 and 3 only ever cost the pilot time on an aircraft where rung 1
failed for a real reason - which the DO NOT ARM annunciator already catches.
The read-back proof that makes the step trustworthy is unchanged.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: Stop claiming to move a gear lever that cannot be moved

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs` (After Takeoff, ~line 348)
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737ChecklistDefinitions.cs` (~line 286)
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/AircraftActionExecutor.cs` (~line 812)

**Interfaces:** Consumes Task 1's work only in the sense of sharing a commit series. Produces no new API.

**Design.** Use what *does* work. `MAIN_GearLever` reads reliably, so the pilot still gets an automatic tick the moment they move the lever — this is strictly better than a bare reminder, and far better than today's false success.

---

- [ ] **Step 1: Flow step becomes a Captain callout**

In `PMDG737FlowDefinitions.cs`, the After Takeoff flow currently has:

```csharp
            SW("AT_GEAR_OFF", "Gear lever: OFF", "EVT_GEAR_LEVER", 1),
```

Replace with a `Captain` step (the helper is already used in this file, e.g. `Captain("DS_DATA", …)`), carrying a comment that records the finding:

```csharp
            // CAPTAIN ITEM, and it must stay one: the NG3 gear lever cannot be positioned
            // by an external client. Live-probed 2026-08-25 against a real 737-800 — 18
            // write shapes across four transports (CDA and TransmitClientEvent, plain
            // params and mouse flags, on EVT_GEAR_LEVER and EVT_GEAR_LEVER_OFF, with and
            // without EVT_GEAR_LEVER_UNLOCK, plus the switch_455_73X L:var and the
            // ROTOR_BRAKE encoded channel) were ALL inert, while MAIN_GearLever proved
            // live by tracking the pilot's own hand movement of the lever.
            // Sending EVT_GEAR_LEVER as a SetSwitch reported success every time and moved
            // nothing, so the checklist stood complete for a lever still at UP.
            // TRAP: transmit + mouse-flag on EVT_GEAR_LEVER makes an AUDIBLE CLICK without
            // moving the lever. Do not accept a click as proof.
            Captain("AT_GEAR_OFF", "Gear lever: OFF"),
```

Match the exact `Captain(...)` signature used elsewhere in this file — read a neighbouring call rather than assuming.

- [ ] **Step 2: Checklist item keeps detection, loses the dead action**

In `PMDG737ChecklistDefinitions.cs`:

```csharp
            Auto("ATKO_GEAR_OFF", "AFTER_TAKEOFF", "Gear lever: OFF", "MAIN_GearLever", v => v > 0.5 && v < 1.5,
                (e, _) => e.SetGearLever(1)),
```

becomes detection-only — same id, same label, same condition, `action: null`:

```csharp
            // Detection-only: MAIN_GearLever is live and trustworthy (it tracks the
            // pilot's own hand movement), but no external write can position this lever —
            // see the AT_GEAR_OFF comment in PMDG737FlowDefinitions. Ticking this item
            // must therefore not fire an action that silently does nothing; it auto-ticks
            // when the pilot actually moves the lever to OFF.
            Auto("ATKO_GEAR_OFF", "AFTER_TAKEOFF", "Gear lever: OFF",
                "MAIN_GearLever", v => v > 0.5 && v < 1.5, action: null),
```

Use the same `Auto(...)` overload shape as neighbouring detection-only items in this file (e.g. `ATC_GEAR` at ~line 525 uses `action: null`) — read one before writing.

Leave `ATC_GEAR` ("Landing gear: UP and OFF", After Takeoff Checklist) untouched: it is already detection-only and its `v < 1.5` accepts both UP and OFF, which is correct.

- [ ] **Step 3: Remove the now-dead `SetGearLever`**

`AircraftActionExecutor.SetGearLever` was called only from the item you just changed. Delete it. Its comment (`// 0=UP,2=DOWN`) is also wrong — the field is 0=UP, 1=OFF, 2=DOWN — so leaving it would mislead the next reader into retrying this.

Verify with a repo-wide search that the 737 executor's `SetGearLever` has no other caller before deleting. **Be careful:** the 777 executor (`MSFSBlindAssist/FirstOfficer/AircraftActionExecutor.cs`) and the iFly executor have their own same-named methods that ARE used — do not touch those.

- [ ] **Step 4: Verify**

Run `dotnet build MSFSBlindAssist.sln -c Debug` — 0 errors, six known warnings.
Run the FULL suite — no regressions.

There is no unit test for these definitions (they are data consumed by sim-facing code); the verification is the build plus the in-sim plan in Task 3.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737ChecklistDefinitions.cs MSFSBlindAssist/FirstOfficer/PMDG737/AircraftActionExecutor.cs
git commit -m "fix(fo): the 737 gear lever OFF is a Captain item, not a silent no-op

Live-probed against a real 737-800: the NG3 gear lever cannot be positioned
by any external write. 18 shapes across four transports were inert, while
MAIN_GearLever proved live by tracking the pilot's own hand movement. The
flow had been sending EVT_GEAR_LEVER param 1, which reports success and
moves nothing, so the checklist stood complete for a lever still at UP.

The step is now a Captain callout and the checklist item keeps its live
detection with no action, so it ticks itself when the pilot moves the lever
and never claims it moved on its own.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 3: Record the findings so they are never re-litigated

**Files:**
- Modify: `CLAUDE.md` (PMDG 737 invariants)
- Modify: `docs/pmdg-737.md`
- Create: `changelog.d/160-737-gear-off-speedbrake.fix.md`

**Interfaces:** Consumes nothing.

**Why this task is not optional.** This session burned a long live-sim slot and two false conclusions to establish these facts. Without them written down, the next reader sees an `EVT_GEAR_LEVER_OFF` event sitting unused in the id table and tries exactly what we tried.

---

- [ ] **Step 1: Confirm the PR number**

Run `gh pr view --json number,url`. Expect `160`. Do not infer it — this repo draws issue and PR numbers from one shared sequence. If `gh` fails, report BLOCKED rather than guessing.

- [ ] **Step 2: `docs/pmdg-737.md`**

Add a section covering, in this order:

1. **Speedbrake ARM** — the working shape (CDA + LEFTSINGLE on id 76424), that transmit + LEFTSINGLE also works, and that the ladder is deliberately one rung with a read-back.
2. **Gear lever OFF is not externally settable.** Give the full ruled-out matrix as a table (the 18 shapes listed in this plan's Evidence section), state that `MAIN_GearLever` is live and trustworthy, and record the audible-click trap explicitly.
3. **Why the reference add-ons appear to do it** — TFM is FSX/NGX-only; FSFO fires stock `GEAR_UP` first (audible retraction) then an inaudible click, speaks its callout regardless, lists `Gear;Up,Down` in its own NG3 vocabulary, and its checklist hangs on the item (user-confirmed). UP and OFF sound identical.
4. **The `ROTOR_BRAKE` encoded channel** — document it as a discovered, *live-verified* third transport: stock event 66587, `param = (pmdgEventId - 69632) * 100 + mouseCode`, mouse codes 01 left-single / 02 right / 04 left-release / 07 wheel-up / 08 wheel-down (01 and the general scheme verified; the others inferred from FSFO's usage). Record the proof: `679201` armed the speedbrake and `679101` disarmed it via plain `TransmitClientEvent`. Note it needs no third-party event registration, and that it does **not** rescue the gear lever. Note also the `switch_<eventOffset>_73X` L:var family FSFO reads state from, and that `switch_455_73X` is a dead output mirror for writes.

- [ ] **Step 3: `CLAUDE.md`**

Add condensed guardrails to the PMDG 737 invariants, in the file's existing one-line style with the `→ [pmdg-737.md](docs/pmdg-737.md)` pointer convention. Cover:

- The 737 gear lever OFF cannot be set externally; the step is a Captain item and the checklist entry is detection-only — never re-add a write, and never accept the audible click as proof.
- The speedbrake ladder is one proven rung (CDA + LEFTSINGLE) plus a read-back; do not restore the escalation.
- The `ROTOR_BRAKE` encoded channel exists and is verified, with the formula — so a future reader finds it before rediscovering it.

- [ ] **Step 4: Changelog fragment**

Create `changelog.d/160-737-gear-off-speedbrake.fix.md` — one paragraph, markdown prose, no heading, written for a pilot rather than a reviewer. It should say that the 737 First Officer no longer claims to have moved the gear lever to OFF (a position the simulator does not let any outside program set, so it is now called out for you to do and ticks itself when you do it), and that arming the speedbrake is quicker because it now uses the method proven to work rather than trying three in turn.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md docs/pmdg-737.md changelog.d/160-737-gear-off-speedbrake.fix.md
git commit -m "docs: record the 737 gear-lever and speedbrake findings

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 6: Hand back the in-sim test plan**

Do not push. Report these for the PR body:

- **Speedbrake:** on approach, run the Landing flow. "Speedbrake: ARMED" should arm on the first attempt with no perceptible delay, and the SPEED BRAKE ARMED light should confirm it.
- **Gear OFF:** after takeoff, run the After Takeoff flow. It should call "Gear lever: OFF" as a Captain action and must NOT announce it as done. Move the lever to OFF by hand — the checklist item must then tick itself. Leave it at UP and the item must stay un-ticked.
- **No regression:** the After Takeoff Checklist item "Landing gear: UP and OFF" should still tick with the lever at either UP or OFF.
