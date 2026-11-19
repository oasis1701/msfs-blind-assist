using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.WasmMemory;

/// <summary>
/// LayoutGenerator.exe as a child process. It runs with no window, its output is read as UTF-8 (the generator writes
/// UTF-8 without a byte order mark, so a path or reason holding "é" or "°" reaches the log intact), and a run past its
/// timeout is killed with everything it started. The first <c>--version</c> answer is kept for the instance's life, so
/// the shared instance (<see cref="NextToApp"/>) asks once per app session. Nothing here throws for a generator that is
/// missing, cannot start, crashes or hangs: that is a null version or a failed run, with the reason in its output.
/// Every process it starts goes into the instance's <see cref="KillOnCloseJob"/> right after it starts, so the app
/// exiting, crashing or being killed ends a generator still running (WASM-30). A job Windows refuses is logged once
/// and changes nothing else: the runs go on as before, without it.
/// </summary>
public sealed class ProcessLayoutGenerator : ILayoutGenerator
{
    public const string ExeName = "LayoutGenerator.exe";

    /// <summary>How long a killed process gets to end, and its last output to arrive.</summary>
    private const int KillWaitMs = 5_000;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly object SharedGate = new();

    /// <summary>Held for the app session, so its job's handle closes only when the app ends: never disposed, not on
    /// a profile switch or a session's end either, where a run finishes by itself.</summary>
    private static ProcessLayoutGenerator? s_shared;

    private readonly string _fileName;
    private readonly string _argumentPrefix;
    private readonly int _runTimeoutMs;
    private readonly int _versionTimeoutMs;
    private readonly object _versionGate = new();
    private readonly KillOnCloseJob? _job;
    private readonly Action<string> _log;
    private int _assignFailureLogged;
    private bool _versionAsked;
    private int? _version;

    /// <summary>Makes the job a generator runs in: <see cref="KillOnCloseJob.TryCreate"/>, or a test's stand-in.</summary>
    internal delegate KillOnCloseJob? JobFactory(out string? failure);

    /// <param name="fileName">The program to start.</param>
    /// <param name="argumentPrefix">Arguments placed before the generator's own (tests stand cmd.exe in for the generator).</param>
    /// <param name="runTimeoutMs">How long a run may take before it is killed.</param>
    /// <param name="versionTimeoutMs">How long <c>--version</c> may take before it is killed.</param>
    public ProcessLayoutGenerator(string fileName, string argumentPrefix = "", int runTimeoutMs = 60_000, int versionTimeoutMs = 10_000)
        : this(fileName, argumentPrefix, runTimeoutMs, versionTimeoutMs, KillOnCloseJob.TryCreate,
            line => Log.Warn("LayoutGenerator", line))
    {
    }

    /// <summary>The test seam: <paramref name="createJob"/> makes the job (a test simulates Windows refusing one) and
    /// <paramref name="log"/> takes the job's one failure line (debug.log in the app).</summary>
    internal ProcessLayoutGenerator(string fileName, string argumentPrefix, int runTimeoutMs, int versionTimeoutMs,
        JobFactory createJob, Action<string> log)
    {
        _fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        _argumentPrefix = argumentPrefix ?? throw new ArgumentNullException(nameof(argumentPrefix));
        _runTimeoutMs = runTimeoutMs;
        _versionTimeoutMs = versionTimeoutMs;
        ArgumentNullException.ThrowIfNull(createJob);
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _job = createJob(out string? failure);
        if (_job is null)
            _log($"no kill-on-close job: {failure}; a generator still running when the app ends keeps running");
    }

    /// <summary>Test seam: called with each process right after it starts and is put in the job.</summary>
    internal Action<Process>? Started { get; set; }

    /// <summary>Test seam: closes this instance's job, as the app's exit does. Never called in the app.</summary>
    internal void CloseJobForTest() => _job?.Dispose();

    /// <summary>The shared generator next to the app (<see cref="AppContext.BaseDirectory"/>), made once; null while
    /// the exe is not there, as in a public build.</summary>
    public static ProcessLayoutGenerator? NextToApp()
    {
        string path = Path.Combine(AppContext.BaseDirectory, ExeName);
        if (!File.Exists(path)) return null;
        lock (SharedGate) return s_shared ??= new ProcessLayoutGenerator(path);
    }

    /// <summary>A new generator for the exe in <paramref name="folder"/>; null when it is not there.</summary>
    internal static ProcessLayoutGenerator? In(string folder)
    {
        string path = Path.Combine(folder, ExeName);
        return File.Exists(path) ? new ProcessLayoutGenerator(path) : null;
    }

