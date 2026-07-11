#nullable enable annotations
// PMDG NG3 Dispatch Test Utility
//
// Connects directly to the running MSFS sim, subscribes to PMDG_NG3_Data
// (with PERIOD.ON_SET / FLAG.CHANGED so values are always fresh), and offers
// an interactive REPL for sending dispatches and observing the resulting
// state changes. Used to identify which dispatch shape PMDG NG3 actually
// accepts for switches like EVT_OH_ELEC_GRD_PWR_SWITCH.
//
// Commands (case-insensitive, parameter is decimal int):
//   read                — print current cached state of key bool/byte fields
//   cda <eventId> <p>   — SetClientData write {EventId, Parameter=p} on Control CDA
//   evt <eventId> <p>   — TransmitClientEvent with eventId, parameter=p
//   alias <eventId>     — pre-register a "#<eventId>" alias for evt
//   help                — show this help
//   q                   — quit
//
// Common eventId values for the 737 NG3:
//   69633  EVT_OH_ELEC_BATTERY_SWITCH
//   69634  EVT_OH_ELEC_BATTERY_GUARD
//   69649  EVT_OH_ELEC_GRD_PWR_SWITCH
//   69659  EVT_OH_ELEC_GEN1_SWITCH
//   69660  EVT_OH_ELEC_APU_GEN1_SWITCH
//   69749  EVT_OH_LIGHTS_TAXI
//
// Common parameter values:
//   0          direct off
//   1          direct on
//   536870912  MOUSE_FLAG_LEFTSINGLE (0x20000000)
//   2147483648 MOUSE_FLAG_RIGHTSINGLE (0x80000000)  — pass as 2147483648
//   131072     MOUSE_FLAG_LEFTRELEASE (0x00020000)
//   524288     MOUSE_FLAG_RIGHTRELEASE (0x00080000)

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.FlightSimulator.SimConnect;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;

namespace PMDGDispatchTester;

// ---------------------------------------------------------------------------
// PMDG NG3 control struct — same shape as the main app's PMDGNG3Control.
// ---------------------------------------------------------------------------
[StructLayout(LayoutKind.Sequential)]
public struct PMDGControl
{
    public uint EventId;
    public uint Parameter;
}

// One-double payload for lvarget reads.
[StructLayout(LayoutKind.Sequential)]
public struct LvarValue
{
    public double Value;
}

// ---------------------------------------------------------------------------
// Subset of PMDG_NG3_Data — we only define the FIRST byte we need, then we
// read individual fields by offset using AddToClientDataDefinition's offset
// parameter. This avoids having to mirror the entire 916-byte struct.
//
// Each "FieldDef" describes one field we want to subscribe to and decode.
// ---------------------------------------------------------------------------
public record FieldDef(string Name, uint Offset, uint Size, string Kind /* "bool" | "byte" */);

class Program
{
    // ----- Client data area names (from PMDG_NG3_SDK.h) -----
    const string PMDG_NG3_DATA_NAME    = "PMDG_NG3_Data";
    const string PMDG_NG3_CONTROL_NAME = "PMDG_NG3_Control";

    // ----- Our own internal IDs (must NOT collide with PMDG's 0x4E47333x range) -----
    enum DataAreaId : uint
    {
        Data    = 0x4E477730,  // "Nww0"
        Control = 0x4E477731,
    }

    enum DataDefId : uint
    {
        DataFields = 0x4E477740,
        Control    = 0x4E477741,
    }

    enum DataRequestId : uint
    {
        DataFields = 60000,
    }

    enum TxEventGroup : uint
    {
        First = 70000,
    }

    enum NotifGroup : uint
    {
        Default = 1,
    }

    enum GroupPriority : uint
    {
        Highest = 1,
    }

