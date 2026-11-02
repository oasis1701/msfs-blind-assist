using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The owner of one session of the Fenix display channel: it runs the client's two calls, decodes each message,
/// feeds the trust gate under one lock, keeps the latest display frame, asks for the Fenix build on each
/// connection of the data call, and writes the channel's log (fenix_channel.log). A reader asks it for a snapshot:
/// a display's verdict and the latest frame, taken together. The Fenix aircraft definition owns one monitor and
/// disposes it when the pilot switches away.
/// </summary>
/// <remarks>
/// <para>Threads. A call's messages and states arrive on the client's pool threads, comparison values and reads on
/// the UI thread, and the tick on a pool thread. One lock guards the gate and every field that decides what the
/// log says, so a verdict is always read from one whole state, and the log's lines come out in the order the gate
/// saw its events. Nothing under the lock waits for anything. A message is decoded before the lock is taken and fed
/// under it. The latest frame is swapped in whole, and a snapshot reads it under the same lock as the verdict, so a
/// reader never sees half of a frame, nor a frame beside a verdict the gate gave on another one.
/// Nothing here throws at a caller, speaks or touches a control, as with <see cref="FenixDataService"/>; every
/// clock reading is <see cref="Environment.TickCount64"/>, so the gate compares times from one source.</para>
/// <para>Each connection of the data call does three things, in this order, under the lock. The gate starts fresh,
/// and the latest frame is cleared with it, so no frame of an earlier connection is read.
/// Then it is given the last ECAM entries message again: that stream sends only when its list changes, so a
/// wrong-shaped message that arrived before this connection is the last thing it said, and the gate's fresh start
/// would otherwise erase the latch that message set. Then a build loop starts, which asks for the build until it
/// is answered. A loop belongs to the connection that started it (its generation): a newer connection, a
/// disconnect and Dispose all end it, and an answer that still comes back from an ended one is dropped, never
/// given to the gate.</para>
/// <para>The log. A line is written for each change of a call's state, for each build, for each change of a
/// display's verdict (its Trusted, or the headline of its reason: the text before the first colon, so the
/// numbers of a disagreement moving every second are one line), and, every 30th tick of a connection, for the
/// comparison pairs. Dispose writes one more, "channel stopped", the last line the log ever gets (the in-sim
/// check that an aircraft switch ended the channel reads it). A data call that opens and says nothing is dropped
/// and asked again for hours, so "connected" is written with the first message and not with the response
/// headers, and a call that is not answering is written only when its reason differs from the last one written
/// since a message arrived: otherwise each try would write two lines. The ECAM entries call has no such cycle
/// (nothing limits its silence), so its "connected" is written with its headers, but its failures to answer follow
/// the same rule. A refusal is written once, since it ends the channel (below). This class formats no number
/// itself: the pairs come from the gate in the invariant culture.</para>
/// <para>A refusal of either call (grpc-status 7 or 16, or HTTP 401 or 403, which the client reports as
/// <see cref="FenixChannelState.Refused"/>) ends the channel for the session ([FNX-21]), not only the call refused:
/// the gate is told the channel is not answering, with the refusal as the reason, so every display reads "channel
/// not answering: &lt;the refusal&gt;" and falls back to the AI read; the build loop ends; "&lt;call&gt;: refused
/// (&lt;reason&gt;); stopped for this session" is written once; and the client is disposed, so the other call stops
/// too and neither asks again. Every callback of the client after it is ignored, as after Dispose. The tick goes
/// on, so the log still gets the verdicts' change, and Dispose still writes "channel stopped" last.</para>
/// <para>After <see cref="Dispose"/> every callback of the client is ignored (it may deliver one that was
/// already under way): no gate call, no log line, no change to <see cref="Latest"/>. Dispose takes no lock and
/// waits for nothing, so it is safe on the UI thread; the one line it writes is the log's own non-blocking
/// write. That line, "channel stopped", comes last: after the flag that quiets the callbacks is set and the
/// tick, the build loop and both calls are stopped, and <c>Write</c> drops every line after it, so the log ends
/// with it. (A line a callback had already handed to the log at that instant is the one thing this cannot
/// recall.)</para>
/// </remarks>
internal sealed class FenixChannelMonitor : IDisposable
{
    /// <summary>The var key of the magnetic heading the channel asks SimConnect for, in degrees.</summary>
    public const string HeadingKey = "FENIX_CHANNEL_HEADING";

