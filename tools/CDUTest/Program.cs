using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.FlightSimulator.SimConnect;

namespace CDUTest;

internal static class Program
{
    private enum DATA_ID : uint { Control = 0 }
    private enum DEF_ID : uint { Control = 0 }
    private enum EVENT_ID : uint { ZeroAlias = 100 }

    [StructLayout(LayoutKind.Sequential)]
    private struct PMDG777Control
    {
        public uint EventId;
        public uint Parameter;
    }

    private const uint SIMCONNECT_OBJECT_ID_USER = 0;
    private const uint MOUSE_FLAG_LEFTSINGLE = 0x20000000;

    private static SimConnect? _sc;
    private static readonly ManualResetEventSlim _connected = new(false);
    private static readonly object _msgLock = new();
    private static uint _nextEventAliasId = 1000;
    private static readonly System.Collections.Generic.Dictionary<uint, uint> _transmitAliases = new();

    private static int Main(string[] args)
    {
        Console.WriteLine("CDUTest — verifies PMDG CDA / TransmitClientEvent dispatch paths");
        Console.WriteLine("Args: <variant> <method> <eventId> [parameter]");
        Console.WriteLine("  variant = 777 | 737   (selects which Control CDA to map)");
        Console.WriteLine("  method  = cda | transmit");
        Console.WriteLine("  Known 777 event IDs:");
        Console.WriteLine("    HOLD    = 69979   PROG    = 69980");
        Console.WriteLine("    NAV_RAD = 69983   FMCCOMM = 73103");
        Console.WriteLine("    INIT_REF= 69972");
        Console.WriteLine("  Known 737 NG3 event IDs:");
        Console.WriteLine("    EVT_OH_ELEC_BATTERY_SWITCH = 69633");
        Console.WriteLine("    EVT_OH_ELEC_BATTERY_GUARD  = 69634");
        Console.WriteLine("    EVT_OH_ELEC_STBY_PWR_SWITCH= 69642");
        Console.WriteLine("    EVT_OH_ELEC_STBY_PWR_GUARD = 69643");
        if (args.Length < 3)
        {
            Console.WriteLine();
            Console.WriteLine("Example: CDUTest 777 cda 69979 1");
            Console.WriteLine("         CDUTest 737 cda 69633 0       # battery → OFF");
            Console.WriteLine("         CDUTest 737 cda 69633 2       # battery → ON (3-pos selector)");
            Console.WriteLine("         CDUTest 737 transmit 69633 536870912  # battery click (LEFTSINGLE)");
            return 1;
        }

        string variant = args[0];
        string method = args[1].ToLowerInvariant();
        uint eventId = uint.Parse(args[2]);
        uint parameter = args.Length >= 4 ? uint.Parse(args[3]) : 1;

        string controlAreaName;
        switch (variant)
        {
            case "777":
                controlAreaName = "PMDG_777X_Control";
                break;
            case "737":
                controlAreaName = "PMDG_NG3_Control";
                break;
            default:
                Console.WriteLine($"Unknown variant '{variant}'. Use '777' or '737'.");
                return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"Variant:    {variant} ({controlAreaName})");
        Console.WriteLine($"Method:     {method}");
        Console.WriteLine($"EventId:    {eventId} (0x{eventId:X})");
        Console.WriteLine($"Parameter:  {parameter} (0x{parameter:X})");
        Console.WriteLine();

        try
        {
            _sc = new SimConnect("CDUTest", IntPtr.Zero, 0x0402, null, 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SimConnect open failed: {ex.Message}");
            Console.WriteLine("Is MSFS running?");
            return 2;
        }

        _sc.OnRecvOpen += (s, e) =>
        {
            Console.WriteLine($"Connected to {e.szApplicationName} v{e.dwApplicationVersionMajor}.{e.dwApplicationVersionMinor}");
            _connected.Set();
        };
        _sc.OnRecvException += (s, e) =>
        {
            Console.WriteLine($"SimConnect exception: {(SIMCONNECT_EXCEPTION)e.dwException} (sendId={e.dwSendID}, index={e.dwIndex})");
        };
        _sc.OnRecvQuit += (s, e) => Console.WriteLine("Sim quit");

        // Pump messages on a worker until we either succeed or time out.
        var pumpStop = new ManualResetEventSlim(false);
        var pumpThread = new Thread(() =>
        {
            while (!pumpStop.IsSet)
            {
                try { _sc?.ReceiveMessage(); }
                catch { /* ignore */ }
                Thread.Sleep(20);
            }
        }) { IsBackground = true };
        pumpThread.Start();

        if (!_connected.Wait(TimeSpan.FromSeconds(5)))
        {
            Console.WriteLine("Timed out waiting for SimConnect open.");
            pumpStop.Set();
            return 3;
        }

        // Map the selected Control CDA (same 8-byte {EventId, Parameter} layout for both variants).
        _sc.MapClientDataNameToID(controlAreaName, DATA_ID.Control);
        uint controlSize = (uint)Marshal.SizeOf<PMDG777Control>();
        _sc.AddToClientDataDefinition(DEF_ID.Control, 0, controlSize, 0, 0);
        _sc.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDG777Control>(DEF_ID.Control);
        Console.WriteLine($"Mapped {controlAreaName} (size={controlSize} bytes)");

        // Allow a brief moment for the mapping to settle before we send.
        Thread.Sleep(150);

        if (method == "cda")
        {
            var ctrl = new PMDG777Control { EventId = eventId, Parameter = parameter };
            _sc.SetClientData(DATA_ID.Control, DEF_ID.Control,
                SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT, 0, ctrl);
            Console.WriteLine($"[CDA] Wrote {controlAreaName} = {{EventId={eventId}, Parameter={parameter}}}");
        }
        else if (method == "transmit")
        {
            // Optional args 5+6: repeat count + gap ms — replicates multi-click rotary
            // walks (e.g. the FO WalkedSelector / baro-knob rotation) at their real
            // inter-click timing, which a one-shot-per-process launch can't test.
            int count = args.Length >= 5 ? int.Parse(args[4]) : 1;
            int gapMs = args.Length >= 6 ? int.Parse(args[5]) : 80;

            string alias = "#" + eventId;
            uint mappedId = _nextEventAliasId++;
            _sc.MapClientEventToSimEvent((EVENT_ID)mappedId, alias);
            Thread.Sleep(100);
            for (int i = 0; i < count; i++)
            {
                _sc.TransmitClientEvent(SIMCONNECT_OBJECT_ID_USER, (EVENT_ID)mappedId,
                    parameter, GROUP_PRIORITY.HIGHEST, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
                Console.WriteLine($"[Transmit] Fired '{alias}' with dwData=0x{parameter:X} ({i + 1}/{count})");
                if (i < count - 1) Thread.Sleep(gapMs);
            }
        }
        else
        {
            Console.WriteLine("Unknown method. Use 'cda' or 'transmit'.");
            pumpStop.Set();
            return 4;
        }

        // Wait briefly so any exception callback can fire before we exit.
        Thread.Sleep(800);

        Console.WriteLine("Done. (Check CDU screen via simconnect-mcp to verify navigation.)");
        pumpStop.Set();
        try { _sc.Dispose(); } catch { }
        return 0;
    }

    private enum GROUP_PRIORITY : uint { HIGHEST = 1 }
}
