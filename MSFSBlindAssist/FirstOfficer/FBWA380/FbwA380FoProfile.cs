using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.FBWA380;

/// <summary>
/// FlyByWire A380 First Officer profile — wires the shared generic FO engine + window to
/// the FBW A380 concretes. Unlike the Fenix/PMDG profiles, the executor also needs the live
/// <see cref="FlyByWireA380Definition"/> instance (its writes delegate to the def's verified
/// <c>ApplyUIVariable</c> panel path), so it is passed into this profile's constructor by the
/// caller (<c>MainForm.ShowFbwA380FirstOfficerDialog</c>) rather than resolved here — the
/// <see cref="IFoProfile{TExec,TState}"/> contract only ever hands us a
/// <see cref="SimConnectManager"/>.
/// </summary>
public sealed class FbwA380FoProfile : IFoProfile<FbwA380ActionExecutor, FbwA380StateEvaluator>
{
    private readonly FlyByWireA380Definition _def;
    private readonly ScreenReaderAnnouncer _announcer;

    public FbwA380FoProfile(FlyByWireA380Definition def, ScreenReaderAnnouncer announcer)
    {
        _def = def;
        _announcer = announcer;
    }

    public string Title => "First Officer — FlyByWire A380";

    public FbwA380ActionExecutor CreateExecutor() => new();
    public FbwA380StateEvaluator CreateEvaluator() => new();

    public void BindDataManager(FbwA380StateEvaluator state, SimConnectManager sc)
        => state.SetSimConnect(sc);

    public void SetExecutorSimConnect(FbwA380ActionExecutor exec, SimConnectManager? sc)
    {
        exec.SetDefinition(_def);
        exec.SetAnnouncer(_announcer);
        exec.SetSimConnect(sc);
    }

    public List<FlowDefinition<FbwA380StateEvaluator>> BuildFlows()
        => FbwA380FlowDefinitions.Build();

    public List<ChecklistGroup<FbwA380ActionExecutor, FbwA380StateEvaluator>> BuildChecklists()
        => FbwA380ChecklistDefinitions.Build();

    public IFoAutoManager CreateAutoManager(
        FbwA380ActionExecutor exec, FbwA380StateEvaluator state,
        ScreenReaderAnnouncer a, UserSettings s)
        => new FbwA380FOAutoManager(exec, state, a)
        {
            AutoGearUpEnabled   = s.FOAutoGearUpEnabled,
            AutoGearDownEnabled = s.FOAutoGearDownEnabled,
            AutoFlapsEnabled    = s.FOAutoFlapsEnabled,   // speed-scheduled extension/retraction (takeoff setting stays a Captain item)
            AutoApEnabled       = s.FOAutoApEnabled,
            AutoApEngageAltitudeAgl = s.FOAutoApEngageAltitudeAgl,
        };

    public IFoPhaseMonitor CreatePhaseMonitor(
        FbwA380ActionExecutor exec, FbwA380StateEvaluator state, ScreenReaderAnnouncer a)
        => new FbwA380FlightPhaseMonitor(exec, a);
}
