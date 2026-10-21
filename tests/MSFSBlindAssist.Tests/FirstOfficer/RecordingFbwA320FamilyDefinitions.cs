using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A32NX twin of <see cref="RecordingA380Definition"/>: a definition that records every write the First
/// Officer's executor asks of it instead of sending it, and reports success, so a write "lands" with no sim.
/// Every plain-key write reaches <c>HandleUIVariableSet</c> through <c>ApplyUIVariable</c>, and it is
/// virtual. Each write also records the manager it was sent through, so a test can tell a held press's
/// release that went through the press's manager from one that re-read the executor's field.
/// </summary>
internal sealed class RecordingFbwA320Definition : FlyByWireA320Definition
{
    private readonly FbwFamilyWriteLog _log = new();

    /// <summary>Every write so far, in order.</summary>
    public (string Key, double Value, SimConnectManager? Sim)[] Writes => _log.Snapshot();

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _log.Add(varKey, value, simConnect);
        return true;
    }

    /// <summary>An executor whose writes land here. It reads as connected (its sim is a manager that never
    /// opened a handle), so its dispatch runs.</summary>
    public FbwA320ActionExecutor Executor()
    {
        var executor = new FbwA320ActionExecutor();
        executor.SetSimConnect(SeededSimConnectCache.ConnectedWith());
        executor.SetDefinition(this);
        executor.SetAnnouncer(new GatedSpeechCapture());
        return executor;
    }
}

/// <summary>The Headwind A330's <see cref="RecordingFbwA320Definition"/> (the A330 FO duplicates the
/// A32NX's, FOA-3).</summary>
internal sealed class RecordingHwA330Definition : HeadwindA330Definition
{
    private readonly FbwFamilyWriteLog _log = new();

    /// <summary>Every write so far, in order.</summary>
    public (string Key, double Value, SimConnectManager? Sim)[] Writes => _log.Snapshot();

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _log.Add(varKey, value, simConnect);
        return true;
    }

    /// <summary>An executor whose writes land here (see <see cref="RecordingFbwA320Definition.Executor"/>).</summary>
    public HwA330ActionExecutor Executor()
    {
        var executor = new HwA330ActionExecutor();
        executor.SetSimConnect(SeededSimConnectCache.ConnectedWith());
        executor.SetDefinition(this);
        executor.SetAnnouncer(new GatedSpeechCapture());
        return executor;
    }
}

/// <summary>The two recorders' write list: a held press's release lands on another thread.</summary>
internal sealed class FbwFamilyWriteLog
{
    private readonly object _gate = new();
    private readonly List<(string Key, double Value, SimConnectManager? Sim)> _writes = new();

    public void Add(string key, double value, SimConnectManager? sim)
    {
        lock (_gate) _writes.Add((key, value, sim));
    }

    public (string Key, double Value, SimConnectManager? Sim)[] Snapshot()
    {
        lock (_gate) return _writes.ToArray();
    }
}
