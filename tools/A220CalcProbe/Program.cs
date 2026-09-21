// A220 MobiFlight calc-path probe (recon item R2 in docs/a220-plan.md).
//
// Sends each command-line argument as an RPN calculator string through the
// MobiFlight WASM module's DEFAULT command channel — exactly the transport
// MSFSBA's SimConnectManager.ExecuteCalculatorCode uses — and prints any
// MobiFlight.Response traffic. Read the resulting L:var back over the
// Coherent debugger (tools/coherent-eval.ps1) to close the loop.
//
// Usage:
//   dotnet run --project tools/A220CalcProbe -- "1 (>L:A22X Taxi Lights)"
//
// Builds standalone (NOT part of the solution), like tools/CDUTest.

using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;

namespace A220CalcProbe;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
public struct CommandData
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
    public string command;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
public struct ResponseData
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
    public string response;
}

static class Program
{
    enum CDA_ID { MF_COMMAND = 1000, MF_RESPONSE = 1001 }
    enum DEF_ID { MF_COMMAND_STRING = 2000, MF_RESPONSE_STRING = 2001 }
    enum REQ_ID { MF_RESPONSE_REQUEST = 3000 }

    static SimConnect _sc;
    static volatile bool _quit;

    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("usage: A220CalcProbe \"<rpn code>\" [\"<rpn code>\" ...]");
            return 2;
        }

        try
        {
            _sc = new SimConnect("A220CalcProbe", IntPtr.Zero, 0, null, 0);
            Console.WriteLine("[OK] Connected to SimConnect.");
        }
        catch (COMException ex)
        {
            Console.WriteLine($"[ERR] Could not connect to MSFS: {ex.Message}");
            return 1;
        }

        _sc.OnRecvException += (_, e) => Console.WriteLine($"[SC EXC] {(SIMCONNECT_EXCEPTION)e.dwException}");
        _sc.OnRecvClientData += OnClientData;
        _sc.OnRecvQuit += (_, _) => _quit = true;

        _sc.MapClientDataNameToID("MobiFlight.Command", CDA_ID.MF_COMMAND);
        _sc.MapClientDataNameToID("MobiFlight.Response", CDA_ID.MF_RESPONSE);
        _sc.AddToClientDataDefinition(DEF_ID.MF_COMMAND_STRING, 0, 1024, 0, 0);
        _sc.AddToClientDataDefinition(DEF_ID.MF_RESPONSE_STRING, 0, 1024, 0, 0);
        _sc.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, ResponseData>(DEF_ID.MF_RESPONSE_STRING);
        _sc.RequestClientData(CDA_ID.MF_RESPONSE, REQ_ID.MF_RESPONSE_REQUEST, DEF_ID.MF_RESPONSE_STRING,
            SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);

        var pump = new Thread(() => { while (!_quit) { try { _sc?.ReceiveMessage(); } catch { } Thread.Sleep(15); } })
            { IsBackground = true };
        pump.Start();
        Thread.Sleep(400);

        foreach (var code in args)
        {
            // "RAW:<cmd>" sends a bare MobiFlight command (e.g. RAW:MF.LVars.List);
            // anything else is calculator code via MF.SimVars.Set.
            string command = code.StartsWith("RAW:") ? code.Substring(4) : "MF.SimVars.Set." + code;
            var cmd = new CommandData { command = command };
            _sc.SetClientData(CDA_ID.MF_COMMAND, DEF_ID.MF_COMMAND_STRING,
                SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT, 0, cmd);
            Console.WriteLine($"[SENT] {command}");
            Thread.Sleep(600);
        }

        Thread.Sleep(8000); // drain responses (LVars.List streams many messages)
        _quit = true;
        Thread.Sleep(100);
        _sc?.Dispose();
        return 0;
    }

    static void OnClientData(SimConnect sc, SIMCONNECT_RECV_CLIENT_DATA data)
    {
        if (data.dwRequestID == (uint)REQ_ID.MF_RESPONSE_REQUEST && data.dwData?.Length > 0
            && data.dwData[0] is ResponseData rd && !string.IsNullOrWhiteSpace(rd.response))
        {
            Console.WriteLine($"[MF RESP] {rd.response}");
        }
    }
}
