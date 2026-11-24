using System.Collections.Concurrent;

namespace MSFSBlindAssist.Accessibility;

/// <summary>
/// Runs every call to a set of screen reader backends on one thread of its own: starting them (Prism's context),
/// connecting to a reader, and each Speak, Braille and close of a connection.
///
/// <para>Announcements come from the UI thread and from background timers (the First Officer managers), and
/// <see cref="ScreenReaderOutput"/> shares one connection between them. Prism reaches JAWS over COM, where a call
/// from another thread than the one that made the connection is normally refused; before Prism, Tolk failed
/// silently off the UI thread for the same reason. With one thread the question does not arise, for any reader.
/// The thread is a single-threaded apartment, like the UI thread NVDA and ZDSR were measured on.</para>
///
/// <para>A caller waits for its call and gets its result or its exception, so a reader that refuses a message
/// still moves <see cref="ScreenReaderOutput"/> to the next one. The thread is a background thread and lives as
/// long as the process; it ends at once when the backends cannot start.</para>
/// </summary>
internal sealed class SingleThreadScreenReaderBackends : IScreenReaderBackends
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;
    private readonly IScreenReaderBackends _inner;

    public SingleThreadScreenReaderBackends(Func<IScreenReaderBackends> innerFactory)
    {
        _thread = new Thread(Pump) { IsBackground = true, Name = "Screen reader output" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        try
        {
            _inner = Run(innerFactory);
            Candidates = Run(() => _inner.Candidates.ToList());
        }
        catch
        {
            _work.CompleteAdding();   // nothing will ever be asked of this thread
            throw;
        }
    }

    public IReadOnlyList<ScreenReaderCandidate> Candidates { get; }

    public IScreenReaderBackend? TryCreate(ScreenReaderCandidate candidate) => Run<IScreenReaderBackend?>(() =>
    {
        var reader = _inner.TryCreate(candidate);
        // What the reader can do is read here, once: Prism computes it when the connection is made.
        return reader == null ? null : new OnThread(this, reader, reader.Name, reader.CanSpeak, reader.CanBraille);
    });

    private void Pump()
    {
        foreach (var call in _work.GetConsumingEnumerable()) call();
    }

    private T Run<T>(Func<T> call)
    {
        if (Thread.CurrentThread == _thread) return call();
        var done = new TaskCompletionSource<T>();
        _work.Add(() =>
        {
            try { done.SetResult(call()); }
            catch (Exception ex) { done.SetException(ex); }
        });
        return done.Task.GetAwaiter().GetResult();   // rethrows the call's own exception
    }

    private void Run(Action call) => Run(() => { call(); return true; });

    private sealed class OnThread : IScreenReaderBackend
    {
        private readonly SingleThreadScreenReaderBackends _owner;
        private readonly IScreenReaderBackend _reader;

        public OnThread(SingleThreadScreenReaderBackends owner, IScreenReaderBackend reader, string name, bool canSpeak, bool canBraille)
        {
            _owner = owner;
            _reader = reader;
            Name = name;
            CanSpeak = canSpeak;
            CanBraille = canBraille;
        }

        public string Name { get; }
        public bool CanSpeak { get; }
        public bool CanBraille { get; }
        public void Speak(string text, bool interrupt) => _owner.Run(() => _reader.Speak(text, interrupt));
        public void Braille(string text) => _owner.Run(() => _reader.Braille(text));
        public void Dispose() => _owner.Run(() => _reader.Dispose());
    }
}
