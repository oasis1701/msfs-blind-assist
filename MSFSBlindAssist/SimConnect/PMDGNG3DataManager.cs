using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.FlightSimulator.SimConnect;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Bridges PMDG NG3 (737-800) Client Data Areas with the app's variable system.
/// Owned by SimConnectManager; used by PMDG737Definition to read cockpit
/// state and send control events.
///
/// Differences from the 777 manager:
///   - 2 CDU sides (Capt/FO) instead of 3 (Capt/FO/Observer).
///   - CDA names/IDs sourced from <see cref="PMDGNG3Constants"/>.
///   - Several fields in <see cref="PMDGNG3DataStruct"/> are fixed-size ASCII
///     byte buffers (display strings). These are exposed via the dedicated
///     <see cref="GetStringFieldValue"/> method and reported as a single
///     change event (not byte-by-byte) by the reflection-based change detector.
/// </summary>
public class PMDGNG3DataManager : IPMDGDataManager
{
    public string AircraftCode => "PMDG_737";
    public int CDUSideCount => 2;

    private static readonly FieldInfo[] s_dataFields =
        typeof(PMDGNG3DataStruct).GetFields(BindingFlags.Public | BindingFlags.Instance);

    // ------------------------------------------------------------------
    // Local enum IDs — these are OUR app's internal SimConnect identifiers
    // for the data and definition objects. They are NOT PMDG's IDs and MUST
    // NOT match the values in PMDG_NG3_SDK.h (which are the IDs PMDG itself
    // registers on its side). Using PMDG's own ID values here caused our
    // RequestClientData subscription to never receive data — the 777 manager
    // uses custom 0x504D44xx values for the same reason. The data-area
    // NAMES still match the SDK ("PMDG_NG3_Data" etc.); names are what
    // SimConnect routes by, IDs are per-client tracking handles.
    // ------------------------------------------------------------------

    private enum PMDG_CLIENT_DATA_ID : uint
    {
        Data    = 0x4E473730,   // "NG70"
        Control = 0x4E473731,   // "NG71"
        CDU_0   = 0x4E473732,   // "NG72"
        CDU_1   = 0x4E473733,   // "NG73"
    }

    private enum PMDG_DATA_DEFINITION_ID : uint
    {
        Data    = 0x4E473734,   // "NG74"
        Control = 0x4E473735,   // "NG75"
        CDU_0   = 0x4E473736,   // "NG76"
        CDU_1   = 0x4E473737,   // "NG77"
    }

    private enum PMDG_DATA_REQUEST_ID : uint
    {
        Data  = 51000,
        CDU_0 = 51001,
        CDU_1 = 51002,
    }

    // ------------------------------------------------------------------
    // Other constants
    // ------------------------------------------------------------------
    private const int CDU_COLS = PMDGNG3Constants.CDU_COLUMNS;
    private const int CDU_ROWS = PMDGNG3Constants.CDU_ROWS;

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------
    private Microsoft.FlightSimulator.SimConnect.SimConnect? _simConnect;
    private MobiFlightWasmModule? _mobiFlightWasm;

    private PMDGNG3DataStruct _lastDataSnapshot;
    private bool _hasSnapshot;

    /// <inheritdoc />
    public bool IsReady => _hasSnapshot;

    private readonly PMDGNG3CDUScreen?[] _lastCDUScreen = new PMDGNG3CDUScreen?[2];
    private System.Windows.Forms.Timer? _pollTimer;

    // ------------------------------------------------------------------
    // Events
    // ------------------------------------------------------------------
    public event EventHandler<PMDGVarUpdateEventArgs>? VariableChanged;

    // ------------------------------------------------------------------
    // Initialization
    // ------------------------------------------------------------------

