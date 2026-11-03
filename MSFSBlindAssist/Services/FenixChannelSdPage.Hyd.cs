using System.Globalization;

namespace MSFSBlindAssist.Services;

// The HYD page (11) of the Fenix SD: the three hydraulic systems, the PTU, the pumps, the RAT, the shutoff (fire) valves
// and the reservoirs, as the SD draws them. Written as the ELEC page is (FenixChannelSdPage.Elec.cs): one reader, every
// field read before any row is built, and the reader's problem returned as the page's one row. Every member added here
// is named for the HYD page.
internal static partial class FenixChannelSdPages
{
    /// <summary>
    /// The HYD page's rows, in the SD's order, every number whole: the pressure of each system (<c>GREEN: 2988 PSI</c>,
    /// then <c>BLUE</c> and <c>YELLOW</c>), the PTU (<c>PTU: idle</c>, or <c>off</c>, <c>yellow to green</c>, <c>green to
    /// yellow</c>), the pumps (<c>GREEN ENG 1 pump</c>, <c>BLUE ELEC
    /// pump</c>, <c>YELLOW ENG 2 pump</c>, <c>YELLOW ELEC pump</c>: "off" without the pump's switch, "LO" with the switch
    /// and no output, else "running", and <c>, OVHT</c> after the blue electric pump's word while it is overheated), the
    /// RAT (<c>RAT: stowed</c>, or <c>deployed</c>), the green and yellow shutoff valves (<c>GREEN fire valve: open</c>,
    /// or <c>closed</c>) and the quantity of each reservoir, a whole number with no unit because the SD draws it as a
    /// gauge (<c>GREEN reservoir: 14</c>, then <c>, LO AIR PRESS</c> while its air pressure is low and <c>, OVHT</c>
    /// while it is overheated). No row speaks a color: they are values and flags the app builds.
    /// </summary>
    /// <remarks>
    /// <para>Every message the rows read must be in the push: one left out is "no field 133" (or 133.1 to 133.3, 133.8,
    /// 133.9), and a field in another wire shape is that field's wrong shape, both as one row with no values, never a
    /// system at 0 PSI. The page returns that problem and never reports it: the window reports it once, under the page's
    /// name. A message present but empty is its defaults: the Fenix sends an empty electric pump message while the pump
    /// is off.</para>
    /// <para>A PTU state the app does not know reads "PTU: not available" and is reported, "PTU state &lt;n&gt; not
    /// known", at once and without stopping the page, but only for a page that is shown: a page stopped by a problem of
    /// its own reports nothing about the PTU.</para>
    /// <para>A flag the SD does not draw is not read, so that a value it does not draw can neither stop the page nor be
    /// reported: the overheat of the yellow electric pump (133.9.3) has no row. Which flag means what is inferred from
    /// the contract's names and the plan's text, and Task 1 measures what the SD draws with each; a difference changes
    /// the rows and their tests together: 133.4/133.5 the green engine pump's switch and output, 133.6/133.7 the yellow
    /// engine pump's, 133.8 and 133.9 the blue and yellow electric pumps, 133.10 and 133.11 the green and yellow shutoff
    /// valves, open while set (inferred from the 2026-11-01 cruise push, engines running, in which both were set), and
    /// 133.12 the RAT.</para>
    /// </remarks>
    internal static FenixChannelRows HydRows(FenixChannelSdPageContext ctx)
    {
        FenixChannelSdReader remote = FenixChannelSdReader.Of(ctx.Push, ctx.ReportProblem);

        FenixChannelSdReader hydraulic = remote.Message(FenixChannelFields.Remote.HydraulicState);
        HydSystemReading green = ReadHydSystem(hydraulic.Message(FenixChannelFields.HydraulicState.Green));
        HydSystemReading blue = ReadHydSystem(hydraulic.Message(FenixChannelFields.HydraulicState.Blue));
        HydSystemReading yellow = ReadHydSystem(hydraulic.Message(FenixChannelFields.HydraulicState.Yellow));

        bool greenPumpSwitch = hydraulic.Bool(FenixChannelFields.HydraulicState.EnginePumpLeftSwitch);
        bool greenPumpOutput = hydraulic.Bool(FenixChannelFields.HydraulicState.EnginePumpLeftOutput);
        bool yellowPumpSwitch = hydraulic.Bool(FenixChannelFields.HydraulicState.EnginePumpRightSwitch);
        bool yellowPumpOutput = hydraulic.Bool(FenixChannelFields.HydraulicState.EnginePumpRightOutput);

        // Only the blue electric pump's overheat is drawn.
        HydElecPumpReading blueElec = ReadHydElecPump(
            hydraulic.Message(FenixChannelFields.HydraulicState.ElecPumpBlue), overheatDrawn: true);
        HydElecPumpReading yellowElec = ReadHydElecPump(
            hydraulic.Message(FenixChannelFields.HydraulicState.ElecPumpYellow), overheatDrawn: false);

        bool greenShutoff = hydraulic.Bool(FenixChannelFields.HydraulicState.ShutoffLeft);
        bool yellowShutoff = hydraulic.Bool(FenixChannelFields.HydraulicState.ShutoffRight);
        bool ratDeployed = hydraulic.Bool(FenixChannelFields.HydraulicState.RatDeployed);

        int ptuState = remote.Int(FenixChannelFields.Remote.PtuState);

        // Every read after a problem gives its default, which must never be spoken.
        if (remote.Problem is { } problem) return FenixChannelRows.NotAvailable(problem);

        return FenixChannelRows.Of(
        [
            $"GREEN: {FenixChannelSd.Whole(green.Pressure)} PSI",
            $"BLUE: {FenixChannelSd.Whole(blue.Pressure)} PSI",
            $"YELLOW: {FenixChannelSd.Whole(yellow.Pressure)} PSI",
            HydPtuRow(ptuState, ctx.ReportProblem),
            $"GREEN ENG 1 pump: {HydPumpWord(greenPumpSwitch, greenPumpOutput)}",
            $"BLUE ELEC pump: {HydPumpWord(blueElec.Switch, blueElec.Output)}{(blueElec.Overheat ? ", OVHT" : "")}",
            $"YELLOW ENG 2 pump: {HydPumpWord(yellowPumpSwitch, yellowPumpOutput)}",
            $"YELLOW ELEC pump: {HydPumpWord(yellowElec.Switch, yellowElec.Output)}",
            $"RAT: {(ratDeployed ? "deployed" : "stowed")}",
            $"GREEN fire valve: {HydValveWord(greenShutoff)}",
            $"YELLOW fire valve: {HydValveWord(yellowShutoff)}",
            HydReservoirRow("GREEN", green),
            HydReservoirRow("BLUE", blue),
            HydReservoirRow("YELLOW", yellow)
        ]);
    }

