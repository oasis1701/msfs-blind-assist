namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// Composes what the primary flight display reader says by itself: PMDG's messages appearing ("PFD message: ALT
/// DISAGREE") and going ("PFD message gone: …"), the items of one way on one line in layout order with the prefix once,
/// appearing before going; a flag that replaces an instrument is one of them (SPD, ALT …); what PMDG draws without
/// speaking it (WINDSHEAR, PULL UP: the aircraft calls them out) never; and its status lines. Nothing is said unless
/// both readings have the displays lit and the Captain's PFD on a screen (else no one sees what PMDG sets). Pure.
/// </summary>
public static class Pmdg737PfdSpeech
{
    public const string Starting = "Primary flight display starting";
    public const string Resumed = "PFD announcements resumed";

    /// <summary>Output Alt+P's line, interrupting, with the classic EFIS look on the Captain's screen; then the AI read follows
    /// (the owner's text, 2026-10-31: WASM-18, WASM-29).</summary>
    public const string KeyClassic = "Classic EFIS is selected, falling back to AI reading";

    /// <summary>Why there is no reading while the simulator has not delivered its airspeed and altitude.</summary>
    public const string WaitingForSimulator = "waiting for simulator data";

    public static string Unavailable(string reason) => $"PFD announcements unavailable: {reason}";
    public static string Stopped(string reason) => $"PFD announcements stopped: {reason}";

    /// <summary>What the window's list says while the reader is unavailable. Output Alt+P said it too until 2026-10-31; it
    /// now goes straight to the AI read (WASM-29).</summary>
    public static string ListUnavailable(string reason) => $"Primary flight display unavailable: {reason}";

    /// <summary>What the window's list says during a short stop, never stale rows.</summary>
    public static string ListStopped(string reason) => $"Primary flight display stopped: {reason}, trying again";

    /// <summary>The lines for the messages that came and went from <paramref name="before"/> to <paramref name="after"/>.</summary>
    public static IReadOnlyList<string> Changes(PfdSample before, PfdSample after, Pmdg737PfdLayout l)
    {
        var lines = new List<string>();
        if (!Visible(before, l) || !Visible(after, l)) return lines;
        var was = Pmdg737PfdText.SpokenMessages(before, l);
        var now = Pmdg737PfdText.SpokenMessages(after, l);
        var appearing = now.Where(m => !was.Contains(m)).ToList();
        var going = was.Where(m => !now.Contains(m)).ToList();
        if (appearing.Count > 0) lines.Add("PFD message: " + string.Join(", ", appearing));
        if (going.Count > 0) lines.Add("PFD message gone: " + string.Join(", ", going));
        return lines;
    }

    /// <summary>The displays lit and the Captain's glass PFD on a screen.</summary>
    internal static bool Visible(PfdSample s, Pmdg737PfdLayout l) =>
        Pmdg737PfdCrossCheck.DisplaysLit(s, l) && Pmdg737PfdCrossCheck.CaptainPfd(s, l.Units) == PfdShown.Shown;
}
