namespace MSFSBlindAssist.Services;

// The COND page (4) of the Fenix SD: the cockpit and cabin zones, the cargo compartments and the fans and modes as the SD
// draws them, top to bottom. Written as the BLEED and PRESS pages are (FenixChannelSdPage.Bleed.cs, .Press.cs): one reader,
// every field read before any row is built, and the reader's problem returned as the page's one row. Every member added here
// is named for the COND page. The ComposeCond seam calls CondRows.
internal static partial class FenixChannelSdPages
{
    // What a HOT AIR valve value reads as (151.7.10, measured at the CYYZ gate on 2026-11-03); any other value reads "not
    // available" in its row and is reported, never guessed.
    private static readonly IReadOnlyDictionary<double, string> CondHotAirWords = new Dictionary<double, string>
    {
        [FenixChannelFields.HotAirStates.Open] = "open",
        [FenixChannelFields.HotAirStates.Closed] = "closed",
        [FenixChannelFields.HotAirStates.InTransit] = "in transit"
    };

    /// <summary>
    /// The COND page's rows, in the SD's order: <c>CKPT</c>, <c>FWD</c> and <c>AFT</c>, each <c>label: n °C, duct n °C, trim
    /// valve n % hot</c>; then <c>HOT AIR: open</c>, <c>closed</c> or <c>in transit</c> (151.7.10); then <c>FWD CARGO</c> only while forward cargo heating is fitted (151.8.5) and <c>AFT CARGO</c> only
    /// while aft cargo heating is (151.8.6); then <c>FAN 1: off</c> (151.8.1), <c>FAN 2: off</c> (151.8.7), <c>ALTN MODE</c>
    /// (151.8.2) and <c>PACK REG</c> (151.8.3), each only while its flag is set. All numbers are whole. No row speaks a color because the Fenix sends none for these rows.
    /// </summary>
    /// <remarks>
    /// <para>The pneumatic state (151) must be in the push with its zone data (151.7) and conditioning states (151.8): one
    /// left out is "no field 151.x", never its defaults; the first problem wins, and the page returns it and never reports it.
    /// The cargo fields are read only while their heating flag is set, since the Fenix sends their values with or without
    /// cargo heating.</para>
    /// <para>The trim valves are a fraction open toward hot (0 to 1; the SD's gauge runs from C to H), spoken as a percent
    /// with "hot" (owner question Q2). HOT AIR (151.7.10) is a
    /// double that follows the HOT AIR pushbutton, measured at the CYYZ gate on 2026-11-03: 1 (closed), then 2 (in transit)
    /// for 3 s, then left out (0, open), and left out in cruise with HOT AIR on. Any other value reads "HOT AIR: not
    /// available" and is reported "field 151.7.10 has an unknown value &lt;v&gt;" without stopping the page.</para>
    /// </remarks>
    internal static FenixChannelRows CondRows(FenixChannelSdPageContext ctx)
    {
        FenixChannelSdReader remote = FenixChannelSdReader.Of(ctx.Push, ctx.ReportProblem);
        FenixChannelSdReader pneumatic = remote.Message(FenixChannelFields.Remote.PneumaticState);

        FenixChannelSdReader zone = pneumatic.Message(FenixChannelFields.PneumaticState.Zone);
        double cockpit = zone.Double(FenixChannelFields.ZoneTemperatureData.CockpitTemp);
        double fwd = zone.Double(FenixChannelFields.ZoneTemperatureData.FwdCabinTemp);
        double aft = zone.Double(FenixChannelFields.ZoneTemperatureData.AftCabinTemp);
        double cockpitDuct = zone.Double(FenixChannelFields.ZoneTemperatureData.CockpitDuctTemp);
        double fwdDuct = zone.Double(FenixChannelFields.ZoneTemperatureData.FwdCabinDuctTemp);
        double aftDuct = zone.Double(FenixChannelFields.ZoneTemperatureData.AftCabinDuctTemp);
        double cockpitTrim = zone.Double(FenixChannelFields.ZoneTemperatureData.CockpitTrimValve);
        double fwdTrim = zone.Double(FenixChannelFields.ZoneTemperatureData.FwdCabinTrimValve);
        double aftTrim = zone.Double(FenixChannelFields.ZoneTemperatureData.AftCabinTrimValve);
        double hotAir = zone.Double(FenixChannelFields.ZoneTemperatureData.HotAirValve);

        FenixChannelSdReader states = pneumatic.Message(FenixChannelFields.PneumaticState.CondStates);
        bool fan1Off = states.Bool(FenixChannelFields.ConditioningStateData.CabFan1Off);
        bool alternate = states.Bool(FenixChannelFields.ConditioningStateData.Alternate);
        bool packReg = states.Bool(FenixChannelFields.ConditioningStateData.PackReg);
        bool cargoFwd = states.Bool(FenixChannelFields.ConditioningStateData.HasCargoHeatingFwd);
        bool cargoAft = states.Bool(FenixChannelFields.ConditioningStateData.HasCargoHeatingAft);
        bool fan2Off = states.Bool(FenixChannelFields.ConditioningStateData.CabFan2Off);

        double fwdCargo = 0, fwdCargoDuct = 0, fwdCargoTrim = 0, aftCargo = 0, aftCargoDuct = 0, aftCargoTrim = 0;
        if (cargoFwd)
        {
            fwdCargo = zone.Double(FenixChannelFields.ZoneTemperatureData.FwdCargoTemp);
            fwdCargoDuct = zone.Double(FenixChannelFields.ZoneTemperatureData.FwdCargoDuctTemp);
            fwdCargoTrim = zone.Double(FenixChannelFields.ZoneTemperatureData.FwdCargoTrimValve);
        }

        if (cargoAft)
        {
            aftCargo = zone.Double(FenixChannelFields.ZoneTemperatureData.AftCargoTemp);
            aftCargoDuct = zone.Double(FenixChannelFields.ZoneTemperatureData.AftCargoDuctTemp);
            aftCargoTrim = zone.Double(FenixChannelFields.ZoneTemperatureData.AftCargoTrimValve);
        }

        // Every read after a problem gives its default, which must never be spoken.
        if (remote.Problem is { } problem) return FenixChannelRows.NotAvailable(problem);

        var rows = new List<string>
        {
            CondZoneRow("CKPT", cockpit, cockpitDuct, cockpitTrim),
            CondZoneRow("FWD", fwd, fwdDuct, fwdTrim),
            CondZoneRow("AFT", aft, aftDuct, aftTrim),
            "HOT AIR: " + SdMeasuredWord(ctx, CondHotAirWords,
                $"{FenixChannelFields.Remote.PneumaticState}.{FenixChannelFields.PneumaticState.Zone}.{FenixChannelFields.ZoneTemperatureData.HotAirValve}",
                hotAir, "not available")
        };
        if (cargoFwd) rows.Add(CondZoneRow("FWD CARGO", fwdCargo, fwdCargoDuct, fwdCargoTrim));
        if (cargoAft) rows.Add(CondZoneRow("AFT CARGO", aftCargo, aftCargoDuct, aftCargoTrim));
        if (fan1Off) rows.Add("FAN 1: off");
        if (fan2Off) rows.Add("FAN 2: off");
        if (alternate) rows.Add("ALTN MODE");
        if (packReg) rows.Add("PACK REG");
        return FenixChannelRows.Of(rows);
    }

    /// <summary>The COND page (4).</summary>
    static partial void ComposeCond(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows)
        => rows = CondRows(ctx);

    // One zone row. The SD draws the trim valve as a gauge from C (cold) to H (hot); the value is a fraction (0 to 1) of how
    // far open it is toward hot, spoken as a percent with "hot".
    private static string CondZoneRow(string label, double temperature, double duct, double trimValve)
        => $"{label}: {FenixChannelSd.Whole(temperature)} °C, duct {FenixChannelSd.Whole(duct)} °C, trim valve {FenixChannelSd.Whole(trimValve * 100)} % hot";
}
