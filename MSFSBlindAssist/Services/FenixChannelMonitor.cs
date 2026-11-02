using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The owner of one session of the Fenix display channel: it runs the client's two calls, decodes each message,
/// feeds the trust gate under one lock, keeps the latest display frame, asks for the Fenix build on each
/// connection of the data call, and writes the channel's log (fenix_channel.log). A reader asks it for a verdict
/// per display and for the latest frame. The Fenix aircraft definition owns one monitor and disposes it when
/// the pilot switches away.
/// </summary>
/// <remarks>
/// <para>Threads. A call's messages and states arrive on the client's pool threads, comparison values and reads on
/// the UI thread, and the tick on a pool thread. One lock guards the gate and every field that decides what the
/// log says, so a verdict is always read from one whole state, and the log's lines come out in the order the gate
/// saw its events. Nothing under the lock waits for anything. A message is decoded before the lock is taken and fed
/// under it, and the latest frame is swapped in whole, so a reader on another thread never sees half of one.
/// Nothing here throws at a caller, speaks or touches a control, as with <see cref="FenixDataService"/>; every
/// clock reading is <see cref="Environment.TickCount64"/>, so the gate compares times from one source.</para>
/// <para>Each connection of the data call does three things, in this order, under the lock. The gate starts fresh.
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
/// headers, and a failure is written only when it differs from the last one written since a message arrived:
/// otherwise each try would write two lines. The ECAM entries call has no such cycle (nothing limits its
/// silence), so its "connected" is written with its headers, but its failures follow the same rule. This class
/// formats no number itself: the pairs come from the gate in the invariant culture.</para>
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
    /// How many ticks of a connection pass between two comparisons lines: 30 of the default one-second tick,
    /// so the log shows each comparison pair every 30 s while the channel is connected.
    /// </summary>
    public const int ComparisonLogEveryTicks = 30;

    // The var keys the Fenix definition already has: the flight director lights and the base class's altitude.
    private const string FdCaptainKey = "I_FCU_EFIS1_FD";
    private const string FdFirstOfficerKey = "I_FCU_EFIS2_FD";
    private const string AltitudeKey = "INDICATED_ALTITUDE";

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

    /// <summary>
    /// The latest display frame, swapped in whole as each message is decoded; null before the first. A push with
    /// no display data (<see cref="FenixChannelFrame.HasData"/> false) never replaces it. It may be stale or
    /// untrustworthy: ask <see cref="VerdictFor"/> before reading a value from it.
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
    /// Whether <paramref name="display"/> may be read to the pilot now, and the first reason it may not. A
    /// monitor that has been disposed trusts nothing: its frames have stopped, and the gate would take 2 s to
    /// notice.
    /// </summary>
    public FenixDisplayVerdict VerdictFor(FenixChannelDisplay display)
    {
        lock (_lock)
        {
            return IsDisposed
                ? new FenixDisplayVerdict(false, "channel stopped")
                : _trust.VerdictFor(display, Now);
        }
    }

    /// <summary>
    /// SimConnect's latest value for one comparison (the flight director lights as 0 or 1, the altitude in feet, the
    /// magnetic heading in degrees, the fuel in kilograms; see <see cref="TryMapComparison"/>), stamped now. Called
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
    /// Maps a SimConnect var key to the comparison it feeds. The flight director lights and the altitude are only
    /// peeked (<paramref name="consume"/> is false, so they carry on to their own handling); the two keys of the
    /// channel's own, the heading and the fuel, are consumed. The fuel arrives in pounds and leaves in kilograms.
    /// A key that is none of the five gives false and is never consumed.
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
    // row, or after Dispose, which a real stream will not do.

    /// <summary>The data call changed state. The client reports each change once, on a pool thread.</summary>
    internal void OnDataState(FenixChannelState state, string text)
    {
        if (IsDisposed) return;

        CancellationTokenSource? ended = null;
        lock (_lock)
        {
            if (IsDisposed) return;

            switch (state)
            {
                case FenixChannelState.Connected:
                    _dataConnected = true;
                    _dataMessageSeen = false;
                    _connectedTicks = 0;

                    // The order matters: see the remarks. No line is written yet: the headers say the call
                    // opened, not that it works.
                    _trust.OnConnected(Now);
                    if (_lastEntries is not null) _trust.OnEcamEntries(_lastEntries);
                    ended = StartBuildLoop();
                    break;

                case FenixChannelState.NotAnswering:
                    ended = DataCallDown(text);
                    WriteFailure(ref _lastDataFailure, $"display data: not answering ({text})");
                    break;

                case FenixChannelState.Refused:
                    ended = DataCallDown(text);
                    WriteFailure(ref _lastDataFailure, $"display data: refused ({text}); stopped for this session");
                    break;
            }
        }

        Cancel(ended);
    }

    /// <summary>One message of the data call: decoded outside the lock, fed to the gate under it.</summary>
    internal void OnDataMessage(byte[] message)
    {
        if (IsDisposed) return;

        FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(message);
        lock (_lock)
        {
            if (IsDisposed) return;

            // A message shows the call works, which the headers do not: only now is "connected" news, and a
            // failure that follows is news again even if it reads like the last one.
            _lastDataFailure = null;
            if (!_dataMessageSeen)
            {
                _dataMessageSeen = true;
                Write("display data: connected");
            }

            _trust.OnFrame(frame, Now);

            // An empty push is no data: the gate skips it, and so does the latest frame.
            if (frame.HasData) Volatile.Write(ref _latest, frame);
        }
    }

    /// <summary>
    /// The ECAM entries call changed state. Only the log hears of it: no verdict depends on this call being
    /// connected until a read uses it.
    /// </summary>
    internal void OnEcamState(FenixChannelState state, string text)
    {
        if (IsDisposed) return;

        lock (_lock)
        {
            if (IsDisposed) return;

            switch (state)
            {
                case FenixChannelState.Connected:
                    Write("ECAM entries: connected");
                    break;
                case FenixChannelState.NotAnswering:
                    WriteFailure(ref _lastEcamFailure, $"ECAM entries: not answering ({text})");
                    break;
                case FenixChannelState.Refused:
                    WriteFailure(ref _lastEcamFailure, $"ECAM entries: refused ({text}); stopped for this session");
                    break;
            }
        }
    }

    /// <summary>
    /// One message of the ECAM entries call: decoded outside the lock, then kept (the gate is given it again on
    /// each connection of the data call) and fed to the gate, which looks only at its shape.
    /// </summary>
    internal void OnEcamMessage(byte[] message)
    {
        if (IsDisposed) return;

        FenixEcamEntries entries = FenixChannelDecoder.DecodeEcamEntries(message);
        lock (_lock)
        {
            if (IsDisposed) return;

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
            // Dispose ran in between, and cancelled only what it found before this loop was installed.
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
            // Ended by a newer connection, a disconnect or Dispose: whatever the query threw on its way out is of
            // no interest.
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
            // An answer belongs to the connection that asked: after a newer connection, a disconnect or Dispose it
            // is no news, and must not reach the gate, where it would stand for a build the channel no longer runs.
            if (IsDisposed || generation != _generation) return;

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
