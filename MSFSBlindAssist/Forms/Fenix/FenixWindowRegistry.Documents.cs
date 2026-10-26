namespace MSFSBlindAssist.Forms.Fenix;

// The Fenix documents window in the "Fenix windows" list.
public static partial class FenixWindowRegistry
{
    static partial void AddDocumentWindows(List<FenixWindowEntry> entries)
        => entries.Add(new FenixWindowEntry(
            "Fenix documents",
            "ECAM procedures, QRH and the Fenix user guide: browse, search and read; Control+P opens the pinned ECAM procedure",
            ctx => ctx.Definition.ShowFenixWindow(() => new FenixDocumentsForm(ctx.Data, ctx.Announcer), f => f.ShowForm())));
}
