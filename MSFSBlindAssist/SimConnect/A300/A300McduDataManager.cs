using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.A300;

/// <summary>One MCDU area as it arrives: 1008 raw bytes (<see cref="A300McduText"/>).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = A300McduText.DataSize)]
public struct A300McduRaw
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = A300McduText.DataSize)]
    public byte[] Bytes;
}

/// <summary>
/// Reads the iniBuilds A300's two MCDU screens as text from the SimConnect client data areas the
/// aircraft publishes, <c>iniAirbusMCDU_1</c> and <c>iniAirbusMCDU_2</c> (docs/a300.md). The
/// aircraft publishes them only while its MCDU export option (<c>L:INI_MCDU_OPTION</c>, the
/// tablet's External Hardware setting) is 1, which the MCDU window switches on when it opens and asks for
/// again on every poll while it is open (<see cref="MSFSBlindAssist.Aircraft.IniA300Definition.EnsureMcduExport"/>).
///
/// The lifecycle is the MD-11 MCDU manager's, for the same reasons: ONE manager per SimConnect
/// connection (a second MapClientDataNameToID of the same name on one connection is DUPLICATE_ID),
/// a registration that resumes after the last step that succeeded, and for each MCDU an ON_SET +
/// CHANGED subscription PLUS a one-time snapshot on its OWN request id. The subscription is handed
/// only the writes that follow it, and the aircraft writes the area only when the screen changes, so
/// without the snapshot a window opened on a still page shows nothing.
/// </summary>
public sealed class A300McduDataManager : IDisposable
{
    // Namespaced ids ('A3'), well away from the PMDG and MD-11 managers' ranges.
    private enum ClientDataId : uint { Captain = 0x41330001, FirstOfficer = 0x41330002 }
    private enum DefineId : uint { Captain = 0x41330101, FirstOfficer = 0x41330102 }
    private enum RequestId : uint
    {
        Captain = 0x41330201, FirstOfficer = 0x41330202,
        CaptainSnapshot = 0x41330211, FirstOfficerSnapshot = 0x41330212,
    }

    private static readonly A300McduUnit[] Units = { A300McduUnit.Captain, A300McduUnit.FirstOfficer };

    private readonly Microsoft.FlightSimulator.SimConnect.SimConnect _simConnect;
    private readonly A300McduScreen?[] _screens = new A300McduScreen?[3];
    private readonly object _lock = new();
    private int _stepsDone;

    public A300McduDataManager(Microsoft.FlightSimulator.SimConnect.SimConnect simConnect)
    {
        _simConnect = simConnect;
    }

    /// <summary>Test seam: no SimConnect handle; only <see cref="Deliver"/>, <see cref="GetScreen"/> and
    /// <see cref="Reset"/> are usable. Must not mention <c>_simConnect</c> (the test process cannot load
    /// the SimConnect wrapper; see the MD-11 manager's seam).</summary>
#pragma warning disable CS8618
    internal A300McduDataManager()
    {
    }
#pragma warning restore CS8618

    public bool IsBoundTo(Microsoft.FlightSimulator.SimConnect.SimConnect handle) =>
        handle != null && ReferenceEquals(_simConnect, handle);

    public A300McduScreen? GetScreen(A300McduUnit unit)
    {
        lock (_lock) return _screens[(int)unit];
    }

    private const int StepCount = 6;

    /// <summary>Maps both areas and defines both reads. A no-op once complete; a call that fails part-way
    /// resumes at the step that failed on the next A300 load on this connection.</summary>
    public void Register()
    {
        if (_stepsDone >= StepCount)
            return;
        try
        {
            while (_stepsDone < StepCount)
            {
                RunStep(_stepsDone);
                _stepsDone++;
            }
            Log.Info("A300", $"MCDU client data areas registered ({A300McduText.DataSize} bytes x2).");
        }
        catch (Exception ex)
        {
            Log.Error("A300", $"Failed to register the MCDU client data areas at step {_stepsDone}: {ex.Message}");
        }
    }

