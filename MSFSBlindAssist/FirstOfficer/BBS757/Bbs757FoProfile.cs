using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.BBS757;

/// <summary>
/// BlueBird 757 First Officer profile (plan 10). The evaluator is created ONCE and is also the
/// executor's state, as on the MD-11: the window calls CreateEvaluator before CreateExecutor.
/// Writes go through the live definition's silent write path (<see cref="Bbs757DefinitionFoTransport"/>).
/// </summary>
public sealed class Bbs757FoProfile : IFoProfile<Bbs757FoActionExecutor, Bbs757FoStateEvaluator>
{
    private readonly BBS757Definition _def;
    private readonly ScreenReaderAnnouncer? _announcer;
    private Bbs757FoStateEvaluator? _state;

    /// <param name="announcer">What the executor speaks itself (an aborted engine start, said at once); null says nothing.</param>
    public Bbs757FoProfile(BBS757Definition def, ScreenReaderAnnouncer? announcer = null)
    {
        _def = def;
        _announcer = announcer;
    }

    public string Title => "First Officer — BlueBird 757";

    public Bbs757FoStateEvaluator CreateEvaluator() => _state ??= new Bbs757FoStateEvaluator();

    public Bbs757FoActionExecutor CreateExecutor()
    {
        var exec = new Bbs757FoActionExecutor();
        exec.SetState(CreateEvaluator());
        return exec;
    }

    public void BindDataManager(Bbs757FoStateEvaluator state, SimConnectManager sc) => state.SetSimConnect(sc);

    public void SetExecutorSimConnect(Bbs757FoActionExecutor exec, SimConnectManager? sc)
    {
        exec.SetTransport(sc == null ? null : new Bbs757DefinitionFoTransport(_def, sc));
        // Interrupting: an aborted start is safety speech (the definition's awaits keep the UI context, so this runs there).
        exec.SetSpeech(_announcer == null ? null : _announcer.AnnounceImmediate);
    }

    public List<FlowDefinition<Bbs757FoStateEvaluator>> BuildFlows() => Bbs757FoFlowDefinitions.Build();

    public List<ChecklistGroup<Bbs757FoActionExecutor, Bbs757FoStateEvaluator>> BuildChecklists()
        => Bbs757FoChecklistDefinitions.Build();

    public IFoAutoManager CreateAutoManager(Bbs757FoActionExecutor exec, Bbs757FoStateEvaluator state,
        ScreenReaderAnnouncer a, UserSettings s)
        // The settings read live, as the other Boeings' managers read SettingsManager.Current: a change applies at once.
        => new Bbs757FoAutoManager(exec, state, a.Announce,
            () => SettingsManager.Current.FOAutoApEnabled, () => SettingsManager.Current.FOAutoCenterPumpsEnabled)
        {
            AutoFlapsEnabled = s.FOAutoFlapsEnabled,   // stored, never acted on (the flap lever is the Captain's)
        };

    public IFoPhaseMonitor CreatePhaseMonitor(Bbs757FoActionExecutor exec, Bbs757FoStateEvaluator state,
        ScreenReaderAnnouncer a)
        => new Bbs757FoFlightPhaseMonitor(exec, a);
}
