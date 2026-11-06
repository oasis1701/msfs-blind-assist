using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// What <see cref="FenixChannelMonitor.Snapshot"/> hands a reader of the Fenix display channel: everything it needs for
/// one display, taken under the monitor's one lock at the same instant, so the parts always belong together. A feature
/// takes its verdict and its data from one snapshot, never from two reads ([FNX-22]).
/// </summary>
/// <param name="Verdict">Whether the display may be read to the pilot now, and the first reason it may not.</param>
/// <param name="Frame">
/// The latest display frame, the one the gate judged in giving <paramref name="Verdict"/>; null before the first good
/// frame of a connection.
/// </param>
/// <param name="Push">
/// The RemoteData message that <paramref name="Frame"/> was decoded from, for a feature to decode its own fields from.
/// It is replaced and cleared with the frame, so the two belong together; null when the frame is.
/// </param>
/// <param name="Entries">
/// The latest ECAM entries message that is not a bare heartbeat (the heartbeat set and no entries), null before the
/// first of the ECAM entries call's connection. A connection of the data call does not clear it; one of the ECAM
/// entries call does. A feature looks at its <see cref="FenixEcamEntries.ShapesOk"/> before it reads the entries.
/// </param>
/// <param name="AtMs">
/// The monitor's clock when the snapshot was taken (<see cref="Environment.TickCount64"/>): the instant the verdict was
/// judged at.
/// </param>
/// <param name="Connection">
/// The number of the data call's connection: 0 before the first connect, and one more with each. A reader that sees a
/// number other than the one it saw last has a new connection, and takes its next good reading silently, without
/// depending on how long the client waited before it reconnected.
/// </param>
/// <param name="EcamConnection">
/// The number of the ECAM entries call's connection, counted the same way: 0 before the first connect, and one more
/// with each. A list belongs to the connection that sent it, so a reader of <paramref name="Entries"/> that sees a
/// number other than the one it saw last forgets the list it remembered.
/// </param>
internal readonly record struct FenixChannelSnapshot(
    FenixDisplayVerdict Verdict, FenixChannelFrame? Frame, FenixChannelPush? Push, FenixEcamEntries? Entries,
    long AtMs, int Connection, int EcamConnection = 0);

/// <summary>
/// What <see cref="FenixChannelMonitor.Session"/> hands the session notice (<see cref="FenixChannelNotice"/>): the few
/// facts about one session of the channel that decide whether the pilot is told it cannot be used, taken under the
/// monitor's one lock at the same instant. It carries no display data and no verdict, and unlike a
/// <see cref="FenixChannelSnapshot"/> it outlasts a reconnect: the gate forgets the build at each connection and the
/// latest frame is cleared, while the session keeps what it learned.
/// </summary>
/// <param name="Build">
/// The latest build the build query answered, null before the first answer. It is not cleared by a reconnect, and an
/// answer from a loop that has ended never reaches it.
/// </param>
/// <param name="BuildChecked">
/// Whether <paramref name="Build"/> is one the channel has been checked against (the monitor's verified set holds it);
/// false while there is no build.
/// </param>
/// <param name="Refused">
/// Whether either call was refused (grpc-status 7 or 16, or HTTP 401 or 403), which ends the channel for the session
/// ([FNX-21]). A call that is merely not answering is no refusal.
/// </param>
/// <param name="FirstDataAtMs">
/// The monitor's clock (<see cref="Environment.TickCount64"/>) when the first frame with data and every shape right
/// arrived, the test that makes a frame the latest one; null while none has. Kept across reconnects, so a gap after the
/// first data never reads as a channel that said nothing.
/// </param>
/// <param name="StartedAtMs">
/// The monitor's clock when <see cref="FenixChannelMonitor.Start"/> ran; 0 before it.
/// </param>
internal readonly record struct FenixChannelSession(
    string? Build, bool BuildChecked, bool Refused, long? FirstDataAtMs, long StartedAtMs);