    private void RunStep(int step)
    {
        switch (step)
        {
            case 0: _simConnect.MapClientDataNameToID(A300McduText.AreaName(A300McduUnit.Captain), ClientDataId.Captain); break;
            case 1: _simConnect.MapClientDataNameToID(A300McduText.AreaName(A300McduUnit.FirstOfficer), ClientDataId.FirstOfficer); break;
            case 2: _simConnect.AddToClientDataDefinition(DefineId.Captain, 0, A300McduText.DataSize, 0, 0); break;
            case 3: _simConnect.AddToClientDataDefinition(DefineId.FirstOfficer, 0, A300McduText.DataSize, 0, 0); break;
            case 4: _simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, A300McduRaw>(DefineId.Captain); break;
            case 5: _simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, A300McduRaw>(DefineId.FirstOfficer); break;
        }
    }

    /// <summary>Subscribes to both MCDUs and reads each once for what is on it now. Subscription first,
    /// snapshot second, so no write can fall between them; a repeat is dropped by content.</summary>
    public void RequestAll()
    {
        if (_stepsDone < StepCount)
            return;
        foreach (var unit in Units)
        {
            var (area, define, subscription, snapshot) = Ids(unit);
            Request(unit, area, subscription, define, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.CHANGED);
            Request(unit, area, snapshot, define, SIMCONNECT_CLIENT_DATA_PERIOD.ONCE, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT);
        }
    }

    private static (ClientDataId Area, DefineId Define, RequestId Subscription, RequestId Snapshot) Ids(A300McduUnit unit) =>
        unit == A300McduUnit.Captain
            ? (ClientDataId.Captain, DefineId.Captain, RequestId.Captain, RequestId.CaptainSnapshot)
            : (ClientDataId.FirstOfficer, DefineId.FirstOfficer, RequestId.FirstOfficer, RequestId.FirstOfficerSnapshot);

    private void Request(A300McduUnit unit, ClientDataId area, RequestId request, DefineId define,
        SIMCONNECT_CLIENT_DATA_PERIOD period, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG flag)
    {
        try
        {
            _simConnect.RequestClientData(area, request, define, period, flag, 0, 0, 0);
        }
        catch (Exception ex)
        {
            Log.Debug("A300", $"MCDU RequestClientData({unit}, {period}) failed: {ex.Message}");
        }
    }

    /// <summary>The unit a delivery belongs to, or null when the request id is not one of ours.</summary>
    public static A300McduUnit? UnitForRequest(uint requestId) => (RequestId)requestId switch
    {
        RequestId.Captain or RequestId.CaptainSnapshot => A300McduUnit.Captain,
        RequestId.FirstOfficer or RequestId.FirstOfficerSnapshot => A300McduUnit.FirstOfficer,
        _ => null,
    };

    /// <summary>Handles a client data delivery; true when it was one of ours.</summary>
    public bool HandleClientData(SIMCONNECT_RECV_CLIENT_DATA data)
    {
        var unit = UnitForRequest(data.dwRequestID);
        if (unit == null)
            return false;
        if (data.dwData is { Length: > 0 } && data.dwData[0] is A300McduRaw raw && raw.Bytes != null)
            Deliver(unit.Value, raw.Bytes);
        return true;
    }

    /// <summary>Decodes and caches one delivery; an identical repeat keeps the old object.</summary>
    internal void Deliver(A300McduUnit unit, byte[] bytes)
    {
        var screen = A300McduText.Decode(unit, bytes);
        if (screen == null)
            return;
        bool first;
        lock (_lock)
        {
            var previous = _screens[(int)unit];
            first = previous == null;
            if (previous == null || !previous.SameContent(screen))
                _screens[(int)unit] = screen;
        }
        if (first)
            Log.Info("A300", $"MCDU {unit} first delivery: blank={screen.IsBlank}, title='{screen.Title.Trim()}'.");
    }

    /// <summary>Forgets the cached screens (the A300 loaded again on this connection).</summary>
    public void Reset()
    {
        lock (_lock) Array.Clear(_screens);
    }

    public void Dispose() => Reset();
}
