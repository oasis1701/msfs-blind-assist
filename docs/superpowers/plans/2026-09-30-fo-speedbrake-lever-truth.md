# First Officer speed brake: lever truth + leave-alone — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The PMDG 737, PMDG 777 and iFly 737 MAX First Officers judge the speed brake by its exact lever position (main's PR #261 tables), never move a deployed speed brake (they say why instead), and the iFly First Officer arms it itself like the PMDG 737's.

**Architecture:** One pure classifier (`SpeedbrakeLeverState`) over main's detent tables answers Down / Armed / Deployed / Unknown and decides what an arm action may do. A generic "leave alone" rule on `FlowStep` and `ChecklistItem` lets a flow step or a hand-tick refuse to act and speak a reason. Each aircraft's evaluator reads its lever (PMDG: SimConnect cache; iFly: SDK snapshot) and each executor gets a verified `SPEEDBRAKE_ARM` action with a backstop that never clicks a lever it cannot see or one that is deployed.

**Tech Stack:** C# 13 / .NET 10 WinForms, xUnit (`tests/MSFSBlindAssist.Tests`).

Spec: `docs/superpowers/specs/2026-09-30-fo-speedbrake-lever-truth-design.md`.

## Global Constraints

- Work ONLY in the worktree `D:\Claude\oasis1701\msfs-blind-assist\.claude\worktrees\pr160-speedbrake` (branch `pr160-speedbrake-truth`). Never `cd` to the main checkout. Never push.
- Test command (from the worktree root): `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "<filter>"`. NEVER build the bare `MSFSBlindAssist.csproj` without `-p:Platform=x64`.
- Main's tables are the ONLY source of lever numbers in production code: `PmdgSpeedBrakeLever.Ng3` / `.B777` / `IFly737SpeedBrakeLever.Detents` (namespace `MSFSBlindAssist.Aircraft`). Never type 100 / 200 / 34 into First Officer production code. Tests MAY assert the measured literals (that is how drift is caught).
- The one spoken sentence is `SpeedbrakeLeverState.LeaveAloneText` = `"Speedbrake extended, not armed. Left as it is."` — no other new speech.
- Field names: `SpeedbrakeLeverState.LeverField` = `"FO_SPEEDBRAKE_LEVER"`, `SpeedbrakeLeverState.ArmedField` = `"FO_SPEEDBRAKE_ARMED"`, pseudo-key `SpeedbrakeLeverState.ArmPseudoKey` = `"SPEEDBRAKE_ARM"`.
- Match the surrounding code's comment density (this codebase explains WHY in comments; keep new comments short and factual).
- Every commit message ends with a blank line then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Files use CRLF in the working tree; the Edit tool preserves that. No BOM.

---

### Task 1: `SpeedbrakeLeverState` — the pure lever truth

**Files:**
- Create: `MSFSBlindAssist/FirstOfficer/SpeedbrakeLeverState.cs`
- Test: `tests/MSFSBlindAssist.Tests/FirstOfficer/SpeedbrakeLeverStateTests.cs`

**Interfaces:**
- Consumes: `MSFSBlindAssist.Aircraft.PmdgSpeedBrakeLever.PositionIndex(IReadOnlyList<PmdgLeverDetent>, double, double)`, `.Ng3`, `.Ng3SettleTolerance`, `.B777`, `.B777SettleTolerance`; `IFly737SpeedBrakeLever.Detents`, `.SettleTolerance`, `.FieldName`.
- Produces (namespace `MSFSBlindAssist.FirstOfficer`):
  - `public enum SpeedbrakeLeverPosition { Unknown, Down, Armed, Deployed }`
  - `public enum SpeedbrakeArmDecision { AlreadyArmed, Arm, LeaveAlone, Unreadable }`
  - `public sealed record SpeedbrakeLeverTable(IReadOnlyList<PmdgLeverDetent> Detents, double SettleTolerance, string LeverKey)` with `double ArmValue`
  - `public static class SpeedbrakeLeverState` with consts `LeverField`, `ArmedField`, `ArmPseudoKey`, `LeaveAloneText`; tables `Pmdg737`, `Pmdg777`, `IFly737`; `Classify(SpeedbrakeLeverTable, double) → SpeedbrakeLeverPosition`; `ArmedValue(SpeedbrakeLeverTable, double lever, double armedLight) → double` (1 / 0 / NaN); `DecideArm(SpeedbrakeLeverPosition, bool armedLightLit, bool extendedLightLit) → SpeedbrakeArmDecision`.

- [ ] **Step 1: Write the failing test**

Create `tests/MSFSBlindAssist.Tests/FirstOfficer/SpeedbrakeLeverStateTests.cs`:

```csharp
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The First Officer's speed-brake truth for the three Boeings, judged on main's measured detent
/// tables (PR #261, 2026-09-30, hydraulics pressurised): ARM is an EXACT position on every one of
/// them, one step past it the spoilers are already up, and anything short of it is not armed.
/// The literals below are the measured values, so a table that drifts fails here.
/// </summary>
public class SpeedbrakeLeverStateTests
{
    [Theory]
    [InlineData(0, SpeedbrakeLeverPosition.Down)]
    [InlineData(95, SpeedbrakeLeverPosition.Down)]      // short of ARM is not armed
    [InlineData(99.8, SpeedbrakeLeverPosition.Armed)]   // inside ARM's own exact tolerance
    [InlineData(100, SpeedbrakeLeverPosition.Armed)]
    [InlineData(101, SpeedbrakeLeverPosition.Deployed)] // spoilers already 34 percent up
    [InlineData(250, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(337, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(400, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(double.NaN, SpeedbrakeLeverPosition.Unknown)]
    public void Pmdg737_lever(double lever, SpeedbrakeLeverPosition expected) =>
        Assert.Equal(expected, SpeedbrakeLeverState.Classify(SpeedbrakeLeverState.Pmdg737, lever));

    [Theory]
    [InlineData(0, SpeedbrakeLeverPosition.Down)]
    [InlineData(22, SpeedbrakeLeverPosition.Down)]      // a hardware axis parks DOWN here
    [InlineData(199, SpeedbrakeLeverPosition.Down)]
    [InlineData(200, SpeedbrakeLeverPosition.Armed)]
    [InlineData(201, SpeedbrakeLeverPosition.Deployed)] // the SDK byte still read 50, "armed"
    [InlineData(203, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(300, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(400, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(double.NaN, SpeedbrakeLeverPosition.Unknown)]
    public void Pmdg777_lever(double lever, SpeedbrakeLeverPosition expected) =>
        Assert.Equal(expected, SpeedbrakeLeverState.Classify(SpeedbrakeLeverState.Pmdg777, lever));

    [Theory]
    [InlineData(0, SpeedbrakeLeverPosition.Down)]
    [InlineData(33, SpeedbrakeLeverPosition.Down)]      // the ARMED light is off at 33
    [InlineData(34, SpeedbrakeLeverPosition.Armed)]
    [InlineData(35, SpeedbrakeLeverPosition.Deployed)]  // spoilers deploy in step from 34
    [InlineData(180, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(224, SpeedbrakeLeverPosition.Deployed)]
    [InlineData(double.NaN, SpeedbrakeLeverPosition.Unknown)]
    public void IFly737_lever(double lever, SpeedbrakeLeverPosition expected) =>
        Assert.Equal(expected, SpeedbrakeLeverState.Classify(SpeedbrakeLeverState.IFly737, lever));

    [Fact]
    public void Each_table_is_mains_own()
    {
        Assert.Same(PmdgSpeedBrakeLever.Ng3, SpeedbrakeLeverState.Pmdg737.Detents);
        Assert.Same(PmdgSpeedBrakeLever.B777, SpeedbrakeLeverState.Pmdg777.Detents);
        Assert.Same(IFly737SpeedBrakeLever.Detents, SpeedbrakeLeverState.IFly737.Detents);
        Assert.Equal(100.0, SpeedbrakeLeverState.Pmdg737.ArmValue);
        Assert.Equal(200.0, SpeedbrakeLeverState.Pmdg777.ArmValue);
        Assert.Equal(34.0, SpeedbrakeLeverState.IFly737.ArmValue);
    }

    [Fact]
    public void Each_table_reads_the_lever_key_main_registers()
    {
        Assert.Equal("switch_679_73X",
            new PMDG737Definition().GetVariables()[SpeedbrakeLeverState.Pmdg737.LeverKey].Name);
        Assert.Equal("switch_498_a",
            new PMDG777Definition().GetVariables()[SpeedbrakeLeverState.Pmdg777.LeverKey].Name);
        Assert.True(new IFly737MAXDefinition().GetVariables()
            .ContainsKey(SpeedbrakeLeverState.IFly737.LeverKey));
    }

    [Theory]
    [InlineData(100, 1, 1.0)]     // lever at ARM, light lit
    [InlineData(100, 0, 0.0)]     // lever at ARM, light out (DO NOT ARM, or no power)
    [InlineData(150, 1, 0.0)]     // the 737 ARMED light stays lit well past ARM
    [InlineData(0, 0, 0.0)]
    public void Pmdg737_armed_needs_the_lever_at_ARM_and_the_light(double lever, double light, double expected) =>
        Assert.Equal(expected, SpeedbrakeLeverState.ArmedValue(SpeedbrakeLeverState.Pmdg737, lever, light));

    [Theory]
    [InlineData(34, 1, 1.0)]      // DIM counts as lit
    [InlineData(34, 2, 1.0)]      // BRIGHT
    [InlineData(100, 1, 0.0)]     // the iFly light is lit from 34 all the way to 224
    [InlineData(224, 2, 0.0)]
    [InlineData(33, 0, 0.0)]
    public void IFly737_armed_needs_the_lever_at_ARM_and_the_light(double lever, double light, double expected) =>
        Assert.Equal(expected, SpeedbrakeLeverState.ArmedValue(SpeedbrakeLeverState.IFly737, lever, light));

    [Fact]
    public void Armed_is_unknown_while_either_input_is()
    {
        Assert.True(double.IsNaN(SpeedbrakeLeverState.ArmedValue(SpeedbrakeLeverState.Pmdg737, double.NaN, 1)));
        Assert.True(double.IsNaN(SpeedbrakeLeverState.ArmedValue(SpeedbrakeLeverState.Pmdg737, 100, double.NaN)));
    }

    [Theory]
    [InlineData(SpeedbrakeLeverPosition.Down, false, false, SpeedbrakeArmDecision.Arm)]
    [InlineData(SpeedbrakeLeverPosition.Armed, true, false, SpeedbrakeArmDecision.AlreadyArmed)]
    [InlineData(SpeedbrakeLeverPosition.Armed, false, false, SpeedbrakeArmDecision.Arm)]  // re-arm is a harmless absolute click
    [InlineData(SpeedbrakeLeverPosition.Deployed, true, false, SpeedbrakeArmDecision.LeaveAlone)]
    [InlineData(SpeedbrakeLeverPosition.Down, false, true, SpeedbrakeArmDecision.LeaveAlone)]  // EXTENDED light lit
    [InlineData(SpeedbrakeLeverPosition.Unknown, false, false, SpeedbrakeArmDecision.Unreadable)]
    [InlineData(SpeedbrakeLeverPosition.Unknown, false, true, SpeedbrakeArmDecision.LeaveAlone)]
    public void The_arm_decision_never_moves_a_deployed_or_unseen_lever(
        SpeedbrakeLeverPosition position, bool armedLit, bool extendedLit, SpeedbrakeArmDecision expected) =>
        Assert.Equal(expected, SpeedbrakeLeverState.DecideArm(position, armedLit, extendedLit));

    [Fact]
    public void The_spoken_reason_says_what_stays_as_it_is()
    {
        Assert.Equal("Speedbrake extended, not armed. Left as it is.", SpeedbrakeLeverState.LeaveAloneText);
        Assert.Equal("FO_SPEEDBRAKE_LEVER", SpeedbrakeLeverState.LeverField);
        Assert.Equal("FO_SPEEDBRAKE_ARMED", SpeedbrakeLeverState.ArmedField);
        Assert.Equal("SPEEDBRAKE_ARM", SpeedbrakeLeverState.ArmPseudoKey);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SpeedbrakeLeverStateTests"`
Expected: build FAILS — `SpeedbrakeLeverState` / `SpeedbrakeLeverPosition` do not exist.

- [ ] **Step 3: Write the implementation**

Create `MSFSBlindAssist/FirstOfficer/SpeedbrakeLeverState.cs`:

