using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Bridges PMDG 777X Client Data Areas with the app's variable system.
/// Owned by SimConnectManager; used by PMDG777Definition to read cockpit
/// state and send control events.
/// </summary>
public class PMDG777DataManager : IDisposable
{
    private static readonly FieldInfo[] s_dataFields =
        typeof(PMDG777XDataStruct).GetFields(BindingFlags.Public | BindingFlags.Instance);
    // ------------------------------------------------------------------
    // Local enum IDs — SimConnect accepts any Enum type for these calls,
    // so we define our own enums rather than casting raw uints.
    // ------------------------------------------------------------------

    private enum PMDG_CLIENT_DATA_ID : uint
    {
        Data    = 0x504D4447,
        Control = 0x504D4449,
        CDU_0   = 0x4E477835,
        CDU_1   = 0x4E477836,
        CDU_2   = 0x4E477837,
    }

    private enum PMDG_DATA_DEFINITION_ID : uint
    {
        Data    = 0x504D4448,
        Control = 0x504D444A,
        CDU_0   = 0x4E477838,
        CDU_1   = 0x4E477839,
        CDU_2   = 0x4E47783A,
    }

    private enum PMDG_DATA_REQUEST_ID : uint
    {
        Data  = 50000,
        CDU_0 = 50001,
        CDU_1 = 50002,
        CDU_2 = 50003,
    }

    // ------------------------------------------------------------------
    // Other constants
    // ------------------------------------------------------------------
    private const uint THIRD_PARTY_EVENT_ID_MIN    = 0x00011000; // 69632

    // CDU dimensions
    private const int CDU_COLS = 24;
    private const int CDU_ROWS = 14;

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------
    private Microsoft.FlightSimulator.SimConnect.SimConnect? _simConnect;
    private MobiFlightWasmModule? _mobiFlightWasm;

    private PMDG777XDataStruct _lastDataSnapshot;
    private bool _hasSnapshot;

    private readonly PMDG777CDUScreen?[] _lastCDUScreen = new PMDG777CDUScreen?[3];
    private System.Windows.Forms.Timer? _pollTimer;

    // Debug counters

    // ------------------------------------------------------------------
    // Events
    // ------------------------------------------------------------------
    public event EventHandler<PMDGVarUpdateEventArgs>? VariableChanged;

    // ------------------------------------------------------------------
    // Initialization
    // ------------------------------------------------------------------

    /// <summary>
    /// Stores references, registers Client Data Areas, and starts polling.
    /// </summary>
    public void Initialize(
        Microsoft.FlightSimulator.SimConnect.SimConnect simConnect,
        MobiFlightWasmModule mobiFlightWasm)
    {
        _simConnect     = simConnect;
        _mobiFlightWasm = mobiFlightWasm;

        try
        {
            RegisterClientDataAreas();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PMDG777DataManager] RegisterClientDataAreas failed: {ex.Message}");
        }

        _pollTimer = new System.Windows.Forms.Timer();
        _pollTimer.Interval = 1000;
        _pollTimer.Tick += PollTimer_Tick;
        _pollTimer.Start();

