using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.IFly737;

/// <summary>
/// iFly 737 MAX8 First Officer profile — wires the shared generic FO engine + window to the
/// iFly concretes. Like the FBW A380 (<see cref="FBWA380.FbwA380FoProfile"/>, this profile's
/// template) the executor needs the live <see cref="IFly737MAXDefinition"/> instance (writes
/// delegate to the def's verified <c>ApplyUIVariable</c> panel path), so it is passed into
/// this profile's constructor by the caller rather than resolved here — the
/// <see cref="IFoProfile{TExec,TState}"/> contract only ever hands us a
/// <see cref="SimConnectManager"/>.
///
/// UNLIKE the A380, this executor also needs a live handle to the evaluator (the centre-fuel-
/// pump ON gate in <c>IFly737ActionExecutor.DispatchCoreAsync</c> reads the evaluator's
/// <c>CenterQtyLbs()</c>, and the <c>PRESS_ALTS</c> pseudo-key reads its stored SimBrief plan).
/// <see cref="IFly737ActionExecutor.SetStateEvaluator"/> has no caller anywhere else in the
/// codebase — if this profile did not call it, both reads would see nothing (the gate's
/// fail-closed design would then suppress EVERY centre-pump ON write, and the pressurization
/// flow step would quietly no-op) with NO error and NO announcement, so this profile calls it
/// from <see cref="SetExecutorSimConnect"/> using the exact evaluator instance stashed by
/// <see cref="CreateEvaluator"/> — the SAME instance
/// <see cref="Forms.FirstOfficer.FirstOfficerForm{TExec,TState}"/> hands to the checklists,
/// flows, phase monitor and auto manager (its constructor calls
/// <c>profile.CreateEvaluator()</c> once, then <c>profile.CreateExecutor()</c>, then later
/// <c>profile.SetExecutorSimConnect(_actionExec, …)</c> — verified by reading
/// FirstOfficerForm.cs's constructor and <c>UpdateServicesFromConnection</c>), never a second,
/// independently-constructed evaluator.
/// </summary>
public sealed class IFly737FoProfile : IFoProfile<IFly737ActionExecutor, IFly737StateEvaluator>
{
    private readonly IFly737MAXDefinition _def;
    private readonly ScreenReaderAnnouncer _announcer;

    // Stashed by CreateEvaluator so SetExecutorSimConnect (called later, on both initial wiring
    // and every reconnect) can hand the executor the SAME instance the rest of the FO engine
    // evaluates against — see the class doc above.
    private IFly737StateEvaluator? _state;

    public IFly737FoProfile(IFly737MAXDefinition def, ScreenReaderAnnouncer announcer)
    {
        _def = def;
        _announcer = announcer;
    }

    public string Title => "First Officer — iFly 737 MAX8";

    public IFly737ActionExecutor CreateExecutor() => new();

    public IFly737StateEvaluator CreateEvaluator()
    {
        var state = new IFly737StateEvaluator();
        state.SetDefinition(_def);
        _state = state;
        return state;
    }

    // No PMDG-style data manager on this airframe — state rides the definition's own SDK
    // client (wired via CreateEvaluator's SetDefinition call above), not a PMDG data manager.
    public void BindDataManager(IFly737StateEvaluator state, SimConnectManager sc) { }

    public void SetExecutorSimConnect(IFly737ActionExecutor exec, SimConnectManager? sc)
    {
        exec.SetDefinition(_def);
        exec.SetAnnouncer(_announcer);
        exec.SetSimConnect(sc);
        // The centre-pump gate + PRESS_ALTS pseudo-key read side — see class doc. _state is
        // always non-null here in practice (FirstOfficerForm's constructor calls
        // CreateEvaluator() before SetExecutorSimConnect can ever be reached), but the setter
        // itself is null-tolerant so a hypothetical out-of-order caller degrades to the
        // documented fail-closed/no-op behaviour rather than throwing.
        exec.SetStateEvaluator(_state);
    }

    public List<FlowDefinition<IFly737StateEvaluator>> BuildFlows()
        => IFly737FlowDefinitions.Build();

    public List<ChecklistGroup<IFly737ActionExecutor, IFly737StateEvaluator>> BuildChecklists()
        => IFly737ChecklistDefinitions.Build();

    public IFoAutoManager CreateAutoManager(
        IFly737ActionExecutor exec, IFly737StateEvaluator state,
        ScreenReaderAnnouncer a, UserSettings s)
        => new IFly737FOAutoManager(exec, state, a)
        {
            AutoFlapsEnabled = s.FOAutoFlapsEnabled,   // stored, never acted on — no 737 auto-flap schedule (deliberate)
        };

    public IFoPhaseMonitor CreatePhaseMonitor(
        IFly737ActionExecutor exec, IFly737StateEvaluator state, ScreenReaderAnnouncer a)
        => new IFly737FlightPhaseMonitor(exec, state, a);
}
