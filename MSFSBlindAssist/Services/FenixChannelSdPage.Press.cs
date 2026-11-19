namespace MSFSBlindAssist.Services;

// The PRESS page (3) of the Fenix SD: the landing elevation, the cabin and the pressurization valves as the SD draws them,
// top to bottom. Written as the BLEED page is (FenixChannelSdPage.Bleed.cs): one reader, every field read before any row is
// built, and the reader's problem returned as the page's one row. Every member added here is named for the PRESS page.
// The ComposePress seam calls PressRows. The CRUISE page
// (FenixChannelSdPage.Cruise.cs) builds its LDG ELEV and ΔP rows with the same two builders below, so they cannot drift.
internal static partial class FenixChannelSdPages
{
    /// <summary>
    /// The PRESS page's rows, in the SD's order: <c>LDG ELEV AUTO: n FT</c> (MAN while the landing elevation is set by hand),
    /// <c>ΔP: n.n PSI</c>, <c>V/S: n FT/MIN</c>, <c>CAB ALT: n FT</c>, then <c>SYS 1</c> only while system 1 is active
    /// or drawn amber (151.9.19), <c>SYS 2</c> only while system 2 is active or drawn amber (151.9.20), <c>MAN</c> only in manual mode, then <c>VENT INLET</c>, <c>VENT EXTRACT</c>,
    /// <c>OUTFLOW: n %</c>, <c>SAFETY</c>, <c>PACK 1</c> and <c>PACK 2</c>. The page speaks the colors the Fenix sends: a
    /// value drawn amber or red gains ", amber" or ", red" and one the SD makes pulse ", pulsing" (151.9); <c>VENT, amber</c>
    /// (a row of its own), <c>BLOWER: fault</c> and <c>EXTRACT: fault</c> show only while set.
    /// </summary>
    /// <remarks>
    /// <para>The pneumatic state (151) must be in the push with its valves (151.1), flags (151.9) and pressurization data
    /// (151.10): one left out is "no field 151.x", never its defaults; the first problem wins, and the page returns it and
    /// never reports it. A valve value that is no state reads "not available" and is reported, without stopping the page.</para>
    /// <para>The packs are drawn by their flow valves (151.1.9, .10). The outflow valve (151.10.6) is a fraction open, spoken
    /// as a percent (owner question Q2). The landing elevation (151.10.7) was measured in flight on 2026-11-03: 550 approaching CYYZ
    /// (elevation 569 ft published, 546.8 ft in the sim), the landing elevation at the SD's 50 ft steps.</para>
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
        bool sys1Amber = flags.Bool(FenixChannelFields.PressurizationFlagsData.Sys1Amber);
        bool sys2Amber = flags.Bool(FenixChannelFields.PressurizationFlagsData.Sys2Amber);
        bool ventAmber = flags.Bool(FenixChannelFields.PressurizationFlagsData.VentAmber);
        bool inletAmber = flags.Bool(FenixChannelFields.PressurizationFlagsData.InletAmber);
        bool extractAmber = flags.Bool(FenixChannelFields.PressurizationFlagsData.ExtractAmber);
        bool blowerFault = flags.Bool(FenixChannelFields.PressurizationFlagsData.BlowerFault);
        bool extractFault = flags.Bool(FenixChannelFields.PressurizationFlagsData.ExtractFault);
        bool outflowAmber = flags.Bool(FenixChannelFields.PressurizationFlagsData.OutflowValveAmber);
        bool pack1Amber = flags.Bool(FenixChannelFields.PressurizationFlagsData.Pack1Amber);
        bool pack2Amber = flags.Bool(FenixChannelFields.PressurizationFlagsData.Pack2Amber);
        PressCabinFlags cabin = PressReadCabinFlags(flags);

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
            PressDeltaPRow(deltaP, cabin),
            $"V/S: {FenixChannelSd.Whole(vs)} FT/MIN{PressWords(cabin.VsAmber, "amber", cabin.VsPulse)}",
            $"CAB ALT: {FenixChannelSd.Whole(altitude)} FT{PressWords(cabin.AltRed, "red", cabin.AltPulse)}"
        };
        if (sys1 || sys1Amber) rows.Add("SYS 1" + PressWords(sys1Amber, "amber", false));
        if (sys2 || sys2Amber) rows.Add("SYS 2" + PressWords(sys2Amber, "amber", false));
        if (manual) rows.Add("MAN");
        if (ventAmber) rows.Add("VENT, amber");
        rows.Add($"VENT INLET: {inlet}{PressWords(inletAmber, "amber", false)}");
        rows.Add($"VENT EXTRACT: {extract}{PressWords(extractAmber, "amber", false)}");
        if (blowerFault) rows.Add("BLOWER: fault");
        if (extractFault) rows.Add("EXTRACT: fault");
        // The outflow valve is a fraction open (0 to 1), spoken as a percent. Measured 2026-11-03: 1.0 on the ground (fully
        // open), 0.5 with RAM AIR on, 0.0842 in cruise.
        rows.Add($"OUTFLOW: {FenixChannelSd.Whole(outflow * 100)} %{PressWords(outflowAmber, "amber", false)}");
        rows.Add($"SAFETY: {safety}");
        rows.Add($"PACK 1: {pack1}{PressWords(pack1Amber, "amber", false)}");
        rows.Add($"PACK 2: {pack2}{PressWords(pack2Amber, "amber", false)}");
        return FenixChannelRows.Of(rows);
    }

    /// <summary>The PRESS page (3).</summary>
    static partial void ComposePress(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows)
        => rows = PressRows(ctx);

    // The LDG ELEV row, shared by PRESS and CRUISE: "AUTO" until the landing elevation is set by hand, then "MAN".
    // The elevation is 151.10.7, measured in flight on 2026-11-03 (550 approaching CYYZ: 569 ft published, 546.8 ft in the sim).
    private static string PressLdgElevRow(bool manualElevation, double feet)
        => $"LDG ELEV {(manualElevation ? "MAN" : "AUTO")}: {FenixChannelSd.Whole(feet)} FT";

    // The ΔP row, shared by PRESS and CRUISE: to one decimal, as the SD draws it, with its color and pulse.
    private static string PressDeltaPRow(double psi, PressCabinFlags cabin)
        => $"ΔP: {FenixChannelSd.OneDecimal(psi)} PSI{PressWords(cabin.DeltaPAmber, "amber", cabin.DeltaPPulse)}";

    // The six color and pulse flags of the three cabin values (151.9), read by PRESS and CRUISE alike.
    private readonly record struct PressCabinFlags(
        bool DeltaPAmber, bool DeltaPPulse, bool VsAmber, bool VsPulse, bool AltRed, bool AltPulse);

    private static PressCabinFlags PressReadCabinFlags(FenixChannelSdReader flags)
        => new(
            flags.Bool(FenixChannelFields.PressurizationFlagsData.DeltaPAmber),
            flags.Bool(FenixChannelFields.PressurizationFlagsData.DeltaPPulse),
            flags.Bool(FenixChannelFields.PressurizationFlagsData.VsAmber),
            flags.Bool(FenixChannelFields.PressurizationFlagsData.VsPulse),
            flags.Bool(FenixChannelFields.PressurizationFlagsData.CabinAltRed),
            flags.Bool(FenixChannelFields.PressurizationFlagsData.CabinAltPulse));

    // ", amber", ", pulsing" or ", amber, pulsing" after a row's text: the color the Fenix sends, then the SD's flash.
    private static string PressWords(bool colored, string color, bool pulsing)
        => (colored ? $", {color}" : "") + (pulsing ? ", pulsing" : "");
}
