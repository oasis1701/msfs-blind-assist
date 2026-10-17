using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.MD11;

/// <summary>
/// TFDi MD-11 First Officer profile. The executor owns its own switch logic (TFDi's decoded
/// handler semantics, <see cref="Md11FoControls"/>) and sends through the live definition's ONE
/// CEVENT bus, so the definition is passed in by the caller (<c>MainForm.ShowTfdiMd11FirstOfficerDialog</c>),
/// exactly as the A330 and iFly profiles take theirs.
///
/// The evaluator is created ONCE and is also the executor's flight state (engine N2, on-ground):
/// the window calls CreateEvaluator before CreateExecutor, and two evaluators would leave the
/// start-switch, gear and flap safety rules reading one that nothing feeds.
/// </summary>
public sealed class Md11FoProfile : IFoProfile<Md11FoActionExecutor, Md11FoStateEvaluator>
{
    private readonly TFDiMD11Definition _def;
    private Md11FoStateEvaluator? _state;

    public Md11FoProfile(TFDiMD11Definition def) => _def = def;

    public string Title => "First Officer — TFDi MD-11";

    public Md11FoStateEvaluator CreateEvaluator() => _state ??= new Md11FoStateEvaluator();

    public Md11FoActionExecutor CreateExecutor()
    {
        var exec = new Md11FoActionExecutor();
        exec.SetFlightState(CreateEvaluator());
        return exec;
    }

    public void BindDataManager(Md11FoStateEvaluator state, SimConnectManager sc) => state.SetSimConnect(sc);

    public void SetExecutorSimConnect(Md11FoActionExecutor exec, SimConnectManager? sc)
    {
        if (sc == null)
        {
            exec.SetTransport(null);
            return;
        }
        _def.Attach(sc);                                    // idempotent: the bus is created once
        exec.SetTransport(new Md11DefinitionFoTransport(_def, sc));
    }

    public List<FlowDefinition<Md11FoStateEvaluator>> BuildFlows() => Md11FoFlowDefinitions.Build();

    public List<ChecklistGroup<Md11FoActionExecutor, Md11FoStateEvaluator>> BuildChecklists()
        => Md11FoChecklistDefinitions.Build();

    public IFoAutoManager CreateAutoManager(Md11FoActionExecutor exec, Md11FoStateEvaluator state,
        ScreenReaderAnnouncer a, UserSettings s)
        => new Md11FoAutoManager { AutoFlapsEnabled = s.FOAutoFlapsEnabled };   // stored, never acted on

    public IFoPhaseMonitor CreatePhaseMonitor(Md11FoActionExecutor exec, Md11FoStateEvaluator state,
        ScreenReaderAnnouncer a)
        => new Md11FoFlightPhaseMonitor(exec, a);
}