    /// <summary>The HYD page (11).</summary>
    static partial void ComposeHyd(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows)
        => rows = HydRows(ctx);

    // What the page reads of one hydraulic system (133.1 to 133.3): the pressure and the reservoir's quantity and flags.
    private readonly record struct HydSystemReading(double Pressure, double Quantity, bool LowAirPressure, bool Overheat);

    // What the page reads of one electric pump (133.8, 133.9). The overheat stays false where the SD draws none.
    private readonly record struct HydElecPumpReading(bool Switch, bool Output, bool Overheat);

    private static HydSystemReading ReadHydSystem(FenixChannelSdReader system)
        => new(
            system.Double(FenixChannelFields.HydSystemState.Pressure),
            system.Double(FenixChannelFields.HydSystemState.Quantity),
            system.Bool(FenixChannelFields.HydSystemState.LowAirPressure),
            system.Bool(FenixChannelFields.HydSystemState.ReservoirOverheat));

    private static HydElecPumpReading ReadHydElecPump(FenixChannelSdReader pump, bool overheatDrawn)
    {
        bool switchOn = pump.Bool(FenixChannelFields.HydElecPumpState.Switch);
        bool output = pump.Bool(FenixChannelFields.HydElecPumpState.Output);
        bool overheat = overheatDrawn && pump.Bool(FenixChannelFields.HydElecPumpState.Overheat);

        return new HydElecPumpReading(switchOn, output, overheat);
    }

    // A pump: "off" without its switch, "LO" with the switch and no output (the SD's own word), else "running".
    private static string HydPumpWord(bool switchOn, bool output) => !switchOn ? "off" : output ? "running" : "LO";

    // A shutoff (fire) valve is open while its field is set. Inferred from the 2026-11-01 cruise push, engines running, in
    // which both were set; Task 1 measures a closed one.
    private static string HydValveWord(bool open) => open ? "open" : "closed";

    // A reservoir's quantity, a whole number with no unit (the SD draws a gauge), then the flags the SD shows with it.
    private static string HydReservoirRow(string label, HydSystemReading system)
        => $"{label} reservoir: {FenixChannelSd.Whole(system.Quantity)}"
           + (system.LowAirPressure ? ", LO AIR PRESS" : "")
           + (system.Overheat ? ", OVHT" : "");

    // The PTU's row. A state the app does not know reads "not available" and is reported, without stopping the page, in
    // the invariant culture whatever the PC's is.
    private static string HydPtuRow(int state, Action<string> report)
    {
        if (HydPtuWord(state) is { } word) return $"PTU: {word}";

        report(string.Create(CultureInfo.InvariantCulture, $"PTU state {state} not known"));
        return "PTU: not available";
    }

    // The word for a PTU state, null for one the app does not know. The four states are those measured in the sim (see
    // PtuStates); a new one is added here with its test.
    private static string? HydPtuWord(int state) => state switch
    {
        FenixChannelFields.PtuStates.Off => "off",
        FenixChannelFields.PtuStates.Idle => "idle",
        FenixChannelFields.PtuStates.YellowToGreen => "yellow to green",
        FenixChannelFields.PtuStates.GreenToYellow => "green to yellow",
        _ => null
    };
}
