namespace MSFSBlindAssist.Forms.Fenix;

// The Cabin window (the flight attendant panel) in the "Fenix windows" list.
public static partial class FenixWindowRegistry
{
    static partial void AddCabinWindows(List<FenixWindowEntry> entries)
        => entries.Add(new FenixWindowEntry(
            "Cabin",
            "Flight attendant panel: cabin ready, announcements and boarding music, lights, doors and slides, temperature, water and waste",
            ctx => ctx.Definition.ShowFenixWindow(() => new FenixCabinForm(ctx), f => f.ShowForm())));
}
