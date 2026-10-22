using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.Fenix;

/// <summary>
/// Fenix A320 First Officer profile — wires the shared generic FO engine + window to the
/// Fenix concretes. Unlike the PMDG profiles there is no aircraft data manager: all state
/// is plain L:vars, so BindDataManager hands the evaluator the SimConnectManager (and, for the few
/// values only the Fenix's own data service has, the Fenix definition).
/// </summary>
public sealed class FenixFoProfile : IFoProfile<FenixActionExecutor, FenixStateEvaluator>
{
    // The app announcer, handed to the executor for the TO CONFIG result readout
    // (the FbwA380FoProfile precedent — all other narration stays FlowManager's).
    private readonly ScreenReaderAnnouncer _announcer;

    public FenixFoProfile(ScreenReaderAnnouncer announcer) => _announcer = announcer;

    public string Title => "First Officer — Fenix A320";

    public FenixActionExecutor CreateExecutor() => new();

    public FenixStateEvaluator CreateEvaluator() => new();

    // The evaluator also reads the Fenix's own data service, through the Fenix definition SimConnectManager
    // holds as the current aircraft (MainForm sets it on load and on every switch). The FO window calls
    // this at construction and on every reconnect. The subscription ends when the definition disposes
    // its data service on a switch, but the evaluator's once-a-second N2 poll does not end that way: it
    // stops only when the evaluator is detached (SetExecutorSimConnect below).
    private FenixStateEvaluator? _boundState;

    public void BindDataManager(FenixStateEvaluator state, SimConnectManager sc)
    {
        _boundState = state;
        state.SetSimConnect(sc);
        state.AttachFenix(sc.CurrentAircraft as FenixA320Definition);
    }

    public void SetExecutorSimConnect(FenixActionExecutor exec, SimConnectManager? sc)
    {
        exec.SetAnnouncer(_announcer);
        exec.SetSimConnect(sc);
        // The FO window detaches the executor with null on a disconnect and when it closes (an aircraft
        // switch closes it), and has no other teardown that reaches the evaluator. Without this the
        // evaluator would go on polling the Fenix's gateway after both. A reconnect binds again, which
        // attaches again, before it hands the executor the connection, so nothing is lost.
        if (sc == null)
            _boundState?.AttachFenix(null);
    }

    public List<FlowDefinition<FenixStateEvaluator>> BuildFlows()
        => FenixFlowDefinitions.Build();

    public List<ChecklistGroup<FenixActionExecutor, FenixStateEvaluator>> BuildChecklists()
        => FenixChecklistDefinitions.Build();

    public IFoAutoManager CreateAutoManager(
        FenixActionExecutor exec, FenixStateEvaluator state,
        ScreenReaderAnnouncer a, UserSettings s)
        => new FenixFOAutoManager(exec, state, a)
        {
            AutoFlapsEnabled = s.FOAutoFlapsEnabled,   // stored; Fenix manager never acts on it
        };

    public IFoPhaseMonitor CreatePhaseMonitor(
        FenixActionExecutor exec, FenixStateEvaluator state, ScreenReaderAnnouncer a)
        => new FenixFlightPhaseMonitor(exec, a);
}
