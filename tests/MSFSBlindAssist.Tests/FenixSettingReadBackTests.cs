// Characterization tests for the wait the Fenix documents and options windows share after a settings write:
// the settings query can still return the old value for up to about 2 s (FNX-13), so a read that does not
// show the change yet is read again, every 250 ms, up to 16 reads.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixSettingReadBackTests
{
    // "old" has not landed, "new" has, null is a read the Fenix did not answer. The last answer repeats.
    static async Task<(FenixReadBackResult<string> Result, int Reads, int Delays)> Wait(
        string?[] answers, bool stopOnNoAnswer = true, CancellationToken ct = default, Action<int>? onRead = null)
    {
        int reads = 0, delays = 0;
        var result = await FenixSettingReadBack.AwaitAsync<string>(
            _ =>
            {
                int n = reads++;
                onRead?.Invoke(reads);
                return Task.FromResult(answers[Math.Min(n, answers.Length - 1)]);
            },
            s => s == "new", TimeSpan.Zero, FenixSettingReadBack.Attempts,
            (_, _) => { delays++; return Task.CompletedTask; }, ct, stopOnNoAnswer);
        return (result, reads, delays);
    }

    [Fact] public async Task A_change_that_lands_on_the_first_read_is_never_waited_for()
    {
        var r = await Wait(new[] { "new" });
        Assert.Equal((new FenixReadBackResult<string>("new", true, true), 1, 0), r);
    }

    [Fact] public async Task A_change_that_lands_on_the_fifth_read_waits_four_times()
    {
        var r = await Wait(new[] { "old", "old", "old", "old", "new" });
        Assert.Equal((new FenixReadBackResult<string>("new", true, true), 5, 4), r);
    }

    [Fact] public async Task A_change_that_never_lands_is_read_exactly_sixteen_times()
    {
        var r = await Wait(new[] { "old" });
        Assert.Equal((new FenixReadBackResult<string>("old", false, true), 16, 15), r);
    }

    [Fact] public async Task The_wait_between_reads_is_the_interval_it_is_given()
    {
        var waits = new List<TimeSpan>();
        int reads = 0;
        await FenixSettingReadBack.AwaitAsync<string>(
            _ => Task.FromResult<string?>(++reads < 4 ? "old" : "new"), s => s == "new",
            TimeSpan.FromMilliseconds(250), 16, (t, _) => { waits.Add(t); return Task.CompletedTask; },
            CancellationToken.None, stopOnNoAnswer: true);
        Assert.Equal(Enumerable.Repeat(TimeSpan.FromMilliseconds(250), 3), waits);
    }

    [Fact] public void The_shipped_wait_outlasts_the_longest_lag_measured()
    {
        // 0.4 to 1.9 s measured on 3.0.0.7438 (FNX-13); the old single retry at 0.5 s gave up inside it.
        Assert.Equal(TimeSpan.FromMilliseconds(250), FenixSettingReadBack.Interval);
        Assert.Equal(16, FenixSettingReadBack.Attempts);
        Assert.True((FenixSettingReadBack.Attempts - 1) * FenixSettingReadBack.Interval > TimeSpan.FromSeconds(2));
    }

    [Fact] public async Task Cancelling_stops_the_wait_even_when_the_delay_ignores_the_token()
    {
        using var cts = new CancellationTokenSource();
        int reads = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Wait(new[] { "old" }, ct: cts.Token, onRead: n => { reads = n; if (n == 3) cts.Cancel(); }));
        Assert.Equal(3, reads);
    }

    [Fact] public async Task A_wait_cancelled_before_it_starts_reads_nothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int reads = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Wait(new[] { "new" }, ct: cts.Token, onRead: n => reads = n));
        Assert.Equal(0, reads);
    }

    // ---- A read the Fenix did not answer ----------------------------------------------------------

    [Fact] public async Task The_options_wait_stops_at_a_read_with_no_answer()
    {
        // Each unanswered read can take the 10 s HTTP time-out, so the wait is not repeated against a Fenix that is gone.
        var r = await Wait(new string?[] { null });
        Assert.Equal((new FenixReadBackResult<string>(null, false, false), 1, 0), r);
    }

    [Fact] public async Task The_options_wait_that_loses_the_Fenix_midway_reports_no_last_answer()
    {
        var r = await Wait(new string?[] { "old", null, "new" });
        Assert.Equal((new FenixReadBackResult<string>(null, false, true), 2, 1), r);
    }

    [Fact] public async Task The_documents_wait_keeps_going_through_reads_with_no_answer()
    {
        var r = await Wait(new string?[] { null, null, "new" }, stopOnNoAnswer: false);
        Assert.Equal((new FenixReadBackResult<string>("new", true, true), 3, 2), r);
    }

    [Fact] public async Task The_documents_wait_with_no_answer_at_all_reads_sixteen_times()
    {
        var r = await Wait(new string?[] { null }, stopOnNoAnswer: false);
        Assert.Equal((new FenixReadBackResult<string>(null, false, false), 16, 15), r);
    }

    [Fact] public async Task The_documents_wait_remembers_an_earlier_answer_when_the_last_read_has_none()
    {
        var r = await Wait(new string?[] { "old", null }, stopOnNoAnswer: false);
        Assert.Equal((new FenixReadBackResult<string>(null, false, true), 16, 15), r);
    }

    // ---- As the options window uses it: reads of the whole settings list, judged by FenixOptionEdits.Applied ----

    static IReadOnlyList<FenixSetting> Settings(string value, string source = "Session")
        => new[]
        {
            new FenixSetting("sound_enabled", "Misc", value, new[] { "true", "false" }, "System.Boolean", source, true),
            new FenixSetting("other", "Misc", "x", Array.Empty<string>(), "System.String", "Internal", false),
        };

    static async Task<(FenixReadBackResult<IReadOnlyList<FenixSetting>> Result, int Reads)> WaitForOption(
        IReadOnlyList<FenixSetting>?[] reads, FenixChangeKind kind, string? sent, FenixSetting before, string key = "sound_enabled")
    {
        int n = 0;
        var result = await FenixSettingReadBack.AwaitAsync<IReadOnlyList<FenixSetting>>(
            _ => Task.FromResult(reads[Math.Min(n++, reads.Length - 1)]),
            list => FenixOptionEdits.ReadBackSettled(list, key, now => FenixOptionEdits.Applied(kind, before, sent, now)),
            TimeSpan.Zero, FenixSettingReadBack.Attempts, (_, _) => Task.CompletedTask, CancellationToken.None, stopOnNoAnswer: true);
        return (result, n);
    }

    [Fact] public async Task A_session_change_the_Fenix_reports_after_three_old_reads_is_applied()
    {
        FenixSetting before = Settings("false", "Internal")[0];
        var (result, reads) = await WaitForOption(
            new[] { Settings("false", "Internal"), Settings("false", "Internal"), Settings("false", "Internal"), Settings("true") },
            FenixChangeKind.Session, "true", before);
        Assert.True(result.Landed);
        Assert.Equal(4, reads);
        Assert.Equal("true", result.Last!.First(s => s.Key == "sound_enabled").Value);
    }

    [Fact] public async Task A_permanent_change_waits_for_the_value_to_become_the_users_own()
    {
        FenixSetting before = Settings("false", "Internal")[0];
        // The value lands first, still the session's; the source follows a read later.
        var (result, reads) = await WaitForOption(
            new[] { Settings("true", "Session"), Settings("true", "UserFile") }, FenixChangeKind.Permanent, "true", before);
        Assert.True(result.Landed);
        Assert.Equal(2, reads);
    }

    [Fact] public async Task A_reset_waits_for_the_option_to_stop_reading_as_it_did()
    {
        FenixSetting before = Settings("false", "UserFile")[0];
        var (result, reads) = await WaitForOption(
            new[] { Settings("false", "UserFile"), Settings("false", "UserFile"), Settings("true", "Internal") }, FenixChangeKind.Reset, null, before);
        Assert.True(result.Landed);
        Assert.Equal(3, reads);
    }

    [Fact] public async Task A_change_the_Fenix_never_applies_gives_the_last_read_after_sixteen()
    {
        FenixSetting before = Settings("false", "Internal")[0];
        var (result, reads) = await WaitForOption(new[] { Settings("false", "Internal") }, FenixChangeKind.Session, "true", before);
        Assert.False(result.Landed);
        Assert.True(result.Answered);
        Assert.Equal(16, reads);
        Assert.Equal("false", result.Last!.First(s => s.Key == "sound_enabled").Value);
    }

    [Fact] public async Task An_answered_read_without_the_option_ends_the_wait_at_once()
    {
        // An answered read that lacks the option's key is not waited out (it was 16 reads, about 4 s, before the
        // window said "not read back"): the wait ends on that read, whose Last has no such option, which is what
        // the window speaks as "Change sent, but the Fenix did not read it back."
        FenixSetting before = Settings("false", "Internal")[0];
        var (result, reads) = await WaitForOption(new[] { Settings("true") }, FenixChangeKind.Session, "true", before, key: "gone");
        Assert.True(result.Landed);
        Assert.True(result.Answered);
        Assert.Equal(1, reads);
        Assert.DoesNotContain(result.Last!, s => s.Key == "gone");
    }

    [Fact] public async Task An_option_that_goes_missing_after_old_reads_ends_the_wait_on_that_read()
    {
        FenixSetting before = Settings("false", "Internal")[0];
        var (result, reads) = await WaitForOption(
            new[] { Settings("false", "Internal"), Settings("false", "Internal"), new[] { Settings("true")[1] } },
            FenixChangeKind.Session, "true", before);
        Assert.True(result.Landed);
        Assert.Equal(3, reads);
        Assert.DoesNotContain(result.Last!, s => s.Key == "sound_enabled");
    }

    [Fact] public void The_settled_test_is_applied_or_missing_and_never_a_read_that_still_has_the_old_value()
    {
        FenixSetting before = Settings("false", "Internal")[0];
        Func<FenixSetting, bool> applied = now => FenixOptionEdits.Applied(FenixChangeKind.Session, before, "true", now);
        Assert.False(FenixOptionEdits.ReadBackSettled(Settings("false", "Internal"), "sound_enabled", applied));
        Assert.True(FenixOptionEdits.ReadBackSettled(Settings("true"), "sound_enabled", applied));
        Assert.True(FenixOptionEdits.ReadBackSettled(Settings("false", "Internal"), "gone", applied));
        Assert.True(FenixOptionEdits.ReadBackSettled(Array.Empty<FenixSetting>(), "sound_enabled", applied));
    }

    [Fact] public async Task A_cancel_during_a_delay_that_ignores_the_token_never_starts_another_read()
    {
        using var cts = new CancellationTokenSource();
        int reads = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FenixSettingReadBack.AwaitAsync<string>(
                _ => { reads++; return Task.FromResult<string?>("old"); }, s => s == "new",
                TimeSpan.Zero, FenixSettingReadBack.Attempts,
                // The delay ignores its token and cancels the wait while it runs.
                (_, _) => { cts.Cancel(); return Task.CompletedTask; }, cts.Token, stopOnNoAnswer: true));
        Assert.Equal(1, reads);
    }
}
