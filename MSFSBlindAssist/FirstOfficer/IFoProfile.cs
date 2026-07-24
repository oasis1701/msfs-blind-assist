namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Per-aircraft First Officer profile. Supplies the concrete executor/evaluator,
/// the flow and checklist data, the auto/phase managers, and the SimConnect wiring
/// so the shared generic <see cref="Forms.FirstOfficer.FirstOfficerForm{TExec,TState}"/>
/// can run any aircraft's FO without aircraft-specific code in the form.
/// </summary>
public interface IFoProfile<TExec, TState>
    where TExec : IFoActionExecutor
    where TState : IFoStateEvaluator
{
    /// <summary>Window title, e.g. "First Officer — PMDG 777".</summary>
    string Title { get; }

    /// <summary>Construct the aircraft's action executor.</summary>
    TExec  CreateExecutor();

    /// <summary>Construct the aircraft's state evaluator.</summary>
    TState CreateEvaluator();

    /// <summary>Bind the evaluator to the aircraft's data manager from the live SimConnect.</summary>
    void   BindDataManager(TState state, SimConnect.SimConnectManager sc);

    /// <summary>Point the executor at the live SimConnect (or null when disconnected).</summary>
    void   SetExecutorSimConnect(TExec exec, SimConnect.SimConnectManager? sc);

    /// <summary>Build the aircraft's phase-of-flight automation flows.</summary>
    List<Models.FlowDefinition<TState>>             BuildFlows();

    /// <summary>Build the aircraft's checklist groups.</summary>
    List<Models.ChecklistGroup<TExec, TState>>      BuildChecklists();

    /// <summary>Construct the aircraft's automatic gear/flap/AP manager.</summary>
    IFoAutoManager  CreateAutoManager(TExec exec, TState state, Accessibility.ScreenReaderAnnouncer a, Settings.UserSettings s);

    /// <summary>Construct the aircraft's altitude-crossing phase monitor.</summary>
    IFoPhaseMonitor CreatePhaseMonitor(TExec exec, TState state, Accessibility.ScreenReaderAnnouncer a);
}
