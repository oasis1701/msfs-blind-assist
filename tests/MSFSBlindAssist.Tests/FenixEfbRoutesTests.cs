// Pins how the Fenix v3 EFB window sorts the page it is on. The EFB's MCDU
// and FAP pages cannot be read with a screen reader, so the window steps back to the EFB home and says
// where the working windows are; every other page is left alone. Classification is by the fragment's
// first segment, on the gateway's own origin only (http 127.0.0.1:8083), so a page that merely carries
// "#/mcdu" on another host is not caught. The router may change the address twice on the way into a
// dead end, so a second catch of the same one within 1.5 s does nothing. A source scan pins
// the window's wiring, which a unit test cannot reach (CI runs xUnit only).

using System.Text.RegularExpressions;
using MSFSBlindAssist.Forms.Fenix;

namespace MSFSBlindAssist.Tests;

public class FenixEfbRoutesTests
{
    // The real addresses the EFB's router shows on the window's external target (the target in the query,
    // the route in the fragment).
    [Fact]
    public void The_real_external_target_addresses_are_caught()
    {
        Assert.Equal(FenixEfbRoute.Mcdu,
            FenixEfbRoutes.Classify("http://127.0.0.1:8083/?target=external#/mcdu?cdu=1&fullscreen=false"));
        Assert.Equal(FenixEfbRoute.Fap,
            FenixEfbRoutes.Classify("http://127.0.0.1:8083/?target=external#/fap/screen-saver"));
    }

    [Fact]
    public void The_same_dead_end_caught_again_within_one_and_a_half_seconds_is_ignored()
    {
        var gate = new FenixEfbDeadEndGate();
        Assert.True(gate.ShouldCatch(FenixEfbRoute.Mcdu, 10_000));
        Assert.False(gate.ShouldCatch(FenixEfbRoute.Mcdu, 10_001));
        Assert.False(gate.ShouldCatch(FenixEfbRoute.Mcdu, 11_499));
    }

    [Fact]
    public void The_same_dead_end_is_caught_again_after_one_and_a_half_seconds()
    {
        var gate = new FenixEfbDeadEndGate();
        Assert.Equal(1_500L, FenixEfbDeadEndGate.WindowMs);
        Assert.True(gate.ShouldCatch(FenixEfbRoute.Fap, 10_000));
        Assert.True(gate.ShouldCatch(FenixEfbRoute.Fap, 11_500));
        // The window starts again from that catch.
        Assert.False(gate.ShouldCatch(FenixEfbRoute.Fap, 12_999));
        Assert.True(gate.ShouldCatch(FenixEfbRoute.Fap, 13_000));
    }

    [Fact]
    public void An_ignored_catch_does_not_hold_the_gate_shut_longer()
    {
        var gate = new FenixEfbDeadEndGate();
        Assert.True(gate.ShouldCatch(FenixEfbRoute.Mcdu, 0));
        Assert.False(gate.ShouldCatch(FenixEfbRoute.Mcdu, 1_000));
        Assert.True(gate.ShouldCatch(FenixEfbRoute.Mcdu, 1_500));
    }

    [Fact]
    public void A_different_dead_end_within_one_and_a_half_seconds_is_caught()
    {
        var gate = new FenixEfbDeadEndGate();
        Assert.True(gate.ShouldCatch(FenixEfbRoute.Mcdu, 10_000));
        Assert.True(gate.ShouldCatch(FenixEfbRoute.Fap, 10_100));
        Assert.True(gate.ShouldCatch(FenixEfbRoute.Mcdu, 10_200));
    }

    // The window's wiring: the browser's SourceChanged is the one hook that sees the EFB's in-page route changes;
    // its handler sorts the address with FenixEfbRoutes, lets the gate decide on a monotonic clock, then goes back
    // to the EFB home and speaks the dead-end line.
    [Fact]
    public void The_window_catches_dead_ends_through_SourceChanged_once_on_a_monotonic_clock()
    {
        string source = File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Forms", "Fenix", "FenixEFBForm.cs"));
        // What must be there is looked for in the code only: a line that is a comment does not count.
        string code = Regex.Replace(source, @"(?m)^\s*//.*$", "");

        Assert.Matches(new Regex(@"\.SourceChanged\s*\+=\s*OnSourceChanged\s*;"), code);

        string handler = MethodBody(code, "OnSourceChanged");
        int classify = handler.IndexOf("FenixEfbRoutes.Classify(", StringComparison.Ordinal);
        int message = handler.IndexOf("FenixEfbRoutes.DeadEndMessage(", StringComparison.Ordinal);
        Match gate = Regex.Match(handler, @"\.ShouldCatch\(\s*\w+\s*,\s*Environment\.TickCount64\s*\)");
        int navigate = handler.IndexOf(".Navigate(EfbUrl)", StringComparison.Ordinal);
        int speak = handler.IndexOf("_announcer.AnnounceImmediate(", StringComparison.Ordinal);
        Assert.True(classify >= 0, "the handler does not sort the address with FenixEfbRoutes.Classify");
        Assert.True(message > classify, "the handler does not take the line from FenixEfbRoutes.DeadEndMessage");
        Assert.True(gate.Success, "the handler does not ask the dead-end gate on Environment.TickCount64");
        Assert.True(navigate > gate.Index, "the handler does not go back to EfbUrl after the gate");
        Assert.True(speak > gate.Index, "the handler does not speak with AnnounceImmediate after the gate");
        Assert.Contains("Log.Debug(\"FenixEFB\"", handler);

        // A wall clock can jump; the window never times anything with it.
        Assert.DoesNotContain("DateTime.Now", source);
        Assert.DoesNotContain("DateTime.UtcNow", source);
    }