/// <summary>
/// The owner of one session of the Fenix display channel: it runs the client's two streams, decodes each message,
/// feeds the trust gate under one lock, keeps the latest display frame with the RemoteData message it was decoded
/// from and the latest ECAM entries, asks for the Fenix build on each connection of the data call, and writes the
/// channel's log (fenix_channel.log). A reader asks it for a snapshot: a display's verdict, the latest frame, that
/// frame's message, the latest ECAM entries, the clock and the connection number, taken together. The session notice
/// asks it for the session instead (<see cref="Session"/>): the facts that outlast a reconnect. The Fenix aircraft
/// definition owns one monitor and disposes it when the pilot switches away.
/// </summary>
/// <remarks>
/// <para>Threads. A call's messages and states arrive on the client's pool threads, reads on the UI thread, and the
/// tick on a pool thread. One lock guards the gate and
/// every field that decides what the log says, so a verdict is always read from one whole state, and the log's lines
/// come out in the order the gate saw its events. Nothing under the lock waits for anything. A message is decoded
/// before the lock is taken and fed under it. The latest frame is swapped in whole with the RemoteData message it
/// was decoded from, and a snapshot reads both under the same lock as the verdict, so a reader never sees half of a
/// frame, a frame beside the message of another push, nor a frame beside a verdict the gate gave on another one.
/// Nothing here throws at a caller, speaks or touches a control, as with <see cref="FenixDataService"/>; every
/// clock reading is <see cref="Environment.TickCount64"/>, so the gate compares times from one source.</para>
/// <para>Each connection of the data call does three things, in this order, under the lock. The gate starts fresh,
/// the connection number goes up, and the latest frame is cleared with its message, so no frame or message of an
/// earlier connection is read.
/// Then the gate is given the last ECAM entries message again: that stream sends only when its list changes, so a
/// wrong-shaped message that arrived before this connection is the last thing it said, and the gate's fresh start
/// would otherwise erase the latch that message set. Then a build loop starts, which asks for the build until it
/// is answered. A loop belongs to the connection that started it (its generation): a newer connection, a
/// disconnect and Dispose all end it, and an answer that still comes back from an ended one is dropped, never
/// given to the gate.</para>
/// <para>The ECAM entries are kept twice, for two readers. The last message of any kind is what the gate is given
/// again at each connection. The list a snapshot hands out is the last message that is not a bare heartbeat (the
/// heartbeat set and no entries): the stream sends a heartbeat to say it is alive and a list when its list changes,
/// so a heartbeat taken for a list would empty it at every beat, and every alert would seem to clear. A connection of
/// the data call does not clear that list either: that stream sends only when its list changes. A connection of the
/// ECAM entries call clears both and counts one more ECAM connection (<see cref="FenixChannelSnapshot.EcamConnection"/>):
/// what the call said before belongs to the connection that ended, perhaps to a Fenix that has since restarted, and
/// a list from it compared with the next one would announce every alert of the earlier session as cleared.</para>
/// <para>The session. The session notice needs a few facts that the gate and the latest frame cannot give it, because
/// they start fresh at each connection: <see cref="Session"/> hands them out under the same lock. The build is the
/// latest one the build query answered, and a reconnect does not clear it (the gate takes the next answer and meanwhile
/// reads "build unknown"; an answer from a loop that has ended is dropped here by the same generation test that keeps
/// it from the gate). It is checked when the verified set holds it. The refusal is the flag a refusal of either call
/// sets (below). The first data is the clock reading of the first frame that becomes the latest one (data, every shape
/// right), and the start is the reading when <see cref="Start"/> ran. None of them is ever cleared, so a gap in the
/// stream, or a reconnect, changes nothing the notice reads.</para>
/// <para>The log. A line is written for each change of a call's state, for each build, and for each change of a
/// display's verdict (its Trusted, or the headline of its reason: the text before the first colon, so the text of a
/// failure that changes while the channel stays down is one line). A feature that cannot decode a section of
/// a push reports why with <see cref="ReportFeatureProblem"/>, and the line "&lt;feature&gt;: &lt;problem&gt;" is written
/// once for each distinct problem. Dispose writes one more, "channel stopped",
/// the last line the log ever gets (the in-sim check that an aircraft switch ended the channel reads it). A data
/// call that opens and says nothing is dropped and asked again for hours, so "connected" is written with the first
/// message and not with the response headers, and a call that is not answering is written only when its reason
/// differs from the last one written since a message arrived: otherwise each try would write two lines. The ECAM
/// entries call follows the same rules: the client drops it after its own silence limit too, three of its 5 s
/// heartbeats missed. A refusal is written once, since it ends the channel (below).</para>
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
    private const string LogChannelName = "fenix_channel";
    private const string StoppedLine = "channel stopped";
    private const int MaxRememberedProblems = 32;

    // In the order the log writes them.
    private static readonly FenixChannelDisplay[] Displays =
    {
        FenixChannelDisplay.Pfd, FenixChannelDisplay.Ewd, FenixChannelDisplay.Sd,
        FenixChannelDisplay.Nd, FenixChannelDisplay.Isis, FenixChannelDisplay.Dcdu
    };

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
    // read without it, through Volatile. _latestMessage is the RemoteData message _latest was decoded from, replaced
    // and cleared with it. Of the ECAM entries messages, _lastEntries is the last of any kind, which the gate is given
    // again at each connection of the data call, and _ecamList the last that is not a bare heartbeat, which a snapshot
    // hands out; a connection of the ECAM entries call clears both. _connection counts the connections of the data
    // call, and _ecamConnection those of the ECAM entries call.
    private FenixChannelFrame? _latest;
    private FenixChannelPush? _latestMessage;
    private FenixEcamEntries? _lastEntries;
    private FenixEcamEntries? _ecamList;
    private int _connection;
    private int _ecamConnection;

    // What Session hands the session notice, guarded by _lock and never cleared by a connection: the latest build the
    // query answered, the clock reading of the first frame with data and every shape right, and the clock reading when
    // Start ran (0 before it).
    private string? _sessionBuild;
    private long? _firstDataAtMs;
    private long _startedAtMs;

    private bool _dataMessageSeen;
    private bool _ecamMessageSeen;
    private string? _lastDataFailure;
    private string? _lastEcamFailure;
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

    /// <summary>The one clock of the channel, for frames and verdicts alike.</summary>
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
    /// Opens both calls and starts the tick, and notes the clock for the session (<see cref="Session"/>), before the
    /// calls open. Once only: a second call, and a call after <see cref="Dispose"/>, do nothing.
    /// </summary>
    public void Start()
    {
        if (IsDisposed || Interlocked.Exchange(ref _started, 1) != 0) return;

        lock (_lock) _startedAtMs = Now;
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

    private static readonly TimeSpan UnaryTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// One read-only map call (MapDataVersion or MapData) through the monitor's own client, limited to 5 s. Once
    /// the channel is refused or disposed it returns at once without a call. A refusal ends the channel for the
    /// session exactly as a stream's does ([FNX-21]) and is logged once. Any other failure is the result's text for
    /// the caller to log and retry after a wait; it changes nothing here.
    /// </summary>
    public async Task<FenixChannelUnaryResult> RequestOnceAsync(
        FenixChannelCall call, byte[] request, CancellationToken ct)
    {
        if (IsStopped) return new(FenixChannelState.NotAnswering, null, "the channel is stopped");

        FenixChannelUnaryResult result = await _client
            .CallOnceAsync(call, request, UnaryTimeout, ct).ConfigureAwait(false);
        if (result.State != FenixChannelState.Refused) return result;

        CancellationTokenSource? ended = null;
        bool refusedNow = false;
        lock (_lock)
        {
            if (!IsStopped)
            {
                ended = Refuse(call.ToString(), result.Text);
                refusedNow = true;
            }
        }

        Cancel(ended);
        if (refusedNow) StopClient();
        return result;
    }

    /// <summary>
    /// Everything a reader needs for <paramref name="display"/>, taken under the one lock at the same instant (see
    /// <see cref="FenixChannelSnapshot"/>): whether it may be read to the pilot now and the first reason it may not,
    /// the latest frame the gate judged in giving that verdict, the RemoteData message that frame was decoded from,
    /// the latest ECAM entries, the monitor's clock and the connection number. The verdict is judged at the clock
    /// the snapshot carries. A reader takes all of it from one snapshot and never in two steps, or its parts would not
    /// belong together: an older frame beside a newer verdict or the reverse, or a frame beside another push's
    /// message. The frame and its message are null before the first of a connection. A monitor that has been disposed
    /// trusts nothing ("channel stopped"): its frames have stopped, and the gate would take 2 s to notice. A stopped
    /// monitor, disposed or ended by a refusal, still hands out the last frame, message and list it had, beside its
    /// untrusted verdict: a reader speaks nothing from them without a trusted one.
    /// </summary>
    public FenixChannelSnapshot Snapshot(FenixChannelDisplay display)
    {
        lock (_lock)
        {
            long now = Now;
            FenixDisplayVerdict verdict = IsDisposed
                ? new FenixDisplayVerdict(false, "channel stopped")
                : _trust.VerdictFor(display, now);
            return new FenixChannelSnapshot(verdict, _latest, _latestMessage, _ecamList, now, _connection, _ecamConnection);
        }
    }

    /// <summary>
    /// The verdict of <see cref="Snapshot"/> alone: whether <paramref name="display"/> may be read to the pilot
    /// now, and the first reason it may not. For the tests: a read takes the whole snapshot, so that its frame and
    /// its verdict come together.
    /// </summary>
    public FenixDisplayVerdict VerdictFor(FenixChannelDisplay display) => Snapshot(display).Verdict;

    /// <summary>
    /// The facts about this session of the channel that the session notice reads (see
    /// <see cref="FenixChannelSession"/>), taken under the one lock so that they belong together: the latest build the
    /// query answered and whether the monitor's verified set holds it, whether either call was refused, the clock
    /// reading of the first frame with data and every shape right, and the clock reading when <see cref="Start"/> ran.
    /// None of it is cleared by a reconnect. Callable from any thread; a monitor that has been disposed or refused
    /// still hands out what it knew.
    /// </summary>
    public FenixChannelSession Session
    {
        get
        {
            lock (_lock)
            {
                string? build = _sessionBuild;
                return new FenixChannelSession(
                    build,
                    build is not null && _verifiedBuilds.Contains(build),
                    Volatile.Read(ref _refused) != 0,
                    _firstDataAtMs,
                    _startedAtMs);
            }
        }
    }

    /// <summary>
    /// A feature that cannot decode a section of a push reports why: the log gets "&lt;feature&gt;: &lt;problem&gt;" once
    /// for each distinct line, with the size limit the monitor's own problems have, so a decoder that finds the same
    /// fault in each of 17 pushes a second writes one line, not 17. Callable from any thread; never throws; after
    /// <see cref="Dispose"/> it writes nothing.
    /// </summary>
    public void ReportFeatureProblem(string feature, string problem) => LogProblem($"{feature}: {problem}");

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
                    _dataMessageSeen = false;

                    // The order matters: see the remarks. No line is written yet: the headers say the call
                    // opened, not that it works. The gate starts fresh, and so does the latest frame with its
                    // message: a frame or a message of the connection before is never read. The ECAM list stays.
                    _trust.OnConnected(Now);
                    Volatile.Write(ref _latest, null);
                    _latestMessage = null;
                    _connection++;
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

    /// <summary>
    /// One message of the data call: decoded outside the lock, fed to the gate under it. Its RemoteData message is
    /// taken outside the lock too, beside the decoder, and kept under the lock only with the frame.
    /// </summary>
    internal void OnDataMessage(byte[] message)
    {
        if (IsStopped) return;

        FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(message);
        FenixChannelPush? remote = RemoteDataOf(message);
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
            // none from a malformed or wrong-shaped frame, which latches it instead. The message is kept with the
            // frame, in the same place and by the same test, so it is replaced exactly when the frame is. (A push
            // with field 1 twice would give the frame the merged values of both parts, and the message only the
            // last part; protobuf-net never sends one.) The first such frame also stamps the session's first data,
            // once and for good: a stream that only ever sends frames the test turns away has not delivered, and
            // after a good frame nothing it sends unsays that.
            if (frame.HasData && frame.ShapesOk)
            {
                Volatile.Write(ref _latest, frame);
                _latestMessage = remote;
                _firstDataAtMs ??= Now;
            }
        }
    }

    // The RemoteData message of a display push: the part in field 1, read as a message of its own. Null when the push
    // is not valid protobuf, has no such part, or the part is not a message. The push holds the bytes without copying
    // them: the client hands every message over in an array of its own (FenixChannelMessageReader.TryTake slices with
    // ToArray), which nothing writes to again.
    private static FenixChannelPush? RemoteDataOf(byte[] message)
    {
        FenixChannelPush? push = FenixChannelPush.From(message);
        return push is not null && push.TryGet(FenixChannelFields.Push.PrimaryData, out FenixChannelField part)
            ? part.AsMessage()
            : null;
    }

    /// <summary>
    /// The ECAM entries call changed state. No verdict depends on this call being connected. A connection starts its
    /// list over: the messages of the connection before are dropped and the ECAM connection number goes up (see the
    /// remarks), and "connected" is written with its first message, as for the data call. Its refusal ends the whole
    /// channel, as one of the data call does.
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
                    // No line yet: the headers say the call opened, not that it works. A gate latch the dropped
                    // messages set stays until the data call's next connection, as every latch does.
                    _ecamMessageSeen = false;
                    _lastEntries = null;
                    _ecamList = null;
                    _ecamConnection++;
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
    /// each connection of the data call) and fed to the gate, which looks only at its shape. A message that is not
    /// a bare heartbeat is also kept as the list a snapshot hands out.
    /// </summary>
    internal void OnEcamMessage(byte[] message)
    {
        if (IsStopped) return;

        FenixEcamEntries entries = FenixChannelDecoder.DecodeEcamEntries(message);
        lock (_lock)
        {
            if (IsStopped) return;

            // As for the data call: a message shows the call works, so only now is "connected" news.
            _lastEcamFailure = null;
            if (!_ecamMessageSeen)
            {
                _ecamMessageSeen = true;
                Write("ECAM entries: connected");
            }

            _lastEntries = entries;
            if (!IsBareHeartbeat(entries)) _ecamList = entries;
            _trust.OnEcamEntries(entries);
        }
    }

    // A heartbeat with no entries in it. The stream sends a heartbeat to say it is alive and a list when its list
    // changes, so a bare heartbeat is not a list that emptied.
    private static bool IsBareHeartbeat(FenixEcamEntries entries) => entries.Heartbeat && entries.Entries.Count == 0;

    // ---- The tick -------------------------------------------------------------------------------

    /// <summary>
    /// One tick of the timer: writes each display's verdict when its Trusted or the headline of its reason has
    /// changed since it was last written (the first tick writes all six).
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

    // The reason up to its first colon. "channel not answering: connection reset" is "channel not answering"; a reason
    // with no colon ("build 3.0.0.7613 not checked") is its own headline.
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
        FenixChannelDisplay.Nd => "ND",
        FenixChannelDisplay.Isis => "ISIS",
        FenixChannelDisplay.Dcdu => "DCDU",
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
            // longer runs, nor the session, which names the latest build the channel has answered.
            if (IsStopped || generation != _generation) return;

            _sessionBuild = build;
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