    // ----- The fields we subscribe to and report on every change -----
    //
    // Offsets are byte positions within PMDG_NG3_Data, computed by walking
    // the SDK header struct from the top. Battery/electrical block:
    //
    //   Offset  Type           Field
    //   ...     ...            ... (preceding fields)
    //   Computed by inspection of PMDGNG3DataStruct.cs (matches SDK).
    //
    // We pick a couple of high-confidence offsets manually below.
    static readonly FieldDef[] s_fields =
    [
        // These offsets are determined empirically — we'll print them at startup
        // and the user can verify they match expected values.
        // Placeholders for now; we use Marshal.OffsetOf approach below.
    ];

    static SimConnect _sc;
    static readonly Dictionary<string, uint> _aliasMap = new();
    static uint _nextTxId = (uint)TxEventGroup.First;
    static uint _nextLvarDefId = 0;
    static uint _nextKevId = 0;
    // lvarget bookkeeping: request id -> L-var name (result printed on receipt)
    static readonly Dictionary<uint, string> _lvarGetRequests = new();
    static uint _nextLvarGetReqId = 70000;
    static readonly object _lock = new();

    // Cached last values per field for delta detection
    static readonly Dictionary<string, double> _lastValues = new();
    static volatile bool _quit;

    static int Main(string[] args)
    {
        Console.WriteLine("=== PMDG NG3 Dispatch Test Utility ===");
        Console.WriteLine();
        try
        {
            _sc = new SimConnect("PMDGDispatchTester", IntPtr.Zero, 0, null, 0);
            Console.WriteLine("[OK] Connected to SimConnect.");
        }
        catch (COMException ex)
        {
            Console.WriteLine($"[ERR] Could not connect to MSFS: {ex.Message}");
            Console.WriteLine("      Make sure MSFS is running and the PMDG 737 is loaded.");
            return 1;
        }

        _sc.OnRecvException     += OnException;
        _sc.OnRecvClientData    += OnClientData;
        _sc.OnRecvSimobjectData += OnSimobjectData;
        _sc.OnRecvQuit          += (_, _) => { Console.WriteLine("[INFO] Sim quit."); _quit = true; };

        SetupClientData();
        Console.WriteLine();
        PrintHelp();

        // Background message-pump thread (SimConnect uses a single-threaded
        // ReceiveMessage callback model).
        var pumpThread = new Thread(MessagePumpLoop) { IsBackground = true };
        pumpThread.Start();

        // Wait briefly for the first snapshot to arrive (PMDG pushes ON_SET
        // so it should land within ~50ms).
        Thread.Sleep(500);
        Console.WriteLine("[INFO] Ready. Commands from stdin (one per line).");

        // Command loop reads stdin. Each command is followed by a delay
        // so the resulting snapshot has time to arrive.
        string line;
        while (!_quit && (line = Console.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#")) continue; // comment
            if (line.StartsWith("sleep "))
            {
                int ms = int.Parse(line.Substring(6));
                Thread.Sleep(ms);
                continue;
            }
            HandleCommand(line);
            Thread.Sleep(800); // give PMDG time to react and push us a snapshot
        }

        Console.WriteLine("[INFO] Exiting.");
        _quit = true;
        Thread.Sleep(100);
        _sc?.Dispose();
        return 0;
    }

    static void SetupClientData()
    {
        // Map names to IDs
        _sc.MapClientDataNameToID(PMDG_NG3_DATA_NAME,    DataAreaId.Data);
        _sc.MapClientDataNameToID(PMDG_NG3_CONTROL_NAME, DataAreaId.Control);

        // Build the data definition by adding the fields we care about.
        // Offsets come from the SDK header — walk the struct manually.
        // For now, focus on the electrical block.
        //
        // SDK layout (truncated to what we care about):
        //   The pre-electrical fields total to byte offset ~234 by inspection.
        //   Rather than miscalculate, we add ALL bytes from offset 0 to the
        //   end-of-electrical block as a single fixed-size buffer, then decode
        //   in C# using offsets we trust from the existing PMDGNG3DataStruct.
        //
        // Simpler approach: define one field per offset we want, with the
        // appropriate primitive size.
        //
        // Important: the offsets below match the existing
        // PMDGNG3DataStruct.cs in the main project. If they ever shift,
        // update both in lockstep.

        // We'll subscribe to the WHOLE 916-byte data struct in one shot, then
        // index into it. SimConnect allows AddToClientDataDefinition with a
        // generic byte block. We then RegisterStruct with our own byte-array
        // wrapper.

        // Use the FULL typed struct so we can decode named fields by reflection.
        uint dataSize = (uint)Marshal.SizeOf<MSFSBlindAssist.SimConnect.PMDGNG3DataStruct>();
        _sc.AddToClientDataDefinition(
            DataDefId.DataFields,
            0,
            dataSize,
            0, 0);
        _sc.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, MSFSBlindAssist.SimConnect.PMDGNG3DataStruct>(DataDefId.DataFields);

        // Subscribe with ON_SET + CHANGED so we always get the freshest data.
        _sc.RequestClientData(
            DataAreaId.Data,
            DataRequestId.DataFields,
            DataDefId.DataFields,
            SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET,
            SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.CHANGED,
            0, 0, 0);

        // Also set up the Control CDA for outbound writes
        const uint controlSize = 8; // 2 × uint
        _sc.AddToClientDataDefinition(
            DataDefId.Control,
            0, controlSize,
            0, 0);
        _sc.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDGControl>(DataDefId.Control);

        Console.WriteLine("[OK] Subscribed to PMDG_NG3_Data (PERIOD.ON_SET, FLAG.CHANGED)");
        Console.WriteLine("[OK] Ready to write to PMDG_NG3_Control");
    }

    static void OnSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        string? name = null;
        lock (_lock)
        {
            if (_lvarGetRequests.TryGetValue(data.dwRequestID, out name))
                _lvarGetRequests.Remove(data.dwRequestID);
        }
        if (name == null) return;
        var v = (LvarValue)data.dwData[0];
        Console.WriteLine($"[LVARGET] L:{name} = {v.Value}");
    }

