namespace MSFSBlindAssist.FirstOfficer;

public interface IFoActionExecutor
{
    bool IsAvailable { get; }
    Task<bool> ExecuteStepAsync(IFlowStepDispatch step);

    /// <summary>
    /// The flow's own entry point: <paramref name="stop"/> is the running flow's Stop. A step is
    /// atomic by default and ignores it (a composite such as the weather radar test always
    /// restores the radar); an executor overrides this only for a step that can end early in a
    /// safe state (the MD-11's light-up watch pushes START back in; the iFly's engine-start
    /// waits only read).
    /// </summary>
    Task<bool> ExecuteStepAsync(IFlowStepDispatch step, CancellationToken stop) => ExecuteStepAsync(step);

    /// <summary>
    /// A flow was stopped part-way (Stop Flow, another flow started, the window closed, or a step
    /// whose failure stops the flow); the run's Stop token is cancelled by then. An executor
    /// whose flow can stop between two steps it must never be left between (the MD-11: a START
    /// switch pulled, its fuel switch still OFF) puts that back here. Default: nothing.
    /// </summary>
    Task OnFlowStoppedAsync() => Task.CompletedTask;

    /// <summary>
    /// Completes once every dispatch queued on the executor's serialize gate at call
    /// time has finished — i.e. the switch writes behind a checklist tick (including a
    /// multi-second closed-loop selector walk, or writes queued behind one) have
    /// actually reached the sim. ChecklistManager uses this to hold the manual-tick
    /// revert grace open while a tick's action is still in flight, then re-stamps the
    /// grace so the readback cadence gets a full window after the action lands.
    /// Executors without a serialize gate return a completed task.
    /// </summary>
    Task WaitForDispatchDrainAsync();
}
