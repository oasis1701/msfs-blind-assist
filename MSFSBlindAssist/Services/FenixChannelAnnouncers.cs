using System.Diagnostics;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The announcer host of the Fenix display channel: one WinForms timer, ticking every 100 ms on the UI thread, that
/// runs the sampler of each announcer, in the order they were given. The
/// announcers are polled here, never driven from the channel's pool thread: a sampler looks at the latest snapshot of the
/// channel, compares it with what it saw last and speaks if something is due, so everything a pilot hears from the
/// channel is judged on the thread that speaks. The Fenix definition owns one host beside its
/// <see cref="FenixChannelMonitor"/>, starts it with the monitor and disposes it first when the pilot switches away.
/// </summary>
/// <remarks>
/// <para>Isolation. A sampler that throws is reported once for each distinct line
/// ("&lt;exception type&gt;: &lt;message&gt; (at &lt;type&gt;.&lt;method&gt;)", the app's own method it came from: see
/// <see cref="ProblemLine"/>) and costs nothing else: the other samplers of that tick still run, the timer stays on, and
/// the next tick runs them all again. The lines remembered are bounded as the monitor's own problems are: 32 distinct
/// ones, and the line that would be the 33rd empties the memory first, so a fault whose message changes at every tick
/// cannot grow it. A log that throws is swallowed in its turn. So no announcer's fault silences another, and none reaches
/// the timer's tick event, where it would go to the application's UI-thread handler.</para>
/// <para>Time. The clock is read once for each tick and its reading is handed to every sampler of that tick, so the
/// samplers agree on when the tick was. In the app it is <see cref="Environment.TickCount64"/>, the one clock of the
/// channel, so a reading compares with a snapshot's <see cref="FenixChannelSnapshot.AtMs"/>.</para>
/// <para>Short samplers. A sampler runs on the UI thread, ten times a second: it only reads a snapshot and speaks, and does
/// no I/O, so a tick stays short.</para>
/// <para>Lifetime. <see cref="Start"/> and <see cref="Dispose"/> run on the UI thread (the definition's background Start
/// and Stop do), which a WinForms timer needs: it ticks on the thread that made it. A second <see cref="Start"/> does
/// nothing, and so does a <see cref="Start"/> after <see cref="Dispose"/>. After <see cref="Dispose"/> a tick does nothing
/// at all, which covers the one that was already queued when the host ended. <see cref="Dispose"/> never throws and a
/// second call does nothing.</para>
/// </remarks>
internal sealed class FenixChannelAnnouncers : IDisposable
{
    /// <summary>How often the host ticks, in milliseconds.</summary>
    public const int IntervalMs = 100;

    // The same bound the monitor puts on the problems it has logged.
    private const int MaxRememberedProblems = 32;

    // Where the app's own code is, for the method a log line names (ProblemLine).
    private const string AppNamespace = "MSFSBlindAssist";

    private readonly Action<long>[] _samplers;
    private readonly Action<string> _logProblem;
    private readonly Func<long> _clock;
    private readonly HashSet<string> _problems = new(StringComparer.Ordinal);

    // Made by Start, ended by Dispose. UI thread only, like everything here, so no lock.
    private System.Windows.Forms.Timer? _timer;
    private bool _disposed;

    /// <param name="samplers">One optional sampler for each announcer, in the order they run. Each is handed the tick's
    /// reading of <paramref name="clock"/> in milliseconds. The list is copied, so the host runs the samplers it was
    /// given, whatever happens to the list afterwards.</param>
    /// <param name="logProblem">Where a throw is reported, once for each distinct line of <see cref="ProblemLine"/>, for
    /// example <see cref="FenixChannelMonitor.ReportFeatureProblem"/> under the name "announcer".</param>
    /// <param name="clock">The time of a tick, read once for each tick: <see cref="Environment.TickCount64"/> in the app.</param>
    public FenixChannelAnnouncers(
        IReadOnlyList<Action<long>> samplers, Action<string> logProblem, Func<long> clock)
    {
        ArgumentNullException.ThrowIfNull(samplers);
        ArgumentNullException.ThrowIfNull(logProblem);
        ArgumentNullException.ThrowIfNull(clock);

        _samplers = samplers.ToArray();
        _logProblem = logProblem;
        _clock = clock;
    }

