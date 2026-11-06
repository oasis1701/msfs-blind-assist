namespace MSFSBlindAssist.Services;

// The F/CTL page (9) of the Fenix SD: the spoilers, the ailerons, the flight control computers, the pitch trim, the elevators
// and the rudder with its trim. Written as the WHEEL page is (FenixChannelSdPage.Wheel.cs): one reader, every field read
// before any row is built, and the reader's problem returned as the page's one row. Every member added here is named for the
// F/CTL page. The ComposeFctl seam calls FctlRows.
internal static partial class FenixChannelSdPages
{
    // The signs, MEASURED in flight on 2026-11-03 (A321); each is one place to change. Ailerons and elevators (as the THS, whose
    // -1.6 in cruise is nose up): negative is up (the ailerons read -25.0 both after touchdown, lift dumping; the elevators -5.0
    // in the flare). The rudder (145.16) and its trim (146.2): positive is right (-18.75 while the heading fell 236 to 221,
    // +8.38 while it rose 222 to 232).
    private const bool FctlSurfaceNegativeIsUp = true;
    private const bool FctlRudderPositiveIsRight = true;

    /// <summary>
    /// The F/CTL page's rows, top to bottom: <c>SPD BRK left</c> (the spoilers 145.2 to .6) and <c>SPD BRK right</c> (145.8
    /// to .12), five whole degrees each ("25°"; measured: the speed brake held 25.0, the ground spoilers 50.0), a faulted
    /// spoiler "&lt;n&gt;° fault"; the row names the order, "SPD BRK left, 1 to 5: 0°, 25°, 25°, 25°, 0°" (spoiler 1 is the
    /// inboard one), and folds to "SPD BRK left: all 0°" when the five texts are equal; <c>L AIL</c> (145.1) and <c>R AIL</c> (145.7), one decimal with "° up" or "° down";
    /// <c>ELAC 1</c>, <c>ELAC 2</c>, <c>SEC 1</c>, <c>SEC 2</c> and <c>SEC 3</c> (148.1 to .5), "normal" while set and
    /// "fault" while clear; <c>PITCH TRIM</c> (145.15), one decimal with "° UP" or "° DN"; <c>L ELEV</c> (145.13) and
    /// <c>R ELEV</c> (145.14) as the ailerons; <c>RUD</c> (145.16) and <c>RUD TRIM</c> (146.2), one decimal with "° left" or
    /// "° right".
    /// </summary>
    /// <remarks>
    /// <para>A position that rounds to 0.0 reads "0.0°" with no direction. Any other faulted surface (145.n.2) ends with
    /// ", fault". While 147.3 (the FCDC has data) is clear, every 145 surface's value reads "XX", as the SD draws it; the
    /// rudder trim and the computers read as usual. A NaN position reads "XX" too.</para>
    /// <para>The signs and the spoilers' unit were MEASURED in flight on 2026-11-03 (A321): the spoilers are degrees, positive
    /// up (25.0 on the speed brake, 50.0 at touchdown). The surfaces' unit (degrees) is measured too: the ailerons stop at
    /// +-25.0 and the elevators at 15.0.</para>
    /// </remarks>
    internal static FenixChannelRows FctlRows(FenixChannelSdPageContext ctx)
    {
        FenixChannelSdReader remote = FenixChannelSdReader.Of(ctx.Push, ctx.ReportProblem);
        FenixChannelSdReader surfaces = remote.Message(FenixChannelFields.Remote.FctlSurfaces);
        FenixChannelSdReader rudder = remote.Message(FenixChannelFields.Remote.RudderData);
        FenixChannelSdReader status = remote.Message(FenixChannelFields.Remote.FctlStatus);
        FenixChannelSdReader computers = remote.Message(FenixChannelFields.Remote.FccStatus);

        (double Position, bool Fault) Read(int field)
        {
            FenixChannelSdReader surface = surfaces.Message(field);
            return (surface.Double(FenixChannelFields.SurfaceState.Position), surface.Bool(FenixChannelFields.SurfaceState.Fault));
        }

        int[] leftSpoilerFields =
        [
            FenixChannelFields.FctlSurfaceData.LeftSpoiler1, FenixChannelFields.FctlSurfaceData.LeftSpoiler2,
            FenixChannelFields.FctlSurfaceData.LeftSpoiler3, FenixChannelFields.FctlSurfaceData.LeftSpoiler4,
            FenixChannelFields.FctlSurfaceData.LeftSpoiler5
        ];
        int[] rightSpoilerFields =
        [
            FenixChannelFields.FctlSurfaceData.RightSpoiler1, FenixChannelFields.FctlSurfaceData.RightSpoiler2,
            FenixChannelFields.FctlSurfaceData.RightSpoiler3, FenixChannelFields.FctlSurfaceData.RightSpoiler4,
            FenixChannelFields.FctlSurfaceData.RightSpoiler5
        ];

        var leftSpoilers = leftSpoilerFields.Select(Read).ToArray();
        var leftAileron = Read(FenixChannelFields.FctlSurfaceData.LeftAileron);
        var rightAileron = Read(FenixChannelFields.FctlSurfaceData.RightAileron);
        var rightSpoilers = rightSpoilerFields.Select(Read).ToArray();
        var leftElevator = Read(FenixChannelFields.FctlSurfaceData.LeftElevator);
        var rightElevator = Read(FenixChannelFields.FctlSurfaceData.RightElevator);
        var trim = Read(FenixChannelFields.FctlSurfaceData.Ths);
        var rudderSurface = Read(FenixChannelFields.FctlSurfaceData.Rudder);

        double rudderTrim = rudder.Double(FenixChannelFields.RudderData.Trim);
        bool fcdc = status.Bool(FenixChannelFields.FctlStatusData.FcdcAvail);
        bool[] available =
        [
            computers.Bool(FenixChannelFields.FccStatusData.Elac1), computers.Bool(FenixChannelFields.FccStatusData.Elac2),
            computers.Bool(FenixChannelFields.FccStatusData.Sec1), computers.Bool(FenixChannelFields.FccStatusData.Sec2),
            computers.Bool(FenixChannelFields.FccStatusData.Sec3)
        ];

        // Every read after a problem gives its default, which must never be spoken.
        if (remote.Problem is { } problem) return FenixChannelRows.NotAvailable(problem);

        string Spoiler((double Position, bool Fault) spoiler)
        {
            if (!fcdc) return FenixChannelSd.Crosses;

            string whole = FenixChannelSd.Whole(spoiler.Position);
            string shown = whole == FenixChannelSd.Crosses ? whole : $"{whole}°";

            return spoiler.Fault ? $"{shown} fault" : shown;
        }

        // Spoiler 1 is the inboard one and 5 the outboard (the A320's numbering; the contract's LeftSpoiler1 to 5 order). Five
        // equal texts fold into "all <text>"; otherwise the row names the order, "1 to 5".
        string SpoilerRow(string label, (double Position, bool Fault)[] spoilers)
        {
            string[] texts = spoilers.Select(Spoiler).ToArray();

            return texts.All(text => text == texts[0])
                ? $"{label}: all {texts[0]}"
                : $"{label}, 1 to 5: {string.Join(", ", texts)}";
        }

        string Surface((double Position, bool Fault) surface, Func<double, string> value)
            => !fcdc ? FenixChannelSd.Crosses : surface.Fault ? $"{value(surface.Position)}, fault" : value(surface.Position);

        string Computer(string label, bool ok) => $"{label}: {(ok ? "normal" : "fault")}";

        return FenixChannelRows.Of(
        [
            SpoilerRow("SPD BRK left", leftSpoilers),
            SpoilerRow("SPD BRK right", rightSpoilers),
            $"L AIL: {Surface(leftAileron, FctlSurfaceDegrees)}",
            $"R AIL: {Surface(rightAileron, FctlSurfaceDegrees)}",
            Computer("ELAC 1", available[0]), Computer("ELAC 2", available[1]), Computer("SEC 1", available[2]),
            Computer("SEC 2", available[3]), Computer("SEC 3", available[4]),
            $"PITCH TRIM: {Surface(trim, FctlPitchTrimDegrees)}",
            $"L ELEV: {Surface(leftElevator, FctlSurfaceDegrees)}",
            $"R ELEV: {Surface(rightElevator, FctlSurfaceDegrees)}",
            $"RUD: {Surface(rudderSurface, FctlRudderDegrees)}",
            $"RUD TRIM: {FctlRudderDegrees(rudderTrim)}"
        ]);
    }

