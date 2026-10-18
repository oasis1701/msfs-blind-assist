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
    /// The flow's Pause, handed over once by its FlowManager: completes at once unless a Pause is in
    /// force, then when it is resumed (throws on the flow's Stop). An executor whose step WAITS (the
    /// iFly's engine-start waits) holds its wait on it and does not count the time paused; an atomic
    /// step ignores it. Default: ignored.
    /// </summary>
    void AttachFlowPause(Func<CancellationToken, Task> waitWhilePaused) { }

    /// <summary>
    /// A flow was stopped part-way (Stop Flow, another flow started, the window closed, or a step
    /// whose failure stops the flow); the run's Stop token is cancelled by then. An executor
    /// whose flow can stop between two steps it must never be left between (the MD-11: a START
    /// switch pulled, its fuel switch still OFF) puts that back here. Default: nothing.
    /// </summary>
    Task OnFlowStoppedAsync() => Task.CompletedTask;

    /// <summary>
    /// The First Officer window is closing (an aircraft switch, app exit): release every press the
    /// executor still holds now, while the sim still listens, instead of on the hold's own timer
    /// (<see cref="HeldPresses"/>). Called on the UI thread before the executor is detached; on app
    /// exit MainForm disconnects right after. Default: nothing (no holds, or released elsewhere:
    /// the PMDG 737 and iFly definitions, the MD-11 bus).
    /// </summary>
    void ReleaseHeldPresses() { }

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
