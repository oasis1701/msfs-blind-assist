namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>Which kind of page the Fenix EFB window's browser is on.</summary>
internal enum FenixEfbRoute
{
    /// <summary>The EFB itself (home, settings, the Fenix pages), or the gateway root: left alone.</summary>
    Efb,
    /// <summary>The EFB's MCDU page: cannot be read with a screen reader.</summary>
    Mcdu,
    /// <summary>The EFB's FAP page: cannot be read with a screen reader.</summary>
    Fap,
    /// <summary>Anything else, including a page on another origin: left alone.</summary>
    Other,
}

/// <summary>
/// Sorts the address the Fenix EFB window is on. The window steps back
/// to the EFB home from the MCDU and FAP pages, which a screen reader cannot use, and says where the
/// working windows are. Only the gateway's own origin (the one <see cref="FenixEFBForm.EfbUrl"/> names)
/// can be anything but <see cref="FenixEfbRoute.Other"/>.
/// </summary>
internal static class FenixEfbRoutes
{
    private static readonly Uri Gateway = new(FenixEFBForm.EfbUrl);

    /// <summary>
    /// Classifies <paramref name="url"/> by the first path segment of its fragment: <c>#/mcdu</c> is
    /// <see cref="FenixEfbRoute.Mcdu"/> (a trailing <c>?query</c> is ignored), <c>#/fap</c> is
    /// <see cref="FenixEfbRoute.Fap"/>, <c>#/efb</c> or no fragment at all is <see cref="FenixEfbRoute.Efb"/>.
    /// A URL that does not parse, is not on the gateway's origin, or names any other segment is
    /// <see cref="FenixEfbRoute.Other"/>.
    /// </summary>
    internal static FenixEfbRoute Classify(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return FenixEfbRoute.Other;
        }
        if (!string.Equals(uri.Scheme, Gateway.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, Gateway.Host, StringComparison.OrdinalIgnoreCase)
            || uri.Port != Gateway.Port)
        {
            return FenixEfbRoute.Other;
        }

        string fragment = uri.Fragment;
        if (fragment.Length <= 1)
        {
            return FenixEfbRoute.Efb;
        }
        if (!fragment.StartsWith("#/", StringComparison.Ordinal))
        {
            return FenixEfbRoute.Other;
        }

        string rest = fragment.Substring(2);
        int end = rest.IndexOfAny(['/', '?']);
        string segment = end < 0 ? rest : rest.Substring(0, end);

        if (segment.Equals("mcdu", StringComparison.OrdinalIgnoreCase)) return FenixEfbRoute.Mcdu;
        if (segment.Equals("fap", StringComparison.OrdinalIgnoreCase)) return FenixEfbRoute.Fap;
        if (segment.Equals("efb", StringComparison.OrdinalIgnoreCase)) return FenixEfbRoute.Efb;
        return FenixEfbRoute.Other;
    }

    /// <summary>
    /// What to say when the window steps back from a dead end (the owner's wording, verbatim), or null for a
    /// route that needs no stepping back.
    /// </summary>
    internal static string? DeadEndMessage(FenixEfbRoute route) => route switch
    {
        FenixEfbRoute.Mcdu =>
            "The EFB's MCDU can't be read with a screen reader. Use the MCDU window instead: input mode, then Shift+M.",
        FenixEfbRoute.Fap =>
            "The EFB's FAP can't be read with a screen reader. Use the Fenix windows list instead: input mode, then Control+Shift+F.",
        _ => null,
    };
}

/// <summary>
/// Decides whether a dead end the Fenix EFB window has just met is caught. The EFB's router may
/// change the address twice on its way into the MCDU, so a second catch of the same route within
/// <see cref="WindowMs"/> of the last catch does nothing: no second navigation, no second spoken line. A
/// different dead end is always caught, and an ignored catch does not hold the gate shut any longer. Times are
/// a monotonic tick count in milliseconds (<see cref="Environment.TickCount64"/>), never the wall clock, which
/// can jump.
/// </summary>
internal sealed class FenixEfbDeadEndGate
{
    /// <summary>How long after a catch the same dead end is ignored, in milliseconds.</summary>
    internal const long WindowMs = 1_500;

    private FenixEfbRoute? _lastRoute;
    private long _lastTick;

    /// <summary>
    /// True when <paramref name="route"/>, met at <paramref name="nowTick"/>, is to be caught (it is then the
    /// last catch); false for the same route met again less than <see cref="WindowMs"/> after the last catch.
    /// </summary>
    internal bool ShouldCatch(FenixEfbRoute route, long nowTick)
    {
        if (_lastRoute == route && nowTick - _lastTick < WindowMs) return false;
        _lastRoute = route;
        _lastTick = nowTick;
        return true;
    }
}
