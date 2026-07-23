using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.IFly;

public class IFlyVariableChangedEventArgs : EventArgs
{
    public string FieldName { get; set; } = "";
    public double Value { get; set; }
    public bool IsInitialSnapshot { get; set; }
}

/// <summary>
/// Client for the official iFly 737 MAX SDK.
///
/// READ:  polls the named shared-memory block "iFly737MAX_SDK_FileMappingObject"
///        (struct ShareMemory737MAXSDK, pack(8) — offsets generated into
///        <see cref="IFlySdkOffsets"/>), optionally serialized by the v1.5 mutex
///        "iFly737MAX_SDK_Mutex". Fires <see cref="VariableChanged"/> for every
///        changed field (arrays flattened as "Name_i"), mirroring the PMDG
///        data-manager event shape so MainForm can bridge into OnSimVarUpdated.
///
/// WRITE: sends WM_COPYDATA command messages (registered message
///        "iFly737MAX_MSG_GAU", payload {int Command; double V1,V2,V3}) to the
///        iFly plugin window — "iFly Plugin - MSFS2024" (MSFS 2024) or
///        "iFly Plugin" (MSFS 2020). This is the official command channel with
///        absolute _SET semantics; no MobiFlight or SimConnect involvement.
///
/// The client is fully independent of SimConnect — it works whenever the sim
/// and the iFly plugin are running.
/// </summary>
public class IFlySdkClient : IDisposable
{
    private const string FileMappingName = "iFly737MAX_SDK_FileMappingObject";
    private const string MutexName = "iFly737MAX_SDK_Mutex";
    private const string MessageName = "iFly737MAX_MSG_GAU";
    private const int PollIntervalMs = 250;

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private Mutex? _sdkMutex;
    private System.Threading.Timer? _pollTimer;
    private SynchronizationContext? _syncContext;
    private byte[]? _lastData;
    private volatile bool _disposed;
    private volatile bool _connected;
    private volatile IFlySdkSnapshot? _snapshot;
    private int _pollBusy;

    // Staleness watchdog: a named section survives while ANY handle is open (ours),
    // so when the sim/plugin exits our reads keep "succeeding" against frozen data —
    // the exception path never fires. Tick18 advances every FS tick while the plugin
    // is alive; N consecutive polls without movement = the plugin is gone (or the sim
    // is paused — also correct to treat as not-live). On resume we re-seed silently
    // (initial-snapshot semantics), never replaying the gap as announcements.
    private const int StaleDisconnectPolls = 12; // ~3 s at 250 ms
    private int _staleCount;
    private bool _staleMode;
    private int _lastTick = int.MinValue;

    /// <summary>Runs an action on the captured UI SynchronizationContext (the def's
    /// background write sequences use this so announces never happen off-thread).</summary>
    public void RunOnUi(Action action) => Post(action);

    /// <summary>Fired on the UI thread for every changed field. Array elements use the "Name_i" key form.</summary>
    public event EventHandler<IFlyVariableChangedEventArgs>? VariableChanged;

    /// <summary>Fired on the UI thread after every poll in which anything changed (CDU screens included).</summary>
    public event EventHandler? SnapshotUpdated;

    /// <summary>Fired on the UI thread when the shared-memory connection is established or lost.</summary>
    public event EventHandler<bool>? ConnectionChanged;

    /// <summary>True once the shared memory has been opened and the plugin reports the MAX is running.</summary>
    public bool IsReady => _connected && !_staleMode && (_snapshot?.IsRunning ?? false);

    /// <summary>Latest polled state; null until the first successful poll.</summary>
    public IFlySdkSnapshot? Snapshot => _snapshot;

    public void Start()
    {
        if (_disposed) return;
        _syncContext = SynchronizationContext.Current;
        _pollTimer ??= new System.Threading.Timer(_ => Poll(), null, 0, PollIntervalMs);
    }

