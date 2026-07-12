using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.FBWA320;

/// <summary>
/// FlyByWire A320 First Officer profile — wires the shared generic FO engine + window to
/// the FBW A320 concretes. Unlike the Fenix/PMDG profiles, the executor also needs the live
/// <see cref="FlyByWireA320Definition"/> instance (its writes delegate to the def's verified
/// <c>ApplyUIVariable</c> panel path), so it is passed into this profile's constructor by the
/// caller (<c>MainForm.ShowFbwA320FirstOfficerDialog</c>) rather than resolved here — the
/// <see cref="IFoProfile{TExec,TState}"/> contract only ever hands us a
/// <see cref="SimConnectManager"/>.
/// </summary>
public sealed class FbwA320FoProfile : IFoProfile<FbwA320ActionExecutor, FbwA320StateEvaluator>
{
    private readonly FlyByWireA320Definition _def;
    private readonly ScreenReaderAnnouncer _announcer;

    public FbwA320FoProfile(FlyByWireA320Definition def, ScreenReaderAnnouncer announcer)
    {
        _def = def;
        _announcer = announcer;
    }

    public string Title => "First Officer — FlyByWire A32NX";

    public FbwA320ActionExecutor CreateExecutor() => new();
    public FbwA320StateEvaluator CreateEvaluator() => new();

    public void BindDataManager(FbwA320StateEvaluator state, SimConnectManager sc)
        => state.SetSimConnect(sc);

    public void SetExecutorSimConnect(FbwA320ActionExecutor exec, SimConnectManager? sc)
    {
        exec.SetDefinition(_def);
        exec.SetAnnouncer(_announcer);
        exec.SetSimConnect(sc);
    }

    public List<FlowDefinition<FbwA320StateEvaluator>> BuildFlows()
        => FbwA320FlowDefinitions.Build();

    public List<ChecklistGroup<FbwA320ActionExecutor, FbwA320StateEvaluator>> BuildChecklists()
        => FbwA320ChecklistDefinitions.Build();

    public IFoAutoManager CreateAutoManager(
        FbwA320ActionExecutor exec, FbwA320StateEvaluator state,
        ScreenReaderAnnouncer a, UserSettings s)
        => new FbwA320FOAutoManager(exec, state, a)
        {
            AutoGearUpEnabled   = s.FOAutoGearUpEnabled,
            AutoGearDownEnabled = s.FOAutoGearDownEnabled,
            AutoFlapsEnabled    = s.FOAutoFlapsEnabled,   // speed-scheduled extension/retraction (takeoff setting stays a Captain item)
            AutoApEnabled       = s.FOAutoApEnabled,
            AutoApEngageAltitudeAgl = s.FOAutoApEngageAltitudeAgl,
        };

    public IFoPhaseMonitor CreatePhaseMonitor(
        FbwA320ActionExecutor exec, FbwA320StateEvaluator state, ScreenReaderAnnouncer a)
        => new FbwA320FlightPhaseMonitor(exec, a);
}
