namespace MSFSBlindAssist.Services;

// The BLEED page (2) of the Fenix SD: the ram air, the packs, the valves and the engine bleeds as the SD draws them, top to
// bottom. Written as the APU page is (FenixChannelSdPage.Apu.cs): one reader, every field read before any row is built, and
// the reader's problem returned as the page's one row. Every member added here is named for the BLEED page. The seam below calls
// BleedRows.
internal static partial class FenixChannelSdPages
{
    /// <summary>
    /// The BLEED page's rows, in the SD's order, every number whole and every pair engine 1 (or pack 1, or left) then
    /// engine 2: <c>RAM AIR</c>, <c>PACK outlet °C</c>, <c>PACK compressor °C</c>, <c>PACK flow valves</c>,
    /// <c>ANTI ICE valves</c> (only while the wing anti-ice switch is on), <c>X BLEED valve</c>, <c>APU valve</c>,
    /// <c>bleed PSI</c>, <c>bleed °C</c>, <c>bleed valves</c> and <c>HP valves</c>. The page speaks the colors the Fenix sends: a side drawn amber in a two-sided row
    /// (ANTI ICE valves, bleed PSI, bleed °C) gains " amber" right after its value, and <c>start air 1, amber</c> and
    /// <c>start air 2, amber</c> follow the HP valves while set (151.4).
    /// </summary>
    /// <remarks>
    /// <para>The pneumatic state (151) must be in the push with its valves (151.1), bleed pressures (151.2), bleed
    /// temperatures (151.3), flags (151.4) and both packs (151.5, 151.6): one left out is "no field 151.x", never its
    /// defaults, and a field of another wire shape is that field's wrong shape, as one row with no values; the first problem
    /// wins. The page returns that problem and never reports it: the window reports it once. Each valve is a valve state with
    /// Open left out on the wire, so a valve the push does not carry reads open; a value that is no state reads "not
    /// available" and is reported, without stopping the page.</para>
    /// <para>Not read: the packs' bypass needles (owner question Q2: a gauge, left out) and the GND connection (no field).</para>
    /// </remarks>
    internal static FenixChannelRows BleedRows(FenixChannelSdPageContext ctx)
    {
        FenixChannelSdReader remote = FenixChannelSdReader.Of(ctx.Push, ctx.ReportProblem);
        FenixChannelSdReader pneumatic = remote.Message(FenixChannelFields.Remote.PneumaticState);

        FenixChannelSdReader valves = pneumatic.Message(FenixChannelFields.PneumaticState.Valves);
        string ramAir = valves.Valve(FenixChannelFields.PneumaticValveData.RamAir);
        string flow1 = valves.Valve(FenixChannelFields.PneumaticValveData.Pack1Flow);
        string flow2 = valves.Valve(FenixChannelFields.PneumaticValveData.Pack2Flow);
        string crossBleed = valves.Valve(FenixChannelFields.PneumaticValveData.Crossbleed);
        string apuBleed = ctx.ApuBleedWord(valves.Valve(FenixChannelFields.PneumaticValveData.ApuBleed));
        string bleed1 = valves.Valve(FenixChannelFields.PneumaticValveData.Eng1Bleed);
        string bleed2 = valves.Valve(FenixChannelFields.PneumaticValveData.Eng2Bleed);
        string hp1 = valves.Valve(FenixChannelFields.PneumaticValveData.Eng1Hp);
        string hp2 = valves.Valve(FenixChannelFields.PneumaticValveData.Eng2Hp);

        FenixChannelSdReader pressures = pneumatic.Message(FenixChannelFields.PneumaticState.BleedPressure);
        double psi1 = pressures.Double(FenixChannelFields.BleedPressureData.Left);
        double psi2 = pressures.Double(FenixChannelFields.BleedPressureData.Right);

        FenixChannelSdReader temperatures = pneumatic.Message(FenixChannelFields.PneumaticState.BleedTemperature);
        double temp1 = temperatures.Double(FenixChannelFields.BleedTemperatureData.Left);
        double temp2 = temperatures.Double(FenixChannelFields.BleedTemperatureData.Right);

        // The flags decide whether the anti-ice valves show, so they are read on every composition.
        FenixChannelSdReader flags = pneumatic.Message(FenixChannelFields.PneumaticState.Flags);
        bool antiIce = flags.Bool(FenixChannelFields.PneumaticFlagsData.WaiSwitchOn);
        bool waiAmberLeft = flags.Bool(FenixChannelFields.PneumaticFlagsData.WaiValveAmberLeft);
        bool waiAmberRight = flags.Bool(FenixChannelFields.PneumaticFlagsData.WaiValveAmberRight);
        bool startAir1 = flags.Bool(FenixChannelFields.PneumaticFlagsData.StartAirAmberLeft);
        bool startAir2 = flags.Bool(FenixChannelFields.PneumaticFlagsData.StartAirAmberRight);
        bool tempAmberLeft = flags.Bool(FenixChannelFields.PneumaticFlagsData.PrecoolerTempAmberLeft);
        bool tempAmberRight = flags.Bool(FenixChannelFields.PneumaticFlagsData.PrecoolerTempAmberRight);
        bool pressAmberLeft = flags.Bool(FenixChannelFields.PneumaticFlagsData.PrecoolerPressAmberLeft);
        bool pressAmberRight = flags.Bool(FenixChannelFields.PneumaticFlagsData.PrecoolerPressAmberRight);
        string waiLeft = "";
        string waiRight = "";
        if (antiIce)
        {
            waiLeft = valves.Valve(FenixChannelFields.PneumaticValveData.WaiLeft);
            waiRight = valves.Valve(FenixChannelFields.PneumaticValveData.WaiRight);
        }

        FenixChannelSdReader pack1 = pneumatic.Message(FenixChannelFields.PneumaticState.Pack1);
        double outlet1 = pack1.Double(FenixChannelFields.PackStateData.OutputTemp);
        double compressor1 = pack1.Double(FenixChannelFields.PackStateData.CompressorTemp);
        FenixChannelSdReader pack2 = pneumatic.Message(FenixChannelFields.PneumaticState.Pack2);
        double outlet2 = pack2.Double(FenixChannelFields.PackStateData.OutputTemp);
        double compressor2 = pack2.Double(FenixChannelFields.PackStateData.CompressorTemp);

        // Every read after a problem gives its default, which must never be spoken.
        if (remote.Problem is { } problem) return FenixChannelRows.NotAvailable(problem);

        var rows = new List<string>
        {
            $"RAM AIR: {ramAir}",
            $"PACK outlet °C: {FenixChannelSd.Whole(outlet1)}, {FenixChannelSd.Whole(outlet2)}",
            $"PACK compressor °C: {FenixChannelSd.Whole(compressor1)}, {FenixChannelSd.Whole(compressor2)}",
            $"PACK flow valves: {flow1}, {flow2}"
        };
        if (antiIce) rows.Add($"ANTI ICE valves: {waiLeft}{BleedAmber(waiAmberLeft)}, {waiRight}{BleedAmber(waiAmberRight)}");
        rows.Add($"X BLEED valve: {crossBleed}");
        rows.Add($"APU valve: {apuBleed}");
        rows.Add($"bleed PSI: {FenixChannelSd.Whole(psi1)}{BleedAmber(pressAmberLeft)}, {FenixChannelSd.Whole(psi2)}{BleedAmber(pressAmberRight)}");
        rows.Add($"bleed °C: {FenixChannelSd.Whole(temp1)}{BleedAmber(tempAmberLeft)}, {FenixChannelSd.Whole(temp2)}{BleedAmber(tempAmberRight)}");
        rows.Add($"bleed valves: {bleed1}, {bleed2}");
        rows.Add($"HP valves: {hp1}, {hp2}");
        if (startAir1) rows.Add("start air 1, amber");
        if (startAir2) rows.Add("start air 2, amber");
        return FenixChannelRows.Of(rows);
    }

    // " amber" right after a side's value, no comma, while the Fenix draws that side amber.
    private static string BleedAmber(bool amber) => amber ? " amber" : "";

    /// <summary>The BLEED page (2).</summary>
    static partial void ComposeBleed(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows)
        => rows = BleedRows(ctx);
}