        System.Diagnostics.Debug.WriteLine($"[PMDG777DataManager] Initialized.");
    }

    // ------------------------------------------------------------------
    // Client Data Area registration
    // ------------------------------------------------------------------

    private void RegisterClientDataAreas()
    {
        if (_simConnect == null) return;

        // ---- Map names to IDs ----
        _simConnect.MapClientDataNameToID("PMDG_777X_Data",    PMDG_CLIENT_DATA_ID.Data);
        _simConnect.MapClientDataNameToID("PMDG_777X_Control", PMDG_CLIENT_DATA_ID.Control);
        _simConnect.MapClientDataNameToID("PMDG_777X_CDU_0",   PMDG_CLIENT_DATA_ID.CDU_0);
        _simConnect.MapClientDataNameToID("PMDG_777X_CDU_1",   PMDG_CLIENT_DATA_ID.CDU_1);
        _simConnect.MapClientDataNameToID("PMDG_777X_CDU_2",   PMDG_CLIENT_DATA_ID.CDU_2);

        // ---- AddToClientDataDefinition (offset 0, full struct size) ----
        uint dataSize    = (uint)Marshal.SizeOf<PMDG777XDataStruct>();
        uint controlSize = (uint)Marshal.SizeOf<PMDG777Control>();
        uint cduSize     = (uint)Marshal.SizeOf<PMDG777CDUScreen>();

        _simConnect.AddToClientDataDefinition(
            PMDG_DATA_DEFINITION_ID.Data,    0, dataSize,    0, 0);
        _simConnect.AddToClientDataDefinition(
            PMDG_DATA_DEFINITION_ID.Control, 0, controlSize, 0, 0);
        _simConnect.AddToClientDataDefinition(
            PMDG_DATA_DEFINITION_ID.CDU_0,   0, cduSize,     0, 0);
        _simConnect.AddToClientDataDefinition(
            PMDG_DATA_DEFINITION_ID.CDU_1,   0, cduSize,     0, 0);
        _simConnect.AddToClientDataDefinition(
            PMDG_DATA_DEFINITION_ID.CDU_2,   0, cduSize,     0, 0);

        // ---- RegisterStruct ----
        _simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDG777XDataStruct>(
            PMDG_DATA_DEFINITION_ID.Data);
        _simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDG777Control>(
            PMDG_DATA_DEFINITION_ID.Control);
        _simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDG777CDUScreen>(
            PMDG_DATA_DEFINITION_ID.CDU_0);
        _simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDG777CDUScreen>(
            PMDG_DATA_DEFINITION_ID.CDU_1);
        _simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDG777CDUScreen>(
            PMDG_DATA_DEFINITION_ID.CDU_2);

        System.Diagnostics.Debug.WriteLine("[PMDG777DataManager] Client data areas registered.");
    }

    // ------------------------------------------------------------------
    // Polling
    // ------------------------------------------------------------------

    private void PollTimer_Tick(object? sender, EventArgs e) => RequestData();

    /// <summary>
    /// Issues a one-shot request for the PMDG_777X_Data CDA.
    /// </summary>
    public void RequestData()
    {
        if (_simConnect == null) return;

        try
        {
            _simConnect.RequestClientData(
                PMDG_CLIENT_DATA_ID.Data,
                PMDG_DATA_REQUEST_ID.Data,
                PMDG_DATA_DEFINITION_ID.Data,
                SIMCONNECT_CLIENT_DATA_PERIOD.ONCE,
                SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PMDG777DataManager] RequestData failed: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Incoming client data dispatch (called by SimConnectManager)
    // ------------------------------------------------------------------

    /// <summary>
    /// Routes SIMCONNECT_RECV_CLIENT_DATA to the correct handler based on request ID.
    /// </summary>
    public void ProcessClientData(SIMCONNECT_RECV_CLIENT_DATA data)
    {
        try
        {
            switch ((PMDG_DATA_REQUEST_ID)data.dwRequestID)
            {
                case PMDG_DATA_REQUEST_ID.Data:
                {
                    var newData = (PMDG777XDataStruct)data.dwData[0];
                    DetectAndRaiseChanges(newData);
                    _lastDataSnapshot = newData;
                    _hasSnapshot      = true;
                    break;
                }
                case PMDG_DATA_REQUEST_ID.CDU_0:
                    _lastCDUScreen[0] = (PMDG777CDUScreen)data.dwData[0];
                    break;
                case PMDG_DATA_REQUEST_ID.CDU_1:
                    _lastCDUScreen[1] = (PMDG777CDUScreen)data.dwData[0];
                    break;
                case PMDG_DATA_REQUEST_ID.CDU_2:
                    _lastCDUScreen[2] = (PMDG777CDUScreen)data.dwData[0];
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PMDG777DataManager] ProcessClientData error: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Change detection via reflection
    // ------------------------------------------------------------------

    private void DetectAndRaiseChanges(PMDG777XDataStruct newData)
    {
        if (!_hasSnapshot)
        {
            return;
        }

        int changeCount = 0;
        foreach (var field in s_dataFields)
        {
            object? oldVal = field.GetValue(_lastDataSnapshot);
            object? newVal = field.GetValue(newData);

            if (field.FieldType.IsArray)
            {
                CompareArrayField(field.Name, oldVal, newVal);
            }
            else if (!Equals(oldVal, newVal))
            {
                double newDouble = ToDouble(newVal);
                double oldDouble = ToDouble(oldVal);
                changeCount++;
                RaiseVariableChanged(field.Name, newDouble);
            }
        }

    }

    private void RaiseAllFields(PMDG777XDataStruct data)
    {
        foreach (var field in s_dataFields)
        {
            object? val = field.GetValue(data);

            if (field.FieldType.IsArray && val is Array arr)
            {
                for (int i = 0; i < arr.Length; i++)
                    RaiseVariableChanged($"{field.Name}_{i}", ToDouble(arr.GetValue(i)));
            }
            else
            {
                RaiseVariableChanged(field.Name, ToDouble(val));
            }
        }
    }

    private void CompareArrayField(string fieldName, object? oldVal, object? newVal)
    {
        if (oldVal is not Array oldArr || newVal is not Array newArr) return;

        int len = Math.Min(oldArr.Length, newArr.Length);
        for (int i = 0; i < len; i++)
        {
            object? ov = oldArr.GetValue(i);
            object? nv = newArr.GetValue(i);
            if (!Equals(ov, nv))
                RaiseVariableChanged($"{fieldName}_{i}", ToDouble(nv));
        }
    }

    private void RaiseVariableChanged(string fieldName, double value) =>
        VariableChanged?.Invoke(this, new PMDGVarUpdateEventArgs
        {
            FieldName = fieldName,
            Value     = value
        });

    private static double ToDouble(object? val) => val switch
    {
        bool   b => b ? 1.0 : 0.0,
        byte   b => (double)b,
        sbyte  s => (double)s,
        short  s => (double)s,
        ushort u => (double)u,
        int    i => (double)i,
        uint   u => (double)u,
        long   l => (double)l,
        ulong  u => (double)u,
        float  f => (double)f,
        double d => d,
        _        => 0.0
    };

    // ------------------------------------------------------------------
    // Field value accessor
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the current value of a data field by name.
    /// Supports array-index suffix, e.g. "ELEC_Gen_Sw_ON_0".
    /// Returns 0 if the field is unknown or no snapshot has arrived yet.
    /// </summary>
    public double GetFieldValue(string fieldName)
    {
        if (!_hasSnapshot)
        {
            return 0.0;
        }

        // Plain field
        var field = typeof(PMDG777XDataStruct)
            .GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);

        if (field != null)
        {
            return ToDouble(field.GetValue(_lastDataSnapshot));
        }

        // Array index suffix: "FieldName_N"
        int lastUnderscore = fieldName.LastIndexOf('_');
        if (lastUnderscore > 0 && int.TryParse(fieldName[(lastUnderscore + 1)..], out int index))
        {
            string baseName = fieldName[..lastUnderscore];
            var baseField = typeof(PMDG777XDataStruct)
                .GetField(baseName, BindingFlags.Public | BindingFlags.Instance);

            if (baseField?.GetValue(_lastDataSnapshot) is Array arr && index < arr.Length)
                return ToDouble(arr.GetValue(index));
        }

        System.Diagnostics.Debug.WriteLine(
            $"[PMDG777DataManager] GetFieldValue: unknown field '{fieldName}'");
        return 0.0;
    }

    // ------------------------------------------------------------------
    // Event dispatch
    // ------------------------------------------------------------------

    /// <summary>
    /// Sends a PMDG control event via CDA (SetClientData) with direct position value.
    /// </summary>
    public void SendEvent(string eventName, uint eventId, int? parameter)
    {
        try
        {
            SendViaCDA(eventId, (uint)(parameter ?? 0));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PMDG777DataManager] SendEvent '{eventName}' failed: {ex.Message}");
        }
    }

    private void SendViaCDA(uint eventId, uint parameter)
    {
        if (_simConnect == null)
        {
            return;
        }

        var ctrl = new PMDG777Control { EventId = eventId, Parameter = parameter };

        _simConnect.SetClientData(
            PMDG_CLIENT_DATA_ID.Control,
            PMDG_DATA_DEFINITION_ID.Control,
            SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT,
            0,
            ctrl);

        System.Diagnostics.Debug.WriteLine(
            $"[PMDG777DataManager] SendViaCDA: eventId=0x{eventId:X} param={parameter}");
    }

    /// <summary>
    /// Guarded toggle: guard event → 150 ms delay → switch event → 150 ms delay → guard event.
    /// </summary>
    public async Task SendGuardedToggle(
        string guardEventName, uint guardEventId,
        string switchEventName, uint switchEventId)
    {
        SendEvent(guardEventName,  guardEventId,  null);
        await Task.Delay(150);
        SendEvent(switchEventName, switchEventId, null);
        await Task.Delay(150);
        SendEvent(guardEventName,  guardEventId,  null);
    }

    // ------------------------------------------------------------------
    // CDU screen reading
    // ------------------------------------------------------------------

    /// <summary>
    /// Requests a one-shot CDU screen snapshot (cdu = 0, 1, or 2).
    /// The result is stored and retrievable via GetCDURows().
    /// </summary>
    public void RequestCDUScreen(int cdu)
    {
        if (_simConnect == null || cdu < 0 || cdu > 2) return;

        var (dataId, defId, reqId) = cdu switch
        {
            0 => (PMDG_CLIENT_DATA_ID.CDU_0, PMDG_DATA_DEFINITION_ID.CDU_0, PMDG_DATA_REQUEST_ID.CDU_0),
            1 => (PMDG_CLIENT_DATA_ID.CDU_1, PMDG_DATA_DEFINITION_ID.CDU_1, PMDG_DATA_REQUEST_ID.CDU_1),
            _ => (PMDG_CLIENT_DATA_ID.CDU_2, PMDG_DATA_DEFINITION_ID.CDU_2, PMDG_DATA_REQUEST_ID.CDU_2)
        };

        try
        {
            _simConnect.RequestClientData(
                dataId, reqId, defId,
                SIMCONNECT_CLIENT_DATA_PERIOD.ONCE,
                SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PMDG777DataManager] RequestCDUScreen({cdu}) failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns 14 text rows from the last received CDU screen for the given CDU index.
    /// Each row is CDU_COLS (24) characters wide.
    /// Returns null if no screen data has been received yet.
    /// Symbol map: 0xA1 → '&lt;', 0xA2 → '&gt;', 0x20–0x7E → literal char, else ' '.
    /// </summary>
    public string[]? GetCDURows(int cdu)
    {
        if (cdu < 0 || cdu > 2 || _lastCDUScreen[cdu] == null) return null;

        var screen = _lastCDUScreen[cdu]!.Value;
        if (!screen.Powered) return null;
        if (screen.Cells == null || screen.Cells.Length < CDU_COLS * CDU_ROWS) return null;

        var rows = new string[CDU_ROWS];
        for (int row = 0; row < CDU_ROWS; row++)
        {
            var sb = new System.Text.StringBuilder(CDU_COLS);
            for (int col = 0; col < CDU_COLS; col++)
            {
                byte sym = screen.Cells[col * CDU_ROWS + row].Symbol;
                char ch  = sym switch
                {
                    0xA1                   => '<',
                    0xA2                   => '>',
                    >= 0x20 and <= 0x7E    => (char)sym,
                    _                      => ' '
                };
                sb.Append(ch);
            }
            rows[row] = sb.ToString();
        }
        return rows;
    }

    public (string[] rows, byte[,] colors, byte[,] flags)? GetCDURowsWithColors(int cdu)
    {
        if (cdu < 0 || cdu > 2) return null;
        var screen = _lastCDUScreen[cdu];
        if (screen == null || !screen.Value.Powered) return null;

        var rows = new string[14];
        var colors = new byte[14, 24];
        var flags = new byte[14, 24];

        for (int row = 0; row < 14; row++)
        {
            var sb = new System.Text.StringBuilder(24);
            for (int col = 0; col < 24; col++)
            {
                var cell = screen.Value.Cells[col * 14 + row];
                byte sym = cell.Symbol;
                colors[row, col] = cell.Color;
                flags[row, col] = cell.Flags;

                if (sym == 0xA1) sb.Append('<');
                else if (sym == 0xA2) sb.Append('>');
                else if (sym >= 0x20 && sym <= 0x7E) sb.Append((char)sym);
                else sb.Append(' ');
            }
            rows[row] = sb.ToString();
        }

        return (rows, colors, flags);
    }

    // ------------------------------------------------------------------
    // IDisposable
    // ------------------------------------------------------------------

    public void Dispose()
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = null;
        System.Diagnostics.Debug.WriteLine("[PMDG777DataManager] Disposed.");
    }
}

// ------------------------------------------------------------------
// Event args
// ------------------------------------------------------------------

/// <summary>
/// Carries a single PMDG 777X variable change notification.
/// </summary>
public class PMDGVarUpdateEventArgs : EventArgs
{
    public string FieldName { get; set; } = string.Empty;
    public double Value     { get; set; }
}
