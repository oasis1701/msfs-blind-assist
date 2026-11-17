using System.Globalization;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// What a press on the tablet starts in the app besides the tap itself (plan 18, Task 6): the SimBrief fetch, the fuel import and
/// the start of boarding start the load watch, and the nav data page's UPDATE or REMOVE starts the nav data watch.
/// </summary>
public enum Bbs757EfbHook
{
    None,
    /// <summary>FETCH or REFRESH SIMBRIEF OFP on the ACARS dashboard.</summary>
    Fetch,
    /// <summary>IMPORT FROM SIMBRIEF on the Fuel page.</summary>
    Import,
    /// <summary>START BOARDING in the top bar.</summary>
    StartBoarding,
    /// <summary>The AIRAC page's button, labeled UPDATE.</summary>
    NavDataUpdate,
    /// <summary>The AIRAC page's button, labeled REMOVE.</summary>
    NavDataRemove,
}

/// <summary>
/// The app's extras on the tablet's pages, as pure decisions (plan 18, Task 6). The tablet window taps the control the pilot
/// pressed; these say which presses the app must also act on, and what to say of the result. Nothing here refuses a press (the
/// owner's ruling of 2026-10-29): the load watch only listens, the nav data watch only reads BlueBird's cycle file, and the
/// sign-in button is an addition to the page, not a replacement of a tablet control.
/// <list type="bullet">
/// <item><see cref="HookFor"/>: by the page and the words the reader gave the pressed control or its key (a box's own words,
/// without the caption the reader may name it by), case and spaces ignored (the reader's sentence case is
/// <see cref="Bbs757EfbVocabulary.Pretty"/>'s: "Fetch SIMBRIEF OFP"). FETCH or REFRESH SIMBRIEF OFP
/// only on the ACARS dashboard (its REMOVE FP is no hook), IMPORT FROM SIMBRIEF only on the Fuel page, UPDATE or REMOVE only on the
/// AIRAC page, START BOARDING only as the top bar's own control (<see cref="Bbs757EfbTopBar.BoardingKey"/>).</item>
/// <item><see cref="PartsFor"/>: which parts of the load a hook has BlueBird start, so which the load watch listens for.</item>
/// <item><see cref="OffersSignIn"/>: the Navigraph sign-in button, an extra of the AIRAC page while the pilot is signed out.</item>
/// <item><see cref="NavDataOutcome"/>: what the nav data watch says when BlueBird's cycle file has been watched long enough.</item>
/// <item><see cref="WatchNavDataAsync"/>: that watch's loop over the file (plan 18, Task 8; MainForm runs it on the UI thread).</item>
/// </list>
/// </summary>
public static class Bbs757EfbExtras
{
    /// <summary>The page BlueBird's FETCH and REFRESH SIMBRIEF OFP buttons are on, as <see cref="Bbs757EfbScreenModel.PageName"/> gives it.</summary>
    private const string AcarsPage = "Navigation, ACARS dashboard";

    /// <summary>The page IMPORT FROM SIMBRIEF is on.</summary>
    private const string FuelPage = "Aircraft, Fuel";

    /// <summary>The page of the Navigraph sign-in, the cycle numbers and the UPDATE or REMOVE button.</summary>
    private const string AiracPage = "Settings, AIRAC data";

    /// <summary>The button the app adds to the AIRAC page while the pilot is not signed in to Navigraph.</summary>
    public const string SignInText = "Sign in to Navigraph";

    /// <summary>
    /// Said once when the SimBrief plan behind a hook cannot be read, so the load watch has no targets and the app will not say when
    /// the fueling and the boarding are complete. The SimBrief ID is never part of it.
    /// </summary>
    public const string PlanUnreadableText = "The SimBrief plan could not be read, so the app won't say when fueling and boarding are complete.";

    /// <summary>Why there is no plan when BlueBird's settings file was read and holds no SimBrief ID.</summary>
    public const string NoSimBriefIdText = "No SimBrief ID is set in the 757's EFB settings.";