    static void OnException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION ex)
    {
        var name = Enum.GetName(typeof(SIMCONNECT_EXCEPTION), ex.dwException) ?? ex.dwException.ToString();
        Console.WriteLine($"\n[SC EXCEPTION] {name} (sendId={ex.dwSendID}, index={ex.dwIndex})");
        Console.Write("> ");
    }

    static void OnClientData(SimConnect sender, SIMCONNECT_RECV_CLIENT_DATA data)
    {
        if (data.dwRequestID != (uint)DataRequestId.DataFields) return;
        var typed = (MSFSBlindAssist.SimConnect.PMDGNG3DataStruct)data.dwData[0];

        // Decode and print the fields we care about. Offsets MUST match
        // PMDGNG3DataStruct.cs in the main project.
        // The most important fields for this debug session are:
        //   ELEC_BatSelector       (offset within ELEC block, byte)
        //   ELEC_annunGRD_POWER_AVAILABLE (bool)
        //   ELEC_GrdPwrSw          (bool)
        //   ELEC_BusTransSw_AUTO   (bool)
        //   ELEC_GenSw[2]          (bool x 2)
        //   ELEC_APUGenSw[2]       (bool x 2)
        //
        // Offsets are derived from the existing PMDGNG3DataStruct field layout.
        // Rather than hard-code, we use the [DEBUG] field index report below
        // on first snapshot for the user to verify.
        //
        // For this iteration, focus on dumping a hex slice of bytes 200-310
        // which covers the entire electrical bool block. The user will be
        // able to see EXACTLY which byte changes when we dispatch.

        var snapshot = ExtractFields(typed);
        bool firstTime = _lastValues.Count == 0;
        var changeList = new List<string>();
        foreach (var (name, val) in snapshot)
        {
            if (!_lastValues.TryGetValue(name, out var prev) || prev != val)
            {
                if (!firstTime) changeList.Add($"{name}={val}");
                _lastValues[name] = val;
            }
        }
        if (firstTime)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Initial snapshot — values:");
            foreach (var (name, val) in snapshot)
                Console.WriteLine($"    {name} = {val}");
        }
        else if (changeList.Count > 0)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CHANGE: " + string.Join(", ", changeList));
        }
    }

    // Decode the electrical fields from the raw byte block.
    // Offsets must match the SDK header / PMDGNG3DataStruct.cs.
    //
    // The electrical block in the SDK header starts at line 154:
    //   bool        ELEC_annunBAT_DISCHARGE;
    //   bool        ELEC_annunTR_UNIT;
    //   bool        ELEC_annunELEC;
    //   uchar       ELEC_DCMeterSelector;
    //   uchar       ELEC_ACMeterSelector;
    //   uchar       ELEC_BatSelector;
    //   bool        ELEC_CabUtilSw;
    //   bool        ELEC_IFEPassSeatSw;
    //   bool        ELEC_annunDRIVE[2];
    //   bool        ELEC_annunSTANDBY_POWER_OFF;
    //   bool        ELEC_IDGDisconnectSw[2];
    //   uchar       ELEC_StandbyPowerSelector;
    //   bool        ELEC_annunGRD_POWER_AVAILABLE;
    //   bool        ELEC_GrdPwrSw;
    //   bool        ELEC_BusTransSw_AUTO;
    //   bool        ELEC_GenSw[2];
    //   bool        ELEC_APUGenSw[2];
    //
    // The exact starting offset of ELEC_annunBAT_DISCHARGE is hard to compute
    // by hand. So instead: on the first snapshot we DUMP a wide hex window
    // and the user (or we) can identify the bytes by changing them in the sim
    // and watching which byte flips.
    //
    // For convenience this method ALSO prints the hex window during early
    // snapshots until we've identified the offsets.
    static int s_snapshotCount = 0;

    // Reflect over the typed struct so we can access named fields cleanly.
    static readonly System.Reflection.FieldInfo[] s_namedFields =
        typeof(MSFSBlindAssist.SimConnect.PMDGNG3DataStruct)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

    static readonly string[] s_namedFieldsOfInterest = new[]
    {
        // Electrical (kept for cross-checks)
        "ELEC_BatSelector",
        "ELEC_GrdPwrSw",
        "ELEC_GenSw",
        "ELEC_APUGenSw",
        // Engine start + ignition
        "ENG_StartSelector",          // 2-element byte[]: 0=GRD, 1=OFF, 2=CONT, 3=FLT
        "ENG_IgnitionSelector",       // byte: 0=IGN L, 1=BOTH, 2=IGN R
        "ENG_StartValve",             // 2-element bool[]: true = valve open
        "ENG_annunENGINE_CONTROL",
        // Fuel
        "FUEL_annunENG_VALVE_CLOSED", // 2-element byte[]: 0=closed (CUTOFF), 1=open (RUN), 2=in transit
        // APU
        "APU_Selector",
        "APU_EGTNeedle",
        // Attendant / ground call (cabin-call probe)
        "COMM_Attend_PressCount",
        "COMM_GrdCall_PressCount",
        "COMM_annunCALL",
        // Airstair door state (interior-controls probe)
        "DOOR_annunAIRSTAIR",
        // Fire panel OVHT/FIRE detection test (FO fire-test probe)
        "FIRE_DetTestSw",             // byte: 0=FAULT/INOP?  1=neutral  2=OVHT/FIRE?
        "WARN_annunFIRE_WARN",        // 2-element bool[]: master FIRE WARN lights
        "WARN_annunOVHT_DET",
        "FIRE_HandleIlluminated",     // 3-element bool[]: Eng1, APU, Eng2 handle lights
        "FIRE_annunFAULT",
        "FIRE_annunAPU_DET_INOP",
        "FIRE_annunENG_OVERHEAT",     // 2-element bool[]
        "WARN_annunMASTER_CAUTION",   // 2-element bool[]
    };

    static (string, double)[] ExtractFields(MSFSBlindAssist.SimConnect.PMDGNG3DataStruct data)
    {
        s_snapshotCount++;
        var list = new List<(string, double)>();
        foreach (var fieldName in s_namedFieldsOfInterest)
        {
            var fi = Array.Find(s_namedFields, f => f.Name == fieldName);
            if (fi == null) continue;
            object? raw = fi.GetValue(data);
            if (raw is Array arr)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    object? elem = arr.GetValue(i);
                    list.Add(($"{fieldName}[{i}]", ConvertToDouble(elem)));
                }
            }
            else
            {
                list.Add((fieldName, ConvertToDouble(raw)));
            }
        }
        return list.ToArray();
    }

    static double ConvertToDouble(object? v) => v switch
    {
        bool b => b ? 1.0 : 0.0,
        byte b => (double)b,
        short s => (double)s,
        ushort u => (double)u,
        int i => (double)i,
        uint u => (double)u,
        long l => (double)l,
        ulong u => (double)u,
        float f => (double)f,
        double d => d,
        _ => 0.0
    };

    static void DumpHexWindow(byte[] bytes, int start, int len)
    {
        for (int i = 0; i < len; i += 16)
        {
            int rowStart = start + i;
            var sb = new System.Text.StringBuilder();
            sb.Append($"  {rowStart,3:D3}: ");
            for (int j = 0; j < 16 && rowStart + j < bytes.Length; j++)
                sb.Append($"{bytes[rowStart + j]:X2} ");
            Console.WriteLine(sb.ToString());
        }
    }

    static void HandleCommand(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        try
        {
            switch (cmd)
            {
                case "help":
                case "?":
                    PrintHelp();
                    break;
                case "read":
                    // Force a fresh request
                    _sc.RequestClientData(
                        DataAreaId.Data,
                        DataRequestId.DataFields,
                        DataDefId.DataFields,
                        SIMCONNECT_CLIENT_DATA_PERIOD.ONCE,
                        SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT,
                        0, 0, 0);
                    break;
                case "cda":
                {
                    if (parts.Length != 3) { Console.WriteLine("usage: cda <eventId> <parameter>"); break; }
                    uint eventId = ParseUInt(parts[1]);
                    uint parameter = ParseUInt(parts[2]);
                    SendViaCDA(eventId, parameter);
                    break;
                }
                case "evt":
                {
                    if (parts.Length != 3) { Console.WriteLine("usage: evt <eventId> <parameter>"); break; }
                    uint eventId = ParseUInt(parts[1]);
                    uint parameter = ParseUInt(parts[2]);
                    SendViaTransmit(eventId, parameter);
                    break;
                }
                case "lvar":
                {
                    // lvar <name> <value> — set an L-var via a throwaway data
                    // definition (same mechanism as the main app's SetLVar).
                    if (parts.Length != 3) { Console.WriteLine("usage: lvar <name> <value>"); break; }
                    double lv = double.Parse(parts[2], CultureInfo.InvariantCulture);
                    var defId = (DataDefId)(0x4E477750 + _nextLvarDefId++);
                    _sc.AddToDataDefinition(defId, $"L:{parts[1]}", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                    _sc.SetDataOnSimObject(defId, SimConnect.SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_DATA_SET_FLAG.DEFAULT, lv);
                    Console.WriteLine($"[LVAR] L:{parts[1]} = {lv} sent.");
                    break;
                }
                case "lvarget":
                {
                    // lvarget <name> — one-shot read of an L-var; value prints
                    // asynchronously as [LVARGET] when the sim responds.
                    if (parts.Length != 2) { Console.WriteLine("usage: lvarget <name>"); break; }
                    var defId = (DataDefId)(0x4E477750 + _nextLvarDefId++);
                    uint reqId = _nextLvarGetReqId++;
                    _sc.AddToDataDefinition(defId, $"L:{parts[1]}", "number",
                        SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                    _sc.RegisterDataDefineStruct<LvarValue>(defId);
                    lock (_lock) { _lvarGetRequests[reqId] = parts[1]; }
                    _sc.RequestDataOnSimObject((DataRequestId)reqId, defId,
                        SimConnect.SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.ONCE,
                        SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                    break;
                }
                case "kev":
                {
                    // kev <K-event-name> <data> — map + transmit a standard sim event.
                    if (parts.Length != 3) { Console.WriteLine("usage: kev <name> <data>"); break; }
                    uint kData = ParseUInt(parts[2]);
                    var kId = (TxEventGroup)(90000 + _nextKevId++);
                    _sc.MapClientEventToSimEvent(kId, parts[1]);
                    _sc.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, kId, kData,
                        NotifGroup.Default, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
                    Console.WriteLine($"[KEV] {parts[1]} data={kData} sent.");
                    break;
                }
                case "q":
                case "quit":
                case "exit":
                    _quit = true;
                    break;
                default:
                    Console.WriteLine($"unknown command: {cmd} (type 'help')");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERR] {ex.GetType().Name}: {ex.Message}");
        }
    }

    static uint ParseUInt(string s)
    {
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return uint.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    static void SendViaCDA(uint eventId, uint parameter)
    {
        var ctrl = new PMDGControl { EventId = eventId, Parameter = parameter };
        _sc.SetClientData(
            DataAreaId.Control,
            DataDefId.Control,
            SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT,
            0,
            ctrl);
        Console.WriteLine($"[CDA] eventId={eventId}, parameter={parameter} (0x{parameter:X8}) sent.");
    }

    static void SendViaTransmit(uint eventId, uint parameter)
    {
        string aliasName = "#" + eventId;
        uint mappedId;
        lock (_lock)
        {
            if (!_aliasMap.TryGetValue(aliasName, out mappedId))
            {
                mappedId = _nextTxId++;
                _aliasMap[aliasName] = mappedId;
                _sc.MapClientEventToSimEvent((TxEventGroup)mappedId, aliasName);
            }
        }
        _sc.TransmitClientEvent(
            0u,                                         // SIMCONNECT_OBJECT_ID_USER
            (TxEventGroup)mappedId,
            parameter,
            (NotifGroup)GroupPriority.Highest,
            SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
        Console.WriteLine($"[EVT] eventId={eventId}, parameter={parameter} (0x{parameter:X8}) sent.");
    }

    static void MessagePumpLoop()
    {
        while (!_quit)
        {
            try
            {
                _sc?.ReceiveMessage();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[PUMP ERR] {ex.Message}");
            }
            Thread.Sleep(15);
        }
    }

    static void PrintHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  read              Force a one-shot data refresh");
        Console.WriteLine("  cda E P           Send via CDA SetClientData (E=eventId, P=parameter)");
        Console.WriteLine("  evt E P           Send via TransmitClientEvent");
        Console.WriteLine("  q                 Quit");
        Console.WriteLine();
        Console.WriteLine("Useful event IDs:");
        Console.WriteLine("  69633  battery switch     69634  battery guard");
        Console.WriteLine("  69649  GRD PWR switch     69659  GEN1   69660 APU GEN1");
        Console.WriteLine("  69749  taxi light");
        Console.WriteLine();
        Console.WriteLine("Useful parameters:");
        Console.WriteLine("  0          off direct");
        Console.WriteLine("  1          on direct");
        Console.WriteLine("  536870912  LEFTSINGLE  (0x20000000)");
        Console.WriteLine("  2147483648 RIGHTSINGLE (0x80000000)");
        Console.WriteLine("  131072     LEFTRELEASE (0x00020000)");
        Console.WriteLine("  524288     RIGHTRELEASE(0x00080000)");
        Console.WriteLine();
    }
}

// Raw byte block for PMDG_NG3_Data — we don't care about field-level decode,
// just snapshot the entire 916-byte block and decode individual bytes in C#.
[StructLayout(LayoutKind.Sequential, Size = 916)]
public struct RawDataBlock
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 916)]
    public byte[] bytes;
}
