namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>Which kind of page the Fenix EFB window's browser is on.</summary>
internal enum FenixEfbRoute
{
    /// <summary>The EFB itself (home, settings, the Fenix pages), or the gateway root: left alone.</summary>
    Efb,
    /// <summary>The EFB's MCDU page: cannot be read with a screen reader (plan 03, decision 8).</summary>
    Mcdu,
    /// <summary>The EFB's FAP page: cannot be read with a screen reader (plan 03, decision 8).</summary>
    Fap,
    /// <summary>Anything else, including a page on another origin: left alone.</summary>
    Other,
}

/// <summary>
/// Sorts the address the Fenix EFB window is on (plan 03, Owner decision 8). The window steps back
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
    /// What to say when the window steps back from a dead end (decision 8, verbatim), or null for a
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
