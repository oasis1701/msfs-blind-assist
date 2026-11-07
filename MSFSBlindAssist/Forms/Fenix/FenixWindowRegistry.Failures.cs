namespace MSFSBlindAssist.Forms.Fenix;

// The Failures window in the "Fenix windows" list.
public static partial class FenixWindowRegistry
{
    static partial void AddFailureWindows(List<FenixWindowEntry> entries)
        => entries.Add(new FenixWindowEntry(
            "Failures",
            "Arm, fail or clear Fenix failures by ATA chapter",
            ctx => ctx.Definition.ShowFenixWindow(() => new FenixFailuresForm(ctx.Definition, ctx.Data, ctx.Announcer), f => f.ShowForm())));
}