    /// <summary>
    /// What is said once when the SimBrief plan behind a hook cannot be read: the reason when one is known (SimBrief's fetch error,
    /// which <c>SimBriefService.FetchLatestOfpXmlAsync</c> words without the ID, or <see cref="NoSimBriefIdText"/>), then
    /// <see cref="PlanUnreadableText"/>; with no reason, that alone. Pure.
    /// </summary>
    public static string PlanUnreadable(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? PlanUnreadableText : $"{reason.Trim()} {PlanUnreadableText}";

    // The nav data watch's texts (plan 18's Global Constraints, verbatim). The cycle is a name, not an amount: it is said as a plain
    // number whatever the Windows culture.
    private const string NavDataRemovedText = "BlueBird removed the installed nav data.";
    private const string NavDataRemovedInsteadText =
        "BlueBird removed the installed nav data instead of updating it. With an active Navigraph subscription, reload the flight and update again.";
    private const string NavDataUnfinishedText = "The update did not finish. Try again after reloading the flight.";
    private const string NavDataNotRemovedText = "BlueBird did not remove the nav data.";

    /// <summary>
    /// The hook a press on <paramref name="item"/> of the page <paramref name="pageName"/> (as
    /// <see cref="Bbs757EfbScreenModel.PageName"/> gives it) starts, or <see cref="Bbs757EfbHook.None"/>. The control's words are
    /// its text or the words of its key (<see cref="Bbs757EfbScreen.KeyWords"/>: a box's own words, which never take the caption
    /// or row label the reader may name it by, so the AIRAC page's "Update, Latest cycle" is UPDATE). Words and page names are
    /// compared with case ignored and runs of spaces taken as one.
    /// </summary>
    public static Bbs757EfbHook HookFor(string pageName, Bbs757EfbItem item)
    {
        string words = Squeeze(item.Text);
        string keyWords = Squeeze(Bbs757EfbScreen.KeyWords(item.Key));
        string page = Squeeze(pageName);
        bool Says(string expected) => Same(words, expected) || Same(keyWords, expected);

        // The boarding control belongs to the top bar, which every page has: only the bar's own item, and only while it offers to start.
        if (string.Equals(item.Key, Bbs757EfbTopBar.BoardingKey, StringComparison.Ordinal))
            return Says("START BOARDING") ? Bbs757EfbHook.StartBoarding : Bbs757EfbHook.None;

        if (Same(page, AcarsPage))
            return Says("FETCH SIMBRIEF OFP") || Says("REFRESH SIMBRIEF OFP") ? Bbs757EfbHook.Fetch : Bbs757EfbHook.None;

        if (Same(page, FuelPage))
            return Says("IMPORT FROM SIMBRIEF") ? Bbs757EfbHook.Import : Bbs757EfbHook.None;

        if (Same(page, AiracPage))
        {
            if (Says("UPDATE")) return Bbs757EfbHook.NavDataUpdate;
            if (Says("REMOVE")) return Bbs757EfbHook.NavDataRemove;
        }
        return Bbs757EfbHook.None;
    }

    /// <summary>
    /// The parts of the load BlueBird starts on a hook. A fetch starts the fueling and the boarding both only with BlueBird's
    /// automatic boarding on (<paramref name="automaticLoading"/>, its <c>automatic_loading</c> setting; an unread setting is not
    /// known to be on), and nothing otherwise; IMPORT FROM SIMBRIEF starts the fueling and START BOARDING the boarding. The nav data
    /// hooks start no load.
    /// </summary>
    public static Bbs757LoadParts PartsFor(Bbs757EfbHook hook, bool? automaticLoading) => hook switch
    {
        Bbs757EfbHook.Fetch => automaticLoading == true ? Bbs757LoadParts.Both : Bbs757LoadParts.None,
        Bbs757EfbHook.Import => Bbs757LoadParts.Fuel,
        Bbs757EfbHook.StartBoarding => Bbs757LoadParts.Boarding,
        _ => Bbs757LoadParts.None,
    };

    /// <summary>
    /// Whether the app adds its Navigraph sign-in button to the page: on the AIRAC page, while the sign-in is known to be missing
    /// (<paramref name="signedIn"/> false). Signed in, or not read, it is not offered.
    /// </summary>
    public static bool OffersSignIn(string pageName, bool? signedIn) =>
        signedIn == false && Same(Squeeze(pageName), AiracPage);

    /// <summary>
    /// What the nav data watch says once <c>NavigationData\cycle.json</c> has been watched after UPDATE or REMOVE was pressed:
    /// <paramref name="before"/> is the cycle installed before the press, <paramref name="after"/> the cycle the file holds now (null
    /// when it holds none or cannot be read) and <paramref name="fileThere"/> whether the file exists now. A newer cycle than before
    /// is an update. Otherwise a cycle that was installed and whose file has gone was removed: with REMOVE pressed that is what was
    /// asked for, with UPDATE pressed the spot was BlueBird's REMOVE after all (a lapsed subscription, a cycle it knew nothing newer
    /// than). Anything else changed nothing. Empty for a hook that is no nav data press: there is nothing to say.
    /// </summary>
    public static string NavDataOutcome(Bbs757EfbHook hook, int? before, int? after, bool fileThere)
    {
        bool removed = before != null && !fileThere;
        switch (hook)
        {
            case Bbs757EfbHook.NavDataUpdate:
                if (after is int cycle && (before is null || cycle > before))
                    return $"Nav data cycle {cycle.ToString(CultureInfo.InvariantCulture)} installed. Reload the flight to use it.";
                return removed ? NavDataRemovedInsteadText : NavDataUnfinishedText;
            case Bbs757EfbHook.NavDataRemove:
                return removed ? NavDataRemovedText : NavDataNotRemovedText;
            default:
                return "";
        }
    }

    /// <summary>How long the nav data watch reads BlueBird's cycle file after UPDATE or REMOVE (the classic window's 20 seconds),
    /// and how often.</summary>
    public const int NavDataWatchMs = 20_000, NavDataPollMs = 1000;

    /// <summary>
    /// The nav data watch, over delegates (MainForm reads <c>NavigationData\cycle.json</c> through them): from <paramref name="before"/>,
    /// the cycle installed when the hook came, the file is read every <see cref="NavDataPollMs"/> for <see cref="NavDataWatchMs"/>
    /// (the classic window's loop), and what came of it is returned (<see cref="NavDataOutcome"/>). UPDATE ends as soon as the file
    /// holds a newer cycle, and is otherwise judged only once the whole window has passed, since BlueBird may rewrite the file during
    /// an update; REMOVE ends as soon as the file is gone. Empty, at once, for a hook that is no nav data press.
    /// </summary>
    public static async Task<string> WatchNavDataAsync(Bbs757EfbHook hook, int? before, Func<int?> readCycle, Func<bool> fileThere,
        Func<int, CancellationToken, Task> delay, Func<long> nowMs, CancellationToken ct)
    {
        if (hook is not (Bbs757EfbHook.NavDataUpdate or Bbs757EfbHook.NavDataRemove)) return "";
        long until = nowMs() + NavDataWatchMs;
        int? after;
        bool there;
        while (true)
        {
            await delay(NavDataPollMs, ct);
            after = readCycle();
            there = fileThere();
            if (hook == Bbs757EfbHook.NavDataUpdate && after is int cycle && (before is null || cycle > before)) break;
            if (hook == Bbs757EfbHook.NavDataRemove && !there) break;
            if (nowMs() >= until) break;
        }
        return NavDataOutcome(hook, before, after, there);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The words with runs of white space as one space and none at the ends.</summary>
    private static string Squeeze(string text) =>
        string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
