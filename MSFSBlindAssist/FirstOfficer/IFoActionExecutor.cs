namespace MSFSBlindAssist.FirstOfficer;

public interface IFoActionExecutor
{
    bool IsAvailable { get; }
    Task<bool> ExecuteStepAsync(IFlowStepDispatch step);

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
