// Characterization tests for the app-wide cabin announcements' pure logic:
// FenixCabinEvents turns each poll of the cabin values into what is said for the doors, the slides and the
// cautions, after a silent baseline. Each test feeds whole polls (a FenixCabinData holding the values of
// one read, Unknown for the rest), as FenixCabinReadyMonitor does once a second. No service, no socket.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixCabinEventsTests
{
    private static readonly FenixDataValue True = new(FenixValueKind.Bool, 0, true, null, "true");
    private static readonly FenixDataValue False = new(FenixValueKind.Bool, 0, false, null, "false");
    private static readonly FenixDataValue Unknown = FenixDataValue.Unknown;
    private static readonly FenixDataValue JsonNull = new(FenixValueKind.Null, 0, false, null, "null");

    private static FenixDataValue Num(double value)
        => new(FenixValueKind.Number, value, false, null, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private const string Doors = FenixCabinAnnouncements.DoorsKey;
    private const string Slides = FenixCabinAnnouncements.SlidesKey;
    private const string Cautions = FenixCabinAnnouncements.CautionsKey;

    // The A320 the cabin starts from (doors 1L 1R 4L 4R, overwing slides, lavatories A D E, bulk cargo).
    private static readonly FenixCabinLayout A320 = FenixCabinLayout.Resolve(FenixAircraftType.Unknown, null, null, null);
    private static readonly FenixCabinLayout A321 = FenixCabinLayout.Resolve(FenixAircraftType.A321, null, null, null);
    private static readonly FenixCabinLayout A319 = FenixCabinLayout.Resolve(FenixAircraftType.A319, null, null, null);

    private static string Door(string key) => FenixCabinNames.Door(key);
    private static string Slide(string key) => FenixCabinNames.Slide(key);

    // One poll: the given values, every other cabin name Unknown (what the data service answers for a name it
    // has no value for).
    private static FenixCabinData Poll(params (string Name, FenixDataValue Value)[] values)
    {
        var data = new FenixCabinData();
        foreach (string name in FenixCabinNames.All) data.Apply(name, Unknown);
        foreach ((string name, FenixDataValue value) in values) data.Apply(name, value);
        return data;
    }

    private static (string Name, FenixDataValue Value)[] PassengerSlides(FenixDataValue value, FenixCabinLayout? layout = null)
        => (layout ?? A320).MainDoors.Select(door => (Slide(FenixCabinLayout.DataKey(door)), value)).ToArray();

    // The texts said for each poll in turn.
    private static string[][] Run(FenixCabinEvents events, FenixCabinLayout layout, params FenixCabinData[] polls)
        => polls.Select(poll => events.Next(poll, layout).Select(e => e.Text).ToArray()).ToArray();

    private static string[] Texts(IReadOnlyList<FenixCabinEvent> events) => events.Select(e => e.Text).ToArray();

    // ---- Doors ----------------------------------------------------------------------------------

    [Fact]
    public void A_door_is_said_on_a_known_change_after_a_silent_baseline()
    {
        var events = new FenixCabinEvents();

        Assert.Empty(events.Next(Poll((Door("d1l"), False)), A320));
        var opened = Assert.Single(events.Next(Poll((Door("d1l"), True)), A320));
        Assert.Equal(new FenixCabinEvent(Doors, "Door 1 left open", "door.d1l"), opened);
        Assert.Empty(events.Next(Poll((Door("d1l"), True)), A320));
        Assert.Equal(new[] { "Door 1 left closed" }, Texts(events.Next(Poll((Door("d1l"), False)), A320)));
    }

    [Fact]
    public void A_door_already_open_when_the_monitoring_starts_is_not_said()
    {
        var events = new FenixCabinEvents();
        Assert.Equal(
            new[] { Array.Empty<string>(), Array.Empty<string>(), new[] { "Door 4 right closed" } },
            Run(events, A320, Poll((Door("d4r"), True)), Poll((Door("d4r"), True)), Poll((Door("d4r"), False))));
    }

    [Fact]
    public void An_unknown_door_value_is_never_said_and_never_a_state()
    {
        // Unknown between two known values keeps the first as the baseline.
        Assert.Equal(new[] { "Door 1 left open" },
            Run(new FenixCabinEvents(), A320, Poll((Door("d1l"), False)), Poll((Door("d1l"), Unknown)), Poll((Door("d1l"), True))).SelectMany(t => t));
        Assert.Empty(
            Run(new FenixCabinEvents(), A320, Poll((Door("d1l"), False)), Poll((Door("d1l"), Unknown)), Poll((Door("d1l"), False))).SelectMany(t => t));

        // Unknown first: the first known value is the silent baseline, JSON null and a number too are not a state.
        Assert.Equal(new[] { "Door 1 left closed" },
            Run(new FenixCabinEvents(), A320,
                Poll((Door("d1l"), Unknown)), Poll((Door("d1l"), JsonNull)), Poll((Door("d1l"), True)), Poll((Door("d1l"), False))).SelectMany(t => t));
        Assert.Empty(
            Run(new FenixCabinEvents(), A320, Poll((Door("d1l"), Num(1))), Poll((Door("d1l"), True))).SelectMany(t => t));
    }

    [Fact]
    public void Only_the_layouts_passenger_doors_are_said_in_their_order()
    {
        // The A320 has no door 2: its change is not said. Doors changing in one poll are said in the FAP's order.
        var events = new FenixCabinEvents();
        events.Next(Poll((Door("d1l"), False), (Door("d2l"), False), (Door("d4r"), False), (Door("d1r"), False)), A320);

        var said = events.Next(Poll((Door("d1l"), True), (Door("d2l"), True), (Door("d4r"), True), (Door("d1r"), True)), A320);

        Assert.Equal(new[] { "Door 1 left open", "Door 1 right open", "Door 4 right open" }, Texts(said));
        Assert.Equal(new[] { "door.d1l", "door.d1r", "door.d4r" }, said.Select(e => e.LineKey).ToArray());
        Assert.All(said, e => Assert.Equal(Doors, e.Kind));

        // The A321 has all eight: door 2 left and door 3 right are said there.
        var a321 = new FenixCabinEvents();
        a321.Next(Poll((Door("d2l"), False), (Door("d3r"), True)), A321);
        Assert.Equal(new[] { "Door 2 left open", "Door 3 right closed" },
            Texts(a321.Next(Poll((Door("d2l"), True), (Door("d3r"), False)), A321)));
    }

    [Fact]
    public void A_door_that_joins_the_layout_keeps_what_was_known_of_it()
    {
        // Door 2 left was known closed while the cabin was laid out as an A320; the A321 layout lists it. Its
        // state when the layout resolves is not said, a later change is.
        var events = new FenixCabinEvents();
        events.Next(Poll((Door("d2l"), False)), A320);
        Assert.Empty(events.Next(Poll((Door("d2l"), False)), A321));
        Assert.Equal(new[] { "Door 2 left open" }, Texts(events.Next(Poll((Door("d2l"), True)), A321)));

        // A door first known in the poll the layout resolves is the silent baseline.
        var fresh = new FenixCabinEvents();
        fresh.Next(Poll(), A320);
        Assert.Empty(fresh.Next(Poll((Door("d3l"), True)), A321));
    }

    // ---- Cargo doors ----------------------------------------------------------------------------

    [Fact]
    public void A_cargo_door_is_said_once_when_it_settles_never_moving()
    {
        var events = new FenixCabinEvents();
        string fwd = FenixCabinNames.CargoForward, moving = FenixCabinNames.CargoFwdMoving;

        Assert.Equal(
            new[]
            {
                Array.Empty<string>(),                 // closed: the baseline
                Array.Empty<string>(),                 // moving
                Array.Empty<string>(),                 // the open flag turns while it moves
                new[] { "Forward cargo door open" },   // settled open
                Array.Empty<string>(),                 // still open
                Array.Empty<string>(),                 // moving again
                new[] { "Forward cargo door closed" }  // settled closed
            },
            Run(events, A320,
                Poll((fwd, False), (moving, False)),
                Poll((fwd, False), (moving, True)),
                Poll((fwd, True), (moving, True)),
                Poll((fwd, True), (moving, False)),
                Poll((fwd, True), (moving, False)),
                Poll((fwd, True), (moving, True)),
                Poll((fwd, False), (moving, False))));

        var aft = new FenixCabinEvents();
        aft.Next(Poll((FenixCabinNames.CargoAft, False)), A320);
        var said = Assert.Single(aft.Next(Poll((FenixCabinNames.CargoAft, True)), A320));
        Assert.Equal(new FenixCabinEvent(Doors, "Aft cargo door open", "cargo.aft"), said);
    }

    [Fact]
    public void A_cargo_door_moving_or_unknown_at_the_start_is_not_said_when_it_settles()
    {
        // Moving at the first poll: the first settled state is the baseline.
        Assert.Empty(Run(new FenixCabinEvents(), A320,
            Poll((FenixCabinNames.CargoForward, True), (FenixCabinNames.CargoFwdMoving, True)),
            Poll((FenixCabinNames.CargoForward, True), (FenixCabinNames.CargoFwdMoving, False))).SelectMany(t => t));

        // Unknown between two known states keeps the baseline; a moved door is then said.
        Assert.Equal(new[] { "Aft cargo door open" }, Run(new FenixCabinEvents(), A320,
            Poll((FenixCabinNames.CargoAft, False)),
            Poll((FenixCabinNames.CargoAft, Unknown)),
            Poll((FenixCabinNames.CargoAft, True))).SelectMany(t => t));
        Assert.Empty(Run(new FenixCabinEvents(), A320,
            Poll((FenixCabinNames.CargoAft, True)),
            Poll((FenixCabinNames.CargoAft, Unknown)),
            Poll((FenixCabinNames.CargoAft, True))).SelectMany(t => t));
    }

    [Fact]
    public void The_bulk_cargo_door_is_said_only_on_an_airframe_that_has_one()
    {
        var a320 = new FenixCabinEvents();
        a320.Next(Poll((FenixCabinNames.CargoBulk, False)), A320);
        var said = Assert.Single(a320.Next(Poll((FenixCabinNames.CargoBulk, True)), A320));
        Assert.Equal(new FenixCabinEvent(Doors, "Bulk cargo door open", "cargo.bulk"), said);

        var a319 = new FenixCabinEvents();
        a319.Next(Poll((FenixCabinNames.CargoBulk, False)), A319);
        Assert.Empty(a319.Next(Poll((FenixCabinNames.CargoBulk, True)), A319));
    }

    // ---- Slides ---------------------------------------------------------------------------------

    [Fact]
    public void The_slides_are_said_when_all_passenger_doors_change_from_armed_to_disarmed_and_back()
    {
        var events = new FenixCabinEvents();
        Assert.Empty(events.Next(Poll(PassengerSlides(True)), A320));

        var disarmed = Assert.Single(events.Next(Poll(PassengerSlides(False)), A320));
        Assert.Equal(new FenixCabinEvent(Slides, "Slides disarmed"), disarmed);
        Assert.Empty(events.Next(Poll(PassengerSlides(False)), A320));
        Assert.Equal(new[] { "Slides armed" }, Texts(events.Next(Poll(PassengerSlides(True)), A320)));
    }

    [Fact]
    public void Mixed_slides_are_silent_and_the_summary_is_said_once_all_agree()
    {
        var events = new FenixCabinEvents();
        events.Next(Poll(PassengerSlides(True)), A320);

        // One door disarmed, then two: mixed, nothing said. All four: disarmed.
        Assert.Empty(events.Next(Poll((Slide("d1l"), False), (Slide("d1r"), True), (Slide("d4l"), True), (Slide("d4r"), True)), A320));
        Assert.Empty(events.Next(Poll((Slide("d1l"), False), (Slide("d1r"), False), (Slide("d4l"), True), (Slide("d4r"), True)), A320));
        Assert.Equal(new[] { "Slides disarmed" }, Texts(events.Next(Poll(PassengerSlides(False)), A320)));

        // Back through mixed to disarmed again: the same summary, nothing said.
        Assert.Empty(events.Next(Poll((Slide("d1l"), True), (Slide("d1r"), False), (Slide("d4l"), False), (Slide("d4r"), False)), A320));
        Assert.Empty(events.Next(Poll(PassengerSlides(False)), A320));
    }

    [Fact]
    public void The_overwing_slides_are_not_part_of_the_summary()
    {
        // Measured 2026-10-23 on the A320: the overwing slides read armed at the gate with the doors disarmed.
        var events = new FenixCabinEvents();
        var overwingArmed = new[] { (Slide("owl"), True), (Slide("owr"), True) };

        Assert.Empty(events.Next(Poll(PassengerSlides(False).Concat(overwingArmed).ToArray()), A320));
        Assert.Equal(new[] { "Slides armed" },
            Texts(events.Next(Poll(PassengerSlides(True).Concat(overwingArmed).ToArray()), A320)));
        Assert.Empty(events.Next(Poll(PassengerSlides(True).Concat(new[] { (Slide("owl"), False), (Slide("owr"), False) }).ToArray()), A320));
    }

    [Fact]
    public void An_unknown_slide_is_no_summary_and_the_first_known_summary_is_silent()
    {
        // Three known armed and one unknown: no summary yet. All four known: the silent baseline.
        var events = new FenixCabinEvents();
        Assert.Empty(events.Next(Poll((Slide("d1l"), True), (Slide("d1r"), True), (Slide("d4l"), True)), A320));
        Assert.Empty(events.Next(Poll(PassengerSlides(True)), A320));

        // A slide gone unknown keeps what was known of it: three disarmed and one unknown is not all disarmed.
        Assert.Empty(events.Next(Poll((Slide("d1l"), False), (Slide("d1r"), False), (Slide("d4l"), False)), A320));
        Assert.Equal(new[] { "Slides disarmed" }, Texts(events.Next(Poll(PassengerSlides(False)), A320)));
    }

    [Fact]
    public void A_new_layout_takes_its_slide_summary_silently_and_a_later_change_is_said()
    {
        // All armed on the A320; the A321 adds doors 2 and 3, also armed: the same summary, nothing said.
        var events = new FenixCabinEvents();
        events.Next(Poll(PassengerSlides(True, A321)), A320);
        Assert.Empty(events.Next(Poll(PassengerSlides(True, A321)), A321));
        Assert.Equal(new[] { "Slides disarmed" }, Texts(events.Next(Poll(PassengerSlides(False, A321)), A321)));

        // The A320's four armed while doors 2 and 3 are disarmed: mixed for the A321, which has no summary of its
        // own yet, so the A320's armed stands, and the slides all disarming is said.
        var mixed = new FenixCabinEvents();
        var start = PassengerSlides(True).Concat(new[] { (Slide("d2l"), False), (Slide("d2r"), False), (Slide("d3l"), False), (Slide("d3r"), False) }).ToArray();
        mixed.Next(Poll(start), A320);
        Assert.Empty(mixed.Next(Poll(start), A321));
        Assert.Equal(new[] { "Slides disarmed" }, Texts(mixed.Next(Poll(PassengerSlides(False, A321)), A321)));

        // No summary before (a slide unknown), all known in the poll the A321 layout resolves: the silent baseline.
        var resolved = new FenixCabinEvents();
        resolved.Next(Poll((Slide("d1l"), False), (Slide("d1r"), False), (Slide("d4l"), False)), A320);
        Assert.Empty(resolved.Next(Poll(PassengerSlides(False, A321)), A321));
        Assert.Equal(new[] { "Slides armed" }, Texts(resolved.Next(Poll(PassengerSlides(True, A321)), A321)));
    }

    // ---- Cautions -------------------------------------------------------------------------------

    [Fact]
    public void A_caution_is_said_once_when_it_appears_and_nothing_when_it_clears()
    {
        var events = new FenixCabinEvents();
        string panel = FenixCabinNames.WaterServicePanelOpen;

        Assert.Equal(
            new[]
            {
                Array.Empty<string>(),
                new[] { "Cabin caution: Water service door open" },
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { "Cabin caution: Water service door open" }
            },
            Run(events, A320, Poll((panel, False)), Poll((panel, True)), Poll((panel, True)), Poll((panel, False)), Poll((panel, True))));

        var events2 = new FenixCabinEvents();
        events2.Next(Poll((panel, False)), A320);
        var said = Assert.Single(events2.Next(Poll((panel, True)), A320));
        Assert.Equal(new FenixCabinEvent(Cautions, "Cabin caution: Water service door open"), said);
    }

    [Fact]
    public void A_caution_already_there_or_first_known_is_not_said()
    {
        // Already there when the monitoring starts.
        Assert.Empty(Run(new FenixCabinEvents(), A320,
            Poll((FenixCabinNames.WasteTankFull, True)), Poll((FenixCabinNames.WasteTankFull, True))).SelectMany(t => t));

        // Its value first known after the others: it was not absent before, only unknown (FNX-1).
        Assert.Empty(Run(new FenixCabinEvents(), A320,
            Poll((FenixCabinNames.WaterTank, Num(80))),
            Poll((FenixCabinNames.WaterTank, Num(80)), (FenixCabinNames.VacuumSystemPowered, False))).SelectMany(t => t));

        // Gone unknown and back: the baseline holds.
        Assert.Empty(Run(new FenixCabinEvents(), A320,
            Poll((FenixCabinNames.WasteServicePanelOpen, True)),
            Poll((FenixCabinNames.WasteServicePanelOpen, Unknown)),
            Poll((FenixCabinNames.WasteServicePanelOpen, True))).SelectMany(t => t));
        Assert.Equal(new[] { "Cabin caution: Vacuum system disabled: ground servicing" }, Run(new FenixCabinEvents(), A320,
            Poll((FenixCabinNames.WasteServicePanelOpen, False)),
            Poll((FenixCabinNames.WasteServicePanelOpen, Unknown)),
            Poll((FenixCabinNames.WasteServicePanelOpen, True))).SelectMany(t => t));
    }

    [Fact]
    public void The_cautions_are_the_status_pages_with_the_door_pressure_messages_in_its_order()
    {
        var events = new FenixCabinEvents();
        var quiet = new (string, FenixDataValue)[]
        {
            (FenixCabinNames.VacuumSystemPowered, True), (FenixCabinNames.VacuumGeneratorOperational, True),
            (FenixCabinNames.LavatoryFlush("A"), True), (FenixCabinNames.LavatoryFlush("D"), True),
            (FenixCabinNames.WasteTank, Num(10)),
            (FenixCabinNames.SlidePressure("d4r"), Num(3500)), (FenixCabinNames.DoorPressure("d1l"), Num(1500))
        };
        Assert.Empty(events.Next(Poll(quiet), A320));

        FenixCabinData now = Poll(
            (FenixCabinNames.VacuumSystemPowered, True), (FenixCabinNames.VacuumGeneratorOperational, False),
            (FenixCabinNames.LavatoryFlush("A"), False), (FenixCabinNames.LavatoryFlush("D"), True),
            (FenixCabinNames.WasteTank, Num(10)),
            (FenixCabinNames.SlidePressure("d4r"), Num(2900)), (FenixCabinNames.DoorPressure("d1l"), Num(900)));
        var said = events.Next(now, A320);

        Assert.Equal(new[]
        {
            "Cabin caution: Vacuum system disabled on ground",
            "Cabin caution: Check lavatory power supply: LAV A",
            "Cabin caution: Door 1 left: check door pressure",
            "Cabin caution: Door 4 right: check slide pressure"
        }, Texts(said));
        Assert.All(said, e => Assert.Equal(Cautions, e.Kind));
        Assert.All(said, e => Assert.Null(e.LineKey));

        // Word for word what the Status page's "Cautions:" line lists.
        string status = FenixCabinLines.Build(now, null, A320).Single(line => line.Key == "status.cautions").Text;
        Assert.Equal("Cautions: " + string.Join("; ", said.Select(e => e.Text["Cabin caution: ".Length..])), status);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Every_announcement_reads_the_same_under_a_comma_decimal_culture(string culture)
        => TestCulture.Run(culture, () =>
        {
            var events = new FenixCabinEvents();
            events.Next(Poll(PassengerSlides(True)
                .Concat(new[]
                {
                    (Door("d1l"), False), (FenixCabinNames.CargoForward, False), (FenixCabinNames.CargoFwdMoving, False),
                    (FenixCabinNames.WasteTank, Num(29.5))
                }).ToArray()), A320);

            var said = events.Next(Poll(PassengerSlides(False)
                .Concat(new[]
                {
                    (Door("d1l"), True), (FenixCabinNames.CargoForward, True), (FenixCabinNames.CargoFwdMoving, False),
                    (FenixCabinNames.WasteTank, Num(30.5))
                }).ToArray()), A320);

            Assert.Equal(new[]
            {
                "Door 1 left open",
                "Forward cargo door open",
                "Slides disarmed",
                "Cabin caution: Waste tank not empty, check level: ground service required"
            }, Texts(said));
            return 0;
        });

    // ---- Keys and rows --------------------------------------------------------------------------

    [Fact]
    public void The_four_kinds_have_stable_keys_and_their_own_rows()
    {
        Assert.Equal("FENIX_CABIN_READY", FenixCabinAnnouncements.CabinReadyKey);
        Assert.Equal("FENIX_CABIN_DOORS", FenixCabinAnnouncements.DoorsKey);
        Assert.Equal("FENIX_CABIN_SLIDES", FenixCabinAnnouncements.SlidesKey);
        Assert.Equal("FENIX_CABIN_CAUTIONS", FenixCabinAnnouncements.CautionsKey);

        Assert.Equal(
            new[]
            {
                new MonitorRow("FENIX_CABIN_READY", "Cabin ready"),
                new MonitorRow("FENIX_CABIN_DOORS", "Cabin doors opening and closing"),
                new MonitorRow("FENIX_CABIN_SLIDES", "Cabin slides armed and disarmed"),
                new MonitorRow("FENIX_CABIN_CAUTIONS", "Cabin cautions")
            },
            FenixCabinAnnouncements.Rows);
        // The cabin-ready row is named by what it mutes, the monitor's one spoken text.
        Assert.Equal(FenixCabinReadyMonitor.SpokenText, FenixCabinAnnouncements.Rows[0].Label);
    }
}
