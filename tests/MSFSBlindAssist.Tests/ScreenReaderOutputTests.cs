using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ScreenReaderOutput picks the screen reader announcements go to from Prism's registry, against fake backends and a
/// fake clock: priority order, the fall-through when a reader closes, the rescan throttle, braille on every message.
/// The real Prism is exercised only live (NVDA, ZDSR, and SAPI with no reader).
/// </summary>
public class ScreenReaderOutputTests
{
    private sealed class FakeReader : IScreenReaderBackend
    {
        public FakeReader(FakeSource source, ScreenReaderCandidate candidate) { Source = source; Candidate = candidate; }
        public FakeSource Source { get; }
        public ScreenReaderCandidate Candidate { get; }
        public string Name => Candidate.Name;
        public bool CanSpeak => !Source.Mute.Contains(Name);
        public bool CanBraille => !Source.NoBrailleSupport.Contains(Name);
        public bool Disposed;
        /// <summary>The reader restarted since this connection was made: Prism's NVDA connection does not reconnect.</summary>
        public bool Stale;
        public void Speak(string text, bool interrupt)
        {
            lock (Source)
            {
                if (Stale) throw new InvalidOperationException($"{Name} restarted; this connection is gone");
                if (Source.Dead.Contains(Name)) throw new InvalidOperationException($"{Name} closed");
                if (Source.WarmingUp.Contains(Name)) throw new InvalidOperationException($"{Name} still starting");
                Source.Spoken.Add((Name, text, interrupt));
            }
        }
        public void Braille(string text)
        {
            lock (Source)
            {
                Source.BrailleCalls++;
                if (Source.NoBraille.Contains(Name)) throw new InvalidOperationException("no braille display");
                Source.Brailled.Add((Name, text));
            }
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeSource : IScreenReaderBackends
    {
        public readonly List<ScreenReaderCandidate> List = new()
        {
            new(1, "NVDA", 103), new(2, "ZDSR", 101), new(3, "JAWS", 100),
        };
        public readonly HashSet<string> Running = new();
        public readonly HashSet<string> Dead = new();
        public readonly HashSet<string> NoBraille = new();
        public int BrailleCalls;
        /// <summary>Readers that start (Prism creates them) but do not take speech yet: NVDA's first seconds.</summary>
        public readonly HashSet<string> WarmingUp = new();
        /// <summary>Readers whose Prism backend says it cannot braille (no SupportsBraille feature).</summary>
        public readonly HashSet<string> NoBrailleSupport = new();
        /// <summary>Readers whose Prism backend says it cannot speak (no SupportsSpeak feature).</summary>
        public readonly HashSet<string> Mute = new();
        public readonly Dictionary<string, int> Creates = new();
        public readonly List<FakeReader> Made = new();
        public readonly List<(string Reader, string Text, bool Interrupt)> Spoken = new();
        public readonly List<(string Reader, string Text)> Brailled = new();

        public IReadOnlyList<ScreenReaderCandidate> Candidates => List;

        public IScreenReaderBackend? TryCreate(ScreenReaderCandidate candidate)
        {
            lock (this)
            {
                Creates[candidate.Name] = Creates.GetValueOrDefault(candidate.Name) + 1;
                if (!Running.Contains(candidate.Name) || Dead.Contains(candidate.Name)) return null;
                var reader = new FakeReader(this, candidate);
                Made.Add(reader);
                return reader;
            }
        }

        /// <summary>A reader closes: its live instances fail, and it no longer starts.</summary>
        public void Close(string name) { Running.Remove(name); Dead.Add(name); }

        /// <summary>A reader starts (again).</summary>
        public void Open(string name) { Running.Add(name); Dead.Remove(name); }

        /// <summary>A reader closes and starts again between two messages: it runs, but every connection made
        /// before is gone for good.</summary>
        public void Restart(string name)
        {
            lock (this) { foreach (var reader in Made.Where(r => r.Name == name)) reader.Stale = true; }
        }
    }

    private sealed class Rig
    {
        public long Now = 100_000;
        public readonly FakeSource Source = new();
        public readonly ScreenReaderOutput Output;
        public Rig() => Output = new ScreenReaderOutput(() => Source, () => Now);
        public void Advance(long ms) => Now += ms;
    }

    [Fact]
    public void The_highest_priority_reader_that_starts_is_used()
    {
        var rig = new Rig();
        rig.Source.Running.UnionWith(new[] { "ZDSR", "JAWS" });

        Assert.True(rig.Output.TrySpeak("Gear down", false));

        Assert.Equal(new[] { ("ZDSR", "Gear down", false) }, rig.Source.Spoken);
        Assert.Equal("ZDSR", rig.Output.ActiveReaderName);
    }

    [Fact]
    public void No_reader_returns_false_and_rescans_only_after_the_interval()
    {
        var rig = new Rig();

        Assert.False(rig.Output.TrySpeak("One", false));
        Assert.Null(rig.Output.ActiveReaderName);
        Assert.Equal(1, rig.Source.Creates["NVDA"]);

        rig.Advance(ScreenReaderOutput.RescanIntervalMs - 1);
        Assert.False(rig.Output.TrySpeak("Two", false));
        Assert.Equal(1, rig.Source.Creates["NVDA"]);

        rig.Advance(1);
        rig.Source.Open("JAWS");
        Assert.True(rig.Output.TrySpeak("Three", false));
        Assert.Equal(2, rig.Source.Creates["NVDA"]);
        Assert.Equal(new[] { ("JAWS", "Three", false) }, rig.Source.Spoken);
    }

    [Fact]
    public void A_failed_speak_falls_through_to_the_next_reader_in_the_same_message()
    {
        var rig = new Rig();
        rig.Source.Running.UnionWith(new[] { "NVDA", "ZDSR" });
        Assert.True(rig.Output.TrySpeak("Before", false));

        rig.Source.Close("NVDA");
        Assert.True(rig.Output.TrySpeak("After", true));

        Assert.Equal(("ZDSR", "After", true), rig.Source.Spoken[^1]);
        Assert.Equal("ZDSR", rig.Output.ActiveReaderName);
        Assert.True(rig.Source.Made.Single(r => r.Name == "NVDA").Disposed);
    }

    [Fact]
    public void A_failed_speak_with_no_other_reader_returns_false()
    {
        var rig = new Rig();
        rig.Source.Running.Add("ZDSR");
        Assert.True(rig.Output.TrySpeak("Before", false));

        rig.Source.Close("ZDSR");

        Assert.False(rig.Output.TrySpeak("After", false));
        Assert.Null(rig.Output.ActiveReaderName);
    }

    [Fact]
    public void A_reader_that_comes_back_is_used_again_after_the_interval()
    {
        var rig = new Rig();
        rig.Source.Running.Add("ZDSR");
        Assert.True(rig.Output.TrySpeak("Before", false));
        rig.Source.Close("ZDSR");
        Assert.False(rig.Output.TrySpeak("Closed", false));

        rig.Source.Open("ZDSR");
        rig.Advance(ScreenReaderOutput.RescanIntervalMs);

        Assert.True(rig.Output.TrySpeak("Back", false));
        Assert.Equal(("ZDSR", "Back", false), rig.Source.Spoken[^1]);
    }

    [Fact]
    public void A_higher_priority_reader_that_starts_later_takes_over_after_the_interval()
    {
        var rig = new Rig();
        rig.Source.Running.UnionWith(new[] { "ZDSR", "JAWS" });
        Assert.True(rig.Output.TrySpeak("One", false));
        int zdsrCreates = rig.Source.Creates["ZDSR"], jawsCreates = rig.Source.Creates.GetValueOrDefault("JAWS");

        rig.Source.Open("NVDA");
        Assert.True(rig.Output.TrySpeak("Two", false));
        Assert.Equal(("ZDSR", "Two", false), rig.Source.Spoken[^1]);

        rig.Advance(ScreenReaderOutput.RescanIntervalMs);
        Assert.True(rig.Output.TrySpeak("Three", false));

        Assert.Equal(("NVDA", "Three", false), rig.Source.Spoken[^1]);
        Assert.Equal(zdsrCreates, rig.Source.Creates["ZDSR"]);
        Assert.Equal(jawsCreates, rig.Source.Creates.GetValueOrDefault("JAWS"));
        Assert.True(rig.Source.Made.Single(r => r.Name == "ZDSR").Disposed);
    }

    [Fact]
    public void A_reader_still_starting_gives_the_message_back_to_the_one_in_use_and_takes_over_once_it_speaks()
    {
        var rig = new Rig();
        rig.Source.Running.Add("ZDSR");
        Assert.True(rig.Output.TrySpeak("One", false));

        rig.Source.Open("NVDA");
        rig.Source.WarmingUp.Add("NVDA");
        rig.Advance(ScreenReaderOutput.RescanIntervalMs);
        Assert.True(rig.Output.TrySpeak("Two", false));
        Assert.Equal(("ZDSR", "Two", false), rig.Source.Spoken[^1]);
        Assert.Equal("ZDSR", rig.Output.ActiveReaderName);

        rig.Source.WarmingUp.Remove("NVDA");
        rig.Advance(ScreenReaderOutput.RescanIntervalMs);
        Assert.True(rig.Output.TrySpeak("Three", false));
        Assert.Equal(("NVDA", "Three", false), rig.Source.Spoken[^1]);
    }

    [Fact]
    public void Every_reader_that_fails_is_skipped_until_one_takes_the_message()
    {
        var rig = new Rig();
        rig.Source.Running.UnionWith(new[] { "NVDA", "ZDSR", "JAWS" });
        Assert.True(rig.Output.TrySpeak("One", false));

        rig.Source.Close("NVDA");
        rig.Source.WarmingUp.Add("ZDSR");

        Assert.True(rig.Output.TrySpeak("Two", false));
        Assert.Equal(("JAWS", "Two", false), rig.Source.Spoken[^1]);
    }

    [Fact]
    public void When_every_running_reader_fails_the_message_goes_to_SAPI()
    {
        var rig = new Rig();
        rig.Source.Running.UnionWith(new[] { "NVDA", "ZDSR" });
        Assert.True(rig.Output.TrySpeak("One", false));

        rig.Source.WarmingUp.UnionWith(new[] { "NVDA", "ZDSR" });

        Assert.False(rig.Output.TrySpeak("Two", false));
    }

    [Fact]
    public void DetectNow_notices_a_reader_that_closed()
    {
        var rig = new Rig();
        rig.Source.Running.Add("NVDA");
        Assert.True(rig.Output.TrySpeak("One", false));

        rig.Source.Close("NVDA");

        Assert.Null(rig.Output.DetectNow());
        Assert.Null(rig.Output.ActiveReaderName);
    }

    [Fact]
    public void DetectNow_keeps_the_reader_in_use_when_it_is_still_the_best()
    {
        var rig = new Rig();
        rig.Source.Running.UnionWith(new[] { "ZDSR", "JAWS" });
        Assert.True(rig.Output.TrySpeak("One", false));
        var inUse = rig.Source.Made.Single(r => r.Name == "ZDSR");

        Assert.Equal("ZDSR", rig.Output.DetectNow());

        // The instance speaking is kept (freeing it can drop its queued speech); the probe's copy is closed.
        Assert.False(inUse.Disposed);
        Assert.True(rig.Output.TrySpeak("Two", false));
        Assert.Equal(2, rig.Source.Made.Count(r => r.Name == "ZDSR"));
        Assert.True(rig.Source.Made.Last(r => r.Name == "ZDSR").Disposed);
    }

    [Fact]
    public void The_top_reader_is_never_probed_again_while_it_works()
    {
        var rig = new Rig();
        rig.Source.Running.Add("NVDA");
        Assert.True(rig.Output.TrySpeak("One", false));

        rig.Advance(ScreenReaderOutput.RescanIntervalMs * 5);
        Assert.True(rig.Output.TrySpeak("Two", false));

        Assert.Equal(1, rig.Source.Creates["NVDA"]);
        Assert.False(rig.Source.Creates.ContainsKey("ZDSR"));
    }

    [Fact]
    public void Every_spoken_message_is_brailled()
    {
        var rig = new Rig();
        rig.Source.Running.Add("NVDA");

        Assert.True(rig.Output.TrySpeak("Flaps three", false));

        Assert.Equal(new[] { ("NVDA", "Flaps three") }, rig.Source.Brailled);
    }

    [Fact]
    public void A_braille_failure_still_counts_as_spoken()
    {
        var rig = new Rig();
        rig.Source.Running.Add("ZDSR");
        rig.Source.NoBraille.Add("ZDSR");

        Assert.True(rig.Output.TrySpeak("Flaps three", false));
        Assert.True(rig.Output.TrySpeak("Gear up", false));

        Assert.Equal("ZDSR", rig.Output.ActiveReaderName);
        Assert.Equal(2, rig.Source.Spoken.Count);
        Assert.Equal(1, rig.Source.Creates["ZDSR"]);
    }

    [Fact]
    public void A_reader_that_cannot_braille_is_never_sent_braille()
    {
        var rig = new Rig();
        rig.Source.Running.Add("JAWS");
        rig.Source.NoBrailleSupport.Add("JAWS");
        rig.Source.NoBraille.Add("JAWS");   // would throw if it were called

        Assert.True(rig.Output.TrySpeak("Flaps three", false));

        Assert.Equal(0, rig.Source.BrailleCalls);
        Assert.Equal(("JAWS", "Flaps three", false), rig.Source.Spoken[^1]);
    }

    [Fact]
    public void A_reader_that_cannot_speak_is_skipped_for_the_next_one()
    {
        var rig = new Rig();
        rig.Source.Running.UnionWith(new[] { "NVDA", "ZDSR" });
        rig.Source.Mute.Add("NVDA");

        Assert.True(rig.Output.TrySpeak("Gear up", false));

        Assert.Equal("ZDSR", rig.Output.ActiveReaderName);
        Assert.DoesNotContain(rig.Source.Spoken, s => s.Reader == "NVDA");
        Assert.True(rig.Source.Made.Single(r => r.Name == "NVDA").Disposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_interrupt_flag_passes_through(bool interrupt)
    {
        var rig = new Rig();
        rig.Source.Running.Add("NVDA");

        Assert.True(rig.Output.TrySpeak("Stop", interrupt));

        Assert.Equal(new[] { ("NVDA", "Stop", interrupt) }, rig.Source.Spoken);
    }

    [Fact]
    public void A_backend_source_that_throws_leaves_speech_to_SAPI()
    {
        int attempts = 0;
        long now = 100_000;
        var output = new ScreenReaderOutput(() => { attempts++; throw new DllNotFoundException("prism.dll"); }, () => now);

        Assert.False(output.TrySpeak("One", false));
        Assert.False(output.TrySpeak("Two", false));
        Assert.Equal(1, attempts);
        Assert.Null(output.DetectNow());
        Assert.False(output.Started);
    }

    [Fact]
    public void Started_is_true_once_Prism_answered_even_with_no_reader_running()
    {
        var rig = new Rig();
        Assert.False(rig.Output.Started);

        Assert.Null(rig.Output.DetectNow());

        Assert.True(rig.Output.Started);
    }

    [Fact]
    public void DetectNow_rescans_at_once_and_names_the_reader()
    {
        var rig = new Rig();
        Assert.False(rig.Output.TrySpeak("One", false));

        rig.Source.Open("ZDSR");

        Assert.Equal("ZDSR", rig.Output.DetectNow());
        Assert.Equal("ZDSR", rig.Output.ActiveReaderName);
    }

    [Fact]
    public void Empty_text_is_not_sent()
    {
        var rig = new Rig();
        rig.Source.Running.Add("NVDA");

        Assert.False(rig.Output.TrySpeak("", false));

        Assert.Empty(rig.Source.Spoken);
    }

    [Fact]
    public void A_reader_that_restarted_takes_the_same_message_over_a_fresh_connection()
    {
        var rig = new Rig();
        rig.Source.Running.Add("NVDA");
        Assert.True(rig.Output.TrySpeak("Before", false));
        var old = rig.Source.Made.Single();

        rig.Source.Restart("NVDA");

        // Not SAPI for this message and the next five seconds: NVDA is running, only the old connection is gone.
        Assert.True(rig.Output.TrySpeak("After", true));
        Assert.Equal(("NVDA", "After", true), rig.Source.Spoken[^1]);
        Assert.Equal("NVDA", rig.Output.ActiveReaderName);
        Assert.True(old.Disposed);
    }

    [Fact]
    public void A_restarted_reader_that_is_not_speaking_yet_gives_the_message_to_the_next_reader()
    {
        var rig = new Rig();
        rig.Source.Running.UnionWith(new[] { "NVDA", "ZDSR" });
        Assert.True(rig.Output.TrySpeak("Before", false));

        rig.Source.Restart("NVDA");
        rig.Source.WarmingUp.Add("NVDA");

        Assert.True(rig.Output.TrySpeak("After", false));
        Assert.Equal(("ZDSR", "After", false), rig.Source.Spoken[^1]);
    }

    [Fact]
    public void A_reader_that_never_spoke_is_connected_once_per_message()
    {
        var rig = new Rig();
        rig.Source.Running.Add("NVDA");
        rig.Source.WarmingUp.Add("NVDA");

        Assert.False(rig.Output.TrySpeak("One", false));

        // The fresh connection is for a reader that spoke before; one that has not spoken yet is not tried twice.
        Assert.Equal(1, rig.Source.Creates["NVDA"]);
    }

    [Fact]
    public void The_reader_in_use_is_kept_while_a_better_one_is_not_speaking_yet()
    {
        var rig = new Rig();
        rig.Source.Running.Add("ZDSR");
        Assert.True(rig.Output.TrySpeak("One", false));
        var inUse = rig.Source.Made.Single(r => r.Name == "ZDSR");

        rig.Source.Open("NVDA");
        rig.Source.WarmingUp.Add("NVDA");
        rig.Advance(ScreenReaderOutput.RescanIntervalMs);
        Assert.True(rig.Output.TrySpeak("Two", false));

        // Closing ZDSR's connection can drop what it has queued, so it stays open until NVDA has spoken.
        Assert.Equal(("ZDSR", "Two", false), rig.Source.Spoken[^1]);
        Assert.False(inUse.Disposed);
        Assert.Equal(1, rig.Source.Creates["ZDSR"]);
        Assert.True(rig.Source.Made.Single(r => r.Name == "NVDA").Disposed);
    }

    [Fact]
    public void A_better_reader_that_keeps_failing_is_tried_less_and_less_often()
    {
        var rig = new Rig();
        rig.Source.Running.Add("ZDSR");
        Assert.True(rig.Output.TrySpeak("Zero", false));
        rig.Source.Open("NVDA");
        rig.Source.WarmingUp.Add("NVDA");
        int Tried() => rig.Source.Made.Count(r => r.Name == "NVDA");

        // Tried at 5 s and 10 s, then after 10 s more (20 s), then after 20 s more (40 s).
        var triedAt = new List<int>();
        for (int seconds = 5; seconds <= 40; seconds += 5)
        {
            int before = Tried();
            rig.Advance(5000);
            Assert.True(rig.Output.TrySpeak($"At {seconds}", false));
            if (Tried() > before) triedAt.Add(seconds);
        }

        Assert.Equal(new[] { 5, 10, 20, 40 }, triedAt);
        Assert.All(rig.Source.Spoken, s => Assert.Equal("ZDSR", s.Reader));
    }

    [Fact]
    public void A_better_reader_that_failed_before_takes_over_once_it_speaks()
    {
        var rig = new Rig();
        rig.Source.Running.Add("ZDSR");
        Assert.True(rig.Output.TrySpeak("One", false));
        rig.Source.Open("NVDA");
        rig.Source.WarmingUp.Add("NVDA");
        rig.Advance(5000);
        Assert.True(rig.Output.TrySpeak("Two", false));
        rig.Advance(5000);
        Assert.True(rig.Output.TrySpeak("Three", false));

        rig.Source.WarmingUp.Remove("NVDA");
        rig.Advance(10_000);
        Assert.True(rig.Output.TrySpeak("Four", true));

        Assert.Equal(("NVDA", "Four", true), rig.Source.Spoken[^1]);
        Assert.Equal("NVDA", rig.Output.ActiveReaderName);
        Assert.True(rig.Source.Made.Single(r => r.Name == "ZDSR").Disposed);
        Assert.Equal(("NVDA", "Four"), rig.Source.Brailled[^1]);
    }

    [Fact]
    public void DetectNow_keeps_the_reader_in_use_when_a_better_one_has_only_just_started()
    {
        var rig = new Rig();
        rig.Source.Running.Add("ZDSR");
        Assert.True(rig.Output.TrySpeak("One", false));
        var inUse = rig.Source.Made.Single(r => r.Name == "ZDSR");
        rig.Source.Open("NVDA");
        rig.Source.WarmingUp.Add("NVDA");

        // Settings names the reader BA speaks through now; NVDA is given the next message, whatever the throttle.
        Assert.Equal("ZDSR", rig.Output.DetectNow());
        Assert.False(inUse.Disposed);
        int nvdaConnections = rig.Source.Made.Count(r => r.Name == "NVDA");

        Assert.True(rig.Output.TrySpeak("Two", false));
        Assert.Equal(("ZDSR", "Two", false), rig.Source.Spoken[^1]);
        Assert.Equal(nvdaConnections + 1, rig.Source.Made.Count(r => r.Name == "NVDA"));
    }

    [Fact]
    public void With_no_reader_for_two_minutes_readers_are_looked_for_every_thirty_seconds()
    {
        var rig = new Rig();
        Assert.False(rig.Output.TrySpeak("At 0", false));
        for (int seconds = 5; seconds <= 120; seconds += 5)
        {
            rig.Advance(5000);
            Assert.False(rig.Output.TrySpeak($"At {seconds}", false));
        }
        Assert.Equal(25, rig.Source.Creates["NVDA"]);   // every 5 s, 0 to 120 s

        rig.Advance(5000);
        Assert.False(rig.Output.TrySpeak("At 125", false));
        Assert.Equal(25, rig.Source.Creates["NVDA"]);

        rig.Advance(25_000);
        rig.Source.Open("JAWS");
        Assert.True(rig.Output.TrySpeak("At 150", false));
        Assert.Equal(26, rig.Source.Creates["NVDA"]);
        Assert.Equal(("JAWS", "At 150", false), rig.Source.Spoken[^1]);
    }

    [Fact]
    public void A_reader_that_ran_and_closed_is_looked_for_every_five_seconds_again()
    {
        var rig = new Rig();
        Assert.False(rig.Output.TrySpeak("At 0", false));
        rig.Advance(200_000);
        rig.Source.Open("ZDSR");
        Assert.True(rig.Output.TrySpeak("Found", false));

        rig.Source.Close("ZDSR");
        Assert.False(rig.Output.TrySpeak("Closed", false));
        int looks = rig.Source.Creates["NVDA"];

        rig.Advance(5000);
        Assert.False(rig.Output.TrySpeak("Five seconds on", false));
        Assert.Equal(looks + 1, rig.Source.Creates["NVDA"]);
    }

    [Fact]
    public void Opening_Settings_brings_the_five_second_look_back()
    {
        var rig = new Rig();
        Assert.False(rig.Output.TrySpeak("At 0", false));
        rig.Advance(200_000);
        Assert.False(rig.Output.TrySpeak("Slow by now", false));

        Assert.Null(rig.Output.DetectNow());
        int looks = rig.Source.Creates["NVDA"];

        rig.Advance(5000);
        Assert.False(rig.Output.TrySpeak("Five seconds on", false));
        Assert.Equal(looks + 1, rig.Source.Creates["NVDA"]);
    }

    [Fact]
    public void Concurrent_speakers_do_not_throw()
    {
        var rig = new Rig();
        rig.Source.Running.UnionWith(new[] { "NVDA", "ZDSR" });

        Parallel.For(0, 200, i =>
        {
            if (i == 100) lock (rig.Source) rig.Source.Close("NVDA");
            rig.Output.TrySpeak($"message {i}", i % 2 == 0);
        });

        Assert.Equal("ZDSR", rig.Output.ActiveReaderName);
    }
}