```csharp
using System.Collections.Generic;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>Where a Boeing speed-brake lever is, as the First Officer judges it.</summary>
public enum SpeedbrakeLeverPosition
{
    /// <summary>Not read yet. Never ticks, never un-ticks, never clicked blind.</summary>
    Unknown,
    /// <summary>Anything short of ARM (a hardware axis parks the 777 at 22 for DOWN).</summary>
    Down,
    /// <summary>Exactly at ARM, within the ARM row's own exact tolerance.</summary>
    Armed,
    /// <summary>Anything past ARM: the spoilers are up.</summary>
    Deployed,
}

/// <summary>What an arm action may do with the lever as it is.</summary>
public enum SpeedbrakeArmDecision
{
    /// <summary>Armed already (and, where the aircraft has one, the ARMED light lit): no click.</summary>
    AlreadyArmed,
    /// <summary>Click / write ARM.</summary>
    Arm,
    /// <summary>The speed brake is extended: clicking ARM would retract it. Never touch it.</summary>
    LeaveAlone,
    /// <summary>The lever has not been read: never click a lever nobody can see.</summary>
    Unreadable,
}

/// <summary>One aircraft's speed-brake lever: main's detent table, its settle tolerance, and the
/// key the First Officer reads it from.</summary>
public sealed record SpeedbrakeLeverTable(IReadOnlyList<PmdgLeverDetent> Detents, double SettleTolerance, string LeverKey)
{
    /// <summary>The ARM detent's rest value (every table's second row).</summary>
    public double ArmValue => Detents[1].Value;
}

/// <summary>
/// The First Officer's speed-brake truth for the PMDG 737, PMDG 777 and iFly 737 MAX, judged on
/// main's measured detent tables (PR #261, 2026-09-30, hydraulics pressurised). ARM is EXACT on
/// all three (one step past it the spoilers are already up), so "armed" is the lever AT ARM,
/// never a light alone: the 737's ARMED light stays lit to about 342 and the iFly's all the way
/// to 224, and the 777 SDK byte truncates 201-203 to exactly "armed". A deployed speed brake is
/// never moved: clicking ARM over it retracts it.
/// </summary>
public static class SpeedbrakeLeverState
{
    /// <summary>FO synthetic: the raw lever value, NaN until read (the PMDG evaluators).</summary>
    public const string LeverField = "FO_SPEEDBRAKE_LEVER";

    /// <summary>FO synthetic: 1 when the lever is exactly at ARM and the ARMED light is lit, 0
    /// when either is not, NaN while either is unknown (the 737 and iFly evaluators).</summary>
    public const string ArmedField = "FO_SPEEDBRAKE_ARMED";

    /// <summary>The verified-arm flow step key every Boeing executor intercepts.</summary>
    public const string ArmPseudoKey = "SPEEDBRAKE_ARM";

    /// <summary>Spoken when the First Officer leaves a deployed speed brake alone.</summary>
    public const string LeaveAloneText = "Speedbrake extended, not armed. Left as it is.";

    /// <summary>PMDG 737 NG3: L:switch_679_73X, read through main's MON_PMDG737_SpeedBrake key.</summary>
    public static readonly SpeedbrakeLeverTable Pmdg737 =
        new(PmdgSpeedBrakeLever.Ng3, PmdgSpeedBrakeLever.Ng3SettleTolerance, "MON_PMDG737_SpeedBrake");

    /// <summary>PMDG 777: L:switch_498_a, read through main's FCTL_Speedbrake key.</summary>
    public static readonly SpeedbrakeLeverTable Pmdg777 =
        new(PmdgSpeedBrakeLever.B777, PmdgSpeedBrakeLever.B777SettleTolerance, "FCTL_Speedbrake");

    /// <summary>iFly 737 MAX: the SDK field Spoiler_Lever_Status.</summary>
    public static readonly SpeedbrakeLeverTable IFly737 =
        new(IFly737SpeedBrakeLever.Detents, IFly737SpeedBrakeLever.SettleTolerance, IFly737SpeedBrakeLever.FieldName);

    /// <summary>Where the lever is: main's PositionIndex rule (a detent within tolerance, DOWN for
    /// anything short of ARM) mapped to four answers.</summary>
    public static SpeedbrakeLeverPosition Classify(SpeedbrakeLeverTable table, double lever)
    {
        if (double.IsNaN(lever)) return SpeedbrakeLeverPosition.Unknown;
        return PmdgSpeedBrakeLever.PositionIndex(table.Detents, lever, table.SettleTolerance) switch
        {
            0 => SpeedbrakeLeverPosition.Down,
            1 => SpeedbrakeLeverPosition.Armed,
            _ => SpeedbrakeLeverPosition.Deployed,
        };
    }

    /// <summary>The <see cref="ArmedField"/> value: the lever exactly at ARM AND the ARMED light lit
    /// (above 0.5: the iFly's DIM counts). NaN while either is unknown.</summary>
    public static double ArmedValue(SpeedbrakeLeverTable table, double lever, double armedLight)
    {
        if (double.IsNaN(lever) || double.IsNaN(armedLight)) return double.NaN;
        return Classify(table, lever) == SpeedbrakeLeverPosition.Armed && armedLight > 0.5 ? 1 : 0;
    }

    /// <summary>What an arm action may do. <paramref name="armedLightLit"/> is true for an aircraft
    /// with no ARMED light (the 777: the lever alone decides); <paramref name="extendedLightLit"/>
    /// false for one with no EXTENDED light.</summary>
    public static SpeedbrakeArmDecision DecideArm(SpeedbrakeLeverPosition position, bool armedLightLit, bool extendedLightLit)
    {
        if (position == SpeedbrakeLeverPosition.Deployed || extendedLightLit) return SpeedbrakeArmDecision.LeaveAlone;
        if (position == SpeedbrakeLeverPosition.Unknown) return SpeedbrakeArmDecision.Unreadable;
        if (position == SpeedbrakeLeverPosition.Armed && armedLightLit) return SpeedbrakeArmDecision.AlreadyArmed;
        return SpeedbrakeArmDecision.Arm;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SpeedbrakeLeverStateTests"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/SpeedbrakeLeverState.cs tests/MSFSBlindAssist.Tests/FirstOfficer/SpeedbrakeLeverStateTests.cs
git commit -m "feat(fo): one speed-brake lever truth for the three Boeings, on main's tables

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Flow steps can be left alone (engine)

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/Models/FlowStep.cs` (add two properties after `RequiresStepSkipText`)
- Modify: `MSFSBlindAssist/FirstOfficer/FlowManager.cs` (new block between the `SkipCondition` block and the `RequiresStepId` block in `RunFlowAsync`)
- Test: `tests/MSFSBlindAssist.Tests/FirstOfficer/FlowManagerLeaveAloneTests.cs`

**Interfaces:**
- Produces: `FlowStep<TState>.LeaveAloneWhen` (`Func<TState, bool>?`) and `FlowStep<TState>.LeaveAloneText` (`string?`).

- [ ] **Step 1: Write the failing test**

Create `tests/MSFSBlindAssist.Tests/FirstOfficer/FlowManagerLeaveAloneTests.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// BEHAVIOUR tests of FlowManager's leave-alone rule (FlowStep.LeaveAloneWhen), on a real
/// FlowManager: when the aircraft's state says the First Officer must not act (arming a speed
/// brake that is already deployed would retract it), nothing is sent, the step's own reason is
/// spoken, and its checklist lines stay out of the completion latch. Harness as
/// FlowManagerStepDependencyTests (InterStepPauseMs is a real 2 s, so flows stay short).
/// </summary>
public class FlowManagerLeaveAloneTests
{
    private const string Reason = "Speedbrake extended, not armed. Left as it is.";

    private sealed class FakeState : IFoStateEvaluator
    {
        public volatile bool Deployed;
        public bool IsAvailable => true;
        public double GetValue(string field) => double.NaN;
        public bool IsOn(string field) => false;
        public bool IsPosition(string field, int position) => false;
        public void SetTakeoffFlaps(int flaps) { }
        public void SetEngineN2(double eng1N2, double eng2N2) { }
        public void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt) { }
    }

    private sealed class FakeExecutor : IFoActionExecutor
    {
        public ConcurrentQueue<string> Sent { get; } = new();
        public bool IsAvailable => true;
        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
        {
            Sent.Enqueue(step.EventName ?? "");
            return Task.FromResult(true);
        }
        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public FakeState State { get; } = new();
        public FakeExecutor Executor { get; } = new();
        public GatedSpeechCapture Speech { get; } = new();
        public FlowManager<FakeExecutor, FakeState> Flows { get; }
        public List<string> SkippedEvents { get; } = new();

        public Harness()
        {
            var checklist = new ChecklistManager<FakeExecutor, FakeState>(
                State, Executor, new List<ChecklistGroup<FakeExecutor, FakeState>>());
            Flows = new FlowManager<FakeExecutor, FakeState>(State, Executor, checklist, Speech);
            Flows.StepSkipped += (_, step, _) => { lock (SkippedEvents) SkippedEvents.Add(step.Id); };
        }

        public async Task RunAsync(FlowDefinition<FakeState> flow)
        {
            Flows.StartFlow(flow);
            var sw = Stopwatch.StartNew();
            while (Flows.IsRunning)
            {
                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), "flow did not finish");
                await Task.Delay(20);
            }
        }
    }

    // A SetSwitch step whose event is its id; it completes "ITEM_<id>" and "ITEM_<id>_CL".
    private static FlowStep<FakeState> Step(string id,
        Func<FakeState, bool>? leaveAloneWhen = null, string? leaveAloneText = null,
        Func<FakeState, bool>? skipWhen = null, string? requires = null) => new()
    {
        Id = id,
        Label = $"{id}: ARMED",
        ActionType = FlowStepActionType.SetSwitch,
        EventName = id,
        TargetValue = 1,
        PostActionDelayMs = 0,
        FailurePolicy = FlowStepFailurePolicy.Skip,
        CompletesChecklistItemId = "ITEM_" + id,
        AlsoCompletesChecklistItemIds = new[] { "ITEM_" + id + "_CL" },
        LeaveAloneWhen = leaveAloneWhen,
        LeaveAloneText = leaveAloneText,
        SkipCondition = skipWhen,
        RequiresStepId = requires,
    };

    private static FlowDefinition<FakeState> Flow(params FlowStep<FakeState>[] steps) => new()
    {
        Id = "TEST",
        Name = "Test",
        Steps = steps.ToList(),
    };

    [Fact]
    public async Task A_step_left_alone_sends_nothing_says_why_and_keeps_its_lines_open()
    {
        var h = new Harness();
        h.State.Deployed = true;

        await h.RunAsync(Flow(Step("A", s => s.Deployed, Reason), Step("B")));

        Assert.Equal(new[] { "B" }, h.Executor.Sent.ToArray());
        Assert.Contains(Reason, h.Speech.All);
        Assert.DoesNotContain("A: ARMED", h.Speech.All);
        Assert.DoesNotContain("Skipping: A: ARMED", h.Speech.All);
        Assert.Equal(new[] { "ITEM_A", "ITEM_A_CL" },
            h.Flows.UnfinishedChecklistItemIds.OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "A" }, h.SkippedEvents);
        Assert.Equal("Test flow complete", h.Speech.All.Last());
    }

    [Fact]
    public async Task A_step_whose_state_allows_it_runs_as_before()
    {
        var h = new Harness();
        h.State.Deployed = false;

        await h.RunAsync(Flow(Step("A", s => s.Deployed, Reason)));

        Assert.Equal(new[] { "A" }, h.Executor.Sent.ToArray());
        Assert.DoesNotContain(Reason, h.Speech.All);
        Assert.Empty(h.Flows.UnfinishedChecklistItemIds);
    }

    [Fact]
    public async Task Already_set_outranks_leave_alone()
    {
        var h = new Harness();

        await h.RunAsync(Flow(Step("A", _ => true, Reason, skipWhen: _ => true)));

        Assert.Empty(h.Executor.Sent);
        Assert.Contains("Already set: A: ARMED", h.Speech.All);
        Assert.DoesNotContain(Reason, h.Speech.All);
        Assert.Empty(h.Flows.UnfinishedChecklistItemIds);
    }

    [Fact]
    public async Task A_step_that_requires_a_left_alone_step_is_skipped_too()
    {
        var h = new Harness();

        await h.RunAsync(Flow(Step("A", _ => true, Reason), Step("B", requires: "A")));

        Assert.Empty(h.Executor.Sent);
        Assert.Equal(new[] { "ITEM_A", "ITEM_A_CL", "ITEM_B", "ITEM_B_CL" },
            h.Flows.UnfinishedChecklistItemIds.OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "A", "B" }, h.SkippedEvents);
    }

    [Fact]
    public async Task Without_its_own_text_it_says_skipping_its_label()
    {
        var h = new Harness();

        await h.RunAsync(Flow(Step("A", _ => true)));

        Assert.Empty(h.Executor.Sent);
        Assert.Contains("Skipping: A: ARMED", h.Speech.All);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FlowManagerLeaveAloneTests"`
Expected: build FAILS — `LeaveAloneWhen` / `LeaveAloneText` are not members of `FlowStep<TState>`.

- [ ] **Step 3: Add the properties to `FlowStep`**

In `MSFSBlindAssist/FirstOfficer/Models/FlowStep.cs`, directly after the `RequiresStepSkipText` property (and before the `// Helper` section), add:

```csharp
    // -----------------------------------------------------------------------
    // Leave alone (the aircraft's state says the First Officer must not act)
    // -----------------------------------------------------------------------

    /// <summary>
    /// If set and it returns true, the First Officer does NOT perform this step: nothing is sent,
    /// <see cref="LeaveAloneText"/> is spoken, and the step's checklist lines are kept out of the
    /// completion latch, so they keep mirroring the aircraft and tick themselves once the pilot
    /// does it. Checked after <see cref="SkipCondition"/> ("Already set" is the truer answer when
    /// the aircraft is already there) and before <see cref="RequiresStepId"/>; the step's id joins
    /// the run's skipped set, so a step that requires it is skipped too. The speed-brake arm
    /// steps use it for a speed brake that is already deployed, which clicking ARM would retract.
    /// </summary>
    public Func<TState, bool>? LeaveAloneWhen { get; set; }

    /// <summary>What is spoken when <see cref="LeaveAloneWhen"/> holds. Say what stays as it is.
    /// Defaults to "Skipping: {AnnounceText}".</summary>
    public string? LeaveAloneText { get; set; }
```

- [ ] **Step 4: Add the rule to `FlowManager.RunFlowAsync`**

In `MSFSBlindAssist/FirstOfficer/FlowManager.cs`, immediately BEFORE the comment block that starts `// A step that builds on an earlier step this run could not complete` (the `RequiresStepId` block), insert:

```csharp
            // A step the aircraft's state says the First Officer must NOT perform
            // (FlowStep.LeaveAloneWhen — arming a speed brake that is already deployed would
            // retract it): nothing is sent, the step's reason is spoken, and it is kept out of
            // the completion latch exactly like a skipped step, so its lines keep mirroring the
            // aircraft. After SkipCondition on purpose — "Already set" is the truer answer when
            // the aircraft is already there.
            if (step.LeaveAloneWhen != null && _state.IsAvailable && step.LeaveAloneWhen(_state))
            {
                foreach (var itemId in step.LinkedChecklistItemIds)
                    _unfinishedChecklistItemIds.Add(itemId);
                _skippedStepIds.Add(step.Id);
                StepSkipped?.Invoke(flow, step, i);
                _announcer.Announce(step.LeaveAloneText ?? $"Skipping: {step.AnnounceText}");
                if (i < flow.Steps.Count - 1)
                {
                    try { await Task.Delay(InterStepPauseMs, ct); }
                    catch (OperationCanceledException) { FlowCancelled?.Invoke(flow); return; }
                }
                continue;
            }

```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FlowManagerLeaveAloneTests|FullyQualifiedName~FlowManagerStepDependencyTests|FullyQualifiedName~FoFlowCompletionExclusionTests"`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/Models/FlowStep.cs MSFSBlindAssist/FirstOfficer/FlowManager.cs tests/MSFSBlindAssist.Tests/FirstOfficer/FlowManagerLeaveAloneTests.cs
git commit -m "feat(fo): a flow step can be left alone, with a spoken reason

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: A hand-tick can be left alone (checklist + window)

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/Models/ChecklistItem.cs` (two properties after `CheckAction`)
- Modify: `MSFSBlindAssist/FirstOfficer/ChecklistManager.cs` (`ToggleItem` gets an `out` overload)
- Modify: `MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs` (the AfterCheck handler that calls `ToggleItem`, ~line 620)
- Test: `tests/MSFSBlindAssist.Tests/FirstOfficer/ChecklistLeaveAloneTests.cs`

**Interfaces:**
- Produces: `ChecklistItem<TExec,TState>.LeaveAloneWhen` (`Func<TState, bool>?`), `.LeaveAloneText` (`string?`); `ChecklistManager.ToggleItem(string groupId, string itemId, out string? leftAloneText)` returning `bool?`. The existing two-argument `ToggleItem` keeps its signature and delegates.

- [ ] **Step 1: Write the failing test**

Create `tests/MSFSBlindAssist.Tests/FirstOfficer/ChecklistLeaveAloneTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The hand-tick half of the leave-alone rule (ChecklistItem.LeaveAloneWhen): ticking a line
/// whose aircraft state says the First Officer must not act runs nothing, leaves the line
/// unticked and hands back the reason at once — instead of running the action and saying
/// "Unable to complete" ten seconds later with no reason.
/// </summary>
public class ChecklistLeaveAloneTests
{
    private const string Reason = "Speedbrake extended, not armed. Left as it is.";

