using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>
/// What a page of the Fenix SD is composed from: the RemoteData message, the frame decoded from it, the loaded Fenix,
/// and where a page reports a problem it found. The message and the frame come from one snapshot ([FNX-22]). The SD
/// window builds one at each refresh, and <see cref="FenixChannelSdPages.Compose"/> hands it unchanged to the composer
/// of the page the frame shows.
/// </summary>
/// <param name="Push">
/// The RemoteData message of the snapshot (<see cref="FenixChannelSnapshot.Push"/>), which a page reads its own fields
/// from. A page checks the wire shape of every field it uses and the presence of the content it needs, as every
/// feature's decoder does.
/// </param>
/// <param name="Frame">
/// The frame the trust gate judged, decoded from <paramref name="Push"/> (<see cref="FenixChannelSnapshot.Frame"/>). It
/// names the page shown (<see cref="FenixChannelFrame.FwcPage"/>).
/// </param>
/// <param name="Variant">
/// The loaded Fenix. The airframe is no channel field: the channel leaves its aircraftType out at the default, as it
/// does the engine family, so a layout that depends on the airframe (the A321's FUEL and DOOR pages) follows
/// <c>Variant.Type</c>. The E/WD (plan 04) takes the engine family from it the same way.
/// </param>
/// <param name="ReportProblem">
/// Where a page reports a problem it found while reading, such as a field in another wire shape. The window that
/// composes the page supplies it, and it is meant for the channel's log
/// (<see cref="FenixChannelMonitor.ReportFeatureProblem"/>, which writes each distinct line once).
/// </param>
internal readonly record struct FenixChannelSdPageContext(
    FenixChannelPush Push, FenixChannelFrame Frame, Aircraft.FenixVariant Variant, Action<string> ReportProblem)
{
    /// <summary>The window's hold on the APU bleed valve's word; with none the word reads as sent.</summary>
    public FenixChannelApuBleedHold? ApuBleedHold { get; init; }

    /// <summary>The snapshot's clock, in milliseconds, for <see cref="ApuBleedHold"/>.</summary>
    public long AtMs { get; init; }

    /// <summary>What to say for the APU bleed valve, whose word as sent is <paramref name="word"/>.</summary>
    public string ApuBleedWord(string word) => ApuBleedHold?.Word(word, AtMs) ?? word;
}

/// <summary>
/// The pages of the Fenix SD as the display channel numbers them, and what every page shares: each page's name, the one
/// word for a valve state, and the dispatch from a page number to the composer that gives the page its rows. The SD
/// window starts its rows with <see cref="NameOf"/> of the frame's page, adds what <see cref="Compose"/> gives for it,
/// and ends them with the bottom line.
/// </summary>
/// <remarks>
/// <para>One seam for each page. A page's composer is a <c>partial void</c> declared below, each implemented in a partial
/// file of its own (<c>FenixChannelSdPage.Apu.cs</c> and the others).</para>
/// <para>The pages' field numbers are not here either. Those only one group of pages reads are in a partial of
/// <see cref="FenixChannelFields"/> (<c>FenixChannelFields.Sd.cs</c>, <c>FenixChannelFields.SdPages.cs</c>), and the
/// ones several pages read are declared once, in FenixChannelFields.cs.</para>
/// <para>Nothing here looks at trust. The window asks for a page only from a snapshot whose display is trusted
/// ([FNX-22]; <c>FenixChannelDisplayForm.RowsFor</c> checks the verdict first). A composer only reads the context it is
/// given and does no I/O, so a refresh stays short on the UI thread.</para>
/// </remarks>
internal static partial class FenixChannelSdPages
{
    // The pages by the number the frame's FwcPage gives them. NameOf and Compose both read these, so a page is never
    // named one thing and composed as another. Pages 0 and 13 are not here: nobody knows what they draw.
    private const int PageApu = 1;
    private const int PageBleed = 2;
    private const int PagePress = 3;
    private const int PageCond = 4;
    private const int PageCruise = 5;
    private const int PageDoor = 6;
    private const int PageElec = 7;
    private const int PageEng = 8;
    private const int PageFctl = 9;
    private const int PageFuel = 10;
    private const int PageHyd = 11;
    private const int PageWheel = 12;
    private const int PageStatus = 14;

    /// <summary>
    /// The name an SD page is shown under, as the SD draws it: APU, BLEED, PRESS, COND, CRUISE, DOOR, ELEC, ENG, F/CTL,
    /// FUEL, HYD, WHEEL or STATUS. Any other number is "page &lt;n&gt;", written in the invariant culture ("page 42",
    /// and "page -1" with an ASCII minus whatever minus sign the PC's culture has), so that a page nobody has named
    /// still has a name to read.
    /// </summary>
    /// <remarks>
    /// Pages 0 and 13 are among the others because nobody knows what they draw. Page 5 is CRUISE, confirmed in cruise
    /// on 3.0.0.7613 (2026-11-03).
    /// </remarks>
    public static string NameOf(int page) => page switch
    {
        PageApu => "APU",
        PageBleed => "BLEED",
        PagePress => "PRESS",
        PageCond => "COND",
        PageCruise => "CRUISE",
        PageDoor => "DOOR",
        PageElec => "ELEC",
        PageEng => "ENG",
        PageFctl => "F/CTL",
        PageFuel => "FUEL",
        PageHyd => "HYD",
        PageWheel => "WHEEL",
        PageStatus => "STATUS",
        _ => string.Create(CultureInfo.InvariantCulture, $"page {page}")
    };

