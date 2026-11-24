using System.Globalization;

namespace MSFSBlindAssist.Services;

// The WHEEL page (12) of the Fenix SD: the gear and its doors, the brake and steering labels, the brake temperatures, the
// released brakes and the tire pressures. Written as the COND and DOOR pages are: one reader, every field read before any
// row is built, and the reader's problem returned as the page's one row. Every member added here is named for the WHEEL
// page. The ComposeWheel seam calls WheelRows.
internal static partial class FenixChannelSdPages
{
    // What a gear's LGCIU value and a gear door's value read as. One line per value measured on the 2026-11-03 gear extension;
    // any other value reads "not available" in its row and is reported, never guessed. A gear DOWN is the absent field (0).
    // The A320 WHEEL page draws a door that is not closed one way, amber, so both its not-closed values read "open".
    private static readonly IReadOnlyDictionary<double, string> WheelGearUnitWords = new Dictionary<double, string>
    {
        [FenixChannelFields.GearUnit.Up] = "up",
        [FenixChannelFields.GearUnit.InTransit] = "in transit",
        [FenixChannelFields.GearUnit.Down] = "down"
    };

    private static readonly IReadOnlyDictionary<double, string> WheelGearDoorWords = new Dictionary<double, string>
    {
        [FenixChannelFields.GearDoorPosition.Closed] = "closed",
        [FenixChannelFields.GearDoorPosition.Opening] = "open",
        [FenixChannelFields.GearDoorPosition.Open] = "open"
    };

