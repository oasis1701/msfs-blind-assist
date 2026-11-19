namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// Composes what the navigation display reader says by itself: the ND's messages appearing ("ND message:
/// EXCESS DATA") and going ("ND message gone: …"), the items of one way on one line in list order with the
/// prefix once, then the active waypoint changing ("ND: PT753, 23.0 NM", name and distance as the corner
/// prints them; "ND: no active waypoint" when the route has none); and its status lines. Nothing is said
/// unless both readings have the displays lit (with them dark no one sees what PMDG sets); messages are
/// compared only while both readings draw the Captain's ND (on no display, no one draws them; the waypoint is
/// the FMC's). Pure; numbers invariant.
/// </summary>
public static class Pmdg737NavDisplaySpeech
{
    public const string Starting = "Navigation display starting";
    public const string Resumed = "ND announcements resumed";

    /// <summary>Output Ctrl+W's answer while the route has no TO waypoint.</summary>
    public const string NoActiveWaypoint = "No active waypoint";

    public static string Unavailable(string reason) => $"ND announcements unavailable: {reason}";
    public static string Stopped(string reason) => $"ND announcements stopped: {reason}";

    /// <summary>What the window's list says while the reader is unavailable. Output Alt+N said it too until 2026-10-31; it
    /// now goes straight to the AI read (WASM-29).</summary>
    public static string ListUnavailable(string reason) => $"Navigation display unavailable: {reason}";

    /// <summary>What the window's list says during a short stop, never stale rows.</summary>
    public static string ListStopped(string reason) => $"Navigation display stopped: {reason}, trying again";

    /// <summary>Output Ctrl+W's answer when the reader has no good reading.</summary>
    public static string WaypointUnavailable(string reason) => $"ND waypoint unavailable: {reason}";

    /// <summary>Why Ctrl+W has no waypoint while the reader waits for the PMDG SDK data (the FMA reader says the
    /// same); the window's list says starting.</summary>
    public const string WaitingForSdk = "waiting for PMDG SDK data";

    /// <summary>The lines for everything that changed from <paramref name="before"/> to <paramref name="after"/>.</summary>
    public static IReadOnlyList<string> Changes(NavDisplaySample before, NavDisplaySample after, Pmdg737NavDisplayLayout l)
    {
        var lines = new List<string>();
        if (!Pmdg737NavDisplayCrossCheck.DisplaysLit(before, l) || !Pmdg737NavDisplayCrossCheck.DisplaysLit(after, l)) return lines;

        if (Pmdg737NavDisplayText.Shown(before, l) && Pmdg737NavDisplayText.Shown(after, l))
        {
            var was = Pmdg737NavDisplayText.Messages(before, l);
            var now = Pmdg737NavDisplayText.Messages(after, l);
            var appearing = now.Where(m => !was.Contains(m)).ToList();
            var going = was.Where(m => !now.Contains(m)).ToList();
            if (appearing.Count > 0) lines.Add("ND message: " + string.Join(", ", appearing));
            if (going.Count > 0) lines.Add("ND message gone: " + string.Join(", ", going));
        }

        if (before.Active?.Name != after.Active?.Name)
        {
            if (after.Active is not { } w)
            {
                lines.Add("ND: no active waypoint");
            }
            else
            {
                bool data = w.LegType != l.Route.NoDataLegType;
                lines.Add($"ND: {w.Name}, {(data ? Pmdg737NavDisplayText.Distance(w.Distance) : "---")} NM");
            }
        }
        return lines;
    }
}