    private void Poll()
    {
        if (_disposed) return;
        if (Interlocked.Exchange(ref _pollBusy, 1) == 1) return;
        try
        {
            if (_accessor == null && !TryOpen())
            {
                if (_connected)
                {
                    _connected = false;
                    Post(() => ConnectionChanged?.Invoke(this, false));
                }
                return;
            }

            var data = new byte[IFlySdkOffsets.StructSize];
            bool gotMutex = false;
            try
            {
                if (_sdkMutex != null)
                {
                    try { gotMutex = _sdkMutex.WaitOne(50); }
                    catch (AbandonedMutexException) { gotMutex = true; }
                }
                _accessor!.ReadArray(0, data, 0, data.Length);
            }
            finally
            {
                if (gotMutex) _sdkMutex!.ReleaseMutex();
            }

            int tick = BitConverter.ToInt32(data, IFlySdkOffsets.Tick18);
            if (_staleMode)
            {
                if (tick == _lastTick) return; // still frozen — wait
                _staleMode = false;            // plugin resumed — fall through to a fresh silent seed
                Log.Info("ifly", "SDK shared memory resumed — re-seeding silently");
            }
            else if (_lastData != null && tick == _lastTick)
            {
                if (++_staleCount >= StaleDisconnectPolls)
                {
                    _staleMode = true;
                    _staleCount = 0;
                    _lastData = null;
                    lock (_lastSynthetic) _lastSynthetic.Clear();
                    _lastTick = tick;
                    if (_connected)
                    {
                        _connected = false;
                        Post(() => ConnectionChanged?.Invoke(this, false));
                    }
                    Log.Info("ifly", "SDK shared memory went stale (sim/plugin stopped or paused) — suspending until it resumes");
                    return;
                }
            }
            else
            {
                _staleCount = 0;
            }
            _lastTick = tick;

            bool first = _lastData == null;
            var previous = _lastData;
            _lastData = data;
            _snapshot = new IFlySdkSnapshot(data);

            if (!_connected)
            {
                _connected = true;
                Post(() => ConnectionChanged?.Invoke(this, true));
                Log.Info("ifly", "SDK shared memory connected");
            }

            if (first)
            {
                // Initial snapshot: fire every field so panel/monitor caches seed
                // with real state (announce-suppressed via IsInitialSnapshot).
                RaiseFieldEvents(null, data, isInitial: true);
                RaiseSyntheticEvents(isInitial: true);
                Post(() => SnapshotUpdated?.Invoke(this, EventArgs.Empty));
                return;
            }

            // Fast path: nothing changed at all this poll.
            if (previous.AsSpan().SequenceEqual(data)) return;

            RaiseFieldEvents(previous, data, isInitial: false);
            RaiseSyntheticEvents(isInitial: false);
            Post(() => SnapshotUpdated?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception)
        {
            // Mapping torn down (sim closed / aircraft unloaded) — drop and re-probe next poll.
            CloseMapping();
            if (_connected)
            {
                _connected = false;
                Post(() => ConnectionChanged?.Invoke(this, false));
            }
            Log.Warn("ifly", "SDK shared memory read failed — mapping closed, will re-probe");
        }
        finally
        {
            Interlocked.Exchange(ref _pollBusy, 0);
        }
    }

    /// <summary>Re-fires every field as an initial-snapshot event (used when a panel needs re-seeding).</summary>
    public void RefireAllFields()
    {
        var data = _lastData;
        if (data == null) return;
        RaiseFieldEvents(null, data, isInitial: true);
        KeyValuePair<string, double>[] synth;
        lock (_lastSynthetic) synth = [.. _lastSynthetic];
        Post(() =>
        {
            foreach (var (name, value) in synth)
                VariableChanged?.Invoke(this, new IFlyVariableChangedEventArgs { FieldName = name, Value = value, IsInitialSnapshot = true });
        });
    }

    private void RaiseFieldEvents(byte[]? previous, byte[] data, bool isInitial)
    {
        var changes = new List<(string Name, double Value)>();
        foreach (var f in IFlySdkFields.All)
        {
            for (int i = 0; i < f.Count; i++)
            {
                int off = f.Offset + i * f.Stride;
                double newVal = ReadField(data, off, f.Kind);
                if (previous != null)
                {
                    double oldVal = ReadField(previous, off, f.Kind);
                    if (oldVal == newVal) continue;
                }
                string key = f.Count > 1 ? $"{f.Name}_{i}" : f.Name;
                changes.Add((key, newVal));
            }
        }
        if (changes.Count == 0) return;
        Post(() =>
        {
            foreach (var (name, value) in changes)
            {
                VariableChanged?.Invoke(this, new IFlyVariableChangedEventArgs
                {
                    FieldName = name,
                    Value = value,
                    IsInitialSnapshot = isInitial,
                });
            }
        });
    }

    // ------------------------------------------------------------------
    // Synthetic composed fields. The MCP windows / transponder code / ELEC LED /
    // IRS display / fuel gauges are stored as PER-DIGIT statuses; a definition
    // keyed on one digit would go stale when only another digit changes. The
    // client therefore composes each window per poll and fires ONE synthetic
    // field when the composed value changes. Numeric windows carry the parsed
    // value (blank = -99999); text displays carry a change counter and the
    // definition renders the text from the live snapshot.
    // ------------------------------------------------------------------

    public const double SyntheticBlank = -99999;

    // Values <= SyntheticTextBase are hash-coded text (see RaiseSyntheticEvents'
    // SYN_MCP_SPEED handling below) — the number itself is meaningless, only
    // "did it change" matters, and the definition re-reads the live text to speak.
    public const double SyntheticTextBase = -100000;

    private readonly Dictionary<string, double> _lastSynthetic = new();

    private void RaiseSyntheticEvents(bool isInitial)
    {
        var snap = _snapshot;
        if (snap == null) return;
        var changes = new List<(string Name, double Value)>();

        void Set(string name, double value)
        {
            lock (_lastSynthetic)
            {
                if (_lastSynthetic.TryGetValue(name, out double old) && old == value && !isInitial) return;
                bool had = _lastSynthetic.ContainsKey(name);
                _lastSynthetic[name] = value;
                if (isInitial && had) return; // RefireAll handles re-seeding separately
            }
            changes.Add((name, value));
        }

        static double ParseNum(string s) =>
            s.Length > 0 && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
                ? v : SyntheticBlank;

        // Blank → SyntheticBlank. Parseable → the number. Non-blank but unparseable
        // (symbol text like "A250") → a text sentinel that still CHANGES when the text
        // changes, so the def announces the literal window text instead of lying
        // "blank, FMC managed". Values ≤ SyntheticTextBase are hash-coded text.
        string spdText = snap.McpSpeedText();
        Set("SYN_MCP_SPEED", snap.McpSpeedBlank() ? SyntheticBlank
            : ParseNum(spdText) is var pv && pv != SyntheticBlank ? pv
            : SyntheticTextBase - (StableHash(spdText) % 100000));
        Set("SYN_MCP_HEADING", ParseNum(snap.McpHeadingText()));
        Set("SYN_MCP_ALTITUDE", ParseNum(snap.McpAltitudeText()));
        Set("SYN_MCP_VS", ParseNum(snap.McpVerticalSpeedText()));
        Set("SYN_MCP_COURSE_1", ParseNum(snap.McpCourseText(0)));
        Set("SYN_MCP_COURSE_2", ParseNum(snap.McpCourseText(1)));
        // Squawk entry fills the window LEFT TO RIGHT one digit per keypress
        // (live-verified 2026-07-23), so a partial 1-3 digit entry parses as a
        // small number ("12__" -> 12) and would announce as a bogus "Squawk 0012".
        // Only a COMPLETE 4-digit window carries a real code; partials stay blank.
        Set("SYN_XPDR_CODE", snap.TransponderCodeDigitCount() == 4
            ? ParseNum(snap.TransponderCodeText()) : SyntheticBlank);
        Set("SYN_FUEL_QTY_L", ParseNum(snap.FuelQuantityText(0)));
        Set("SYN_FUEL_QTY_R", ParseNum(snap.FuelQuantityText(1)));
        Set("SYN_FUEL_QTY_C", ParseNum(snap.FuelQuantityText(2)));
        // Radio tuning panels 1-3: left window = active, right = standby.
        for (int rtp = 0; rtp < 3; rtp++)
        {
            Set($"SYN_RTP{rtp + 1}_ACTIVE", ParseNum(snap.RtpText(rtp, rightSide: false)));
            Set($"SYN_RTP{rtp + 1}_STANDBY", ParseNum(snap.RtpText(rtp, rightSide: true)));
        }
        // NAV control panels 1-2: left window = active, right = standby. Content
        // hash (not ParseNum) because the window carries a mode flag (ILS/VOR/GLS)
        // alongside the frequency; the definition renders from the live snapshot.
        for (int nav = 0; nav < 2; nav++)
        {
            Set($"SYN_NAV{nav + 1}_ACTIVE", StableHash(snap.NavWindowText(nav, 0)));
            Set($"SYN_NAV{nav + 1}_STANDBY", StableHash(snap.NavWindowText(nav, 1)));
        }
        // ADF control panel 1-2 (unit 0 = Left/ADF1, 1 = Right/ADF2): a plain
        // frequency, no mode flag, so ParseNum (blank -> SyntheticBlank) is the
        // right shape here — same as the RTP/MCP windows, not the NAV hash path.
        Set("SYN_ADF_L", ParseNum(snap.AdfText(0)));
        Set("SYN_ADF_R", ParseNum(snap.AdfText(1)));
        // Text displays: value is a content hash so equality tracks the text.
        Set("SYN_ELEC_LED", StableHash(snap.ElecLedLine(0) + "|" + snap.ElecLedLine(1)));
        Set("SYN_IRS_DISPLAY", StableHash(snap.IrsDisplayText()));

        if (changes.Count == 0) return;
        Post(() =>
        {
            foreach (var (name, value) in changes)
            {
                VariableChanged?.Invoke(this, new IFlyVariableChangedEventArgs
                {
                    FieldName = name,
                    Value = value,
                    IsInitialSnapshot = isInitial,
                });
            }
        });
    }

    private static double StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h;
        }
    }

    private static double ReadField(byte[] data, int offset, char kind) => kind switch
    {
        'I' => BitConverter.ToInt32(data, offset),
        'D' => BitConverter.ToDouble(data, offset),
        _ => data[offset],
    };

    private bool TryOpen()
    {
        if (_disposed) return false;
        try
        {
            _mmf = MemoryMappedFile.OpenExisting(FileMappingName, MemoryMappedFileRights.Read);
            _accessor = _mmf.CreateViewAccessor(0, IFlySdkOffsets.StructSize, MemoryMappedFileAccess.Read);
            try { _sdkMutex = Mutex.OpenExisting(MutexName); }
            catch { _sdkMutex = null; } // pre-v1.5 plugin: no mutex — reads are still safe (torn reads self-heal next poll)
            _lastData = null;
            return true;
        }
        catch
        {
            CloseMapping();
            return false;
        }
    }

    private void CloseMapping()
    {
        _accessor?.Dispose();
        _accessor = null;
        _mmf?.Dispose();
        _mmf = null;
        _sdkMutex?.Dispose();
        _sdkMutex = null;
        _lastData = null;
        _snapshot = null;
        lock (_lastSynthetic) _lastSynthetic.Clear();
        _lastTick = int.MinValue;
        _staleCount = 0;
    }

    private void Post(Action action)
    {
        if (_disposed) return;
        if (_syncContext != null) _syncContext.Post(_ => { if (!_disposed) action(); }, null);
        else action();
    }

    public void Dispose()
    {
        _disposed = true;
        _pollTimer?.Dispose();
        _pollTimer = null;
        CloseMapping();
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------------
    // Command channel (WM_COPYDATA to the iFly plugin window)
    // ------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct IFlyMessage
    {
        public int Command;
        public double Value1;
        public double Value2;
        public double Value3;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    private const uint WM_COPYDATA = 0x004A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, ref COPYDATASTRUCT lParam, uint flags, uint timeout, out IntPtr result);

    private static uint _registeredMessage;

    // Edge-triggered log gate: only speak on the transition into failure, never
    // once per send in a sustained-failure loop (commands are user- or 40-120 ms-paced).
    private static bool _lastSendFailed;

    private static IntPtr FindPluginWindow()
    {
        // MSFS 2024 plugin window first, then the MSFS 2020 title.
        var hwnd = FindWindowEx(IntPtr.Zero, IntPtr.Zero, null, "iFly Plugin - MSFS2024");
        if (hwnd == IntPtr.Zero)
            hwnd = FindWindowEx(IntPtr.Zero, IntPtr.Zero, null, "iFly Plugin");
        return hwnd;
    }

    /// <summary>
    /// Sends one command to the iFly plugin. Value semantics per key_command.h:
    /// Value1 = click sound on/off (we always send 1), Value2/Value3 = command-specific.
    /// Returns false when the plugin window can't be found or doesn't answer.
    /// </summary>
    public bool SendCommand(IFlyKeyCommand command, double value2 = 0, double value3 = 0, double value1 = 1)
    {
        if (_registeredMessage == 0)
            _registeredMessage = RegisterWindowMessage(MessageName);
        if (_registeredMessage == 0) return false;

        // Resolve the plugin window EVERY send: a cached HWND can be recycled by
        // Windows after a sim restart, and WM_COPYDATA to the recycled window
        // "succeeds" (delivered) while the aircraft hears nothing. A title scan is
        // microseconds; commands are user- or 40-120 ms-paced. (PR #163 finding M3.)
        var pluginWindow = FindPluginWindow();
        if (pluginWindow == IntPtr.Zero)
        {
            if (!_lastSendFailed) Log.Warn("ifly", "SendCommand: iFly plugin window not found");
            _lastSendFailed = true;
            return false;
        }

        var msg = new IFlyMessage { Command = (int)command, Value1 = value1, Value2 = value2, Value3 = value3 };
        int size = Marshal.SizeOf<IFlyMessage>();
        // The SDK sample declares cbData = sizeof(message)+2, so allocate the two
        // trailing bytes it claims — otherwise the kernel copy reads past our buffer.
        IntPtr buffer = Marshal.AllocHGlobal(size + 2);
        try
        {
            Marshal.StructureToPtr(msg, buffer, false);
            Marshal.WriteInt16(buffer, size, 0);
            var cds = new COPYDATASTRUCT
            {
                dwData = (IntPtr)_registeredMessage,
                cbData = size + 2, // the SDK sample sends sizeof(message)+2 — match it exactly
                lpData = buffer,
            };
            var ok = SendMessageTimeout(pluginWindow, WM_COPYDATA, IntPtr.Zero, ref cds, SMTO_ABORTIFHUNG, 1000, out _);
            if (ok == IntPtr.Zero)
            {
                // Window may have been recreated between the resolve above and this
                // send — re-resolve once and retry.
                pluginWindow = FindPluginWindow();
                if (pluginWindow != IntPtr.Zero)
                    ok = SendMessageTimeout(pluginWindow, WM_COPYDATA, IntPtr.Zero, ref cds, SMTO_ABORTIFHUNG, 1000, out _);
            }

            if (ok == IntPtr.Zero)
            {
                if (!_lastSendFailed) Log.Warn("ifly", $"SendCommand: no answer from the iFly plugin window for {command}");
                _lastSendFailed = true;
                return false;
            }

            _lastSendFailed = false;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
