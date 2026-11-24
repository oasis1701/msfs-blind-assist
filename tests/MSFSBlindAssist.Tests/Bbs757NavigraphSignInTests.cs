using System.Globalization;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The Navigraph sign-in of BlueBird's tablet (<see cref="Bbs757NavigraphSignIn"/>), moved out of the classic 757 EFB window
/// for the tablet's own window (plan 18, Task 6): the address a code may be opened at, the code spelled for speech, how long
/// the browser waits for the speech, and the flow itself over a fake tablet, a fake clock and a fake browser. The first three
/// are the classic window's old tests, unchanged.
/// </summary>
public class Bbs757NavigraphSignInTests
{
    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    // ---- Navigraph sign-in helpers (moved from the classic window) ----------

    [Theory]
    [InlineData("https://navigraph.com/code?user_code=ABCD1234", true)]
    [InlineData("https://identity.api.navigraph.com/code?user_code=ABCD1234", true)]
    [InlineData("https://NAVIGRAPH.COM/code", true)]
    [InlineData("http://navigraph.com/code", false)]                // not https
    [InlineData("https://evilnavigraph.com/code", false)]
    [InlineData("https://navigraph.com.evil.example/code", false)]
    [InlineData("https://evil.example/navigraph.com", false)]
    [InlineData("https://evil.example/?next=https://navigraph.com", false)]
    [InlineData("https://navigraph.com@evil.example/", false)]
    [InlineData("https://sub.navigraph.com/code", false)]
    [InlineData("https://navigraph.com:8443/code", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    [InlineData("navigraph.com/code", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_https_navigraph_addresses_are_opened(string? uri, bool expected)
    {
        Assert.Equal(expected, Bbs757NavigraphSignIn.IsTrustedNavigraphUri(uri, out _));
    }

    [Theory]
    [InlineData("ABCD1234", "A B C D 1 2 3 4")]
    [InlineData("ab-12", "a b dash 1 2")]
    [InlineData(" XY ", "X Y")]
    public void The_navigraph_code_is_spelled_letter_by_letter(string code, string expected)
    {
        Assert.Equal(expected, Bbs757NavigraphSignIn.SpellCode(code));
    }

    [Theory]
    [InlineData("A", 2000)]                           // never shorter than two seconds
    [InlineData("ABCD", 2900)]                        // 1.5 s, then 350 ms a spoken character
    [InlineData("ABCD-1234", 4650)]                   // the dash is a word too
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ", 6000)]
    public void The_browser_waits_for_the_code_to_be_spoken(string code, int expectedMs)
    {
        Assert.Equal(expectedMs, Bbs757NavigraphSignIn.CodeSpeechMs(code));
    }

    [Fact]
    public void A_trusted_address_comes_back_as_the_uri_to_open()
    {
        Assert.True(Bbs757NavigraphSignIn.IsTrustedNavigraphUri(" https://navigraph.com/code?user_code=AB-12 ", out var uri));
        Assert.Equal("https://navigraph.com/code?user_code=AB-12", uri!.AbsoluteUri);

        Assert.False(Bbs757NavigraphSignIn.IsTrustedNavigraphUri("https://evil.example/", out var refused));
        Assert.Null(refused);
    }

    // ---- the flow, over fakes -----------------------------------------------------------------------------------------------

    private const string NotReachable = "The 757's EFB is not reachable. Is the aircraft loaded?";
    private const string Trusted = "https://navigraph.com/code?user_code=ABCD1234";

    /// <summary>A tablet that answers the given reads in turn (the last one again and again), a browser, and what happened, in order.</summary>
    private sealed class Fakes
    {
        public readonly List<string> Events = new();
        public (bool SignedIn, string? Code, string? Uri)[] Reads = { (false, null, null) };
        public bool Readable = true;
        public bool BrowserOpens = true;
        /// <summary>Whether the page's early line is taken (the tablet window's), so it shows among the events.</summary>
        public bool Shows;
        private int _next;

        public Bbs757NavigraphIo Io => new()
        {
            Show = Shows ? line => Events.Add("show: " + line) : null,
            GetNavigraph = () =>
            {
                Events.Add("read");
                return Task.FromResult(Reads[Math.Min(_next++, Reads.Length - 1)]);
            },
            WaitReadable = _ =>
            {
                Events.Add("wait");
                return Task.FromResult(Readable);
            },
            Speak = (text, atOnce) => Events.Add((atOnce ? "say now: " : "say: ") + text),
            Delay = (ms, ct) =>
            {
                Events.Add("delay " + ms);
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            OpenBrowser = uri =>
            {
                Events.Add("open " + uri.AbsoluteUri);
                return BrowserOpens;
            },
        };
    }

    [Fact]
    public async Task Signed_in_says_so_and_asks_for_nothing_more()
    {
        var fakes = new Fakes { Reads = new[] { (true, (string?)null, (string?)null) } };

        string line = await Bbs757NavigraphSignIn.SignInAsync(fakes.Io, CancellationToken.None);

        Assert.Equal("Already signed in to Navigraph", line);
        Assert.Equal(new[] { "read", "say: Already signed in to Navigraph" }, fakes.Events);
    }

    [Fact]
    public async Task A_tablet_that_does_not_answer_is_said_to_be_unreachable_at_once()
    {
        var fakes = new Fakes { Readable = false };

        string line = await Bbs757NavigraphSignIn.SignInAsync(fakes.Io, CancellationToken.None);

        Assert.Equal(NotReachable, line);
        Assert.Equal(new[] { "read", "wait", "say now: " + NotReachable }, fakes.Events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task No_code_once_the_helper_answers_asks_for_another_try_at_once(string? code)
    {
        var fakes = new Fakes { Reads = new[] { (false, code, (string?)null) } };

        string line = await Bbs757NavigraphSignIn.SignInAsync(fakes.Io, CancellationToken.None);

        Assert.Equal("No Navigraph sign-in code is waiting yet. Try again in a minute.", line);
        Assert.Equal(new[]
        {
            "read", "wait", "read",
            "say now: No Navigraph sign-in code is waiting yet. Try again in a minute.",
        }, fakes.Events);
    }

    [Fact]
    public async Task A_code_that_comes_with_the_second_read_is_used()
    {
        // A client that has just connected answers empty until its first scrape: read again once it answers.
        var fakes = new Fakes
        {
            Reads = new[] { (false, (string?)null, (string?)null), (false, "AB-12", (string?)"https://navigraph.com/code?user_code=AB-12") },
        };

        string line = await Bbs757NavigraphSignIn.SignInAsync(fakes.Io, CancellationToken.None);

        Assert.Equal("Navigraph code: AB-12", line);
        Assert.Equal(new[]
        {
            "read", "wait", "read",
            "say: Navigraph code A B dash 1 2. The sign-in page opens in your browser in a moment.",
            "delay 3250",
            "open https://navigraph.com/code?user_code=AB-12",
        }, fakes.Events);
    }

    [Fact]
    public async Task A_code_is_spoken_before_the_browser_opens_and_the_browser_waits_for_the_speech()
    {
        var fakes = new Fakes { Reads = new[] { (false, (string?)" ABCD1234 ", (string?)Trusted) } };

        string line = await Bbs757NavigraphSignIn.SignInAsync(fakes.Io, CancellationToken.None);

        Assert.Equal("Navigraph code: ABCD1234", line);   // the code kept on the page, as given
        Assert.Equal(new[]
        {
            "read",
            "say: Navigraph code A B C D 1 2 3 4. The sign-in page opens in your browser in a moment.",
            "delay " + Bbs757NavigraphSignIn.CodeSpeechMs("ABCD1234"),
            "open " + Trusted,
        }, fakes.Events);
        Assert.Equal(4300, Bbs757NavigraphSignIn.CodeSpeechMs("ABCD1234"));   // 1.5 s and 350 ms for each of the eight
    }

    [Fact]
    public async Task The_code_is_on_the_page_before_it_is_spoken()
    {
        // Plan 18, Task 8: the tablet window keeps the code for reading character by character while it is spoken, not after the
        // two to six seconds the browser waits for the speech.
        var fakes = new Fakes { Reads = new[] { (false, (string?)" ABCD1234 ", (string?)Trusted) }, Shows = true };

        string line = await Bbs757NavigraphSignIn.SignInAsync(fakes.Io, CancellationToken.None);

        Assert.Equal("Navigraph code: ABCD1234", line);
        Assert.Equal(new[]
        {
            "read",
            "show: Navigraph code: ABCD1234",
            "say: Navigraph code A B C D 1 2 3 4. The sign-in page opens in your browser in a moment.",
            "delay 4300",
            "open " + Trusted,
        }, fakes.Events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://evil.example/")]
    public async Task A_code_with_no_address_to_open_is_on_the_page_too(string? address)
    {
        var fakes = new Fakes { Reads = new[] { (false, (string?)"ABCD1234", address) }, Shows = true };

        string line = await Bbs757NavigraphSignIn.SignInAsync(fakes.Io, CancellationToken.None);

        Assert.Equal("Navigraph code: ABCD1234", line);
        Assert.Equal("show: Navigraph code: ABCD1234", fakes.Events[1]);
        Assert.StartsWith("say: Navigraph code A B C D 1 2 3 4. ", fakes.Events[2]);
        Assert.Equal(3, fakes.Events.Count);
    }

    [Theory]
    [InlineData(true, null)]       // signed in already
    [InlineData(false, "")]        // no code waiting
    public async Task Nothing_is_put_on_the_page_without_a_code(bool signedIn, string? code)
    {
        var fakes = new Fakes { Reads = new[] { (signedIn, code, (string?)null) }, Shows = true };

        await Bbs757NavigraphSignIn.SignInAsync(fakes.Io, CancellationToken.None);

        Assert.DoesNotContain(fakes.Events, e => e.StartsWith("show:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://evil.example/?next=https://navigraph.com")]
    [InlineData("http://navigraph.com/code")]
    [InlineData("https://navigraph.com.evil.example/code")]
    [InlineData("javascript:alert(1)")]
    public async Task An_address_that_is_not_Navigraphs_is_never_opened(string address)
    {
        var fakes = new Fakes { Reads = new[] { (false, (string?)"ABCD1234", (string?)address) } };

        string line = await Bbs757NavigraphSignIn.SignInAsync(fakes.Io, CancellationToken.None);

        Assert.Equal("Navigraph code: ABCD1234", line);
        Assert.Equal(new[]
        {
            "read",
            "say: Navigraph code A B C D 1 2 3 4. Its sign-in address is not a Navigraph address, so it was not opened.",
        }, fakes.Events);   // no wait for the speech either: nothing follows it
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task A_code_with_no_address_is_spoken_and_nothing_is_opened(string? address)
    {
        var fakes = new Fakes { Reads = new[] { (false, (string?)"ABCD1234", address) } };

        string line = await Bbs757NavigraphSignIn.SignInAsync(fakes.Io, CancellationToken.None);

        Assert.Equal("Navigraph code: ABCD1234", line);
        Assert.Equal(new[] { "read", "say: Navigraph code A B C D 1 2 3 4. No sign-in address came with it." }, fakes.Events);
    }

    [Fact]
    public async Task A_browser_that_does_not_open_is_said_after_the_attempt()
    {
        var fakes = new Fakes { Reads = new[] { (false, (string?)"ABCD1234", (string?)Trusted) }, BrowserOpens = false };

        string line = await Bbs757NavigraphSignIn.SignInAsync(fakes.Io, CancellationToken.None);

        Assert.Equal("Navigraph code: ABCD1234", line);   // the code stays on the page to be typed in by hand
        Assert.Equal(new[]
        {
            "read",
            "say: Navigraph code A B C D 1 2 3 4. The sign-in page opens in your browser in a moment.",
            "delay 4300",
            "open " + Trusted,
            "say: The sign-in page could not be opened.",
        }, fakes.Events);
    }

    [Fact]
    public async Task A_sign_in_canceled_while_the_code_is_being_spoken_never_opens_the_browser()
    {
        var events = new List<string>();
        using var cts = new CancellationTokenSource();
        var io = new Bbs757NavigraphIo
        {
            GetNavigraph = () => Task.FromResult((false, (string?)"ABCD1234", (string?)Trusted)),
            WaitReadable = _ => Task.FromResult(true),
            Speak = (text, atOnce) => events.Add("say: " + text),
            Delay = (ms, ct) => Task.Delay(Timeout.Infinite, ct),   // the wait for the speech, until the window goes
            OpenBrowser = uri => { events.Add("open"); return true; },
        };

        var running = Bbs757NavigraphSignIn.SignInAsync(io, cts.Token);
        Assert.False(running.IsCompleted);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(new[] { "say: Navigraph code A B C D 1 2 3 4. The sign-in page opens in your browser in a moment." }, events);
    }

    [Fact]
    public async Task A_sign_in_canceled_while_the_tablet_is_being_read_says_nothing()
    {
        // The window went away while the read was out: its answer is not spoken afterwards.
        var events = new List<string>();
        var answer = new TaskCompletionSource<(bool SignedIn, string? Code, string? Uri)>();
        using var cts = new CancellationTokenSource();
        var io = new Bbs757NavigraphIo
        {
            GetNavigraph = () => answer.Task,
            WaitReadable = _ => Task.FromResult(true),
            Speak = (text, atOnce) => events.Add("say: " + text),
            Delay = (ms, ct) => Task.CompletedTask,
            OpenBrowser = uri => { events.Add("open"); return true; },
        };

        var running = Bbs757NavigraphSignIn.SignInAsync(io, cts.Token);
        cts.Cancel();
        answer.SetResult((false, "ABCD1234", Trusted));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Empty(events);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task The_speech_the_line_and_the_wait_are_the_same_in_any_Windows_culture(string culture)
    {
        var fakes = new Fakes { Reads = new[] { (false, (string?)"ABCD-1234", (string?)Trusted) } };
        var saved = CultureInfo.CurrentCulture;
        string line;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            line = await Bbs757NavigraphSignIn.SignInAsync(fakes.Io, CancellationToken.None);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }

        Assert.Equal("Navigraph code: ABCD-1234", line);
        Assert.Equal(new[]
        {
            "read",
            "say: Navigraph code A B C D dash 1 2 3 4. The sign-in page opens in your browser in a moment.",
            "delay 4650",
            "open " + Trusted,
        }, fakes.Events);
    }

    // ---- waiting for the helper to answer ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_helper_that_already_answers_is_not_waited_for()
    {
        var waits = new List<int>();

        bool readable = await Bbs757NavigraphSignIn.WaitReadableAsync(() => true, (ms, _) => { waits.Add(ms); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.True(readable);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task A_helper_that_starts_answering_is_waited_for_in_tenths_of_a_second()
    {
        var waits = new List<int>();
        int asked = 0;

        bool readable = await Bbs757NavigraphSignIn.WaitReadableAsync(() => ++asked > 3, (ms, _) => { waits.Add(ms); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.True(readable);
        Assert.Equal(new[] { 100, 100, 100 }, waits);
    }

    [Fact]
    public async Task A_helper_that_never_answers_is_given_two_and_a_half_seconds()
    {
        // A client that has just connected is readable after its first scrape, about a second later (measured).
        var waits = new List<int>();

        bool readable = await Bbs757NavigraphSignIn.WaitReadableAsync(() => false, (ms, _) => { waits.Add(ms); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.False(readable);
        Assert.Equal(2500, waits.Sum());
        Assert.All(waits, w => Assert.Equal(100, w));
    }

    [Fact]
    public async Task The_wait_for_the_helper_ends_with_the_window()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Bbs757NavigraphSignIn.WaitReadableAsync(
            () => false, (ms, ct) => Task.Delay(ms, ct), cts.Token));
    }
}
