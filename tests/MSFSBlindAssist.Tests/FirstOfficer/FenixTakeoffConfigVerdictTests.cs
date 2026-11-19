using System.Globalization;
using MSFSBlindAssist.FirstOfficer.Fenix;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Fenix TO CONFIG verdict, shared by the First Officer and the panel's button (PR 160 second
/// review): an amber takeoff-config fault lights the master CAUTION and was spoken as "normal", and an
/// unread light defaulted to 0 and said "normal" too.
/// </summary>
public class FenixTakeoffConfigVerdictTests
{
    [Theory]
    [InlineData(1.0, 0.0, "Takeoff config: check configuration.")]
    [InlineData(1.0, 1.0, "Takeoff config: check configuration.")]   // the warning outranks the caution
    [InlineData(0.0, 1.0, "Takeoff config: caution. Check the ECAM.")]
    [InlineData(0.0, 0.0, "Takeoff config normal.")]
    public void ReadLights_GiveTheirVerdict(double warning, double caution, string expected)
        => Assert.Equal(expected, FenixTakeoffConfigVerdict.Text(warning, caution));

    [Theory]
    [InlineData(null, 0.0)]
    [InlineData(0.0, null)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(0.0, double.NaN)]
    public void AnUnreadLight_IsNeverNormal(double? warning, double? caution)
        => Assert.Equal("Takeoff config: no result read. Check the ECAM.", FenixTakeoffConfigVerdict.Text(warning, caution));

    // A lit light is a definite answer even when the other one is unread.
    [Fact]
    public void ALitWarning_IsSpoken_WhateverTheCautionReads()
        => Assert.Equal("Takeoff config: check configuration.", FenixTakeoffConfigVerdict.Text(1.0, null));

    // ---- the watch over fresh reads (v3 delays the master warning after the press) ----

    private const string Check = "Takeoff config: check configuration.";
    private const string Caution = "Takeoff config: caution. Check the ECAM.";
    private const string NoResult = "Takeoff config: no result read. Check the ECAM.";
    private const string Normal = "Takeoff config normal.";
    private static readonly TimeSpan VerdictAfter = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan PressWait = TimeSpan.FromSeconds(3);

    private static TakeoffConfigSample S(double seconds, double? warning, double? caution)
        => new(TimeSpan.FromSeconds(seconds), warning, caution);

    [Fact]
    public void Decide_WarningAmongDarkSamples_IsCheck()
        => Assert.Equal(Check, FenixTakeoffConfigVerdict.Decide(
            new[] { S(0.2, 0, 0), S(1.0, 1, 0), S(2.6, 0, 0) }, VerdictAfter));

    [Fact]
    public void Decide_LitWarningOfTwo_IsCheck()
        => Assert.Equal(Check, FenixTakeoffConfigVerdict.Decide(new[] { S(1.0, 2, 0) }, VerdictAfter));

    [Fact]
    public void Decide_CautionOnly_IsCaution()
        => Assert.Equal(Caution, FenixTakeoffConfigVerdict.Decide(new[] { S(1.0, 0, 1), S(2.6, 0, 1) }, VerdictAfter));

    [Fact]
    public void Decide_DarkBeforeTheVerdictTime_IsNoResult()
        => Assert.Equal(NoResult, FenixTakeoffConfigVerdict.Decide(new[] { S(2.4, 0, 0) }, VerdictAfter));

    [Fact]
    public void Decide_DarkAtTheVerdictTime_IsNormal()
        => Assert.Equal(Normal, FenixTakeoffConfigVerdict.Decide(new[] { S(2.5, 0, 0) }, VerdictAfter));

    [Fact]
    public void Decide_UnreadCautionAtTheVerdictTime_IsNoResult()
        => Assert.Equal(NoResult, FenixTakeoffConfigVerdict.Decide(new[] { S(2.6, 0, null) }, VerdictAfter));

    [Fact]
    public void Decide_NoSamples_IsNoResult()
        => Assert.Equal(NoResult, FenixTakeoffConfigVerdict.Decide(Array.Empty<TakeoffConfigSample>(), VerdictAfter));

    // A fake clock that delay() advances; reads answer from a script keyed on key, fake time and call count.
    private sealed class Rig
    {
        public TimeSpan Now;
        public readonly List<string> Reads = new();
        public Func<string, TimeSpan, int, double?> Script = (_, _, _) => 0.0;
        private readonly Dictionary<string, int> _count = new();

        public Task<double?> Read(string key)
        {
            _count.TryGetValue(key, out var n);
            _count[key] = n + 1;
            Reads.Add(key);
            return Task.FromResult(Script(key, Now, n));
        }

        public Task<string> Watch() => FenixTakeoffConfigVerdict.WatchAsync(
            Read, PressWait, VerdictAfter, () => Now, d => { Now += d; return Task.CompletedTask; });
    }