    private sealed class FakeExec : IFoActionExecutor
    {
        public bool IsAvailable => true;
        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step) => Task.FromResult(true);
        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    private sealed class FakeState : IFoStateEvaluator
    {
        public bool Deployed { get; set; }
        public Dictionary<string, double> Values { get; } = new();
        public bool IsAvailable => true;
        public double GetValue(string field) => Values.TryGetValue(field, out double v) ? v : double.NaN;
        public bool IsOn(string field) => GetValue(field) > 0.5;
        public bool IsPosition(string field, int position) => Math.Abs(GetValue(field) - position) < 0.5;
        public void SetTakeoffFlaps(int flaps) { }
        public void SetEngineN2(double eng1N2, double eng2N2) { }
        public void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt) { }
    }

    private sealed class Rig
    {
        public int ActionsRun;
        public List<string> Failures { get; } = new();
        public FakeState State { get; } = new();
        public ChecklistGroup<FakeExec, FakeState> Group { get; }
        public ChecklistManager<FakeExec, FakeState> Mgr { get; }
        public ChecklistItem<FakeExec, FakeState> Item => Group.Items[0];

        public Rig(string? text = Reason)
        {
            var item = new ChecklistItem<FakeExec, FakeState>
            {
                Id = "SPDBRK", GroupId = "G", Label = "Speedbrake: ARMED",
                Type = ChecklistItemType.AutoDetectable,
                AutoCompleteAllowed = true,
                ManualCompletionAllowed = true,
                StateFieldName = "F1",
                StateCondition = v => v > 0.5,
                RevertBehavior = RevertBehavior.RevertToState,
                CheckAction = (_, _) => { ActionsRun++; return Task.CompletedTask; },
                LeaveAloneWhen = s => s.Deployed,
                LeaveAloneText = text,
            };
            Group = new ChecklistGroup<FakeExec, FakeState> { Id = "G", Name = "G", Items = new() { item } };
            Mgr = new ChecklistManager<FakeExec, FakeState>(State, new FakeExec(), new() { Group });
            Mgr.ItemActionFailed += (_, i) => Failures.Add(i.Id);
            State.Values["F1"] = 0;
        }
    }

    [Fact]
    public void A_tick_on_a_line_whose_state_says_leave_it_alone_runs_nothing_and_says_why()
    {
        var r = new Rig();
        r.State.Deployed = true;

        bool? result = r.Mgr.ToggleItem("G", "SPDBRK", out string? why);

        Assert.False(result);
        Assert.False(r.Item.IsChecked);
        Assert.Equal(0, r.ActionsRun);
        Assert.Equal(Reason, why);
        Assert.False(r.Item.AwaitingActionConfirmation);

        // Nothing is owed, so no "Unable to complete" follows later either.
        r.Item.LastManualCheckUtc = DateTime.UtcNow - TimeSpan.FromSeconds(11);
        r.Mgr.EvaluateAutoDetection();
        Assert.Empty(r.Failures);
    }

    [Fact]
    public void Otherwise_the_tick_runs_its_action_as_before()
    {
        var r = new Rig();
        r.State.Deployed = false;

        bool? result = r.Mgr.ToggleItem("G", "SPDBRK", out string? why);

        Assert.True(result);
        Assert.True(r.Item.IsChecked);
        Assert.Equal(1, r.ActionsRun);
        Assert.Null(why);
    }

    [Fact]
    public void Unticking_is_never_refused()
    {
        var r = new Rig();
        r.Item.IsChecked = true;
        r.State.Deployed = true;

        bool? result = r.Mgr.ToggleItem("G", "SPDBRK", out string? why);

        Assert.False(result);
        Assert.False(r.Item.IsChecked);
        Assert.Null(why);
    }

    [Fact]
    public void The_two_argument_overload_refuses_the_same_way()
    {
        var r = new Rig();
        r.State.Deployed = true;

        Assert.False(r.Mgr.ToggleItem("G", "SPDBRK"));
        Assert.False(r.Item.IsChecked);
        Assert.Equal(0, r.ActionsRun);
    }

    [Fact]
    public void Without_its_own_text_it_says_skipping_the_label()
    {
        var r = new Rig(text: null);
        r.State.Deployed = true;

        r.Mgr.ToggleItem("G", "SPDBRK", out string? why);

        Assert.Equal("Skipping: Speedbrake: ARMED", why);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ChecklistLeaveAloneTests"`
Expected: build FAILS — `LeaveAloneWhen` is not a member of `ChecklistItem`, and there is no three-argument `ToggleItem`.

- [ ] **Step 3: Add the properties to `ChecklistItem`**

In `MSFSBlindAssist/FirstOfficer/Models/ChecklistItem.cs`, directly after the `CheckAction` property, add:

```csharp

    /// <summary>
    /// If set and it returns true when the pilot ticks this line ON by hand, the First Officer
    /// does NOT run <see cref="CheckAction"/>: the tick is refused (the line stays unticked) and
    /// <see cref="LeaveAloneText"/> is handed back for the window to speak. The hand-tick
    /// counterpart of FlowStep.LeaveAloneWhen (arming a speed brake that is already deployed
    /// would retract it). Unticking is never refused.
    /// </summary>
    public Func<TState, bool>? LeaveAloneWhen { get; set; }

    /// <summary>The reason spoken when <see cref="LeaveAloneWhen"/> refuses a tick. Say what stays
    /// as it is. Defaults to "Skipping: {Label}".</summary>
    public string? LeaveAloneText { get; set; }
```

- [ ] **Step 4: Add the `out` overload to `ChecklistManager.ToggleItem`**

In `MSFSBlindAssist/FirstOfficer/ChecklistManager.cs`, replace the start of the existing method:

```csharp
    public bool? ToggleItem(string groupId, string itemId)
    {
        var group = FindGroup(groupId);
        var item  = group?.Items.FirstOrDefault(i => i.Id == itemId);
        if (group == null || item == null || !item.ManualCompletionAllowed) return null;

        item.IsChecked = !item.IsChecked;
```

with:

```csharp
    public bool? ToggleItem(string groupId, string itemId) => ToggleItem(groupId, itemId, out _);

    /// <param name="leftAloneText">Set when a tick ON was refused because the item's
    /// <see cref="ChecklistItem{TExec,TState}.LeaveAloneWhen"/> holds: the item stays unchecked,
    /// nothing ran, and this is what to say. Null otherwise.</param>
    public bool? ToggleItem(string groupId, string itemId, out string? leftAloneText)
    {
        leftAloneText = null;
        var group = FindGroup(groupId);
        var item  = group?.Items.FirstOrDefault(i => i.Id == itemId);
        if (group == null || item == null || !item.ManualCompletionAllowed) return null;

        // A tick ON that the aircraft's state says the First Officer must not act on (the
        // speed-brake arm over a deployed speed brake): refuse it outright — no action, no
        // grace stamp, nothing owed — and hand back the reason, so it is spoken now rather
        // than as "Unable to complete" when the revert catches up ten seconds later.
        if (!item.IsChecked && item.LeaveAloneWhen != null && _state.IsAvailable && item.LeaveAloneWhen(_state))
        {
            leftAloneText = item.LeaveAloneText ?? $"Skipping: {item.Label}";
            return false;
        }

        item.IsChecked = !item.IsChecked;
```

(Leave the rest of the method body unchanged. Confirm the manager's state field is named `_state`; it is used by `EvaluateItemState`.)

- [ ] **Step 5: Speak the reason from the window as ONE utterance**

In `MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs`, find the AfterCheck handler block:

```csharp
        _suppressTreeEvents = true;
        if (_checklistMgr.ToggleItem(groupId, itemId) == null)
        {
            // Toggle rejected — revert the checkbox
            e.Node.Checked = item.IsChecked;
        }
        else
        {
            e.Node.Checked = item.IsChecked;
            string status = item.IsChecked ? "checked" : "unchecked";
            _announcer.AnnounceImmediate($"{item.Label}: {status}");
        }
        _suppressTreeEvents = false;
```

and replace it with:

```csharp
        _suppressTreeEvents = true;
        if (_checklistMgr.ToggleItem(groupId, itemId, out string? leftAloneText) == null)
        {
            // Toggle rejected — revert the checkbox
            e.Node.Checked = item.IsChecked;
        }
        else
        {
            e.Node.Checked = item.IsChecked;
            // A tick the First Officer refused (ChecklistItem.LeaveAloneWhen) speaks its reason
            // IN PLACE of the status line — one utterance, so the interrupting status line can
            // never cut the reason off.
            string status = item.IsChecked ? "checked" : "unchecked";
            _announcer.AnnounceImmediate(leftAloneText ?? $"{item.Label}: {status}");
        }
        _suppressTreeEvents = false;
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ChecklistLeaveAloneTests|FullyQualifiedName~FoFailedTickAnnouncementTests|FullyQualifiedName~FoFlowCompletionExclusionTests"`
Expected: all PASS. Then build the solution to prove the form compiles: `dotnet build MSFSBlindAssist.sln -c Debug` → 0 errors.

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/Models/ChecklistItem.cs MSFSBlindAssist/FirstOfficer/ChecklistManager.cs MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs tests/MSFSBlindAssist.Tests/FirstOfficer/ChecklistLeaveAloneTests.cs
git commit -m "feat(fo): a hand-tick can be left alone and says why at once

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: PMDG 777 — read L:switch_498_a, verified arm, leave a deployed lever alone

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/Pmdg777SpeedbrakeLever.cs` (rewrite)
- Modify: `MSFSBlindAssist/FirstOfficer/AircraftStateEvaluator.cs` (777 evaluator)
- Modify: `MSFSBlindAssist/FirstOfficer/Pmdg777FoProfile.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/AircraftActionExecutor.cs` (777 executor)
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG777FlowDefinitions.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG777ChecklistDefinitions.cs`
- Test (new): `tests/MSFSBlindAssist.Tests/FirstOfficer/Pmdg777SpeedbrakeLeverReadTests.cs`
- Test (edit): `tests/MSFSBlindAssist.Tests/FirstOfficer/Pmdg777FlowOrderingTests.cs`, `tests/MSFSBlindAssist.Tests/FoPr160ProcedureFixTests.cs`, `tests/MSFSBlindAssist.Tests/FirstOfficer/Pmdg777FlowChecklistLinkTests.cs`

**Interfaces:**
- Consumes: Task 1 (`SpeedbrakeLeverState`, `SpeedbrakeLeverPosition`, `SpeedbrakeArmDecision`), Task 2 (`FlowStep.LeaveAloneWhen/LeaveAloneText`), Task 3 (`ChecklistItem.LeaveAloneWhen/LeaveAloneText`).
- Produces: `Pmdg777SpeedbrakeLever.LeverField` (= `SpeedbrakeLeverState.LeverField`), `DownValue`/`ArmedValue`/`HalfDeployedValue`/`UpValue` (now `double` properties from main's table), `Position/IsDown/IsArmed/IsDeployed(double)`; `AircraftStateEvaluator.SetCachedValueSource(Func<string, double?>?)`, `SpeedbrakeLeverPos()`; `AircraftActionExecutor.ArmSpeedbrakeAsync()`.

- [ ] **Step 1: Write the failing tests**

(a) Create `tests/MSFSBlindAssist.Tests/FirstOfficer/Pmdg777SpeedbrakeLeverReadTests.cs`:

```csharp
using MSFSBlindAssist.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The PMDG 777 First Officer reads the speed-brake lever from main's L:switch_498_a (key
/// FCTL_Speedbrake, 0 / 200 / 300 / 400) through SimConnect's cache, never the SDK's
/// FCTL_Speedbrake_Lever byte, which truncates 201-203 to "armed" and an axis DOWN at 22 to 5.
/// </summary>
public class Pmdg777SpeedbrakeLeverReadTests
{
    private static AircraftStateEvaluator With(double? lever)
    {
        var eval = new AircraftStateEvaluator();
        eval.SetCachedValueSource(key => key == SpeedbrakeLeverState.Pmdg777.LeverKey ? lever : null);
        return eval;
    }

    [Fact]
    public void The_lever_field_reads_mains_L_var_from_the_cache() =>
        Assert.Equal(201.0, With(201).GetValue(Pmdg777SpeedbrakeLever.LeverField));

    [Fact]
    public void An_unread_lever_is_NaN()
    {
        Assert.True(double.IsNaN(With(null).GetValue(Pmdg777SpeedbrakeLever.LeverField)));
        Assert.True(double.IsNaN(new AircraftStateEvaluator().GetValue(Pmdg777SpeedbrakeLever.LeverField)));
    }

    [Theory]
    [InlineData(0, true, false, false)]
    [InlineData(22, true, false, false)]    // hardware axis DOWN
    [InlineData(200, false, true, false)]
    [InlineData(201, false, false, true)]   // spoilers 34 percent up
    [InlineData(400, false, false, true)]
    public void Down_armed_and_deployed_come_from_the_lever(double lever, bool down, bool armed, bool deployed)
    {
        var eval = With(lever);
        Assert.Equal(down, eval.IsSpeedbrakeDown());
        Assert.Equal(armed, eval.IsSpeedbrakeArmed());
        Assert.Equal(deployed, eval.IsSpeedbrakeDeployed());
    }

    [Fact]
    public void An_unread_lever_is_none_of_down_armed_or_deployed()
    {
        var eval = With(null);
        Assert.False(eval.IsSpeedbrakeDown());
        Assert.False(eval.IsSpeedbrakeArmed());
        Assert.False(eval.IsSpeedbrakeDeployed());
    }
}
```

(b) In `tests/MSFSBlindAssist.Tests/FirstOfficer/Pmdg777FlowOrderingTests.cs`, replace EVERYTHING from the line `    // 1. Speedbrake lever scale` section's opening `// =====…` line through the end of the test `AfterLandingChecklist_speedbrake_down_uses_the_same_scale` (i.e. up to, not including, the `// =====…` line that opens `// 2. Seat belt signs`) with:

```csharp
    // =====================================================================
    // 1. Speedbrake lever scale
    //
    // The First Officer reads the lever from L:switch_498_a (main's FCTL_Speedbrake key), never
    // the SDK's FCTL_Speedbrake_Lever byte. Measured 2026-09-30 (PR #261, hydraulics
    // pressurised): the lever rests at DOWN 0 / ARM 200 / 50 percent 300 / UP 400; the byte is
    // that value / 4, TRUNCATED, so a lever at 201-203 (spoilers already 34 percent up) read 50,
    // "armed", and a hardware axis's DOWN at 22 read 5, "not down". The SDK header's
    // "25: ARMED" is wrong on both scales. The values come from main's PmdgSpeedBrakeLever.B777.
    // =====================================================================

    [Theory]
    [InlineData(0, true)]
    [InlineData(22, true)]     // a hardware axis parks DOWN here
    [InlineData(199, true)]    // short of ARM is not armed
    [InlineData(200, false)]
    [InlineData(300, false)]
    [InlineData(400, false)]
    public void SpeedbrakeDown_is_anything_short_of_ARM(double lever, bool expected) =>
        Assert.Equal(expected, Pmdg777SpeedbrakeLever.IsDown(lever));

    [Theory]
    [InlineData(200, true)]
    [InlineData(199, false)]
    [InlineData(201, false)]   // spoilers already 34 percent up
    [InlineData(50, false)]    // the old SDK-byte value
    [InlineData(0, false)]
    [InlineData(400, false)]
    public void SpeedbrakeArmed_is_exactly_the_ARM_detent(double lever, bool expected) =>
        Assert.Equal(expected, Pmdg777SpeedbrakeLever.IsArmed(lever));

    [Theory]
    [InlineData(201, true)]
    [InlineData(300, true)]
    [InlineData(400, true)]
    [InlineData(200, false)]
    [InlineData(0, false)]
    public void SpeedbrakeDeployed_is_anything_past_ARM(double lever, bool expected) =>
        Assert.Equal(expected, Pmdg777SpeedbrakeLever.IsDeployed(lever));

    [Fact]
    public void An_unread_lever_is_none_of_the_three()
    {
        Assert.False(Pmdg777SpeedbrakeLever.IsDown(double.NaN));
        Assert.False(Pmdg777SpeedbrakeLever.IsArmed(double.NaN));
        Assert.False(Pmdg777SpeedbrakeLever.IsDeployed(double.NaN));
    }

    [Fact]
    public void The_measured_detents_come_from_mains_table()
    {
        Assert.Equal(0.0, Pmdg777SpeedbrakeLever.DownValue);
        Assert.Equal(200.0, Pmdg777SpeedbrakeLever.ArmedValue);
        Assert.Equal(300.0, Pmdg777SpeedbrakeLever.HalfDeployedValue);
        Assert.Equal(400.0, Pmdg777SpeedbrakeLever.UpValue);
    }

    [Fact]
    public void LandingChecklist_speedbrake_accepts_the_armed_lever()
    {
        var item = Item("LANDING_CL", "LDG_SPEEDBRAKE");
        Assert.Equal(Pmdg777SpeedbrakeLever.LeverField, item.StateFieldName);
        Assert.True(item.EvaluateState(Pmdg777SpeedbrakeLever.ArmedValue),
            "ticking 'Speedbrake: ARMED' must not revert once the lever reaches ARM");
        Assert.False(item.EvaluateState(Pmdg777SpeedbrakeLever.DownValue));
        Assert.False(item.EvaluateState(201));
    }

    [Fact]
    public void LandingChecklist_speedbrake_leaves_a_deployed_lever_alone()
    {
        var item = Item("LANDING_CL", "LDG_SPEEDBRAKE");
        Assert.NotNull(item.LeaveAloneWhen);
        Assert.Equal(SpeedbrakeLeverState.LeaveAloneText, item.LeaveAloneText);
    }

    [Fact]
    public void LandingFlow_speedbrake_goes_through_the_verified_arm()
    {
        var arm = Step("LANDING", "LD_SPEEDBRAKE_ARM");
        Assert.Equal(SpeedbrakeLeverState.ArmPseudoKey, arm.EventName);
        Assert.Equal(Pmdg777SpeedbrakeLever.LeverField, arm.VerifyFieldName);
        Assert.NotNull(arm.VerifyCondition);
        Assert.True(arm.VerifyCondition!(Pmdg777SpeedbrakeLever.ArmedValue),
            "the Landing flow must not announce 'Skipping' on a lever it just armed");
        Assert.False(arm.VerifyCondition!(Pmdg777SpeedbrakeLever.DownValue));
    }

    [Fact]
    public void LandingFlow_never_clicks_ARM_over_a_deployed_lever()
    {
        // Clicking the ARM detent retracts a lever the pilot has raised. Armed is "Already
        // set"; deployed is left alone with its reason — never "Already set".
        var arm = Step("LANDING", "LD_SPEEDBRAKE_ARM");
        Assert.NotNull(arm.SkipCondition);
        Assert.NotNull(arm.LeaveAloneWhen);
        Assert.Equal(SpeedbrakeLeverState.LeaveAloneText, arm.LeaveAloneText);
    }

    [Fact]
    public void AfterLandingChecklist_speedbrake_down_uses_the_same_scale()
    {
        var item = Item("AFTER_LANDING", "AL_SPEEDBRAKE");
        Assert.Equal(Pmdg777SpeedbrakeLever.LeverField, item.StateFieldName);
        Assert.True(item.EvaluateState(Pmdg777SpeedbrakeLever.DownValue));
        Assert.True(item.EvaluateState(22), "a hardware axis parks DOWN at 22");
        Assert.False(item.EvaluateState(Pmdg777SpeedbrakeLever.ArmedValue));
    }

```

(c) In `tests/MSFSBlindAssist.Tests/FoPr160ProcedureFixTests.cs`, in `Pmdg777_HasALandingFlow_ThatArmsTheSpeedbrake`, replace

```csharp
        Assert.Equal("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM", arm.EventName);
        Assert.Equal("FCTL_Speedbrake_Lever", arm.VerifyFieldName);
```
with
```csharp
        Assert.Equal(SpeedbrakeLeverState.ArmPseudoKey, arm.EventName);
        Assert.Equal(Pmdg777SpeedbrakeLever.LeverField, arm.VerifyFieldName);
```
and in `Pmdg777_LandingChecklistSpeedbrake_ActuallyArms` replace `Assert.Equal("FCTL_Speedbrake_Lever", item.StateFieldName);` with `Assert.Equal(Pmdg777SpeedbrakeLever.LeverField, item.StateFieldName);`. Then add, right after that test:

```csharp
    // The 777 flow's verified arm is intercepted before the event table is consulted, so it
    // must never collide with a real PMDG event name.
    [Fact]
    public void Pmdg777_SpeedbrakePseudoKey_IsNotARealPmdgEvent()
    {
        Assert.False(MSFSBlindAssist.Aircraft.PMDG777Definition.EventIds
            .ContainsKey(SpeedbrakeLeverState.ArmPseudoKey));
    }
```

(d) In `tests/MSFSBlindAssist.Tests/FirstOfficer/Pmdg777FlowChecklistLinkTests.cs`, replace the two speed-brake entries at the end of `BuildEventFields()`:

```csharp
        // Speed brake detent click events (the lever is an analog 0-100 position).
        m["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM"] = new (string, Func<int, double>)[]
        { ("FCTL_Speedbrake_Lever", _ => Pmdg777SpeedbrakeLever.ArmedValue) };
        m["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN"] = new (string, Func<int, double>)[]
        { ("FCTL_Speedbrake_Lever", _ => Pmdg777SpeedbrakeLever.DownValue) };
```
with
```csharp
        // Speed brake: the verified arm (SPEEDBRAKE_ARM → ArmSpeedbrakeAsync) and the detent
        // click events drive main's lever L-var, read through FO_SPEEDBRAKE_LEVER.
        m[SpeedbrakeLeverState.ArmPseudoKey] = new (string, Func<int, double>)[]
        { (Pmdg777SpeedbrakeLever.LeverField, _ => Pmdg777SpeedbrakeLever.ArmedValue) };
        m["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM"] = new (string, Func<int, double>)[]
        { (Pmdg777SpeedbrakeLever.LeverField, _ => Pmdg777SpeedbrakeLever.ArmedValue) };
        m["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN"] = new (string, Func<int, double>)[]
        { (Pmdg777SpeedbrakeLever.LeverField, _ => Pmdg777SpeedbrakeLever.DownValue) };
```
and in `Every_written_event_is_in_the_777_event_table`, add `SpeedbrakeLeverState.ArmPseudoKey` to the `pseudoKeys` set:
```csharp
        { "OXY_TEST_CAPT", "OXY_TEST_FO", "FIRE_OVHT_TEST", "TCAS_TEST", "WXR_TEST", "EMER_EXIT_LIGHTS",
          SpeedbrakeLeverState.ArmPseudoKey };
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Pmdg777"`
Expected: build FAILS — `SetCachedValueSource`, `Pmdg777SpeedbrakeLever.LeverField`, `LeaveAloneWhen` on those steps/items do not exist yet.

- [ ] **Step 3: Rewrite `Pmdg777SpeedbrakeLever`**

Replace the whole of `MSFSBlindAssist/FirstOfficer/Pmdg777SpeedbrakeLever.cs` with:

```csharp
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// The PMDG 777 speed-brake lever as the First Officer reads it: main's <c>L:switch_498_a</c>
/// (key <c>FCTL_Speedbrake</c>, 0 / 200 / 300 / 400), judged on main's
/// <see cref="PmdgSpeedBrakeLever.B777"/> table through <see cref="SpeedbrakeLeverState"/>, so
/// the two can never drift apart again. Never the SDK's <c>FCTL_Speedbrake_Lever</c> byte: it is
/// that value / 4, TRUNCATED, so a lever at 201-203 (spoilers already 34 percent up) reads
/// exactly 50, "armed", and a hardware axis's DOWN at 22 reads 5, "not down" (measured
/// 2026-09-30, docs/pmdg-777.md).
/// </summary>
public static class Pmdg777SpeedbrakeLever
{
    /// <summary>The evaluator field the lever is read through.</summary>
    public const string LeverField = SpeedbrakeLeverState.LeverField;

    public static double DownValue => PmdgSpeedBrakeLever.B777[0].Value;
    public static double ArmedValue => PmdgSpeedBrakeLever.B777[1].Value;
    public static double HalfDeployedValue => PmdgSpeedBrakeLever.B777[2].Value;
    public static double UpValue => PmdgSpeedBrakeLever.B777[^1].Value;

    public static SpeedbrakeLeverPosition Position(double lever) =>
        SpeedbrakeLeverState.Classify(SpeedbrakeLeverState.Pmdg777, lever);

    /// <summary>Anything short of ARM (a hardware axis parks DOWN at 22). False while unread.</summary>
    public static bool IsDown(double lever) => Position(lever) == SpeedbrakeLeverPosition.Down;

    /// <summary>Exactly at ARM. False while unread.</summary>
    public static bool IsArmed(double lever) => Position(lever) == SpeedbrakeLeverPosition.Armed;

    /// <summary>Anything past ARM. False while unread.</summary>
    public static bool IsDeployed(double lever) => Position(lever) == SpeedbrakeLeverPosition.Deployed;
}
```

- [ ] **Step 4: 777 evaluator reads the lever from SimConnect's cache**

In `MSFSBlindAssist/FirstOfficer/AircraftStateEvaluator.cs`:

(i) After the `SetDataManager` method, add:
```csharp

    // Values the PMDG CDA does not carry come from SimConnect's variable cache (a
    // ConcurrentDictionary, safe from the flow's pool threads) — the speed-brake lever's main
    // L-var. Set by Pmdg777FoProfile.BindDataManager; a test injects its own.
    private Func<string, double?>? _cachedValue;

    public void SetCachedValueSource(Func<string, double?>? source) => _cachedValue = source;
```
(ii) In `GetValue`, right after the two `FO_ENG1_N2` / `FO_ENG2_N2` lines, add:
```csharp

        // The speed-brake lever comes from SimConnect too (main's L:switch_498_a) — served
        // ahead of the CdaReady gate like N2.
        if (fieldName == SpeedbrakeLeverState.LeverField) return SpeedbrakeLeverPos();
```
(iii) Replace the speed-brake block:
```csharp
    // SpeedBrakeLever is an ANALOG 0–100 position (0 DOWN, 25 ARMED, 26–100 DEPLOYED),
    // NOT a detent index — see Pmdg777SpeedbrakeLever, which owns the scale. The comment
    // that used to sit here said "0=Down, 1=Armed, 2–7 = deployed positions", and every
    // 777 speedbrake condition was written against it.
    public double SpeeedbrakeLeverPos()   => GetValue("FCTL_Speedbrake_Lever");
    public bool IsSpeedbrakeDown()        => Pmdg777SpeedbrakeLever.IsDown(SpeeedbrakeLeverPos());
    public bool IsSpeedbrakeArmed()       => Pmdg777SpeedbrakeLever.IsArmed(SpeeedbrakeLeverPos());
    public bool IsSpeedbrakeDeployed()    => Pmdg777SpeedbrakeLever.IsDeployed(SpeeedbrakeLeverPos());
```
with:
```csharp
    // The speed-brake lever is main's L:switch_498_a (0 / 200 / 300 / 400), never the SDK's
    // truncating FCTL_Speedbrake_Lever byte — see Pmdg777SpeedbrakeLever. NaN until read.
    public double SpeedbrakeLeverPos()    => _cachedValue?.Invoke(SpeedbrakeLeverState.Pmdg777.LeverKey) ?? double.NaN;
    public bool IsSpeedbrakeDown()        => Pmdg777SpeedbrakeLever.IsDown(SpeedbrakeLeverPos());
    public bool IsSpeedbrakeArmed()       => Pmdg777SpeedbrakeLever.IsArmed(SpeedbrakeLeverPos());
    public bool IsSpeedbrakeDeployed()    => Pmdg777SpeedbrakeLever.IsDeployed(SpeedbrakeLeverPos());
```
(`SpeeedbrakeLeverPos` has no other callers; `grep -rn SpeeedbrakeLeverPos MSFSBlindAssist` must return nothing afterwards. A comment mentioning it in a test file is updated in Task 7.)

- [ ] **Step 5: Bind the cache in the 777 profile**

In `MSFSBlindAssist/FirstOfficer/Pmdg777FoProfile.cs`, replace
```csharp
    public void BindDataManager(AircraftStateEvaluator state, SimConnectManager sc)
        => state.SetDataManager(sc.PMDGDataManager as PMDG777DataManager);
```
with
```csharp
    public void BindDataManager(AircraftStateEvaluator state, SimConnectManager sc)
    {
        state.SetDataManager(sc.PMDGDataManager as PMDG777DataManager);
        state.SetCachedValueSource(sc.GetCachedVariableValue);
    }
```

- [ ] **Step 6: 777 executor — backstop and verified arm**

In `MSFSBlindAssist/FirstOfficer/AircraftActionExecutor.cs` (the 777 executor):

(i) In `ExecuteStepAsync`'s pseudo-key `switch (step.EventName)`, add a case after `case "OXY_TEST_FO": ...`:
```csharp
                case SpeedbrakeLeverState.ArmPseudoKey: return ArmSpeedbrakeAsync();
```
(ii) In `DispatchCoreAsync`, immediately BEFORE the `// RULING A (§6): suppress a center-pump ON write…` comment, add:
```csharp
        // Never click ARM over a speed brake that is not known to be DOWN: over a deployed one
        // the ARM detent RETRACTS it, and an unread lever may be deployed. An armed one needs
        // no click. This guards every caller (the verified arm, the checklist tick).
        if (eventName == SpeedbrakeArmEvent)
        {
            var decision = SpeedbrakeArmDecisionNow();
            if (decision == SpeedbrakeArmDecision.AlreadyArmed) return true;
            if (decision != SpeedbrakeArmDecision.Arm)
            {
                Log.Debug("FirstOfficer", $"777 speedbrake ARM not sent: {decision}.");
                return false;
            }
        }

```
(iii) Replace the two speed-brake convenience lines:
```csharp
    // Speedbrake lever
    public bool SetSpeedbrakeDown()  => ExecuteSingle("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN", null, false, true);
    public bool SetSpeedbrakeArmed() => ExecuteSingle("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM", null, false, true);
```
with:
```csharp
    // Speedbrake lever
    public bool SetSpeedbrakeDown()  => ExecuteSingle("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN", null, false, true);
    public bool SetSpeedbrakeArmed() => ExecuteSingle(SpeedbrakeArmEvent, null, false, true);

    private const string SpeedbrakeArmEvent = "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM";

    /// <summary>The verified arm waits this long for the lever to reach ARM: it takes about five
    /// seconds from DOWN (ten end to end, measured 2026-09-30) and rides the 1 Hz L-var batch.
    /// A flow step's own verify reads only 600 ms after dispatch, which is why this waits.</summary>
    public const int SpeedbrakeArmVerifyMs = 8000;
    private const int SpeedbrakeArmPollMs = 250;

    private double SpeedbrakeLever() =>
        _simConnect?.GetCachedVariableValue(SpeedbrakeLeverState.Pmdg777.LeverKey) ?? double.NaN;

    // The 777 SDK has no speed-brake ARMED or EXTENDED light: the lever alone decides.
    private SpeedbrakeArmDecision SpeedbrakeArmDecisionNow() =>
        SpeedbrakeLeverState.DecideArm(Pmdg777SpeedbrakeLever.Position(SpeedbrakeLever()),
            armedLightLit: true, extendedLightLit: false);

    /// <summary>
    /// Speed brake to ARM, verified: an armed lever is left as it is (true), a deployed or unread
    /// one is never clicked (false — the flow's leave-alone rule has normally spoken already),
    /// otherwise ARM is clicked and the lever must reach ARM within
    /// <see cref="SpeedbrakeArmVerifyMs"/>. The wait runs outside the dispatch gate so a hand-tick
    /// elsewhere is not held behind it.
    /// </summary>
    public async Task<bool> ArmSpeedbrakeAsync()
    {
        if (!IsAvailable) return false;
        var decision = SpeedbrakeArmDecisionNow();
        if (decision == SpeedbrakeArmDecision.AlreadyArmed) return true;
        if (decision != SpeedbrakeArmDecision.Arm)
        {
            Log.Debug("FirstOfficer", $"777 speedbrake arm not attempted: {decision}.");
            return false;
        }
        if (!await DispatchAsync(SpeedbrakeArmEvent, null, false, true)) return false;

        var deadline = DateTime.UtcNow.AddMilliseconds(SpeedbrakeArmVerifyMs);
        while (DateTime.UtcNow < deadline)
        {
            if (Pmdg777SpeedbrakeLever.IsArmed(SpeedbrakeLever())) return true;
            await Task.Delay(SpeedbrakeArmPollMs);
        }
        bool armed = Pmdg777SpeedbrakeLever.IsArmed(SpeedbrakeLever());
        if (!armed) Log.Debug("FirstOfficer", $"777 speedbrake arm: the lever did not reach ARM ({SpeedbrakeLever()}).");
        return armed;
    }
```

- [ ] **Step 7: 777 flow and checklist wiring**

(i) In `MSFSBlindAssist/FirstOfficer/PMDG777FlowDefinitions.cs`, replace the Landing step:
```csharp
            Skip(SW("LD_SPEEDBRAKE_ARM",   "Speedbrake: ARM",   "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM", null,
               true, "FCTL_Speedbrake_Lever", Pmdg777SpeedbrakeLever.IsArmed, "LDG_SPEEDBRAKE"),
                s => s.IsSpeedbrakeArmed() || s.IsSpeedbrakeDeployed()),
```
with:
```csharp
            LeaveAlone(Skip(SW("LD_SPEEDBRAKE_ARM", "Speedbrake: ARM", SpeedbrakeLeverState.ArmPseudoKey, null,
               true, Pmdg777SpeedbrakeLever.LeverField, Pmdg777SpeedbrakeLever.IsArmed, "LDG_SPEEDBRAKE"),
                s => s.IsSpeedbrakeArmed()),
                s => s.IsSpeedbrakeDeployed(), SpeedbrakeLeverState.LeaveAloneText),
```
and rewrite the comment above it to say: the step goes through the verified arm (`AircraftActionExecutor.ArmSpeedbrakeAsync`, which waits for the lever; the flow's own verify reads only 600 ms after dispatch), an armed lever is "Already set", and a DEPLOYED one is left alone with its reason, because clicking ARM over it retracts it. Remove the sentences about "the SDK's ARMED value (25)" and "v > 0.5 && v < 1.5".
Add a helper next to `Skip(…)` at the bottom of the file:
```csharp
    // A step the First Officer must not perform in the aircraft's current state
    // (FlowStep.LeaveAloneWhen): nothing is sent, `text` is spoken, its lines stay open.
    private static FlowStep<AircraftStateEvaluator> LeaveAlone(FlowStep<AircraftStateEvaluator> step,
        Func<AircraftStateEvaluator, bool> when, string text)
    {
        step.LeaveAloneWhen = when;
        step.LeaveAloneText = text;
        return step;
    }
```
(ii) In `MSFSBlindAssist/FirstOfficer/PMDG777ChecklistDefinitions.cs`, replace:
```csharp
            Auto("LDG_SPEEDBRAKE", "LANDING_CL", "Speedbrake: ARMED",
                "FCTL_Speedbrake_Lever", Pmdg777SpeedbrakeLever.IsArmed,
                action: (e, s) => { if (!s.IsSpeedbrakeDeployed()) e.SetSpeedbrakeArmed(); }),
```
with:
```csharp
            LeaveAlone(Auto("LDG_SPEEDBRAKE", "LANDING_CL", "Speedbrake: ARMED",
                Pmdg777SpeedbrakeLever.LeverField, Pmdg777SpeedbrakeLever.IsArmed,
                action: (e, _) => e.SetSpeedbrakeArmed()),
                s => s.IsSpeedbrakeDeployed(), SpeedbrakeLeverState.LeaveAloneText),
```
and replace:
```csharp
            Auto("AL_SPEEDBRAKE", "AFTER_LANDING", "Speed Brake lever: DOWN",
                "FCTL_Speedbrake_Lever", Pmdg777SpeedbrakeLever.IsDown,
```
with:
```csharp
            Auto("AL_SPEEDBRAKE", "AFTER_LANDING", "Speed Brake lever: DOWN",
                Pmdg777SpeedbrakeLever.LeverField, Pmdg777SpeedbrakeLever.IsDown,
```
Rewrite the comment above `LDG_SPEEDBRAKE` to: ticking it arms the lever; it reads main's lever L-var (exactly ARM, never the truncating SDK byte); a tick over a DEPLOYED lever is refused with its reason (the executor never clicks ARM over one either). Remove the "(25)" / "v > 0.5 && v < 1.5" history.
Add a helper next to `Reminder(…)` at the bottom of the file:
```csharp
    /// <summary>A line whose hand-tick the First Officer refuses in the aircraft's current state
    /// (ChecklistItem.LeaveAloneWhen), speaking <paramref name="text"/> instead.</summary>
    private static Item LeaveAlone(Item item, Func<AircraftStateEvaluator, bool> when, string text)
    {
        item.LeaveAloneWhen = when;
        item.LeaveAloneText = text;
        return item;
    }
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Pmdg777|FullyQualifiedName~FoPr160ProcedureFixTests|FullyQualifiedName~SpeedbrakeLeverStateTests"`
Expected: all PASS. Then `grep -rn '"FCTL_Speedbrake_Lever"' MSFSBlindAssist/FirstOfficer` → no matches.

- [ ] **Step 9: Commit**

```bash
git add -A MSFSBlindAssist/FirstOfficer tests/MSFSBlindAssist.Tests
git commit -m "fix(fo/777): judge the speed brake by main's lever L-var; verified arm; leave a deployed one alone

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: PMDG 737 — lever plus light, verified arm, leave a deployed one alone

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/AircraftStateEvaluator.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/Pmdg737FoProfile.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/AircraftActionExecutor.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/SpeedbrakeArmLadder.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737ChecklistDefinitions.cs`
- Test (new): `tests/MSFSBlindAssist.Tests/FirstOfficer/Pmdg737SpeedbrakeTruthTests.cs`
- Test (edit): `tests/MSFSBlindAssist.Tests/FoPr160ProcedureFixTests.cs`, `tests/MSFSBlindAssist.Tests/FirstOfficer/Pmdg737FlowChecklistLinkTests.cs`

**Interfaces:**
- Consumes: Tasks 1-3.
- Produces: 737 `AircraftStateEvaluator.SetCachedValueSource(Func<string,double?>?)`, `SpeedbrakeLever()`, `SpeedbrakePosition()`, `IsSpeedbrakeArmed()`, `IsSpeedbrakeDeployed()`; synthetic fields `SpeedbrakeLeverState.LeverField` and `SpeedbrakeLeverState.ArmedField`; `SpeedbrakeArmLadder.PseudoKey` now equals `SpeedbrakeLeverState.ArmPseudoKey`.

- [ ] **Step 1: Write the failing tests**

(a) Create `tests/MSFSBlindAssist.Tests/FirstOfficer/Pmdg737SpeedbrakeTruthTests.cs`:

```csharp
using System.Linq;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.FirstOfficer.PMDG737;
using Xunit;

using Pmdg737Eval = MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator;
using Pmdg737Flows = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737FlowDefinitions;
using Pmdg737Checklist = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737ChecklistDefinitions;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The PMDG 737 First Officer judges "Speedbrake: ARMED" by the lever exactly at ARM
/// (L:switch_679_73X = 100, main's MON_PMDG737_SpeedBrake) AND the ARMED light: the light alone
/// stays lit to about 342, so a speed brake partly up read as armed. A deployed speed brake is
/// left alone with its reason, never counted as done.
/// </summary>
public class Pmdg737SpeedbrakeTruthTests
{
    private static Pmdg737Eval With(double? lever)
    {
        var eval = new Pmdg737Eval();
        eval.SetCachedValueSource(key => key == SpeedbrakeLeverState.Pmdg737.LeverKey ? lever : null);
        return eval;
    }

    private static FlowStep<Pmdg737Eval> LandingArm() =>
        Pmdg737Flows.Build().Single(f => f.Id == "LANDING").Steps.Single(s => s.Id == "LD_SPDBRK");

    private static ChecklistItem<MSFSBlindAssist.FirstOfficer.PMDG737.AircraftActionExecutor, Pmdg737Eval> Line(string group, string id) =>
        Pmdg737Checklist.Build().Single(g => g.Id == group).Items.Single(i => i.Id == id);

    [Fact]
    public void The_lever_field_reads_mains_L_var_from_the_cache() =>
        Assert.Equal(150.0, With(150).GetValue(SpeedbrakeLeverState.LeverField));

    [Fact]
    public void An_unread_lever_is_NaN() =>
        Assert.True(double.IsNaN(With(null).GetValue(SpeedbrakeLeverState.LeverField)));

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(100.0, false)]
    [InlineData(101.0, true)]    // spoilers already 34 percent up
    [InlineData(337.0, true)]
    public void Deployed_is_anything_past_ARM(double lever, bool expected) =>
        Assert.Equal(expected, With(lever).IsSpeedbrakeDeployed());

    [Fact]
    public void Armed_stays_unknown_until_the_CDA_has_delivered_the_light()
    {
        // A lever at ARM alone never says armed: the ARMED light must agree, and with no CDA
        // snapshot the field is indeterminate, never a guess.
        var eval = With(100);
        Assert.True(double.IsNaN(eval.GetValue(SpeedbrakeLeverState.ArmedField)));
        Assert.False(eval.IsSpeedbrakeArmed());
    }

    [Fact]
    public void The_landing_arm_goes_through_the_verified_pseudo_key()
    {
        var step = LandingArm();
        Assert.Equal(SpeedbrakeLeverState.ArmPseudoKey, step.EventName);
        Assert.Equal(SpeedbrakeArmLadder.PseudoKey, step.EventName);
        Assert.Equal(SpeedbrakeLeverState.ArmedField, step.VerifyFieldName);
        Assert.Equal(new[] { "LDA_SPDBRK", "LDC_SPDBRK" }, step.LinkedChecklistItemIds.ToArray());
    }

    [Fact]
    public void An_armed_speed_brake_is_already_set_and_a_deployed_one_is_left_alone()
    {
        var step = LandingArm();
        Assert.NotNull(step.SkipCondition);
        Assert.NotNull(step.LeaveAloneWhen);
        Assert.Equal(SpeedbrakeLeverState.LeaveAloneText, step.LeaveAloneText);
        Assert.True(step.LeaveAloneWhen!(With(150)));
        Assert.False(step.LeaveAloneWhen!(With(0)));
        Assert.False(step.LeaveAloneWhen!(With(null)));  // unread goes to the arm, which refuses it
    }

    [Fact]
    public void Both_lines_read_the_armed_field_and_only_the_landing_group_line_arms()
    {
        var group = Line("LANDING", "LDA_SPDBRK");
        Assert.Equal(SpeedbrakeLeverState.ArmedField, group.StateFieldName);
        Assert.NotNull(group.CheckAction);
        Assert.NotNull(group.LeaveAloneWhen);
        Assert.Equal(SpeedbrakeLeverState.LeaveAloneText, group.LeaveAloneText);

        var readback = Line("LANDING_CL", "LDC_SPDBRK");
        Assert.Equal(SpeedbrakeLeverState.ArmedField, readback.StateFieldName);
        Assert.Null(readback.CheckAction);
    }
}
```

(b) In `tests/MSFSBlindAssist.Tests/FoPr160ProcedureFixTests.cs`:
- In `Pmdg737_LandingGroupSpeedbrake_AutoDetectsFromTheArmedAnnunciator` and `Pmdg737_LandingChecklistSpeedbrake_VerifiesButDoesNotActuate`, replace `Assert.Equal("MAIN_annunSPEEDBRAKE_ARMED", item.StateFieldName);` with `Assert.Equal(SpeedbrakeLeverState.ArmedField, item.StateFieldName);`, rename the first test to `Pmdg737_LandingGroupSpeedbrake_AutoDetectsFromTheLeverAndTheArmedLight`, and update the comment above them to say the lines read the lever exactly at ARM AND the ARMED light (the light alone stays lit to about 342).
- In `Pmdg737_LandingFlowSpeedbrake_GoesThroughTheVerifiedPseudoKey`, replace `Assert.Equal(SbLadder.ArmedField, step.VerifyFieldName);` with `Assert.Equal(SpeedbrakeLeverState.ArmedField, step.VerifyFieldName);`.

(c) In `tests/MSFSBlindAssist.Tests/FirstOfficer/Pmdg737FlowChecklistLinkTests.cs`, replace
```csharp
        // Closed-loop verified arm (ArmSpeedbrakeAsync) → the ARMED annunciator.
        m[SpeedbrakeArmLadder.PseudoKey] = new (string, Func<int, double>)[]
        { (SpeedbrakeArmLadder.ArmedField, _ => 1) };
```
with
```csharp
        // Closed-loop verified arm (ArmSpeedbrakeAsync) → the lever at ARM with the ARMED light.
        m[SpeedbrakeArmLadder.PseudoKey] = new (string, Func<int, double>)[]
        { (SpeedbrakeLeverState.ArmedField, _ => 1) };
```
(add `using MSFSBlindAssist.FirstOfficer;` if the file lacks it).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Pmdg737|FullyQualifiedName~FoPr160ProcedureFixTests"`
Expected: build FAILS — `SetCachedValueSource` / `IsSpeedbrakeDeployed` are not on the 737 evaluator.

- [ ] **Step 3: 737 evaluator**

In `MSFSBlindAssist/FirstOfficer/PMDG737/AircraftStateEvaluator.cs`:

(i) After `SetDataManager`, add:
```csharp

    // Values the NG3 CDA does not carry come from SimConnect's variable cache (a
    // ConcurrentDictionary, safe from the flow's pool threads): the speed-brake lever, main's
    // MON_PMDG737_SpeedBrake (L:switch_679_73X — the NG3 SDK has no lever field). Set by
    // Pmdg737FoProfile.BindDataManager; a test injects its own.
    private Func<string, double?>? _cachedValue;

    public void SetCachedValueSource(Func<string, double?>? source) => _cachedValue = source;
```
(ii) In `GetValue`, right after the `FO_ENG2_N2` line, add:
```csharp
        // Speed brake: the lever from SimConnect (no CDA needed), and "armed" = the lever
        // exactly at ARM AND the ARMED light — the light alone stays lit to about 342.
        if (field == SpeedbrakeLeverState.LeverField) return SpeedbrakeLever();
        if (field == SpeedbrakeLeverState.ArmedField)
            return !CdaReady ? double.NaN
                : SpeedbrakeLeverState.ArmedValue(SpeedbrakeLeverState.Pmdg737, SpeedbrakeLever(),
                      RawValue(SpeedbrakeArmLadder.ArmedField));
```
(iii) Add a small section after the `IsPosition` method:
```csharp

    // -----------------------------------------------------------------------
    // Speed brake (lever: main's L:switch_679_73X, ARM exactly 100)
    // -----------------------------------------------------------------------
    public double SpeedbrakeLever() =>
        _cachedValue?.Invoke(SpeedbrakeLeverState.Pmdg737.LeverKey) ?? double.NaN;
    public SpeedbrakeLeverPosition SpeedbrakePosition() =>
        SpeedbrakeLeverState.Classify(SpeedbrakeLeverState.Pmdg737, SpeedbrakeLever());
    public bool IsSpeedbrakeArmed()    => GetValue(SpeedbrakeLeverState.ArmedField) > 0.5;
    public bool IsSpeedbrakeDeployed() => SpeedbrakePosition() == SpeedbrakeLeverPosition.Deployed;
```

- [ ] **Step 4: Bind the cache in the 737 profile**

In `MSFSBlindAssist/FirstOfficer/PMDG737/Pmdg737FoProfile.cs`, replace
```csharp
    public void BindDataManager(AircraftStateEvaluator state, SimConnectManager sc)
        => state.SetDataManager(sc.PMDGDataManager as PMDGNG3DataManager);
```
with
```csharp
    public void BindDataManager(AircraftStateEvaluator state, SimConnectManager sc)
    {
        state.SetDataManager(sc.PMDGDataManager as PMDGNG3DataManager);
        state.SetCachedValueSource(sc.GetCachedVariableValue);
    }
```

- [ ] **Step 5: 737 arm decision and 3 s verify**

In `MSFSBlindAssist/FirstOfficer/PMDG737/AircraftActionExecutor.cs`:

(i) Change `private const int SpeedbrakeArmVerifyMs = 1200;` to
```csharp
    // The lever rides the 1 Hz L-var batch and travels DOWN→ARM in about half a second, so the
    // confirmation (lever at ARM AND the light) needs more than one batch.
    private const int SpeedbrakeArmVerifyMs = 3000;
```
(ii) In `ArmSpeedbrakeAsync`, replace the guard:
```csharp
            if (FieldOn(SpeedbrakeArmLadder.ArmedField) || FieldOn(SpeedbrakeArmLadder.ExtendedField))
                return true;
```
with:
```csharp
            var decision = SpeedbrakeLeverState.DecideArm(SpeedbrakePositionNow(sc),
                FieldOn(SpeedbrakeArmLadder.ArmedField), FieldOn(SpeedbrakeArmLadder.ExtendedField));
            if (decision == SpeedbrakeArmDecision.AlreadyArmed) return true;
            if (decision != SpeedbrakeArmDecision.Arm)
            {
                Log.Debug("FirstOfficer", decision == SpeedbrakeArmDecision.LeaveAlone
                    ? "Speedbrake arm not attempted: the speed brake is extended."
                    : "Speedbrake arm not attempted: the lever position is not known yet.");
                return false;
            }
```
and rewrite the comment block above that guard: an armed lever (lever at ARM and the light) needs no click; a DEPLOYED lever or a lit EXTENDED light is never clicked (the click would retract the spoilers — including the ground spoilers on rollout) and the arm honestly reports NOT done, since the speed brake is not armed; an unread lever is never clicked blind.
(iii) Replace `WaitForSpeedbrakeArmedAsync` with:
```csharp
    /// <summary>Polls for the lever at ARM with the ARMED light lit, for
    /// <c>SpeedbrakeArmVerifyMs</c>. Returns as soon as both agree.</summary>
    private async Task<bool> WaitForSpeedbrakeArmedAsync()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(SpeedbrakeArmVerifyMs);
        while (DateTime.UtcNow < deadline)
        {
            if (IsSpeedbrakeArmedNow()) return true;
            await Task.Delay(SpeedbrakeArmPollMs);
        }
        return IsSpeedbrakeArmedNow();
    }

    private SpeedbrakeLeverPosition SpeedbrakePositionNow(SimConnectManager sc) =>
        SpeedbrakeLeverState.Classify(SpeedbrakeLeverState.Pmdg737,
            sc.GetCachedVariableValue(SpeedbrakeLeverState.Pmdg737.LeverKey) ?? double.NaN);

    private bool IsSpeedbrakeArmedNow() =>
        _sc is { } sc
        && SpeedbrakePositionNow(sc) == SpeedbrakeLeverPosition.Armed
        && FieldOn(SpeedbrakeArmLadder.ArmedField);
```
Update the method's `<returns>` doc to "true once the lever is at ARM with the ARMED light lit (or already was); false if it is extended, unread, or the click did not take."

- [ ] **Step 6: `SpeedbrakeArmLadder` — one pseudo-key, corrected note**

In `MSFSBlindAssist/FirstOfficer/PMDG737/SpeedbrakeArmLadder.cs`, change
`public const string PseudoKey = "SPEEDBRAKE_ARM";` to
`public const string PseudoKey = SpeedbrakeLeverState.ArmPseudoKey;`
and replace the class doc's NOTE paragraph (the one ending "…the FO state evaluator cannot reach (it reads the CDA struct and synthetics only).") with:
```csharp
/// NOTE: <see cref="ArmedField"/> alone does not prove ARMED: it stays lit from ARM to about 342
/// on <c>L:switch_679_73X</c> (measured 2026-09-30, PR #261), i.e. with the spoilers already up.
/// "Armed" is the lever exactly at ARM AND this light (<see cref="SpeedbrakeLeverState.ArmedField"/>);
/// the evaluator reads the lever from SimConnect's cache (main's MON_PMDG737_SpeedBrake).
```

- [ ] **Step 7: 737 flow and checklist wiring**

(i) In `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs`, replace:
```csharp
            Also(SW("LD_SPDBRK", "Speedbrake: ARMED", SpeedbrakeArmLadder.PseudoKey, null,
               SpeedbrakeArmLadder.ArmedField, v => v > 0.5, "LDA_SPDBRK"), "LDC_SPDBRK"),
```
with:
```csharp
            LeaveAlone(Skip(Also(SW("LD_SPDBRK", "Speedbrake: ARMED", SpeedbrakeArmLadder.PseudoKey, null,
               SpeedbrakeLeverState.ArmedField, v => v > 0.5, "LDA_SPDBRK"), "LDC_SPDBRK"),
                s => s.IsSpeedbrakeArmed()),
                s => s.IsSpeedbrakeDeployed(), SpeedbrakeLeverState.LeaveAloneText),
```
and extend its comment with one sentence: an armed speed brake is "Already set", and a deployed one is left alone with its reason (clicking ARM retracts it). Add the helper next to `Skip(…)`:
```csharp
    // A step the First Officer must not perform in the aircraft's current state
    // (FlowStep.LeaveAloneWhen): nothing is sent, `text` is spoken, its lines stay open.
    private static Step LeaveAlone(Step step, Func<AircraftStateEvaluator, bool> when, string text)
    {
        step.LeaveAloneWhen = when;
        step.LeaveAloneText = text;
        return step;
    }
```
(ii) In `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737ChecklistDefinitions.cs`, replace:
```csharp
            AutoAsync("LDA_SPDBRK", "LANDING", "Speedbrake: ARMED",
                SpeedbrakeArmLadder.ArmedField, v => v > 0.5,
                (e, _) => e.ArmSpeedbrakeAsync()),
```
with:
```csharp
            LeaveAlone(AutoAsync("LDA_SPDBRK", "LANDING", "Speedbrake: ARMED",
                SpeedbrakeLeverState.ArmedField, v => v > 0.5,
                (e, _) => e.ArmSpeedbrakeAsync()),
                s => s.IsSpeedbrakeDeployed(), SpeedbrakeLeverState.LeaveAloneText),
```
and replace:
```csharp
            Auto("LDC_SPDBRK", "LANDING_CL", "Speedbrake: ARMED",
                SpeedbrakeArmLadder.ArmedField, v => v > 0.5, action: null),
```
with:
```csharp
            Auto("LDC_SPDBRK", "LANDING_CL", "Speedbrake: ARMED",
                SpeedbrakeLeverState.ArmedField, v => v > 0.5, action: null),
```
Rewrite the long comment above `LDA_SPDBRK`: detected on the lever exactly at ARM AND the ARMED light (the light alone stays lit to about 342); it still reverts on its own when the auto speed brake deploys on touchdown (unless latched by a completed flow); a hand re-tick then over a DEPLOYED lever is refused by the leave-alone rule with its reason, and the executor never clicks ARM over one either. Add the helper next to `Reminder(…)`:
```csharp
    // A line whose hand-tick the First Officer refuses in the aircraft's current state
    // (ChecklistItem.LeaveAloneWhen), speaking `text` instead.
    private static Item LeaveAlone(Item item, Func<AircraftStateEvaluator, bool> when, string text)
    {
        item.LeaveAloneWhen = when;
        item.LeaveAloneText = text;
        return item;
    }
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Pmdg737|FullyQualifiedName~FoPr160ProcedureFixTests|FullyQualifiedName~SpeedbrakeArmLadderTests"`
Expected: all PASS.

- [ ] **Step 9: Commit**

```bash
git add -A MSFSBlindAssist/FirstOfficer tests/MSFSBlindAssist.Tests
git commit -m "fix(fo/737): armed means the lever at ARM and the light; leave a deployed speed brake alone

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: iFly 737 MAX — the First Officer arms it, verified, and leaves a deployed one alone

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/IFly737/IFly737StateEvaluator.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/IFly737/IFly737ActionExecutor.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/IFly737/IFly737FlowDefinitions.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/IFly737/IFly737ChecklistDefinitions.cs`
- Test (rewrite): `tests/MSFSBlindAssist.Tests/FirstOfficer/IFly737LandingSpeedbrakeCheckTests.cs`
- Test (edit): `tests/MSFSBlindAssist.Tests/FirstOfficer/IFly737ProfileStructureTests.cs`, `tests/MSFSBlindAssist.Tests/FirstOfficer/IFly737ExecutorTests.cs`, `tests/MSFSBlindAssist.Tests/FirstOfficer/IFly737FlowChecklistLinkTests.cs`

**Interfaces:**
- Consumes: Tasks 1-3; main's `IFly737SpeedBrakeLever.FieldName` ("Spoiler_Lever_Status"), main's lever write through `IFly737MAXDefinition.ApplyUIVariable` (FLTCTRL_SPOILER; records the pick so the settle announcer stays silent).
- Produces: `IFly737StateEvaluator.SpeedbrakePosition()`, `IsSpeedbrakeArmed()`, `IsSpeedbrakeDeployed()`, synthetic `SpeedbrakeLeverState.ArmedField`; `IFly737ActionExecutor.KeySpeedbrakeArm` (= `SpeedbrakeLeverState.ArmPseudoKey`), `ArmSpeedbrakeAsync()`.

- [ ] **Step 1: Write the failing tests**

(a) Replace the whole of `tests/MSFSBlindAssist.Tests/FirstOfficer/IFly737LandingSpeedbrakeCheckTests.cs` with:

```csharp
using System;
using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect.IFly;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// iFly 737 MAX8: the First Officer arms the speed brake itself, as the PMDG 737's does (same
/// aircraft type), through the verified SPEEDBRAKE_ARM action — main's PR #261 gave the lever a
/// measured write (FLTCTRL_SPOILER, the same 0-224 scale as the read, ARM exactly 34). "Armed"
/// is the lever exactly at 34 AND the SPEED BRAKE ARMED light: the light alone is lit from 34
/// all the way to 224. A deployed speed brake is left alone with its reason.
/// </summary>
public class IFly737LandingSpeedbrakeCheckTests
{
    private static FlowDefinition<IFly737StateEvaluator> LandingFlow()
        => IFly737FlowDefinitions.Build().Single(f => f.Id == "LANDING");

    private static FlowStep<IFly737StateEvaluator> Arm()
        => LandingFlow().Steps.Single(s => s.Id == "LD_SPDBRK");

    private static IFly737StateEvaluator Ready(int lever, byte armedLight)
    {
        var data = new byte[IFlySdkOffsets.StructSize];
        BitConverter.GetBytes(lever).CopyTo(data, IFlySdkOffsets.Spoiler_Lever_Status);
        data[IFlySdkOffsets.SPEED_BRAKE_ARMED_Light_Status] = armedLight;
        var snap = new IFlySdkSnapshot(data);
        var eval = new IFly737StateEvaluator();
        eval.SnapshotSource = () => snap;
        eval.ReadySource = () => true;
        return eval;
    }

    [Fact]
    public void Landing_flow_matches_the_PMDG_737s_order()
    {
        Assert.Equal(new[] { "LD_START_CONT", "LD_SPDBRK", "LD_MISSED", "LD_GEAR_DOWN_CHECK" },
            LandingFlow().Steps.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void The_first_officer_arms_through_the_verified_pseudo_key()
    {
        var step = Arm();
        Assert.Equal(FlowStepActionType.SetSwitch, step.ActionType);
        Assert.Equal(IFly737ActionExecutor.KeySpeedbrakeArm, step.EventName);
        Assert.Equal(SpeedbrakeLeverState.ArmPseudoKey, step.EventName);
        Assert.Equal(new[] { "LDA_SPDBRK", "LDC_SPDBRK" }, step.LinkedChecklistItemIds.ToArray());
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
    }

    [Fact]
    public void An_armed_speed_brake_is_already_set_and_a_deployed_one_is_left_alone()
    {
        var step = Arm();
        Assert.True(step.SkipCondition!(Ready(34, 1)));
        Assert.False(step.SkipCondition!(Ready(0, 0)));
        Assert.False(step.SkipCondition!(Ready(100, 1)));   // lit, but deployed
        Assert.True(step.LeaveAloneWhen!(Ready(100, 1)));
        Assert.False(step.LeaveAloneWhen!(Ready(0, 0)));
        Assert.Equal(SpeedbrakeLeverState.LeaveAloneText, step.LeaveAloneText);
    }

    [Theory]
    [InlineData(0, 0, 0.0)]
    [InlineData(33, 0, 0.0)]
    [InlineData(34, 1, 1.0)]     // DIM counts
    [InlineData(34, 2, 1.0)]     // BRIGHT
    [InlineData(34, 0, 0.0)]
    [InlineData(100, 1, 0.0)]    // the light is lit all the way up
    [InlineData(224, 2, 0.0)]
    public void Armed_is_the_lever_at_34_and_the_light(int lever, byte light, double expected) =>
        Assert.Equal(expected, Ready(lever, light).GetValue(SpeedbrakeLeverState.ArmedField));

    [Fact]
    public void Armed_is_unknown_until_the_SDK_is_ready()
    {
        var eval = Ready(34, 1);
        eval.ReadySource = () => false;
        Assert.True(double.IsNaN(eval.GetValue(SpeedbrakeLeverState.ArmedField)));
    }

    [Fact]
    public void Both_lines_read_the_armed_field_and_only_the_landing_group_line_arms()
    {
        var groups = IFly737ChecklistDefinitions.Build();
        var group = groups.Single(g => g.Id == "LANDING").Items.Single(i => i.Id == "LDA_SPDBRK");
        Assert.Equal(SpeedbrakeLeverState.ArmedField, group.StateFieldName);
        Assert.NotNull(group.CheckAction);
        Assert.NotNull(group.LeaveAloneWhen);
        Assert.Equal(SpeedbrakeLeverState.LeaveAloneText, group.LeaveAloneText);

        var readback = groups.Single(g => g.Id == "LANDING_CL").Items.Single(i => i.Id == "LDC_SPDBRK");
        Assert.Equal(SpeedbrakeLeverState.ArmedField, readback.StateFieldName);
        Assert.Null(readback.CheckAction);
    }
}
```

(b) In `tests/MSFSBlindAssist.Tests/FirstOfficer/IFly737ProfileStructureTests.cs`, rename the test `TakeoffFlaps_LandingAutobrake_Speedbrake_AreReminders` to `TakeoffFlaps_LandingAutobrake_AreReminders_SpeedbrakeIsArmedByTheFirstOfficer` and replace its speed-brake block (from the comment `// Speedbrake ARM (Landing) — a Captain item on this aircraft` through `Assert.Equal("SPEED_BRAKE_ARMED_Light_Status", speedbrakeReadback.StateFieldName);`) with:
```csharp
        // Speedbrake ARM (Landing) — the First Officer arms it, as on the PMDG 737 (a verified
        // SPEEDBRAKE_ARM through main's measured lever write), judged by the lever exactly at
        // ARM AND the ARMED light (FO_SPEEDBRAKE_ARMED). See IFly737LandingSpeedbrakeCheckTests.
        var speedbrakeArm = groups.First(g => g.Id == "LANDING").Items.Single(i => i.Id == "LDA_SPDBRK");
        Assert.Equal(ChecklistItemType.AutoDetectable, speedbrakeArm.Type);
        Assert.NotNull(speedbrakeArm.CheckAction);
        Assert.NotNull(speedbrakeArm.LeaveAloneWhen);
        Assert.Equal(SpeedbrakeLeverState.ArmedField, speedbrakeArm.StateFieldName);

        // Its Landing Checklist twin reads the same field but must still be action-free per
        // the _CL invariant checked above.
        var speedbrakeReadback = groups.First(g => g.Id == "LANDING_CL").Items.Single(i => i.Id == "LDC_SPDBRK");
        Assert.Equal(ChecklistItemType.AutoDetectable, speedbrakeReadback.Type);
        Assert.Null(speedbrakeReadback.CheckAction);
        Assert.Equal(SpeedbrakeLeverState.ArmedField, speedbrakeReadback.StateFieldName);
```
Also add to this file a new fact pinning the write command main added:
```csharp
    // PR #261 gave the lever a real write (FLTCTRL_SPOILER). A display-only field still has none.
    [Fact]
    public void The_speed_brake_lever_is_writable_now_and_a_display_field_is_not()
    {
        var def = new IFly737MAXDefinition();
        Assert.True(def.HasWriteCommand("Spoiler_Lever_Status"));
        Assert.False(def.HasWriteCommand("Hydraulic_Brake_Pressure_Status"));
    }
```
(add `using MSFSBlindAssist.Aircraft;` / `using MSFSBlindAssist.FirstOfficer;` if missing).

(c) In `tests/MSFSBlindAssist.Tests/FirstOfficer/IFly737ExecutorTests.cs`, add `"SPEEDBRAKE_ARM"` to the `Expected` array, and add:
```csharp
    // The verified arm writes ARM (34) through ApplySilent, which refuses any value that is not
    // one of the lever combo's declared positions — ARM must be one.
    [Fact]
    public void Speedbrake_ARM_is_a_declared_lever_position()
    {
        var exec = new IFly737ActionExecutor();
        exec.SetDefinition(new MSFSBlindAssist.Aircraft.IFly737MAXDefinition());
        Assert.True(exec.IsDeclaredPosition(MSFSBlindAssist.Aircraft.IFly737SpeedBrakeLever.FieldName,
            SpeedbrakeLeverState.IFly737.ArmValue));
    }
```

(d) In `tests/MSFSBlindAssist.Tests/FirstOfficer/IFly737FlowChecklistLinkTests.cs`, add to `MappedKeys`:
```csharp
        // SPEEDBRAKE_ARM pseudo-key (ArmSpeedbrakeCoreAsync): the lever at ARM with the ARMED
        // light, which is what both "Speedbrake: ARMED" lines read.
        [IFly737ActionExecutor.KeySpeedbrakeArm] = new (string, Func<int, double>)[]
        { (SpeedbrakeLeverState.ArmedField, _ => 1) },
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~IFly737"`
Expected: build FAILS — `KeySpeedbrakeArm` and the evaluator's speed-brake members do not exist.

- [ ] **Step 3: iFly evaluator**

In `MSFSBlindAssist/FirstOfficer/IFly737/IFly737StateEvaluator.cs`:
(i) In `GetValue`, after the `IFly737GearConfirmation.DownField` line, add:
```csharp
        // "Speedbrake: ARMED" — the lever exactly at ARM (34) AND the SPEED BRAKE ARMED light;
        // the light alone is lit from 34 all the way to 224 (measured 2026-09-30, PR #261).
        if (field == SpeedbrakeLeverState.ArmedField)
            return SpeedbrakeLeverState.ArmedValue(SpeedbrakeLeverState.IFly737,
                GetValue(IFly737SpeedBrakeLever.FieldName), GetValue(SpeedbrakeArmedLight));
```
(ii) After `IsPosition`, add:
```csharp

    /// <summary>The SPEED BRAKE ARMED light (0 Off / 1 DIM / 2 BRIGHT).</summary>
    public const string SpeedbrakeArmedLight = "SPEED_BRAKE_ARMED_Light_Status";

    /// <summary>The SPEEDBRAKES EXTENDED light.</summary>
    public const string SpeedbrakeExtendedLight = "SPEEDBRAKES_EXTENDED_Light_Status";

    public SpeedbrakeLeverPosition SpeedbrakePosition() =>
        SpeedbrakeLeverState.Classify(SpeedbrakeLeverState.IFly737, GetValue(IFly737SpeedBrakeLever.FieldName));
    public bool IsSpeedbrakeArmed()    => GetValue(SpeedbrakeLeverState.ArmedField) > 0.5;
    public bool IsSpeedbrakeDeployed() => SpeedbrakePosition() == SpeedbrakeLeverPosition.Deployed;
```

- [ ] **Step 4: iFly executor — the verified arm**

In `MSFSBlindAssist/FirstOfficer/IFly737/IFly737ActionExecutor.cs`:
(i) After `KeyPressAlts`, add:
```csharp
    /// <summary>Speed brake to ARMED, verified (<see cref="ArmSpeedbrakeAsync"/>) — the PMDG 737's
    /// shape: armed is left as it is, a deployed or unread lever is never written.</summary>
    public const string KeySpeedbrakeArm = SpeedbrakeLeverState.ArmPseudoKey;

    /// <summary>The lever write lands at once (measured, PR #261); the SDK is polled every 250 ms.</summary>
    public const int SpeedbrakeArmVerifyMs = 1500;
    private const int SpeedbrakeArmPollMs = 100;
```
(ii) Add to `PseudoKeyHandlers`:
```csharp
            [KeySpeedbrakeArm] = e => e.ArmSpeedbrakeCoreAsync(),
```
(iii) After `StartApuCoreAsync` (or next to `SetAltimetersStandardAsync`), add:
```csharp
    /// <summary>Speed brake to ARMED for a checklist tick — takes the gate (see
    /// <see cref="ArmSpeedbrakeCoreAsync"/>).</summary>
    public async Task<bool> ArmSpeedbrakeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsAvailable) return false;
            await PaceAsync();
            return await ArmSpeedbrakeCoreAsync();
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Armed already (lever at ARM and the ARMED light lit): true, no write. Deployed, the
    /// EXTENDED light lit, or unread: false and no write — writing ARM over a deployed speed
    /// brake retracts it. Otherwise the lever is written to ARM through the panel's own write
    /// path (FLTCTRL_SPOILER, which records the pick so the settle announcer stays silent) and
    /// must read armed within <see cref="SpeedbrakeArmVerifyMs"/>.
    /// </summary>
    private async Task<bool> ArmSpeedbrakeCoreAsync()
    {
        var state = _state;
        if (state == null) return false;
        var decision = SpeedbrakeLeverState.DecideArm(state.SpeedbrakePosition(),
            state.GetValue(IFly737StateEvaluator.SpeedbrakeArmedLight) > 0.5,
            state.GetValue(IFly737StateEvaluator.SpeedbrakeExtendedLight) > 0.5);
        switch (decision)
        {
            case SpeedbrakeArmDecision.AlreadyArmed:
                return true;
            case SpeedbrakeArmDecision.LeaveAlone:
                Log.Debug("ifly_fo", "speedbrake arm not attempted: the speed brake is extended");
                return false;
            case SpeedbrakeArmDecision.Unreadable:
                Log.Warn("ifly_fo", $"speedbrake arm not attempted: {IFly737SpeedBrakeLever.FieldName} unreadable");
                return false;
        }

        if (!ApplySilent(IFly737SpeedBrakeLever.FieldName, SpeedbrakeLeverState.IFly737.ArmValue)) return false;
        _lastWriteUtc = DateTime.UtcNow;

        var deadline = DateTime.UtcNow.AddMilliseconds(SpeedbrakeArmVerifyMs);
        while (DateTime.UtcNow < deadline)
        {
            if (state.IsSpeedbrakeArmed()) return true;
            await Task.Delay(SpeedbrakeArmPollMs);
        }
        if (state.IsSpeedbrakeArmed()) return true;
        Log.Warn("ifly_fo", $"speedbrake arm did not take (lever {state.GetValue(IFly737SpeedBrakeLever.FieldName)})");
        return false;
    }
```
(add `using MSFSBlindAssist.Aircraft;` if `IFly737SpeedBrakeLever` does not resolve).

- [ ] **Step 5: iFly flow and checklist wiring**

(i) In `MSFSBlindAssist/FirstOfficer/IFly737/IFly737FlowDefinitions.cs`, in `BuildLanding()`:
- set `Description = "Start switches CONT, speedbrake armed, missed approach altitude, then confirms the gear is down.",`
- replace the two lines `// Speedbrake ARM is a Captain reminder …` + `Captain("LD_SPDBRK", "Speedbrake: ARMED"),` with:
```csharp
            // Verified arm via the SPEEDBRAKE_ARM pseudo-key (IFly737ActionExecutor.
            // ArmSpeedbrakeCoreAsync), the PMDG 737's shape: it writes ARM through main's
            // measured lever write and proves it against the lever AND the ARMED light. It
            // completes BOTH "Speedbrake: ARMED" lines, so a failed arm leaves neither latched.
            // Armed is "Already set"; a deployed speed brake is left alone with its reason.
            LeaveAlone(Skip(Also(SW("LD_SPDBRK", "Speedbrake: ARMED", IFly737ActionExecutor.KeySpeedbrakeArm, null,
                    "LDA_SPDBRK"), "LDC_SPDBRK"),
                s => s.IsSpeedbrakeArmed()),
                s => s.IsSpeedbrakeDeployed(), SpeedbrakeLeverState.LeaveAloneText),
```
- delete the whole `LD_SPDBRK_CHECK` step (its long comment and the `Skip(WaitForField("LD_SPDBRK_CHECK", …))` expression). `LD_MISSED` and `LD_GEAR_DOWN_CHECK` stay as they are.
- Add the helper next to `Skip(…)`:
```csharp
    // A step the First Officer must not perform in the aircraft's current state
    // (FlowStep.LeaveAloneWhen): nothing is sent, `text` is spoken, its lines stay open.
    private static Step LeaveAlone(Step step, Func<IFly737StateEvaluator, bool> when, string text)
    {
        step.LeaveAloneWhen = when;
        step.LeaveAloneText = text;
        return step;
    }
```
(ii) In `MSFSBlindAssist/FirstOfficer/IFly737/IFly737ChecklistDefinitions.cs`, replace:
```csharp
            Auto("LDA_SPDBRK", "LANDING", "Speedbrake: ARMED", "SPEED_BRAKE_ARMED_Light_Status", v => v > 0.5,
                action: null),
```
with:
```csharp
            LeaveAlone(AutoAsync("LDA_SPDBRK", "LANDING", "Speedbrake: ARMED",
                SpeedbrakeLeverState.ArmedField, v => v > 0.5,
                (e, _) => e.ArmSpeedbrakeAsync()),
                s => s.IsSpeedbrakeDeployed(), SpeedbrakeLeverState.LeaveAloneText),
```
and rewrite its comment: ticking it arms the speed brake (the PMDG 737's shape); detected on the lever exactly at ARM AND the ARMED light; a tick over a deployed speed brake is refused with its reason. Replace:
```csharp
            Auto("LDC_SPDBRK", "LANDING_CL", "Speedbrake: ARMED", "SPEED_BRAKE_ARMED_Light_Status", v => v > 0.5,
                action: null),
```
with:
```csharp
            Auto("LDC_SPDBRK", "LANDING_CL", "Speedbrake: ARMED", SpeedbrakeLeverState.ArmedField, v => v > 0.5,
                action: null),
```
and update its two-line comment to say it reads the lever at ARM AND the light (the light alone is lit to 224). Add the helper next to `Reminder(…)`:
```csharp
    // A line whose hand-tick the First Officer refuses in the aircraft's current state
    // (ChecklistItem.LeaveAloneWhen), speaking `text` instead.
    private static Item LeaveAlone(Item item, Func<IFly737StateEvaluator, bool> when, string text)
    {
        item.LeaveAloneWhen = when;
        item.LeaveAloneText = text;
        return item;
    }
```
(The class-doc bullets in both files are corrected in Task 7.)

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~IFly737"`
Expected: all PASS. Then `grep -rn LD_SPDBRK_CHECK MSFSBlindAssist tests` → no matches (docs are Task 7).

- [ ] **Step 7: Commit**

```bash
git add -A MSFSBlindAssist/FirstOfficer tests/MSFSBlindAssist.Tests
git commit -m "feat(fo/ifly): the First Officer arms the speed brake, verified, and leaves a deployed one alone

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Notes and docs say what is true now

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/IFly737MAXDefinition.cs` (`HasWriteCommand` doc, ~line 1052-1066)
- Modify: `MSFSBlindAssist/FirstOfficer/IFly737/IFly737FlowDefinitions.cs` (class doc bullet "Speedbrake ARM (LD_SPDBRK) is a Captain reminder …", ~line 86-92)
- Modify: `MSFSBlindAssist/FirstOfficer/IFly737/IFly737ChecklistDefinitions.cs` (class doc bullet "Speedbrake ARM (Landing) is a Captain item here …", ~line 32-37)
- Modify: `tests/MSFSBlindAssist.Tests/FirstOfficer/IFly737ProfileStructureTests.cs` (doc of `EverySetSwitchStep_Resolves`, ~line 326-345)
- Modify: `tests/MSFSBlindAssist.Tests/FirstOfficer/IFly737ExecutorTests.cs` (comment ~line 295)
- Modify: `tests/MSFSBlindAssist.Tests/FirstOfficer/Pmdg777FlowOrderingTests.cs` (any remaining mention of `SpeeedbrakeLeverPos` / "25" / 0-100)
- Modify: `docs/first-officer.md`, `docs/pmdg-777.md`, `docs/pmdg-737.md`, `docs/ifly-737.md`, `docs/ifly-737-first-officer-test-plan.md`, `docs/pmdg-737-first-officer-test-plan.md`
- Modify: `CLAUDE.md` — two Invariants bullets PR #160 added that are now false: the one starting "**`FCTL_Speedbrake_Lever` is an ANALOG 0–100 POSITION…**" (PMDG 777 group) and the one starting "iFly 737 MAX8 speedbrake ARM, takeoff flaps and landing autobrake are Captain items…"

**Interfaces:** none (comments and docs only).

- [ ] **Step 1: Find every stale statement**

Run:
```bash
grep -rn -i "Spoiler_Lever_Status\|SpeeedbrakeLeverPos\|LD_SPDBRK_CHECK\|captain reminder\|captain item\|FCTL_Speedbrake_Lever\|cannot reach\|ArmedValue 50\|25: ARMED\|Speedbrake.*read-only\|read-only.*speedbrake\|scale mismatch" MSFSBlindAssist/FirstOfficer MSFSBlindAssist/Aircraft/IFly737MAXDefinition.cs tests/MSFSBlindAssist.Tests/FirstOfficer docs/first-officer.md docs/pmdg-777.md docs/pmdg-737.md docs/ifly-737.md docs/ifly-737-first-officer-test-plan.md docs/pmdg-737-first-officer-test-plan.md
```
Every hit that describes the FIRST OFFICER's speed brake must be corrected per Step 2. Leave alone: main's own panel text about `FCTL_Speedbrake_Lever` in `docs/pmdg-777.md`'s FIRST paragraph (it is correct), `IFly737FieldOffsetsByKeyTests.cs`, and anything about other controls that happen to be "Captain reminders".

- [ ] **Step 2: Correct them (facts to state, in the files' own style)**

- `IFly737MAXDefinition.HasWriteCommand` doc: the example of a registered-but-read-only key becomes `Hydraulic_Brake_Pressure_Status` (a `Disp` field). Keep the history in one clause: the mutation probe once pointed a step at `Spoiler_Lever_Status`, which PR #261 has since given a real write (`FLTCTRL_SPOILER`).
- `IFly737ProfileStructureTests.EverySetSwitchStep_Resolves` doc: same substitution and the same one-clause history.
- `IFly737ExecutorTests` comment ~line 295: say `Spoiler_Lever_Status` was once such a key and is writable since PR #261.
- iFly flow class-doc bullet: Speedbrake ARM (`LD_SPDBRK`) is a verified First Officer arm through the `SPEEDBRAKE_ARM` pseudo-key (main's measured lever write, ARM exactly 34), judged by the lever at ARM AND the ARMED light; a deployed speed brake is left alone with "Speedbrake extended, not armed. Left as it is."; it completes both "Speedbrake: ARMED" lines. Remove the read-only / unverified-scale / `LD_SPDBRK_CHECK` text.
- iFly checklist class-doc bullet: `LDA_SPDBRK` arms on tick (with the leave-alone rule), `LDC_SPDBRK` is check-only; both read `FO_SPEEDBRAKE_ARMED`.
- `docs/pmdg-777.md`: in the "ANALOG 0–100 position" section, replace the sentence claiming "two tables carrying the same measured values (…50…75…100)" with: the First Officer now reads the lever the same way as the panel — main's `L:switch_498_a` through `Aircraft/PmdgSpeedBrakeLever.B777` (via `FirstOfficer/SpeedbrakeLeverState`), never the byte — so there is ONE table. Mark the 0/50/75/100 table as the SDK byte's values (history). In the "Two separate traps" list, add that the First Officer later also moved off the byte (it read 201-203 as armed and an axis DOWN at 22 as not down). Replace the last paragraph's "every arm path skips on armed-OR-deployed" with: armed is "Already set"; a deployed lever is left alone with "Speedbrake extended, not armed. Left as it is." (flow step and hand-tick), and the executor never clicks ARM over a lever not known to be down; the verified arm waits up to 8 s because the lever takes about five seconds from DOWN to ARM and a flow step's own verify reads after only 600 ms.
- `docs/pmdg-737.md` "Speedbrake ARM — one proven rung": add a paragraph dated 2026-09-30: the ARMED light alone does not prove ARMED (lit from 100 to about 342 on `L:switch_679_73X`, PR #261); "armed" is now the lever exactly at ARM AND the light, read from SimConnect's cache (main's `MON_PMDG737_SpeedBrake`); a deployed speed brake or a lit EXTENDED light is never clicked and the arm reports NOT done (it used to count "extended" as done); the verify window is 3 s (the lever rides the 1 Hz batch).
- `docs/ifly-737.md` bullet "The speedbrake lines are confirmed by the SPEED BRAKE ARMED light…": rewrite to the new state — the First Officer arms it (verified `SPEEDBRAKE_ARM`, parity with the PMDG 737), both lines read lever-at-34 AND the light (the light alone is lit 34-224), a deployed one is left alone; `LD_SPDBRK_CHECK` and the Captain reminder are gone. Pinned by `IFly737LandingSpeedbrakeCheckTests`.
- `docs/first-officer.md` lines ~83-84 ("speedbrake ARM and landing autobrake stay Captain reminders … unverified scale mismatch"): speedbrake ARM is now a First Officer action on the iFly too; landing autobrake stays a Captain reminder. Near the "737 speed-brake lever detents are SDK mouse-click events" bullet, add one bullet: **Speed brake is judged by the lever, and a deployed one is left alone (2026-09-30).** Summarise the three jets' rule (`SpeedbrakeLeverState` over main's tables), the leave-alone rule on `FlowStep`/`ChecklistItem` (`LeaveAloneWhen`/`LeaveAloneText`, checked after `SkipCondition`, hand-tick refused with the reason spoken in place of the status line), and that a CAPTAIN REMINDER note elsewhere in this file about the iFly "Speedbrake: ARMED" is history.
- `docs/ifly-737-first-officer-test-plan.md`: the Landing table row, section B7 and the lines ~275-280 and ~430-440 describe the Captain reminder. Rewrite B7 as "Speedbrake — the First Officer arms it" with two in-sim checks: (1) lever DOWN, run Landing → "Speedbrake: ARMED" and both lines tick; (2) lever at the FLIGHT detent → "Speedbrake extended, not armed. Left as it is.", the lever does not move, both lines stay open and tick once the pilot arms. Keep the scale-mismatch history as one sentence (PR #261 measured the write: same 0-224 scale as the read, ARM 34).
- `docs/pmdg-737-first-officer-test-plan.md`: where Landing's speedbrake is described, add the flight-detent leave-alone check (same two scenarios as above).
- `CLAUDE.md`, PMDG 777 bullet "**`FCTL_Speedbrake_Lever` is an ANALOG 0–100 POSITION…**": rewrite it as ONE bullet (keep it an invariant, keep the `→ [pmdg-777.md](docs/pmdg-777.md)` pointer) stating: the First Officer reads the 777 speed-brake lever from main's `L:switch_498_a` (key `FCTL_Speedbrake`, 0 / 200 / 300 / 400) through `FirstOfficer/Pmdg777SpeedbrakeLever` → `SpeedbrakeLeverState` over main's `PmdgSpeedBrakeLever.B777`, NEVER the SDK byte `FCTL_Speedbrake_Lever` (that value / 4, truncated: 201-203 read 50 "armed" with the spoilers 34 % up, and an axis DOWN at 22 read 5); keep, as history in one sentence each, that the SDK header's "25: ARMED" is wrong and that the profile once tested `v > 0.5 && v < 1.5`; ARM is exact, DOWN is anything short of ARM; armed is "Already set", a deployed lever is left alone with "Speedbrake extended, not armed. Left as it is." (flow step via `FlowStep.LeaveAloneWhen`, hand-tick via `ChecklistItem.LeaveAloneWhen`) and the executor's `DispatchCoreAsync` never clicks ARM over a lever not known to be DOWN; the Landing step goes through the verified `SPEEDBRAKE_ARM` (`ArmSpeedbrakeAsync`, up to 8 s) because the lever takes about 5 s DOWN→ARM and a flow step's own verify reads after 600 ms. Remove the claim that "every arm path must skip on armed-OR-deployed" (a deployed lever is now left alone with a reason, not skipped as "Already set").
- `CLAUDE.md`, iFly bullet "iFly 737 MAX8 speedbrake ARM, takeoff flaps and landing autobrake are Captain items…": rewrite as: takeoff flaps and landing autobrake are Captain items; the speed brake is ARMED BY THE FIRST OFFICER since main's PR #261 measured the lever write (`FLTCTRL_SPOILER`, same 0-224 scale as `Spoiler_Lever_Status`, ARM exactly 34), through the verified `SPEEDBRAKE_ARM` (parity with the PMDG 737, same aircraft type); both "Speedbrake: ARMED" lines read `FO_SPEEDBRAKE_ARMED` = lever exactly at ARM AND the ARMED light (the light alone is lit 34-224); a deployed speed brake is left alone with its reason; `LD_SPDBRK_CHECK` and the Captain reminder are gone. Keep the `→ [first-officer.md](docs/first-officer.md), [ifly-737.md](docs/ifly-737.md)` pointer.

- [ ] **Step 3: Verify nothing stale is left and the build is clean**

Re-run the Step 1 grep; every remaining hit must be either history clearly marked as such or unrelated. Then:
Run: `dotnet build MSFSBlindAssist.sln -c Debug` → 0 errors.

- [ ] **Step 4: Commit**

```bash
git add -A MSFSBlindAssist tests docs CLAUDE.md
git commit -m "docs(fo): the speed brake notes say what the First Officer does now

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
