namespace MSFSBlindAssist.Forms.Fenix;

// The Fenix options window in the "Fenix windows" list.
public static partial class FenixWindowRegistry
{
    static partial void AddOptionsWindows(List<FenixWindowEntry> entries)
        => entries.Add(new FenixWindowEntry(
            "Fenix options",
            "Every Fenix EFB option with its value; the options that change how MSFS Blind Assist works come first",
            ctx => ctx.Definition.ShowFenixWindow(() => new FenixOptionsForm(ctx.Data, ctx.Announcer), f => f.ShowForm())));
}
