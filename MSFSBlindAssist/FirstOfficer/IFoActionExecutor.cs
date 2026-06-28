namespace MSFSBlindAssist.FirstOfficer;

public interface IFoActionExecutor
{
    bool IsAvailable { get; }
    Task<bool> ExecuteStepAsync(IFlowStepDispatch step);
}
