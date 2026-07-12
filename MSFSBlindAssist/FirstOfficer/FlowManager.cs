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
        _pauseTcs = new TaskCompletionSource<bool>();
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

    private async Task RunFlowAsync(FlowDefinition<TState> flow, CancellationToken ct)
    {
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
            if (_paused && _pauseTcs != null)
            {
                try { await _pauseTcs.Task.WaitAsync(ct); }
                catch (OperationCanceledException)
                {
                    FlowCancelled?.Invoke(flow);
                    return;
                }
            }

            CurrentStepIndex = i;
            var step = flow.Steps[i];

            // Check if step is already in the desired state — skip gracefully
            if (step.SkipCondition != null && _state.IsAvailable && step.SkipCondition(_state))
            {
                _announcer.Announce($"Already set: {step.AnnounceText}");
                StepCompleted?.Invoke(flow, step, i);
                if (!string.IsNullOrEmpty(step.CompletesChecklistItemId))
                    _checklist.MarkComplete(step.CompletesChecklistItemId);
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
                if (!string.IsNullOrEmpty(step.CompletesChecklistItemId))
                    _checklist.MarkComplete(step.CompletesChecklistItemId);

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

        FlowCompleted?.Invoke(flow);
        _announcer.AnnounceImmediate($"{flow.Name} flow complete");
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
