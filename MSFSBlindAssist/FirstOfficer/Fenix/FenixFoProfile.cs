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

    // The evaluator the window last bound, kept so SetExecutorSimConnect below can detach it.
    private FenixStateEvaluator? _boundState;

    // The evaluator also reads the Fenix's own data service, through the Fenix definition SimConnectManager
    // holds as the current aircraft (MainForm sets it on load and on every switch). The FO window calls
    // this at construction, on every show (Shift+C or the menu) and when MainForm raises
    // OnSimConnectChanged (on a connect). The subscription ends when the definition disposes its data
    // service on a switch, but the evaluator's once-a-second N2 poll does not end that way: it stops
    // only when the evaluator is detached (SetExecutorSimConnect below).
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
        // The window passes null from a bind made while the sim is disconnected (its construction, or a
        // Shift+C or menu show before the connect) and from its Dispose (an aircraft switch, or app exit).
        // Closing the window only hides it, and MainForm raises OnSimConnectChanged only on a connect
        // (MainForm.AircraftSwitch.cs), so closing the window, or a disconnect, detaches nothing by itself:
        // the poll runs on until the next bind made while disconnected or the Dispose (read from the code,
        // not run in the sim). The window has no other teardown that reaches the evaluator, so without
        // this it would go on polling the Fenix's gateway after the Dispose. A later connected bind
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
