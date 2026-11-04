namespace MSFSBlindAssist.Services;

// The PRESS page (3) of the Fenix SD: the landing elevation, the cabin and the pressurization valves as the SD draws them,
// top to bottom. Written as the BLEED page is (FenixChannelSdPage.Bleed.cs): one reader, every field read before any row is
// built, and the reader's problem returned as the page's one row. Every member added here is named for the PRESS page.
// Inert: only the tests call PressRows until a later task wires the ComposePress seam. The CRUISE page
// (FenixChannelSdPage.Cruise.cs) builds its LDG ELEV and ΔP rows with the same two builders below, so they cannot drift.
internal static partial class FenixChannelSdPages
{
    /// <summary>
    /// The PRESS page's rows, in the SD's order: <c>LDG ELEV AUTO: n FT</c> (MAN while the landing elevation is set by hand),
    /// <c>ΔP: n.n PSI</c>, <c>V/S: n FT/MIN</c>, <c>CAB ALT: n FT</c>, then <c>SYS 1</c> only while system 1 is active,
    /// <c>SYS 2</c> only while system 2 is, <c>MAN</c> only in manual mode, then <c>VENT INLET</c>, <c>VENT EXTRACT</c>,
    /// <c>OUTFLOW: n %</c>, <c>SAFETY</c>, <c>PACK 1</c> and <c>PACK 2</c>. No row speaks a color.
    /// </summary>
    /// <remarks>
    /// <para>The pneumatic state (151) must be in the push with its valves (151.1), flags (151.9) and pressurization data
    /// (151.10): one left out is "no field 151.x", never its defaults; the first problem wins, and the page returns it and
    /// never reports it. A valve value that is no state reads "not available" and is reported, without stopping the page.</para>
    /// <para>The packs are drawn by their flow valves (151.1.9, .10). The outflow valve (151.10.6) is a fraction open, spoken
    /// as a percent (owner question Q2). The landing elevation (151.10.7) is inferred, Task 1 measures it.</para>
    /// </remarks>
    internal static FenixChannelRows PressRows(FenixChannelSdPageContext ctx)
    {
        FenixChannelSdReader remote = FenixChannelSdReader.Of(ctx.Push, ctx.ReportProblem);
        FenixChannelSdReader pneumatic = remote.Message(FenixChannelFields.Remote.PneumaticState);

        FenixChannelSdReader valves = pneumatic.Message(FenixChannelFields.PneumaticState.Valves);
        string pack1 = valves.Valve(FenixChannelFields.PneumaticValveData.Pack1Flow);
        string pack2 = valves.Valve(FenixChannelFields.PneumaticValveData.Pack2Flow);

        FenixChannelSdReader flags = pneumatic.Message(FenixChannelFields.PneumaticState.PressFlags);
        bool manual = flags.Bool(FenixChannelFields.PressurizationFlagsData.ManualMode);
        bool sys1 = flags.Bool(FenixChannelFields.PressurizationFlagsData.Sys1Active);
        bool sys2 = flags.Bool(FenixChannelFields.PressurizationFlagsData.Sys2Active);
        bool manualElev = flags.Bool(FenixChannelFields.PressurizationFlagsData.ManualLandingElev);

        FenixChannelSdReader press = pneumatic.Message(FenixChannelFields.PneumaticState.PressData);
        double deltaP = press.Double(FenixChannelFields.PressurizationData.DifferentialPressure);
        double vs = press.Double(FenixChannelFields.PressurizationData.CabinVs);
        double altitude = press.Double(FenixChannelFields.PressurizationData.CabinAlt);
        string inlet = press.Valve(FenixChannelFields.PressurizationData.InletValve);
        string extract = press.Valve(FenixChannelFields.PressurizationData.ExtractValve);
        double outflow = press.Double(FenixChannelFields.PressurizationData.OutflowValve);
        double landing = press.Double(FenixChannelFields.PressurizationData.LandingCabinAlt);
        string safety = press.Valve(FenixChannelFields.PressurizationData.SafetyValve);

        // Every read after a problem gives its default, which must never be spoken.
        if (remote.Problem is { } problem) return FenixChannelRows.NotAvailable(problem);

        var rows = new List<string>
        {
            PressLdgElevRow(manualElev, landing),
            PressDeltaPRow(deltaP),
            $"V/S: {FenixChannelSd.Whole(vs)} FT/MIN",
            $"CAB ALT: {FenixChannelSd.Whole(altitude)} FT"
        };
        if (sys1) rows.Add("SYS 1");
        if (sys2) rows.Add("SYS 2");
        if (manual) rows.Add("MAN");
        rows.Add($"VENT INLET: {inlet}");
        rows.Add($"VENT EXTRACT: {extract}");
        // Inferred, Task 1 measures it: the outflow valve is a fraction open (0 to 1), spoken as a percent.
        rows.Add($"OUTFLOW: {FenixChannelSd.Whole(outflow * 100)} %");
        rows.Add($"SAFETY: {safety}");
        rows.Add($"PACK 1: {pack1}");
        rows.Add($"PACK 2: {pack2}");
        return FenixChannelRows.Of(rows);
    }

    // The LDG ELEV row, shared by PRESS and CRUISE: "AUTO" until the landing elevation is set by hand, then "MAN".
    // The elevation is 151.10.7, inferred, Task 1 measures it.
    private static string PressLdgElevRow(bool manualElevation, double feet)
        => $"LDG ELEV {(manualElevation ? "MAN" : "AUTO")}: {FenixChannelSd.Whole(feet)} FT";

    // The ΔP row, shared by PRESS and CRUISE: to one decimal, as the SD draws it.
    private static string PressDeltaPRow(double psi) => $"ΔP: {FenixChannelSd.OneDecimal(psi)} PSI";
}
