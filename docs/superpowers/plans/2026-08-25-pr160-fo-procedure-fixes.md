# PR #160 First Officer Procedure Fixes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix four owner-reported First Officer defects — A320 descent-prep wording, the PMDG 777 speedbrake arming an approach phase too early, the PMDG 737 speedbrake never arming (and failing silently), and the PMDG 777 secondary ground-power button skipping itself at Electrical Power Up.

**Architecture:** All four changes live in the `MSFSBlindAssist/FirstOfficer/` flow and checklist definition files, which are pure data builders reached through public static `Build()` methods. Two decisions that were previously hand-written lambdas are extracted into pure statics (`GroundPowerGate`, `SpeedbrakeArmLadder`) so they can be unit-tested without SimConnect — following the existing `CenterPumpGate` idiom. The one behavioural addition, a closed-loop 737 speedbrake arm, lives in the 737 action executor and walks the pure ladder.

**Tech Stack:** C# 13 / .NET 10, Windows Forms, xUnit. No new dependencies.

## Global Constraints

- **Build the SOLUTION, never the bare csproj:** `dotnet build MSFSBlindAssist.sln -c Debug`. A bare `dotnet build MSFSBlindAssist\MSFSBlindAssist.csproj` silently defaults to `Platform=AnyCPU` and writes to a different folder than the x64 run path.
- **Tests:** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
- **The exe is file-locked while MSFSBA runs** (MSB3021) — close the app before building.
- **Branch:** `feature/first-officer`. Upstream is `fork/feature/first-officer` (github.com/blindflightsimmer), **not** `origin`. Do not push in these tasks; the final task handles delivery.
- **Never commit directly to `main`.**
- **Landing autobrake is ALWAYS a Captain item** (project-wide rule) — no task may automate it.
- **Screen-reader rule:** never announce a direct UI interaction. No task adds an announcement.
- **Changelog fragments use PR number `160`** and are named `changelog.d/160-<slug>.<category>.md`. Content is written for a pilot, not a reviewer.
- **Existing test style:** structural FO tests walk the public `Build()` accessors — see `tests/MSFSBlindAssist.Tests/FoShutdownSecureTighteningTests.cs` for the helper shapes (`FlowStepIds`, `ChecklistItemIds`).

---

## File Structure

| File | Responsibility | Task |
|------|----------------|------|
| `tests/MSFSBlindAssist.Tests/FoPr160ProcedureFixTests.cs` | **new** — all structural assertions for tasks 1–4 | 1–4 |
| `MSFSBlindAssist/FirstOfficer/Fenix/FenixChecklistDefinitions.cs` | Fenix descent group wording | 1 |
| `MSFSBlindAssist/FirstOfficer/Fenix/FenixFlowDefinitions.cs` | Fenix descent flow wording | 1 |
| `MSFSBlindAssist/FirstOfficer/FBWA320/FbwA320ChecklistDefinitions.cs` | A32NX descent group wording | 1 |
| `MSFSBlindAssist/FirstOfficer/FBWA320/FbwA320FlowDefinitions.cs` | A32NX descent flow wording | 1 |
| `MSFSBlindAssist/FirstOfficer/GroundPowerGate.cs` | **new** — pure per-side/per-direction GPU press rule | 2 |
| `tests/MSFSBlindAssist.Tests/GroundPowerGateTests.cs` | **new** — GPU gate truth table | 2 |
| `MSFSBlindAssist/FirstOfficer/PMDG777FlowDefinitions.cs` | 777 GPU predicates; speedbrake out of Approach; new Landing flow | 2, 3 |
| `MSFSBlindAssist/FirstOfficer/PMDG777ChecklistDefinitions.cs` | 777 speedbrake out of Approach group; arm action on `LDG_SPEEDBRAKE` | 3 |
| `MSFSBlindAssist/FirstOfficer/PMDG737/SpeedbrakeArmLadder.cs` | **new** — pure escalation order + DO-NOT-ARM early exit | 4 |
| `tests/MSFSBlindAssist.Tests/SpeedbrakeArmLadderTests.cs` | **new** — ladder ordering tests | 4 |
| `MSFSBlindAssist/FirstOfficer/PMDG737/AircraftActionExecutor.cs` | `ArmSpeedbrakeAsync` + pseudo-key interception | 4 |
| `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs` | `LD_SPDBRK` verified via pseudo-key | 4 |
| `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737ChecklistDefinitions.cs` | `LDA_SPDBRK` / `LDC_SPDBRK` auto-detect | 4 |
| `changelog.d/160-*.md` | **new** ×4 — release notes | 5 |

---

## Task 1: A320 descent preparation wording (Fenix + FBW A32NX)

**Files:**
- Create: `tests/MSFSBlindAssist.Tests/FoPr160ProcedureFixTests.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixChecklistDefinitions.cs` (`BuildDescent`, ~line 292–303)
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixFlowDefinitions.cs` (`BuildDescent`, ~line 326–340)
- Modify: `MSFSBlindAssist/FirstOfficer/FBWA320/FbwA320ChecklistDefinitions.cs` (`BuildDescent`, ~line 336–350)
- Modify: `MSFSBlindAssist/FirstOfficer/FBWA320/FbwA320FlowDefinitions.cs` (`BuildDescent`, ~line 377–392)

**Interfaces:**
- Consumes: nothing.
- Produces: the shared test file `FoPr160ProcedureFixTests.cs` with class `FoPr160ProcedureFixTests` and these `private static` helpers, which tasks 2–4 append facts to:
  - `static IEnumerable<string> FlowStepIds<TState>(IEnumerable<FlowDefinition<TState>> flows, string flowId) where TState : IFoStateEvaluator`
  - `static IEnumerable<string> ChecklistItemIds<TExec, TState>(IEnumerable<ChecklistGroup<TExec, TState>> groups, string groupId) where TExec : IFoActionExecutor where TState : IFoStateEvaluator`
  - `static string FlowStepLabel<TState>(IEnumerable<FlowDefinition<TState>> flows, string flowId, string stepId) where TState : IFoStateEvaluator`
  - `static string ChecklistItemLabel<TExec, TState>(IEnumerable<ChecklistGroup<TExec, TState>> groups, string groupId, string itemId) where TExec : IFoActionExecutor where TState : IFoStateEvaluator`
  - The item id that survives is `DC_MCDU`; `DC_ARRPERF` is removed from all four files.

---

- [ ] **Step 1: Write the failing test**

Create `tests/MSFSBlindAssist.Tests/FoPr160ProcedureFixTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;

using A320Flows = MSFSBlindAssist.FirstOfficer.FBWA320.FbwA320FlowDefinitions;
using A320Checklist = MSFSBlindAssist.FirstOfficer.FBWA320.FbwA320ChecklistDefinitions;
using FenixFlows = MSFSBlindAssist.FirstOfficer.Fenix.FenixFlowDefinitions;
using FenixChecklist = MSFSBlindAssist.FirstOfficer.Fenix.FenixChecklistDefinitions;
using Pmdg777Flows = MSFSBlindAssist.FirstOfficer.PMDG777FlowDefinitions;
using Pmdg777Checklist = MSFSBlindAssist.FirstOfficer.PMDG777ChecklistDefinitions;
using Pmdg737Flows = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737FlowDefinitions;
using Pmdg737Checklist = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737ChecklistDefinitions;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Guardrails for the four owner-reported First Officer defects fixed under PR #160
/// (design: docs/superpowers/specs/2026-08-25-pr160-fo-procedure-fixes-design.md):
///   1. A320 descent preparation wording — no EFB, no "top of descent".
///   2. PMDG 777 secondary ground power skipping itself at Electrical Power Up.
///   3. PMDG 777 speedbrake arming during Approach instead of Landing.
///   4. PMDG 737 speedbrake arming with no verification.
/// Pure-logic only — every fact walks the public Build() accessors the app enumerates.
/// No SimConnect, no executor invocation.
/// </summary>
public class FoPr160ProcedureFixTests
{
    // -- helpers ----------------------------------------------------------

    private static IEnumerable<string> FlowStepIds<TState>(
        IEnumerable<FlowDefinition<TState>> flows, string flowId)
        where TState : IFoStateEvaluator =>
        flows.Single(f => f.Id == flowId).Steps.Select(s => s.Id);

    private static IEnumerable<string> ChecklistItemIds<TExec, TState>(
        IEnumerable<ChecklistGroup<TExec, TState>> groups, string groupId)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator =>
        groups.Single(g => g.Id == groupId).Items.Select(i => i.Id);

