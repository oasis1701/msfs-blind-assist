namespace MSFSBlindAssist.Services;

// The FUEL page (10) of the Fenix SD: the fuel used and on board, the valves, the tanks, the pumps and the tank
// temperatures, as the SD draws them, in the layout of the loaded airframe. Written as the HYD page is
// (FenixChannelSdPage.Hyd.cs): one reader, every field read before any row is built, and the reader's problem returned as
// the page's one row. Every member added here is named for the FUEL page.
internal static partial class FenixChannelSdPages
{
    /// <summary>
    /// The FUEL page's rows, in the SD's order, every number whole. The layout follows the loaded airframe
    /// (<c>ctx.Variant.Type</c>), never a channel field: the channel sends no airframe.
    /// <para>An A319 or A320: the fuel used by both engines (<c>F.USED 1+2: 7220 KG</c>, or <c>LBS</c>, from the weight
    /// unit, 154), the APU, LP and crossfeed valves in the valve words (<c>LP valves: open, closed</c>), the five tanks
    /// (<c>left outer tank: 1101</c>, then the left inner, center, right inner and right outer: a whole number with no
    /// unit, as the SD draws none beside a tank), the left, center and right pumps in pairs (<c>left pumps: running,
    /// LO</c>: "off" without the pump's switch, "LO" with the switch and low pressure, else "running"), the left and right
    /// transfer valves in pairs, the fuel on board (<c>FOB: 8150 KG</c>) and the temperatures of the outer tanks and then
    /// of the inner tanks (<c>outer tanks °C: 12, 17</c>), each pair left then right.</para>
    /// <para>An A321 has no outer tanks, transfer valves, center pumps or outer temperatures: its wing tanks are <c>left
    /// tank</c> and <c>right tank</c>, and after <c>center tank</c> come the center transfer valves (<c>center transfer
    /// valves: open, closed</c>: "open" while the valve's field is set, else "closed"), then <c>ACT 1</c> while the
    /// variant's ActCount is 1 or more and <c>ACT 2</c> while it is 2 or more (none while the count is unread), then the
    /// right tank, the left and right pumps, the fuel on board and the one pair of temperatures, <c>tanks °C</c>.</para>
    /// <para>The rows speak the colors the Fenix sends: a pump, an LP valve or a center transfer valve (or its flow, while it flows) the SD
    /// draws amber gains " amber" right after its word (<c>left pumps: running amber, LO</c>, <c>LP valves: open,
    /// closed amber</c>), and an ACT pump drawn amber is the row <c>ACT pump, amber</c> after the ACT rows (an A321 with
    /// ACT tanks only; 134.15 is read only there). Fields checked against the installed Fenix 3.0.0.7613 on 2026-11-03
    /// ([FNX-23]).</para>
    /// </summary>
    /// <remarks>
    /// <para>The airframe is decided first. A Type the page has no layout for, <c>Unknown</c> or any other value, is "airframe
    /// not known", before a field is read: a push that is complete or missing the whole fuel message is no help in
    /// choosing a layout.</para>
    /// <para>Every message the rows read must be in the push: one left out is "no field 134" (or 134.2 to 134.7, 134.11
    /// to 134.14, and on the A321 134.1; its center pumps, 134.4 and 134.5, are not read), and a field in another wire
    /// shape is that field's wrong shape, both as one row with no values, never a valve open or a tank empty. The page
    /// returns that problem and never reports it: the window reports it once, under the page's name. A valve with no state
    /// field reads "open", which is how the Fenix sends Open, and one with a value that is no state reads "not available"
    /// and is reported without stopping the page.</para>
    /// <para>A part the airframe lacks is not read, so that a value the SD does not draw can neither stop the page nor be
    /// reported, and a value the push carries for it (the channel sends values for the A321's outer tanks, transfer valves
    /// and center pumps) can never prove the part exists ([FNX-15]). The ACT tanks follow the variant's config, never the
    /// data.</para>
    /// <para>Inferred, not measured, until the sim run (Task 1): the A321's layout and the parts it lacks, as the plan's
    /// text gives them; that the A318 follows the A320's layout; which valve, pump and tank each field is (from the
    /// contract's names: 134.11.1 and .2 the LP valves, .3 the APU, .4 the crossfeed, .5 and .6 the left transfer valves, .7
    /// and .8 the right; 134.12.1 to .5 the tanks left outer to right outer, .6 the total, .7 and .8 ACT 1 and 2; 134.14.1
    /// to .4 the temperatures left outer, left inner, right inner, right outer); 134.2/.3, .4/.5 and .6/.7 the left, center
    /// and right pumps with their switch (.1) and low pressure (.3); and 134.1.3 and .4 the A321's left and right center
    /// transfer valves, open while set. A difference changes the rows and their tests together.</para>
    /// </remarks>
    internal static FenixChannelRows FuelRows(FenixChannelSdPageContext ctx)
    {
        if (FuelLayoutOf(ctx.Variant.Type) is not { } layout) return FenixChannelRows.NotAvailable("airframe not known");

        bool a321 = layout == FuelLayout.A321;
        int actCount = ctx.Variant.ActCount ?? 0;

        FenixChannelSdReader remote = FenixChannelSdReader.Of(ctx.Push, ctx.ReportProblem);
        FenixChannelSdReader fuel = remote.Message(FenixChannelFields.Remote.FuelState);

        double usedTotal = fuel.Message(FenixChannelFields.FuelState.Used).Double(FenixChannelFields.FuelUsedData.Total);
        string unit = FenixChannelSd.WeightUnit(remote);

        FuelValvesReading valves = ReadFuelValves(fuel.Message(FenixChannelFields.FuelState.ValveState), a321);

        // The center transfer valves are the A321's alone.
        string centerTransferValves = "";
        if (a321)
        {
            FenixChannelSdReader jetPumps = fuel.Message(FenixChannelFields.FuelState.JetPumps);
            // A side is amber while its valve is drawn amber, or while its flow is drawn AND the flow is amber, said once.
            // The Fenix sends the flow's color (134.1.7, .8) while no flow is drawn (set all through a cruise with the flow
            // and the valves never set), and the SD draws the flow arrow only while that side flows, so the color alone
            // says nothing.
            bool leftFlowAmber = jetPumps.Bool(FenixChannelFields.JetPumpData.LeftFlow)
                & jetPumps.Bool(FenixChannelFields.JetPumpData.LeftFlowAmber);
            bool rightFlowAmber = jetPumps.Bool(FenixChannelFields.JetPumpData.RightFlow)
                & jetPumps.Bool(FenixChannelFields.JetPumpData.RightFlowAmber);
            centerTransferValves = string.Join(", ",
                FuelOpenWord(jetPumps.Bool(FenixChannelFields.JetPumpData.LeftValveOpen))
                    + FuelAmber(jetPumps.Bool(FenixChannelFields.JetPumpData.LeftValveAmber) | leftFlowAmber),
                FuelOpenWord(jetPumps.Bool(FenixChannelFields.JetPumpData.RightValveOpen))
                    + FuelAmber(jetPumps.Bool(FenixChannelFields.JetPumpData.RightValveAmber) | rightFlowAmber));
        }

        // The LP valves drawn amber (134.9, 134.10).
        bool lp1Amber = fuel.Bool(FenixChannelFields.FuelState.ValveAmberEngine1);
        bool lp2Amber = fuel.Bool(FenixChannelFields.FuelState.ValveAmberEngine2);

        FuelTanksReading tanks = ReadFuelTanks(fuel.Message(FenixChannelFields.FuelState.Quantity), a321, actCount);

        string leftPumps = ReadFuelPumpPair(
            fuel.Message(FenixChannelFields.FuelState.LeftWing1), fuel.Message(FenixChannelFields.FuelState.LeftWing2));
        string centerPumps = a321
            ? ""
            : ReadFuelPumpPair(
                fuel.Message(FenixChannelFields.FuelState.Center1), fuel.Message(FenixChannelFields.FuelState.Center2));
        string rightPumps = ReadFuelPumpPair(
            fuel.Message(FenixChannelFields.FuelState.RightWing1), fuel.Message(FenixChannelFields.FuelState.RightWing2));

        FuelTemperaturesReading temperatures = ReadFuelTemperatures(
            fuel.Message(FenixChannelFields.FuelState.Temperature), a321);

        // The ACT pump (134.15) is read only where the page shows ACT rows: an A321 with ACT tanks.
        bool actPumpAmber = a321 && actCount >= 1
            && fuel.Message(FenixChannelFields.FuelState.ActPump).Bool(FenixChannelFields.ActPumpData.Amber);

        // Every read after a problem gives its default, which must never be spoken.
        if (remote.Problem is { } problem) return FenixChannelRows.NotAvailable(problem);

        string used = $"F.USED 1+2: {FenixChannelSd.Whole(usedTotal)} {unit}";
        string onBoard = $"FOB: {FenixChannelSd.Whole(tanks.Total)} {unit}";
        string lpValves = $"LP valves: {valves.Lp1}{FuelAmber(lp1Amber)}, {valves.Lp2}{FuelAmber(lp2Amber)}";

        if (a321)
        {
            var a321Rows = new List<string>
            {
                used,
                $"APU valve: {valves.Apu}",
                lpValves,
                $"X FEED valve: {valves.Crossfeed}",
                $"left tank: {FenixChannelSd.Whole(tanks.LeftInner)}",
                $"center tank: {FenixChannelSd.Whole(tanks.Center)}",
                $"center transfer valves: {centerTransferValves}"
            };

            if (actCount >= 1) a321Rows.Add($"ACT 1: {FenixChannelSd.Whole(tanks.Act1)}");
            if (actCount >= 2) a321Rows.Add($"ACT 2: {FenixChannelSd.Whole(tanks.Act2)}");
            if (actPumpAmber) a321Rows.Add("ACT pump, amber");

            a321Rows.Add($"right tank: {FenixChannelSd.Whole(tanks.RightInner)}");
            a321Rows.Add($"left pumps: {leftPumps}");
            a321Rows.Add($"right pumps: {rightPumps}");
            a321Rows.Add(onBoard);
            a321Rows.Add($"tanks °C: {FenixChannelSd.Whole(temperatures.LeftInner)}, {FenixChannelSd.Whole(temperatures.RightInner)}");
            return FenixChannelRows.Of(a321Rows);
        }

        return FenixChannelRows.Of(
        [
            used,
            $"APU valve: {valves.Apu}",
            lpValves,
            $"X FEED valve: {valves.Crossfeed}",
            $"left outer tank: {FenixChannelSd.Whole(tanks.LeftOuter)}",
            $"left inner tank: {FenixChannelSd.Whole(tanks.LeftInner)}",
            $"center tank: {FenixChannelSd.Whole(tanks.Center)}",
            $"right inner tank: {FenixChannelSd.Whole(tanks.RightInner)}",
            $"right outer tank: {FenixChannelSd.Whole(tanks.RightOuter)}",
            $"left pumps: {leftPumps}",
            $"center pumps: {centerPumps}",
            $"right pumps: {rightPumps}",
            $"left transfer valves: {valves.LeftTransfer1}, {valves.LeftTransfer2}",
            $"right transfer valves: {valves.RightTransfer1}, {valves.RightTransfer2}",
            onBoard,
            $"outer tanks °C: {FenixChannelSd.Whole(temperatures.LeftOuter)}, {FenixChannelSd.Whole(temperatures.RightOuter)}",
            $"inner tanks °C: {FenixChannelSd.Whole(temperatures.LeftInner)}, {FenixChannelSd.Whole(temperatures.RightInner)}"
        ]);
    }