    /// <summary>The var key of the total fuel the channel asks SimConnect for, in pounds.</summary>
    public const string FuelKey = "FENIX_CHANNEL_FUEL";

    /// <summary>
    /// The var key of the true altitude the channel asks SimConnect for, PLANE ALTITUDE, in feet: the channel's
    /// altitude field is the true altitude too. Never the indicated altitude, which follows the baro setting the
    /// Fenix writes into the sim's altimeter ([FNX-25]).
    /// </summary>
    public const string AltitudeKey = "FENIX_CHANNEL_ALTITUDE";

    /// <summary>
    /// How many ticks of a connection pass between two comparisons lines: 30 of the default one-second tick,
    /// so the log shows each comparison pair every 30 s while the channel is connected.
    /// </summary>
    public const int ComparisonLogEveryTicks = 30;

    // The var keys the Fenix definition already has: the flight director lights.
    private const string FdCaptainKey = "I_FCU_EFIS1_FD";
    private const string FdFirstOfficerKey = "I_FCU_EFIS2_FD";

    private const string LogChannelName = "fenix_channel";
    private const string StoppedLine = "channel stopped";
    private const int MaxRememberedProblems = 32;

    // In the order the log writes them.
    private static readonly FenixChannelDisplay[] Displays =
        { FenixChannelDisplay.Pfd, FenixChannelDisplay.Ewd, FenixChannelDisplay.Sd };

    private readonly object _lock = new();
    private readonly FenixChannelTrust _trust;
    private readonly FenixChannelClient _client;
    private readonly Func<CancellationToken, Task<string?>> _readBuild;
    private readonly IReadOnlySet<string> _verifiedBuilds;
    private readonly Action<string> _log;
    private readonly TimeSpan _tick;
    private readonly TimeSpan _buildRetry;
    private readonly System.Threading.Timer _timer;

    // The gate, the log's memory and the connection's bookkeeping: all guarded by _lock. The latest frame is also
    // read without it, through Volatile.
    private FenixChannelFrame? _latest;
    private FenixEcamEntries? _lastEntries;
    private bool _dataConnected;
    private bool _dataMessageSeen;
    private string? _lastDataFailure;
    private string? _lastEcamFailure;
    private int _connectedTicks;
    private long _generation;
    private readonly (bool Trusted, string Headline)?[] _loggedVerdicts = new (bool, string)?[Displays.Length];

    // Canceled to end the build loop of a connection that is over. Swapped with Interlocked so Dispose needs no
    // lock. Never disposed, as in FenixChannelClient: it holds no timer and no handle, and a loop may still read
    // its token.
    private CancellationTokenSource? _buildLoop;

    private readonly HashSet<string> _problems = new(StringComparer.Ordinal);

    private int _started;
    private int _refused;   // 1 once either call was refused: the channel is over for the session ([FNX-21])
    private int _disposed;

