using System.Diagnostics;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// What the Navigraph sign-in flow needs from the app (plan 18, Task 6): the hidden helper view that holds BlueBird's sign-in
/// (<c>CoherentBbs757TabletClient</c>), the pilot's screen reader and browser, and a clock. Every member is the caller's, so the
/// flow runs over fakes in tests.
/// </summary>
public sealed class Bbs757NavigraphIo
{
    /// <summary>
    /// Signed in or not, and the pending device sign-in's user code and address, read now from the helper view
    /// (<c>CoherentBbs757TabletClient.GetNavigraphAsync</c>): (false, null, null) when it cannot be read.
    /// </summary>
    public required Func<Task<(bool SignedIn, string? Code, string? Uri)>> GetNavigraph { get; init; }

    /// <summary>
    /// Waits a little for the helper view to answer and says whether it does (<see cref="Bbs757NavigraphSignIn.WaitReadableAsync"/>
    /// over the client's <c>IsReadable</c>): "not signed in, no code" and "not reachable" read the same from
    /// <see cref="GetNavigraph"/>.
    /// </summary>
    public required Func<CancellationToken, Task<bool>> WaitReadable { get; init; }

    /// <summary>Says a text with the screen reader; the flag is whether it is said at once (interrupting) or queued.</summary>
    public required Action<string, bool> Speak { get; init; }

    /// <summary>Waits the given milliseconds.</summary>
    public required Func<int, CancellationToken, Task> Delay { get; init; }

    /// <summary>Opens an address in the pilot's browser (<see cref="Bbs757NavigraphSignIn.OpenInBrowser"/>); false when it could not.</summary>
    public required Func<Uri, bool> OpenBrowser { get; init; }

    /// <summary>
    /// Puts the line the page keeps ("Navigraph code: {code}") on the page as soon as a code was read, before it is spoken, so the
    /// pilot can read it character by character while it is said (the tablet window, plan 18 Task 8); null when nothing shows it
    /// early (the line is still returned). Called on the flow's own thread; never given anything but that line.
    /// </summary>
    public Action<string>? Show { get; init; }
}

/// <summary>
/// Signing in to Navigraph through BlueBird's tablet (plan 18, Task 6; moved out of the classic 757 EFB window). BlueBird shows a
/// device sign-in: a user code and an address to open in a browser. The tablet window offers this as an extra button of the AIRAC
/// page, so the code is read from the hidden helper view that holds BlueBird's sign-in, never from the picture. The code is
/// spoken letter by letter BEFORE the browser opens (opening it moves focus, and the screen reader reading the browser would cut
/// the code off), and kept on the page for reading character by character. Only an https address on navigraph.com or
/// identity.api.navigraph.com is ever opened. The code is never logged, and neither is the address it came with.
/// </summary>
public static class Bbs757NavigraphSignIn
{
    /// <summary>The helper view does not answer (the 757 is not loaded, or its tablet is not up).</summary>
    public const string NotReachableText = "The 757's EFB is not reachable. Is the aircraft loaded?";

    private const string AlreadySignedInText = "Already signed in to Navigraph";
    private const string NoCodeText = "No Navigraph sign-in code is waiting yet. Try again in a minute.";
    private const string NoAddressText = "No sign-in address came with it.";
    private const string NotNavigraphAddressText = "Its sign-in address is not a Navigraph address, so it was not opened.";
    private const string NotOpenedText = "The sign-in page could not be opened.";

    /// <summary>
    /// How long the helper view is given to answer: the tablet client scrapes every second while the CDU window is hidden, and a
    /// client that has just connected is readable only after its first scrape (about a second later, measured), so two cycles and a margin.
    /// </summary>
    public const int ReadableWaitMs = 2500;

    private const int ReadablePollMs = 100;

    /// <summary>The two hosts a sign-in address may have to be opened.</summary>
    private static readonly string[] NavigraphHosts = { "navigraph.com", "identity.api.navigraph.com" };

