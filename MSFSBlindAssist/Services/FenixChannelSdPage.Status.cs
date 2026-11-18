namespace MSFSBlindAssist.Services;

// The STATUS page (14) of the Fenix SD: the text the SD draws in its two columns. The first SD page composer, and the
// idiom the others copy: one reader for the page (FenixChannelSdReader.Of), every field read before any row is built, the
// reader's problem returned as the page's one row, and a content condition ("no STATUS text") the same way. A page
// composer never reports a problem it returns: FenixChannelSd.Compose reports it once, under the page's name.
internal static partial class FenixChannelSdPages
{
    /// <summary>
    /// The STATUS page's rows: each line of the left column (175) that <see cref="FenixChannelSpeech.Line(string?)"/>
    /// speaks, in order, then each of the right column (176). A blank line is skipped. Channel text keeps its colors
    /// ("NORMAL, green") and is spoken nowhere else ([FNX-24]). A page with no line to speak is "no STATUS text", and a
    /// column of another shape is that field's wrong shape, which stops this page only.
    /// </summary>
    /// <remarks>
    /// A format letter the app does not know in either column never gets here while the STATUS page is shown: the
    /// window refuses the whole SD first (<see cref="FenixChannelSdFrame.UnknownCode"/>), so a line the speech gives
    /// nothing for is a blank one.
    /// </remarks>
    internal static FenixChannelRows StatusRows(FenixChannelSdPageContext ctx)
    {
        FenixChannelSdReader reader = FenixChannelSdReader.Of(ctx.Push, ctx.ReportProblem);
        IReadOnlyList<string> left = reader.Strings(FenixChannelFields.Remote.StatusLeft);
        IReadOnlyList<string> right = reader.Strings(FenixChannelFields.Remote.StatusRight);

        // Every read after a problem gives its default, which must never be spoken.
        if (reader.Problem is { } problem) return FenixChannelRows.NotAvailable(problem);

        var rows = new List<string>();
        foreach (string coded in left.Concat(right))
        {
            if (FenixChannelSpeech.Line(coded) is { } spoken && !string.IsNullOrWhiteSpace(spoken))
            {
                rows.Add(spoken);
            }
        }

        return rows.Count == 0 ? FenixChannelRows.NotAvailable("no STATUS text") : FenixChannelRows.Of(rows);
    }

    /// <summary>The STATUS page (14).</summary>
    static partial void ComposeStatus(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows)
        => rows = StatusRows(ctx);
}
