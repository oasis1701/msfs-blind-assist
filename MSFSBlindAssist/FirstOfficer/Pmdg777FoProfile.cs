using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// PMDG 777 First Officer profile — wires the shared generic FO engine + window
/// to the 777 concretes (executor, evaluator, data manager) and data
/// (flow + checklist definitions, auto/phase managers).
/// </summary>
public sealed class Pmdg777FoProfile : IFoProfile<AircraftActionExecutor, AircraftStateEvaluator>
{
    public string Title => "First Officer — PMDG 777";

    public AircraftActionExecutor CreateExecutor() => new();

    public AircraftStateEvaluator CreateEvaluator() => new();

    public void BindDataManager(AircraftStateEvaluator state, SimConnectManager sc)
        => state.SetDataManager(sc.PMDGDataManager as PMDG777DataManager);

    public void SetExecutorSimConnect(AircraftActionExecutor exec, SimConnectManager? sc)
        => exec.SetSimConnect(sc);

    public List<FlowDefinition<AircraftStateEvaluator>> BuildFlows()
        => PMDG777FlowDefinitions.Build();

    public List<ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator>> BuildChecklists()
        => PMDG777ChecklistDefinitions.Build();

    public IFoAutoManager CreateAutoManager(
        AircraftActionExecutor exec, AircraftStateEvaluator state,
        ScreenReaderAnnouncer a, UserSettings s)
        => new FOAutoManager(exec, state, a)
        {
            AutoGearUpEnabled   = s.FOAutoGearUpEnabled,
            AutoGearDownEnabled = s.FOAutoGearDownEnabled,
            AutoFlapsEnabled    = s.FOAutoFlapsEnabled,
            AutoApEnabled       = s.FOAutoApEnabled,
        };

    public IFoPhaseMonitor CreatePhaseMonitor(
        AircraftActionExecutor exec, AircraftStateEvaluator state, ScreenReaderAnnouncer a)
        => new FlightPhaseMonitor(exec, state, a);
}