    /// <summary>The FUEL page (10).</summary>
    static partial void ComposeFuel(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows)
        => rows = FuelRows(ctx);

    // The two layouts the page has.
    private enum FuelLayout
    {
        A320,
        A321
    }

    // The layout of an airframe, null for one the page has none for. The A318 follows the A320's: inferred, as no A318 has
    // been measured; Task 1 measures it.
    private static FuelLayout? FuelLayoutOf(Aircraft.FenixAircraftType type) => type switch
    {
        Aircraft.FenixAircraftType.A318 => FuelLayout.A320,
        Aircraft.FenixAircraftType.A319 => FuelLayout.A320,
        Aircraft.FenixAircraftType.A320 => FuelLayout.A320,
        Aircraft.FenixAircraftType.A321 => FuelLayout.A321,
        _ => null
    };

    // What the page reads of the fuel valves (134.11), each as its valve word. The transfer valves are the A320's layout
    // alone, and stay empty on the A321.
    private readonly record struct FuelValvesReading(
        string Lp1, string Lp2, string Apu, string Crossfeed, string LeftTransfer1, string LeftTransfer2,
        string RightTransfer1, string RightTransfer2);

    // What the page reads of the quantities (134.12): the outer tanks on the A320's layout alone, the ACT tanks only
    // while the variant's config says the airframe has them.
    private readonly record struct FuelTanksReading(
        double LeftOuter, double LeftInner, double Center, double RightInner, double RightOuter, double Total, double Act1,
        double Act2);