    /// <param name="readBuild">Asks the Fenix for its build, for example <c>FenixDataService.GetSoftwareVersionAsync</c>:
    /// the build, or null when it did not answer. A query that throws counts as one that did not answer.</param>
    /// <param name="verifiedBuilds">The builds the channel has been checked against. By default
    /// <see cref="FenixChannelFields.VerifiedBuilds"/>.</param>
    /// <param name="handler">For tests: answers the client's HTTP calls instead of the network.</param>
    /// <param name="log">Where the log lines go. By default the <c>fenix_channel</c> log.</param>
    /// <param name="tick">How often the verdicts are looked at for the log. By default one second.</param>
    /// <param name="buildRetry">How long to wait before asking for the build again. By default 30 seconds.</param>
    /// <param name="retryDelay">For tests: the client's wait before it asks a call again, given the attempt and
    /// whether the last answer was grpc-status 12. By default the client's own schedule.</param>
    public FenixChannelMonitor(
        Func<CancellationToken, Task<string?>> readBuild,
        IReadOnlySet<string>? verifiedBuilds = null,
        HttpMessageHandler? handler = null,
        Action<string>? log = null,
        TimeSpan? tick = null,
        TimeSpan? buildRetry = null,
        Func<int, bool, TimeSpan>? retryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(readBuild);

        _tick = tick ?? TimeSpan.FromSeconds(1);
        _buildRetry = buildRetry ?? TimeSpan.FromSeconds(30);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_tick, TimeSpan.Zero, nameof(tick));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_buildRetry, TimeSpan.Zero, nameof(buildRetry));

        _readBuild = readBuild;
        _verifiedBuilds = verifiedBuilds ?? FenixChannelFields.VerifiedBuilds;
        _log = log ?? Log.Channel(LogChannelName).Info;
        _trust = new FenixChannelTrust(_verifiedBuilds);
        _client = new FenixChannelClient(handler, retryDelay);

        // Not running until Start: its callback must find the monitor whole.
        _timer = new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>The one clock of the channel, for frames, SimConnect's values and verdicts alike.</summary>
    private static long Now => Environment.TickCount64;

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    // True once no callback of the client is heard any more: after a refusal of either call, and after Dispose.
    private bool IsStopped => IsDisposed || Volatile.Read(ref _refused) != 0;

    /// <summary>
    /// The latest display frame the gate took values from, swapped in whole; null before the first frame of a
    /// connection. Each connection of the data call clears it, and only a frame with data
    /// (<see cref="FenixChannelFrame.HasData"/>) and every shape right (<see cref="FenixChannelFrame.ShapesOk"/>)
    /// replaces it: the gate takes no values from the others. For the tests: a read takes its frame from
    /// <see cref="Snapshot"/>, with the verdict that judged it, never from here.
    /// </summary>
    public FenixChannelFrame? Latest => Volatile.Read(ref _latest);

    /// <summary>
    /// Opens both calls and starts the tick. Once only: a second call, and a call after
    /// <see cref="Dispose"/>, do nothing.
    /// </summary>
    public void Start()
    {
        if (IsDisposed || Interlocked.Exchange(ref _started, 1) != 0) return;

        _client.Start(FenixChannelCall.DisplayData, OnDataMessage, OnDataState);
        _client.Start(FenixChannelCall.EcamEntries, OnEcamMessage, OnEcamState);
        try
        {
            _timer.Change(_tick, _tick);
        }
        catch (ObjectDisposedException)
        {
            // Dispose ran in between: nothing to tick.
        }
    }

    /// <summary>
    /// Whether <paramref name="display"/> may be read to the pilot now (and the first reason it may not), with the
    /// latest frame, both taken under the one lock at the same instant: the frame is the one the gate judged in
    /// giving the verdict, never an older frame beside a newer verdict or the reverse. A read composes from this
    /// frame only when this verdict trusts it (<see cref="FenixChannelReads.Choose"/>). The frame is null before
    /// the first of a connection. A monitor that has been disposed trusts nothing ("channel stopped"): its frames
    /// have stopped, and the gate would take 2 s to notice.
    /// </summary>
    public (FenixDisplayVerdict Verdict, FenixChannelFrame? Frame) Snapshot(FenixChannelDisplay display)
    {
        lock (_lock)
        {
            FenixDisplayVerdict verdict = IsDisposed
                ? new FenixDisplayVerdict(false, "channel stopped")
                : _trust.VerdictFor(display, Now);
            return (verdict, _latest);
        }
    }

    /// <summary>
    /// The verdict of <see cref="Snapshot"/> alone: whether <paramref name="display"/> may be read to the pilot
    /// now, and the first reason it may not. For the tests: a read takes the whole snapshot, so that its frame and
    /// its verdict come together.
    /// </summary>
    public FenixDisplayVerdict VerdictFor(FenixChannelDisplay display) => Snapshot(display).Verdict;

    /// <summary>
    /// SimConnect's latest value for one comparison (the flight director lights as 0 or 1, the true altitude in feet,
    /// the magnetic heading in degrees, the fuel in kilograms; see <see cref="TryMapComparison"/>), stamped now. Called
    /// from the UI thread as the values arrive.
    /// </summary>
    public void OnComparison(FenixComparison kind, double value)
    {
        lock (_lock)
        {
            if (IsDisposed) return;
            _trust.OnComparison(kind, value, Now);
        }
    }

    /// <summary>
    /// Ends everything at once: the tick, the build loop and both calls (the client drops its streams), and then
    /// writes "channel stopped", the last line of the log. Never throws, takes no lock and waits for nothing; a
    /// second call does nothing and writes nothing.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try { _timer.Dispose(); } catch (Exception) { }
        Cancel(Interlocked.Exchange(ref _buildLoop, null));
        try { _client.Dispose(); } catch (Exception) { }

        // Last, with the callbacks quiet and everything stopped: Write drops every line from here on, so this is
        // the one the log ends with.
        WriteToLog(StoppedLine);
    }

    /// <summary>
    /// Maps a SimConnect var key to the comparison it feeds. The flight director lights are only peeked
    /// (<paramref name="consume"/> is false, so they carry on to their own handling); the three keys of the channel's
    /// own, the heading, the fuel and the true altitude, are consumed. The fuel arrives in pounds and leaves in
    /// kilograms. A key that is none of the five gives false and is never consumed: INDICATED_ALTITUDE among them,
    /// since the Fenix's baro setting moves it ([FNX-25]).
    /// </summary>
    public static bool TryMapComparison(
        string varKey, double value, out FenixComparison kind, out double converted, out bool consume)
    {
        converted = value;
        consume = false;
        switch (varKey)
        {
            case FdCaptainKey:
                kind = FenixComparison.FdLightCapt;
                return true;
            case FdFirstOfficerKey:
                kind = FenixComparison.FdLightFo;
                return true;
            case AltitudeKey:
                kind = FenixComparison.AltitudeFt;
                consume = true;
                return true;
            case HeadingKey:
                kind = FenixComparison.MagHeadingDeg;
                consume = true;
                return true;
            case FuelKey:
                kind = FenixComparison.FuelKg;
                converted = value / FenixChannelTrust.PoundsPerKilogram;
                consume = true;
                return true;
            default:
                kind = default;
                converted = 0;
                return false;
        }
    }

    // ---- The client's callbacks -----------------------------------------------------------------
    // Internal rather than private so a test can deliver a state or a message at an exact moment, twice in a
    // row, or after a refusal or Dispose, which a real stream will not do. After either, each one returns at once.

    /// <summary>The data call changed state. The client reports each change once, on a pool thread.</summary>
    internal void OnDataState(FenixChannelState state, string text)
    {
        if (IsStopped) return;

        CancellationTokenSource? ended = null;
        bool refused = false;
        lock (_lock)
        {
            if (IsStopped) return;

            switch (state)
            {
                case FenixChannelState.Connected:
                    _dataConnected = true;
                    _dataMessageSeen = false;
                    _connectedTicks = 0;

                    // The order matters: see the remarks. No line is written yet: the headers say the call
                    // opened, not that it works. The gate starts fresh, and so does the latest frame: a frame of
                    // the connection before is never read.
                    _trust.OnConnected(Now);
                    Volatile.Write(ref _latest, null);
                    if (_lastEntries is not null) _trust.OnEcamEntries(_lastEntries);
                    ended = StartBuildLoop();
                    break;

                case FenixChannelState.NotAnswering:
                    ended = DataCallDown(text);
                    WriteFailure(ref _lastDataFailure, $"display data: not answering ({text})");
                    break;

                case FenixChannelState.Refused:
                    ended = Refuse("display data", text);
                    refused = true;
                    break;
            }
        }

        Cancel(ended);
        if (refused) StopClient();
    }

    /// <summary>One message of the data call: decoded outside the lock, fed to the gate under it.</summary>
    internal void OnDataMessage(byte[] message)
    {
        if (IsStopped) return;

        FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(message);
        lock (_lock)
        {
            if (IsStopped) return;

            // A message shows the call works, which the headers do not: only now is "connected" news, and a
            // failure that follows is news again even if it reads like the last one.
            _lastDataFailure = null;
            if (!_dataMessageSeen)
            {
                _dataMessageSeen = true;
                Write("display data: connected");
            }

            _trust.OnFrame(frame, Now);

            // The latest frame is the one the gate took values from: it skips an empty push (no data), and takes
            // none from a malformed or wrong-shaped frame, which latches it instead.
            if (frame.HasData && frame.ShapesOk) Volatile.Write(ref _latest, frame);
        }
    }

    /// <summary>
    /// The ECAM entries call changed state. Only the log hears of its connection: no verdict depends on this call
    /// being connected until a read uses it. Its refusal ends the whole channel, as one of the data call does.
    /// </summary>
    internal void OnEcamState(FenixChannelState state, string text)
    {
        if (IsStopped) return;

        CancellationTokenSource? ended = null;
        bool refused = false;
        lock (_lock)
        {
            if (IsStopped) return;

            switch (state)
            {
                case FenixChannelState.Connected:
                    Write("ECAM entries: connected");
                    break;
                case FenixChannelState.NotAnswering:
                    WriteFailure(ref _lastEcamFailure, $"ECAM entries: not answering ({text})");
                    break;
                case FenixChannelState.Refused:
                    ended = Refuse("ECAM entries", text);
                    refused = true;
                    break;
            }
        }

        Cancel(ended);
        if (refused) StopClient();
    }

    /// <summary>
    /// One message of the ECAM entries call: decoded outside the lock, then kept (the gate is given it again on
    /// each connection of the data call) and fed to the gate, which looks only at its shape.
    /// </summary>
    internal void OnEcamMessage(byte[] message)
    {
        if (IsStopped) return;

        FenixEcamEntries entries = FenixChannelDecoder.DecodeEcamEntries(message);
        lock (_lock)
        {
            if (IsStopped) return;

            _lastEcamFailure = null;
            _lastEntries = entries;
            _trust.OnEcamEntries(entries);
        }
    }

    // ---- The tick -------------------------------------------------------------------------------

    /// <summary>
    /// One tick of the timer: writes each display's verdict when its Trusted or the headline of its reason has
    /// changed since it was last written (the first tick writes all three), and, while the data call is connected,
    /// every 30th tick of the connection the comparison pairs.
    /// </summary>
    internal void Tick()
    {
        if (IsDisposed) return;

        try
        {
            lock (_lock)
            {
                if (IsDisposed) return;

                long now = Now;
                for (int i = 0; i < Displays.Length; i++)
                {
                    WriteVerdictChange(i, now);
                }

                if (_dataConnected && ++_connectedTicks % ComparisonLogEveryTicks == 0)
                {
                    Write("comparisons: " + string.Join("; ", _trust.DescribeComparisons()));
                }
            }
        }
        catch (Exception ex)
        {
            // The timer's callback runs on a pool thread, where an exception would end the process.
            LogProblem($"tick failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Called with the lock held.
    private void WriteVerdictChange(int index, long now)
    {
        FenixChannelDisplay display = Displays[index];
        FenixDisplayVerdict verdict = _trust.VerdictFor(display, now);
        string headline = Headline(verdict.Reason);
        if (_loggedVerdicts[index] is { } last && last.Trusted == verdict.Trusted && last.Headline == headline) return;

        _loggedVerdicts[index] = (verdict.Trusted, headline);
        string name = NameOf(display);
        Write(verdict.Trusted ? $"{name} trusted" : $"{name} not trusted: {verdict.Reason}");
    }

    // The reason up to its first colon. "altitude disagrees: channel 1,023 ft, sim 2,050 ft" is "altitude
    // disagrees"; a reason with no colon ("build 3.0.0.7613 not checked") is its own headline.
    private static string Headline(string reason)
    {
        int colon = reason.IndexOf(':');
        return colon < 0 ? reason : reason[..colon];
    }

    private static string NameOf(FenixChannelDisplay display) => display switch
    {
        FenixChannelDisplay.Pfd => "PFD",
        FenixChannelDisplay.Ewd => "E/WD",
        FenixChannelDisplay.Sd => "SD",
        _ => display.ToString()
    };

    // ---- A refusal ------------------------------------------------------------------------------

    // A refusal of either call ends the channel for the session ([FNX-21]): from here on no callback of the client
    // is heard; the gate is told the channel is not answering, with the refusal as the reason, so every display
    // reads "channel not answering: <the refusal>"; the build loop ends; and the refusal is written once, the last
    // line either call gives the log. The caller then stops the client (StopClient) once the lock is released, so
    // the other call stops too. Called with the lock held; returns the build loop to cancel.
    private CancellationTokenSource? Refuse(string call, string text)
    {
        Volatile.Write(ref _refused, 1);
        _dataConnected = false;
        _trust.OnDisconnected(text);
        Write($"{call}: refused ({text}); stopped for this session");
        return EndBuildLoop();
    }

    // Ends both calls at once: the client drops its streams and asks again for neither. Its Dispose never throws,
    // takes no lock and waits for nothing; the monitor's own Dispose calls it again later, which does nothing.
    private void StopClient()
    {
        try { _client.Dispose(); } catch (Exception) { }
    }

    // ---- The build ------------------------------------------------------------------------------

    // The data call is down: no display is trusted until it connects again, and the build loop of the connection
    // that has gone ends. Called with the lock held; returns the source to cancel once it is released.
    private CancellationTokenSource? DataCallDown(string reason)
    {
        _dataConnected = false;
        _trust.OnDisconnected(reason);
        return EndBuildLoop();
    }

    // Starts the build loop of the connection that has just opened, under the next generation. Called with the
    // lock held; returns the loop it replaces, for the caller to cancel once the lock is released.
    private CancellationTokenSource? StartBuildLoop()
    {
        long generation = ++_generation;
        var loop = new CancellationTokenSource();
        CancellationTokenSource? superseded = Interlocked.Exchange(ref _buildLoop, loop);

        if (IsDisposed)
        {
            // Dispose ran in between, and canceled only what it found before this loop was installed.
            Cancel(loop);
            return superseded;
        }

        CancellationToken token = loop.Token;
        _ = Task.Run(() => RunBuildLoopAsync(generation, token));
        return superseded;
    }

    // Ends the build loop, and with a new generation makes any answer that is still on its way come too late.
    // Called with the lock held; returns the source to cancel once it is released.
    private CancellationTokenSource? EndBuildLoop()
    {
        _generation++;
        return Interlocked.Exchange(ref _buildLoop, null);
    }

    // Asks for the build until it is answered, waiting _buildRetry between tries, then gives it to the gate. Runs
    // on a pool thread, never under the lock, and never throws.
    private async Task RunBuildLoopAsync(long generation, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? build = await AskForBuildAsync(ct).ConfigureAwait(false);
                if (build is not null)
                {
                    ApplyBuild(generation, build);
                    return;
                }

                await Task.Delay(_buildRetry, ct).ConfigureAwait(false);
            }
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // Ended by a newer connection, a disconnect, a refusal or Dispose: whatever the query threw on its way
            // out is of no interest.
        }
        catch (Exception ex)
        {
            LogProblem($"build loop stopped: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<string?> AskForBuildAsync(CancellationToken ct)
    {
        try
        {
            return await _readBuild(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // A query that fails is an answer that did not come: the build stays unknown and the loop asks again.
            // A timeout inside the query is one too, though it arrives as a cancellation.
            LogProblem($"build query failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private void ApplyBuild(long generation, string build)
    {
        lock (_lock)
        {
            // An answer belongs to the connection that asked: after a newer connection, a disconnect, a refusal or
            // Dispose it is no news, and must not reach the gate, where it would stand for a build the channel no
            // longer runs.
            if (IsStopped || generation != _generation) return;

            _trust.OnBuild(build);
            Write(_verifiedBuilds.Contains(build)
                ? $"build {build}: checked"
                : $"build {build}: not checked; every display uses the AI read");
        }
    }

    // ---- The log --------------------------------------------------------------------------------

    // Writes a call's failure unless it is the one written last since a message arrived (lastWritten is null until
    // one is written, and after each message). Called with the lock held.
    private void WriteFailure(ref string? lastWritten, string line)
    {
        if (lastWritten == line) return;

        lastWritten = line;
        Write(line);
    }

    // Each distinct problem once (a query that fails the same way every 30 s is one line), as
    // FenixDataService.LogProblem does.
    private void LogProblem(string problem)
    {
        lock (_problems)
        {
            if (_problems.Contains(problem)) return;
            if (_problems.Count >= MaxRememberedProblems) _problems.Clear();
            _problems.Add(problem);
        }

        Write(problem);
    }

    // A log that throws must not take a callback, the tick or the build loop down with it. After Dispose nothing
    // is written: the log's last line is Dispose's own, "channel stopped".
    private void Write(string line)
    {
        if (IsDisposed) return;

        WriteToLog(line);
    }

    // The call to the log itself, which never throws. Dispose uses it directly for the one line Write would drop.
    private void WriteToLog(string line)
    {
        try
        {
            _log(line);
        }
        catch (Exception)
        {
        }
    }

    // Cancels a loop that has ended. Never throws: a callback registered on its token may.
    private static void Cancel(CancellationTokenSource? source)
    {
        if (source is null) return;

        try
        {
            source.Cancel();
        }
        catch (Exception)
        {
        }
    }
}
