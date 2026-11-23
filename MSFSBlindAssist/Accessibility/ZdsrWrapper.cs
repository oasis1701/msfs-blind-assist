using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Accessibility;

/// <summary>The five calls of ZDSR's client API (ZDSRAPI.txt in ZDSR's folder), behind an interface so the
/// wrapper's logic can be tested without ZDSR. Return codes: 0 success, 1 version mismatch, 2 "ZDSR is not running
/// or not licensed", 3 speaking (GetSpeakState) or no braille display (Braille), 4 not speaking, 8 command not
/// delivered, 9 separate channel not ready.</summary>
internal interface IZdsrApi
{
    string Location { get; }
    int InitTTS(int channelType, string? channelName, bool keyDownInterrupt);
    int Speak(string text, bool interrupt);
    int GetSpeakState();
    void StopSpeak();
    int Braille(string text, bool flashMessage);
}

/// <summary>
/// ZDSR (争渡读屏, Zhengdu Screen Reader) through its own client API, ZDSRAPI_x64.dll. Tolk has no ZDSR driver, so
/// before this a ZDSR user heard SAPI. ScreenReaderAnnouncer tries this after NVDA and before Tolk, in Screen
/// Reader mode only (ScreenReaderAnnouncer.ZdsrAllowed).
///
/// <para>The DLL is never shipped or copied: it is loaded from the folder of the running ZDSRMain.exe (ZDSR's docs
/// ask for that; older copies stop working when ZDSR updates, and ZDSR reads its ZDSRAPI.ini overrides from the
/// DLL's folder); an elevated BA accepts that folder only under Program Files (<see cref="MayLoadFrom"/>). It is
/// loaded once per process, only after ZDSRMain.exe has been seen running, and never freed: it
/// runs a thread of its own. Speech goes to ZDSR's own channel (InitTTS type 0, the reader's own voice, like the
/// NVDA controller), key presses interrupt it as they do a screen reader's own speech, and the interrupt flag of
/// each message is passed through unchanged. Every spoken message is also brailled as a flash message, as Tolk
/// brailles for JAWS.</para>
/// </summary>
public sealed class ZdsrWrapper
{
    internal const string ProcessName = "ZDSRMain";
    internal const string DllName = "ZDSRAPI_x64.dll";

    internal const int ResultSuccess = 0;
    internal const int ResultNotRunning = 2;
    internal const int StateSpeaking = 3;
    internal const int StateNotSpeaking = 4;
    /// <summary>Not a ZDSR code: what a call that threw is reported as.</summary>
    internal const int ResultCallFailed = -1;

    /// <summary>InitTTS's channel type 0: ZDSR's own channel.</summary>
    internal const int ZdsrChannel = 0;

    /// <summary>How long a running / not-running verdict is reused, so each announcement costs one Speak call
    /// rather than a state query too. A failed Speak resets it at once.</summary>
    internal const long VerdictCacheMs = 1000;
    /// <summary>How often, until ZDSR has been found once, the process list is checked for ZDSRMain.exe.</summary>
    internal const long LocateIntervalMs = 5000;
    /// <summary>After this long without an answer from ZDSR, the next check applies the channel settings again.
    /// The check only runs when something is announced, so ZDSR can close and come back between two announcements
    /// without ever being seen down; far shorter than a ZDSR restart, far longer than the gap between the checks
    /// of a busy stretch (one a second).</summary>
    internal const long ReinitAfterIdleMs = 3000;

    private readonly object _gate = new();
    private readonly Func<IZdsrApi?> _locate;
    private readonly Func<long> _clockMs;
    private readonly Func<bool> _processRunning;

    private IZdsrApi? _api;
    private long _nextLocateMs;
    private long _verdictUntilMs;
    private long _lastContactMs;
    private bool _running;
    private int _lastResult = int.MinValue;
    private int _lastInitResult = int.MinValue;

    public ZdsrWrapper() : this(ZdsrNativeApi.TryLocate, () => Environment.TickCount64, ZdsrNativeApi.IsZdsrProcessRunning) { }

    internal ZdsrWrapper(Func<IZdsrApi?> locate, Func<long> clockMs, Func<bool> processRunning)
    {
        _locate = locate;
        _clockMs = clockMs;
        _processRunning = processRunning;
    }

