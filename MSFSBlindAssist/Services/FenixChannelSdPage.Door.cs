namespace MSFSBlindAssist.Services;

// The DOOR page (6) of the Fenix SD: the crew oxygen pressure, then the airframe's doors, exits and slides. Written as the
// COND page is (FenixChannelSdPage.Cond.cs): one reader, every field read before any row is built, and the reader's problem
// returned as the page's one row. Every member added here is named for the DOOR page. The seam below calls DoorRows
// and nothing else calls it.
internal static partial class FenixChannelSdPages
{
    /// <summary>
    /// The DOOR page's rows: <c>OXY: n PSI</c> (180.5, whole) while the SDAC has data (180.2), else <c>OXY: XX PSI</c>; then the
    /// airframe's doors, nose to tail and left before right, each <c>label: closed</c> (its field set) or <c>label: open</c>,
    /// and for a door with a slide <c>, SLIDE armed</c> (the slide's field set) or <c>, SLIDE disarmed</c>. No row speaks a
    /// color.
    /// </summary>
    /// <remarks>
    /// <para>Doors are named as the cabin window names them (<c>FenixCabinLines.DoorName</c>, by the FAP's numbering). The
    /// exits follow <c>ctx.Variant.Type</c> and 179.26 only, never the Fenix's <c>emergency_exits_type</c> setting that the
    /// cabin window follows (<c>FenixCabinLayout.Resolve</c>, [FNX-15]), because the channel's page context carries no
    /// settings; a non-default exits setting is not followed yet.</para>
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
            oxygenData ? $"OXY: {FenixChannelSd.Whole(oxygenPressure)} PSI" : $"OXY: {FenixChannelSd.Crosses} PSI",
            DoorRow("AVIONIC 1", avionics1),
            DoorRow("AVIONIC 2", avionics2),
            DoorRow("AVIONIC 3", avionics3),
            DoorRow("Door 1 left", entry1L, slide1L),
            DoorRow("Door 1 right", entry1R, slide1R),
            DoorRow("AVIONIC 4", avionics4),
            DoorRow("Forward cargo door", cargoFwd)
        };

        // The exits' names and fields are inferred, Task 1 measures them: wing 1 is fields 8 (left) and 10 (right), wing 2
        // is 9 (left) and 11 (right). No exit reads a slide until Task 1 has mapped one.
        if (type == Aircraft.FenixAircraftType.A321)
        {
            rows.Add(DoorRow("Door 2 left", wing1L));
            rows.Add(DoorRow("Door 2 right", wing1R));
            rows.Add(DoorRow("Door 3 left", wing2L));
            rows.Add(DoorRow("Door 3 right", wing2R));
        }
        else if (doubleOverwing)
        {
            rows.Add(DoorRow("Overwing exit 1 left", wing1L));
            rows.Add(DoorRow("Overwing exit 1 right", wing1R));
            rows.Add(DoorRow("Overwing exit 2 left", wing2L));
            rows.Add(DoorRow("Overwing exit 2 right", wing2R));
        }
        else
        {
            rows.Add(DoorRow("Overwing exit left", wing1L));
            rows.Add(DoorRow("Overwing exit right", wing1R));
        }

        rows.Add(DoorRow("Aft cargo door", cargoAft));
        if (hasBulk) rows.Add(DoorRow("Bulk cargo door", bulk));
        rows.Add(DoorRow("Door 4 left", entry4L, slide4L));
        rows.Add(DoorRow("Door 4 right", entry4R, slide4R));
        return FenixChannelRows.Of(rows);
    }

    /// <summary>The DOOR page (6).</summary>
    static partial void ComposeDoor(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows)
        => rows = DoorRows(ctx);

    // One door row: its field set means closed; a slide, when the door has one, is armed while its field is set.
    private static string DoorRow(string label, bool closed, bool? slideArmed = null)
    {
        string row = $"{label}: {(closed ? "closed" : "open")}";
        return slideArmed is { } armed ? $"{row}, SLIDE {(armed ? "armed" : "disarmed")}" : row;
    }
}