    // What the page reads of the temperatures (134.14): the outer tanks' on the A320's layout alone.
    private readonly record struct FuelTemperaturesReading(
        double LeftOuter, double LeftInner, double RightInner, double RightOuter);

    private static FuelValvesReading ReadFuelValves(FenixChannelSdReader valves, bool a321)
    {
        string lp1 = valves.Valve(FenixChannelFields.FuelValveStates.Engine1);
        string lp2 = valves.Valve(FenixChannelFields.FuelValveStates.Engine2);
        string apu = valves.Valve(FenixChannelFields.FuelValveStates.Apu);
        string crossfeed = valves.Valve(FenixChannelFields.FuelValveStates.Crossfeed);

        string leftTransfer1 = "";
        string leftTransfer2 = "";
        string rightTransfer1 = "";
        string rightTransfer2 = "";
        if (!a321)
        {
            leftTransfer1 = valves.Valve(FenixChannelFields.FuelValveStates.LeftTransfer1);
            leftTransfer2 = valves.Valve(FenixChannelFields.FuelValveStates.LeftTransfer2);
            rightTransfer1 = valves.Valve(FenixChannelFields.FuelValveStates.RightTransfer1);
            rightTransfer2 = valves.Valve(FenixChannelFields.FuelValveStates.RightTransfer2);
        }

        return new FuelValvesReading(
            lp1, lp2, apu, crossfeed, leftTransfer1, leftTransfer2, rightTransfer1, rightTransfer2);
    }

