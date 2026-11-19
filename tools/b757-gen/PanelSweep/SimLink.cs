using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using MSFSBlindAssist.Utils;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;

namespace PanelSweep;

internal sealed class SimLinkException : Exception
{
    public SimLinkException(string message) : base(message) { }
}

/// <summary>
/// The sweep's whole contact with the sim, and the ONLY file that touches SimConnect. --dry-run never
/// reaches it.
///
/// Reads are SimConnect data definitions of "L:name" (units "number"), requested once per read, as the
/// app reads its L:vars. Writes are MobiFlight "MF.SimVars.Set.&lt;rpn&gt;" commands on the default
/// "MobiFlight.Command" client-data area, the channel the app's ExecuteCalculatorCode writes on: Set runs
/// the code ONCE. They are never sent as "MF.SimVars.Add", which REGISTERS an expression the module then
/// re-evaluates on every poll (measured 2026-10-28: a write registered that
/// way re-ran every cycle and drove a trim to its stop). Every command carries a fresh "{seq} 0 * " prefix
/// so two identical writes are never byte-identical (CalcCommand).
///
/// Each write returns the id of the packet it was sent in, so a SimConnect exception the sim raises for it
/// (whose dwSendID names that packet) can be told apart from one raised for a read: <see cref="WriteErrors"/>.
/// A message handler that throws, or a receive that fails with anything but SimConnect's own COMException,
/// is never swallowed: it is printed and kept in <see cref="Fault"/>, and the sweep stops once the control
/// in progress has been put back.
/// </summary>
internal sealed class SimLink : IDisposable
{
    private enum AREA : uint { Command = 1000 }
    private enum CMD_DEF : uint { Command = 2000 }
    private enum DEF : uint { }
    private enum REQ : uint { }

