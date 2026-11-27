using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.DA40;

using Step = FlowStep<DA40FoStateEvaluator>;
using Item = ChecklistItem<DA40FoActionExecutor, DA40FoStateEvaluator>;
using Flow = FlowDefinition<DA40FoStateEvaluator>;
using Group = ChecklistGroup<DA40FoActionExecutor, DA40FoStateEvaluator>;

/// <summary>
/// One DA40 phase, built ONCE into both of its halves: the flow the First Officer runs and the
/// action checklist that mirrors it. Every write step is its own action line, whose tick fires the
/// same write and whose auto-detect condition is the step's own "Already set" condition; every
/// Captain step is a Captain line; a wait or a read-only check has no action line and completes the
/// read-back lines it confirms. Built together, the two cannot drift apart, which is what FO-7 asks
/// for: no action line without a step that delivers it.
///
/// A step's read-back links (<c>cl</c>) name the lines of the aircraft's own checklist page it
/// delivers (<see cref="DA40FoReadback"/>); FlowManager marks them, and keeps them out of the latch
/// when the step is skipped (FO-6).
/// </summary>
internal sealed class DA40FoPhase
{
    public DA40FoPhase(string id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }

    /// <summary>Further groups a finished run latches (the read-back page a shared page's flow
    /// shares: the XLS's three engine starts share one COWS page).</summary>
    public string[] Related { get; init; } = Array.Empty<string>();

    public List<Step> Steps { get; } = new();
    public List<Item> Items { get; } = new();

