using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.HWA330;

/// <summary>
/// FlyByWire A320 First Officer profile — wires the shared generic FO engine + window to
/// the FBW A320 concretes. Unlike the Fenix/PMDG profiles, the executor also needs the live
/// <see cref="FlyByWireA320Definition"/> instance (its writes delegate to the def's verified
/// <c>ApplyUIVariable</c> panel path), so it is passed into this profile's constructor by the
/// caller (<c>MainForm.ShowHwA330FirstOfficerDialog</c>) rather than resolved here — the
/// <see cref="IFoProfile{TExec,TState}"/> contract only ever hands us a
/// <see cref="SimConnectManager"/>.
/// </summary>
public sealed class HwA330FoProfile : IFoProfile<HwA330ActionExecutor, HwA330StateEvaluator>
{
    private readonly HeadwindA330Definition _def;
    private readonly ScreenReaderAnnouncer _announcer;

    public HwA330FoProfile(HeadwindA330Definition def, ScreenReaderAnnouncer announcer)
    {
        _def = def;
        _announcer = announcer;
    }

    public string Title => "First Officer — HeadwindSim A330-900neo";

    public HwA330ActionExecutor CreateExecutor() => new();
    public HwA330StateEvaluator CreateEvaluator() => new();

    public void BindDataManager(HwA330StateEvaluator state, SimConnectManager sc)
        => state.SetSimConnect(sc);

    public void SetExecutorSimConnect(HwA330ActionExecutor exec, SimConnectManager? sc)
    {
        exec.SetDefinition(_def);
        exec.SetAnnouncer(_announcer);
        exec.SetSimConnect(sc);
    }

    public List<FlowDefinition<HwA330StateEvaluator>> BuildFlows()
        => HwA330FlowDefinitions.Build();

    public List<ChecklistGroup<HwA330ActionExecutor, HwA330StateEvaluator>> BuildChecklists()
        => HwA330ChecklistDefinitions.Build();

    public IFoAutoManager CreateAutoManager(
        HwA330ActionExecutor exec, HwA330StateEvaluator state,
        ScreenReaderAnnouncer a, UserSettings s)
        => new HwA330FOAutoManager(exec, state, a)
        {
            AutoFlapsEnabled = s.FOAutoFlapsEnabled,   // speed-scheduled extension/retraction (takeoff setting stays a Captain item)
        };

    public IFoPhaseMonitor CreatePhaseMonitor(
        HwA330ActionExecutor exec, HwA330StateEvaluator state, ScreenReaderAnnouncer a)
        => new HwA330FlightPhaseMonitor(exec, a);
}