    /// <summary>The first line of <c>--version</c>'s standard output as a whole number, asked once: null when the
    /// generator could not start, timed out, exited non-zero or printed anything else.</summary>
    public int? AskVersion()
    {
        lock (_versionGate)
        {
            if (!_versionAsked)
            {
                var (exitCode, _, stdout, _) = Execute("--version", _versionTimeoutMs);
                // The generator prints an invariant integer; NumberStyles.None reads digits only, on any PC's culture.
                _version = exitCode == 0 && stdout.Count > 0
                    && int.TryParse(stdout[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int version)
                    ? version
                    : null;
                _versionAsked = true;
            }
            return _version;
        }
    }

    public GeneratorRun Run(string wasmFile, string layoutsRoot)
    {
        ArgumentNullException.ThrowIfNull(wasmFile);
        ArgumentNullException.ThrowIfNull(layoutsRoot);
        var (exitCode, timedOut, stdout, stderr) = Execute(Quote(wasmFile) + " " + Quote(layoutsRoot), _runTimeoutMs);
        return new GeneratorRun(exitCode, timedOut, stdout.Concat(stderr).ToList());
    }

    /// <summary>How the process starts: no shell and no window, standard output and error redirected and read as UTF-8.</summary>
    internal ProcessStartInfo StartInfo(string arguments) => new()
    {
        FileName = _fileName,
        Arguments = _argumentPrefix.Length == 0 ? arguments : _argumentPrefix + " " + arguments,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Utf8,
        StandardErrorEncoding = Utf8,
    };

    /// <summary>One argument quoted for the Windows command line: inside quotes, a backslash run before a quote or at the
    /// end is doubled and a quote escaped, so a folder ending in "\" stays one argument.</summary>
    internal static string Quote(string argument)
    {
        var quoted = new StringBuilder(argument.Length + 2).Append('"');
        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
                quoted.Append('\\', backslashes * 2 + 1);
            else
                quoted.Append('\\', backslashes);
            quoted.Append(c);
            backslashes = 0;
        }
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    /// <summary>Runs the process to its end or its timeout. Both streams are read as they come (a full pipe never
    /// blocks the child) and, once it exits, drained to their end, so no trailing line is lost.</summary>
    private (int? ExitCode, bool TimedOut, List<string> Stdout, List<string> Stderr) Execute(string arguments, int timeoutMs)
    {
        var stdout = new List<string>();
        var stderr = new List<string>();
        using var process = new Process { StartInfo = StartInfo(arguments) };
        process.OutputDataReceived += (_, e) => Add(stdout, e.Data);
        process.ErrorDataReceived += (_, e) => Add(stderr, e.Data);
        try
        {
            process.Start();
            PutInJob(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return (null, false, new List<string> { $"could not start {_fileName}: {ex.Message}" }, new List<string>());
        }
        Started?.Invoke(process);

        try
        {
            if (process.WaitForExit(timeoutMs))
            {
                process.WaitForExit(); // after a timed wait, only this overload waits for the redirected output's end
                return (process.ExitCode, false, Snapshot(stdout), Snapshot(stderr));
            }
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
            {
                // It ended between the timeout and the kill, or a process in its tree could not be killed (AggregateException).
            }
            process.WaitForExit(KillWaitMs);
            return (null, true, Snapshot(stdout), Snapshot(stderr));
        }
        finally
        {
            // An instance its caller has dropped could otherwise be collected during the wait, and its job's handle
            // closed by the finalizer, ending the run it is waiting for.
            GC.KeepAlive(_job);
        }
    }

    /// <summary>Puts a started process in the job. Windows refusing is logged the first time only, and the run goes
    /// on without the job: a job never stops or fails a run.</summary>
    private void PutInJob(Process process)
    {
        if (_job is null || _job.TryAssign(process, out string? failure)) return;
        if (Interlocked.Exchange(ref _assignFailureLogged, 1) == 0)
            _log($"generator not put in the kill-on-close job: {failure}; it keeps running if the app ends first (logged once)");
    }

    private static void Add(List<string> lines, string? line)
    {
        if (string.IsNullOrEmpty(line)) return; // drops the stream's end (null) and blank lines
        lock (lines) lines.Add(line);
    }

    private static List<string> Snapshot(List<string> lines)
    {
        lock (lines) return lines.ToList();
    }
}
