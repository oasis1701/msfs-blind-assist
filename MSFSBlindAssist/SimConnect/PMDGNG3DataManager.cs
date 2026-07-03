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

    private PMDGNG3DataStruct _lastDataSnapshot;
    private bool _hasSnapshot;
    private SynchronizationContext? _syncContext;

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
        MobiFlightWasmModule? mobiFlightWasm)
    {
        _simConnect     = simConnect;
        // Initialize runs on the UI thread (it creates the WinForms poll timer
        // below); capture the context so RaiseFieldChanged can marshal a
        // pool-thread re-raise back onto it — VariableChanged consumers
        // (MainForm) do UI work.
        _syncContext    = SynchronizationContext.Current;

        try
        {
            RegisterClientDataAreas();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PMDGNG3DataManager] RegisterClientDataAreas failed: {ex.Message}");
        }

        // Restart the 1Hz polling. We tried ON_SET push-subscription per the
        // SDK sample, but the FLAG.CHANGED filter never delivers an initial
        // baseline snapshot — _hasSnapshot stays false until something changes,
        // which makes IsReady return false and the guarded-switch dispatcher
        // bail out with "Switch not ready". Polling guarantees a snapshot
        // within 1 second of startup. The "stale cache" risk that ON_SET was
        // meant to mitigate turned out to be PMDG-side state inconsistency,
        // not snapshot lag, so polling is fine.
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

    private void PollTimer_Tick(object? sender, EventArgs e)
    {
        // Suspend THIS manager's ambient 1 Hz poll while one of its own
        // closed-loop walks runs — the walk substitutes its own ~300 ms
        // PERIOD.ONCE requests, which keep the monitor cache fresh, and the
        // response-counting handshake in RequestFreshSnapshotAsync guarantees
        // each awaited snapshot postdates the walk's last click. (Instance
        // state, not static: a walk orphaned on a swapped-out manager must
        // never suspend the replacement manager's poll.)
        if (Volatile.Read(ref _walksInProgress) > 0) return;
        RequestData();
    }

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
            // Count only successfully-issued requests — the freshness handshake
            // (RequestFreshSnapshotAsync) pairs this with _dataResponsesSeen.
            Interlocked.Increment(ref _dataRequestsIssued);
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
                    // Wake a closed-loop walk awaiting a fresh snapshot — AFTER
                    // the snapshot fields update, so the awaiter's GetFieldValue
                    // read is guaranteed post-refresh. Response-counting keeps a
                    // STRAGGLER honest: a late response to an EARLIER request
                    // (sampled before the walk's last click) must not satisfy
                    // the handshake, so the TCS completes only once responses
                    // have caught up with every request issued up to and
                    // including the walk's own (responses arrive in request
                    // order on the one shared request ID). The clamp keeps a
                    // presumed-lost response that straggles in after a timeout
                    // resync from over-running the request count.
                    if (Interlocked.Read(ref _dataResponsesSeen) <
                        Interlocked.Read(ref _dataRequestsIssued))
                    {
                        Interlocked.Increment(ref _dataResponsesSeen);
                    }
                    if (Interlocked.Read(ref _dataResponsesSeen) >=
                        Interlocked.Read(ref _snapshotRequiredResponses))
                    {
                        _snapshotTcs?.TrySetResult(true);
                    }
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

    // Cache for derived-field values so we can detect change deltas
    // independently of PMDG's lying source fields. Keyed by the user-facing
    // varKey (e.g. "ELEC_GrdPwrSw"), value is the most recent derived bool.
    private readonly Dictionary<string, bool> _lastDerivedValues = new();

    // Local-state tracking for switches where PMDG exposes no reliable
    // per-switch signal (e.g. ELEC_APUGenSw_0/1 share a single annunciator
    // so we can't tell them apart from PMDG data alone). Updated when the
    // user dispatches, displayed as the effective state. Defaults to false
    // (cold-and-dark assumption). Keyed by varKey.
    private readonly Dictionary<string, bool> _localSwitchStates = new()
    {
        // APU GEN 1/2 removed — they're now two-button push pairs (no state
        // displayed), so no local tracking is needed. Engine GEN 1/2 remain
        // here as combos with local-state display.
        ["ELEC_GenSw_0"]    = false,
        ["ELEC_GenSw_1"]    = false,
    };

    /// <summary>
    /// Records that the user just dispatched a switch to a new state. The
    /// next snapshot's derived-override pass will publish the recorded state
    /// (rather than guessing from PMDG's data, which lies for these
    /// switches). Called from PMDG737Definition.HandleUIVariableSet right
    /// after the dispatch is sent.
    /// </summary>
    public void NotifyLocalSwitchState(string varKey, bool newState)
    {
        if (!_localSwitchStates.ContainsKey(varKey)) return;
        _localSwitchStates[varKey] = newState;
        // Fire a synthetic event immediately so the UI updates without
        // waiting for the next 1Hz snapshot.
        RaiseDerivedIfChanged(varKey, newState, isFirstSnapshot: false);
    }

    /// <summary>
    /// PMDG NG3 reports some switch-position bool fields (ELEC_GrdPwrSw,
    /// ELEC_GenSw, ELEC_APUGenSw) that don't reflect the actual bus
    /// connectivity — they read as "true" even when the source is not
    /// providing power. Verified against ELEC_BusPowered[] and the
    /// annunciator OFF lamps via the PMDGDispatchTester rig 2026-05-24.
    /// For these specific fields we OVERRIDE the natural snapshot value with
    /// a derived value computed from the truthful fields PMDG also exposes.
    /// Synthetic events fire AFTER the natural foreach in DetectAndRaiseChanges
    /// so they win in the per-frame cache.
    ///
    /// Derivation rules (display the EFFECTIVE bus-connected state, not the
    /// raw switch detent — PMDG doesn't reliably expose detent position):
    ///   - ELEC_GrdPwrSw   ⟵ ELEC_BusPowered[9] OR [10] (AC GROUND SVC) —
    ///                       these buses are only ever powered by GPU.
    ///   - ELEC_APUGenSw_X ⟵ (APU_Selector >= 1) AND
    ///                       (NOT ELEC_annunAPU_GEN_OFF_BUS).
    ///                       The annunciator lights when APU is running but
    ///                       gen is off-bus, so NOT-annun says "either APU
    ///                       off OR gen on-bus" — gating on APU running
    ///                       disambiguates to "gen on-bus". PMDG exposes a
    ///                       single shared annunciator so both APU gens
    ///                       display the same state.
    ///   - ELEC_GenSw_X    ⟵ NOT ELEC_annunGEN_BUS_OFF[X] —
    ///                       lamp lit ⟹ no gen on this bus ⟹ engine gen off.
    ///                       Lamp off ⟹ some gen (engine or APU) is on bus;
    ///                       if APU is the source the user will see APU GEN
    ///                       shown ON too, so this gives the user enough info
    ///                       to disambiguate by looking at both rows.
    /// </summary>
    private void RaiseDerivedFieldOverrides(PMDGNG3DataStruct newData, bool isFirstSnapshot)
    {
        // Ground Power and APU GEN 1/2 no longer have derived state — they're
        // exposed as stateless push-button pairs (e.g. "Ground Power On" /
        // "Ground Power Off"), so nothing to publish here.
        //
        // Engine GEN 1/2 still use locally-tracked state with cold-and-dark
        // default. Updated via NotifyLocalSwitchState from the dispatch path.
        foreach (var kvp in _localSwitchStates)
            RaiseDerivedIfChanged(kvp.Key, kvp.Value, isFirstSnapshot);

        // Engine fuel control levers (ENG_StartLever_0/1): derived from
        // FUEL_annunENG_VALVE_CLOSED[i]. The SDK header comment claims
        // "0: Closed  1: Open  2: In transit" but the FIELD'S ACTUAL
        // POLARITY IS INVERTED on PMDG NG3 — confirmed against live cockpit
        // state on 2026-05-24:
        //     N1=0 (engine 1 off, lever at CUTOFF) → field reads 1
        //     N1=21.6 (engine 2 running, lever at RUN) → field reads 0
        // The field is the annunciator lamp ("VALVE_CLOSED" warning) which
        // lights (1) when the valve IS closed — so 1 = closed (CUTOFF) and
        // 0 = open (RUN). The SDK comment misnames the bits.
        //   Empirical mapping:
        //     0 → Run    (valve open, no warning)
        //     1 → Cutoff (valve closed, warning lit)
        //     2 → In transit — treat as Cutoff (strict): "Run" only displayed
        //                       when valve fully opens. Brief flicker during
        //                       animation is preferable to false-positive Run.
        byte[] fuelValveClosed = newData.FUEL_annunENG_VALVE_CLOSED ?? new byte[2];
        bool lever0Run = fuelValveClosed.Length > 0 && fuelValveClosed[0] == 0;
        bool lever1Run = fuelValveClosed.Length > 1 && fuelValveClosed[1] == 0;
        RaiseDerivedIfChanged("ENG_StartLever_0", lever0Run, isFirstSnapshot);
        RaiseDerivedIfChanged("ENG_StartLever_1", lever1Run, isFirstSnapshot);
    }

    private void RaiseDerivedIfChanged(string fieldName, bool newVal, bool isFirstSnapshot)
    {
        bool fire = isFirstSnapshot;
        if (!_lastDerivedValues.TryGetValue(fieldName, out bool prev) || prev != newVal)
            fire = true;
        _lastDerivedValues[fieldName] = newVal;
        if (fire)
        {
            RaiseVariableChanged(fieldName, newVal ? 1.0 : 0.0, isFirstSnapshot);
        }
    }

    private void DetectAndRaiseChanges(PMDGNG3DataStruct newData)
    {
        // First snapshot: there's no prior snapshot to compare against, so
        // _lastDataSnapshot holds the default(struct) value (zeros for scalars,
        // nulls for array refs). We INTENTIONALLY proceed through the loop
        // anyway — scalar non-zero fields fire VariableChanged so the UI cache
        // (currentSimVarValues in MainForm) populates with the real cockpit
        // state on first arrival. CompareArrayField is null-safe below and
        // fires an event for every element on first call. Without this, the
        // panel combos default to OFF/position-0 and never update if the
        // value doesn't subsequently change (Battery=BAT, Ground Power=ON,
        // IRS_DisplaySelector=HDG/STS — all common steady-state values that
        // never change after sim load).
        bool isFirstSnapshot = !_hasSnapshot;

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
                    if (isFirstSnapshot || ArrayHasChanged(oldVal as Array, newVal as Array))
                    {
                        RaiseVariableChanged(field.Name, 0, isFirstSnapshot);
                    }
                }
                else
                {
                    CompareArrayField(field.Name, oldVal, newVal, fireAll: isFirstSnapshot, isInitialSnapshot: isFirstSnapshot);
                }
            }
            else if (isFirstSnapshot || !Equals(oldVal, newVal))
            {
                // Suppress PMDG's natural value for fields whose displayed
                // state is derived from elsewhere (see RaiseDerivedFieldOverrides).
                if (s_derivedOverrideFields.Contains(field.Name)) continue;
                double newDouble = ToDouble(newVal);
                RaiseVariableChanged(field.Name, newDouble, isFirstSnapshot);
            }
        }

        // Fire derived-value overrides for fields where PMDG's natural value
        // doesn't reflect actual bus connectivity.
        RaiseDerivedFieldOverrides(newData, isFirstSnapshot);
    }

    // Field NAMES (not varKeys) of scalar PMDG fields whose snapshot value
    // is unreliable — suppress natural events. Empty now that ELEC_GrdPwrSw
    // is exposed as a stateless push-button pair (its natural events would
    // be silently dropped by the field-to-key map anyway since varKey
    // ELEC_GrdPwrSw no longer exists, but explicit suppression saves a
    // round-trip).
    private static readonly HashSet<string> s_derivedOverrideFields = new();

    // Array fields whose per-element events should be suppressed.
    private static readonly HashSet<string> s_derivedOverrideArrayFields = new()
    {
        "ELEC_GenSw",
        "ELEC_APUGenSw",
    };

    private void CompareArrayField(string fieldName, object? oldVal, object? newVal, bool fireAll = false, bool isInitialSnapshot = false)
    {
        if (newVal is not Array newArr) return;
        // Suppress natural events for arrays whose per-element values are
        // derived elsewhere (see RaiseDerivedFieldOverrides).
        if (s_derivedOverrideArrayFields.Contains(fieldName)) return;
        Array? oldArr = oldVal as Array;
        int len = newArr.Length;

        for (int i = 0; i < len; i++)
        {
            object? nv = newArr.GetValue(i);
            object? ov = (oldArr != null && i < oldArr.Length) ? oldArr.GetValue(i) : null;
            if (fireAll || oldArr == null || !Equals(ov, nv))
                RaiseVariableChanged($"{fieldName}_{i}", ToDouble(nv), isInitialSnapshot);
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

    private void RaiseVariableChanged(string fieldName, double value, bool isInitialSnapshot = false) =>
        VariableChanged?.Invoke(this, new PMDGVarUpdateEventArgs
        {
            FieldName = fieldName,
            Value     = value,
            IsInitialSnapshot = isInitialSnapshot,
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

        // Derived-override fields: PMDG's raw bool lies for these (see
        // RaiseDerivedFieldOverrides). Return the cached derived value
        // instead so panel-open code that reads via this method gets the
        // truthful state matching the cockpit. Without this short-circuit,
        // the panel-refresh path at MainForm.cs:4640+ overwrites our
        // synthetic-event-populated cache with PMDG's lying value, and the
        // combo flips to ON.
        if (_lastDerivedValues.TryGetValue(fieldName, out bool derived))
        {
            return derived ? 1.0 : 0.0;
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

    private const uint MOUSE_FLAG_LEFTSINGLE  = 0x20000000;
    private const uint MOUSE_FLAG_RIGHTSINGLE = 0x80000000;
    private const uint MOUSE_FLAG_LEFTRELEASE  = 0x00020000;
    private const uint MOUSE_FLAG_RIGHTRELEASE = 0x00080000;
    private const int  CLICK_GAP_MS = 60;

    /// <summary>
    /// Sends a paired press-and-release sequence for a momentary spring-loaded
    /// toggle (e.g. the GRD POWER switch and APU / engine generator switches on
    /// the 737 NG). Per PMDG_NG3_ConnectionTest.cpp `toggleFlightDirector` —
    /// the documented convention for momentary press-to-toggle events is
    /// MOUSE_FLAG_LEFTSINGLE followed by MOUSE_FLAG_LEFTRELEASE.
    ///
    /// Direction matches click-walking convention: target=1 (up/ON) uses LEFT,
    /// target=0 (down/OFF) uses RIGHT. A bare LEFTSINGLE (no RELEASE) makes
    /// PMDG NG3 play the switch-click sound but never commit the state — the
    /// switch springs back to its prior value within a frame.
    /// </summary>
    public async Task SendMomentaryToggle(uint eventId, int targetPosition)
    {
        bool up = targetPosition != 0;
        uint pressFlag   = up ? MOUSE_FLAG_LEFTSINGLE  : MOUSE_FLAG_RIGHTSINGLE;
        uint releaseFlag = up ? MOUSE_FLAG_LEFTRELEASE : MOUSE_FLAG_RIGHTRELEASE;
        SendEventViaTransmitWithTarget(eventId, pressFlag);
        await Task.Delay(CLICK_GAP_MS);
        SendEventViaTransmitWithTarget(eventId, releaseFlag);
    }

    /// <summary>
    /// Walks the switch by sending mouse-click TransmitClientEvents one step
    /// at a time. ClkR (RIGHTSINGLE) for downward steps (current &gt; target),
    /// ClkL (LEFTSINGLE) for upward steps. Matches TFM's
    /// CalculateSwitchPosition(useClicks=true) convention — proven to work
    /// against the NG3 WASM module without explicit guard manipulation
    /// (PMDG NG3 lifts and lowers the cover internally when a guarded
    /// switch receives a click event).
    /// </summary>
    public async Task WalkSelectorViaClicks(uint eventId, int currentPosition, int targetPosition)
    {
        if (currentPosition == targetPosition) return;
        bool steppingDown = currentPosition > targetPosition;
        uint clickFlag = steppingDown ? MOUSE_FLAG_RIGHTSINGLE : MOUSE_FLAG_LEFTSINGLE;
        int steps = Math.Abs(targetPosition - currentPosition);
        for (int i = 0; i < steps; i++)
        {
            SendEventViaTransmitWithTarget(eventId, clickFlag);
            if (i < steps - 1) await Task.Delay(CLICK_GAP_MS);
        }
    }

    // ------------------------------------------------------------------
    // Fresh-snapshot handshake + walk-active state (closed-loop walks)
    // ------------------------------------------------------------------

    // Completed by ProcessClientData's Data case when a snapshot lands.
    // Swapped fresh by RequestFreshSnapshotAsync before each one-shot request.
    private volatile TaskCompletionSource<bool>? _snapshotTcs;

    // Count of in-flight closed-loop walks on THIS manager (instance state on
    // purpose: a walk orphaned on a disposed manager after an aircraft swap
    // must not suspend the replacement manager's ambient poll). Self-clearing
    // by construction: the walk's try/finally decrements on every exit path
    // (target reached, budget exhausted, snapshot timeout, exception).
    private int _walksInProgress;

    // Request/response bookkeeping for the freshness handshake. Data-CDA
    // responses carry no correlation beyond the shared request ID, but they
    // arrive in request order — so "responses seen >= requests issued at the
    // time OUR request went out, plus one" proves the applied snapshot
    // postdates our request (and therefore the click that preceded it).
    private long _dataRequestsIssued;
    private long _dataResponsesSeen;
    private long _snapshotRequiredResponses;

    private const int SNAPSHOT_TIMEOUT_MS = 1500;

    /// <summary>
    /// Requests a one-shot Data-CDA refresh and completes when a snapshot that
    /// POSTDATES this request has been applied (false on timeout / no
    /// SimConnect). The response-counting handshake (see ProcessClientData)
    /// rejects stragglers — a late response to an earlier request, sampled
    /// before the walk's last click, cannot satisfy the wait even when the
    /// ambient poll issued it just before the walk started. Serialized by the
    /// walk (single caller); the single-slot _snapshotTcs is not safe for
    /// concurrent callers, which is why this is private.
    /// </summary>
    private async Task<bool> RequestFreshSnapshotAsync()
    {
        if (_simConnect == null) return false;
        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _snapshotRequiredResponses,
            Interlocked.Read(ref _dataRequestsIssued) + 1);
        _snapshotTcs = tcs;
        RequestData();
        var done = await Task.WhenAny(tcs.Task, Task.Delay(SNAPSHOT_TIMEOUT_MS));
        if (done != tcs.Task)
        {
            // Timed out — presume the outstanding response(s) lost so one lost
            // response can't permanently starve every future handshake (the
            // required count only ever grows). If a presumed-lost response DOES
            // straggle in later, ProcessClientData's clamp absorbs it.
            Interlocked.Exchange(ref _dataResponsesSeen,
                Interlocked.Read(ref _dataRequestsIssued));
            return false;
        }
        return true;
    }

    // Closed-loop walk pacing: detented rotaries are far slower to accept clicks
    // than CLICK_GAP_MS. Snapshot freshness between clicks is handled separately
    // by the awaited RequestFreshSnapshotAsync — this gap only paces PMDG's
    // click acceptance.
    private const int CLOSED_LOOP_CLICK_GAP_MS = 300;
    // Attempt budget: the widest walked selector spans 4 detents; the headroom
    // absorbs dropped clicks and stale-read re-decisions.
    private const int CLOSED_LOOP_MAX_CLICKS = 12;

    /// <summary>
    /// Closed-loop click-walk for detented rotaries whose CDA position write AND
    /// transmit-with-target dispatch are both silent no-ops — live-probed
    /// 2026-07-03 on <c>EVT_TCAS_MODE</c> (the transponder mode selector), the
    /// only known member so far. Three deliberate differences from
    /// <see cref="WalkSelectorViaClicks"/>:
    ///   1. The click DIRECTION is inverted vs the TFM convention: RIGHTSINGLE
    ///      steps UP (toward higher positions, e.g. TA/RA) and LEFTSINGLE steps
    ///      DOWN (toward STBY) — both directions verified in-sim.
    ///   2. Every iteration AWAITS a fresh Data-CDA snapshot
    ///      (<see cref="RequestFreshSnapshotAsync"/>) before reading. The
    ///      ambient poll is only 1 Hz, so an unawaited re-read is stale most of
    ///      the time — and a stale read fires an extra click past the target
    ///      (overshoot, oscillation, budget exhaustion on middle detents).
    ///   3. PMDG drops detent clicks probabilistically (4 clicks at 80 ms moved
    ///      the selector only 3 detents, and even at 300 ms one of 4 was eaten);
    ///      the fresh re-read makes every dropped click self-correct.
    /// Returns the VERIFIED landed position (from the walk's own final fresh
    /// read — equal to the target on success, elsewhere on budget exhaustion),
    /// or null when it could not verify (manager not ready / snapshot timeout,
    /// i.e. the last cached value may predate an in-flight click). Callers own
    /// all announcement/UI semantics, including suppressing the per-detent
    /// monitor callouts for the walk's duration (the 737 def gates its
    /// XPDR_ModeSel case on its own walk-active flag).
    /// </summary>
    public async Task<int?> WalkSelectorClosedLoop(uint eventId, string fieldName, int targetPosition)
    {
        Interlocked.Increment(ref _walksInProgress);
        try
        {
            for (int i = 0; i < CLOSED_LOOP_MAX_CLICKS; i++)
            {
                if (!IsReady) return null;
                if (!await RequestFreshSnapshotAsync()) return null;
                int current = (int)Math.Round(GetFieldValue(fieldName));
                if (current == targetPosition) return current;
                SendEventViaTransmitWithTarget(eventId,
                    current < targetPosition ? MOUSE_FLAG_RIGHTSINGLE : MOUSE_FLAG_LEFTSINGLE);
                await Task.Delay(CLOSED_LOOP_CLICK_GAP_MS);
            }
            // Budget exhausted — one last fresh read gives the verified landed
            // position (needed: the 12th click just fired).
            if (!await RequestFreshSnapshotAsync()) return null;
            return (int)Math.Round(GetFieldValue(fieldName));
        }
        finally
        {
            Interlocked.Decrement(ref _walksInProgress);
        }
    }

    /// <summary>
    /// Re-raises <see cref="VariableChanged"/> for one field with its current
    /// cached value, marshalled to the UI thread. Used after a FAILED
    /// closed-loop walk: the walk suppressed the field's natural change events
    /// (delivered synchronously while its gate was up) and the value will not
    /// change again on its own, so this replays the landed state through the
    /// standard pipeline — MainForm re-syncs the panel combo and the monitor
    /// announces the real position as a background change.
    /// </summary>
    public void RaiseFieldChanged(string fieldName)
    {
        if (!_hasSnapshot) return;
        void Fire() => RaiseVariableChanged(fieldName, GetFieldValue(fieldName));
        if (_syncContext != null) _syncContext.Post(_ => Fire(), null);
        else Fire();
    }

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
        // Fail-fast for any closed-loop walk orphaned on this instance by an
        // aircraft swap: RequestFreshSnapshotAsync returns false immediately
        // instead of burning its full timeout against a connection whose
        // responses now route to the replacement manager.
        _simConnect = null;
        System.Diagnostics.Debug.WriteLine("[PMDGNG3DataManager] Disposed.");
    }
}