    private static string FlowStepLabel<TState>(
        IEnumerable<FlowDefinition<TState>> flows, string flowId, string stepId)
        where TState : IFoStateEvaluator =>
        flows.Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId).Label;

    private static string ChecklistItemLabel<TExec, TState>(
        IEnumerable<ChecklistGroup<TExec, TState>> groups, string groupId, string itemId)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator =>
        groups.Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId).Label;

    // -- 1. A320 descent preparation wording -------------------------------

    // The EFB has no landing-performance answer on the A320 (VAPP comes off the MCDU
    // PERF APPR page), and neither A320 profile has a CRUISE group — the Descent group
    // IS the pre-TOD preparation group, so "before top of descent" contradicted where
    // the item lives. The two reminders were one job split across two lines.

    [Fact]
    public void Fenix_DescentPrep_IsOneItem_WithNoEfbAndNoTopOfDescent()
    {
        var groups = FenixChecklist.Build();
        var ids = ChecklistItemIds(groups, "DESCENT").ToList();

        Assert.DoesNotContain("DC_ARRPERF", ids);
        Assert.Contains("DC_MCDU", ids);

        string label = ChecklistItemLabel(groups, "DESCENT", "DC_MCDU");
        Assert.DoesNotContain("EFB", label);
        Assert.DoesNotContain("top of descent", label);
        Assert.Contains("PERF APPR", label);
    }

    [Fact]
    public void Fenix_DescentFlow_IsOneItem_WithNoEfbAndNoTopOfDescent()
    {
        var flows = FenixFlows.Build();
        var ids = FlowStepIds(flows, "DESCENT").ToList();

        Assert.DoesNotContain("DC_ARRPERF", ids);
        Assert.Contains("DC_MCDU", ids);

        string label = FlowStepLabel(flows, "DESCENT", "DC_MCDU");
        Assert.DoesNotContain("EFB", label);
        Assert.DoesNotContain("top of descent", label);
        Assert.Contains("PERF APPR", label);
    }

    [Fact]
    public void A32nx_DescentPrep_IsOneItem_WithNoEfbAndNoTopOfDescent()
    {
        var groups = A320Checklist.Build();
        var ids = ChecklistItemIds(groups, "DESCENT").ToList();

        Assert.DoesNotContain("DC_ARRPERF", ids);
        Assert.Contains("DC_MCDU", ids);

        string label = ChecklistItemLabel(groups, "DESCENT", "DC_MCDU");
        Assert.DoesNotContain("EFB", label);
        Assert.DoesNotContain("top of descent", label);
        Assert.Contains("PERF APPR", label);
    }

    [Fact]
    public void A32nx_DescentFlow_IsOneItem_WithNoEfbAndNoTopOfDescent()
    {
        var flows = A320Flows.Build();
        var ids = FlowStepIds(flows, "DESCENT").ToList();

        Assert.DoesNotContain("DC_ARRPERF", ids);
        Assert.Contains("DC_MCDU", ids);

        string label = FlowStepLabel(flows, "DESCENT", "DC_MCDU");
        Assert.DoesNotContain("EFB", label);
        Assert.DoesNotContain("top of descent", label);
        Assert.Contains("PERF APPR", label);
    }

    // The two A320 profiles were written as copies; the wording must not drift apart.
    [Fact]
    public void BothA320Profiles_UseTheSameDescentPrepWording()
    {
        string fenix = ChecklistItemLabel(FenixChecklist.Build(), "DESCENT", "DC_MCDU");
        string a32nx = ChecklistItemLabel(A320Checklist.Build(), "DESCENT", "DC_MCDU");
        Assert.Equal(fenix, a32nx);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FoPr160ProcedureFixTests"`

Expected: **4 failed, 1 passed.** The four wording facts fail on `Assert.DoesNotContain("DC_ARRPERF", ids)`. `BothA320Profiles_UseTheSameDescentPrepWording` passes already — the two profiles are identical copies today — and is a pin: it must go on passing after the change, so the two never drift apart.

- [ ] **Step 3: Edit the Fenix checklist**

In `MSFSBlindAssist/FirstOfficer/Fenix/FenixChecklistDefinitions.cs`, `BuildDescent`, replace these two lines:

```csharp
            Reminder("DC_ARRPERF", "DESCENT", "Calculate arrival performance on the EFB"),
            Reminder("DC_MCDU", "DESCENT", "Complete the MCDU approach page and minimums before top of descent"),
```

with:

```csharp
            // ONE descent-preparation item. The EFB carries no landing-performance answer
            // on the A320 — VAPP comes off the MCDU PERF APPR page from the QNH /
            // temperature / wind / minimums the crew enters. And there is no CRUISE group
            // on this profile, so THIS group is the pre-TOD preparation: an item that says
            // "before top of descent" contradicts where it lives.
            Reminder("DC_MCDU", "DESCENT",
                "Descent preparation: MCDU PERF APPR set — QNH, temperature, wind and minimums; landing configuration reviewed"),
```

- [ ] **Step 4: Edit the Fenix flow**

In `MSFSBlindAssist/FirstOfficer/Fenix/FenixFlowDefinitions.cs`, `BuildDescent`, replace:

```csharp
            Captain("DC_ARRPERF", "Calculate arrival performance on the EFB"),
            Captain("DC_MCDU", "Complete the MCDU approach page and minimums before top of descent"),
```

with:

```csharp
            // ONE descent-preparation item — see FenixChecklistDefinitions.BuildDescent.
            Captain("DC_MCDU",
                "Descent preparation: MCDU PERF APPR set — QNH, temperature, wind and minimums; landing configuration reviewed"),
```

- [ ] **Step 5: Edit the FBW A32NX checklist**

In `MSFSBlindAssist/FirstOfficer/FBWA320/FbwA320ChecklistDefinitions.cs`, `BuildDescent`, replace:

```csharp
            Reminder("DC_ARRPERF", "DESCENT", "Calculate arrival performance on the EFB"),
            Reminder("DC_MCDU", "DESCENT", "Complete the MCDU approach page and minimums before top of descent"),
```

with:

```csharp
            // ONE descent-preparation item. The EFB carries no landing-performance answer
            // on the A320 — VAPP comes off the MCDU PERF APPR page from the QNH /
            // temperature / wind / minimums the crew enters. And there is no CRUISE group
            // on this profile, so THIS group is the pre-TOD preparation: an item that says
            // "before top of descent" contradicts where it lives. Kept word-for-word
            // identical to the Fenix profile's item — the two were written as copies.
            Reminder("DC_MCDU", "DESCENT",
                "Descent preparation: MCDU PERF APPR set — QNH, temperature, wind and minimums; landing configuration reviewed"),
```

- [ ] **Step 6: Edit the FBW A32NX flow**

In `MSFSBlindAssist/FirstOfficer/FBWA320/FbwA320FlowDefinitions.cs`, `BuildDescent`, replace:

```csharp
            Captain("DC_ARRPERF", "Calculate arrival performance on the EFB"),
            Captain("DC_MCDU", "Complete the MCDU approach page and minimums before top of descent"),
```

with:

```csharp
            // ONE descent-preparation item — see FbwA320ChecklistDefinitions.BuildDescent.
            Captain("DC_MCDU",
                "Descent preparation: MCDU PERF APPR set — QNH, temperature, wind and minimums; landing configuration reviewed"),
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FoPr160ProcedureFixTests"`

Expected: 5 passed, 0 failed.

- [ ] **Step 8: Confirm no other code references `DC_ARRPERF`**

Run: `grep -rn "DC_ARRPERF" --include=*.cs .`

Expected: no output. If anything is found (a phase monitor, a persisted-state map, another profile), stop and report it — it must be handled before committing.

- [ ] **Step 9: Build the solution**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

Expected: `Build succeeded`. (Close MSFSBA first if it is running — the exe is file-locked, MSB3021.)

- [ ] **Step 10: Commit**

```bash
git add tests/MSFSBlindAssist.Tests/FoPr160ProcedureFixTests.cs MSFSBlindAssist/FirstOfficer/Fenix/FenixChecklistDefinitions.cs MSFSBlindAssist/FirstOfficer/Fenix/FenixFlowDefinitions.cs MSFSBlindAssist/FirstOfficer/FBWA320/FbwA320ChecklistDefinitions.cs MSFSBlindAssist/FirstOfficer/FBWA320/FbwA320FlowDefinitions.cs
git commit -m "fix(fo): one descent-preparation item on both A320s, without the EFB or TOD wording

The EFB has no landing-performance answer on the A320 - VAPP comes off the
MCDU PERF APPR page. And neither A320 profile has a CRUISE group, so the
Descent group IS the pre-TOD preparation: an item reading 'before top of
descent' contradicted where it lives.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: PMDG 777 — both ground power buttons connect at Electrical Power Up

**Files:**
- Create: `MSFSBlindAssist/FirstOfficer/GroundPowerGate.cs`
- Create: `tests/MSFSBlindAssist.Tests/GroundPowerGateTests.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG777FlowDefinitions.cs` (6 skip predicates: `EPU_GND_PWR_PRIM`/`_SEC` ~line 77–81, `BS_GND_PWR_1`/`_2` ~line 276–281, `SEC_GND_PWR_PRIM`/`_SEC` ~line 588–593)
- Modify: `tests/MSFSBlindAssist.Tests/FoPr160ProcedureFixTests.cs` (append)

**Interfaces:**
- Consumes: the helpers `FlowStepIds` and the class `FoPr160ProcedureFixTests` from Task 1.
- Produces: `public static class MSFSBlindAssist.FirstOfficer.GroundPowerGate` with
  - `public static bool NeedsPress(bool sideOn, bool wantOn)`
  - `public static bool ShouldSkip(bool sideOn, bool wantOn)`

**Background — the actual defect.** Both Electrical Power Up steps share one predicate, `s => s.IsAnyGpuOn()`. The primary press connects primary, which makes `IsAnyGpuOn()` true, so the **secondary step skips itself** and the secondary receptacle is never connected. Secure then correctly presses only the one button that is on — which is what the owner reported. Secure and Before Start are already per-side and are behaviourally unchanged by this task; they are rewritten only so all six predicates read from one documented rule.

---

- [ ] **Step 1: Write the failing gate test**

Create `tests/MSFSBlindAssist.Tests/GroundPowerGateTests.cs`:

```csharp
using Xunit;
using MSFSBlindAssist.FirstOfficer;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 777's two external-power buttons are momentary TOGGLES, so a press is only
/// correct on a side whose current state differs from the wanted one. This is the
/// rule the Electrical Power Up flow got wrong: both of its GPU steps shared one
/// "is ANY GPU on?" predicate, so connecting the primary made the secondary step
/// skip itself and the secondary receptacle was never connected all flight.
/// </summary>
public class GroundPowerGateTests
{
    [Theory]
    // connecting (wantOn: true)
    [InlineData(false, true,  true)]   // side off, want on  -> press
    [InlineData(true,  true,  false)]  // side on,  want on  -> already there, pressing would DISCONNECT
    // disconnecting (wantOn: false)
    [InlineData(true,  false, true)]   // side on,  want off -> press
    [InlineData(false, false, false)]  // side off, want off -> already there, pressing would CONNECT
    public void NeedsPress_IsTrue_OnlyWhenTheSideDisagreesWithWhatIsWanted(
        bool sideOn, bool wantOn, bool expected)
        => Assert.Equal(expected, GroundPowerGate.NeedsPress(sideOn, wantOn));

    [Theory]
    [InlineData(false, true,  false)]
    [InlineData(true,  true,  true)]
    [InlineData(true,  false, false)]
    [InlineData(false, false, true)]
    public void ShouldSkip_IsTheInverseOfNeedsPress(bool sideOn, bool wantOn, bool expected)
        => Assert.Equal(expected, GroundPowerGate.ShouldSkip(sideOn, wantOn));

    // The regression itself: connecting side 1 must not decide anything about side 2.
    // Under the old shared "any GPU on" predicate this pair was (skip, skip).
    [Fact]
    public void ConnectingOneSide_DoesNotSuppressTheOther()
    {
        const bool side1On = true, side2On = false;
        Assert.True(GroundPowerGate.ShouldSkip(side1On, wantOn: true));
        Assert.False(GroundPowerGate.ShouldSkip(side2On, wantOn: true));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~GroundPowerGateTests"`

Expected: build error — `The name 'GroundPowerGate' does not exist in the current context`.

- [ ] **Step 3: Write the gate**

Create `MSFSBlindAssist/FirstOfficer/GroundPowerGate.cs`:

```csharp
namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Which of the PMDG 777's two external-power buttons a flow step must actually press.
///
/// Both buttons are momentary TOGGLES: a press is only correct on a side whose CURRENT
/// state differs from the WANTED one. Pressing an already-connected side DISCONNECTS it;
/// pressing a disconnected side during a power-down CONNECTS it. So the decision is
/// per-side AND per-direction, and it is never legitimate for one side's state to decide
/// the other's.
///
/// This exists because the Electrical Power Up flow got exactly that wrong: BOTH of its
/// GPU steps shared one <c>s =&gt; s.IsAnyGpuOn()</c> predicate, so the primary press made
/// "any GPU on" true and the SECONDARY step skipped itself — the secondary receptacle was
/// never connected, and Secure then had only one side to disconnect. Extracted rather than
/// fixed in place because the flow's own state evaluator wraps a concrete
/// PMDG777DataManager that cannot be constructed without SimConnect, so the predicates are
/// not directly testable; this is (see CenterPumpGate) the project's idiom for making an
/// FO decision unit-testable.
/// </summary>
public static class GroundPowerGate
{
    /// <param name="sideOn">This side's external-power ON annunciator
    /// (<c>ELEC_annunExtPowr_ON_0</c> / <c>_1</c>).</param>
    /// <param name="wantOn">true when the step is CONNECTING ground power (Electrical
    /// Power Up), false when it is DISCONNECTING (Before Start, Secure).</param>
    public static bool NeedsPress(bool sideOn, bool wantOn) => sideOn != wantOn;

    /// <summary>Skip predicate form, for <c>FlowStep.SkipCondition</c>.</summary>
    public static bool ShouldSkip(bool sideOn, bool wantOn) => !NeedsPress(sideOn, wantOn);
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~GroundPowerGateTests"`

Expected: 9 passed, 0 failed.

- [ ] **Step 5: Append the structural test**

Append inside class `FoPr160ProcedureFixTests` in `tests/MSFSBlindAssist.Tests/FoPr160ProcedureFixTests.cs`, before the closing brace:

```csharp
    // -- 2. PMDG 777 ground power ------------------------------------------

    // Both GPU steps must survive at Electrical Power Up. The defect was not a missing
    // step but a shared skip predicate that made the second one skip itself once the
    // first had connected; the predicates themselves are not directly testable (the 777
    // state evaluator wraps a concrete PMDG777DataManager), which is why the rule lives
    // in GroundPowerGate — see GroundPowerGateTests.
    [Fact]
    public void Pmdg777_ElectricalPowerUp_StillDrivesBothGroundPowerSides()
    {
        var ids = FlowStepIds(Pmdg777Flows.Build(), "ELEC_POWER_UP").ToList();
        Assert.Contains("EPU_GND_PWR_PRIM", ids);
        Assert.Contains("EPU_GND_PWR_SEC", ids);
    }

    [Fact]
    public void Pmdg777_Secure_StillDisconnectsBothGroundPowerSides()
    {
        var ids = FlowStepIds(Pmdg777Flows.Build(), "SECURE").ToList();
        Assert.Contains("SEC_GND_PWR_PRIM", ids);
        Assert.Contains("SEC_GND_PWR_SEC", ids);
    }
```

Add `using MSFSBlindAssist.FirstOfficer;` if it is not already present (Task 1 added it).

- [ ] **Step 6: Run to confirm these two already pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FoPr160ProcedureFixTests"`

Expected: 7 passed. These two are pinning tests — the steps already exist, and the point is that a future edit must not delete one.

- [ ] **Step 7: Rewire the Electrical Power Up predicates**

In `MSFSBlindAssist/FirstOfficer/PMDG777FlowDefinitions.cs`, `BuildElectricalPowerUp`, replace:

```csharp
            // Try GPU — push both buttons and wait to see if power comes on.
            // APU is never started here; it is always started during Before Start.
            Skip(Momentary("EPU_GND_PWR_PRIM", "Ground power primary: PUSH",  "EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH"),
                s => s.IsAnyGpuOn()),
            Skip(Momentary("EPU_GND_PWR_SEC",  "Ground power secondary: PUSH", "EVT_OH_ELEC_GRD_PWR_SEC_SWITCH"),
                s => s.IsAnyGpuOn()),
```

with:

```csharp
            // Try GPU — push both buttons and wait to see if power comes on.
            // APU is never started here; it is always started during Before Start.
            // PER SIDE, never "is ANY GPU on": both steps once shared that predicate, so
            // connecting the primary made it true and the SECONDARY step skipped itself —
            // the secondary receptacle was never connected, and Secure then found only one
            // side to disconnect. See GroundPowerGate.
            Skip(Momentary("EPU_GND_PWR_PRIM", "Ground power primary: PUSH",  "EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH"),
                s => GroundPowerGate.ShouldSkip(s.IsGpuPower1On(), wantOn: true)),
            Skip(Momentary("EPU_GND_PWR_SEC",  "Ground power secondary: PUSH", "EVT_OH_ELEC_GRD_PWR_SEC_SWITCH"),
                s => GroundPowerGate.ShouldSkip(s.IsGpuPower2On(), wantOn: true)),
```

- [ ] **Step 8: Route the Before Start predicates through the gate (no behaviour change)**

In the same file, `BuildBeforeStart`, replace:

```csharp
            Skip(Momentary("BS_GND_PWR_1", "Ground power primary: disconnect",
                "EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH"), s => !s.IsGpuPower1On()),
            Skip(Momentary("BS_GND_PWR_2", "Ground power secondary: disconnect",
                "EVT_OH_ELEC_GRD_PWR_SEC_SWITCH"),  s => !s.IsGpuPower2On()),
```

with:

```csharp
            Skip(Momentary("BS_GND_PWR_1", "Ground power primary: disconnect",
                "EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH"),
                s => GroundPowerGate.ShouldSkip(s.IsGpuPower1On(), wantOn: false)),
            Skip(Momentary("BS_GND_PWR_2", "Ground power secondary: disconnect",
                "EVT_OH_ELEC_GRD_PWR_SEC_SWITCH"),
                s => GroundPowerGate.ShouldSkip(s.IsGpuPower2On(), wantOn: false)),
```

- [ ] **Step 9: Route the Secure predicates through the gate (no behaviour change)**

In the same file, `BuildSecure`, replace:

```csharp
            Skip(Momentary("SEC_GND_PWR_PRIM", "Ground power primary: PUSH",
                "EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH"), s => !s.IsGpuPower1On()),
            Skip(Momentary("SEC_GND_PWR_SEC", "Ground power secondary: PUSH",
                "EVT_OH_ELEC_GRD_PWR_SEC_SWITCH"),  s => !s.IsGpuPower2On()),
```

with:

```csharp
            Skip(Momentary("SEC_GND_PWR_PRIM", "Ground power primary: PUSH",
                "EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH"),
                s => GroundPowerGate.ShouldSkip(s.IsGpuPower1On(), wantOn: false)),
            Skip(Momentary("SEC_GND_PWR_SEC", "Ground power secondary: PUSH",
                "EVT_OH_ELEC_GRD_PWR_SEC_SWITCH"),
                s => GroundPowerGate.ShouldSkip(s.IsGpuPower2On(), wantOn: false)),
```

- [ ] **Step 10: Verify `IsAnyGpuOn` is still used, or remove it cleanly**

Run: `grep -rn "IsAnyGpuOn" --include=*.cs .`

Expected: it remains referenced by `AircraftStateEvaluator.cs` (its definition) and by the `FO_ANY_GPU_ON` synthetic used for the checklist auto-tick. Leave both alone — the synthetic is correct for "did external power come on at all", which is a genuinely different question from "should I press this button". If the grep shows it is now referenced ONLY by its own definition, still leave it: it is a small, correctly-named evaluator method and deleting it is out of scope.

- [ ] **Step 11: Build and run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`.

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: all tests pass, no pre-existing failures introduced.

- [ ] **Step 12: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/GroundPowerGate.cs tests/MSFSBlindAssist.Tests/GroundPowerGateTests.cs tests/MSFSBlindAssist.Tests/FoPr160ProcedureFixTests.cs MSFSBlindAssist/FirstOfficer/PMDG777FlowDefinitions.cs
git commit -m "fix(fo): connect BOTH 777 ground power receptacles at Electrical Power Up

Both GPU steps shared one 'is any GPU on' skip predicate, so the primary
press made it true and the secondary step skipped itself - the secondary
was never connected, and Secure then had only one side to disconnect.
The per-side, per-direction toggle rule now lives in GroundPowerGate,
which all six GPU predicates read from.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 3: PMDG 777 — speedbrake moves from Approach to a new Landing flow

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG777FlowDefinitions.cs` (`Build()` list ~line 30–42; `BuildApproachSetup` ~line 459–472; new `BuildLanding`)
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG777ChecklistDefinitions.cs` (`BuildApproach` ~line 584–594; `BuildLandingChecklist` ~line 622–640)
- Modify: `tests/MSFSBlindAssist.Tests/FoPr160ProcedureFixTests.cs` (append)

**Interfaces:**
- Consumes: `FlowStepIds`, `ChecklistItemIds` from Task 1.
- Produces: 777 flow id `"LANDING"` containing step ids `LD_SPEEDBRAKE_ARM` and `LD_MISSED`. Checklist item `LDG_SPEEDBRAKE` in group `LANDING_CL` gains a non-null `CheckAction`. Ids `APPA_SPEEDBRAKE` and `APP_SPEEDBRAKE_ARM` cease to exist.

**Background.** Approach Setup runs at the descent/approach transition, well before the landing configuration is established — arming there is too early. The existing flow step already declares `CompletesChecklistItemId = "LDG_SPEEDBRAKE"`, an item in the **Landing** checklist, so only the dispatch site was wrong. No 777 `LANDING` checklist **group** is added: `LANDING_CL` already holds the speedbrake/gear/flaps items and a one-item group would duplicate it.

---

- [ ] **Step 1: Append the failing tests**

Append inside class `FoPr160ProcedureFixTests`, before the closing brace:

```csharp
    // -- 3. PMDG 777 speedbrake: Approach -> Landing ------------------------

    [Fact]
    public void Pmdg777_ApproachSetupFlow_NoLongerArmsTheSpeedbrake()
    {
        var ids = FlowStepIds(Pmdg777Flows.Build(), "APPROACH_SETUP").ToList();
        Assert.DoesNotContain("APP_SPEEDBRAKE_ARM", ids);
        Assert.Contains("APP_ALTIMETERS", ids);
    }

    [Fact]
    public void Pmdg777_ApproachGroup_NoLongerArmsTheSpeedbrake()
    {
        var ids = ChecklistItemIds(Pmdg777Checklist.Build(), "APPROACH").ToList();
        Assert.DoesNotContain("APPA_SPEEDBRAKE", ids);
        Assert.Contains("APPA_ALTIMETERS", ids);
    }

    [Fact]
    public void Pmdg777_HasALandingFlow_ThatArmsTheSpeedbrake()
    {
        var flows = Pmdg777Flows.Build();
        var landing = flows.Single(f => f.Id == "LANDING");

        Assert.Equal(new[] { "LD_SPEEDBRAKE_ARM", "LD_MISSED" },
                     landing.Steps.Select(s => s.Id).ToArray());
        Assert.Contains("LANDING_CL", landing.RelatedChecklistGroupIds);

        var arm = landing.Steps.Single(s => s.Id == "LD_SPEEDBRAKE_ARM");
        Assert.Equal("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM", arm.EventName);
        Assert.Equal("FCTL_Speedbrake_Lever", arm.VerifyFieldName);
        Assert.Equal("LDG_SPEEDBRAKE", arm.CompletesChecklistItemId);
    }

    // The Landing flow must run AFTER Approach Setup and BEFORE After Landing, so the
    // FO window lists it in the order a pilot flies it.
    [Fact]
    public void Pmdg777_LandingFlow_SitsBetweenApproachSetupAndAfterLanding()
    {
        var ids = Pmdg777Flows.Build().Select(f => f.Id).ToList();
        Assert.Equal(ids.IndexOf("APPROACH_SETUP") + 1, ids.IndexOf("LANDING"));
        Assert.Equal(ids.IndexOf("LANDING") + 1, ids.IndexOf("AFTER_LANDING"));
    }

    // Ticking "Speedbrake: ARMED" on the Landing checklist must actually arm it; the
    // item verified but never actuated (action: null).
    [Fact]
    public void Pmdg777_LandingChecklistSpeedbrake_ActuallyArms()
    {
        var item = Pmdg777Checklist.Build()
            .Single(g => g.Id == "LANDING_CL").Items
            .Single(i => i.Id == "LDG_SPEEDBRAKE");
        Assert.NotNull(item.CheckAction);
        Assert.Equal("FCTL_Speedbrake_Lever", item.StateFieldName);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FoPr160ProcedureFixTests"`

Expected: 5 new failures. `Pmdg777_HasALandingFlow_...` and `..._SitsBetween...` throw `InvalidOperationException: Sequence contains no matching element` (no `LANDING` flow); the other three fail their asserts.

- [ ] **Step 3: Remove the Approach Setup speedbrake step**

In `MSFSBlindAssist/FirstOfficer/PMDG777FlowDefinitions.cs`, `BuildApproachSetup`, replace the whole method body's `Steps` block:

```csharp
        Description = "Sets altimeters and confirms configuration for approach.",
        RelatedChecklistGroupIds = new[] { "APPROACH", "APPROACH_CL", "LANDING_CL" },
        Steps = new()
        {
            Captain("APP_ALTIMETERS",   "Altimeters: Set local QNH / transition"),
            SW("APP_SPEEDBRAKE_ARM",    "Speedbrake: ARM",   "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM", null,
               true, "FCTL_Speedbrake_Lever", v => v > 0.5 && v < 1.5, "LDG_SPEEDBRAKE"),
        }
```

with:

```csharp
        Description = "Sets altimeters for approach.",
        RelatedChecklistGroupIds = new[] { "APPROACH", "APPROACH_CL" },
        Steps = new()
        {
            // The speedbrake is NOT armed here. Approach Setup runs at the
            // descent/approach transition, well before the landing configuration is
            // established — arming there is too early. It moved to the Landing flow,
            // which is where its own CompletesChecklistItemId ("LDG_SPEEDBRAKE", an item
            // in LANDING_CL) always said it belonged.
            Captain("APP_ALTIMETERS",   "Altimeters: Set local QNH / transition"),
        }
```

- [ ] **Step 4: Add the Landing flow**

In the same file, insert this method immediately after `BuildApproachSetup()`. The file's comment banners are numbered, so also renumber the three below it — `Flow 10: After Landing` → `Flow 11`, `Flow 11: Shutdown` → `Flow 12`, `Flow 12: Secure` → `Flow 13`:

```csharp
    // -----------------------------------------------------------------------
    // Flow 10: Landing
    // -----------------------------------------------------------------------
    private static FlowDefinition<AircraftStateEvaluator> BuildLanding() => new()
    {
        Id = "LANDING",
        Name = "Landing",
        Description = "Speedbrake armed and missed approach altitude set for landing.",
        RelatedChecklistGroupIds = new[] { "LANDING_CL" },
        Steps = new()
        {
            // Moved out of Approach Setup: too early there. The ARM detent is an ABSOLUTE
            // mouse-click position, not a toggle, so re-arming an already-armed lever is a
            // no-op and this step needs no skip guard (unlike the ground-power buttons).
            SW("LD_SPEEDBRAKE_ARM",   "Speedbrake: ARM",   "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM", null,
               true, "FCTL_Speedbrake_Lever", v => v > 0.5 && v < 1.5, "LDG_SPEEDBRAKE"),
            // The 737's "Engine start switches: CONT" is deliberately NOT mirrored here —
            // 777 ignition is automatic and needs no CONT selection for landing.
            Captain("LD_MISSED",      "Set the missed approach altitude"),
        }
    };
```

- [ ] **Step 5: Register the Landing flow**

In the same file, in `Build()`, replace:

```csharp
        BuildApproachSetup(),
        BuildAfterLanding(),
```

with:

```csharp
        BuildApproachSetup(),
        BuildLanding(),
        BuildAfterLanding(),
```

- [ ] **Step 6: Remove the Approach group speedbrake item**

In `MSFSBlindAssist/FirstOfficer/PMDG777ChecklistDefinitions.cs`, `BuildApproach`, replace:

```csharp
            Reminder("APPA_ALTIMETERS", "APPROACH", "Altimeters: Set local QNH / transition"),
            Auto("APPA_SPEEDBRAKE", "APPROACH", "Speedbrake: ARM",
                "FCTL_Speedbrake_Lever", v => v > 0.5 && v < 1.5,
                action: (e, _) => e.SetSpeedbrakeArmed()),
```

with:

```csharp
            // The speedbrake is NOT armed here — too early. It lives on the Landing
            // checklist (LDG_SPEEDBRAKE) and the Landing flow.
            Reminder("APPA_ALTIMETERS", "APPROACH", "Altimeters: Set local QNH / transition"),
```

- [ ] **Step 7: Give the Landing checklist item its arm action**

In the same file, `BuildLandingChecklist`, replace:

```csharp
            Auto("LDG_SPEEDBRAKE", "LANDING_CL", "Speedbrake: ARMED",
                "FCTL_Speedbrake_Lever", v => v > 0.5 && v < 1.5,
                action: null),
```

with:

```csharp
            // Ticking this ARMS the lever — it used to verify but never actuate, so a
            // pilot who ticked it on an unarmed lever got a tick and nothing else. The
            // ARM detent is absolute (not a toggle), so a tick on an already-armed lever
            // is a harmless no-op.
            Auto("LDG_SPEEDBRAKE", "LANDING_CL", "Speedbrake: ARMED",
                "FCTL_Speedbrake_Lever", v => v > 0.5 && v < 1.5,
                action: (e, _) => e.SetSpeedbrakeArmed()),
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FoPr160ProcedureFixTests"`

Expected: 12 passed, 0 failed.

- [ ] **Step 9: Confirm the removed ids are gone everywhere**

Run: `grep -rn "APPA_SPEEDBRAKE\|APP_SPEEDBRAKE_ARM" --include=*.cs .`

Expected: no output.

- [ ] **Step 10: Build and run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`.

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: all pass.

- [ ] **Step 11: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/PMDG777FlowDefinitions.cs MSFSBlindAssist/FirstOfficer/PMDG777ChecklistDefinitions.cs tests/MSFSBlindAssist.Tests/FoPr160ProcedureFixTests.cs
git commit -m "fix(fo): arm the 777 speedbrake on a new Landing flow, not during Approach

Approach Setup runs at the descent/approach transition, long before the
landing configuration is established. The step's own CompletesChecklistItemId
already pointed at LDG_SPEEDBRAKE in the Landing checklist. Ticking that
checklist item now actually arms the lever - it verified but never actuated.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 4: PMDG 737 — closed-loop, verified speedbrake arm

**Files:**
- Create: `MSFSBlindAssist/FirstOfficer/PMDG737/SpeedbrakeArmLadder.cs`
- Create: `tests/MSFSBlindAssist.Tests/SpeedbrakeArmLadderTests.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/AircraftActionExecutor.cs` (constants block ~line 84; `ExecuteStepAsync` pseudo-key switch ~line 243–255; add `ArmSpeedbrakeAsync` next to `WarningTestAsync` ~line 486; `SetSpeedbrakeArmed` ~line 689; dispatch-table comment ~line 219–229)
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs` (`BuildLanding`, `LD_SPDBRK` ~line 400)
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737ChecklistDefinitions.cs` (`LDA_SPDBRK` ~line 328–329; `LDC_SPDBRK` ~line 544)
- Modify: `tests/MSFSBlindAssist.Tests/FoPr160ProcedureFixTests.cs` (append)

**Interfaces:**
- Consumes: `ChecklistItemIds` from Task 1; nothing from Tasks 2–3.
- Produces:
  - `public enum MSFSBlindAssist.FirstOfficer.PMDG737.SpeedbrakeArmTransport { CdaClick, TransmitClick, TransmitPressRelease }`
  - `public static class MSFSBlindAssist.FirstOfficer.PMDG737.SpeedbrakeArmLadder` with
    `public const string ArmedField = "MAIN_annunSPEEDBRAKE_ARMED";`,
    `public const string DoNotArmField = "MAIN_annunSPEEDBRAKE_DO_NOT_ARM";`,
    `public const string PseudoKey = "SPEEDBRAKE_ARM";`,
    `public static IReadOnlyList<SpeedbrakeArmTransport> Attempts { get; }`,
    `public static bool ShouldContinue(int attemptIndex, bool armed, bool doNotArmLit)`
  - `public Task<bool> AircraftActionExecutor.ArmSpeedbrakeAsync()`

**Background.** Two faults. (A) The actuation may be reaching a transport the NG3 ignores: the event id is right (`THIRD_PARTY_EVENT_ID_MIN + 6792`, matching the `PMDG_NG3_SDK.h` in the Community folder) and the table forces `MOUSE_FLAG_LEFTSINGLE`, but the NG3 has a documented family of CDA-deaf controls (`EVT_TCAS_MODE`, `EVT_OH_LIGHTS_POS_STROBE`, the CDU keys) that respond only to `TransmitClientEvent` mouse-clicks. (B) A failed arm is invisible — all three sites are unverified, resting on the comment *"No speedbrake-lever state field exists in the NG3 CDA struct."* That is **false**: `MAIN_annunSPEEDBRAKE_ARMED` is in `PMDGNG3DataStruct.cs` line 617 and in the SDK header line 384, and the executor's own comment cites it as the field the 2026-07-03 verification watched.

**Known limitation to preserve in the code comments:** `MAIN_annunSPEEDBRAKE_ARMED` reflects the auto-speedbrake system being armed, not raw lever position, so it will not light cold-and-dark. All three items live only in the Landing phase, so this is acceptable — but running the Landing checklist on the ground will leave the item un-ticked.

---

- [ ] **Step 1: Write the failing ladder test**

Create `tests/MSFSBlindAssist.Tests/SpeedbrakeArmLadderTests.cs`:

```csharp
using System.Linq;
using Xunit;
using MSFSBlindAssist.FirstOfficer.PMDG737;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The PMDG 737 speedbrake arm escalates across transports because the NG3 has a
/// documented family of CDA-deaf controls that only respond to TransmitClientEvent
/// mouse-clicks, and it could not be settled from the repo which family the speedbrake
/// detents belong to. The ORDER matters (cheapest/most-likely first) and the DO NOT ARM
/// early exit matters (an auto-speedbrake fault cannot be clicked away).
/// </summary>
public class SpeedbrakeArmLadderTests
{
    [Fact]
    public void Attempts_EscalateFromCdaToTransmitToPressRelease()
    {
        Assert.Equal(
            new[]
            {
                SpeedbrakeArmTransport.CdaClick,
                SpeedbrakeArmTransport.TransmitClick,
                SpeedbrakeArmTransport.TransmitPressRelease,
            },
            SpeedbrakeArmLadder.Attempts.ToArray());
    }

    [Fact]
    public void ShouldContinue_StopsAsSoonAsTheLeverIsArmed()
    {
        Assert.False(SpeedbrakeArmLadder.ShouldContinue(
            attemptIndex: 0, armed: true, doNotArmLit: false));
    }

    // An auto-speedbrake fault cannot be cleared by more clicks, and the DO NOT ARM
    // annunciator is already announced separately, so the pilot hears the real reason.
    [Fact]
    public void ShouldContinue_StopsWhenDoNotArmIsLit()
    {
        Assert.False(SpeedbrakeArmLadder.ShouldContinue(
            attemptIndex: 0, armed: false, doNotArmLit: true));
    }

    [Fact]
    public void ShouldContinue_KeepsGoingWhileAttemptsRemain()
    {
        Assert.True(SpeedbrakeArmLadder.ShouldContinue(
            attemptIndex: 0, armed: false, doNotArmLit: false));
        Assert.True(SpeedbrakeArmLadder.ShouldContinue(
            attemptIndex: 1, armed: false, doNotArmLit: false));
    }

    [Fact]
    public void ShouldContinue_StopsAfterTheLastAttempt()
    {
        int last = SpeedbrakeArmLadder.Attempts.Count - 1;
        Assert.False(SpeedbrakeArmLadder.ShouldContinue(
            attemptIndex: last, armed: false, doNotArmLit: false));
    }

    // These names are read by the flow step's VerifyFieldName and both checklist items;
    // a typo here is a silently non-detecting item, so pin them.
    [Fact]
    public void FieldNames_MatchThePmdgNg3Struct()
    {
        Assert.Equal("MAIN_annunSPEEDBRAKE_ARMED", SpeedbrakeArmLadder.ArmedField);
        Assert.Equal("MAIN_annunSPEEDBRAKE_DO_NOT_ARM", SpeedbrakeArmLadder.DoNotArmField);
        Assert.Equal("SPEEDBRAKE_ARM", SpeedbrakeArmLadder.PseudoKey);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SpeedbrakeArmLadderTests"`

Expected: build error — `The type or namespace name 'SpeedbrakeArmLadder' could not be found`.

- [ ] **Step 3: Write the ladder**

Create `MSFSBlindAssist/FirstOfficer/PMDG737/SpeedbrakeArmLadder.cs`:

```csharp
using System.Collections.Generic;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

/// <summary>How one arm attempt reaches the sim.</summary>
public enum SpeedbrakeArmTransport
{
    /// <summary>CDA control write with MOUSE_FLAG_LEFTSINGLE — the executor's normal path.</summary>
    CdaClick,
    /// <summary>TransmitClientEvent under the "#id" alias with MOUSE_FLAG_LEFTSINGLE — the
    /// transport the NG3's CDA-deaf controls (EVT_TCAS_MODE, the position-light selector,
    /// the CDU keys) require.</summary>
    TransmitClick,
    /// <summary>Transmit LEFTSINGLE, hold, then LEFTRELEASE — the shape the warning-test
    /// buttons need (see AircraftActionExecutor.WarningTestAsync).</summary>
    TransmitPressRelease,
}

/// <summary>
/// Pure escalation policy for arming the PMDG 737 speedbrake.
///
/// The event id is correct (THIRD_PARTY_EVENT_ID_MIN + 6792, matching the shipped
/// PMDG_NG3_SDK.h) and the dispatch table already forces MOUSE_FLAG_LEFTSINGLE, but the
/// NG3 has a documented family of CDA-deaf controls that only respond to
/// TransmitClientEvent mouse-clicks, and which family the speedbrake detents belong to
/// could not be settled from the repository. So the arm ESCALATES, reading
/// <see cref="ArmedField"/> back between attempts, and reports honestly if none takes —
/// the previous code dispatched once and reported success unconditionally.
///
/// Split out from the executor so the order and the early exit are testable without
/// SimConnect; the executor owns the I/O and the read-back timing.
///
/// NOTE: <see cref="ArmedField"/> reflects the auto-speedbrake system being ARMED, not raw
/// lever position, so it will not light cold-and-dark. Every consumer lives in the Landing
/// phase, where the aircraft is powered and configured. The NG3 exposes no lever-position
/// field at all — the analog position is only readable through the L-var switch_679_73X
/// (ARM = 100), which the FO state evaluator cannot reach (it reads the CDA struct and
/// synthetics only).
/// </summary>
public static class SpeedbrakeArmLadder
{
    /// <summary>PMDGNG3DataStruct field proving the lever reached ARM.</summary>
    public const string ArmedField = "MAIN_annunSPEEDBRAKE_ARMED";

    /// <summary>PMDGNG3DataStruct field for an auto-speedbrake fault. Lit, no number of
    /// clicks can arm the system, so the ladder stops — and this annunciator is already
    /// announced independently, so the pilot hears the real reason.</summary>
    public const string DoNotArmField = "MAIN_annunSPEEDBRAKE_DO_NOT_ARM";

    /// <summary>Flow-step EventName that AircraftActionExecutor.ExecuteStepAsync
    /// intercepts (same mechanism as FIRE_TEST / GPWS_TEST / TCAS_TEST). Not a real
    /// PMDG event name — it must never appear in PMDG737Definition.EventIds.</summary>
    public const string PseudoKey = "SPEEDBRAKE_ARM";

    /// <summary>Cheapest and most-likely first.</summary>
    public static IReadOnlyList<SpeedbrakeArmTransport> Attempts { get; } = new[]
    {
        SpeedbrakeArmTransport.CdaClick,
        SpeedbrakeArmTransport.TransmitClick,
        SpeedbrakeArmTransport.TransmitPressRelease,
    };

    /// <summary>Should another attempt be made after the one at <paramref name="attemptIndex"/>?</summary>
    /// <param name="attemptIndex">Zero-based index of the attempt just made.</param>
    /// <param name="armed">ArmedField read back after that attempt.</param>
    /// <param name="doNotArmLit">DoNotArmField read back after that attempt.</param>
    public static bool ShouldContinue(int attemptIndex, bool armed, bool doNotArmLit)
        => !armed && !doNotArmLit && attemptIndex < Attempts.Count - 1;
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SpeedbrakeArmLadderTests"`

Expected: 6 passed, 0 failed.

- [ ] **Step 5: Commit the pure half**

```bash
git add MSFSBlindAssist/FirstOfficer/PMDG737/SpeedbrakeArmLadder.cs tests/MSFSBlindAssist.Tests/SpeedbrakeArmLadderTests.cs
git commit -m "feat(fo): pure escalation policy for the PMDG 737 speedbrake arm

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 6: Append the failing wiring tests**

Append inside class `FoPr160ProcedureFixTests`, before the closing brace:

```csharp
    // -- 4. PMDG 737 speedbrake: verified arm -------------------------------

    // All three sites used to be unverified, resting on a comment claiming no
    // lever state field exists in the NG3 CDA struct. MAIN_annunSPEEDBRAKE_ARMED
    // does exist (PMDGNG3DataStruct.cs, and PMDG_NG3_SDK.h) - so a failed arm was
    // reported as success and the pilot landed with the lever down.

    [Fact]
    public void Pmdg737_LandingGroupSpeedbrake_AutoDetectsFromTheArmedAnnunciator()
    {
        var item = Pmdg737Checklist.Build()
            .Single(g => g.Id == "LANDING").Items
            .Single(i => i.Id == "LDA_SPDBRK");

        Assert.Equal(ChecklistItemType.AutoDetectable, item.Type);
        Assert.Equal("MAIN_annunSPEEDBRAKE_ARMED", item.StateFieldName);
        Assert.NotNull(item.CheckAction);
    }

    [Fact]
    public void Pmdg737_LandingChecklistSpeedbrake_VerifiesButDoesNotActuate()
    {
        var item = Pmdg737Checklist.Build()
            .Single(g => g.Id == "LANDING_CL").Items
            .Single(i => i.Id == "LDC_SPDBRK");

        Assert.Equal(ChecklistItemType.AutoDetectable, item.Type);
        Assert.Equal("MAIN_annunSPEEDBRAKE_ARMED", item.StateFieldName);
        Assert.Null(item.CheckAction);
    }

    [Fact]
    public void Pmdg737_LandingFlowSpeedbrake_GoesThroughTheVerifiedPseudoKey()
    {
        var step = Pmdg737Flows.Build()
            .Single(f => f.Id == "LANDING").Steps
            .Single(s => s.Id == "LD_SPDBRK");

        Assert.Equal(SpeedbrakeArmLadder.PseudoKey, step.EventName);
        Assert.Equal(SpeedbrakeArmLadder.ArmedField, step.VerifyFieldName);
        Assert.Equal("LDC_SPDBRK", step.CompletesChecklistItemId);
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
    }

    // The pseudo-key is intercepted before the dispatch table is consulted, so it must
    // never collide with a real PMDG event name.
    [Fact]
    public void Pmdg737_SpeedbrakePseudoKey_IsNotARealPmdgEvent()
    {
        Assert.False(MSFSBlindAssist.Aircraft.PMDG737Definition.EventIds
            .ContainsKey(SpeedbrakeArmLadder.PseudoKey));
    }
```

Add these usings to the top of the file:

```csharp
using MSFSBlindAssist.FirstOfficer.PMDG737;
```

- [ ] **Step 7: Run to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FoPr160ProcedureFixTests"`

Expected: 3 new failures (`LDA_SPDBRK` is `Actionable` not `AutoDetectable`; `LDC_SPDBRK` is `CaptainReminder`; `LD_SPDBRK.EventName` is the real event, not the pseudo-key). `Pmdg737_SpeedbrakePseudoKey_IsNotARealPmdgEvent` should already pass.

- [ ] **Step 8: Add the arm timings to the executor constants**

In `MSFSBlindAssist/FirstOfficer/PMDG737/AircraftActionExecutor.cs`, immediately after the `MouseFlagLeftReleaseU` declaration (~line 91), add:

```csharp
    // Speedbrake arm read-back. The ambient CDA poll is 1 Hz, so a 1.2 s window always
    // contains at least one refresh. PMDGNG3DataManager.RequestFreshSnapshotAsync is NOT
    // used — it is private and documented as unsafe for concurrent callers.
    private const int SpeedbrakeArmVerifyMs = 1200;
    private const int SpeedbrakeArmPollMs = 100;
    // Press-and-hold for the third rung, matching the warning-test press/release shape.
    private const int SpeedbrakeArmHoldMs = 120;
```

- [ ] **Step 9: Add `ArmSpeedbrakeAsync` to the executor**

In the same file, immediately after `WarningTestAsync` (~line 503), add:

```csharp
    /// <summary>
    /// Arms the speedbrake and PROVES it, escalating across transports.
    ///
    /// The old path dispatched one CDA + LEFTSINGLE click and reported success
    /// unconditionally — so when it did not take, the Landing flow said "Speedbrake:
    /// ARMED" and the checklist item ticked while the lever stayed down. The NG3 has a
    /// documented family of CDA-deaf controls that only move on TransmitClientEvent
    /// mouse-clicks, and which family the speedbrake detents belong to could not be
    /// settled from the repo, so each rung is tried and READ BACK in turn (see
    /// <see cref="SpeedbrakeArmLadder"/>).
    ///
    /// Holds <c>_dispatchGate</c> across the whole ladder and uses <c>DispatchCoreAsync</c>
    /// / the raw send methods internally — never <c>DispatchAsync</c>, which would deadlock
    /// on the gate. Worst case is roughly four seconds, which
    /// <c>ChecklistManager.RunCheckActionWithGraceAsync</c> already covers: it holds revert
    /// until the action completes AND the dispatch gate drains, which is what the
    /// multi-second transponder walk relies on.
    /// </summary>
    /// <returns>true once MAIN_annunSPEEDBRAKE_ARMED confirms; false if no rung took.</returns>
    public async Task<bool> ArmSpeedbrakeAsync()
    {
        const string ev = "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM";
        var sc = _sc;
        if (sc == null || !PMDG737Definition.EventIds.TryGetValue(ev, out int evId))
            return false;
        uint id = (uint)evId;

        await _dispatchGate.WaitAsync();
        try
        {
            for (int i = 0; i < SpeedbrakeArmLadder.Attempts.Count; i++)
            {
                await PaceAsync();
                switch (SpeedbrakeArmLadder.Attempts[i])
                {
                    case SpeedbrakeArmTransport.CdaClick:
                        sc.SendPMDGEvent(ev, id, MouseFlagLeftSingle);
                        break;
                    case SpeedbrakeArmTransport.TransmitClick:
                        sc.SendPMDGEventViaTransmitWithTarget(id, MouseFlagLeftSingleU);
                        break;
                    case SpeedbrakeArmTransport.TransmitPressRelease:
                        sc.SendPMDGEventViaTransmitWithTarget(id, MouseFlagLeftSingleU);
                        await Task.Delay(SpeedbrakeArmHoldMs);
                        sc.SendPMDGEventViaTransmitWithTarget(id, MouseFlagLeftReleaseU);
                        break;
                }
                _lastWriteUtc = DateTime.UtcNow;

                bool armed = await WaitForSpeedbrakeArmedAsync();
                if (armed) return true;

                bool doNotArm = FieldOn(SpeedbrakeArmLadder.DoNotArmField);
                if (!SpeedbrakeArmLadder.ShouldContinue(i, armed, doNotArm))
                {
                    Log.Debug("FirstOfficer",
                        doNotArm
                            ? "Speedbrake arm abandoned: DO NOT ARM is lit (auto-speedbrake fault)."
                            : "Speedbrake arm failed: no transport moved the lever to ARM.");
                    return false;
                }
            }
            return false;
        }
        finally { _dispatchGate.Release(); }
    }

    /// <summary>Polls the ARMED annunciator for <c>SpeedbrakeArmVerifyMs</c>. Returns as
    /// soon as it reads true.</summary>
    private async Task<bool> WaitForSpeedbrakeArmedAsync()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(SpeedbrakeArmVerifyMs);
        while (DateTime.UtcNow < deadline)
        {
            if (FieldOn(SpeedbrakeArmLadder.ArmedField)) return true;
            await Task.Delay(SpeedbrakeArmPollMs);
        }
        return FieldOn(SpeedbrakeArmLadder.ArmedField);
    }

    private bool FieldOn(string field)
        => (_sc?.PMDGDataManager?.GetFieldValue(field) ?? 0.0) > 0.5;
```

`ImplicitUsings` is enabled, so `System` and `System.Threading.Tasks` need no import. The file does **not** currently use the logger, so add this one line to its usings (top of `AircraftActionExecutor.cs`, after `using MSFSBlindAssist.SimConnect;`):

```csharp
using MSFSBlindAssist.Utils.Logging;
```

Per the project rule, every log write goes through `Utils/Logging/Log` — never `File.AppendAllText` or a hand-built path.

- [ ] **Step 10: Intercept the pseudo-key**

In the same file, in `ExecuteStepAsync`'s pseudo-key switch, add a case alongside the existing tests:

```csharp
                case "OXY_TEST_FO":   return OxygenTestFOAsync();
                // Closed-loop, verified arm — see ArmSpeedbrakeAsync. The plain event
                // name is NOT used from the flow any more, because a bare dispatch
                // reports success whether or not the lever moved.
                case SpeedbrakeArmLadder.PseudoKey: return ArmSpeedbrakeAsync();
```

- [ ] **Step 11: Route the executor's own convenience method through the ladder**

In the same file, replace:

```csharp
    public bool SetSpeedbrakeArmed()     => Fire("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM", 1);
```

with:

```csharp
    /// <summary>Fire-and-forget arm for callers that cannot await (the synchronous
    /// CheckAction shape). Prefer <see cref="ArmSpeedbrakeAsync"/> — it reports whether
    /// the lever actually reached ARM.</summary>
    public bool SetSpeedbrakeArmed()     { _ = ArmSpeedbrakeAsync(); return true; }
```

- [ ] **Step 12: Correct the stale dispatch-table comment**

In the same file, in the dispatch table's speed-brake block, replace the sentence
`Live-verified 2026-07-03: CDA + LEFTSINGLE on _ARM lit MAIN_annunSPEEDBRAKE_ARMED.`
with:

```csharp
        // 2026-08-25: that 2026-07-03 verification no longer reproduces — the owner
        // reports the lever not arming. The FO path therefore no longer trusts a single
        // transport: ArmSpeedbrakeAsync escalates CDA click -> transmit click -> transmit
        // press/release and reads MAIN_annunSPEEDBRAKE_ARMED back between each. This
        // table entry still governs the non-FO callers of these detent events.
```

- [ ] **Step 13: Rewire the Landing flow step**

In `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs`, `BuildLanding`, replace:

```csharp
            SW("LD_SPDBRK", "Speedbrake: ARMED", "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM", null, isMomentary: true),
```

with:

```csharp
            // Verified arm via the SPEEDBRAKE_ARM pseudo-key (intercepted in
            // AircraftActionExecutor.ExecuteStepAsync, same mechanism as GPWS_TEST /
            // TCAS_TEST). A bare dispatch of the real event reported success whether or
            // not the lever moved; this one proves it against the ARMED annunciator, and
            // the Skip failure policy means a genuine failure is announced without
            // aborting the rest of the Landing flow.
            SW("LD_SPDBRK", "Speedbrake: ARMED", SpeedbrakeArmLadder.PseudoKey, null,
               SpeedbrakeArmLadder.ArmedField, v => v > 0.5, "LDC_SPDBRK"),
```

- [ ] **Step 14: Rewire the two checklist items**

In `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737ChecklistDefinitions.cs`, `BuildLanding`, replace:

```csharp
            // No speedbrake-lever state field exists in the NG3 CDA struct — action-only.
            ActionManual("LDA_SPDBRK", "LANDING", "Speedbrake: ARMED", (e, _) => e.SetSpeedbrakeArmed()),
```

with:

```csharp
            // Detected on MAIN_annunSPEEDBRAKE_ARMED. (The old comment here claimed the
            // NG3 CDA struct has no speedbrake state field; it does — this one — and
            // without it a failed arm ticked the item anyway.) The annunciator reflects
            // the auto-speedbrake system being ARMED rather than raw lever position, so it
            // will not light cold-and-dark; this item only exists in the Landing phase.
            AutoAsync("LDA_SPDBRK", "LANDING", "Speedbrake: ARMED",
                SpeedbrakeArmLadder.ArmedField, v => v > 0.5,
                (e, _) => e.ArmSpeedbrakeAsync()),
```

In `BuildLandingChecklist`, replace:

```csharp
            Reminder("LDC_SPDBRK", "LANDING_CL", "Speedbrake: ARMED"),
```

with:

```csharp
            // Verify-only on the checklist — the Landing flow and the Landing group item
            // are what actuate. Mirrors the 777's LDG_SPEEDBRAKE.
            Auto("LDC_SPDBRK", "LANDING_CL", "Speedbrake: ARMED",
                SpeedbrakeArmLadder.ArmedField, v => v > 0.5, action: null),
```

`AutoAsync` expects `Func<AircraftActionExecutor, AircraftStateEvaluator, Task>`; `ArmSpeedbrakeAsync` returns `Task<bool>`, which is assignable to `Task`, so `(e, _) => e.ArmSpeedbrakeAsync()` compiles as-is. If the compiler disagrees, write `(e, _) => (Task)e.ArmSpeedbrakeAsync()`.

Both definition files already live in namespace `MSFSBlindAssist.FirstOfficer.PMDG737`, so `SpeedbrakeArmLadder` resolves without an import. `ImplicitUsings` is enabled, so nothing else is needed.

- [ ] **Step 15: Run the tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FoPr160ProcedureFixTests"`

Expected: 16 passed, 0 failed.

- [ ] **Step 16: Confirm the stale claim is gone**

Run: `grep -rn "No speedbrake-lever state field" --include=*.cs .`

Expected: no output.

- [ ] **Step 17: Build and run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`.

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: all pass.

- [ ] **Step 18: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/PMDG737/AircraftActionExecutor.cs MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737ChecklistDefinitions.cs tests/MSFSBlindAssist.Tests/FoPr160ProcedureFixTests.cs
git commit -m "fix(fo): prove the PMDG 737 speedbrake actually armed

The arm dispatched one CDA click and reported success unconditionally, so
a lever that never moved still ticked the item and announced 'Speedbrake:
ARMED'. It now escalates CDA click -> transmit click -> transmit press/release,
reading MAIN_annunSPEEDBRAKE_ARMED back between each, and stops early when
DO NOT ARM is lit. Both checklist items detect from that annunciator, so a
failed arm reverts instead of ticking.

Removes the stale 'no speedbrake-lever state field exists in the NG3 CDA
struct' claim - MAIN_annunSPEEDBRAKE_ARMED is in the struct and in the SDK
header, and the executor's own comment already cited it.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 5: Changelog fragments and PR test plan

**Files:**
- Create: `changelog.d/160-a320-descent-prep-wording.improvement.md`
- Create: `changelog.d/160-777-speedbrake-landing-flow.fix.md`
- Create: `changelog.d/160-737-speedbrake-arm-verified.fix.md`
- Create: `changelog.d/160-777-secondary-ground-power.fix.md`

**Interfaces:**
- Consumes: nothing. Runs after tasks 1–4 land.
- Produces: nothing consumed by later tasks.

---

- [ ] **Step 1: Confirm the PR number**

Run: `gh pr view --json number,url,headRepositoryOwner`

Expected: number `160`. **Do not guess.** If it differs, use the printed number in every filename below. (CI fails a fragment whose number does not match the real PR.)

- [ ] **Step 2: Write the four fragments**

`changelog.d/160-a320-descent-prep-wording.improvement.md`:

```markdown
The A320 descent checklist and flow now carry one descent-preparation item instead of two, and it no longer sends you to the EFB for a landing calculation the EFB does not do — the numbers come off the MCDU PERF APPR page. It also no longer says "before top of descent" while you are already descending. Applies to both the Fenix and the FlyByWire A320.
```

`changelog.d/160-777-speedbrake-landing-flow.fix.md`:

```markdown
The PMDG 777 no longer arms the speedbrake during the approach setup — far too early. There is now a Landing flow that arms it and reminds you to set the missed approach altitude, and ticking "Speedbrake: ARMED" on the Landing checklist actually arms the lever, which it never did before.
```

`changelog.d/160-737-speedbrake-arm-verified.fix.md`:

```markdown
The PMDG 737 First Officer now proves the speedbrake reached ARM instead of assuming it. Previously a lever that never moved still announced "Speedbrake: ARMED" and ticked the checklist, so you could be on short final with the speedbrake down and nothing to tell you. It now tries the arm several ways, checks the SPEED BRAKE ARMED light after each, and leaves the item un-ticked and reports it if the lever really did not move — or stops and lets the DO NOT ARM light speak for itself when the auto-speedbrake system is faulted.
```

`changelog.d/160-777-secondary-ground-power.fix.md`:

```markdown
The PMDG 777 Electrical Power Up flow now connects both external power receptacles. It used to stop after the primary, so the secondary stayed disconnected for the whole flight and the Secure flow then only had one button to press on shutdown.
```

- [ ] **Step 3: Verify the fragment convention**

Run: `cat changelog.d/README.md`

Confirm the category suffixes used above (`improvement`, `fix`) and the `<pr>-<slug>.<category>.md` shape still match. Fix any filename that does not.

- [ ] **Step 4: Commit**

```bash
git add changelog.d/160-a320-descent-prep-wording.improvement.md changelog.d/160-777-speedbrake-landing-flow.fix.md changelog.d/160-737-speedbrake-arm-verified.fix.md changelog.d/160-777-secondary-ground-power.fix.md
git commit -m "docs: changelog fragments for the PR #160 First Officer procedure fixes

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 5: Report the in-sim test plan**

Do **not** push. Hand this back to the repo owner as the in-sim plan (it belongs in the PR body):

1. **Fenix / A32NX** — open First Officer, Descent group and Descent flow. One preparation item, no EFB, no "top of descent".
2. **777 approach** — run Approach Setup on descent; it must not touch the speedbrake. Run the new Landing flow on final: lever moves to ARM and `LDG_SPEEDBRAKE` ticks on the Landing checklist. Untick and re-tick that checklist item with the lever down — it must arm.
3. **737 landing** — lever DOWN, run the Landing flow. Confirm the lever reaches ARM (the app's own speed-brake monitor announces "Speed brake armed"; L-var `switch_679_73X` reads 100). Repeat by ticking `LDA_SPDBRK` on the Landing group; it must arm the same way.

   Then test the failure side of each path (block the lever, or disconnect the linkage, so the arm genuinely cannot take):
   - **Running the Landing flow**: expect to hear `"Skipping: Speedbrake: ARMED"` a couple of seconds in. Once the flow finishes, `LDA_SPDBRK`/`LDC_SPDBRK` must remain **un-ticked** while the rest of the Landing section ticks — the section header must not read complete. Then set the lever to ARM by hand: the item must tick itself on the next auto-detect poll, proving the group did not latch against it.
   - **Ticking `LDA_SPDBRK` by hand**: within ~10 s (the manual-tick grace) expect to hear `"Unable to complete: Speedbrake: ARMED"` and see the checkbox un-tick.

   **Fixed (2026-08-25, this branch):** the defect originally recorded here — a flow whose step reported `StepSkipped`/`StepFailed` could still be force-ticked and permanently latched by `ChecklistManager.MarkGroupComplete`'s unconditional per-group tick, and a failed manual tick reverted with no announcement — is fixed, on both paths. `FlowManager` now records the `CompletesChecklistItemId` of every step its `Skip` failure policy let it continue past, in `FlowManager.UnfinishedChecklistItemIds`; `FirstOfficerForm.OnFlowCompleted` passes that set as `MarkGroupComplete`'s new `excludeItemIds` parameter, which leaves those items un-ticked instead of force-ticking them. The group's completion latch is **not** withheld — `group.CompletionLatched = true` is still set unconditionally, because it is a deliberate, load-bearing record: a phase the flow otherwise worked correctly must stand complete for the rest of the flight even as unrelated switches move later (flaps retracting after takeoff, the speedbrake stowing on rollout). Withholding the latch would have stripped that record from every other item in the group over one excluded item; instead each excluded item is marked individually with `ChecklistItem.ExemptFromCompletionLatch`, and the revert branch's `(!group.CompletionLatched || item.ExemptFromCompletionLatch)` check lets that one item keep mirroring live state inside an otherwise-latched group. Separately, a manual tick that fires a linked action now sets `ChecklistItem.AwaitingActionConfirmation`; a revert while that mark stands raises `ChecklistManager.ItemActionFailed`, and `FirstOfficerForm` speaks `"Unable to complete: {label}"`. An ordinary revert (the pilot moving the switch back themselves, or an item with no linked action) stays silent, as before. Neither fix is specific to the speedbrake or the 737; both apply to every `AutoAsync`/`RevertToState` checklist item reachable from a flow or a manual tick, on every profile.
4. **737 transport probe** — with the 737 loaded, `tools/PMDGDispatchTester`: send `EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM` as (a) CDA + `0x20000000`, (b) transmit + `0x20000000`, (c) transmit `0x20000000` then `0x00020000`, reading `MAIN_annunSPEEDBRAKE_ARMED` and `switch_679_73X` between each and resetting the lever to DOWN in between. Whichever rung fires is the one to record in the executor comment; the ladder can be trimmed to it in a follow-up. **Do not probe with the simconnect MCP's `send_pmdg_event`** — its CDA write silently fails on the NG3.
5. **777 ground power** — cold and dark with ground power available: Electrical Power Up must connect **both** primary and secondary. Before Start must disconnect both once the APU is running. Secure, from a state with both connected, must press **both** buttons. Re-run Secure with nothing connected: both steps skip and nothing is connected by accident.

**Push note when the owner is ready:** upstream is `fork/feature/first-officer` (github.com/blindflightsimmer), not `origin`.
