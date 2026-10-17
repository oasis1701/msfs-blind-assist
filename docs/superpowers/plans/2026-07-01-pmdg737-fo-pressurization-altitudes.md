# PMDG 737 FO Pressurization FLT/LAND ALT Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The 737 First Officer sets the pressurization panel's FLT ALT (SimBrief cruise) and LAND ALT (SimBrief destination elevation) itself — flow, checklist tick, and auto-verify — instead of leaving them as captain reminders.

**Architecture:** SimBrief values flow through the existing `ApplyFlightPlanThresholds` path into the 737 `AircraftStateEvaluator` (rounded/clamped at storage), surface as synthetic `GetValue` fields for checklist auto-detect (the `FO_ENG1_N2` precedent), and reach the preflight flow via a new optional `FlowStep.TargetValueProvider` resolved by `FlowManager` at dispatch time (null = quiet skip, with the old captain reminder kept as a `SkipCondition`'d fallback). The direct-control events `EVT_OH_PRESS_FLT_ALT_SET` / `EVT_OH_PRESS_LAND_ALT_SET` (from PR #120) take literal feet as the CDA parameter.

**Tech Stack:** C# 13 / .NET 9 WinForms; PMDG NG3 SDK CDA events; no automated test project (per CLAUDE.md: verification = x64 solution build + in-sim test plan — do NOT add unit tests).

**Spec:** `docs/superpowers/specs/2026-07-01-pmdg737-fo-pressurization-altitudes-design.md`

## Global Constraints

- **777 untouched** except the mandatory `IFoStateEvaluator` no-op (interface parity, `SetEngineN2` precedent).
- **Branch:** work directly on `feature/777-first-officer` (the FO subsystem lives there, unmerged).
- **Build command (ALWAYS the solution):** `dotnet build MSFSBlindAssist.sln -c Debug` — never the csproj alone (AnyCPU output-path trap, see CLAUDE.md). Exe may be file-locked (MSB3021) if MSFSBA is running — that only blocks the final copy, compile errors still surface.
- **CDA same-frame coalescing rule:** never two PMDG CDA writes in the same frame — flow steps are spaced by `PostActionDelayMs`; multi-write CheckActions must go through the spaced/gated helpers.
- **`*_CL` readback groups stay action-free** (no non-null `CheckAction` on any `*_CL` item).
- **Rounding/clamping (from spec):** FLT ALT nearest 500 ft, clamp 0–42,000; LAND ALT nearest 50 ft, clamp 0–14,000. Below-sea-level LAND ALT out of scope (min 0). Rounding happens ONCE, at evaluator storage.
- **Match tolerance:** "within one knob step" is implemented as **strictly less than** one step (`< 500` / `< 50`) — window values are step-quantized, so a full-step difference is a *different* setting, not a match; strict-less still absorbs float fuzz.
- No `Directory.Build.props` changes, no new projects, no renames.

---

### Task 1: Evaluator plan storage + synthetic match fields + SimBrief push

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/IFoStateEvaluator.cs` (19-line file; add one method after `SetEngineN2`)
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/AircraftStateEvaluator.cs` (synthetic block at ~line 43; append storage block after `GetTakeoffFlaps()` ~line 220)
- Modify: `MSFSBlindAssist/FirstOfficer/AircraftStateEvaluator.cs` (the **777** evaluator; add no-op after `SetEngineN2` ~line 242)
- Modify: `MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs` (`ApplyFlightPlanThresholds`, after the takeoff-flaps block ~line 239)

**Interfaces:**
- Consumes: `SimBriefOFP.InitialAltitude` / `SimBriefOFP.DestElevation` (strings, feet — `MSFSBlindAssist/Models/SimBriefOFP.cs:16,49`), PMDG CDA fields `AIR_FltAltWindow` / `AIR_LandAltWindow` (uints in `PMDGNG3DataStruct.cs:461-462`, readable via `GetFieldValue`).
- Produces (Tasks 3 and 4 rely on these exact members on the **737** `AircraftStateEvaluator`):
  - `int? PlannedFltAltFt { get; }` — rounded/clamped, null when unavailable
  - `int? PlannedLandAltFt { get; }` — rounded/clamped, null when unavailable
  - `bool HasPressurizationPlan { get; }` — at least one value available
  - `bool FltAltMatches()` / `bool LandAltMatches()` — window vs plan, strict-less-than one step
  - Synthetic `GetValue` keys: `"FO_PRESS_ALTS_MATCH"`, `"FO_PRESS_LAND_ALT_MATCH"` (1/0)
  - Interface: `void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt)`

- [ ] **Step 1: Add the interface method**

In `MSFSBlindAssist/FirstOfficer/IFoStateEvaluator.cs`, after the `SetEngineN2` declaration (line 18), add:

```csharp

    /// <summary>
    /// Store the SimBrief pressurization plan — cruise (flight) altitude and destination
    /// field elevation, feet; null = that value unavailable in the OFP. The 737 evaluator
    /// stores it (rounded to the panel knob steps) and serves the synthetic GetValue keys
    /// "FO_PRESS_ALTS_MATCH" / "FO_PRESS_LAND_ALT_MATCH" for checklist auto-detect; the
    /// 777's pressurization is automatic (FMC landing altitude), so its evaluator no-ops.
    /// </summary>
    void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt);
```

- [ ] **Step 2: 737 evaluator — storage, rounding, accessors, match helpers**

In `MSFSBlindAssist/FirstOfficer/PMDG737/AircraftStateEvaluator.cs`, append after `public int  GetTakeoffFlaps() => _takeoffFlaps;` (line 220, before the closing brace):

```csharp

    // -----------------------------------------------------------------------
    // SimBrief pressurization plan (set when an OFP is loaded). Rounded to the panel
    // knob steps + clamped AT STORAGE — FLT ALT nearest 500 ft (0..42000), LAND ALT
    // nearest 50 ft (0..14000) — so every consumer (the Preflight flow's target
    // providers, the FO_PRESS_* synthetic match fields, the checklist CheckAction)
    // reads the exact value the cockpit window will show. PR #120's panel path rounds
    // DOWN to mirror the knob; a stored value is already a step multiple, so the
    // event-side round-down is a no-op and the two paths cannot disagree.
    // -1 sentinel = that value not available (no plan / unparseable OFP field).
    // -----------------------------------------------------------------------
    private int _plannedFltAltFt = -1;
    private int _plannedLandAltFt = -1;

    public void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt)
    {
        _plannedFltAltFt  = cruiseAltFt is int c ? RoundToStep(c, 500, 42000) : -1;
        _plannedLandAltFt = destElevFt  is int d ? RoundToStep(d, 50, 14000)  : -1;
    }

    private static int RoundToStep(int feet, int step, int maxFt)
    {
        if (feet < 0) feet = 0;           // below-sea-level LAND ALT out of scope (PR #120)
        if (feet > maxFt) feet = maxFt;
        return (int)Math.Round(feet / (double)step, MidpointRounding.AwayFromZero) * step;
    }

    /// <summary>Planned FLT ALT (rounded/clamped), or null when no SimBrief plan.</summary>
    public int? PlannedFltAltFt  => _plannedFltAltFt  >= 0 ? _plannedFltAltFt  : null;
    /// <summary>Planned LAND ALT (rounded/clamped), or null when no SimBrief plan.</summary>
    public int? PlannedLandAltFt => _plannedLandAltFt >= 0 ? _plannedLandAltFt : null;
    /// <summary>At least one planned pressurization value is available.</summary>
    public bool HasPressurizationPlan => _plannedFltAltFt >= 0 || _plannedLandAltFt >= 0;

    // Window-vs-plan match, strictly less than one knob step: window values are
    // step-quantized, so a full-step difference is a DIFFERENT setting, not a match;
    // strict-less still absorbs float fuzz.
    public bool FltAltMatches()  => _plannedFltAltFt  >= 0 && Math.Abs(GetValue("AIR_FltAltWindow")  - _plannedFltAltFt)  < 500;
    public bool LandAltMatches() => _plannedLandAltFt >= 0 && Math.Abs(GetValue("AIR_LandAltWindow") - _plannedLandAltFt) < 50;

    // Every AVAILABLE planned value matches its window (a partial plan checks what exists).
    private bool AllPressAltsMatch() =>
        HasPressurizationPlan
        && (_plannedFltAltFt  < 0 || FltAltMatches())
        && (_plannedLandAltFt < 0 || LandAltMatches());
```

- [ ] **Step 3: 737 evaluator — synthetic GetValue keys**

In the same file, in `GetValue` (lines 41–48), extend the synthetic block. Replace:

```csharp
        // Synthetic FO-only fields (not in the PMDG CDA struct), served from the timer-pushed cache.
        if (field == "FO_ENG1_N2") return Volatile.Read(ref _eng1N2);
        if (field == "FO_ENG2_N2") return Volatile.Read(ref _eng2N2);
```

with:

```csharp
        // Synthetic FO-only fields (not in the PMDG CDA struct). N2 from the timer-pushed
        // cache; the FO_PRESS_* keys compare the live FLT/LAND ALT windows to the stored
        // SimBrief plan (checklist auto-detect reads them — see the plan block below).
        if (field == "FO_ENG1_N2") return Volatile.Read(ref _eng1N2);
        if (field == "FO_ENG2_N2") return Volatile.Read(ref _eng2N2);
        if (field == "FO_PRESS_ALTS_MATCH")     return AllPressAltsMatch() ? 1 : 0;
        if (field == "FO_PRESS_LAND_ALT_MATCH") return LandAltMatches() ? 1 : 0;
```

- [ ] **Step 4: 777 evaluator — no-op**

In `MSFSBlindAssist/FirstOfficer/AircraftStateEvaluator.cs` (the 777 one), after the `SetEngineN2` no-op method (~line 242), add:

```csharp

    /// <summary>
    /// Interface parity with the 737 (which sets pressurization FLT/LAND ALT from the
    /// SimBrief plan). The 777's pressurization is automatic (FMC landing altitude) —
    /// nothing to store.
    /// </summary>
    public void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt) { }
```

Note: if the build in Step 6 reports any OTHER `IFoStateEvaluator` implementer missing the method, add the same no-op there (none is known — only the two evaluators implement it).

- [ ] **Step 5: FirstOfficerForm — push the plan on SimBrief load**

In `MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs`, inside `ApplyFlightPlanThresholds`, after the takeoff-flaps block (after line 239's closing brace, before the method's closing brace), add:

```csharp

        // Pressurization plan: FLT ALT = SimBrief cruise, LAND ALT = destination field
        // elevation (interface method — the 737 evaluator stores it rounded to the panel
        // knob steps; the 777 no-ops, its pressurization is automatic). DestElevation may
        // legitimately parse to 0 (sea level), so no > 0 gate on it.
        int? cruiseFt   = int.TryParse(ofp.InitialAltitude, out int crz) && crz > 0 ? crz : null;
        int? destElevFt = int.TryParse(ofp.DestElevation, out int elev) ? elev : null;
        _stateEval.SetPlannedPressurizationAltitudes(cruiseFt, destElevFt);
        if (cruiseFt != null || destElevFt != null)
        {
            var pressParts = new List<string>();
            if (cruiseFt != null)   pressParts.Add($"flight altitude {cruiseFt} feet");
            if (destElevFt != null) pressParts.Add($"landing altitude {destElevFt} feet");
            _announcer.AnnounceImmediate($"Pressurization plan: {string.Join(", ", pressParts)}.");
        }
```

- [ ] **Step 6: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded (MSB3021 exe-copy failure acceptable if the app is running; compile errors are not).

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/IFoStateEvaluator.cs MSFSBlindAssist/FirstOfficer/PMDG737/AircraftStateEvaluator.cs MSFSBlindAssist/FirstOfficer/AircraftStateEvaluator.cs MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs
git commit -m "feat(fo737): store SimBrief pressurization plan + synthetic match fields

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: FlowStep.TargetValueProvider + FlowManager quiet-skip

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/Models/FlowStep.cs` (add property near `TargetValue`, ~line 73)
- Modify: `MSFSBlindAssist/FirstOfficer/FlowManager.cs` (top of the `SetSwitch`/`SetSwitchMultiple` case in `ExecuteStepAsync`, ~line 256)

**Interfaces:**
- Consumes: `FlowStep<TState>.TargetValue` (`int?`), `FlowManager._state` (`TState`), `IFlowStepDispatch.TargetValue` (read by executors — unchanged; the resolved value lands in `TargetValue` before dispatch).
- Produces (Task 3 relies on): `FlowStep<TState>.TargetValueProvider` — `Func<TState, int?>?`; semantics: non-null provider overrides `TargetValue` at dispatch; provider returning null = **quiet skip** (step reports success, announces nothing).

- [ ] **Step 1: Add the property to FlowStep**

In `MSFSBlindAssist/FirstOfficer/Models/FlowStep.cs`, after the `TargetValue` property (line 73), add:

```csharp

    /// <summary>
    /// Resolves <see cref="TargetValue"/> dynamically at dispatch time — for values
    /// unknown when the static flow definitions are built (e.g. SimBrief-derived
    /// pressurization altitudes). When non-null, FlowManager writes the resolved value
    /// into <see cref="TargetValue"/> immediately before dispatch (re-resolved on every
    /// run, so the mutation never goes stale). Returning null means the required data is
    /// unavailable → the step is QUIETLY skipped: success result, NO announcement (the
    /// generic "Already set:"/"Skipping:" wordings would be wrong for "no flight plan").
    /// Pair such steps with a fallback CaptainReminder that is SkipCondition'd away when
    /// the data IS available, so the pilot still hears something in the no-data case.
    /// </summary>
    public Func<TState, int?>? TargetValueProvider { get; set; }
```

- [ ] **Step 2: Resolve the provider in FlowManager**

In `MSFSBlindAssist/FirstOfficer/FlowManager.cs`, in `ExecuteStepAsync`, at the very top of the combined case (line 258, immediately after `case FlowStepActionType.SetSwitchMultiple:` opens the block and BEFORE the `_executor.IsAvailable` check), add:

```csharp
                    // Resolve a dynamic target (e.g. SimBrief-derived) just before dispatch.
                    // Null = required data unavailable → quiet skip (see TargetValueProvider).
                    if (step.TargetValueProvider != null)
                    {
                        int? resolved = step.TargetValueProvider(_state);
                        if (resolved is null) return true;
                        step.TargetValue = resolved;
                    }

```

The resulting case must read:

```csharp
                case FlowStepActionType.SetSwitch:
                case FlowStepActionType.SetSwitchMultiple:
                {
                    // Resolve a dynamic target (e.g. SimBrief-derived) just before dispatch.
                    // Null = required data unavailable → quiet skip (see TargetValueProvider).
                    if (step.TargetValueProvider != null)
                    {
                        int? resolved = step.TargetValueProvider(_state);
                        if (resolved is null) return true;
                        step.TargetValue = resolved;
                    }

                    if (!_executor.IsAvailable)
                    {
                        _announcer.Announce($"Sim not connected — cannot perform: {step.AnnounceText}");
                        return false;
                    }

                    _announcer.Announce(step.AnnounceText);
                    bool sent = await _executor.ExecuteStepAsync(step);
```

(The `return true` path deliberately precedes the announce; the run loop will fire `StepCompleted` — the step shows as done in the UI, silently. `RetryThenStop` re-entry re-resolves harmlessly.)

- [ ] **Step 3: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/Models/FlowStep.cs MSFSBlindAssist/FirstOfficer/FlowManager.cs
git commit -m "feat(fo): FlowStep.TargetValueProvider with quiet-skip on null

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Preflight flow sets FLT/LAND ALT (with captain fallback)

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs` (replace line 101; extend `Captain` helper ~line 446; add `DynSW` helper next to `SW` ~line 380)

**Interfaces:**
- Consumes (Task 1): `AircraftStateEvaluator.PlannedFltAltFt` / `.PlannedLandAltFt` (`int?`), `.FltAltMatches()` / `.LandAltMatches()` / `.HasPressurizationPlan` (`bool`). (Task 2): `TargetValueProvider` quiet-skip. Events `EVT_OH_PRESS_FLT_ALT_SET` / `EVT_OH_PRESS_LAND_ALT_SET` exist in `PMDG737Definition.EventIds` (`PMDG737Definition.cs:3347-3348`) and are NOT in the executor's dispatch `Table` → default `Dispatch.Simple` → `SendPMDGEvent(name, id, feet)`, exactly the PR #120 panel shape.
- Produces: flow step ids `PF_FLT_ALT`, `PF_LAND_ALT` (referenced by the test plan in Task 5).

- [ ] **Step 1: Extend the Captain helper with a separate reminder text**

In `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs`, replace (line 446):

```csharp
    private static Step Captain(string id, string label) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = label,
        PostActionDelayMs = 200,
    };
```

with:

```csharp
    // reminderText: what "Captain action required: …" speaks (defaults to label). A
    // separate short label matters when the step can be SKIPPED — the skip path reads
    // "Already set: {label}", where an imperative sentence would compose badly.
    private static Step Captain(string id, string label, string? reminderText = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = reminderText ?? label,
        PostActionDelayMs = 200,
    };
```

- [ ] **Step 2: Add the DynSW helper**

In the same file, directly after the `SW` helper (after line 380's closing `};`), add:

```csharp

    // SetSwitch whose target resolves at DISPATCH time from evaluator state — for
    // SimBrief-derived values unknown when these static definitions are built. A null
    // provider result quietly skips the step (see FlowStep.TargetValueProvider).
    private static Step DynSW(string id, string label, string eventName,
        Func<AircraftStateEvaluator, int?> provider,
        Func<AircraftStateEvaluator, bool>? skipWhen = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValueProvider = provider,
        SkipCondition = skipWhen,
        PostActionDelayMs = 350,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };
```

- [ ] **Step 3: Replace the PF_PRESS captain step in BuildPreflight**

Replace (line 101):

```csharp
            Captain("PF_PRESS", "Set flight and landing altitudes on the pressurization panel."),
```

with:

```csharp
            // Pressurization FLT/LAND ALT from the SimBrief plan (PMDG Direct Control
            // events take literal feet; values pre-rounded to the knob steps at storage —
            // see AircraftStateEvaluator.SetPlannedPressurizationAltitudes). Quietly
            // skipped when no plan is loaded — the Captain fallback below announces
            // instead. Two separate steps keep the CDA writes in separate sim frames.
            // The PR #120 window monitors announce the resulting values automatically.
            DynSW("PF_FLT_ALT", "Flight altitude: set", "EVT_OH_PRESS_FLT_ALT_SET",
                s => s.PlannedFltAltFt, skipWhen: s => s.FltAltMatches()),
            DynSW("PF_LAND_ALT", "Landing altitude: set", "EVT_OH_PRESS_LAND_ALT_SET",
                s => s.PlannedLandAltFt, skipWhen: s => s.LandAltMatches()),
            Skip(Captain("PF_PRESS", "Flight and landing altitudes",
                    "Set flight and landing altitudes on the pressurization panel."),
                s => s.HasPressurizationPlan),
```

- [ ] **Step 4: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs
git commit -m "feat(fo737): preflight flow sets FLT/LAND ALT from SimBrief

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Checklist items + executor convenience

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/AircraftActionExecutor.cs` (add public method after `StartApuAsync`, ~line 267)
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737ChecklistDefinitions.cs` (lines 85, 236, 343)

**Interfaces:**
- Consumes (Task 1): `AircraftStateEvaluator.PlannedFltAltFt` / `.PlannedLandAltFt`; synthetic keys `"FO_PRESS_ALTS_MATCH"` / `"FO_PRESS_LAND_ALT_MATCH"`. Executor privates `FireSpacedAsync((string ev, int? target)[])` (same class). Checklist helpers `Auto(id, groupId, label, field, condition, revert, action)` and `AutoAsync(id, groupId, label, field, condition, revert, Func<AircraftActionExecutor, AircraftStateEvaluator, Task> action)` (`PMDG737ChecklistDefinitions.cs:404-443`).
- Produces: `public Task SetPressurizationAltitudesAsync(AircraftStateEvaluator state)` on the 737 executor.

- [ ] **Step 1: Executor convenience method**

In `MSFSBlindAssist/FirstOfficer/PMDG737/AircraftActionExecutor.cs`, after the `StartApuAsync` method (line 267's closing brace), add:

```csharp

    /// <summary>Set pressurization FLT ALT + LAND ALT from the stored SimBrief plan
    /// (values pre-rounded/clamped at evaluator storage; the direct-control events take
    /// literal feet). SPACED — two CDA writes must not share a sim frame. A missing plan
    /// value is skipped; with no plan at all this is a no-op (the checklist item then
    /// behaves like the old manual reminder).</summary>
    public async Task SetPressurizationAltitudesAsync(AircraftStateEvaluator state)
    {
        var actions = new List<(string ev, int? target)>();
        if (state.PlannedFltAltFt  is int f) actions.Add(("EVT_OH_PRESS_FLT_ALT_SET", f));
        if (state.PlannedLandAltFt is int l) actions.Add(("EVT_OH_PRESS_LAND_ALT_SET", l));
        if (actions.Count > 0) await FireSpacedAsync(actions.ToArray());
    }
```

- [ ] **Step 2: PREFLIGHT state-group item (tick fires the set; auto-ticks on match)**

In `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737ChecklistDefinitions.cs`, replace (line 85):

```csharp
            Reminder("PF_PRESS", "PREFLIGHT", "Flight and landing altitudes: SET"),
```

with:

```csharp
            // Auto-ticks when both FLT/LAND ALT windows match the SimBrief plan (synthetic
            // field — see AircraftStateEvaluator); ticking fires both direct-set events,
            // SPACED (two CDA writes must not share a frame). RevertToState: it is a value
            // check, so dialing away from the plan should untick it (the PF_AB pattern).
            // No plan loaded → the action no-ops and it behaves like the old reminder.
            AutoAsync("PF_PRESS", "PREFLIGHT", "Flight and landing altitudes: SET",
                "FO_PRESS_ALTS_MATCH", v => v > 0.5, RevertBehavior.RevertToState,
                (e, s) => e.SetPressurizationAltitudesAsync(s)),
```

- [ ] **Step 3: PREFLIGHT_CL mode-selector readback auto-ticks**

Replace (line 236):

```csharp
            Reminder("PFC_PRESS", "PREFLIGHT_CL", "Pressurization mode selector: AUTO"),
```

with:

```csharp
            Auto("PFC_PRESS", "PREFLIGHT_CL", "Pressurization mode selector: AUTO",
                "AIR_PressurizationModeSelector", v => v < 0.5, RevertBehavior.RevertToState, action: null),
```

- [ ] **Step 4: DESCENT_CL landing-altitude readback auto-verifies**

Replace (line 343):

```csharp
            Reminder("DC_PRESS", "DESCENT_CL", "Pressurization: landing altitude set"),
```

with:

```csharp
            // Auto-verifies (action-free, per the *_CL invariant): ticks when the LAND ALT
            // window matches the SimBrief destination elevation. No plan → manual tick.
            Auto("DC_PRESS", "DESCENT_CL", "Pressurization: landing altitude set",
                "FO_PRESS_LAND_ALT_MATCH", v => v > 0.5, RevertBehavior.RevertToState, action: null),
```

- [ ] **Step 5: Verify the `*_CL` invariant structurally**

Run: `Grep pattern "_CL"` over `PMDG737ChecklistDefinitions.cs` and confirm no `*_CL` group item passes a non-null action — the two converted `*_CL` items above must both say `action: null`.

- [ ] **Step 6: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/PMDG737/AircraftActionExecutor.cs MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737ChecklistDefinitions.cs
git commit -m "feat(fo737): pressurization checklist auto-detect + tick-to-set

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Docs — test plan Part I + CLAUDE.md

**Files:**
- Modify: `docs/pmdg-737-first-officer-test-plan.md` (append after Part H)
- Modify: `CLAUDE.md` (First Officer Automation section — the "SimBrief + auto-managers" paragraph)

**Interfaces:**
- Consumes: step ids `PF_FLT_ALT` / `PF_LAND_ALT` (Task 3), synthetic keys (Task 1), item behaviors (Task 4).

- [ ] **Step 1: Append Part I to the test plan**

Append to `docs/pmdg-737-first-officer-test-plan.md`:

```markdown

## Part I — pressurization FLT/LAND ALT from SimBrief (2026-07-01)

Setup: PMDG 737 at a gate, powered, MSFSBA connected. A SimBrief OFP filed — note its
initial/cruise altitude and destination field elevation.

1. **SimBrief load announce** — First Officer window → Load SimBrief. Alongside the
   existing transition/flaps announcements, expect: "Pressurization plan: flight altitude
   <cruise> feet, landing altitude <destination elevation> feet."
2. **Preflight flow sets both** — run the Preflight flow. After "Engine bleeds: ON" expect
   "Flight altitude: set" followed by the PR #120 monitor callout "Flight altitude
   <cruise, rounded to 500> feet", then "Landing altitude: set" + "Landing altitude
   <destination elevation, rounded to 50> feet". The captain pressurization reminder must
   NOT fire — instead "Already set: Flight and landing altitudes". Confirm the values on
   the Air Systems panel (Flight/Landing Altitude fields read them back on focus).
3. **Idempotent re-run** — run Preflight again: both steps announce "Already set: …" and
   send nothing.
4. **No-SimBrief fallback** — fresh session WITHOUT loading SimBrief; run Preflight:
   no "Flight altitude: set"/"Landing altitude: set" announcements at all (quiet skip),
   and the reminder fires: "Captain action required: Set flight and landing altitudes on
   the pressurization panel."
5. **Checklist tick fires the set** — SimBrief loaded, then dial FLT ALT wrong via the
   Air Systems panel. Checklists → Preflight → "Flight and landing altitudes: SET" shows
   unticked; tick it → both values set (two callouts, ~350 ms apart) and it re-ticks.
6. **Auto-tick from panel** — untick state (dial a window off-plan), then hand-set both
   windows to the planned values via the Air Systems panel: item auto-ticks.
7. **Descent auto-verify** — Descent Checklist → "Pressurization: landing altitude set"
   auto-ticks while LAND ALT matches destination elevation; dial LAND ALT 500 ft off →
   it unticks (RevertToState).
8. **Mode-selector readback** — Preflight Checklist → "Pressurization mode selector:
   AUTO" auto-ticks with the selector in AUTO; select MAN → unticks.
9. **777 regression** — load the 777, open its FO window, Load SimBrief. The
   "Pressurization plan: …" announce IS expected (it lives in the shared form; the 777
   evaluator no-ops the stored value). The key checks: no new 777 flow steps or checklist
   items, the 777 Preflight flow and checklists behave exactly as before, no errors.
```

- [ ] **Step 2: Update CLAUDE.md's FO section**

In `CLAUDE.md`, section "First Officer Automation (PMDG 777 & 737)", find the paragraph
starting `**SimBrief + auto-managers.**` and insert after its first sentence (the one
ending `(IFoStateEvaluator.SetTakeoffFlaps, aircraft-agnostic)`):

```markdown
 **737 pressurization altitudes are FO-SET from SimBrief (2026-07-01):** Load SimBrief also pushes `SetPlannedPressurizationAltitudes(cruise, destElev)` (interface method; 777 = no-op — its pressurization is FMC-automatic and stays untouched). The 737 evaluator rounds/clamps at storage (FLT nearest 500 / LAND nearest 50 ft — matches the PR #120 knob steps so the event-side round-down is a no-op) and serves synthetic match keys `FO_PRESS_ALTS_MATCH` / `FO_PRESS_LAND_ALT_MATCH` (window vs plan, strictly < one knob step). The Preflight flow sets both via `EVT_OH_PRESS_FLT/LAND_ALT_SET` using the new `FlowStep.TargetValueProvider` (resolved at dispatch; **null = QUIET skip** — no announcement, success result — paired with a Captain fallback step SkipCondition'd away when a plan exists, so no-SimBrief behavior is unchanged). Preflight SETS, descent only VERIFIES (`DC_PRESS` auto-ticks off the land match key, action-free per the `*_CL` invariant); `PF_PRESS` tick fires `SetPressurizationAltitudesAsync` (spaced writes). Test plan Part I.
```

- [ ] **Step 3: Commit**

```bash
git add docs/pmdg-737-first-officer-test-plan.md CLAUDE.md
git commit -m "docs(fo737): test plan Part I + CLAUDE.md for pressurization auto-set

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Final verification (after all tasks)

- [ ] `dotnet build MSFSBlindAssist.sln -c Debug` → Build succeeded.
- [ ] `git log --oneline -6` shows the five commits on `feature/777-first-officer`.
- [ ] Grep `TargetValueProvider` → exactly three code files: `FlowStep.cs` (definition), `FlowManager.cs` (resolution), `PMDG737FlowDefinitions.cs` (DynSW). No 777 file touched except the evaluator no-op.
- [ ] In-sim verification is the human owner's Part I run (this project has no automated tests by design).