    private static double ButtonFrom(string key, TimeSpan now, TimeSpan edge)
        => key == FenixTakeoffConfigVerdict.ButtonFreshKey && now >= edge ? 1.0 : 0.0;

    [Fact]
    public async Task A_warning_that_lights_late_is_still_caught()
    {
        var edge = TimeSpan.FromSeconds(0.4);
        var rig = new Rig();
        rig.Script = (k, now, _) => k == FenixTakeoffConfigVerdict.MasterWarningFreshKey
            ? (now >= edge + TimeSpan.FromSeconds(2.0) ? 2.0 : 0.0)
            : ButtonFrom(k, now, edge);
        Assert.Equal(Check, await rig.Watch());
    }

    [Fact]
    public async Task Normal_comes_only_after_the_verdict_time()
    {
        var edge = TimeSpan.FromSeconds(0.4);
        var rig = new Rig();
        rig.Script = (k, now, _) => ButtonFrom(k, now, edge);
        Assert.Equal(Normal, await rig.Watch());
        Assert.True(rig.Now >= edge + VerdictAfter);
    }

    // The First Officer cancels the watch when its press was never sent: it stops at the next read, no 30 s poll.
    [Fact]
    public async Task A_cancelled_watch_stops_at_its_next_read_with_no_result()
    {
        var rig = new Rig();
        using var cancel = new CancellationTokenSource();
        rig.Script = (_, _, n) =>
        {
            if (n == 2) cancel.Cancel();
            return 0.0;
        };
        string verdict = await FenixTakeoffConfigVerdict.WatchAsync(rig.Read, PressWait, VerdictAfter,
            () => rig.Now, d => { rig.Now += d; return Task.CompletedTask; }, cancel.Token);
        Assert.Equal(NoResult, verdict);
        Assert.Equal(3, rig.Reads.Count);
    }

    [Fact]
    public async Task No_press_seen_is_no_result()
    {
        var rig = new Rig();
        rig.Script = (_, _, _) => 0.0;
        Assert.Equal(NoResult, await rig.Watch());
        Assert.True(rig.Now >= PressWait);
        Assert.DoesNotContain(FenixTakeoffConfigVerdict.MasterWarningFreshKey, rig.Reads);
        Assert.DoesNotContain(FenixTakeoffConfigVerdict.MasterCautionFreshKey, rig.Reads);
    }

    [Fact]
    public async Task A_button_already_held_needs_a_release_first()
    {
        var rig = new Rig();
        var buttonTimes = new List<TimeSpan>();
        rig.Script = (k, now, n) =>
        {
            if (k != FenixTakeoffConfigVerdict.ButtonFreshKey) return 0.0;
            buttonTimes.Add(now);
            return n == 2 ? 0.0 : 1.0;   // 1, 1, 0, 1, 1, ...
        };
        Assert.Equal(Normal, await rig.Watch());
        var t0 = buttonTimes[3];
        Assert.True(rig.Now >= t0 + VerdictAfter);
    }

    [Fact]
    public async Task A_warning_lit_before_the_press_is_check()
    {
        var rig = new Rig();
        rig.Script = (k, now, _) => k == FenixTakeoffConfigVerdict.MasterWarningFreshKey
            ? 1.0 : ButtonFrom(k, now, TimeSpan.FromSeconds(0.2));
        Assert.Equal(Check, await rig.Watch());
    }

    [Fact]
    public async Task Caution_waits_for_a_late_warning()
    {
        var edge = TimeSpan.FromSeconds(0.2);
        var rig = new Rig();
        rig.Script = (k, now, _) => k switch
        {
            FenixTakeoffConfigVerdict.MasterCautionFreshKey => now >= edge + TimeSpan.FromSeconds(0.5) ? 1.0 : 0.0,
            FenixTakeoffConfigVerdict.MasterWarningFreshKey => now >= edge + TimeSpan.FromSeconds(1.5) ? 2.0 : 0.0,
            _ => ButtonFrom(k, now, edge),
        };
        Assert.Equal(Check, await rig.Watch());
    }

    [Fact]
    public async Task Unread_lights_never_give_normal()
    {
        var rig = new Rig();
        rig.Script = (k, now, _) => k == FenixTakeoffConfigVerdict.ButtonFreshKey
            ? ButtonFrom(k, now, TimeSpan.FromSeconds(0.2)) : null;
        Assert.Equal(NoResult, await rig.Watch());
    }

    [Fact]
    public async Task The_verdict_is_the_same_under_a_comma_decimal_culture()
    {
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
        try
        {
            var rig = new Rig();
            rig.Script = (k, now, _) => ButtonFrom(k, now, TimeSpan.FromSeconds(0.2));
            Assert.Equal(Normal, await rig.Watch());
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void The_hold_is_never_shorter_than_v2s()
        => Assert.True(FenixTakeoffConfigVerdict.HoldMs >= 1500);
}