    public Flow ToFlow() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        RelatedChecklistGroupIds = Related,
        Steps = Steps,
    };

    public Group ToGroup() => new() { Id = Id, Name = Name, Items = Items };

    // -----------------------------------------------------------------------
    // Builders
    // -----------------------------------------------------------------------

    /// <summary>A switch the First Officer sets: "Already set" and the action line both read
    /// <paramref name="field"/> through <paramref name="isSet"/>.</summary>
    public DA40FoPhase Set(string id, string label, string key, int value, string field, Func<double, bool> isSet,
        params string[] cl)
    {
        Steps.Add(Link(new Step
        {
            Id = id, Label = label,
            ActionType = FlowStepActionType.SetSwitch,
            EventName = key, TargetValue = value,
            SkipCondition = s => isSet(s.GetValue(field)),
            FailurePolicy = FlowStepFailurePolicy.Skip,
        }, id, cl));
        Items.Add(Auto(id, label, field, isSet, (e, _) => e.Set(key, value)));
        return this;
    }

    /// <summary>Several switches set by one step (all lights off), detected on one field.</summary>
    public DA40FoPhase Multi(string id, string label, (string Key, int Value)[] writes, string field,
        Func<double, bool> isSet, params string[] cl)
    {
        Steps.Add(Link(new Step
        {
            Id = id, Label = label,
            ActionType = FlowStepActionType.SetSwitchMultiple,
            MultiActions = writes.Select(w => (w.Key, (int?)w.Value)).ToList(),
            SkipCondition = s => isSet(s.GetValue(field)),
            FailurePolicy = FlowStepFailurePolicy.Skip,
        }, id, cl));
        Items.Add(Auto(id, label, field, isSet, async (e, _) =>
        {
            foreach (var (key, value) in writes) await e.Set(key, value);
        }));
        return this;
    }

    /// <summary>
    /// A write with no "Already set": a test move (the flap travel test, the voter swap) whose
    /// whole point is the movement. With <paramref name="field"/> it has an action line detected
    /// there; without one it is flow-only.
    /// </summary>
    public DA40FoPhase Move(string id, string label, string key, int value, string? field = null,
        Func<double, bool>? isSet = null, params string[] cl)
    {
        Steps.Add(Link(new Step
        {
            Id = id, Label = label,
            ActionType = FlowStepActionType.SetSwitch,
            EventName = key, TargetValue = value,
            FailurePolicy = FlowStepFailurePolicy.Skip,
        }, field != null ? id : null, cl));
        if (field != null && isSet != null)
            Items.Add(Auto(id, label, field, isSet, (e, _) => e.Set(key, value)));
        return this;
    }

    /// <summary>
    /// A composite the executor runs itself (a pseudo-key: the crank, the prime, the ECU test, the
    /// transponder, the doors). With <paramref name="field"/> its line is detected there; without one
    /// the line is a plain action the pilot ticks to run it.
    /// </summary>
    public DA40FoPhase Run(string id, string label, string key, int value, string? field = null,
        Func<double, bool>? isSet = null, FlowStepFailurePolicy onFail = FlowStepFailurePolicy.Skip,
        params string[] cl)
    {
        Steps.Add(Link(new Step
        {
            Id = id, Label = label,
            ActionType = FlowStepActionType.SetSwitch,
            EventName = key, TargetValue = value,
            SkipCondition = field != null && isSet != null ? s => isSet(s.GetValue(field)) : null,
            FailurePolicy = onFail,
        }, id, cl));
        Items.Add(field != null && isSet != null
            ? Auto(id, label, field, isSet, (e, _) => e.Set(key, value))
            : Action(id, label, (e, _) => e.Set(key, value)));
        return this;
    }

    /// <summary>
    /// A setting whose value comes from the aircraft at the moment it is made (the tank to select,
    /// the altimeter to match). With no value the step is a SILENT skip (FO-20) and the paired
    /// Captain step, skipped whenever there is a value, says what to do.
    /// </summary>
    public DA40FoPhase Provide(string id, string label, string key, Func<DA40FoStateEvaluator, int?> provider,
        string field, Func<double, bool> isSet, string noValueReminder, params string[] cl)
    {
        Steps.Add(Link(new Step
        {
            Id = id, Label = label,
            ActionType = FlowStepActionType.SetSwitch,
            EventName = key,
            TargetValueProvider = provider,
            SkipCondition = s => isSet(s.GetValue(field)),
            FailurePolicy = FlowStepFailurePolicy.Skip,
        }, id, cl));
        Steps.Add(new Step
        {
            Id = id + "_CPT", Label = noValueReminder,
            ActionType = FlowStepActionType.CaptainReminder,
            ReminderText = noValueReminder,
            SkipCondition = s => provider(s) != null || isSet(s.GetValue(field)),
        });
        Items.Add(Auto(id, label, field, isSet,
            (e, s) => provider(s) is int v ? e.Set(key, v) : Task.CompletedTask));
        return this;
    }

    /// <summary>A read-only check: waits for <paramref name="field"/>, never writes, skipped on a
    /// timeout. It has no action line of its own; it completes the read-back lines it confirms.</summary>
    public DA40FoPhase Check(string id, string label, string field, Func<double, bool> holds, int timeoutSeconds,
        params string[] cl)
    {
        Steps.Add(Link(new Step
        {
            Id = id, Label = label,
            ActionType = FlowStepActionType.WaitForCondition,
            ConditionFieldName = field,
            Condition = holds,
            TimeoutSeconds = timeoutSeconds,
            FailurePolicy = FlowStepFailurePolicy.Skip,
        }, null, cl));
        return this;
    }

    /// <summary>A fixed wait (the cool-down, the voter's RPM bump). Flow-only.</summary>
    public DA40FoPhase Wait(string id, string label, int seconds, params string[] cl)
    {
        Steps.Add(Link(new Step
        {
            Id = id, Label = label,
            ActionType = FlowStepActionType.WaitSeconds,
            WaitSeconds = seconds,
        }, null, cl));
        return this;
    }

    /// <summary>A Captain item: spoken, never written; "Already set" when <paramref name="done"/>
    /// reads true (a lever already at idle).</summary>
    public DA40FoPhase Captain(string id, string text, Func<DA40FoStateEvaluator, bool>? done = null,
        params string[] cl)
    {
        Steps.Add(Link(new Step
        {
            Id = id, Label = text,
            ActionType = FlowStepActionType.CaptainReminder,
            ReminderText = text,
            SkipCondition = done,
        }, id, cl));
        Items.Add(new Item
        {
            Id = id, GroupId = Id, Label = text,
            Type = ChecklistItemType.CaptainReminder,
            ManualCompletionAllowed = true,
            ReminderText = text,
        });
        return this;
    }

    // -----------------------------------------------------------------------
    // Modifiers, applied to the step and line just added
    // -----------------------------------------------------------------------

    /// <summary>The last step (and its line) must not be performed in this state: nothing is sent,
    /// <paramref name="text"/> is spoken, its lines stay open (FlowStep.LeaveAloneWhen).</summary>
    public DA40FoPhase LeaveAlone(Func<DA40FoStateEvaluator, bool> when, string text)
    {
        var step = Steps[^1];
        step.LeaveAloneWhen = when;
        step.LeaveAloneText = text;
        if (Items.Count > 0 && Items[^1].Id == step.Id)
        {
            Items[^1].LeaveAloneWhen = when;
            Items[^1].LeaveAloneText = text;
        }
        return this;
    }

    /// <summary>The last step builds on <paramref name="stepId"/>: skipped, saying
    /// <paramref name="text"/>, when that one was skipped this run (FlowStep.RequiresStepId).</summary>
    public DA40FoPhase Requires(string stepId, string text)
    {
        Steps[^1].RequiresStepId = stepId;
        Steps[^1].RequiresStepSkipText = text;
        return this;
    }

    /// <summary>A failure of the last step stops the flow (the engine did not start).</summary>
    public DA40FoPhase StopOnFailure()
    {
        Steps[^1].FailurePolicy = FlowStepFailurePolicy.Stop;
        return this;
    }

    // -----------------------------------------------------------------------

    private static Step Link(Step step, string? actionLine, string[] cl)
    {
        step.CompletesChecklistItemId = actionLine;
        step.AlsoCompletesChecklistItemIds = cl;
        return step;
    }

    private Item Auto(string id, string label, string field, Func<double, bool> isSet,
        Func<DA40FoActionExecutor, DA40FoStateEvaluator, Task> action) => new()
    {
        Id = id, GroupId = Id, Label = label,
        Type = ChecklistItemType.AutoDetectable,
        AutoCompleteAllowed = true,
        ManualCompletionAllowed = true,
        StateFieldName = field,
        StateCondition = isSet,
        RevertBehavior = RevertBehavior.RevertToState,
        CheckAction = action,
    };

    private Item Action(string id, string label, Func<DA40FoActionExecutor, DA40FoStateEvaluator, Task> action) => new()
    {
        Id = id, GroupId = Id, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
        CheckAction = action,
    };
}
