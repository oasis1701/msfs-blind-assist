using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.Fenix;

/// <summary>
/// Fenix A320 First Officer profile — wires the shared generic FO engine + window to the
/// Fenix concretes. Unlike the PMDG profiles there is no aircraft data manager: all state
/// is plain L:vars, so BindDataManager just hands the evaluator the SimConnectManager.
/// </summary>
public sealed class FenixFoProfile : IFoProfile<FenixActionExecutor, FenixStateEvaluator>
{
    public string Title => "First Officer — Fenix A320";

    public FenixActionExecutor CreateExecutor() => new();

    public FenixStateEvaluator CreateEvaluator() => new();

    public void BindDataManager(FenixStateEvaluator state, SimConnectManager sc)
        => state.SetSimConnect(sc);

    public void SetExecutorSimConnect(FenixActionExecutor exec, SimConnectManager? sc)
        => exec.SetSimConnect(sc);

    public List<FlowDefinition<FenixStateEvaluator>> BuildFlows()
        => FenixFlowDefinitions.Build();

    public List<ChecklistGroup<FenixActionExecutor, FenixStateEvaluator>> BuildChecklists()
        => FenixChecklistDefinitions.Build();

    public IFoAutoManager CreateAutoManager(
        FenixActionExecutor exec, FenixStateEvaluator state,
        ScreenReaderAnnouncer a, UserSettings s)
        => new FenixFOAutoManager(exec, state, a)
        {
            AutoGearUpEnabled   = s.FOAutoGearUpEnabled,
            AutoGearDownEnabled = s.FOAutoGearDownEnabled,
            AutoFlapsEnabled    = s.FOAutoFlapsEnabled,   // stored; Fenix manager never acts on it
            AutoApEnabled       = s.FOAutoApEnabled,
        };

    public IFoPhaseMonitor CreatePhaseMonitor(
        FenixActionExecutor exec, FenixStateEvaluator state, ScreenReaderAnnouncer a)
        => new FenixFlightPhaseMonitor(exec, a);
}