    /// <summary>
    /// Whether a sign-in address may be opened in the pilot's browser: an absolute https address on
    /// the default port, no user name, its host exactly navigraph.com or identity.api.navigraph.com.
    /// The host is compared whole, never searched in the text ("navigraph.com.example" and
    /// "example/navigraph.com" are refused).
    /// </summary>
    public static bool IsTrustedNavigraphUri(string? text, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttps || !parsed.IsDefaultPort || parsed.UserInfo.Length > 0) return false;
        foreach (string host in NavigraphHosts)
        {
            if (string.Equals(parsed.Host, host, StringComparison.OrdinalIgnoreCase))
            {
                uri = parsed;
                return true;
            }
        }
        return false;
    }

    /// <summary>A sign-in code spelled for speech: "A B 1 2", a dash said as "dash", spaces dropped.</summary>
    public static string SpellCode(string code)
    {
        var parts = new List<string>();
        foreach (char c in code)
        {
            if (char.IsWhiteSpace(c)) continue;
            parts.Add(c == '-' ? "dash" : c.ToString());
        }
        return string.Join(" ", parts);
    }

    /// <summary>
    /// How long the browser waits after the code is spoken, so opening it (which moves focus, and
    /// NVDA then reads the browser) does not cut the code off: 1.5 s and 350 ms per spoken
    /// character (a dash is a word), at least 2 s and at most 6 s. The code also stays on the page.
    /// </summary>
    public static int CodeSpeechMs(string code)
    {
        int spoken = SpellCode(code).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Clamp(1500 + 350 * spoken, 2000, 6000);
    }

    /// <summary>
    /// The sign-in, said as it goes and returned as the line the page keeps: "Navigraph code: {code}" once a code was spoken
    /// (whether or not its address could be opened: the pilot can type the code in by hand; given to <see cref="Bbs757NavigraphIo.Show"/>
    /// first, before it is spoken), else the refusal or the news that the pilot is already signed in, which is also what was said. A
    /// refusal is said at once, everything else queued:
    /// <list type="bullet">
    /// <item>the helper view does not answer: <see cref="NotReachableText"/>; it is read again once it does, because a client that
    /// has just connected answers empty;</item>
    /// <item>signed in: "Already signed in to Navigraph";</item>
    /// <item>no code waiting: "No Navigraph sign-in code is waiting yet. Try again in a minute.";</item>
    /// <item>a code with no address, or with one that is not Navigraph's: the code spelled, and why nothing was opened;</item>
    /// <item>a code with a Navigraph address: the code spelled and "The sign-in page opens in your browser in a moment.", then
    /// <see cref="CodeSpeechMs"/> for the speech to finish, then the browser; "The sign-in page could not be opened." if it would not.</item>
    /// </list>
    /// Throws <see cref="OperationCanceledException"/> when <paramref name="ct"/> is canceled (the window goes away), and says
    /// nothing after that.
    /// </summary>
    public static async Task<string> SignInAsync(Bbs757NavigraphIo io, CancellationToken ct)
    {
        var (signedIn, code, uriText) = await io.GetNavigraph();
        ct.ThrowIfCancellationRequested();
        if (!signedIn && string.IsNullOrWhiteSpace(code))
        {
            // Nothing read, or read too soon after the helper's client connected: read again once
            // it answers, so the answer given is the helper's, not the empty one.
            bool readable = await io.WaitReadable(ct);
            ct.ThrowIfCancellationRequested();
            if (!readable) return Refuse(io, NotReachableText);
            (signedIn, code, uriText) = await io.GetNavigraph();
            ct.ThrowIfCancellationRequested();
        }
        if (signedIn) return Say(io, AlreadySignedInText);
        if (string.IsNullOrWhiteSpace(code)) return Refuse(io, NoCodeText);

        // The code is spoken BEFORE the browser opens: opening it moves focus and the screen reader
        // reads the browser, which would cut the code off. The page keeps the code as it is,
        // for reading character by character, and has it before it is spoken.
        string line = $"Navigraph code: {code.Trim()}";
        io.Show?.Invoke(line);
        if (!IsTrustedNavigraphUri(uriText, out var uri) || uri == null)
        {
            string why = string.IsNullOrWhiteSpace(uriText) ? NoAddressText : NotNavigraphAddressText;
            io.Speak($"Navigraph code {SpellCode(code)}. {why}", false);
            return line;
        }

        io.Speak($"Navigraph code {SpellCode(code)}. The sign-in page opens in your browser in a moment.", false);
        await io.Delay(CodeSpeechMs(code), ct);
        ct.ThrowIfCancellationRequested();
        if (!io.OpenBrowser(uri)) io.Speak(NotOpenedText, false);
        return line;
    }

    private static string Refuse(Bbs757NavigraphIo io, string text)
    {
        io.Speak(text, true);
        return text;
    }

    private static string Say(Bbs757NavigraphIo io, string text)
    {
        io.Speak(text, false);
        return text;
    }

    /// <summary>
    /// Waits for the helper view to answer: true at once when it does, else after polling every tenth of a second for
    /// <see cref="ReadableWaitMs"/> in all, false when it still does not. Throws <see cref="OperationCanceledException"/> when
    /// <paramref name="delay"/> does (the window goes away).
    /// </summary>
    public static async Task<bool> WaitReadableAsync(Func<bool> isReadable, Func<int, CancellationToken, Task> delay, CancellationToken ct)
    {
        for (int waited = 0; !isReadable(); waited += ReadablePollMs)
        {
            if (waited >= ReadableWaitMs) return false;
            await delay(ReadablePollMs, ct);
        }
        return true;
    }

    /// <summary>Opens an address in the pilot's default browser; false when that fails. Never logs the address, which carries the code.</summary>
    public static bool OpenInBrowser(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            // Never the address: it carries the sign-in code.
            Log.Debug("BBS757", $"Navigraph sign-in page not opened: {ex.GetType().Name}");
            return false;
        }
    }
}