    /// <summary>
    /// Whether the timer is on: made by <see cref="Start"/>, and ended by <see cref="Dispose"/>. For the tests, which pin
    /// that a throw in a sampler leaves it on.
    /// </summary>
    internal bool IsRunning => _timer is { Enabled: true };

    /// <summary>
    /// Makes the timer and starts it, at <see cref="IntervalMs"/>. Call it on the UI thread. Once only: a second call, and
    /// a call after <see cref="Dispose"/>, do nothing.
    /// </summary>
    public void Start()
    {
        if (_disposed || _timer is not null) return;

        // Kept before it starts, so a Dispose after a failed start still ends it.
        var timer = new System.Windows.Forms.Timer { Interval = IntervalMs };
        _timer = timer;
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    /// <summary>
    /// One tick, which is what the timer calls: each sampler in order, with one reading of the clock handed to all of
    /// them. Nothing a sampler throws escapes (see the remarks). After <see cref="Dispose"/> it does nothing. Internal
    /// for the tests, which call it themselves with no message pump.
    /// </summary>
    internal void Tick()
    {
        if (_disposed) return;

        long now = _clock();

        foreach (Action<long> sampler in _samplers)
        {
            try
            {
                sampler(now);
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }
    }

    /// <summary>
    /// Stops and disposes the timer, and ends the host: from then on a tick does nothing. Never throws; a second call does
    /// nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        System.Windows.Forms.Timer? timer = _timer;
        _timer = null;
        try
        {
            timer?.Stop();
            timer?.Dispose();
        }
        catch (Exception)
        {
            // The host is over either way, and the monitor beside it must still be disposed by the caller.
        }
    }

    // Each distinct line once, bounded as the monitor's problems are.
    private void Report(Exception ex)
    {
        string problem = ProblemLine(ex);
        if (_problems.Contains(problem)) return;

        if (_problems.Count >= MaxRememberedProblems) _problems.Clear();
        _problems.Add(problem);

        try
        {
            _logProblem(problem);
        }
        catch (Exception)
        {
            // A log that throws must not take the tick down with it.
        }
    }

    /// <summary>
    /// The line a throw is logged as: "&lt;exception type&gt;: &lt;message&gt; (at &lt;type&gt;.&lt;method&gt;)", the last
    /// part naming the first method of the app's own on its stack trace (the first frame whose declaring type is in the
    /// app's namespace, with that type's short name). The host knows its samplers only as delegates, so this is what
    /// tells the log which of them it was. It is not simply the first frame: an index past the end of a list or an array
    /// is thrown from a helper of the framework's (ThrowHelper.ThrowArgumentOutOfRange_IndexMustBeLessException and
    /// ThrowHelpers.ThrowIndexOutOfRangeException, measured on .NET 10), and a line naming that tells nobody which
    /// sampler it was. An exception with no such frame, one never thrown or one thrown and caught inside the framework,
    /// leaves the part out. Only names go into the line, no number is formatted here, so the PC's culture changes
    /// nothing. Internal for the tests.
    /// </summary>
    internal static string ProblemLine(Exception ex)
    {
        string line = $"{ex.GetType().Name}: {ex.Message}";

        foreach (StackFrame frame in new StackTrace(ex).GetFrames())
        {
            if (frame.GetMethod() is { DeclaringType: { } type } method && IsInTheApp(type))
                return $"{line} (at {type.Name}.{method.Name})";
        }

        return line;
    }

    // A nested type, such as the one that holds a lambda, reads the namespace of the type around it.
    private static bool IsInTheApp(Type type) =>
        type.Namespace is { } name
        && (name == AppNamespace || name.StartsWith(AppNamespace + ".", StringComparison.Ordinal));
}