    /// <summary>The F/CTL page (9).</summary>
    static partial void ComposeFctl(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows)
        => rows = FctlRows(ctx);

    // An aileron's or elevator's position: "2.3° up" or "2.3° down" by the measured sign, "0.0°" when it rounds to zero.
    private static string FctlSurfaceDegrees(double position)
        => FctlDegrees(position, FctlSurfaceNegativeIsUp ? "up" : "down", FctlSurfaceNegativeIsUp ? "down" : "up");

    // The rudder's or its trim's position: "3.3° right" or "3.3° left" by the measured sign.
    private static string FctlRudderDegrees(double position)
        => FctlDegrees(position, FctlRudderPositiveIsRight ? "left" : "right", FctlRudderPositiveIsRight ? "right" : "left");

    // The pitch trim in the SD's words, "1.9° UP" or "0.6° DN"; negative is nose up (the THS reads -1.6 in cruise).
    private static string FctlPitchTrimDegrees(double position)
        => FctlDegrees(position, "UP", "DN");

    // One decimal of the magnitude, "°", and the word of its sign; no word when the magnitude rounds to 0.0, and the crosses
    // alone for a value that is not a number.
    private static string FctlDegrees(double position, string negativeWord, string positiveWord)
    {
        string magnitude = FenixChannelSd.OneDecimal(Math.Abs(position));
        if (magnitude == FenixChannelSd.Crosses) return magnitude;
        if (magnitude == "0.0") return "0.0°";

        return $"{magnitude}° {(position < 0 ? negativeWord : positiveWord)}";
    }
}