    /// <summary>
    /// The WHEEL page's rows, top to bottom: <c>L/G CTL</c> (137.12); <c>nose gear: &lt;word&gt;</c>
    /// (143.1.1, 143.1.2) and <c>nose gear door</c> (144.1); <c>N/W STEERING</c> (137.9); <c>ANTI SKID, amber</c> (137.17) or
    /// <c>, green</c> (137.16); <c>BSCU 1, amber</c> (137.10) and <c>BSCU 2, amber</c> (137.11); <c>NORM BRK</c> (137.8 amber,
    /// 137.7 green); <c>ALTN BRK</c> (137.15 amber, 137.2 green); <c>ACCU ONLY</c> (137.18); <c>AUTO BRK: LO</c>, MED or
    /// MAX (137.4, .5, .6) with <c>, amber</c> after it while 137.13 (<c>AUTO BRK, amber</c> with no mode); <c>left gear</c> (143.2) and <c>left gear door</c> (144.2); <c>right gear</c> (143.3) and
    /// <c>right gear door</c> (144.3); while 137.19 (the tire pressure indicators are fitted), <c>nose tires: left n PSI,
    /// right n PSI</c> (139.1, .2); and one row per main wheel, <c>wheel 1: brake n °C, tire n PSI</c> (138.1 to .4 and
    /// 139.3 to .6; without the indicators just <c>wheel n: brake n °C</c>), with <c>amber</c> right after the temperature
    /// while the Fenix draws that brake amber (140.1 to .4), ending <c>, released</c> while that wheel's brake is released
    /// (141.1 to .4). Numbers are whole. Colors: the SD speaks the colors the Fenix sends. A field named for a color speaks
    /// it, and a label sent as one flag with no color (N/W STEERING, L/G CTL, ACCU ONLY) stays bare, never a guessed color.
    /// </summary>
    /// <remarks>
    /// <para>The wheels are numbered as the A320 numbers them, 1 to 4 left to right facing forward (1 and 2 the left main
    /// gear, 3 and 4 the right). The contract's tire fields <c>LeftMain1</c>/<c>LeftMain2</c> are taken as wheels 1 and 2
    /// and <c>RightMain1</c>/<c>RightMain2</c> as 3 and 4 (its order; the Fenix's own SD drawing is not yet compared).</para>
    /// <para>Not read: 135, 136 and 142 (a light, the brake pressure gauge and a brake's color, none drawn as SD
    /// text). 139 is read only while 137.19, so a push without it and without the indicators is a complete page. 140 is
    /// always read: the Fenix always sends it, an empty message while no brake is amber, so a push without it is
    /// "not available, no field 140".</para>
    /// <para>With more than one of 137.4, .5 and .6 set, the AUTO BRK row names the first in LO, MED, MAX order: the SD
    /// shows one.</para>
    /// <para>Gear words, measured on the 2026-11-03 gear extension. A gear value reads "up" (2), "in transit" (1) or "down"
    /// (0, the absent field: a gear down is a present, empty message, never a missing one); a door value reads "closed" (1)
    /// or "open" (0 while it opens, 2 while the gear extends). A gear row says the word once while both LGCIUs give it
    /// ("nose gear: down") and names each when they differ ("nose gear: LGCIU 1 down, LGCIU 2 in transit"). Any other
    /// value reads "not available" (in the named form "LGCIU &lt;n&gt; not available"; both unknown, one "nose gear: not
    /// available") and is reported "field &lt;path&gt; has an unknown value &lt;v&gt;" without stopping the
    /// page.</para>
    /// </remarks>
    internal static FenixChannelRows WheelRows(FenixChannelSdPageContext ctx)
    {
        FenixChannelSdReader remote = FenixChannelSdReader.Of(ctx.Push, ctx.ReportProblem);
        FenixChannelSdReader flags = remote.Message(FenixChannelFields.Remote.WheelBrakeFlags);
        FenixChannelSdReader temps = remote.Message(FenixChannelFields.Remote.WheelTemp);
        FenixChannelSdReader amberBrakes = remote.Message(FenixChannelFields.Remote.WheelAmber);
        FenixChannelSdReader released = remote.Message(FenixChannelFields.Remote.BrakeReleased);
        FenixChannelSdReader gears = remote.Message(FenixChannelFields.Remote.GearPosition);
        FenixChannelSdReader doors = remote.Message(FenixChannelFields.Remote.GearDoor);

        bool landingGearControl = flags.Bool(FenixChannelFields.WheelBrakeFlags.LgCtlLabel);
        bool steering = flags.Bool(FenixChannelFields.WheelBrakeFlags.SteeringLabel);
        bool antiSkidGreen = flags.Bool(FenixChannelFields.WheelBrakeFlags.AntiSkidGreen);
        bool antiSkidAmber = flags.Bool(FenixChannelFields.WheelBrakeFlags.AntiSkidAmber);
        bool bscu1 = flags.Bool(FenixChannelFields.WheelBrakeFlags.Bscu1Amber);
        bool bscu2 = flags.Bool(FenixChannelFields.WheelBrakeFlags.Bscu2Amber);
        bool normalGreen = flags.Bool(FenixChannelFields.WheelBrakeFlags.NormBrakeGreen);
        bool normalAmber = flags.Bool(FenixChannelFields.WheelBrakeFlags.NormBrakeAmber);
        bool alternateGreen = flags.Bool(FenixChannelFields.WheelBrakeFlags.AlternateBrakeGreen);
        bool alternateAmber = flags.Bool(FenixChannelFields.WheelBrakeFlags.AlternateBrakeAmber);
        bool accumulatorOnly = flags.Bool(FenixChannelFields.WheelBrakeFlags.AccuOnly);
        bool autoLow = flags.Bool(FenixChannelFields.WheelBrakeFlags.AutoBrakeLow);
        bool autoMed = flags.Bool(FenixChannelFields.WheelBrakeFlags.AutoBrakeMed);
        bool autoMax = flags.Bool(FenixChannelFields.WheelBrakeFlags.AutoBrakeMax);
        bool autoAmber = flags.Bool(FenixChannelFields.WheelBrakeFlags.AutoBrakeAmber);
        bool tpis = flags.Bool(FenixChannelFields.WheelBrakeFlags.TpisFitted);

        double[] brakeTemps =
        [
            temps.Double(FenixChannelFields.WheelTempData.Wheel1), temps.Double(FenixChannelFields.WheelTempData.Wheel2),
            temps.Double(FenixChannelFields.WheelTempData.Wheel3), temps.Double(FenixChannelFields.WheelTempData.Wheel4)
        ];

        bool[] amberWheels =
        [
            amberBrakes.Bool(FenixChannelFields.WheelAmberData.Wheel1), amberBrakes.Bool(FenixChannelFields.WheelAmberData.Wheel2),
            amberBrakes.Bool(FenixChannelFields.WheelAmberData.Wheel3), amberBrakes.Bool(FenixChannelFields.WheelAmberData.Wheel4)
        ];

        bool[] releasedWheels =
        [
            released.Bool(FenixChannelFields.BrakeReleasedData.Left1), released.Bool(FenixChannelFields.BrakeReleasedData.Left2),
            released.Bool(FenixChannelFields.BrakeReleasedData.Right1), released.Bool(FenixChannelFields.BrakeReleasedData.Right2)
        ];

        // The gears: each a nested message of its two LGCIUs.
        FenixChannelSdReader noseGear = gears.Message(FenixChannelFields.GearPositionData.Nose);
        FenixChannelSdReader leftGear = gears.Message(FenixChannelFields.GearPositionData.Left);
        FenixChannelSdReader rightGear = gears.Message(FenixChannelFields.GearPositionData.Right);
        (double, double) nose = WheelLgcius(noseGear);
        (double, double) left = WheelLgcius(leftGear);
        (double, double) right = WheelLgcius(rightGear);

        double noseDoor = doors.Double(FenixChannelFields.GearDoorData.Nose);
        double leftDoor = doors.Double(FenixChannelFields.GearDoorData.LeftMain);
        double rightDoor = doors.Double(FenixChannelFields.GearDoorData.RightMain);

        // 139 is the tire pressures: required only while the indicators are fitted.
        double[] pressures = [];
        if (tpis)
        {
            FenixChannelSdReader tires = remote.Message(FenixChannelFields.Remote.TyrePressure);
            pressures =
            [
                tires.Double(FenixChannelFields.TyrePressureData.NoseLh), tires.Double(FenixChannelFields.TyrePressureData.NoseRh),
                tires.Double(FenixChannelFields.TyrePressureData.LeftMain1), tires.Double(FenixChannelFields.TyrePressureData.LeftMain2),
                tires.Double(FenixChannelFields.TyrePressureData.RightMain1), tires.Double(FenixChannelFields.TyrePressureData.RightMain2)
            ];
        }

        // Every read after a problem gives its default, which must never be spoken (nor reported as an unknown value).
        if (remote.Problem is { } problem) return FenixChannelRows.NotAvailable(problem);

        string gearPath = $"{FenixChannelFields.Remote.GearPosition}";
        string doorPath = $"{FenixChannelFields.Remote.GearDoor}";
        var rows = new List<string>();
        if (landingGearControl) rows.Add("L/G CTL");
        rows.Add(WheelGearRow(ctx, "nose gear", $"{gearPath}.{FenixChannelFields.GearPositionData.Nose}", nose));
        rows.Add(WheelGearDoorRow(ctx, "nose gear door", $"{doorPath}.{FenixChannelFields.GearDoorData.Nose}", noseDoor));
        if (steering) rows.Add("N/W STEERING");
        if (antiSkidAmber) rows.Add("ANTI SKID, amber");
        else if (antiSkidGreen) rows.Add("ANTI SKID, green");
        if (bscu1) rows.Add("BSCU 1, amber");
        if (bscu2) rows.Add("BSCU 2, amber");
        if (normalAmber) rows.Add("NORM BRK, amber");
        else if (normalGreen) rows.Add("NORM BRK, green");
        if (alternateAmber) rows.Add("ALTN BRK, amber");
        else if (alternateGreen) rows.Add("ALTN BRK, green");
        if (accumulatorOnly) rows.Add("ACCU ONLY");
        string? autoMode = autoLow ? "LO" : autoMed ? "MED" : autoMax ? "MAX" : null;
        if (autoMode is not null || autoAmber)
        {
            string auto = autoMode is null ? "AUTO BRK" : $"AUTO BRK: {autoMode}";
            rows.Add(autoAmber ? $"{auto}, amber" : auto);
        }
        rows.Add(WheelGearRow(ctx, "left gear", $"{gearPath}.{FenixChannelFields.GearPositionData.Left}", left));
        rows.Add(WheelGearDoorRow(ctx, "left gear door", $"{doorPath}.{FenixChannelFields.GearDoorData.LeftMain}", leftDoor));
        rows.Add(WheelGearRow(ctx, "right gear", $"{gearPath}.{FenixChannelFields.GearPositionData.Right}", right));
        rows.Add(WheelGearDoorRow(ctx, "right gear door", $"{doorPath}.{FenixChannelFields.GearDoorData.RightMain}", rightDoor));
        if (tpis)
        {
            rows.Add($"nose tires: left {FenixChannelSd.Whole(pressures[0])} PSI, right {FenixChannelSd.Whole(pressures[1])} PSI");
        }

        for (int wheel = 0; wheel < 4; wheel++)
        {
            string row = $"wheel {wheel + 1}: brake {FenixChannelSd.Whole(brakeTemps[wheel])} °C{(amberWheels[wheel] ? " amber" : "")}";
            if (tpis) row += $", tire {FenixChannelSd.Whole(pressures[wheel + 2])} PSI";
            if (releasedWheels[wheel]) row += ", released";
            rows.Add(row);
        }

        return FenixChannelRows.Of(rows);
    }

