// Pins how a typed Fenix FCU set knows the Encoder Acceleration option ([FNX-20]): from the Fenix's settings
// query, with a short cache so a set does not wait on a query every time. Anything but a readable "true" or
// "false" is unread, which the sets treat as on (the chunked path, which lands either way). An "off" is kept
// only briefly: a stale off lets one whole-difference write through before the loop sees it multiplied.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixEncoderAccelerationTests
{
    static FenixSetting S(string key, string? value)
        => new(key, "Realism", value, Array.Empty<string>(), "System.Boolean", "Internal", true);

    static IReadOnlyList<FenixSetting> With(string? value)
        => new[] { S("fd_ls_sync_enabled", "true"), S(FenixOptionKeys.EncoderAcceleration, value) };

    [Theory]
    [InlineData("True", true)] [InlineData("true", true)] [InlineData("TRUE", true)]
    [InlineData("False", false)] [InlineData("false", false)]
    [InlineData("", null)] [InlineData(null, null)] [InlineData("yes", null)] [InlineData("1", null)]
    public void Only_true_or_false_is_read(string? raw, bool? expected)
        => Assert.Equal(expected, FenixEncoderAcceleration.Parse(With(raw)));

    [Fact]
    public void No_answer_or_no_such_option_is_unread()
    {
        Assert.Null(FenixEncoderAcceleration.Parse(Array.Empty<FenixSetting>()));
        Assert.Null(FenixEncoderAcceleration.Parse(null));
        Assert.Null(FenixEncoderAcceleration.Parse(new[] { S("fd_ls_sync_enabled", "false") }));
        // The whole key, never a part of it.
        Assert.Null(FenixEncoderAcceleration.Parse(new[] { S("encoder_acceleration", "true") }));
    }

    [Theory] [InlineData("tr-TR")] [InlineData("de-DE")]
    public void Reading_does_not_depend_on_the_culture(string culture)
        => Assert.True(TestCulture.Run(culture, () => FenixEncoderAcceleration.Parse(With("TRUE"))));

    private sealed class Clock { public long Now; }

    [Fact]
    public async Task On_is_kept_for_a_minute_and_read_again_after()
    {
        var clock = new Clock();
        int reads = 0;
        var option = new FenixEncoderAcceleration(_ => { reads++; return Task.FromResult(With("True")); }, () => clock.Now);

        Assert.True(await option.ReadAsync());
        clock.Now += FenixEncoderAcceleration.OnCacheMs - 1;
        Assert.True(await option.ReadAsync());
        Assert.Equal(1, reads);

        clock.Now += 1;
        Assert.True(await option.ReadAsync());
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task Off_is_kept_only_briefly()
    {
        var clock = new Clock();
        int reads = 0;
        string value = "False";
        var option = new FenixEncoderAcceleration(_ => { reads++; return Task.FromResult(With(value)); }, () => clock.Now);

        Assert.False(await option.ReadAsync());
        clock.Now += FenixEncoderAcceleration.OffCacheMs - 1;
        Assert.False(await option.ReadAsync());
        Assert.Equal(1, reads);

        value = "True";   // the pilot turned it on
        clock.Now += 1;
        Assert.True(await option.ReadAsync());
        Assert.Equal(2, reads);
        Assert.True(FenixEncoderAcceleration.OffCacheMs < FenixEncoderAcceleration.OnCacheMs);
    }

    [Fact]
    public async Task Unread_is_kept_like_on_so_a_gateway_that_is_down_is_not_asked_every_set()
    {
        var clock = new Clock();
        int reads = 0;
        var option = new FenixEncoderAcceleration(_ => { reads++; return Task.FromResult<IReadOnlyList<FenixSetting>>(Array.Empty<FenixSetting>()); },
            () => clock.Now);

        Assert.Null(await option.ReadAsync());
        clock.Now += FenixEncoderAcceleration.OffCacheMs + 1;
        Assert.Null(await option.ReadAsync());
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task A_slow_or_failing_query_is_unread_and_never_throws()
    {
        var slow = new FenixEncoderAcceleration(async ct => { await Task.Delay(Timeout.Infinite, ct); return With("False"); },
            () => 0, readTimeoutMs: 50);
        Assert.Null(await slow.ReadAsync());

        var failing = new FenixEncoderAcceleration(_ => throw new InvalidOperationException("gateway"), () => 0);
        Assert.Null(await failing.ReadAsync());
    }

    [Fact]
    public void The_query_waits_at_most_a_second_and_a_half()
        => Assert.Equal(1500, FenixEncoderAcceleration.ReadTimeoutMs);
}
