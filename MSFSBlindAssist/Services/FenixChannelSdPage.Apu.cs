namespace MSFSBlindAssist.Services;

// The APU page (1) of the Fenix SD: the APU's generator, its bleed and its memos as the SD draws them, top to bottom. Written
// as the ELEC page is (FenixChannelSdPage.Elec.cs): one reader, every field read before any row is built, and the reader's
// problem returned as the page's one row. Every member added here is named for the APU page. The seam below calls
// ApuRows.
internal static partial class FenixChannelSdPages
{
    /// <summary>
    /// The APU page's rows, in the SD's order, every number whole: the generator (<c>APU GEN: 38 %, 115 V, 400 HZ</c> while
    /// the APU master is on and the GEN pushbutton is not off, <c>APU GEN: OFF</c> while the pushbutton is off, else
    /// <c>APU GEN</c> alone, the very row the ELEC page writes), <c>AVAIL</c> while the APU is available, the bleed
    /// (<c>BLEED: 35 PSI</c>, and <c>BLEED valve: open</c>), <c>N: 100 %</c>, <c>EGT: 412 °C</c>, then the memos
    /// <c>FLAP OPEN</c>, <c>FUEL LO PR</c> and <c>LOW OIL LEVEL</c>, each only while its flag is set. <c>APU title, amber</c> comes first while the Fenix draws the title amber (24.13);
    /// the other rows carry no color because the Fenix sends none for them.
    /// </summary>
    /// <remarks>
    /// <para>The APU (24) and the pneumatic state's valves (151.1) and pressures (151.2) must be in the push: one left out
    /// is "no field 24" (or 151, 151.1, 151.2) and a field of another wire shape is that field's wrong shape, both as one
    /// row with no values; the first problem wins. The page returns that problem and never reports it: the window reports it
    /// once. The APU bleed valve is a valve state with Open left out on the wire, so a push without it reads open; a
    /// value that is no state reads "not available" and is reported, without stopping the page.</para>
    /// <para>The bleed pressure (151.2.3) is NaN with the APU off and reads "XX". 24.14 (the GEN pushbutton off) is read only
    /// while 24.12 (the master) is set, and the generator's three figures only while it is not off, as the ELEC page does.</para>
    /// </remarks>
    internal static FenixChannelRows ApuRows(FenixChannelSdPageContext ctx)
    {
        FenixChannelSdReader remote = FenixChannelSdReader.Of(ctx.Push, ctx.ReportProblem);

        FenixChannelSdReader apu = remote.Message(FenixChannelFields.Remote.ApuData);
        bool master = apu.Bool(FenixChannelFields.ApuData.MasterPower);
        bool off = false;
        double load = 0;
        double volts = 0;
        double frequency = 0;
        if (master)
        {
            off = apu.Bool(FenixChannelFields.ApuData.Off);
            if (!off)
            {
                load = apu.Double(FenixChannelFields.ApuData.Load);
                volts = apu.Double(FenixChannelFields.ApuData.Volts);
                frequency = apu.Double(FenixChannelFields.ApuData.Frequency);
            }
        }

        bool amberTitle = apu.Bool(FenixChannelFields.ApuData.AmberTitle);
        bool avail = apu.Bool(FenixChannelFields.ApuData.Avail);
        double n1 = apu.Double(FenixChannelFields.ApuData.N1);
        double egt = apu.Double(FenixChannelFields.ApuData.Egt);
        bool flapOpen = apu.Bool(FenixChannelFields.ApuData.FlapOpen);
        bool fuelLowPressure = apu.Bool(FenixChannelFields.ApuData.FuelLowPressure);
        bool lowOil = apu.Bool(FenixChannelFields.ApuData.LowOil);

        FenixChannelSdReader pneumatic = remote.Message(FenixChannelFields.Remote.PneumaticState);
        string bleedValve = ctx.ApuBleedWord(pneumatic.Message(FenixChannelFields.PneumaticState.Valves)
            .Valve(FenixChannelFields.PneumaticValveData.ApuBleed));
        double bleedPressure = pneumatic.Message(FenixChannelFields.PneumaticState.BleedPressure)
            .Double(FenixChannelFields.BleedPressureData.Apu);

        // Every read after a problem gives its default, which must never be spoken.
        if (remote.Problem is { } problem) return FenixChannelRows.NotAvailable(problem);

        // The title is drawn amber (24.13) from APU master on until AVAIL in a start, and from the end of the cooldown until
        // the APU stops (measured 2026-11-03); it is the page's first row, before the generator.
        var rows = new List<string>();
        if (amberTitle) rows.Add("APU title, amber");
        rows.Add(ElecApuRow(master, off, load, volts, frequency));
        if (avail) rows.Add("AVAIL");

        rows.Add($"BLEED: {FenixChannelSd.Whole(bleedPressure)} PSI");
        rows.Add($"BLEED valve: {bleedValve}");
        rows.Add($"N: {FenixChannelSd.Whole(n1)} %");
        rows.Add($"EGT: {FenixChannelSd.Whole(egt)} °C");
        if (flapOpen) rows.Add("FLAP OPEN");
        if (fuelLowPressure) rows.Add("FUEL LO PR");
        if (lowOil) rows.Add("LOW OIL LEVEL");
        return FenixChannelRows.Of(rows);
    }

    /// <summary>The APU page (1).</summary>
    static partial void ComposeApu(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows)
        => rows = ApuRows(ctx);
}
