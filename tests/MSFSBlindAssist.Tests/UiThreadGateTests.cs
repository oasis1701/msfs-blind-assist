using System.Runtime.ExceptionServices;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the decision behind SimConnectManager.RequestVariable's UI-thread post (review round 2,
/// B1). The MD-11's walks and read-backs reach ReadFreshAsync from thread-pool continuations,
/// and SimConnect is not thread-safe. On the UI thread — or with no captured context — the caller
/// runs inline exactly as before; from any other thread the work is posted once and the caller
/// must not run it itself.
/// </summary>
public class UiThreadGateTests
{
    /// <summary>Records posts instead of running them, so a test sees exactly what reached the "UI thread".</summary>
    private sealed class RecordingContext : SynchronizationContext
    {
        private readonly List<(SendOrPostCallback Callback, object? State)> _posted = new();

        public int PostCount { get { lock (_posted) return _posted.Count; } }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_posted) _posted.Add((d, state));
        }

        public void RunPosted()
        {
            List<(SendOrPostCallback Callback, object? State)> batch;
            lock (_posted)
            {
                batch = new(_posted);
                _posted.Clear();
            }
            foreach (var (callback, state) in batch) callback(state);
        }
    }

    /// <summary>A UI thread that is gone — WinForms' BeginInvoke once the thread's window has closed.</summary>
    private sealed class ClosedContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException("Invoke or BeginInvoke cannot be called on a control until the window handle has been created.");
    }

    /// <summary>Runs <paramref name="body"/> on a dedicated thread — never the test's own.</summary>
    private static T OnAnotherThread<T>(Func<T> body)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    [Fact]
    public void OnTheUiThread_TheCallerRunsInline_AndNothingIsPosted()
    {
        var context = new RecordingContext();
        var gate = new UiThreadGate(context, Environment.CurrentManagedThreadId);
        var ran = 0;

        Assert.True(gate.IsOnUiThread);
        Assert.False(gate.PostIfOffThread(() => ran++));
        Assert.Equal(0, context.PostCount);
        Assert.Equal(0, ran);                      // the gate never runs it — the caller does, inline
    }

    [Fact]
    public void FromABackgroundThread_TheWorkIsPostedExactlyOnce_AndRunsOnlyWhenTheUiThreadRunsIt()
    {
        var context = new RecordingContext();
        var gate = new UiThreadGate(context, Environment.CurrentManagedThreadId);
        var ran = 0;

        var (posted, onUiThread) = OnAnotherThread(() => (gate.PostIfOffThread(() => ran++), gate.IsOnUiThread));

        Assert.True(posted);                       // the caller must return without doing the work
        Assert.False(onUiThread);
        Assert.Equal(1, context.PostCount);
        Assert.Equal(0, ran);                      // nothing ran on the calling thread
        context.RunPosted();
        Assert.Equal(1, ran);
    }

    [Fact]
    public void WithNoCapturedContext_EveryCallerRunsInline()
    {
        var gate = new UiThreadGate(context: null, Environment.CurrentManagedThreadId);

        var posted = OnAnotherThread(() => gate.PostIfOffThread(() => { }));

        Assert.False(posted);                      // today's behaviour: nothing to post to
    }

    [Fact]
    public void AUiThreadThatCanNoLongerAcceptAPost_DropsTheWork_AndStillTellsTheCallerToReturn()
    {
        var gate = new UiThreadGate(new ClosedContext(), Environment.CurrentManagedThreadId);
        var ran = 0;

        var posted = OnAnotherThread(() => gate.PostIfOffThread(() => ran++));

        Assert.True(posted);                       // never run on the wrong thread instead
        Assert.Equal(0, ran);
    }
}
