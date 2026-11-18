namespace MSFSBlindAssist.Services;

// The CRUISE page (5) of the Fenix SD: the engines' fuel used, oil and vibration, then the cabin, as the SD draws them, top
// to bottom. Written as the PRESS page is (FenixChannelSdPage.Press.cs), whose LDG ELEV and ΔP row builders it calls. Every
// member added here is named for the CRUISE page. Inert: only the tests call CruiseRows until a later task wires the
// ComposeCruise seam, which calls CruiseRows.
internal static partial class FenixChannelSdPages
{
    /// <summary>
    /// The CRUISE page's rows, in the SD's order, every pair engine 1 then engine 2: <c>F.USED &lt;unit&gt;</c>,
    /// <c>F.USED 1+2: n &lt;unit&gt;</c>, <c>OIL QT</c> and <c>VIB N1</c> and <c>VIB N2</c> (one decimal), then the PRESS
    /// page's <c>LDG ELEV</c> and <c>ΔP</c> rows, <c>CAB V/S: n FT/MIN</c>, <c>CAB ALT: n FT</c>, and the cabin
    /// temperatures <c>CKPT</c>, <c>FWD</c> and <c>AFT</c> in °C. The cabin rows speak the colors and pulses the Fenix sends, as on the PRESS page (151.9).
    /// </summary>
    /// <remarks>
    /// <para>Needs the engines (129) with both engines (129.1, 129.2), the fuel used (134.13), the cabin zones (151.7), the
    /// flags (151.9) and the pressurization data (151.10): one left out is "no field &lt;path&gt;", never its defaults; the
    /// first problem wins, and the page returns it and never reports it. The weight unit (154) follows the label of a pair
    /// and the value of the total; a field left out is pounds.</para>
    /// <para>The landing elevation (151.10.7) was measured in flight on 2026-11-03: 550 approaching CYYZ
    /// (elevation 569 ft published, 546.8 ft in the sim), the landing elevation at the SD's 50 ft steps.</para>
    /// </remarks>
    internal static FenixChannelRows CruiseRows(FenixChannelSdPageContext ctx)
    {
        FenixChannelSdReader remote = FenixChannelSdReader.Of(ctx.Push, ctx.ReportProblem);
        string unit = FenixChannelSd.WeightUnit(remote);

        FenixChannelSdReader engines = remote.Message(FenixChannelFields.Remote.EngineState);
        FenixChannelSdReader left = engines.Message(FenixChannelFields.EngineControlState.Left);
        double oil1 = left.Double(FenixChannelFields.SingleEngineState.OilQuantity);
        bool oilPulse1 = left.Bool(FenixChannelFields.SingleEngineState.OilQtyPulse);
        double vibN1a = left.Double(FenixChannelFields.SingleEngineState.VibrationN1);
        double vibN2a = left.Double(FenixChannelFields.SingleEngineState.VibrationN2);
        FenixChannelSdReader right = engines.Message(FenixChannelFields.EngineControlState.Right);
        double oil2 = right.Double(FenixChannelFields.SingleEngineState.OilQuantity);
        bool oilPulse2 = right.Bool(FenixChannelFields.SingleEngineState.OilQtyPulse);
        double vibN1b = right.Double(FenixChannelFields.SingleEngineState.VibrationN1);
        double vibN2b = right.Double(FenixChannelFields.SingleEngineState.VibrationN2);

        FenixChannelSdReader used = remote.Message(FenixChannelFields.Remote.FuelState)
            .Message(FenixChannelFields.FuelState.Used);
        double used1 = used.Double(FenixChannelFields.FuelUsedData.Engine1);
        double used2 = used.Double(FenixChannelFields.FuelUsedData.Engine2);
        double usedTotal = used.Double(FenixChannelFields.FuelUsedData.Total);

        FenixChannelSdReader pneumatic = remote.Message(FenixChannelFields.Remote.PneumaticState);
        FenixChannelSdReader zone = pneumatic.Message(FenixChannelFields.PneumaticState.Zone);
        double cockpit = zone.Double(FenixChannelFields.ZoneTemperatureData.CockpitTemp);
        double forward = zone.Double(FenixChannelFields.ZoneTemperatureData.FwdCabinTemp);
        double aft = zone.Double(FenixChannelFields.ZoneTemperatureData.AftCabinTemp);
        FenixChannelSdReader flags = pneumatic.Message(FenixChannelFields.PneumaticState.PressFlags);
        bool manualElev = flags.Bool(FenixChannelFields.PressurizationFlagsData.ManualLandingElev);
        PressCabinFlags cabin = PressReadCabinFlags(flags);

        FenixChannelSdReader press = pneumatic.Message(FenixChannelFields.PneumaticState.PressData);
        double deltaP = press.Double(FenixChannelFields.PressurizationData.DifferentialPressure);
        double vs = press.Double(FenixChannelFields.PressurizationData.CabinVs);
        double altitude = press.Double(FenixChannelFields.PressurizationData.CabinAlt);
        double landing = press.Double(FenixChannelFields.PressurizationData.LandingCabinAlt);

        // Every read after a problem gives its default, which must never be spoken.
        if (remote.Problem is { } problem) return FenixChannelRows.NotAvailable(problem);

        return FenixChannelRows.Of(new[]
        {
            $"F.USED {unit}: {FenixChannelSd.Whole(used1)}, {FenixChannelSd.Whole(used2)}",
            $"F.USED 1+2: {FenixChannelSd.Whole(usedTotal)} {unit}",
            $"OIL QT: {FenixChannelSd.OneDecimal(oil1)}{EngPulsing(oilPulse1)}, {FenixChannelSd.OneDecimal(oil2)}{EngPulsing(oilPulse2)}",
            $"VIB N1: {FenixChannelSd.OneDecimal(vibN1a)}, {FenixChannelSd.OneDecimal(vibN1b)}",
            $"VIB N2: {FenixChannelSd.OneDecimal(vibN2a)}, {FenixChannelSd.OneDecimal(vibN2b)}",
            PressLdgElevRow(manualElev, landing),
            PressDeltaPRow(deltaP, cabin),
            $"CAB V/S: {FenixChannelSd.Whole(vs)} FT/MIN{PressWords(cabin.VsAmber, "amber", cabin.VsPulse)}",
            $"CAB ALT: {FenixChannelSd.Whole(altitude)} FT{PressWords(cabin.AltRed, "red", cabin.AltPulse)}",
            $"CKPT: {FenixChannelSd.Whole(cockpit)} °C",
            $"FWD: {FenixChannelSd.Whole(forward)} °C",
            $"AFT: {FenixChannelSd.Whole(aft)} °C"
        });
    }

    /// <summary>The CRUISE page (5).</summary>
    static partial void ComposeCruise(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows)
        => rows = CruiseRows(ctx);
}
