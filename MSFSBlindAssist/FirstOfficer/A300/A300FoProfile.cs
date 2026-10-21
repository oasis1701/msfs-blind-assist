using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.A300;

/// <summary>
/// iniBuilds A300 First Officer profile. Writes go through the live definition's panel rows (A300DefinitionFoPort,
/// [A300-1]), so the definition and the app's one announcer come from its row in
/// <c>Forms.FirstOfficer.FirstOfficerWindows</c> ([FO-24]), as the A380 profile takes them. The evaluator is created
/// once and shared with the executor.
/// </summary>
public sealed class A300FoProfile : IFoProfile<A300FoActionExecutor, A300FoStateEvaluator>
{
    private readonly IniA300Definition _def;
    private readonly ScreenReaderAnnouncer _announcer;
    private A300FoStateEvaluator? _state;

    public A300FoProfile(IniA300Definition def, ScreenReaderAnnouncer announcer)
    {
        _def = def;
        _announcer = announcer;
    }

    public string Title => "First Officer — iniBuilds A300";

    public A300FoStateEvaluator CreateEvaluator() => _state ??= new A300FoStateEvaluator();

    public A300FoActionExecutor CreateExecutor()
    {
        var exec = new A300FoActionExecutor();
        exec.SetState(CreateEvaluator());
        exec.SetAnnouncer(_announcer);
        return exec;
    }

    public void BindDataManager(A300FoStateEvaluator state, SimConnectManager sc) => state.SetSimConnect(sc);

    public void SetExecutorSimConnect(A300FoActionExecutor exec, SimConnectManager? sc)
    {
        if (sc == null)
        {
            exec.SetPort(null);
            return;
        }
        _def.Attach(sc);
        exec.SetPort(new A300DefinitionFoPort(_def, sc, _announcer));
    }

    public List<FlowDefinition<A300FoStateEvaluator>> BuildFlows() => A300FoFlowDefinitions.Build();

    public List<ChecklistGroup<A300FoActionExecutor, A300FoStateEvaluator>> BuildChecklists()
        => A300FoChecklistDefinitions.Build();

    public IFoAutoManager CreateAutoManager(A300FoActionExecutor exec, A300FoStateEvaluator state,
        ScreenReaderAnnouncer a, UserSettings s)
        => new A300FoAutoManager { AutoFlapsEnabled = s.FOAutoFlapsEnabled };   // stored, never acted on

    public IFoPhaseMonitor CreatePhaseMonitor(A300FoActionExecutor exec, A300FoStateEvaluator state,
        ScreenReaderAnnouncer a)
        => new A300FoFlightPhaseMonitor(exec, a);
}