    /// <summary>The WHEEL page (12).</summary>
    static partial void ComposeWheel(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows)
        => rows = WheelRows(ctx);

    // One gear's two LGCIU positions.
    private static (double, double) WheelLgcius(FenixChannelSdReader gear)
        => (gear.Double(FenixChannelFields.GearUnitStatus.Lgciu1), gear.Double(FenixChannelFields.GearUnitStatus.Lgciu2));

    // "<label>: <word>" while both LGCIUs give the same word, else "<label>: LGCIU 1 <word>, LGCIU 2 <word>". A position with
    // no word reads "not available" in either form and is reported (once per field, even when both are unknown).
    private static string WheelGearRow(FenixChannelSdPageContext ctx, string label, string path, (double, double) lgcius)
    {
        string first = SdMeasuredWord(ctx, WheelGearUnitWords, $"{path}.{FenixChannelFields.GearUnitStatus.Lgciu1}", lgcius.Item1, "not available");
        string second = SdMeasuredWord(ctx, WheelGearUnitWords, $"{path}.{FenixChannelFields.GearUnitStatus.Lgciu2}", lgcius.Item2, "not available");
        return first == second ? $"{label}: {first}" : $"{label}: LGCIU 1 {first}, LGCIU 2 {second}";
    }

    // "<label>: <word>" for a gear door.
    private static string WheelGearDoorRow(FenixChannelSdPageContext ctx, string label, string path, double value)
        => $"{label}: {SdMeasuredWord(ctx, WheelGearDoorWords, path, value, "not available")}";

    // The word for a measured value (doubles compared exactly: the Fenix sends the enum's whole numbers as doubles), else
    // the unavailable words, with the value reported. 7 reads "7", 2.5 "2.5", in the invariant culture. WHEEL and COND
    // share it (controller Ruling 27): the one member of this plan not named for a page, so the two pages never copy it.
    private static string SdMeasuredWord(FenixChannelSdPageContext ctx, IReadOnlyDictionary<double, string> words, string path,
        double value, string unavailable)
    {
        if (words.TryGetValue(value, out string? word)) return word;

        ctx.ReportProblem(string.Create(CultureInfo.InvariantCulture,
            $"field {path} has an unknown value {value.ToString("0.################", CultureInfo.InvariantCulture)}"));
        return unavailable;
    }
}
