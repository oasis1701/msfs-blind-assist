using System.Runtime.CompilerServices;
using System.Threading;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// One weather radar test at a time on a PMDG jet, whoever starts it: the panel's Weather Radar
/// Test button (the definition's WxrTestSequenceAsync) and the First Officer's (the executor's
/// WxrTestAsync, a flow step or a Preflight tick). The test toggles the EFIS WXR overlay blind, so
/// two sequences interleaved turned the overlay off before TEST, the test was silent and the
/// overlay was left ON. A start while one runs is refused; the runner ends the gate in a finally.
/// One gate per SimConnect connection and jet: the panel and the First Officer share the app's one
/// connection, and nothing else does. Thread-safe: the panel starts on the UI thread, the First
/// Officer on a pool thread.
/// </summary>
public sealed class WxrTestGate
{
    private static readonly ConditionalWeakTable<SimConnectManager, WxrTestGate> s_pmdg737 = new();
    private static readonly ConditionalWeakTable<SimConnectManager, WxrTestGate> s_pmdg777 = new();

    /// <summary>The PMDG 737's gate on this connection.</summary>
    public static WxrTestGate Pmdg737(SimConnectManager simConnect) => s_pmdg737.GetValue(simConnect, _ => new WxrTestGate());

    /// <summary>The PMDG 777's gate on this connection.</summary>
    public static WxrTestGate Pmdg777(SimConnectManager simConnect) => s_pmdg777.GetValue(simConnect, _ => new WxrTestGate());

    private int _running;

    /// <summary>True, and the gate is taken, when no test is running; false while one runs.</summary>
    public bool TryBegin() => Interlocked.CompareExchange(ref _running, 1, 0) == 0;

    /// <summary>The test that took the gate has finished (always from a finally).</summary>
    public void End() => Volatile.Write(ref _running, 0);

    public bool IsRunning => Volatile.Read(ref _running) != 0;
}
