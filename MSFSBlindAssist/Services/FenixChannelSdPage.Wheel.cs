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
    /// The WHEEL page's rows, top to bottom: <c>L/G CTL</c> (137.12); <c>nose gear: &lt;LGCIU 1&gt;, &lt;LGCIU 2&gt;</c>
    /// (143.1.1, 143.1.2) and <c>nose gear door</c> (144.1); <c>N/W STEERING</c> (137.9); <c>ANTI SKID</c> (137.16 or .17);
    /// <c>NORM BRK</c> (137.7 or .8); <c>ALTN BRK</c> (137.2 or .15); <c>ACCU ONLY</c> (137.18); <c>AUTO BRK: LO</c>, MED or
    /// MAX (137.4, .5, .6); <c>left gear</c> (143.2) and <c>left gear door</c> (144.2); <c>right gear</c> (143.3) and
    /// <c>right gear door</c> (144.3); <c>brakes °C: n, n, n, n</c> (138.1 to .4); <c>REL: 1, 2</c> (the wheels of 141.1 to
    /// .4 that are set, none: no row); and, while 137.19 (the tire pressure indicators are fitted), <c>nose tires PSI</c>
    /// (139.1, .2), <c>left tires PSI</c> (139.3, .4) and <c>right tires PSI</c> (139.5, .6). Numbers are whole. A label
    /// row is the bare label: named values speak no color (owner question Q4).
    /// </summary>
    /// <remarks>
    /// <para>Not read: 135, 136, 140 and 142 (a light, the brake pressure gauge and a brake's color, none drawn as SD
    /// text). 139 is read only while 137.19, so a push without it and without the indicators is a complete page.</para>
    /// <para>With more than one of 137.4, .5 and .6 set, the AUTO BRK row names the first in LO, MED, MAX order: the SD
    /// shows one.</para>
    /// <para>Gear words, measured on the 2026-11-03 gear extension. A gear value reads "up" (2), "in transit" (1) or "down"
    /// (0, the absent field: a gear down is a present, empty message, never a missing one); a door value reads "closed" (1)
    /// or "open" (0 while it opens, 2 while the gear extends). Any other value reads "LGCIU &lt;n&gt; not available" (a
    /// door: "not available") and is reported "field &lt;path&gt; has an unknown value &lt;v&gt;" without stopping the
    /// page.</para>
    /// </remarks>
    internal static FenixChannelRows WheelRows(FenixChannelSdPageContext ctx)
    {
        FenixChannelSdReader remote = FenixChannelSdReader.Of(ctx.Push, ctx.ReportProblem);
        FenixChannelSdReader flags = remote.Message(FenixChannelFields.Remote.WheelBrakeFlags);
        FenixChannelSdReader temps = remote.Message(FenixChannelFields.Remote.WheelTemp);
        FenixChannelSdReader released = remote.Message(FenixChannelFields.Remote.BrakeReleased);
        FenixChannelSdReader gears = remote.Message(FenixChannelFields.Remote.GearPosition);
        FenixChannelSdReader doors = remote.Message(FenixChannelFields.Remote.GearDoor);

        bool landingGearControl = flags.Bool(FenixChannelFields.WheelBrakeFlags.LgCtlLabel);
        bool steering = flags.Bool(FenixChannelFields.WheelBrakeFlags.SteeringLabel);
        bool antiSkid = flags.Bool(FenixChannelFields.WheelBrakeFlags.AntiSkidGreen)
            || flags.Bool(FenixChannelFields.WheelBrakeFlags.AntiSkidAmber);
        bool normalBrake = flags.Bool(FenixChannelFields.WheelBrakeFlags.NormBrakeGreen)
            || flags.Bool(FenixChannelFields.WheelBrakeFlags.NormBrakeAmber);
        bool alternateBrake = flags.Bool(FenixChannelFields.WheelBrakeFlags.AlternateBrakeGreen)
            || flags.Bool(FenixChannelFields.WheelBrakeFlags.AlternateBrakeAmber);
        bool accumulatorOnly = flags.Bool(FenixChannelFields.WheelBrakeFlags.AccuOnly);
        bool autoLow = flags.Bool(FenixChannelFields.WheelBrakeFlags.AutoBrakeLow);
        bool autoMed = flags.Bool(FenixChannelFields.WheelBrakeFlags.AutoBrakeMed);
        bool autoMax = flags.Bool(FenixChannelFields.WheelBrakeFlags.AutoBrakeMax);
        bool tpis = flags.Bool(FenixChannelFields.WheelBrakeFlags.TpisFitted);

        double[] brakeTemps =
        [
            temps.Double(FenixChannelFields.WheelTempData.Wheel1), temps.Double(FenixChannelFields.WheelTempData.Wheel2),
            temps.Double(FenixChannelFields.WheelTempData.Wheel3), temps.Double(FenixChannelFields.WheelTempData.Wheel4)
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
        if (antiSkid) rows.Add("ANTI SKID");
        if (normalBrake) rows.Add("NORM BRK");
        if (alternateBrake) rows.Add("ALTN BRK");
        if (accumulatorOnly) rows.Add("ACCU ONLY");
        if (autoLow) rows.Add("AUTO BRK: LO");
        else if (autoMed) rows.Add("AUTO BRK: MED");
        else if (autoMax) rows.Add("AUTO BRK: MAX");
        rows.Add(WheelGearRow(ctx, "left gear", $"{gearPath}.{FenixChannelFields.GearPositionData.Left}", left));
        rows.Add(WheelGearDoorRow(ctx, "left gear door", $"{doorPath}.{FenixChannelFields.GearDoorData.LeftMain}", leftDoor));
        rows.Add(WheelGearRow(ctx, "right gear", $"{gearPath}.{FenixChannelFields.GearPositionData.Right}", right));
        rows.Add(WheelGearDoorRow(ctx, "right gear door", $"{doorPath}.{FenixChannelFields.GearDoorData.RightMain}", rightDoor));
        rows.Add($"brakes °C: {string.Join(", ", brakeTemps.Select(FenixChannelSd.Whole))}");

        string[] releasedNames = Enumerable.Range(1, 4).Where(wheel => releasedWheels[wheel - 1])
            .Select(wheel => wheel.ToString(CultureInfo.InvariantCulture)).ToArray();
        if (releasedNames.Length > 0) rows.Add($"REL: {string.Join(", ", releasedNames)}");

        if (tpis)
        {
            rows.Add($"nose tires PSI: {FenixChannelSd.Whole(pressures[0])}, {FenixChannelSd.Whole(pressures[1])}");
            rows.Add($"left tires PSI: {FenixChannelSd.Whole(pressures[2])}, {FenixChannelSd.Whole(pressures[3])}");
            rows.Add($"right tires PSI: {FenixChannelSd.Whole(pressures[4])}, {FenixChannelSd.Whole(pressures[5])}");
        }

        return FenixChannelRows.Of(rows);
    }

    /// <summary>The WHEEL page (12).</summary>
    static partial void ComposeWheel(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows)
        => rows = WheelRows(ctx);

    // One gear's two LGCIU positions.
    private static (double, double) WheelLgcius(FenixChannelSdReader gear)
        => (gear.Double(FenixChannelFields.GearUnitStatus.Lgciu1), gear.Double(FenixChannelFields.GearUnitStatus.Lgciu2));

    // "<label>: <LGCIU 1>, <LGCIU 2>": a position with no word reads "LGCIU <n> not available" and is reported.
    private static string WheelGearRow(FenixChannelSdPageContext ctx, string label, string path, (double, double) lgcius)
    {
        string first = SdMeasuredWord(ctx, WheelGearUnitWords, $"{path}.{FenixChannelFields.GearUnitStatus.Lgciu1}", lgcius.Item1, "LGCIU 1 not available");
        string second = SdMeasuredWord(ctx, WheelGearUnitWords, $"{path}.{FenixChannelFields.GearUnitStatus.Lgciu2}", lgcius.Item2, "LGCIU 2 not available");
        return $"{label}: {first}, {second}";
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
