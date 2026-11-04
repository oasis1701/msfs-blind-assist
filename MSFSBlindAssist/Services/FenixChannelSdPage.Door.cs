namespace MSFSBlindAssist.Services;

// The DOOR page (6) of the Fenix SD: the crew oxygen pressure, then the airframe's doors, exits and slides. Written as the
// COND page is (FenixChannelSdPage.Cond.cs): one reader, every field read before any row is built, and the reader's problem
// returned as the page's one row. Every member added here is named for the DOOR page. Inert: only the tests call DoorRows
// until a later task wires the ComposeDoor seam.
internal static partial class FenixChannelSdPages
{
    /// <summary>
    /// The DOOR page's rows: <c>OXY: n PSI</c> (180.5, whole) while the SDAC has data (180.2), else <c>OXY: XX</c>; then the
    /// airframe's doors, nose to tail and left before right, each <c>label: closed</c> (its field set) or <c>label: open</c>,
    /// and for a door with a slide <c>, SLIDE armed</c> (the slide's field set) or <c>, SLIDE disarmed</c>. No row speaks a
    /// color.
    /// </summary>
    /// <remarks>
    /// <para>The airframe is <c>ctx.Variant.Type</c>, never a channel field. A319, A320 and A321 have door tables; an
    /// unknown airframe, and the A318, for which the Fenix has no door table and nothing is measured, read "airframe not
    /// known" before anything else, whatever the push holds.</para>
    /// <para>A door or slide is listed by the airframe's table alone, never by its field's presence: a real cruise push set
    /// every slide field 16 to 25, overwing slides included, on an airframe without overwing exits. The exits' names and
    /// fields are inferred, Task 1 measures them, and no exit reads a slide until Task 1 has mapped one. BULK shows only
    /// while 179.27, and the A319 and A320 have two exit pairs only while 179.26.</para>
    /// </remarks>
    internal static FenixChannelRows DoorRows(FenixChannelSdPageContext ctx)
    {
        Aircraft.FenixAircraftType type = ctx.Variant.Type;
        if (type is not (Aircraft.FenixAircraftType.A319 or Aircraft.FenixAircraftType.A320 or Aircraft.FenixAircraftType.A321))
        {
            return FenixChannelRows.NotAvailable("airframe not known");
        }

        FenixChannelSdReader remote = FenixChannelSdReader.Of(ctx.Push, ctx.ReportProblem);
        FenixChannelSdReader doors = remote.Message(FenixChannelFields.Remote.Doors);
        FenixChannelSdReader oxygen = remote.Message(FenixChannelFields.Remote.CrewOxy);

        // A door's field set means closed; a slide's field set means armed.
        bool avionics1 = doors.Bool(FenixChannelFields.DoorStatus.Avionics1);
        bool avionics2 = doors.Bool(FenixChannelFields.DoorStatus.Avionics2);
        bool avionics3 = doors.Bool(FenixChannelFields.DoorStatus.Avionics3);
        bool entry1L = doors.Bool(FenixChannelFields.DoorStatus.FwdEntryLeft);
        bool entry1R = doors.Bool(FenixChannelFields.DoorStatus.FwdEntryRight);
        bool avionics4 = doors.Bool(FenixChannelFields.DoorStatus.Avionics4);
        bool cargoFwd = doors.Bool(FenixChannelFields.DoorStatus.FwdCargo);
        bool wing1L = doors.Bool(FenixChannelFields.DoorStatus.LeftWing1);
        bool wing2L = doors.Bool(FenixChannelFields.DoorStatus.LeftWing2);
        bool wing1R = doors.Bool(FenixChannelFields.DoorStatus.RightWing1);
        bool wing2R = doors.Bool(FenixChannelFields.DoorStatus.RightWing2);
        bool cargoAft = doors.Bool(FenixChannelFields.DoorStatus.AftCargo);
        bool bulk = doors.Bool(FenixChannelFields.DoorStatus.Bulk);
        bool entry4L = doors.Bool(FenixChannelFields.DoorStatus.AftEntryLeft);
        bool entry4R = doors.Bool(FenixChannelFields.DoorStatus.AftEntryRight);
        bool slide1L = doors.Bool(FenixChannelFields.DoorStatus.SlideFwdLeft);
        bool slide1R = doors.Bool(FenixChannelFields.DoorStatus.SlideFwdRight);
        bool slide4L = doors.Bool(FenixChannelFields.DoorStatus.SlideAftLeft);
        bool slide4R = doors.Bool(FenixChannelFields.DoorStatus.SlideAftRight);
        bool doubleOverwing = doors.Bool(FenixChannelFields.DoorStatus.HasDoubleOverwingExit);
        bool hasBulk = doors.Bool(FenixChannelFields.DoorStatus.HasBulkCargoDoor);

        bool oxygenData = oxygen.Bool(FenixChannelFields.CrewOxygenStatus.SdacHasData);
        double oxygenPressure = oxygen.Double(FenixChannelFields.CrewOxygenStatus.LeftPressure);

        // Every read after a problem gives its default, which must never be spoken.
        if (remote.Problem is { } problem) return FenixChannelRows.NotAvailable(problem);

        var rows = new List<string>
        {
            oxygenData ? $"OXY: {FenixChannelSd.Whole(oxygenPressure)} PSI" : $"OXY: {FenixChannelSd.Crosses}",
            DoorRow("AVIONIC 1", avionics1),
            DoorRow("AVIONIC 2", avionics2),
            DoorRow("AVIONIC 3", avionics3),
            DoorRow("CABIN 1L", entry1L, slide1L),
            DoorRow("CABIN 1R", entry1R, slide1R),
            DoorRow("AVIONIC 4", avionics4),
            DoorRow("CARGO FWD", cargoFwd)
        };

        // The exits' names and fields are inferred, Task 1 measures them: wing 1 is fields 8 (left) and 10 (right), wing 2
        // is 9 (left) and 11 (right). No exit reads a slide until Task 1 has mapped one.
        if (type == Aircraft.FenixAircraftType.A321)
        {
            rows.Add(DoorRow("EMER EXIT 2L", wing1L));
            rows.Add(DoorRow("EMER EXIT 2R", wing1R));
            rows.Add(DoorRow("EMER EXIT 3L", wing2L));
            rows.Add(DoorRow("EMER EXIT 3R", wing2R));
        }
        else if (doubleOverwing)
        {
            rows.Add(DoorRow("EMER EXIT L1", wing1L));
            rows.Add(DoorRow("EMER EXIT R1", wing1R));
            rows.Add(DoorRow("EMER EXIT L2", wing2L));
            rows.Add(DoorRow("EMER EXIT R2", wing2R));
        }
        else
        {
            rows.Add(DoorRow("EMER EXIT L", wing1L));
            rows.Add(DoorRow("EMER EXIT R", wing1R));
        }

        rows.Add(DoorRow("CARGO AFT", cargoAft));
        if (hasBulk) rows.Add(DoorRow("BULK", bulk));
        rows.Add(DoorRow("CABIN 4L", entry4L, slide4L));
        rows.Add(DoorRow("CABIN 4R", entry4R, slide4R));
        return FenixChannelRows.Of(rows);
    }

    // One door row: its field set means closed; a slide, when the door has one, is armed while its field is set.
    private static string DoorRow(string label, bool closed, bool? slideArmed = null)
    {
        string row = $"{label}: {(closed ? "closed" : "open")}";
        return slideArmed is { } armed ? $"{row}, SLIDE {(armed ? "armed" : "disarmed")}" : row;
    }
}