    /// <summary>
    /// The one word for a valve state, so that every page that shows a valve says its state the same way: "open",
    /// "closed", "in transit", "failed open" or "failed closed". DataInvalid, which the SD draws as crosses, is "XX". A
    /// number that is no state of <see cref="FenixChannelFields.ValveStateEnum"/> is "not available". The words are
    /// fixed text, so nothing here follows the PC's culture.
    /// </summary>
    /// <remarks>
    /// A valve with no state field is Open, which the Fenix leaves out on the wire. Passing 0 for an absent field is
    /// the page's part, not this method's.
    /// </remarks>
    public static string ValveWord(int state) => state switch
    {
        FenixChannelFields.ValveStateEnum.Open => "open",
        FenixChannelFields.ValveStateEnum.Closed => "closed",
        FenixChannelFields.ValveStateEnum.Transit => "in transit",
        FenixChannelFields.ValveStateEnum.FailedOpen => "failed open",
        FenixChannelFields.ValveStateEnum.FailedClose => "failed closed",
        FenixChannelFields.ValveStateEnum.DataInvalid => "XX",
        _ => "not available"
    };

    /// <summary>
    /// What the composer of <paramref name="page"/> made of <paramref name="ctx"/>: its rows, or not available with the
    /// page's problem. Null when the page has no composer, because its plan has not landed or because the number is no
    /// page the dispatch knows, and the SD window then shows the page's name and the bottom line alone.
    /// </summary>
    /// <remarks>
    /// <para>The page's one seam is called with no rows set, and what it sets is what comes back. A seam with no
    /// implementation is dropped by C#, so it sets nothing. Nothing in <paramref name="ctx"/> is read here.</para>
    /// <para>Nothing is caught. A composer says what it expects as a problem in the rows it returns, and one that
    /// throws reaches the window's <c>RowsFor</c>, which reports it and shows the window's one status row.</para>
    /// </remarks>
    public static FenixChannelRows? Compose(int page, FenixChannelSdPageContext ctx)
    {
        FenixChannelRows? rows = null;
        switch (page)
        {
            case PageApu:
                ComposeApu(ctx, ref rows);
                break;
            case PageBleed:
                ComposeBleed(ctx, ref rows);
                break;
            case PagePress:
                ComposePress(ctx, ref rows);
                break;
            case PageCond:
                ComposeCond(ctx, ref rows);
                break;
            case PageCruise:
                ComposeCruise(ctx, ref rows);
                break;
            case PageDoor:
                ComposeDoor(ctx, ref rows);
                break;
            case PageElec:
                ComposeElec(ctx, ref rows);
                break;
            case PageEng:
                ComposeEng(ctx, ref rows);
                break;
            case PageFctl:
                ComposeFctl(ctx, ref rows);
                break;
            case PageFuel:
                ComposeFuel(ctx, ref rows);
                break;
            case PageHyd:
                ComposeHyd(ctx, ref rows);
                break;
            case PageWheel:
                ComposeWheel(ctx, ref rows);
                break;
            case PageStatus:
                ComposeStatus(ctx, ref rows);
                break;
            default:
                // No page of the SD has this number, so there is no composer to ask: no rows.
                break;
        }

        return rows;
    }

    // The page seams, one for each page, called by Compose and nowhere else. An implementation sets rows to the page's
    // rows, or to FenixChannelRows.NotAvailable with the page's problem, and leaves it null to show no content, so the
    // page shows as its name and the bottom line alone.

    /// <summary>The APU page (1).</summary>
    static partial void ComposeApu(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows);

    /// <summary>The BLEED page (2).</summary>
    static partial void ComposeBleed(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows);

    /// <summary>The PRESS page (3).</summary>
    static partial void ComposePress(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows);

    /// <summary>The COND page (4).</summary>
    static partial void ComposeCond(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows);

    /// <summary>The CRUISE page (5).</summary>
    static partial void ComposeCruise(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows);

    /// <summary>The DOOR page (6).</summary>
    static partial void ComposeDoor(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows);

    /// <summary>The WHEEL page (12).</summary>
    static partial void ComposeWheel(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows);

    /// <summary>The F/CTL page (9).</summary>
    static partial void ComposeFctl(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows);

    /// <summary>The ENG page (8).</summary>
    static partial void ComposeEng(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows);

    /// <summary>The ELEC page (7).</summary>
    static partial void ComposeElec(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows);

    /// <summary>The HYD page (11).</summary>
    static partial void ComposeHyd(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows);

    /// <summary>The FUEL page (10).</summary>
    static partial void ComposeFuel(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows);

    /// <summary>The STATUS page (14).</summary>
    static partial void ComposeStatus(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows);
}