    private const uint FirstDef = 10000;
    private const uint FirstReq = 100000;
    private const int CommandBytes = 1024;
    private const uint UserObject = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    internal struct CommandData
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CommandBytes)]
        public string command;
    }

    // Filled by SimConnect's marshaller (RegisterDataDefineStruct), never assigned in code (CS0649 is off in the csproj).
    internal struct SingleValue
    {
        public double value;
    }

    private sealed class Pending
    {
        public readonly ManualResetEventSlim Done = new(false);
        public double Value;
    }

    private readonly Microsoft.FlightSimulator.SimConnect.SimConnect _sc;
    private readonly TextWriter _out;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _opened = new(false);
    private readonly Dictionary<string, uint> _defs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<uint, Pending> _pending = new();
    private Thread? _pump;
    private volatile bool _stop;
    private uint _nextDef = FirstDef;
    private uint _nextReq = FirstReq;
    private long _seq;
    private int _exceptions;
    private int _faults;
    private volatile string? _fault;

    /// <summary>The SimConnect exceptions the sim raised, by the id of the packet that caused each.</summary>
    private readonly ConcurrentDictionary<uint, SIMCONNECT_EXCEPTION> _exceptionsBySendId = new();

    /// <summary>True once the sim has told us it is quitting.</summary>
    public volatile bool SimQuit;

    /// <summary>How many SimConnect exception messages the sim has sent (a bad datum name, a dropped write, ...).</summary>
    public int Exceptions => Volatile.Read(ref _exceptions);

    /// <summary>
    /// The first fault on the pump thread (a message handler that threw, or a receive that failed with anything
    /// but SimConnect's own COMException), or null. Once set, reads may no longer be delivered: the sweep puts
    /// back the control in progress and stops.
    /// </summary>
    public string? Fault => _fault;

    /// <summary>How many faults the pump thread has met (only the first is printed in full).</summary>
    public int Faults => Volatile.Read(ref _faults);

    private SimLink(Microsoft.FlightSimulator.SimConnect.SimConnect sc, TextWriter output)
    {
        _sc = sc;
        _out = output;
        _sc.OnRecvOpen += (s, e) => Handle("open", () => _opened.Set());
        _sc.OnRecvQuit += (s, e) => Handle("quit", () => { SimQuit = true; _out.WriteLine("The sim is quitting."); });
        _sc.OnRecvException += (s, e) => Handle("exception", () =>
        {
            Interlocked.Increment(ref _exceptions);
            _exceptionsBySendId[e.dwSendID] = (SIMCONNECT_EXCEPTION)e.dwException;
            _out.WriteLine($"SimConnect exception: {(SIMCONNECT_EXCEPTION)e.dwException} (sendId={e.dwSendID}, index={e.dwIndex})");
        });
        _sc.OnRecvSimobjectData += (s, e) => Handle("data", () => OnData(s, e));
    }

    /// <summary>Runs a message handler's body; a throw is recorded as the link's <see cref="Fault"/>, never swallowed.</summary>
    private void Handle(string handler, Action body)
    {
        try { body(); }
        catch (Exception ex) { RecordFault($"the {handler} handler threw {ex.GetType().Name}: {ex.Message}"); }
    }

    private void RecordFault(string message)
    {
        if (Interlocked.Increment(ref _faults) > 1) return;   // the first is the one that matters; the summary counts the rest
        _fault = message;
        try
        {
            _out.WriteLine($"SimConnect FAULT: {message}. The sweep stops once the control in progress is put back.");
        }
        catch { /* the console is gone: Fault still stops the run */ }
    }

    /// <summary>
    /// The SimConnect exceptions the sim raised for the packets <paramref name="sendIds"/> (the ids
    /// <see cref="WriteCalc"/> returned), as their names, e.g. "DATA_ERROR"; empty when there were none. An
    /// exception arrives a little after its packet, so ask after the read-back wait.
    /// </summary>
    public IReadOnlyList<string> WriteErrors(IEnumerable<uint> sendIds) =>
        sendIds.Where(id => id != 0)
            .Select(id => _exceptionsBySendId.TryGetValue(id, out var kind) ? kind.ToString().Replace("SIMCONNECT_EXCEPTION_", "") : null)
            .OfType<string>()
            .ToList();

    /// <summary>
    /// Opens SimConnect, maps the MobiFlight command area and pings the module. Throws
    /// <see cref="SimLinkException"/> when the sim is not running or does not answer.
    /// </summary>
    public static SimLink Connect(TextWriter output)
    {
        EnsureNativeDll(output);
        return Open(output);
    }

    /// <summary>
    /// The half of <see cref="Connect"/> that names SimConnect's types. It is its own method, never inlined, because
    /// the JIT loads Microsoft.FlightSimulator.SimConnect.dll when it compiles a method that names one of its types, and
    /// that assembly needs the native SimConnect.dll beside it to load at all: with both halves in one method the load
    /// failed before <see cref="EnsureNativeDll"/> had copied it (the first live run, 2026-10-28: "Could not load file
    /// or assembly … Microsoft.FlightSimulator.SimConnect.dll", nothing written).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SimLink Open(TextWriter output)
    {
        Microsoft.FlightSimulator.SimConnect.SimConnect sc;
        try
        {
            sc = new Microsoft.FlightSimulator.SimConnect.SimConnect("PanelSweep", IntPtr.Zero, 0x0402, null, 0);
        }
        catch (Exception ex)
        {
            throw new SimLinkException($"SimConnect did not open: {ex.Message}. Is MSFS running?");
        }

        var link = new SimLink(sc, output);
        link.StartPump();
        if (!link._opened.Wait(TimeSpan.FromSeconds(5)))
        {
            link.Dispose();
            throw new SimLinkException("Timed out waiting for SimConnect to open.");
        }

        // The area is the one MobiFlight's WASM module owns; the app maps it the same way
        // (MobiFlightWasmModule.MapClientDataAreas / SetupDataDefinitions).
        lock (link._gate)
        {
            sc.MapClientDataNameToID("MobiFlight.Command", AREA.Command);
            sc.AddToClientDataDefinition(CMD_DEF.Command, 0, CommandBytes, 0, 0);
        }
        link.SendCommand("MF.Ping");   // MobiFlight's documentation: the first command may be ignored
        Thread.Sleep(300);
        output.WriteLine("SimConnect open; MobiFlight command channel mapped.");
        return link;
    }

    /// <summary>
    /// Copies the SimConnect.dll for the running sim next to the exe, as the app does when it starts
    /// (Program.EnsureCorrectSimConnectDll). Must run before the first SimConnect call.
    /// </summary>
    private static void EnsureNativeDll(TextWriter output)
    {
        string dir = AppContext.BaseDirectory;
        string sim = SimulatorDetector.DetectRunningSimulator();
        if (sim == "Unknown")
            throw new SimLinkException("No Microsoft Flight Simulator process (FlightSimulator2024 or FlightSimulator) is running.");

        string sourceName = SimulatorDetector.GetSimConnectDllName(sim);
        string source = Path.Combine(dir, sourceName);
        string target = Path.Combine(dir, "SimConnect.dll");
        if (!File.Exists(source))
            throw new SimLinkException($"{sourceName} is missing from {dir}; rebuild the tool.");

        bool same = File.Exists(target)
            && new FileInfo(source).Length == new FileInfo(target).Length
            && Math.Abs((new FileInfo(source).LastWriteTime - new FileInfo(target).LastWriteTime).TotalSeconds) < 2;
        if (!same)
        {
            try { File.Copy(source, target, overwrite: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SimLinkException($"Could not copy {sourceName} to SimConnect.dll ({ex.Message}). Is another copy of the tool running?");
            }
        }
        output.WriteLine($"Simulator: {sim} (using {sourceName}).");
    }

    private void StartPump()
    {
        _pump = new Thread(Pump) { IsBackground = true, Name = "PanelSweep SimConnect pump" };
        _pump.Start();
    }

    /// <summary>
    /// SimConnect is not thread-safe, so the pump and every call hold the same gate. A handler only
    /// sets an event or a counter, so holding the gate in a dispatch is harmless. A COMException is
    /// SimConnect's own "nothing queued" (or the sim closing, which OnRecvQuit reports); anything else is
    /// a fault (<see cref="RecordFault"/>), never swallowed. The handlers catch their own throws
    /// (<see cref="Handle"/>), so one reaching here came from the receive itself.
    /// </summary>
    private void Pump()
    {
        while (!_stop)
        {
            lock (_gate)
            {
                for (int i = 0; i < 16; i++)
                {
                    try { _sc.ReceiveMessage(); }
                    catch (COMException) { break; }   // nothing queued, or the sim is closing
                    catch (Exception ex)
                    {
                        RecordFault($"receiving a SimConnect message threw {ex.GetType().Name}: {ex.Message}");
                        break;
                    }
                }
            }
            Thread.Sleep(10);
        }
    }

    private void OnData(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        if (_pending.TryGetValue(data.dwRequestID, out var pending)
            && data.dwData is { Length: > 0 } && data.dwData[0] is SingleValue v)
        {
            pending.Value = v.value;
            pending.Done.Set();
        }
    }

    /// <summary>
    /// One L:var read fresh (a one-shot request), or null when no value arrived within
    /// <paramref name="timeoutMs"/>. With <paramref name="rebind"/> the definition is made again: a
    /// definition bound to an L:var that did not exist yet never delivers, even after the var is created
    /// (the app's RebindVariableDataDefinition).
    /// </summary>
    public double? ReadLVar(string name, int timeoutMs = 2000, bool rebind = false) =>
        Read("L:" + name, "number", timeoutMs, rebind);

    /// <summary>One stock SimVar read fresh (units "bool", "number", ...), or null.</summary>
    public double? ReadSimVar(string name, string units, int timeoutMs = 2000) =>
        Read(name, units, timeoutMs, rebind: false);

    private double? Read(string datumName, string units, int timeoutMs, bool rebind)
    {
        if (SimQuit) return null;
        string key = datumName + "|" + units;
        var pending = new Pending();
        uint reqId;
        try
        {
            lock (_gate)
            {
                if (rebind && _defs.TryGetValue(key, out uint old))
                {
                    _sc.ClearDataDefinition((DEF)old);
                    _defs.Remove(key);
                }
                if (!_defs.TryGetValue(key, out uint defId))
                {
                    defId = _nextDef++;
                    _sc.AddToDataDefinition((DEF)defId, datumName, units, SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                    _sc.RegisterDataDefineStruct<SingleValue>((DEF)defId);
                    _defs[key] = defId;
                }
                reqId = _nextReq++;
                _pending[reqId] = pending;
                _sc.RequestDataOnSimObject((REQ)reqId, (DEF)defId, UserObject,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
        }
        catch (Exception ex)
        {
            throw new SimLinkException($"Reading {datumName} failed: {ex.Message}");
        }

        bool got = pending.Done.Wait(timeoutMs);
        _pending.TryRemove(reqId, out _);
        return got ? pending.Value : null;
    }

    /// <summary>
    /// One calculator write, run ONCE ("MF.SimVars.Set", never "Add"), made unique so MobiFlight cannot
    /// drop it as a repeat of the command before. Throws when it is longer than the command buffer
    /// rather than sending the cut-off half of a click. Returns the id of the packet it went in, for
    /// <see cref="WriteErrors"/> (0 when SimConnect would not say).
    /// </summary>
    public uint WriteCalc(string rpn)
    {
        if (SimQuit) throw new SimLinkException("The sim has quit.");
        string command = CalcCommand.Build(rpn, Interlocked.Increment(ref _seq));
        if (command.Length > CalcCommand.MaxChars)
            throw new SimLinkException($"The command is {command.Length} characters; the buffer takes {CalcCommand.MaxChars}, so it was not sent.");
        return SendCommand(command);
    }

    private uint SendCommand(string command)
    {
        try
        {
            lock (_gate)
            {
                _sc.SetClientData(AREA.Command, CMD_DEF.Command, SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT, 0,
                    new CommandData { command = command });
                // Under the same gate, so no other packet can have gone in between.
                try
                {
                    uint sendId = _sc.GetLastSentPacketID();
                    return sendId;
                }
                catch (COMException) { return 0; }
            }
        }
        catch (Exception ex)
        {
            throw new SimLinkException($"Sending a command failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _stop = true;
        _pump?.Join(500);
        lock (_gate)
        {
            try { _sc.Dispose(); } catch { /* the sim may already be gone */ }
        }
    }
}
