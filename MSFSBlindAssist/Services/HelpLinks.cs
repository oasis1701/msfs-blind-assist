namespace MSFSBlindAssist.Services;

/// <summary>
/// What the Help menu opens. User Guide opens the one-page copy that release.yml and preview.yml build from guide/
/// and put next to the exe, which matches the installed version, or the online guide when that copy is missing, as
/// it is in a local build. Project Website opens the website. See docs/user-guide.md.
/// </summary>
public static class HelpLinks
{
    /// <summary>
    /// The bundled guide's file name: a contract with release.yml and preview.yml, which write it (GUIDE-3);
    /// HelpLinksTests pins both sides.
    /// </summary>
    public const string BundledGuideFileName = "MSFS Blind Assist User Guide.html";

    public const string OnlineGuideUrl = "https://msfsblindassist.com/guide/";

    public const string WebsiteUrl = "https://msfsblindassist.com/";

    /// <summary>The bundled guide in <paramref name="appDirectory"/> when <paramref name="fileExists"/> finds it, else the online guide.</summary>
    public static (string Target, bool IsBundled) ResolveGuide(string appDirectory, Func<string, bool> fileExists)
    {
        string bundled = Path.Combine(appDirectory, BundledGuideFileName);
        return fileExists(bundled) ? (bundled, true) : (OnlineGuideUrl, false);
    }
}
