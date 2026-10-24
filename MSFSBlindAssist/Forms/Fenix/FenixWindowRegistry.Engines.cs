namespace MSFSBlindAssist.Forms.Fenix;

// The Engines window in the "Fenix windows" list.
public static partial class FenixWindowRegistry
{
    static partial void AddEngineWindows(List<FenixWindowEntry> entries)
        => entries.Add(new FenixWindowEntry(
            "Engines",
            "N1, EGT, N2, fuel flow, oil and air temperatures, from the Fenix's own values",
            ctx => ctx.Definition.ShowFenixWindow(() => new FenixEnginesForm(ctx), f => f.ShowForm())));
}
