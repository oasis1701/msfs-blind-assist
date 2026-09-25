using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.FirstOfficer.Models;

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

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private TaskCompletionSource<bool>? _pauseTcs;
    private volatile bool _paused;

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
    public FlowDefinition<TState>? CurrentFlow { get; private set; }
    public int CurrentStepIndex { get; private set; }

    // Checklist items belonging to steps this run announced as SKIPPED — i.e. the step
    // failed and its FailurePolicy let the flow continue. FirstOfficerForm passes these to
    // MarkGroupComplete so flow completion cannot tick and latch an item the flow never
    // delivered. Only the Skip branch contributes: Stop and an exhausted RetryThenStop both
    // raise FlowFailed and return, so FlowCompleted never fires on those runs, and the
    // "Already set" early-continue is a SUCCESS (it raises StepCompleted and marks the item).
    //
    // NOT UNIT-TESTED, and not for want of trying: reaching this bookkeeping needs a real
    // FlowManager run, and the constructor takes a concrete ScreenReaderAnnouncer — no
    // parameterless ctor, no interface, no virtual members, and a real ctor that loads the
    // Tolk native DLL and starts an NVDA client, so a test process would drive whatever
    // screen reader is running on the machine. Passing null! fails immediately: RunFlowAsync
    // announces "flow started" before it examines a single step. The same blocker is already
    // recorded in IFly737AutoManagerTests / IFly737ExecutorTests. The CONSUMER half is
    // covered — FoFlowCompletionExclusionTests pins what MarkGroupComplete does with this
    // set — so what rests on reading is only which ids land in it and when it clears.
    // Treat the Clear() below and the Skip branch's Add() as load-bearing: deleting either
    // silently un-ticks a later flow's items, with no test to catch it.
    private readonly HashSet<string> _unfinishedChecklistItemIds = new(StringComparer.Ordinal);

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
        _cts = new CancellationTokenSource();
        _runTask = RunFlowAsync(flow, _cts.Token);
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
        _paused = false;
        _pauseTcs?.TrySetCanceled();
        _cts?.Cancel();
        _cts = null;
    }

    // -----------------------------------------------------------------------
    // Private execution engine
    // -----------------------------------------------------------------------

    // Waits out an active Pause. Factored out of the original top-of-loop check (still
    // called there, unchanged behaviour) so the same wait can ALSO run (1) on every
    // iteration of the WaitForCondition loop, before the condition is read, and (2) once
    // more immediately before FlowCompleted fires — see those call sites for the bugs
    // each closes. Cancellation is NOT caught here: it propagates as
    // OperationCanceledException so each call site can handle it the way it already
    // handles cancellation elsewhere in that same method.
    private Task WaitWhilePausedAsync(CancellationToken ct)
    {
        if (_paused && _pauseTcs != null)
            return _pauseTcs.Task.WaitAsync(ct);
        return Task.CompletedTask;
    }

    private async Task RunFlowAsync(FlowDefinition<TState> flow, CancellationToken ct)
    {
        _unfinishedChecklistItemIds.Clear();
        FlowStarted?.Invoke(flow);
        _announcer.AnnounceImmediate($"{flow.Name} flow started");

        for (int i = 0; i < flow.Steps.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                FlowCancelled?.Invoke(flow);
                _announcer.AnnounceImmediate($"{flow.Name} flow cancelled");
                return;
            }

            // Pause check
            try { await WaitWhilePausedAsync(ct); }
            catch (OperationCanceledException)
            {
                FlowCancelled?.Invoke(flow);
                return;
            }

            CurrentStepIndex = i;
            var step = flow.Steps[i];

            // Check if step is already in the desired state — skip gracefully
            if (step.SkipCondition != null && _state.IsAvailable && step.SkipCondition(_state))
            {
                _announcer.Announce($"Already set: {step.AnnounceText}");
                StepCompleted?.Invoke(flow, step, i);
                foreach (var itemId in step.LinkedChecklistItemIds)
                    _checklist.MarkComplete(itemId);
                if (i < flow.Steps.Count - 1)
                {
                    try { await Task.Delay(InterStepPauseMs, ct); }
                    catch (OperationCanceledException) { FlowCancelled?.Invoke(flow); return; }
                }
                continue;
            }

            StepStarted?.Invoke(flow, step, i);

            bool success = await ExecuteStepAsync(flow, step, i, ct);

            if (!success)
            {
                switch (step.FailurePolicy)
                {
                    case FlowStepFailurePolicy.Stop:
                        FlowFailed?.Invoke(flow, $"Step '{step.Label}' failed");
                        _announcer.AnnounceImmediate($"{flow.Name} flow stopped. Unable to complete: {step.AnnounceText}");
                        return;

                    case FlowStepFailurePolicy.Skip:
                        // EVERY linked item, not just CompletesChecklistItemId — a step that
                        // delivers a line in both the action group and the read-back checklist
                        // (AlsoCompletesChecklistItemIds) must keep both out of the latch.
                        foreach (var itemId in step.LinkedChecklistItemIds)
                            _unfinishedChecklistItemIds.Add(itemId);
                        StepSkipped?.Invoke(flow, step, i);
                        _announcer.Announce($"Skipping: {step.AnnounceText}");
                        break;

                    case FlowStepFailurePolicy.RetryThenStop:
                        bool retried = false;
                        for (int r = 0; r < step.RetryCount; r++)
                        {
                            await Task.Delay(1000, ct);
                            bool retryOk = await ExecuteStepAsync(flow, step, i, ct);
                            if (retryOk) { retried = true; break; }
                        }
                        if (!retried)
                        {
                            FlowFailed?.Invoke(flow, $"Step '{step.Label}' failed after retries");
                            _announcer.AnnounceImmediate($"{flow.Name} flow stopped. Unable to complete: {step.AnnounceText}");
                            return;
                        }
                        break;
                }
            }
            else
            {
                StepCompleted?.Invoke(flow, step, i);

                // Auto-tick linked checklist item
                foreach (var itemId in step.LinkedChecklistItemIds)
                    _checklist.MarkComplete(itemId);

                // Delay between steps — at least InterStepPauseMs so flows read at
                // a human pace; a longer per-step PostActionDelayMs still wins.
                int pause = Math.Max(step.PostActionDelayMs, InterStepPauseMs);
                if (i < flow.Steps.Count - 1 && pause > 0)
                {
                    try { await Task.Delay(pause, ct); }
                    catch (OperationCanceledException) { FlowCancelled?.Invoke(flow); return; }
                }
            }
        }

        // Pause check before completion: without this, a flow paused during its own final
        // step (e.g. the 737/iFly 20 s gear-check wait) still fell through here and
        // completed — announcing "flow complete" and latching the checklist group — while
        // the window still showed Paused. Wait out any active pause first, same as the
        // top-of-loop check above.
        try { await WaitWhilePausedAsync(ct); }
        catch (OperationCanceledException)
        {
            FlowCancelled?.Invoke(flow);
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

    private async Task<bool> ExecuteStepAsync(FlowDefinition<TState> flow, FlowStep<TState> step, int index, CancellationToken ct)
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
                    return true;
                }

                case FlowStepActionType.WaitSeconds:
                {
                    int total = step.WaitSeconds;
                    _announcer.Announce($"Waiting {total} seconds: {step.AnnounceText}");
                    await Task.Delay(TimeSpan.FromSeconds(total), ct);
                    return true;
                }

                case FlowStepActionType.WaitForCondition:
                {
                    if (step.ConditionFieldName == null || step.Condition == null)
                        return true; // No condition defined — treat as complete

                    _announcer.Announce($"Waiting for: {step.AnnounceText}");
                    int elapsed = 0;
                    while (elapsed < step.TimeoutSeconds)
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
                        if (step.Condition(v)) return true;
                        await Task.Delay(1000, ct);
                        elapsed++;
                    }
                    _announcer.Announce($"Timed out waiting for: {step.AnnounceText}");
                    StepFailed?.Invoke(flow, step, index, "Timed out");
                    return false;
                }

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
                    if (!sent)
                    {
                        StepFailed?.Invoke(flow, step, index, "Event not sent");
                        return false;
                    }

                    // Optionally verify state after brief settle time
                    if (step.VerifyFieldName != null && step.VerifyCondition != null)
                    {
                        await Task.Delay(600, ct);
                        double v = _state.GetValue(step.VerifyFieldName);
                        if (!step.VerifyCondition(v))
                        {
                            StepFailed?.Invoke(flow, step, index, "State verification failed");
                            return false;
                        }
                    }
                    return true;
                }

                default:
                    return true;
            }
        }
        catch (OperationCanceledException)
        {
            FlowCancelled?.Invoke(flow);
            throw;
        }
        catch (Exception ex)
        {
            StepFailed?.Invoke(flow, step, index, ex.Message);
            return false;
        }
    }
}
