using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Executes PMDG 777 flows asynchronously.
/// Runs steps in sequence, handles waits and conditions, and raises events
/// that the UI and announcement service can subscribe to.
/// </summary>
public class FlowManager<TExec, TState>
    where TExec : IFoActionExecutor
    where TState : IFoStateEvaluator
{
    private readonly TState _state;
    private readonly TExec _executor;
    private readonly ChecklistManager<TExec, TState> _checklist;
    private readonly ScreenReaderAnnouncer _announcer;

    // Minimum audible gap between flow steps — a screen-reader FO must read at a
    // human pace, not zip (user request 2026-07-08). This is announcement pacing
    // ON TOP of the executors' write spacing, never a replacement for it.
    private const int InterStepPauseMs = 2000;

    // A step's verify (FlowStep.VerifyFieldName) reads first after VerifySettleMs, then every
    // VerifyPollMs until it holds or VerifyWindowMs have passed since the write. A single read
    // at 600 ms judged a snapshot the write had not reached yet: the PMDG CDA snapshot and the
    // FO window's L:var poll both refresh about once a second, so roughly half of the 777's
    // verified writes were spoken as "Skipping: …" over a switch that had moved. The window
    // spans two refreshes; a write that does land is still judged at its first fresh read.
    private const int VerifySettleMs = 600;
    private const int VerifyPollMs = 200;
    private const int VerifyWindowMs = 2600;

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private TaskCompletionSource<bool>? _pauseTcs;
    private volatile bool _paused;

    // A Stop lands at the next token-aware wait, but the executor's step takes none (it is atomic:
    // the PMDG weather radar test runs about 25 s and always restores the radar), so a Stop pressed
    // during one lands only when it returns. These say so, for the window to answer the Stop at once.
    private volatile bool _stopRequested;
    private volatile bool _inExecutorStep;

    // -----------------------------------------------------------------------
    // Events (fired from background task — consumers must marshal to UI thread)
    // -----------------------------------------------------------------------

    public event Action<FlowDefinition<TState>>? FlowStarted;
    public event Action<FlowDefinition<TState>>? FlowCompleted;
    public event Action<FlowDefinition<TState>>? FlowCancelled;
    public event Action<FlowDefinition<TState>, string>? FlowFailed;
    public event Action<FlowDefinition<TState>>? FlowPaused;
    public event Action<FlowDefinition<TState>>? FlowResumed;

    public event Action<FlowDefinition<TState>, FlowStep<TState>, int>? StepStarted;
    public event Action<FlowDefinition<TState>, FlowStep<TState>, int>? StepCompleted;
    public event Action<FlowDefinition<TState>, FlowStep<TState>, int, string>? StepFailed;
    public event Action<FlowDefinition<TState>, FlowStep<TState>, int>? StepSkipped;
    public event Action<string>? CaptainReminderRequired;

    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------

    public bool IsRunning  => _runTask is { IsCompleted: false };
    public bool IsPaused   => _paused;
    /// <summary>A Stop was pressed and the run has not unwound yet (see <see cref="IsInExecutorStep"/>).</summary>
    public bool IsStopping => _stopRequested && IsRunning;
    /// <summary>The run is waiting on the executor's step, which a Stop does not interrupt.</summary>
    public bool IsInExecutorStep => _inExecutorStep && IsRunning;
    public FlowDefinition<TState>? CurrentFlow { get; private set; }
    public int CurrentStepIndex { get; private set; }

    // Checklist items belonging to steps this run SKIPPED — a step that failed and whose
    // FailurePolicy let the flow continue, a step skipped by the dependency gate, a step left
    // alone by its LeaveAloneWhen rule, or a step whose TargetValueProvider had no value
    // (FO-20: skipped SILENTLY, the paired Captain reminder is the speech). FirstOfficerForm
    // passes these to MarkGroupComplete so flow completion cannot tick and latch an item the
    // flow never delivered. Only those four contribute: a Stop-policy failure raises FlowFailed
    // and returns, and a cancelled run raises FlowCancelled, so FlowCompleted never fires on
    // those runs; the "Already set" early-continue is a SUCCESS (it raises StepCompleted and
    // marks the item).
    //
    // Pinned by BEHAVIOUR tests on a real FlowManager run (FlowManagerStepDependencyTests,
    // FlowManagerLeaveAloneTests, FlowManagerTargetProviderTests): the announce entry points
    // are virtual, so a recording ScreenReaderAnnouncer subclass (GatedSpeechCapture,
    // tests/MuteWrapHarness.cs) replaces the screen reader. They pin which ids land here on a
    // Skip-policy failure, on a dependency skip, on a leave-alone skip and on a null target,
    // that an "Already set" step lands nothing, and that the set clears between runs. The
    // CONSUMER half — what MarkGroupComplete does with this set — is
    // FoFlowCompletionExclusionTests.
    private readonly HashSet<string> _unfinishedChecklistItemIds = new(StringComparer.Ordinal);

    // Ids of the steps THIS run skipped — a Skip-policy step that failed or timed out, a
    // step left alone by its LeaveAloneWhen rule, a step whose TargetValueProvider had no
    // value, or a step skipped because its FlowStep.RequiresStepId is in here already (so a
    // dependency chain propagates). Read by the dependency gate at the top of the step loop;
    // cleared with _unfinishedChecklistItemIds when the next run starts. Every Add() (the Skip
    // branch's, and SkipWithoutRunningAsync's for the leave-alone rule, the null target and
    // the dependency gate) is pinned by FlowManagerStepDependencyTests /
    // FlowManagerLeaveAloneTests / FlowManagerTargetProviderTests; FlowStepDependencyIdTests
    // pins that every profile's RequiresStepId names an earlier step of the same flow.
    private readonly HashSet<string> _skippedStepIds = new(StringComparer.Ordinal);

    // Ids of the SWITCH steps this run actually sent and completed (not "Already set", not
    // skipped). Read by the FlowStep.RunOnlyIfStepSent gate; cleared when the next run starts.
    private readonly HashSet<string> _sentStepIds = new(StringComparer.Ordinal);

    // What one attempt at a step came to. NoTarget: the step's FlowStep.TargetValueProvider
    // returned null (the data it needs, e.g. a SimBrief plan, is not there), so nothing was
    // sent. That is a SILENT SKIP, never a success (FO-20).
    private enum StepOutcome { Done, Failed, NoTarget }

    /// <summary>Checklist item ids the most recent run could not deliver. Valid to read
    /// from the FlowCompleted handler; cleared when the next run starts. Returns a
    /// snapshot, not the live set, so a caller may hold onto or enumerate the result
    /// without racing the next run's Clear()/Add() on this same set.</summary>
    public IReadOnlyCollection<string> UnfinishedChecklistItemIds => _unfinishedChecklistItemIds.ToArray();

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    public FlowManager(
        TState state,
        TExec executor,
        ChecklistManager<TExec, TState> checklist,
        ScreenReaderAnnouncer announcer)
    {
        _state     = state;
        _executor  = executor;
        _checklist = checklist;
        _announcer = announcer;
        // A step that waits inside the executor holds on the flow's Pause too.
        _executor.AttachFlowPause(WaitWhilePausedAsync);
    }

    // -----------------------------------------------------------------------
    // Public control API
    // -----------------------------------------------------------------------

    public void StartFlow(FlowDefinition<TState> flow)
    {
        if (IsRunning) Cancel();
        CurrentFlow = flow;
        CurrentStepIndex = 0;
        _paused = false;
        _stopRequested = false;
        _inExecutorStep = false;
        _cts = new CancellationTokenSource();
        _runTask = RunFlowAsync(flow, _cts);
    }

    public void Pause()
    {
        if (!IsRunning || _paused) return;
        _paused = true;
        // RunContinuationsAsynchronously: Resume() is a UI-thread click and the flow awaits on
        // the UI thread, so a default TCS would run the rest of the flow INLINE inside
        // TrySetResult — before FlowResumed and the "resumed" announcement. A flow paused in its
        // final wait then sent "flow complete" (non-interrupting) and had it cut off by the
        // interrupting "resumed", with the status overwritten to "Resumed".
        _pauseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (CurrentFlow != null) FlowPaused?.Invoke(CurrentFlow);
        _announcer.AnnounceImmediate($"{CurrentFlow?.Name ?? "Flow"} paused");
    }

    public void Resume()
    {
        if (!_paused) return;
        _paused = false;
        _pauseTcs?.TrySetResult(true);
        _pauseTcs = null;
        if (CurrentFlow != null) FlowResumed?.Invoke(CurrentFlow);
        _announcer.AnnounceImmediate($"{CurrentFlow?.Name ?? "Flow"} resumed");
    }

    public void Cancel()
    {
        if (IsRunning) _stopRequested = true;
        _paused = false;
        _pauseTcs?.TrySetCanceled();
        _cts?.Cancel();
        _cts = null;
    }

    // -----------------------------------------------------------------------
    // Private execution engine
    // -----------------------------------------------------------------------

    // Waits out an active Pause. Runs at the top of every step, on every iteration of the
    // WaitForCondition loop before the condition is read, and once more immediately before
    // FlowCompleted fires — see those call sites for the bugs each closes. Cancellation is
    // NOT caught here: it propagates as OperationCanceledException to RunFlowAsync's single
    // cancellation exit.
    private Task WaitWhilePausedAsync(CancellationToken ct)
    {
        if (_paused && _pauseTcs != null)
            return _pauseTcs.Task.WaitAsync(ct);
        return Task.CompletedTask;
    }

    /// <summary>A skipped step (a Skip-policy failure, or one never run: leave-alone rule,
    /// dependency gate, a target provider with no value): keeps its linked checklist lines out of
    /// the completion latch, records its id as skipped, raises StepSkipped, speaks
    /// <paramref name="spokenText"/> (queued; null speaks nothing — the null-target skip is silent,
    /// FO-20), and pauses <see cref="InterStepPauseMs"/> unless it is the last step. A cancellation
    /// during that pause propagates to RunFlowAsync's cancellation exit.</summary>
    private async Task RecordSkipAsync(
        FlowDefinition<TState> flow, FlowStep<TState> step, int index, string? spokenText, CancellationToken ct)
    {
        foreach (var itemId in step.LinkedChecklistItemIds)
            _unfinishedChecklistItemIds.Add(itemId);
        _skippedStepIds.Add(step.Id);
        StepSkipped?.Invoke(flow, step, index);
        if (spokenText != null)
            _announcer.Announce(spokenText);
        if (index < flow.Steps.Count - 1)
            await Task.Delay(InterStepPauseMs, ct);
    }

    // A run that ends part-way, by a Stop or by a Stop-policy failure, lets the executor put back
    // what a flow must never be left between (the MD-11's START pulled, its fuel still OFF) before
    // the run says how it ended. A clean-up that throws is logged; the run still ends.
    private async Task CleanUpStoppedRunAsync(FlowDefinition<TState> flow)
    {
        try { await _executor.OnFlowStoppedAsync(); }
        catch (Exception ex) { Log.Warn("FO", $"{flow.Id}: stop clean-up threw: {ex.Message}"); }
    }

    private async Task RunFlowAsync(FlowDefinition<TState> flow, CancellationTokenSource run)
    {
        var ct = run.Token;
        _unfinishedChecklistItemIds.Clear();
        _skippedStepIds.Clear();
        _sentStepIds.Clear();
        FlowStarted?.Invoke(flow);
        _announcer.AnnounceImmediate($"{flow.Name} flow started");

        // ONE exit for every cancellation (Stop Flow, a new flow started over this one, the
        // window disposed on an aircraft switch or app exit), wherever the run is: between
        // steps, in a pause, a wait, a verify or the inter-step delay. Each of those once caught
        // its own cancellation and raised FlowCancelled without a word, so Stop was silent
        // unless it landed exactly between two steps.
        try
        {
            for (int i = 0; i < flow.Steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await WaitWhilePausedAsync(ct);

                CurrentStepIndex = i;
                var step = flow.Steps[i];

                // Check if step is already in the desired state — skip gracefully
                if (step.SkipCondition != null && _state.IsAvailable && step.SkipCondition(_state))
                {
                    _announcer.Announce($"Already set: {step.AnnounceText}");
                    Log.Debug("FO", $"{flow.Id}.{step.Id}: already set");
                    StepCompleted?.Invoke(flow, step, i);
                    foreach (var itemId in step.LinkedChecklistItemIds)
                        _checklist.MarkComplete(itemId);
                    if (i < flow.Steps.Count - 1)
                        await Task.Delay(InterStepPauseMs, ct);
                    continue;
                }

                // A step the aircraft's state says the First Officer must NOT perform
                // (FlowStep.LeaveAloneWhen — arming a speed brake that is already deployed would
                // retract it): nothing is sent, the step's reason is spoken, and it is kept out of
                // the completion latch exactly like a skipped step, so its lines keep mirroring the
                // aircraft. After SkipCondition on purpose — "Already set" is the truer answer when
                // the aircraft is already there.
                if (step.LeaveAloneWhen != null && _state.IsAvailable && step.LeaveAloneWhen(_state))
                {
                    await RecordSkipAsync(flow, step, i,
                        step.LeaveAloneText ?? $"Skipping: {step.AnnounceText}", ct);
                    continue;
                }

                // A step that builds on an earlier step this run could not complete
                // (FlowStep.RequiresStepId) is skipped like a failed Skip-policy step: its
                // linked items stay out of the latch, and its own id joins the skipped set so
                // a chain of dependent steps stays skipped. After the SkipCondition above on
                // purpose — "Already set" is the truer answer when the aircraft is already
                // there, and a skip text that says what stays as it is would be wrong then.
                if (step.RequiresStepId != null && _skippedStepIds.Contains(step.RequiresStepId))
                {
                    await RecordSkipAsync(flow, step, i,
                        step.RequiresStepSkipText ?? $"Skipping: {step.AnnounceText}", ct);
                    continue;
                }

                // A step that only follows an earlier switch this run actually SENT
                // (FlowStep.RunOnlyIfStepSent: the APU cool-down waits). When that switch was
                // already set, skipped or failed there is nothing to wait for: pass silently as
                // done — no speech, so no inter-step pause either (FO-5 paces spoken steps), and
                // not a skip, so a step that requires this one still runs.
                if (step.RunOnlyIfStepSent != null && !_sentStepIds.Contains(step.RunOnlyIfStepSent))
                {
                    Log.Debug("FO", $"{flow.Id}.{step.Id}: passed, {step.RunOnlyIfStepSent} was not sent this run");
                    StepCompleted?.Invoke(flow, step, i);
                    foreach (var itemId in step.LinkedChecklistItemIds)
                        _checklist.MarkComplete(itemId);
                    continue;
                }

                StepStarted?.Invoke(flow, step, i);

                StepOutcome outcome = await ExecuteStepAsync(flow, step, i, ct);

                // A Stop while the executor was writing (its steps are atomic; only the MD-11's
                // light-up watch and the iFly's engine-start waits end early on the Stop) lands
                // here: the step's outcome is not
                // reported, its lines not marked, and the run goes to the cancellation exit
                // instead of on to the next step or to "flow complete".
                ct.ThrowIfCancellationRequested();

                // FO-20: a TargetValueProvider with no value (no SimBrief plan) is a SILENT SKIP,
                // never a success. Nothing was sent, so its lines are neither marked nor latched:
                // as a quiet success it latched "Flaps: takeoff setting" over a lever at 0 on the
                // A320s. Silent because the step's paired Captain reminder is what the pilot hears
                // ("Skipping: …" would say the flow failed when it only had no plan). Its id joins
                // the skipped set like any other skip, so a step that RequiresStepId it — one that
                // would build on a value the flow never set — is skipped too, as after a failure.
                if (outcome == StepOutcome.NoTarget)
                {
                    Log.Debug("FO", $"{flow.Id}.{step.Id}: no target value, skipped silently");
                    await RecordSkipAsync(flow, step, i, spokenText: null, ct);
                    continue;
                }

                if (outcome == StepOutcome.Failed)
                {
                    if (step.FailurePolicy == FlowStepFailurePolicy.Stop)
                    {
                        Log.Debug("FO", $"{flow.Id}.{step.Id}: failed, flow stopped");
                        // Ended part-way as after a Stop: the run's token is cancelled first, which
                        // is how an executor tells this run's work from a later run's (the MD-11's
                        // START pulls), and the executor puts back what it must. Without it a fuel
                        // step skipped and a timed-out wait after it left START pulled, fuel OFF.
                        run.Cancel();
                        await CleanUpStoppedRunAsync(flow);
                        FlowFailed?.Invoke(flow, $"Step '{step.Label}' failed");
                        _announcer.AnnounceImmediate($"{flow.Name} flow stopped. Unable to complete: {step.AnnounceText}");
                        return;
                    }

                    // Skip policy. EVERY linked item, not just CompletesChecklistItemId — a step
                    // that delivers a line in both the action group and the read-back checklist
                    // (AlsoCompletesChecklistItemIds) must keep both out of the latch. Spoken, so
                    // paced like every other spoken step (FO-5): without the pause the next step's
                    // words and write followed "Skipping" at once.
                    Log.Debug("FO", $"{flow.Id}.{step.Id}: failed, skipped");
                    await RecordSkipAsync(flow, step, i, $"Skipping: {step.AnnounceText}", ct);
                }
                else
                {
                    Log.Debug("FO", $"{flow.Id}.{step.Id}: done");
                    if (step.ActionType is FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple)
                        _sentStepIds.Add(step.Id);
                    StepCompleted?.Invoke(flow, step, i);

                    // Auto-tick linked checklist item
                    foreach (var itemId in step.LinkedChecklistItemIds)
                        _checklist.MarkComplete(itemId);

                    // The pause between steps, so flows read at a human pace (FO-5).
                    if (i < flow.Steps.Count - 1)
                        await Task.Delay(InterStepPauseMs, ct);
                }
            }

            // Pause check before completion: without this, a flow paused during its own final
            // step (e.g. the 737/iFly 20 s gear-check wait) still fell through here and
            // completed — announcing "flow complete" and latching the checklist group — while
            // the window still showed Paused. Wait out any active pause first, same as the
            // top-of-loop check above.
            await WaitWhilePausedAsync(ct);
            // A Stop during the last step reaches no further token-aware wait (the last step has
            // no inter-step pause), so it is checked once more: it must never complete the flow,
            // latch its groups and say "flow complete" after the pilot pressed Stop.
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            // Before "flow cancelled": a flow stopped between two steps it must never be left
            // between (the MD-11's START pulled, fuel still OFF) is put back first. A detached
            // executor (the window disposed) has no aircraft to write to and does nothing.
            await CleanUpStoppedRunAsync(flow);
            FlowCancelled?.Invoke(flow);
            // Queued, not interrupting: on an aircraft switch it follows the switch's own
            // speech instead of cutting it off, and after Stop Flow nothing else is speaking.
            _announcer.Announce($"{flow.Name} flow cancelled");
            return;
        }
        catch (Exception ex)
        {
            // A throw outside ExecuteStepAsync's own catch: a SkipCondition or LeaveAloneWhen reading
            // the evaluator mid-reconnect, a StepStarted or StepCompleted handler. Ended like a
            // Stop-policy failure and said aloud: it once faulted the run in silence, with no
            // FlowFailed, so the window kept showing the flow running and the fault went unobserved.
            var failed = CurrentStepIndex >= 0 && CurrentStepIndex < flow.Steps.Count ? flow.Steps[CurrentStepIndex] : null;
            Log.Error("FO", $"{flow.Id}.{failed?.Id}: flow threw", ex);
            run.Cancel();
            await CleanUpStoppedRunAsync(flow);
            FlowFailed?.Invoke(flow, $"Step '{failed?.Label}' threw");
            _announcer.AnnounceImmediate(failed != null
                ? $"{flow.Name} flow stopped. Unable to complete: {failed.AnnounceText}"
                : $"{flow.Name} flow stopped.");
            return;
        }

        FlowCompleted?.Invoke(flow);
        // NON-INTERRUPTING (Announce), never AnnounceImmediate (owner decision 2026-09-22).
        // This runs straight after the last step with no pause, and AnnounceImmediate
        // interrupts the screen reader and cancels its buffered speech — so the last step of
        // a flow was routinely cut off, including a skipped wait's "Timed out waiting for: … /
        // Skipping: …" (the PMDG 737 gear checks), which a blind pilot then heard as silence
        // followed by "flow complete": success. Non-interrupting, the step's own words are
        // heard first and "flow complete" follows them. Pinned by the source-text guard
        // FlowManager_FlowComplete_IsNonInterrupting (FoPr160ProcedureFixTests).
        _announcer.Announce($"{flow.Name} flow complete");
    }

    private async Task<StepOutcome> ExecuteStepAsync(FlowDefinition<TState> flow, FlowStep<TState> step, int index, CancellationToken ct)
    {
        try
        {
            switch (step.ActionType)
            {
                case FlowStepActionType.CaptainReminder:
                {
                    string text = step.ReminderText ?? step.Label;
                    CaptainReminderRequired?.Invoke(text);
                    _announcer.Announce($"Captain action required: {text}");
                    return StepOutcome.Done;
                }

                case FlowStepActionType.WaitSeconds:
                {
                    int total = step.WaitSeconds;
                    _announcer.Announce($"Waiting {total} seconds: {step.AnnounceText}");
                    await Task.Delay(TimeSpan.FromSeconds(total), ct);
                    return StepOutcome.Done;
                }

                case FlowStepActionType.WaitForCondition:
                {
                    if (step.ConditionFieldName == null || step.Condition == null)
                        return StepOutcome.Done; // No condition defined — treat as complete

                    _announcer.Announce($"Waiting for: {step.AnnounceText}");
                    // Read at 0, 1, … TimeoutSeconds: the last read is AT the timeout, after its own
                    // pause check. Ending on the count after the last 1 s delay, a Pause pressed in
                    // that second still timed the wait out under "Paused", and a condition that came
                    // true in it was reported as timed out.
                    for (int elapsed = 0; ; elapsed++)
                    {
                        ct.ThrowIfCancellationRequested();
                        // Pause check, before the condition is read: without this, Pause
                        // during a flow's FINAL wait (e.g. the 737/iFly 20 s gear checks)
                        // was announced but had no effect — elapsed kept advancing and the
                        // wait still timed out (and the flow still went on to complete and
                        // latch) while the window showed Paused. Cancellation here
                        // propagates like ct.ThrowIfCancellationRequested above — caught by
                        // this method's own catch block below, same as everywhere else in
                        // this loop.
                        await WaitWhilePausedAsync(ct);
                        double v = _state.GetValue(step.ConditionFieldName);
                        if (step.Condition(v)) return StepOutcome.Done;
                        if (elapsed >= step.TimeoutSeconds) break;
                        await Task.Delay(1000, ct);
                    }
                    _announcer.Announce($"Timed out waiting for: {step.AnnounceText}");
                    StepFailed?.Invoke(flow, step, index, "Timed out");
                    return StepOutcome.Failed;
                }

                case FlowStepActionType.SetSwitch:
                case FlowStepActionType.SetSwitchMultiple:
                {
                    // Resolve a dynamic target (e.g. SimBrief-derived) just before dispatch.
                    // Null = required data unavailable → NoTarget, a SILENT SKIP handled by
                    // RunFlowAsync (FO-20; see TargetValueProvider). Never a success.
                    if (step.TargetValueProvider != null)
                    {
                        int? resolved = step.TargetValueProvider(_state);
                        if (resolved is null) return StepOutcome.NoTarget;
                        step.TargetValue = resolved;
                    }

                    if (!_executor.IsAvailable)
                    {
                        _announcer.Announce($"Sim not connected — cannot perform: {step.AnnounceText}");
                        return StepOutcome.Failed;
                    }

                    _announcer.Announce(step.AnnounceText);
                    bool sent;
                    _inExecutorStep = true;
                    try { sent = await _executor.ExecuteStepAsync(step, ct); }
                    finally { _inExecutorStep = false; }
                    if (!sent)
                    {
                        StepFailed?.Invoke(flow, step, index, "Event not sent");
                        return StepOutcome.Failed;
                    }

                    // Optionally verify the state the write was meant to reach (see VerifyWindowMs)
                    if (step.VerifyFieldName != null && step.VerifyCondition != null
                        && !await VerifiesWithinWindowAsync(step.VerifyFieldName, step.VerifyCondition, ct))
                    {
                        StepFailed?.Invoke(flow, step, index, "State verification failed");
                        return StepOutcome.Failed;
                    }
                    return StepOutcome.Done;
                }

                default:
                    return StepOutcome.Done;
            }
        }
        // A cancellation goes to RunFlowAsync's single exit, which raises FlowCancelled once.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            StepFailed?.Invoke(flow, step, index, ex.Message);
            return StepOutcome.Failed;
        }
    }

    // True as soon as the verify field satisfies its condition, false once VerifyWindowMs have
    // passed without it (reads after VerifySettleMs, then every VerifyPollMs).
    private async Task<bool> VerifiesWithinWindowAsync(string field, Func<double, bool> condition, CancellationToken ct)
    {
        await Task.Delay(VerifySettleMs, ct);
        for (int waited = VerifySettleMs; ; waited += VerifyPollMs)
        {
            if (condition(_state.GetValue(field))) return true;
            if (waited >= VerifyWindowMs) return false;
            await Task.Delay(VerifyPollMs, ct);
        }
    }
}
