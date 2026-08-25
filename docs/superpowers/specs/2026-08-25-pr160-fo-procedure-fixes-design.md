# PR #160 First Officer procedure fixes — design

**Date:** 2026-08-25
**Branch:** `feature/first-officer` (PR #160, head on the `fork` remote)
**Scope:** Four independent, owner-reported defects in the First Officer flows and
checklists — two procedural (wrong wording, wrong phase), two functional (a control that
never actuates, a step that skips itself).

---

## 1. Fenix A320 + FBW A32NX — descent preparation wording

### Problem

The Descent group carries two reminders:

| id | text |
|----|------|
| `DC_ARRPERF` | Calculate arrival performance on the EFB |
| `DC_MCDU` | Complete the MCDU approach page and minimums before top of descent |

Three faults, reported by the repo owner:

1. **The EFB is not used for landing calculations on the A320.** Arrival performance comes
   off the MCDU PERF APPR page, which computes VAPP from the QNH / temperature / wind /
   minimums the crew enters. Pointing the pilot at the EFB sends them somewhere that has no
   answer.
2. **"before top of descent" contradicts where the item lives.** Neither A320 profile has a
   CRUISE group — the Descent group *is* the descent-preparation group, run before TOD.
   Reading "before top of descent" while already descending is confusing.
3. The two items describe one job split across two lines.

The strings are byte-identical in the Fenix and FBW A32NX profiles (the two were written as
copies), so both are in scope.

### Design

Delete `DC_ARRPERF` from the checklist group and the flow. Retitle `DC_MCDU`:

> Descent preparation: MCDU PERF APPR set — QNH, temperature, wind and minimums; landing
> configuration reviewed

Both remain `Reminder` (checklist) / `Captain` (flow) — nothing here is automatable, and the
landing autobrake stays a Captain item under the project-wide rule.

### Files

- `MSFSBlindAssist/FirstOfficer/Fenix/FenixChecklistDefinitions.cs` (`BuildDescent`)
- `MSFSBlindAssist/FirstOfficer/Fenix/FenixFlowDefinitions.cs` (`BuildDescent`)
- `MSFSBlindAssist/FirstOfficer/FBWA320/FbwA320ChecklistDefinitions.cs` (`BuildDescent`)
- `MSFSBlindAssist/FirstOfficer/FBWA320/FbwA320FlowDefinitions.cs` (`BuildDescent`)

### Non-goals

The A380 profile is not touched — its descent items are worded differently and were not
reported.

---

## 2. PMDG 777 — speedbrake arms too early

### Problem

The speedbrake is armed during **Approach**, in both the flow and the checklist:

- `PMDG777FlowDefinitions.cs` — `APP_SPEEDBRAKE_ARM`, in the `APPROACH_SETUP` flow
- `PMDG777ChecklistDefinitions.cs` — `APPA_SPEEDBRAKE`, in the `APPROACH` group

Approach Setup runs at the descent/approach transition, well before the landing
configuration is established. Arming there is too early.

The flow step already declares `CompletesChecklistItemId = "LDG_SPEEDBRAKE"` — an item in the
**Landing** checklist. The original author knew where the item belonged; only the dispatch
site was wrong. The 777 has no Landing flow at all, while the 737 does.

### Design

**Remove** both Approach entries. `APPROACH_SETUP` and the `APPROACH` group are each left
with their altimeters item, which is correct for that phase.

**Add** a `LANDING` flow, inserted in `Build()` between `BuildApproachSetup()` and
`BuildAfterLanding()`:

```
Id = "LANDING", Name = "Landing"
Description = "Speedbrake armed and missed approach altitude set for landing."
RelatedChecklistGroupIds = ["LANDING_CL"]
Steps:
  LD_SPEEDBRAKE_ARM  — the moved step verbatim: EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM,
                       momentary, verified on FCTL_Speedbrake_Lever in (0.5, 1.5),
                       completing LDG_SPEEDBRAKE
  LD_MISSED          — Captain reminder, "Set the missed approach altitude"
```

The 737's "Engine start switches: CONT" is deliberately **not** copied — 777 ignition is
automatic and needs no CONT selection for landing.

**Give** `LDG_SPEEDBRAKE` in `LANDING_CL` its arm action. It is currently `action: null`, so
ticking it verifies but never actuates. The ARM detent is an absolute mouse-click position,
not a toggle, so a tick while already armed is a no-op — unlike the ground-power buttons in
§4, this needs no guard.

### Rejected alternative

Adding a 777 `LANDING` checklist **group** to mirror the 737 exactly. `LANDING_CL` already
holds the speedbrake, gear and flaps items; a second one-item group would duplicate it for no
gain. The 737's LANDING/LANDING_CL split exists because its Landing group carries the
start-switch action, which the 777 does not have.

### Files

- `MSFSBlindAssist/FirstOfficer/PMDG777FlowDefinitions.cs`
- `MSFSBlindAssist/FirstOfficer/PMDG777ChecklistDefinitions.cs`

---

## 3. PMDG 737 — speedbrake does not arm

### Problem

Arming the speedbrake from the Landing flow, or by ticking the Landing checklist item, does
not reliably move the lever. Two separate faults:

**A. The actuation may be reaching a transport the NG3 ignores.** The event id is correct
(`THIRD_PARTY_EVENT_ID_MIN + 6792`, checked against the `PMDG_NG3_SDK.h` shipped in the
Community folder) and the dispatch table already forces `MOUSE_FLAG_LEFTSINGLE`. The
executor's comment records a 2026-07-03 live verification of CDA + LEFTSINGLE, but the owner
reports it not working now. The NG3 has a documented family of CDA-deaf controls
(`EVT_TCAS_MODE`, `EVT_OH_LIGHTS_POS_STROBE`, the CDU keys) that only respond to
`TransmitClientEvent` mouse-clicks; the speedbrake detents may belong to it. This cannot be
settled from the repo — it needs a live 737.

**B. A failed arm is completely invisible.** Nothing verifies the result:

| site | today | consequence |
|------|-------|-------------|
| `LDA_SPDBRK` (LANDING group) | `ActionManual` | ticks unconditionally, armed or not |
| `LDC_SPDBRK` (LANDING_CL) | `Reminder` | pilot-asserted only |
| `LD_SPDBRK` (LANDING flow) | `SW`, no verify field | reports success on dispatch |

All three carry, or descend from, the comment *"No speedbrake-lever state field exists in the
NG3 CDA struct."* That is **false**: `MAIN_annunSPEEDBRAKE_ARMED` is in
`PMDGNG3DataStruct.cs` and in the SDK header, and the executor's own comment cites it as the
field it watched during the 2026-07-03 verification. The comment is stale and must go.

### Design

#### `ArmSpeedbrakeAsync()` — closed-loop, escalating

New public method on `FirstOfficer/PMDG737/AircraftActionExecutor.cs`. Holds `_dispatchGate`
across the whole ladder and calls `DispatchCoreAsync` / the raw `SendPMDGEvent*` methods
internally — never `DispatchAsync`, which would deadlock on the gate (the established rule in
the class doc).

After each attempt it polls `MAIN_annunSPEEDBRAKE_ARMED` for up to ~1.2 s. The ambient CDA
poll is 1 Hz, so that window always contains at least one refresh.
`PMDGNG3DataManager.RequestFreshSnapshotAsync` is **not** used — it is private and documented
as unsafe for concurrent callers.

1. CDA + `MOUSE_FLAG_LEFTSINGLE` (today's path)
2. `SendPMDGEventViaTransmitWithTarget(id, MOUSE_FLAG_LEFTSINGLE)`
3. Transmit `LEFTSINGLE` → ~120 ms hold → transmit `LEFTRELEASE`
   (the shape `WarningTestAsync` already uses for CDA-deaf momentaries)

Returns `true` as soon as the annunciator confirms; `false` if no rung takes. Worst case
~4 s, which `ChecklistManager.RunCheckActionWithGraceAsync` already covers — it holds revert
until the action completes *and* the dispatch gate drains, which is what the multi-second
transponder walk relies on.

**Early exit:** if `MAIN_annunSPEEDBRAKE_DO_NOT_ARM` is lit after the first attempt, stop.
That is an auto-speedbrake fault; further clicks cannot help, and the DO NOT ARM annunciator
is already independently announced, so the pilot hears the real reason.

The ladder itself is a pure, ordered list of attempt descriptors
(`SpeedbrakeArmLadder`) so the escalation order and the DO-NOT-ARM early exit are
unit-testable without SimConnect; the executor walks that list and performs the I/O.

#### Wiring

| site | becomes |
|------|---------|
| `LD_SPDBRK` (flow) | `SW` on pseudo-key `"SPEEDBRAKE_ARM"`, intercepted in `ExecuteStepAsync` alongside `GPWS_TEST` / `TCAS_TEST`; `VerifyFieldName = "MAIN_annunSPEEDBRAKE_ARMED"`; `FailurePolicy.Skip` so a failure is announced and the flow continues |
| `LDA_SPDBRK` (LANDING group) | `AutoAsync` on `MAIN_annunSPEEDBRAKE_ARMED`, action `ArmSpeedbrakeAsync` |
| `LDC_SPDBRK` (LANDING_CL) | `Auto` on the same field, `action: null` — verify-only, matching the 777's `LDG_SPEEDBRAKE` |

The stale "no state field exists" comments are deleted at all three sites.

### Known limitation

`MAIN_annunSPEEDBRAKE_ARMED` reflects the **auto-speedbrake system being armed**, not raw
lever position, so it will not light cold-and-dark. All three items live only in the Landing
phase, where the aircraft is powered and configured, so this is acceptable — but running the
Landing checklist on the ground will leave the item un-ticked. This is a property of the
annunciator, not a defect to fix. The NG3 exposes no lever-position field at all; the analog
position is only readable through the L-var `switch_679_73X` (ARM = 100), which the FO state
evaluator cannot reach — it reads the PMDG CDA struct and synthetics only.

### Files

- `MSFSBlindAssist/FirstOfficer/PMDG737/SpeedbrakeArmLadder.cs` (new)
- `MSFSBlindAssist/FirstOfficer/PMDG737/AircraftActionExecutor.cs`
- `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs`
- `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737ChecklistDefinitions.cs`

---

## 4. PMDG 777 — Secure hits only one ground power switch

### Problem

Reported against the Secure flow, but Secure is innocent. Its two steps are correctly gated
per side:

```
Skip(Momentary("SEC_GND_PWR_PRIM", ...), s => !s.IsGpuPower1On()),
Skip(Momentary("SEC_GND_PWR_SEC",  ...), s => !s.IsGpuPower2On()),
```

The root cause is upstream, in **Electrical Power Up**, where both steps share one predicate:

```
Skip(Momentary("EPU_GND_PWR_PRIM", ...), s => s.IsAnyGpuOn()),
Skip(Momentary("EPU_GND_PWR_SEC",  ...), s => s.IsAnyGpuOn()),
```

The primary press connects primary, which makes `IsAnyGpuOn()` true, so the **secondary step
skips itself and the secondary receptacle is never connected**. Secure then correctly finds
only one side on and presses only one button.

The Electrical Power Up *checklist* item does it per-side and connects both, so the flow and
its own checklist already disagree — which is the tell.

### Design

New pure static `MSFSBlindAssist/FirstOfficer/GroundPowerGate.cs`, following the
`CenterPumpGate` idiom:

```csharp
/// The 777's two external-power buttons are momentary TOGGLES: a press is only
/// correct on a side whose current state differs from the wanted one. Pressing an
/// already-connected side DISCONNECTS it; pressing a disconnected side during a
/// power-down CONNECTS it.
public static class GroundPowerGate
{
    public static bool NeedsPress(bool sideOn, bool wantOn) => sideOn != wantOn;
    public static bool ShouldSkip(bool sideOn, bool wantOn) => !NeedsPress(sideOn, wantOn);
}
```

All six GPU skip predicates route through it:

| flow | step | predicate |
|------|------|-----------|
| Electrical Power Up | `EPU_GND_PWR_PRIM` | `ShouldSkip(s.IsGpuPower1On(), wantOn: true)` |
| Electrical Power Up | `EPU_GND_PWR_SEC` | `ShouldSkip(s.IsGpuPower2On(), wantOn: true)` |
| Before Start | `BS_GND_PWR_1` | `ShouldSkip(s.IsGpuPower1On(), wantOn: false)` |
| Before Start | `BS_GND_PWR_2` | `ShouldSkip(s.IsGpuPower2On(), wantOn: false)` |
| Secure | `SEC_GND_PWR_PRIM` | `ShouldSkip(s.IsGpuPower1On(), wantOn: false)` |
| Secure | `SEC_GND_PWR_SEC` | `ShouldSkip(s.IsGpuPower2On(), wantOn: false)` |

Before Start and Secure are behaviourally unchanged — the rewrite is so the per-side,
per-direction rule lives in one documented, tested place instead of six hand-written lambdas,
one of which was wrong.

Pressing a secondary button at an airport with no secondary supply is harmless: the button is
momentary and the receptacle simply reports nothing available, which is already what happens
to the primary today when no ground power is connected.

### Rejected alternative

Gating on `ELEC_annunExtPowr_AVAIL[n]`. More precise, but the annunciator reads NaN before
the first CDA snapshot, which would skip **both** sides and leave a cold-and-dark aircraft
with no ground power at all — a worse failure than pressing a button that does nothing.

### Files

- `MSFSBlindAssist/FirstOfficer/GroundPowerGate.cs` (new)
- `MSFSBlindAssist/FirstOfficer/PMDG777FlowDefinitions.cs`

---

## Testing

Test-driven: every automated assertion below is written and seen to fail before the
corresponding change is made.

### Automated (xUnit, `tests/MSFSBlindAssist.Tests`)

Structural assertions walking the public `Build()` accessors, in the style of
`FoShutdownSecureTighteningTests`:

- **§1** — `DC_ARRPERF` absent from the Fenix and FBW A32NX Descent group and flow;
  `DC_MCDU`'s label contains neither `"EFB"` nor `"top of descent"`, on both profiles.
- **§2** — `APPA_SPEEDBRAKE` absent from the 777 `APPROACH` group; `APP_SPEEDBRAKE_ARM`
  absent from `APPROACH_SETUP`; a `LANDING` flow exists containing `LD_SPEEDBRAKE_ARM`;
  `LDG_SPEEDBRAKE` in `LANDING_CL` has a non-null `CheckAction`.
- **§3** — `LDA_SPDBRK` and `LDC_SPDBRK` are auto-detect items whose `StateFieldName` is
  `MAIN_annunSPEEDBRAKE_ARMED`; the flow step `LD_SPDBRK` carries the same `VerifyFieldName`;
  `SpeedbrakeArmLadder` yields the three attempts in escalation order and stops after the
  first when DO NOT ARM is lit.
- **§4** — `GroundPowerGate` truth table (all four `sideOn` × `wantOn` combinations), and the
  777 `ELEC_POWER_UP` flow still contains both GPU steps.

The GPU skip predicates themselves cannot be unit-tested directly: `AircraftStateEvaluator`
takes a concrete `PMDG777DataManager` that cannot be constructed without SimConnect. That is
precisely why the decision is extracted into `GroundPowerGate`.

### In-sim (owner runs; PR body)

1. **Fenix / A32NX** — open First Officer, Descent group and Descent flow. One preparation
   item, no EFB, no "top of descent".
2. **777 approach** — run Approach Setup on descent. It must not touch the speedbrake. Run
   the new Landing flow on final: lever moves to ARM, `LDG_SPEEDBRAKE` ticks on the Landing
   checklist. Then untick and re-tick that checklist item with the lever down — it must arm.
3. **737 landing** — lever DOWN, run the Landing flow. Confirm the lever reaches ARM
   (`switch_679_73X` = 100, and the app's own speed-brake monitor announces "Speed brake
   armed"). Repeat by ticking `LDA_SPDBRK` on the Landing group. If either fails, the item
   must now **revert and report**, not tick silently.
4. **737 transport probe** — with the 737 loaded, `tools/PMDGDispatchTester`: send
   `EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM` as (a) CDA + `0x20000000`, (b) transmit +
   `0x20000000`, (c) transmit `0x20000000` then `0x00020000`, reading
   `MAIN_annunSPEEDBRAKE_ARMED` and `switch_679_73X` between each and resetting the lever to
   DOWN in between. Whichever rung fires is the one to record in the executor comment; the
   ladder can then be trimmed to it in a follow-up.
   *Do not probe with the simconnect MCP's `send_pmdg_event` — its CDA write silently fails on
   the NG3.*
5. **777 ground power** — cold and dark with ground power available: Electrical Power Up must
   connect **both** primary and secondary. Before Start must disconnect both once the APU is
   running. Secure (from a state with both connected) must press **both** buttons. Re-run
   Secure with nothing connected: both steps skip, nothing is connected by accident.

---

## Delivery

Four changelog fragments under PR #160's number:

- `changelog.d/160-a320-descent-prep-wording.improvement.md`
- `changelog.d/160-777-speedbrake-landing-flow.fix.md`
- `changelog.d/160-737-speedbrake-arm-verified.fix.md`
- `changelog.d/160-777-secondary-ground-power.fix.md`

Written for a pilot, not a reviewer.

**Push note:** this branch's upstream is `fork/feature/first-officer`
(github.com/blindflightsimmer), not `origin`.