    /// <summary>
    /// Stores references, registers Client Data Areas, and starts polling.
    /// MobiFlightWasmModule is accepted to satisfy the interface contract but
    /// NG3 uses CDA broadcast for everything and does not require it.
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
                $"[PMDGNG3DataManager] RegisterClientDataAreas failed: {ex.Message}");
        }

        _pollTimer = new System.Windows.Forms.Timer();
        _pollTimer.Interval = 1000;
        _pollTimer.Tick += PollTimer_Tick;
        _pollTimer.Start();

        System.Diagnostics.Debug.WriteLine($"[PMDGNG3DataManager] Initialized.");
    }

    // ------------------------------------------------------------------
    // Client Data Area registration
    // ------------------------------------------------------------------

    private void RegisterClientDataAreas()
    {
        if (_simConnect == null) return;

        // ---- Map names to IDs ----
        _simConnect.MapClientDataNameToID(PMDGNG3Constants.PMDG_NG3_DATA_NAME,    PMDG_CLIENT_DATA_ID.Data);
        _simConnect.MapClientDataNameToID(PMDGNG3Constants.PMDG_NG3_CONTROL_NAME, PMDG_CLIENT_DATA_ID.Control);
        _simConnect.MapClientDataNameToID(PMDGNG3Constants.PMDG_NG3_CDU_0_NAME,   PMDG_CLIENT_DATA_ID.CDU_0);
        _simConnect.MapClientDataNameToID(PMDGNG3Constants.PMDG_NG3_CDU_1_NAME,   PMDG_CLIENT_DATA_ID.CDU_1);

        // ---- AddToClientDataDefinition (offset 0, full struct size) ----
        uint dataSize    = (uint)Marshal.SizeOf<PMDGNG3DataStruct>();
        uint controlSize = (uint)Marshal.SizeOf<PMDGNG3Control>();
        uint cduSize     = (uint)Marshal.SizeOf<PMDGNG3CDUScreen>();

        _simConnect.AddToClientDataDefinition(
            PMDG_DATA_DEFINITION_ID.Data,    0, dataSize,    0, 0);
        _simConnect.AddToClientDataDefinition(
            PMDG_DATA_DEFINITION_ID.Control, 0, controlSize, 0, 0);
        _simConnect.AddToClientDataDefinition(
            PMDG_DATA_DEFINITION_ID.CDU_0,   0, cduSize,     0, 0);
        _simConnect.AddToClientDataDefinition(
            PMDG_DATA_DEFINITION_ID.CDU_1,   0, cduSize,     0, 0);

        // ---- RegisterStruct ----
        _simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDGNG3DataStruct>(
            PMDG_DATA_DEFINITION_ID.Data);
        _simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDGNG3Control>(
            PMDG_DATA_DEFINITION_ID.Control);
        _simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDGNG3CDUScreen>(
            PMDG_DATA_DEFINITION_ID.CDU_0);
        _simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PMDGNG3CDUScreen>(
            PMDG_DATA_DEFINITION_ID.CDU_1);

        System.Diagnostics.Debug.WriteLine("[PMDGNG3DataManager] Client data areas registered.");
    }

    // ------------------------------------------------------------------
    // Polling
    // ------------------------------------------------------------------

    private void PollTimer_Tick(object? sender, EventArgs e) => RequestData();

    /// <summary>
    /// Issues a one-shot request for the PMDG_NG3_Data CDA.
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
                $"[PMDGNG3DataManager] RequestData failed: {ex.Message}");
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
                    var newData = (PMDGNG3DataStruct)data.dwData[0];
                    DetectAndRaiseChanges(newData);
                    _lastDataSnapshot = newData;
                    _hasSnapshot      = true;
                    break;
                }
                case PMDG_DATA_REQUEST_ID.CDU_0:
                    _lastCDUScreen[0] = (PMDGNG3CDUScreen)data.dwData[0];
                    break;
                case PMDG_DATA_REQUEST_ID.CDU_1:
                    _lastCDUScreen[1] = (PMDGNG3CDUScreen)data.dwData[0];
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PMDGNG3DataManager] ProcessClientData error: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Change detection via reflection
    // ------------------------------------------------------------------

    private void DetectAndRaiseChanges(PMDGNG3DataStruct newData)
    {
        if (!_hasSnapshot)
        {
            return;
        }

        foreach (var field in s_dataFields)
        {
            object? oldVal = field.GetValue(_lastDataSnapshot);
            object? newVal = field.GetValue(newData);

            if (field.FieldType.IsArray)
            {
                // ASCII string fields: announce only once on change, do not
                // emit a separate event per byte (a 7-byte IRS display change
                // from "PPOS" to "WIND" should be ONE event, not seven).
                if (IsAsciiStringField(field.Name))
                {
                    if (ArrayHasChanged(oldVal as Array, newVal as Array))
                    {
                        RaiseVariableChanged(field.Name, 0);
                    }
                }
                else
                {
                    CompareArrayField(field.Name, oldVal, newVal);
                }
            }
            else if (!Equals(oldVal, newVal))
            {
                double newDouble = ToDouble(newVal);
                RaiseVariableChanged(field.Name, newDouble);
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

    private static bool ArrayHasChanged(Array? prev, Array? next)
    {
        if (prev == null || next == null) return prev != next;
        if (prev.Length != next.Length) return true;
        for (int i = 0; i < prev.Length; i++)
            if (!Equals(prev.GetValue(i), next.GetValue(i))) return true;
        return false;
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
    /// Supports array-index suffix, e.g. "MCP_Course_0".
    /// String fields (ASCII byte buffers) return 0 — callers should use
    /// <see cref="GetStringFieldValue"/> instead.
    /// Returns 0 if the field is unknown or no snapshot has arrived yet.
    /// </summary>
    public double GetFieldValue(string fieldName)
    {
        if (!_hasSnapshot)
        {
            return 0.0;
        }

        // Plain field
        var field = typeof(PMDGNG3DataStruct)
            .GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);

        if (field != null)
        {
            // ASCII string fields can't be coerced to a meaningful double.
            if (IsAsciiStringField(fieldName)) return 0.0;

            object? value = field.GetValue(_lastDataSnapshot);
            // Non-string byte[] arrays should be accessed via "_N" suffix below.
            if (value is Array) return 0.0;
            return ToDouble(value);
        }

        // Array index suffix: "FieldName_N"
        int lastUnderscore = fieldName.LastIndexOf('_');
        if (lastUnderscore > 0 && int.TryParse(fieldName[(lastUnderscore + 1)..], out int index))
        {
            string baseName = fieldName[..lastUnderscore];
            // Reject array-index access for ASCII string fields — they are
            // strings, not byte arrays from the caller's perspective.
            if (IsAsciiStringField(baseName)) return 0.0;

            var baseField = typeof(PMDGNG3DataStruct)
                .GetField(baseName, BindingFlags.Public | BindingFlags.Instance);

            if (baseField?.GetValue(_lastDataSnapshot) is Array arr && index < arr.Length)
                return ToDouble(arr.GetValue(index));
        }

        System.Diagnostics.Debug.WriteLine(
            $"[PMDGNG3DataManager] GetFieldValue: unknown field '{fieldName}'");
        return 0.0;
    }

    /// <summary>
    /// Read an ASCII-string field by name. Decodes the null-terminated byte buffer
    /// to a managed string. Returns null if the field doesn't exist or isn't a string field.
    /// Callers must cast IPMDGDataManager to PMDGNG3DataManager to use this method —
    /// string fields are NG3-specific.
    /// </summary>
    public string? GetStringFieldValue(string fieldName)
    {
        if (!_hasSnapshot) return null;
        if (!IsAsciiStringField(fieldName)) return null;
        var field = typeof(PMDGNG3DataStruct).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (field?.GetValue(_lastDataSnapshot) is not byte[] bytes) return null;
        int len = Array.IndexOf<byte>(bytes, 0);
        if (len < 0) len = bytes.Length;
        return Encoding.ASCII.GetString(bytes, 0, len);
    }

    private static bool IsAsciiStringField(string name) =>
        name is "IRS_DisplayLeft" or "IRS_DisplayRight"
             or "ELEC_MeterDisplayTop" or "ELEC_MeterDisplayBottom"
             or "AIR_DisplayFltAlt" or "AIR_DisplayLandAlt"
             or "FMC_flightNumber";

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
                $"[PMDGNG3DataManager] SendEvent '{eventName}' failed: {ex.Message}");
        }
    }

    private void SendViaCDA(uint eventId, uint parameter)
    {
        if (_simConnect == null)
        {
            return;
        }

        var ctrl = new PMDGNG3Control { EventId = eventId, Parameter = parameter };

        _simConnect.SetClientData(
            PMDG_CLIENT_DATA_ID.Control,
            PMDG_DATA_DEFINITION_ID.Control,
            SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT,
            0,
            ctrl);

        System.Diagnostics.Debug.WriteLine(
            $"[PMDGNG3DataManager] SendViaCDA: eventId=0x{eventId:X} param={parameter}");
    }

    /// <summary>
    /// Sends a PMDG event through the standard SimConnect TransmitClientEvent path
    /// with the event registered under the alias "#&lt;eventId&gt;". Used for absolute-position
    /// selectors (3+ detents) where PMDG's CDA handler steps one detent regardless of the
    /// supplied parameter. The standard path accepts the target position as dwData and
    /// PMDG's event router places the switch at that detent in one shot.
    /// </summary>
    public void SendEventViaTransmitWithTarget(uint eventId, uint targetPosition)
    {
        if (_simConnect == null) return;
        try
        {
            string aliasName = "#" + eventId;
            uint id;
            lock (_transmitLock)
            {
                if (!_transmitEventIds.ContainsKey(aliasName))
                {
                    uint mappedId = _nextTransmitEventId++;
                    _transmitEventIds[aliasName] = mappedId;
                    _simConnect.MapClientEventToSimEvent(
                        (TRANSMIT_EVENT_GROUP)mappedId, aliasName);
                }
                id = _transmitEventIds[aliasName];
            }
            _simConnect.TransmitClientEvent(
                SIMCONNECT_OBJECT_ID_USER,
                (TRANSMIT_EVENT_GROUP)id,
                targetPosition,
                TRANSMIT_GROUP_PRIORITY.HIGHEST,
                SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PMDGNG3DataManager] SendEventViaTransmitWithTarget eventId=0x{eventId:X} failed: {ex.Message}");
        }
    }

    // Local registry for events mapped through TransmitClientEvent. Distinct from
    // SimConnectManager.eventIds so we don't collide with global event-name semantics.
    // _nextTransmitEventId starts high enough to leave room for the standard ranges.
    private readonly Dictionary<string, uint> _transmitEventIds = new();
    private readonly object _transmitLock = new();
    private uint _nextTransmitEventId = 90000;
    private enum TRANSMIT_EVENT_GROUP : uint { }
    private enum TRANSMIT_GROUP_PRIORITY : uint { HIGHEST = 1 }
    private const uint SIMCONNECT_OBJECT_ID_USER = 0;

    /// <summary>
    /// Guarded set: open guard (param=1) → set switch (param=targetPosition) → close guard (param=0).
    /// 150 ms gaps so PMDG's frame loop processes each transition. The guard-close runs inside
    /// try/finally so a thrown mid-sequence does not strand the cover open on a safety-critical
    /// switch. Works for two-position guarded toggles AND multi-position guarded selectors —
    /// the switch event accepts the absolute target position via CDA in both cases.
    /// </summary>
    public async Task SendGuardedSet(
        string guardEventName, uint guardEventId,
        string switchEventName, uint switchEventId,
        int targetPosition)
    {
        SendEvent(guardEventName,  guardEventId,  1);
        await Task.Delay(150);
        try
        {
            SendEvent(switchEventName, switchEventId, targetPosition);
        }
        finally
        {
            await Task.Delay(150);
            SendEvent(guardEventName, guardEventId, 0);
        }
    }

    // ------------------------------------------------------------------
    // CDU screen reading
    // ------------------------------------------------------------------

    /// <summary>
    /// Requests a one-shot CDU screen snapshot (cdu = 0 Capt or 1 FO).
    /// The result is stored and retrievable via GetCDURows().
    /// </summary>
    public void RequestCDUScreen(int cdu)
    {
        if (_simConnect == null || cdu < 0 || cdu >= CDUSideCount) return;

        var (dataId, defId, reqId) = cdu switch
        {
            0 => (PMDG_CLIENT_DATA_ID.CDU_0, PMDG_DATA_DEFINITION_ID.CDU_0, PMDG_DATA_REQUEST_ID.CDU_0),
            _ => (PMDG_CLIENT_DATA_ID.CDU_1, PMDG_DATA_DEFINITION_ID.CDU_1, PMDG_DATA_REQUEST_ID.CDU_1),
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
                $"[PMDGNG3DataManager] RequestCDUScreen({cdu}) failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns 14 text rows from the last received CDU screen for the given CDU index.
    /// Each row is CDU_COLS (24) characters wide.
    /// Returns null if no screen data has been received yet.
    /// Symbol map: 0xA1 → '&lt;', 0xA2 → '&gt;', 0xA3 → '↑', 0xA4 → '↓',
    /// 0x20–0x7E → literal char, else ' '.
    /// </summary>
    public string[]? GetCDURows(int cdu)
    {
        if (cdu < 0 || cdu >= CDUSideCount || _lastCDUScreen[cdu] == null) return null;

        var screen = _lastCDUScreen[cdu]!.Value;
        if (!screen.Powered) return null;
        if (screen.Cells == null || screen.Cells.Length < CDU_COLS * CDU_ROWS) return null;

        var rows = new string[CDU_ROWS];
        for (int row = 0; row < CDU_ROWS; row++)
        {
            var sb = new StringBuilder(CDU_COLS);
            for (int col = 0; col < CDU_COLS; col++)
            {
                byte sym = screen.Cells[col * CDU_ROWS + row].Symbol;
                sb.Append(DecodeCellSymbol(sym));
            }
            rows[row] = sb.ToString();
        }
        return rows;
    }

    public (string[] rows, byte[,] colors, byte[,] flags)? GetCDURowsWithColors(int cdu)
    {
        if (cdu < 0 || cdu >= CDUSideCount) return null;
        var screen = _lastCDUScreen[cdu];
        if (screen == null || !screen.Value.Powered) return null;
        if (screen.Value.Cells == null || screen.Value.Cells.Length < CDU_COLS * CDU_ROWS) return null;

        var rows = new string[CDU_ROWS];
        var colors = new byte[CDU_ROWS, CDU_COLS];
        var flags = new byte[CDU_ROWS, CDU_COLS];

        for (int row = 0; row < CDU_ROWS; row++)
        {
            var sb = new StringBuilder(CDU_COLS);
            for (int col = 0; col < CDU_COLS; col++)
            {
                var cell = screen.Value.Cells[col * CDU_ROWS + row];
                colors[row, col] = cell.Color;
                flags[row, col] = cell.Flags;
                sb.Append(DecodeCellSymbol(cell.Symbol));
            }
            rows[row] = sb.ToString();
        }

        return (rows, colors, flags);
    }

    private static char DecodeCellSymbol(byte sym) => sym switch
    {
        0xA1                => '<',
        0xA2                => '>',
        0xA3                => '↑', // up arrow
        0xA4                => '↓', // down arrow
        >= 0x20 and <= 0x7E => (char)sym,
        _                   => ' '
    };

    // ------------------------------------------------------------------
    // IDisposable
    // ------------------------------------------------------------------

    public void Dispose()
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = null;
        VariableChanged = null;
        System.Diagnostics.Debug.WriteLine("[PMDGNG3DataManager] Disposed.");
    }
}