    private static FuelTanksReading ReadFuelTanks(FenixChannelSdReader quantity, bool a321, int actCount)
    {
        double leftOuter = a321 ? 0 : quantity.Double(FenixChannelFields.FuelQuantityData.LeftOuter);
        double leftInner = quantity.Double(FenixChannelFields.FuelQuantityData.LeftInner);
        double center = quantity.Double(FenixChannelFields.FuelQuantityData.Center);
        double rightInner = quantity.Double(FenixChannelFields.FuelQuantityData.RightInner);
        double rightOuter = a321 ? 0 : quantity.Double(FenixChannelFields.FuelQuantityData.RightOuter);
        double total = quantity.Double(FenixChannelFields.FuelQuantityData.Total);

        // The ACT tanks are the A321's, as many as its config says.
        double act1 = a321 && actCount >= 1 ? quantity.Double(FenixChannelFields.FuelQuantityData.Act1) : 0;
        double act2 = a321 && actCount >= 2 ? quantity.Double(FenixChannelFields.FuelQuantityData.Act2) : 0;

        return new FuelTanksReading(leftOuter, leftInner, center, rightInner, rightOuter, total, act1, act2);
    }

    private static FuelTemperaturesReading ReadFuelTemperatures(FenixChannelSdReader temperature, bool a321)
    {
        double leftOuter = a321 ? 0 : temperature.Double(FenixChannelFields.FuelTemperatureData.LeftOuter);
        double leftInner = temperature.Double(FenixChannelFields.FuelTemperatureData.LeftInner);
        double rightInner = temperature.Double(FenixChannelFields.FuelTemperatureData.RightInner);
        double rightOuter = a321 ? 0 : temperature.Double(FenixChannelFields.FuelTemperatureData.RightOuter);

        return new FuelTemperaturesReading(leftOuter, leftInner, rightInner, rightOuter);
    }

    // Two pumps of one side as their words, "running, LO".
    private static string ReadFuelPumpPair(FenixChannelSdReader first, FenixChannelSdReader second)
        => $"{FuelPumpWord(first)}, {FuelPumpWord(second)}";

    // A pump: "off" without its switch, "LO" with the switch and low pressure (the SD's own word), else "running".
    private static string FuelPumpWord(FenixChannelSdReader pump)
    {
        bool switchOn = pump.Bool(FenixChannelFields.FuelPumpState.Switch);
        bool lowPressure = pump.Bool(FenixChannelFields.FuelPumpState.LowPressure);

        string word = !switchOn ? "off" : lowPressure ? "LO" : "running";
        return word + FuelAmber(pump.Bool(FenixChannelFields.FuelPumpState.Amber));
    }

    // " amber" right after a value the SD draws amber, no comma.
    private static string FuelAmber(bool amber) => amber ? " amber" : "";

    // A valve whose field is a bool: "open" while it is set, else "closed".
    private static string FuelOpenWord(bool open) => open ? "open" : "closed";
}