    /// <summary>True while ZDSR is running, licensed and answering its API.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                long now = _clockMs();
                if (_api == null)
                {
                    if (now < _nextLocateMs) return false;
                    _nextLocateMs = now + LocateIntervalMs;
                    _api = _locate();
                    if (_api == null) return false;
                }
                else if (now < _verdictUntilMs)
                {
                    return _running;
                }
                _verdictUntilMs = now + VerdictCacheMs;

                // (Re)apply the channel settings whenever ZDSR may have been restarted: when it is not known to be
                // up (the first check, and after it was seen closed), and when it has not answered for
                // ReinitAfterIdleMs (a restart nobody saw). ZDSRAPI.txt: InitTTS "can be called repeatedly".
                if (!_running || now - _lastContactMs >= ReinitAfterIdleMs)
                {
                    int init = Call(api => api.InitTTS(ZdsrChannel, null, true));
                    // Logged when it changes, not on every check: with ZDSR closed this runs once a second.
                    if (init != ResultSuccess && init != _lastInitResult)
                    {
                        Log.Debug("Accessibility", $"ZDSR InitTTS returned {Describe(init)}");
                    }
                    _lastInitResult = init;
                }

                int state = Call(api => api.GetSpeakState());
                Record(IsRunningState(state), state);
                if (_running) _lastContactMs = now;
                return _running;
            }
        }
    }

    /// <summary>Why ZDSR, although ZDSRMain.exe is running, is not being spoken through, as a sentence for the
    /// Settings status box; null when ZDSR speaks, is not running at all, or has not been checked yet (read
    /// <see cref="IsRunning"/> first).</summary>
    public string? UnavailableReason
    {
        get
        {
            lock (_gate)
            {
                if (_running) return null;
                bool processRunning;
                try
                {
                    processRunning = _processRunning();
                }
                catch (Exception ex)
                {
                    Log.Debug("Accessibility", $"ZDSR process check failed: {ex.GetType().Name}: {ex.Message}");
                    processRunning = false;
                }
                return DescribeUnavailable(processRunning, _api != null, _lastResult);
            }
        }
    }

    /// <summary>Where the API was loaded from, or null until ZDSR has been found.</summary>
    public string? Location
    {
        get { lock (_gate) { return _api?.Location; } }
    }

    /// <summary>Speaks <paramref name="text"/> through ZDSR and brailles it as a flash message. With
    /// <paramref name="interrupt"/> ZDSR clears its queue and stops what it is saying; without it the text waits
    /// until ZDSR is idle. False when ZDSR did not take the text, so the caller falls back to the next output.</summary>
    public bool Speak(string text, bool interrupt)
    {
        if (string.IsNullOrEmpty(text)) return false;
        lock (_gate)
        {
            if (_api == null) return false;
            int result = Call(api => api.Speak(text, interrupt));
            if (result != ResultSuccess)
            {
                // Closed, unlicensed or not taking commands: report not running until the next check, so this
                // message and the ones after it go to the fallback rather than into a dead API.
                Record(false, result);
                _verdictUntilMs = _clockMs() + VerdictCacheMs;
                return false;
            }
            _lastContactMs = _clockMs();
            // 3 means no braille display is connected; the speech still counts.
            Call(api => api.Braille(text, true));
            return true;
        }
    }

    /// <summary>Stops ZDSR's speech. Not part of the announce path (Speak's interrupt flag does that); for
    /// diagnostics.</summary>
    public void StopSpeech()
    {
        lock (_gate)
        {
            if (_api == null) return;
            Call(api => { api.StopSpeak(); return ResultSuccess; });
        }
    }

    /// <summary>GetSpeakState's two answers from a running ZDSR; every other code means it cannot speak.</summary>
    internal static bool IsRunningState(int state) => state is StateSpeaking or StateNotSpeaking;

    /// <summary>The folders to look for <see cref="DllName"/> in, in order: the running ZDSRMain.exe's own folder
    /// (a portable copy, or an install anywhere), then the standard install folder under Program Files (x86).</summary>
    internal static IReadOnlyList<string> CandidateFolders(string? runningExePath, string? programFilesX86)
    {
        var folders = new List<string>();
        void Add(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            string trimmed = folder.TrimEnd('\\', '/');
            if (!folders.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) folders.Add(trimmed);
        }
        if (!string.IsNullOrWhiteSpace(runningExePath)) Add(Path.GetDirectoryName(runningExePath));
        if (!string.IsNullOrWhiteSpace(programFilesX86)) Add(Path.Combine(programFilesX86, "zdsr", "zdsr"));
        return folders;
    }

    /// <summary>Whether ZDSR's API may be loaded from <paramref name="folder"/>. Not elevated: from anywhere (a
    /// program that can start a process named ZDSRMain can already do whatever BA can). Elevated: only from under
    /// <paramref name="protectedRoots"/> (the Program Files folders, which only an administrator can write to), so
    /// a non-elevated program cannot get its own DLL loaded into an elevated BA by naming itself ZDSRMain.exe.</summary>
    internal static bool MayLoadFrom(string folder, bool elevated, IEnumerable<string?> protectedRoots)
        => !elevated || IsUnderProtectedRoot(folder, protectedRoots);

    /// <summary>True when <paramref name="folder"/>, with any ".." resolved, lies below one of the roots.</summary>
    internal static bool IsUnderProtectedRoot(string folder, IEnumerable<string?> protectedRoots)
    {
        string full;
        try
        {
            full = Path.GetFullPath(folder).TrimEnd('\\', '/');
        }
        catch (Exception)
        {
            return false;
        }
        foreach (string? root in protectedRoots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            string rootFull;
            try
            {
                rootFull = Path.GetFullPath(root).TrimEnd('\\', '/');
            }
            catch (Exception)
            {
                continue;
            }
            if (full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>The sentence behind <see cref="UnavailableReason"/>. <paramref name="lastResult"/> is the last
    /// code ZDSR answered a state query or a Speak with, int.MinValue when it was never asked.</summary>
    internal static string? DescribeUnavailable(bool processRunning, bool apiLoaded, int lastResult)
    {
        if (!processRunning) return null;
        if (!apiLoaded)
        {
            return $"ZDSR is running, but its speech interface ({DllName}) could not be loaded. See Troubleshooting in the guide.";
        }
        return lastResult switch
        {
            int.MinValue => null,
            1 => "ZDSR is running, but it and its speech interface are different versions. Update ZDSR.",
            ResultNotRunning => "ZDSR is running but not accepting speech: it reports that it is not licensed, or it is still starting.",
            _ => $"ZDSR is running but did not accept speech: code {Describe(lastResult)}.",
        };
    }

    /// <summary>A ZDSR return code in words, for the log.</summary>
    internal static string Describe(int code) => code switch
    {
        ResultSuccess => "0 (success)",
        1 => "1 (version mismatch)",
        ResultNotRunning => "2 (ZDSR is not running or not licensed)",
        StateSpeaking => "3 (speaking)",
        StateNotSpeaking => "4 (not speaking)",
        8 => "8 (command not delivered)",
        9 => "9 (separate channel not ready)",
        ResultCallFailed => "the call failed",
        _ => code.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private void Record(bool running, int result)
    {
        if (running != _running || (!running && result != _lastResult))
        {
            Log.Info("Accessibility", running
                ? $"ZDSR speech available (API at {_api?.Location})"
                : $"ZDSR speech unavailable: {Describe(result)}");
        }
        _running = running;
        _lastResult = result;
    }

    private int Call(Func<IZdsrApi, int> call)
    {
        try
        {
            return call(_api!);
        }
        catch (Exception ex)
        {
            Log.Debug("Accessibility", $"ZDSR API call failed: {ex.GetType().Name}: {ex.Message}");
            return ResultCallFailed;
        }
    }
}

/// <summary>ZDSRAPI_x64.dll, bound at run time from ZDSR's own folder (see <see cref="ZdsrWrapper"/>). One binding per
/// process; the library is never freed.</summary>
internal sealed class ZdsrNativeApi : IZdsrApi
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int InitTtsFn(int type, [MarshalAs(UnmanagedType.LPWStr)] string? channelName,
        [MarshalAs(UnmanagedType.Bool)] bool keyDownInterrupt);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SpeakFn([MarshalAs(UnmanagedType.LPWStr)] string text,
        [MarshalAs(UnmanagedType.Bool)] bool interrupt);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetSpeakStateFn();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void StopSpeakFn();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int BrailleFn([MarshalAs(UnmanagedType.LPWStr)] string text,
        [MarshalAs(UnmanagedType.Bool)] bool flashMessage);

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private static readonly object LoadGate = new();
    private static ZdsrNativeApi? _shared;
    private static string? _lastLoadProblem;

    private readonly InitTtsFn _initTts;
    private readonly SpeakFn _speak;
    private readonly GetSpeakStateFn _getSpeakState;
    private readonly StopSpeakFn _stopSpeak;
    private readonly BrailleFn _braille;

    public string Location { get; }

    private ZdsrNativeApi(string path, IntPtr library)
    {
        Location = path;
        _initTts = Export<InitTtsFn>(library, "InitTTS");
        _speak = Export<SpeakFn>(library, "Speak");
        _getSpeakState = Export<GetSpeakStateFn>(library, "GetSpeakState");
        _stopSpeak = Export<StopSpeakFn>(library, "StopSpeak");
        _braille = Export<BrailleFn>(library, "Braille");
    }

    public int InitTTS(int channelType, string? channelName, bool keyDownInterrupt)
        => _initTts(channelType, channelName, keyDownInterrupt);
    public int Speak(string text, bool interrupt) => _speak(text, interrupt);
    public int GetSpeakState() => _getSpeakState();
    public void StopSpeak() => _stopSpeak();
    public int Braille(string text, bool flashMessage) => _braille(text, flashMessage);

    /// <summary>The process-wide binding: null while ZDSRMain.exe is not running, or when neither its folder nor the
    /// standard install folder holds a usable ZDSRAPI_x64.dll.</summary>
    internal static IZdsrApi? TryLocate()
    {
        lock (LoadGate)
        {
            if (_shared != null) return _shared;

            Process[] processes = Process.GetProcessesByName(ZdsrWrapper.ProcessName);
            string? exePath = null;
            try
            {
                if (processes.Length == 0) return null;
                foreach (Process process in processes)
                {
                    exePath = ImagePath(process.Id);
                    if (exePath != null) break;
                }
            }
            finally
            {
                foreach (Process process in processes) process.Dispose();
            }

            var problems = new List<string>();
            bool elevated = Environment.IsPrivilegedProcess;
            string?[] protectedRoots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetEnvironmentVariable("ProgramW6432"),
            };
            foreach (string folder in ZdsrWrapper.CandidateFolders(exePath,
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)))
            {
                if (!ZdsrWrapper.MayLoadFrom(folder, elevated, protectedRoots))
                {
                    problems.Add($"{folder} refused: MSFS Blind Assist is running as administrator, "
                                 + "and then loads ZDSR's API only from under Program Files");
                    continue;
                }
                string dll = Path.Combine(folder, ZdsrWrapper.DllName);
                if (!File.Exists(dll))
                {
                    problems.Add($"{dll} not found");
                    continue;
                }
                IntPtr library = IntPtr.Zero;
                try
                {
                    library = NativeLibrary.Load(dll);
                    _shared = new ZdsrNativeApi(dll, library);
                    Log.Info("Accessibility", $"ZDSR API loaded from {dll}");
                    return _shared;
                }
                catch (Exception ex)
                {
                    // A library that loaded but lacks an export stays loaded: freeing it could stop a thread it
                    // already started.
                    problems.Add($"{dll}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            string problem = $"ZDSR is running ({exePath ?? "path unreadable"}) but its API could not be loaded: "
                             + string.Join("; ", problems);
            if (problem != _lastLoadProblem)
            {
                _lastLoadProblem = problem;
                Log.Warn("Accessibility", problem);
            }
            return null;
        }
    }

    /// <summary>Whether a ZDSRMain.exe is in the process list right now. A full process enumeration (about 3 ms
    /// with 300 processes, measured 2026-11-23): for the Settings status box, never the announce path.</summary>
    internal static bool IsZdsrProcessRunning()
    {
        Process[] processes = Process.GetProcessesByName(ZdsrWrapper.ProcessName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes) process.Dispose();
        }
    }

    private static T Export<T>(IntPtr library, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    /// <summary>The executable's path. ZDSRMain runs elevated with UI access, so Process.MainModule is refused; limited
    /// query access is enough for the image name.</summary>
    private static string? ImagePath(int processId)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            uint size = (uint)buffer.Capacity;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString(0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
