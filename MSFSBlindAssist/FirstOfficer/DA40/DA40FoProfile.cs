using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.DA40;

/// <summary>
/// The COWS DA40 First Officer profile, one class for both airframes (the definition says which).
/// The executor writes through the live <see cref="CowsDA40Definition"/>, so the definition is passed
/// in by <c>Forms.FirstOfficer.FirstOfficerWindows.Create</c>, as the FlyByWire and MD-11 profiles
/// take theirs.
///
/// The evaluator is created ONCE and is also the executor's reading of the aircraft (the crank's RPM,
/// the ECU test's stage): the window calls CreateEvaluator before CreateExecutor, and two evaluators
/// would leave the executor reading one nothing feeds. The read-back pages are read from the installed
/// package once per window (<see cref="NativeChecklistReader.LoadPages"/>, keyed by the SimObject
/// folder, which is the aircraft code).
/// </summary>
public sealed class DA40FoProfile : IFoProfile<DA40FoActionExecutor, DA40FoStateEvaluator>
{
    private readonly CowsDA40Definition _def;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Func<string, IReadOnlyList<NativeChecklistPage>?> _loadPages;
    private DA40FoStateEvaluator? _state;

    public DA40FoProfile(CowsDA40Definition def, ScreenReaderAnnouncer announcer)
        : this(def, announcer, NativeChecklistReader.LoadPages) { }

    /// <summary>Test seam: where the pages come from.</summary>
    internal DA40FoProfile(CowsDA40Definition def, ScreenReaderAnnouncer announcer,
        Func<string, IReadOnlyList<NativeChecklistPage>?> loadPages)
    {
        _def = def;
        _announcer = announcer;
        _loadPages = loadPages;
    }

    public string Title => _def.Variant == DA40Variant.NG
        ? "First Officer — COWS Diamond DA40-NG"
        : "First Officer — COWS Diamond DA40-XLS";

    public DA40FoStateEvaluator CreateEvaluator()
    {
        if (_state != null) return _state;
        _state = new DA40FoStateEvaluator { Variant = _def.Variant };
        _state.SetBreakerKeys(_def.BreakerKeys);
        return _state;
    }

    public DA40FoActionExecutor CreateExecutor()
    {
        var exec = new DA40FoActionExecutor();
        exec.SetState(CreateEvaluator());
        return exec;
    }

    public void BindDataManager(DA40FoStateEvaluator state, SimConnectManager sc) => state.SetSimConnect(sc);

    public void SetExecutorSimConnect(DA40FoActionExecutor exec, SimConnectManager? sc)
    {
        exec.SetDefinition(_def);
        exec.SetAnnouncer(_announcer);
        exec.SetSimConnect(sc);
    }

    public List<FlowDefinition<DA40FoStateEvaluator>> BuildFlows() => DA40FoFlowDefinitions.Build(_def.Variant);

    public List<ChecklistGroup<DA40FoActionExecutor, DA40FoStateEvaluator>> BuildChecklists()
        => DA40FoChecklistDefinitions.Build(_def.Variant, _loadPages(_def.AircraftCode));

    public IFoAutoManager CreateAutoManager(DA40FoActionExecutor exec, DA40FoStateEvaluator state,
        ScreenReaderAnnouncer a, UserSettings s)
        => new DA40FoAutoManager { AutoFlapsEnabled = s.FOAutoFlapsEnabled };   // stored, never acted on

    public IFoPhaseMonitor CreatePhaseMonitor(DA40FoActionExecutor exec, DA40FoStateEvaluator state,
        ScreenReaderAnnouncer a)
        => new DA40FoFlightPhaseMonitor(exec, a);
}
