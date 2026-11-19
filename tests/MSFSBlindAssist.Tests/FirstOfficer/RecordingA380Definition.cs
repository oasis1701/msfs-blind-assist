using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// An A380 definition that records every write the First Officer's executor asks of it instead of sending
/// it, and reports success, so a write "lands" with no sim. The executor is sealed and its <c>Set</c> is
/// not virtual, so the recorder sits one step down: <c>ApplyUIVariable</c> (what every plain-key write goes
/// through) calls <c>HandleUIVariableSet</c>, which is virtual. Shared by
/// <see cref="FbwA380FuelPumpHandTickTests"/> (what a hand-tick writes) and <see cref="FbwA380FlowRun"/>
/// (a flow run whose writes land).
/// </summary>
internal sealed class RecordingA380Definition : FlyByWireA380Definition
{
    private readonly object _gate = new();
    private readonly List<(string Key, double Value)> _writes = new();

    /// <summary>Every write so far, in order.</summary>
    public (string Key, double Value)[] Writes
    {
        get { lock (_gate) return _writes.ToArray(); }
    }

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        lock (_gate) _writes.Add((varKey, value));
        return true;
    }

    private readonly List<string> _calculatorCode = new();

    /// <summary>Every calculator string the First Officer's per-build writes sent
    /// (FlyByWireA380Definition.SendFirstOfficerCalculatorCode), in order, without the unique prefix.</summary>
    public string[] CalculatorCode
    {
        get { lock (_gate) return _calculatorCode.ToArray(); }
    }

    internal override void SendFirstOfficerCalculatorCode(SimConnectManager sc, string rpn)
    {
        lock (_gate) _calculatorCode.Add(rpn);
    }

    /// <summary>An executor whose writes land here. It reads as connected (its sim is a manager that never
    /// opened a handle), so its dispatch runs, and this definition records each write instead of sending
    /// it.</summary>
    public FbwA380ActionExecutor Executor(string simulator = "FS2024", params (string Key, double Value)[] seed)
    {
        var executor = new FbwA380ActionExecutor();
        // The simulator whose FlyByWire build the writes go to (A380BuildNames): FS2024 unless a test
        // says otherwise; the seed is what the definition reads before a per-build write.
        executor.SetSimConnect(SeededSimConnectCache.ConnectedWith(seed).On(simulator));
        executor.SetDefinition(this);
        executor.SetAnnouncer(new GatedSpeechCapture());
        return executor;
    }
}