    // The text of a method's body, from its signature to its closing brace (braces in its interpolated strings
    // are balanced, so a simple count holds).
    private static string MethodBody(string source, string method)
    {
        Match signature = Regex.Match(source, @"\bvoid\s+" + method + @"\s*\(");
        Assert.True(signature.Success, $"{method} is not in the form");
        int open = source.IndexOf('{', signature.Index);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        throw new InvalidOperationException($"{method} has no closing brace");
    }

    [Fact]
    public void Dead_ends_are_recognised()
    {
        Assert.Equal(FenixEfbRoute.Mcdu, FenixEfbRoutes.Classify("http://127.0.0.1:8083/#/mcdu"));
        Assert.Equal(FenixEfbRoute.Mcdu, FenixEfbRoutes.Classify("http://127.0.0.1:8083/#/mcdu?cdu=1&fullscreen=false"));
        Assert.Equal(FenixEfbRoute.Fap, FenixEfbRoutes.Classify("http://127.0.0.1:8083/#/fap/screen-saver"));
        Assert.Equal(FenixEfbRoute.Fap, FenixEfbRoutes.Classify("http://127.0.0.1:8083/#/fap/cabin-status"));
    }

    [Fact]
    public void Efb_routes_are_left_alone()
    {
        Assert.Equal(FenixEfbRoute.Efb, FenixEfbRoutes.Classify("http://127.0.0.1:8083/?target=external#/efb"));
        Assert.Equal(FenixEfbRoute.Efb, FenixEfbRoutes.Classify("http://127.0.0.1:8083/#/efb/fenix/ground-services"));
        Assert.Equal(FenixEfbRoute.Efb, FenixEfbRoutes.Classify("http://127.0.0.1:8083/#/efb/settings"));
        Assert.Equal(FenixEfbRoute.Efb, FenixEfbRoutes.Classify("http://127.0.0.1:8083/"));

        Assert.Equal(FenixEfbRoute.Other, FenixEfbRoutes.Classify("https://support.fenixsim.com/#/mcdu"));
        Assert.Equal(FenixEfbRoute.Other, FenixEfbRoutes.Classify(null));
        Assert.Equal(FenixEfbRoute.Other, FenixEfbRoutes.Classify(""));
    }

    [Theory]
    [InlineData("http://example.com/#/fap/cabin-status")]
    [InlineData("http://127.0.0.1:9999/#/mcdu")]
    [InlineData("https://127.0.0.1:8083/#/fap")]
    [InlineData("http://localhost:8083/#/mcdu")]
    [InlineData("http://127.0.0.1:8083/#/somewhere/else")]
    [InlineData("http://127.0.0.1:8083/#mcdu")]
    [InlineData("not a url")]
    public void Another_origin_another_page_or_an_unparseable_url_is_other(string url)
    {
        Assert.Equal(FenixEfbRoute.Other, FenixEfbRoutes.Classify(url));
    }

    [Fact]
    public void The_scheme_host_and_segment_are_compared_ignoring_case()
    {
        Assert.Equal(FenixEfbRoute.Mcdu, FenixEfbRoutes.Classify("HTTP://127.0.0.1:8083/#/MCDU"));
        Assert.Equal(FenixEfbRoute.Fap, FenixEfbRoutes.Classify("http://127.0.0.1:8083/#/Fap/cabin-status"));
    }

    [Fact]
    public void Each_dead_end_says_where_to_go()
    {
        Assert.Equal(
            "The EFB's MCDU can't be read with a screen reader. Use the MCDU window instead: input mode, then Shift+M.",
            FenixEfbRoutes.DeadEndMessage(FenixEfbRoute.Mcdu));
        Assert.Equal(
            "The EFB's FAP can't be read with a screen reader. Use the Fenix windows list instead: input mode, then Control+Shift+F.",
            FenixEfbRoutes.DeadEndMessage(FenixEfbRoute.Fap));
        Assert.Null(FenixEfbRoutes.DeadEndMessage(FenixEfbRoute.Efb));
        Assert.Null(FenixEfbRoutes.DeadEndMessage(FenixEfbRoute.Other));
    }
}
