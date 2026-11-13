// Characterization tests for FenixDataPoller, the once-a-second reader of the Fenix data-service
// names its live subscription does not stream (engine N2 is measured to arrive once when a
// subscription opens and never again). The read is a fake delegate, never the network.
//
// Timing: every wait for something to happen is a signal (a TaskCompletionSource) with a generous
// timeout. The 200 ms pauses only prove that nothing MORE happens, so a slow machine can make them
// pass trivially but never fail.

using System.Globalization;
using MSFSBlindAssist.Services;
using Answer = System.Collections.Generic.IReadOnlyDictionary<string, MSFSBlindAssist.Services.FenixDataValue>;

namespace MSFSBlindAssist.Tests;

public class FenixDataPollerTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(200);

    // ---- Helpers --------------------------------------------------------------------------------

    private static FenixDataValue Num(double value)
        => new(FenixValueKind.Number, value, false, null, value.ToString(CultureInfo.InvariantCulture));

    private static Answer Answering(params (string Name, FenixDataValue Value)[] pairs)
        => pairs.ToDictionary(p => p.Name, p => p.Value);

    private static Task<Answer> Immediately(params (string Name, FenixDataValue Value)[] pairs)
        => Task.FromResult(Answering(pairs));

    // A read that never answers until its token is cancelled (Dispose), then throws like a real
    // cancelled HTTP call would.
    private static async Task<Answer> Held(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        return Answering();
    }

    // The fake read: counts the reads, records what each was asked and given, and answers through
    // the behaviour the test hands it.
    private sealed class Reader
    {
        private readonly Func<CancellationToken, Task<Answer>> _behavior;
        private readonly object _lock = new();
        private readonly List<(int Count, TaskCompletionSource Signal)> _waiters = new();
        private readonly List<string[]> _asked = new();
        private readonly List<CancellationToken> _tokens = new();
        private int _started;
        private int _inFlight;
        private int _maxInFlight;

        public Reader(Func<CancellationToken, Task<Answer>>? behavior = null)
            => _behavior = behavior ?? (_ => Immediately());

        public int Started { get { lock (_lock) return _started; } }
        public int MaxInFlight { get { lock (_lock) return _maxInFlight; } }
        public string[][] Asked { get { lock (_lock) return _asked.ToArray(); } }
        public CancellationToken[] Tokens { get { lock (_lock) return _tokens.ToArray(); } }

        // Completes once at least <count> reads have started.
        public Task WhenStarted(int count)
        {
            lock (_lock)
            {
                if (_started >= count) return Task.CompletedTask;
                var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((count, signal));
                return signal.Task;
            }
        }

        public async Task<Answer> Read(IReadOnlyList<string> names, CancellationToken ct)
        {
            lock (_lock)
            {
                _started++;
                _inFlight++;
                _maxInFlight = Math.Max(_maxInFlight, _inFlight);
                _asked.Add(names.ToArray());
                _tokens.Add(ct);
                foreach (var waiter in _waiters.Where(w => _started >= w.Count).ToList())
                {
                    waiter.Signal.TrySetResult();
                    _waiters.Remove(waiter);
                }
            }
            try
            {
                return await _behavior(ct).ConfigureAwait(false);
            }
            finally
            {
                lock (_lock) _inFlight--;
            }
        }
    }

    // What the poller handed to onValue, from a pool thread.
    private sealed class Seen
    {
        private readonly object _lock = new();
        private readonly List<(string Name, FenixDataValue Value)> _items = new();

        public void Add(string name, FenixDataValue value)
        {
            lock (_lock) _items.Add((name, value));
        }

        public (string Name, FenixDataValue Value)[] Items { get { lock (_lock) return _items.ToArray(); } }
    }

    // ---- PollOnceAsync --------------------------------------------------------------------------

    [Fact]
    public async Task PollOnce_hands_each_answered_name_to_the_callback_in_order()
    {
        // The answer lists b before a: the callback order is the names' order, not the answer's.
        var reader = new Reader(_ => Immediately(("b", FenixDataValue.Unknown), ("a", Num(1))));
        var seen = new Seen();
        using var poller = new FenixDataPoller(["a", "b"], reader.Read, seen.Add);

        await poller.PollOnceAsync();

        var items = seen.Items;
        Assert.Equal(["a", "b"], items.Select(i => i.Name));
        Assert.Equal(FenixValueKind.Number, items[0].Value.Kind);
        Assert.Equal(1, items[0].Value.Number);
        Assert.False(items[1].Value.IsKnown);
        Assert.Equal(FenixValueKind.EmptyOrUnknown, items[1].Value.Kind);
        Assert.Equal(1, reader.Started);
        Assert.Equal(["a", "b"], reader.Asked[0]);
    }

    [Fact]
    public async Task PollOnce_skips_a_name_the_answer_does_not_hold()
    {
        // The service answers every name it was asked (ReadManyAsync); a read that does not is
        // not turned into a value: only what the answer holds reaches the callback.
        var reader = new Reader(_ => Immediately(("b", Num(2)), ("other", Num(9))));
        var seen = new Seen();
        using var poller = new FenixDataPoller(["a", "b"], reader.Read, seen.Add);

        await poller.PollOnceAsync();

        Assert.Equal(["b"], seen.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task PollOnce_swallows_a_failing_read()
    {
        var reader = new Reader(_ => throw new InvalidOperationException("the gateway fell over"));
        var seen = new Seen();
        using var poller = new FenixDataPoller(["a"], reader.Read, seen.Add);

        await poller.PollOnceAsync();
        await poller.PollOnceAsync();

        Assert.Empty(seen.Items);
        Assert.Equal(2, reader.Started);
    }

    [Fact]
    public async Task PollOnce_swallows_a_throwing_callback_and_still_delivers_the_other_names()
    {
        var reader = new Reader(_ => Immediately(("a", Num(1)), ("b", Num(2))));
        var seen = new Seen();
        using var poller = new FenixDataPoller(["a", "b"], reader.Read, (name, value) =>
        {
            if (name == "a") throw new InvalidOperationException("a bad handler");
            seen.Add(name, value);
        });

        await poller.PollOnceAsync();

        Assert.Equal(["b"], seen.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task PollOnce_with_a_cancelled_token_does_not_read()
    {
        var reader = new Reader();
        using var poller = new FenixDataPoller(["a"], reader.Read, (_, _) => { });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await poller.PollOnceAsync(cancelled.Token);

        Assert.Equal(0, reader.Started);
    }

    [Fact]
    public async Task A_late_answer_after_Dispose_is_dropped()
    {
        // A read that ignores its token and answers after Dispose (a form already closed): nothing
        // reaches the callback.
        var gate = new TaskCompletionSource<Answer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new Reader(_ => gate.Task);
        var seen = new Seen();
        var poller = new FenixDataPoller(["a"], reader.Read, seen.Add);

        var poll = poller.PollOnceAsync();
        await reader.WhenStarted(1).WaitAsync(Generous);
        poller.Dispose();
        gate.SetResult(Answering(("a", Num(1))));
        await poll.WaitAsync(Generous);

        Assert.Empty(seen.Items);
    }

    // ---- Start ----------------------------------------------------------------------------------

    [Fact]
    public async Task Start_polls_until_disposed()
    {
        var reader = new Reader(_ => Immediately(("a", Num(1))));
        var seen = new Seen();
        var poller = new FenixDataPoller(["a"], reader.Read, seen.Add, TimeSpan.FromMilliseconds(10));

        poller.Start();
        await reader.WhenStarted(3).WaitAsync(Generous);
        poller.Dispose();
        int startedAtDispose = reader.Started;
        await Task.Delay(Quiet);

        Assert.NotEmpty(seen.Items);
        // A read already on its way when Dispose ran may still start; no more than that.
        Assert.True(reader.Started <= startedAtDispose + 1,
            $"{reader.Started} reads started, {startedAtDispose} at Dispose");
    }

    [Fact]
    public async Task Start_keeps_polling_after_a_failing_read()
    {
        var reader = new Reader(_ => throw new InvalidOperationException("the gateway fell over"));
        var poller = new FenixDataPoller(["a"], reader.Read, (_, _) => { }, TimeSpan.FromMilliseconds(10));

        poller.Start();
        await reader.WhenStarted(3).WaitAsync(Generous);
        poller.Dispose();
    }

    [Fact]
    public async Task Reads_never_overlap()
    {
        var gate = new TaskCompletionSource<Answer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new Reader(_ => gate.Task);
        using var poller = new FenixDataPoller(["a"], reader.Read, (_, _) => { }, TimeSpan.FromMilliseconds(1));

        poller.Start();
        await reader.WhenStarted(1).WaitAsync(Generous);
        await Task.Delay(Quiet);
        Assert.Equal(1, reader.Started);

        // Once the held read answers the loop goes on to the next one.
        gate.SetResult(Answering());
        await reader.WhenStarted(2).WaitAsync(Generous);
        Assert.Equal(1, reader.MaxInFlight);
    }

    [Fact]
    public async Task Start_twice_runs_one_loop_and_Start_after_Dispose_never_reads()
    {
        var reader = new Reader(Held);
        using (var poller = new FenixDataPoller(["a"], reader.Read, (_, _) => { }, TimeSpan.FromMilliseconds(1)))
        {
            poller.Start();
            poller.Start();
            await reader.WhenStarted(1).WaitAsync(Generous);
            await Task.Delay(Quiet);

            // A second loop would have started its own read, the first being held open.
            Assert.Equal(1, reader.Started);
        }

        var afterDispose = new Reader();
        var disposed = new FenixDataPoller(["a"], afterDispose.Read, (_, _) => { }, TimeSpan.FromMilliseconds(1));
        disposed.Dispose();

        disposed.Start();
        await Task.Delay(Quiet);

        Assert.Equal(0, afterDispose.Started);
    }

    [Fact]
    public async Task No_names_never_reads()
    {
        var reader = new Reader();
        using var poller = new FenixDataPoller([], reader.Read, (_, _) => { }, TimeSpan.FromMilliseconds(1));

        poller.Start();
        await poller.PollOnceAsync();
        await Task.Delay(Quiet);

        Assert.Equal(0, reader.Started);
    }

    // ---- Dispose --------------------------------------------------------------------------------

    [Fact]
    public async Task Dispose_cancels_the_reads_token()
    {
        var reader = new Reader(Held);
        var poller = new FenixDataPoller(["a"], reader.Read, (_, _) => { }, TimeSpan.FromMilliseconds(1));

        poller.Start();
        await reader.WhenStarted(1).WaitAsync(Generous);
        var token = reader.Tokens[0];
        Assert.False(token.IsCancellationRequested);

        poller.Dispose();

        Assert.True(token.IsCancellationRequested);
        // The cancelled read ends the loop quietly: it does not read again.
        await Task.Delay(Quiet);
        Assert.Equal(1, reader.Started);
    }

    [Fact]
    public async Task Dispose_is_idempotent_and_never_throws()
    {
        var never = new FenixDataPoller(["a"], new Reader().Read, (_, _) => { });
        never.Dispose();
        never.Dispose();

        // A callback registered on the read's token that throws on cancel must not reach the caller.
        var reader = new Reader(async ct =>
        {
            ct.Register(() => throw new InvalidOperationException("a bad cancel callback"));
            return await Held(ct).ConfigureAwait(false);
        });
        var running = new FenixDataPoller(["a"], reader.Read, (_, _) => { });
        running.Start();
        await reader.WhenStarted(1).WaitAsync(Generous);

        running.Dispose();
        running.Dispose();
    }

    // ---- Construction ---------------------------------------------------------------------------

    [Fact]
    public void The_default_interval_is_one_second()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), FenixDataPoller.DefaultInterval);
    }

    [Fact]
    public async Task The_names_are_copied_and_blank_ones_dropped()
    {
        var reader = new Reader();
        var names = new List<string> { "a", "", null!, "b" };
        using var poller = new FenixDataPoller(names, reader.Read, (_, _) => { });
        names.Add("c");

        await poller.PollOnceAsync();

        Assert.Equal(["a", "b"], reader.Asked[0]);
    }

    [Fact]
    public void A_missing_argument_or_a_negative_interval_is_refused()
    {
        var reader = new Reader();
        Assert.Throws<ArgumentNullException>(() => new FenixDataPoller(null!, reader.Read, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => new FenixDataPoller(["a"], null!, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => new FenixDataPoller(["a"], reader.Read, null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FenixDataPoller(["a"], reader.Read, (_, _) => { }, TimeSpan.FromMilliseconds(-5)));
    }
}
