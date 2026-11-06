namespace MSFSBlindAssist.Services;

/// <summary>
/// Keeps the ND's map points current (<see cref="FenixChannelNdMapData"/>). Once a second it asks GetMapDataVersion and
/// fetches GetMapData only when the version differs from the one last fetched, so a quiet ND costs two bytes a second.
/// The loop runs on the thread pool and never touches a control: the window's composer reads <see cref="Latest"/> on
/// the UI thread, under the lock. Both calls are read-only ([FNX-21]) and go through the request function it is given
/// (the monitor's <see cref="FenixChannelMonitor.RequestOnceAsync"/>).
/// </summary>
/// <remarks>
/// A failure (a state other than answered, no message, a reply that does not decode) keeps the last good points,
/// sets the problem until the next success and waits <see cref="RetryMs"/> before the next try. The time of the
/// reply is the last time it was confirmed current: set on a fetch and on every version answer that matches the
/// fetched version, so a run of failures ages the reply out (<see cref="FenixChannelNdMapData.Check"/>).
/// </remarks>
internal sealed class FenixChannelNdMapPoller : IDisposable
{
    /// <summary>The wait between two version questions. Part C read the version once a second.</summary>
    public const int PollMs = 1000;

    /// <summary>The wait after a failure.</summary>
    public const int RetryMs = 5000;

    // A MapDataRequest with member 1 = true: the captain's.
    private static readonly byte[] CaptainRequest = [0x08, 0x01];

    private readonly Func<FenixChannelCall, byte[], CancellationToken, Task<FenixChannelUnaryResult>> _request;
    private readonly Func<long> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _cancel = new();
    private readonly object _lock = new();

    private (FenixNdMapPoints? Points, long ReceivedAtMs, string? Problem) _latest;
    private bool _started;
    private bool _disposed;
    private long? _fetchedVersion;

    public FenixChannelNdMapPoller(
        Func<FenixChannelCall, byte[], CancellationToken, Task<FenixChannelUnaryResult>> request, Func<long> clock)
        : this(request, clock, Task.Delay)
    {
    }

    /// <summary>For the tests: the waits are the given function's, so no test waits in real time.</summary>
    internal FenixChannelNdMapPoller(
        Func<FenixChannelCall, byte[], CancellationToken, Task<FenixChannelUnaryResult>> request, Func<long> clock,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _request = request;
        _clock = clock;
        _delay = delay;
    }

    /// <summary>The last good reply, when it was last confirmed current, and why the latest try failed (null when it did not).</summary>
    public (FenixNdMapPoints? Points, long ReceivedAtMs, string? Problem) Latest
    {
        get
        {
            lock (_lock) return _latest;
        }
    }

    /// <summary>Starts the loop, once. After <see cref="Dispose"/> it does nothing.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_started || _disposed) return;
            _started = true;

            CancellationToken token = _cancel.Token;
            Loop = Task.Run(() => RunAsync(token), token);
        }
    }

    /// <summary>The loop's task, for the tests to await after <see cref="Dispose"/>; null before <see cref="Start"/>.</summary>
    internal Task? Loop { get; private set; }

    public void Dispose()
    {
        Task? loop;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            loop = Loop;
        }

        _cancel.Cancel();

        // The loop may still be reading the token, so the source goes when the loop has ended.
        if (loop is null)
        {
            _cancel.Dispose();
        }
        else
        {
            loop.ContinueWith(_ => _cancel.Dispose(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                bool ok = await PollAsync(ct).ConfigureAwait(false);
                await _delay(TimeSpan.FromMilliseconds(ok ? PollMs : RetryMs), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed: nothing more is asked.
        }
    }

    // One poll: false when it failed (the problem is set).
    private async Task<bool> PollAsync(CancellationToken ct)
    {
        try
        {
            FenixChannelUnaryResult answer = await _request(FenixChannelCall.MapDataVersion, CaptainRequest, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return true;
            if (Failure(answer, "the map version") is { } versionProblem) return Fail(versionProblem);

            FenixChannelPush? versionReply = FenixChannelPush.From(answer.Message!);
            if (versionReply is null) return Fail("the map version reply is not valid protobuf");

            ulong version = 0;
            if (versionReply.TryGet(FenixChannelFields.MapDataVersionReply.Version, out FenixChannelField field))
            {
                if (field.Type != FenixWireType.Varint) return Fail("field 1 of the map version reply has the wrong shape");

                version = field.Varint;
            }

            if (_fetchedVersion is { } fetched && fetched == (long)Math.Min(version, long.MaxValue))
            {
                lock (_lock)
                {
                    if (!_disposed) _latest = (_latest.Points, _clock(), null);
                }

                return true;
            }

            FenixChannelUnaryResult data = await _request(FenixChannelCall.MapData, CaptainRequest, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return true;
            if (Failure(data, "the map points") is { } dataProblem) return Fail(dataProblem);

            FenixChannelSection<FenixNdMapPoints> decoded = FenixChannelNdMapData.Decode(data.Message!);
            if (!decoded.IsOk) return Fail(decoded.Problem ?? "no map points");

            lock (_lock)
            {
                if (_disposed) return true;

                _fetchedVersion = decoded.Value!.Version;
                _latest = (decoded.Value, _clock(), null);
            }

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return true;
        }
        catch (Exception ex)
        {
            return Fail($"the map read failed ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private static string? Failure(FenixChannelUnaryResult answer, string what)
    {
        if (answer.State != FenixChannelState.Connected)
        {
            return string.IsNullOrWhiteSpace(answer.Text) ? $"{what} is not answering" : answer.Text;
        }

        return answer.Message is null ? $"{what} came back empty" : null;
    }

    // Keeps the last good points and their time; only the problem changes.
    private bool Fail(string problem)
    {
        lock (_lock)
        {
            if (!_disposed) _latest = (_latest.Points, _latest.ReceivedAtMs, problem);
        }

        return false;
    }
}
