using System.Collections.Concurrent;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Announcements come from the UI thread and from background timers (the First Officer managers), and one
/// connection to the screen reader is shared by all of them. JAWS is reached over COM, where a call from another
/// thread than the one that made the connection is refused, so every call to Prism goes through one thread.
/// </summary>
public class SingleThreadScreenReaderBackendsTests
{
    private sealed class Recorder
    {
        public readonly ConcurrentBag<(string Call, int Thread)> Calls = new();
        public ApartmentState FactoryApartment;
        public void Note(string call) => Calls.Add((call, Environment.CurrentManagedThreadId));
    }

    private sealed class RecordingReader : IScreenReaderBackend
    {
        private readonly Recorder _recorder;
        public RecordingReader(Recorder recorder) => _recorder = recorder;
        public bool Throw;
        public string Name { get { _recorder.Note("Name"); return "JAWS"; } }
        public bool CanSpeak { get { _recorder.Note("CanSpeak"); return true; } }
        public bool CanBraille { get { _recorder.Note("CanBraille"); return true; } }
        public void Speak(string text, bool interrupt)
        {
            _recorder.Note("Speak");
            if (Throw) throw new InvalidOperationException("JAWS closed");
        }
        public void Braille(string text) => _recorder.Note("Braille");
        public void Dispose() => _recorder.Note("Dispose");
    }

    private sealed class RecordingSource : IScreenReaderBackends
    {
        private readonly Recorder _recorder;
        public RecordingSource(Recorder recorder) => _recorder = recorder;
        public readonly ScreenReaderCandidate Jaws = new(3, "JAWS", 100);
        public readonly ScreenReaderCandidate NotRunning = new(1, "NVDA", 103);
        public RecordingReader? Last;
        public IReadOnlyList<ScreenReaderCandidate> Candidates
        {
            get { _recorder.Note("Candidates"); return new[] { NotRunning, Jaws }; }
        }
        public IScreenReaderBackend? TryCreate(ScreenReaderCandidate candidate)
        {
            _recorder.Note("TryCreate");
            return candidate == Jaws ? Last = new RecordingReader(_recorder) : null;
        }
    }

    private static (SingleThreadScreenReaderBackends Backends, RecordingSource Source, Recorder Recorder) Make()
    {
        var recorder = new Recorder();
        RecordingSource? source = null;
        var backends = new SingleThreadScreenReaderBackends(() =>
        {
            recorder.Note("Factory");
            recorder.FactoryApartment = Thread.CurrentThread.GetApartmentState();
            return source = new RecordingSource(recorder);
        });
        return (backends, source!, recorder);
    }

    private static void OnAnotherThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.Start();
        thread.Join();
        if (error != null) throw new Xunit.Sdk.XunitException("The other thread threw: " + error);
    }

    [Fact]
    public void Every_call_reaches_the_reader_on_the_same_thread_whoever_makes_it()
    {
        var (backends, source, recorder) = Make();

        var reader = backends.TryCreate(source.Jaws)!;
        _ = reader.Name; _ = reader.CanSpeak; _ = reader.CanBraille;
        reader.Speak("From the test thread", false);
        OnAnotherThread(() =>
        {
            reader.Speak("From a background timer", true);
            reader.Braille("From a background timer");
        });
        OnAnotherThread(() =>
        {
            _ = backends.Candidates;
            Assert.Null(backends.TryCreate(source.NotRunning));
            reader.Dispose();
        });

        var calls = recorder.Calls.Select(c => c.Call).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Braille", "CanBraille", "CanSpeak", "Candidates", "Dispose", "Factory", "Name", "Speak", "TryCreate" }, calls);
        int thread = Assert.Single(recorder.Calls.Select(c => c.Thread).Distinct());
        Assert.NotEqual(Environment.CurrentManagedThreadId, thread);
    }

    [Fact]
    public void That_thread_is_a_single_threaded_apartment_like_the_UI_thread_the_readers_were_measured_on()
    {
        var (_, _, recorder) = Make();

        Assert.Equal(ApartmentState.STA, recorder.FactoryApartment);
    }

    [Fact]
    public void The_candidates_keep_their_order()
    {
        var (backends, source, _) = Make();

        Assert.Equal(new[] { source.NotRunning, source.Jaws }, backends.Candidates);
    }

    [Fact]
    public void A_reader_that_refuses_a_message_throws_the_same_error_to_the_caller()
    {
        var (backends, source, _) = Make();
        var reader = backends.TryCreate(source.Jaws)!;
        source.Last!.Throw = true;

        // ScreenReaderOutput moves to the next reader on this exception, so it must cross the thread.
        var error = Assert.Throws<InvalidOperationException>(() => reader.Speak("Gear down", false));
        Assert.Equal("JAWS closed", error.Message);
    }

    [Fact]
    public void A_source_that_cannot_start_throws_to_the_caller()
    {
        // prism.dll missing: ScreenReaderOutput catches this and leaves speech to SAPI.
        var error = Assert.Throws<DllNotFoundException>(() =>
            new SingleThreadScreenReaderBackends(() => throw new DllNotFoundException("prism.dll")));
        Assert.Equal("prism.dll", error.Message);
    }

    [Fact]
    public void A_whole_message_goes_through_ScreenReaderOutput_from_any_thread()
    {
        var (backends, _, recorder) = Make();
        var output = new ScreenReaderOutput(() => backends, () => 100_000);

        Assert.True(output.TrySpeak("From the test thread", false));
        bool spoken = false;
        OnAnotherThread(() => spoken = output.TrySpeak("From a background timer", true));

        Assert.True(spoken);
        Assert.Equal(2, recorder.Calls.Count(c => c.Call == "Speak"));
        Assert.Single(recorder.Calls.Select(c => c.Thread).Distinct());
    }
}
