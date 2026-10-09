namespace MSFSBlindAssist.FirstOfficer;

public interface IFlowStepDispatch
{
    Models.FlowStepActionType ActionType { get; }
    string? EventName { get; }
    int? TargetValue { get; }
    IReadOnlyList<(string EventName, int? TargetValue)> MultiActions { get; }
    bool UsesMouseFlag { get; }
    bool IsMomentary { get; }
}
